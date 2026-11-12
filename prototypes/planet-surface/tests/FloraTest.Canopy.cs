using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Plants;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Rng = Planets.Gen.Rng;

namespace PlanetSurface.Tests;

/// <summary>
/// Canopy collision: every tree species of every kind of world has canopy hulls (its parts' real shapes,
/// not a box round the tree); they scale with the tree; rays and a flyer's sphere sent at the canopy's
/// own surface are stopped; and many trees cost little physics time.
/// </summary>
public partial class FloraTest
{
    static Flora TreeKindOf(PlantFamily f) => f switch
    {
        PlantFamily.Conifer => Flora.Conifer, PlantFamily.Broadleaf => Flora.Broadleaf, PlantFamily.Jungle => Flora.JungleTree,
        PlantFamily.Palm => Flora.Palm, PlantFamily.Cactus => Flora.Cactus, _ => Flora.GiantMushroom,
    };

    /// <summary>Canopy-only body of a tree instance, as the streamer builds it; returns the body and its shapes.</summary>
    static Rid CanopyBody(Rid space, List<FloraSolid> solids, Vector3 at, List<Rid> shapes, Dictionary<(int, int, int), Rid> cache, Plant[] plants)
    {
        Rid body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
        PhysicsServer3D.BodySetCollisionLayer(body, Layers.Flora);
        PhysicsServer3D.BodySetCollisionMask(body, 0);
        foreach (FloraSolid s in solids)
        {
            if (!s.IsCanopy) continue;
            var key = (s.Plant, s.Variant, s.Part);
            if (!cache.TryGetValue(key, out Rid shape)) shapes.Add(cache[key] = shape = TerrainStreamer.MakeCanopyShape(plants[s.Plant], s.Variant, s.Part));
            PhysicsServer3D.BodyAddShape(body, shape, new Transform3D(s.ShapeTransform.Basis, s.Center + at));
        }
        PhysicsServer3D.BodySetSpace(body, space);
        return body;
    }


