using System;
using System.Collections.Generic;
using System.Linq;
using PoiGenerator.Gen;
using FinishKind = PoiGenerator.Gen.Finish;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// The count of opaque triangles in a mesh that would draw without texture, by what they are: any triangle whose code
    /// is the plain finish (so the place shader draws a flat colour), and any whose vertices disagree about their code.
    /// A triangle's frame is the shader's own (across and up from the vertex normal, in metres): it must hold the face's
    /// real area (the normal and the face agree), so the texture is neither collapsed to a line nor stretched.
    /// </summary>
    static void Untextured(IEnumerable<ChunkMesh> meshes, Func<V3, V3, string> what, Dictionary<string, int> into, ref int tris)
    {
        foreach (ChunkMesh m in meshes)
            for (int s = 0; s < 2; s++)
            {
                MeshBuffer b = m.Surfaces[s];
                V3 P(int i) => new(b.Positions[i * 3], b.Positions[i * 3 + 1], b.Positions[i * 3 + 2]);
                V3 N(int i) => new(b.Normals[i * 3], b.Normals[i * 3 + 1], b.Normals[i * 3 + 2]);
                for (int t = 0; t + 2 < b.Indices.Count; t += 3)
                {
                    int i0 = b.Indices[t], i1 = b.Indices[t + 1], i2 = b.Indices[t + 2];
                    V3 cross = V3.Cross(P(i1) - P(i0), P(i2) - P(i0));
                    float area = MathF.Sqrt(V3.Dot(cross, cross)) * 0.5f;
                    if (area < 1e-4f) continue;
                    tris++;
                    V3 c = (P(i0) + P(i1) + P(i2)) * (1f / 3f), n = N(i0);
                    string why = null;
                    if (FinishKind.Plain == Gen.Surfaces.FinishOf(b.Codes[i0 * 2]) || b.Codes[i0 * 2] != b.Codes[i1 * 2] || b.Codes[i0 * 2] != b.Codes[i2 * 2]) why = "plain";
                    else if (MathF.Abs(V3.Dot(cross.Normalized(), n.Normalized())) < 0.7f) why = "frame";
                    if (why != null) { string key = why + " " + what(c, n) + (n.Y > 0.5f ? " up" : n.Y < -0.5f ? " down" : MathF.Abs(n.Y) > 0.05f ? " slope" : " side"); into[key] = into.GetValueOrDefault(key) + 1; }
                }
            }
    }

    /// <summary>Every face of every shaped block, in every direction and in each make, and of fresh places, carries its material's texture.</summary>
    void FacesTextured()
    {
        var look = new PlaceLook { Book = _lib.Finishes };
        var bad = new Dictionary<string, int>();
        int tris = 0;
        var types = new List<BlockType> { BlockType.Cube, BlockType.Ramp, BlockType.Corner, BlockType.InvCorner, BlockType.Pillar, BlockType.Rib, BlockType.Spike };
        for (BlockType t = BlockType.Half; t < BlockType.RoundSlope; t++) types.Add(t);
        foreach (BlockType t in types)
            foreach (Dir f in Dirs.All)
                foreach (Role r in new[] { Role.Wall, Role.Roof, Role.Floor })
                    foreach (Material mat in new[] { Material.Concrete, Material.Timber, Material.Steel, Material.Ceramic, Material.Rock })
                    {
                        var g = new BlockGrid(new Int3(-1, -1, -1), new Int3(3, 3, 3)) { Look = look };
                        g.Set(new Int3(0, 0, 0), new Block(t, mat, new Rgb(0xb0, 0xb0, 0xa8), r, f));
                        string key = $"{t}/{r}/{mat}";
                        var one = new Dictionary<string, int>();
                        Untextured(Mesher.Build(g, new CellMask(g.Min, g.Size), new MeshOptions { Rock = false, Standoff = false, Collision = false }), (c, n) => key, one, ref tris);
                        foreach (var (k, v) in one) bad[k] = bad.GetValueOrDefault(k) + v;
                    }
        int single = bad.Values.Sum();
        Check(single == 0, $"every face of every block type and shape, in every direction, is textured and framed ({single} of {tris} triangles are not: {string.Join("; ", bad.OrderByDescending(p => p.Value).Take(25).Select(p => $"{p.Key} {p.Value}"))}) (Faces)");

        var rand = new Random();
        int placeTris = 0;
        var inPlaces = new Dictionary<string, int>();
        foreach (FamilyDef f in _lib.Families.OrderBy(_ => rand.Next()).Take(30))
        {
            PoiInstance inst;
            try { inst = PoiFactory.Generate(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (PoiState)rand.Next(4), (ulong)rand.NextInt64()); }
            catch (InvalidOperationException) { continue; }
            BlockGrid g = inst.Blocks;
            MeshOptions near = MeshOptions.OnPlanet(g.Min);
            near.Collision = false;
            Untextured(Mesher.Build(g, inst.Layout.Occupied, near), (c, n) =>
            {
                Block at = g.Get(new Int3((int)MathF.Floor(c.X - n.X * 0.1f), (int)MathF.Floor(c.Y - n.Y * 0.1f), (int)MathF.Floor(c.Z - n.Z * 0.1f)));
                return at.IsAir || at.Material is Material.Plant or Material.Bloom or Material.Flesh or Material.DeadLumen or Material.Lumen or Material.Water or Material.Glass
                    ? "growth" : $"{at.Type}/{at.Role}/{at.Material}";
            }, inPlaces, ref placeTris);
        }
        int inP = inPlaces.Where(p => !p.Key.Contains("growth")).Sum(p => p.Value);
        Check(inP == 0, $"every face of fresh places of every family is textured ({inP} of {placeTris} triangles are not: {string.Join("; ", inPlaces.Where(p => !p.Key.Contains("growth")).OrderByDescending(p => p.Value).Take(25).Select(p => $"{p.Key} {p.Value}"))}) (Faces)");
    }
}
