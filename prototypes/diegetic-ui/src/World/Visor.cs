using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Logic;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
using Godot;

namespace DiegeticUi.World;

/// <summary>
/// The helmet visor: the only "HUD", and it is part of the suit. Minimal and curved to the
/// glass: a compass arcing across the top, vitals along the bottom curve, a mini-map in the
/// corner. When all is well the vitals fade away; a vital flashes while it is still only a
/// warning, before it costs health. The readouts lag a touch behind fast head turns, as if
/// projected on glass that moves with you.
///
/// Drawn every frame straight onto the main canvas (no SubViewport): it is always in view,
/// and it is a few dozen draw calls.
/// </summary>
public partial class Visor : CanvasLayer
{
    static Palette C => UiTheme.Colors;

    public GameState State { get; init; }
    public Player Player { get; init; }
    public Interactor Interactor { get; init; }
    public Camera3D Camera { get; init; }

    /// <summary>0..1, set by the game from where you stand: ice on the glass.</summary>
    public float FrostTarget { get; set; }

    Control _hud;
    ColorRect _glass;
    ShaderMaterial _glassMaterial;

    Vector2 _lag;
    float _vitalsAlpha = 1, _calm, _frost, _harm, _focusFade = 1, _vitalsFocus = 1;
    double _time;
    readonly List<(string Text, Color Colour, double Until)> _messages = new();
    (string Title, int Minutes, double Until) _banner;
    Vector3 _fireFrom;
    double _fireUntil = -1;

    public override void _Ready()
    {
        Layer = 1;
        // The glass (frost, reflections, the helmet's edge) first, then the readouts: they are
        // projected on the inside of the glass, so frost on the outside never hides them.
        _glassMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/visor_glass.gdshader") };
        _glass = new ColorRect { Name = "Glass", MouseFilter = Control.MouseFilterEnum.Ignore, Material = _glassMaterial };
        _glass.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_glass);

        _hud = new Control { Name = "Hud", MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hud.Draw += DrawHud;
        AddChild(_hud);
    }

    // ---- What the game tells the visor ---------------------------------------------------

    /// <summary>A line from VESTA, the suit's assistant, at the lower left.</summary>
    public void Say(string text, Color? colour = null)
    {
        _messages.RemoveAll(m => m.Text == text);
        _messages.Add((text, colour ?? C.Holo, _time + 5));
        if (_messages.Count > 4) _messages.RemoveAt(0);
    }

    public void Announce(Announcement a)
    {
        _banner = (a.Threat.Title, a.MinutesLeft, _time + 3.2);
        Say(a.MinutesLeft > 0 ? $"VESTA: {a.Threat.Title}, {a.MinutesLeft} MIN" : "VESTA: CONTACT. THEY ARE HERE", C.Danger);
    }

    /// <summary>Incoming fire from a world position: the glass flashes on that side for a moment.</summary>
    public void UnderFire(Vector3 from)
    {
        _fireFrom = from;
        _fireUntil = _time + 1.3;
    }

    /// <summary>Is this vital showing a warning (flashing) right now? Tests read it.</summary>
    public bool Flashing(VitalKind kind) => State.Vitals.All.First(v => v.Kind == kind).State != VitalState.Ok;

    public float VitalsAlpha => _vitalsAlpha;

    /// <summary>True on the "on" beat of a warning blink. Screenshots wait for it.</summary>
    public bool FlashLit => !State.Vitals.AllWell && Mathf.PosMod((float)_time * 2.2f, 1f) is > 0.1f and < 0.5f;

    // ---- Per frame ------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += delta;
        float s = UiScale();

        // Inertia: the readouts trail a fast head turn by a few pixels, then settle.
        Vector2 target = (Player.LookRate * 12f * s).LimitLength(26f * s);
        _lag = _lag.Lerp(target, 1 - Mathf.Exp(-dt * 9f));
        _hud.Position = _lag;

