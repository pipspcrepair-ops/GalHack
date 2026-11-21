using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Surface;
using SurvivalVitals.Model;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// After the sleeper dies, and the one bag (owner, 16 Nov), on a fresh world every run:
///
/// 0. A gang kills the sleeper in its place: within 5 s every one of them is out of the fight, and none
///    fires again.
/// 1. A band of raiders kills the sleeper out in the open. Every one of them is out of the fight within
///    5 s; nothing attacks the sleeper or the body for a minute; the band goes back to its day and one of
///    them walks over and rummages at the body and its bag, which holds just what it did (everything the
///    sleeper carried but the toolbar and the tool, which stay with them). After the respawn nothing
///    comes for the spot.
/// 2. One of the band killed drops a bag of their goods (their rifle and its rounds among them).
/// 3. A hungry predator near the body feeds on it.
/// 4. A trader held at gunpoint tosses a bag that lands on the ground in front of the sleeper, between
///    them, a share of their goods; then points at it, and at the way out.
/// 5. A Husk kills the sleeper with the helmet open: it kneels at the body's face and brings its worms up
///    into it, and the body rises a Husk in the sleeper's suit that keeps within 30 m of the bag, and is
///    met there again after going far off and coming back.
/// 6. Every bag rests on what is under it: its lowest point on it, nothing of it inside it.
///
/// With --shots=DIR (windowed): rummage.png, worms.png, risen.png, tossed.png.
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/AftermathTest.tscn
/// </summary>
public partial class AftermathTest : Node
{
    const int WatchdogMinutes = 16;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();
    Game.Game _game;
    Driver _drive;
    string _shots;
    bool _snapping;
    readonly List<Lootable> _bags = new();
    double _max;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--shots=")) _shots = arg["--shots=".Length..];
        if (_shots != null) Directory.CreateDirectory(_shots);
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    IEnumerable<int> Scenario()
    {
        // A gang's place on the world (as CombatTest seats one), to be killed in.
        PlanetSurface.Poi.PoiSiting.ForceFamily = "sewage_works";
        PlanetSurface.Poi.PoiSiting.ForceState = PoiGenerator.Gen.PoiState.Abandoned;
        PlanetSurface.Poi.PoiSiting.ForceOccupancy = new PoiGenerator.Gen.OccupancyRoll(PoiGenerator.Gen.Occupant.Gang);
        WorldChoice world = WorldChoice.Fresh("temperate");
        GD.Print($"-- {world} --");
        _game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        PlanetSurface.Poi.PoiSiting.ForceState = null;
        PlanetSurface.Poi.PoiSiting.ForceOccupancy = null;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.42;
        _max = _game.Suit.Sim.Data.Vitals.Health.Max;
        _game.Zone.Loot.Landed += l => { if (!_bags.Contains(l)) _bags.Add(l); };
        foreach (int x in Gang()) yield return x;
        foreach (int x in Band()) yield return x;
        foreach (int x in Toss()) yield return x;
        foreach (int x in Husk()) yield return x;
        Rests();
        _game.QueueFree();
        yield return 0;
    }

    // ---- 0: killed by a gang in its place ---------------------------------------------------------

