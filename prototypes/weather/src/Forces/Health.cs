using System;

namespace Weather.Forces;

/// <summary>Fall damage and the last stand. There is no general per-hit cap: tornado flings have their own floor (see <see cref="ForceModule.FlingFloor"/>).</summary>
public static class DamageRules
{
    /// <summary>Dropping below this share of max health opens the last-stand window.</summary>
    public const float LastStandThreshold = 0.25f;
    public const float LastStandSeconds = 6f;

    /// <summary>Damage taken during the last stand is multiplied by this.</summary>
    public const float LastStandFactor = 0.3f;

    /// <summary>Landing slower than this is free: a 6 m drop.</summary>
    public const float SafeImpactSpeed = 11f;

    /// <summary>Fall damage per m/s over the safe speed: a 30 m drop (24 m/s) costs 52.</summary>
    public const float DamagePerImpactSpeed = 4f;

    public static float FallDamage(float impactSpeed) =>
        MathF.Max(0f, impactSpeed - SafeImpactSpeed) * DamagePerImpactSpeed;
}

/// <summary>Health with the last stand and slow regeneration.</summary>
public sealed class Health
{
    public float Max { get; }
    public float Current { get; private set; }
    public bool Dead => Current <= 0f;

    /// <summary>Seconds left of the last stand, 0 when it isn't running.</summary>
    public float LastStand { get; private set; }
    public bool InLastStand => LastStand > 0f;

    /// <summary>Seconds since the last hit; regeneration waits for a quiet spell.</summary>
    public float SinceHit { get; private set; } = 999f;

    public float RegenDelay { get; set; } = 6f;
    public float RegenPerSecond { get; set; } = 2.5f;

    bool _lastStandArmed = true;

    public Health(float max)
    {
        Max = max;
        Current = max;
    }

    public float Fraction => Current / Max;

    /// <summary>
    /// Takes a hit and returns the damage actually dealt. During the last stand damage is cut.
    /// <paramref name="floor"/> is the least health this hit may leave (a tornado fling passes one).
    /// </summary>
    public float Hit(float raw, float floor = 0f)
    {
        if (raw <= 0f || Dead) return 0f;
        float damage = raw;
        if (InLastStand) damage *= DamageRules.LastStandFactor;
        damage = MathF.Min(damage, MathF.Max(0f, Current - floor));
        Current -= damage;
        SinceHit = 0f;

        if (_lastStandArmed && Current > 0f && Current < DamageRules.LastStandThreshold * Max)
        {
            LastStand = DamageRules.LastStandSeconds;
            _lastStandArmed = false; // once per dip: climb back above the threshold to re-arm
        }
        return damage;
    }

    public void Tick(float dt)
    {
        if (Dead) return;
        SinceHit += dt;
        LastStand = MathF.Max(0f, LastStand - dt);
        if (SinceHit > RegenDelay) Current = MathF.Min(Max, Current + RegenPerSecond * dt);
        if (Current >= DamageRules.LastStandThreshold * Max) _lastStandArmed = true;
    }

    /// <summary>Sets health directly (tests, respawn).</summary>
    public void Set(float value)
    {
        Current = Math.Clamp(value, 0f, Max);
        LastStand = 0f;
        _lastStandArmed = Current >= DamageRules.LastStandThreshold * Max;
    }
}
