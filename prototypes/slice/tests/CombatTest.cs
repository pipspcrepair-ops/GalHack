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
///   burst is aimed first; one killed leaves their weapon and its ammo in a body to search.
/// - Out in the wild: a raider with a rifle shoots from its muzzle and drops the rifle; a band set on
///   rallies as one; a machine fires its mount; a club strikes for the club's damage.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/CombatTest.tscn
/// </summary>
public partial class CombatTest : Node
{
    const int WatchdogMinutes = 9;
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

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    static readonly string[] Carried = { "rifle", "shotgun", "pistol", "musket", "crossbow", "bow", "laser_rifle", "energy_pistol", "club", "machete", "spear" };

    IEnumerable<int> Scenario()
    {
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
        for (int i = 0; i < 1800 && game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        Place place = game.Places.PlaceOf(site);
        Town town = place?.Memory.Town;
        Check(town != null, $"it stands with its people ({town?.People.Count}, {place?.Memory.Mood})");
        if (town == null) yield break;
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        for (int i = 0; i < 600 && town.People.Any(p => p.Looks == null || p.View == null && !p.Indoors && !p.Away); i++) { game.Suit.Vitals.Health = max; yield return 0; }
        var armed = town.People.Where(p => p.Looks != null && Weapons.Armed(p.Figure.Prop)).ToList();
        GD.Print($"  ({armed.Count} of {town.People.Count} armed: {string.Join(", ", armed.Select(p => p.Figure.Prop).GroupBy(x => x).Select(g => $"{g.Count()} {g.Key}"))})");
        if (armed.Count == 0) { GD.Print("  (nobody armed here this time)"); yield break; }
        // Each one's weapon is the item for what they carry, held as the player's own model.
        foreach (Town.Person p in armed) if (p.Kit == null) { town.Provoked(); break; }
        yield return 0;
        bool items = armed.All(p => p.Kit != null && p.Kit.Def.Item == Weapons.ItemOf(p.Figure.Prop));
        Check(items, $"each carries the item for their weapon ({string.Join(", ", armed.Take(4).Select(p => $"{p.Figure.Prop}: {p.Kit?.Def.Id}"))})");
        var drawn = armed.Where(p => p.View != null && p.Figure.Prop != "bow").ToList();
        Check(drawn.All(p => p.Model != null && p.Holder?.GetParent() == p.View), $"held as the player's own model of it, in the hand ({drawn.Count(p => p.Model != null)} of {drawn.Count} drawn)");
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
        int aims = 0, aimsBeforeFirst = -1;
        town.Aimed += _ => aims++;
        town.ShotFired += (who, def, muzzle, dir, _) =>
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
        Check(shots.Count > 0, $"they fire on you: {shots.Count} rounds in 40 s ({town.Engagements} in the fight, {town.People.Count(q => q.Sees)} see you, {town.Shots} shots, {town.Blows} blows)");
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
        // One of them killed: what they carried is left in their body.
        Town.Person victim = armed.FirstOrDefault(p => !p.Dead && p.Kit != null);
        if (victim != null)
        {
            string item = victim.Kit.Def.Item;
            int bodies = game.PeopleBodies;
            town.Struck(victim, 9999f, DamageClass.Kinetic, byPlayer: true);
            yield return 0;
            var body = game.Zone.Loot.All.Where(l => l.Kind == Items.LootKind.Corpse).OrderBy(l => l.Position.DistanceTo(town.World(victim.Pos))).FirstOrDefault();
            Check(game.PeopleBodies == bodies + 1 && body != null && body.Storage.Count(item) == 1, $"one killed leaves a body holding their {item} ({body?.Storage.Count(item)}) and its ammo ({(victim.Kit.Def.Ammo == "" ? "none needed" : $"{body?.Storage.Count(victim.Kit.Def.Ammo)} {victim.Kit.Def.Ammo}")})");
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
        GD.Print($"  (fight: {town.Engagements} engaged, {town.Flanks} flanking, {town.FallBacks} fell back, {town.Shots} shots, {town.Blows} blows)");
    }

    /// <summary>Out in the wild: a raider's rifle, a band's rally, a machine's mount, a club's blow.</summary>
    IEnumerable<int> Wild(Game.Game game, Driver drive)
    {
        GD.Print("-- people and machines in the wild --");
        var patch = game.Zone.Wildlife;
        WildSim sim = patch.Sim;
        if (sim == null) { Check(false, "the patch has a sim"); yield break; }
        double max = game.Suit.Sim.Data.Vitals.Health.Max;
        // Somewhere open, away from the place.
        Vector3 open = game.Survivor.WorldFeet + new Vector3(120f, 0f, 60f);
        game.Survivor.PlaceOutside((game.Zone.GroundAt(open.X, open.Z) + Vector3.Up * 0.3f).ToNumerics());
        for (int i = 0; i < 120; i++) { game.Suit.Vitals.Health = max; yield return 0; }
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
        // Killed: the rifle and what's left of its rounds in the body.
        sim.Damage(rifle, 9999f, patch.Player);
        for (int i = 0; i < 10; i++) yield return 0;
        var corpse = game.Zone.Loot.All.FirstOrDefault(l => l.Creature == rifle);
        Check(corpse != null && corpse.Storage.Count("line-rifle") == 1, $"it dies and leaves the rifle ({corpse?.Storage.Count("line-rifle")}) and its rounds ({corpse?.Storage.Count("rifle-rounds")})");

        // A band set on as one.
        var band = new List<Creature>();
        for (int i = 0; i < 3; i++) band.Add(sim.Spawn(Raider(i == 0 ? "club" : "rifle"), patch.PatchLocal(Ground(feet + new Vector3(-30f, 0f, i * 8f - 8f)))));
        yield return 0;
        int rallied = sim.Rallied;
        sim.Damage(band[0], 1f, patch.Player);
        Check(band.Skip(1).All(c => c.ProvokedBy == patch.Player) && sim.Rallied >= rallied + 2, $"one of a band set on, the rest turn on you too ({sim.Rallied - rallied} rallied)");
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
