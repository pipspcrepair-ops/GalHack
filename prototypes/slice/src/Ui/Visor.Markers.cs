using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using Godot;

namespace Slice.Ui;

/// <summary>
/// Markers on the things themselves, as Empyrion draws them: each of <see cref="Markers"/> at its target's place on
/// the screen as an icon, a short name and a distance. A target behind you, or off the side of the view, is pinned
/// to the screen's edge in its direction with a small arrow. Markers keep out of the visor's boxes (nudged off
/// them, or left out when they can't be), and where labels would land on one another the nearer keeps its label
/// and the others shrink to their icons. Far markers are smaller and fainter, so a crowded view still reads.
/// </summary>
public partial class Visor
{
    /// <summary>One marker as laid out: where its target projects to, where it is drawn, whether it was pinned to the edge (and in which direction) or nudged off a box, its icon's and label's boxes, how far, how strongly it shows.</summary>
    public readonly record struct PlacedMarker(Marker Mark, Vector2 Anchor, Vector2 At, bool Pinned, bool Behind, bool Nudged, Vector2 Direction,
        Rect2 Icon, Rect2 Label, bool Labelled, float Distance, float Alpha);

    /// <summary>The icon's side at the default scale, the gap kept from the screen's edge, and how far markers shrink and fade.</summary>
    public const float MarkerIcon = 26, MarkerMargin = 30, ShrinkFrom = 300, ShrinkTo = 3000, FadeFrom = 600, FadeTo = 6000;

