using System;
using Godot;

namespace Weather.Model;

public enum FrontKind
{
    /// <summary>A line of thunderstorms with a shelf cloud along its leading edge.</summary>
    Storm,
    /// <summary>The same outflow over dry loose ground: a wall of lifted dust.</summary>
    Dust,
}

/// <summary>
/// A storm front: a long line advancing on the wind. Its cells line up just behind the leading
/// edge, the storm's deck trails behind it, and the air ahead stays open until it arrives, so it is
/// in sight before it does. A thunderstorm's carries a shelf cloud along that edge (some a roll
/// cloud ahead of it); over dry loose ground the outflow lifts a wall of dust instead.
///
/// The world is a torus, so the line runs along one of its own lattice directions and closes on
/// itself (<see cref="Period"/> long), and it comes round again every <see cref="Spacing"/> metres
/// across the world: every point measures itself against the nearest copy. The edge's bows, bays
/// and lobes are whole waves along that period, so nothing about it has a seam anywhere.
/// </summary>
public sealed class Front
{
    public int Id { get; init; }
    public FrontKind Kind { get; init; }

    /// <summary>A point of its leading edge's straight line (before its bows), on the XZ plane (Y is 0).</summary>
    public Vector3 Centre { get; internal set; }

    /// <summary>The way it advances, a unit vector on the XZ plane (x, z): close to the storm's wind.</summary>
    public Vector2 Heading { get; init; }

    /// <summary>How fast it advances, m/s.</summary>
    public float Speed { get; init; }

    /// <summary>How long its line is before it closes on itself, metres.</summary>
    public float Period { get; init; }

    /// <summary>How far apart its copies stand across the world, metres (huge on a world that doesn't wrap).</summary>
    public float Spacing { get; init; }

    /// <summary>How far along its line one copy is from the next, metres.</summary>
    public float Shear { get; init; }

    /// <summary>How far its great bows hold the edge back, metres.</summary>
    public float Bow { get; init; }

    /// <summary>How many great bows, and how many bays, along its period.</summary>
    public int Bows { get; init; }
    public int Bays { get; init; }

    /// <summary>How far behind the leading edge the storm reaches, metres.</summary>
    public float Depth { get; init; }

    /// <summary>How strong it is when fully grown, 0.5..1 (a dust wall's thickness inside, a shelf's drop).</summary>
    public float Severity { get; init; }

    /// <summary>It carries a roll cloud: a long low tube ahead of its edge, turning on its long axis.</summary>
    public bool Roll { get; init; }

    /// <summary>0..1: the phases of its bows and bays.</summary>
    public float Seed { get; init; }

    /// <summary>0 to 1: growing as it comes into sight, fully grown, fading once its weather has gone.</summary>
    public float Strength { get; internal set; }

    public Vector3 Velocity => new(Heading.X * Speed, 0f, Heading.Y * Speed);

    /// <summary>How far ahead of the leading edge its open air reaches before the next copy's storm: half the spacing less the bows.</summary>
    public float Reach => Spacing * 0.5f - Bow;

    /// <summary>Which copy of the line is nearest a point (its edge's index across the world).</summary>
    public int Copy(Vector3 p)
    {
        float dx = p.X - Centre.X, dz = p.Z - Centre.Z;
        return (int)MathF.Round((dx * Heading.X + dz * Heading.Y) / Spacing);
    }

    /// <summary>A point against the nearest copy of the front: how far along its line, and how far ahead of its edge (negative behind, under the storm).</summary>
    public (float along, float ahead) Local(Vector3 p)
    {
        float dx = p.X - Centre.X, dz = p.Z - Centre.Z;
        float u = -dx * Heading.Y + dz * Heading.X, s = dx * Heading.X + dz * Heading.Y;
        float j = MathF.Round(s / Spacing);
        u -= j * Shear;
        return (u, s - j * Spacing + Lag(u));
    }

    public float Along(Vector3 p) => Local(p).along;
    public float Ahead(Vector3 p) => Local(p).ahead;

