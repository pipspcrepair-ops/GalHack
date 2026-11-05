using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// What a structure's core is set to. One structure with two modes (the design's base and
/// capital vessel): a base stays put and keeps the wildlife from spawning round it; a capital
/// vessel flies, and blocks nothing.
/// </summary>
public enum CoreMode { Base, Capital }

/// <summary>
/// A player structure as the wildlife sees it: where it stands, how big its footprint is on the
/// ground, and its core's mode. The footprint is a rectangle turned by <see cref="Yaw"/>.
/// </summary>
public sealed class Structure
{
    public string Name = "";
    public CoreMode Mode = CoreMode.Base;
    public Vector3 Center;
    /// <summary>Half the footprint's size along its own X and Z, in metres.</summary>
    public Vector2 HalfExtents = new(5f, 5f);
    public float Yaw;

    public bool IsBase => Mode == CoreMode.Base;

    /// <summary>Flat distance from a point to the footprint's edge; 0 on or inside it.</summary>
    public float DistanceToFootprint(Vector3 p)
    {
        Vector3 d = p - Center;
        float c = MathF.Cos(-Yaw), s = MathF.Sin(-Yaw);
        float lx = d.X * c + d.Z * s, lz = -d.X * s + d.Z * c;
        float ox = MathF.Max(MathF.Abs(lx) - HalfExtents.X, 0f), oz = MathF.Max(MathF.Abs(lz) - HalfExtents.Y, 0f);
        return MathF.Sqrt(ox * ox + oz * oz);
    }

    public override string ToString() => $"{Name} ({Mode}, {HalfExtents.X * 2f:F0} x {HalfExtents.Y * 2f:F0} m)";
}

/// <summary>The no-spawn rule round bases, shared by the spawner and the tests.</summary>
public static class BaseZones
{
    /// <summary>
    /// True if a creature may not spawn at <paramref name="p"/>: it lies within the clearance of a
    /// base-mode structure's footprint (plus <paramref name="margin"/>, for a whole group's spread).
    /// Capital-mode structures never block.
    /// </summary>
    public static bool Blocks(IReadOnlyList<Structure> structures, Vector3 p, float clearance, float margin = 0f)
    {
        foreach (Structure s in structures)
            if (s.IsBase && s.DistanceToFootprint(p) < clearance + margin) return true;
        return false;
    }

    /// <summary>The nearest base-mode footprint edge to a point, or infinity if there are no bases.</summary>
    public static float NearestBase(IReadOnlyList<Structure> structures, Vector3 p)
    {
        float best = float.PositiveInfinity;
        foreach (Structure s in structures)
            if (s.IsBase) best = MathF.Min(best, s.DistanceToFootprint(p));
        return best;
    }
}
