using System;

namespace GalHack.Physics;

/// <summary>
/// Collision outcomes as plain formulas, the same for every body: vessels, wrecks, crates,
/// players and creatures. Impact speed is always the closing speed along the contact normal,
/// so scraping along a wall never counts, only running into it.
///
/// Damage is never capped and never scaled down. The only floor anywhere is the tornado
/// fling's, and that belongs to the weather code (it passes the floor to the body's health).
/// </summary>
public static class Impacts
{
    /// <summary>
    /// The one damage formula: mass (t) × (closing speed − safe speed)² × scale. Below the safe
    /// speed it is exactly zero. Ramming uses the reduced mass and both sides take it.
    /// </summary>
    public static float Damage(float massKg, float closingSpeed, PhysicsRules.ContactData c)
    {
        float excess = MathF.Max(0f, closingSpeed - c.SafeSpeed);
        return Units.Tonnes(massKg) * excess * excess * c.DamageScale;
    }

    /// <summary>
    /// The mass that matters when two bodies meet. Both take the same damage from it, so
    /// ramming always hurts the rammer too; a heavy ship simply has far more hull to lose.
    /// A fixed obstacle has infinite mass, so the reduced mass is the body's own.
    /// </summary>
    public static float ReducedMass(float a, float b) =>
        float.IsInfinity(b) ? a : float.IsInfinity(a) ? b : a * b / (a + b);

    /// <summary>Kinetic energy in MJ.</summary>
    public static float EnergyMJ(float massKg, float speed) => 0.5f * massKg * speed * speed / 1e6f;

    /// <summary>
    /// Crater radius for a ground hit: scale × ∛(energy in MJ), capped at the terrain's own
    /// crater cap. Zero below the crater speed, and zero when the blast is too small to remove
    /// a cell.
    /// </summary>
    public static float CraterRadius(float massKg, float closingSpeed, PhysicsRules.CraterData c)
    {
        if (closingSpeed < c.Speed) return 0f;
        float radius = MathF.Min(c.Scale * MathF.Cbrt(EnergyMJ(massKg, closingSpeed)), c.MaxRadius);
        return radius < c.MinRadius ? 0f : radius;
    }

    /// <summary>How much damage a body of this kind and mass can take.</summary>
    public static float Integrity(BodyKind kind, float massKg, PhysicsRules.IntegrityData i) =>
        Units.Tonnes(massKg) * i.PerTonne(kind);
}
