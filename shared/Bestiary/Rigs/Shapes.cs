using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

public enum Shape { Sphere, Box, Capsule, Cylinder, Cone, Taper, Loft }

/// <summary>
/// What a surface is made of. The shader reads it from the mesh (UV.y) and shades each kind
/// its own way, so one material per species still gives wet eyes, glossy horn, dull hide and
/// thin wing membrane.
/// </summary>
public static class Mat
{
    public const float Skin = 0f, Horn = 1f, Carapace = 2f, Membrane = 3f, Eye = 4f;
    /// <summary>A worm: wet, and it moves on its own (the shader sways it by how far along the worm each point is).</summary>
    public const float Worm = 5f;
}

/// <summary>
/// One primitive in a piece's local space. Size is the full extent: a sphere's diameters, a
/// capsule's (diameter, total length, diameter) along +Y, a cylinder's (bottom diameter,
/// height, top diameter), a taper's (bottom diameter, length, top diameter). Glow is how much
/// it lights up; Pattern is how much of the species' skin pattern shows on it (bellies and
/// claws stay plain). Color2 is the underside colour of a loft (a belly that blends in rather
/// than a second shape stuck on).
/// </summary>
public struct Prim
{
    public Shape Shape;
    public Vector3 Size;
    public Transform3D Local;
    public Color Color, Color2;
    public float Glow, Pattern, Material;
    public int Sides;
    public LoftProfile Loft;
    /// <summary>A worm's segment: which worm (its own sway), and how far along the worm (0 root to 1 tip) its two ends are.</summary>
    public int Worm;
    public float AlongFrom, AlongTo;
}

/// <summary>
/// A lofted shape: elliptical cross-sections along local Z (front at the lowest Z), each with
/// its own half-width, half-height and centre height. Bodies, heads and jaws are lofts, so a
/// creature has shoulders, a waist and haunches instead of being one egg.
/// </summary>
public sealed class LoftProfile
{
    public readonly float[] Z, W, H, Y;

    LoftProfile(int n)
    {
        Z = new float[n];
        W = new float[n];
        H = new float[n];
        Y = new float[n];
    }

    public int Count => Z.Length;

    /// <summary>
    /// Builds a profile from key sections at t in [0, 1] from front (z0) to back (z1): each key
    /// is (t, half-width, half-height, centre height). Sections between keys are eased, and the
    /// last <paramref name="round"/> of the length at each end closes like a hemisphere, so the
    /// shape needs no caps and has no seams.
    /// </summary>
    public static LoftProfile Smooth(float z0, float z1, (float t, float w, float h, float y)[] keys, int stations = 14, float round = 0.18f)
    {
        var p = new LoftProfile(stations);
        for (int i = 0; i < stations; i++)
        {
            // Denser near the ends, where the rounding happens.
            float t = 0.5f - 0.5f * MathF.Cos(MathF.PI * i / (stations - 1));
            (float w, float h, float y) = Eval(keys, t);
            float end = MathF.Min(t, 1f - t) / MathF.Max(1e-3f, round);
            float r = end >= 1f ? 1f : MathF.Sqrt(MathF.Max(0f, 1f - (1f - end) * (1f - end)));
            p.Z[i] = z0 + (z1 - z0) * t;
            p.W[i] = w * r;
            p.H[i] = h * r;
            p.Y[i] = y;
        }
        return p;
    }

    static (float, float, float) Eval((float t, float w, float h, float y)[] keys, float t)
    {
        if (t <= keys[0].t) return (keys[0].w, keys[0].h, keys[0].y);
        for (int i = 0; i + 1 < keys.Length; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            if (t > b.t) continue;
            float u = (t - a.t) / MathF.Max(1e-4f, b.t - a.t);
            u = u * u * (3f - 2f * u);
            return (a.w + (b.w - a.w) * u, a.h + (b.h - a.h) * u, a.y + (b.y - a.y) * u);
        }
        var last = keys[^1];
        return (last.w, last.h, last.y);
    }
}

