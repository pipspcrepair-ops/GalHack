using System;
using System.Globalization;
using System.Numerics;

namespace Encounters.Director;

/// <summary>Small vector helpers the director needs. System.Numerics keeps it free of the engine.</summary>
public static class MathX
{
    public const float Deg = MathF.PI / 180f;

    public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        float length = v.Length();
        return length > 1e-6f ? v / length : fallback;
    }

    /// <summary>The angle between two directions, in degrees.</summary>
    public static float AngleDeg(Vector3 a, Vector3 b)
    {
        float la = a.Length(), lb = b.Length();
        if (la < 1e-6f || lb < 1e-6f) return 0f;
        return MathF.Acos(Math.Clamp(Vector3.Dot(a, b) / (la * lb), -1f, 1f)) / Deg;
    }

    public static Vector3 Rotate(Vector3 v, Vector3 axis, float radians) =>
        Vector3.Transform(v, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), radians));

    public static Vector3 AnyPerpendicular(Vector3 v)
    {
        Vector3 other = MathF.Abs(v.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(v, other));
    }

    public static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    public static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Formats with the invariant culture, so logs (and their hashes) never depend on the locale.</summary>
    public static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
