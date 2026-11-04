using System;

namespace SurvivalVitals.Model;

public enum Pace { Idle, Walk, Run, Sprint }

/// <summary>
/// What the body is doing this tick. The scene fills it from input; tests script it.
/// <paramref name="FallSpeed"/> is the downward speed while airborne (0 on the ground).
/// </summary>
public readonly record struct Activity(Pace Pace, bool Mining = false, double FallSpeed = 0);

/// <summary>One flag per thing the visor can flash.</summary>
[Flags]
public enum Alert
{
    None = 0,
    Health = 1 << 0,
    Food = 1 << 1,
    Stamina = 1 << 2,
    Oxygen = 1 << 3,      // suit oxygen running low while you need it
    Breath = 1 << 4,      // nothing to breathe: holding your breath
    Cold = 1 << 5,
    Heat = 1 << 6,
    Radiation = 1 << 7,
    Fall = 1 << 8,        // falling faster than a safe landing
    Lava = 1 << 9,        // close to a lava pool
}

/// <summary>
/// Every way a body loses health. Harm is only ever health loss, and it stops as soon as its
/// cause stops: there are no lingering conditions to treat.
/// </summary>
public enum DamageKind { Hit, Fall, Starvation, Suffocation, Cold, Heat, Radiation, Lava, Debug }

/// <summary>What the worn gear means for the body this tick.</summary>
public readonly record struct GearState(bool HasSuit, bool HelmetOn, double OxygenCapacity, Modifiers Mods);

public sealed class TickReport
{
    /// <summary>Vitals flashing on the visor. Always includes everything in <see cref="Harm"/>.</summary>
    public Alert Warnings;
    /// <summary>Causes costing health right now.</summary>
    public Alert Harm;
    /// <summary>Health lost this tick.</summary>
    public double Damage;
    public Exposure Exposure;
    public Pace Pace;
}

/// <summary>
/// The body: health, food, stamina, suit oxygen, body temperature and radiation, with every
/// rate and threshold from vitals.json.
///
/// Exposure vitals (temperature, radiation, breath) lag behind the environment on purpose:
/// the lag is the warning. A vital crosses its warning line, the visor flashes, and only some
/// seconds later does it cross the line where health starts to drain. Health only drains while
/// the cause is still there, so stepping out of a hazard stops harm on the very next tick.
/// </summary>
public sealed class Vitals
{
    readonly VitalsRules _r;

    public double Health, Food, Stamina, Oxygen, BodyTemperature, Radiation, Breath;

    /// <summary>Ran stamina dry: no running until it is back to <c>runAgainAt</c>.</summary>
    public bool Winded { get; private set; }

    public DamageKind LastDamage { get; private set; }

    bool _fallWarned;

    public Vitals(VitalsRules rules, double oxygenCapacity)
    {
        _r = rules;
        Health = rules.Health.Max;
        Food = rules.Food.Max;
        Stamina = rules.Stamina.Max;
        Oxygen = oxygenCapacity;
        BodyTemperature = rules.Temperature.Normal;
        Breath = rules.Oxygen.HoldBreathSeconds;
    }

    public bool CanRun => !Winded && Stamina > 0;
    public bool Starving => Food <= 0;
    public bool Dead => Health <= 0;

    /// <summary>Food per second at this pace, mining or not, with the suit's hunger multiplier.</summary>
    public double FoodDrainPerSecond(Pace pace, bool mining, Modifiers mods) =>
        _r.Food.DrainPerMinute / 60.0 * _r.Food.Pace.For(pace) * (mining ? _r.Food.MiningMultiplier : 1) * mods.Hunger;

    /// <summary>Suit oxygen per second with the helmet on: full rate in no air, less in thin air.</summary>
    public double OxygenDrainPerSecond(Exposure env, Modifiers mods)
    {
        double thinness = Math.Clamp(1 - env.OxygenPercent / _r.Oxygen.BreathablePercent, 0, 1);
        return _r.Oxygen.DrainPerSecond * thinness * mods.OxygenUse;
    }

