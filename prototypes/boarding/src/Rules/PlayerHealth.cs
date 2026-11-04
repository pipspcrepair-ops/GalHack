using System;
using Godot;

namespace Boarding;

/// <summary>
/// The player's health. Damage is never reduced, capped or floored: what hits you lands in
/// full, and a capital gun can vaporise you on foot. Fairness lives in how enemies aim
/// (see <see cref="AimRamp"/>: warning shots first, then a ramp to their baseline accuracy),
/// not in how much a hit hurts.
///
/// Health regenerates after a few seconds without being hit. Below a threshold the HUD
/// warns with a heartbeat; that is a warning only and changes no numbers.
/// </summary>
public sealed class PlayerHealth
{
    readonly Tunables _t;
    float _sinceDamage;

    public float Max { get; }
    public float Current { get; private set; }

    public bool Dead => Current <= 0f;
    public bool Low => Current < Max * _t.LowHealthFraction;
    public float Fraction => Current / Max;

    /// <summary>Damage taken (the full amount, or what was left if it killed).</summary>
    public event Action<float> Damaged;

    public PlayerHealth(Tunables tunables)
    {
        _t = tunables;
        Max = tunables.PlayerHealth;
        Current = Max;
    }

    /// <summary>Applies damage exactly as given and returns what was taken.</summary>
    public float TakeDamage(float amount)
    {
        if (Dead || amount <= 0f) return 0f;
        float before = Current;
        Current = Mathf.Max(0f, Current - amount);
        _sinceDamage = 0f;
        float taken = before - Current;
        Damaged?.Invoke(taken);
        return taken;
    }

    public void Tick(float dt)
    {
        _sinceDamage += dt;
        if (Dead) return;
        if (_sinceDamage >= _t.RegenDelay) Current = Mathf.Min(Max, Current + _t.RegenPerSecond * dt);
    }

    public void Reset()
    {
        Current = Max;
        _sinceDamage = 0f;
    }

    /// <summary>For tests: set health directly.</summary>
    public void DebugSet(float value) => Current = value;
}
