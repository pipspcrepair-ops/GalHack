using System;
using System.Collections.Generic;
using DiegeticUi.Screens;
using Godot;

namespace DiegeticUi.Theme;

/// <summary>
/// Base for the chunky screen widgets. Any change to how a widget looks goes through
/// <see cref="Changed"/>, which also tells the world screen showing it to redraw once.
/// </summary>
public abstract partial class ScreenWidget : Control
{
    protected static Palette C => UiTheme.Colors;

    public bool Hovered { get; private set; }

    /// <summary>Raised when the cursor moves onto or off the widget: consoles use it to preview costs.</summary>
    public event Action<bool> HoverChanged;

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter) SetHovered(true);
        else if (what == NotificationMouseExit) SetHovered(false);
    }

    void SetHovered(bool hovered)
    {
        if (Hovered == hovered) return;
        Hovered = hovered;
        HoverChanged?.Invoke(hovered);
        Changed();
    }

    public void Changed()
    {
        QueueRedraw();
        WorldScreen.MarkDirtyFor(this);
    }

    protected static bool IsLeftPress(InputEvent e, out bool pressed)
    {
        pressed = e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true };
        return e is InputEventMouseButton { ButtonIndex: MouseButton.Left };
    }
}

/// <summary>A labelled segmented bar: "POWER  162 / 260 KW", with an optional preview delta.</summary>
public partial class SegmentBar : ScreenWidget
{
    public string Caption { get; init; } = "";
    public string Unit { get; init; } = "";
    public string Format { get; init; } = "0";
    public int Count { get; init; } = 24;

    double _value, _max = 1, _preview;

    public SegmentBar() => MouseFilter = MouseFilterEnum.Ignore;

    public void Set(double value, double max, double preview = 0)
    {
        if (value == _value && max == _max && preview == _preview) return;
        (_value, _max, _preview) = (value, max, preview);
        Changed();
    }

    public override void _Draw()
    {
        float w = Size.X;
        double load = _max > 0 ? _value / _max : 0;
        Color fill = UiTheme.LoadColor(load);
        this.Text(UiTheme.Label, UiTheme.SizeLabel, Vector2.Zero, Caption, C.Text);

        string reading = $"{_value.ToString(Format)} / {_max.ToString(Format)} {Unit}";
        this.Text(UiTheme.Mono, UiTheme.SizeLabel, new Vector2(0, -2), reading, fill, HorizontalAlignment.Right, w - 24);
        if (_preview != 0)
        {
            // The cost of what's under the cursor, right next to the number it changes.
            string delta = (_preview > 0 ? "+" : "") + _preview.ToString(Format);
            Color dc = _value + _preview > _max ? C.Danger : C.Warn;
            float rw = Paint.TextWidth(UiTheme.Mono, UiTheme.SizeLabel, reading);
            this.Text(UiTheme.Mono, UiTheme.SizeLabel, new Vector2(w - 24 - rw - 22 - Paint.TextWidth(UiTheme.Mono, UiTheme.SizeLabel, delta), -2), delta, dc);
        }
        this.Segments(new Rect2(0, 40, w - 24, Size.Y - 40), _value, _max, fill, _preview, Count);
    }
}

/// <summary>A big two-position rocker switch with its caption and cost printed on the housing.</summary>
public partial class RockerSwitch : ScreenWidget
{
    public string Caption { get; init; } = "";
    public string Detail { get; set; } = "";
    public bool On { get; private set; }

    public event Action<bool> Toggled;

    public void SetOn(bool on)
    {
        if (On == on) return;
        On = on;
        Changed();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!IsLeftPress(e, out bool pressed) || !pressed) return;
        SetOn(!On);
        Toggled?.Invoke(On);
        AcceptEvent();
    }

    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;
        this.Text(UiTheme.Label, UiTheme.SizeLabel, Vector2.Zero, Caption, C.Text);
        var housing = new Rect2(0, 46, w, h - 46 - 36);
        this.Block(housing, C.Panel);
        if (Hovered) this.Frame(housing, C.Accent2, 4);

        // Two halves; the one that is "in" is a raised bright block.
        var off = new Rect2(housing.Position + new Vector2(10, 10), new Vector2(housing.Size.X / 2 - 15, housing.Size.Y - 20));
        var on = new Rect2(off.Position + new Vector2(off.Size.X + 10, 0), off.Size);
        this.Block(off, On ? C.Rule : C.Text);
        this.Block(on, On ? C.Good : C.Rule);
        this.TextIn(UiTheme.Label, UiTheme.SizeHeading, off, "OFF", On ? C.Dim : C.Glass, HorizontalAlignment.Center);
        this.TextIn(UiTheme.Label, UiTheme.SizeHeading, on, "ON", On ? C.Glass : C.Dim, HorizontalAlignment.Center);

        this.Text(UiTheme.Mono, UiTheme.SizeSmall, new Vector2(0, h - 28), Detail, Hovered ? C.Warn : C.Dim);
    }
}

