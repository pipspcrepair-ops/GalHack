using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>
/// Moves what crosses a zone's edge into the next zone in one step on the main thread: a vessel
/// with whoever rides in its cabin (the sleeper in its seat or aboard), or the sleeper alone.
/// The bodies keep their identity (shared physics' <see cref="PhysicsWorld.Release"/> and
/// <see cref="PhysicsWorld.Adopt"/>: riders stay aboard, nothing lets go), their nodes move under
/// the new zone (a vessel's cabin, and anyone in it, inside its hull), and nothing is ever in both
/// zones' worlds or in neither: there is no tick between leaving one and arriving in the other.
/// The record of where it is was committed before this runs (<see cref="ZoneLedger"/>).
/// </summary>
public static class Handover
{
    /// <summary>What moved: the bodies, in the order they left.</summary>
    public static List<Body> Move(SurfaceZone from, SurfaceZone to, Survivor survivor, Vessel vessel,
        N.Vector3 position, N.Quaternion rotation, N.Vector3 velocity)
    {
        Body carried = vessel?.Body ?? survivor.Body;
        bool riding = vessel != null && survivor.Body.Frame == vessel.Body;

        // Out of the old world, as it is: its host lets go of the bindings as each body leaves.
        List<Body> moving = from.Physics.Release(carried);
        foreach (Body b in moving) from.Host.Query.Forget(b);
        if (vessel != null)
        {
            from.Physics.StuckFields.Remove(vessel.Field);
            from.Weather.Bridge?.Untrack(vessel.Body);
            from.Untrack(vessel);
        }
        if (moving.Contains(survivor.Body)) from.Weather.Bridge?.Untrack(survivor.Body);

        // The new place. Riders keep their place aboard (their positions are the cabin's).
        N.Quaternion turn = N.Quaternion.Normalize(rotation * N.Quaternion.Conjugate(carried.Rotation));
        carried.AngularVelocity = N.Vector3.Transform(carried.AngularVelocity, turn);
        if (vessel == null)
        {
            carried.Up = N.Vector3.Transform(carried.Up, turn);
            survivor.ViewFrame = N.Quaternion.Normalize(turn * survivor.ViewFrame);
        }
        carried.Position = position;
        carried.Rotation = rotation;
        carried.Velocity = velocity;

        // Into the new world.
        to.Physics.Adopt(moving);
        if (vessel != null)
        {
            vessel.Zone = to;
            to.Host.Bind(vessel.Body, vessel.Binding);
            to.Physics.StuckFields.Add(vessel.Field);
            to.Weather.Bridge?.Track(vessel.Body);
            to.Track(vessel);
            vessel.Node.Reparent(to, false);
            vessel.Binding.Teleport();
            vessel.Node.ResetPhysicsInterpolation();
        }
        if (moving.Contains(survivor.Body))
        {
            survivor.Zone = to;
            to.Host.Bind(survivor.Body, survivor.Binding);
            to.Weather.Bridge?.Track(survivor.Body);
            if (!riding)
            {
                survivor.Node.Reparent(to, false);
                survivor.Binding.Teleport();
            }
            survivor.Node.ResetPhysicsInterpolation();
        }
        return moving;
    }

    /// <summary>Which zones' worlds a body is registered in (the atomic check: exactly one).</summary>
    public static int WorldsHolding(Body body, params SurfaceZone[] zones)
    {
        int n = 0;
        foreach (SurfaceZone z in zones)
            if (z?.Physics != null && Contains(z.Physics.Bodies, body)) n++;
        return n;
    }

    static bool Contains(IReadOnlyList<Body> bodies, Body b)
    {
        for (int i = 0; i < bodies.Count; i++)
            if (bodies[i] == b) return true;
        return false;
    }
}