    /// <summary>The markers as they would be drawn in a window of this size, nearest first.</summary>
    public List<PlacedMarker> LayoutMarkers(Vector2 size, float uiScale)
    {
        var placed = new List<PlacedMarker>();
        Camera3D cam = Game.Camera;
        if (cam == null || Game.Survivor == null || size.X < 10 || size.Y < 10) return placed;
        HudLayout layout = HudLayout.Compute(size, uiScale);
        float s = layout.Scale;
        var boxes = layout.Boxes.Select(b => b.Box).ToList();
        Vector2 centre = size / 2f;
        var inner = new Rect2(new Vector2(MarkerMargin, MarkerMargin) * s, size - new Vector2(MarkerMargin, MarkerMargin) * 2f * s);
        Transform3D view = cam.GlobalTransform.AffineInverse();
        float focal = size.Y / 2f / MathF.Tan(Mathf.DegToRad(cam.Fov) / 2f); // the camera keeps its height: Fov is vertical
        Vector3 eye = cam.GlobalPosition;
        var labels = new List<Rect2>();
        foreach (Marker m in Markers().OrderBy(m => m.At.DistanceSquaredTo(eye)))
        {
            float d = m.At.DistanceTo(eye);
            Vector3 local = view * m.At;
            bool behind = local.Z > -0.05f;
            Vector2 anchor = behind ? centre : centre + new Vector2(local.X, -local.Y) * focal / -local.Z;
            Vector2 dir = new Vector2(local.X, -local.Y);
            dir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : new Vector2(0f, 1f);
            bool pinned = behind || !inner.HasPoint(anchor);
            Vector2 at = pinned ? Edge(centre, dir, inner) : anchor;
            float near = Mathf.Clamp((d - ShrinkFrom) / (ShrinkTo - ShrinkFrom), 0f, 1f);
            float side = MarkerIcon * s * Mathf.Lerp(1f, 0.65f, near);
            float alpha = m.Alpha * Mathf.Lerp(1f, 0.5f, Mathf.Clamp((d - FadeFrom) / (FadeTo - FadeFrom), 0f, 1f));
            if (alpha <= 0.02f) continue;
            Rect2 icon = new(at - new Vector2(side, side) / 2f, new Vector2(side, side));
            Rect2 label = pinned ? new Rect2(icon.Position, Vector2.Zero) : LabelBox(m, icon, d, s);
            bool nudged = false;
            if (pinned)
            {
                // Pinned: slid along the edge, either way, to the nearest stretch clear of the visor's boxes and of the markers pinned already.
                Rect2? free = null;
                for (int k = 0; k <= 40 && free == null; k++)
                    foreach (float turn in k == 0 ? new[] { 0f } : new[] { k * 0.04f, -k * 0.04f })
                    {
                        Vector2 e = Edge(centre, dir.Rotated(turn), inner);
                        var r = new Rect2(e - icon.Size / 2f, icon.Size);
                        if (!boxes.Any(b => b.Intersects(r)) && !placed.Any(q => q.Pinned && q.Icon.Intersects(r))) { free = r; nudged = k > 0; break; }
                    }
                if (free is not Rect2 spot) continue;
                icon = spot;
                label = new Rect2(icon.Position, Vector2.Zero);
                placed.Add(new PlacedMarker(m, anchor, icon.GetCenter(), true, behind, nudged, dir, icon, label, false, d, alpha));
                continue;
            }
            // Off the visor's boxes: the icon and its label together if they can go, else the icon alone.
            Rect2 whole = pinned ? icon : icon.Merge(label);
            if (Clear(whole, boxes, size) is Vector2 move)
            {
                nudged = move != Vector2.Zero;
                icon.Position += move;
                label.Position += move;
            }
            else
            {
                Vector2? alone = Clear(icon, boxes, size);
                if (alone is not Vector2 shift) continue; // nowhere clear of the boxes: not drawn
                nudged = true;
                icon.Position += shift;
                label = new Rect2(icon.Position, Vector2.Zero);
            }
            at = icon.GetCenter();
            // The nearer keeps its label; one landing on a label or an icon already placed shrinks to its icon.
            bool labelled = label.Size.X > 0 && !labels.Any(l => l.Intersects(label)) && !placed.Any(p => p.Icon.Intersects(label))
                && !boxes.Any(b => b.Intersects(label));
            if (!labelled) label = new Rect2(icon.Position, Vector2.Zero);
            else labels.Add(label);
            placed.Add(new PlacedMarker(m, anchor, at, pinned, behind, nudged, dir, icon, label, labelled, d, alpha));
        }
        return placed;
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

    /// <summary>The label's box: the name over the distance, to the right of the icon.</summary>
    static Rect2 LabelBox(Marker m, Rect2 icon, float d, float s)
    {
        float w = MathF.Max(Paint.TextWidth(UiTheme.Label, (int)(13 * s), Fitted(m.Tag, s)), Paint.TextWidth(UiTheme.Mono, (int)(12 * s), Distance(d))) + 8 * s;
        return new Rect2(icon.End.X + 4 * s, icon.GetCenter().Y - 15 * s, w, 30 * s);
    }

    static string Fitted(string tag, float s) => HudLayout.Fit(UiTheme.Label, (int)(13 * s), tag, 170 * s);

    /// <summary>A distance as the marker shows it: metres, or kilometres from one on.</summary>
    public static string Distance(float d) => d < 1000f ? $"{d:F0} m" : $"{d / 1000f:F1} km";

    /// <summary>
    /// The smallest move (none, if it is clear already) that takes a box off every one of the visor's boxes and keeps it
    /// in the window; null if a few tries find none.
    /// </summary>
    static Vector2? Clear(Rect2 r, List<Rect2> boxes, Vector2 size)
    {
        var screen = new Rect2(Vector2.Zero, size);
        Vector2 total = Vector2.Zero;
        for (int i = 0; i < 8; i++)
        {
            Rect2 at = new(r.Position + total, r.Size);
            Rect2? hit = boxes.FirstOrDefault(b => b.Intersects(at)) is Rect2 b0 && b0.Intersects(at) ? b0 : null;
            if (hit is not Rect2 b) return screen.Encloses(at) ? total : null;
            var moves = new[]
            {
                new Vector2(b.Position.X - 2f - at.End.X, 0f), new Vector2(b.End.X + 2f - at.Position.X, 0f),
                new Vector2(0f, b.Position.Y - 2f - at.End.Y), new Vector2(0f, b.End.Y + 2f - at.Position.Y),
            };
            Vector2 best = moves.Where(mv => screen.Encloses(new Rect2(at.Position + mv, at.Size))).OrderBy(mv => mv.LengthSquared()).FirstOrDefault(new Vector2(float.NaN, float.NaN));
            if (float.IsNaN(best.X)) return null;
            total += best;
        }
        return null;
    }

    void DrawMarkers()
    {
        float s = S;
        foreach (PlacedMarker p in LayoutMarkers(_hud.Size, UiScale))
        {
            Color tone = new(p.Mark.Colour, p.Alpha);
            float r = p.Icon.Size.X / 2f;
            Vector2 c = p.Icon.GetCenter();
            _hud.DrawCircle(c, r, new Color(C.Glass, 0.55f * p.Alpha));
            MarkerGlyph(p.Mark.Kind, c, r * 0.62f, tone);
            _hud.DrawArc(c, r, 0, Mathf.Tau, 24, tone, MathF.Max(1f, 1.6f * s));
            if (p.Pinned)
            {
                // A small arrow just outside the ring, pointing the way to turn.
                Vector2 d = p.Direction, n = new(-d.Y, d.X);
                Vector2 tip = c + d * (r + 9f * s), back = c + d * (r + 3f * s);
                _hud.DrawColoredPolygon(new[] { tip, back + n * 5f * s, back - n * 5f * s }, tone);
            }
            if (!p.Labelled) continue;
            _hud.Block(p.Label, new Color(C.Glass, 0.45f * p.Alpha));
            _hud.Text(UiTheme.Label, (int)(13 * s), p.Label.Position + new Vector2(4 * s, 1 * s), Fitted(p.Mark.Tag, s), tone);
            _hud.Text(UiTheme.Mono, (int)(12 * s), p.Label.Position + new Vector2(4 * s, 15 * s), Distance(p.Distance), new Color(C.Text, p.Alpha));
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
