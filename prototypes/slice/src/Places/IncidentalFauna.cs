using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Wildlife;
using Rng = Wildlife.Rng;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Places;

/// <summary>
/// What lives in the places nobody marked: the sewers, drains and galleries under a town. The same
/// importance rule everywhere: people first (squatters, where anyone survives), then threats (the
/// world's dark, a crawler in the vault, the old machines on a high-tech world), then harmless
/// vermin on a small fixed budget. They stand only in open air that reaches a way in (never a sealed
/// pocket), by the world's danger and its tech, and each cell of the network keeps its own few.
/// </summary>
public static class IncidentalFauna
{
    /// <summary>Side of a cell of the network that has its own sim, m.</summary>
    public const int Cell = 64;
    /// <summary>The most one cell holds: a group of people, up to two threats, and vermin with what is left.</summary>
    public const int Budget = 8, PeopleMax = 3, ThreatMax = 2, AmbientMax = 4;

    public enum Role { People, Threat, Ambient }

    public sealed record Made(Creature Creature, Role Role, UnderKind Net, PInt3 Cell);

    /// <summary>The air that reaches a way in: open cells joined (face to face) to the sky's, the shafts' and the outfalls' own. A pocket the walls closed off is not in it.</summary>
    public static HashSet<PInt3> OpenAir(UnderBuilt b)
    {
        var air = new HashSet<PInt3>();
        var queue = new Queue<PInt3>();
        foreach (PInt3 c in b.Daylight) if (air.Add(c)) queue.Enqueue(c);
        foreach (PInt3 c in b.WaysIn) if (air.Add(c)) queue.Enqueue(c);
        while (queue.Count > 0)
        {
            PInt3 c = queue.Dequeue();
            foreach (Dir d in Dirs.All)
            {
                PInt3 n = c + d.Vec();
                if (b.Open.Contains(n) && air.Add(n)) queue.Enqueue(n);
            }
        }
        return air;
    }

    /// <summary>The cells of the network that have a sim: a 64 m square each, by its corner index.</summary>
    public static IEnumerable<(int X, int Z)> Cells(UnderBuilt b, HashSet<PInt3> air)
    {
        var seen = new HashSet<(int, int)>();
        foreach (PInt3 c in air)
            if (b.Open.Contains(c) && seen.Add((Floor(c.X), Floor(c.Z)))) yield return (Floor(c.X), Floor(c.Z));
    }

    static int Floor(int v) => (int)Math.Floor(v / (double)Cell);

    /// <summary>The network as rock and open air for creatures: its cells in the sim's frame (the anchor is the sim's corner in the world's).</summary>
    public static BlockSpace Space(UnderBuilt b, Vector3I anchor) =>
        new((x, y, z) => b.Solid(new PInt3(x, y, z)), new Vector3I(b.Min.X - 1, b.Min.Y - 1, b.Min.Z - 1), new Vector3I(b.Max.X + 2, b.Max.Y + 2, b.Max.Z + 2)) { Offset = -anchor };