    /// <summary>A gang's place, its fighters on the sleeper: killed, every one of them stands down within 5 s and none fires again.</summary>
    IEnumerable<int> Gang()
    {
        GD.Print("-- killed by a gang in its place: every one of them stands down --");
        var site = _game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == "sewage_works" && s.Instance.Occupancy?.Main == PoiGenerator.Gen.Occupant.Gang);
        if (site?.Instance.Townsfolk == null) { GD.Print("  (no gang's works could be seated on this world)"); yield break; }
        var cell = site.Instance.Townsfolk.Spots.First().Cell;
        Vector3 camp = _game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + cell.X + 0.5f, site.Origin.Y + cell.Y, site.Origin.Z + cell.Z + 0.5f), _game.Survivor.WorldFeet);
        _game.Survivor.PlaceOutside(camp.ToNumerics() + System.Numerics.Vector3.UnitY * 0.2f);
        for (int i = 0; i < 3600 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) { TopUp(); yield return 0; }
        Slice.Places.Town town = _game.Places.PlaceOf(site)?.Memory.Town;
        if (town == null) { Check(false, "the gang's place stands with its people"); yield break; }
        for (int i = 0; i < 600 && town.People.Any(p => p.Looks == null || p.View == null); i++) { TopUp(); yield return 0; }
        int shots = 0;
        town.ShotFired += (_, _, _, _, _, _) => shots++;
        town.Provoked();
        for (int i = 0; i < 60 * 40 && !(town.People.Count(p => p.Engaged && !p.Dead) >= 2 && shots >= 2); i++) { TopUp(); yield return 0; }
        var fighting = town.People.Where(p => p.Engaged && !p.Dead).ToList();
        Check(fighting.Count >= 1, $"the gang is on the sleeper ({fighting.Count} in the fight, {shots} shots and blows)");
        if (fighting.Count == 0) yield break;
        _game.Suit.Hit(_max * 3.0, DamageKind.Hit);
        for (int i = 0; i < 5 * 60; i++) yield return 0;
        var still = town.People.Where(p => p.Engaged || p.Holding).ToList();
        Check(_game.Suit.Dead && still.Count == 0, $"killed, every one of them stands down within 5 s ({string.Join(", ", still.Select(p => $"{p.Job} {p.Part}"))})");
        int after = shots;
        for (int i = 0; i < 40 * 60 && !town.People.Any(p => p.Engaged); i++) yield return 0;
        Check(shots == after && !town.People.Any(p => p.Engaged), $"and none fires on the body or takes it up again ({shots - after} shots after)");
        foreach (int x in _drive.Tap(Key.R)) yield return x;
        for (int i = 0; i < 120; i++) yield return 0;
    }

    // ---- 1 to 3: killed by a band -----------------------------------------------------------------

    IEnumerable<int> Band()
    {
        GD.Print("-- killed by a band: they stop, go back to their day, one goes through the bag; a predator eats --");
        foreach (int x in Out(170f)) yield return x;
        WildlifePatch patch = _game.Zone.Wildlife;
        WildSim sim = patch.Sim;
        ITarget you = patch.Player;
        Vector3 feet = _game.Survivor.WorldFeet;
        // Things to lose, and what stays: the toolbar's stacks, slot for slot, and the tool.
        // (The tool in the bag's slots, not on the toolbar, by its old id; a stack of two bars on the toolbar, the rest in the bag.)
        foreach (var (id, n) in new[] { ("iron-bar", 6), ("copper-wire", 5), ("trail-ration", 3), ("survival-tool", 1) }) _game.Pack.Storage.Add(id, n);
        _game.Pack.Grid[3].Item = "iron-bar";
        _game.Pack.Grid[3].Count = 2;
        _game.Pack.Grid.Reconcile();
        Bill carried = Copy(_game.Pack.Storage.Contents), stays = new();
        for (int i = 0; i < _game.Pack.Grid.Toolbar; i++) if (_game.Pack.Grid[i].Item is { } item) stays.Add(item, _game.Pack.Grid[i].Count);
        string tool = PlayerInventory.SleeperTool;
        if (carried[tool] > stays[tool]) stays.Add(tool, carried[tool] - stays[tool]);
        Bill expect = new();
        foreach (var (id, n) in carried) if (n > stays[id]) expect.Add(id, n - stays[id]);

        // Three raiders with rifles, 16 m off, hostile, and set on.
        Vector3 east = Flat(_game.Survivor.WorldLook());
        Herd band = Band(sim, patch, feet + east * 16f, PersonRole.Raider, Occupancy.Raider, "rifle", 3, Stance.Hostile);
        sim.Damage(band.Members[0], 1f, you);
        int fired = _game.Arms.OthersFired;
        for (int i = 0; i < 60 * 20 && !(band.Members.Count(c => InFight(sim, c, you)) >= 2 && _game.Arms.OthersFired - fired >= 2); i++) { TopUp(); yield return 0; }
        var attackers = sim.Creatures.Where(c => InFight(sim, c, you)).ToList();
        Check(attackers.Count >= 2, $"the band is in the fight ({attackers.Count} on the sleeper, {_game.Arms.OthersFired - fired} rounds fired)");

        // Killed where they stand.
        _game.Suit.Hit(_max * 3.0, DamageKind.Hit);
        for (int i = 0; i < 10 && _game.Body == null; i++) yield return 0;
        Lootable bag = _game.DeathBag;
        Creature body = _game.Body;
        Vector3 spot = patch.PatchLocal(feet);
        Check(_game.Suit.Dead && body is { Dead: true } && body.Species.Id == Slice.Player.CharacterModel.Sleeper.Id && WildSim.Flat(body.Position - spot).Length() < 0.5f,
            $"the sleeper's body lies where they fell, kept by the wildlife ({(body == null ? "none" : $"{WildSim.Flat(body.Position - spot).Length():F2} m off")})");
        Check(bag != null && bag.Yours && bag.Storage.Contents.Equals(expect) && _game.Pack.Storage.Contents.Equals(stays),
            $"the bag holds what they carried but the toolbar and the tool ({Describe(bag?.Storage.Contents)}; expected {Describe(expect)}; kept {Describe(_game.Pack.Storage.Contents)})");
        if (bag == null) yield break;
        Bill inBag = Copy(bag.Storage.Contents);
        Vector3 bagAt = patch.PatchLocal(bag.Position);

        // Within 5 s every attacker is out of the fight.
        int bites = 0, commits = 0, onSpot = 0;
        patch.Attacked += _ => bites++;
        sim.Committed += e => { if (e.Target == you) commits++; if (WildSim.Flat(e.Creature.AimPoint - spot).Length() < 3f) onSpot++; };
        for (int i = 0; i < 5 * 60; i++) yield return 0;
        var still = attackers.Where(c => InFight(sim, c, you)).ToList();
        Check(still.Count == 0, $"every attacker left the fight within 5 s ({string.Join(", ", still.Select(c => $"{c.Species.CommonName} {c.State}"))})");
        int aimed = 0;

        // A minute: nothing attacks the sleeper or the body; back to their day; one goes through the bag, which is never touched.
        bool rummaged = false, shot = false, same = true;
        float nearest = float.MaxValue;
        for (int i = 0; i < 60 * 60; i++)
        {
            same &= bag.Storage.Contents.Equals(inBag);
            if (sim.Creatures.Any(c => _game.Arms.People.KitOf(c) is { Pending: > 0 } k && k.Target == you)) aimed++;
            foreach (Creature m in band.Members.Where(m => m.Alive && !m.Removed))
            {
                float d = WildSim.Flat(m.Position - bagAt).Length();
                nearest = MathF.Min(nearest, d);
                if (m.Act == BodyAct.Rummage && d < 1.8f)
                {
                    rummaged = true;
                    if (!shot && m.RummageFor < 4.5f)
                    {
                        shot = true;
                        foreach (int x in Shot("rummage.png", Near(patch.FromPatch(m.Position), bag.Position))) yield return x;
                    }
                }
            }
            if (rummaged && i > 50 * 60) break;
            yield return 0;
        }
        Check(bites == 0 && commits == 0 && aimed == 0, $"nothing attacks the sleeper or the body for a minute ({bites} bites, {commits} blows committed, {aimed} ticks of rounds on them)");
        Check(band.Party.Mood is PartyMood.Easy or PartyMood.Watching && band.Party.Foe == null, $"the band goes back to its day ({band.Party.Mood})");
        Check(rummaged && sim.Rummages > 0, $"one of them goes to the body and its bag and rummages ({sim.Rummages} rummages, nearest {nearest:F1} m)");
        Check(same && bag.Storage.Contents.Equals(expect), $"and takes nothing: the bag is as it was ({Describe(bag.Storage.Contents)})");

        // 2. They're killed: their goods stay on their bodies, their rifle and rounds with them (owner).
        Creature one = band.Members.First(m => m.Alive);
        var killed = band.Members.Where(m => m.Alive).ToList();
        foreach (Creature m in killed) sim.Damage(m, 9999f, null);
        for (int i = 0; i < 90; i++) yield return 0;
        Lootable theirs = _game.Zone.Loot.All.FirstOrDefault(l => l.Owner == one);
        int bodies = killed.Count(m => _game.Zone.Loot.All.Any(l => l.Kind == LootKind.Corpse && l.Creature == m));
        Check(bodies == killed.Count && theirs is { Kind: LootKind.Corpse } && theirs.Storage.Count("line-rifle") == 1,
            $"a person killed keeps their goods on the body, searched there ({bodies} of {killed.Count} bodies; {Describe(theirs?.Storage.Contents)})");
        Check(!_game.Zone.Loot.All.Any(l => l.Kind == LootKind.Bag && l.Owner is Creature o && killed.Contains(o)), "and no bag drops beside them");

        // 3. A hungry predator near the body eats it.
        // (A predator if the world has one that walks, else a scavenger: both eat what they find.)
        Species hunter = patch.Life.Fauna.Species.Where(s => s.Behaviour is Behaviour.Predator or Behaviour.Scavenger && s.Rig != RigKind.Swarm && s.Figure == null && !s.IsDeep && s.Domestic == DomesticKind.None)
            .OrderBy(s => (s.Flies ? 2 : 0) + (s.Behaviour == Behaviour.Predator ? 0 : 1)).ThenBy(s => MathF.Abs(s.Body.Size - 1.2f)).FirstOrDefault();
        if (hunter == null) { GD.Print("  (no predator or scavenger on this world: nothing to eat the body)"); }
        else
        {
            float meat = body.MeatLeft;
            Creature eater = sim.Spawn(hunter, sim.FreeSpot(spot - east * 18f, new Wildlife.Rng((ulong)_rng.NextInt64())));
            eater.Hunger = 1f;
            bool ate = false;
            for (int i = 0; i < 60 * 60 && !ate; i++)
            {
                ate = eater.State == CState.Eat && WildSim.Flat(eater.Position - body.Position).Length() < eater.Radius + body.Radius + 1f && body.MeatLeft < meat;
                yield return 0;
            }
            Check(ate, $"a hungry {hunter.CommonName} ({hunter.Behaviour}) near the body feeds on it ({eater.State}, {WildSim.Flat(eater.Position - body.Position).Length():F1} m off, meat {meat:F1} to {body.MeatLeft:F1})");
            eater.Removed = true;
        }

        // The respawn is as it was, and nothing comes for the spot after it.
        onSpot = 0;
        foreach (int x in _drive.Tap(Key.R)) yield return x;
        for (int i = 0; i < 10 * 60; i++) yield return 0;
        Check(!_game.Suit.Dead && onSpot == 0, $"up again (respawned), and nothing attacks where they lay ({onSpot})");
    }

    // ---- 4: goods tossed down at gunpoint ---------------------------------------------------------

    IEnumerable<int> Toss()
    {
        GD.Print("-- a trader held at gunpoint tosses a bag down in front of the sleeper --");
        foreach (int x in Out(170f)) yield return x;
        WildlifePatch patch = _game.Zone.Wildlife;
        WildSim sim = patch.Sim;
        Vector3 feet = _game.Survivor.WorldFeet, ahead = Flat(_game.Survivor.WorldLook());
        Herd band = Band(sim, patch, feet + ahead * 9f, PersonRole.Trader, Occupancy.Friendly, "", 1, Stance.Welcoming);
        Creature trader = band.Members[0];
        Vector3 toYou = patch.PatchLocal(feet) - trader.Position;
        trader.Yaw = MathF.Atan2(-toYou.X, -toYou.Z); // (facing the sleeper: they see who has the gun on them)
        Arm("tack-pistol");
        int offered = _game.Offered;
        Lootable bag = null;
        for (int i = 0; i < 60 * 12 && bag == null; i++)
        {
            TopUp();
            _drive.Face(patch.FromPatch(trader.Position) + Vector3.Up * trader.Species.Figure.Height * 0.6f);
            if (_game.Offered > offered) bag = _game.Zone.Loot.All.LastOrDefault(l => l.Owner == trader);
            yield return 0;
        }
        _game.Controls.Look = Vector2.Zero;
        Vector3 thrower = patch.FromPatch(trader.Position), me = _game.Survivor.WorldFeet;
        Check(bag != null, $"held at gunpoint, the trader tosses a bag ({sim.Offers} offers; the trader {trader.State} {trader.Act}, {WildSim.Flat(thrower - me).Length():F1} m off)");
        if (bag == null) yield break;
        // The gestures after it: a point at it, then at the way out.
        var seen = new List<(BodyAct Act, Vector3 At)>();
        bool flew = bag.Flying != null;
        for (int i = 0, down = 0; i < 60 * 4; i++)
        {
            // Once it is down, the sleeper looks at it and at the one who threw it (the shot through their own eyes).
            if (bag.Flying == null) _drive.Face(bag.Position * 0.65f + patch.FromPatch(trader.Position) * 0.35f + Vector3.Up * 0.5f);
            else _game.Controls.Look = Vector2.Zero;
            if (trader.Act != BodyAct.None && trader.LookAt is { } at) seen.Add((trader.Act, patch.FromPatch(at)));
            if (bag.Flying == null && ++down == 20) foreach (int x in Shot("tossed.png", null)) yield return x;
            yield return 0;
        }
        _game.Controls.Look = Vector2.Zero;
        Check(flew && bag.Flying == null, "it comes down on an arc and rests");
        Vector3 b = bag.Position, line = Flat(thrower - me);
        float d = WildSim.Flat(thrower - me).Length(), along = (b - me).Dot(line), off = WildSim.Flat(b - me - line * along).Length();
        Check(along > 0.8f && along < d - 0.8f && off < 1.5f, $"it lands on the ground in front of the sleeper, between them ({along:F1} of {d:F1} m along, {off:F2} m to the side)");
        Bill all = _game.BodyDrops(trader.Species, Wildlife.Rng.For(trader.Species.Seed, "drop", trader.Id));
        int given = bag.Storage.Contents.Sum(kv => kv.Value), had = all.Sum(kv => kv.Value);
        Check(given >= 1 && (given < had || had == 1), $"a share of their goods, never all ({given} of {had}: {Describe(bag.Storage.Contents)})");
        bool atBag = seen.Any(s => s.Act == BodyAct.Point && WildSim.Flat(s.At - b).Length() < 1.2f);
        int first = seen.FindIndex(s => s.Act == BodyAct.Point && WildSim.Flat(s.At - b).Length() < 1.2f);
        bool away = first >= 0 && seen.Skip(first).Any(s => s.Act == BodyAct.Point && WildSim.Flat(s.At - thrower).Length() > d + 5f);
        Check(seen.Any(s => s.Act == BodyAct.Present) && atBag && away, $"the toss, a point at it, then a point at the way out ({string.Join(" > ", seen.Select(s => s.Act).Distinct())})");
        trader.Removed = true;
        Disarm();
    }

    // ---- 5: killed by a Husk -----------------------------------------------------------------------

    IEnumerable<int> Husk()
    {
        GD.Print("-- killed by a Husk: the worms into the face, and the body rises near its bag --");
        foreach (int x in Out(170f)) yield return x;
        WildlifePatch patch = _game.Zone.Wildlife;
        WildSim sim = patch.Sim;
        Vector3 feet = _game.Survivor.WorldFeet, ahead = Flat(_game.Survivor.WorldLook());
        _game.Pack.Storage.Add("iron-bar", 2);
        _game.Pack.Grid.Reconcile();
        _game.Suit.Sim.Player.Gear.HelmetOn = false;
        Species host = Enumerable.Range(0, 12).Select(i => patch.Life.People.Person((ulong)_rng.NextInt64(), PersonRole.Worker, Occupancy.Friendly))
            .FirstOrDefault(s => s.Figure.Height + Seizing.ReachAbove >= Seizing.StandingHead) ?? patch.Life.People.Person((ulong)_rng.NextInt64(), PersonRole.Guard, Occupancy.Friendly);
        Species kind = sim.TurnHost(host, (ulong)_rng.NextInt64());
        Creature husk = sim.Spawn(kind, patch.PatchLocal(Ground(feet + ahead * 1.4f)));
        husk.Yaw = MathF.Atan2(ahead.X, ahead.Z);
        husk.Rig.Reset(sim, husk);
        yield return 0;
        Creature before = _game.Body;
        Seizing.Begin(sim, husk, patch.Player);
        for (int i = 0; i < 60 * 8 && _game.Body == before; i++) yield return 0;
        Creature body = _game.Body == before ? null : _game.Body;
        Lootable bag = _game.DeathBag;
        Check(_game.Suit.Dead && body != null && husk.Infests == body, $"the helmet open, the Husk kills the sleeper and goes to the body ({(_game.Suit.Dead ? "dead" : "alive")}, infesting {husk.Infests == body})");
        if (body == null || bag == null) yield break;
        int mouthfuls = 0;
        sim.Spat += g => { if (g.From == husk && husk.InfestFor > 0f) mouthfuls++; };
        bool knelt = false, shot = false;
        Creature risen = null;
        sim.Turned += e => { if (e.Victim is CreatureTarget { Creature: var v } && v == body) risen = e.Turned; };
        for (int i = 0; i < 60 * 40 && risen == null; i++)
        {
            Vector3 head = new(body.Rig.Hits[1].X, body.Rig.Hits[1].Y, body.Rig.Hits[1].Z);
            if (husk.InfestFor > 0f) knelt |= WildSim.Flat(husk.Position - head).Length() < 1f;
            if (!shot && husk.InfestFor > 2.4f)
            {
                shot = true;
                Vector3 h = patch.FromPatch(head), side = Flat(patch.FromPatch(husk.Position) - h).Cross(Vector3.Up);
                Vector3 eye = h + side * 2.2f + Flat(patch.FromPatch(husk.Position) - h) * 0.6f + Vector3.Up * 1.1f;
                foreach (int x in Shot("worms.png", new Transform3D(Basis.LookingAt(h + Vector3.Up * 0.35f - eye, Vector3.Up), eye))) yield return x;
            }
            yield return 0;
        }
        Check(knelt && mouthfuls >= 3, $"it kneels at the face and brings its worms up into it ({mouthfuls} mouthfuls)");
        Check(risen != null && body.Removed && risen.Species.Husk && risen.Species.Id.StartsWith(Slice.Player.CharacterModel.Sleeper.Id) && risen.Species.Figure.Dress == "suit",
            $"the body rises a Husk in the sleeper's suit ({risen?.Species.Id}, {risen?.Species.Figure.Dress}, its cloth #{risen?.Species.Figure.Cloth.ToHtml(false)})");
        if (risen == null) yield break;
        Vector3 bagAt = patch.PatchLocal(bag.Position);
        float far = 0f;
        for (int i = 0; i < 60 * 30; i++)
        {
            far = MathF.Max(far, WildSim.Flat(risen.Position - bagAt).Length());
            if (i == 60 * 4)
            {
                Vector3 at = patch.FromPatch(risen.Position), from = Flat(at - bag.Position);
                Vector3 eye = bag.Position - from * 3.5f + from.Cross(Vector3.Up) * 1.5f + Vector3.Up * 1.7f;
                foreach (int x in Shot("risen.png", new Transform3D(Basis.LookingAt((at + bag.Position) * 0.5f + Vector3.Up * 0.8f - eye, Vector3.Up), eye))) yield return x;
            }
            yield return 0;
        }
        Check(!risen.Dead && far < 30f && bag.Risen == risen.Species, $"it keeps about the bag (at most {far:F1} m from it in 30 s)");

        // A corpse run: up again, far off and back. It's met there.
        foreach (int x in _drive.Tap(Key.R)) yield return x;
        for (int i = 0; i < 120; i++) yield return 0;
        _game.Suit.Sim.Player.Gear.HelmetOn = true;
        Vector3 away = bag.Position + ahead * 800f;
        foreach (int x in Go(away)) yield return x;
        Vector3 back = bag.Position - ahead * 70f;
        foreach (int x in Go(back)) yield return x;
        for (int i = 0; i < 180; i++) { TopUp(); yield return 0; }
        WildSim now = patch.Sim;
        Creature met = now.Creatures.FirstOrDefault(c => c.Species == bag.Risen && !c.Dead && !c.Removed);
        Check(now != sim && met != null && WildSim.Flat(met.Position - patch.PatchLocal(bag.Position)).Length() < 30f,
            $"after going {WildSim.Flat(away - bag.Position).Length():F0} m off and back, it is there by the bag ({(met == null ? "not there" : $"{WildSim.Flat(met.Position - patch.PatchLocal(bag.Position)).Length():F1} m from it")}, {patch.Patches} patches)");
    }

    // ---- 6: every bag rests on the ground ----------------------------------------------------------

    void Rests()
    {
        GD.Print("-- every bag rests on what is under it --");
        var bad = new List<string>();
        int n = 0;
        foreach (Lootable l in _bags.Where(l => l.Kind == LootKind.Bag && GodotObject.IsInstanceValid(l.Node)))
        {
            n++;
            var (gap, sink, overlap) = Fit(l);
            if (gap > 0.03f || sink > 0.03f || overlap) bad.Add($"{l.Name} at {l.Position}: gap {gap:F3}, sink {sink:F3}{(overlap ? ", overlaps" : "")}");
        }
        // (The sleeper's bags and the tossed bundle: the dead keep their things on their bodies.)
        Check(n >= 3 && bad.Count == 0, $"{n} bags, each on the ground: no gap, no overlap ({string.Join("; ", bad.Take(4))})");
        Check(_bags.All(l => !GodotObject.IsInstanceValid(l.Node) || l.Node.GetNode<StaticBody3D>("Body").CollisionLayer == GalHack.Physics.Layers.Props), "each has a body of its own shape on the props' layer: real collision");
    }

    /// <summary>
    /// How a bag sits: of five points on its flat bottom (its middle and round its edge), the gap under the
    /// lowest to the face straight below it, and how far the deepest is in it; and whether its hull, shrunk a
    /// little about its middle, meets anything solid.
    /// </summary>
    (float Gap, float Sink, bool Overlap) Fit(Lootable l)
    {
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        Transform3D t = l.Node.GlobalTransform;
        float gap = float.MaxValue, sink = 0f;
        foreach (Vector3 p in new[] { Vector3.Zero, new(0.15f, 0f, 0f), new(-0.15f, 0f, 0f), new(0f, 0f, 0.12f), new(0f, 0f, -0.12f) })
        {
            Vector3 at = t * p;
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 0.4f, at + Vector3.Down * 2f, Lootables.RestOn));
            if (hit.Count == 0) continue;
            float d = at.Y - ((Vector3)hit["position"]).Y;
            gap = MathF.Min(gap, MathF.Max(0f, d));
            sink = MathF.Max(sink, -d);
        }
        var q = new PhysicsShapeQueryParameters3D { Shape = Shrunk(), CollisionMask = Lootables.RestOn, Transform = t };
        bool overlap = space.IntersectShape(q, 4).Count > 0;
        return (gap == float.MaxValue ? 0f : gap, sink, overlap);
    }

    static ConvexPolygonShape3D _shrunk;

    /// <summary>A bag's hull shrunk by a twelfth about its middle: what must meet nothing solid.</summary>
    ConvexPolygonShape3D Shrunk()
    {
        if (_shrunk != null) return _shrunk;
        Lootable any = _bags.First(l => GodotObject.IsInstanceValid(l.Node));
        var hull = (ConvexPolygonShape3D)any.Node.GetNode<StaticBody3D>("Body").GetChild<CollisionShape3D>(0).Shape;
        Vector3 middle = Vector3.Up * Lootables.BagHeight * 0.5f;
        return _shrunk = new ConvexPolygonShape3D { Points = hull.Points.Select(p => middle + (p - middle) * 0.92f).ToArray() };
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    bool InFight(WildSim sim, Creature c, ITarget you) => !c.Dead && !c.Removed && (
        c.Attacking && c.AttackTarget == you || c.Holding == you
        || c.State is CState.Chase or CState.Stalk && (c.Noticed == you || c.AttackTarget == you || c.Quarry == you)
        || c.ProvokedBy == you && sim.Time - c.ProvokedAt < 12f
        || c.Herd?.Party is { Mood: PartyMood.Fighting } b && b.Foe == you
        || _game.Arms.People.KitOf(c) is { Pending: > 0 } k && k.Target == you);

    /// <summary>A band of people at a spot: <paramref name="n"/> of a trade, holding a prop, with the stance given.</summary>
    Herd Band(WildSim sim, WildlifePatch patch, Vector3 at, PersonRole role, Occupancy occupancy, string prop, int n, Stance stance)
    {
        Vector3 local = patch.PatchLocal(Ground(at));
        var h = new Herd { Home = local, Goal = local, Center = local, Spread = 3f };
        Species Person()
        {
            Species s = patch.Life.People.Person((ulong)_rng.NextInt64(), role, occupancy);
            if (prop != "") s.Figure.Prop = prop;
            return s;
        }
        Species first = Person();
        h.Party = Party.Roll(first, (ulong)_rng.NextInt64());
        h.Party.Temper.Stance = stance;
        sim.Herds.Add(h);
        for (int i = 0; i < n; i++)
            sim.Spawn(i == 0 ? first : Person(), sim.FreeSpot(local + new Vector3(MathF.Cos(i * 2.4f), 0f, MathF.Sin(i * 2.4f)) * (i == 0 ? 0f : 2.2f), new Wildlife.Rng((ulong)_rng.NextInt64())), h);
        return h;
    }

    /// <summary>Off somewhere open under the sky, well away, with nothing about: and waits for the patch round there to settle.</summary>
    IEnumerable<int> Out(float far)
    {
        Vector3 start = _game.Survivor.WorldFeet;
        for (int tries = 0; tries < 24; tries++)
        {
            float turn = (float)_rng.NextDouble() * MathF.Tau;
            Vector3 open = start + new Vector3(MathF.Cos(turn), 0f, MathF.Sin(turn)) * (far + tries * 30f);
            foreach (int x in Go(open)) yield return x;
            Vector3 feet = _game.Survivor.WorldFeet;
            Vector3 n = Slope(feet);
            if (!_game.Sheltered() && n.Y > 0.93f && !_game.Zone.Wildlife.Sim.Nav.Blocked(_game.Zone.Wildlife.PatchLocal(feet))) break;
        }
        WildlifePatch patch = _game.Zone.Wildlife;
        Vector3 here = _game.Survivor.WorldFeet;
        foreach (Creature c in patch.Sim.Creatures) if (patch.WorldOf(c).DistanceTo(here) < 90f) c.Removed = true;
        for (int i = 0; i < 60; i++) { TopUp(); yield return 0; }
    }

    /// <summary>Put down at a spot on the ground, and on until the wildlife's patch round there has settled.</summary>
    IEnumerable<int> Go(Vector3 to)
    {
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(to.X, to.Z) + Vector3.Up * 0.3f).ToNumerics());
        WildlifePatch patch = _game.Zone.Wildlife;
        foreach (int x in _drive.UntilSettled(30)) yield return x;
        WildSim sim = patch.Sim;
        for (int i = 0, same = 0; i < 60 * 25 && same < 150; i++)
        {
            same = patch.Sim == sim && _game.Survivor.Body.OnFloor ? same + 1 : 0;
            sim = patch.Sim;
            TopUp();
            yield return 0;
        }
    }

    Vector3 Slope(Vector3 at)
    {
        var hit = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 1f, at + Vector3.Down * 3f, Lootables.RestOn));
        return hit.Count > 0 ? (Vector3)hit["normal"] : Vector3.Up;
    }

    /// <summary>A near third-person view of someone at a thing: from the thing's far side a few strides off, eye high, both in view.</summary>
    static Transform3D Near(Vector3 who, Vector3 thing)
    {
        Vector3 away = Flat(thing - who);
        Vector3 eye = thing + away * 2.6f + away.Cross(Vector3.Up) * 0.9f + Vector3.Up * 1.6f;
        return new Transform3D(Basis.LookingAt((who + thing) * 0.5f + Vector3.Up * 0.45f - eye, Vector3.Up), eye);
    }

    /// <summary>A weapon in hand on slot 1, loaded (people read it, aimed, as a threat).</summary>
    void Arm(string id)
    {
        GalHack.Arms.WeaponDef d = _game.Arms.Table.Get(id);
        PlayerInventory pack = _game.Pack;
        if (pack[d.Item] < 1) pack.Storage.Add(d.Item, 1);
        if (d.Ammo != "" && pack[d.Ammo] < 10) pack.Storage.Add(d.Ammo, 10);
        for (int i = 1; i < pack.Grid.Count; i++)
            if (pack.Grid[i].Item == d.Item) { pack.Grid[i].Item = null; pack.Grid[i].Count = 0; }
        pack.Grid[0].Item = d.Item;
        pack.Grid[0].Count = 1;
        pack.Grid.Reconcile();
        pack.Selected = 0;
        GalHack.Arms.Gun gun = _game.Arms.GunFor(d);
        if (gun.Loaded == 0) gun.LoadNow();
    }

    void Disarm() => _game.Pack.Selected = _game.Pack.Grid.Toolbar - 1;

    void TopUp()
    {
        if (!_game.Suit.Dead) _game.Suit.Vitals.Health = _max;
    }

    Vector3 Ground(Vector3 at) => _game.Zone.GroundAt(at.X, at.Z);
    static Vector3 Flat(Vector3 v) => new Vector3(v.X, 0f, v.Z).Normalized();
    static Bill Copy(Bill b) { var c = new Bill(); foreach (var (id, n) in b) c.Add(id, n); return c; }
    string Describe(Bill b) => b == null ? "none" : _game.Zone.Data.Items.Describe(b, 6);

    /// <summary>A frame from a camera (null: the sleeper's own eyes), when shots were asked for: the visor off for it.</summary>
    IEnumerable<int> Shot(string name, Transform3D? camera)
    {
        if (_shots == null) yield break;
        _game.FixedCamera = camera;
        _game.Visor.Visible = false;
        for (int i = 0; i < 4; i++) yield return 0;
        _snapping = true;
        Capture(Path.Combine(_shots, name));
        while (_snapping) yield return 0;
        _game.FixedCamera = null;
        _game.Visor.Visible = true;
    }

    async void Capture(string file)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print(err == Error.Ok ? $"  (saved {file})" : $"  (could not save {file}: {err})");
        _snapping = false;
    }
}
