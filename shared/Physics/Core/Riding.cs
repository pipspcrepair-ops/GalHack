using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// Riding on moving structures, the one approach for both places a player can be aboard:
/// <b>a rider keeps its state in the structure's frame, and its world pose is always
/// structure × local</b>.
///
/// - <b>Inside</b> (walkable-ship's proven cabin world): the rider is simulated in the
///   structure's never-drawn cabin world, where the cabin never moves and gravity is the core's.
///   Position and velocity are structure-local.
/// - <b>On the outer hull</b>: the rider is simulated in the main world (so terrain, other ships
///   and weather still touch it), but before it moves each tick it is carried by exactly the
///   hull's motion since the last tick: position and heading, spin included. Its velocity is
///   kept relative to the hull.
///
/// Stepping on or off either one is the same conversion: position, velocity (with the
/// structure's spin at that point) and view direction move between frames.
///
/// Why not let the physics engine's moving-platform velocity carry hull riders: it lags a tick,
/// doesn't turn the rider with the ship, loses floor contact when a ship accelerates downward
/// faster than gravity, and is a different mechanism from the cabin, which must never see
/// motion at all. Explicit carry is exact at any speed and deterministic.
/// </summary>
public static class Riding
{
    // ---- Inside: the cabin world -------------------------------------------------------

    /// <summary>Moves a rider into a structure's cabin frame. Its world position and velocity are kept.</summary>
    public static void EnterCabin(Body rider, Body structure)
    {
        Vector3 p = rider.WorldPosition, v = rider.WorldVelocity;
        rider.Platform = null;
        rider.Frame = structure;
        rider.Position = structure.ToLocal(p);
        rider.Velocity = MathX.Unrotate(structure.Rotation, v - structure.PointVelocity(p));
        rider.Rotation = Quaternion.Identity;
    }

    /// <summary>Moves a rider out of its cabin into the world, keeping the structure's velocity at the door plus its own.</summary>
    public static void LeaveCabin(Body rider)
    {
        Vector3 p = rider.WorldPosition, v = rider.WorldVelocity;
        rider.Frame = null;
        rider.Position = p;
        rider.Velocity = v;
    }

    // ---- On the outer hull ---------------------------------------------------------------

    /// <summary>The rider has landed on a structure's hull: from now on it is carried by it.</summary>
    public static void StandOn(Body rider, Body structure)
    {
        if (rider.Platform == structure) return;
        Vector3 v = rider.WorldVelocity;
        rider.Platform = structure;
        rider.Velocity = v - structure.PointVelocity(rider.Position);
        Remember(rider);
    }

    /// <summary>The rider has left the hull (jumped, walked off, flung): it keeps the hull's velocity where it left.</summary>
    public static void StepOff(Body rider)
    {
        if (rider.Platform == null) return;
        Vector3 v = rider.WorldVelocity;
        rider.Platform = null;
        rider.Velocity = v;
    }

    /// <summary>Records where the rider stands on the hull, after it has moved this tick.</summary>
    public static void Remember(Body rider)
    {
        Body hull = rider.Platform;
        if (hull == null) return;
        rider.HullLocal = hull.ToLocal(rider.Position);
        rider.HullHeading = MathX.Heading(hull.Rotation);
    }

    /// <summary>
    /// Carries the rider by the hull's motion since <see cref="Remember"/>, before it moves this
    /// tick. Returns how far the hull turned (radians about world up), for the rider's view.
    /// </summary>
    public static float Carry(Body rider)
    {
        Body hull = rider.Platform;
        if (hull == null) return 0f;
        rider.Position = hull.ToParent(rider.HullLocal);
        float heading = MathX.Heading(hull.Rotation);
        float turned = MathX.WrapAngle(heading - rider.HullHeading);
        rider.HullHeading = heading;
        return turned;
    }

    /// <summary>A direction in a structure's frame.</summary>
    public static Vector3 DirectionToLocal(Body structure, Vector3 worldDirection) => MathX.Unrotate(structure.Rotation, worldDirection);

    /// <summary>A structure-frame direction in the world.</summary>
    public static Vector3 DirectionToWorld(Body structure, Vector3 localDirection) => MathX.Rotate(structure.Rotation, localDirection);
}