/// <summary>
/// A vertical lever in a slot: up is on. Drag the handle, or click to throw it. The handle
/// eases to its new position, which is the one time this widget asks for redraws on its own.
/// </summary>
public partial class Lever : ScreenWidget
{
    public string Caption { get; init; } = "";
    public string Detail { get; set; } = "";
    public string UpLabel { get; init; } = "ON";
    public string DownLabel { get; init; } = "OFF";
    public bool On { get; private set; }

    public event Action<bool> Thrown;

    float _handle;          // 0 down .. 1 up, as drawn
    bool _dragging;
    float _dragStartY, _dragStartHandle;
    bool _moved;

    public void SetOn(bool on, bool instant = false)
    {
        On = on;
        if (instant) _handle = on ? 1 : 0;
        SetProcess(true);
        Changed();
    }

    Rect2 Plate => new(0, 46, Size.X, Size.Y - 46 - 36);
    Rect2 Slot => new(Size.X * 0.64f - 16, Plate.Position.Y + 14, 32, Plate.Size.Y - 28);

    public override void _GuiInput(InputEvent e)
    {
        if (IsLeftPress(e, out bool pressed))
        {
            if (pressed)
            {
                _dragging = true;
                _moved = false;
                _dragStartY = ((InputEventMouseButton)e).Position.Y;
                _dragStartHandle = _handle;
            }
            else if (_dragging)
            {
                _dragging = false;
                bool on = _moved ? _handle > 0.5f : !On; // a plain click throws it
                SetOn(on);
                Thrown?.Invoke(On);
            }
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion motion && _dragging)
        {
            float travel = Slot.Size.Y - 50;
            _handle = Mathf.Clamp(_dragStartHandle - (motion.Position.Y - _dragStartY) / travel, 0, 1);
            _moved |= Mathf.Abs(motion.Position.Y - _dragStartY) > 6;
            Changed();
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        if (_dragging) return;
        float target = On ? 1 : 0;
        if (Mathf.IsEqualApprox(_handle, target))
        {
            SetProcess(false);
            return;
        }
        _handle = Mathf.MoveToward(_handle, target, (float)delta * 6f);
        Changed();
    }

    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;
        this.Text(UiTheme.Label, UiTheme.SizeLabel, Vector2.Zero, Caption, C.Text);
        Rect2 plate = Plate, slot = Slot;
        this.Block(plate, C.Panel);
        if (Hovered || _dragging) this.Frame(plate, C.Accent2, 4);

        // Positions are painted on the plate, left of the slot, like a real lever gate.
        var up = new Rect2(plate.Position + new Vector2(14, 14), new Vector2(slot.Position.X - plate.Position.X - 64, 44));
        var down = new Rect2(plate.Position.X + 14, plate.End.Y - 58, up.Size.X, 44);
        this.Block(up, On ? C.Good : C.Glass);
        this.Block(down, On ? C.Glass : C.Text);
        this.TextIn(UiTheme.Label, UiTheme.SizeLabel, up, UpLabel, On ? C.Glass : C.Dim, HorizontalAlignment.Center);
        this.TextIn(UiTheme.Label, UiTheme.SizeLabel, down, DownLabel, On ? C.Dim : C.Glass, HorizontalAlignment.Center);
        this.Block(slot, C.Glass);

        // The handle: a fat grip riding in the slot.
        float travel = slot.Size.Y - 50;
        float y = slot.End.Y - 25 - _handle * travel;
        var grip = new Rect2(slot.Position.X - 30, y - 23, slot.Size.X + 60, 46);
        this.Block(grip, On ? C.Accent : C.Accent.Darkened(0.2f));
        for (int i = 0; i < 3; i++)
            this.Block(new Rect2(grip.Position.X + 14, grip.Position.Y + 10 + i * 11, grip.Size.X - 28, 4), C.Glass);

        this.Text(UiTheme.Mono, UiTheme.SizeSmall, new Vector2(0, h - 28), Detail, Hovered ? C.Warn : C.Dim);
    }
}

/// <summary>One line in a <see cref="RowList"/>.</summary>
public sealed record RowItem(string Name, string Right, Color Tab, bool Highlight = false, bool Clickable = true, object Tag = null, string Badge = null);

/// <summary>
/// A list of chunky rows: a colour tab, a name, a number on the right. Highlighted rows are a
/// solid block of the second faction colour (the trader's "wanted"). Click a row to act on it.
/// </summary>
public partial class RowList : ScreenWidget
{
    public float RowHeight { get; init; } = 46;
    public float Gap { get; init; } = 6;
    public int FontSize { get; init; } = UiTheme.SizeLabel - 2;
    public IReadOnlyList<RowItem> Rows { get; private set; } = Array.Empty<RowItem>();

