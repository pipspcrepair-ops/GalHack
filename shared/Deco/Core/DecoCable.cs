using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Deco;

/// <summary>
/// A wire, cable, rope, chain or hose. It is always attached, never floating: either strung
/// between two anchors with a natural sag (a catenary), or hanging from one anchor straight down
/// under gravity, lying along the ground where it reaches it. Pieces declare their cables by their
/// anchor points; the forge hangs them after a piece is toppled or broken, so gravity is always
/// right and a cut cable drops from whatever still holds it.
/// </summary>
public sealed class DecoCable
{
    /// <summary>The first anchor (always attached).</summary>
    public Vector3 A;
    /// <summary>The second anchor when <see cref="Spanned"/>; otherwise only a hint for which way the fallen end lies.</summary>
    public Vector3 B;
    /// <summary>Strung between two anchors (true), or hanging from <see cref="A"/> only (false).</summary>
    public bool Spanned = true;
    /// <summary>Spans: extra length over the straight line, as a share (0.03 is a taut wire, 0.15 a slack rope).</summary>
    public float Slack = 0.04f;
    /// <summary>Drops: how much lies along the ground past where it lands.</summary>
    public float Tail = 0.3f;
    public float Radius = 0.01f;
    public DecoSlot Slot = DecoSlot.Dark;
    /// <summary>Breaking the piece cuts it at one end, and that end drops.</summary>
    public bool Breakable = true;

    public DecoCable Clone() => (DecoCable)MemberwiseClone();
}

/// <summary>
/// Cable geometry anyone can use (the infrastructure network strings power lines with it): a
/// sagging span between two anchors, and a hanging drop from one anchor to the ground.
/// </summary>
public static class DecoCables
{
    /// <summary>
    /// The catenary through <paramref name="a"/> and <paramref name="b"/> whose length is the chord
    /// times (1 + <paramref name="slack"/>), as a polyline of <paramref name="segments"/> pieces.
    /// Gravity is -Y. A vertical span is straight.
    /// </summary>
    public static List<Vector3> Span(Vector3 a, Vector3 b, float slack, int segments = 14)
    {
        var pts = new List<Vector3>(segments + 1);
        Vector3 d = b - a;
        var horiz = new Vector2(d.X, d.Z);
        float h = horiz.Length(), v = d.Y;
        float chord = d.Length();
        if (h < 1e-3f || chord < 1e-4f || slack <= 1e-5f)
        {
            for (int i = 0; i <= segments; i++) pts.Add(Vector3.Lerp(a, b, i / (float)segments));
            return pts;
        }
        float length = chord * (1 + slack);
        // Solve sqrt(L^2 - v^2) = 2 k sinh(h / 2k) for the catenary parameter k (bisection on 1/k).
        float target = MathF.Sqrt(MathF.Max(length * length - v * v, h * h * 1.000001f));
        double lo = 1e-6, hi = 1e3;
        for (int i = 0; i < 100; i++)
        {
            double mid = (lo + hi) / 2, k = 1 / mid;
            double f = 2 * k * Math.Sinh(h / (2 * k));
            if (double.IsInfinity(f) || f > target) hi = mid; else lo = mid;
        }
        float kk = (float)(1 / ((lo + hi) / 2));
        // Shift so the curve passes through both ends: y = k cosh((x - x0) / k) + c, x from 0 at a to h at b.
        float x0 = h / 2 - kk * MathF.Asinh(v / (2 * kk * MathF.Sinh(h / (2 * kk))));
        float c = -kk * MathF.Cosh((0 - x0) / kk);
        Vector2 dir = horiz / h;
        for (int i = 0; i <= segments; i++)
        {
            float x = h * i / segments;
            float y = kk * MathF.Cosh((x - x0) / kk) + c;
            pts.Add(new Vector3(a.X + dir.X * x, a.Y + y, a.Z + dir.Y * x));
        }
        pts[^1] = b;
        return pts;
    }

