using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Geometry;
using Godot;
using GalHack.Testing;
using PoiGenerator.Gen;
using NV3 = System.Numerics.Vector3;
using NV4 = System.Numerics.Vector4;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// No two faces of a place's blocks z-fight: on fresh places of every family (and city lots and the hulks) in
    /// whatever state they roll, meshed as the game meshes them, no two faces of different looks lie in one plane
    /// over each other (<see cref="Coplanar"/>). Pairs are counted by the blocks that drew them.
    /// </summary>
    void CoplanarFaces()
    {
        var rand = new Random();
        var spawns = new List<PoiInstance>();
        foreach (FamilyDef f in _lib.Families.Where(f => !f.Tags.Contains("grand")).OrderBy(_ => rand.Next()).Take(40))
        {
            var states = Enum.GetValues<PoiState>().Where(s => f.StateWeights[(int)s] > 0f).ToList();
            if (states.Count == 0) continue;
            try { spawns.Add(PoiFactory.Generate(_lib, f, rand.Next(f.Variants.Count), f.Rarity, states[rand.Next(states.Count)], (ulong)rand.NextInt64())); }
            catch (Exception) { /* a family that refuses this roll is another check's business */ }
        }
        foreach (string id in new[] { "crashed_freighter", "city_house", "city_terrace", "city_works" })
            if (_lib.Families.FirstOrDefault(x => x.Id == id) is FamilyDef f)
                try { spawns.Add(PoiFactory.Generate(_lib, f, rand.Next(f.Variants.Count), f.Rarity, PoiState.Ruined, (ulong)rand.NextInt64())); } catch (Exception) { }
        int total = 0, faces = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var kinds = new Dictionary<string, int>();
        var worst = new List<string>();
        foreach (PoiInstance p in spawns)
        {
            BlockGrid g = p.Blocks;
            var list = new List<Coplanar.Face>();
            var sources = new List<string>();
            var source = new Dictionary<string, int>();
            foreach (ChunkMesh chunk in Mesher.Build(g, p.Layout.Occupied, new MeshOptions { Rock = false, Collision = false }))
                for (int s = 0; s < chunk.Surfaces.Length; s++)
                {
                    MeshBuffer m = chunk.Surfaces[s];
                    for (int t = 0; t + 2 < m.Indices.Count; t += 3)
                    {
                        int i0 = m.Indices[t], i1 = m.Indices[t + 1], i2 = m.Indices[t + 2];
                        NV3 P(int i) => new(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
                        NV3 N(int i) => new(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
                        NV3 a = P(i0), b = P(i1), c = P(i2), n = N(i0) + N(i1) + N(i2);
                        // Who drew it: the block a hair behind the face's middle.
                        NV3 inside = (a + b + c) / 3f - NV3.Normalize(n == NV3.Zero ? NV3.UnitY : n) * 0.01f;
                        Block bl = g.Get(new Int3((int)MathF.Floor(inside.X), (int)MathF.Floor(inside.Y), (int)MathF.Floor(inside.Z)));
                        string who = bl.IsAir ? "air" : $"{bl.Type}";
                        if (!source.TryGetValue(who, out int id)) { source[who] = id = sources.Count; sources.Add(who); }
                        var col = new NV4(m.Colors[i0 * 4], m.Colors[i0 * 4 + 1], m.Colors[i0 * 4 + 2], m.Colors[i0 * 4 + 3]);
                        list.Add(new Coplanar.Face(a, b, c, n, Coplanar.Look(s * 1000 + (int)m.Codes[i0 * 2], col), id));
                    }
                }
            faces += list.Count;
            List<Coplanar.Pair> pairs = Coplanar.Find(list);
            total += pairs.Count;
            foreach (var (k, n) in Coplanar.BySource(list, pairs, i => sources[i])) kinds[k] = kinds.GetValueOrDefault(k) + n;
            if (pairs.Count > 0)
            {
                Coplanar.Pair w = pairs.MaxBy(q => q.Area);
                Coplanar.Face f = list[w.P];
                worst.Add($"{p.Family.Id} {p.State}: {pairs.Count} pairs, e.g. {sources[f.Source]}/{sources[list[w.Q].Source]} {w.Area * 1e4f:F0} cm2 at {(f.A + f.B + f.C) / 3f}");
            }
        }
        var cost = System.Diagnostics.Stopwatch.StartNew();
        foreach (PoiInstance p in spawns.Take(12)) Mesher.Build(p.Blocks, p.Layout.Occupied, new MeshOptions { Rock = false, Collision = false, Standoff = false });
        long plain = cost.ElapsedMilliseconds;
        cost.Restart();
        foreach (PoiInstance p in spawns.Take(12)) Mesher.Build(p.Blocks, p.Layout.Occupied, new MeshOptions { Rock = false, Collision = false });
        GD.Print($"    meshing 12 places: {plain} ms plain, {cost.ElapsedMilliseconds} ms with faces set apart");
        GD.Print($"    {spawns.Count} places, {faces:N0} faces, {total} fighting pairs ({clock.ElapsedMilliseconds} ms, meshing and the standoff pass included)");
        foreach (var kv in kinds.OrderByDescending(k => k.Value).Take(12)) GD.Print($"      {kv.Value,6}  {kv.Key}");
        foreach (string w in worst.OrderByDescending(w => w).Take(8)) GD.Print("      e.g. " + w);
        Check(total == 0, $"no two faces of a place's blocks lie in one plane over each other ({total} pairs in {worst.Count} of {spawns.Count} places)");
    }
}
