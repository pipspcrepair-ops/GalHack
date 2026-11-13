using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;

namespace BlockBuilding;

/// <summary>
/// Meshing checks and timings: a few exact triangle counts, then a 20,000-block structure meshed
/// from scratch and edited one block at a time (each edit rebuilds only its dirty chunks).
/// </summary>
public static class MeshBenchmark
{
    public static void Run(Catalogue cat, Action<bool, string> check)
    {
        BlockType steel = cat.Type("steel"), hardened = cat.Type("hardened"), combat = cat.Type("combat"), glass = cat.Type("glass");

        // Exact counts on tiny cases.
        var one = new Structure(cat);
        one.Set(Int3.Zero, Building.Make(one, steel));
        check(Triangles(one) == 12, "a lone cube is 6 quads");
        for (int x = 1; x < 8; x++) one.Set(new Int3(x, 0, 0), Building.Make(one, steel));
        check(Triangles(one) == 12, "a row of 8 identical cubes greedy-merges to 6 quads");
        one.Set(new Int3(3, 0, 0), Building.Make(one, hardened));
        check(Triangles(one) > 12 && Triangles(one) <= 28, $"a different material splits the merge ({Triangles(one)} triangles)");
        var slope = new Structure(cat);
        slope.Set(Int3.Zero, Building.Make(slope, steel, cat.Shape("slope")));
        check(Triangles(slope) == 8, "a slope is 2 squares, 1 ramp and 2 triangles (8 triangles)");
        slope.Set(new Int3(0, 0, 1), Building.Make(slope, steel));
        check(Triangles(slope) == 16, "a slope's full back face hides the cube face behind it, and vice versa");

        // The big one.
        var s = BigStructure(cat, 20_000);
        int chunks = s.Chunks.Count, blocks = s.Count;
        // The first pass includes the JIT compiling the mesher; the second is the steady state.
        var cold = Stopwatch.StartNew();
        foreach (Int3 c in s.Chunks.Keys.ToList()) ChunkMesher.ToMesh(ChunkMesher.Build(s, c))?.Dispose();
        cold.Stop();

        var times = new List<double>();
        double buildMs = 0, uploadMs = 0;
        long triangles = 0, boxes = 0, hulls = 0;
        var total = Stopwatch.StartNew();
        foreach (Int3 c in s.Chunks.Keys.ToList())
        {
            var sw = Stopwatch.StartNew();
            ChunkMeshData data = ChunkMesher.Build(s, c);
            double built = sw.Elapsed.TotalMilliseconds;
            ArrayMesh mesh = ChunkMesher.ToMesh(data);
            sw.Stop();
            buildMs += built;
            uploadMs += sw.Elapsed.TotalMilliseconds - built;
            times.Add(sw.Elapsed.TotalMilliseconds);
            triangles += (data.Opaque.Indices.Count + data.Glass.Indices.Count) / 3;
            boxes += data.Boxes.Count;
            hulls += data.Hulls.Count;
            mesh?.Dispose();
        }
        total.Stop();
        s.DirtyChunks.Clear();

        // Single-block edits: knock out or add a block, then remesh what it dirtied.
        var rng = new Random(7);
        var (min, max) = s.Bounds();
        var edits = new List<double>();
        int remeshed = 0;
        for (int i = 0; i < 60; i++)
        {
            var p = new Int3(rng.Next(min.X, max.X + 1), rng.Next(min.Y, max.Y + 1), rng.Next(min.Z, max.Z + 1));
            if (p == s.Core) continue;
            var sw = Stopwatch.StartNew();
            if (s.Has(p)) s.Clear(p);
            else s.Set(p, Building.Make(s, steel));
            foreach (Int3 c in s.DirtyChunks)
            {
                ChunkMesher.ToMesh(ChunkMesher.Build(s, c))?.Dispose();
                remeshed++;
            }
            s.DirtyChunks.Clear();
            sw.Stop();
            edits.Add(sw.Elapsed.TotalMilliseconds);
        }

        GD.Print($"      {blocks} blocks in {chunks} chunks of 16³, {triangles:N0} triangles, {boxes} collider boxes, {hulls} convex hulls");
        GD.Print($"      full build: {total.Elapsed.TotalMilliseconds:0.0} ms total ({buildMs:0.0} ms meshing, {uploadMs:0.0} ms making ArrayMeshes), " +
                 $"{times.Average():0.00} ms average per chunk, {times.Max():0.00} ms worst chunk");
        GD.Print($"      first (cold, JIT compiling) pass: {cold.Elapsed.TotalMilliseconds:0.0} ms");
        GD.Print($"      single-block edit: {edits.Average():0.00} ms average, {edits.Max():0.00} ms worst ({remeshed / (double)edits.Count:0.0} chunks rebuilt per edit)");
        check(blocks >= 20_000, $"benchmark structure has {blocks} blocks");
        check(total.Elapsed.TotalMilliseconds < 3000, $"a 20,000-block structure meshes in {total.Elapsed.TotalMilliseconds:0} ms");
        check(edits.Average() < 16, $"a single-block edit remeshes within a 60 fps frame ({edits.Average():0.0} ms)");
    }

