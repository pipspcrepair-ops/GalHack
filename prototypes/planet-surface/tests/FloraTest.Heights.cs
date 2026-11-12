using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Plants;
using Godot;
using Planets.Gen;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Rng = Planets.Gen.Rng;

namespace PlanetSurface.Tests;

/// <summary>
/// Tree heights: each kind of tree on each world rolls its own range; every tree stands inside it, young
/// and old mixed; near, far and distant drawings and the colliders all carry the one scale per tree.
/// </summary>
public partial class FloraTest
{
    void TreeHeights()
    {
        var ranges = new List<Dictionary<PlantFamily, (float, float)>>();
        foreach (var (a, v) in new[] { (Archetype.Temperate, 0), (Archetype.Alien, 1), (Archetype.Temperate, 2) })
        {
            ulong seed = Fresh();
            PlanetSpec spec = PlanetSpec.Roll(seed, a, v, 8);
            PlanetWorld w = PlanetWorld.Build(spec, null);
            var mine = new Dictionary<PlantFamily, (float, float)>();
            foreach (PlantFamily f in Enum.GetValues<PlantFamily>())
                if (w.Flora.ScaleRange(f) is var (lo, hi)) mine[f] = (lo, hi);
            ranges.Add(mine);
            // Rebuilt from the same seed: the same ranges.
            PlanetWorld again = PlanetWorld.Build(spec, null);
            bool same = mine.All(kv => again.Flora.ScaleRange(kv.Key) is var (lo, hi) && lo == kv.Value.Item1 && hi == kv.Value.Item2);
            Check(same && mine.Count > 0 && mine.All(kv => kv.Value.Item1 < 1f && kv.Value.Item2 > 1f && kv.Value.Item1 > 0.2f), $"{a}/{v}: tree height ranges are deterministic and sane ({mine.Count} kinds)");

            var rng = new Rng(Fresh());
            int trees = 0, outside = 0, mismatch = 0, noFar = 0, proud = 0, young = 0, old = 0;
            float smallest = 9999f, tallest = 0f;
            var ratio = new Dictionary<(int, int), (float Lo, float Hi)>();
            for (int i = 0; i < 60; i++)
            {
                int fx = (int)(rng.NextFloat() * FarBuilder.Count(w.Terrain.Width)), fz = (int)(rng.NextFloat() * FarBuilder.Count(w.Terrain.Height));
                var job = new FarJob { Fx = fx, Fz = fz, Lod = 0, SpanX = FarBuilder.Span(fx, w.Terrain.Width), SpanZ = FarBuilder.Span(fz, w.Terrain.Height) };
                FarBuilder.Build(job, w.Source, w.Palette, new HashSet<long>());
                if (job.Error != null) throw job.Error;
                // Every drawing of a tree by its spot: the transform's scale (column Y) and its width.
                var near = new Dictionary<long, (float S, float Sx, float Y, int Variant, Plant P, float X, float Z)>();
                var far = new Dictionary<long, List<(float S, float Sx)>>();
                foreach (FloraBatch b in job.Flora)
                {
                    if (b.Plant < 0) continue;
                    Plant p = w.Flora.Book.Plants[b.Plant];
                    if (!p.Tree) continue;
                    for (int k = 0; k < b.Count; k++)
                    {
                        int o = k * 16;
                        float lx = b.Buffer[o + 3], lz = b.Buffer[o + 11];
                        long id = FarBuilder.ScatterId(fx, fz, (int)(lx / FarBuilder.ScatterCell), (int)(lz / FarBuilder.ScatterCell));
                        float sy = b.Buffer[o + 5], sx = new Vector3(b.Buffer[o], b.Buffer[o + 4], b.Buffer[o + 8]).Length();
                        if (!b.Far) near[id] = (sy, sx, b.Buffer[o + 7], b.Variant, p, lx, lz);
                        else
                        {
                            if (!far.TryGetValue(id, out var list)) far[id] = list = new();
                            list.Add((sy, sx));
                        }
                    }
                }
                foreach (var (id, n) in near)
                {
                    double x = fx * (double)FarBuilder.Size + n.X, z = fz * (double)FarBuilder.Size + n.Z;
                    if (w.Palette.Planted != null && w.Palette.Planted.ContainsKey(id)) continue; // ruin trees shrink to fit their walls
                    trees++;
                    var (lo, hi) = w.Flora.HeightRange(n.P).Value;
                    float height = n.P.Height * n.S;
                    if (height < lo * 0.995f || height > hi * 1.005f) outside++;
                    float unit = (height - lo) / (hi - lo);
                    if (unit < 0.3f) young++;
                    if (unit > 0.7f) old++;
                    smallest = MathF.Min(smallest, height);
                    tallest = MathF.Max(tallest, height);
                    // Uniform: trunk and crown scale together.
                    if (MathF.Abs(n.Sx - n.S) > 0.001f * n.S) mismatch++;
                    if (!far.TryGetValue(id, out var drawings)) noFar++;
                    else if (drawings.Any(d => MathF.Abs(d.S - n.S) > 1e-4f || MathF.Abs(d.Sx - n.Sx) > 1e-4f)) mismatch++;
                    // Roots meet the ground.
                    if (n.Y > w.Source.SurfaceHeight(x, z) - FloraSolidRules.Sink + 0.01f) proud++;
                    // Colliders: the first solid part of this tree scales with it.
                    foreach (FloraSolid s in job.Solids.Where(s => s.Id == id && s.Plant == n.P.Index))
                    {
                        float r = s.Extents.X / n.S;
                        var key = (n.P.Index, n.Variant);
                        ratio[key] = ratio.TryGetValue(key, out var cur) ? (MathF.Min(cur.Lo, r), MathF.Max(cur.Hi, r)) : (r, r);
                        break;
                    }
                }
            }
            int colliderOff = ratio.Count(kv => kv.Value.Hi - kv.Value.Lo > 0.02f * kv.Value.Hi + 1e-4f);
            Check(trees > 100 && outside == 0, $"{a}/{v}: every tree's height is inside its kind's range: {trees} trees, {outside} outside; {smallest:F1} to {tallest:F1} m");
            Check(young > trees / 8 && old > trees / 12, $"{a}/{v}: young and old are mixed: {young} young, {old} old of {trees}");
            Check(noFar == 0 && mismatch == 0, $"{a}/{v}: near and far drawings of a tree share its scale: {mismatch} differ, {noFar} without a far drawing");
            Check(colliderOff == 0, $"{a}/{v}: colliders scale with their tree ({ratio.Count} species-variants, {colliderOff} off)");
            Check(proud == 0, $"{a}/{v}: no tree floats or sinks ({proud} proud of the ground)");
        }
        bool differ = false;
        for (int i = 1; i < ranges.Count; i++)
            foreach (var (f, r) in ranges[i])
                if (ranges[0].TryGetValue(f, out var q) && (MathF.Abs(q.Item1 - r.Item1) > 0.01f || MathF.Abs(q.Item2 - r.Item2) > 0.01f)) differ = true;
        Check(differ, "tree height ranges differ between worlds");
    }
}
