using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Slice.Places;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// The unseen attacker (OccupantTest --unseen): places are filled and a sleeper walked through every room.
/// Nothing is put in a solid, floor or ceiling cell or where no open cell reaches it, every body is built, no
/// blow lands through a wall, floor or ceiling, and every blow was announced (its windup) by something in view.
/// </summary>
public partial class OccupantTest
{
    sealed class UnseenTally
    {
        public int Places, Made, Solid, Unreached, NoBody, Hits, Through, Hidden, Unannounced, Failed, Culled;
        public string Example = "", HitExample = "";
    }

    void Unseen()
    {
        GD.Print("-- the unseen attacker --");
        var all = _lib.Families.Where(f => !WorldProfile.Unbuilt(f) && OccupancyLayer.Applies(f, PoiState.Abandoned)).ToList();
        var works = all.Where(f => f.Id.Contains("water_works") || f.Id.Contains("sewage")).ToList();
        var tallyWorks = new UnseenTally();
        var tallyAll = new UnseenTally();
        for (int w = 0; w < 3; w++)
        {
            WorldLife life = Fresh(out _);
            var deep = new PlaceFauna.Deep(life, null, 1f);
            for (int i = 0; i < 4 && works.Count > 0; i++) Walk(works[_rng.Next(works.Count)], deep, tallyWorks);
            for (int i = 0; i < 5; i++) Walk(all[_rng.Next(all.Count)], deep, tallyAll, true);
        }
        foreach (var (name, t) in new[] { ("water and sewage works", tallyWorks), ("every family", tallyAll) })
        {
            GD.Print($"    {name}: {t.Places} places, {t.Made} creatures, {t.Hits} blows landed, {t.Culled} creatures taken out as unreached ({t.Failed} seeds with no valid layout skipped)");
            Check(t.Made > 20 && t.Solid == 0 && t.Unreached == 0 && t.NoBody == 0, $"{name}: nothing spawned in a solid, floor or ceiling cell ({t.Solid}), out of reach of open air ({t.Unreached}) or with no built body ({t.NoBody}) of {t.Made}{t.Example}");
            Check(t.Through == 0 && t.Hidden == 0 && t.Unannounced == 0, $"{name}: of {t.Hits} blows landed, {t.Through} through a wall, floor or ceiling, {t.Hidden} from something out of sight, {t.Unannounced} with no windup first{t.HitExample}");
        }
    }

