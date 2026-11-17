using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Arms;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Arms;
using Slice.Game;
using Slice.Places;
using Slice.Surface;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// How people fight (one weapon truth, then fighting like people), on a fresh world every run:
///
/// - Offline: every weapon a person can carry is a row of shared/Arms with an item (and its ammo's)
///   in the catalogue; hand weapons and bare hands strike within a reach.
/// - A gang's place: each armed one holds the player's own model of their weapon item; when they
///   fire it's that row from that model's muzzle (never the chest), the player takes that row's
///   damage, rounds are used up, the group goes in together (one pinning, others flanking), each
///   burst is aimed first; one killed drops their weapon and its ammo in their bag.
/// - Out in the wild: a raider with a rifle shoots from its muzzle and drops the rifle; a band set on
///   rallies as one; a machine fires its mount; a club strikes for the club's damage.
/// - A town's edge: raiders who come near draw fire from its guards, the sleeper of no account to either.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/CombatTest.tscn [-Extra --squatters (that check alone)]
/// </summary>
public partial class CombatTest : Node
{
    const int WatchdogMinutes = 12;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
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

    /// <summary>Whether checks count (off: a run as it was, printed only).</summary>
    bool _counting = true;

    void Check(bool ok, string what)
    {
        if (!_counting) { GD.Print($"  (as it was) {what}"); return; }
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    static readonly string[] Carried = { "rifle", "shotgun", "pistol", "musket", "crossbow", "bow", "laser_rifle", "energy_pistol", "club", "machete", "spear" };

    IEnumerable<int> Scenario()
    {
        if (OS.GetCmdlineUserArgs().Contains("--squatters"))
        {
            foreach (int x in Squatters()) yield return x;
            yield break;
        }
        PlanetSurface.Poi.PoiSiting.ForceFamily = "sewage_works";
        PlanetSurface.Poi.PoiSiting.ForceState = PoiState.Abandoned;
        PlanetSurface.Poi.PoiSiting.ForceOccupancy = new OccupancyRoll(Occupant.Gang);
        WorldChoice world = WorldChoice.Fresh("temperate");
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        PlanetSurface.Poi.PoiSiting.ForceState = null;
        PlanetSurface.Poi.PoiSiting.ForceOccupancy = null;
        game.Zone.Weather.Bridge.Calm = true;
        Rows(game.Zone.Data.Arms, game.Zone.Data.Items);
        foreach (int x in Gang(game, drive, world)) yield return x;
        foreach (int x in Wild(game, drive)) yield return x;
        foreach (int x in World(game)) yield return x;
        game.QueueFree();
        yield return 0;
        foreach (int x in Squatters()) yield return x;
        // NPCs' rounds that hit an ally or a bystander (owner: "they should try not to shoot each other"; a round into one of
        // the target's own side is no stray): the town's edge fought out first with the line of fire not looked along for
        // bodies (how it was: counted, not checked), then as it is, on the same world.
        WorldChoice edge = WorldChoice.Fresh("temperate");
        Slice.Arms.FireLine.On = false;
        _counting = false;
        int strays = Slice.Arms.ArmsHost.Strays, rounds = Slice.Arms.ArmsHost.NpcRounds;
        foreach (int x in TownEdge(edge)) yield return x;
        int offStrays = Slice.Arms.ArmsHost.Strays - strays, offRounds = Slice.Arms.ArmsHost.NpcRounds - rounds;
        Slice.Arms.FireLine.On = true;
        _counting = true;
        (strays, rounds) = (Slice.Arms.ArmsHost.Strays, Slice.Arms.ArmsHost.NpcRounds);
        int held = Slice.Arms.FireLine.Held;
        foreach (int x in TownEdge(edge)) yield return x;
        int onStrays = Slice.Arms.ArmsHost.Strays - strays, onRounds = Slice.Arms.ArmsHost.NpcRounds - rounds;
        Check(onStrays <= Math.Max(1, onRounds / 50) && onRounds >= offRounds / 2, $"NPCs' rounds into allies and bystanders, raiders against a town's guards: {offStrays} of {offRounds} with the line of fire not looked along for bodies, {onStrays} of {onRounds} with it ({Slice.Arms.FireLine.Held - held} rounds held for someone in the line); they still fire");
    }

    /// <summary>
    /// Raiders at a town's edge (the owner, 16 Nov 2026, of people: "they should attack people as well"): a
    /// band of armed raiders put down outside a survivor town near one of its guards, the sleeper off to one
    /// side and of no account to either. The town fires on them, its guards and whoever else has the nerve,
    /// with their own weapons (the aim and shots they'd use on you), and none of it is at you.
    /// </summary>
    IEnumerable<int> TownEdge(WorldChoice world)
    {
        PlanetSurface.Poi.PoiSiting.ForceFamily = "survivor_town";
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        GD.Print($"-- raiders at a town's edge, {world} --");
        game.Zone.Weather.Bridge.Calm = true;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        var site = game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == "survivor_town" && s.Instance?.Townsfolk != null);
        if (site == null) { Check(false, "a survivor town stands on this world"); game.QueueFree(); yield break; }
        if (site.Instance.Mood == Mood.Hostile)
        {
            site.Instance.Mood = Mood.Wary;
            GD.Print("  (TEST: this town was rolled hostile to you: set wary, so its guards have only the raiders to deal with)");
        }
        Vector3 Ground(Vector3 at) => game.Zone.GroundAt(at.X, at.Z) + Vector3.Up * 0.3f;
        Vector3 gate = game.Zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), game.Survivor.WorldFeet);
        Int3 f = site.Instance.Layout.EntranceFacing.Vec();
        Vector3 outward = new Vector3(f.X, 0f, f.Z).LengthSquared() > 0.1f ? new Vector3(f.X, 0f, f.Z).Normalized() : Vector3.Right, side = outward.Cross(Vector3.Up);
        game.Survivor.PlaceOutside(Ground(gate + outward * 24f + side * 18f).ToNumerics());
        for (int i = 0; i < 1800 && game.Places.PlaceOf(site) is not { Ready: true }; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        Town town = game.Places.PlaceOf(site)?.Memory.Town;
        WildlifePatch patch = game.Zone.Wildlife;
        // A long move swaps the patch's sim: wait for the one round you now to settle.
        for (int i = 0, same = 0; i < 60 * 20 && same < 180; i++)
        {
            WildSim was = patch.Sim;
            game.Suit.Vitals.Health = max;
            yield return 0;
            same = patch.Sim == was ? same + 1 : 0;
        }
        if (town?.Memory.Mood == Mood.Hostile) town.Memory.Mood = Mood.Wary;
        for (int i = 0; i < 600 && town != null && !town.People.Any(Out); i++) { game.Suit.Vitals.Health = max; yield return 0; }
        bool Out(Town.Person q) => q.Role == "guard" && !q.Dead && !q.Indoors && q.Looks != null && Weapons.Armed(q.Figure.Prop);
        Town.Person guard = town?.People.Where(Out).OrderByDescending(q => Weapons.Ranged(q.Figure.Prop)).ThenBy(q => (patch.FromPatch(q.Position) - gate).Length()).FirstOrDefault();
        Check(guard != null, $"the town stands with an armed guard out ({town?.People.Count(q => q.Role == "guard")} guards, {town?.People.Count} people)");
        if (guard == null) { game.QueueFree(); yield break; }
        Vector3 post = patch.FromPatch(guard.Position), middle = town.World(new Vector3(town.Data.AreaCenter.X, 0f, town.Data.AreaCenter.Z));
        Vector3 away = new Vector3(post.X - middle.X, 0f, post.Z - middle.Z);
        away = away.LengthSquared() > 1f ? away.Normalized() : outward;
        Species raider = patch.Life.People.Person((ulong)_rng.NextInt64(), PersonRole.Raider, Occupancy.Raider);
        raider.Figure.Prop = "rifle";
        List<Creature> band = Staging.PutBand(patch.Sim, raider, patch.PatchLocal(Ground(post + away * 20f)), 3); // (in reach of a fight from the guard, and of the town's look round from its edge)
        Check(band.Count > 0, $"a band of raiders put down 20 m out from the guard ({band.Count})");
        if (band.Count == 0) { game.QueueFree(); yield break; }
        band[0].Herd.Party.Temper.Stance = Stance.Hostile;
        // (They stay about where they were put: a new herd's first update counts its time from long ago, so its timer is long.)
        band[0].Herd.Home = band[0].Herd.Goal = band[0].Position;
        band[0].Herd.GoalTimer = 1e9f;
        int fire = 0, guardShots = 0, atYou = 0, bandShots = game.Arms.People.Shots;
        Mood mood = town.Memory.Mood;
        town.ShotFired += (who, _, _, _, _, _) => { fire++; if (who.Role == "guard") guardShots++; };
        town.Aimed += _ => atYou++; // (every burst at you is aimed where you can see it first)
        for (int i = 0; i < 60 * 40 && guardShots < 3; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        GD.Print($"  (the raiders: {string.Join(", ", band.Select(c => $"{c.State} {(c.Dead ? "down" : $"{c.Health:F0}")} {(patch.FromPatch(c.Position) - post).Length():F0} m"))}; {game.Arms.People.Shots - bandShots} shots of theirs; the town fought {town.Fought}, {town.Shots} shots)");
        Check(fire > 0, $"raiders near a town draw its fire: {fire} rounds, {guardShots} of them its guards', in {(guardShots >= 3 ? "under " : "")}40 s");
        Check(atYou == 0 && town.Memory.Mood == mood, $"none of it at you, and the town still {town.Memory.Mood} with you ({atYou} aimed at you)");
        game.QueueFree();
        yield return 0;
    }

    /// <summary>
    /// Squatters and their place's beasts (owner, 17 Nov 2026: in a mine "there were people with guns squatting, with aggressive
    /// animals next to them, getting along"): squatters' works stand, three of the world's dangers are put down in its rooms
    /// near its people (in the place's own creatures' sim), the sleeper well off; they fight: the beasts go for the people, and
    /// those with a weapon and the nerve fight them. Any beast the squatters keep (collared) never goes for one of them.
    /// </summary>
    IEnumerable<int> Squatters()
    {
        PlanetSurface.Poi.PoiSiting.ForceFamily = "sewage_works";
        PlanetSurface.Poi.PoiSiting.ForceState = PoiState.Abandoned;
        PlanetSurface.Poi.PoiSiting.ForceOccupancy = new OccupancyRoll(Occupant.Squatters);
        WorldChoice world = WorldChoice.Fresh("temperate");
        var game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(game);
        var drive = new Driver(game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        PlanetSurface.Poi.PoiSiting.ForceState = null;
        PlanetSurface.Poi.PoiSiting.ForceOccupancy = null;
        game.Zone.Weather.Bridge.Calm = true;
        GD.Print($"-- squatters and their place's beasts, {world} --");
        var site = game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == "sewage_works" && s.Instance.Occupancy?.Main == Occupant.Squatters);
        if (site?.Instance.Townsfolk == null) { GD.Print("  (no squatters' works could be seated on this world)"); game.QueueFree(); yield break; }
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        Vector3 camp = game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + site.Instance.Townsfolk.Spots[0].Cell.X + 0.5f, site.Origin.Y + site.Instance.Townsfolk.Spots[0].Cell.Y, site.Origin.Z + site.Instance.Townsfolk.Spots[0].Cell.Z + 0.5f), game.Survivor.WorldFeet);
        game.Survivor.PlaceOutside((camp + Vector3.Up * 0.2f).ToNumerics());
        for (int i = 0; i < 3600 && game.Places.PlaceOf(site) is not { Ready: true }; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        Place place = game.Places.PlaceOf(site);
        Town town = place?.Memory.Town;
        for (int i = 0; i < 600 && town != null && town.People.Any(p => p.Looks == null); i++) { game.Suit.Vitals.Health = max; yield return 0; }
        var outs = town?.People.Where(p => !p.Dead && !p.Indoors && !p.Away && p.Looks != null).ToList();
        Check(outs is { Count: > 0 }, $"the squatters' works stand with its people out ({outs?.Count} of {town?.People.Count})");
        if (outs is not { Count: > 0 }) { game.QueueFree(); yield break; }
        // Its own creatures' sim, or one made for it as the game makes them, its blocks for their space.
        WildlifePatch patch = game.Zone.Wildlife;
        DeepSite deep = place.Memory.Fauna?.Site;
        ISolidSpace space = place.Memory.Fauna?.Space ?? PlaceFauna.SpaceOf(place.Memory);
        if (deep == null)
        {
            var sim = new WildSim(game.Zone.Data.Wildlife, new Wildlife.Terrain((x, z) => -1000f, 300f), new Wildlife.NavGrid(300f, 3f));
            deep = DeepSite.Create(patch, sim, "Beasts");
            deep.Town = town;
            place.AddChild(deep);
        }
        Species beast = patch.Life.Fauna.Species.Where(s => CrossReactions.Hunts(s) && !s.Husk && !s.Mechanical && !s.Flies && s.Rig == RigKind.Legged && s.Body.Size < 2f).OrderBy(_ => _rng.Next()).FirstOrDefault();
        if (beast == null) { GD.Print("  (no walking hunter on this world)"); game.QueueFree(); yield break; }
        var beasts = new List<Creature>();
        for (int i = 0; i < 3; i++)
        {
            Town.Person near = outs[i % outs.Count];
            Creature c = deep.Sim.SpawnIn(beast, space, deep.Local(town.World(near.Pos)) + new Vector3(i - 1, 0f, 1f) * 2.5f);
            if (!c.Removed) beasts.Add(c);
        }
        Check(beasts.Count > 0, $"{beasts.Count} {beast.CommonName} put down in its rooms near its people");
        if (beasts.Count == 0) { game.QueueFree(); yield break; }
        // The sleeper well off (still near enough for the place to stand).
        game.Survivor.PlaceOutside((camp + Vector3.Up * 0.2f + new Vector3(30f, 0f, 30f)).ToNumerics());
        int Met() => town.Fought + town.FledFrom + town.Avoided + town.Barricades;
        int met = Met(), fought = town.Fought, onPeople = 0, byKept = 0, shots = 0;
        void Bitten(AttackEvent e)
        {
            if (Town.Of(e.Target)?.Town != town) return;
            if (e.Creature.Keeper == town) byKept++;
            else onPeople++;
        }
        patch.Bitten += Bitten;
        town.ShotFired += (_, _, _, _, _, meant) => { if (meant is Creature) shots++; };
        for (int i = 0; i < 60 * 45 && (Met() == met || onPeople == 0); i++) { game.Suit.Vitals.Health = max; yield return 0; }
        patch.Bitten -= Bitten;
        int kept = deep.Sim.Creatures.Count(c => c.Keeper == town) + patch.Sim.Creatures.Count(c => c.Keeper == town);
        GD.Print($"  (the beasts: {string.Join(", ", beasts.Select(c => $"{c.State} {(c.Dead ? "down" : $"{c.Health:F0}")} after {c.Noticed?.Name ?? "nobody"} {(c.Noticed != null ? (c.Noticed.Position - c.Position).Length() : 0f):F1} m{(c.AttackTarget != null ? $", attacking {c.AttackTarget.Name}" : "")}"))}; {kept} kept by the squatters; {outs.Count(p => p.Kit is { Strike: false } || Weapons.Armed(p.Figure.Prop))} armed)");
        Check(Met() > met, $"the squatters meet the beasts in their rooms as dangers: {town.Fought - fought} fights started ({shots} rounds at them), {Met() - met - (town.Fought - fought)} fled, kept wide of or barricaded");
        Check(onPeople > 0, $"the beasts go for the squatters: {onPeople} attacks on them");
        Check(byKept == 0, $"none of the squatters' own beasts ({kept}) goes for one of them ({byKept} attacks)");
        game.QueueFree();
        yield return 0;
    }

    /// <summary>The weapon rows people's weapons are, as data.</summary>
    void Rows(Armoury table, GalHack.Items.ItemCatalogue items)
    {
        GD.Print("-- people's weapons are the player's rows --");
        var missing = new List<string>();
        foreach (string prop in Carried)
        {
            WeaponDef d = table.OfItem(Weapons.ItemOf(prop));
            if (d == null || !items.Has(d.Item) || d.Ammo != "" && !items.Has(d.Ammo)) missing.Add($"{prop} ({Weapons.ItemOf(prop)})");
        }
        Check(missing.Count == 0, $"every carried weapon ({Carried.Length}) is a weapon row with an item and its ammo: missing {string.Join(", ", missing)}");
        var strikes = table.All.Where(d => d.Delivery == Delivery.Strike).ToList();
        Check(strikes.Count >= 4 && strikes.All(d => d.Reach >= 1f && d.Ammo == "") && table.OfItem(Weapons.Fists) != null, $"hand weapons and bare hands strike within a reach, without ammo: {string.Join(", ", strikes.Select(d => $"{d.Id} {d.Reach} m {d.Damage}"))}");
        var era = new List<string>();
        for (int e = 0; e < 3; e++)
            for (int i = 0; i < 40; i++)
            {
                string prop = Weapons.ForEra(e, e, Wildlife.Rng.For((ulong)_rng.NextInt64(), "w"));
                if (table.OfItem(Weapons.ItemOf(prop)) == null) era.Add(prop);
            }
        Check(era.Count == 0, $"every era's weapon table gives a weapon row ({string.Join(", ", era.Distinct())})");
    }

    /// <summary>A gang's place: armed with real items, fired from the muzzle, used up, together, and left where they fall.</summary>
    IEnumerable<int> Gang(Game.Game game, Driver drive, WorldChoice world)
    {
        GD.Print($"-- a gang's place, {world} --");
        var site = game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == "sewage_works" && s.Instance.Occupancy?.Main == Occupant.Gang);
        if (site?.Instance.Townsfolk == null) { GD.Print("  (no gang's works could be seated on this world)"); yield break; }
        Vector3 Local(PoiGenerator.Gen.Int3 c) => game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + c.X + 0.5f, site.Origin.Y + c.Y, site.Origin.Z + c.Z + 0.5f), game.Survivor.WorldFeet);
        ActivitySpot camp = site.Instance.Townsfolk.Spots.First();
        game.Survivor.PlaceOutside(Local(camp.Cell).ToNumerics() + System.Numerics.Vector3.UnitY * 0.2f);
        for (int i = 0; i < 3600 && game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0; // (a minute: a busy machine builds it slower)
        Place place = game.Places.PlaceOf(site);
        Town town = place?.Memory.Town;
        Check(town != null, $"it stands with its people ({town?.People.Count}, {place?.Memory.Mood})");
        if (town == null) yield break;
        int aims = 0, aimsBeforeFirst = -1;
        town.Aimed += _ => aims++;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        for (int i = 0; i < 600 && town.People.Any(p => p.Looks == null || p.View == null && !p.Indoors && !p.Away); i++) { game.Suit.Vitals.Health = max; yield return 0; }
        var armed = town.People.Where(p => p.Looks != null && Weapons.Armed(p.Figure.Prop)).ToList();
        GD.Print($"  ({armed.Count} of {town.People.Count} armed: {string.Join(", ", armed.Select(p => p.Figure.Prop).GroupBy(x => x).Select(g => $"{g.Count()} {g.Key}"))})");
        if (armed.Count == 0) { GD.Print("  (nobody armed here this time)"); yield break; }
        // Each one's weapon is the item for what they carry, held as the player's own model.
        foreach (Town.Person p in armed) if (p.Kit == null) { town.Provoked(); break; }
        yield return 0;
        // (A weapon is hung in the hand a frame after its body is made: a moment for the last body's.)
        for (int i = 0; i < 120 && armed.Any(p => p.View != null && p.Figure.Prop != "bow" && p.Model == null); i++) { game.Suit.Vitals.Health = max; yield return 0; }
        bool items = armed.All(p => p.Kit != null && p.Kit.Def.Item == Weapons.ItemOf(p.Figure.Prop));
        Check(items, $"each carries the item for their weapon ({string.Join(", ", armed.Take(4).Select(p => $"{p.Figure.Prop}: {p.Kit?.Def.Id}"))})");
        var drawn = armed.Where(p => p.View != null && p.Figure.Prop != "bow").ToList();
        Check(drawn.All(p => p.Model != null && p.Holder?.GetParent() == p.View), $"held as the player's own model of it, in the hand ({drawn.Count(p => p.Model != null)} of {drawn.Count} drawn)");
        // Away until the place is taken down (its people's bodies and guns with it), and back: each gun is hung in the new hand, and nothing
        // touches what went (a gun left posed on a freed node threw every tick, 170 times on a PopRoute).
        int thrown = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> count = (_, e) => { if (e.Exception is ObjectDisposedException or NullReferenceException) thrown++; };
        AppDomain.CurrentDomain.FirstChanceException += count;
        game.Survivor.PlaceOutside(Local(camp.Cell).ToNumerics() + new System.Numerics.Vector3(3000f, 0f, 0f));
        for (int i = 0; i < 1200 && game.Places.PlaceOf(site) != null; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        bool dropped = game.Places.PlaceOf(site) == null;
        game.Survivor.PlaceOutside(Local(camp.Cell).ToNumerics() + System.Numerics.Vector3.UnitY * 0.2f);
        for (int i = 0; i < 3600 && game.Places.PlaceOf(site) is not { Ready: true }; i++) { game.Suit.Vitals.Health = max; yield return 0; } // (a minute, as at first)
        place = game.Places.PlaceOf(site);
        for (int i = 0; i < 600 && armed.Any(p => !p.Dead && p.View == null && !p.Indoors && !p.Away); i++) { game.Suit.Vitals.Health = max; yield return 0; }
        for (int i = 0; i < 120; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        AppDomain.CurrentDomain.FirstChanceException -= count;
        var again = armed.Where(p => p.View != null && p.Figure.Prop != "bow").ToList();
        Check(dropped && place?.Memory.Town == town && thrown == 0 && again.All(p => p.Model != null && p.Holder?.GetParent() == p.View),
            $"taken down and stood again: {(dropped ? "dropped" : "never dropped")}, {(place?.Memory.Town == town ? "stood again" : "never stood again")}, {thrown} exceptions, guns in the new hands {again.Count(p => p.Holder?.GetParent() == p.View)} of {again.Count}");
        drawn = again;
        // (--shots=<folder>: a picture of each of a few of them, close, with what they hold.)
        string shots0 = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots="))?.Substring(8);
        if (shots0 != null)
            foreach (Town.Person p in drawn.Where(q => !q.Indoors).Take(3))
            {
                Vector3 front = place.ToWorld(p.Pos + p.Forward * 2.6f + p.Forward.Cross(Vector3.Up) * 0.8f + Vector3.Up * 0.2f);
                game.Survivor.PlaceOutside(front.ToNumerics());
                for (int i = 0; i < 40; i++)
                {
                    game.Suit.Vitals.Health = max;
                    drive.Face(place.ToWorld(p.Pos + Vector3.Up * p.Figure.Height * 0.6f));
                    yield return 0;
                }
                string file = System.IO.Path.Combine(shots0, $"armed-{p.Figure.Prop}-{town.People.IndexOf(p)}.png");
                GetViewport().GetTexture().GetImage().SavePng(file);
                GD.Print($"  (shot: {file}, {p.Figure.Prop} as {p.Kit?.Def.Id})");
            }

        // Their shots, as they go.
        var shots = new List<(Town.Person Who, WeaponDef Def, Vector3 Muzzle, Vector3 Model, Vector3 Chest)>();
        var dealt = new List<float>();
        town.ShotFired += (who, def, muzzle, dir, _, _) =>
        {
            if (aimsBeforeFirst < 0 && def.Delivery != Delivery.Strike) aimsBeforeFirst = aims;
            Vector3 model = who.Model?.Muzzle is { } m && who.View is { Visible: true } ? m.GlobalPosition : muzzle;
            shots.Add((who, def, muzzle, model, town.World(who.Pos + Vector3.Up * 1.5f)));
        };
        game.Arms.Dealt += (kind, thing, amount) => { if (kind == HitKind.Player) dealt.Add(amount); };
        int rounds = armed.Sum(p => p.Kit?.Rounds ?? 0);
        // Among them at their camp, and they know you're there.
        game.Survivor.PlaceOutside(Local(camp.Cell).ToNumerics() + System.Numerics.Vector3.UnitY * 0.2f);
        town.Provoked();
        for (int i = 0; i < 60 * 40 && shots.Count(s => s.Def.Delivery != Delivery.Strike) < 4; i++)
        {
            game.Suit.Vitals.Health = max;
            yield return 0;
        }
        Check(shots.Count > 0, $"they fire on you: {shots.Count} rounds in 40 s ({town.Engagements} in the fight, {town.People.Count(q => q.Sees)} see you, {town.Shots} shots, {town.Blows} blows), {town.Dropped} bursts held off: {town.LastHeld}");
        var blows = shots.Where(s => s.Def.Delivery == Delivery.Strike).ToList();
        shots = shots.Where(s => s.Def.Delivery != Delivery.Strike).ToList();
        if (blows.Count > 0) Check(blows.All(s => s.Def == s.Who.Kit.Def), $"hand weapons strike with their own row ({string.Join(", ", blows.Select(s => s.Def.Id).Distinct())})");
        if (shots.Count > 0)
        {
            Check(shots.All(s => s.Def == s.Who.Kit?.Def || s.Def == s.Who.Emptied?.Def), $"every round is the shooter's own weapon row ({string.Join(", ", shots.Select(s => s.Def.Id).Distinct())})");
            Check(shots.All(s => s.Muzzle.DistanceTo(s.Model) < 0.05f), $"from the muzzle of the model in their hands (worst {shots.Max(s => s.Muzzle.DistanceTo(s.Model)):F2} m off)");
            Check(shots.All(s => s.Def.Delivery == Delivery.Strike || s.Muzzle.DistanceTo(s.Chest) > 0.2f), $"never from the chest (nearest {shots.Min(s => s.Muzzle.DistanceTo(s.Chest)):F2} m from it)");
            Check(aimsBeforeFirst >= 1, $"the first was aimed where you could see it ({aimsBeforeFirst} aims before it)");
            Check(dealt.Count == 0 || dealt.All(d => shots.Concat(blows).Any(s => MathF.Abs(s.Def.Damage - d) < 0.01f)), $"what hit you was their weapon's damage ({string.Join(", ", dealt.Take(6).Select(d => $"{d:F0}"))} of {string.Join("/", shots.Concat(blows).Select(s => $"{s.Def.Id} {s.Def.Damage}").Distinct())})");
            int left = armed.Sum(p => p.Kit?.Rounds ?? 0) + armed.Sum(p => p.Emptied?.Rounds ?? 0);
            Check(left < rounds, $"rounds are used up ({rounds} carried, {left} left)");
        }
        int fighting = armed.Count(p => p.Engaged && !p.Dead);
        Check(fighting >= Math.Min(2, armed.Count(p => !p.Dead && p.Figure.Stage is not (LifeStage.Child or LifeStage.Elder))), $"they go in together: {fighting} of {armed.Count} armed in the fight; parts {string.Join(", ", town.People.Where(p => p.Engaged).GroupBy(p => p.Part).Select(g => $"{g.Count()} {g.Key}"))}");
        if (armed.Count(p => p.Engaged && p.Kit is { Strike: false }) >= 3) Check(town.Flanks >= 1, $"one pins you while others go wide ({town.Flanks} flanking)");
        // One of them killed: what they carried stays on their body (owner).
        Town.Person victim = armed.FirstOrDefault(p => !p.Dead && p.Kit != null);
        if (victim != null)
        {
            string item = victim.Kit.Def.Item;
            town.Struck(victim, 9999f, DamageClass.Kinetic, byPlayer: true);
            yield return 0;
            var body = game.Zone.Loot.All.FirstOrDefault(l => l.Owner == victim);
            Check(body is { Kind: Items.LootKind.Corpse } && body.Storage.Count(item) == 1, $"one killed keeps their {item} on the body ({body?.Storage.Count(item)}) and its ammo ({(victim.Kit.Def.Ammo == "" ? "none needed" : $"{body?.Storage.Count(victim.Kit.Def.Ammo)} {victim.Kit.Def.Ammo}")})");
            Check(victim.LastHarm == DamageClass.Kinetic, "and how they were killed reaches the body (its damage class)");
        }
        // Unarmed and hurt: they run.
        Town.Person unarmed = town.People.FirstOrDefault(p => !p.Dead && p.Looks != null && !Weapons.Armed(p.Figure.Prop) && !p.Engaged);
        if (unarmed != null)
        {
            town.Struck(unarmed, 1f, DamageClass.Kinetic, byPlayer: true);
            for (int i = 0; i < 90; i++) { game.Suit.Vitals.Health = max; yield return 0; }
            Check(unarmed.Fleeing || unarmed.Indoors || unarmed.Engaged, $"an unarmed one hit runs for it, or (a raider with the nerve) goes in with their fists ({(unarmed.Engaged ? $"fists, {unarmed.Part}" : unarmed.Fleeing ? "fleeing" : unarmed.Indoors ? "indoors" : "stood")})");
        }
        // Hand to hand: one of them with a blade, a club or fists (or a gunman whose rounds run out, nerve up, so fists), and
        // you at their side, out of their line: within 20 s a blow lands, and every blow faces you.
        string brawler = "none";
        if (town.People.Where(p => p.Engaged && !p.Dead && p.Kit != null).OrderBy(p => p.Kit.Strike ? 0 : 1).FirstOrDefault() is { } fighter)
        {
            brawler = fighter.Kit.Strike ? fighter.Kit.Def.Id : $"a {fighter.Kit.Def.Id} run dry";
            if (!fighter.Kit.Strike) fighter.Kit.Loaded = fighter.Kit.Loose = fighter.Kit.Items = 0;
            fighter.Nerve = 1f;
            game.Survivor.PlaceOutside(town.World(fighter.Pos + fighter.Forward.Cross(Vector3.Up).Normalized() * 1.2f + Vector3.Up * 0.1f).ToNumerics());
        }
        Town.Person brawl = town.People.Where(p => p.Engaged && !p.Dead && p.Kit != null).OrderBy(p => p.Kit.Strike ? 0 : 1).FirstOrDefault();
        for (int i = 0; i < 60 * 20 && town.Blows == 0; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        if (town.Blows == 0 && brawl != null) GD.Print($"  (the brawler: engaged {brawl.Engaged}, part {brawl.Part}, kit {brawl.Kit?.Def.Id} strike {brawl.Kit?.Strike}, {(town.World(brawl.Pos) - game.Survivor.WorldFeet).Length():F1} m from you, way {brawl.Step}/{brawl.Path?.Count ?? -1}, sees {brawl.Sees}, winding {brawl.Winding:F2}, swing in {brawl.SwingIn:F2}, indoors {brawl.Indoors}, fleeing {brawl.Fleeing}, scared {brawl.ScaredFor:F1}, dead {brawl.Dead})");
        GD.Print($"  (fight: {town.Engagements} engaged, {town.Flanks} flanking, {town.FallBacks} fell back, {town.Shots} shots, {town.Blows} blows)");
        Check(town.Blows > 0 && town.WorstBlowFacing <= 20f, $"anyone who hits you faces you as it lands: {town.Blows} blows ({brawler}, you at their side), the chest at worst {town.WorstBlowFacing:F1} degrees off you (at most 20)");
    }

    /// <summary>Out in the wild: a raider's rifle, a band's rally, a machine's mount, a club's blow.</summary>
    IEnumerable<int> Wild(Game.Game game, Driver drive)
    {
        GD.Print("-- people and machines in the wild --");
        var patch = game.Zone.Wildlife;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        // Somewhere open under the sky (no roof over it).
        Vector3 start = game.Survivor.WorldFeet;
        for (int tries = 0; tries < 24; tries++)
        {
            float turn = tries * 2.4f, far = 150f + tries * 30f;
            Vector3 open = start + new Vector3(MathF.Cos(turn), 0f, MathF.Sin(turn)) * far;
            game.Survivor.PlaceOutside((game.Zone.GroundAt(open.X, open.Z) + Vector3.Up * 0.3f).ToNumerics());
            for (int i = 0; i < 60; i++) { game.Suit.Vitals.Health = max; yield return 0; }
            if (!game.Sheltered()) break;
        }
        // A long move swaps the patch's sim: wait for the one round you now to settle.
        WildSim sim = patch.Sim;
        for (int i = 0, same = 0; i < 60 * 20 && same < 180; i++)
        {
            same = patch.Sim == sim ? same + 1 : 0;
            sim = patch.Sim;
            game.Suit.Vitals.Health = max;
            yield return 0;
        }
        if (sim == null) { Check(false, "the patch has a sim"); yield break; }
        Vector3 feet = game.Survivor.WorldFeet;
        Vector3 Ground(Vector3 at) => game.Zone.GroundAt(at.X, at.Z);
        Species Raider(string prop)
        {
            Species s = patch.Life.People.Person((ulong)_rng.NextInt64(), PersonRole.Raider, Occupancy.Raider);
            s.Figure.Prop = prop;
            return s;
        }
        int shotsBefore = game.Arms.People.Shots, fired = game.Arms.OthersFired;
        var rifle = sim.Spawn(Raider("rifle"), patch.PatchLocal(Ground(feet + new Vector3(14f, 0f, 0f))));
        rifle.Nerve = 1f; // (one with the nerve to fight you: hurt, a timid one runs)
        sim.Damage(rifle, 1f, patch.Player);
        var muzzles = new List<float>();
        for (int i = 0; i < 60 * 20 && game.Arms.People.Shots - shotsBefore < 3; i++)
        {
            game.Suit.Vitals.Health = max;
            SimArms.Kit k = game.Arms.People.KitOf(rifle);
            if (k?.Model != null && k.Pending > 0) muzzles.Add(game.Arms.People.Muzzle(rifle, k, patch.Origin).DistanceTo(k.Model.Muzzle.GlobalPosition));
            yield return 0;
        }
        SimArms.Kit kit = game.Arms.People.KitOf(rifle);
        GD.Print($"  (the raider: {rifle.State}, you attackable {patch.Player.Attackable} (dead {game.Suit.Dead}, sheltered {game.Sheltered()}, aboard {game.Survivor.Body.Frame != null}), its gun reaches {Arming.ShootReach(rifle, patch.Player):F0} m)");
        Check(kit?.Clip.Def.Id == "line-rifle", $"a raider with a rifle carries the line rifle ({kit?.Clip.Def.Id})");
        Check(game.Arms.People.Shots - shotsBefore >= 1 && game.Arms.OthersFired > fired, $"it shoots you from range rather than closing in ({game.Arms.People.Shots - shotsBefore} rounds, at {rifle.Position.DistanceTo(patch.Player.Position):F1} m)");
        Check(kit?.Model != null, $"its rifle is drawn in its hands ({game.Arms.People.Drawn} weapons drawn on the wild's people)");
        Check(muzzles.Count == 0 || muzzles.Max() < 0.05f, $"from the muzzle of the model ({(muzzles.Count > 0 ? muzzles.Max() : 0f):F2} m off)");
        Check(kit != null && kit.Clip.Fired > 0, $"using up its rounds ({kit?.Clip.Fired} fired, {kit?.Clip.Rounds} left)");
        // Killed: the rifle and what's left of its rounds on its body.
        sim.Damage(rifle, 9999f, patch.Player);
        for (int i = 0; i < 10; i++) yield return 0;
        var corpse = game.Zone.Loot.All.FirstOrDefault(l => l.Owner == rifle);
        Check(corpse != null && corpse.Storage.Count("line-rifle") == 1, $"it dies and leaves the rifle ({corpse?.Storage.Count("line-rifle")}) and its rounds ({corpse?.Storage.Count("rifle-rounds")})");

        // A band set on as one.
        var band = new List<Creature>();
        for (int i = 0; i < 3; i++) band.Add(sim.Spawn(Raider(i == 0 ? "club" : "rifle"), patch.PatchLocal(Ground(feet + new Vector3(-30f, 0f, i * 8f - 8f)))));
        yield return 0;
        int rallied = sim.Rallied;
        sim.Damage(band[0], 1f, patch.Player);
        Check(band.Skip(1).All(c => c.ProvokedBy == patch.Player), $"one of a band set on, the rest turn on you too ({sim.Rallied - rallied} rallied; {string.Join(", ", band.Select(c => $"{c.Species.CommonName} {(c.Dead ? "dead" : c.Removed ? "gone" : "")} {c.Position.DistanceTo(band[0].Position):F0} m {(c.ProvokedBy == patch.Player ? "on you" : "")}"))})");
        Check(MathF.Abs(Attacks.BlowOf(band[0]) - game.Zone.Data.Arms.OfItem("spiked-club").Damage) < 0.01f, $"a club's blow deals the club's damage ({Attacks.BlowOf(band[0]):F0})");
        Species fists = Raider("");
        var bare = sim.Spawn(fists, patch.PatchLocal(Ground(feet + new Vector3(-40f, 0f, 20f))));
        Check(MathF.Abs(Attacks.BlowOf(bare) - game.Zone.Data.Arms.OfItem(Weapons.Fists).Damage) < 0.01f, $"bare hands punch for the fists' damage ({Attacks.BlowOf(bare):F0})");
        foreach (Creature c in band.Append(bare)) sim.Kill(c, null);

        // A Husk clump hurt catches, burns down and is gone.
        var clump = new WormMass { Position = patch.PatchLocal(Ground(feet + new Vector3(30f, 0f, 30f))), Size = 0.8f, Seed = (ulong)_rng.NextInt64() };
        sim.Masses.Add(clump);
        yield return 0;
        WormMasses.Damage(sim, clump, 1f);
        float lit = clump.Burning;
        for (int i = 0; i < 60 * 12 && !clump.Dead; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        Check(lit > 0f && clump.Dead && clump.BurntOut, $"a Husk clump hurt catches alight and burns out ({(clump.Dead ? $"gone after {clump.Burning:F1} s" : $"still {clump.Health:F0} health after 12 s")})");
        sim.Masses.Remove(clump);

        // A machine fires its mount.
        Species robot = Robots.Roll(patch.Life.World, 7, 1);
        Check(robot.Mount == "line-rifle", $"a mid-tech machine has a mounted gun ({robot.Mount})");
        int before = game.Arms.People.Shots;
        var machine = sim.Spawn(robot, patch.PatchLocal(Ground(feet + new Vector3(0f, 0f, 16f))));
        sim.Damage(machine, 1f, patch.Player);
        for (int i = 0; i < 60 * 20 && game.Arms.People.Shots - before < 1; i++) { game.Suit.Vitals.Health = max; yield return 0; }
        SimArms.Kit mount = game.Arms.People.KitOf(machine);
        Check(game.Arms.People.Shots > before && mount is { Mounted: true }, $"it fires its mount ({game.Arms.People.Shots - before} rounds, drawn {(mount?.Model != null ? "on its arm" : "not yet")})");
        sim.Kill(machine, null);
    }
}
