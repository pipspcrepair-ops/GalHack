using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// The capital hulks (src/Gen/Arch/CapitalHull.cs) on fresh seeds, every crash variant: the hull's length
/// and beam in the chosen range, three decks and the tower's two, the thruster banks at the stern, the
/// bridge with its chairs and consoles behind glass, every room walked to from a way in without a jump and
/// every door opening onto floor both sides, and each way of coming down a pose of its own.
/// </summary>
public partial class PoiTest
{
    void Hulks()
    {
        FamilyDef fam = _lib.Families.First(f => f.Id == "crashed_freighter");
        int bad = 0, built = 0;
        var problems = new List<string>();
        var poses = new List<(string Name, double[] Keel, int Gaps)>();
        int told = 0;
        Architect.Rejected = (layout, why) =>
        {
            if (told++ > 3) return;
            GD.Print("      rejected: " + string.Join("; ", why.Take(2)));
            var nav = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
            foreach (var (p, b) in layout.Blocks.Blocks())
            {
                if (!BlockInfo.IsSlope(b.Type) || !layout.Occupied[p + Int3.Up] && p.Y >= 0) continue;
                if (nav.Solid(p + Int3.Up) || nav.Solid(p + Int3.Up * 2) || nav.Solid(p + Int3.Up * 3))
                {
                    GD.Print($"        slope {p} {b} facing {b.Facing}; above: {string.Join(", ", Enumerable.Range(1, 3).Select(i => $"{layout.Blocks.Get(p + Int3.Up * i)} occ {layout.Occupied[p + Int3.Up * i]}"))}");
                    break;
                }
            }
            var reach = nav.Reachable(layout.EntranceCell);
            foreach (var m in layout.Modules.Where(m => !m.Cells.Any(reach.Contains)).Take(4)) GD.Print($"        unreachable {m.Purpose} storey {m.Storey} {m.Min} to {m.Max}");
            foreach (var l in layout.Extras.Levels) GD.Print($"        level {l.Name} walk {l.WalkY} reached {reach.Any(c => c.Y == l.WalkY)}");
        };
        foreach (VariantDef v in fam.Variants)
            for (int k = 0; k < 2; k++)
            {
                ulong seed = (ulong)Random.Shared.NextInt64();
                Layout layout;
                try { layout = PoiFactory.BuildLayout(_lib, fam, v.Index, fam.Rarity, seed, new LayoutHints(Dirs.Horizontal[Random.Shared.Next(4)])); }
                catch (Exception e) { bad++; problems.Add($"{v.Name} seed {seed}: {e.Message}"); continue; }
                built++;
                BlockGrid g = layout.Blocks;
                var hull = g.Blocks().Where(b => b.Block.Role is Role.Wall or Role.Roof or Role.Window && b.Block.Type != BlockType.Lattice).Select(b => b.Cell).ToList();
                bool alongZ = hull.Max(c => c.Z) - hull.Min(c => c.Z) >= hull.Max(c => c.X) - hull.Min(c => c.X);
                int Along(Int3 c) => alongZ ? c.Z : c.X;
                int Across(Int3 c) => alongZ ? c.X : c.Z;
                var slices = hull.GroupBy(Along).ToDictionary(s => s.Key, s => s.ToList());
                int lo = slices.Keys.Min(), hi = slices.Keys.Max();
                int length = slices.Count, gaps = hi - lo + 1 - slices.Count;
                int beam = slices.Values.Max(s => s.Max(Across) - s.Min(Across) + 1);
                int decks = layout.Modules.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault).Select(m => m.Storey).Distinct().Count();
                // The engines: thrusters facing aft (most of them), all in the last part of the hull's length.
                var thrusters = g.Blocks().Where(b => b.Block.Type == BlockType.Thruster).ToList();
                Dir aft = thrusters.Count == 0 ? Dir.North : thrusters.GroupBy(t => t.Block.Facing).OrderByDescending(x => x.Count()).First().Key;
                Int3 a = aft.Vec();
                int Proj(Int3 c) => c.X * a.X + c.Z * a.Z;
                int pMin = hull.Min(Proj), pMax = hull.Max(Proj);
                var main = thrusters.Where(t => t.Block.Facing == aft).ToList();
                bool stern = main.Count >= 24 && main.All(t => Proj(t.Cell) >= pMax - 0.1 * (pMax - pMin));
                // The bridge: chairs or consoles in it, glass in front of it.
                var bridge = layout.Modules.FirstOrDefault(m => m.Purpose == "bridge");
                var bridgeCells = bridge == null ? new HashSet<Int3>() : new HashSet<Int3>(bridge.Cells);
                int seats = bridgeCells.Count(c => g.Get(c).Type is BlockType.Seat or BlockType.Terminal);
                int panes = bridgeCells.Count(c => Dirs.Horizontal.Any(h => g.Get(c + h.Vec()).Type == BlockType.Pane));
                // Walked from the way in: the layout checks (every room reached, doors in walls with floor both sides), then a spawn validated.
                var walk = LayoutCheck.Check(layout);
                var nav = new NavGrid(g, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
                int intoWalls = layout.Doorways.Count(d => d.Cells.Where(c => c.Y == d.Cells.Min(x => x.Y)).Any(c => !nav.Standable(c + d.Facing.Vec()) || !nav.Standable(c - d.Facing.Vec())));
                PoiInstance inst = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, seed + 1);
                var valid = Validator.Check(inst);
                // The pose: the keel's height in ten stretches from bow to stern.
                double[] keel = Enumerable.Range(0, 10).Select(i =>
                {
                    var part = slices.Where(s => (s.Key - lo) * 10 / Math.Max(1, hi - lo + 1) == i).SelectMany(s => s.Value).ToList();
                    return part.Count == 0 ? double.NaN : part.Min(c => c.Y);
                }).ToArray();
                if (!(alongZ ? a.Z > 0 : a.X > 0)) Array.Reverse(keel);
                if (k == 0) poses.Add((v.Name, keel, gaps));
                GD.Print($"      {v.Name}: {length} m long ({gaps} m between pieces), {beam} m beam, {decks} storeys, {thrusters.Count} thrusters ({main.Count} aft), bridge {seats} chairs and consoles, {panes} cells by glass, {layout.Modules.Count} rooms, {layout.Attempts} attempts; keel {string.Join(" ", keel.Select(x => double.IsNaN(x) ? "-" : x.ToString("F0")))}");
                void Bad(bool ok, string what) { if (!ok) { bad++; problems.Add($"{v.Name} seed {seed}: {what}"); } }
                Bad(length is >= 150 and <= 240, $"length {length} m outside 150 to 240");
                Bad(beam is >= 36 and <= 60, $"beam {beam} m outside 36 to 60");
                Bad(decks >= 3, $"only {decks} storeys");
                Bad(stern, $"thrusters not at the stern ({main.Count} aft of {thrusters.Count})");
                Bad(seats >= 2 && panes >= 6, $"bridge has {seats} chairs and consoles, {panes} cells by glass");
                Bad(walk.Count == 0, "layout: " + string.Join("; ", walk.Take(3)));
                Bad(intoWalls == 0, $"{intoWalls} doors open onto a wall");
                Bad(valid.Count == 0, "spawn: " + string.Join("; ", valid.Take(3)));
            }
        Architect.Rejected = null;
        // Each crash a pose of its own: the keel's line differs by metres, or the hull lies in pieces.
        int alike = 0;
        for (int i = 0; i < poses.Count; i++)
            for (int j = i + 1; j < poses.Count; j++)
            {
                double diff = poses[i].Keel.Zip(poses[j].Keel).Where(z => !double.IsNaN(z.First) && !double.IsNaN(z.Second)).Select(z => Math.Abs(z.First - z.Second)).DefaultIfEmpty(0).Max();
                if (diff < 3 && poses[i].Gaps == poses[j].Gaps) { alike++; problems.Add($"{poses[i].Name} and {poses[j].Name} lie alike (keels within {diff:F0} m)"); }
            }
        foreach (string p in problems.Take(12)) GD.PrintErr("      " + p);
        Check(bad == 0 && alike == 0 && built == fam.Variants.Count * 2, $"capital hulks: {built} built on fresh seeds, sizes in range, thrusters at the stern, bridges glazed and crewed, every room walked to, {poses.Count} poses all different");
    }
}
