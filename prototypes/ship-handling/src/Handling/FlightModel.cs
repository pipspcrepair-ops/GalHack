using Godot;

namespace ShipHandling.Handling;

/// <summary>What the pilot asks for this tick.</summary>
public struct PilotInput
{
    /// <summary>Ship-local thrust request, each -1..1: x right, y up, z back (forward is -1).</summary>
    public Vector3 Move;

    /// <summary>Turn request, each -1..1: x pitch up, y yaw left, z roll left.</summary>
    public Vector3 Turn;

    /// <summary>Held to fire a boost burst when it is ready.</summary>
    public bool Boost;

    /// <summary>Hover vessels: hop. Edge-triggered by whoever builds the input.</summary>
    public bool Jump;

    /// <summary>False when nobody is at the controls: the autopilot holds course.</summary>
    public bool Seated;

    public static PilotInput Hands(Vector3 move, Vector3 turn = default, bool boost = false) =>
        new() { Move = move, Turn = turn, Boost = boost, Seated = true };

    public static readonly PilotInput LetGo = new() { Seated = true };
    public static readonly PilotInput Autopilot = new() { Seated = false };

    public bool AnyThrust => Move.LengthSquared() > 1e-4f || Boost || Jump;
}

/// <summary>A vessel's motion between ticks. Plain data, so a trajectory can be replayed exactly.</summary>
public struct FlightState
{
    /// <summary>World velocity, m/s.</summary>
    public Vector3 Velocity;

    /// <summary>Ship-local spin, rad/s: x pitch, y yaw, z roll.</summary>
    public Vector3 AngularVelocity;

    public Basis Orientation;

    public float BoostLeft;
    public float BoostCooldown;
    public bool Cruise;
    public float CruiseSpeed;

    /// <summary>What the thrusters did this tick, as ship-local acceleration (m/s²). Drives the flames.</summary>
    public Vector3 ThrustLocal;

    public readonly bool Boosting => BoostLeft > 0f;
    public readonly bool BoostReady => BoostLeft <= 0f && BoostCooldown <= 0f;

    public static FlightState At(Basis orientation) => new() { Orientation = orientation };
}

/// <summary>
/// The flight rules, one tick at a time. Pure and deterministic: the same state and input
/// always give the same result.
///
/// Per ship axis, the vessel chases a target velocity: the stick times top speed. Pushing the
/// stick accelerates at that direction's thrust / mass; letting go, flight assist brakes with
/// whatever thrusters face the right way (never weaker than the assist floor) and eases out the
/// last few m/s, so every stop is smooth and never overshoots. Because the target never exceeds
/// top speed, nothing (boost included) can push past it, and mass only changes how fast the
/// target is reached.
/// </summary>
public static class FlightModel
{
    public static void Step(ref FlightState s, in PilotInput input, HandlingStats stats, FlightConditions where,
        Tunables t, float dt)
    {
        // Boost: a fixed burst once triggered, then a cooldown before the next.
        if (input.Seated && input.Boost && s.BoostReady)
        {
            s.BoostLeft = t.Boost.Duration;
            s.BoostCooldown = t.Boost.Cooldown;
        }
        float boost = s.Boosting ? t.Boost.Multiplier : 1f;

        Turn(ref s, input, stats, where, t, dt);
        Translate(ref s, input, stats, where, t, dt, boost);

        if (s.BoostLeft > 0f) s.BoostLeft = Mathf.Max(0f, s.BoostLeft - dt);
        else if (s.BoostCooldown > 0f) s.BoostCooldown = Mathf.Max(0f, s.BoostCooldown - dt);
    }

    /// <summary>C: hold the current forward speed, or let go of cruise.</summary>
    public static void ToggleCruise(ref FlightState s)
    {
        s.Cruise = !s.Cruise;
        if (s.Cruise) s.CruiseSpeed = Mathf.Max(0f, -(Frame(s.Orientation, false).Inverse() * s.Velocity).Z);
    }

    /// <summary>
    /// The frame velocity is controlled in. Hover vessels steer in a level, yaw-only frame: their
    /// tilt follows the ground and must not turn "forward" into "down a hill".
    /// </summary>
    public static Basis Frame(Basis orientation, bool hover)
    {
        if (!hover) return orientation;
        Vector3 forward = -orientation.Z;
        forward.Y = 0f;
        if (forward.LengthSquared() < 1e-6f) forward = Vector3.Forward;
        return Basis.LookingAt(forward.Normalized(), Vector3.Up);
    }

