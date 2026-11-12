using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>Mesh times of a few big places (benchmark, not a check): --seed=N to compare two builds on the same places.</summary>
    void MeshTimes()
    {
        string arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--seed="));
        ulong seed = arg != null ? ulong.Parse(arg["--seed=".Length..]) : (ulong)Random.Shared.NextInt64();
        GD.Print($"TIMES seed {seed}");
        var standoff = typeof(MeshOptions).GetField("Standoff");
        foreach (string id in new[] { "sewage_works", "works_complex", "old_farm", "city_tower", "hospital" })
        {
            FamilyDef f = _lib.Families.FirstOrDefault(x => x.Id == id);
            if (f == null) { GD.Print($"TIMES {id}: none"); continue; }
            PoiInstance inst;
            try { inst = PoiFactory.Spawn(_lib, PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, seed), PoiState.Ruined, seed + 1); }
            catch (Exception e) { GD.Print($"TIMES {id}: {e.Message}"); continue; }
            BlockGrid g = inst.Blocks;
            MeshOptions Game(bool off) { var o = new MeshOptions { RockMin = g.Min, RockMax = g.Max, RockBottom = g.Min.Y }; if (off) standoff?.SetValue(o, false); return o; }
            MeshOptions Merged(bool merged, bool off)
            {
                var o = new MeshOptions
                {
                    ChunkSize = 32, Doors = false, Collision = true, Merged = merged, Drawn = true,
                    RockMin = new Int3(1, 0, 1), RockMax = new Int3(0, 0, 0), RockBottom = g.Min.Y, GroundShow = MeshOptions.AllGround,
                };
                if (off) standoff?.SetValue(o, false);
                return o;
            }
            (long Ms, int Quads, int Tris) Time(Func<MeshOptions> o)
            {
                long best = long.MaxValue;
                int q = 0, t = 0;
                for (int i = 0; i < 6; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var list = Mesher.Build(g, inst.Layout.Occupied, o());
                    best = Math.Min(best, sw.ElapsedMilliseconds);
                    q = list.Sum(m => m.Quads);
                    t = list.Sum(m => m.Collision.Count) / 9;
                }
                return (best, q, t);
            }
            if (standoff != null)
                foreach (var built in new[] { Mesher.Build(g, inst.Layout.Occupied, Game(false)), Mesher.Build(g, inst.Layout.Occupied, Merged(true, false)) })
                {
                    var list = new List<GalHack.Geometry.Coplanar.Face>();
                    foreach (ChunkMesh chunk in built)
                        for (int s = 0; s < chunk.Surfaces.Length; s++)
                        {
                            MeshBuffer m = chunk.Surfaces[s];
                            System.Numerics.Vector3 P(int i) => new(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
                            System.Numerics.Vector3 N(int i) => new(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
                            for (int k = 0; k + 2 < m.Indices.Count; k += 3)
                            {
                                int i0 = m.Indices[k], i1 = m.Indices[k + 1], i2 = m.Indices[k + 2];
                                var col = new System.Numerics.Vector4(m.Colors[i0 * 4], m.Colors[i0 * 4 + 1], m.Colors[i0 * 4 + 2], m.Colors[i0 * 4 + 3]);
                                list.Add(new GalHack.Geometry.Coplanar.Face(P(i0), P(i1), P(i2), N(i0) + N(i1) + N(i2), GalHack.Geometry.Coplanar.Look(s * 1000 + (int)m.Codes[i0 * 2], col), s));
                            }
                        }
                    GD.Print($"TIMES   left fighting: {GalHack.Geometry.Coplanar.Find(list).Count} pairs of {list.Count} faces");
                }
            var a0 = Time(() => Game(false));
            double T(long t) => t * 1000.0 / Stopwatch.Frequency / 3;
            var a = a0;
            var b = Time(() => Merged(false, false));
            var c = Time(() => Merged(true, false));
            string off = "";
            if (standoff != null)
            {
                var a2 = Time(() => Game(true));
                var b2 = Time(() => Merged(false, true));
                var c2 = Time(() => Merged(true, true));
                off = $" | standoff off: game {a2.Ms} ms, plain {b2.Ms} ms, merged {c2.Ms} ms";
            }
            GD.Print($"TIMES {id} {g.CountBlocks()} blocks: game {a.Ms} ms ({a.Quads} quads, {a.Tris} tris), plain {b.Ms} ms ({b.Quads}, {b.Tris}), merged {c.Ms} ms ({c.Quads}, {c.Tris}){off}");
        }
    }
}