    public TickReport Tick(double dt, Activity act, Exposure env, GearState gear)
    {
        var report = new TickReport { Exposure = env };
        if (Dead) return report;
        Modifiers m = gear.Mods;
        Pace pace = act.Pace >= Pace.Run && !CanRun ? Pace.Walk : act.Pace;
        report.Pace = pace;

        // Food: always draining, faster when working hard.
        Food = Math.Max(0, Food - FoodDrainPerSecond(pace, act.Mining, m) * dt);

        // Body temperature settles toward a point set by how much felt cold or heat the suit
        // can't handle, at a slow capped rate, and recovers quickly once that is gone.
        TemperatureRules t = _r.Temperature;
        double cold = Math.Max(0, t.AmbientComfort.Low - env.Temperature - m.ColdProtection);
        double heat = Math.Max(0, env.Temperature - t.AmbientComfort.High - m.HeatProtection);
        double target = Math.Clamp(t.Normal + (heat - cold) * t.BodyDegreesPerDegree, t.Normal - 8, t.Normal + 8);
        bool worsening = target != t.Normal && Math.Sign(target - BodyTemperature) == Math.Sign(target - t.Normal);
        BodyTemperature = MoveToward(BodyTemperature, target, (worsening ? t.DriftPerSecond : t.RecoverPerSecond) * dt);

        // Stamina: running costs it, resting restores it (fastest standing still), heat saps it.
        StaminaRules s = _r.Stamina;
        double staminaRate = pace switch
        {
            Pace.Idle => s.RestIdlePerSecond,
            Pace.Walk => s.RestWalkPerSecond,
            Pace.Run => -s.RunDrainPerSecond * m.StaminaUse,
            _ => -s.SprintDrainPerSecond * m.StaminaUse,
        };
        Stamina = Math.Clamp(Stamina + (staminaRate - heat * s.HeatDrainPerDegree) * dt, 0, s.Max);
        if (Stamina <= 0) Winded = true;
        else if (Winded && Stamina >= s.RunAgainAt) Winded = false;

        // Oxygen: the suit only spends it with the helmet on in thin or no air, and refills in
        // any breathable room or cockpit. With nothing to breathe you hold your breath first.
        OxygenRules o = _r.Oxygen;
        Oxygen = Math.Min(Oxygen, gear.OxygenCapacity); // capacity shrinks when an oxygen booster comes off
        bool pressurised = env.Volume != null;
        bool airOk = pressurised || env.OxygenPercent >= o.BreathablePercent;
        if (pressurised) Oxygen = Math.Min(gear.OxygenCapacity, Oxygen + o.RefillPerSecond * dt);
        else if (gear.HelmetOn && !airOk) Oxygen = Math.Max(0, Oxygen - OxygenDrainPerSecond(env, m) * dt);
        bool breathing = airOk || (gear.HelmetOn && Oxygen > 0);
        Breath = breathing
            ? Math.Min(o.HoldBreathSeconds, Breath + o.HoldBreathSeconds * dt)
            : Math.Max(0, Breath - dt);

        // Radiation: the body's dose follows the shielded ambient level at a capped rate.
        RadiationRules r = _r.Radiation;
        double ambientRad = env.Radiation * (1 - m.RadiationShielding);
        Radiation = MoveToward(Radiation, ambientRad, (ambientRad > Radiation ? r.RisePerSecond : r.ClearPerSecond) * dt);

        // ---- Warnings, then harm -----------------------------------------------------------
        Alert warn = Alert.None, harm = Alert.None;
        double damage = 0;
        void Hurt(Alert why, DamageKind kind, double amount)
        {
            harm |= why;
            damage += Damage(amount, kind, m);
        }

        if (Food < _r.Food.WarnBelow * _r.Food.Max) warn |= Alert.Food;
        if (Food <= 0) Hurt(Alert.Food, DamageKind.Starvation, _r.Food.StarvingDamagePerSecond * dt);

        if (Stamina < s.WarnBelow * s.Max) warn |= Alert.Stamina;

        if (gear.HelmetOn && !airOk && Oxygen < o.WarnBelow * gear.OxygenCapacity) warn |= Alert.Oxygen;
        if (!breathing) warn |= Alert.Breath;
        if (!breathing && Breath <= 0) Hurt(Alert.Breath, DamageKind.Suffocation, o.SuffocationDamagePerSecond * dt);

        if (BodyTemperature < t.WarnBand.Low) warn |= Alert.Cold;
        if (BodyTemperature > t.WarnBand.High) warn |= Alert.Heat;
        if (cold > 0 && BodyTemperature < t.ComfortBand.Low)
            Hurt(Alert.Cold, DamageKind.Cold, (t.DamagePerSecond + t.DamagePerDegree * cold) * dt);
        if (heat > 0 && BodyTemperature > t.ComfortBand.High)
            Hurt(Alert.Heat, DamageKind.Heat, (t.DamagePerSecond + t.DamagePerDegree * heat) * dt);

        if (Radiation > r.WarnAbove) warn |= Alert.Radiation;
        if (Radiation > r.DamageAbove && ambientRad > r.DamageAbove)
            Hurt(Alert.Radiation, DamageKind.Radiation, (r.DamagePerSecond + r.DamagePerRad * (Radiation - r.DamageAbove)) * dt);

        if (env.LavaMetres <= _r.Lava.WarnMetres) warn |= Alert.Lava;
        if (env.LavaMetres <= 0) Hurt(Alert.Lava, DamageKind.Lava, _r.Lava.DamagePerSecond * dt);

        _fallWarned = act.FallSpeed > _r.Fall.SafeSpeed;
        if (_fallWarned) warn |= Alert.Fall;

        if (Health < _r.Health.WarnBelow * _r.Health.Max) warn |= Alert.Health;

        report.Warnings = warn | harm;
        report.Harm = harm;
        report.Damage = damage;
        return report;
    }

