using System;
using System.Collections.Generic;
using System.Linq;
using PoiGenerator.Gen;
using FinishKind = PoiGenerator.Gen.Finish;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// Every face of a place is finished in something (brick, render, boards, tile, plate...), by rule from
    /// what it is made of, the part it plays, its kit and its room: no bare flat colour on a wall, floor or
    /// roof, near or far. The code a face carries is the same however the place is meshed (whole, one chunk
    /// at a time, merged from further off), so a remeshed chunk never shows a seam.
    /// </summary>
    void Surfaces()
    {
        // The data: every kit's walls, floors and roofs have a finish on every side they show.
        var missing = new List<string>();
        var look = new PlaceLook { Book = _lib.Finishes };
        foreach (Kit k in _lib.Kits.Values)
        {
            var kl = new PlaceLook { Book = _lib.Finishes, Kit = k.Finishes, KitRooms = k.KeepRooms, Alien = k.Alien };
            foreach (PaletteEntry e in k.Palette.Where(e => e.Type is BlockType.Cube or BlockType.Ramp && e.Role is Role.Wall or Role.Floor or Role.Roof && e.Material is not (Material.Glass or Material.Lumen or Material.Crystal)))
                foreach (Side s in e.Role == Role.Wall ? new[] { Side.Out, Side.In } : e.Role == Role.Floor ? new[] { Side.Floor, Side.Ceiling } : new[] { Side.Roof, Side.Ceiling })
                    foreach (Rgb c in e.Colors)
                        if (kl.Pick(new Block(e.Type, e.Material, c, e.Role), s, e.Type == BlockType.Ramp, 0).Finish == FinishKind.Plain)
                            missing.Add($"{k.Id} '{e.Symbol}' {e.Material} {e.Role} {s}");
        }
        Check(missing.Count == 0, $"every kit's walls, floors and roofs are finished on every side ({missing.Count} not: {string.Join("; ", missing.Distinct().Take(6))})");
        Check(_lib.Finishes.Rooms.Count >= 20 && _lib.Finishes.Rooms.ContainsKey("office") && _lib.Finishes.Rooms.ContainsKey("kitchen"), $"{_lib.Finishes.Rooms.Count} room purposes have finishes of their own inside");
        float big = Gen.Surfaces.Code(FinishKind.Glass, 3, 3, 3, true, true, 3, true, Gen.Surfaces.Under.Poured, 15);
        Check(big < (1 << 24) && (int)big == (int)MathF.Round(big) && Gen.Surfaces.FinishOf(big) == FinishKind.Glass, "a face's code fits a float exactly");

        // Places as spawned: their opaque faces carry a finish, the far drawings too.
        var rand = new Random();
        int faces = 0, plain = 0, mergedFaces = 0, mergedPlain = 0, farFaces = 0, farPlain = 0;
        var bare = new Dictionary<string, int>();
        var kinds = new HashSet<FinishKind>();
        var families = _lib.Families.OrderBy(_ => rand.Next()).Take(28).ToList();
        foreach (FamilyDef f in families)
        {
            PoiInstance inst;
            try { inst = PoiFactory.Generate(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (PoiState)rand.Next(4), (ulong)rand.NextInt64()); }
            catch (InvalidOperationException) { continue; }
            Check(inst.Blocks.Look != null, $"{f.Id}: a spawned place knows its look");
            BlockGrid g = inst.Blocks;
            MeshOptions near = MeshOptions.OnPlanet(g.Min);
            near.Collision = false;
            foreach (ChunkMesh m in Mesher.Build(g, inst.Layout.Occupied, near))
                for (int s = 0; s < 2; s++)
                {
                    MeshBuffer b = m.Surfaces[s];
                    Check(b.Codes.Count == b.VertexCount * 2, $"{f.Id}: every vertex has its code");
                    for (int v = 0; v + 3 < b.VertexCount; v += 4)
                    {
                        faces++;
                        FinishKind fin = Gen.Surfaces.FinishOf(b.Codes[v * 2]);
                        kinds.Add(fin);
                        if (fin != FinishKind.Plain) continue;
                        plain++;
                        // Which block left it plain: the cell the face is on.
                        var c = new Int3((int)MathF.Floor((b.Positions[v * 3] + b.Positions[(v + 2) * 3]) * 0.5f - b.Normals[v * 3] * 0.1f),
                            (int)MathF.Floor((b.Positions[v * 3 + 1] + b.Positions[(v + 2) * 3 + 1]) * 0.5f - b.Normals[v * 3 + 1] * 0.1f),
                            (int)MathF.Floor((b.Positions[v * 3 + 2] + b.Positions[(v + 2) * 3 + 2]) * 0.5f - b.Normals[v * 3 + 2] * 0.1f));
                        Block at = g.Get(c);
                        string key = at.IsAir ? "air" : $"{at.Type}/{at.Material}/{at.Role}";
                        bare[key] = bare.GetValueOrDefault(key) + 1;
                    }
                }
            MeshOptions mid = MeshOptions.OnPlanet(g.Min);
            mid.Merged = true;
            mid.Collision = false;
            mid.Detail = false;
            foreach (ChunkMesh m in Mesher.Build(g, inst.Layout.Occupied, mid))
                for (int s = 0; s < 2; s++)
                    for (int v = 0; v + 3 < m.Surfaces[s].VertexCount; v += 4)
                    {
                        mergedFaces++;
                        if (Gen.Surfaces.FinishOf(m.Surfaces[s].Codes[v * 2]) == FinishKind.Plain) mergedPlain++;
                    }
            ChunkMesh far = Mesher.Coarse(g, inst.Layout.Occupied);
            for (int s = 0; s < 2; s++)
                for (int v = 0; v + 3 < far.Surfaces[s].VertexCount; v += 4)
                {
                    farFaces++;
                    if (Gen.Surfaces.FinishOf(far.Surfaces[s].Codes[v * 2]) == FinishKind.Plain) farPlain++;
                }
        }
        string worst = string.Join(", ", bare.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key} {kv.Value}"));
        Check(plain <= faces * 0.06, $"of {faces} opaque faces in {families.Count} fresh places, {100.0 * plain / Math.Max(1, faces):F1}% are bare colour (most: {worst})");
        Check(mergedPlain <= mergedFaces * 0.06, $"from further off, {100.0 * mergedPlain / Math.Max(1, mergedFaces):F1}% of {mergedFaces} merged faces are bare");
        Check(farPlain <= farFaces * 0.1, $"far off, {100.0 * farPlain / Math.Max(1, farFaces):F1}% of {farFaces} lump faces are bare");
        Check(kinds.Count >= 12, $"{kinds.Count} finishes seen across them: {string.Join(" ", kinds.OrderBy(k => k))}");

        // One chunk meshed alone carries the same codes as the whole build gives it.
        FamilyDef town = _lib.Family("survivor_town");
        PoiInstance t = PoiFactory.Generate(_lib, town, 0, town.Rarity, PoiState.Ruined, (ulong)rand.NextInt64());
        MeshOptions o = MeshOptions.OnPlanet(t.Blocks.Min);
        o.ChunkSize = 16;
        o.Collision = false;
        List<ChunkMesh> whole = Mesher.Build(t.Blocks, t.Layout.Occupied, o);
        ChunkMesh busiest = whole.OrderByDescending(m => m.Quads).First();
        ChunkMesh alone = Mesher.BuildChunk(t.Blocks, t.Layout.Occupied, busiest.Chunk, 16, _ => o);
        static string Codes(ChunkMesh m) => string.Join(",", Enumerable.Range(0, 5).SelectMany(s => m.Surfaces[s].Codes.Where((_, i) => i % 2 == 0)).OrderBy(x => x));
        Check(alone != null && Codes(alone) == Codes(busiest), $"a chunk meshed alone has the codes the whole build gave it ({busiest.Quads} faces)");
    }
}
