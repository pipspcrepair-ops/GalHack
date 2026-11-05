using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// Where the water is: a surface height over each column that holds water (the generator's sea
/// level and rivers, a sandbox lake). Water is a volume, everything below that surface and above
/// the ground; bodies go into it. Decided by the terrain's natural ground alone, as terrain-digging's
/// static water is, so digging never moves water: a pit dug beside a lake stays dry.
/// </summary>
public interface IWaterField
{
    /// <summary>The water surface over this point's column, if the column holds water; false on dry ground.</summary>
    bool SurfaceAt(Vector3 point, out float surface);
}

/// <summary>How a body sits in water this tick: the surface over it and the share of its height under it.</summary>
public readonly record struct WaterState(float Surface, float Fraction)
{
    public static readonly WaterState Dry = new(float.NegativeInfinity, 0f);

    public bool InWater => Fraction > 0f;

    /// <summary>
    /// Wholly under the surface. For a player this is the vitals hook: their head is under water, so
    /// the suit has to breathe its own oxygen.
    /// </summary>
    public bool Underwater => Fraction >= 1f;
}

/// <summary>
/// Water's forces, as plain formulas (the world applies them to every body in the zone's world;
/// a game with its own character controller can apply them itself):
/// - <b>buoyancy</b>: the weight of the water displaced, water density / body density × the body's
///   weight × the share of it under the surface, straight up against gravity. A body lighter than
///   water floats with that share under; a heavier one sinks to the bed;
/// - <b>drag</b>: ½ρ·(CdA × dragScale)·|v|·v plus a linear damping, both × the share under, applied
///   implicitly so they can only ever slow a body, never reverse it within a tick, however hard it
///   hits the water.
/// </summary>
public static class WaterForces
{
    /// <summary>
    /// The share of an upright body's height that is under a surface: 0 dry, 1 wholly under.
    /// <paramref name="feet"/> is the height of its bottom, <paramref name="height"/> its height.
    /// </summary>
    public static float SubmergedFraction(float feet, float height, float surface) =>
        height <= 0f ? (feet < surface ? 1f : 0f) : Math.Clamp((surface - feet) / height, 0f, 1f);

    /// <summary>Buoyancy, N: the displaced water's weight, pushing against gravity.</summary>
    public static Vector3 Buoyancy(float mass, float bodyDensity, float fraction, Vector3 gravity, PhysicsRules.WaterData rules) =>
        fraction <= 0f || bodyDensity <= 0f ? Vector3.Zero : -gravity * (mass * rules.Density / bodyDensity * fraction);

    /// <summary>
    /// Water drag on a body moving at <paramref name="velocity"/> through still water, N: quadratic
    /// drag on its streamlined area plus linear damping, as the force that takes the velocity to
    /// v / (1 + k·dt) this tick (k the drag rate), so it is stable at any speed.
    /// </summary>
    public static Vector3 Drag(float mass, float dragArea, Vector3 velocity, float fraction, PhysicsRules.WaterData rules, float dt)
    {
        if (fraction <= 0f || mass <= 0f) return Vector3.Zero;
        float k = fraction * (rules.Damping + 0.5f * rules.Density * dragArea * rules.DragScale * velocity.Length() / mass);
        return -velocity * (mass * k / (1f + k * dt));
    }
}
