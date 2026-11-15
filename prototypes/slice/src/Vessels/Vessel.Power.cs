using GalHack.Items.Bridge;
using GalHack.Items.Production;
using GalHack.Structures;
using ShipHandling.Handling;

namespace Slice.Vessels;

/// <summary>
/// A structure's live power (shared/Items' <see cref="StructurePower"/>): its devices draw from the core's capacitor by
/// what they are doing, and its generators burn fuel from its storage to keep it charged. Out of power, its machines
/// stop, its lights go dark, its sealed rooms stop holding their air, and a flying ship loses thrust and falls.
/// </summary>
public sealed partial class Vessel
{
    /// <summary>Its capacitor, generators and draw.</summary>
    public StructurePower Power { get; private set; }

    /// <summary>Power to its devices: switched on, and power to give (charge, or a generator with fuel).</summary>
    public bool Powered => PowerOn && Power.Supply > 0;

    System.Func<PowerState, Int3, Block, double> _doing;

    /// <summary>One tick of its power, in real time (<paramref name="free"/>: Creative's rule, its generators burn nothing); the share it met is what its flight gets.</summary>
    public void TickPower(double dt, bool free)
    {
        _doing ??= Doing;
        Power.Tick(dt, PowerOn && !IsWreck && Structure.Core != null, free, _doing);
        if (Flight != null) Flight.Power = Powered ? (float)Power.Supply : 0f;
    }

    /// <summary>Flying under its own power: on the flight model and not landed.</summary>
    bool Flying => Flight != null && Structure.Mode != CoreMode.Base && Controller is { Frozen: false };

    /// <summary>The handling direction a thruster pushes the hull, by the structure direction it pushes toward.</summary>
    static readonly Direction[] Pushes = { Direction.Right, Direction.Left, Direction.Up, Direction.Down, Direction.Back, Direction.Forward };

    /// <summary>What a device is doing now, 0 to 1, in a state its function draws in (power.json).</summary>
    double Doing(PowerState state, Int3 cell, Block b) => state switch
    {
        PowerState.Throttle => Flying ? Flight.Throttle[(int)Pushes[(int)Orientations.ToWorld(b.Rot, Dir.NegZ)]] : 0,
        PowerState.Turning => Flying ? Flight.Turning : 0,
        PowerState.Hovering => Flying && Structure.Mode == CoreMode.Hover ? 1 : 0,
        PowerState.Lit => b.Off ? 0 : 1,
        PowerState.Working => Machines.TryGetValue(cell, out Constructor c) && c.Current != null ? 1 : 0,
        PowerState.Seated => SeatTaken(cell) ? 1 : 0,
        PowerState.On => 1,
        _ => 0, // turrets, guns, shields and drives draw once phase 2 wires them in
    };

    bool SeatTaken(Int3 cell)
    {
        foreach (Int3 c in Roles.Seated.Values)
            if (c == cell) return true;
        return false;
    }
}
