using System;
using System.Collections.Generic;
using System.Linq;

namespace DiegeticUi.Logic;

public enum VitalKind { Health, Food, Oxygen, Temperature, Radiation }

/// <summary>
/// Ok: the visor can fade. Warning: the vital flashes, but nothing is lost yet. Critical:
/// it is costing health right now.
/// </summary>
public enum VitalState { Ok, Warning, Critical }

/// <summary>
/// One vital and the two lines that matter to the player: where it starts flashing, and where
/// it starts to hurt. The warning line always sits well before the harm line, so the visor
/// flashes with time left to act. That gap is the whole point.
/// </summary>
public sealed class Vital
{
    public VitalKind Kind { get; init; }
    public string Label { get; init; }
    public string Unit { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double WarnLow { get; init; } = double.NegativeInfinity;
    public double WarnHigh { get; init; } = double.PositiveInfinity;
    public double HarmLow { get; init; } = double.NegativeInfinity;
    public double HarmHigh { get; init; } = double.PositiveInfinity;

    public double Value { get; internal set; }

    public VitalState State =>
        Value <= HarmLow || Value >= HarmHigh ? VitalState.Critical
        : Value <= WarnLow || Value >= WarnHigh ? VitalState.Warning
        : VitalState.Ok;

    /// <summary>0..1 across the display range, for a bar.</summary>
    public double Fraction => Math.Clamp((Value - Min) / (Max - Min), 0, 1);
}

/// <summary>What the player is standing in. The game fills this from zones and weather.</summary>
public struct Surroundings
{
    public double AmbientC;
    public double RadiationPerSecond; // mSv/s
    public bool Breathable;

    public static Surroundings ShipInterior => new() { AmbientC = 19, Breathable = true };
}

/// <summary>
/// The suit's vitals: health, food, oxygen, core temperature and radiation dose. Pure
/// simulation, no engine types, so the warning-before-harm rule is testable headless.
/// </summary>
public sealed class Vitals
{
    // Rates per second. Tuned for a prototype you can watch: real play would be slower.
    const double FoodBurn = 0.02, OxygenBurn = 0.35, OxygenRefill = 6.0;
    const double ComfortLowC = -10, ComfortHighC = 45, ProtectedLowC = -90, ProtectedHighC = 120;
    const double ChillPerDegree = 0.0045, Rewarm = 0.25;
    const double DoseDecay = 0.05, ShieldedDose = 0.15;
    const double Regen = 0.4;

    public Vital Health { get; } = new()
    {
        Kind = VitalKind.Health, Label = "HP", Unit = "%", Min = 0, Max = 100, Value = 100,
        WarnLow = 35, HarmLow = 0,
    };
    public Vital Food { get; } = new()
    {
        Kind = VitalKind.Food, Label = "FOOD", Unit = "%", Min = 0, Max = 100, Value = 82,
        WarnLow = 20, HarmLow = 0,
    };
    public Vital Oxygen { get; } = new()
    {
        Kind = VitalKind.Oxygen, Label = "O2", Unit = "%", Min = 0, Max = 100, Value = 100,
        WarnLow = 25, HarmLow = 0,
    };
    /// <summary>Core body temperature, held at 37 °C by the suit until the hazard beats it.</summary>
    public Vital Temperature { get; } = new()
    {
        Kind = VitalKind.Temperature, Label = "TEMP", Unit = "°C", Min = 30, Max = 44, Value = 37,
        WarnLow = 35.5, WarnHigh = 38.5, HarmLow = 34.0, HarmHigh = 40.0,
    };
    /// <summary>Accumulated dose in mSv.</summary>
    public Vital Radiation { get; } = new()
    {
        Kind = VitalKind.Radiation, Label = "RAD", Unit = "mSv", Min = 0, Max = 150, Value = 0,
        WarnHigh = 50, HarmHigh = 100,
    };

    public IReadOnlyList<Vital> All { get; }

    /// <summary>Health lost per second right now, from every critical vital.</summary>
    public double DamagePerSecond { get; private set; }

    public bool CostingHealth => DamagePerSecond > 0;
    public bool AllWell => All.All(v => v.State == VitalState.Ok);

    public Vitals() => All = new[] { Health, Food, Oxygen, Temperature, Radiation };

    public void Tick(double dt, Surroundings around, Hazard protection)
    {
        Food.Value = Math.Max(0, Food.Value - FoodBurn * dt);

        Oxygen.Value = around.Breathable
            ? Math.Min(100, Oxygen.Value + OxygenRefill * dt)
            : Math.Max(0, Oxygen.Value - (protection.HasFlag(Hazard.Vacuum) ? 0.5 : 1) * OxygenBurn * dt);

        // The suit holds 37 °C inside its comfort band (wider with thermal gear). Outside it,
        // the core drifts at a rate proportional to how far past the band you are.
        double low = protection.HasFlag(Hazard.Cold) ? ProtectedLowC : ComfortLowC;
        double high = protection.HasFlag(Hazard.Heat) ? ProtectedHighC : ComfortHighC;
        double drift = around.AmbientC < low ? -(low - around.AmbientC) * ChillPerDegree
            : around.AmbientC > high ? (around.AmbientC - high) * ChillPerDegree
            : 0;
        Temperature.Value = drift != 0
            ? Math.Clamp(Temperature.Value + drift * dt, Temperature.Min, Temperature.Max)
            : MoveToward(Temperature.Value, 37, Rewarm * dt);

        double dose = around.RadiationPerSecond * (protection.HasFlag(Hazard.Radiation) ? ShieldedDose : 1);
        Radiation.Value = Math.Clamp(Radiation.Value + (dose - DoseDecay) * dt, 0, Radiation.Max);

        DamagePerSecond =
            (Food.State == VitalState.Critical ? 0.5 : 0) +
            (Oxygen.State == VitalState.Critical ? 6 : 0) +
            (Temperature.State == VitalState.Critical ? 3 : 0) +
            (Radiation.State == VitalState.Critical ? 1.5 : 0);

        Health.Value = DamagePerSecond > 0
            ? Math.Max(0, Health.Value - DamagePerSecond * dt)
            : Food.Value > Food.WarnLow ? Math.Min(100, Health.Value + Regen * dt) : Health.Value;
    }

    static double MoveToward(double from, double to, double step) =>
        Math.Abs(to - from) <= step ? to : from + Math.Sign(to - from) * step;
}
