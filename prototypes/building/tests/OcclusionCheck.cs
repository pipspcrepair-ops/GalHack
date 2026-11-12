using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;

namespace BlockBuilding;

/// <summary>
/// Does a block hide a cube's face without really covering it? Asked of the meshes themselves, not
/// the catalogue's masks: a cube is meshed with the block beside it, and if the cube's touching
/// face is gone, the block meshed alone must draw opaque triangles over every point of that face.
/// A point left uncovered is a hole you can see through the construct. Shared with the slice's
/// tests, whose catalogue has more blocks in it.
/// </summary>
public static class OcclusionCheck
{
    const int Samples = 9;

    public sealed class Result
    {
        public int Cases, Culled, Holes;
        public readonly List<string> Worst = new();
        /// <summary>Block ids that hid at least one cube face, having covered it completely.</summary>
        public readonly SortedSet<string> Hiders = new(StringComparer.Ordinal);
    }

    public static IEnumerable<(string name, Block block)> EveryBlock(Catalogue cat)
    {
        var s = new Structure(cat);
        foreach (BlockType t in cat.Types.Skip(1))
        {
            if (t.IsArmor)
                foreach (ShapeDef shape in cat.Shapes) yield return ($"{t.Id}/{shape.Id}", Building.Make(s, t, shape));
            else
            {
                Block b = Building.Make(s, t);
                yield return (t.Id, b);
                if (t.IsDoor)
                {
                    b.Flags |= BlockFlags.Open;
                    yield return ($"{t.Id} (open)", b);
                }
            }
        }
    }

    /// <summary>Every block in every orientation against a cube on every side.</summary>
    public static Result Run(Catalogue cat, string cubeType = "iron-plated")
    {
        var result = new Result();
        BlockType cube = cat.Type(cubeType);
        foreach (var (name, proto) in EveryBlock(cat))
            for (int rot = 0; rot < Orientations.Count; rot++)
                foreach (Dir d in Dirs.All)
                {
                    Block b = proto;
                    b.Rot = (byte)rot;
                    One(cat, cube, name, b, d, result);
                }
        return result;
    }

    static void One(Catalogue cat, BlockType cubeType, string name, Block block, Dir d, Result result)
    {
        var s = new Structure(cat);
        // A block spanning several cells grows away from the cube, as the build tool places it: touching, never overlapping.
        Int3 at = new Int3(8, 8, 8), beside = Footprint.AnchorFor(cat.Types[block.Type], block.Rot, at + d.Vec(), d);
        s.Set(at, Building.Make(s, cubeType));
        s.Set(beside, block);
        float size = cat.Grid.CellSize;
        int axis = d.Axis();
        float plane = (at[axis] + d.Sign() * 0.5f) * size;
        Vector3 outward = MeshBuffers.ToGodot(d.Normal());

        ChunkMeshData both = ChunkMesher.Build(s, Int3.Zero, colliders: false);
        result.Cases++;
        var face = TrianglesOn(both.Opaque, axis, plane, outward);
        if (Covered(face, at, axis, d, size) == Samples * Samples) return; // the cube's face is there: nothing to hide it
        result.Culled++;

        var alone = new Structure(cat);
        alone.Set(beside, block);
        ChunkMeshData solo = ChunkMesher.Build(alone, Int3.Zero, colliders: false);
        int covered = Covered(TrianglesOn(solo.Opaque, axis, plane, -outward), at, axis, d, size);
        if (covered < Samples * Samples)
        {
            result.Holes++;
            if (result.Worst.Count < 12) result.Worst.Add($"{name} rot {block.Rot} on {Dirs.Names[(int)d]}: {covered} of {Samples * Samples} points covered");
        }
        else result.Hiders.Add(name);
    }

    static List<(Vector3 a, Vector3 b, Vector3 c)> TrianglesOn(MeshBuffers m, int axis, float plane, Vector3 facing)
    {
        var found = new List<(Vector3, Vector3, Vector3)>();
        for (int i = 0; i + 2 < m.Indices.Count; i += 3)
        {
            Vector3 a = m.Vertices[m.Indices[i]], b = m.Vertices[m.Indices[i + 1]], c = m.Vertices[m.Indices[i + 2]];
            if (MathF.Abs(a[axis] - plane) > 1e-3f || MathF.Abs(b[axis] - plane) > 1e-3f || MathF.Abs(c[axis] - plane) > 1e-3f) continue;
            if (m.Normals[m.Indices[i]].Dot(facing) < 0.5f) continue;
            found.Add((a, b, c));
        }
        return found;
    }

    /// <summary>How many of the sample points across the cell's face some triangle covers.</summary>
    static int Covered(List<(Vector3 a, Vector3 b, Vector3 c)> triangles, Int3 cell, int axis, Dir d, float size)
    {
        int u = (axis + 1) % 3, v = (axis + 2) % 3, covered = 0;
        for (int i = 0; i < Samples; i++)
            for (int j = 0; j < Samples; j++)
            {
                float pu = (cell[u] - 0.5f + (i + 0.5f) / Samples) * size, pv = (cell[v] - 0.5f + (j + 0.5f) / Samples) * size;
                foreach (var (a, b, c) in triangles)
                    if (Inside(pu, pv, a, b, c, u, v))
                    {
                        covered++;
                        break;
                    }
            }
        return covered;
    }

    static bool Inside(float px, float py, Vector3 a, Vector3 b, Vector3 c, int u, int v)
    {
        float e0 = (b[u] - a[u]) * (py - a[v]) - (b[v] - a[v]) * (px - a[u]);
        float e1 = (c[u] - b[u]) * (py - b[v]) - (c[v] - b[v]) * (px - b[u]);
        float e2 = (a[u] - c[u]) * (py - c[v]) - (a[v] - c[v]) * (px - c[u]);
        return (e0 >= 0 && e1 >= 0 && e2 >= 0) || (e0 <= 0 && e1 <= 0 && e2 <= 0);
    }
}
