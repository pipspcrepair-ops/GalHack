using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace GalHack.Deco;

/// <summary>
/// A seeded random stream (SplitMix64). Never <c>System.Random</c> or <c>string.GetHashCode</c>,
/// which differ between runs: the same seed must dress a room the same way every time.
/// </summary>
public sealed class DecoRng
{
    ulong _state;

    public DecoRng(ulong seed) => _state = seed ^ 0x6A09E667F3BCC909UL;

    public ulong Next()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        return DecoHash.Mix(z);
    }

    /// <summary>A uniform float in [0, 1).</summary>
    public float Float() => (Next() >> 40) / (float)(1UL << 24);
    public float Range(float min, float max) => min + (max - min) * Float();
    /// <summary>A uniform int in [0, n).</summary>
    public int Int(int n) => n <= 1 ? 0 : (int)(Next() % (ulong)n);
    /// <summary>A uniform int in [min, max], both included.</summary>
    public int Between(int min, int max) => max <= min ? min : min + Int(max - min + 1);
    public bool Chance(float p) => p > 0 && Float() < p;
    public T Pick<T>(IReadOnlyList<T> items) => items[Int(items.Count)];
    /// <summary>A new stream for one purpose, so adding draws to one never shifts another.</summary>
    public DecoRng Fork(string tag) => new(DecoHash.Mix(Next() ^ DecoHash.Fnv(tag)));
    public DecoRng Fork(ulong tag) => new(DecoHash.Mix(Next() ^ tag));

    public int PickWeighted(IReadOnlyList<float> weights)
    {
        float total = 0f;
        foreach (float w in weights) total += Math.Max(0f, w);
        if (total <= 0f) return -1;
        float r = Float() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            r -= Math.Max(0f, weights[i]);
            if (r < 0f) return i;
        }
        return weights.Count - 1;
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Int(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

public static class DecoHash
{
    /// <summary>FNV-1a, stable across runs and machines.</summary>
    public static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h;
    }

    public static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static ulong Combine(ulong a, ulong b) => Mix(a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2)));

    /// <summary>A float in [0, 1) from a hash.</summary>
    public static float Unit(ulong h) => (Mix(h) >> 40) / (float)(1UL << 24);

    /// <summary>A stable value in [0, 1) for a point in space (quantised to a millimetre).</summary>
    public static float At(Vector3 p, ulong salt)
    {
        ulong h = salt;
        h = Combine(h, (ulong)(long)MathF.Round(p.X * 1000f));
        h = Combine(h, (ulong)(long)MathF.Round(p.Y * 1000f));
        h = Combine(h, (ulong)(long)MathF.Round(p.Z * 1000f));
        return Unit(h);
    }
}

