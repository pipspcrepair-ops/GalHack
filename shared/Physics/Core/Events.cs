using System.Numerics;

namespace GalHack.Physics;

/// <summary>What a contact was with.</summary>
public enum Surface
{
    None,

    /// <summary>Terrain or anything else fixed to the planet. Fast hits here can crater it.</summary>
    Ground,

    /// <summary>A probe-only water surface.</summary>
    Water,

    /// <summary>Another registered body (see <see cref="Contact.Other"/>).</summary>
    Body,

    /// <summary>Something solid that isn't registered (a static prop, a base that never moves).</summary>
    Static,

    /// <summary>A planet's trunks, crowns and boulders (the flora layer): fixed like the ground, but a structure can break a tree (<see cref="IFloraField"/>).</summary>
    Flora,
}

/// <summary>
/// One contact as a mover found it. The normal points out of what was hit, toward the body. <see cref="Part"/> is the
/// engine's own handle on what was hit, for the field that owns it (a flora contact's shape, for <see cref="IFloraField"/>).
/// </summary>
public readonly record struct Contact(Vector3 Point, Vector3 Normal, Body Other, Surface Surface, object Part = null);

/// <summary>
/// One collision, as the rest of the game hears about it. <see cref="Other"/> is null for the
/// ground or anything else fixed. Speed is the closing speed along the normal, m/s; damage is
/// what <see cref="Body"/> took (ramming: both the same, unless one side is a player, whose own
/// safe speed is higher).
/// </summary>
public sealed record ImpactInfo(
    Body Body,
    Body Other,
    Vector3 Point,
    Vector3 Normal,
    float Speed,
    float Damage,
    Surface Surface,
    float CraterRadius)
{
    public bool HitGround => Surface == Surface.Ground;
}

/// <summary>A real hole blasted into the terrain by a ground impact, as the terrain logged it.</summary>
public sealed record CraterInfo(Body Cause, Vector3 Center, float Radius, int CellsRemoved, int CellsRefused);

/// <summary>A body found inside something and moved to open space.</summary>
public readonly record struct RescueInfo(Body Body, Vector3 From, Vector3 To, string Inside);

/// <summary>A section cut off a structure: the new wreck body, and the structure it came from.</summary>
public readonly record struct SeverInfo(Body Parent, Body Wreck);

/// <summary>An axis-aligned box in world space, for "something changed here" notices.</summary>
public readonly record struct Region(Vector3 Min, Vector3 Max)
{
    public static Region Around(Vector3 center, float radius) =>
        new(center - new Vector3(radius), center + new Vector3(radius));

    public Region Grow(float by) => new(Min - new Vector3(by), Max + new Vector3(by));

    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

    public Region Union(Region o) => new(Vector3.Min(Min, o.Min), Vector3.Max(Max, o.Max));
}
