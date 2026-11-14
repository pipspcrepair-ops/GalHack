using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Material = PoiGenerator.Gen.Material;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// Round things read round (the owner, 14 Nov: tanks built as stepped circles of cubes). Every rim piece collides as its
    /// plan. Over fresh places of every kind, every round object as spawned: no saw-teeth (no step in an outline, no rim cell
    /// left whole or open where its round cuts it), its outline within a quarter metre of its circle at every course, inside
    /// as well where its wall is two cells thick; each ladder up one lands on a deck a body stands on. Prints a tank's blocks
    /// and triangles cut and as whole cubes.
    /// </summary>
    void RoundCheck()
    {
        // Each piece, alone: a point is inside its collision (by the triangles a ray crosses) just where it is inside its plan.
        int wrong = 0, probes = 0;
        for (BlockType t = BlockType.Wedge; t <= BlockType.RoundIn; t++)
            foreach (Dir f in Dirs.Horizontal)
            {
                var g = new BlockGrid(new Int3(-1, -1, -1), new Int3(3, 3, 3));
                var b = new Block(t, Material.Concrete, new Rgb(0x80, 0x80, 0x80), Role.Wall, f);
                g.Set(new Int3(0, 0, 0), b);
                var tris = Mesher.Build(g, new CellMask(g.Min, g.Size), new MeshOptions { Rock = false, Standoff = false }).SelectMany(m => m.Collision).ToList();
                V3[] plan = Shapes.Plan(b);
                for (int i = 0; i < 9; i++)
                    for (int k = 0; k < 9; k++)
                    {
                        float x = (i + 0.53f) / 9f, z = (k + 0.41f) / 9f; // (off every cut's line)
                        probes++;
                        if (InPlan(plan, x, z) != (Crossings(tris, new V3(x, 0.5f, z)) % 2 == 1)) wrong++;
                    }
            }
        Check(wrong == 0, $"every rim piece collides as its plan ({wrong} of {probes} probes disagree) (Round)");

        // A tank as the sites lay it, cut and as whole cubes.
        foreach (bool cut in new[] { false, true })
        {
            Round.Cut = cut;
            try
            {
                var c = new Canvas(_lib.Kits.Values.First());
                Round.Lay(c, 0.5, 0.5, 0, 9, _ => (4.4, 0), _ => new Block(BlockType.Cube, Material.Steel, new Rgb(0xa8, 0xaa, 0xa4), Role.Wall));
                var g = new BlockGrid(new Int3(-6, -1, -6), new Int3(13, 12, 13));
                foreach (var (p, blk) in c.Blocks) g.Set(p, blk);
                var mesh = Mesher.Build(g, new CellMask(g.Min, g.Size), new MeshOptions { Rock = false });
                int tris = mesh.Sum(m => m.Surfaces.Sum(s => s.Indices.Count / 3)), solid = mesh.Sum(m => m.Collision.Count / 9);
                GD.Print($"      a tank 8.8 m across and 10 high, {(cut ? "cut round" : "whole cubes")}: {g.CountBlocks()} blocks, {tris} triangles drawn, {solid} colliding");
            }
            finally { Round.Cut = true; }
        }

        var rand = new Random();
        int places = 0, bodies = 0, courses = 0, teeth = 0, opened = 0, ladders = 0, unlanded = 0, rails = 0, offEdge = 0;
        double worst = 0;
        string worstAt = "";
        var shares = new List<double>();
        foreach (FamilyDef fam in _lib.Families.Where(f => !f.Tags.Contains("city") && !f.Space))
        {
            PoiInstance inst;
            try
            {
                var techs = fam.Techs.Count > 0 ? fam.Techs.Select(WorldProfile.Parse).Distinct().ToList() : Enum.GetValues<TechLevel>().ToList();
                Layout layout = PoiFactory.SpawnLayout(_lib, fam, rand.Next(fam.Variants.Count), fam.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], techs[rand.Next(techs.Count)]), out _);
                if (layout == null || layout.Extras.Rounds.Count == 0) continue;
                inst = PoiFactory.Spawn(_lib, layout, PoiState.Active, (ulong)rand.NextInt64());
            }
            catch (Exception) { continue; }
            places++;
            BlockGrid g = inst.Blocks;
            NavGrid nav = inst.Nav();
            foreach (RoundBody body in inst.Extras.Rounds)
            {
                bodies++;
                for (int y = Math.Max(body.Y0, Assembler.GroundY + 1); y <= body.Y1; y++)
                {
                    double ro = body.Outer[y - body.Y0], ri = body.Inner[y - body.Y0];
                    if (ro < 0.75) continue;
                    courses++;
                    var want = Round.Ring(body.X, body.Z, ro, ri).Solid.ToDictionary(p => (p.X, p.Z));
                    // What stands where the course meant something: the piece meant, or not (see below).
                    var open = new HashSet<(int, int)>();
                    foreach (var ((x, z), p) in want)
                    {
                        Block b = g.Get(new Int3(x, y, z));
                        if (b.Type == p.Type && (p.Type == BlockType.Cube || b.Facing == p.Facing)) continue;
                        // (Cut back after, a slope or a half for a cube: a tooth. Anything else, a block, another round, a beam across a way cut through, nothing: an opening.)
                        double cd = Math.Sqrt((x + 0.5 - body.X) * (x + 0.5 - body.X) + (z + 0.5 - body.Z) * (z + 0.5 - body.Z));
                        bool rim = Math.Abs(cd - ro) <= 1.2 || ri > 0 && ri <= ro - 2 && Math.Abs(cd - ri) <= 1.2;
                        if (rim && b.Type is BlockType.Half or BlockType.Quarter or BlockType.Chamfer or BlockType.ChamferCorner or BlockType.ChamferInvCorner or BlockType.Ramp or BlockType.Corner or BlockType.InvCorner) teeth++;
                        else { open.Add((x, z)); opened++; }
                    }
                    bool Near((int X, int Z) c) { for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) if (open.Contains((c.X + dx, c.Z + dz))) return true; return false; } // (an opening's edges are its own, not the round's)
                    // The outline as built: every face of a cell's plan not covered by its neighbour's, and every cut.
                    foreach (var ((x, z), _) in want)
                    {
                        if (Near((x, z))) continue;
                        Block b = g.Get(new Int3(x, y, z));
                        if (Shapes.PlanOf(b) is not { } plan) continue; // (reshaped: counted above)
                        for (int i = 0; i < plan.Length; i++)
                        {
                            V3 a = plan[i], e = plan[(i + 1) % plan.Length];
                            Dir? side = Shapes.OnSide(a, e);
                            if (side is Dir d && Shapes.Covers(g.Get(new Int3(x, y, z) + d.Vec()), d.Opposite(), Shapes.Span(a, e, d))) continue;
                            double mx = x + (a.X + e.X) / 2 - body.X, mz = z + (a.Z + e.Z) / 2 - body.Z, ml = Math.Sqrt(mx * mx + mz * mz);
                            bool outerSide = ri <= 0 || ml > (ro + ri) / 2;
                            if (Math.Abs(ml - (outerSide ? ro : ri)) > 1.2) continue; // (not on an outline: a drum's rooms, planned by the block)
                            if (!outerSide && ri > ro - 2) continue; // (a wall under two cells: its inner face is the outer cut's back)
                            // (A cell's face left open running out from the middle: a step.)
                            double along = Math.Abs(((e.X - a.X) * mx + (e.Z - a.Z) * mz) / Math.Max(1e-9, Math.Sqrt((e.X - a.X) * (e.X - a.X) + (e.Z - a.Z) * (e.Z - a.Z)) * ml));
                            if (side != null && along > 0.7071) { teeth++; continue; }
                            for (int s = 0; s <= 4; s++)
                            {
                                double px = x + a.X + (e.X - a.X) * s / 4.0 - body.X, pz = z + a.Z + (e.Z - a.Z) * s / 4.0 - body.Z;
                                double off = Math.Abs(Math.Sqrt(px * px + pz * pz) - (outerSide ? ro : ri));
                                if (off > worst) { worst = off; worstAt = $"{inst.Layout.Family.Id} round at ({body.X:0.0}, {body.Z:0.0}) r {ro:0.00}/{ri:0.00} course {y}"; }
                            }
                        }
                    }
                }
            }
            // Each rail round a top runs along its outline: over a cut, or along a whole block's open face.
            foreach (var (c, b) in g.Blocks().Where(q => q.Block.Type == BlockType.Rail && q.Block.Variant == Shapes.RimRailVariant).ToList())
            {
                rails++;
                Block under = g.Get(c + Int3.Down);
                bool ok = BlockInfo.IsFullCube(under.Type) ? b.Facing.IsHorizontal() && !BlockInfo.Collides(g.Get(c + Int3.Down + b.Facing.Vec()).Type) : Shapes.PlanOf(under) != null;
                if (!ok) offEdge++;
            }
            // Each ladder up a round thing's side lands on a deck a body stands on.
            foreach (var (c, b) in g.Blocks().Where(q => q.Block.Type == BlockType.Ladder && g.Get(q.Cell + Int3.Up).Type != BlockType.Ladder).ToList())
            {
                Int3 back = c + b.Facing.Opposite().Vec();
                if (!inst.Extras.Rounds.Any(body => back.Y >= body.Y0 && back.Y <= body.Y1
                    && Math.Sqrt((back.X + 0.5 - body.X) * (back.X + 0.5 - body.X) + (back.Z + 0.5 - body.Z) * (back.Z + 0.5 - body.Z)) < body.Outer[back.Y - body.Y0] + 0.5)) continue;
                ladders++;
                if (!nav.Standable(back + Int3.Up)) unlanded++;
            }
            int all = 0, round = 0;
            foreach (var (_, b) in g.Blocks()) { if (!BlockInfo.Collides(b.Type)) continue; all++; if (BlockInfo.ShapeOf(b) == ShapeClass.Round) round++; }
            shares.Add(round / (double)Math.Max(1, all));
        }
        GD.Print($"      round: {places} places, {bodies} round things, {courses} courses; worst stray {worst:0.000} m ({worstAt}); {opened} cells of courses built over or open (doors, breaches, a drum's rooms, what joins it); ladders {ladders}; round share per place median {100 * shares.OrderBy(s => s).ElementAtOrDefault(shares.Count / 2):0.0}%, most {100 * shares.DefaultIfEmpty(0).Max():0.0}%");
        Check(places > 0 && bodies > 0 && teeth == 0 && worst <= Round.Tolerance + 1e-6,
            $"every round thing is round: no saw-teeth ({teeth}), its outline within {Round.Tolerance} m of its circle at every course ({worst:0.000} m) (Round)");
        Check(unlanded == 0, $"every ladder up a round thing lands on a deck a body stands on ({unlanded} of {ladders} don't) (Round)");
        Check(offEdge == 0, $"every rail round a round thing's top runs along its outline ({offEdge} of {rails} don't) (Round)");
    }

    static bool InPlan(V3[] plan, float x, float z)
    {
        bool inside = false;
        for (int i = 0, j = plan.Length - 1; i < plan.Length; j = i++)
            if ((plan[i].Z > z) != (plan[j].Z > z) && x < (plan[j].X - plan[i].X) * (z - plan[i].Z) / (plan[j].Z - plan[i].Z) + plan[i].X) inside = !inside;
        return inside;
    }

    /// <summary>How many of a triangle soup's triangles a ray from a point along +x crosses.</summary>
    static int Crossings(List<float> tris, V3 o)
    {
        int n = 0;
        for (int i = 0; i + 8 < tris.Count; i += 9)
        {
            V3 a = new(tris[i], tris[i + 1], tris[i + 2]), b = new(tris[i + 3], tris[i + 4], tris[i + 5]), c = new(tris[i + 6], tris[i + 7], tris[i + 8]);
            // Möller–Trumbore along (1, 0, 0).
            V3 d = new(1, 0, 0), e1 = b - a, e2 = c - a, p = V3.Cross(d, e2);
            float det = V3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-8f) continue;
            V3 t = o - a;
            float u = V3.Dot(t, p) / det;
            if (u < 0 || u > 1) continue;
            V3 q = V3.Cross(t, e1);
            float v = V3.Dot(d, q) / det;
            if (v < 0 || u + v > 1) continue;
            if (V3.Dot(e2, q) / det > 0) n++;
        }
        return n;
    }
}
