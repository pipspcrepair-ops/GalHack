using Godot;

namespace ShipHandling.Physics;

/// <summary>
/// One collision, as the rest of the game hears about it. <see cref="Other"/> is null for the
/// ground or anything else fixed. Speed is the closing speed along the normal, m/s.
/// </summary>
public sealed record ImpactInfo(
    Vessel Vessel,
    Vessel Other,
    Vector3 Point,
    Vector3 Normal,
    float Speed,
    float Damage,
    float CraterRadius)
{
    public bool HitGround => Other == null;
}

/// <summary>A crater blasted into the ground by a hard landing that wasn't one.</summary>
public readonly record struct Crater(Vector3 Position, Vector3 Normal, float Radius);
