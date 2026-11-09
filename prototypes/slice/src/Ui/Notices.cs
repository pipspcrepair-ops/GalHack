using System;
using System.Collections.Generic;
using DiegeticUi.Theme;
using Godot;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>What a notice is news of: each has its own little picture.</summary>
public enum NoticeKind { Level, Unlock, Learnt, Claimed, Made, Saved }

/// <summary>
/// News in a slot of its own (<see cref="HudLayout.Notices"/>, the top right beside the compass): a new
/// level, a tech unlock, a recipe learnt, a wreck claimed, an order made, a blueprint saved. Each is an icon,
/// a label and a value ("LEVEL · 4"); it slides in, holds a few seconds and fades. At most
/// <see cref="MostShown"/> show at once, one to a row of the slot, and the rest wait their turn, so nothing
/// ever lands on anything else. With a menu up the slot moves into the menu header's top right corner
/// (<see cref="HudLayout.MenuNotices"/>), which the menus keep clear. Hazards keep the alerts' row and the
/// suit's own messages keep its lines.
/// </summary>
public partial class Notices : CanvasLayer
{
    static Palette C => UiTheme.Colors;

    public Game.Game Game;

    /// <summary>The most notices shown at once (the slot's rows).</summary>
    public const int MostShown = 3;

    /// <summary>Seconds a notice takes to come in, holds, and takes to fade.</summary>
    public const double In = 0.25, Hold = 3.5, Fade = 0.75;

    /// <summary>A notice: its kind, label and value, and how many like it were folded into it while it waited.</summary>
    public sealed class Notice
    {
        public NoticeKind Kind;
        public string Label, Value;
        public int More;
        public double Since = -1;

        /// <summary>The value as shown: with "+N" for those folded into it.</summary>
        public string Shown => More > 0 ? $"{Value} +{More}" : Value;
    }

    readonly List<Notice> _shown = new();
    readonly List<Notice> _queue = new();
    Control _view;
    double _time;

    /// <summary>The notices up now, oldest first (tests read it).</summary>
    public IReadOnlyList<Notice> Shown => _shown;

    /// <summary>How many wait for a row.</summary>
    public int Waiting => _queue.Count;

    /// <summary>The most shown at once since the last <see cref="ResetMost"/> (tests).</summary>
    public int MostAtOnce { get; private set; }

    public void ResetMost() => MostAtOnce = _shown.Count;

    public override void _Ready()
    {
        Layer = 9; // with the alerts: over the visor, the Tab menu and the escape menu; under the start screen
        ProcessMode = ProcessModeEnum.Always; // timed while the escape menu is up
        _view = new Control { Name = "Notices", MouseFilter = Control.MouseFilterEnum.Ignore };
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _view.Draw += Draw;
        AddChild(_view);
    }

    /// <summary>
    /// News for the slot. One like the last one waiting (same kind and label) folds into it: a later level replaces
    /// the value, anything else counts on ("UNLOCKED · Steel block +2"), so a burst never queues for long.
    /// </summary>
    public void Post(NoticeKind kind, string label, string value)
    {
        if (_queue.Count > 0 && _queue[^1].Kind == kind && _queue[^1].Label == label)
        {
            if (kind == NoticeKind.Level) _queue[^1].Value = value;
            else _queue[^1].More++;
            return;
        }
        _queue.Add(new Notice { Kind = kind, Label = label, Value = value });
        Step(0);
    }

    /// <summary>How strongly a notice shows now: up from nothing as it comes in, full while it holds, down to nothing as it fades.</summary>
    public double Alpha(Notice n)
    {
        double age = _time - n.Since;
        if (n.Since < 0 || age < 0) return 0;
        if (age < In) return age / In;
        if (age < In + Hold) return 1;
        return Math.Clamp(1 - (age - In - Hold) / Fade, 0, 1);
    }

    public override void _Process(double delta) => Step(delta);

    void Step(double delta)
    {
        _time += delta;
        _shown.RemoveAll(n => _time - n.Since >= In + Hold + Fade);
        while (_shown.Count < MostShown && _queue.Count > 0)
        {
            Notice n = _queue[0];
            _queue.RemoveAt(0);
            n.Since = _time;
            _shown.Add(n);
        }
        MostAtOnce = Math.Max(MostAtOnce, _shown.Count);
        _view?.QueueRedraw();
    }

    /// <summary>Where the slot is now, in the window's pixels: the menu header's corner with a menu up, else the visor's.</summary>
    public Rect2 Slot(Vector2 size)
    {
        bool menu = Game != null && (Game.Menu?.IsOpen == true || Game.Escape?.IsOpen == true);
        return menu ? HudLayout.MenuToScreen(HudLayout.MenuNotices, size) : HudLayout.Compute(size, Game?.Visor?.UiScale ?? 1f, Game?.Visor?.MarkersInWorld == false).Notices;
    }

    /// <summary>The slot's rows, top to bottom: one per notice shown at once, never on one another.</summary>
    public static List<Rect2> Rows(Rect2 slot)
    {
        var rows = new List<Rect2>();
        float gap = slot.Size.Y * HudLayout.NoticeGap / (MostShown * HudLayout.NoticeRowH + (MostShown - 1) * HudLayout.NoticeGap);
        float h = (slot.Size.Y - (MostShown - 1) * gap) / MostShown;
        for (int i = 0; i < MostShown; i++) rows.Add(new Rect2(slot.Position.X, slot.Position.Y + i * (h + gap), slot.Size.X, h));
        return rows;
    }

