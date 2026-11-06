using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// Headless test of the wildlife prototype. Run with:
///   godot --headless --path prototypes/wildlife res://tests/WildlifeTest.tscn
/// Exits 0 if every check passes, 1 if any fails. Almost everything here drives the pure
/// simulation directly, thousands of steps a second; the performance check also times the
/// real creature views (meshes and all) syncing in the scene tree.
/// </summary>
public partial class WildlifeTest : Node
{
    const float Dt = 1f / 60f;

    WildlifeData _data;
    FaunaGenerator _gen;
    SpaceFaunaData _space;
    SpaceFaunaGenerator _spaceGen;
    int _failures, _checks;

    public override void _Ready() => Run();

    async void Run()
    {
        try
        {
            _data = DataFiles.LoadWildlife();
            _gen = new FaunaGenerator(_data.Fauna);
            Determinism();
            Variety();
            Weirdness();
            Bloom();
            Quality();
            SignatureAttacks();
            Stability();
            TelegraphedFirstAttack();
            Herds();
            Packs();
            Ambushers();
            Harvesting();
            CreaturesKeepOffTrunks();
            RunDown();
            StorageCap();
            CodexFirstFinder();
            NoSpawnsNearBases();
            WanderersStillEnter();
            _space = DataFiles.LoadSpace();
            _spaceGen = new SpaceFaunaGenerator(_space, _data.Combat);
            SpaceGenerator();
            SpaceDropTables();
            SpaceAnimation();
            SpaceAttacksAreFair();
            SpaceBehaviours();
            DeckCreatureKinds();
            SpacePerformance();
            Performance();
            await PlayableScene();
        }
        catch (Exception e)
        {
            Fail($"crashed: {e}");
        }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_checks} checks)" : $"{_failures} OF {_checks} CHECK(S) FAILED");
        // Stop sound and release shared resources before quitting, so nothing is left running.
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    // ---- The playable scene, driven through its real input path -------------------------

    async System.Threading.Tasks.Task PlayableScene()
    {
        var main = new Main { Name = "Main" };
        AddChild(main);
        await Frames(30);
        int live = main.Sim.Creatures.Count(c => !c.Removed);
        Check(live > 30 && main.View.ViewCount == live, $"the playable planet spawns its fauna ({live} creatures on {main.Fauna.PlanetName}, {main.View.ViewCount} views)");
        Structure outpost = main.Outpost.Structure;
        float closest = main.Sim.Creatures.Where(c => !c.Removed).Min(c => outpost.DistanceToFootprint(c.Home));
        Check(main.Sim.Structures.Contains(outpost) && outpost.IsBase && closest >= _data.Spawning.BaseClearance,
            $"the playable planet's outpost is a base, and nothing spawned within {_data.Spawning.BaseClearance:F0} m of it (closest {closest:F0} m from its deck)");
        await DiscoveryCards(main);
        await TrunksBlock(main);

        string alien = main.Fauna.PlanetName;
        Tap(Key.Key1);
        await Frames(10);
        string temperate = main.Fauna.PlanetName;
        bool temperateOk = main.World == WorldKind.Temperate && main.Planet.Style.Trees == TreeStyle.Temperate && main.Fauna.Species.All(s => !s.Corrupted);
        Tap(Key.Key3);
        await Frames(10);
        bool bloomOk = main.World == WorldKind.Bloom && main.Fauna.Species.All(s => s.Corrupted) && main.Planet.Style.Trees == TreeStyle.Bloom;
        Tap(Key.Key2);
        await Frames(10);
        Check(temperateOk && bloomOk && main.World == WorldKind.Alien && main.Fauna.PlanetName == alien && temperate != alien,
            $"keys 1-3 switch the world and regenerate fauna and trees ({temperate}, then the Bloom, then back to {alien})");

        // Walk up to the hovercraft, board it with F, drive with W, get out with F.
        Player p = main.Player;
        p.Teleport(main.Vessel.GlobalPosition + new Vector3(-3f, 0f, 0f));
        await Frames(5);
        Tap(Key.F);
        await Frames(2);
        bool boarded = p.Driving && main.Vessel.Driven;
        Vector3 from = main.Vessel.GlobalPosition;
        Hold(Key.W, true);
        await Frames(120);
        Hold(Key.W, false);
        float moved = WildSim.Flat(main.Vessel.GlobalPosition - from).Length();
        Tap(Key.F);
        await Frames(2);
        Check(boarded && moved > 5f && !p.Driving && !main.Vessel.Driven && p.Target.Attackable,
            $"F boards the hovercraft, W drives it ({moved:F1} m in 2 s), F climbs out again");

        main.QueueFree();
        await Frames(2);
    }

    /// <summary>
    /// The discovery card, in the real scene. Meeting a species for the first time brings up one
    /// brief card (name, form, behaviour, size, planet and first finder) with its chime; meeting
    /// it again brings up nothing. The card never takes input: nothing on the HUD can catch the
    /// mouse or the keyboard focus, and keys and movement keep working under it. It never shows
    /// while something is winding up or lunging at the player, or while the player is down, and
    /// the player can still sidestep a lunge while a card waits.
    /// </summary>
    async System.Threading.Tasks.Task DiscoveryCards(Main main)
    {
        Player p = main.Player;
        float maxHealth = p.Health.Max;
        p.Health.Max = p.Health.Current = 1e6f; // nothing about may end the staging in a respawn
        var shows = new Dictionary<string, int>();
        var discovered = new Dictionary<string, int>();
        main.Hud.CardShown += e => shows[e.SpeciesId] = shows.GetValueOrDefault(e.SpeciesId) + 1;
        main.Codex.Discovered += e => discovered[e.SpeciesId] = discovered.GetValueOrDefault(e.SpeciesId) + 1;
        main.Hud.ClearCards();
        await Frames(2);

        var unseen = main.Fauna.Species.Where(x => !main.Codex.Knows(x.Id) && x.Rig != RigKind.Swarm).ToList();
        Species attacker = unseen.Where(x => x.Stats.Damage > 0f).OrderBy(x => x.Plan == BodyPlan.Giant ? 1 : 0).FirstOrDefault();
        // The two met in peace should be ones that leave the player alone, so no attack holds their cards back.
        List<Species> calm = unseen.Where(x => x != attacker).OrderBy(x => Peaceful(x) ? 0 : 1).Take(2).ToList();
        if (attacker == null || calm.Count < 2)
        {
            Fail($"discovery card test needs three never-seen species on {main.Fauna.PlanetName}, one that attacks (found {unseen.Count})");
            return;
        }
        var spawned = new List<Creature>();
        // Nobody may meet these three by chance while the test is setting up its own meetings,
        // and no other hunter nearby may pick a fight that holds the cards back.
        foreach (Creature c in main.Sim.Creatures)
            if (c.Species == attacker || calm.Contains(c.Species)
                || (c.Stats.Damage > 0f && WildSim.Flat(c.Position - p.GlobalPosition).Length() < 150f)) main.Sim.Remove(c);

        // A first meeting: one card, with everything on it, and its chime.
        Species s1 = calm[0];
        spawned.Add(PutInView(main, s1, 6f));
        int waited = await WaitFor(() => main.Hud.ShowingCard?.SpeciesId == s1.Id && main.Hud.CardVisible, 900);
        bool chimed = main.Audio.UiPlayer.Playing;
        string text = main.Hud.CardText;
        bool complete = text.Contains(s1.Name) && text.Contains(s1.CommonName, StringComparison.OrdinalIgnoreCase) && text.Contains(s1.FormLabel)
                        && text.Contains(s1.BehaviourLabel) && text.Contains($"{s1.Body.Size:F1} m") && text.Contains(main.Fauna.PlanetName) && text.Contains("Player 1");
        await WaitFor(() => main.Hud.ShowingCard?.SpeciesId != s1.Id, 900);
        float seconds = main.Hud.CardOnScreen;
        Check(waited >= 0 && complete && chimed && seconds is > 3f and < 6f,
            $"meeting a new species brings up its discovery card with a chime, for {seconds:F1} s ({text.Replace('\n', '|')})");

        // Another of the same kind, right in front, brings up nothing.
        spawned.Add(PutInView(main, s1, 4f));
        await Frames(150);
        Check(shows.GetValueOrDefault(s1.Id) == 1 && discovered.GetValueOrDefault(s1.Id) == 1 && main.Hud.ShowingCard?.SpeciesId != s1.Id,
            $"the card appears once per species: a second {s1.CommonName} in view brings up nothing (cards for it: {shows.GetValueOrDefault(s1.Id)})");
        foreach (Creature c in spawned) main.Sim.Remove(c);

        // While a card is up, nothing can take the mouse or the keyboard, and the game plays on.
        Species s2 = calm[1];
        spawned.Add(PutInView(main, s2, 6f));
        waited = await WaitFor(() => main.Hud.ShowingCard?.SpeciesId == s2.Id && main.Hud.Card.Modulate.A > 0.9f, 900);
        List<Control> controls = main.Hud.FindChildren("*", "Control", true, false).OfType<Control>().ToList();
        bool inert = controls.Count > 0 && controls.All(c => c.MouseFilter == Control.MouseFilterEnum.Ignore && c.FocusMode == Control.FocusModeEnum.None);
        Vector2 centre = main.Hud.Card.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = centre, GlobalPosition = centre });
        await Frames(1);
        Control hovered = GetViewport().GuiGetHoveredControl();
        Control focused = GetViewport().GuiGetFocusOwner();
        float clock = main.Sim.Clock.Time01;
        Tap(Key.N);
        await Frames(1);
        bool keyReached = MathF.Abs(Mathf.PosMod(main.Sim.Clock.Time01 - clock, 1f) - 0.25f) < 0.02f;
        main.Sim.Clock.Time01 = clock;
        Vector3 from = p.GlobalPosition;
        Hold(Key.W, true);
        await Frames(30);
        Hold(Key.W, false);
        float walked = WildSim.Flat(p.GlobalPosition - from).Length();
        bool stillUp = main.Hud.CardVisible && main.Hud.ShowingCard?.SpeciesId == s2.Id;
        Check(waited >= 0 && inert && hovered == null && focused == null && keyReached && walked > 1.5f && !GetTree().Paused && stillUp,
            $"the card never takes input: all {controls.Count} HUD controls ignore the mouse and focus, nothing under the cursor at the card, a key still reaches the game, the player walks {walked:F1} m under it");

        // Combat: a never-seen hunter lunges at the player while that card is still up. The card
        // gets out of the way at once, the player sidesteps the lunge, and the hunter's own card
        // waits until the fight is over.
        foreach (Creature c in spawned) main.Sim.Remove(c);
        Vector3 look = WildSim.Flat(-main.Camera.GlobalBasis.Z).Normalized();
        Creature hunter = main.Sim.Spawn(attacker, WildSim.Flat(p.GlobalPosition + look * MathF.Max(1.2f, attacker.Stats.AttackRange * 0.8f)));
        Vector3 toPlayer = WildSim.Flat(p.GlobalPosition - hunter.Position).Normalized();
        hunter.Yaw = MathF.Atan2(-toPlayer.X, -toPlayer.Z);
        spawned.Add(hunter);
        AttackEvent? outcome = null;
        void OnAttack(AttackEvent e) { if (e.Creature == hunter) outcome ??= e; }
        main.Sim.Attacked += OnAttack;
        bool started = Attacks.Start(main.Sim, hunter, p.Target), dodging = false, cardInFight = false;
        int fightFrames = 0;
        for (int i = 0; i < 900 && outcome == null; i++)
        {
            await Frames(1);
            if (main.UnderAttack())
            {
                fightFrames++;
                cardInFight |= main.Hud.CardVisible;
            }
            if (hunter.Committed && !dodging)
            {
                Hold(Key.D, true); // step aside once it has committed
                dodging = true;
            }
        }
        Hold(Key.D, false);
        main.Sim.Attacked -= OnAttack;
        main.Sim.Remove(hunter);
        bool queuedInFight = main.Codex.Knows(attacker.Id);
        waited = await WaitFor(() =>
        {
            cardInFight |= main.UnderAttack() && main.Hud.CardVisible;
            return main.Hud.ShowingCard?.SpeciesId == attacker.Id && main.Hud.CardVisible;
        }, 60 * 20);
        bool ok = started && outcome is { Hit: false } && fightFrames > 20 && !cardInFight && queuedInFight && waited >= 0 && shows.GetValueOrDefault(attacker.Id) == 1;
        Check(ok, ok
            ? $"no card ever shows during an attack on the player ({fightFrames} frames of {attacker.Attack.Name.ToLowerInvariant()} wind-up and lunge, none with a card up), the sidestep still dodges it, and the {attacker.CommonName}'s own card comes once it is over"
            : $"cards and combat: attack started {started}, outcome {(outcome == null ? "none" : outcome.Value.Hit ? "hit" : "dodged")}, {fightFrames} fight frames, card shown in the fight {cardInFight}, seen in the fight {queuedInFight}, its card after {waited} frames, shown {shows.GetValueOrDefault(attacker.Id)} times");

        // A player who is down sees no card either; it comes once they are back on their feet.
        var test = new CodexEntry { SpeciesId = "test-downed", Name = "Testbeast", CommonName = "test beast", Form = "walker", Behaviour = "grazer", Size = 1f, Planet = "Test", Finder = "Player 1" };
        main.Hud.ClearCards();
        p.Health.Current = 0f;
        main.Hud.QueueDiscovery(test, Colors.White);
        bool shownDown = false;
        for (int i = 0; i < 600 && p.Health.Downed; i++)
        {
            shownDown |= main.Hud.CardVisible;
            await Frames(1);
        }
        waited = await WaitFor(() => main.Hud.ShowingCard == test && main.Hud.CardVisible, 600);
        Check(!shownDown && !p.Health.Downed && waited >= 0, "no card shows while the player is down; it comes once they are back up");

        main.Hud.ClearCards();
        foreach (Creature c in spawned) main.Sim.Remove(c);
        p.Health.Max = p.Health.Current = maxHealth;
    }

    /// <summary>Leaves a player alone unless provoked: grazers, herds and oddities that don't hunt, and no giants.</summary>
    static bool Peaceful(Species s) => s.Behaviour is Behaviour.Grazer or Behaviour.Herd or Behaviour.HarmlessOddity && !s.Stats.Hunts && s.Plan != BodyPlan.Giant;

    /// <summary>A creature of this species on the ground straight ahead of the camera, facing it.</summary>
    static Creature PutInView(Main main, Species s, float distance)
    {
        Vector3 eye = main.Camera.GlobalPosition, look = WildSim.Flat(-main.Camera.GlobalBasis.Z).Normalized();
        Creature c = main.Sim.Spawn(s, WildSim.Flat(eye + look * distance));
        c.Yaw = MathF.Atan2(look.X, look.Z);
        return c;
    }

    /// <summary>Waits up to a number of physics frames for a condition; returns the frames waited, or -1.</summary>
    async System.Threading.Tasks.Task<int> WaitFor(Func<bool> condition, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition()) return i;
            await Frames(1);
        }
        return condition() ? maxFrames : -1;
    }

    async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    static void Tap(Key key)
    {
        Hold(key, true);
        Hold(key, false);
    }

    static void Hold(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = pressed });

    // ---- Generator --------------------------------------------------------------------

    void Determinism()
    {
        PlanetFauna a = _gen.Generate(1234, 0.7f, false), b = _gen.Generate(1234, 0.7f, false);
        bool same = a.Species.Count == b.Species.Count && a.PlanetName == b.PlanetName
                    && a.Species.Zip(b.Species).All(p => Describe(p.First) == Describe(p.Second));
        Check(same, $"the same seed gives the same species ({a.Species.Count} on {a.PlanetName})");
        PlanetFauna c = _gen.Generate(1235, 0.7f, false);
        Check(Describe(a.Species[0]) != Describe(c.Species[0]) || a.Species.Count != c.Species.Count, "a different seed gives different species");
        GD.Print($"      e.g. {a.Species[0]}");
        GD.Print($"           {a.Species[1]}");
    }

    static string Describe(Species s) =>
        $"{s.Name}|{s.CommonName}|{s.PartSignature}|{s.Behaviour}|{s.Temperament}|{s.Colors.Base.ToHtml()}|{s.Colors.Accent.ToHtml()}|{s.Colors.Pattern}|{s.Body.Size:F4}|{s.Body.LegLength:F4}|{s.Stats.MaxHealth}|{s.Drops.Meat}|{s.Weirdness:F4}";

    void Variety()
    {
        int planets = 0, planetsWith8 = 0, worstPlans = 99;
        bool countsOk = true, plansOk = true, partsOk = true;
        for (ulong seed = 1; seed <= 40; seed++)
        foreach (float alien in new[] { 0.1f, 0.5f, 0.9f })
        {
            PlanetFauna f = _gen.Generate(seed * 7919, alien, false);
            planets++;
            int n = f.Species.Count;
            countsOk &= n is >= 6 and <= 10;
            int plans = f.Species.Take(8).Select(s => s.Plan).Distinct().Count();
            if (n >= 8)
            {
                planetsWith8++;
                worstPlans = Math.Min(worstPlans, plans);
                plansOk &= plans >= 4;
            }
            else plansOk &= plans >= Math.Min(4, n);
            partsOk &= f.Species.Select(s => s.PartSignature).Distinct().Count() == n;
        }
        Check(countsOk, $"every planet rolls 6 to 10 species ({planets} planets)");
        Check(plansOk, $"at least 4 distinct body plans in 8 species (worst of {planetsWith8} eight-plus planets: {worstPlans})");
        Check(partsOk, "no two species on a planet share a part set");
    }

    void Weirdness()
    {
        float Mean(float alien)
        {
            float sum = 0f;
            int n = 0;
            for (ulong seed = 1; seed <= 40; seed++)
                foreach (Species s in _gen.Generate(seed * 104729, alien, false).Species)
                {
                    sum += s.Weirdness;
                    n++;
                }
            return sum / n;
        }
        float w0 = Mean(0f), w5 = Mean(0.5f), w1 = Mean(1f);
        Check(w0 < w5 && w5 < w1, $"weirdness rises with alienness ({w0:F2} at 0, {w5:F2} at 0.5, {w1:F2} at 1)");
    }

    void Bloom()
    {
        bool ok = true, marked = true;
        int n = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            PlanetFauna clean = _gen.Generate(seed * 31, 0.6f, false), bloom = _gen.Generate(seed * 31, 0.6f, true);
            ok &= clean.Species.Count == bloom.Species.Count;
            for (int i = 0; i < Math.Min(clean.Species.Count, bloom.Species.Count); i++)
            {
                Species c = clean.Species[i], b = bloom.Species[i];
                marked &= b.Corrupted && !c.Corrupted && b.Name.StartsWith(_data.Fauna.Corruption.Prefix) && b.Colors.Pattern == Pattern.Veins;
                ok &= b.Plan == c.Plan && b.Form == c.Form && b.Weirdness > c.Weirdness;
                n++;
            }
        }
        Check(marked, $"Bloom variants are marked as corrupted ({n} species)");
        Check(ok, "Bloom fauna are corrupted versions of the local fauna (same body plans, weirder)");
    }

    /// <summary>
    /// Variety without accidents: thousands of generated species, from temperate to fully
    /// alien and Bloom-corrupted, and not one breaks the combination rules (parts on the wrong
    /// rig, form or behaviour, clashing pairs), the proportion bands, or the even-legs rule.
    /// Then a few of the rules spot-checked directly, so a data edit can't quietly undo them.
    /// </summary>
    void Quality()
    {
        int species = 0;
        var problems = new List<string>();
        int hunterSnouts = 0, grazerScythes = 0, insectHeadsOnBeasts = 0, oddLegs = 0, lampreyHorns = 0;
        var insectForms = new HashSet<string> { "skitterer", "tripod", "centipede", "burrower", "springer", "cloud", "pack", "crystalback" };
        for (ulong seed = 1; seed <= 150; seed++)
        foreach (float alien in new[] { 0f, 0.35f, 0.7f, 1f })
        foreach (Species sp in _gen.Generate(seed * 6151, alien, seed % 5 == 0).Species)
        {
            species++;
            foreach (string issue in FaunaQuality.Problems(_data.Fauna, sp))
                if (problems.Count < 5) problems.Add($"{sp.Name} ({sp.Form}, {sp.Behaviour}): {issue}");
            if (sp.IsHunter && sp.Part("head") == "snout") hunterSnouts++;
            if (sp.Behaviour is Behaviour.Grazer or Behaviour.Herd && sp.Part("limbs") == "scythes") grazerScythes++;
            if (sp.Part("head") == "mandibles" && !insectForms.Contains(sp.Form)) insectHeadsOnBeasts++;
            if (sp.Rig == RigKind.Legged && sp.Body.Legs % 2 == 1 && sp.Form != "tripod") oddLegs++;
            if (sp.Part("head") == "maw" && sp.Part("horns") is "pair" or "ram" or "brow" or "antlers" or "tusks") lampreyHorns++;
        }
        Check(problems.Count == 0, problems.Count == 0
            ? $"no degenerate creatures: {species} generated species (600 planets, alienness 0 to 1, a fifth Bloom-infested) all pass the part, form, behaviour and proportion rules"
            : $"degenerate creatures: {string.Join("; ", problems)}");
        Check(hunterSnouts + grazerScythes + insectHeadsOnBeasts + oddLegs + lampreyHorns == 0,
            $"rule spot checks: hunters with grazer snouts {hunterSnouts}, grazers with scythes {grazerScythes}, insect heads on beasts {insectHeadsOnBeasts}, odd-legged walkers {oddLegs}, horned lamprey mouths {lampreyHorns}");
    }

    /// <summary>Every behaviour has a signature attack, and every species that can hurt you carries one.</summary>
    void SignatureAttacks()
    {
        var styles = new SortedSet<string>();
        var names = new SortedSet<string>();
        string problem = null;
        for (ulong seed = 1; seed <= 60; seed++)
        foreach (Species sp in _gen.Generate(seed * 389, seed % 3 * 0.45f, seed % 7 == 0).Species)
        {
            if (sp.Stats.Damage <= 0f) continue;
            AttackDef a = sp.Attack;
            if (a == null || a.Name == "" || a.Telegraph < 0.6f) problem ??= $"{sp.Name} ({sp.Behaviour}) has no proper signature attack";
            else if (sp.TelegraphWindup(_data.Combat) < a.Telegraph) problem ??= $"{sp.Name}: telegraph shorter than its attack's";
            styles.Add(a.Style.ToString());
            names.Add(a.Name);
        }
        Check(problem == null && styles.Count >= 6, problem ?? $"every attacker has a signature attack with its own telegraph ({styles.Count} styles: {string.Join(", ", styles)}; {names.Count} attacks: {string.Join(", ", names)})");
    }

    // ---- Animation stability ------------------------------------------------------------

    void Stability()
    {
        int creatures = 0, frames = 0, footChecks = 0;
        float worstFoot = 0f, worstFootTol = 0f;
        string problem = null;
        var target = new DummyTarget { Name = "Walker", Health = new Health(1e9f) };

        foreach ((ulong seed, float alien, bool bloom) in new[] { (11UL, 0.15f, false), (12UL, 0.85f, false), (13UL, 0.6f, true), (14UL, 1f, false), (15UL, 0.4f, true) })
        {
            PlanetFauna f = _gen.Generate(seed, alien, bloom);
            WildSim w = NewSim(seed);
            w.Targets.Add(target);
            w.Clock.Time01 = 0.7f; // runs into the night half way, so day and night behaviours both play
            var spawned = new List<Creature>();
            for (int i = 0; i < f.Species.Count; i++)
            {
                Species s = f.Species[i];
                float a = i * MathF.Tau / f.Species.Count;
                var at = new Vector3(MathF.Cos(a) * 40f, 0f, MathF.Sin(a) * 40f);
                spawned.AddRange(w.SpawnGroup(s, at, s.Behaviour == Behaviour.Herd ? 5 : 2));
            }
            creatures += spawned.Count;
            var homes = spawned.ToDictionary(c => c, c => c.Position);

            for (int step = 0; step < 3600 && problem == null; step++)
            {
                float t = step * Dt;
                // The walker strolls a wide circle through everyone's patch, provoking hunts and flights.
                target.Position = Ground(w, new Vector3(MathF.Cos(t * 0.12f) * 45f, 0f, MathF.Sin(t * 0.12f) * 45f));
                target.Forward = new Vector3(-MathF.Sin(t * 0.12f), 0f, MathF.Cos(t * 0.12f));
                if (step == 1800) // and a few die half way through, to exercise death poses
                    foreach (Creature c in spawned.Where((c, i) => i % 7 == 3)) w.Damage(c, 1e6f, target);
                w.Step(Dt);
                frames++;

                foreach (Creature c in spawned)
                {
                    if (c.Removed) continue;
                    string what = $"{c.Species.Name} ({c.Species.Rig}/{c.Species.Form}) t={t:F1}s";
                    if (!Geo.Finite(c.Position)) { problem = $"NaN position: {what}"; break; }
                    foreach (Transform3D p in c.Rig.Pose)
                        if (!Geo.Finite(p)) { problem = $"NaN pose: {what}"; break; }
                    if (problem != null) break;
                    float fromHome = WildSim.Flat(c.Position - homes[c]).Length();
                    if (fromHome > 220f) { problem = $"wandered {fromHome:F0} m off: {what}"; break; }
                    if (!BodyInRange(w, c, out string why)) { problem = $"{why}: {what}"; break; }

                    if (c.Rig is LeggedRig legs && c.Alive && step > 5)
                    {
                        float tol = 0.12f * c.Species.Body.LegLength + 0.05f;
                        for (int i = 0; i < legs.FootCount; i++)
                        {
                            if (!legs.FootPlanted(i)) continue;
                            Vector3 target3 = legs.FootTarget(i), drawn = legs.FootDrawn(i);
                            float err = MathF.Max(drawn.DistanceTo(target3), MathF.Abs(target3.Y - w.Terrain.Height(target3.X, target3.Z)));
                            footChecks++;
                            if (err / tol > worstFoot / MathF.Max(worstFootTol, 1e-4f) || worstFootTol == 0f) { worstFoot = err; worstFootTol = tol; }
                            if (err > tol) { problem = $"planted foot {i} off the ground by {err:F2} m (tolerance {tol:F2}): {what} {c.State} {c.Phase} v {c.Velocity.Length():F1} time {c.PhaseTime:F2}"; break; }
                        }
                    }
                    if (problem != null) break;
                }
            }
        }
        Check(problem == null, problem ?? $"{creatures} creatures from 5 planets animate 60 s each with no NaNs, bodies in range, nothing flying off ({frames} frames)");
        Check(problem == null, $"planted walker feet stay within contact tolerance ({footChecks} foot checks, worst {worstFoot:F3} m of {worstFootTol:F3} m)");
    }

    static bool BodyInRange(WildSim w, Creature c, out string why)
    {
        Proportions b = c.Species.Body;
        float h = c.Rig.BodyClearance;
        (float lo, float hi) = c.Species.Rig switch
        {
            RigKind.Legged => (-0.3f * b.Height - 0.05f, b.LegLength * 1.1f + b.Height), // hiding ambushers press flat into the grass
            RigKind.Chain => (-b.Width * 1.5f - 0.4f, b.Size * 0.5f + b.Width),
            RigKind.Hopper => (-0.1f, b.LegLength * 2.5f + b.Height + b.Size),
            RigKind.Winged => (-0.2f, 30f),
            RigKind.GasBag => (-0.2f, 25f),
            _ => (-0.5f, 8f),
        };
        why = h < lo || h > hi ? $"body height {h:F2} m outside [{lo:F2}, {hi:F2}]" : null;
        return why == null;
    }

    // ---- Combat -------------------------------------------------------------------------

    /// <summary>
    /// The design rule: a creature's first attack on a player is always a telegraphed lunge,
    /// with a wind-up of at least its configured telegraph time, and stepping aside once it
    /// commits dodges it. Checked for every species that can attack, on several planets.
    /// </summary>
    void TelegraphedFirstAttack()
    {
        int species = 0, dodged = 0, landed = 0, stillTargets = 0;
        float shortestMargin = float.MaxValue;
        string problem = null;
        var attackers = new List<Species>();
        for (ulong seed = 1; seed <= 12; seed++)
            foreach (Species s in _gen.Generate(seed * 977, seed % 3 == 0 ? 0.2f : 0.8f, seed % 4 == 0).Species)
                if (s.Stats.Damage > 0f) attackers.Add(s);

        foreach (Species s in attackers)
        {
            species++;
            float telegraph = s.TelegraphWindup(_data.Combat);
            foreach (bool dodge in new[] { true, false })
            {
                WildSim w = NewSim(s.Seed, obstacles: false);
                Creature c = w.Spawn(s, Vector3.Zero);
                c.Yaw = 0f;
                var target = new DummyTarget { Position = Ground(w, c.Forward * (c.Stats.AttackRange * 0.8f)) };
                w.Targets.Add(target);
                float telegraphAt = -1f, contactAt = -1f, windup = 0f;
                bool first = false, hit = false;
                int telegraphs = 0;
                w.Telegraph += e => { if (telegraphs++ == 0) { telegraphAt = e.Time; windup = e.Windup; first = e.First; } };
                w.Attacked += e => { if (contactAt < 0f) { contactAt = e.Time; hit = e.Hit; } };
                Attacks.Start(w, c, target);
                Vector3 side = c.Forward.Cross(Vector3.Up).Normalized();
                for (int step = 0; step < 600 && contactAt < 0f; step++)
                {
                    // React at the commit: walk sideways at 5.5 m/s, the player's walking pace.
                    if (dodge && c.Committed) target.Position = Ground(w, target.Position + side * 5.5f * Dt);
                    w.Step(Dt);
                }
                float gap = contactAt - telegraphAt;
                if (telegraphAt < 0f || contactAt < 0f) problem ??= $"{s.Name} ({s.Behaviour}) never attacked";
                else if (!first) problem ??= $"{s.Name}: first attack not flagged as the telegraphed one";
                else if (windup + 1e-4f < telegraph || gap + 1e-3f < telegraph) problem ??= $"{s.Name}: wind-up {gap:F2} s, shorter than its telegraph {telegraph:F2} s";
                else if (dodge && hit) problem ??= $"{s.Name} ({s.Rig}): sidestepping after the commit did not dodge it";
                shortestMargin = MathF.Min(shortestMargin, gap - telegraph);
                if (dodge && !hit) dodged++;
                if (!dodge)
                {
                    stillTargets++;
                    if (hit) landed++;
                }

                // Its next attack comes at the normal rate.
                if (!dodge && problem == null)
                {
                    float second = -1f;
                    w.Telegraph += e => { if (!e.First) second = e.Windup; };
                    for (int step = 0; step < 300 && c.Attacking; step++) w.Step(Dt);
                    Attacks.Start(w, c, target);
                    if (second < 0f || second > telegraph) problem ??= $"{s.Name}: second attack wind-up {second:F2} s is not the normal rate";
                }
            }
        }
        Check(problem == null && species > 10, problem ?? $"first attacks are telegraphed: wind-up >= the configured telegraph for all {species} attacking species (tightest margin {shortestMargin:F3} s)");
        Check(problem == null && dodged == species, $"every first attack can be dodged by stepping aside after the commit ({dodged}/{species} dodged)");
        Check(landed == stillTargets, $"a target that stands still is hit ({landed}/{stillTargets}): the telegraph is a real attack, at full damage");
    }

    // ---- Behaviours ---------------------------------------------------------------------

    void Herds()
    {
        float worst = 0f, worstAllowed = 0f;
        int herds = 0;
        bool ok = true;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            Species s = _gen.Generate(seed * 50021, seed % 2 == 0 ? 0.2f : 0.8f, false).Species.First(x => x.Behaviour == Behaviour.Herd);
            WildSim w = NewSim(seed);
            List<Creature> herd = w.SpawnGroup(s, new Vector3(10f, 0f, 10f), 7);
            for (int step = 0; step < 3600; step++) w.Step(Dt);
            Vector3 center = herd.Aggregate(Vector3.Zero, (sum, c) => sum + c.Position) / herd.Count;
            float spread = herd.Max(c => WildSim.Flat(c.Position - center).Length());
            float allowed = herd[0].Herd.Spread * 2f + 4f;
            herds++;
            if (spread / allowed > worst / MathF.Max(worstAllowed, 1e-3f) || worstAllowed == 0f) { worst = spread; worstAllowed = allowed; }
            ok &= spread <= allowed;
        }
        Check(ok, $"herds stay cohesive over 60 s ({herds} herds of 7, widest {worst:F1} m from the middle, allowed {worstAllowed:F1} m)");

        // And when spooked they bolt together, then settle again.
        Species h = _gen.Generate(4242, 0.3f, false).Species.First(x => x.Behaviour == Behaviour.Herd);
        WildSim sim = NewSim(4242);
        List<Creature> members = sim.SpawnGroup(h, Vector3.Zero, 6);
        var scare = new DummyTarget { Position = Ground(sim, new Vector3(3f, 0f, 0f)), ThreatScale = 2.5f };
        sim.Targets.Add(scare);
        for (int step = 0; step < 90; step++) sim.Step(Dt);
        bool allFled = members.All(c => c.State == CState.Flee || members[0].Herd.Alarmed);
        sim.Targets.Clear();
        for (int step = 0; step < 900; step++) sim.Step(Dt);
        Vector3 mid = members.Aggregate(Vector3.Zero, (sum, c) => sum + c.Position) / members.Count;
        float after = members.Max(c => WildSim.Flat(c.Position - mid).Length());
        Check(allFled && after <= members[0].Herd.Spread * 2f + 4f, $"a spooked herd bolts as one and regroups ({after:F1} m spread after)");
    }

    // ---- Packs --------------------------------------------------------------------------

    /// <summary>A walking pack-hunter species, for the pack tests.</summary>
    Species PackSpecies(ulong salt)
    {
        for (ulong seed = 1; seed < 400; seed++)
            if (_gen.Generate(seed * 7 + salt, 0.4f, false).Species.FirstOrDefault(x => x.Behaviour == Behaviour.PackHunter && x.Rig == RigKind.Legged) is { } s)
                return s;
        return null;
    }

    /// <summary>
    /// Pack hunters: common across planets; roaming together; spreading round their prey
    /// before anyone commits; taking turns so commits are at least a telegraph apart; landing
    /// full damage; and breaking off together when the leader dies or half the pack is hurt.
    /// </summary>
    void Packs()
    {
        int planets = 0, withPack = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            planets++;
            if (_gen.Generate(seed * 104723, (seed % 5) * 0.25f, false).Species.Any(x => x.Behaviour == Behaviour.PackHunter)) withPack++;
        }
        Check(withPack >= planets / 2, $"pack hunters appear on most planets ({withPack} of {planets})");

        Species s = PackSpecies(1);
        if (s == null) { Fail("no walking pack-hunter species found to test"); return; }
        PackRules rules = _data.Fauna.Pack;
        float telegraph = s.TelegraphWindup(_data.Combat);

        // Roaming: a pack of five, nobody about, a minute.
        {
            WildSim w = NewSim(s.Seed, obstacles: false);
            List<Creature> pack = w.SpawnGroup(s, Vector3.Zero, 5);
            float worst = 0f;
            for (int step = 0; step < 3600; step++)
            {
                w.Step(Dt);
                if (step % 30 != 0) continue;
                Vector3 mid = pack.Aggregate(Vector3.Zero, (sum, c) => sum + c.Position) / pack.Count;
                worst = MathF.Max(worst, pack.Max(c => WildSim.Flat(c.Position - mid).Length()));
            }
            float allowed = rules.Spacing * 3f + 4f;
            Check(worst <= allowed && pack[0].Pack.Phase == PackPhase.Roam, $"a roaming pack stays together over 60 s (5 {s.Form}s, widest {worst:F1} m from the middle, allowed {allowed:F1} m)");
        }

        // The hunt: a target that stands still and can't die, 28 m away.
        {
            WildSim w = NewSim(s.Seed + 1, obstacles: false);
            List<Creature> pack = w.SpawnGroup(s, Vector3.Zero, 5);
            Pack p = pack[0].Pack;
            var target = new DummyTarget { Name = "Prey", Health = new Health(1e9f), Position = Ground(w, new Vector3(28f, 0f, 6f)) };
            w.Targets.Add(target);
            var commits = new List<(float time, Creature c)>();
            var firstHits = new Dictionary<Creature, float>();
            float spread = -1f;
            int sides = 0;
            w.Committed += e =>
            {
                commits.Add((e.Time, e.Creature));
                if (spread >= 0f) return;
                // At the first commit: where is everyone round the prey?
                var angles = pack.Where(c => c.Alive).Select(c => MathF.Atan2(c.Position.X - target.Position.X, c.Position.Z - target.Position.Z)).OrderBy(a => a).ToList();
                // The spread is the circle minus the widest empty gap between neighbours.
                float gap = angles[0] + MathF.Tau - angles[^1];
                for (int i = 0; i + 1 < angles.Count; i++) gap = MathF.Max(gap, angles[i + 1] - angles[i]);
                spread = 360f - Mathf.RadToDeg(gap);
                float front = p.FrontAngle;
                sides = (angles.Any(a => Geo.WrapAngle(a - front) > 0.5f) ? 1 : 0) + (angles.Any(a => Geo.WrapAngle(a - front) < -0.5f) ? 1 : 0);
            };
            w.Attacked += e => { if (e.Hit && !firstHits.ContainsKey(e.Creature)) firstHits[e.Creature] = e.Damage; };
            for (int step = 0; step < 60 * 45 && commits.Count < 8; step++) w.Step(Dt);

            Check(spread >= 150f && sides == 2, $"the pack surrounds its prey before anyone commits (members spread over {spread:F0} degrees round it, on both sides of the front)");
            float minGap = float.MaxValue;
            for (int i = 1; i < commits.Count; i++) minGap = MathF.Min(minGap, commits[i].time - commits[i - 1].time);
            int attackers = commits.Select(x => x.c).Distinct().Count();
            Check(commits.Count >= 4 && minGap + 1e-3f >= telegraph,
                $"pack commits are staggered by at least the telegraph ({commits.Count} commits by {attackers} members, closest {minGap:F2} s apart, telegraph {telegraph:F2} s)");
            bool full = firstHits.Count > 0 && firstHits.Values.All(d => MathF.Abs(d - s.Stats.Damage) < 1e-3f);
            Check(full, $"each member's first bite lands full damage ({firstHits.Count} members hit, {string.Join(", ", firstHits.Values.Select(d => d.ToString("F0")))} damage each, species damage {s.Stats.Damage:F0})");
        }

        // Breaking off: kill the leader mid-hunt; then, separately, hurt half the pack.
        foreach (bool leader in new[] { true, false })
        {
            WildSim w = NewSim(s.Seed + 2, obstacles: false);
            List<Creature> pack = w.SpawnGroup(s, Vector3.Zero, 4);
            Pack p = pack[0].Pack;
            var target = new DummyTarget { Name = "Prey", Health = new Health(1e9f), Position = Ground(w, new Vector3(22f, 0f, 0f)) };
            w.Targets.Add(target);
            for (int step = 0; step < 60 * 30 && p.Phase is not (PackPhase.Surround or PackPhase.Engage); step++) w.Step(Dt);
            PackPhase was = p.Phase;
            if (leader) w.Kill(p.Leader, target);
            else foreach (Creature c in pack.Take(2)) w.Damage(c, c.Stats.MaxHealth * 0.5f, null);
            var before = pack.Where(c => c.Alive).ToDictionary(c => c, c => WildSim.Flat(c.Position - target.Position).Length());
            for (int step = 0; step < 30; step++) w.Step(Dt);
            bool retreating = p.Phase == PackPhase.Retreat;
            for (int step = 0; step < 150; step++) w.Step(Dt);
            float gained = before.Keys.Where(c => c.Alive).Min(c => WildSim.Flat(c.Position - target.Position).Length() - before[c]);
            Check(retreating && gained > 4f,
                $"the pack breaks off together when {(leader ? "its leader dies" : "half of it is hurt")} (was {was}, every survivor {gained:F1}+ m further off 3 s later)");
        }
    }

    void Ambushers()
    {
        Species s = null;
        for (ulong seed = 1; seed < 200 && s == null; seed++)
            s = _gen.Generate(seed * 13, 0.7f, false).Species.FirstOrDefault(x => x.Behaviour == Behaviour.Ambusher && x.Stats.Damage > 0f && x.Rig is RigKind.Legged or RigKind.Chain);
        if (s == null) { Fail("no ambusher species found to test"); return; }

        WildSim w = NewSim(s.Seed, obstacles: false);
        Creature c = w.Spawn(s, Vector3.Zero);
        var target = new DummyTarget { Position = Ground(w, new Vector3(0f, 0f, -Math.Min(4f, s.Stats.AmbushRadius * 0.6f))) };
        w.Targets.Add(target);
        int telegraphs = 0, hits = 0;
        w.Telegraph += _ => telegraphs++;
        w.Attacked += e => { if (e.Hit) hits++; };

        w.Clock.Time01 = 0.45f; // late morning
        float hidden = 0f;
        for (int step = 0; step < 1200; step++)
        {
            w.Step(Dt);
            if (step > 600) hidden = MathF.Max(hidden, c.WantHidden);
        }
        bool hidByDay = telegraphs == 0 && c.State == CState.Hidden && hidden > 0.9f;
        Check(hidByDay, $"ambusher ({s.Name}, {s.Rig}) stays hidden by day with you {target.Position.Length():F1} m away ({telegraphs} attacks)");

        w.Clock.Time01 = 0.9f; // night
        for (int step = 0; step < 600 && hits == 0; step++) w.Step(Dt);
        Check(telegraphs > 0 && hits > 0, $"ambusher strikes at night ({telegraphs} telegraphed attack(s), {hits} hit(s))");
    }

    // ---- Harvesting, storage, codex -----------------------------------------------------

    void Harvesting()
    {
        WildSim w = NewSim(7, obstacles: false);
        var field = new HarvestField(_data.Harvest, w.Nav);
        Plant tree = field.Add(PlantKind.Tree, Ground(w, new Vector3(0f, 0f, -4f)), 0.4f, 7f, 2.5f, 0);
        var storage = new Storage(_data.Harvest.StorageCapacity);
        var blades = new HarvesterBlades(_data.Harvest);
        var vessel = new Transform3D(Basis.Identity, Ground(w, Vector3.Zero) + Vector3.Up * 0.8f);
        bool blockedBefore = w.Nav.Blocked(tree.Position);
        for (int step = 0; step < 600 && tree.Alive; step++) blades.Tick(vessel, Dt, field, w, storage, null);
        Check(!tree.Alive && storage.Count(Items.Wood) == _data.Harvest.WoodPerTree && storage.Count(Items.Organics) > 0,
            $"harvesting a tree removes it and adds its wood ({storage.Count(Items.Wood)} wood, {storage.Count(Items.Organics)} organics)");
        Check(blockedBefore && !w.Nav.Blocked(tree.Position), "a felled tree stops blocking the nav grid");

        var near = new List<Vector3> { tree.Position + new Vector3(20f, 0f, 0f) };
        for (float t = 0f; t < _data.Harvest.RegrowSeconds + 30f; t += 0.5f) field.Update(0.5f, near);
        bool stayedDown = !tree.Alive;
        var far = new List<Vector3> { tree.Position + new Vector3(_data.Harvest.RegrowBlockRadius + 30f, 0f, 0f) };
        field.Update(0.5f, far);
        Check(stayedDown && tree.Alive && w.Nav.Blocked(tree.Position),
            $"a felled tree regrows only when no player is within {_data.Harvest.RegrowBlockRadius:F0} m (waited {_data.Harvest.RegrowSeconds + 30f:F0} s nearby)");
    }

    /// <summary>
    /// Tree trunks are solid to creatures: one driven straight at a trunk slides round it, and a
    /// hunter chasing prey on the far side of one goes round it, never through.
    /// </summary>
    void CreaturesKeepOffTrunks()
    {
        Species walker = null, hunter = null;
        for (ulong seed = 1; seed < 300 && (walker == null || hunter == null); seed++)
            foreach (Species x in _gen.Generate(seed * 433, 0.4f, false).Species)
            {
                if (walker == null && x.Rig == RigKind.Legged && x.Plan != BodyPlan.Giant) walker = x;
                if (hunter == null && x.Behaviour == Behaviour.Predator && x.Rig == RigKind.Legged && x.Plan != BodyPlan.Giant) hunter = x;
            }
        if (walker == null || hunter == null) { Fail("no walking species found for the trunk test"); return; }

        // Pushed straight at a trunk, a hair off its middle, for 8 s.
        WildSim w = NewSim(31, obstacles: false);
        var field = new HarvestField(_data.Harvest, w.Nav);
        Plant tree = field.Add(PlantKind.Tree, Ground(w, new Vector3(0f, 0f, -8f)), 0.45f, 8f, 3f, 0);
        Creature c = w.Spawn(walker, new Vector3(0.05f, 0f, 0f));
        float allowed = tree.TrunkRadius + c.Radius * 0.5f, closest = float.MaxValue;
        bool past = false;
        for (int step = 0; step < 480; step++)
        {
            c.Desired = new Vector3(0f, 0f, -c.Stats.WalkSpeed);
            Locomotion.Move(w, c, Dt);
            closest = MathF.Min(closest, WildSim.Flat(c.Position - tree.Position).Length());
            past |= c.Position.Z < tree.Position.Z - allowed;
        }

        // A hunter chasing prey that stands behind a trunk.
        WildSim w2 = NewSim(32, obstacles: false);
        var field2 = new HarvestField(_data.Harvest, w2.Nav);
        Plant tree2 = field2.Add(PlantKind.Tree, Ground(w2, new Vector3(0f, 0f, -7f)), 0.45f, 8f, 3f, 0);
        Creature h = w2.Spawn(hunter, Vector3.Zero);
        var prey = new DummyTarget { Name = "Prey", Position = Ground(w2, new Vector3(0f, 0f, -12f)), Health = new Health(1e9f) };
        w2.Targets.Add(prey);
        h.Noticed = prey; // it saw where they went, before the trunk came between them
        h.Awareness = 1f;
        h.NoticedAt = w2.Time;
        h.LastSeen = prey.Position;
        float allowed2 = tree2.TrunkRadius + h.Radius * 0.5f, closest2 = float.MaxValue;
        bool reached = false;
        w2.Attacked += e => reached = true;
        for (int step = 0; step < 900 && !reached; step++)
        {
            w2.Step(Dt);
            closest2 = MathF.Min(closest2, WildSim.Flat(h.Position - tree2.Position).Length());
        }
        Check(closest >= allowed - 1e-3f && past && closest2 >= allowed2 - 1e-3f && reached,
            $"creatures can't pass through a tree trunk: one pushed straight at it slides round (closest {closest:F2} m from its middle, trunk + half body {allowed:F2} m), a hunter goes round one to reach its prey (closest {closest2:F2} m)");
    }

    /// <summary>
    /// In the real scene: the player walks into a trunk and stops against it; the hovercraft
    /// with a full hold drives into one and is held off it; chopping a tree drops its collider
    /// the moment it falls, and the player walks through where it stood.
    /// </summary>
    async System.Threading.Tasks.Task TrunksBlock(Main main)
    {
        Player p = main.Player;
        float maxHealth = p.Health.Max;
        p.Health.Max = p.Health.Current = 1e6f; // a passing hunter must not end the staging in a respawn
        HarvestField field = main.Forest.Field;
        Vector3 start = p.GlobalPosition;
        List<Plant> trees = field.Plants.Where(x => x.Kind == PlantKind.Tree && x.Alive).OrderBy(x => WildSim.Flat(x.Position - start).Length()).ToList();
        int collidersOk = trees.Count(x => main.Forest.TrunkOf(x) is { Disabled: false, Shape: CylinderShape3D cyl } && MathF.Abs(cyl.Radius - x.TrunkRadius) < 1e-4f);
        Check(collidersOk == trees.Count && trees.Count > 50, $"every standing tree has a trunk collider ({collidersOk} of {trees.Count})");

        // Walk straight into the nearest trunk for two seconds.
        Plant tree = trees[0];
        Vector3 away = WildSim.Flat(start - tree.Position).Normalized();
        p.Teleport(Ground(main, tree.Position + away * 3f) + Vector3.Up * 0.1f);
        p.SetLook(MathF.Atan2(away.X, away.Z), 0f);
        await Frames(2);
        float closest = float.MaxValue;
        Hold(Key.W, true);
        for (int i = 0; i < 120; i++)
        {
            await Frames(1);
            closest = MathF.Min(closest, WildSim.Flat(p.GlobalPosition - tree.Position).Length());
        }
        Hold(Key.W, false);
        bool stopped = closest >= tree.TrunkRadius + 0.3f;

        // Fell it: the collider goes the moment the tree does, and the player walks on through.
        bool droppedAtOnce = false;
        void OnFelled(Plant felled) { if (felled == tree) droppedAtOnce = main.Forest.TrunkOf(tree).Disabled; }
        field.Felled += OnFelled;
        field.Chip(tree, 1e6f);
        field.Felled -= OnFelled;
        float through = float.MaxValue;
        Hold(Key.W, true);
        for (int i = 0; i < 90; i++)
        {
            await Frames(1);
            through = MathF.Min(through, WildSim.Flat(p.GlobalPosition - tree.Position).Length());
        }
        Hold(Key.W, false);
        Check(stopped && droppedAtOnce && through < tree.TrunkRadius,
            $"the player can't walk through a trunk (stopped {closest:F2} m from its middle, radius {tree.TrunkRadius:F2} m); felling it drops its collider at once, and then they walk over the stump ({through:F2} m)");

        // The hovercraft with a full hold drives at a tree: the blades won't take it, and the hull is held off it.
        Vessel v = main.Vessel;
        Plant target = trees.Skip(1).First(x => trees.All(o => o == x || o == tree || WildSim.Flat(o.Position - x.Position).Length() > 4f));
        Vector3 back = WildSim.Flat(start - target.Position).Normalized();
        v.Yaw = MathF.Atan2(back.X, back.Z); // forward is -back: at the tree
        v.GlobalPosition = Ground(main, target.Position + back * 7f) + Vector3.Up * 0.85f;
        v.Velocity = Vector3.Zero;
        v.Storage.Clear();
        v.Storage.Add(Items.Wood, v.Storage.Capacity);
        v.AutoThrottle = 0.5f;
        float held = float.MaxValue;
        for (int i = 0; i < 180; i++)
        {
            await Frames(1);
            held = MathF.Min(held, WildSim.Flat(v.GlobalPosition - target.Position).Length());
        }
        bool standing = target.Alive;
        // Room in the hold again: now it cuts, and the trunk stops blocking the moment it falls.
        v.Storage.Clear();
        bool cutAtOnce = false, felledIt = false;
        void OnCut(Plant felled) { if (felled == target) { felledIt = true; cutAtOnce = main.Forest.TrunkOf(target).Disabled; } }
        field.Felled += OnCut;
        for (int i = 0; i < 600 && !felledIt; i++) await Frames(1);
        field.Felled -= OnCut;
        v.AutoThrottle = 0f;
        v.Storage.Clear();
        p.Health.Max = p.Health.Current = maxHealth;
        Check(standing && held >= target.TrunkRadius + 0.45f && felledIt && cutAtOnce,
            $"the hovercraft is held off a tree it isn't cutting (closest {held:F2} m from the trunk with a full hold, tree still standing); once there is room it cuts it down and the trunk's collider goes as it falls");
    }

    static Vector3 Ground(Main main, Vector3 p) => new(p.X, main.Planet.Terrain.Height(p.X, p.Z), p.Z);

    void RunDown()
    {
        PlanetFauna f = _gen.Generate(99, 0.5f, false);
        Species s = f.Species.First(x => x.Rig is RigKind.Legged or RigKind.Hopper or RigKind.Chain && x.Plan != BodyPlan.Giant);
        WildSim w = NewSim(99, obstacles: false);
        Creature c = w.Spawn(s, new Vector3(0f, 0f, -3.6f));
        var storage = new Storage(_data.Harvest.StorageCapacity);
        var blades = new HarvesterBlades(_data.Harvest);
        var vessel = new Transform3D(Basis.Identity, Ground(w, Vector3.Zero) + Vector3.Up * 0.8f);
        for (int step = 0; step < 300 && !c.Removed; step++)
        {
            blades.Tick(vessel, Dt, null, w, storage, null);
            w.Step(Dt);
        }
        Check(c.Removed && c.Harvested && storage.Count(Items.Meat) > 0,
            $"running down a {s.CommonName} puts its meat in the hold ({storage.Count(Items.Meat)} meat{(storage.Items.Count > 1 ? " and a rare part" : "")})");
    }

    void StorageCap()
    {
        var storage = new Storage(50);
        int wood = storage.Add(Items.Wood, 80), meat = storage.Add(Items.Meat, 5);
        Check(wood == 50 && meat == 0 && storage.Total == 50 && storage.Full, $"the storage cap is enforced (took {wood} of 80 wood, then {meat} meat; {storage.Total}/{storage.Capacity})");

        WildSim w = NewSim(8, obstacles: false);
        var field = new HarvestField(_data.Harvest, w.Nav);
        Plant tree = field.Add(PlantKind.Tree, Ground(w, new Vector3(0f, 0f, -4f)), 0.4f, 7f, 2.5f, 0);
        // Nothing harvested is thrown away: with too little room the blades leave the tree alone.
        var small = new Storage(10);
        var blades = new HarvesterBlades(_data.Harvest);
        var vessel = new Transform3D(Basis.Identity, Ground(w, Vector3.Zero) + Vector3.Up * 0.8f);
        int refused = 0;
        for (int step = 0; step < 120; step++) refused += blades.Tick(vessel, Dt, field, w, small, null).Refused;
        bool untouched = tree.Alive && tree.Health == tree.MaxHealth && small.Total == 0 && refused > 0;
        int need = field.RemainingYield(tree);
        var exact = new Storage(need);
        for (int step = 0; step < 600 && tree.Alive; step++) blades.Tick(vessel, Dt, field, w, exact, null);
        Check(untouched && !tree.Alive && exact.Full,
            $"the blades only cut what the hold can take: a 10-unit hold leaves a {need}-unit tree untouched, a {need}-unit hold takes all of it");

        // Same for creatures: no room for the whole carcass, no chewing.
        Species s = _gen.Generate(99, 0.5f, false).Species.First(x => x.Rig is RigKind.Legged or RigKind.Hopper && x.Plan != BodyPlan.Giant);
        WildSim w2 = NewSim(99, obstacles: false);
        Creature c = w2.Spawn(s, new Vector3(0f, 0f, -3.6f));
        var tiny = new Storage(1);
        int refusedC = 0;
        for (int step = 0; step < 60; step++) { refusedC += blades.Tick(vessel, Dt, null, w2, tiny, null).Refused; w2.Step(Dt); }
        Check(refusedC > 0 && c.Alive && c.Health == c.Stats.MaxHealth && tiny.Total == 0,
            $"with no room for its {s.Drops.Meat} meat, the blades leave a {s.CommonName} unharmed");
    }

    /// <summary>
    /// The codex records who met a species first, once, and raises one discovery for it: the
    /// event the game hangs the discovery card on. Later sightings, by anyone, change nothing.
    /// </summary>
    void CodexFirstFinder()
    {
        var codex = new Codex();
        Species s = _gen.Generate(5, 0.5f, false).Species[0];
        var raised = new List<CodexEntry>();
        codex.Discovered += raised.Add;
        bool first = codex.Record(s, "Pip", 10f, "Test IV");
        bool again = codex.Record(s, "Rex", 20f, "Test IV");
        bool againPip = codex.Record(s, "Pip", 30f, "Test IV");
        CodexEntry e = codex.Get(s.Id);
        Check(first && !again && !againPip && codex.Count == 1 && raised.Count == 1 && raised[0] == e && e.Finder == "Pip" && e.FoundAt == 10f
              && e.Name == s.Name && e.Form == s.FormLabel && e.Behaviour == s.BehaviourLabel && e.Size == s.Body.Size && e.Planet == "Test IV",
            $"the codex records the first finder, once, with one discovery raised ({e?.Name}: {e?.Form}, {e?.Behaviour}, {e?.Size:F1} m, first found by {e?.Finder} on {e?.Planet})");
    }

    // ---- Bases: nothing spawns near them -------------------------------------------------

    /// <summary>
    /// No creature spawns or respawns within the clearance of a base, measured from its
    /// footprint's edge. An hour of a planet's fauna being killed off and topped back up, with a
    /// small base right where the herd would start, a big one out in the hills, a capital ship
    /// parked between and a player strolling through all of it: every spawn is checked.
    /// </summary>
    void NoSpawnsNearBases()
    {
        SpawnRules rules = _data.Spawning;
        float clear = rules.BaseClearance;
        const ulong Seed = 4040;
        PlanetFauna f = _gen.Generate(Seed, 0.5f, false);
        var groves = new List<Vector3> { new(-30f, 0f, -40f), new(110f, 0f, 20f), new(-90f, 0f, 120f), new(40f, 0f, -150f) };

        WildSim w = NewSim(Seed);
        var hut = new Structure { Name = "hut", Center = Ground(w, new Vector3(0f, 0f, -22f)), HalfExtents = new Vector2(3f, 3f) };
        var fortress = new Structure { Name = "fortress", Center = Ground(w, new Vector3(125f, 0f, 115f)), HalfExtents = new Vector2(40f, 28f), Yaw = 0.6f };
        var carrier = new Structure { Name = "carrier", Mode = CoreMode.Capital, Center = Ground(w, new Vector3(-100f, 0f, -80f)), HalfExtents = new Vector2(30f, 10f), Yaw = -0.3f };
        w.Structures.AddRange(new[] { hut, fortress, carrier });
        var player = new DummyTarget { Name = "Stroller", Health = new Health(1e9f) };
        w.Targets.Add(player);

        var spawns = new List<Vector3>();
        float nearestPlayer = float.MaxValue;
        bool respawning = false;
        w.Spawned += c =>
        {
            spawns.Add(c.Position);
            if (respawning) nearestPlayer = MathF.Min(nearestPlayer, WildSim.Flat(c.Position - player.Position).Length());
        };
        var spawner = new Spawner(w, f, groves, Seed);
        spawner.Populate();
        int opening = spawns.Count;
        respawning = true;

        var rng = new Rng(17);
        const float Step = 0.5f;
        int kills = 0;
        for (int i = 0; i < (int)(3600f / Step); i++)
        {
            float t = i * Step;
            // The player strolls a wide loop through everything, the bases included.
            Vector3 at = new(MathF.Cos(t * 0.011f) * 160f, 0f, MathF.Sin(t * 0.017f) * 160f);
            Vector3 ahead = new(MathF.Cos((t + 1f) * 0.011f) * 160f, 0f, MathF.Sin((t + 1f) * 0.017f) * 160f);
            player.Position = Ground(w, at);
            player.Forward = (ahead - at).Normalized();
            // Something dies every few seconds: shot, harvested or eaten.
            if (i % 8 == 0)
            {
                List<Creature> alive = w.Creatures.Where(c => !c.Removed && c.Alive).ToList();
                if (alive.Count > 0)
                {
                    Creature victim = alive[rng.Int(0, alive.Count - 1)];
                    w.Kill(victim, null);
                    victim.Removed = true;
                    kills++;
                }
            }
            spawner.Step(Step);
        }

        int inside = spawns.Count(p => w.InBaseZone(p));
        float nearest = spawns.Min(p => BaseZones.NearestBase(w.Structures, p));
        Check(inside == 0 && spawner.Respawned > 300 && spawner.RejectedForBase > 0,
            $"no creature spawns or respawns within {clear:F0} m of a base: {spawns.Count} spawns ({opening} at the start, {spawner.Respawned} respawns over an hour with {kills} kills), nearest {nearest:F1} m from a base's edge; {spawner.RejectedForBase} spots turned down for a base");
        Check(nearestPlayer >= rules.MinPlayerDistance,
            $"respawns keep away from the player and out of view (nearest {nearestPlayer:F0} m, at least {rules.MinPlayerDistance:F0}; {spawner.RejectedForPlayers} spots turned down)");

        // The zone runs from the footprint's edge, so a big base keeps a bigger one.
        bool EdgeAt(Structure s)
        {
            Vector3 along = new Basis(Vector3.Up, s.Yaw) * Vector3.Right;
            float reach = s.HalfExtents.X + clear;
            return BaseZones.Blocks(new[] { s }, s.Center + along * (reach - 0.5f), clear) && !BaseZones.Blocks(new[] { s }, s.Center + along * (reach + 0.5f), clear);
        }
        float Area(Structure s)
        {
            int n = 0;
            for (float x = -400f; x <= 400f; x += 4f)
                for (float z = -400f; z <= 400f; z += 4f)
                    if (BaseZones.Blocks(new[] { s }, s.Center + new Vector3(x, 0f, z), clear)) n++;
            return n * 16f;
        }
        float hutArea = Area(hut), fortArea = Area(fortress);
        float hutReach = hut.HalfExtents.X + clear, fortReach = fortress.HalfExtents.X + clear;
        float nearHut = spawns.Min(p => WildSim.Flat(p - hut.Center).Length()), nearFort = spawns.Min(p => WildSim.Flat(p - fortress.Center).Length());
        Check(EdgeAt(hut) && EdgeAt(fortress) && fortArea > hutArea * 2f && nearHut >= hut.HalfExtents.X + clear && nearFort >= fortress.HalfExtents.Y + clear,
            $"a large base gets a larger zone: it runs {clear:F0} m from the footprint's edge, {hutReach:F0} m out from a 6 m hut's centre and {fortReach:F0} m from an 80 x 56 m fortress's ({hutArea / 1e4f:F1} ha against {fortArea / 1e4f:F1} ha); nearest spawns {nearHut:F0} m and {nearFort:F0} m from their centres");

        // Capital mode blocks nothing: a ship parked where the herd starts gets creatures round it;
        // switch the same core to base mode and none come within the clearance.
        (int near, float closest) Opening(CoreMode mode)
        {
            WildSim ws = NewSim(Seed);
            var parked = new Structure { Name = "core", Mode = mode, Center = Ground(ws, new Vector3(0f, 0f, -22f)), HalfExtents = new Vector2(12f, 8f) };
            ws.Structures.Add(parked);
            var placed = new List<Vector3>();
            ws.Spawned += c => placed.Add(c.Position);
            new Spawner(ws, f, groves, Seed).Populate();
            return (placed.Count(p => parked.DistanceToFootprint(p) < clear), placed.Min(p => parked.DistanceToFootprint(p)));
        }
        (int capitalNear, float capitalClosest) = Opening(CoreMode.Capital);
        (int baseNear, float baseClosest) = Opening(CoreMode.Base);
        int byCarrier = spawns.Count(p => carrier.DistanceToFootprint(p) < clear);
        Check(capitalNear > 0 && baseNear == 0 && byCarrier > 0,
            $"capital-mode structures don't block spawns: {capitalNear} creatures spawn within {clear:F0} m of a capital core parked where the herd starts (closest {capitalClosest:F1} m), none once it is in base mode (closest {baseClosest:F1} m); over the hour {byCarrier} spawned near the parked carrier");

        // The clearance is past the furthest any creature can notice a player, so nothing new
        // appears close enough to have seen you at home.
        float longest = 0f;
        for (ulong seed = 1; seed <= 200; seed++)
            foreach (Species s in _gen.Generate(seed * 9137, seed % 4 * 0.33f, seed % 6 == 0).Species)
                longest = MathF.Max(longest, s.Stats.Perception);
        Check(clear > longest, $"the {clear:F0} m clearance is past the longest perception any creature rolls ({longest:F0} m over 200 planets)");
    }

    /// <summary>
    /// Only spawning is blocked. A herd that settles just outside a base's zone still grazes its
    /// way in, and a hunter shot at from the base charges all the way in and bites.
    /// </summary>
    void WanderersStillEnter()
    {
        float clear = _data.Spawning.BaseClearance;
        Species grazers = null;
        for (ulong seed = 1; seed < 200 && grazers == null; seed++)
            grazers = _gen.Generate(seed * 211, 0.3f, false).Species.FirstOrDefault(x => x.Behaviour == Behaviour.Herd && x.Rig == RigKind.Legged && x.Plan != BodyPlan.Giant);
        Species hunter = null;
        for (ulong seed = 1; seed < 400 && hunter == null; seed++)
            hunter = _gen.Generate(seed * 223, 0.4f, false).Species.FirstOrDefault(x => x.Behaviour == Behaviour.Predator && x.Rig == RigKind.Legged && x.Stats.Retaliates && x.Plan != BodyPlan.Giant);
        if (grazers == null || hunter == null) { Fail("no herd or retaliating predator species found for the base tests"); return; }

        // The herd: spawned legally, its edge a few metres outside the zone.
        WildSim w = NewSim(grazers.Seed, obstacles: false);
        var outpost = new Structure { Name = "outpost", Center = Ground(w, Vector3.Zero), HalfExtents = new Vector2(8f, 6f) };
        w.Structures.Add(outpost);
        float spread = w.GroupRadius(grazers, 6);
        Vector3 home = Ground(w, new Vector3(outpost.HalfExtents.X + clear + spread + 4f, 0f, 0f));
        List<Creature> herd = w.SpawnGroup(grazers, home, 6);
        bool spawnedOutside = herd.Count == 6 && herd.All(c => !w.InBaseZone(c.Position));
        var entered = new HashSet<Creature>();
        float deepest = float.MaxValue;
        for (int step = 0; step < 60 * 60 * 10; step++)
        {
            w.Step(Dt);
            if (step % 30 != 0) continue;
            foreach (Creature c in herd)
            {
                float d = outpost.DistanceToFootprint(c.Position);
                deepest = MathF.Min(deepest, d);
                if (d < clear) entered.Add(c);
            }
        }
        Check(spawnedOutside && entered.Count > 0,
            $"creatures already outside may wander in: {entered.Count} of a herd of 6 spawned outside the zone grazed into it within 10 minutes (deepest {deepest:F0} m from the base's edge)");

        // The hunter: shot at from inside the base every few seconds, it comes for the shooter.
        WildSim w2 = NewSim(hunter.Seed, obstacles: false);
        var keep = new Structure { Name = "keep", Center = Ground(w2, Vector3.Zero), HalfExtents = new Vector2(8f, 6f) };
        w2.Structures.Add(keep);
        var defender = new DummyTarget { Name = "Defender", Position = Ground(w2, Vector3.Zero), Health = new Health(1e9f) };
        w2.Targets.Add(defender);
        Vector3 start = Ground(w2, new Vector3(keep.HalfExtents.X + clear + 2f, 0f, 0f));
        Creature c2 = w2.Spawn(hunter, start);
        bool legal = !w2.InBaseZone(c2.Position);
        AttackEvent? bite = null;
        w2.Attacked += e => bite ??= e;
        float reachedAt = -1f;
        for (int step = 0; step < 60 * 60 && bite == null; step++)
        {
            if (step % 240 == 0) w2.Damage(c2, 1f, defender); // a shot from the base
            w2.Step(Dt);
        }
        if (bite != null) reachedAt = bite.Value.Time;
        Check(legal && bite != null && keep.DistanceToFootprint(c2.Position) < 3f,
            $"attackers still reach a base: a {hunter.CommonName} spawned outside the zone, shot at from the base, ran {start.Length():F0} m in and attacked after {reachedAt:F0} s");
    }

    // ---- Performance --------------------------------------------------------------------

    void Performance()
    {
        PlanetFauna f = _gen.Generate(2026, 0.8f, false);
        List<Species> walkers = f.Species.Where(s => s.Rig != RigKind.Swarm).ToList();
        Species swarm = f.Species.FirstOrDefault(s => s.Rig == RigKind.Swarm) ?? SwarmSpecies();
        WildSim w = NewSim(2026);
        var target = new DummyTarget { Health = new Health(1e9f), Position = Ground(w, Vector3.Zero) };
        w.Targets.Add(target);
        var rng = new Rng(5);
        for (int i = 0; i < 200; i++)
        {
            Species s = walkers[i % walkers.Count];
            w.Spawn(s, w.FreeSpot(new Vector3(rng.Range(-150f, 150f), 0f, rng.Range(-150f, 150f)), rng));
        }
        Creature cloud = w.Spawn(swarm, new Vector3(5f, 0f, 5f));
        int members = cloud.Rig is SwarmRig sr ? sr.Members.Length : 0;
        // Long enough for .NET's tiered JIT to finish optimising the hot paths.
        for (int i = 0; i < 600; i++) w.Step(Dt);

        // Three windows of 300 frames, judged on the best: another process on the machine can
        // slow a whole window down, and that is the machine's noise, not the code's cost. All
        // three are printed.
        const int Frames = 900, Window = 300;
        w.Profile = true;
        var windows = new List<double>();
        double max = 0;
        for (int k = 0; k < Frames / Window; k++)
        {
            (double a, double m) = Time(() => w.Step(Dt), Window);
            windows.Add(a);
            max = Math.Max(max, m);
        }
        w.Profile = false;
        double avg = windows.Min();
        GD.Print($"PERF  sim + rigs, all 200 creatures + a swarm of {members} at full rate: {avg:F2} ms/frame (best of three 300-frame windows: {string.Join(", ", windows.Select(x => x.ToString("F2")))}), {max:F2} ms worst frame");
        GD.Print($"PERF    of which brains {w.ThinkMs / Frames:F2} ms, locomotion {w.MoveMs / Frames:F2} ms, rigs {w.RigMs / Frames:F2} ms");
        var perKind = new List<string>();
        foreach (var group in w.Creatures.Where(c => !c.Removed).GroupBy(c => c.Species.Rig))
        {
            List<Creature> list = group.ToList();
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < 20; r++) foreach (Creature c in list) c.Rig.Update(w, c, Dt);
            perKind.Add($"{group.Key} {sw.Elapsed.TotalMilliseconds * 1000.0 / (20 * list.Count):F1} us x{list.Count}");
        }
        GD.Print($"PERF    rig update per creature: {string.Join(", ", perKind)}");

        var view = new WildlifeView { Name = "PerfView" };
        AddChild(view);
        view.Bind(w, null);
        for (int i = 0; i < 60; i++) { w.Step(Dt); view.Sync(Vector3.Zero); }
        var viewWindows = new List<double>();
        double vMax = 0;
        for (int k = 0; k < Frames / Window; k++)
        {
            (double a, double m) = Time(() => { w.Step(Dt); view.Sync(Vector3.Zero); }, Window);
            viewWindows.Add(a);
            vMax = Math.Max(vMax, m);
        }
        double vAvg = viewWindows.Min();
        GD.Print($"PERF  as the game runs it, with distance LOD and syncing {view.NodeCount} mesh nodes ({view.ViewCount} views): {vAvg:F2} ms/frame (windows: {string.Join(", ", viewWindows.Select(x => x.ToString("F2")))}), {vMax:F2} ms worst frame (headless, no GPU)");
        // The budget is checked on what the game runs every frame (sim, rigs on their distance
        // schedule, and syncing every mesh). Posing every rig every frame is the worst case,
        // guarded more loosely: it has the least headroom when other work shares the machine.
        Check(vAvg < 4.0, $"200 creatures + a swarm cost {vAvg:F2} ms/frame as the game runs them, sim, rigs and mesh sync (budget 4 ms, best of three windows)");
        Check(avg < 6.0, $"worst case, every rig posed every frame: {avg:F2} ms/frame (guard 6 ms, best of three windows)");
        view.QueueFree();
    }

    Species SwarmSpecies()
    {
        for (ulong seed = 1; ; seed++)
            if (_gen.Generate(seed, 0.8f, false).Species.FirstOrDefault(s => s.Rig == RigKind.Swarm) is { } s) return s;
    }

    static (double avg, double max) Time(Action step, int frames)
    {
        var sw = new Stopwatch();
        double total = 0, max = 0;
        for (int i = 0; i < frames; i++)
        {
            sw.Restart();
            step();
            double ms = sw.Elapsed.TotalMilliseconds;
            total += ms;
            max = Math.Max(max, ms);
        }
        return (total / frames, max);
    }

    // ---- Helpers ------------------------------------------------------------------------

    WildSim NewSim(ulong seed, bool obstacles = true)
    {
        var terrain = new Terrain(seed);
        var nav = new NavGrid(terrain.HalfSize, 3f);
        if (obstacles)
        {
            var rng = Rng.For(seed, "test-rocks");
            for (int i = 0; i < 60; i++)
            {
                var p = new Vector3(rng.Range(-120f, 120f), 0f, rng.Range(-120f, 120f));
                if (p.Length() > 15f) nav.AddObstacle(p, rng.Range(0.6f, 3f));
            }
        }
        return new WildSim(_data, terrain, nav);
    }

    static Vector3 Ground(WildSim w, Vector3 p) => new(p.X, w.Terrain.Height(p.X, p.Z), p.Z);

    void Check(bool ok, string what)
    {
        _checks++;
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
