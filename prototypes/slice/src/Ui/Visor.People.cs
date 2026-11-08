using System;
using System.Collections.Generic;
using DiegeticUi.Theme;
using Godot;
using Slice.Game;
using Wildlife;

namespace Slice.Ui;

/// <summary>
/// The visor's people parts: the gesture wheel while its key is held (a ring of seven, an icon
/// and one word each, the pointer pushed by the mouse), the gesture you just made, a town's
/// standing with you as an icon and a number, and (an option, off by default) a small marker at
/// the screen edge toward where a significant sound came from.
/// </summary>
public partial class Visor
{
    readonly List<(Vector3 at, float weight, double at_)> _cues = new();
    bool _cuesHooked;

    static string WheelWord(PlayerGesture g) => g switch
    {
        PlayerGesture.Wave => "WAVE", PlayerGesture.Beckon => "BECKON", PlayerGesture.HandsUp => "HANDS UP", PlayerGesture.Stop => "STOP",
        PlayerGesture.Point => "POINT", PlayerGesture.Threaten => "THREATEN", _ => "THANKS",
    };

    /// <summary>Significant sounds marked since the option was turned on (tests).</summary>
    public int CuesMarked { get; private set; }

    public override void _EnterTree()
    {
        if (_cuesHooked) return;
        _cuesHooked = true;
        SoundCues.Heard += OnCue;
    }

    public override void _ExitTree()
    {
        SoundCues.Heard -= OnCue;
        _cuesHooked = false;
    }

    void OnCue(Vector3 at, float w)
    {
        if (Game?.Audio?.Settings is not { SoundDirection: true }) return;
        _cues.Add((at, w, _time));
        CuesMarked++;
        if (_cues.Count > 12) _cues.RemoveAt(0);
    }

    void DrawPeople()
    {
        DrawSoundMarkers();
        DrawStanding();
        if (Game.WheelOpen) DrawWheel();
        else if (Game.LastGesture is PlayerGesture g && Time.GetTicksMsec() / 1000.0 - Game.LastGestureAt < 1.4)
        {
            float s = S;
            Vector2 at = _hud.Size * 0.5f + new Vector2(0f, 120f * s);
            GestureIcon(g, at, 22f * s, new Color(C.Accent, 0.85f), s);
        }
    }

    void DrawWheel()
    {
        float s = S;
        Vector2 c = _hud.Size * 0.5f;
        float r0 = 70f * s, r1 = 190f * s;
        int n = Slice.Game.Game.Wheel.Length;
        _hud.DrawCircle(c, r1 + 8f * s, new Color(C.Glass, 0.45f));
        for (int i = 0; i < n; i++)
        {
            float a0 = -Mathf.Pi * 0.5f + (i - 0.5f) * Mathf.Tau / n, a1 = a0 + Mathf.Tau / n;
            bool on = i == Game.WheelPick;
            var pts = new List<Vector2>();
            for (int k = 0; k <= 8; k++) pts.Add(c + Vector2.FromAngle(Mathf.Lerp(a0, a1, k / 8f) + 0.01f) * r1);
            for (int k = 8; k >= 0; k--) pts.Add(c + Vector2.FromAngle(Mathf.Lerp(a0, a1, k / 8f) + 0.01f) * r0);
            _hud.DrawColoredPolygon(pts.ToArray(), on ? new Color(C.Accent, 0.55f) : new Color(C.Glass, 0.7f));
            _hud.DrawPolyline(pts.ToArray(), new Color(C.Dim, 0.8f), 1.5f * s);
            float mid = (a0 + a1) * 0.5f;
            Vector2 at = c + Vector2.FromAngle(mid) * (r0 + r1) * 0.5f;
            PlayerGesture g = Slice.Game.Game.Wheel[i];
            GestureIcon(g, at - new Vector2(0f, 12f * s), 20f * s, on ? C.Text : C.Dim, s);
            string word = WheelWord(g);
            int size = (int)(14 * s);
            float w = UiTheme.Label.GetStringSize(word, HorizontalAlignment.Left, -1, size).X;
            _hud.Text(UiTheme.Label, size, at + new Vector2(-w * 0.5f, 26f * s), word, on ? C.Text : C.Dim);
        }
        _hud.DrawCircle(c + Game.WheelAim * r0 * 0.9f, 5f * s, C.Accent);
    }

