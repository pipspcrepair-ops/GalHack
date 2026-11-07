using System;
using System.Collections.Generic;
using DiegeticUi.Theme;
using Godot;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// A small immediate-mode UI for the Tab menu, in the diegetic UI's style (its palette, fonts and
/// chunky blocks). The same layout code runs twice: once per physics tick as the input pass
/// (it reads <see cref="Controls"/>' mouse and says what was clicked), and once per frame as the
/// draw pass. So what you click is always exactly what is drawn, and a test can find any widget's
/// rectangle by its id and click it through the game's own mouse input. Coordinates are a fixed
/// 1600 x 900 canvas, scaled to the window.
/// </summary>
public sealed class Gui
{
    public const float Width = 1600f, Height = 900f;
    static Palette C => UiTheme.Colors;

    /// <summary>The draw pass's canvas, or null in the input pass.</summary>
    public CanvasItem Canvas;
    public bool Drawing => Canvas != null;

    // Input, for the input pass.
    public Vector2 Mouse;
    public bool Pressed, Released, Held, RightPressed, Shift, Ctrl, RightReleased, RightHeld;
    /// <summary>Wheel ticks this pass (positive: up).</summary>
    public int Wheel;
    /// <summary>The clock double clicks are timed on, seconds (real time; tests may set it).</summary>
    public Func<double> Clock = () => Time.GetTicksMsec() / 1000.0;

    /// <summary>Every widget's rectangle from the last input pass, by id (tests click them).</summary>
    public readonly Dictionary<string, Rect2> Rects = new();
    readonly Dictionary<string, Rect2> _building = new();

    /// <summary>What is being dragged: its id, a payload and a label to draw at the pointer.</summary>
    public string DragId;
    public object DragPayload;
    public string DragLabel;
    public Color DragColour;
    Vector2 _dragFrom;

    /// <summary>A sound to play for what the last input pass did ("click", "drop", "tab").</summary>
    public string Sound;

    public void BeginInput(Vector2 mouse, bool pressed, bool released, bool held, bool right, bool shift, bool ctrl = false, bool rightReleased = false, bool rightHeld = false, int wheel = 0)
    {
        Canvas = null;
        Tip = null;
        FirstBox = null;
        Mouse = mouse;
        Pressed = pressed;
        Released = released;
        Held = held;
        RightPressed = right;
        Shift = shift;
        Ctrl = ctrl;
        RightReleased = rightReleased;
        RightHeld = rightHeld;
        Wheel = wheel;
        if (wheel != 0 && DragPayload is IDragAmount da) da.Step(wheel * (shift ? 10 : 1));
        Sound = null;
        _building.Clear();
    }

    public void EndInput()
    {
        Rects.Clear();
        foreach (var (k, v) in _building) Rects[k] = v;
        if (LetGo && DragId != null && !_dropTaken) { DragId = null; DragPayload = null; DragRight = false; }
        _dropTaken = false;
        LastTip = Tip;
        LastFirstBox = FirstBox;
    }

    public void BeginDraw(CanvasItem canvas)
    {
        Canvas = canvas;
        Tip = null;
    }

    bool _dropTaken;

    /// <summary>The drag in progress was begun with the right button (half a stack, or one with Ctrl).</summary>
    public bool DragRight;

    /// <summary>The button that began the drag was let go this pass.</summary>
    public bool LetGo => DragRight ? RightReleased : Released;

    public bool Over(Rect2 r) => r.HasPoint(Mouse);

    void Register(string id, Rect2 r)
    {
        if (!Drawing && id != null) _building[id] = r;
    }

    // ---- Drawing ----------------------------------------------------------------------------

    public void Fill(Rect2 r, Color c) { if (Drawing) Canvas.Block(r, c); }
    public void Outline(Rect2 r, Color c, float w = 2f) { if (Drawing) Canvas.Frame(r, c, w); }

    public void Text(string text, Vector2 at, Color colour, int size = 18, Font font = null)
    {
        if (Drawing) Canvas.Text(font ?? UiTheme.Label, size, at, text, colour);
    }

