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
    static Rid CanopyBody(Space3DHolder space, List<FloraSolid> solids, Vector3 at, List<Rid> shapes, Dictionary<(int, int, int), Rid> cache, Plant[] plants)
    {
        Rid body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
        PhysicsServer3D.BodySetCollisionLayer(body, Layers.Flora);
        PhysicsServer3D.BodySetCollisionMask(body, 0);
        foreach (FloraSolid s in solids)
        {
            Rid shape;
            if (!s.IsCanopy) continue;
            var key = (s.Plant, s.Variant, s.Part);
            if (!cache.TryGetValue(key, out shape))
            {
                var pts = plants[s.Plant].Canopy[s.Variant][s.Part].Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
                shape = PhysicsServer3D.ConvexPolygonShapeCreate();
                PhysicsServer3D.ShapeSetData(shape, pts);
                cache[key] = shape;
                shapes.Add(shape);
            }
            PhysicsServer3D.BodyAddShape(body, shape, new Transform3D(s.Basis.Scaled(Vector3.One * s.Scale), s.Center + at));
        }
        PhysicsServer3D.BodySetSpace(body, space.Rid);
        return body;
    }

    readonly struct Space3DHolder
    {
        public readonly Rid Rid;
        public Space3DHolder(Rid r) { Rid = r; }
    }

    async System.Threading.Tasks.Task Canopies()
    {
        // 1. Every tree species of every kind of world has a canopy.
        int species = 0, without = 0, nonConvexBox = 0;
        var worlds = new List<PlanetFlora>();
        foreach (var (a, v) in new[] { (Archetype.Temperate, 0), (Archetype.Temperate, 1), (Archetype.Alien, 0), (Archetype.Alien, 1), (Archetype.Arid, 0), (Archetype.Ice, 0), (Archetype.Lava, 0) })
        {
            var (_, _, flora) = Roll(Fresh(), a, v);
            worlds.Add(flora);
            foreach (Plant p in flora.Book.Plants.Where(p => p.Tree))
            {
                species++;
                if (p.Canopy == null || p.Canopy.Length == 0 || p.Canopy.Any(h => h == null || h.Length == 0 || h.Any(pts => pts.Length < 4))) without++;
                else if (p.Canopy[0].Length == 1 && p.Form is not (PlantForm.Domed or PlantForm.Barrel or PlantForm.Organ)) nonConvexBox++;
            }
        }
        Check(species > 20 && without == 0, $"every tree kind has canopy collision: {species} tree species on 7 worlds, {without} without");

        // 2. Rays and a flyer's sphere at the canopy's own surface are stopped, at the smallest and the largest scale.
        Rid space = GetViewport().World3D.Space;
        var holder = new Space3DHolder(space);
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
                    bodies.Add(CanopyBody(holder, solids, at, shapes, cache, flora.Book.Plants.ToArray()));
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
                        targets.Add((at + world + (side + Vector3.Up * 0.3f).Normalized() * (6f + scale * 4f), at + world, p.Family.ToString()));
                    }
                }
            }
        }
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        PhysicsDirectSpaceState3D state = PhysicsServer3D.SpaceGetDirectState(space);
        int rays = 0, rayStopped = 0, spheres = 0, sphereStopped = 0;
        Rid flyer = PhysicsServer3D.BodyCreate();
        Rid ball = PhysicsServer3D.SphereShapeCreate();
        PhysicsServer3D.ShapeSetData(ball, 0.6f);
        PhysicsServer3D.BodyAddShape(flyer, ball);
        PhysicsServer3D.BodySetMode(flyer, PhysicsServer3D.BodyMode.Kinematic);
        PhysicsServer3D.BodySetCollisionMask(flyer, Layers.Flora);
        PhysicsServer3D.BodySetCollisionLayer(flyer, 0);
        PhysicsServer3D.BodySetSpace(flyer, space);
        var motion = new PhysicsTestMotionParameters3D();
        var result = new PhysicsTestMotionResult3D();
        int step = Math.Max(1, targets.Count / 600);
        for (int i = 0; i < targets.Count; i += step)
        {
            var (from, to, _) = targets[i];
            var q = PhysicsRayQueryParameters3D.Create(from, to, Layers.Flora);
            rays++;
            var hit = state.IntersectRay(q);
            // Stopped at or before the point: the ray's end lies inside a hull, so it must be hit coming in (a ray starting outside).
            if (hit.Count > 0 && ((Vector3)hit["position"]).DistanceTo(from) <= from.DistanceTo(to) + 0.01f) rayStopped++;
            motion.From = new Transform3D(Basis.Identity, from);
            motion.Motion = to - from;
            spheres++;
            if (PhysicsServer3D.BodyTestMotion(flyer, motion, result)) sphereStopped++;
        }
        PhysicsServer3D.FreeRid(flyer);
        Check(rays > 200 && rayStopped >= rays * 0.99f, $"shots into canopies are stopped: {rayStopped} of {rays} rays at canopy surfaces, {treeCount} trees at their smallest and largest, {hulls} hulls");
        Check(spheres > 200 && sphereStopped >= spheres * 0.99f, $"a flyer flying into a canopy is stopped: {sphereStopped} of {spheres} sweeps");

        // 3. Hulls follow the canopy's shape: no box round a whole tree (a hull's volume is well inside its tree's bounding box).
        int boxy = 0, checkedTrees = 0;
        foreach (PlanetFlora flora in worlds.Take(4))
            foreach (Plant p in flora.Book.Plants.Where(p => p.Tree && p.Canopy != null))
            {
                for (int v = 0; v < p.Canopy.Length; v++)
                {
                    var pts = p.Canopy[v].SelectMany(h => h).ToArray();
                    if (pts.Length == 0) continue;
                    checkedTrees++;
                    var lo = new System.Numerics.Vector3(float.MaxValue); var hi = new System.Numerics.Vector3(float.MinValue);
                    foreach (var q in pts) { lo = System.Numerics.Vector3.Min(lo, q); hi = System.Numerics.Vector3.Max(hi, q); }
                    var size = hi - lo;
                    float box = size.X * size.Y * size.Z;
                    // A single hull filling its tree's whole box is a box in disguise.
                    foreach (var h in p.Canopy[v])
                    {
                        var l2 = new System.Numerics.Vector3(float.MaxValue); var h2 = new System.Numerics.Vector3(float.MinValue);
                        foreach (var q in h) { l2 = System.Numerics.Vector3.Min(l2, q); h2 = System.Numerics.Vector3.Max(h2, q); }
                        var s2 = h2 - l2;
                        if (p.Canopy[v].Length == 1 && s2.X * s2.Y * s2.Z > 0.98f * box && p.Form is PlantForm.Spire) boxy++;
                    }
                }
            }
        Check(boxy == 0, $"canopy hulls are parts' shapes, not a box round the tree ({checkedTrees} meshes)");

        // 4. Physics cost: many trees (a full near ring's worth), then queries among them.
        var sw = Stopwatch.StartNew();
        var cost = new List<Rid>();
        int many = 0;
        PlanetFlora fl = worlds[0];
        var pool = fl.Book.Plants.Where(p => p.Tree).ToArray();
        var solidsAll = new List<FloraSolid>();
        for (int i = 0; i < 400; i++)
        {
            Plant p = pool[i % pool.Length];
            var (lo, hi) = fl.ScaleRange(p.Family).Value;
            var one = new List<FloraSolid>();
            rules.MakeAll(TreeKindOf(p.Family), p, i % p.Near.Length, 100000 + i, Vector3.Zero, lo + (hi - lo) * rng.NextFloat(), rng.NextFloat() * 6f, one);
            many += one.Count(s => s.IsCanopy);
            cost.Add(CanopyBody(holder, one, new Vector3(-4000f + (i % 20) * 12f, 3000f, -4000f + (i / 20) * 12f), shapes, cache, fl.Book.Plants.ToArray()));
        }
        double buildMs = sw.Elapsed.TotalMilliseconds;
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        state = PhysicsServer3D.SpaceGetDirectState(space);
        sw.Restart();
        int hitsMany = 0;
        for (int i = 0; i < 2000; i++)
        {
            var from = new Vector3(-4000f + rng.NextFloat() * 240f, 3000f + 25f, -4000f + rng.NextFloat() * 240f);
            if (state.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 30f, Layers.Flora)).Count > 0) hitsMany++;
        }
        double rayMs = sw.Elapsed.TotalMilliseconds / 2000.0;
        double step0 = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess);
        double worst = 0;
        for (int i = 0; i < 60; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            worst = Math.Max(worst, Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess));
        }
        GD.Print($"canopy physics cost: {cost.Count} trees, {many} hulls: build {buildMs:F1} ms in all ({buildMs / cost.Count:F3} ms a tree), ray {rayMs * 1000:F1} us each ({hitsMany} of 2000 hit), physics step worst {worst * 1000:F2} ms (idle step {step0 * 1000:F2} ms)");
        Check(buildMs / cost.Count < 2.0 && rayMs < 0.5 && worst < 0.004, "many trees' canopies cost little physics time");

        foreach (Rid b in bodies.Concat(cost)) PhysicsServer3D.FreeRid(b);
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
