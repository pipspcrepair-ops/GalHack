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
    int _failures;

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
            RunDown();
            StorageCap();
            CodexIsSilent();
            Performance();
            await PlayableScene();
        }
        catch (Exception e)
        {
            Fail($"crashed: {e}");
        }
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- The playable scene, driven through its real input path -------------------------

    async System.Threading.Tasks.Task PlayableScene()
    {
        var main = new Main { Name = "Main" };
        AddChild(main);
        await Frames(30);
        int live = main.Sim.Creatures.Count(c => !c.Removed);
        Check(live > 30 && main.View.ViewCount == live, $"the playable planet spawns its fauna ({live} creatures on {main.Fauna.PlanetName}, {main.View.ViewCount} views)");
        await FirstSightingIsSilent(main);

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
    /// Meeting a creature for the first time puts nothing on screen and plays nothing: no card,
    /// banner, toast or chime. Puts a never-seen species right in front of the camera, waits
    /// until the codex has silently recorded it, and compares everything the HUD shows.
    /// </summary>
    async System.Threading.Tasks.Task FirstSightingIsSilent(Main main)
    {
        Species s = main.Fauna.Species.First(x => !main.Codex.Knows(x.Id) && x.Rig != RigKind.Swarm);
        Vector3 eye = main.Camera.GlobalPosition, look = -main.Camera.GlobalBasis.Z;
        await Frames(2);
        string Snapshot() => string.Join("|", VisibleControls(main).Select(c => c is Label l ? $"{c.Name}:{StripLive(l.Text)}" : c.Name.ToString()));
        string before = Snapshot();
        int playersBefore = Playing(main);
        int known = main.Codex.Count;

        Creature c = main.Sim.Spawn(s, WildSim.Flat(eye + WildSim.Flat(look).Normalized() * 6f));
        c.Yaw = main.Player.Yaw;
        bool recorded = false;
        string shown = null;
        for (int i = 0; i < 90; i++)
        {
            await Frames(1);
            recorded |= main.Codex.Knows(s.Id);
            string now = Snapshot();
            if (now != before) shown ??= now;
            if (VisibleControls(main).OfType<Label>().Any(l => l.Text.Contains(s.Name))) shown ??= $"the name {s.Name} on screen";
        }
        int playersAfter = Playing(main);
        Check(recorded && main.Codex.Count == known + 1 && shown == null && playersAfter <= playersBefore,
            $"a first sighting is recorded silently: codex {known} -> {main.Codex.Count}, nothing new on screen{(shown != null ? $" (but showed: {shown})" : "")}, no UI sound");
        main.Sim.Remove(c);
    }

    static IEnumerable<Control> VisibleControls(Node root)
    {
        foreach (Node n in root.FindChildren("*", "Control", true, false))
            if (n is Control { Visible: true } ctl && ctl.IsVisibleInTree() && (ctl is not Label l || l.Text.Trim() != "")) yield return ctl;
    }

    /// <summary>The status text's live numbers (clock, fps, health, counts) change by themselves; blank them out.</summary>
    static string StripLive(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"[0-9]+", "#");

    /// <summary>Non-positional sound players that are playing: a UI chime would be one.</summary>
    static int Playing(Node root) => root.FindChildren("*", "AudioStreamPlayer", true, false).Count(n => n is AudioStreamPlayer { Playing: true });

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
                            if (err > tol) { problem = $"planted foot {i} off the ground by {err:F2} m (tolerance {tol:F2}): {what}"; break; }
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
            s = _gen.Generate(seed * 13, 0.7f, false).Species.FirstOrDefault(x => x.Behaviour == Behaviour.Ambusher && x.Stats.Damage > 0f);
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
    /// The codex records who saw a species first, silently. It has no event to hang an
    /// announcement on, and the HUD has nothing that could show one.
    /// </summary>
    void CodexIsSilent()
    {
        var codex = new Codex();
        Species s = _gen.Generate(5, 0.5f, false).Species[0];
        bool first = codex.Record(s, "Pip", 10f, "Test IV");
        bool again = codex.Record(s, "Rex", 20f, "Test IV");
        CodexEntry e = codex.Get(s.Id);
        Check(first && !again && codex.Count == 1 && e.Finder == "Pip" && e.FoundAt == 10f,
            $"a sighting is recorded once, to the first finder ({e?.Name}, seen first by {e?.Finder})");

        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        string[] announce = { "card", "discover", "newspecies", "announce", "toast", "banner", "popup" };
        var leftovers = new[] { typeof(Codex), typeof(Hud), typeof(Main), typeof(ProceduralAudio) }
            .SelectMany(t => t.GetMembers(flags).Select(m => $"{t.Name}.{m.Name}"))
            .Where(n => announce.Any(a => n.ToLowerInvariant().Contains(a)))
            .ToList();
        Check(typeof(Codex).GetEvents(flags).Length == 0 && leftovers.Count == 0,
            $"no first-encounter announcement code remains (codex has no events; announcement-like members: {(leftovers.Count == 0 ? "none" : string.Join(", ", leftovers))})");
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

        const int Frames = 600;
        w.Profile = true;
        (double avg, double max) = Time(() => w.Step(Dt), Frames);
        w.Profile = false;
        GD.Print($"PERF  sim + rigs, all 200 creatures + a swarm of {members} at full rate: {avg:F2} ms/frame average, {max:F2} ms worst");
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
        (double vAvg, double vMax) = Time(() => { w.Step(Dt); view.Sync(Vector3.Zero); }, Frames);
        GD.Print($"PERF  same, with distance LOD and syncing {view.NodeCount} mesh nodes ({view.ViewCount} views): {vAvg:F2} ms/frame average, {vMax:F2} ms worst (headless, no GPU)");
        Check(avg < 4.0, $"200 creatures + a swarm update in {avg:F2} ms/frame at full rate, every rig every frame (budget 4 ms)");
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
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
