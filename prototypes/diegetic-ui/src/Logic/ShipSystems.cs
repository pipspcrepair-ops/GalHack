using System;

namespace DiegeticUi.Logic;

public enum ShipSystem { Shields, CoreGravity }

/// <summary>
/// The hidden numbers behind a ship: power, CPU and mass. The console shows them live, and,
/// more importantly, shows what a switch would do to them before you flip it.
/// </summary>
public sealed class ShipSystems
{
    public const double PowerCapKw = 260, CpuCap = 64, HullMassT = 412, MaxMassT = 520;
    const double BasePowerKw = 128, BaseCpu = 31;

    public bool Shields { get; private set; }
    public bool CoreGravity { get; private set; } = true;
    public Inventory Hold { get; }
    public int Version { get; private set; }

    public ShipSystems(Inventory hold) => Hold = hold;

    public double PowerKw => BasePowerKw + (Shields ? Cost(ShipSystem.Shields).PowerKw : 0)
                             + (CoreGravity ? Cost(ShipSystem.CoreGravity).PowerKw : 0);

    public double CpuUsed => BaseCpu + (Shields ? Cost(ShipSystem.Shields).Cpu : 0)
                             + (CoreGravity ? Cost(ShipSystem.CoreGravity).Cpu : 0);

    public double MassT => HullMassT + Hold.TotalMassKg / 1000.0;

    public static (double PowerKw, double Cpu) Cost(ShipSystem system) => system switch
    {
        ShipSystem.Shields => (72, 18),
        ShipSystem.CoreGravity => (34, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(system)),
    };

    public bool IsOn(ShipSystem system) => system == ShipSystem.Shields ? Shields : CoreGravity;

    /// <summary>The change in power and CPU if this switch were flipped now (negative when switching off).</summary>
    public (double PowerKw, double Cpu) PreviewToggle(ShipSystem system)
    {
        var (power, cpu) = Cost(system);
        return IsOn(system) ? (-power, -cpu) : (power, cpu);
    }

    public void Set(ShipSystem system, bool on)
    {
        if (IsOn(system) == on) return;
        if (system == ShipSystem.Shields) Shields = on;
        else CoreGravity = on;
        Version++;
    }

    public void Toggle(ShipSystem system) => Set(system, !IsOn(system));
}
