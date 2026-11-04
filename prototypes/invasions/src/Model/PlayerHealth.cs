using System;

namespace Invasions.Model;

/// <summary>
/// Player health. Damage is never reduced or capped: a capital gun can vaporise a player on foot.
/// Fairness comes from how hostiles shoot (warning shots, an accuracy ramp and visible round travel
/// time, see <see cref="FireControl"/>), not from softening the hit.
/// </summary>
public sealed class PlayerHealth
{
    public double Max { get; }
    public double Current { get; private set; }
    public bool Alive => Current > 0;
    public double Fraction => Current / Max;

    public PlayerHealth(double max = 100)
    {
        Max = max;
        Current = max;
    }

    /// <summary>Applies a hit in full. Returns the damage taken.</summary>
    public double TakeHit(double amount)
    {
        if (!Alive || amount <= 0) return 0;
        Current = Math.Max(0, Current - amount);
        return amount;
    }

    public void Heal(double amount) => Current = Math.Min(Max, Current + Math.Max(0, amount));

    public void Respawn() => Current = Max;
}
