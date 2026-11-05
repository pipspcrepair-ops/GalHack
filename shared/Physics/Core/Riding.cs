using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// Riding structures, one mechanism: <b>a rider of a structure lives in that structure's cabin
/// world</b>, inside its interior or out on its hull alike. The cabin world (walkable-ship's
/// proven approach) is a never-drawn physics space holding a motionless copy of the structure's
/// colliders, outer hull and interior both, at structure-local coordinates. A rider's position,
/// velocity and orientation are structure-local; its world pose is always structure × local.
/// However the ship flies, rolls or orbits, the character controller only ever sees a still ship.
///
/// - <b>Joining</b>: a body in the zone's world that lands on (or, in the air, touches) a hull
///   joins that structure's cabin world, keeping its world position, velocity and orientation.
/// - <b>Inside</b>, down is the core's gravity (the zone's when the core is off). Walking out of an
///   airlock onto the hull never leaves the cabin world.
/// - <b>On the outer hull</b>, down is the hull face under the rider's feet when mag boots are on
///   (otherwise the same as inside), and weather reaches the rider. It lets go of the ship when it
///   jumps, or once it has been out of contact for a moment or drifted away from the hull; then it
///   goes back to the zone's world with its world velocity (ship plus its own) and, in space,
///   drifts off with that inertia.
/// </summary>
public static class Riding
{
    /// <summary>Moves a rider into a structure's cabin world. World position, velocity and orientation are kept.</summary>
    public static void EnterCabin(Body rider, Body structure)
    {
        Vector3 p = rider.WorldPosition, v = rider.WorldVelocity;
        Quaternion q = rider.WorldRotation;
        Vector3 up = rider.Frame == null ? rider.Up : MathX.Rotate(rider.Frame.Rotation, rider.Up);
        rider.Frame = structure;
        rider.Position = structure.ToLocal(p);
        rider.Velocity = MathX.Unrotate(structure.Rotation, v - structure.PointVelocity(p));
        rider.Rotation = Quaternion.Normalize(Quaternion.Conjugate(structure.Rotation) * q);
        rider.Up = MathX.Unrotate(structure.Rotation, up);
        rider.LostContactFor = 0f;
    }

    /// <summary>
    /// Moves a rider out of its cabin world into the zone's world, with the structure's velocity
    /// at that point plus its own, and its orientation as it was.
    /// </summary>
    public static void LeaveCabin(Body rider)
    {
        Body structure = rider.Frame;
        if (structure == null) return;
        Vector3 p = rider.WorldPosition, v = rider.WorldVelocity;
        Quaternion q = rider.WorldRotation;
        Vector3 up = MathX.Rotate(structure.Rotation, rider.Up);
        rider.Frame = null;
        rider.OnOuterHull = false;
        rider.Position = p;
        rider.Velocity = v;
        rider.Rotation = q;
        rider.Up = up;
        rider.LostContactFor = 0f;
    }

    /// <summary>
    /// Whether an outer-hull rider lets go of its ship this tick. A jump lets go at once; otherwise
    /// only after it has been out of contact for <see cref="PhysicsRules.RidingData.LetGoAfter"/>
    /// or has drifted more than <see cref="PhysicsRules.RidingData.LetGoDistance"/> from the hull,
    /// so walking over a bump or an edge never drops anyone. Inside the interior nothing lets go.
    /// </summary>
    public static bool LetGo(PhysicsRules.RidingData rules, Body rider, bool jumped, bool touching, float distanceFromHull, float dt)
    {
        if (rider.Frame == null) return false;
        if (!rider.OnOuterHull)
        {
            rider.LostContactFor = 0f;
            return false;
        }
        if (jumped) return true;
        rider.LostContactFor = touching ? 0f : rider.LostContactFor + dt;
        return rider.LostContactFor > rules.LetGoAfter || distanceFromHull > rules.LetGoDistance;
    }

    /// <summary>How long the rider has been out of contact with the hull it rides, s.</summary>
    public static float OutOfContactFor(Body rider) => rider.LostContactFor;

    /// <summary>
    /// Turns a walking body's frame so its up is <paramref name="up"/>, by the smallest rotation,
    /// so its heading carries across (walking forward over an edge stays forward).
    /// </summary>
    public static void SetUp(Body body, Vector3 up)
    {
        up = Vector3.Normalize(up);
        Quaternion turn = FromTo(body.Up, up);
        body.Up = up;
        body.Rotation = MathX.TiltToward(Quaternion.Normalize(turn * body.Rotation), up, 1f);
    }

    /// <summary>The smallest rotation taking direction <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        float d = Vector3.Dot(from, to);
        if (d > 0.99999f) return Quaternion.Identity;
        if (d < -0.99999f)
        {
            Vector3 axis = MathF.Abs(from.X) < 0.9f ? Vector3.Cross(from, Vector3.UnitX) : Vector3.Cross(from, Vector3.UnitZ);
            return MathX.AxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        return MathX.AxisAngle(Vector3.Normalize(Vector3.Cross(from, to)), MathF.Acos(Math.Clamp(d, -1f, 1f)));
    }

    /// <summary>A direction in a structure's frame.</summary>
    public static Vector3 DirectionToLocal(Body structure, Vector3 worldDirection) => MathX.Unrotate(structure.Rotation, worldDirection);

    /// <summary>A structure-frame direction in the world.</summary>
    public static Vector3 DirectionToWorld(Body structure, Vector3 localDirection) => MathX.Rotate(structure.Rotation, localDirection);
}