/// <summary>An sRGB colour, 0..1 per channel. Kept free of engine types.</summary>
public readonly record struct DecoRgb(float R, float G, float B)
{
    public static DecoRgb Hex(string hex)
    {
        int v = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new DecoRgb(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
    }

    public DecoRgb Lerp(DecoRgb o, float t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);
    public DecoRgb Scale(float k) => new(Math.Clamp(R * k, 0, 1), Math.Clamp(G * k, 0, 1), Math.Clamp(B * k, 0, 1));
    public float Luma => 0.299f * R + 0.587f * G + 0.114f * B;
    public string ToHex() => $"{(int)MathF.Round(R * 255):x2}{(int)MathF.Round(G * 255):x2}{(int)MathF.Round(B * 255):x2}";
}

public static class DecoGeo
{
    public const float Deg = MathF.PI / 180f;

    public static Matrix4x4 RotX(float deg) => Matrix4x4.CreateRotationX(deg * Deg);
    public static Matrix4x4 RotY(float deg) => Matrix4x4.CreateRotationY(deg * Deg);
    public static Matrix4x4 RotZ(float deg) => Matrix4x4.CreateRotationZ(deg * Deg);
    public static Matrix4x4 Move(float x, float y, float z) => Matrix4x4.CreateTranslation(x, y, z);
    public static Matrix4x4 Move(Vector3 v) => Matrix4x4.CreateTranslation(v);

    /// <summary>A rotation about an axis through a pivot (System.Numerics: row vectors, so apply left to right).</summary>
    public static Matrix4x4 About(Vector3 pivot, Matrix4x4 rotation) => Move(-pivot) * rotation * Move(pivot);

    /// <summary>
    /// Yaw about +Y as the rooms use it: local +Z (a piece's back) turns to (sin, 0, cos). Matches
    /// Godot's <c>Basis(Vector3.Up, angle)</c>.
    /// </summary>
    public static Vector2 Yaw(Vector2 xz, float deg)
    {
        float s = MathF.Sin(deg * Deg), c = MathF.Cos(deg * Deg);
        return new Vector2(xz.X * c + xz.Y * s, -xz.X * s + xz.Y * c);
    }

    public static Matrix4x4 YawMatrix(float deg) => Matrix4x4.CreateRotationY(deg * Deg);

    /// <summary>A matrix whose basis rows are the given axes (scaled) and whose origin is <paramref name="origin"/>.</summary>
    public static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z, Vector3 origin) =>
        new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, origin.X, origin.Y, origin.Z, 1);

    /// <summary>A transform taking the unit Y segment (-0.5..0.5) to the segment a-b, with the given cross-section size.</summary>
    public static Matrix4x4 Along(Vector3 a, Vector3 b, float sx, float sz)
    {
        Vector3 d = b - a;
        float len = d.Length();
        Vector3 y = len < 1e-6f ? Vector3.UnitY : d / len;
        Vector3 helper = MathF.Abs(y.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 x = Vector3.Normalize(Vector3.Cross(helper, y));
        Vector3 z = Vector3.Cross(x, y);
        return Basis(x * sx, y * len, z * sz, (a + b) * 0.5f);
    }

    /// <summary>Andrew's monotone chain: the convex hull of 2D points, counter-clockwise, without repeats.</summary>
    public static List<Vector2> Hull(List<Vector2> pts)
    {
        var p = new List<Vector2>(pts);
        p.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var unique = new List<Vector2>();
        foreach (Vector2 v in p)
            if (unique.Count == 0 || Vector2.DistanceSquared(unique[^1], v) > 1e-10f) unique.Add(v);
        if (unique.Count < 3) return unique;
        var h = new List<Vector2>();
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        foreach (Vector2 v in unique)
        {
            while (h.Count >= 2 && Cross(h[^2], h[^1], v) <= 1e-9f) h.RemoveAt(h.Count - 1);
            h.Add(v);
        }
        int lower = h.Count + 1;
        for (int i = unique.Count - 2; i >= 0; i--)
        {
            Vector2 v = unique[i];
            while (h.Count >= lower && Cross(h[^2], h[^1], v) <= 1e-9f) h.RemoveAt(h.Count - 1);
            h.Add(v);
        }
        h.RemoveAt(h.Count - 1);
        return h;
    }

    /// <summary>The closest point of a convex hull (a polygon, segment or point) to <paramref name="q"/>, and whether q is inside it.</summary>
    public static Vector2 ClosestOnHull(List<Vector2> hull, Vector2 q, out bool inside, float margin = 0f)
    {
        inside = false;
        if (hull.Count == 0) return q;
        if (hull.Count == 1) return hull[0];
        if (hull.Count >= 3)
        {
            inside = true;
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % hull.Count];
                Vector2 e = b - a;
                float cross = e.X * (q.Y - a.Y) - e.Y * (q.X - a.X);
                float len = e.Length();
                if (len > 1e-9f && cross / len < margin) { inside = false; break; }
            }
            if (inside) return q;
        }
        Vector2 best = hull[0];
        float bestD = float.MaxValue;
        int n = hull.Count == 2 ? 1 : hull.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = hull[i], b = hull[(i + 1) % hull.Count];
            Vector2 e = b - a;
            float t = e.LengthSquared() < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(q - a, e) / e.LengthSquared(), 0, 1);
            Vector2 c = a + e * t;
            float d = Vector2.DistanceSquared(c, q);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>Möller–Trumbore: distance along the ray to the triangle, or -1.</summary>
    public static float Ray(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) return -1;
        float inv = 1f / det;
        Vector3 t = o - a;
        float u = Vector3.Dot(t, p) * inv;
        if (u < 0 || u > 1) return -1;
        Vector3 q = Vector3.Cross(t, e1);
        float v = Vector3.Dot(d, q) * inv;
        if (v < 0 || u + v > 1) return -1;
        float dist = Vector3.Dot(e2, q) * inv;
        return dist > 1e-5f ? dist : -1;
    }
}