    public void TextIn(string text, Rect2 r, Color colour, int size = 18, HorizontalAlignment align = HorizontalAlignment.Left, Font font = null)
    {
        if (Drawing) Canvas.TextIn(font ?? UiTheme.Label, size, r, text, colour, align);
    }

    /// <summary>A warning-striped band round a rectangle, drawn just inside its edge.</summary>
    public void Stripes(Rect2 r, float band, Color colour)
    {
        if (!Drawing) return;
        foreach (Rect2 b in new[]
                 {
                     new Rect2(r.Position, new Vector2(r.Size.X, band)), new Rect2(r.Position.X, r.End.Y - band, r.Size.X, band),
                     new Rect2(r.Position.X, r.Position.Y + band, band, r.Size.Y - 2 * band), new Rect2(r.End.X - band, r.Position.Y + band, band, r.Size.Y - 2 * band),
                 })
        {
            Canvas.Block(b, new Color(0.04f, 0.04f, 0.05f));
            Vector2[] edge = { b.Position, new(b.End.X, b.Position.Y), b.End, new(b.Position.X, b.End.Y) };
            // Bands of equal width at 45 degrees, on the lines x + y = c, so the pattern runs on round every corner.
            float period = band * 2f, top = b.Position.Y, bottom = b.End.Y;
            for (float c = Mathf.Floor((b.Position.X + top) / period) * period - period; c < b.End.X + bottom + period; c += period)
            {
                Vector2[] stripe = { new(c - top, top), new(c + band - top, top), new(c + band - bottom, bottom), new(c - bottom, bottom) };
                foreach (Vector2[] piece in Geometry2D.IntersectPolygons(edge, stripe))
                    if (piece.Length >= 3) Canvas.DrawColoredPolygon(piece, colour);
            }
        }
    }

    public void Bar(Rect2 r, double value, double max, Color fill, int count = 12)
    {
        if (Drawing) Canvas.Segments(r, Math.Clamp(value, 0, max), Math.Max(1e-6, max), fill, count: count, gap: 2);
    }

    /// <summary>A section caption with a rule under it.</summary>
    public void Caption(string text, Vector2 at, float width, Color? colour = null)
    {
        Text(text, at, colour ?? C.Accent, 16, UiTheme.Display);
        Fill(new Rect2(at.X, at.Y + 24, width, 2), new Color(C.Rule, 0.8f));
    }

    // ---- Widgets ----------------------------------------------------------------------------

    /// <summary>A block button. True on the input pass when it was clicked (and enabled).</summary>
    public bool Button(string id, Rect2 r, string label, Color colour, bool enabled = true, bool lit = false, int size = 17)
    {
        Register(id, r);
        bool hot = Over(r) && enabled;
        if (Drawing)
        {
            Color bg = !enabled ? new Color(C.Glass, 0.5f) : lit ? colour : hot ? new Color(colour, 0.45f) : new Color(C.Glass, 0.85f);
            Canvas.Block(r, bg);
            Canvas.Block(new Rect2(r.Position, new Vector2(5, r.Size.Y)), enabled ? colour : C.Dim);
            Canvas.TextIn(UiTheme.Label, size, r.Grow(-6), label, !enabled ? C.Dim : lit ? C.Glass : C.Text, HorizontalAlignment.Center);
            return false;
        }
        bool clicked = enabled && Pressed && hot && DragId == null;
        if (clicked) Sound = "click";
        return clicked;
    }

    /// <summary>A tab header: lit when current. True when clicked.</summary>
    public bool Tab(string id, Rect2 r, string label, bool current, Color colour)
    {
        bool clicked = Button(id, r, label, colour, enabled: true, lit: current, size: 18);
        if (clicked) Sound = "tab";
        return clicked;
    }

