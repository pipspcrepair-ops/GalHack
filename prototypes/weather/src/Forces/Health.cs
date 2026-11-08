using System;

namespace Weather.Forces;

/// <summary>
/// Fall damage. Damage is never reduced: no per-hit cap, no last stand, and none for a tornado's
/// fling either. Its landing hurts like any other fall; the jetpack is what saves you.
/// </summary>
public static class DamageRules
{
    /// <summary>Landing slower than this is free: a 6 m drop.</summary>
    public const float SafeImpactSpeed = 11f;

    /// <summary>Fall damage per m/s over the safe speed: a 30 m drop (24 m/s) costs 52.</summary>
    public const float DamagePerImpactSpeed = 4f;

    public static float FallDamage(float impactSpeed) =>
        MathF.Max(0f, impactSpeed - SafeImpactSpeed) * DamagePerImpactSpeed;
}

/// <summary>Health with slow regeneration.</summary>
public sealed class Health
{
    public float Max { get; }
    public float Current { get; private set; }
    public bool Dead => Current <= 0f;

    /// <summary>Seconds since the last hit; regeneration waits for a quiet spell.</summary>
    public float SinceHit { get; private set; } = 999f;

    public float RegenDelay { get; set; } = 6f;
    public float RegenPerSecond { get; set; } = 2.5f;

    public Health(float max)
    {
        Max = max;
        Current = max;
    }

    public float Fraction => Current / Max;

    /// <summary>Takes a hit at full strength and returns the damage dealt.</summary>
    public float Hit(float raw)
    {
        if (raw <= 0f || Dead) return 0f;
        float damage = MathF.Min(raw, Current);
        Current -= damage;
        SinceHit = 0f;
        return damage;
    }

    public void Tick(float dt)
    {
        if (Dead) return;
        SinceHit += dt;
        if (SinceHit > RegenDelay) Current = MathF.Min(Max, Current + RegenPerSecond * dt);
    }

    /// <summary>Sets health directly (tests, respawn).</summary>
    public void Set(float value) => Current = Math.Clamp(value, 0f, Max);
}
