using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using Godot;

namespace Slice.Ui;

/// <summary>
/// Markers on the things themselves, as Empyrion draws them: each of <see cref="Markers"/> at its target's place on
/// the screen as an icon, a short name and a distance (MARKERS: WORLD; in COMPASS they sit on the compass strip
/// instead). A target behind you, or off the side of the view, is pinned to the screen's edge in its direction with a
/// small arrow. An icon on its target is never moved off it: a visor box over it hides it, and a label shows only where
/// it is clear. Every either/or choice (pinned or not, which side of the icon the label takes, whether it shows, how far
/// a pinned icon has slid along the edge to clear a box, which side a target behind you is pinned to) remembers its last
/// answer and needs a margin to change it, so none flips from frame to frame. Far markers are smaller and fainter.
/// </summary>
public partial class Visor
{
    /// <summary>One marker as laid out: where its target projects to, where it is drawn, whether it was pinned to the edge (and in which direction) or slid along it, its icon's and label's boxes, how far, how strongly it shows.</summary>
    public readonly record struct PlacedMarker(Marker Mark, Vector2 Anchor, Vector2 At, bool Pinned, bool Behind, bool Nudged, Vector2 Direction,
        Rect2 Icon, Rect2 Label, bool Labelled, float Distance, float Alpha);

    /// <summary>The icon's side at the default scale, the gap kept from the screen's edge, and how far markers shrink and fade.</summary>
    public const float MarkerIcon = 17, MarkerMargin = 20, ShrinkFrom = 300, ShrinkTo = 3000, FadeFrom = 600, FadeTo = 6000;

    /// <summary>The name's and the distance's type sizes at the default scale (the distance is the smaller).</summary>
    public const float NameSize = 9, DistanceSize = 7.5f;

    /// <summary>What a marker remembers between frames (see the class summary).</summary>
    sealed class MarkState
    {
        public bool Labelled, LabelLeft, Hidden, Pinned;
        public float Slide;
        public int Side = 1;
        public int Seen;
    }

    readonly Dictionary<string, MarkState> _marks = new();
    int _layoutPass;

    /// <summary>The markers as the last draw laid them out (empty in COMPASS mode): what the checks hold against the camera.</summary>
    public List<PlacedMarker> Drawn { get; private set; } = new();

