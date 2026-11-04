using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// Small vector and rotation helpers, so the rules read like the Godot code they came from.
///
/// Quaternion products are Hamilton products: <c>a * b</c> rotates by b first, then a. So a
/// rotation about a world axis goes on the left (<see cref="ThenWorld"/>) and one about the
/// body's own axes goes on the right (<see cref="ThenLocal"/>), exactly like Godot's
/// <c>worldBasis * basis</c> and <c>basis * localBasis</c>.
/// </summary>
public static class MathX
{
    public static Vector3 Rotate(Quaternion q, Vector3 v) => Vector3.Transform(v, q);
    public static Vector3 Unrotate(Quaternion q, Vector3 v) => Vector3.Transform(v, Quaternion.Conjugate(q));

    public static Quaternion AxisAngle(Vector3 axis, float angle) => Quaternion.CreateFromAxisAngle(axis, angle);

    /// <summary>Rotate by <paramref name="r"/> about world axes after <paramref name="q"/>.</summary>
    public static Quaternion ThenWorld(Quaternion q, Quaternion r) => Quaternion.Normalize(r * q);

    /// <summary>Rotate by <paramref name="r"/> about the body's own axes after <paramref name="q"/>.</summary>
    public static Quaternion ThenLocal(Quaternion q, Quaternion r) => Quaternion.Normalize(q * r);

    public static Vector3 Up(Quaternion q) => Rotate(q, Vector3.UnitY);
    public static Vector3 Forward(Quaternion q) => Rotate(q, -Vector3.UnitZ);
    public static Vector3 Right(Quaternion q) => Rotate(q, Vector3.UnitX);

    /// <summary>A rotation about world up by a heading (0 faces -Z, positive turns left).</summary>
    public static Quaternion Yaw(float heading) => AxisAngle(Vector3.UnitY, heading);

    /// <summary>
    /// The heading of a rotation about world up, from its twist about Y. Robust at any pitch,
    /// so a rider's view keeps turning with the ship even while it climbs vertically.
    /// </summary>
    public static float Heading(Quaternion q)
    {
        if (MathF.Abs(q.Y) < 1e-9f && MathF.Abs(q.W) < 1e-9f) return 0f;
        return 2f * MathF.Atan2(q.Y, q.W);
    }

    /// <summary>
    /// The level, yaw-only frame a rotation faces (forward flattened onto the ground), like the
    /// handling model's hover frame. Falls back to straight ahead when pointing up or down.
    /// </summary>
    public static Quaternion Level(Quaternion q)
    {
        Vector3 f = Forward(q);
        f.Y = 0f;
        if (f.LengthSquared() < 1e-6f) return Yaw(Heading(q));
        f = Vector3.Normalize(f);
        return Yaw(MathF.Atan2(-f.X, -f.Z));
    }

    public static float Angle(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b));
        return MathF.Acos(Math.Clamp(d, -1f, 1f));
    }

    /// <summary>Turns <paramref name="q"/> so its up points toward <paramref name="targetUp"/> by a share of the angle; heading kept.</summary>
    public static Quaternion TiltToward(Quaternion q, Vector3 targetUp, float share)
    {
        Vector3 up = Vector3.Normalize(Up(q));
        float angle = Angle(up, targetUp);
        if (angle < 1e-5f) return q;
        Vector3 axis = Vector3.Cross(up, targetUp);
        if (axis.LengthSquared() < 1e-10f) return q;
        return ThenWorld(q, AxisAngle(Vector3.Normalize(axis), angle * share));
    }

    /// <summary>Spherical blend between two directions (unit vectors), like Godot's Vector3.Slerp.</summary>
    public static Vector3 Slerp(Vector3 from, Vector3 to, float t)
    {
        float angle = Angle(from, to);
        if (angle < 1e-6f) return to;
        Vector3 axis = Vector3.Cross(from, to);
        if (axis.LengthSquared() < 1e-12f) return Vector3.Normalize(Vector3.Lerp(from, to, t));
        return Rotate(AxisAngle(Vector3.Normalize(axis), angle * t), from);
    }

    public static float MoveToward(float from, float to, float delta) =>
        MathF.Abs(to - from) <= delta ? to : from + MathF.Sign(to - from) * delta;

    public static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        float l = v.Length();
        return l > 1e-6f ? v / l : fallback;
    }

    public static Vector3 LimitLength(Vector3 v, float max)
    {
        float l = v.Length();
        return l > max && l > 0f ? v * (max / l) : v;
    }

    /// <summary>Removes the part of a velocity going into a surface: what's left slides along it.</summary>
    public static Vector3 Slide(Vector3 v, Vector3 normal) => v - normal * Vector3.Dot(v, normal);

    public static float WrapAngle(float a)
    {
        a %= MathF.Tau;
        if (a > MathF.PI) a -= MathF.Tau;
        else if (a < -MathF.PI) a += MathF.Tau;
        return a;
    }
}
