using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using Godot;
using Slice.Items;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// A tab's nodes linked to their parents and children; on Structures, laid out in lanes, one per
/// device family. In a lane a node's next tier (else its child with the longest line) shares its row
/// one column right, so tiers read left to right; its other children start lower rows one column
/// right of it, on the first row with room and an empty column either side, so a wire only runs
/// down the gap beside its parent and no tile looks chained to a neighbour it isn't. A node whose
/// parent is in another lane heads its own line; loose ones take the first free spots.
/// </summary>
public sealed class TechLayout
{
    public sealed class Cell
    {
        public TechNode Node;
        public Cell Parent;
        public readonly List<Cell> Kids = new();
        /// <summary>Structures: its lane, and its column and row within the lane.</summary>
        public string Lane;
        public int Col, Row;
    }

    /// <summary>
    /// The Structures lanes, top to bottom, and the nodes heading each (an id, or a family of tiers
    /// by its prefix): a node is in the lane of the nearest head up its line, so a new device lands
    /// with its parent's family; the hull (the core, the base block and its plating) has the rest.
    /// </summary>
    public static readonly (string Name, string[] Heads)[] StructureLanes =
    {
        ("HULL", Array.Empty<string>()),
        ("INTERIOR", new[] { "command-chair", "door", "light", "ladder" }),
        ("POWER", new[] { "generator" }),
        ("STORAGE", new[] { "container" }),
        ("PRODUCTION", new[] { "portable-fabricator", "fabricator" }),
        ("HOVER", new[] { "hover" }),
        ("FLIGHT", new[] { "thruster" }),
        ("WARP", new[] { "warp-drive" }),
    };

    public readonly List<Cell> Cells = new();
    public readonly Dictionary<string, Cell> ById = new();
    /// <summary>Structures: the lanes in order, each with its cells and rows, and the columns the widest needs.</summary>
    public readonly List<(string Name, List<Cell> Cells, int Rows)> Lanes = new();
    public int Cols;

    public TechLayout(IEnumerable<TechNode> nodes, bool lanes = false)
    {
        foreach (TechNode n in nodes)
        {
            var c = new Cell { Node = n };
            Cells.Add(c);
            ById[n.Id] = c;
        }
        foreach (Cell c in Cells)
        {
            string parent = c.Node.Unlock.Parent;
            if (parent == null || !ById.TryGetValue(parent, out Cell pc) || pc == c) continue;
            bool loop = false;
            for (Cell x = pc; x != null; x = x.Parent) if (x == c) loop = true;
            if (loop) continue;
            c.Parent = pc;
            pc.Kids.Add(c);
        }
        if (lanes) LayLanes();
    }

    /// <summary>A tier's family: its id without the "-t2" ("fuel-tank-t2" is a fuel tank).</summary>
    static string Family(string id)
    {
        int t = id.LastIndexOf("-t", StringComparison.Ordinal);
        return t > 0 && t + 2 < id.Length && id[(t + 2)..].All(char.IsDigit) ? id[..t] : id;
    }

    static int Line(Cell c) => c.Kids.Count == 0 ? 0 : 1 + c.Kids.Sum(Line);

    public static string LaneOf(Cell c)
    {
        for (Cell x = c; x != null; x = x.Parent)
            foreach (var (name, heads) in StructureLanes)
                if (heads.Any(h => x.Node.Id == h || x.Node.Id.StartsWith(h + "-", StringComparison.Ordinal))) return name;
        return StructureLanes[0].Name;
    }

    /// <summary>A cell's children in its own lane: its next tier first, then the longest line.</summary>
    static IEnumerable<Cell> Next(Cell c) => c.Kids.Where(k => k.Lane == c.Lane)
        .OrderBy(k => Family(k.Node.Id) == Family(c.Node.Id) ? 0 : 1).ThenByDescending(Line)
        .ThenBy(k => k.Node.Level).ThenBy(k => k.Node.Name, StringComparer.Ordinal);