    /// <summary>
    /// A draggable thing (an inventory slot, a palette entry). Pressing starts a drag; returns
    /// true on the input pass when it was clicked without moving (released where it started).
    /// </summary>
    public bool Draggable(string id, Rect2 r, object payload, string label, Color colour)
    {
        Register(id, r);
        if (Drawing) return false;
        bool left = Pressed && Over(r), right = RightPressed && Over(r) && payload is IDragAmount;
        if ((left || right) && payload != null && DragId == null)
        {
            DragId = id;
            DragPayload = payload;
            DragLabel = label;
            DragColour = colour;
            DragRight = right && !left;
            if (DragRight && payload is IDragAmount da) da.Begin(Ctrl);
            _dragFrom = Mouse;
        }
        return !DragRight && Released && DragId == id && Over(r) && Mouse.DistanceTo(_dragFrom) < 6f;
    }

    /// <summary>A right button press and release on the widget it began on, without moving: a right click (it splits a stack).</summary>
    public bool RightClick(string id, Rect2 r) =>
        !Drawing && DragRight && RightReleased && DragId == id && Over(r) && Mouse.DistanceTo(_dragFrom) < 6f;

    /// <summary>Is the pointer over this widget while something is being dragged (hover to open a tab)?</summary>
    public bool DragOver(Rect2 r) => !Drawing && DragId != null && Over(r);

    /// <summary>A drop target: true on the input pass when something being dragged was let go over it (not onto itself).</summary>
    public bool DropTarget(string id, Rect2 r, out object payload)
    {
        payload = null;
        Register(id, r);
        if (Drawing || !LetGo || DragId == null || DragId == id || !Over(r)) return false;
        payload = DragPayload;
        DragId = null;
        DragPayload = null;
        _dropTaken = true;
        Sound = "drop";
        return true;
    }

    /// <summary>Was something being dragged let go outside every drop target (dropped out of the window)? Call last.</summary>
    public bool DroppedOutside(Rect2 window, out object payload)
    {
        payload = null;
        if (Drawing || !LetGo || DragId == null || window.HasPoint(Mouse)) return false;
        payload = DragPayload;
        DragId = null;
        DragPayload = null;
        _dropTaken = true;
        return true;
    }

    /// <summary>The thing being dragged, at the pointer (draw pass, last).</summary>
    public void DrawDrag()
    {
        if (!Drawing || DragId == null) return;
        var r = new Rect2(Mouse - new Vector2(30, 30), new Vector2(60, 60));
        Canvas.Block(r, new Color(DragColour, 0.85f));
        Canvas.TextIn(UiTheme.Label, 13, r.Grow(-4), DragLabel ?? "", C.Glass, HorizontalAlignment.Center);
        if (DragPayload is IDragAmount da && da.Shown is string shown)
            Canvas.TextIn(UiTheme.Mono, 15, new Rect2(r.Position + new Vector2(2, r.Size.Y - 22), new Vector2(r.Size.X - 6, 20)), shown, C.Glass, HorizontalAlignment.Right);
    }

    /// <summary>A right click on a widget's rectangle this pass.</summary>
    public bool RightClicked(Rect2 r) => !Drawing && RightPressed && Over(r);

    // ---- Text boxes ---------------------------------------------------------------------------

    /// <summary>The first text box laid out this pass (the "/" key focuses it), and the last pass's.</summary>
    public string FirstBox, LastFirstBox;

    /// <summary>
    /// A one-line text box (a filter). Drawn with its text, or a dim placeholder, and a caret while
    /// it has the keyboard. Returns true on the input pass when it was clicked (the caller gives it
    /// the keyboard). A read-only box (an in-world screen: no keyboard) shows its text and ignores clicks.
    /// </summary>
    public bool TextBox(string id, Rect2 r, string text, string placeholder, bool focused, bool readOnly = false)
    {
        Register(id, r);
        if (!readOnly) FirstBox ??= id;
        if (Drawing)
        {
            Canvas.Block(r, new Color(C.Panel, 0.9f));
            Canvas.Frame(r, focused ? C.Accent : new Color(C.Rule, 0.9f), focused ? 2.5f : 1.5f);
            bool empty = string.IsNullOrEmpty(text);
            string shown = empty ? placeholder : text;
            Canvas.TextIn(UiTheme.Label, 14, r.Grow(-7), shown + (focused && ((int)(Time.GetTicksMsec() / 500) & 1) == 0 ? "_" : ""), empty ? C.Dim : C.Text, HorizontalAlignment.Left);
            return false;
        }
        bool clicked = !readOnly && Pressed && Over(r) && DragId == null;
        if (clicked) Sound = "click";
        return clicked;
    }

