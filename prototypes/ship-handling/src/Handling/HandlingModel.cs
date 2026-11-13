using System.Collections.Generic;
using Godot;

namespace ShipHandling.Handling;

/// <summary>The six ship-local directions a vessel can thrust in.</summary>
public enum Direction { Forward, Back, Up, Down, Left, Right }

/// <summary>The planet a vessel flies on. Pure data from the tunables file.</summary>
public sealed record Planet(string Name, float Gravity, float AtmosphereCeiling)
{
    public static Planet From(Tunables t) => new(t.Planet.Name, t.Planet.Gravity, t.Planet.AtmosphereCeiling);

    /// <summary>
    /// Inside the atmosphere a vessel has weight and the lower air top speed; above the ceiling
    /// it flies on space handling. One altitude switch keeps it simple and readable.
    /// </summary>
    public FlightConditions At(float altitude) => new(Gravity, altitude < AtmosphereCeiling);
}

/// <summary>
/// Where a vessel is flying. <see cref="PlanetGravity"/> is always the planet's, because the
/// lift margin is about taking off from it; <see cref="Gravity"/> is what actually pulls.
/// </summary>
public readonly record struct FlightConditions(float PlanetGravity, bool InAtmosphere)
{
    public float Gravity => InAtmosphere ? PlanetGravity : 0f;
}

/// <summary>
/// The four numbers every ship shows on its console and in build mode, plus what they are made
/// of. Mass is tonnes and thrust kN, so thrust / mass reads straight as m/s².
/// </summary>
public sealed class HandlingStats
{
    /// <summary>In Hover Mode: lifted by its hover plates, at hover's top speeds.</summary>
    public bool IsHover { get; init; }
    public float Mass { get; init; }

    /// <summary>Total thrust per direction, kN.</summary>
    public float[] Thrust { get; init; } = new float[6];

    public float GyroTorque { get; init; }

    /// <summary>Gyro torque / mass in °/s, before the floor and the mass band's cap.</summary>
    public float RawTurnRate { get; init; }

    /// <summary>Number two: turn rate in °/s (pitch and yaw; roll is faster by the roll factor).</summary>
    public float TurnRate { get; init; }
    public bool TurnAtFloor => RawTurnRate < TurnRate;
    public bool TurnCapped => RawTurnRate > TurnRate;
    public float RollFactor { get; init; }

    /// <summary>Number three: top speed here, m/s: the mass band's (or Hover Mode's), not the thrust's.</summary>
    public float TopSpeed { get; init; }
    public float TopSpeedAir { get; init; }
    public float TopSpeedSpace { get; init; }
    public bool InAtmosphere { get; init; }

    /// <summary>What holds it up, kN: hover lift on a hover vessel, upward thrust on anything else.</summary>
    public float UpwardThrust { get; init; }

    /// <summary>Weight on the planet, kN.</summary>
    public float Weight { get; init; }
    public float PlanetGravity { get; init; }

    /// <summary>Hover vessels only: the ride height their engines hold, m.</summary>
    public float RideHeight { get; init; }

    /// <summary>Number one: acceleration in a direction, m/s². Thrust / mass, nothing else.</summary>
    public float Accel(Direction direction) => Thrust[(int)direction] / Mass;

    /// <summary>Number four: upward thrust minus weight, kN. Negative means it can't take off.</summary>
    public float LiftMargin => UpwardThrust - Weight;
    public bool CanLiftOff => LiftMargin >= 0f;

    /// <summary>How much more cargo it could lift, tonnes.</summary>
    public float SpareLift => PlanetGravity > 0f ? LiftMargin / PlanetGravity : float.PositiveInfinity;

    /// <summary>The console's warning, shown before anyone tries to take off. Null when all is well.</summary>
    public string LiftWarning => CanLiftOff ? null : IsHover
        ? UpwardThrust <= 0f ? "NO HOVER LIFT · HOVER PLATES · 0" : $"TOO HEAVY TO HOVER · {LiftMargin:+0;-0} kN · -{-SpareLift:F1} t"
        : $"TOO HEAVY TO TAKE OFF · {LiftMargin:+0;-0} kN · -{-SpareLift:F1} t";

