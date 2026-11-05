using System;
using System.Numerics;
using GalHack.Physics;

namespace CapitalBase.World;

/// <summary>
/// A structure held exactly where it should be, every tick, by rule rather than by forces:
/// - <b>Anchored:</b> a base, or a capital ship still keyed into the ground, at a fixed pose in the
///   world. Nothing integrates it (it sleeps through every tick), so gravity, wind, a ram or the
///   ground dug out from under it can never move it.
/// - <b>Docked:</b> a vessel parked on a host, at a fixed pose in the host's frame. After the host
///   has moved each tick it is put back on the host, with the host's velocity at that point, so it
///   rides along exactly however the host flies, and it feels no wind of its own.
///
/// The craft keeps its <see cref="VesselController"/> aside while pinned and gets it back on
/// release, so a parked ship that lifts off is the same flying ship it was.
/// </summary>
public sealed class Pinned : IBodyController, IForceReactor
{
    /// <summary>The structure it is docked on, or null when anchored to the world.</summary>
    public readonly Body Host;

    /// <summary>The pose it is held at: world (anchored) or the host's frame (docked).</summary>
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;

    readonly Action<Body> _placed;

    Pinned(Body host, Vector3 position, Quaternion rotation, Action<Body> placed)
    {
        Host = host;
        Position = position;
        Rotation = Quaternion.Normalize(rotation);
        _placed = placed;
    }

    public bool Docked => Host != null;

    /// <summary>Held at its pose in the world.</summary>
    public static Pinned Anchor(Body body, Action<Body> placed) => new(null, body.Position, body.Rotation, placed);

    /// <summary>Held at its present pose relative to a host structure.</summary>
    public static Pinned Dock(Body body, Body host, Action<Body> placed) =>
        new(host, host.ToLocal(body.Position), Quaternion.Normalize(Quaternion.Inverse(host.Rotation) * body.Rotation), placed);

    /// <summary>Held at a saved pose relative to a host structure.</summary>
    public static Pinned DockAt(Body host, Vector3 local, Quaternion localRotation, Action<Body> placed) => new(host, local, localRotation, placed);

    /// <summary>Held at a saved pose in the world.</summary>
    public static Pinned AnchorAt(Vector3 position, Quaternion rotation, Action<Body> placed) => new(null, position, rotation, placed);

    public void Drive(PhysicsWorld world, Body body, float dt) => Hold(body);

    public void React(PhysicsWorld world, Body body, float dt) => body.Asleep = true;

    public void AfterMove(PhysicsWorld world, Body body, float dt) => Hold(body);

    /// <summary>Puts the body on its pose, at the host's velocity there (or at rest), not simulating.</summary>
    public void Hold(Body body)
    {
        if (Host != null)
        {
            body.Position = Host.ToParent(Position);
            body.Rotation = Quaternion.Normalize(Host.Rotation * Rotation);
            body.Velocity = Host.PointVelocity(body.Position);
            body.AngularVelocity = Host.AngularVelocity;
        }
        else
        {
            body.Position = Position;
            body.Rotation = Rotation;
            body.Velocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
        }
        body.Asleep = true;
        _placed?.Invoke(body);
    }
}
