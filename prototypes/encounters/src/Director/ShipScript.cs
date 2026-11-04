using System;
using System.Numerics;

namespace Encounters.Director;

public enum Motion { Line, Arrive, Orbit, Depart }

/// <summary>
/// A scripted flight in closed form: position and heading are pure functions of time. The
/// director samples it on its slow tick to make decisions, and the renderer samples the same
/// function every frame, so ships move smoothly without the director ticking faster, and a run
/// with the same seed replays exactly.
/// </summary>
public sealed class ShipScript
{
    public Motion Kind { get; private set; }
    public double T0 { get; private set; }

    // Line: P0 + Velocity t, facing Facing (or along Velocity if Facing is zero).
    public Vector3 P0 { get; private set; }
    public Vector3 Velocity { get; private set; }
    public Vector3 Facing { get; private set; }

    // Arrive: P0 to Target in Travel seconds, slowing to a stop, then hold (facing Facing) or orbit.
    public Vector3 Target { get; private set; }
    public float Travel { get; private set; }
    public bool ThenOrbit { get; private set; }

    // Orbit: a circle in the U/V plane around Center, bobbing along the normal.
    public Vector3 Center { get; private set; }
    public Vector3 U { get; private set; }
    public Vector3 V { get; private set; }
    public float Radius { get; private set; }
    public float Omega { get; private set; }
    public float Phase { get; private set; }
    public float Bob { get; private set; }

    // Depart: along Dir, accelerating, turning from FromForward to Dir over the first seconds.
    public Vector3 Dir { get; private set; }
    public Vector3 FromForward { get; private set; }
    public float Speed0 { get; private set; }
    public float Accel { get; private set; }

    const float DepartTurnSeconds = 4f;

    public static ShipScript Line(double t0, Vector3 from, Vector3 velocity, Vector3 facing = default) =>
        new() { Kind = Motion.Line, T0 = t0, P0 = from, Velocity = velocity, Facing = facing };

    public static ShipScript Orbit(double t0, Vector3 center, Vector3 u, Vector3 v, float radius, float omega, float phase, float bob) =>
        new() { Kind = Motion.Orbit, T0 = t0, Center = center, U = u, V = v, Radius = radius, Omega = omega, Phase = phase, Bob = bob };

    /// <summary>Flies to a point, starting at <paramref name="speed"/> and slowing to a stop, then holds facing <paramref name="facing"/>.</summary>
    public static ShipScript ArriveAndHold(double t0, Vector3 from, Vector3 to, float speed, Vector3 facing) =>
        new() { Kind = Motion.Arrive, T0 = t0, P0 = from, Target = to, Travel = TravelTime(from, to, speed), Facing = facing };

    /// <summary>Flies to the point on an orbit where it starts, then circles it.</summary>
    public static ShipScript ArriveAndOrbit(double t0, Vector3 from, float speed, Vector3 center, Vector3 u, Vector3 v, float radius, float omega, float phase)
    {
        var script = new ShipScript
        {
            Kind = Motion.Arrive, T0 = t0, P0 = from, ThenOrbit = true,
            Center = center, U = u, V = v, Radius = radius, Omega = omega, Phase = phase,
        };
        script.Target = script.OrbitPoint(0f, out Vector3 tangent);
        script.Facing = tangent;
        script.Travel = TravelTime(from, script.Target, speed);
        return script;
    }

    public static ShipScript Depart(double t0, Vector3 from, Vector3 fromForward, Vector3 dir, float speed0, float accel) =>
        new() { Kind = Motion.Depart, T0 = t0, P0 = from, FromForward = fromForward, Dir = dir, Speed0 = speed0, Accel = accel };

    /// <summary>A linear slow-down from the cruise speed to a stop covers the distance in twice the cruise time.</summary>
    static float TravelTime(Vector3 from, Vector3 to, float speed) => MathF.Max(1f, 2f * Vector3.Distance(from, to) / MathF.Max(speed, 1f));

    /// <summary>True once an arriving ship has docked or reached its work site.</summary>
    public bool Arrived(double time) => Kind == Motion.Arrive && time - T0 >= Travel;

    public void Evaluate(double time, out Vector3 position, out Vector3 forward)
    {
        float t = (float)(time - T0);
        switch (Kind)
        {
            case Motion.Line:
                position = P0 + Velocity * t;
                forward = Facing != Vector3.Zero ? Facing : MathX.SafeNormalize(Velocity, -Vector3.UnitZ);
                return;

            case Motion.Orbit:
                position = OrbitPoint(t, out forward);
                return;

            case Motion.Arrive:
                if (t < Travel)
                {
                    float u = MathF.Max(0f, t / Travel);
                    float eased = 1f - (1f - u) * (1f - u);
                    position = P0 + (Target - P0) * eased;
                    Vector3 travel = MathX.SafeNormalize(Target - P0, -Vector3.UnitZ);
                    // Swing round to the final heading while slowing down, rather than snapping at the end.
                    forward = MathX.SafeNormalize(Vector3.Lerp(travel, Facing, MathX.SmoothStep(0.7f, 1f, u)), travel);
                    return;
                }
                if (ThenOrbit)
                {
                    position = OrbitPoint(t - Travel, out forward);
                    return;
                }
                position = Target;
                forward = Facing;
                return;

            default: // Depart
                position = P0 + Dir * (Speed0 * t + 0.5f * Accel * t * t);
                forward = MathX.SafeNormalize(Vector3.Lerp(FromForward, Dir, MathX.SmoothStep(0f, DepartTurnSeconds, t)), Dir);
                return;
        }
    }

    Vector3 OrbitPoint(float t, out Vector3 forward)
    {
        float theta = Phase + Omega * t;
        float c = MathF.Cos(theta), s = MathF.Sin(theta);
        Vector3 normal = Vector3.Cross(U, V);
        float bobArg = 0.6f * theta + Phase;
        Vector3 position = Center + Radius * (c * U + s * V) + normal * (Bob * MathF.Sin(bobArg));
        Vector3 velocity = Radius * Omega * (-s * U + c * V) + normal * (Bob * 0.6f * Omega * MathF.Cos(bobArg));
        forward = MathX.SafeNormalize(velocity, U);
        return position;
    }
}
