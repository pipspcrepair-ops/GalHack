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
    public bool Pressed, Released, Held, RightPressed, Shift;

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

    public void BeginInput(Vector2 mouse, bool pressed, bool released, bool held, bool right, bool shift)
    {
        Canvas = null;
        Mouse = mouse;
        Pressed = pressed;
        Released = released;
        Held = held;
        RightPressed = right;
        Shift = shift;
        Sound = null;
        _building.Clear();
    }

    public void EndInput()
    {
        Rects.Clear();
        foreach (var (k, v) in _building) Rects[k] = v;
        if (Released && DragId != null && !_dropTaken) { DragId = null; DragPayload = null; }
        _dropTaken = false;
    }

    public void BeginDraw(CanvasItem canvas) => Canvas = canvas;

    bool _dropTaken;

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
        if (Pressed && Over(r) && payload != null && DragId == null)
        {
            DragId = id;
            DragPayload = payload;
            DragLabel = label;
            DragColour = colour;
            _dragFrom = Mouse;
        }
        return Released && DragId == id && Over(r) && Mouse.DistanceTo(_dragFrom) < 6f;
    }

    /// <summary>A drop target: true on the input pass when something being dragged was let go over it (not onto itself).</summary>
    public bool DropTarget(string id, Rect2 r, out object payload)
    {
        payload = null;
        Register(id, r);
        if (Drawing || !Released || DragId == null || DragId == id || !Over(r)) return false;
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
        if (Drawing || !Released || DragId == null || window.HasPoint(Mouse)) return false;
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
    }

    /// <summary>A right click on a widget's rectangle this pass.</summary>
    public bool RightClicked(Rect2 r) => !Drawing && RightPressed && Over(r);
}