    /// <summary>
    /// Gyros alone turn the ship. Thrusters add no torque, so nothing about the build can make it
    /// spin by itself. Hover vessels only yaw; their tilt belongs to the hover controller.
    /// </summary>
    static void Turn(ref FlightState s, in PilotInput input, HandlingStats stats, FlightConditions where,
        Tunables t, float dt)
    {
        float rate = Mathf.DegToRad(stats.TurnRate);
        var limit = new Vector3(rate, rate, rate * stats.RollFactor);
        Vector3 target = input.Seated ? input.Turn.Clamp(-Vector3.One, Vector3.One) * limit : Vector3.Zero;

        if (stats.IsHover)
        {
            target.X = 0f;
            target.Z = 0f;
            s.AngularVelocity.X = 0f;
            s.AngularVelocity.Z = 0f;
        }
        else if (input.Seated && where.InAtmosphere && Mathf.Abs(input.Turn.Z) < 0.05f && t.Assist.HorizonLevel > 0f)
        {
            // Horizon assist, to keep a first ship forgiving: in air, if you aren't rolling, the
            // wings ease back to level. Skipped when pointing nearly straight up or down, where
            // "level" has no meaning.
            Basis o = s.Orientation;
            if (Mathf.Abs(o.Z.Y) < 0.85f)
                target.Z = Mathf.Clamp(-o.X.Y * t.Assist.HorizonLevel, -limit.Z, limit.Z);
        }

        Vector3 spinUp = limit / Mathf.Max(t.Turning.SpinUpTime, 1e-3f);
        for (int axis = 0; axis < 3; axis++)
            s.AngularVelocity[axis] = Mathf.MoveToward(s.AngularVelocity[axis], target[axis], spinUp[axis] * dt);

        float spin = s.AngularVelocity.Length();
        if (spin > 1e-7f)
            s.Orientation = (s.Orientation * new Basis(s.AngularVelocity / spin, spin * dt)).Orthonormalized();
    }

    static void Translate(ref FlightState s, in PilotInput input, HandlingStats stats, FlightConditions where,
        Tunables t, float dt, float boost)
    {
        bool hover = stats.IsHover;
        Basis frame = Frame(s.Orientation, hover);
        Vector3 local = frame.Inverse() * s.Velocity;
        float top = stats.TopSpeed;
        Vector3 move = input.Move.Clamp(-Vector3.One, Vector3.One).LimitLength(1f);

        Vector3 desired;
        if (!input.Seated)
        {
            desired = local.LimitLength(top); // the autopilot holds course
            move = Vector3.Zero;
        }
        else if (s.Cruise)
        {
            // W and S trim the cruise speed instead of pushing directly.
            s.CruiseSpeed = Mathf.Clamp(s.CruiseSpeed - move.Z * t.Assist.CruiseRate * dt, 0f, top);
            desired = new Vector3(move.X * top, move.Y * top, -s.CruiseSpeed).LimitLength(top);
            move.Z = -1f; // cruise drives forward with the main engines, it doesn't brake
        }
        else desired = move * top;

        bool weighed = !hover && where.Gravity > 0f;
        bool sinking = weighed && !stats.CanLiftOff;
        if (hover) desired.Y = local.Y; // ride height is the hover controller's job

        Vector3 before = local;
        for (int axis = 0; axis < 3; axis++)
        {
            float dv = desired[axis] - local[axis];
            if (Mathf.Abs(dv) < 1e-7f) continue;

            Direction dir = DirectionOf(axis, dv);
            float a = stats.Accel(dir) * boost;
            if (weighed && dir == Direction.Up)
                a = Mathf.Max(0f, (stats.Thrust[(int)Direction.Up] * boost - stats.Weight) / stats.Mass); // climbing pays for weight

            bool driving = Mathf.Abs(move[axis]) > 0.01f && Mathf.Sign(move[axis]) == Mathf.Sign(dv);
            if (!driving)
            {
                // Flight assist: the floor keeps every ship stoppable. It can't hold up a ship too heavy to fly.
                if (!(sinking && dir == Direction.Up)) a = Mathf.Max(a, t.Assist.MinBrake * boost);
                if (Mathf.Abs(dv) <= t.Assist.StopBelow)
                {
                    local[axis] = desired[axis];
                    continue;
                }
            }

            a = Mathf.Min(a, Mathf.Abs(dv) * t.Assist.EaseRate); // ease out: no overshoot, no jolt
            local[axis] += Mathf.Clamp(dv, -a * dt, a * dt);
        }
        s.ThrustLocal = (local - before) / dt;

        s.Velocity = frame * local;
        // Too heavy: the thrusters are flat out and still losing to gravity.
        if (sinking) s.Velocity += Vector3.Down * (-stats.LiftMargin / stats.Mass) * dt;
    }

    static Direction DirectionOf(int axis, float sign) => axis switch
    {
        0 => sign > 0f ? Direction.Right : Direction.Left,
        1 => sign > 0f ? Direction.Up : Direction.Down,
        _ => sign > 0f ? Direction.Back : Direction.Forward,
    };
}
