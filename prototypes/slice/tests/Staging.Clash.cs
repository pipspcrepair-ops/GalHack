using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Places;
using Slice.Surface;
using Wildlife;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

public sealed partial class Staging
{
    /// <summary>
    /// --view=clash [--bands=FILE] [--berth=FILE] [--out=FILE]: people taking on each other and the world's
    /// dangers, through the sleeper's eyes by day. Two bands of the world's people fighting it out ahead, raiders
    /// on an armed band (--bands); a band bound across in front of you past a predator it has seen, its way bent
    /// wide round it (--berth); then, where the world has a living town (--force-family=survivor_town), raiders
    /// come up to its edge and its guards firing on them (--out). STAGING: the bands, the predator and the raiders
    /// are put down near you with their stances set (printed); the predator is held where it stands.
    /// </summary>
    IEnumerable<int> ClashView()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 stand = FlatSpot(_game.Survivor.WorldFeet, (float)Num("off", 120));
        _game.Survivor.PlaceOutside((stand + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in Daylight()) yield return x;
        WildlifePatch patch = _game.Zone.Wildlife;
        foreach (int x in Settled(patch)) yield return x;
        patch.Frozen = false;
        _game.Visor.Visible = false;
        double max = _game.Suit.Sim.Data.Vitals.Health.Max;
        Vector3 feet = _game.Survivor.WorldFeet, fwd = Openest(feet), side = fwd.Cross(Vector3.Up).Normalized();
        Clear(patch, feet, fwd, 60f);

        // Raiders on an armed band of the world's own, 40 m ahead (nearer each other than you: it's them they go for): they meet, and it's a fight.
        // STAGING: both sides carry what their era shoots with, so the fight shows as shots.
        var raiders = Enumerable.Range(0, 4).Select(i => patch.Life.People.Person(Wildlife.Rng.Mix(patch.Life.World.Seed, 911 + (ulong)i), PersonRole.Raider, Occupancy.Raider)).ToList();
        raiders[0].Hosts = raiders;
        Species other = BandKind(patch.Life, armed: true);
        foreach (Species r in raiders.Concat(other.Hosts ?? new List<Species> { other })) r.Figure.Prop = Shooting(patch.Life.World.Now, r.Seed);
        List<Creature> a = Band(patch, raiders[0], feet + fwd * 44f + side * 14f, Stance.Hostile, feet + fwd * 44f - side * 6f);
        List<Creature> b = Band(patch, other, feet + fwd * 40f - side * 14f, Stance.Wary, feet + fwd * 40f + side * 6f);
        foreach (Creature m in b) m.Nerve = MathF.Max(m.Nerve, 0.8f); // (they stand and shoot back rather than run: the frame is the fight)
        foreach (int x in _drive.LookAt(feet + fwd * 42f + Vector3.Up * 1.2f, 20)) yield return x;
        // Taken on a shot, once both sides are at it: the raiders in their fight, the others shooting back.
        int seen = 0, before = _game.Arms.OthersFired;
        for (int i = 0; i < 60 * 40; i++)
        {
            _game.Suit.Vitals.Health = max;
            bool both = a[0].Herd.Party.Mood == PartyMood.Fighting && b.Any(m => m.Alive && (m.Attacking || m.WantAim > 0.6f)), shot = _game.Arms.OthersFired > before;
            before = _game.Arms.OthersFired;
            if (both && shot && ++seen >= 3) break;
            yield return 0;
        }
        foreach (int x in _drive.LookAt(Mid(patch, a).Lerp(Mid(patch, b), 0.5f) + Vector3.Up * 1.2f, 6)) yield return x;
        GD.Print($"(the clash: raiders {a[0].Herd.Party.Mood}, {other.CommonName} {b[0].Herd.Party.Mood}; {_game.Arms.People.Shots} shots by the wild's people; {Line(patch, a, feet)} | {Line(patch, b, feet)})");
        foreach (int x in Shot(_args.GetValueOrDefault("bands", "clash-bands.png"))) yield return x;

        // A band bound across ahead, past a predator it has seen: its way bends round it.
        Clear(patch, feet, fwd, 90f);
        Species beast = patch.Life.Fauna.Species.FirstOrDefault(s => CrossReactions.Danger(s) && !s.Flies && s.Rig != RigKind.Swarm && s.Body.Size is > 0.6f and < 3f)
            ?? patch.Life.Fauna.Species.FirstOrDefault(CrossReactions.Danger);
        if (beast == null) { GD.Print("(no predator on this world: no berth to show)"); yield break; }
        Vector3 lair = Ground(feet + fwd * 24f);
        Creature it = patch.Sim.Spawn(beast, patch.PatchLocal(lair));
        it.ThinkEvery = 1e9f; // STAGING: it stays where it was put
        Species walkers = BandKind(patch.Life, armed: true);
        // Bound across behind it: the way bends away from you, round it.
        List<Creature> c = Band(patch, walkers, feet + fwd * 28f - side * 45f, Stance.Welcoming, feet + fwd * 28f + side * 50f);
        foreach (Creature m in c) m.Nerve = 0.05f; // (none with the nerve to go for it: they give it room)
        Creature lead = Brain.Leader(c[0].Herd);
        float berth = CrossReactions.Berth(lead.Nerve, c.Any(m => Weapons.Armed(m.Species.Figure.Prop)));
        foreach (int x in _drive.LookAt(lair + Vector3.Up * 1f, 20)) yield return x;
        float least = float.MaxValue;
        for (int i = 0; i < 60 * 120; i++)
        {
            _game.Suit.Vitals.Health = max;
            Vector3 mid = Mid(patch, c);
            least = MathF.Min(least, c.Where(m => m.Alive).Min(m => (patch.WorldOf(m) - lair).Length()));
            // Taken as they go round it: level with it, beyond it.
            if ((mid - lair).Dot(side) > -2f && c[0].Herd.Party.Bending) break;
            yield return 0;
        }
        _game.Survivor.PlaceOutside((Ground(feet) + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.LookAt(lair.Lerp(Mid(patch, c), 0.5f) + Vector3.Up * 1f, 12)) yield return x;
        GD.Print($"(the berth: {walkers.CommonName} led at nerve {lead.Nerve:F2}, berth {berth:F0} m, the nearest of them {least:F0} m from the {beast.CommonName}; {c[0].Herd.Party.Mood}, bending {c[0].Herd.Party.Bending}; {Line(patch, c, feet)})");
        foreach (int x in Shot(_args.GetValueOrDefault("berth", "clash-berth.png"))) yield return x;
        it.Removed = true;

        // A living town's edge: raiders come up to it, and its guards shoot.
        var site = _game.Zone.Planet.Pois.Where(s => s.Instance?.Townsfolk != null && s.Instance.State == PoiState.Active)
            .OrderBy(s => _game.Places.DistanceTo(s, feet)).FirstOrDefault();
        if (site == null) { GD.Print("(no living town on this world: no town edge to show)"); yield break; }
        if (site.Instance.Mood == Mood.Hostile)
        {
            site.Instance.Mood = Mood.Wary;
            GD.Print($"(STAGING: {site.Name} was rolled hostile to you: set wary, so its guards have only the raiders to deal with)");
        }
        Vector3 gate = _game.Zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), feet);
        Int3 f = site.Instance.Layout.EntranceFacing.Vec();
        Vector3 outward = new Vector3(f.X, 0f, f.Z).LengthSquared() > 0.1f ? new Vector3(f.X, 0f, f.Z).Normalized() : Vector3.Right;
        _game.Survivor.PlaceOutside((Ground(gate + outward * 22f + outward.Cross(Vector3.Up) * 16f) + Vector3.Up * 0.3f).ToNumerics());
        for (int i = 0; i < 1800 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) { _game.Suit.Vitals.Health = max; yield return 0; }
        Town town = _game.Places.PlaceOf(site)?.Memory.Town;
        foreach (int x in Settled(patch)) yield return x;
        if (town?.Memory.Mood == Mood.Hostile) town.Memory.Mood = Mood.Wary;
        bool Out(Town.Person q) => q.Role == "guard" && !q.Dead && !q.Indoors && q.Looks != null && Weapons.Armed(q.Figure.Prop);
        for (int i = 0; i < 900 && town != null && !town.People.Any(Out); i++) { _game.Suit.Vitals.Health = max; yield return 0; }
        Town.Person guard = town?.People.Where(Out).OrderByDescending(q => Weapons.Ranged(q.Figure.Prop)).ThenBy(q => (patch.FromPatch(q.Position) - gate).Length()).FirstOrDefault();
        if (guard == null) { GD.Print($"(no armed guard out at {site.Name}: no town edge to show)"); yield break; }
        Vector3 post = patch.FromPatch(guard.Position), middle = town.World(new Vector3(town.Data.AreaCenter.X, 0f, town.Data.AreaCenter.Z));
        Vector3 away = new Vector3(post.X - middle.X, 0f, post.Z - middle.Z);
        away = away.LengthSquared() > 1f ? away.Normalized() : outward;
        Vector3 across = away.Cross(Vector3.Up).Normalized();
        // Side-on to them both: the guard near, the raiders out beyond.
        _game.Survivor.PlaceOutside((Ground(post + away * 10f + across * 14f) + Vector3.Up * 0.3f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        Clear(patch, post + away * 30f, away, 40f);
        List<Creature> gang = Band(patch, raiders[0], post + away * 32f, Stance.Hostile, post + away * 20f);
        foreach (int x in _drive.LookAt(post + away * 16f + Vector3.Up * 1.4f, 20)) yield return x;
        int guardShots = 0;
        bool fresh = false;
        town.ShotFired += (who, _, _, _, _) => { if (who.Role == "guard") { guardShots++; fresh = true; } };
        for (int i = 0; i < 60 * 40; i++)
        {
            _game.Suit.Vitals.Health = max;
            if (guardShots >= 2 && fresh) break;
            fresh = false;
            yield return 0;
        }
        GD.Print($"(the town's edge at {site.Name}: {guardShots} rounds from its guards, the town fought {town.Fought} times; raiders {gang[0].Herd.Party.Mood}, {Line(patch, gang, _game.Survivor.WorldFeet)})");
        foreach (int x in Shot(_args.GetValueOrDefault("out", "clash-town.png"))) yield return x;
    }

    /// <summary>A long move swaps the patch's sim: until the one round the sleeper now has stood a few seconds.</summary>
    IEnumerable<int> Settled(WildlifePatch patch)
    {
        for (int i = 0, same = 0; i < 60 * 20 && same < 180; i++)
        {
            WildSim was = patch.Sim;
            yield return 0;
            same = patch.Sim == was ? same + 1 : 0;
        }
    }

    /// <summary>Facing the openest way from a point: the fewest plants ahead.</summary>
    Vector3 Openest(Vector3 from)
    {
        Vector3 way = Vector3.Forward;
        int fewest = int.MaxValue;
        for (int k = 0; k < 8; k++)
        {
            var dir = new Vector3(MathF.Cos(k * MathF.Tau / 8f), 0f, MathF.Sin(k * MathF.Tau / 8f));
            int n = _game.Zone.Streamer.FloraNear(from + dir * 25f, 22f).Count();
            if (n < fewest) { fewest = n; way = dir; }
        }
        return way;
    }

    /// <summary>STAGING: everything else near cleared, and the plants ahead felled, so the frame shows the people.</summary>
    void Clear(WildlifePatch patch, Vector3 at, Vector3 fwd, float reach)
    {
        foreach (Creature c in patch.Sim.Creatures) if (c.Species.Domestic == DomesticKind.None && patch.WorldOf(c).DistanceTo(at) < reach + 30f) c.Removed = true;
        int felled = 0;
        for (float t = 0f; t <= reach; t += 3f)
            foreach (var plant in _game.Zone.Streamer.FloraNear(at + fwd * t, 26f).ToList())
                if (_game.Zone.Planet.Felled.Add(plant.Id)) { _game.Zone.Streamer.Fell(plant.Id); felled++; }
        GD.Print($"(STAGING: the creatures near cleared, {felled} plants ahead felled)");
    }

    /// <summary>What someone of an era shoots with: its weapon table's first ranged pick (a bow or crossbow low, a gun mid and high).</summary>
    static string Shooting(int era, ulong seed)
    {
        var rng = Wildlife.Rng.For(seed, "staged");
        for (int i = 0; i < 20; i++) if (Weapons.ForEra(era, era, rng) is var p && Weapons.Ranged(p)) return p;
        return era <= 0 ? "bow" : "rifle";
    }

    /// <summary>
    /// STAGING and tests: a band of a kind put down round a point (sim frame), its natural group, or by hand where the
    /// ground wouldn't take one (a clearance no band is put down in): the sim stands each on the ground.
    /// </summary>
    public static List<Creature> PutBand(WildSim sim, Species s, Vector3 at, int count)
    {
        List<Creature> band = sim.SpawnGroup(s, at, count);
        if (band.Count > 0) return band;
        var herd = new Herd { Home = at, Goal = at, Center = at, Spread = 3f, Party = Party.Roll(s, Wildlife.Rng.Mix(s.Seed, (ulong)sim.Herds.Count)) };
        sim.Herds.Add(herd);
        for (int i = 0; i < count; i++) band.Add(sim.Spawn(s, at + new Vector3(i * 1.6f - 2.4f, 0f, (i % 2) * 1.2f), herd));
        return band;
    }

    /// <summary>STAGING: a band of a kind put down at a point, its stance set, bound for another.</summary>
    List<Creature> Band(WildlifePatch patch, Species s, Vector3 at, Stance stance, Vector3 bound)
    {
        at = Ground(at);
        List<Creature> band = PutBand(patch.Sim, s, patch.PatchLocal(at), 4);
        Herd h = band[0].Herd;
        h.Party.Temper.Stance = stance;
        h.Home = h.Goal = patch.PatchLocal(Ground(bound));
        h.GoalTimer = 120f;
        GD.Print($"(STAGING: {band.Count} of {s.CommonName} put down {at.DistanceTo(_game.Survivor.WorldFeet):F0} m off, their stance set {stance}: {string.Join(", ", band.Select(m => m.Species.Figure.Prop is { Length: > 0 } p ? p : "unarmed"))})");
        return band;
    }

    static Vector3 Mid(WildlifePatch patch, List<Creature> band) => band.Where(m => m.Alive && !m.Removed).Aggregate(Vector3.Zero, (v, m) => v + patch.WorldOf(m)) / Math.Max(1, band.Count(m => m.Alive && !m.Removed));

    static string Line(WildlifePatch patch, List<Creature> band, Vector3 feet) => string.Join("; ", band.Select(m => $"{m.Species.Figure.Role} {(m.Dead ? "down" : m.State.ToString())} {patch.WorldOf(m).DistanceTo(feet):F0} m"));
}
