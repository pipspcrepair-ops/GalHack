using GalHack.Physics.Engine;
using GalHack.Items.Bridge;
using GalHack.Items.Production;
using GalHack.Structures;
using ShipHandling.Handling;

namespace Slice.Vessels;

/// <summary>
/// A structure's live power (shared/Items' <see cref="StructurePower"/>): its devices draw from the core's capacitor by
/// what they are doing, and its generators burn fuel from its storage to keep it charged. Out of power, its machines
/// stop, its lights go dark, its sealed rooms stop holding their air, its core's gravity fails, its shield drops, and a
/// flying ship loses thrust and falls. Its solar panels charge it by the sun on them before any fuel burns.
/// </summary>
public sealed partial class Vessel
{
    /// <summary>Its capacitor, generators and draw.</summary>
    public StructurePower Power { get; private set; }

    /// <summary>Power to its devices: switched on, and power to give (charge, or a generator with fuel).</summary>
    public bool Powered => PowerOn && Power.Supply > 0;

    System.Func<PowerState, Int3, Block, double> _doing;

    /// <summary>
    /// One tick of its power, in real time (<paramref name="free"/>: Creative's rule, its generators burn nothing). The share
    /// it met is what its flight gets; its core's gravity holds only while it has power.
    /// </summary>
    public void TickPower(double dt, bool free)
    {
        _doing ??= Doing;
        Power.Sun = SunOnPanels();
        Power.ShieldsOn = SystemOn(StructureBar.Shields);
        Bar.Cool(dt);
        Power.Tick(dt, PowerOn && !IsWreck && Structure.Core != null, free, _doing);
        if (Flight != null) Flight.Power = Powered ? (float)Power.Supply : 0f;
        bool gravity = Structure.GravityOn && Powered;
        if (Body.Core != null && Body.Core.On != gravity)
        {
            Body.Core.On = gravity;
            Cabin.SetGravity(Body.Core);
        }
    }

    /// <summary>
    /// The share of full sun on its panels: the sun's height here (day, night and latitude), less what the cloud over it
    /// takes, or the weather's own cut there where that is deeper (a dust storm's murk, only inside the storm).
    /// </summary>
    double SunOnPanels()
    {
        if (Power.SolarMW <= 0 || Zone.Sky == null) return 0;
        double sun = System.Math.Clamp(Zone.Sky.ToSun.Y, 0, 1);
        if (Zone.Weather?.Sim is not { } sim) return sun;
        Weather.Model.Conditions air = Slice.Surface.SurfaceWeather.AirAt(sim, Body.WorldPosition.ToGodot());
        return sun * System.Math.Min(1 - Power.Rules.SolarCloudCut * System.Math.Clamp(air.CloudCover, 0, 1), System.Math.Clamp(air.Solar, 0, 1));
    }

    /// <summary>Flying under its own power: on the flight model and not landed.</summary>
    bool Flying => Flight != null && Structure.Mode != CoreMode.Base && Controller is { Frozen: false };

    /// <summary>The handling direction a thruster pushes the hull, by the structure direction it pushes toward.</summary>
    static readonly Direction[] Pushes = { Direction.Right, Direction.Left, Direction.Up, Direction.Down, Direction.Back, Direction.Forward };

    /// <summary>The handling direction a thruster block pushes the hull (away from its nozzle).</summary>
    public static Direction PushOf(Block b) => Pushes[(int)Orientations.ToWorld(b.Rot, Dir.NegZ)];

    /// <summary>What a device is doing now, 0 to 1, in a state its function draws in (power.json).</summary>
    double Doing(PowerState state, Int3 cell, Block b) => state switch
    {
        PowerState.Throttle => Flying ? Flight.Throttle[(int)PushOf(b)] : 0,
        PowerState.Turning => Flying ? Flight.Turning : 0,
        PowerState.Hovering => Flying && Structure.Mode == CoreMode.Hover ? 1 : 0,
        PowerState.Lit => b.Off ? 0 : 1,
        PowerState.Working => Machines.TryGetValue(cell, out Constructor c) && c.Current != null ? 1 : 0,
        PowerState.Seated => SeatTaken(cell) ? 1 : 0,
        PowerState.On => 1,
        PowerState.Firing => Bar.Firing(cell) ? 1 : 0,
        PowerState.Up => Power.ShieldUp && !Power.ShieldRecharging ? 1 : 0,
        PowerState.Recharging => Power.ShieldRecharging ? 1 : 0,
        PowerState.Engaged => Power.Engaged(Structure.TypeOf(b).Function) ? 1 : 0,
        _ => 0, // a turret's idle and tracking: turrets have no aim of their own in the slice yet
    };

    bool SeatTaken(Int3 cell)
    {
        foreach (Int3 c in Roles.Seated.Values)
            if (c == cell) return true;
        return false;
    }
}