    void Walk(FamilyDef f, PlaceFauna.Deep deep, UnseenTally t, bool quick = false)
    {
        PoiInstance inst;
        try { inst = Place(f, PoiState.Abandoned); }
        catch (InvalidOperationException) { t.Failed++; return; } // the generator gave up on a seed: the game skips such a place
        PlaceMemory m = Memory(inst);
        m.Rooms = Decor.Shared?.Plan(m);
        PlaceFauna fauna = PlaceFauna.Plan(m, deep);
        PoiPopulation pop = fauna.Population;
        if (pop.Silent) return;
        t.Places++;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var terrain = new Wildlife.Terrain((x, z) => -50f, 64f);
        var sim = new WildSim(_data, terrain, new Wildlife.NavGrid(terrain.HalfSize, 3f));
        BlockSpace space = PlaceFauna.SpaceOf(m);
        List<Creature> made = PoiFauna.Spawn(sim, pop, space);
        PoiFauna.PutDeferred(sim, pop, space, null);
        t.Culled += PlaceFauna.CullUnreached(sim, PlaceFauna.Reach(inst, fauna.Rooms));
        HashSet<(int, int, int)> open = Reach(inst, space, fauna.Rooms.Rooms);
        foreach (Creature c in sim.Creatures.ToArray())
        {
            if (c.Removed || c.InWalls) continue;
            t.Made++;
            Vector3 mid = c.Species.Climbs ? Crawl.Center(c) : c.Position + Vector3.Up * 0.5f;
            if (Interpenetration.Inside(sim, c) || space.Density(mid) > Interpenetration.Tolerance)
            {
                if (t.Solid++ == 0) t.Example = $"; e.g. a {c.Species.CommonName} at {mid} in {f.Id}";
            }
            else if (!open.Contains(((int)MathF.Floor(mid.X), (int)MathF.Floor(mid.Y), (int)MathF.Floor(mid.Z))) && t.Unreached++ == 0) t.Example = $"; e.g. a {c.Species.CommonName} unreached at {mid} in {f.Id}, in {Describe(inst, mid)}";
            if (c.Rig == null || c.Species.Body == null) t.NoBody++;
        }
        var target = new DummyTarget { Name = "sleeper", Health = new Health(1e9f) };
        sim.Targets.Add(target);
        var telegraphed = new Dictionary<Creature, float>();
        sim.Telegraph += e => telegraphed[e.Creature] = e.Time;
        sim.Attacked += e =>
        {
            if (!e.Hit || e.Damage <= 0f || e.Target != target) return; // blows on the sleeper
            Creature c = e.Creature;
            t.Hits++;
            Vector3 chest = target.Position + Vector3.Up * 1.0f, eye = target.Position + Vector3.Up * 1.6f;
            Vector3 body = c.Species.Climbs ? Crawl.Center(c) : c.Position + Vector3.Up * 0.8f;
            string where = $"; e.g. a {c.Species.CommonName} ({c.Species.Role}) at {c.Position}, the sleeper at {target.Position}, in {f.Id}";
            if (!SpaceMath.Clear(space, c.StrikePoint + Vector3.Up * 0.1f, chest) && !SpaceMath.Clear(space, body, chest))
            {
                if (t.Through++ == 0) t.HitExample = where;
            }
            // Drawn: a body that is built and, near you or mid-attack, always shown (DeepSite.MustDraw), else in the eye's line.
            float near = WildSim.Flat(c.Position - target.Position).Length() + MathF.Abs(c.Position.Y - target.Position.Y) * 0.5f;
            if (c.Removed || c.Rig == null || !(Slice.Surface.DeepSite.MustDraw(c, near) || SpaceMath.Clear(space, eye, eye.Lerp(body, 0.85f))))
            {
                if (t.Hidden++ == 0 && t.HitExample == "") t.HitExample = where;
            }
            if (!telegraphed.TryGetValue(c, out float at) || sim.Time - at > 4f)
            {
                if (t.Unannounced++ == 0 && t.HitExample == "") t.HitExample = where;
            }
        };
        foreach (PoiRoom r in fauna.Rooms.Rooms.OrderBy(_ => _rng.Next()))
        {
            if (r.Size.Y < 2f) continue;
            if (clock.Elapsed.TotalSeconds > (quick ? 10 : 25)) break; // a big place is walked in part
            var stops = new List<Vector3> { r.FloorCenter };
            if (!quick && r.Size.X * r.Size.Z > 40f) { stops.Add(new Vector3(r.Min.X + 1f, r.Min.Y, r.Min.Z + 1f)); stops.Add(new Vector3(r.Max.X - 1f, r.Min.Y, r.Max.Z - 1f)); }
            foreach (Vector3 s in stops)
            {
                target.Position = s;
                for (int k = 0; k < 50; k++) sim.Step(0.1f);
            }
        }
        GD.Print($"    {f.Id}: {sim.Creatures.Count} creatures, {t.Hits} blows so far, {clock.Elapsed.TotalSeconds:F0} s");
    }

    static string Describe(PoiInstance inst, Vector3 p)
    {
        var cell = new PoiGenerator.Gen.Int3((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
        PlacedModule m = inst.Layout.Modules.FirstOrDefault(x => x.Cells.Contains(cell));
        return m == null ? $"no module (occupied {inst.Layout.Occupied[cell]}, block {inst.Blocks.Get(cell).Type}, below {inst.Blocks.Get(cell + PoiGenerator.Gen.Int3.Down).Type}, above {inst.Blocks.Get(cell + PoiGenerator.Gen.Int3.Up).Type})" : $"{m.Kind} {m.Purpose} storey {m.Storey}";
    }

    /// <summary>Open cells the way in reaches (6-neighbour flood over the place's grid).</summary>
    static HashSet<(int, int, int)> Reach(PoiInstance inst, BlockSpace space, List<PoiRoom> rooms)
    {
        var seen = new HashSet<(int, int, int)>();
        PoiGenerator.Gen.Int3 g = inst.Layout.EntranceCell;
        var stack = new Stack<(int, int, int)>();
        stack.Push((g.X, g.Y, g.Z));
        foreach (PoiRoom r in rooms) stack.Push(((int)r.Center.X, (int)MathF.Floor(r.Min.Y), (int)r.Center.Z)); // rooms are walkable: the generator proves it
        BlockGrid b = inst.Blocks;
        while (stack.Count > 0)
        {
            var (x, y, z) = stack.Pop();
            if (x < b.Min.X || y < b.Min.Y || z < b.Min.Z || x > b.Max.X || y > b.Max.Y || z > b.Max.Z) continue;
            if (space.SolidCell(x, y, z) && space.SlopeCell(x, y, z) < 0 || !seen.Add((x, y, z))) continue;
            stack.Push((x + 1, y, z)); stack.Push((x - 1, y, z)); stack.Push((x, y + 1, z));
            stack.Push((x, y - 1, z)); stack.Push((x, y, z + 1)); stack.Push((x, y, z - 1));
        }
        return seen;
    }
}
