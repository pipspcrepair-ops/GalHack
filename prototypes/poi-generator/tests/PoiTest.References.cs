using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>The reference ranges (data/references.txt): rooms' areas, corridors, storeys and least footprints.</summary>
    sealed class References
    {
        public readonly Dictionary<string, (float Min, float Max)> Rooms = new();
        public readonly Dictionary<string, (float Min, float Max)> Corridors = new();
        public readonly Dictionary<string, (float W, float D)> Footprints = new();
        public readonly Dictionary<string, (float Min, float Max)> Clear = new();
        /// <summary>Shape shares by manner and kind ("low pitched"), each class's band in %.</summary>
        public readonly Dictionary<string, Dictionary<string, (float Min, float Max)>> Shapes = new();
        public (float Min, float Max) Corridor = (2, 4);
        public (float Pitch, float Min, float Max) Storey = (4, 3, 4);

        public static References Load()
        {
            var r = new References();
            foreach (string raw in System.IO.File.ReadAllLines(ProjectSettings.GlobalizePath("res://data/references.txt")))
            {
                string line = raw.Split('#')[0].Trim();
                if (line.Length == 0) continue;
                string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                float F(int i) => float.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture);
                switch (p[0])
                {
                    case "room": r.Rooms[p[1]] = (F(2), F(3)); break;
                    case "corridor": r.Corridor = (F(1), F(2)); break;
                    case "storey": r.Storey = (F(1), F(2), F(3)); break;
                    case "footprint": r.Footprints[p[1]] = (F(2), F(3)); break;
                    case "clear": r.Clear[p[1]] = (F(2), F(3)); break;
                    case "shapes":
                        var bands = r.Shapes[$"{p[1]} {p[2]}"] = new();
                        for (int i = 3; i + 2 < p.Length; i += 3) bands[p[i]] = (F(i + 1), F(i + 2));
                        break;
                    default:
                        if (p[0].StartsWith("corridor.")) r.Corridors[p[0]["corridor.".Length..]] = (F(1), F(2));
                        break;
                }
            }
            return r;
        }
    }

    /// <summary>One tech level's tallies for the place-patterns checklist (docs/place-patterns.md section 6).</summary>
    sealed class TechTally
    {
        public readonly List<int> Areas = new(), Spans = new();
        public long RoomFloor, CorridorFloor, Dressing, Containers, Lights;
        public int Places;
    }

    /// <summary>
    /// The place-patterns checklist (docs/place-patterns.md, section 6) by tech level, on fresh spawned places: room areas
    /// and narrowest spans (G3, G4), corridors' share of the floor (G6), shape shares (G9), dressing, containers and lights
    /// per m2 of room floor (G10 to G12). Printed for every level; held to the narrowest span (3 m and over for 9 rooms
    /// in 10), a corridor share of 5 to 45%, and the shape shares of each manner and kind of place within 5 points of
    /// the references' bands (rule P6), counted over the blocks a place shows (<see cref="ShapeKind"/>).
    /// </summary>
    void PatternChecklist()
    {
        var rand = new Random();
        var tallies = new Dictionary<TechLevel, TechTally>();
        var shapes = new Dictionary<string, (int Places, long Seen, long[] By)>();
        foreach (FamilyDef f in _lib.Families.Where(f => !f.Tags.Contains("city") && !f.Space))
            for (int i = 0; i < (f.Tags.Contains("grand") ? 1 : 2); i++)
            {
                TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
                PoiInstance inst;
                try
                {
                    Layout layout = PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech), out _);
                    if (layout == null) continue;
                    inst = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, (ulong)rand.NextInt64());
                }
                catch (Exception) { continue; }
                if (!tallies.TryGetValue(tech, out var t)) tallies[tech] = t = new TechTally();
                t.Places++;
                var nav = inst.Nav();
                foreach (PlacedModule m in inst.Layout.Modules)
                {
                    // Its floor layer, furniture and all (a cell with a table in it is still floor).
                    var floor = m.Cells.Where(c => c.Y == m.Min.Y && Settle.Holds(inst.Blocks, nav, c + Int3.Down)).ToList();
                    if (floor.Count == 0) continue;
                    if (m.Kind == ModuleKind.Corridor) { t.CorridorFloor += floor.Count; continue; }
                    if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault)) continue;
                    t.RoomFloor += floor.Count;
                    t.Areas.Add(floor.Count);
                    var set = floor.ToHashSet();
                    var across = floor.Select(c => PlaceAudit.Span(set, c)).OrderBy(w => w).ToList();
                    t.Spans.Add(across[across.Count / 2]);
                    foreach (Int3 c in floor)
                    {
                        Block b = inst.Blocks.Get(c);
                        if (b.IsAir || BlockInfo.IsFinish(b.Type)) continue;
                        if (b.Type is BlockType.Cache or BlockType.Crate or BlockType.Locker or BlockType.Shelf) t.Containers++;
                        if (b.Role is Role.Prop or Role.Debris or Role.Loot or Role.Story && BlockInfo.Collides(b.Type)) t.Dressing++;
                    }
                }
                t.Lights += inst.Lights.Count;
                string kind = ShapeKind(inst);
                var (places, seen, by) = shapes.TryGetValue(kind, out var s) ? s : (0, 0L, new long[Enum.GetValues<ShapeClass>().Length]);
                seen += CountShapes(inst, by);
                shapes[kind] = (places + 1, seen, by);
            }        bool spansOk = true, shareOk = true;
        foreach (var (tech, t) in tallies.OrderBy(k => k.Key))
        {
            t.Areas.Sort();
            float roomFloor = Math.Max(1, t.RoomFloor);
            float corridorShare = t.CorridorFloor / (float)Math.Max(1, t.CorridorFloor + t.RoomFloor);
            int wide = t.Spans.Count(w => w >= 3);
            GD.Print($"    {tech} tech, {t.Places} places, {t.Areas.Count} rooms: area p10 {t.Areas[t.Areas.Count / 10]} median {t.Areas[t.Areas.Count / 2]} p90 {t.Areas[t.Areas.Count * 9 / 10]} m2 (G3), " +
                $"narrowest span 3 m or more {wide * 100 / Math.Max(1, t.Spans.Count)}% (G4), corridors {corridorShare:P0} of the floor (G6), " +
                $"dressing {t.Dressing / roomFloor:0.00} and containers {t.Containers * 100f / roomFloor:0.0} per 100 m2, a light per {roomFloor / Math.Max(1, t.Lights):0} m2 (G10-G12)");
            spansOk &= wide * 10 >= t.Spans.Count * 9;
            shareOk &= corridorShare is >= 0.05f and <= 0.45f;
        }
        Check(spansOk, "rooms are at least 3 m across at their narrowest, 9 in 10 at every tech level (G4)");
        Check(shareOk, "corridors take 5 to 45% of the floor at every tech level (G6)");

        foreach (var kv in Shaping.Why.OrderByDescending(k => k.Value).Take(45)) GD.Print($"      why {kv.Key} {kv.Value}");
        // Shape shares (G9) by manner and kind, judged where enough places of a kind were drawn.
        var refs = References.Load();
        var off = new List<string>();
        foreach (var (kind, (places, seen, by)) in shapes.OrderBy(k => k.Key))
        {
            float Pc(params ShapeClass[] cs) => cs.Sum(c => by[(int)c]) * 100f / Math.Max(1, seen);
            var share = new Dictionary<string, float>
            {
                ["cube"] = Pc(ShapeClass.Cube), ["slope"] = Pc(ShapeClass.Slope), ["corner"] = Pc(ShapeClass.Corner),
                ["part"] = Pc(ShapeClass.Part, ShapeClass.Plate), ["pole"] = Pc(ShapeClass.Pole), ["round"] = Pc(ShapeClass.Round), ["window"] = Pc(ShapeClass.Window),
            };
            var bands = refs.Shapes.GetValueOrDefault(kind) ?? new();
            var wrong = bands.Where(b => share[b.Key] < b.Value.Min - 5 || share[b.Key] > b.Value.Max + 5).Select(b => $"{b.Key} {share[b.Key]:0} against {b.Value.Min:0}-{b.Value.Max:0}").ToList();
            bool judged = places >= 3;
            if (judged && wrong.Count > 0) off.Add($"{kind}: {string.Join(", ", wrong)}");
            GD.Print($"    {(!judged ? "  (few) " : wrong.Count == 0 ? "  " : "! ")}shapes {kind,-14} {places,3} places, {seen} blocks: {string.Join(" ", share.Select(k => $"{k.Key} {k.Value:0}%"))}, stair {Pc(ShapeClass.Stair):0}% rail {Pc(ShapeClass.Rail):0}% other {Pc(ShapeClass.Other):0}%");
        }
        Check(off.Count == 0, $"every manner and kind of place builds with the shapes the references give, within 5 points (G9){(off.Count > 0 ? ": " + string.Join("; ", off) : "")}");

    }

    /// <summary>
    /// Our places against the reference ranges (data/references.txt, drawn from planning practice and the two games'
    /// hand-built places as numbers only): on fresh places of every family, each room's floor area by what it is for,
    /// each corridor's clear width, and each family's footprint. Most rooms of a purpose (2 in 3) fall between 3/4 of
    /// the least area and twice the most (a 1 m grid with 1 m walls runs larger); most corridors (9 in 10) are as wide
    /// as the ranges say; and no family's median footprint is smaller than the games' own places of its kind.
    /// </summary>
    void ReferenceRanges()
    {
        References refs = References.Load();
        var rand = new Random();
        var areas = new Dictionary<string, List<int>>();
        var corridors = new List<(string Family, float Width)>();
        var footprints = new Dictionary<string, List<(int W, int D)>>();
        var glazing = new Dictionary<string, float>();
        var clears = new Dictionary<string, List<int>>();
        foreach (FamilyDef f in _lib.Families.Where(f => !f.Tags.Contains("city") && !f.Space))
        {
            int n = f.Tags.Contains("grand") ? 2 : 4;
            for (int i = 0; i < n; i++)
            {
                TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
                Layout layout;
                try { layout = PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech), out _); }
                catch (Exception) { continue; }
                if (layout == null) continue;
                var nav = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
                int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
                foreach (PlacedModule m in layout.Modules)
                {
                    var floor = m.Cells.Where(c => c.Y == m.Min.Y && nav.StandsOn(c)).ToList();
                    if (floor.Count == 0) continue;
                    x0 = Math.Min(x0, m.Min.X); x1 = Math.Max(x1, m.Max.X); z0 = Math.Min(z0, m.Min.Z); z1 = Math.Max(z1, m.Max.Z);
                    // A corridor, as planned for one (hubs, galleries, landings, catwalks and crawlways are other things).
                    if (m.Kind == ModuleKind.Corridor && m.Purpose == "corridor")
                    {
                        // Its clear width: at each floor cell the shorter of its runs across x and z, the corridor's median
                        // (an L or a tee is as wide as its arms).
                        var set = floor.ToHashSet();
                        var across = floor.Select(c => PlaceAudit.Span(set, c)).OrderBy(w => w).ToList();
                        corridors.Add((m.Max.Y < 0 ? "underground" : f.Id, across[across.Count / 2]));
                    }
                    else if (refs.Rooms.ContainsKey(m.Purpose))
                    {
                        if (PlaceAudit.RoomClear(m, layout.Blocks, nav) is int clear)
                        {
                            if (!clears.TryGetValue(m.Purpose, out var cl)) clears[m.Purpose] = cl = new List<int>();
                            cl.Add(clear);
                        }
                        if (!areas.TryGetValue(m.Purpose, out var list)) areas[m.Purpose] = list = new List<int>();
                        list.Add(floor.Count);
                    }
                }
                if (x0 == int.MaxValue) continue;
                // Its footprint is all it built on the ground, yard, field and silo too (as a game's lot is), not just its rooms.
                foreach (var (c, _) in layout.Blocks.Blocks())
                {
                    if (c.Y < 0) continue;
                    x0 = Math.Min(x0, c.X); x1 = Math.Max(x1, c.X); z0 = Math.Min(z0, c.Z); z1 = Math.Max(z1, c.Z);
                }
                // Glazing: windows against the walls they are set in, above ground (printed by family, a measure for the window rules).
                int glass = 0, wall = 0;
                foreach (var (c, b) in layout.Blocks.Blocks())
                {
                    if (c.Y < 0) continue;
                    if (b.Role == Role.Window && b.Type == BlockType.Pane) glass++;
                    else if (b.Role == Role.Wall && BlockInfo.IsFullCube(b.Type)) wall++;
                }
                if (glass + wall > 0) glazing[f.Id] = glazing.GetValueOrDefault(f.Id) + glass / (float)(glass + wall) / n;
                if (!footprints.TryGetValue(f.Id, out var fp)) footprints[f.Id] = fp = new List<(int, int)>();
                fp.Add((x1 - x0 + 1, z1 - z0 + 1));
            }
        }

        // Rooms by purpose.
        int purposes = 0, offPurposes = 0;
        foreach (var (id, list) in areas.OrderBy(k => k.Key))
        {
            var (min, max) = refs.Rooms[id];
            list.Sort();
            int inside = list.Count(a => a >= min * 0.75f && a <= max * 2f);
            // Judged where there are enough of them to say (a purpose seen a handful of times is printed, not judged).
            bool judged = list.Count >= 20, ok = !judged || inside * 3 >= list.Count * 2;
            if (judged) purposes++;
            if (!ok) offPurposes++;
            GD.Print($"    {(!judged ? "  (few) " : ok ? "  " : "! ")}{id,-14} {list.Count,4} rooms, area p10 {list[list.Count / 10]} median {list[list.Count / 2]} p90 {list[list.Count * 9 / 10]} m2 against {min:0}-{max:0} ({inside * 100 / list.Count}% in range)");
        }
        Check(offPurposes == 0, $"rooms are sized as their purpose is in the references: {purposes - offPurposes} of {purposes} purposes mostly in range");

        // Clear heights by purpose: ordinary rooms 3 to 4 m; the tall purposes are printed against theirs.
        int ordinary = 0, ordinaryIn = 0;
        foreach (var (id, list) in clears.OrderBy(k => k.Key))
        {
            var (lo, hi) = refs.Clear.TryGetValue(id, out var r) ? r : (3f, 4f);
            int inside = list.Count(h => h >= lo && h <= hi);
            if (!refs.Clear.ContainsKey(id)) { ordinary += list.Count; ordinaryIn += inside; }
            else GD.Print($"    clear height {id}: {string.Join(" ", list.GroupBy(h => h).OrderBy(g => g.Key).Select(g => $"{g.Key} m x{g.Count()}"))} against {lo:0}-{hi:0} m");
        }
        Check(ordinaryIn * 10 >= ordinary * 9, $"ordinary rooms are 3 to 4 m clear: {ordinaryIn} of {ordinary}" +
            $" (outside: {string.Join(", ", clears.Where(k => !refs.Clear.ContainsKey(k.Key)).Select(k => (k.Key, Out: k.Value.Count(h => h < 3 || h > 4))).Where(k => k.Out > 0).OrderByDescending(k => k.Out).Take(8).Select(k => $"{k.Key} {k.Out} ({string.Join(" ", clears[k.Key].Where(h => h < 3 || h > 4).GroupBy(h => h).OrderBy(g => g.Key).Select(g => $"{g.Key} m x{g.Count()}"))})"))})");

        // Corridors.
        bool Wide((string Family, float Width) c)
        {
            var (lo, hi) = refs.Corridors.FirstOrDefault(k => c.Family.Contains(k.Key)).Value is var v && v != default ? v : refs.Corridor;
            return c.Width >= lo - 0.25f && c.Width <= hi + 0.25f;
        }
        int wide = corridors.Count(Wide);
        var widths = corridors.Select(c => c.Width).OrderBy(w => w).ToList();
        if (widths.Count > 0)
            GD.Print($"    corridors: {widths.Count}, width p10 {widths[widths.Count / 10]:0.0} median {widths[widths.Count / 2]:0.0} p90 {widths[widths.Count * 9 / 10]:0.0} m; too narrow or wide: {string.Join(", ", corridors.Where(c => !Wide(c)).GroupBy(c => c.Family).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key} {g.Count()}"))}");
        Check(wide * 10 >= corridors.Count * 9, $"corridors are as wide as the references say: {wide} of {corridors.Count}");

        GD.Print($"    glazing, share of wall cells that are windows: {string.Join(", ", glazing.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value:P0}"))}");

        // Footprints: never smaller than the games' own places of the kind.
        var small = new List<string>();
        foreach (var (id, list) in footprints)
        {
            FamilyDef f = _lib.Family(id);
            foreach (var (key, (w, d)) in refs.Footprints)
            {
                if (!(id.Contains(key) || f.Tags.Contains(key))) continue;
                var areasF = list.Select(x => x.W * x.D).OrderBy(a => a).ToList();
                int median = areasF[areasF.Count / 2];
                GD.Print($"    footprint {id}: median {median} m2 against at least {w * d:0} ({key})");
                if (median < w * d) small.Add($"{id} {median} m2 < {w * d:0}");
                break;
            }
        }
        Check(small.Count == 0, $"no place is smaller than the games' own of its kind{(small.Count > 0 ? ": " + string.Join(", ", small) : "")}");
    }
}