        // Fade the vitals out once everything has been fine for a few seconds.
        bool calm = State.Vitals.AllWell && State.Warnings.Next == null;
        _calm = calm ? _calm + dt : 0;
        _vitalsAlpha = Mathf.MoveToward(_vitalsAlpha, _calm > 3f ? 0f : 1f, dt * (calm ? 0.8f : 4f));

        // Using a framed screen, the visor gets out of the way (only warnings stay); with the
        // wrist up it dims.
        FocusMachine<IFocusTarget> focus = Interactor.Focus;
        float focusTarget = !focus.Busy ? 1f : focus.Target.MovesCamera ? 0f : 0.45f;
        _focusFade = Mathf.MoveToward(_focusFade, focusTarget, dt * 4f);
        _vitalsFocus = Mathf.MoveToward(_vitalsFocus, focus.Busy ? 0f : 1f, dt * 4f); // calm vitals step aside for any screen

        _frost = Mathf.MoveToward(_frost, FrostTarget, dt * (FrostTarget > _frost ? 0.12f : 0.3f));
        _harm = Mathf.MoveToward(_harm, State.Vitals.CostingHealth ? 1f : 0f, dt * 2f);
        _glassMaterial.SetShaderParameter("frost", _frost);
        _glassMaterial.SetShaderParameter("harm", _harm);
        _glassMaterial.SetShaderParameter("aspect", _hud.Size.X / Mathf.Max(1, _hud.Size.Y));

        float fire = _time < _fireUntil ? (float)((_fireUntil - _time) / 1.3) : 0;
        _glassMaterial.SetShaderParameter("fire", fire);
        if (fire > 0) _glassMaterial.SetShaderParameter("fire_dir", FireDirection());

