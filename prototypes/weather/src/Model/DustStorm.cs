using System;
using Godot;

namespace Weather.Model;

/// <summary>
/// A dust storm: a body of lifted dust kilometres across that rolls over the land on its own wind,
/// its leading edge a wall. It forms over dry loose ground, grows, is fed while it runs over such
/// ground and dies away off it or when its life is spent. Inside it the dust storm's weather holds,
/// as deep as you are in; outside it the world's own sky and visibility do.
/// </summary>
public sealed class DustStorm
{
    public int Id { get; init; }

    /// <summary>The middle of its footprint, on the XZ plane (Y is 0).</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>The way it moves, a unit vector on the XZ plane (x, z): its own wind's.</summary>
    public Vector2 Heading { get; init; }
    public float Speed { get; init; }

    /// <summary>Half its footprint across its way and along it, metres, and how tall it stands.</summary>
    public float HalfWidth { get; init; }
    public float HalfLength { get; init; }
    public float Height { get; init; }

    /// <summary>How far in from its outline the dust thickens to all of it, metres.</summary>
    public float Edge { get; init; }

    /// <summary>0 to 1: growing, fully grown, or dying away.</summary>
    public float Strength { get; internal set; }

    public float Age { get; internal set; }
    public float Life { get; init; }

    /// <summary>0..1, for whatever draws it.</summary>
    public float Seed { get; init; }

    /// <summary>The weather inside it, settled (the dust storm's own, on its own wind).</summary>
    public Conditions Air { get; init; }

    public Vector3 Velocity => new(Heading.X * Speed, 0f, Heading.Y * Speed);

    /// <summary>A point against it: how far along its way (ahead of its middle) and across, metres; on a wrapping world, the nearest copy.</summary>
    public Vector2 Local(Vector3 p, Vector2 world)
    {
        Vector2 d = Apart(p, Position, world);
        return new Vector2(d.X * Heading.X + d.Y * Heading.Y, -d.X * Heading.Y + d.Y * Heading.X);
    }

    /// <summary>From <paramref name="b"/> to <paramref name="a"/> on the XZ plane, to the nearest copy on a wrapping world (size zero: no wrap).</summary>
    public static Vector2 Apart(Vector3 a, Vector3 b, Vector2 world)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        if (world.X > 0f) dx -= world.X * MathF.Round(dx / world.X);
        if (world.Y > 0f) dz -= world.Y * MathF.Round(dz / world.Y);
        return new Vector2(dx, dz);
    }

    /// <summary>How far inside its outline a point is on the ground, metres (negative outside).</summary>
    public float Inside(Vector3 p, Vector2 world)
    {
        Vector2 l = Local(p, world);
        float r = new Vector2(l.X / HalfLength, l.Y / HalfWidth).Length();
        if (r < 1e-4f) return MathF.Min(HalfLength, HalfWidth);
        return l.Length() * (1f - r) / r; // along the line from its middle, to where that line crosses the outline
    }

    /// <summary>
    /// How deep in it a point is, 0 to 1: nothing outside its outline, all of it <see cref="Edge"/> in,
    /// and as strong as it is. Over its top the air is clear; <paramref name="above"/> is the point's
    /// height over the ground (0 or less: on it).
    /// </summary>
    public float Depth(Vector3 p, Vector2 world, float above)
    {
        float inside = Inside(p, world);
        if (inside <= 0f || Strength <= 0f) return 0f;
        return Strength * Front.Smooth(0f, Edge, inside) * (1f - Front.Smooth(Height * 0.7f, Height, above));
    }
}

/// <summary>How dust storms live, from data/weather.json.</summary>
public sealed class DustStormRules
{
    /// <summary>The most at once round the observer.</summary>
    public int MaxStorms { get; set; } = 2;

    public float HalfWidthMin { get; set; } = 1000f;
    public float HalfWidthMax { get; set; } = 2200f;

    /// <summary>Its length along its way as a share of its width.</summary>
    public float LengthMin { get; set; } = 0.5f;
    public float LengthMax { get; set; } = 0.75f;
    public float HeightMin { get; set; } = 300f;
    public float HeightMax { get; set; } = 650f;
    public float Edge { get; set; } = 350f;

    /// <summary>It forms this far upwind of the observer, metres (its leading edge's distance), so its wall is watched coming.</summary>
    public float FormNear { get; set; } = 2500f;
    public float FormFar { get; set; } = 5500f;

    /// <summary>Its speed as a share of its wind's.</summary>
    public float DriftShare { get; set; } = 0.55f;
    public float GrowSeconds { get; set; } = 60f;
    public float FadeSeconds { get; set; } = 90f;
    public float LifeMin { get; set; } = 420f;
    public float LifeMax { get; set; } = 840f;
    public float SpawnEvery { get; set; } = 25f;

    /// <summary>Gone once its middle is this far from the observer, metres.</summary>
    public float Range { get; set; } = 12000f;

    /// <summary>Dry loose ground (<see cref="Regions.DustGround"/>) it forms over, and keeps being fed over.</summary>
    public float FormGround { get; set; } = 0.6f;
    public float FedGround { get; set; } = 0.3f;
}
