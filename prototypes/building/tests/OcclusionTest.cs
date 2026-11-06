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