    // ---- Tooltips -----------------------------------------------------------------------------

    /// <summary>The tooltip asked for this pass (the last one under the pointer wins), and the last input pass's (tests read it).</summary>
    public List<(string Text, Color Colour)> Tip, LastTip;
    Rect2 _tipAnchor;

    /// <summary>Shows a tooltip for a widget while the pointer is over it and nothing is being dragged.</summary>
    public void SetTip(Rect2 anchor, Func<List<(string Text, Color Colour)>> lines)
    {
        if (!Over(anchor) || DragId != null) return;
        Tip = lines();
        _tipAnchor = anchor;
    }

    /// <summary>
    /// Draws the tooltip last, beside its widget and inside <paramref name="bounds"/>: right of it if
    /// there is room, else left; moved up so it never runs off the bottom. The first line is the title.
    /// </summary>
    public void DrawTip(Rect2 bounds)
    {
        if (!Drawing || Tip == null || Tip.Count == 0) return;
        Rect2 box = TipBox(bounds, _tipAnchor, Tip);
        Canvas.Block(box, new Color(C.Glass, 0.97f));
        Canvas.Frame(box, Tip[0].Colour, 2f);
        Canvas.Block(new Rect2(box.Position, new Vector2(box.Size.X, 4)), Tip[0].Colour);
        float y = box.Position.Y + 8;
        for (int i = 0; i < Tip.Count; i++)
        {
            int size = i == 0 ? TipTitle : TipLine;
            Canvas.Text(i == 0 ? UiTheme.Display : UiTheme.Label, size, new Vector2(box.Position.X + 10, y), Tip[i].Text, Tip[i].Colour);
            y += size + 7;
        }
    }

    const int TipTitle = 15, TipLine = 13;

    /// <summary>Where a tooltip goes: beside the anchor, wholly inside the bounds.</summary>
    public static Rect2 TipBox(Rect2 bounds, Rect2 anchor, IReadOnlyList<(string Text, Color Colour)> lines)
    {
        float w = 0, h = 12;
        for (int i = 0; i < lines.Count; i++)
        {
            int size = i == 0 ? TipTitle : TipLine;
            w = MathF.Max(w, Paint.TextWidth(i == 0 ? UiTheme.Display : UiTheme.Label, size, lines[i].Text));
            h += size + 7;
        }
        w = MathF.Min(w + 22, bounds.Size.X);
        h = MathF.Min(h, bounds.Size.Y);
        float x = anchor.End.X + 8;
        if (x + w > bounds.End.X) x = anchor.Position.X - 8 - w;
        x = Math.Clamp(x, bounds.Position.X, bounds.End.X - w);
        float y = Math.Clamp(anchor.Position.Y, bounds.Position.Y, bounds.End.Y - h);
        return new Rect2(x, y, w, h);
    }
}

/// <summary>A dragged stack whose amount can be chosen: all of it, half (right-drag), one (Ctrl + right-drag), and the wheel adjusts.</summary>
public interface IDragAmount
{
    /// <summary>How many are being carried (int.MaxValue: the whole stack).</summary>
    int Amount { get; set; }
    /// <summary>The stack's size at the start of the drag.</summary>
    int Max { get; }
    /// <summary>Right button began the drag: half, or one with Ctrl.</summary>
    void Begin(bool ctrl);
    /// <summary>The mouse wheel changed the amount.</summary>
    void Step(int by);
    /// <summary>The amount to draw by the pointer, or null for the whole stack.</summary>
    string Shown { get; }
}