    /// <summary>The markers as they would be drawn in a window of this size, nearest first.</summary>
    public List<PlacedMarker> LayoutMarkers(Vector2 size, float uiScale)
    {
        var placed = new List<PlacedMarker>();
        Camera3D cam = Game.Camera;
        if (cam == null || Game.Survivor == null || size.X < 10 || size.Y < 10) return placed;
        _layoutPass++;
        HudLayout layout = HudLayout.Compute(size, uiScale);
        float s = layout.Scale;
        var boxes = layout.Boxes.Select(b => b.Box).ToList();
        boxes.AddRange(ScreenBoxes(size, s));
        Vector2 centre = size / 2f;
        var inner = new Rect2(new Vector2(MarkerMargin, MarkerMargin) * s, size - new Vector2(MarkerMargin, MarkerMargin) * 2f * s);
        Transform3D view = cam.GlobalTransform.AffineInverse();
        float focal = size.Y / 2f / MathF.Tan(Mathf.DegToRad(cam.Fov) / 2f); // the camera keeps its height: Fov is vertical
        Vector3 eye = cam.GlobalPosition;
        var labels = new List<Rect2>();
        foreach (Marker m in Markers().OrderBy(m => m.At.DistanceSquaredTo(eye)))
        {
            string key = $"{m.Kind}|{m.Tag}|{m.At.X:F1}|{m.At.Z:F1}";
            if (!_marks.TryGetValue(key, out MarkState st)) _marks[key] = st = new MarkState();
            st.Seen = _layoutPass;
            float d = m.At.DistanceTo(eye);
            Vector3 local = view * m.At;
            bool behind = local.Z > -0.05f;
            Vector2 anchor = behind ? centre : centre + new Vector2(local.X, -local.Y) * focal / -local.Z;
            // Directly behind, a hair either side of the line decides which way the pin swings: it keeps the side it had until clearly past.
            float across = local.X;
            float band = 0.06f * MathF.Max(new Vector2(local.X, local.Y).Length(), 1e-3f);
            if (behind && MathF.Abs(across) < band) across = st.Side * MathF.Max(MathF.Abs(across), 1e-4f);
            else if (MathF.Abs(across) >= band) st.Side = across < 0f ? -1 : 1;
            Vector2 dir = new(across, -local.Y);
            dir = dir.LengthSquared() > 1e-8f ? dir.Normalized() : new Vector2(0f, 1f);
            // Pinned when behind or past the margin; once pinned it needs to be a little inside the margin to come off.
            bool pinned = behind || !(st.Pinned ? inner.Grow(-2f * s) : inner).HasPoint(anchor);
            st.Pinned = pinned;
            float near = Mathf.Clamp((d - ShrinkFrom) / (ShrinkTo - ShrinkFrom), 0f, 1f);
            float side = MarkerIcon * s * Mathf.Lerp(1f, 0.7f, near);
            float alpha = m.Alpha * Mathf.Lerp(1f, 0.5f, Mathf.Clamp((d - FadeFrom) / (FadeTo - FadeFrom), 0f, 1f));
            if (alpha <= 0.02f) continue;
            var iconSize = new Vector2(side, side);
            if (pinned)
            {
                // Slid along the edge only as far as it takes to clear the visor's boxes and the markers pinned already; it keeps the slide it had while that still clears.
                bool Free(float turn, float grow, out Rect2 r)
                {
                    r = new Rect2(Edge(centre, dir.Rotated(turn), inner) - iconSize / 2f, iconSize);
                    Rect2 g = r.Grow(grow);
                    return !boxes.Any(b => b.Intersects(g)) && !placed.Any(q => q.Pinned && q.Icon.Intersects(g));
                }
                Rect2 spot = default;
                bool found = false;
                if (Free(0f, st.Slide == 0f ? 0f : 5f * s, out spot)) { st.Slide = 0f; found = true; }
                else if (st.Slide != 0f && Free(st.Slide, 0f, out spot)) found = true;
                else
                {
                    float first = st.Slide < 0f ? -1f : 1f;
                    for (int k = 1; k <= 40 && !found; k++)
                        foreach (float sign in new[] { first, -first })
                            if (Free(sign * k * 0.04f, 0f, out spot)) { st.Slide = sign * k * 0.04f; found = true; break; }
                }
                if (!found) continue;
                placed.Add(new PlacedMarker(m, anchor, spot.GetCenter(), true, behind, st.Slide != 0f, dir, spot, new Rect2(spot.Position, Vector2.Zero), false, d, alpha));
                continue;
            }
            // On screen: the icon sits exactly on its target. A box over it hides it (and it comes back only once clear by a few pixels).
            var icon = new Rect2(anchor - iconSize / 2f, iconSize);
            Rect2 probe = st.Hidden ? icon.Grow(4f * s) : icon;
            st.Hidden = boxes.Any(b => b.Intersects(probe));
            if (st.Hidden) continue;
            // The label goes right of the icon, or left of it where the right would run off the window; it shows where it is clear of the boxes and of the labels and icons placed already.
            float lw = MathF.Max(Paint.TextWidth(UiTheme.Label, (int)(NameSize * s), Fitted(m.Tag, s)), Paint.TextWidth(UiTheme.Mono, (int)(DistanceSize * s), Distance(d))) + 6 * s;
            float lh = 20 * s;
            Rect2 Box(bool toLeft) => new(toLeft ? icon.Position.X - 3 * s - lw : icon.End.X + 3 * s, icon.GetCenter().Y - lh / 2f, lw, lh);
            bool fitsRight = Box(false).End.X <= size.X - 2 * s, fitsLeft = Box(true).Position.X >= 2 * s;
            bool left = st.LabelLeft ? fitsLeft || !fitsRight : !fitsRight && fitsLeft;
            st.LabelLeft = left;
            Rect2 label = Box(left);
            Rect2 want = st.Labelled ? label : label.Grow(4f * s);
            bool labelled = (left ? fitsLeft : fitsRight) && !labels.Any(l => l.Intersects(want)) && !placed.Any(p => p.Icon.Intersects(want))
                && !boxes.Any(b => b.Intersects(want));
            st.Labelled = labelled;
            if (labelled) labels.Add(label); else label = new Rect2(icon.Position, Vector2.Zero);
            placed.Add(new PlacedMarker(m, anchor, anchor, false, behind, false, dir, icon, label, labelled, d, alpha));
        }
        if (_marks.Count > 64) foreach (string k in _marks.Where(e => e.Value.Seen != _layoutPass).Select(e => e.Key).ToList()) _marks.Remove(k);
        return placed;
    }

    /// <summary>Off only for a test that measures what the screens' boxes change.</summary>
    public static bool HoldScreensClear = true;