    // ---- Drawing ----------------------------------------------------------------------------------

    void Draw()
    {
        Vector2 size = _view.Size;
        if (size.Y < 100f || Game?.Survivor == null || _shown.Count == 0) return; // headless, or nothing yet
        List<Rect2> rows = Rows(Slot(size));
        for (int i = 0; i < _shown.Count && i < rows.Count; i++)
        {
            Notice n = _shown[i];
            float a = (float)Alpha(n);
            if (a <= 0.01f) continue;
            // It slides in from the right edge of its row as it comes in.
            Rect2 r = rows[i];
            float slide = (float)Math.Clamp((_time - n.Since) / In, 0, 1);
            r.Position += new Vector2((1f - slide) * r.Size.X * 0.15f, 0f);
            Draw(n, r, a);
        }
    }

    void Draw(Notice n, Rect2 r, float a)
    {
        float h = r.Size.Y;
        Color tone = Tone(n.Kind);
        _view.Block(r, new Color(C.Glass, 0.82f * a));
        _view.Block(new Rect2(r.Position, new Vector2(MathF.Max(2f, h * 0.12f), h)), new Color(tone, a));
        float icon = h * 0.62f;
        Vector2 centre = r.Position + new Vector2(h * 0.18f + h * 0.5f, h * 0.5f);
        Glyph(n.Kind, centre, icon / 2f, new Color(tone, a));
        float x = r.Position.X + h * 1.2f, room = r.End.X - x - h * 0.2f;
        int labelSize = Math.Max(8, (int)(h * 0.46f)), valueSize = Math.Max(8, (int)(h * 0.5f));
        string label = n.Label;
        float labelW = Paint.TextWidth(UiTheme.Display, labelSize, label);
        _view.TextIn(UiTheme.Display, labelSize, new Rect2(x, r.Position.Y, labelW + 2, h), label, new Color(tone, a));
        float vx = x + labelW + h * 0.3f;
        string value = HudLayout.Fit(UiTheme.Label, valueSize, n.Shown, MathF.Max(0f, r.End.X - vx - h * 0.2f));
        if (room > labelW) _view.TextIn(UiTheme.Label, valueSize, new Rect2(vx, r.Position.Y, r.End.X - vx, h), value, new Color(C.Text, a));
    }

    static Color Tone(NoticeKind kind) => kind switch
    {
        NoticeKind.Level => C.Accent,
        NoticeKind.Unlock or NoticeKind.Learnt => C.Holo,
        NoticeKind.Claimed => C.Good,
        _ => C.Accent2,
    };

    /// <summary>A small line picture of the kind, centred on <paramref name="c"/>, <paramref name="r"/> its half-size.</summary>
    void Glyph(NoticeKind kind, Vector2 c, float r, Color tone)
    {
        float w = MathF.Max(1.5f, r * 0.22f);
        void Line(float x0, float y0, float x1, float y1) => _view.DrawLine(c + new Vector2(x0, y0) * r, c + new Vector2(x1, y1) * r, tone, w);
        switch (kind)
        {
            case NoticeKind.Level: // two chevrons up
                Line(-0.8f, 0.1f, 0f, -0.7f); Line(0f, -0.7f, 0.8f, 0.1f);
                Line(-0.8f, 0.8f, 0f, 0f); Line(0f, 0f, 0.8f, 0.8f);
                break;
            case NoticeKind.Unlock: // an open padlock
                _view.DrawRect(new Rect2(c + new Vector2(-0.7f, -0.05f) * r, new Vector2(1.4f, 0.95f) * r), tone, false, w);
                _view.DrawArc(c + new Vector2(0.35f, -0.4f) * r, 0.42f * r, Mathf.Pi, Mathf.Tau, 10, tone, w);
                Line(0.77f, -0.4f, 0.77f, -0.2f);
                break;
            case NoticeKind.Learnt: // a sheet with lines on it
                _view.DrawRect(new Rect2(c + new Vector2(-0.6f, -0.85f) * r, new Vector2(1.2f, 1.7f) * r), tone, false, w);
                Line(-0.35f, -0.4f, 0.35f, -0.4f); Line(-0.35f, 0f, 0.35f, 0f); Line(-0.35f, 0.4f, 0.15f, 0.4f);
                break;
            case NoticeKind.Claimed: // a flag on a pole
                Line(-0.6f, -0.85f, -0.6f, 0.85f);
                _view.DrawColoredPolygon(new[] { c + new Vector2(-0.6f, -0.85f) * r, c + new Vector2(0.75f, -0.5f) * r, c + new Vector2(-0.6f, -0.15f) * r }, tone);
                break;
            case NoticeKind.Made: // a crate
                _view.DrawRect(new Rect2(c + new Vector2(-0.75f, -0.55f) * r, new Vector2(1.5f, 1.3f) * r), tone, false, w);
                Line(-0.75f, -0.15f, 0.75f, -0.15f); Line(0f, -0.55f, 0f, -0.15f);
                break;
            default: // saved: an arrow down into a tray
                Line(0f, -0.85f, 0f, 0.3f); Line(-0.4f, -0.1f, 0f, 0.3f); Line(0.4f, -0.1f, 0f, 0.3f);
                Line(-0.8f, 0.2f, -0.8f, 0.8f); Line(-0.8f, 0.8f, 0.8f, 0.8f); Line(0.8f, 0.8f, 0.8f, 0.2f);
                break;
        }
    }
}