/// <summary>A rigid piece of a creature: the body, a head, one leg bone. The rig poses pieces.</summary>
public sealed class Piece
{
    public string Name;
    public readonly List<Prim> Prims = new();
    /// <summary>Thin bits (tendrils, whiskers, toes) don't cast shadows: cheap and nobody notices.</summary>
    public bool Shadow = true;
    /// <summary>Not a surface but a blur: drawn see-through, with its vertex alpha as its strength (<see cref="CreatureMaterials.Blur"/>).</summary>
    public bool Blur;

    public Piece(string name) => Name = name;

    public Piece Add(Shape shape, Vector3 size, Vector3 position, Color color, float pattern = 0f, float glow = 0f, Basis? basis = null, int sides = 0, float material = Mat.Skin)
    {
        Prims.Add(new Prim
        {
            Shape = shape,
            Size = size,
            Local = new Transform3D(basis ?? Basis.Identity, position),
            Color = color,
            Color2 = color,
            Glow = glow,
            Pattern = pattern,
            Sides = sides,
            Material = material,
        });
        return this;
    }

    /// <summary>A rod from a to b (a capsule or cone along the line).</summary>
    public Piece Rod(Vector3 a, Vector3 b, float diameter, Color color, float pattern = 0f, float glow = 0f, bool cone = false, float material = Mat.Skin)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return this;
        Basis basis = Geo.AlongY(d / len);
        if (cone) return Add(Shape.Cone, new Vector3(diameter, len, 0f), (a + b) * 0.5f, color, pattern, glow, basis, 0, material);
        return Add(Shape.Capsule, new Vector3(diameter, len + diameter, diameter), (a + b) * 0.5f, color, pattern, glow, basis, 0, material);
    }

    /// <summary>
    /// A rounded taper from a (radius ra) to b (radius rb): a sphere at each end joined by the
    /// cone that touches both. Two tapers that share an end point and radius meet without a
    /// seam at any angle, which is how knees, necks and tails stay smooth when they bend.
    /// A tip radius near zero makes a claw, a horn or a tooth.
    /// </summary>
    public Piece Taper(Vector3 a, Vector3 b, float ra, float rb, Color color, float pattern = 0f, float glow = 0f, float material = Mat.Skin, int sides = 0)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return this;
        return Add(Shape.Taper, new Vector3(ra * 2f, len, rb * 2f), a, color, pattern, glow, Geo.AlongY(d / len), sides, material);
    }

    /// <summary>A curved horn or claw: a chain of tapers through the points, thinning to a tip.</summary>
    public Piece Horn(Color color, float baseRadius, float material, params Vector3[] points)
    {
        int n = points.Length - 1;
        for (int i = 0; i < n; i++)
        {
            float r0 = baseRadius * (1f - i / (float)n), r1 = baseRadius * (1f - (i + 1) / (float)n);
            Taper(points[i], points[i + 1], MathF.Max(r0, baseRadius * 0.05f), MathF.Max(r1, baseRadius * 0.05f), color, 0f, 0f, material, 7);
        }
        return this;
    }

    /// <summary>
    /// A worm: a chain of tapers from its root along a wandering line, thinning to nothing, each
    /// segment marked with its worm and how far along it lies, so the shader sways the whole worm
    /// from a still root to a lashing tip. <paramref name="bend"/> is how much it wanders and droops a joint.
    /// </summary>
    public Piece Worm(Vector3 root, Vector3 dir, float length, float radius, Color color, int worm, Rng rng, int joints = 4, float droop = 0.35f, float bend = 0.35f)
    {
        Vector3 at = root, d = dir.Normalized();
        float seg = length / joints, r = radius;
        for (int i = 0; i < joints; i++)
        {
            Vector3 next = at + d * seg;
            float r2 = i == joints - 1 ? radius * 0.3f : r * 0.86f;
            Taper(at, next, r, r2, color, 0f, 0f, Mat.Worm, 5);
            Prim p = Prims[^1];
            p.Worm = worm;
            p.AlongFrom = i / (float)joints;
            p.AlongTo = (i + 1f) / joints;
            Prims[^1] = p;
            at = next;
            r = r2;
            d = (d + new Vector3(rng.Range(-bend, bend), -droop, rng.Range(-bend, bend))).Normalized();
        }
        return this;
    }

    /// <summary>A lofted shape in this piece, offset and turned by an optional transform.</summary>
    public Piece Loft(LoftProfile profile, Color top, Color under, float pattern = 1f, float glow = 0f, float material = Mat.Skin, Vector3? offset = null, Basis? basis = null, int sides = 14)
    {
        Prims.Add(new Prim
        {
            Shape = Shape.Loft,
            Loft = profile,
            Local = new Transform3D(basis ?? Basis.Identity, offset ?? Vector3.Zero),
            Color = top,
            Color2 = under,
            Pattern = pattern,
            Glow = glow,
            Material = material,
            Sides = sides,
        });
        return this;
    }
}

