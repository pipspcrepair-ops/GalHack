using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DiegeticUi.Theme;
using Godot;
using Slice.Items;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// Where each tech node stands: a tidy tree, columns by the level a node opens at and one row for
/// every branch. A node's first child shares its row; the other children take the rows below, so a
/// branch reads left to right and a wire only ever runs in the empty row between a node and its
/// child (never behind a box). A child that opens at the same level as its parent steps one column
/// right, so the parent is always to its left. Loose nodes with no parent are packed in a grid under the trees.
/// </summary>
public sealed class TechLayout
{
    public sealed class Cell
    {
        public TechNode Node;
        public Cell Parent;
        public readonly List<Cell> Kids = new();
        /// <summary>The level it stands at (never below its parent's) and the step right within that level.</summary>
        public int El, Sub, Col, Row;
    }

    public readonly List<Cell> Cells = new();
    public readonly Dictionary<string, Cell> ById = new();
    /// <summary>The columns, left to right: a level and a step within it.</summary>
    public readonly List<(int Level, int Sub)> Columns = new();
    public int Rows;

    public TechLayout(IEnumerable<TechNode> nodes)
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
        var roots = Cells.Where(c => c.Parent == null).OrderBy(c => c.Node.Level).ThenBy(c => c.Node.Name, StringComparer.Ordinal).ToList();
        foreach (Cell r in roots) Levels(r);
        foreach (var col in Cells.Select(c => (c.El, c.Sub)).Distinct().OrderBy(x => x.El).ThenBy(x => x.Sub)) Columns.Add(col);
        foreach (Cell c in Cells) c.Col = Columns.IndexOf((c.El, c.Sub));
        int row = 0;
        foreach (Cell r in roots.Where(r => r.Kids.Count > 0)) row = Place(r, row);
        // Loose nodes: a grid in their level columns, below the trees.
        var taken = new HashSet<(int, int)>();
        foreach (Cell r in roots.Where(r => r.Kids.Count == 0))
        {
            int at = row;
            while (taken.Contains((at, r.Col))) at++;
            taken.Add((at, r.Col));
            r.Row = at;
        }
        Rows = Cells.Count == 0 ? 0 : Cells.Max(c => c.Row) + 1;
    }

    void Levels(Cell c)
    {
        c.El = c.Parent == null ? c.Node.Level : Math.Max(c.Node.Level, c.Parent.El);
        c.Sub = c.Parent != null && c.El == c.Parent.El ? c.Parent.Sub + 1 : 0;
        c.Kids.Sort((a, b) => a.Node.Level != b.Node.Level ? a.Node.Level.CompareTo(b.Node.Level) : string.CompareOrdinal(a.Node.Name, b.Node.Name));
        foreach (Cell k in c.Kids) Levels(k);
    }

    /// <summary>Gives the cell a row (its first child shares it) and returns the next row free below its branch.</summary>
    int Place(Cell c, int row)
    {
        c.Row = row;
        if (c.Kids.Count == 0) return row + 1;
        int next = row;
        foreach (Cell k in c.Kids) next = Place(k, next);
        return next;
    }

    /// <summary>Everything a node needs first: itself and its parents, up to the root.</summary>
    public HashSet<Cell> PathTo(Cell c)
    {
        var set = new HashSet<Cell>();
        for (Cell x = c; x != null; x = x.Parent) set.Add(x);
        return set;
    }

    /// <summary>Everything a node opens up: all its children and theirs.</summary>
    public HashSet<Cell> Opens(Cell c)
    {
        var set = new HashSet<Cell>();
        var todo = new Stack<Cell>(c.Kids);
        while (todo.Count > 0)
        {
            Cell x = todo.Pop();
            if (!set.Add(x)) continue;
            foreach (Cell k in x.Kids) todo.Push(k);
        }
        return set;
    }
}