    void LayLanes()
    {
        foreach (Cell c in Cells) c.Lane = LaneOf(c);
        foreach (var (name, _) in StructureLanes)
        {
            var cells = Cells.Where(c => c.Lane == name).ToList();
            if (cells.Count == 0) continue;
            var taken = new HashSet<(int Row, int Col)>();
            var heads = cells.Where(c => c.Parent?.Lane != name).OrderBy(c => c.Node.Level).ThenBy(c => c.Node.Name, StringComparer.Ordinal).ToList();
            foreach (Cell h in heads.Where(h => Next(h).Any())) Place(h, 0, 0, taken);
            // Loose heads pack into rows as wide as the lane's trees, or wide enough for all of them in one.
            var loose = heads.Where(h => !Next(h).Any()).ToList();
            int width = Math.Max(taken.Count == 0 ? 0 : taken.Max(t => t.Col) + 1, 2 * loose.Count - 1);
            foreach (Cell h in loose)
            {
                int spot = 0;
                while (!Free(taken, spot / width, spot % width, 1)) spot++;
                Take(h, spot / width, spot % width, taken);
            }
            Lanes.Add((name, cells, cells.Max(c => c.Row) + 1));
            Cols = Math.Max(Cols, cells.Max(c => c.Col) + 1);
        }
    }

    /// <summary>Room for <paramref name="len"/> tiles from a column, with the columns either side empty.</summary>
    static bool Free(HashSet<(int, int)> taken, int row, int col, int len) => !Enumerable.Range(col - 1, len + 2).Any(k => taken.Contains((row, k)));

    static void Take(Cell c, int row, int col, HashSet<(int, int)> taken)
    {
        c.Row = row;
        c.Col = col;
        taken.Add((row, col));
    }

    /// <summary>A cell and its line of next tiers from a column, on the first row from <paramref name="row"/> with room; then each other child one column right of its parent, on a lower row.</summary>
    static void Place(Cell c, int col, int row, HashSet<(int, int)> taken)
    {
        var line = new List<Cell>();
        for (Cell x = c; x != null; x = Next(x).FirstOrDefault()) line.Add(x);
        while (!Free(taken, row, col, line.Count)) row++;
        for (int i = 0; i < line.Count; i++) Take(line[i], row, col + i, taken);
        foreach (Cell x in line)
            foreach (Cell k in Next(x).Skip(1)) Place(k, x.Col + 1, x.Row + 1, taken);
    }
}