    static int Triangles(Structure s)
    {
        int n = 0;
        foreach (Int3 c in s.Chunks.Keys)
        {
            ChunkMeshData d = ChunkMesher.Build(s, c);
            n += (d.Opaque.Indices.Count + d.Glass.Indices.Count) / 3;
        }
        return n;
    }

    /// <summary>
    /// A deterministic slab of hull: mostly armour cubes in bands of material and paint (as a
    /// real ship is), with a sprinkling of shapes, glass and components, and holes to make faces.
    /// </summary>
    public static Structure BigStructure(Catalogue cat, int target)
    {
        var s = new Structure(cat) { Name = "Benchmark" };
        var rng = new Random(1977);
        BlockType[] armour = { cat.Type("steel"), cat.Type("hardened"), cat.Type("combat") };
        BlockType glass = cat.Type("glass");
        BlockType[] parts = { cat.Type("thruster"), cat.Type("generator"), cat.Type("container"), cat.Type("fuel-tank-t1") };
        ushort[] paints =
        {
            0,
            s.Palette.Intern(Paint.Solid(0xe9dfc4)),
            s.Palette.Intern(Paint.Solid(0xe85d04).WithPattern(Pattern.Stripes, 0xf4a100)),
            s.Palette.Intern(Paint.Solid(0x0a9396) with { Grime = 2 }),
            s.Palette.Intern(Paint.Solid(0x2b2d42).WithPattern(Pattern.Checker, 0xe9dfc4)),
        };
        s.Set(Int3.Zero, Building.Make(s, cat.CoreType));
        const int w = 40, h = 13, d = 44;
        for (int z = -d / 2; z < d / 2 && s.Count < target; z++)
            for (int y = 0; y < h && s.Count < target; y++)
                for (int x = -w / 2; x < w / 2 && s.Count < target; x++)
                {
                    var p = new Int3(x, y, z);
                    if (p == Int3.Zero || rng.NextDouble() < 0.04) continue;
                    double roll = rng.NextDouble();
                    Block b;
                    if (roll < 0.02) b = Building.Make(s, parts[rng.Next(parts.Length)], null, rng.Next(Orientations.Count));
                    else if (roll < 0.05) b = Building.Make(s, glass);
                    else if (roll < 0.17) b = Building.Make(s, armour[0], cat.Shapes[1 + rng.Next(cat.Shapes.Count - 1)], rng.Next(Orientations.Count));
                    else b = Building.Make(s, armour[(y / 4) % 3]);
                    b.Paint = FacePaint.All(paints[(z / 6 + 10) % paints.Length]);
                    s.Set(p, b);
                }
        return s;
    }
}