    /// <summary>The four numbers as console lines, the same wherever they are shown.</summary>
    public IEnumerable<string> ConsoleLines()
    {
        yield return $"ACCEL  fwd {Accel(Direction.Forward):F1}  back {Accel(Direction.Back):F1}  " +
                     $"up {Accel(Direction.Up):F1}  down {Accel(Direction.Down):F1}  " +
                     $"left {Accel(Direction.Left):F1}  right {Accel(Direction.Right):F1} m/s²";
        yield return $"TURN   {TurnRate:F0}°/s" + (TurnAtFloor ? $"  (floor; gyros alone give {RawTurnRate:F1})" : TurnCapped ? $"  (capped; gyros give {RawTurnRate:F0})" : "");
        yield return IsHover
            ? $"TOP    {TopSpeed:F0} m/s"
            : $"TOP    {TopSpeed:F0} m/s {(InAtmosphere ? "in air" : "in space")}  ({TopSpeedAir:F0} air, {TopSpeedSpace:F0} space)";
        string spare = float.IsInfinity(SpareLift) ? "" : CanLiftOff ? $"  carries {SpareLift:F0} t more" : "";
        string climb = IsHover ? $"  ride {RideHeight:F1} m" : $"  climbs {Mathf.Max(0f, LiftMargin / Mass):F1} m/s²";
        yield return $"LIFT   {LiftMargin:+0;-0} kN{climb}{spare}";
    }
}

/// <summary>
/// Turns a vessel's component data into its four numbers. Pure arithmetic, so build mode can
/// show exactly what flight will do.
/// </summary>
public static class HandlingModel
{
    /// <summary>Unit vectors for each direction, ship-local (forward is -Z).</summary>
    public static readonly Vector3[] Directions =
    {
        Vector3.Forward, Vector3.Back, Vector3.Up, Vector3.Down, Vector3.Left, Vector3.Right,
    };

    public static float Mass(IVesselStructure s)
    {
        float mass = s.HullMass + s.CargoMass;
        foreach (var t in s.Thrusters) mass += t.Mass;
        foreach (var g in s.Gyros) mass += g.Mass;
        foreach (var h in s.HoverEngines) mass += h.Mass;
        foreach (var g in s.LandingGear) mass += g.Mass;
        return mass;
    }

    /// <param name="carried">Tonnes parked on it (other craft riding it): its thrust has to move them too.</param>
    public static HandlingStats Compute(IVesselStructure s, FlightConditions where, Tunables t, float carried = 0f)
    {
        float mass = Mass(s) + carried;

        // Each thruster pushes along its facing, so it counts towards the direction it faces.
        // Its position is never read: placement adds no torque, and a lopsided build flies true.
        var thrust = new float[6];
        foreach (var thruster in s.Thrusters)
            for (int d = 0; d < 6; d++)
                thrust[d] += thruster.Thrust * Mathf.Max(0f, thruster.Facing.Normalized().Dot(Directions[d]));

        float torque = 0f;
        foreach (var gyro in s.Gyros) torque += gyro.Torque;
        float rawTurn = mass > 0f ? torque / mass : 0f;

        float hoverLift = 0f, ride = 0f;
        foreach (var engine in s.HoverEngines)
        {
            hoverLift += engine.Lift;
            ride += engine.RideHeight;
        }
        if (s.HoverEngines.Count > 0) ride /= s.HoverEngines.Count;

        // Top speeds and the turn cap come from what the structure is: its mass band, or Hover Mode's speeds.
        Tunables.BandData band = t.Band(mass);
        Tunables.SpeedData speeds = s.HoverMode ? t.Speeds.Hover : band;
        return new HandlingStats
        {
            IsHover = s.HoverMode,
            Mass = mass,
            Thrust = thrust,
            GyroTorque = torque,
            RawTurnRate = rawTurn,
            TurnRate = Mathf.Max(Mathf.Min(rawTurn, band.MaxTurn), t.Turning.Floor),
            RollFactor = t.Turning.RollFactor,
            TopSpeedAir = speeds.TopSpeedAir,
            TopSpeedSpace = speeds.TopSpeedSpace,
            TopSpeed = where.InAtmosphere ? speeds.TopSpeedAir : speeds.TopSpeedSpace,
            InAtmosphere = where.InAtmosphere,
            // One mode lifts on plates, the other on thrusters: in Hover Mode thrusters only push the craft along.
            UpwardThrust = s.HoverMode ? hoverLift : thrust[(int)Direction.Up],
            Weight = mass * where.PlanetGravity,
            PlanetGravity = where.PlanetGravity,
            RideHeight = ride,
        };
    }
}
