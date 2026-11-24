using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>How far apart in depth two panes of one window may sit and still be one plane, m (a standoff is 3 mm).</summary>
    const float PaneDepthTolerance = 0.01f;

    sealed class PaneStats
    {
        public int Places, Panes, Sheets, Unknown, Wrong, Windows, BadWindows, Flipped, Instances;
        public string Example = "";
        public void Add(PaneStats o)
        {
            Places += o.Places; Panes += o.Panes; Sheets += o.Sheets; Unknown += o.Unknown; Wrong += o.Wrong;
            Windows += o.Windows; BadWindows += o.BadWindows; Flipped += o.Flipped; Instances += o.Instances;
            if (Example == "") Example = o.Example;
        }
    }

    /// <summary>A pane that is a window (or a glass wall, or a balcony's glass): what a person looks through.</summary>
    static bool GlassPane(Block b) => b.Type == BlockType.Pane && b.Material == PoiGenerator.Gen.Material.Glass && b.Role is Role.Window or Role.Wall or Role.Decor;

    /// <summary>Panes of one window: the openings and glass walls, not a balcony's glass.</summary>
    static bool WindowPane(Block b) => GlassPane(b) && b.Role is Role.Window or Role.Wall;

    /// <summary>
    /// The way the wall round a pane runs, read from the cells beside it and nothing the mesher decides: 0 along x, 1 along z,
    /// -1 when the cells say nothing either way. A pane is held at the ends of its sheet by the wall (or a post) it is set in,
    /// while across it there is open air or the room, so the axis with both neighbours standing is the run; where both stand
    /// (a partition meets the wall, a wall two cells thick) or neither does, the axis with the longer line of solids through it is.
    /// </summary>
    static int WallRun(BlockGrid g, Int3 p)
    {
        bool Held(Dir a, Dir b) => !g.Get(p + a.Vec()).IsAir && !g.Get(p + b.Vec()).IsAir;
        bool hx = Held(Dir.East, Dir.West), hz = Held(Dir.North, Dir.South);
        if (hx != hz) return hx ? 0 : 1;
        int Reach(Dir d)
        {
            int n = 0;
            while (n < 24 && !g.Get(p + d.Vec() * (n + 1)).IsAir) n++;
            return n;
        }
        int rx = Reach(Dir.East) + Reach(Dir.West), rz = Reach(Dir.North) + Reach(Dir.South);
        return rx == rz ? -1 : rx > rz ? 0 : 1;
    }

    /// <summary>The sheet the mesher draws for each pane cell, from its shapes: which way it runs (0 along x, 1 along z) and how deep it sits across the wall.</summary>
    static Dictionary<Int3, (int Axis, float Depth)> PaneSheets(BlockGrid g)
    {
        var sheets = new Dictionary<Int3, (int, float)>();
        foreach (var (p, b) in g.Blocks())
        {
            if (!GlassPane(b)) continue;
            var boxes = Shapes.For(b, p, g);
            if (boxes.Count == 0) continue;
            ShapeBox bx = boxes[0];
            bool alongX = bx.Hi.X - bx.Lo.X > bx.Hi.Z - bx.Lo.Z;
            sheets[p] = alongX ? (0, p.Z + (bx.Lo.Z + bx.Hi.Z) / 2f) : (1, p.X + (bx.Lo.X + bx.Hi.X) / 2f);
        }
        return sheets;
    }

    /// <summary>The same read off the finished mesh, standoff and all: every pane's big faces, front and back, by the cell that drew them.</summary>
    static Dictionary<Int3, (int Axis, float Depth)> MeshedSheets(BlockGrid g, CellMask occupied)
    {
        var sum = new Dictionary<Int3, (int Axis, float Sum, int N)>();
        foreach (ChunkMesh chunk in Mesher.Build(g, occupied, new MeshOptions { Rock = false, Collision = false }))
        {
            MeshBuffer m = chunk.Surfaces[(int)Surface.Glass];
            for (int t = 0; t + 2 < m.Indices.Count; t += 3)
            {
                int i0 = m.Indices[t], i1 = m.Indices[t + 1], i2 = m.Indices[t + 2];
                System.Numerics.Vector3 P(int i) => new(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
                System.Numerics.Vector3 a = P(i0), b = P(i1), c = P(i2);
                System.Numerics.Vector3 cross = System.Numerics.Vector3.Cross(b - a, c - a);
                if (cross.Length() * 0.5f < 0.25f) continue; // the sheet's ends and edges are slivers; its faces are half a square metre a triangle
                System.Numerics.Vector3 n = System.Numerics.Vector3.Normalize(cross);
                if (MathF.Abs(n.Y) > 0.5f) continue;
                System.Numerics.Vector3 inside = (a + b + c) / 3f - n * 0.01f;
                var cell = new Int3((int)MathF.Floor(inside.X), (int)MathF.Floor(inside.Y), (int)MathF.Floor(inside.Z));
                if (!GlassPane(g.Get(cell))) continue;
                bool facesX = MathF.Abs(n.X) > MathF.Abs(n.Z);
                float plane = facesX ? a.X : a.Z;
                var s = sum.TryGetValue(cell, out var held) ? held : (Axis: facesX ? 1 : 0, Sum: 0f, N: 0);
                sum[cell] = (s.Axis, s.Sum + plane, s.N + 1);
            }
        }
        return sum.ToDictionary(kv => kv.Key, kv => (kv.Value.Axis, kv.Value.Sum / kv.Value.N));
    }

    /// <summary>Judges every pane of a grid against the wall round it, and every window against itself.</summary>
    static PaneStats JudgePanes(BlockGrid g, Dictionary<Int3, (int Axis, float Depth)> sheets, string who)
    {
        var s = new PaneStats { Places = 1 };
        // A pane with no air beside it at all (set in a retaining wall, a podium, the core of a tower) is seen from nowhere: it is left out.
        var cells = g.Blocks().Where(x => GlassPane(x.Block) && Dirs.Horizontal.Any(d => g.Get(x.Cell + d.Vec()).IsAir)).Select(x => x.Cell).ToList();
        var run = new Dictionary<Int3, int>();
        foreach (Int3 c in cells)
        {
            run[c] = WallRun(g, c);
            s.Panes++;
            if (run[c] < 0) s.Unknown++;
            if (!sheets.TryGetValue(c, out var sh)) continue;
            s.Sheets++;
            if (run[c] >= 0 && sh.Axis != run[c])
            {
                s.Wrong++;
                if (s.Example == "") s.Example = $"{who}: the pane at {c} is drawn along {(sh.Axis == 0 ? "x" : "z")} in a wall that runs along {(run[c] == 0 ? "x" : "z")}";
            }
        }
        // A window: panes that touch, in a wall that runs one way (a corner is where two windows meet, not one window turning).
        var window = cells.Where(c => WindowPane(g.Get(c)) && run[c] >= 0).ToList();
        var parent = window.ToDictionary(c => c, c => c);
        Int3 Root(Int3 c) { while (parent[c] != c) c = parent[c] = parent[parent[c]]; return c; }
        foreach (Int3 c in window)
            foreach (Int3 d in new[] { new Int3(1, 0, 0), new Int3(0, 1, 0), new Int3(0, 0, 1) })
                if (parent.ContainsKey(c + d) && run[c + d] == run[c]) parent[Root(c)] = Root(c + d);
        foreach (var group in window.GroupBy(Root).Where(x => x.Count() >= 2))
        {
            var drawn = group.Where(sheets.ContainsKey).Select(c => sheets[c]).ToList();
            if (drawn.Count < 2) continue;
            s.Windows++;
            bool mixed = drawn.Select(d => d.Axis).Distinct().Count() > 1;
            bool deep = drawn.Max(d => d.Depth) - drawn.Min(d => d.Depth) > PaneDepthTolerance;
            if (!mixed && !deep) continue;
            s.BadWindows++;
            if (s.Example == "") s.Example = $"{who}: a window of {group.Count()} panes at {group.First()} {(mixed ? "turns" : $"spans {drawn.Max(d => d.Depth) - drawn.Min(d => d.Depth):F2} m of depth")}" + "\n" + PaneSlice(g, group.First(), sheets);
        }
        return s;
    }

    /// <summary>A level of a grid round a cell as text, for a failure to show: a drawn pane is - (its sheet runs along x) or | (along z), a pane behind another +, other solids x, air a dot; the cell is *.</summary>
    static string PaneSlice(BlockGrid g, Int3 at, Dictionary<Int3, (int Axis, float Depth)> sheets)
    {
        var rows = new List<string>();
        for (int z = at.Z - 6; z <= at.Z + 6; z++)
        {
            var row = new System.Text.StringBuilder();
            for (int x = at.X - 12; x <= at.X + 12; x++)
            {
                var c = new Int3(x, at.Y, z);
                Block b = g.Get(c);
                row.Append(c == at ? '*' : b.IsAir ? '.' : b.Type != BlockType.Pane ? 'x' : sheets.TryGetValue(c, out var sh) ? (sh.Axis == 0 ? '-' : '|') : '+');
            }
            rows.Add("        " + row);
        }
        return string.Join("\n", rows);
    }

    static CityLot RollLot(string kind, Rng rng)
    {
        float fall = rng.Float();
        int Step() => rng.Chance(0.4f) ? rng.Range(1, 6) : 0;
        return kind switch
        {
            "tower" => new CityLot(kind, rng.Range(17, 19), 17, rng.Range(2, 31), rng.Range(0, 1000), fall, Lean: rng.Chance(0.3f), Toppled: rng.Chance(0.2f)),
            "midrise" => new CityLot(kind, rng.Range(12, 16), 17, rng.Range(2, 9), rng.Range(0, 1000), fall, Lean: rng.Chance(0.2f)),
            "terrace" => new CityLot(kind, new[] { 9, 10, 13, 14, 17 }[rng.Range(0, 5)], 17, rng.Range(1, 4), rng.Range(0, 1000), fall, Back: Step(), Left: Step(), Right: Step(), EndRight: rng.Chance(0.3f)),
            "house" => new CityLot(kind, rng.Range(10, 13), 17, rng.Range(1, 3), rng.Range(0, 1000), fall, Back: Step(), Left: Step(), Right: Step(), EndRight: rng.Chance(0.3f)),
            "works" => new CityLot(kind, rng.Range(17, 21), 17, 1, rng.Range(0, 1000), fall),
            "police" or "fire" => new CityLot(kind, rng.Range(15, 21), 17, 2, rng.Range(0, 1000), fall, Back: Step(), Left: Step(), Right: Step(), EndRight: rng.Chance(0.3f)),
            _ => new CityLot(kind, 34, 34, 1, rng.Range(0, 1000), fall),
        };
    }

    /// <summary>
    /// Every pane lies in its wall's plane and inside its frame, and every window is one plane: on fresh places of every
    /// family (and city towers, midrises and terraces on lots of every facing), the sheet the mesher draws for each glass pane
    /// runs the way the wall round it runs (the cells either side of it along the sheet stand, the cells across it don't);
    /// the panes of a window that is several cells wide, tall or deep all sit in one plane, at one depth within
    /// <see cref="PaneDepthTolerance"/>; a pane keeps its plane when the years take the wall from round it; and the finished
    /// mesh, standoff and all, draws the sheets the blocks ask for. Counted by family; the counts must be zero.
    /// </summary>
    void Panes()
    {
        var rand = new Random();
        var rng = new Rng((ulong)rand.NextInt64());
        var byFamily = new SortedDictionary<string, PaneStats>(StringComparer.Ordinal);
        var meshed = new PaneStats();
        int meshedPlaces = 0, meshOff = 0, skipped = 0;
        string meshExample = "";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (FamilyDef f in _lib.Families)
        {
            bool city = f.Id.StartsWith("city_");
            string kind = city ? f.Id["city_".Length..] : "";
            int n = kind is "tower" or "midrise" or "terrace" ? 14 : city ? 5 : 2;
            if (f.Tags.Contains("grand") && !city) n = 1;
            for (int k = 0; k < n; k++)
            {
                Layout layout;
                try
                {
                    int variant = rand.Next(f.Variants.Count);
                    ulong seed = (ulong)rand.NextInt64();
                    layout = city
                        ? PoiFactory.BuildLayout(_lib, f, variant, f.Rarity, seed, new LayoutHints(Dirs.Horizontal[rand.Next(4)], (TechLevel)rand.Next(3), RollLot(kind, rng)))
                        : PoiFactory.BuildLayout(_lib, f, variant, f.Rarity, seed);
                }
                catch (Exception) { skipped++; continue; } // a roll the family won't lay out is another check's business
                var sheets = PaneSheets(layout.Blocks);
                PaneStats s = JudgePanes(layout.Blocks, sheets, $"{f.Id}");
                // Left to the years: the same place spawned in a state it rolls, its surviving panes against the layout's.
                var states = Enum.GetValues<PoiState>().Where(st => f.StateWeights[(int)st] > 0f).ToList();
                if (s.Panes > 0 && states.Count > 0 && k == 0)
                    try
                    {
                        PoiInstance inst = PoiFactory.Spawn(_lib, layout, states[rand.Next(states.Count)], (ulong)rand.NextInt64());
                        var after = PaneSheets(inst.Blocks);
                        s.Instances++;
                        foreach (var (c, was) in sheets)
                            if (after.TryGetValue(c, out var now) && now.Axis != was.Axis)
                            {
                                s.Flipped++;
                                if (s.Example == "") s.Example = $"{f.Id} {inst.State}: the pane at {c} turned from {(was.Axis == 0 ? "x" : "z")} to {(now.Axis == 0 ? "x" : "z")} when its wall fell";
                            }
                    }
                    catch (Exception) { }
                // The mesh itself, on a few: the sheets it draws are the sheets the blocks ask for, the windows still planes.
                if (s.Sheets > 0 && (kind is "tower" or "midrise" or "terrace" && k < 2 || !city && k == 0 && rand.Next(4) == 0))
                {
                    var drawn = MeshedSheets(layout.Blocks, layout.Occupied);
                    meshedPlaces++;
                    foreach (var (c, want) in sheets)
                        if (!drawn.TryGetValue(c, out var got) || got.Axis != want.Axis || MathF.Abs(got.Depth - want.Depth) > PaneDepthTolerance)
                        {
                            meshOff++;
                            if (meshExample == "") meshExample = $"{f.Id}: the pane at {c} is {(drawn.ContainsKey(c) ? "drawn at " + drawn[c] : "not drawn")}, the block asks for {want}";
                        }
                    meshed.Add(JudgePanes(layout.Blocks, drawn, f.Id));
                }
                if (!byFamily.TryGetValue(f.Id, out PaneStats total)) byFamily[f.Id] = total = new PaneStats();
                total.Add(s);
            }
        }
        var all = new PaneStats();
        foreach (var s in byFamily.Values) all.Add(s);
        GD.Print($"    {all.Places} places of {byFamily.Count} families, {all.Panes:N0} glass panes ({all.Sheets:N0} drawn, {all.Unknown:N0} in a wall that says nothing either way), {all.Windows:N0} windows of two or more panes ({clock.ElapsedMilliseconds} ms, {skipped} rolls skipped)");
        foreach (var (id, s) in byFamily.Where(kv => kv.Value.Wrong > 0 || kv.Value.BadWindows > 0 || kv.Value.Flipped > 0).OrderByDescending(kv => kv.Value.Wrong + kv.Value.BadWindows))
            GD.Print($"      {id,-28} {s.Wrong,6} panes across their wall of {s.Sheets,6}, {s.BadWindows,5} windows out of true of {s.Windows,5}, {s.Flipped,4} turned by the years");
        foreach (string id in new[] { "city_tower", "city_midrise", "city_terrace" })
            if (byFamily.TryGetValue(id, out PaneStats c)) GD.Print($"      {id,-28} {c.Wrong,6} wrong of {c.Sheets,6} panes drawn, {c.BadWindows,5} windows out of true of {c.Windows,5}");
        if (all.Example != "") GD.Print("      e.g. " + all.Example);
        Check(all.Sheets >= 3000 && new[] { "city_tower", "city_midrise", "city_terrace" }.All(id => byFamily.TryGetValue(id, out PaneStats c) && c.Sheets > 100),
            $"enough panes to judge: {all.Sheets:N0} drawn, towers {byFamily.GetValueOrDefault("city_tower")?.Sheets}, midrises {byFamily.GetValueOrDefault("city_midrise")?.Sheets}, terraces {byFamily.GetValueOrDefault("city_terrace")?.Sheets}");
        Check(all.Wrong == 0, $"every pane lies in its wall's plane, inside its frame: {all.Wrong} of {all.Sheets:N0} drawn across their wall, in {byFamily.Count(kv => kv.Value.Wrong > 0)} of {byFamily.Count} families");
        Check(all.BadWindows == 0, $"every window of two or more panes is one plane, within {PaneDepthTolerance * 100f:F0} cm of depth and facing one way: {all.BadWindows} of {all.Windows:N0} are not, in {byFamily.Count(kv => kv.Value.BadWindows > 0)} families");
        Check(all.Flipped == 0, $"a pane keeps its plane when its wall falls: {all.Flipped} turned across {all.Instances} places spawned in a state");
        Check(meshOff == 0 && meshed.Wrong == 0 && meshed.BadWindows == 0, $"the finished mesh draws the sheets the blocks ask for, in true: {meshOff} panes off, {meshed.Wrong} across their wall, {meshed.BadWindows} of {meshed.Windows} windows out of true, in {meshedPlaces} meshed places {meshExample} {meshed.Example}");
    }
}
