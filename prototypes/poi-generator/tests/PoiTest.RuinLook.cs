using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// Roadside ruins drawn whole: every drawn vertex of a spawned ruin lies within a few metres of its footprint and
    /// no higher than its tallest block (no piece floats off in the sky), nothing is left hanging, and no opaque face is
    /// drawn in bare colour (every one carries a finish for the place shader).
    /// </summary>
    void RuinLook()
    {
        var rand = new Random();
        int spawns = 0, places = 0, stray = 0, flat = 0, hanging = 0;
        var flatBy = new Dictionary<string, int>();
        string first = "";
        foreach (var f in _lib.Families)
            for (int i = 0, n = Roadside(f) ? 40 : 2; i < n; i++)
            {
                var tech = (TechLevel)rand.Next(3);
                ulong seed = (ulong)rand.NextInt64();
                var layout = PoiFactory.BuildLayout(_lib, f, rand.Next(f.Variants.Count), Rarity.Common, seed, new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech, null, new HomeHints(rand.Next(), (Climate)rand.Next(4), (float)rand.NextDouble())));
                if (layout == null) continue;
                var inst = PoiFactory.Spawn(_lib, layout, rand.Next(4) == 0 ? PoiState.Abandoned : PoiState.Ruined, (ulong)rand.NextInt64());
                var occ = layout.Occupied;
                bool roadside = Roadside(f);
                int top = roadside ? inst.Blocks.Blocks().Max(b => b.Cell.Y) + 2 : int.MaxValue;
                if (roadside) { spawns++; hanging += Structure.Floating(inst.Blocks, occ, null, true).Count; }
                places++;
                foreach (ChunkMesh m in Mesher.Build(inst.Blocks, occ, new MeshOptions { Rock = false, Collision = false }))
                    foreach (Surface s in Enum.GetValues<Surface>())
                    {
                        var buf = m.Surfaces[(int)s];
                        for (int k = 0; k < buf.VertexCount; k++)
                        {
                            float x = buf.Positions[k * 3], y = buf.Positions[k * 3 + 1], z = buf.Positions[k * 3 + 2];
                            if (roadside && (x < occ.Min.X - 4 || x > occ.Min.X + occ.Size.X + 4 || z < occ.Min.Z - 4 || z > occ.Min.Z + occ.Size.Z + 4 || y > top))
                            {
                                if (stray++ == 0) first = $"{f.Id} {inst.State}: a face at ({x:0.0}, {y:0.0}, {z:0.0}) off a footprint {occ.Min}+{occ.Size}, top {top}";
                            }
                            else if (s is Surface.Matte or Surface.Metal && buf.Codes[k * 2] == 0f)
                            {
                                // The block the face belongs to: just behind it.
                                var b = inst.Blocks.Get(new Int3((int)MathF.Floor(x - buf.Normals[k * 3] * 0.01f), (int)MathF.Floor(y - buf.Normals[k * 3 + 1] * 0.01f), (int)MathF.Floor(z - buf.Normals[k * 3 + 2] * 0.01f)));
                                string key = $"{b.Type}/{b.Material}/{b.Role}";
                                if (!flatBy.ContainsKey(key)) GD.Print($"      bare face: {key} in {f.Id} {inst.State}, layout seed {seed} at ({x:0.0}, {y:0.0}, {z:0.0})");
                                flatBy[key] = flatBy.GetValueOrDefault(key) + 1;
                                flat++;
                            }
                        }
                    }
            }
        Check(spawns > 0 && stray == 0 && hanging == 0, $"ruins are drawn whole: {spawns} spawns, {stray} vertices off their footprint or above their top, {hanging} blocks hanging{(first.Length > 0 ? $" (first: {first})" : "")} (RuinLook)");
        Check(flat == 0, $"places' faces all painted, {places} spawns of every family: {flat} vertices in bare colour{(flat > 0 ? ": " + string.Join(", ", flatBy.OrderByDescending(p => p.Value).Take(12).Select(p => p.Key + " " + p.Value)) : "")} (RuinLook)");
    }

    static bool Roadside(FamilyDef f) => f.Tags.Contains("flavour") && f.Id.StartsWith("flavour_");
}
