using System.Collections.Generic;
using Godot;

namespace Wildlife;

public enum Shape { Sphere, Box, Capsule, Cylinder, Cone }

/// <summary>
/// One primitive in a piece's local space. Size is the full extent: a sphere's diameters, a
/// capsule's (diameter, total length, diameter) along +Y, a cylinder's (bottom diameter,
/// height, top diameter). Glow is how much it lights up; Pattern is how much of the species'
/// skin pattern shows on it (bellies and claws stay plain).
/// </summary>
public struct Prim
{
    public Shape Shape;
    public Vector3 Size;
    public Transform3D Local;
    public Color Color;
    public float Glow, Pattern;
    public int Sides;
}

/// <summary>A rigid piece of a creature: the body, a head, one leg bone. The rig poses pieces.</summary>
public sealed class Piece
{
    public string Name;
    public readonly List<Prim> Prims = new();
    /// <summary>Thin bits (tendrils, whiskers, toes) don't cast shadows: cheap and nobody notices.</summary>
    public bool Shadow = true;

    public Piece(string name) => Name = name;

    public Piece Add(Shape shape, Vector3 size, Vector3 position, Color color, float pattern = 0f, float glow = 0f, Basis? basis = null, int sides = 0)
    {
        Prims.Add(new Prim
        {
            Shape = shape,
            Size = size,
            Local = new Transform3D(basis ?? Basis.Identity, position),
            Color = color,
            Glow = glow,
            Pattern = pattern,
            Sides = sides,
        });
        return this;
    }

    /// <summary>A rod from a to b (a capsule or cone along the line).</summary>
    public Piece Rod(Vector3 a, Vector3 b, float diameter, Color color, float pattern = 0f, float glow = 0f, bool cone = false)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return this;
        Basis basis = Geo.AlongY(d / len);
        if (cone) return Add(Shape.Cone, new Vector3(diameter, len, 0f), (a + b) * 0.5f, color, pattern, glow, basis);
        return Add(Shape.Capsule, new Vector3(diameter, len + diameter, diameter), (a + b) * 0.5f, color, pattern, glow, basis);
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