    public event Action<RowItem> Clicked;

    int _hover = -1;

    public void SetRows(IReadOnlyList<RowItem> rows)
    {
        Rows = rows;
        Changed();
    }

    public Rect2 RowRect(int i) => new(0, i * (RowHeight + Gap), Size.X, RowHeight);

    int RowAt(Vector2 p)
    {
        int i = (int)(p.Y / (RowHeight + Gap));
        return i >= 0 && i < Rows.Count && RowRect(i).HasPoint(p) ? i : -1;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m)
        {
            int hover = RowAt(m.Position);
            if (hover != _hover)
            {
                _hover = hover;
                Changed();
            }
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } b)
        {
            int i = RowAt(b.Position);
            if (i >= 0 && Rows[i].Clickable) Clicked?.Invoke(Rows[i]);
            AcceptEvent();
        }
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationMouseExit && _hover != -1)
        {
            _hover = -1;
            Changed();
        }
    }

    public override void _Draw()
    {
        int visible = (int)((Size.Y + Gap) / (RowHeight + Gap));
        for (int i = 0; i < Rows.Count && i < visible; i++)
        {
            RowItem row = Rows[i];
            Rect2 r = RowRect(i);
            bool hover = i == _hover && row.Clickable;
            Color ink = row.Highlight ? C.Glass : row.Clickable ? C.Text : C.Dim;
            this.Block(r, row.Highlight ? (hover ? C.Accent2.Lightened(0.2f) : C.Accent2) : hover ? C.Panel.Lightened(0.1f) : C.Panel);
            this.Block(new Rect2(r.Position, new Vector2(10, r.Size.Y)), row.Highlight ? C.Glass : row.Tab);
            this.TextIn(row.Highlight ? UiTheme.Heavy : UiTheme.Label, FontSize, r, row.Name, ink, padX: 24);
            float right = 16;
            if (row.Badge != null)
            {
                float bw = Paint.TextWidth(UiTheme.Label, 15, row.Badge) + 16;
                var badge = new Rect2(r.End.X - bw - 120, r.Position.Y + 11, bw, r.Size.Y - 22);
                this.Block(badge, C.Glass);
                this.TextIn(UiTheme.Label, 15, badge, row.Badge, C.Accent2, HorizontalAlignment.Center);
            }
            this.TextIn(UiTheme.Mono, FontSize, r, row.Right, ink, HorizontalAlignment.Right, right);
            if (hover) this.Frame(r, row.Highlight ? C.Glass : C.Accent2, 3);
        }
        if (Rows.Count > visible)
            this.Text(UiTheme.Label, 16, new Vector2(24, visible * (RowHeight + Gap) - 4), $"+{Rows.Count - visible} MORE", C.Dim);
    }
}

/// <summary>A flat colour-block button with big type. Hover brightens it; it never pops anything up.</summary>
public partial class BlockButton : ScreenWidget
{
    public string Text { get; set; } = "";
    public string Sub { get; set; } = "";
    public Color Colour { get; set; } = UiTheme.Colors.Accent;
    public Color TextColour { get; set; } = UiTheme.Colors.Glass;
    public bool Enabled { get; set; } = true;
    public bool Selected { get; set; }
    public int FontSize { get; set; } = UiTheme.SizeLabel;

    public event Action Pressed;

    bool _down;

    public override void _GuiInput(InputEvent e)
    {
        if (!IsLeftPress(e, out bool pressed)) return;
        if (pressed) _down = true;
        else if (_down)
        {
            _down = false;
            if (Enabled && Hovered) Pressed?.Invoke();
        }
        Changed();
        AcceptEvent();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        Color body = !Enabled ? C.Rule : _down ? Colour.Darkened(0.3f) : Hovered ? Colour.Lightened(0.18f) : Colour;
        if (!Enabled || Selected || Hovered) this.Block(rect, body);
        else
        {
            this.Block(rect, C.Panel);
            this.Block(new Rect2(0, 0, 12, Size.Y), body);    // unlit: just a colour tab
        }
        Color ink = !Enabled ? C.Dim : (Selected || Hovered) ? TextColour : C.Text;
        float pad = (Selected || Hovered || !Enabled) ? 20 : 28;
        Font font = Selected || Hovered ? UiTheme.Heavy : UiTheme.Label;
        if (string.IsNullOrEmpty(Sub))
            this.TextIn(font, FontSize, rect, Text, ink, padX: pad);
        else
        {
            this.Text(font, FontSize, new Vector2(pad, 8), Text, ink);
            this.Text(UiTheme.Mono, UiTheme.SizeSmall - 2, new Vector2(pad, Size.Y - 30), Sub, (Selected || Hovered) ? ink : C.Dim);
        }
    }
}
