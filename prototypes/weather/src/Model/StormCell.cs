using System;
using Godot;

namespace Weather.Model;

/// <summary>
/// A storm you can see and walk out from under: its dark tower of cloud, the rain shaft below it,
/// and the strikes and funnels that come out of it. Cells drift with the wind; a funnel holds its
/// cell over it for as long as it lives. How much of a weather's rain is the cells' and how much
/// falls everywhere is the weather's <see cref="Param.Cellular"/>.
/// </summary>
public sealed class StormCell
{
    public int Id { get; init; }

    /// <summary>Under the middle of the tower, on the XZ plane (Y is 0).</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>How far out its rain reaches, metres.</summary>
    public float Radius { get; init; }

    /// <summary>0 to 1: building up, fully grown, or dying away.</summary>
    public float Strength { get; internal set; }

    /// <summary>The funnel it has let down, or null.</summary>
    public Tornado Funnel { get; internal set; }

    /// <summary>How much of it lies over a point, 0 to 1: all of it in the core, nothing past its edge.</summary>
    public float Cover(Vector3 p)
    {
        float d = new Vector2(p.X - Position.X, p.Z - Position.Z).Length();
        float t = Math.Clamp((Radius - d) / (Radius * 0.5f), 0f, 1f);
        return Strength * t * t * (3f - 2f * t);
    }
}

/// <summary>How storm cells live, from data/weather.json.</summary>
public sealed class CellRules
{
    public int MaxCells { get; set; } = 4;

    /// <summary>A weather makes cells from this much rain (or its match in lightning).</summary>
    public float From { get; set; } = 0.15f;
    public float RadiusMin { get; set; } = 420f;
    public float RadiusMax { get; set; } = 800f;

    /// <summary>Cells form upwind out to this far and are gone past it, metres.</summary>
    public float Range { get; set; } = 2800f;

    /// <summary>A cell's speed as a share of the wind's.</summary>
    public float DriftShare { get; set; } = 0.7f;
    public float GrowSeconds { get; set; } = 30f;
    public float SpawnEvery { get; set; } = 12f;
}
