using System;
using GalHack.Physics;
using GalHack.Physics.Engine;
using ShipHandling.Handling;
using N = System.Numerics;

namespace Slice.Vessels;

/// <summary>
/// Hands on a vessel's stick, as a player's are: everything here only ever sets <see cref="HandlingFlight.Input"/>,
/// so the vessel flies by the flight model and its own thrusters, whatever it is made of (capital-base's Pilot, on the
/// slice's vessels, with attitude added: a flying vessel holds whatever attitude it is left in, so a pilot levels it
/// and points its nose where it is going).
/// </summary>
public static class SkyPilot
{
    /// <summary>How hard the stick chases an attitude error, 1/s: the turn rate asked is this times the error.</summary>
    const float AttitudeGain = 1.6f;

    /// <summary>The heading (0 faces -Z, positive turns left) a flat direction points.</summary>
    public static float HeadingOf(N.Vector3 dir) => MathF.Atan2(-dir.X, -dir.Z);

    /// <summary>Stick input for a world velocity and a heading, level: thrust in the seat's frame, the turn that brings the hull level on that heading.</summary>
    public static PilotInput Velocity(Vessel v, N.Vector3 world, float heading, bool boost = false)
    {
        HandlingStats stats = v.Stats;
        float top = stats?.TopSpeed ?? 1f;
        Godot.Basis seat = v.Flight.State.Seat;
        N.Vector3 hull = MathX.Unrotate(v.Body.Rotation, world / MathF.Max(top, 1f));
        Godot.Vector3 move = seat.Inverse() * hull.ToGodot();
        return PilotInput.Hands(move, Level(v, heading), boost);
    }

    /// <summary>The turn asked to bring the hull level on a heading: pitch, yaw and roll in the seat's frame, each -1..1.</summary>
    public static Godot.Vector3 Level(Vessel v, float heading)
    {
        N.Quaternion q = v.Body.Rotation, want = MathX.Yaw(heading);
        N.Quaternion e = N.Quaternion.Normalize(N.Quaternion.Conjugate(q) * want);
        if (e.W < 0f) e = N.Quaternion.Negate(e);
        float s = MathF.Sqrt(MathF.Max(0f, 1f - e.W * e.W));
        float angle = 2f * MathF.Acos(Math.Clamp(e.W, -1f, 1f));
        N.Vector3 err = s > 1e-5f ? new N.Vector3(e.X, e.Y, e.Z) / s * angle : N.Vector3.Zero;
        float rate = Godot.Mathf.DegToRad(MathF.Max(v.Stats?.TurnRate ?? 12f, 1f));
        var limit = new N.Vector3(rate, rate, rate * (v.Stats?.RollFactor ?? 1f));
        N.Vector3 turn = err * AttitudeGain / limit;
        Godot.Vector3 seated = v.Flight.State.Seat.Inverse() * turn.ToGodot();
        return seated.Clamp(-Godot.Vector3.One, Godot.Vector3.One);
    }

    /// <summary>
    /// Down onto the ground under a point, on its gear: over it first, then down at a rate that slows near the ground, and
    /// the stick let go once a foot (or a block) touches, so the vessel's controller settles it. <paramref name="height"/>
    /// is how far its feet are above the ground there.
    /// </summary>
    public static PilotInput Land(Vessel v, N.Vector2 spot, float height, float heading, float rate = 4f)
    {
        if (v.Controller?.Gear is { Contact: true } || v.Body.OnFloor || height < 0.15f) return PilotInput.LetGo;
        N.Vector3 p = v.Body.Position;
        var across = new N.Vector3(spot.X - p.X, 0f, spot.Y - p.Z);
        N.Vector3 horizontal = MathX.LimitLength(across * 0.6f, 6f);
        float down = across.Length() > 2.5f ? 0f : -Math.Clamp(height * 0.35f, 0.7f, rate);
        return Velocity(v, horizontal + N.Vector3.UnitY * down, heading);
    }
}