        _messages.RemoveAll(m => m.Until < _time);
        _hud.QueueRedraw();
    }

    float UiScale() => _hud.Size.Y / 900f;

    /// <summary>Screen-space direction (y down) toward the shooter, from the camera's point of view.</summary>
    Vector2 FireDirection()
    {
        Vector3 local = Camera.GlobalBasis.Inverse() * (_fireFrom - Camera.GlobalPosition);
        var dir = new Vector2(local.X, -local.Y);
        if (dir.LengthSquared() < 1e-4f) dir = Vector2.Down; // dead behind: show it low
        return dir.Normalized();
    }

    static Color A(Color c, float alpha) => new(c, c.A * alpha);

    // ---- Drawing ----------------------------------------------------------------------------

    void DrawHud()
    {
        Vector2 size = _hud.Size;
        if (size.Y < 100) return; // not laid out yet
        float s = UiScale();
        DrawCompass(size, s);
        DrawMiniMap(new Vector2(size.X - 150 * s, 158 * s), 100 * s, s);
        DrawVitals(size, s);
        DrawWarnings(size, s);
        DrawPrompt(size, s);
        DrawMessages(size, s);
        DrawFireChevron(size, s);
    }

    void DrawCompass(Vector2 size, float s)
    {
        float alpha = 0.75f * _focusFade;
        if (alpha < 0.01f) return;
        float radius = 1500 * s;
        var centre = new Vector2(size.X / 2, 58 * s + radius);
        float heading = Mathf.PosMod(-Mathf.RadToDeg(Player.Yaw), 360);
        const float span = 54, k = 0.0052f;

        Vector2 Arc(float rel, float lift = 0) =>
            centre + new Vector2(Mathf.Sin(rel * k), -Mathf.Cos(rel * k)) * (radius + lift);
        float Fade(float rel) => 1 - Mathf.Pow(Mathf.Abs(rel) / span, 3);

        var line = new Vector2[25];
        for (int i = 0; i < line.Length; i++) line[i] = Arc(-span + i * span * 2 / (line.Length - 1));
        _hud.DrawPolyline(line, A(C.Text, alpha * 0.45f), 2 * s, true);

        int first = (int)Mathf.Floor((heading - span) / 5) * 5;
        for (int h = first; h <= heading + span; h += 5)
        {
            float rel = h - heading;
            if (Mathf.Abs(rel) > span) continue;
            int deg = (int)Mathf.PosMod(h, 360);
            float a = alpha * Fade(rel);
            bool major = deg % 15 == 0;
            _hud.DrawLine(Arc(rel), Arc(rel, (major ? -14 : -7) * s), A(C.Text, a), (major ? 3 : 2) * s, true);
            string label = deg switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W", _ => deg % 30 == 0 ? (deg / 10).ToString("00") : null };
            if (label == null) continue;
            bool cardinal = deg % 90 == 0;
            Font font = cardinal ? UiTheme.Display : UiTheme.Label;
            int fs = (int)((cardinal ? 22 : 16) * s);
            Vector2 at = Arc(rel, -22 * s);
            _hud.DrawSetTransform(at, rel * k);
            _hud.Text(font, fs, new Vector2(-40 * s, -fs - 2 * s), label, A(cardinal ? C.Accent2 : C.Text, a), HorizontalAlignment.Center, 80 * s);
            _hud.DrawSetTransform(Vector2.Zero);
        }

        // Your heading, under a notch.
        Vector2 top = Arc(0, 6 * s);
        _hud.DrawColoredPolygon(new[] { top, top + new Vector2(-7, 10) * s, top + new Vector2(7, 10) * s }, A(C.Accent2, alpha));
        _hud.Text(UiTheme.Mono, (int)(17 * s), top + new Vector2(-40 * s, 12 * s), ((int)heading).ToString("000"), A(C.Text, alpha), HorizontalAlignment.Center, 80 * s);

        // Contacts: the waypoint set at the holo-table, and the depot.
        foreach (var (body, colour, diamond) in Contacts())
        {
            float rel = Mathf.Wrap(Bearing(body) - heading, -180, 180);
            bool off = Mathf.Abs(rel) > span;
            float shown = Mathf.Clamp(rel, -span, span);
            Vector2 p = Arc(shown, 12 * s);
            float r = 7 * s;
            Vector2[] mark = diamond
                ? new[] { p + new Vector2(0, -r), p + new Vector2(r, 0), p + new Vector2(0, r), p + new Vector2(-r, 0) }
                : new[] { p + new Vector2(-r, -r) * 0.8f, p + new Vector2(r, -r) * 0.8f, p + new Vector2(r, r) * 0.8f, p + new Vector2(-r, r) * 0.8f };
            _hud.DrawColoredPolygon(mark, A(colour, alpha));
            if (off) _hud.Text(UiTheme.Label, (int)(15 * s), p + new Vector2(rel < 0 ? 10 * s : -30 * s, -8 * s), rel < 0 ? "<" : ">", A(colour, alpha));
            _hud.Text(UiTheme.Label, (int)(14 * s), p + new Vector2(-60 * s, 10 * s), body.Name, A(colour, alpha * 0.9f), HorizontalAlignment.Center, 120 * s);
        }
    }

    IEnumerable<(Body, Color, bool)> Contacts()
    {
        Body depot = State.Map.Bodies.FirstOrDefault(b => b.Kind == BodyKind.Station);
        if (depot != null && depot != State.Map.Selected) yield return (depot, C.Good, false);
        if (State.Map.Selected is { Orbit: > 0 } waypoint) yield return (waypoint, C.Accent2, true);
    }

    /// <summary>Compass bearing from the ship to a body, degrees, 0 = north (map +Y).</summary>
    float Bearing(Body body)
    {
        var (sx, sy) = State.Map.Ship.PositionAt(State.Map.Clock);
        var (bx, by) = body.PositionAt(State.Map.Clock);
        return Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2((float)(bx - sx), (float)(by - sy))), 360);
    }

    void DrawMiniMap(Vector2 centre, float radius, float s)
    {
        float alpha = 0.85f * _focusFade;
        if (alpha < 0.01f) return;
        StarMap map = State.Map;
        float scale = radius / 1.3f;
        double t = map.Clock;
        Vector2 At((double X, double Y) p) => centre + new Vector2((float)p.X, -(float)p.Y) * scale;

        // Unknown space is dark; the surveyed part of the system is a lighter disc.
        float known = (float)map.Bodies.Where(b => b.Discovered).Max(b => b.Orbit) + 0.12f;
        _hud.DrawCircle(centre, radius, A(new Color(0.02f, 0.03f, 0.05f, 0.75f), alpha));
        _hud.DrawCircle(centre, known * scale, A(new Color(0.09f, 0.16f, 0.24f, 0.7f), alpha));
        for (int i = 0; i < 24; i++)
        {
            float ang = i * Mathf.Tau / 24;
            Vector2 d = Vector2.Right.Rotated(ang);
            _hud.DrawLine(centre + d * (known * scale + 6 * s), centre + d * (radius - 4 * s), A(C.Rule, alpha * 0.6f), 1.5f * s);
        }

        foreach (Body body in map.Bodies)
        {
            if (body.Orbit > 0 && body.Discovered)
                _hud.DrawArc(centre, (float)body.Orbit * scale, 0, Mathf.Tau, 48, A(C.Holo, alpha * 0.3f), 1.5f * s, true);
            Vector2 p = At(body.PositionAt(t));
            if (!body.Discovered)
            {
                _hud.Text(UiTheme.Label, (int)(14 * s), p - new Vector2(10 * s, 9 * s), "?", A(new Color("8b6fb3"), alpha), HorizontalAlignment.Center, 20 * s);
                continue;
            }
            float r = body.Kind == BodyKind.Star ? 6 * s : 3.5f * s;
            _hud.DrawCircle(p, r, A(Paint.Rgb(body.Color), alpha));
        }

        if (map.Selected is { } sel)
        {
            Vector2 p = At(sel.PositionAt(t));
            _hud.DrawArc(p, 9 * s, 0, Mathf.Tau, 20, A(C.Accent2, alpha), 2.5f * s, true);
            _hud.Text(UiTheme.Label, (int)(14 * s), p + new Vector2(12 * s, -9 * s), sel.Name, A(C.Accent2, alpha));
        }

        // You: an arrowhead pointing the way you face.
        Vector2 you = At(map.Ship.PositionAt(t));
        Vector2 f = Vector2.Up.Rotated(-Player.Yaw);
        Vector2 side = new(-f.Y, f.X);
        _hud.DrawColoredPolygon(new[] { you + f * 9 * s, you - f * 6 * s + side * 6 * s, you - f * 3 * s, you - f * 6 * s - side * 6 * s }, A(C.Accent2, alpha));

        _hud.DrawArc(centre, radius, 0, Mathf.Tau, 64, A(C.Text, alpha * 0.5f), 2 * s, true);
        _hud.Text(UiTheme.Display, (int)(15 * s), centre + new Vector2(-20 * s, -radius - 22 * s), "N", A(C.Accent2, alpha), HorizontalAlignment.Center, 40 * s);
    }

    void DrawVitals(Vector2 size, float s)
    {
        if (_vitalsAlpha < 0.01f) return;
        IReadOnlyList<Vital> vitals = State.Vitals.All;
        float w = 166 * s, h = 70 * s, gap = 14 * s;
        float total = vitals.Count * w + (vitals.Count - 1) * gap;
        float x0 = size.X / 2 - total / 2;
        float curve = 0.00016f / s;

        for (int i = 0; i < vitals.Count; i++)
        {
            Vital v = vitals[i];
            float cx = x0 + i * (w + gap) + w / 2, dx = cx - size.X / 2;
            float cy = size.Y - 62 * s - h / 2 - curve * dx * dx;    // rising toward the edges of the glass
            float tilt = Mathf.Atan(-2 * curve * dx);
            _hud.DrawSetTransform(new Vector2(cx, cy), tilt);

            VitalState state = v.State;
            // Warning blinks slow; critical blinks fast. Either way the block itself lights up.
            bool lit = state switch
            {
                VitalState.Warning => Mathf.PosMod((float)_time * 2.2f, 1f) < 0.6f,
                VitalState.Critical => Mathf.PosMod((float)_time * 4.5f, 1f) < 0.55f,
                _ => false,
            };
            Color tone = state switch { VitalState.Warning => C.Warn, VitalState.Critical => C.Danger, _ => C.Good };
            float a = state == VitalState.Ok ? _vitalsAlpha * _vitalsFocus : 1;
            if (a < 0.01f)
            {
                _hud.DrawSetTransform(Vector2.Zero);
                continue;
            }
            var rect = new Rect2(-w / 2, -h / 2, w, h);
            _hud.DrawRect(rect, A(lit ? tone : new Color(0.03f, 0.05f, 0.09f, 0.62f), a));
            _hud.DrawRect(new Rect2(rect.Position, new Vector2(8 * s, h)), A(tone, a));
            Color ink = lit ? C.Glass : state == VitalState.Ok ? C.Text : tone;
            _hud.Text(UiTheme.Label, (int)(17 * s), rect.Position + new Vector2(18 * s, 6 * s), v.Label, A(lit ? C.Glass : C.Dim, a));
            _hud.Text(UiTheme.Mono, (int)(25 * s), rect.Position + new Vector2(0, 2 * s), Reading(v), A(ink, a), HorizontalAlignment.Right, w - 10 * s);
            _hud.Segments(new Rect2(rect.Position + new Vector2(18 * s, h - 20 * s), new Vector2(w - 30 * s, 10 * s)),
                v.Fraction * 10, 10, A(lit ? C.Glass : tone, a), 0, 10, 3 * s, a);

            if (state != VitalState.Ok)
            {
                // Say what it means in two words, above the block: before harm, after harm.
                string tag = state == VitalState.Critical ? $"-{State.Vitals.DamagePerSecond:0} HP/S" : Advice(v);
                _hud.Text(UiTheme.Label, (int)(16 * s), rect.Position + new Vector2(0, -24 * s), tag, A(tone, a), HorizontalAlignment.Center, w);
            }
            _hud.DrawSetTransform(Vector2.Zero);
        }
    }

    static string Reading(Vital v) => v.Kind switch
    {
        VitalKind.Temperature => $"{v.Value:0.0}°",
        VitalKind.Radiation => $"{v.Value:0}",
        _ => $"{v.Value:0}",
    };

    static string Advice(Vital v) => v.Kind switch
    {
        VitalKind.Temperature => v.Value < 37 ? "TOO COLD" : "TOO HOT",
        VitalKind.Oxygen => "LOW O2",
        VitalKind.Food => "EAT SOON",
        VitalKind.Radiation => "GET OUT",
        _ => "HURT",
    };

    void DrawWarnings(Vector2 size, float s)
    {
        WarningTimeline w = State.Warnings;
        Threat threat = w.Next;

        if (_time < _banner.Until)
        {
            // A fresh announcement: a big block across the upper glass, blinking at first.
            double age = _banner.Until - _time;
            bool blink = age > 1.8 && Mathf.PosMod((float)_time * 3, 1) < 0.35f;
            var r = new Rect2(size.X / 2 - 400 * s, 150 * s, 800 * s, 124 * s);
            _hud.DrawRect(r, blink ? C.Text : C.Danger);
            _hud.Stripes(new Rect2(r.Position, new Vector2(56 * s, r.Size.Y)), C.Glass, C.Danger, 12 * s);
            _hud.Stripes(new Rect2(r.End.X - 56 * s, r.Position.Y, 56 * s, r.Size.Y), C.Glass, C.Danger, 12 * s);
            _hud.Text(UiTheme.Display, (int)(26 * s), r.Position + new Vector2(0, 14 * s), _banner.Title, C.Glass, HorizontalAlignment.Center, r.Size.X);
            string big = _banner.Minutes switch { 0 => "CONTACT", 1 => "1 MINUTE", _ => $"{_banner.Minutes} MINUTES" };
            _hud.Text(UiTheme.Mono, (int)(52 * s), r.Position + new Vector2(0, 46 * s), big, C.Glass, HorizontalAlignment.Center, r.Size.X);
        }
        else if (threat != null)
        {
            var r = new Rect2(size.X / 2 - 190 * s, 128 * s, 380 * s, 46 * s);
            _hud.DrawRect(r, new Color(C.Danger, 0.9f));
            _hud.TextIn(UiTheme.Label, (int)(20 * s), r, "INVASION", C.Glass, padX: 16 * s);
            _hud.TextIn(UiTheme.Mono, (int)(30 * s), r, "T-" + Paint.Clock(w.Remaining(threat)), C.Glass, HorizontalAlignment.Right, 16 * s);
        }
    }

    void DrawPrompt(Vector2 size, float s)
    {
        FocusMachine<IFocusTarget> focus = Interactor.Focus;
        var centre = size / 2;
        if (focus.State == FocusState.Free)
        {
            _hud.DrawCircle(centre, 2.6f * s, new Color(C.Text, 0.85f));
            if (Interactor.LookTarget is { } target)
            {
                _hud.DrawArc(centre, 11 * s, 0, Mathf.Tau, 24, new Color(C.Accent2, 0.9f), 2 * s, true);
                Chip(new Vector2(size.X / 2, size.Y / 2 + 52 * s), "E", target.Prompt, s);
            }
            return;
        }
        if (focus.State == FocusState.Using)
        {
            bool wrist = focus.Target is { MovesCamera: false };
            Chip(new Vector2(size.X / 2, wrist ? 34 * s + 150 * s : size.Y - 26 * s), wrist ? "TAB" : "ESC", wrist ? "LOWER" : "STEP BACK", s, 0.7f);
        }
    }

    /// <summary>A key cap and a word: the visor's only kind of prompt.</summary>
    void Chip(Vector2 centre, string key, string text, float s, float alpha = 1)
    {
        int fs = (int)(19 * s);
        float kw = Paint.TextWidth(UiTheme.Display, fs, key) + 18 * s;
        float tw = Paint.TextWidth(UiTheme.Label, fs, text) + 24 * s;
        float h = 34 * s, x = centre.X - (kw + tw) / 2, y = centre.Y - h / 2;
        _hud.DrawRect(new Rect2(x, y, kw + tw, h), new Color(0.03f, 0.05f, 0.09f, 0.65f * alpha));
        _hud.DrawRect(new Rect2(x, y, kw, h), new Color(C.Accent2, alpha));
        _hud.TextIn(UiTheme.Display, fs, new Rect2(x, y, kw, h), key, C.Glass, HorizontalAlignment.Center);
        _hud.TextIn(UiTheme.Label, fs, new Rect2(x + kw, y, tw, h), text, new Color(C.Text, alpha), HorizontalAlignment.Center);
    }

    void DrawMessages(Vector2 size, float s)
    {
        for (int i = 0; i < _messages.Count; i++)
        {
            var (text, colour, until) = _messages[_messages.Count - 1 - i];
            float a = Mathf.Clamp((float)(until - _time) / 0.6f, 0, 1) * _focusFade;
            _hud.Text(UiTheme.Label, (int)(19 * s), new Vector2(46 * s, size.Y - 210 * s - i * 28 * s), text, A(colour, a));
        }
    }

    void DrawFireChevron(Vector2 size, float s)
    {
        if (_time >= _fireUntil) return;
        float a = (float)((_fireUntil - _time) / 1.3);
        Vector2 dir = FireDirection();
        Vector2 p = size / 2 + new Vector2(dir.X * size.X * 0.36f, dir.Y * size.Y * 0.36f);
        Vector2 side = new(-dir.Y, dir.X);
        for (int i = 0; i < 2; i++)
        {
            Vector2 tip = p + dir * (i * 16 * s);
            _hud.DrawColoredPolygon(new[] { tip + dir * 14 * s, tip + side * 16 * s, tip + side * 9 * s - dir * 2 * s, tip + dir * 5 * s, tip - side * 9 * s - dir * 2 * s, tip - side * 16 * s },
                A(new Color("ff7a2a"), a));
        }
        _hud.Text(UiTheme.Label, (int)(16 * s), p - dir * 34 * s - new Vector2(60 * s, 10 * s), "WARNING SHOT", A(new Color("ff7a2a"), a), HorizontalAlignment.Center, 120 * s);
    }
}
