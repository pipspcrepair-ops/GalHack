using Godot;

namespace ShipHandling.Handling;

/// <summary>
/// Collision outcomes as plain formulas. Impact speed is the closing speed along the contact
/// normal, so scraping along a wall never counts, only running into it.
/// </summary>
public static class ImpactRules
{
    /// <summary>
    /// Damage = mass × (impact speed − safe speed)² × scale. Below the safe speed (landing,
    /// docking, nudging) it is exactly zero.
    /// </summary>
    public static float Damage(float mass, float impactSpeed, Tunables t)
    {
        float excess = Mathf.Max(0f, impactSpeed - t.Contacts.SafeSpeed);
        return mass * excess * excess * t.Contacts.DamageScale;
    }

    /// <summary>
    /// The mass that matters when two vessels meet. Both take the same damage from it, so
    /// ramming always hurts the rammer too; a heavy ship simply has far more hull to lose.
    /// </summary>
    public static float ReducedMass(float a, float b) => a * b / (a + b);

    /// <summary>Kinetic energy in MJ (mass in tonnes).</summary>
    public static float EnergyMJ(float mass, float speed) => mass * speed * speed / 2000f;

    /// <summary>Crater radius for a ground hit: grows with the cube root of energy, capped. Zero below the crater speed.</summary>
    public static float CraterRadius(float mass, float impactSpeed, Tunables t)
    {
        if (impactSpeed < t.Contacts.CraterSpeed) return 0f;
        float radius = t.Contacts.CraterScale * Mathf.Pow(EnergyMJ(mass, impactSpeed), 1f / 3f);
        return Mathf.Min(radius, t.Contacts.CraterMaxRadius);
    }

    public static float HullPoints(IVesselStructure s, Tunables t) => s.HullMass * t.Contacts.HullPointsPerTonne;
}