    /// <summary>
    /// Landing. Only a fall the visor was already flashing for can hurt, so no fall is ever
    /// silent. Returns the health lost.
    /// </summary>
    public double Land(double speed, Modifiers mods)
    {
        bool warned = _fallWarned;
        _fallWarned = false;
        if (!warned || speed <= _r.Fall.SafeSpeed) return 0;
        return Damage((speed - _r.Fall.SafeSpeed) * _r.Fall.DamagePerMetrePerSecond, DamageKind.Fall, mods);
    }

    /// <summary>Applies one damage event: armor softens hits and falls. Returns the health lost.</summary>
    public double Damage(double amount, DamageKind kind, Modifiers mods)
    {
        if (amount <= 0 || Dead) return 0;
        if (kind is DamageKind.Hit or DamageKind.Fall) amount *= 1 - mods.Armor;
        if (kind is DamageKind.Fall) amount *= mods.FallDamage;
        amount = Math.Min(amount, Health);
        Health -= amount;
        LastDamage = kind;
        return amount;
    }

    /// <summary>Restores health. Healing doesn't work while starving; returns the health gained.</summary>
    public double Heal(double amount)
    {
        if (Starving || Dead) return 0;
        double before = Health;
        Health = Math.Min(_r.Health.Max, Health + amount);
        return Health - before;
    }

    /// <summary>Clears every exposure the body carries: what a healing booth does besides health.</summary>
    public void Normalise()
    {
        BodyTemperature = _r.Temperature.Normal;
        Radiation = 0;
        Breath = _r.Oxygen.HoldBreathSeconds;
    }

    /// <summary>Back on your feet: full health and no death spiral, with at least some food.</summary>
    public void ResetForRespawn(double oxygenCapacity)
    {
        Health = _r.Health.Max;
        Food = Math.Max(Food, _r.Death.RespawnFoodFloor * _r.Food.Max);
        Stamina = _r.Stamina.Max;
        Winded = false;
        Oxygen = oxygenCapacity;
        Normalise();
    }

    /// <summary>For the K key: death on demand, to test death and respawn.</summary>
    public void Kill()
    {
        Health = 0;
        LastDamage = DamageKind.Debug;
    }

    static double MoveToward(double from, double to, double step) =>
        Math.Abs(to - from) <= step ? to : from + Math.Sign(to - from) * step;
}
