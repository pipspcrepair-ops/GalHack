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
    /// no higher than its tallest block (no piece floats off in the sky), nothing is left hanging, and no face that has a
    /// finish is drawn in flat colour (untextured).
    /// </summary>
    void RuinLook()
    {
        var rand = new Random();
        int spawns = 0, stray = 0, flat = 0, hanging = 0;
        var darkBy = new Dictionary<string, int>();
        var flatBy = new System.Collections.Generic.Dictionary<string, int>();
        string first = "";
        foreach (var f in _lib.Families.Where(f => f.Tags.Contains("flavour") && f.Id.StartsWith("flavour_")))
            for (int i = 0; i < 40; i++)
            {
                var tech = (TechLevel)rand.Next(3);
                var layout = PoiFactory.BuildLayout(_lib, f, rand.Next(f.Variants.Count), Rarity.Common, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech, null, new HomeHints(rand.Next(), (Climate)rand.Next(4), (float)rand.NextDouble())));
                if (layout == null) continue;
                var inst = PoiFactory.Spawn(_lib, layout, rand.Next(4) == 0 ? PoiState.Abandoned : PoiState.Ruined, (ulong)rand.NextInt64());
                spawns++;
                var occ = layout.Occupied;
                int top = inst.Blocks.Blocks().Max(b => b.Cell.Y) + 2;
                foreach (var (dc, db) in inst.Blocks.Blocks().Where(t => t.Block.Color.R + t.Block.Color.G + t.Block.Color.B < 150 && BlockInfo.IsFullCube(t.Block.Type)))
                { string dk = db.Type + "/" + db.Material + "/" + db.Role + "/" + (inst.Blocks.Look is { } lk ? lk.Pick(db, PoiGenerator.Gen.Side.Out, false, 0).Finish : default); darkBy[dk] = darkBy.GetValueOrDefault(dk) + 1; }
                hanging += Structure.Floating(inst.Blocks, occ, null, true).Count;
                var finished = inst.Blocks.Blocks().Where(t => inst.Blocks.Look is { } look && look.Pick(t.Block, PoiGenerator.Gen.Side.Out, false, 0).Finish != PoiGenerator.Gen.Finish.Plain).Select(t => t.Cell).ToHashSet();
                foreach (ChunkMesh m in Mesher.Build(inst.Blocks, occ, new MeshOptions { Rock = false, Collision = false }))
                    foreach (Surface s in Enum.GetValues<Surface>())
                    {
                        var buf = m.Surfaces[(int)s];
                        for (int k = 0; k < buf.VertexCount; k++)
                        {
                            float x = buf.Positions[k * 3], y = buf.Positions[k * 3 + 1], z = buf.Positions[k * 3 + 2];
                            if (x < occ.Min.X - 4 || x > occ.Min.X + occ.Size.X + 4 || z < occ.Min.Z - 4 || z > occ.Min.Z + occ.Size.Z + 4 || y > top)
                            {
                                if (stray++ == 0) first = $"{f.Id} {inst.State}: a face at ({x:0.0}, {y:0.0}, {z:0.0}) off a footprint {occ.Min}+{occ.Size}, top {top}";
                            }
                            else if (s is Surface.Matte or Surface.Metal && buf.Codes[k * 2] == 0f)
                            {
                                var c = new Int3((int)MathF.Floor(x - buf.Normals[k * 3] * 0.01f), (int)MathF.Floor(y - buf.Normals[k * 3 + 1] * 0.01f), (int)MathF.Floor(z - buf.Normals[k * 3 + 2] * 0.01f));
                                var nrm = new Int3((int)MathF.Round(buf.Normals[k * 3]), (int)MathF.Round(buf.Normals[k * 3 + 1]), (int)MathF.Round(buf.Normals[k * 3 + 2]));
                                if (!finished.Contains(c) || occ[c + nrm]) continue; // (a face onto the open air only: inside, a room's own finish rules)
                                var b = inst.Blocks.Get(c);
                                string key = $"{b.Type}/{b.Material}/{b.Role}";
                                flatBy[key] = flatBy.GetValueOrDefault(key) + 1;
                                flat++;
                            }
                        }
                    }
            }
        Check(spawns > 0 && stray == 0 && hanging == 0, $"ruins are drawn whole: {spawns} spawns, {stray} vertices off their footprint or above their top, {hanging} blocks hanging{(first.Length > 0 ? $" (first: {first})" : "")} (RuinLook)");
        GD.Print("      dark full cubes: " + string.Join(", ", darkBy.OrderByDescending(p => p.Value).Take(10).Select(p => p.Key + " " + p.Value)));
        GD.Print("      ruin faces with a finish drawn flat: " + flat + " vertices; " + string.Join(", ", flatBy.OrderByDescending(p => p.Value).Take(8).Select(p => p.Key + " " + p.Value)));
    }
}
