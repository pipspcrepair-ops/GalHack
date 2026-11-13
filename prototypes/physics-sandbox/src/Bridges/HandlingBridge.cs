using GalHack.Physics;
using GalHack.Physics.Engine;
using ShipHandling.Handling;
using N = System.Numerics;
using PhysicsFlightZone = GalHack.Physics.FlightZone;

namespace PhysicsSandbox.Bridges;

/// <summary>
/// The bridge between ship-handling's handling model and the shared physics module: the
/// physics world asks for the flight rules' wanted motion each tick, and this runs
/// <see cref="FlightModel.Step"/> on it, unchanged. Boost and cruise live in the handling
/// model's own flight state between ticks; velocity, orientation and spin come from the body.
/// </summary>
public sealed class HandlingFlight : IFlightRules
{
    public readonly Tunables Tunables;
    public readonly IVesselStructure Structure;

    /// <summary>The pilot's controls, set every tick by whoever flies it.</summary>
    public PilotInput Input = PilotInput.Autopilot;

    /// <summary>The handling model's own state (boost, cruise). Velocity and orientation are copied in each tick.</summary>
    public FlightState State = FlightState.At(Godot.Basis.Identity);

    public HandlingStats Stats { get; private set; }

    int _revision = -1;
    bool _inAir;
    float _carried;

    public HandlingFlight(IVesselStructure structure, Tunables tunables)
    {
        Structure = structure;
        Tunables = tunables;
    }

    public static FlightConditions Conditions(PhysicsFlightZone zone) => new(zone.PlanetGravity, zone.InAtmosphere);

    VesselStats IFlightRules.Stats(Body body, PhysicsFlightZone zone)
    {
        // Craft parked on it count in its mass: its thrust has to cover the total.
        float carried = Units.Tonnes(body.Carried);
        if (Stats == null || _revision != Structure.Revision || _inAir != zone.InAtmosphere || _carried != carried)
        {
            Stats = HandlingModel.Compute(Structure, Conditions(zone), Tunables, carried);
            _revision = Structure.Revision;
            _inAir = zone.InAtmosphere;
            _carried = carried;
        }
        return new VesselStats(Stats.IsHover, Stats.CanLiftOff, Stats.RideHeight, Stats.LiftWarning);
    }

    /// <summary>Below this speed an unmanned vessel holds station instead of holding course, m/s.</summary>
    public const float ParkingSpeed = 2f;

    public void Step(Body body, ref FlightMotion motion, PhysicsFlightZone zone, float dt)
    {
        State.Velocity = motion.Velocity.ToGodot();
        State.Orientation = motion.Rotation.ToBasis();
        State.AngularVelocity = motion.LocalSpin.ToGodot();

        // The autopilot holds course, which is right for a ship flying on while its pilot walks
        // about. A parked vessel, though, or one nearly stopped, must not drift off: holding
        // "course" would let the wind blow a parked rover away at wind speed, or a rammed ship sail
        // off at the speed it was knocked to. With nobody at the controls it holds station instead,
        // braking like a pilot who has let go of the stick.
        PilotInput input = Input;
        if (!input.Seated && (motion.Parked || motion.Velocity.Length() < ParkingSpeed)) input = PilotInput.LetGo;
        FlightModel.Step(ref State, input, Stats, Conditions(zone), Tunables, dt);
        motion.Velocity = State.Velocity.ToNumerics();
        motion.Rotation = N.Quaternion.Normalize(State.Orientation.ToNumerics());
        motion.LocalSpin = State.AngularVelocity.ToNumerics();
    }

    public bool Seated => Input.Seated;
    public bool AnyThrust => Input.AnyThrust;
    public bool Jump => Input.Jump;

    public void Halt()
    {
        State.Cruise = false;
        State.BoostLeft = 0f;
    }

    public void ToggleCruise() => FlightModel.ToggleCruise(ref State);
}
