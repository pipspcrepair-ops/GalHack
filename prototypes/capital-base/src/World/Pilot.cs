using System;
using GalHack.Physics;
using GalHack.Physics.Engine;
using ShipHandling.Handling;
using N = System.Numerics;

namespace CapitalBase.World;

/// <summary>
/// A simple autopilot that flies through the handling model's own controls, like a player would:
/// it only ever sets the stick. Used by the tests, the screenshots and the "land here" key.
/// </summary>
public static class Pilot
{
    /// <summary>Stick input for a world velocity (the flight model steers in the hull's own frame).</summary>
    public static PilotInput Velocity(Craft c, N.Vector3 world)
    {
        float top = c.Flight.Stats?.TopSpeed ?? c.World.Handling.Class(c.Spec.Class).TopSpeedAir;
        return PilotInput.Hands(MathX.Unrotate(c.Body.Rotation, world / top).ToGodot());
    }

    /// <summary>Toward a point, slowing as it nears it.</summary>
    public static PilotInput Toward(Craft c, N.Vector3 target, float speed) =>
        Velocity(c, MathX.LimitLength((target - c.Body.Position) * 0.5f, speed));

    /// <summary>Within this of a point (and slow), it counts as there.</summary>
    public static bool At(Craft c, N.Vector3 target, float within) =>
        N.Vector3.Distance(c.Body.Position, target) < within && c.Body.Velocity.Length() < 1.5f;

    /// <summary>
    /// Lands at a point (x, z) on whatever is under it: holds over it, comes down at
    /// <paramref name="rate"/> slowing near the ground, and lets go of the stick once a foot (or a
    /// block) touches, so the vessel controller settles it.
    /// </summary>
    public static PilotInput Land(Craft c, N.Vector2 spot, float rate)
    {
        if (c.Vessel.Gear is { Contact: true } || c.Body.OnFloor || c.HullClearance < Craft.TouchClearance) return PilotInput.LetGo;
        N.Vector3 p = c.Body.Position;
        var across = new N.Vector3(spot.X - p.X, 0f, spot.Y - p.Z);
        N.Vector3 horizontal = MathX.LimitLength(across * 0.5f, 8f);
        float height = c.Vessel.Gear != null && float.IsFinite(c.Vessel.Gear.FootAltitude) ? c.Vessel.Gear.FootAltitude : 30f;
        height = MathF.Min(height, c.HullClearance);
        float down = across.Length() > 2f ? 0f : -Math.Clamp(height * 0.4f, 0.8f, rate);
        return Velocity(c, horizontal + N.Vector3.UnitY * down);
    }
}
