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
/// TECH: the tech tree, one tab per kind of thing (base and capital vessel, small and hover vessel,
/// tools, weapons, deco, misc), built from the unlock on every item and block. See
/// <see cref="TechLayout"/>: nodes stand in columns by the level they open at and every branch has
/// its own row, joined to its parent by a right-angle wire. Pointing at a node lights its path back
/// to the root and everything it opens, and dims the rest. A tree wider or taller than the window
/// scrolls (the wheel, shift and the wheel, or the arrows). Unlocked nodes are lit; locked ones say
/// what they need (a level, points, their parent); one you can afford unlocks on a click. The deco
/// tab holds a family per theme set: pick a set, then its pieces show.
/// </summary>
public sealed class TechSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    public string Tab = "base";
    /// <summary>The deco set shown on a tab with groups.</summary>
    public string Group;
    /// <summary>How far the tree is scrolled, in columns and rows.</summary>
    public int ColOff, RowOff;

    const float BoxW = 204, BoxH = 48, ColW = 244, RowH = 58;
    int _visCols = 6, _visRows = 10;
    string _key, _layoutKey;
    TechLayout _layout;

    public TechSection(Game.Game game) => _game = game;
    public Vector2 PreferredSize => new(1560, 790);

    /// <summary>What the section's picture depends on besides the pointer, so scrolling redraws.</summary>
    public string Signature => $"{Tab}|{Group}|{ColOff}|{RowOff}";

    static string KeyOf(string tab, string group) => group == null ? tab : tab + "/" + group;

    TechLayout LayoutFor(string tab, string group)
    {
        string key = KeyOf(tab, group);
        if (_layout == null || _layoutKey != key)
        {
            _layout = new TechLayout(_game.Progress.Nodes.Where(n => n.Tab == tab && (group == null || n.Group == group)));
            _layoutKey = key;
        }
        return _layout;
    }

    /// <summary>Scrolls to a node (switching to its tab and set), so a script can click it.</summary>
    public void Reveal(string id)
    {
        TechNode n = _game.Progress.Node(id);
        if (n == null) return;
        Tab = n.Tab;
        if (n.Group != null) Group = n.Group;
        TechLayout l = LayoutFor(Tab, n.Group);
        _key = KeyOf(Tab, n.Group);
        ColOff = RowOff = 0;
        if (!l.ById.TryGetValue(id, out TechLayout.Cell c)) return;
        if (c.Col >= _visCols) ColOff = c.Col - _visCols + 1;
        if (c.Row >= _visRows) RowOff = c.Row - _visRows + 1;
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
        g.Text($"LEVEL {p.Level}   {p.Points} UNLOCK POINTS   {p.Xp} XP{(p.Creative ? "   (CREATIVE: everything unlocked)" : "")}", o + new Vector2(0, 46), C.Accent2, 17, UiTheme.Display);
        var onTab = p.Nodes.Where(n => n.Tab == Tab).ToList();
        if (onTab.Count == 0) return;
        // Tabs with groups (deco): a row of sets, then one set's pieces. Up to 12 sets a row, two rows.
        var groups = onTab.Select(n => n.Group).Where(x => x != null).Distinct().ToList();
        float top = 0;
        string group = null;
        if (groups.Count > 0)
        {
            if (Group == null || !groups.Contains(Group)) Group = groups[0];
            group = Group;
            float gw = (area.Size.X - 10) / 12;
            for (int i = 0; i < groups.Count; i++)
            {
                var r = new Rect2(o.X + i % 12 * gw, o.Y + 76 + i / 12 * 30, gw - 4, 26);
                if (g.Button($"tech:group:{groups[i]}", r, groups[i].ToUpperInvariant(), C.Holo, lit: groups[i] == Group, size: 11)) { Group = groups[i]; group = Group; }
            }
            top = (groups.Count + 11) / 12 * 30 + 4;
        }
        string key = KeyOf(Tab, group);
        if (key != _key)
        {
            _key = key;
            ColOff = RowOff = 0;
        }
        TechLayout lay = LayoutFor(Tab, group);

        // The window on the tree: whole columns and rows only, so no box is ever cut.
        float headY = o.Y + 80 + top, treeY = o.Y + 106 + top;
        float viewH = area.End.Y - 24 - treeY;
        _visCols = Math.Max(1, (int)((area.Size.X + (ColW - BoxW)) / ColW));
        _visRows = Math.Max(1, (int)(viewH / RowH));
        var view = new Rect2(o.X, treeY, area.Size.X, _visRows * RowH);
        int maxCol = Math.Max(0, lay.Columns.Count - _visCols), maxRow = Math.Max(0, lay.Rows - _visRows);
        if (!g.Drawing && g.Over(new Rect2(o.X, headY, area.Size.X, view.End.Y - headY)) && _game.Controls.Wheel != 0)
        {
            int step = Math.Sign(_game.Controls.Wheel);
            if (g.Shift) ColOff += step; else RowOff += step * 2;
        }
        if (maxCol > 0 || maxRow > 0)
        {
            float bx = area.End.X - 4 * 44;
            if (g.Button("tech:scroll:left", new Rect2(bx, o.Y + 42, 40, 28), "<", C.Accent2, enabled: ColOff > 0, size: 16)) ColOff--;
            if (g.Button("tech:scroll:right", new Rect2(bx + 44, o.Y + 42, 40, 28), ">", C.Accent2, enabled: ColOff < maxCol, size: 16)) ColOff++;
            if (g.Button("tech:scroll:up", new Rect2(bx + 88, o.Y + 42, 40, 28), "^", C.Accent2, enabled: RowOff > 0, size: 16)) RowOff = Math.Max(0, RowOff - 3);
            if (g.Button("tech:scroll:down", new Rect2(bx + 132, o.Y + 42, 40, 28), "v", C.Accent2, enabled: RowOff < maxRow, size: 16)) RowOff = Math.Min(maxRow, RowOff + 3);
            g.TextIn("wheel: up and down · shift + wheel: sideways", new Rect2(area.End.X - 460, area.End.Y - 20, 460, 18), C.Dim, 13, HorizontalAlignment.Right);
        }
        ColOff = Math.Clamp(ColOff, 0, maxCol);
        RowOff = Math.Clamp(RowOff, 0, maxRow);

        Vector2 Origin(TechLayout.Cell c) => new(o.X + (c.Col - ColOff) * ColW, treeY + (c.Row - RowOff) * RowH);
        bool Shown(TechLayout.Cell c) => c.Col >= ColOff && c.Col < ColOff + _visCols && c.Row >= RowOff && c.Row < RowOff + _visRows;
        Rect2 BoxOf(TechLayout.Cell c) => new(Origin(c), new Vector2(BoxW, BoxH));

        // Which node the pointer is on; its path and what it opens stay lit.
        TechLayout.Cell hover = null;
        foreach (TechLayout.Cell c in lay.Cells)
            if (Shown(c) && g.Over(BoxOf(c))) hover = c;
        HashSet<TechLayout.Cell> path = hover == null ? null : lay.PathTo(hover), opens = hover == null ? null : lay.Opens(hover);
        bool Lit(TechLayout.Cell c) => hover == null || path.Contains(c) || opens.Contains(c);

        // Level headers over the columns.
        for (int ci = ColOff; ci < Math.Min(lay.Columns.Count, ColOff + _visCols); ci++)
        {
            var (level, sub) = lay.Columns[ci];
            if (sub == 0) g.TextIn($"LEVEL {level}", new Rect2(o.X + (ci - ColOff) * ColW, headY, BoxW, 20), p.Level >= level ? C.Good : C.Dim, 14, HorizontalAlignment.Center, UiTheme.Mono);
            else g.TextIn($"LEVEL {level} ›", new Rect2(o.X + (ci - ColOff) * ColW, headY, BoxW, 20), C.Dim, 14, HorizontalAlignment.Center, UiTheme.Mono); // a step on, for what a node of this level opens
        }

        // Wires: out of a parent's right side, along the gutter, then into the child along its own row.
        if (g.Drawing)
        {
            void Seg(Vector2 a, Vector2 b, Color col, float w)
            {
                if (a.X == b.X) // vertical
                {
                    if (a.X < view.Position.X || a.X > view.End.X) return;
                    float y0 = Math.Max(Math.Min(a.Y, b.Y), view.Position.Y), y1 = Math.Min(Math.Max(a.Y, b.Y), view.End.Y);
                    if (y0 <= y1) g.Canvas.DrawLine(new Vector2(a.X, y0), new Vector2(a.X, y1), col, w);
                }
                else
                {
                    if (a.Y < view.Position.Y || a.Y > view.End.Y) return;
                    float x0 = Math.Max(Math.Min(a.X, b.X), view.Position.X), x1 = Math.Min(Math.Max(a.X, b.X), view.End.X);
                    if (x0 <= x1) g.Canvas.DrawLine(new Vector2(x0, a.Y), new Vector2(x1, a.Y), col, w);
                }
            }
            void Wire(TechLayout.Cell parent, TechLayout.Cell kid, bool lit)
            {
                Vector2 pa = Origin(parent) + new Vector2(BoxW, BoxH / 2), ka = Origin(kid) + new Vector2(0, BoxH / 2);
                float tx = pa.X + (ColW - BoxW) / 2;
                bool done = p.IsUnlocked(kid.Node.Id);
                bool on = hover != null && lit;
                Color col = on ? C.Accent : done ? C.Good : C.Rule;
                if (hover != null && !on) col = new Color(col, 0.2f);
                float w = on ? 3.5f : 2f;
                Seg(pa, new Vector2(tx, pa.Y), col, w);
                Seg(new Vector2(tx, pa.Y), new Vector2(tx, ka.Y), col, w);
                Seg(new Vector2(tx, ka.Y), ka, col, w);
                if (Shown(kid) && ka.X - 8 >= view.Position.X)
                    g.Canvas.DrawColoredPolygon(new[] { ka, ka + new Vector2(-8, -5), ka + new Vector2(-8, 5) }, col);
            }
            // The lit wires last, so they run over the rest.
            bool EdgeLit(TechLayout.Cell c) => hover != null && (path.Contains(c) && path.Contains(c.Parent) || opens.Contains(c) && (opens.Contains(c.Parent) || c.Parent == hover));
            foreach (TechLayout.Cell c in lay.Cells)
                if (c.Parent != null && !EdgeLit(c)) Wire(c.Parent, c, false);
            foreach (TechLayout.Cell c in lay.Cells)
                if (c.Parent != null && EdgeLit(c)) Wire(c.Parent, c, true);
        }

        foreach (TechLayout.Cell c in lay.Cells)
        {
            if (!Shown(c)) continue;
            TechNode n = c.Node;
            Rect2 r = BoxOf(c);
            UnlockState state = p.StateOf(n);
            Color tone = state switch { UnlockState.Unlocked => C.Good, UnlockState.Affordable => C.Accent, _ => C.Dim };
            if (g.Button($"tech:{n.Id}", r, "", tone, enabled: state == UnlockState.Affordable, lit: false))
            {
                if (p.Unlock(n.Id)) g.Sound = "unlock";
            }
            if (!g.Drawing) continue;
            float fade = Lit(c) ? 1f : 0.3f;
            Color F(Color col) => new Color(col, col.A * fade);
            string name = Plain(n, out string tag);
            float tagW = tag == null ? 0 : 22;
            g.Fill(r, F(new Color(state == UnlockState.Unlocked ? C.Panel : C.Glass, 0.95f)));
            g.Fill(new Rect2(r.Position, new Vector2(6, r.Size.Y)), F(tone));
            g.Outline(r, F(c == hover ? C.Accent : tone), c == hover ? 3.5f : state == UnlockState.Affordable ? 3f : 1.5f);
            var (nt, ns) = Fit(name, 14, 11, BoxW - 18 - tagW, UiTheme.Label);
            g.Text(nt, r.Position + new Vector2(12, 4 + (14 - ns) / 2f), F(state == UnlockState.Unlocked ? C.Text : C.Dim), ns);
            string status = state == UnlockState.Unlocked ? $"UNLOCKED · {n.Cost} PT" : Regex.Replace(p.Needs(n), @"\s*\((large|small)\)", "", RegexOptions.IgnoreCase);
            var (st, ss) = Fit(status, 12, 9, BoxW - 18, UiTheme.Label);
            g.Text(st, r.Position + new Vector2(12, 27 + (12 - ss) / 2f), F(state == UnlockState.Affordable ? C.Accent : state == UnlockState.Unlocked ? C.Good : C.Warn), ss);
            if (tag != null)
            {
                var badge = new Rect2(r.End.X - 22, r.Position.Y + 3, 18, 18);
                g.Fill(badge, F(tag == "L" ? C.Accent2 : C.Holo));
                g.TextIn(tag, badge, F(C.Glass), 13, HorizontalAlignment.Center, UiTheme.Display);
            }
        }

        if (hover != null)
        {
            TechNode n = hover.Node;
            UnlockState state = p.StateOf(n);
            string plain = Plain(n, out string tg);
            var lines = new List<(string, Color)>
            {
                (plain + (tg == "L" ? "  (large grid)" : tg == "S" ? "  (small grid)" : ""), C.Accent),
                ($"Opens at level {n.Level} · {n.Cost} unlock point{(n.Cost == 1 ? "" : "s")}", C.Text),
                (state == UnlockState.Unlocked ? "Unlocked" : p.Needs(n), state == UnlockState.Unlocked ? C.Good : state == UnlockState.Affordable ? C.Accent : C.Warn),
            };
            if (hover.Parent != null) lines.Add(($"Needs: {Plain(hover.Parent.Node, out _)}", C.Dim));
            if (hover.Kids.Count > 0) lines.Add(($"Opens {lay.Opens(hover).Count} more: {string.Join(", ", hover.Kids.Take(4).Select(k => Plain(k.Node, out _)))}{(hover.Kids.Count > 4 ? "..." : "")}", C.Dim));
            g.SetTip(BoxOf(hover), () => lines);
        }
    }
}