/// <summary>Everything a species looks like, as pieces. Built once per species and shared.</summary>
public sealed class RigLayout
{
    public readonly List<Piece> Pieces = new();

    public int Add(Piece piece)
    {
        Pieces.Add(piece);
        return Pieces.Count - 1;
    }
}

/// <summary>Small geometry helpers shared by the rigs.</summary>
public static class Geo
{
    /// <summary>A basis whose Y axis points along dir (any twist).</summary>
    public static Basis AlongY(Vector3 dir)
    {
        Vector3 y = dir.Normalized();
        Vector3 hint = Mathf.Abs(y.Y) < 0.95f ? Vector3.Up : Vector3.Right;
        Vector3 x = hint.Cross(y).Normalized();
        Vector3 z = x.Cross(y);
        return new Basis(x, y, z);
    }

    /// <summary>A basis with Y along dir and X as close to side as possible. Y may be stretched.</summary>
    public static Basis AlongY(Vector3 dir, Vector3 side, float stretch = 1f)
    {
        Vector3 y = dir.Normalized();
        Vector3 x = side - y * side.Dot(y);
        if (x.LengthSquared() < 1e-6f) x = (Mathf.Abs(y.Y) < 0.95f ? Vector3.Up : Vector3.Right).Cross(y);
        x = x.Normalized();
        Vector3 z = x.Cross(y);
        return new Basis(x, y * stretch, z);
    }

    /// <summary>A segment piece's pose: origin at from, Y along to - from.</summary>
    public static Transform3D Segment(Vector3 from, Vector3 to, Vector3 side, float restLength = 0f)
    {
        Vector3 d = to - from;
        float len = d.Length();
        if (len < 1e-5f) return new Transform3D(Basis.Identity, from);
        float stretch = restLength > 0f ? len / restLength : 1f;
        return new Transform3D(AlongY(d / len, side, stretch), from);
    }

    /// <summary>Rotation about local X by pitch (positive raises -Z), then about Y by yaw.</summary>
    public static Basis YawPitch(float yaw, float pitch) => new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch);

    /// <summary>Direction pointing forward (-Z) pitched up by pitch and turned by yaw.</summary>
    public static Vector3 Forward(float yaw, float pitch) =>
        new(-Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), -Mathf.Cos(yaw) * Mathf.Cos(pitch));

    /// <summary>Direction pointing backward (+Z) pitched up by pitch and swung right by yaw.</summary>
    public static Vector3 Backward(float yaw, float pitch) =>
        new(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));

    public static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public static bool Finite(Transform3D t) =>
        Finite(t.Origin) && Finite(t.Basis.X) && Finite(t.Basis.Y) && Finite(t.Basis.Z);

    public static float Smooth(float current, float target, float rate, float dt) =>
        current + (target - current) * (1f - Mathf.Exp(-rate * dt));

    public static Vector3 Smooth(Vector3 current, Vector3 target, float rate, float dt) =>
        current + (target - current) * (1f - Mathf.Exp(-rate * dt));

    public static float WrapAngle(float a) => Mathf.Wrap(a, -Mathf.Pi, Mathf.Pi);
}