    /// <summary>How near, in metres, a machine's screen is held clear of markers and the use prompt.</summary>
    public const float ScreenRange = 6f;

    /// <summary>
    /// Where the machine screens up close (within <see cref="ScreenRange"/>, facing the camera) project to in a window of this
    /// size, bezel and a margin included. Markers are hidden under these boxes (the same hysteresis as for the visor's
    /// own boxes) and the use prompt keeps out of them. One that is too close to project fills the window.
    /// </summary>
    public List<Rect2> ScreenBoxes(Vector2 size, float s)
    {
        var boxes = new List<Rect2>();
        Camera3D cam = Game.Camera;
        if (!HoldScreensClear || cam == null || Game.MachineScreens == null || size.X < 10 || size.Y < 10) return boxes;
        Transform3D view = cam.GlobalTransform.AffineInverse();
        float focal = size.Y / 2f / MathF.Tan(Mathf.DegToRad(cam.Fov) / 2f);
        var window = new Rect2(Vector2.Zero, size);
        foreach (var m in Game.MachineScreens.Screens.Values)
        {
            DiegeticUi.Screens.WorldScreen sc = m.Screen;
            if (!IsInstanceValid(sc) || !sc.IsVisibleInTree()) continue;
            Vector3 to = cam.GlobalPosition - sc.GlobalPosition;
            if (to.Length() > ScreenRange || to.Dot(sc.GlobalBasis.Z) <= 0f) continue;
            float hx = sc.SizeMeters.X / 2f + sc.Bezel, hy = sc.SizeMeters.Y / 2f + sc.Bezel;
            Rect2 r = default;
            bool first = true, close = false;
            foreach (var (cx, cy) in new[] { (-hx, hy), (hx, hy), (hx, -hy), (-hx, -hy) })
            {
                Vector3 l = view * (sc.GlobalTransform * new Vector3(cx, cy, 0f));
                if (l.Z > -0.05f) { close = true; break; }
                Vector2 p = size / 2f + new Vector2(l.X, -l.Y) * focal / -l.Z;
                r = first ? new Rect2(p, Vector2.Zero) : r.Expand(p);
                first = false;
            }
            if (close) r = window;
            r = r.Grow(6f * s);
            if (r.Intersects(window)) boxes.Add(r);
        }
        return boxes;
    }

    /// <summary>
    /// Where the use prompt goes: under the crosshair, or, with that on a machine's screen, below the screen's box (above it
    /// where the visor's key hints are in the way), so it never covers the glass.
    /// </summary>
    public Rect2 PromptBox(Vector2 size, float uiScale, string label)
    {
        HudLayout layout = HudLayout.Compute(size, uiScale);
        float s = layout.Scale, h = 24f * s;
        float w = Paint.TextWidth(UiTheme.Label, (int)(18 * s), label) + 8f * s;
        var r = new Rect2(size.X / 2f - w / 2f, size.Y / 2f + 18f * s, w, h);
        foreach (Rect2 b in ScreenBoxes(size, s))
        {
            if (!b.Intersects(r)) continue;
            r.Position = new Vector2(r.Position.X, b.End.Y + 2f * s);
            if (r.End.Y > layout.Hints.Position.Y) r.Position = new Vector2(r.Position.X, b.Position.Y - 2f * s - h);
        }
        return r;
    }

    /// <summary>Where a ray from the centre in this direction leaves the rectangle.</summary>
    static Vector2 Edge(Vector2 centre, Vector2 dir, Rect2 inner)
    {
        float t = float.MaxValue;
        if (dir.X > 1e-5f) t = MathF.Min(t, (inner.End.X - centre.X) / dir.X);
        if (dir.X < -1e-5f) t = MathF.Min(t, (inner.Position.X - centre.X) / dir.X);
        if (dir.Y > 1e-5f) t = MathF.Min(t, (inner.End.Y - centre.Y) / dir.Y);
        if (dir.Y < -1e-5f) t = MathF.Min(t, (inner.Position.Y - centre.Y) / dir.Y);
        return centre + dir * (t == float.MaxValue ? 0f : t);
    }

    static string Fitted(string tag, float s) => HudLayout.Fit(UiTheme.Label, (int)(NameSize * s), tag, 120 * s);

    /// <summary>A distance as the marker shows it: metres, or kilometres from one on.</summary>
    public static string Distance(float d) => d < 1000f ? $"{d:F0} m" : $"{d / 1000f:F1} km";