    /// <summary>A gesture as a few strokes: a raised hand, a curling call, two hands up, a flat palm, an arrow, a fist, a hand on the heart.</summary>
    void GestureIcon(PlayerGesture g, Vector2 at, float r, Color ink, float s)
    {
        float w = 2.2f * s;
        void Line(Vector2 a, Vector2 b) => _hud.DrawLine(at + a * r, at + b * r, ink, w, true);
        void Hand(Vector2 o, float tilt)
        {
            _hud.DrawCircle(at + o * r, 0.28f * r, ink);
            for (int f = -1; f <= 2; f++)
            {
                Vector2 d = Vector2.FromAngle(-Mathf.Pi * 0.5f + tilt + f * 0.32f);
                Line(o + d * 0.22f, o + d * 0.6f);
            }
        }
        switch (g)
        {
            case PlayerGesture.Wave:
                Hand(new Vector2(0f, 0.15f), 0.25f);
                _hud.DrawArc(at + new Vector2(0.15f, 0.1f) * r, 0.75f * r, -1.2f, -0.2f, 6, ink, w);
                break;
            case PlayerGesture.Beckon:
                _hud.DrawArc(at, 0.55f * r, 0.6f, 4.8f, 10, ink, w);
                Line(new Vector2(-0.1f, -0.55f), new Vector2(0.15f, -0.75f));
                Line(new Vector2(-0.1f, -0.55f), new Vector2(0.18f, -0.35f));
                break;
            case PlayerGesture.HandsUp:
                Hand(new Vector2(-0.45f, 0.2f), -0.15f);
                Hand(new Vector2(0.45f, 0.2f), 0.15f);
                break;
            case PlayerGesture.Stop:
                _hud.DrawRect(new Rect2(at + new Vector2(-0.3f, -0.1f) * r, new Vector2(0.6f, 0.6f) * r), ink);
                for (int f = 0; f < 4; f++) Line(new Vector2(-0.24f + f * 0.16f, -0.1f), new Vector2(-0.24f + f * 0.16f, -0.65f));
                break;
            case PlayerGesture.Point:
                Line(new Vector2(-0.7f, 0f), new Vector2(0.7f, 0f));
                Line(new Vector2(0.7f, 0f), new Vector2(0.35f, -0.35f));
                Line(new Vector2(0.7f, 0f), new Vector2(0.35f, 0.35f));
                break;
            case PlayerGesture.Threaten:
                _hud.DrawCircle(at, 0.42f * r, ink);
                for (int k = 0; k < 3; k++) Line(new Vector2(-0.3f + k * 0.3f, -0.42f), new Vector2(-0.38f + k * 0.38f, -0.8f));
                break;
            default:
                // A hand on the heart.
                _hud.DrawCircle(at + new Vector2(-0.2f, -0.15f) * r, 0.26f * r, ink);
                _hud.DrawCircle(at + new Vector2(0.2f, -0.15f) * r, 0.26f * r, ink);
                _hud.DrawColoredPolygon(new[] { at + new Vector2(-0.45f, -0.05f) * r, at + new Vector2(0.45f, -0.05f) * r, at + new Vector2(0f, 0.55f) * r }, ink);
                break;
        }
    }

    /// <summary>The settlement you are in: its standing with you as an icon (a house, its door open or shut) and a number. No words.</summary>
    void DrawStanding()
    {
        Slice.Places.Town town = Game.TownHere;
        if (town == null) return;
        float s = S, st = town.Standing;
        Vector2 at = new(_hud.Size.X * 0.5f - 210f * s, 34f * s);
        Color tone = st <= Slice.Places.Town.StandingBarred ? C.Danger : st < Slice.Places.Town.StandingWary ? C.Warn : st > 30f ? C.Accent : C.Text;
        // A house: a roof and walls, its door shut when they won't open the gate to you.
        float h = 16f * s;
        _hud.DrawColoredPolygon(new[] { at + new Vector2(-h, 0f), at + new Vector2(h, 0f), at + new Vector2(0f, -h * 0.9f) }, tone);
        _hud.DrawRect(new Rect2(at + new Vector2(-h * 0.8f, 0f), new Vector2(h * 1.6f, h * 1.1f)), tone, false, 2f * s);
        if (town.GatesShut) _hud.DrawRect(new Rect2(at + new Vector2(-h * 0.25f, h * 0.35f), new Vector2(h * 0.5f, h * 0.75f)), tone);
        _hud.Text(UiTheme.Mono, (int)(18 * s), at + new Vector2(h * 1.3f, -h * 0.55f), $"{st:+0;-0;0}", tone);
    }

    void DrawSoundMarkers()
    {
        if (_cues.Count == 0) return;
        Camera3D cam = _hud.GetViewport().GetCamera3D();
        if (cam == null) return;
        float s = S;
        Vector2 c = _hud.Size * 0.5f;
        Vector2 radius = c * new Vector2(0.86f, 0.8f);
        for (int i = _cues.Count - 1; i >= 0; i--)
        {
            var (at, weight, when) = _cues[i];
            double age = _time - when;
            Vector3 local = cam.GlobalTransform.Basis.Inverse() * (at - cam.GlobalPosition);
            // Your own shots, and what's at your shoulder, aren't marked.
            if (age > 1.8 || local.Length() < 4f || local.Length() > 160f) { _cues.RemoveAt(i); continue; }
            float angle = MathF.Atan2(local.X, -local.Z);
            Vector2 dir = new(MathF.Sin(angle), -MathF.Cos(angle));
            Vector2 p = c + dir * radius;
            float a = (float)(1.0 - age / 1.8) * Math.Clamp(0.5f + 0.4f * weight, 0.4f, 1f);
            Vector2 side = new(-dir.Y, dir.X);
            float size = 14f * s;
            _hud.DrawColoredPolygon(new[] { p + dir * size, p - dir * size * 0.3f + side * size * 0.75f, p - dir * size * 0.3f - side * size * 0.75f }, new Color(C.Text, 0.55f * a));
        }
    }
}
