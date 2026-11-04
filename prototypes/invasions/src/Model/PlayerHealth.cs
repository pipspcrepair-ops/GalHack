using System;
using Invasions.Director;

namespace Invasions.Model;

/// <summary>
/// Player health with the design's protection against NPC fire: no single NPC hit takes more than a
/// fixed share of max health, and dropping below a threshold opens a short grace window of reduced
/// damage, once per dip (it re-arms after healing back above the threshold or respawning).
/// </summary>
public sealed class PlayerHealth
{
    readonly PlayerSafetyData _rules;
    double _graceUntil = double.NegativeInfinity;
    bool _graceSpent;

    public double Max { get; }
    public double Current { get; private set; }
    public bool Alive => Current > 0;
    public double Fraction => Current / Max;

    public PlayerHealth(PlayerSafetyData rules, double max = 100)
    {
        _rules = rules;
        Max = max;
        Current = max;
    }

    public bool InGrace(double now) => now < _graceUntil;

    /// <summary>Applies one NPC hit at simulated time <paramref name="now"/>. Returns the damage actually taken.</summary>
    public double ApplyNpcHit(double amount, double now)
    {
        if (!Alive || amount <= 0) return 0;
        amount = Math.Min(amount, Max * _rules.MaxHitFraction);
        if (InGrace(now)) amount *= _rules.GraceDamageScale;
        Current = Math.Max(0, Current - amount);

        if (Alive && !_graceSpent && Current < Max * _rules.GraceBelowFraction)
        {
            _graceSpent = true;
            _graceUntil = now + _rules.GraceSeconds;
        }
        return amount;
    }

    public void Heal(double amount)
    {
        Current = Math.Min(Max, Current + Math.Max(0, amount));
        if (Current >= Max * _rules.GraceBelowFraction) _graceSpent = false;
    }

    public void Respawn()
    {
        Current = Max;
        _graceSpent = false;
        _graceUntil = double.NegativeInfinity;
    }
}