    async System.Threading.Tasks.Task Canopies()
    {
        // 1. Every tree species of every kind of world has a canopy.
        int species = 0, without = 0;
        var columnOnly = new HashSet<PlantForm>();
        var worlds = new List<PlanetFlora>();
        foreach (var (a, v) in new[] { (Archetype.Temperate, 0), (Archetype.Temperate, 1), (Archetype.Alien, 0), (Archetype.Alien, 1), (Archetype.Arid, 0), (Archetype.Ice, 0), (Archetype.Lava, 0) })
        {
            var (_, _, flora) = Roll(Fresh(), a, v);
            worlds.Add(flora);
            foreach (Plant p in flora.Book.Plants.Where(p => p.Tree))
            {
                species++;
                // A canopy is any leafy part of the mesh that is not already a solid column; a crown made wholly of columns (limbs, caps of tubes) is solid already.
                for (int mi = 0; mi < p.Near.Length; mi++)
                {
                    PlantMesh m = p.Near[mi];
                    var solid = p.Columns[mi].Select(c => c.Part).ToHashSet();
                    bool leafy = Enumerable.Range(1, m.PartKind.Length - 1).Any(part => !solid.Contains(part) && m.PartKind[part] != (byte)PartKind.Wood && PartExtent(m, part) >= 0.3f && m.PartOf.Contains(part));
                    bool hulled = p.Canopy?[mi] is { Length: > 0 } hs && hs.All(h => h.Length >= 4);
                    if (leafy && !hulled) { without++; GD.Print($"  no canopy: {p.Family} {p.Form}"); }
                    if (!leafy) columnOnly.Add(p.Form);
                }
            }
        }
        GD.Print($"  crowns made wholly of solid columns: {string.Join(", ", columnOnly)}");
        Check(species > 20 && without == 0, $"every tree kind with a leafy canopy has canopy collision: {species} tree species on 7 worlds, {without} meshes without");

        // 2. Rays and a flyer's sphere at the canopy's own surface are stopped, at the smallest and the largest scale.
        Rid space = GetViewport().World3D.Space;
        var cache = new Dictionary<(int, int, int), Rid>();
        var shapes = new List<Rid>();
        var bodies = new List<Rid>();
        var rules = FarBuilder.Solids;
        var rng = new Rng(Fresh());
        var targets = new List<(Vector3 Origin, Vector3 To, string Name)>();
        int placed = 0, hulls = 0;
        int treeCount = 0;
        foreach (PlanetFlora flora in worlds.Take(4))
        {
            foreach (Plant p in flora.Book.Plants.Where(p => p.Tree).Take(10))
            {
                foreach (float unit in new[] { 0f, 1f })
                {
                    var (lo, hi) = flora.ScaleRange(p.Family).Value;
                    float scale = lo + (hi - lo) * unit;
                    int variant = (int)(rng.NextFloat() * p.Near.Length);
                    var at = new Vector3((placed % 12) * 90f, 3000f, (placed / 12) * 90f);
                    placed++;
                    var solids = new List<FloraSolid>();
                    rules.MakeAll(TreeKindOf(p.Family), p, variant, placed, Vector3.Zero, scale, 0.7f, solids);
                    var canopy = solids.Where(s => s.IsCanopy).ToList();
                    hulls += canopy.Count;
                    bodies.Add(CanopyBody(space, solids, at, shapes, cache, flora.Book.Plants.ToArray()));
                    treeCount++;
                    // Sample points: centroids of canopy triangles of parts big enough to be solid.
                    PlantMesh m = p.Near[variant];
                    var covered = new HashSet<int>(p.Columns[variant].Select(c => c.Part));
                    var turn = new Basis(Vector3.Up, 0.7f);
                    var extents = new Dictionary<int, float>();
                    for (int t = 0; t < m.Triangles; t++)
                    {
                        int part = m.PartOf[t];
                        if (part <= 0 || covered.Contains(part) || m.PartKind[part] == (byte)PartKind.Wood) continue;
                        System.Numerics.Vector3 c = (m.Positions[t * 3] + m.Positions[t * 3 + 1] + m.Positions[t * 3 + 2]) / 3f;
                        Vector3 world = turn * new Vector3(c.X, c.Y, c.Z) * scale;
                        // Only parts the hulls cover (those past the size floor): the part's own extent.
                        if (!extents.TryGetValue(part, out float extent)) extents[part] = extent = PartExtent(m, part);
                        if (extent < 0.3f) continue;
                        Vector3 flat = new Vector3(world.X, 0f, world.Z);
                        Vector3 side = flat.LengthSquared() > 0.01f ? flat.Normalized() : Vector3.Right;
                        targets.Add((at + world + (side + Vector3.Up * 0.3f).Normalized() * (p.CrownRadius * scale + 3f), at + world, $"{p.Form} x{scale:F2}"));
                    }
                }
            }
        }
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        PhysicsDirectSpaceState3D state = PhysicsServer3D.SpaceGetDirectState(space);
        int rays = 0, rayStopped = 0, spheres = 0, sphereStopped = 0;
        var misses = new Dictionary<string, int>();
        Rid flyer = PhysicsServer3D.BodyCreate();
        Rid ball = PhysicsServer3D.SphereShapeCreate();
        PhysicsServer3D.ShapeSetData(ball, 0.6f);
        PhysicsServer3D.BodyAddShape(flyer, ball);
        PhysicsServer3D.BodySetMode(flyer, PhysicsServer3D.BodyMode.Kinematic);
        PhysicsServer3D.BodySetCollisionMask(flyer, GalHack.Physics.Layers.VesselMask); // a vessel hull sweeps with the vessels' own mask
        PhysicsServer3D.BodySetCollisionLayer(flyer, GalHack.Physics.Layers.Hull);
        PhysicsServer3D.BodySetSpace(flyer, space);
        var motion = new PhysicsTestMotionParameters3D();
        var result = new PhysicsTestMotionResult3D();
        int step = Math.Max(1, targets.Count / 600);
        for (int i = 0; i < targets.Count; i += step)
        {
            var (from, to, name) = targets[i];
            var q = PhysicsRayQueryParameters3D.Create(from, to, Layers.Flora);
            rays++;
            var hit = state.IntersectRay(q);
            // Stopped at or before the point: the ray's end lies inside a hull, so it must be hit coming in (a ray starting outside).
            if (hit.Count > 0 && ((Vector3)hit["position"]).DistanceTo(from) <= from.DistanceTo(to) + 0.15f) rayStopped++;
            else misses[name] = misses.GetValueOrDefault(name) + 1;
            motion.From = new Transform3D(Basis.Identity, from);
            motion.Motion = to - from;
            spheres++;
            if (PhysicsServer3D.BodyTestMotion(flyer, motion, result)) sphereStopped++;
        }
        PhysicsServer3D.FreeRid(flyer);
        foreach (var kv in misses.OrderByDescending(k => k.Value).Take(12)) GD.Print($"  missed: {kv.Key} {kv.Value}");
        Check(rays > 200 && rayStopped >= rays * 0.99f, $"shots into canopies are stopped: {rayStopped} of {rays} rays at canopy surfaces, {treeCount} trees at their smallest and largest, {hulls} hulls");
        Check(spheres > 200 && sphereStopped >= spheres * 0.99f, $"a vessel hull flying into a canopy is stopped: {sphereStopped} of {spheres} sweeps");
        // A loose prop on the Props layer is not in a vessel's way (the hull never snags on crates and people at its own deck).
        Check((GalHack.Physics.Layers.VesselMask & GalHack.Physics.Layers.Props) == 0 && (GalHack.Physics.Layers.VesselMask & Layers.Flora) != 0, "vessels collide with flora, not with loose props");

        // 3. Physics cost against a baseline taken in this run: 30 falling balls with no trees, then with 400 trees in their way.
        var balls = new List<Rid>();
        Rid ballShape = PhysicsServer3D.SphereShapeCreate();
        PhysicsServer3D.ShapeSetData(ballShape, 0.4f);
        for (int i = 0; i < 30; i++)
        {
            Rid b = PhysicsServer3D.BodyCreate();
            PhysicsServer3D.BodySetMode(b, PhysicsServer3D.BodyMode.Rigid);
            PhysicsServer3D.BodyAddShape(b, ballShape);
            PhysicsServer3D.BodySetCollisionLayer(b, 0);
            PhysicsServer3D.BodySetCollisionMask(b, Layers.Flora);
            PhysicsServer3D.BodySetState(b, PhysicsServer3D.BodyState.Transform, new Transform3D(Basis.Identity, new Vector3(-4000f + rng.NextFloat() * 240f, 3040f, -4000f + rng.NextFloat() * 240f)));
            PhysicsServer3D.BodySetSpace(b, space);
            balls.Add(b);
        }
        async System.Threading.Tasks.Task<double> MedianStep()
        {
            var times = new List<double>();
            for (int i = 0; i < 70; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (i >= 20) times.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess));
            }
            return times.OrderBy(t => t).ElementAt(times.Count / 2);
        }
        double idleStep = await MedianStep();
        var sw = Stopwatch.StartNew();
        var cost = new List<Rid>();
        int many = 0;
        PlanetFlora fl = worlds[0];
        var pool = fl.Book.Plants.Where(p => p.Tree).ToArray();
        for (int i = 0; i < 400; i++)
        {
            Plant p = pool[i % pool.Length];
            var (lo, hi) = fl.ScaleRange(p.Family).Value;
            var one = new List<FloraSolid>();
            rules.MakeAll(TreeKindOf(p.Family), p, i % p.Near.Length, 100000 + i, Vector3.Zero, lo + (hi - lo) * rng.NextFloat(), rng.NextFloat() * 6f, one);
            many += one.Count(s => s.IsCanopy);
            cost.Add(CanopyBody(space, one, new Vector3(-4000f + (i % 20) * 12f, 3000f, -4000f + (i / 20) * 12f), shapes, cache, fl.Book.Plants.ToArray()));
        }
        double buildMs = sw.Elapsed.TotalMilliseconds;
        double treeStep = await MedianStep();
        state = PhysicsServer3D.SpaceGetDirectState(space);
        sw.Restart();
        for (int i = 0; i < 2000; i++)
        {
            var from = new Vector3(-4000f + rng.NextFloat() * 240f, 3000f + 25f, -4000f + rng.NextFloat() * 240f);
            state.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 30f, Layers.Flora));
        }
        double rayMs = sw.Elapsed.TotalMilliseconds / 2000.0;
        GD.Print($"canopy physics cost: {cost.Count} trees, {many} hulls: build {buildMs:F1} ms ({buildMs / cost.Count:F3} a tree), ray {rayMs * 1000:F1} us, physics step {idleStep * 1000:F2} ms without trees, {treeStep * 1000:F2} ms with");
        Check(buildMs / cost.Count < 2.0 && rayMs < 0.5 && treeStep - idleStep < 0.001, "many trees' canopies cost little physics time");

        foreach (Rid b in bodies.Concat(cost).Concat(balls)) PhysicsServer3D.FreeRid(b);
        PhysicsServer3D.FreeRid(ballShape);
        foreach (Rid s in shapes) PhysicsServer3D.FreeRid(s);
    }

    static float PartExtent(PlantMesh m, int part)
    {
        System.Numerics.Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (int t = 0; t < m.Triangles; t++)
        {
            if (m.PartOf[t] != part) continue;
            for (int v = 0; v < 3; v++) { lo = System.Numerics.Vector3.Min(lo, m.Positions[t * 3 + v]); hi = System.Numerics.Vector3.Max(hi, m.Positions[t * 3 + v]); }
        }
        return (hi - lo).Length();
    }
}
