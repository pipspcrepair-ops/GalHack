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

    /// <summary>What the build can do with all the power it asks for: the console's numbers.</summary>
    public HandlingStats Stats { get; private set; }

    /// <summary>
    /// The share of their power its devices get, set each tick by whoever holds its power (1: all they ask for).
    /// Short, thrust and turning shrink with it; at none the ship is dead: no thrust, no assist, and it falls.
    /// </summary>
    public float Power = 1f;

    /// <summary>Last tick, the share of each direction's thrust in use (handling's <see cref="Direction"/> order), and of the gyros' turn: what they draw power by.</summary>
    public readonly float[] Throttle = new float[6];
    public float Turning { get; private set; }

    /// <summary>What it flies on now: <see cref="Stats"/> at the power it gets.</summary>
    HandlingStats _live;

    int _revision = -1;
    bool _inAir;
    float _carried, _livePower = -1f;

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
            _livePower = -1f;
        }
        // Power in steps of a twentieth, so a brownout doesn't recompute the numbers every tick.
        float power = Power >= 1f ? 1f : Power <= 0f ? 0f : Godot.Mathf.Max(0.05f, Godot.Mathf.Round(Power * 20f) / 20f);
        if (power != _livePower)
        {
            _livePower = power;
            _live = power >= 1f ? Stats : HandlingModel.Compute(Structure, Conditions(zone), Tunables, carried, power);
        }
        // Short of power, the console says so, not that it is too heavy.
        string warning = _live.CanLiftOff ? null : !Stats.CanLiftOff ? Stats.LiftWarning : power <= 0f ? "NO POWER" : "LOW POWER";
        return new VesselStats(_live.IsHover, _live.CanLiftOff, _live.RideHeight, warning);
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
        if (!input.Seated && (motion.Parked || motion.Velocity.Length() < ParkingSpeed)) input = PilotInput.Station;
        if (_livePower <= 0f)
        {
            // Dead: nothing pushes, brakes, holds or turns. It falls (hover plates let it down themselves), holding its attitude.
            System.Array.Clear(Throttle);
            Turning = 0f;
            State.ThrustLocal = Godot.Vector3.Zero;
            motion.Coasting = true;
            if (!Stats.IsHover) motion.Velocity -= N.Vector3.UnitY * (zone.Gravity * dt);
            motion.LocalSpin = N.Vector3.Zero;
            return;
        }
        FlightModel.Step(ref State, input, _live, Conditions(zone), Tunables, dt);
        bool holds = FlightModel.Holds(State, input, _live);
        motion.Coasting = !holds;
        motion.Velocity = State.Velocity.ToNumerics();
        motion.Rotation = N.Quaternion.Normalize(State.Orientation.ToNumerics());
        motion.LocalSpin = State.AngularVelocity.ToNumerics();
        Measure(zone, holds);
    }

    /// <summary>
    /// What the thrusters and gyros did this tick, as shares of all they can do: the change of velocity the flight
    /// model made, plus holding the ship's weight up while the assist holds it in gravity (with the assist off, gravity
    /// pulls and nothing pays for it), per hull direction.
    /// </summary>
    void Measure(PhysicsFlightZone zone, bool holds)
    {
        System.Array.Clear(Throttle);
        Godot.Vector3 a = State.ThrustLocal;
        if (holds && !Stats.IsHover && zone.Gravity > 0f) a += State.Orientation.Inverse() * (Godot.Vector3.Up * zone.Gravity);
        for (int axis = 0; axis < 3; axis++)
        {
            float c = a[axis];
            if (Godot.Mathf.Abs(c) < 1e-4f) continue;
            Direction d = axis switch
            {
                0 => c > 0f ? Direction.Right : Direction.Left,
                1 => c > 0f ? Direction.Up : Direction.Down,
                _ => c > 0f ? Direction.Back : Direction.Forward,
            };
            float thrust = Stats.Thrust[(int)d];
            if (thrust > 0f) Throttle[(int)d] = Godot.Mathf.Min(1f, Godot.Mathf.Abs(c) * Stats.Mass / thrust);
        }
        float rate = Godot.Mathf.DegToRad(Stats.TurnRate);
        Turning = rate > 0f ? Godot.Mathf.Min(1f, State.AngularVelocity.Length() / rate) : 0f;
    }

    public bool Seated => Input.Seated;
    public bool AnyThrust => Input.AnyThrust;
    public bool Jump => Input.Jump;

    public void Halt()
    {
        State.Cruise = false;
        State.BoostLeft = 0f;
        System.Array.Clear(Throttle); // landed: nothing pushes or turns
        Turning = 0f;
    }

    public void ToggleCruise() => FlightModel.ToggleCruise(ref State);

    public void ToggleAssist() => FlightModel.ToggleAssist(ref State);

    /// <summary>Flight assist holds it (the HUD's passive thrusters); false: they fire only when told (active).</summary>
    public bool Assisted => !State.AssistOff || Stats?.IsHover == true;
}
