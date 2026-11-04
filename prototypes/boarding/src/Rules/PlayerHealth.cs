using System;
using System.Collections.Generic;
using Godot;

namespace Boarding;

/// <summary>
/// The player's health and the "no instant death" rules, separate from any node so they
/// can be tested directly:
///
/// 1. No single NPC hit takes more than a fixed share of max health (35%).
/// 2. Heavy attackers open with weak, telegraphed warning hits and ramp up to full damage.
///    If one stops hitting you for a while it forgets, and opens with warnings again.
/// 3. The first time health drops below 25% you get a short last-stand window: damage is
///    reduced and you cannot drop below 1 hp, so there's always a moment to break away.
///    It re-arms once you regenerate above the threshold.
/// </summary>
public sealed class PlayerHealth
{
    readonly Tunables _t;
    readonly Dictionary<int, (int hits, float last)> _heavies = new();
    float _time, _sinceDamage;
    bool _lastStandArmed = true;

    public float Max { get; }
    public float Current { get; private set; }
    public float LastStandRemaining { get; private set; }

    public bool Dead => Current <= 0f;
    public bool InLastStand => LastStandRemaining > 0f;
    public bool Low => Current < Max * _t.LastStandHealthFraction;
    public float Fraction => Current / Max;

    /// <summary>Damage actually taken, after every rule.</summary>
    public event Action<float> Damaged;
    public event Action LastStandStarted;

    public PlayerHealth(Tunables tunables)
    {
        _t = tunables;
        Max = tunables.PlayerHealth;
        Current = Max;
    }

    /// <summary>Applies an NPC hit and returns the damage taken.</summary>
    public float TakeNpcHit(int attackerId, float raw, bool heavy)
    {
        if (Dead) return 0f;
        float scale = heavy ? HeavyScale(attackerId) : 1f;
        float damage = Mathf.Min(raw * scale, Max * _t.NpcMaxHitFraction);
        return Apply(damage);
    }

    /// <summary>Your own breaching charge, falls, or the ship blowing up: not capped.</summary>
    public float TakeEnvironmentDamage(float amount, bool canKill)
    {
        if (Dead) return 0f;
        if (canKill && !InLastStand)
        {
            float before = Current;
            Current = Mathf.Max(0f, Current - amount);
            _sinceDamage = 0f;
            Damaged?.Invoke(before - Current);
            return before - Current;
        }
        return Apply(amount);
    }

    float HeavyScale(int attackerId)
    {
        _heavies.TryGetValue(attackerId, out var record);
        if (_time - record.last > _t.AttackerMemorySeconds) record.hits = 0;
        int n = record.hits;
        _heavies[attackerId] = (n + 1, _time);

        int warn = _t.HeavyWarningHits, ramp = _t.HeavyRampHits;
        if (n < warn) return _t.HeavyWarningScale;
        if (n < warn + ramp) return Mathf.Lerp(_t.HeavyWarningScale, 1f, (n - warn + 1f) / (ramp + 1f));
        return 1f;
    }

    float Apply(float damage)
    {
        float before = Current;
        float threshold = Max * _t.LastStandHealthFraction;

        if (InLastStand)
        {
            damage *= _t.LastStandDamageScale;
            Current = Mathf.Max(1f, Current - damage);
        }
        else
        {
            Current -= damage;
            if (Current < threshold && _lastStandArmed)
            {
                _lastStandArmed = false;
                LastStandRemaining = _t.LastStandSeconds;
                Current = Mathf.Max(1f, Current);
                LastStandStarted?.Invoke();
            }
            Current = Mathf.Max(0f, Current);
        }

        _sinceDamage = 0f;
        float taken = before - Current;
        Damaged?.Invoke(taken);
        return taken;
    }

    public void Tick(float dt)
    {
        _time += dt;
        _sinceDamage += dt;
        LastStandRemaining = Mathf.Max(0f, LastStandRemaining - dt);
        if (Dead) return;
        if (_sinceDamage >= _t.RegenDelay) Current = Mathf.Min(Max, Current + _t.RegenPerSecond * dt);
        if (!_lastStandArmed && Current >= Max * _t.LastStandHealthFraction && !InLastStand) _lastStandArmed = true;
    }

    public void Reset()
    {
        Current = Max;
        LastStandRemaining = 0f;
        _lastStandArmed = true;
        _heavies.Clear();
    }

    /// <summary>For tests: set health directly.</summary>
    public void DebugSet(float value) => Current = value;
}