    /// <summary>How far the edge lags behind its straight line at a distance along it: great bows, bays, lobes (sky.gdshader's front_lag).</summary>
    public float Lag(float along)
    {
        float f = along / Period;
        return Bow * (0.5f - 0.5f * MathF.Cos(MathF.Tau * (Bows * f + Seed)))
            + 220f * MathF.Sin(MathF.Tau * (Bays * f + Fract(Seed * 7.13f)))
            + 90f * MathF.Sin(MathF.Tau * (MathF.Round(Period / 1600f) * f + Fract(Seed * 3.71f)));
    }

    /// <summary>How much of the storm behind the edge lies over a point, 0 to 1.</summary>
    public float Cover(Vector3 p)
    {
        float a = Ahead(p);
        return Strength * Smooth(150f, -900f, a) * Smooth(-Depth, -Depth * 0.7f, a);
    }

    /// <summary>How open the air is at a point because the front's storm is not over it, 0 to 1: ahead of its edge, and past the deck trailing behind it.</summary>
    public float Clear(Vector3 p) => Strength * (1f - Under(p));

    /// <summary>How far a point is under the front's weather, 0 to 1, whatever its strength: behind its edge, short of the end of its deck.</summary>
    public float Under(Vector3 p)
    {
        float a = Ahead(p);
        return Smooth(900f, 0f, a) * Smooth(-Depth * 1.05f, -Depth * 0.75f, a);
    }

    /// <summary>The point of its leading edge at a distance along the copy nearest <paramref name="near"/>, on the XZ plane.</summary>
    public Vector3 EdgeAt(float along, Vector3 near)
    {
        int j = Copy(near);
        var perp = new Vector2(-Heading.Y, Heading.X);
        Vector2 at = new Vector2(Centre.X, Centre.Z) + Heading * (j * Spacing - Lag(along)) + perp * (along + j * Shear);
        return new Vector3(at.X, 0f, at.Y);
    }

    /// <summary>
    /// The line nearest a wind on a world of this size: along one of the torus's own short lattice
    /// directions, so it closes on itself, and with its copies at least <paramref name="minSpacing"/>
    /// apart. Returns the heading (within a few tens of degrees of the wind), the period, the spacing
    /// and the shear. A world that doesn't wrap (size zero) gets the wind's own heading.
    /// </summary>
    public static (Vector2 heading, float period, float spacing, float shear) Lattice(Vector2 wind, Vector2 world, float minSpacing)
    {
        Vector2 w = wind.LengthSquared() > 1e-4f ? wind.Normalized() : new Vector2(1f, 0f);
        if (world.X <= 0f || world.Y <= 0f) return (w, 40000f, 1e7f, 0f);
        (Vector2 h, float L, float S, float c) best = default;
        float bestScore = float.MinValue;
        for (int m = 0; m <= 3; m++)
        for (int n = -3; n <= 3; n++)
        {
            if ((m == 0 && n != 1) || Gcd(m, Math.Abs(n)) != 1) continue;
            var v = new Vector2(m * world.X, n * world.Y);
            float L = v.Length(), S = world.X * world.Y / L;
            var h = new Vector2(-v.Y, v.X) / L;
            if (h.Dot(w) < 0f) h = -h;
            // Close to the wind first; among those, copies far apart.
            float score = h.Dot(w) - (S < minSpacing ? 2f : 0f) + MathF.Min(S, 3f * minSpacing) / (3f * minSpacing) * 0.04f;
            if (score <= bestScore) continue;
            // The step from one copy to the next: a lattice vector one spacing ahead; how far along it shifts.
            var e = new Vector2(-h.Y, h.X);
            float shear = 0f;
            for (int i = -3; i <= 3; i++)
            for (int k = -3; k <= 3; k++)
            {
                var t = new Vector2(i * world.X, k * world.Y);
                if (MathF.Abs(t.Dot(h) - S) < 1f) { shear = t.Dot(e); i = k = 4; }
            }
            bestScore = score;
            best = (h, L, S, shear);
        }
        return (best.h, best.L, best.S, best.c);
    }

    static int Gcd(int a, int b) => b == 0 ? Math.Max(a, 1) : Gcd(b, a % b);

    static float Fract(float x) => x - MathF.Floor(x);

    internal static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
