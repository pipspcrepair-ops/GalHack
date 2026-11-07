using System;
using System.Diagnostics;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;

namespace BlockBuilding;

/// <summary>
/// Faces are culled only behind a genuinely full, opaque face: nothing beside a cube may leave a
/// hole through the construct, and real cubes must go on culling. Run with:
///   .\tools\test.ps1 -Project prototypes/building -Scene res://tests/OcclusionTest.tscn
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class OcclusionTest : Node
{
    int _passes, _failures;
    Catalogue _cat;

    public override void _Ready()
    {
        _cat = GameData.Catalogue;
        foreach (GridDef grid in _cat.Grids)
        {
            OcclusionCheck.Result r = OcclusionCheck.Run(_cat, grid);
            foreach (string w in r.Worst) GD.Print($"      {grid.Id}: {w}");
            Check(r.Holes == 0, $"{grid.Id} grid: every block in every orientation against a cube on every side, {r.Cases} cases: {r.Culled} cube faces hidden, {r.Holes} of them left a hole");
            GD.Print($"      hiders: {string.Join(", ", r.Hiders)}");
        }

        Structure pair = Pair("steel", "steel");
        Check(Faces(pair) == 10, $"cube against cube still culls: two cubes draw 10 faces, not 12 ({Faces(pair)})");
        Check(Faces(Pair("steel", "concrete")) == 10, "and across materials");
        Check(Faces(Pair("steel", "generator")) > 0 && _cat.OccludeMask(Rotated("generator")) != 0, "a device that really is a full box still hides what's behind it");
        int beside = Faces(Pair("glass", "steel"));
        ChunkMeshData panes = ChunkMesher.Build(Pair("glass", "steel"), Int3.Zero, colliders: false);
        Check(beside == 6 && panes.Glass.VertexCount > 0, $"glass hides nothing: the cube beside it keeps all 6 of its faces ({beside})");
        int overclaims = 0;
        foreach (BlockType t in _cat.Types.Skip(1).Where(t => !t.IsArmor))
            for (int open = 0; open < (t.IsDoor ? 2 : 1); open++)
            {
                var probe = new Block { Type = (ushort)t.Index, Flags = open == 1 ? BlockFlags.Open : BlockFlags.None };
                byte mask = _cat.OccludeMask(probe);
                int declared = open == 1 ? t.CoverFacesOpen : t.CoverFaces, kept = 0;
                foreach (Dir d in Dirs.All) if ((mask & (1 << (int)d)) != 0) kept |= 1 << (int)d;
                if (kept == declared) continue;
                overclaims++;
                GD.Print($"      {t.Id}{(open == 1 ? " (open)" : "")}: declares {string.Join(" ", Dirs.All.Where(d => BlockType.Has(declared, d)).Select(d => Dirs.Names[(int)d]))}; really covers {string.Join(" ", Dirs.All.Where(d => BlockType.Has(kept, d)).Select(d => Dirs.Names[(int)d]))}");
            }
        Check(overclaims == 0, "no component's data claims to cover a face its parts don't");
        Seams();
        Cost();

        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (ok) _passes++;
        else _failures++;
    }

    Block Rotated(string id)
    {
        var s = new Structure(_cat, _cat.Grid(GridSize.Large));
        return Building.Make(s, _cat.Type(id));
    }

    Structure Pair(string a, string b)
    {
        var s = new Structure(_cat, _cat.Grid(GridSize.Large));
        s.Set(Int3.Zero, Building.Make(s, _cat.Type(a)));
        s.Set(new Int3(1, 0, 0), Building.Make(s, _cat.Type(b)));
        return s;
    }

    /// <summary>The quads two cubes draw once their faces are merged out: counted by outward area, in cell faces.</summary>
    static int Faces(Structure s)
    {
        ChunkMeshData d = ChunkMesher.Build(s, Int3.Zero, colliders: false);
        float cell = s.Grid.CellSize, area = 0f;
        for (int i = 0; i + 2 < d.Opaque.Indices.Count; i += 3)
        {
            Vector3 a = d.Opaque.Vertices[d.Opaque.Indices[i]], b = d.Opaque.Vertices[d.Opaque.Indices[i + 1]], c = d.Opaque.Vertices[d.Opaque.Indices[i + 2]];
            area += (b - a).Cross(c - a).Length() * 0.5f;
        }
        return (int)MathF.Round(area / (cell * cell));
    }

    /// <summary>
    /// A hull across chunk edges with blocks shot out of it, redrawn only as the game redraws it (the chunks
    /// the structure marks dirty): every face that now shows, inside and out, must be there to a ray,
    /// front side on, judged from the triangles and not from what the mesher thinks it culled.
    /// </summary>
    void Seams()
    {
        var rand = new Random();
        var s = new Structure(_cat, _cat.Grid(GridSize.Large));
        const int Lo = -9, Hi = 11;
        for (int x = Lo; x <= Hi; x++)
            for (int y = Lo; y <= Hi; y++)
                for (int z = Lo; z <= Hi; z++)
                    if (x == Lo || y == Lo || z == Lo || x == Hi || y == Hi || z == Hi || (x == 0 || y == 0 || z == 0) && rand.Next(3) == 0)
                        s.Set(new Int3(x, y, z), Building.Make(s, _cat.Type("steel")));
        var meshes = new System.Collections.Generic.Dictionary<Int3, ChunkMeshData>();
        void Redraw()
        {
            foreach (Int3 c in s.DirtyChunks)
                if (s.Chunks.ContainsKey(c)) meshes[c] = ChunkMesher.Build(s, c, colliders: false);
                else meshes.Remove(c);
            s.DirtyChunks.Clear();
        }
        Redraw();
        var blocks = s.Blocks().Select(b => b.pos).OrderBy(_ => rand.Next()).ToList();
        foreach (Int3 p in blocks.Take(blocks.Count / 4)) s.Clear(p);
        Redraw();

        float cell = s.Grid.CellSize;
        var tris = new System.Collections.Generic.List<(Vector3 A, Vector3 B, Vector3 C)>();
        foreach (ChunkMeshData d in meshes.Values)
            for (int i = 0; i + 2 < d.Opaque.Indices.Count; i += 3)
                tris.Add((d.Opaque.Vertices[d.Opaque.Indices[i]], d.Opaque.Vertices[d.Opaque.Indices[i + 1]], d.Opaque.Vertices[d.Opaque.Indices[i + 2]]));
        int faces = 0, missing = 0;
        foreach (var (p, _) in s.Blocks())
            foreach (Dir d in Dirs.All)
            {
                if (s.TryGet(p + d.Vec(), out Block nb) && !nb.IsEmpty) continue;
                faces++;
                Vector3 normal = MeshBuffers.ToGodot(d.Normal());
                Vector3 side1 = new(normal.Y, normal.Z, normal.X), side2 = normal.Cross(side1);
                Vector3 face = (new Vector3(p.X, p.Y, p.Z) + normal * 0.5f + side1 * ((float)rand.NextDouble() * 0.8f - 0.4f) + side2 * ((float)rand.NextDouble() * 0.8f - 0.4f)) * cell;
                Vector3 from = face + normal * 0.3f * cell, dir = -normal;
                bool met = false;
                foreach (var (a, b, c) in tris)
                {
                    Vector3 e1 = b - a, e2 = c - a;
                    if (dir.Dot(e1.Cross(e2)) <= 0f) continue; // its back: not drawn from here (Godot's fronts wind clockwise)
                    Vector3 h = dir.Cross(e2);
                    float det = e1.Dot(h);
                    if (MathF.Abs(det) < 1e-9f) continue;
                    Vector3 o = from - a;
                    float u = o.Dot(h) / det, v = dir.Dot(o.Cross(e1)) / det;
                    if (u < -1e-4f || v < -1e-4f || u + v > 1.0001f) continue;
                    float t = e2.Dot(o.Cross(e1)) / det;
                    if (MathF.Abs(t - 0.3f * cell) < 0.02f * cell) { met = true; break; }
                }
                if (!met) missing++;
            }
        Check(missing == 0 && faces > 1000, $"a shot-up hull across chunk edges, redrawn only where the structure says: {faces} faces that show, {missing} missing to a ray");
    }

    /// <summary>What honest culling costs: a hull with thrusters set into it, meshed.</summary>
    void Cost()
    {
        var s = new Structure(_cat, _cat.Grid(GridSize.Small));
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    if (x == 0 || y == 0 || z == 0 || x == 15 || y == 15 || z == 15)
                        s.Set(new Int3(x, y, z), Building.Make(s, _cat.Type((x + y + z) % 5 == 0 ? "thruster" : "steel")));
        // The same chunk with plain steel where the thrusters are, turn about in the same run: what a
        // thruster costs to mesh at all, whatever else the machine is doing.
        var plain = new Structure(_cat, _cat.Grid(GridSize.Small));
        foreach (var (p, _) in s.Blocks()) plain.Set(p, Building.Make(plain, _cat.Type("steel")));
        var data = new ChunkMeshData();
        double best = double.MaxValue, bestPlain = double.MaxValue;
        for (int window = 0; window < 5; window++)
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) ChunkMesher.Build(plain, Int3.Zero, data, colliders: false);
            bestPlain = Math.Min(bestPlain, clock.Elapsed.TotalMilliseconds / 5);
            clock.Restart();
            for (int i = 0; i < 5; i++) ChunkMesher.Build(s, Int3.Zero, data, colliders: false);
            best = Math.Min(best, clock.Elapsed.TotalMilliseconds / 5);
        }
        GD.Print($"      plain steel shell {bestPlain:F2} ms; {data.Opaque.VertexCount} vertices with thrusters {best:F2} ms");
        // Vertices are the cost that doesn't depend on who else is using the machine. Before this
        // fix the chunk drew 52692; the time is printed for reference (12 to 18 ms under load, as before).
        Check(data.Opaque.VertexCount <= 56000, $"honest culling costs little: a chunk one block in five a thruster draws {data.Opaque.VertexCount} vertices, was 52692");
        Check(bestPlain < 2.0, $"and real cubes keep the whole optimisation: a plain steel shell meshes in {bestPlain:F2} ms (best of five)");
    }
}