    void DrawMarkers()
    {
        float s = S;
        if (!MarkersInWorld) { Drawn = new(); return; }
        Drawn = LayoutMarkers(_hud.Size, UiScale);
        foreach (PlacedMarker p in Drawn)
        {
            Color tone = new(p.Mark.Colour, p.Alpha);
            float r = p.Icon.Size.X / 2f;
            Vector2 c = p.Icon.GetCenter();
            _hud.DrawCircle(c, r, new Color(C.Glass, 0.55f * p.Alpha));
            MarkerGlyph(p.Mark.Kind, c, r * 0.62f, tone);
            _hud.DrawArc(c, r, 0, Mathf.Tau, 24, tone, MathF.Max(1f, 1.2f * s));
            if (p.Pinned)
            {
                // A small arrow just outside the ring, pointing the way to turn.
                Vector2 d = p.Direction, n = new(-d.Y, d.X);
                Vector2 tip = c + d * (r + 7f * s), back = c + d * (r + 2.5f * s);
                _hud.DrawColoredPolygon(new[] { tip, back + n * 4f * s, back - n * 4f * s }, tone);
            }
            if (!p.Labelled) continue;
            _hud.Block(p.Label, new Color(C.Glass, 0.45f * p.Alpha));
            _hud.Text(UiTheme.Label, (int)(NameSize * s), p.Label.Position + new Vector2(3 * s, 0f), Fitted(p.Mark.Tag, s), tone);
            _hud.Text(UiTheme.Mono, (int)(DistanceSize * s), p.Label.Position + new Vector2(3 * s, 10 * s), Distance(p.Distance), new Color(C.Text, p.Alpha * 0.85f));
        }
    }

    /// <summary>The marker's picture, by kind, centred on <paramref name="c"/> with half-size <paramref name="r"/>.</summary>
    void MarkerGlyph(string kind, Vector2 c, float r, Color tone)
    {
        float w = MathF.Max(1.2f, r * 0.24f);
        void Line(float x0, float y0, float x1, float y1) => _hud.DrawLine(c + new Vector2(x0, y0) * r, c + new Vector2(x1, y1) * r, tone, w);
        Vector2 P(float x, float y) => c + new Vector2(x, y) * r;
        switch (kind)
        {
            case "bunker": // a hatch in the ground
                Line(-1f, 0.4f, 1f, 0.4f); _hud.DrawRect(new Rect2(P(-0.55f, -0.6f), new Vector2(1.1f, 1f) * r), tone, false, w);
                break;
            case "place": // a roofline
                _hud.DrawPolyline(new[] { P(-0.9f, 0.8f), P(-0.9f, -0.1f), P(0f, -0.85f), P(0.9f, -0.1f), P(0.9f, 0.8f), P(-0.9f, 0.8f) }, tone, w);
                break;
            case "deposit": // a crystal
                _hud.DrawColoredPolygon(new[] { P(0f, -0.95f), P(0.7f, -0.2f), P(0f, 0.95f), P(-0.7f, -0.2f) }, tone);
                break;
            case "vessel": // a craft seen from above
                _hud.DrawColoredPolygon(new[] { P(0f, -0.95f), P(0.75f, 0.8f), P(0f, 0.4f), P(-0.75f, 0.8f) }, tone);
                break;
            case "base": // an anchored block on legs
                _hud.DrawRect(new Rect2(P(-0.75f, -0.75f), new Vector2(1.5f, 1f) * r), tone, true);
                Line(-0.6f, 0.25f, -0.85f, 0.9f); Line(0.6f, 0.25f, 0.85f, 0.9f);
                break;
            case "backpack": // a pack with its flap
                _hud.DrawRect(new Rect2(P(-0.6f, -0.6f), new Vector2(1.2f, 1.5f) * r), tone, false, w);
                Line(-0.6f, -0.1f, 0.6f, -0.1f); Line(-0.25f, -0.6f, -0.25f, -0.9f); Line(0.25f, -0.6f, 0.25f, -0.9f); Line(-0.25f, -0.9f, 0.25f, -0.9f);
                break;
            case "mark": // a pin
                _hud.DrawCircle(P(0f, -0.35f), 0.5f * r, tone);
                _hud.DrawColoredPolygon(new[] { P(-0.4f, -0.1f), P(0.4f, -0.1f), P(0f, 0.95f) }, tone);
                break;
            default: // a ping: a dot in a ring
                _hud.DrawCircle(c, 0.35f * r, tone);
                _hud.DrawArc(c, 0.85f * r, 0, Mathf.Tau, 20, tone, w);
                break;
        }
    }
}