/// <summary>
/// TECH: the tech tree on one screen, one tab per kind of thing (Large, Small, tools, weapons, misc),
/// built from the unlock on every item and block. Each tab is a compact grid: a column per level, a
/// tile per node with its item's icon, name, cost and state. Large and Small are split into lanes:
/// the core and what every mode shares, then hover tech and flight tech side by side as parallel
/// branches. Misc keeps the deco sets below its own nodes: pick a set, its pieces show. Pointing at
/// a tile lights what it needs and what it opens and dims the rest; one you can afford unlocks on a
/// click. Nothing scrolls: the tiles shrink to fit the screen.
/// </summary>
public sealed class TechSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    public string Tab = "large";
    /// <summary>The deco set shown on the misc tab.</summary>
    public string Group;
    /// <summary>Kept for scripts that scrolled the old tree; nothing scrolls now.</summary>
    public int ColOff, RowOff;

    /// <summary>Tile sizes: the largest a tile grows to, the smallest it may shrink to, the gaps, and the lane label's width.</summary>
    public const float MaxTileW = 230, MaxTileH = 46, MinTileH = 26, Gap = 4, LaneW = 74;

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
            _layout = new TechLayout(_game.Progress.Nodes.Where(n => n.Tab == tab));
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

    /// <summary>"Steel block (large)" to "Steel block" and its grid: L, S, or none.</summary>
    static string Plain(TechNode n, out string tag)
    {
        tag = n.Small switch { true => "S", false => "L", _ => null };
        return Regex.Replace(n.Name, @"\s*\((large|small)\)\s*$", "", RegexOptions.IgnoreCase);
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

    /// <summary>Which lane a node of Large or Small sits in: hover tech, flight tech, or the core and what every mode shares. The nearest hover plate or thruster up its line decides.</summary>
    public static string LaneOf(TechLayout.Cell c)
    {
        for (TechLayout.Cell x = c; x != null; x = x.Parent)
        {
            string block = x.Node.BlockId ?? x.Node.ItemId ?? "";
            if (block.StartsWith("hover", StringComparison.Ordinal)) return "HOVER";
            if (block.StartsWith("thruster", StringComparison.Ordinal)) return "FLIGHT";
        }
        return "CORE";
    }

    /// <summary>The lanes of a tab, top to bottom, with their nodes.</summary>
    List<(string Name, List<TechLayout.Cell> Cells)> Lanes(TechLayout lay, string group)
    {
        var lanes = new List<(string, List<TechLayout.Cell>)>();
        if (Tab is "large" or "small")
            foreach (string lane in new[] { "CORE", "HOVER", "FLIGHT" })
            {
                var cells = lay.Cells.Where(c => LaneOf(c) == lane).ToList();
                if (cells.Count > 0) lanes.Add((lane, cells));
            }
        else
        {
            var own = lay.Cells.Where(c => c.Node.Group == null).ToList();
            if (own.Count > 0) lanes.Add((Progress.TabName(Tab), own));
            if (group != null) lanes.Add(("DECO", lay.Cells.Where(c => c.Node.Group == group).ToList()));
        }
        return lanes;
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

        var lanes = Lanes(lay, group);
        var shown = lanes.SelectMany(l => l.Cells).ToList();
        // Columns by level; a crowded level spreads over several columns. The split that gives the
        // most readable tiles wins (tall enough for two lines, wide enough for a name).
        var levels = shown.Select(c => c.Node.Level).Distinct().OrderBy(x => x).ToList();
        int Count(int lane, int level) => lanes[lane].Cells.Count(c => c.Node.Level == level);
        float headH = 22, laneGap = 10;
        GridArea = new Rect2(o.X, top, area.Size.X, area.End.Y - top);
        float rowsH = GridArea.Size.Y - headH - laneGap * (lanes.Count - 1);
        int most = Enumerable.Range(0, lanes.Count).SelectMany(li => levels.Select(l => Count(li, l))).Max();
        Dictionary<int, int> span = null;
        List<int> laneRows = null;
        float tileW = 0, tileH = 0, best = -1;
        for (int rows = 1; rows <= most; rows++)
        {
            var k = levels.ToDictionary(l => l, l => Enumerable.Range(0, lanes.Count).Max(li => (Count(li, l) + rows - 1) / rows));
            var lr = Enumerable.Range(0, lanes.Count).Select(li => Math.Max(1, levels.Max(l => (Count(li, l) + k[l] - 1) / k[l]))).ToList();
            float w = Math.Min(MaxTileW, (area.Size.X - LaneW) / k.Values.Sum() - Gap), h = Math.Min(MaxTileH, rowsH / lr.Sum() - Gap);
            float score = Math.Min(h / 44f, w / 150f);
            if (score > best) { best = score; span = k; laneRows = lr; tileW = w; tileH = h; }
        }
        var colOf = new Dictionary<int, int>();
        int next = 0;
        foreach (int l in levels) { colOf[l] = next; next += span[l]; }
        float ColX(int col) => o.X + LaneW + col * (tileW + Gap);

        // The level headers, over each level's first column.
        foreach (int level in levels)
            g.TextIn($"LV {level}", new Rect2(ColX(colOf[level]), top, tileW, headH - 4), p.Level >= level ? C.Good : C.Dim, 13, HorizontalAlignment.Left, UiTheme.Mono);

        // Pointing at a tile: what it needs and what it opens stay lit.
        TechLayout.Cell hover = null;
        var at = new Dictionary<TechLayout.Cell, Rect2>();
        float y = top + headH;
        for (int li = 0; li < lanes.Count; li++)
        {
            var (name, cells) = lanes[li];
            float laneH = laneRows[li] * (tileH + Gap) - Gap;
            Color laneTone = name switch { "HOVER" => C.Holo, "FLIGHT" => C.Accent, "DECO" => C.Holo, _ => C.Accent2 };
            g.Fill(new Rect2(o.X, y, 4, laneH), laneTone);
            var (laneText, laneSize) = Fit(name == "DECO" ? group?.ToUpperInvariant() ?? name : name, 12, 8, LaneW - 12, UiTheme.Display);
            g.TextIn(laneText, new Rect2(o.X + 8, y, LaneW - 12, Math.Min(laneH, 22)), laneTone, laneSize, HorizontalAlignment.Left, UiTheme.Display);
            foreach (var byLevel in cells.GroupBy(c => c.Node.Level))
            {
                int k = 0, rowsHere = laneRows[li];
                foreach (TechLayout.Cell c in byLevel.OrderBy(c => c.Node.Name, StringComparer.Ordinal))
                {
                    var r = new Rect2(ColX(colOf[c.Node.Level] + k / rowsHere), y + k % rowsHere * (tileH + Gap), tileW, tileH);
                    k++;
                    at[c] = r;
                    Placed[c.Node.Id] = r;
                    if (g.Over(r)) hover = c;
                }
            }
            y += laneH + laneGap;
        }        HashSet<TechLayout.Cell> path = hover == null ? null : lay.PathTo(hover), opens = hover == null ? null : lay.Opens(hover);
        bool Lit(TechLayout.Cell c) => hover == null || path.Contains(c) || opens.Contains(c);

        foreach (var (c, r) in at) Tile(g, p, c, r, Lit(c), c == hover);

        if (hover != null)
        {
            TechNode n = hover.Node;
            UnlockState state = p.StateOf(n);
            string plain = Plain(n, out string tg);
            var lines = new List<(string, Color)>
            {
                (plain + (tg == "L" ? "  (large)" : tg == "S" ? "  (small)" : ""), C.Accent),
                ($"LEVEL {n.Level} · {n.Cost} PT", C.Text),
                (state == UnlockState.Unlocked ? "UNLOCKED" : p.Needs(n), state == UnlockState.Unlocked ? C.Good : state == UnlockState.Affordable ? C.Accent : C.Warn),
            };
            if (hover.Parent != null) lines.Add(($"NEEDS · {Plain(hover.Parent.Node, out _)}", C.Dim));
            if (hover.Kids.Count > 0) lines.Add(($"OPENS · {string.Join(", ", hover.Kids.Take(4).Select(k => Plain(k.Node, out _)))}{(hover.Kids.Count > 4 ? "..." : "")}", C.Dim));
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
        string name = Plain(n, out _);
        bool tall = r.Size.Y >= 42;
        float icon = Math.Min(r.Size.Y - 6, 40);
        float textX = r.Position.X + 9 + icon + 5, textW = r.End.X - 4 - textX;
        const float BadgeW = 24, BadgeH = 14;
        string status = state switch
        {
            UnlockState.Unlocked => "",
            UnlockState.Affordable => "UNLOCK",
            UnlockState.NeedsLevel => $"LV {n.Level}",
            UnlockState.NeedsParent => "NEEDS " + (c.Parent != null ? Plain(c.Parent.Node, out _).ToUpperInvariant() : "PARENT"),
            _ => "POINTS",
        };
        string cost = state == UnlockState.Unlocked ? "" : $"{n.Cost}";
        // Tall: the name on two lines, the status and the cost under it. Short: the name on one line and the cost beside it.
        var (one, two, ns) = tall ? Wrap(name, 12, 9, textW, UiTheme.Label) : (Fit(name, 12, 9, textW - BadgeW - 4, UiTheme.Label).Text, "", Fit(name, 12, 9, textW - BadgeW - 4, UiTheme.Label).Size);
        var badge = tall ? new Rect2(r.End.X - BadgeW - 3, r.End.Y - BadgeH - 3, BadgeW, BadgeH) : new Rect2(r.End.X - BadgeW - 3, r.Position.Y + (r.Size.Y - BadgeH) / 2f, BadgeW, BadgeH);
        var line1 = new Rect2(textX, r.Position.Y + 2, tall ? textW : textW - BadgeW - 4, tall ? 14 : r.Size.Y - 4);
        var line2 = new Rect2(textX, r.Position.Y + 15, textW, 14);
        var line3 = new Rect2(textX, r.End.Y - BadgeH - 3, badge.Position.X - 4 - textX, BadgeH);
        var (st, ss) = Fit(status, 10, 8, line3.Size.X, UiTheme.Label);
        if (!g.Drawing)
        {
            // Laid out in the input pass too, so the tests can measure it.
            g.TextIn(one, line1, C.Text, ns);
            if (tall && two != "") g.TextIn(two, line2, C.Text, ns);
            if (tall && st != "") g.TextIn(st, line3, C.Dim, ss);
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
        // The cost on its badge, or a tick once unlocked.
        g.Fill(badge, F(new Color(state == UnlockState.Unlocked ? C.Good : state == UnlockState.Affordable ? C.Accent : C.Rule, 0.85f)));
        if (cost != "") g.TextIn(cost, badge, F(C.Glass), 11, HorizontalAlignment.Center, UiTheme.Mono);
        else
        {
            Vector2 m = badge.GetCenter();
            g.Canvas.DrawPolyline(new[] { m + new Vector2(-5, 0), m + new Vector2(-2, 3), m + new Vector2(5, -4) }, F(C.Glass), 2f);
        }
    }}