/// <summary>
/// TECH: the tech tree on one screen, one tab per kind of thing (structures, tools, weapons, deco,
/// misc), built from the unlock on every item and block. Each tab is a compact grid: a column per
/// level, a tile per node with its item's icon, name, cost and state. Structures is one tree from
/// the core, shown as lanes per device family (<see cref="TechLayout"/>) with each family's tiers
/// left to right and wires to what a node opens; hover and flight are lanes side by side. Deco
/// shows one set at a time: pick a set, its pieces show. Pointing at
/// a tile marks it and its tip says what it needs and what it opens; one you can afford unlocks on a
/// click. Nothing scrolls: the tiles shrink to fit the screen.
/// </summary>
public sealed class TechSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    public string Tab = "structures";
    /// <summary>The deco set shown on the deco tab.</summary>
    public string Group;
    /// <summary>Kept for scripts that scrolled the old tree; nothing scrolls now.</summary>
    public int ColOff, RowOff;

    /// <summary>Tile sizes: the largest a tile grows to, the smallest it may shrink to, the gaps, and the lane label's width.</summary>
    public const float MaxTileW = 230, MaxTileH = 46, MinTileH = 26, Gap = 4, LaneW = 100;

    string _layoutKey;
    TechLayout _layout;

    /// <summary>Every tile laid out in the last input pass, by node (tests measure them).</summary>
    public readonly Dictionary<string, Rect2> Placed = new();
    /// <summary>The grid's area in the last input pass (tests).</summary>
    public Rect2 GridArea;

    public TechSection(Game.Game game) => _game = game;
    public Vector2 PreferredSize => new(1560, 790);

    /// <summary>What the section's picture depends on besides the pointer.</summary>
    public string Signature => $"{Tab}|{Group}";

    TechLayout LayoutFor(string tab)
    {
        if (_layout == null || _layoutKey != tab)
        {
            _layout = new TechLayout(_game.Progress.Nodes.Where(n => n.Tab == tab), lanes: tab == "structures");
            _layoutKey = tab;
        }
        return _layout;
    }

    /// <summary>Shows a node's tab (and set), so a script can click it.</summary>
    public void Reveal(string id)
    {
        TechNode n = _game.Progress.Node(id);
        if (n == null) return;
        Tab = n.Tab;
        if (n.Group != null) Group = n.Group;
    }

    /// <summary>Text shrunk (down to <paramref name="min"/>) and then cut with an ellipsis until it fits the width.</summary>
    static (string Text, int Size) Fit(string text, int size, int min, float width, Font font)
    {
        int s = size;
        while (s > min && Paint.TextWidth(font, s, text) > width) s--;
        if (Paint.TextWidth(font, s, text) <= width) return (text, s);
        while (text.Length > 1 && Paint.TextWidth(font, s, text + "…") > width) text = text[..^1];
        return (text.TrimEnd() + "…", s);
    }

    /// <summary>A lane's colour bar and name down the left edge.</summary>
    static void LaneLabel(Gui g, string name, float x, float y, float h, Color? holo = null)
    {
        Color tone = holo ?? name switch { "HOVER" or "DECO" => C.Holo, "FLIGHT" => C.Accent, _ => C.Accent2 };
        g.Fill(new Rect2(x, y, 4, h), tone);
        var (text, size) = Fit(name, 12, 8, LaneW - 12, UiTheme.Display);
        g.TextIn(text, new Rect2(x + 8, y, LaneW - 12, Math.Min(h, 22)), tone, size, HorizontalAlignment.Left, UiTheme.Display);
    }

    /// <summary>Structures: a lane per device family, the tiers left to right, a wire from each node to what it opens in its lane.</summary>
    void LaneGrid(Gui g, Progress p, TechLayout lay, Dictionary<TechLayout.Cell, Rect2> at)
    {
        const float ColGap = 16, LaneGap = 8;
        int rows = lay.Lanes.Sum(l => l.Rows);
        float tileW = Math.Min(MaxTileW, (GridArea.Size.X - LaneW + ColGap) / lay.Cols - ColGap);
        float tileH = Math.Min(MaxTileH, (GridArea.Size.Y - LaneGap * (lay.Lanes.Count - 1) + Gap) / rows - Gap);
        float y = GridArea.Position.Y;
        foreach (var (name, cells, laneRows) in lay.Lanes)
        {
            float laneH = laneRows * (tileH + Gap) - Gap;
            LaneLabel(g, name, GridArea.Position.X, y, laneH);
            foreach (TechLayout.Cell c in cells)
                at[c] = new Rect2(GridArea.Position.X + LaneW + c.Col * (tileW + ColGap), y + c.Row * (tileH + Gap), tileW, tileH);
            y += laneH + LaneGap;
        }
        if (!g.Drawing) return;
        // Out of the parent's right edge, down the gap beside it, into the child's left edge.
        foreach (var (c, r) in at)
            if (c.Parent != null && c.Parent.Lane == c.Lane && at.TryGetValue(c.Parent, out Rect2 up))
            {
                float x = up.End.X + ColGap / 2, from = up.GetCenter().Y, to = r.GetCenter().Y;
                g.Canvas.DrawPolyline(new[] { new Vector2(up.End.X, from), new Vector2(x, from), new Vector2(x, to), new Vector2(r.Position.X, to) },
                    p.IsUnlocked(c.Node.Id) ? C.Good : C.Rule, 2f);
            }
    }

    /// <summary>The other tabs: a column per level (a crowded level spreads over several), the tab's own nodes in one lane and the chosen deco set's in another.</summary>
    void LevelGrid(Gui g, Progress p, TechLayout lay, string group, Dictionary<TechLayout.Cell, Rect2> at)
    {
        var lanes = new List<(string Name, List<TechLayout.Cell> Cells)>();
        var own = lay.Cells.Where(c => c.Node.Group == null).ToList();
        if (own.Count > 0) lanes.Add((Progress.TabName(Tab), own));
        if (group != null) lanes.Add((group.ToUpperInvariant(), lay.Cells.Where(c => c.Node.Group == group).ToList()));
        var shown = lanes.SelectMany(l => l.Cells).ToList();
        // The split that gives the most readable tiles wins (tall enough for two lines, wide enough for a name).
        var levels = shown.Select(c => c.Node.Level).Distinct().OrderBy(x => x).ToList();
        int Count(int lane, int level) => lanes[lane].Cells.Count(c => c.Node.Level == level);
        float headH = 22, laneGap = 10, top = GridArea.Position.Y, left = GridArea.Position.X;
        float rowsH = GridArea.Size.Y - headH - laneGap * (lanes.Count - 1);
        int most = Enumerable.Range(0, lanes.Count).SelectMany(li => levels.Select(l => Count(li, l))).Max();
        Dictionary<int, int> span = null;
        List<int> laneRows = null;
        float tileW = 0, tileH = 0, best = -1;
        for (int rows = 1; rows <= most; rows++)
        {
            var k = levels.ToDictionary(l => l, l => Enumerable.Range(0, lanes.Count).Max(li => (Count(li, l) + rows - 1) / rows));
            var lr = Enumerable.Range(0, lanes.Count).Select(li => Math.Max(1, levels.Max(l => (Count(li, l) + k[l] - 1) / k[l]))).ToList();
            float w = Math.Min(MaxTileW, (GridArea.Size.X - LaneW) / k.Values.Sum() - Gap), h = Math.Min(MaxTileH, rowsH / lr.Sum() - Gap);
            float score = Math.Min(h / 44f, w / 150f);
            if (score > best) { best = score; span = k; laneRows = lr; tileW = w; tileH = h; }
        }
        var colOf = new Dictionary<int, int>();
        int next = 0;
        foreach (int l in levels) { colOf[l] = next; next += span[l]; }
        float ColX(int col) => left + LaneW + col * (tileW + Gap);

        // The level headers, over each level's first column.
        foreach (int level in levels)
            g.TextIn($"LV {level}", new Rect2(ColX(colOf[level]), top, tileW, headH - 4), p.Level >= level ? C.Good : C.Dim, 13, HorizontalAlignment.Left, UiTheme.Mono);

        float y = top + headH;
        for (int li = 0; li < lanes.Count; li++)
        {
            var (name, cells) = lanes[li];
            float laneH = laneRows[li] * (tileH + Gap) - Gap;
            LaneLabel(g, name, left, y, laneH, li > 0 || own.Count == 0 ? C.Holo : null);
            foreach (var byLevel in cells.GroupBy(c => c.Node.Level))
            {
                int k = 0, rowsHere = laneRows[li];
                foreach (TechLayout.Cell c in byLevel.OrderBy(c => c.Node.Name, StringComparer.Ordinal))
                {
                    at[c] = new Rect2(ColX(colOf[c.Node.Level] + k / rowsHere), y + k % rowsHere * (tileH + Gap), tileW, tileH);
                    k++;
                }
            }
            y += laneH + laneGap;
        }
    }

    public void Build(Gui g, Rect2 area)
    {
        Progress p = _game.Progress;
        Vector2 o = area.Position;
        float tabW = Math.Min(262, (area.Size.X + 8) / Progress.Tabs.Length - 8);
        for (int i = 0; i < Progress.Tabs.Length; i++)
        {
            string t = Progress.Tabs[i];
            if (g.Tab($"tech:tab:{t}", new Rect2(o + new Vector2(i * (tabW + 8), 0), new Vector2(tabW, 36)), Progress.TabName(t), Tab == t, C.Accent2)) Tab = t;
        }
        g.Text($"LEVEL {p.Level}", o + new Vector2(0, 46), C.Accent2, 17, UiTheme.Display);
        g.Text($"{p.Points} PTS", o + new Vector2(170, 48), C.Accent2, 16, UiTheme.Mono);
        g.Text($"{p.Xp} XP", o + new Vector2(290, 48), C.Text, 16, UiTheme.Mono);
        if (p.Creative) g.Text("CREATIVE", o + new Vector2(440, 48), C.Holo, 16, UiTheme.Label);
        Placed.Clear();
        TechLayout lay = LayoutFor(Tab);
        if (lay.Cells.Count == 0) return;

        // Misc: its deco sets, a row of buttons; one set's pieces form the last lane.
        float top = o.Y + 76;
        var groups = lay.Cells.Select(c => c.Node.Group).Where(x => x != null).Distinct().ToList();
        string group = null;
        if (groups.Count > 0)
        {
            if (Group == null || !groups.Contains(Group)) Group = groups[0];
            group = Group;
            const int PerRow = 12;
            float gw = (area.Size.X - LaneW) / PerRow;
            g.TextIn("DECO", new Rect2(o.X, top, LaneW - 8, 26), C.Holo, 13, HorizontalAlignment.Left, UiTheme.Display);
            for (int i = 0; i < groups.Count; i++)
            {
                var r = new Rect2(o.X + LaneW + i % PerRow * gw, top + i / PerRow * 30, gw - 4, 26);
                if (g.Button($"tech:group:{groups[i]}", r, groups[i].ToUpperInvariant(), C.Holo, lit: groups[i] == Group, size: 11)) { Group = groups[i]; group = Group; }
            }
            top += (groups.Count + PerRow - 1) / PerRow * 30 + 6;
        }

        GridArea = new Rect2(o.X, top, area.Size.X, area.End.Y - top);
        var at = new Dictionary<TechLayout.Cell, Rect2>();
        if (lay.Lanes.Count > 0) LaneGrid(g, p, lay, at);
        else LevelGrid(g, p, lay, group, at);
        TechLayout.Cell hover = null;
        foreach (var (c, r) in at)
        {
            Placed[c.Node.Id] = r;
            if (g.Over(r)) hover = c;
        }
        // Every tile stays lit; the one pointed at is marked and its tip says what it needs and opens.
        foreach (var (c, r) in at) Tile(g, p, c, r, true, c == hover);

        if (hover != null)
        {
            TechNode n = hover.Node;
            UnlockState state = p.StateOf(n);
            var lines = new List<(string, Color)>
            {
                (n.Name, C.Accent),
                ($"LEVEL {n.Level} · {n.Cost} PT", C.Text),
                (state == UnlockState.Unlocked ? "UNLOCKED" : p.Needs(n), state == UnlockState.Unlocked ? C.Good : state == UnlockState.Affordable ? C.Accent : C.Warn),
            };
            if (hover.Parent != null) lines.Add(($"NEEDS · {hover.Parent.Node.Name}", C.Dim));
            if (hover.Kids.Count > 0) lines.Add(($"OPENS · {string.Join(", ", hover.Kids.Take(4).Select(k => k.Node.Name))}{(hover.Kids.Count > 4 ? "..." : "")}", C.Dim));
            g.SetTip(at[hover], () => lines);
        }
    }

    /// <summary>A name on up to two lines: the largest size (down to <paramref name="min"/>) at which it fits, the second line cut if it still doesn't.</summary>
    static (string One, string Two, int Size) Wrap(string text, int size, int min, float width, Font font)
    {
        string[] words = text.Split(' ');
        for (int s = size; s >= min; s--)
        {
            string one = words[0];
            int k = 1;
            while (k < words.Length && Paint.TextWidth(font, s, one + " " + words[k]) <= width) one += " " + words[k++];
            string two = string.Join(" ", words.Skip(k));
            if (Paint.TextWidth(font, s, one) <= width && Paint.TextWidth(font, s, two) <= width) return (one, two, s);
            if (s == min) return (Fit(one, s, s, width, font).Text, Fit(two, s, s, width, font).Text, s);
        }
        return (text, "", min);
    }

    /// <summary>One tile: the state's colour down its edge, the item's icon, its name (two lines when it is tall enough), and its cost or what it waits for.</summary>
    void Tile(Gui g, Progress p, TechLayout.Cell c, Rect2 r, bool lit, bool hot)
    {
        TechNode n = c.Node;
        UnlockState state = p.StateOf(n);
        Color tone = state switch { UnlockState.Unlocked => C.Good, UnlockState.Affordable => C.Accent, UnlockState.NeedsPoints => C.Accent2, _ => C.Dim };
        if (g.Button($"tech:{n.Id}", r, "", tone, enabled: state == UnlockState.Affordable, lit: false) && p.Unlock(n.Id)) g.Sound = "unlock";
        string name = n.Name;
        bool tall = r.Size.Y >= 42;
        float icon = Math.Min(r.Size.Y - 6, 40);
        float textX = r.Position.X + 9 + icon + 5, textW = r.End.X - 4 - textX;
        const float BadgeW = 24, BadgeH = 14;
        string status = state switch
        {
            UnlockState.Unlocked => "",
            UnlockState.Affordable => "UNLOCK",
            UnlockState.NeedsLevel => $"LV {n.Level}",
            UnlockState.NeedsParent => "NEEDS " + (c.Parent != null ? c.Parent.Node.Name.ToUpperInvariant() : "PARENT"),
            _ => "POINTS",
        };
        string cost = state == UnlockState.Unlocked ? "" : $"{n.Cost}";
        // Tall: the name on two lines, the status and the cost under it. Short: the name on one line, the level it waits for and the cost beside it.
        string waits = !tall && state == UnlockState.NeedsLevel ? $"LV {n.Level}" : "";
        float waitsW = waits == "" ? 0 : Paint.TextWidth(UiTheme.Mono, 10, waits) + 6, nameW = textW - BadgeW - 4 - waitsW;
        var (one, two, ns) = tall ? Wrap(name, 12, 9, textW, UiTheme.Label) : (Fit(name, 12, 9, nameW, UiTheme.Label).Text, "", Fit(name, 12, 9, nameW, UiTheme.Label).Size);
        var badge = tall ? new Rect2(r.End.X - BadgeW - 3, r.End.Y - BadgeH - 3, BadgeW, BadgeH) : new Rect2(r.End.X - BadgeW - 3, r.Position.Y + (r.Size.Y - BadgeH) / 2f, BadgeW, BadgeH);
        var line1 = new Rect2(textX, r.Position.Y + 2, tall ? textW : nameW, tall ? 14 : r.Size.Y - 4);
        var waitsAt = new Rect2(badge.Position.X - 4 - waitsW, r.Position.Y, waitsW, r.Size.Y);
        var line2 = new Rect2(textX, r.Position.Y + 15, textW, 14);
        var line3 = new Rect2(textX, r.End.Y - BadgeH - 3, badge.Position.X - 4 - textX, BadgeH);
        var (st, ss) = Fit(status, 10, 8, line3.Size.X, UiTheme.Label);
        if (!g.Drawing)
        {
            // Laid out in the input pass too, so the tests can measure it.
            g.TextIn(one, line1, C.Text, ns);
            if (tall && two != "") g.TextIn(two, line2, C.Text, ns);
            if (tall && st != "") g.TextIn(st, line3, C.Dim, ss);
            if (waits != "") g.TextIn(waits, waitsAt, C.Dim, 10, HorizontalAlignment.Right, UiTheme.Mono);
            if (cost != "") g.TextIn(cost, badge, C.Text, 11, HorizontalAlignment.Center, UiTheme.Mono);
            return;
        }
        float fade = lit ? 1f : 0.28f;
        Color F(Color col) => new(col, col.A * fade);
        g.Fill(r, F(g.Back(new Color(state == UnlockState.Unlocked ? C.Panel : C.Glass, 0.9f))));
        g.Fill(new Rect2(r.Position, new Vector2(5, r.Size.Y)), F(tone));
        if (hot || state == UnlockState.Affordable) g.Outline(r, F(hot ? C.Text : tone), hot ? 3f : 2f);
        Texture2D tex = ItemIcons.Shared?.Get(n.ItemId);
        var iconBox = new Rect2(r.Position.X + 9, r.Position.Y + (r.Size.Y - icon) / 2f, icon, icon);
        if (tex != null) g.Canvas.DrawTextureRect(tex, iconBox, false, new Color(1, 1, 1, (state == UnlockState.Unlocked || state == UnlockState.Affordable ? 1f : 0.45f) * fade));
        else g.Fill(iconBox, F(new Color(tone, 0.25f)));
        Color ink = F(state is UnlockState.Unlocked or UnlockState.Affordable ? C.Text : C.Dim);
        g.TextIn(one, line1, ink, ns);
        if (tall && two != "") g.TextIn(two, line2, ink, ns);
        if (tall && st != "") g.TextIn(st, line3, F(state == UnlockState.Affordable ? C.Accent : C.Warn), ss);
        if (waits != "") g.TextIn(waits, waitsAt, F(C.Warn), 10, HorizontalAlignment.Right, UiTheme.Mono);
        // The cost on its badge, or a tick once unlocked.
        g.Fill(badge, F(new Color(state == UnlockState.Unlocked ? C.Good : state == UnlockState.Affordable ? C.Accent : C.Rule, 0.85f)));
        if (cost != "") g.TextIn(cost, badge, F(C.Glass), 11, HorizontalAlignment.Center, UiTheme.Mono);
        else
        {
            Vector2 m = badge.GetCenter();
            g.Canvas.DrawPolyline(new[] { m + new Vector2(-5, 0), m + new Vector2(-2, 3), m + new Vector2(5, -4) }, F(C.Glass), 2f);
        }
    }}