    /// <summary>
    /// Fills one cell of the network: people, then threats, then vermin (see the class), each out of
    /// <paramref name="allow"/>'s sight, on this cell's floors only. Run it once per cell, off the main thread.
    /// </summary>
    public static List<Made> Fill(WildSim sim, UnderBuilt b, HashSet<PInt3> air, ISolidSpace space, Vector3I anchor, (int X, int Z) cell, PlaceFauna.Deep deep, ulong seed, Func<Vector3, bool> allow)
    {
        var made = new List<Made>();
        var rng = Rng.For(seed ^ (ulong)(cell.X * 73856093L ^ cell.Z * 19349663L), "incidental");
        var floors = new List<(PInt3 C, UnderSpace Room)>();
        var ceilings = new List<PInt3>();
        UnderSpace RoomOf(PInt3 p) => b.Spaces.FirstOrDefault(s => s.Kind is UnderSpaceKind.Junction or UnderSpaceKind.Sump or UnderSpaceKind.Hall && p.X >= s.Min.X && p.X <= s.Max.X && p.Y >= s.Min.Y && p.Y <= s.Max.Y && p.Z >= s.Min.Z && p.Z <= s.Max.Z);
        foreach (PInt3 p in air.OrderBy(c => c.Y).ThenBy(c => c.Z).ThenBy(c => c.X))
        {
            if (Floor(p.X) != cell.X || Floor(p.Z) != cell.Z || !b.Open.Contains(p) || b.Daylight.Contains(p) || b.Water.Contains(p)) continue;
            bool below = b.Solid(p + PInt3.Down), above = b.Solid(p + PInt3.Up), clear = !b.Solid(p + PInt3.Up + PInt3.Up);
            Vector3 at = ToSim(p, anchor);
            if (below && !above && clear && allow(at)) floors.Add((p, RoomOf(p)));
            else if (above && !below && !b.Solid(p + PInt3.Down + PInt3.Down) && allow(at)) ceilings.Add(p);
        }
        if (floors.Count == 0) return made;
        UnderKind Net(PInt3 p) => b.Spaces.FirstOrDefault(s => p.X >= s.Min.X && p.X <= s.Max.X && p.Y >= s.Min.Y && p.Y <= s.Max.Y && p.Z >= s.Min.Z && p.Z <= s.Max.Z)?.Network ?? UnderKind.Sewer;
        var chambers = floors.Where(f => f.Room != null).ToList();
        WorldLife life = deep.Life;
        DeepFauna fauna = deep.Fauna;
        TechLevel tech = b.Plan.Tech;
        float danger = Math.Clamp(0.35f + 0.4f * (1f - deep.Livable), 0f, 1f);
        int left = Budget;

        List<Creature> Put(Species sp, int n, IEnumerable<(PInt3 C, UnderSpace Room)> where, Role role, bool ceiling = false)
        {
            var spots = ceiling ? ceilings.Select(c => (C: c, Room: (UnderSpace)null)).ToList() : where.Where(f => CaveFauna.Roomy(space, Local(f.C, anchor), sp)).ToList();
            var group = new List<Creature>();
            if (spots.Count == 0) return group;
            var home = spots[rng.Int(0, spots.Count - 1)];
            for (int i = 0; i < n && left > 0; i++)
            {
                var spot = spots.OrderBy(s => (s.C.X - home.C.X) * (s.C.X - home.C.X) + (s.C.Z - home.C.Z) * (s.C.Z - home.C.Z)).Skip(Math.Min(spots.Count - 1, rng.Int(0, 6))).First();
                Vector3 centre = Local(spot.C, anchor);
                Creature c = ceiling ? sim.SpawnIn(sp, space, centre, Vector3.Down) : sim.SpawnIn(sp, space, centre + new Vector3(i * 0.7f, 0.3f, 0f));
                if (c == null) continue;
                if (ceiling) c.Ambush = new AmbushPoint { Kind = HideKind.Ceiling, Position = Crawl.Center(c), Normal = c.Facing.Y };
                else c.Home = centre;
                made.Add(new Made(c, role, Net(spot.C), spot.C));
                group.Add(c);
                left--;
            }
            return group;
        }

        // People first: squatters and scavengers where anyone survives, in the dry chambers, more of them where the old world was kinder.
        float peopleChance = tech == TechLevel.Low ? 0.5f : tech == TechLevel.Mid ? 0.35f : 0.2f;
        var folk = life.RuinCast.Where(k => k.Species.Figure != null).ToList();
        if (life.World.Peopled && folk.Count > 0 && rng.Chance(peopleChance))
        {
            Species who = rng.Weighted(folk, k => k.Weight).Species;
            Put(who, Math.Min(PeopleMax, rng.Int(who.GroupMin, who.GroupMax)), chambers.Count > 0 ? chambers : floors, Role.People);
        }
        // Then what is dangerous: the world's dark, the old machines of a high-tech world, a crawler on the roof of a chamber.
        if (rng.Chance(danger))
        {
            var pool = new List<Species>();
            foreach (Species k in life.Dark.Concat(tech == TechLevel.High ? life.Machines : Array.Empty<Species>()))
                if (fauna.Indoor(fauna.Holding(k, DeepRole.Lurker, PoiTheme.Industrial)) is { } sized && fauna.Fitted(sized, 4f, 3.2f) is { } fit && (fit.Figure != null || fit.BodyHeight >= DeepFauna.IndoorMin - 0.05f)) pool.Add(fit);
            Species crawler = fauna.For(DeepRole.Crawler, PoiTheme.Industrial, 1);
            int n = rng.Int(1, ThreatMax);
            if (pool.Count > 0 && rng.Chance(0.5f)) Put(pool[rng.Int(0, pool.Count - 1)], n, chambers.Count > 0 ? chambers : floors, Role.Threat);
            else Put(crawler, n, null, Role.Threat, ceiling: true);
        }
        // Last and cheapest: vermin, a few, wherever there is damp.
        Species vermin = fauna.For(DeepRole.Subsister, PoiTheme.Industrial, 1);
        Put(vermin, Math.Min(AmbientMax, left), floors, Role.Ambient);
        return made;
    }

    /// <summary>A world cell in the sim's frame: its centre.</summary>
    static Vector3 Local(PInt3 world, Vector3I anchor) => SpaceMath.Center(new Vector3I(world.X - anchor.X, world.Y - anchor.Y, world.Z - anchor.Z));
    static Vector3 ToSim(PInt3 world, Vector3I anchor) => Local(world, anchor);
}