    /// <summary>
    /// A cable hanging from <paramref name="anchor"/>: straight down to the ground at
    /// <paramref name="groundY"/>, then lying along it for <paramref name="tail"/> metres towards
    /// <paramref name="lie"/> with a loose loop where it landed.
    /// </summary>
    public static List<Vector3> Drop(Vector3 anchor, float groundY, Vector3 lie, float tail, float radius)
    {
        var pts = new List<Vector3> { anchor };
        float rest = groundY + radius;
        if (anchor.Y > rest + 1e-3f) pts.Add(anchor with { Y = rest });
        var dir = new Vector2(lie.X, lie.Z);
        if (dir.LengthSquared() < 1e-6f) dir = Vector2.UnitX;
        dir = Vector2.Normalize(dir);
        var side = new Vector2(-dir.Y, dir.X);
        Vector3 start = pts[^1];
        // A loose loop where it landed, then the rest of the tail.
        float loop = MathF.Min(0.12f, tail * 0.3f);
        if (loop > 0.02f)
            for (int i = 1; i <= 6; i++)
            {
                float t = i / 6f * MathF.Tau;
                var o = dir * (MathF.Sin(t) * loop) + side * ((1 - MathF.Cos(t)) * loop * 0.6f);
                pts.Add(new Vector3(start.X + o.X, rest, start.Z + o.Y));
            }
        Vector3 from = pts[^1];
        int n = Math.Max(1, (int)(tail / 0.12f));
        for (int i = 1; i <= n; i++)
        {
            float s = tail * i / n;
            var o = dir * s + side * (MathF.Sin(s * 6f) * 0.03f);
            pts.Add(new Vector3(from.X + o.X, rest, from.Z + o.Y));
        }
        return pts;
    }

    /// <summary>The polyline a cable takes now: its span, or its drop to the ground at y = 0.</summary>
    public static List<Vector3> Path(DecoCable c, float groundY = 0)
    {
        if (c.Spanned)
        {
            // A span that would sag through the ground lies along it instead.
            var span = Span(c.A, c.B, c.Slack, Math.Clamp((int)(Vector3.Distance(c.A, c.B) / 0.15f), 4, 24));
            for (int i = 0; i < span.Count; i++) if (span[i].Y < groundY + c.Radius) span[i] = span[i] with { Y = groundY + c.Radius };
            return span;
        }
        Vector3 lie = c.B - c.A;
        return Drop(c.A with { Y = MathF.Max(c.A.Y, groundY + c.Radius) }, groundY, lie, c.Tail, c.Radius);
    }

    /// <summary>Thin tubes along a polyline (4-sided: cables are drawn chunky and cheap).</summary>
    public static List<DecoPart> Tubes(IReadOnlyList<Vector3> pts, float radius, DecoSlot slot)
    {
        var parts = new List<DecoPart>();
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            if (Vector3.DistanceSquared(pts[i], pts[i + 1]) < 1e-8f) continue;
            // Overlap a little so joints don't gap.
            Vector3 d = Vector3.Normalize(pts[i + 1] - pts[i]) * radius * 0.5f;
            // The overlap never reaches below the lower of its two points: a length coming down onto the floor
            // would push its end (and with its edge down, its corner) into it.
            Vector3 a = pts[i] - d, b = pts[i + 1] + d;
            float low = MathF.Min(pts[i].Y, pts[i + 1].Y);
            if (a.Y < low) a.Y = low;
            if (b.Y < low) b.Y = low;
            // Turned a quarter-face about its run: a length lying on the floor has an edge up, not a flat
            // face, so where two lengths overlap at a bend no two flat tops lie in one plane and flicker.
            parts.Add(new DecoPart { Kind = DecoPrim.Cylinder, Segments = 4, Slot = slot, Flags = PartFlags.NoCollide, M = DecoGeo.RotY(45) * DecoGeo.Along(a, b, radius * 2, radius * 2) });
        }
        return parts;
    }
}
