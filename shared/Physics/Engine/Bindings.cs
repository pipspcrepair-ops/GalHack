using System.Collections.Generic;
using Godot;
using N = System.Numerics;

namespace GalHack.Physics.Engine;

/// <summary>
/// Ties one registered <see cref="Body"/> to the node that represents it in the engine. The
/// body is the truth for kinematic bodies (the binding writes the node); for free rigid bodies
/// the engine integrates and the binding reads the result back.
/// </summary>
public abstract class BodyBinding
{
    public Body Body { get; internal set; }

    /// <summary>The node whose transform is the body's.</summary>
    public abstract Node3D Node { get; }

    /// <summary>The collision object the engine sees (for ray exclusion and collider lookups).</summary>
    public abstract CollisionObject3D Collider { get; }

    /// <summary>Kinematic bodies move in this order: hulls first, then creatures, then characters (riders need their hull moved first).</summary>
    public virtual int MoveOrder => 0;

    /// <summary>Before forces: read anything the engine changed since the last tick.</summary>
    public virtual void Sync(PhysicsHost host, float dt) { }

    /// <summary>Kinematic bodies: sweep through the world with collisions and report the contacts.</summary>
    public virtual void Move(PhysicsHost host, float dt) { }

    /// <summary>After the tick: free bodies hand their summed force and impulse to the engine.</summary>
    public virtual void Flush(PhysicsHost host, float dt) { }

    /// <summary>The body was moved by the rules (a rescue, a placement): put the node there, at rest.</summary>
    public abstract void Teleport();

    /// <summary>Something changed nearby: wake a sleeping engine body.</summary>
    public virtual void Wake() { }
}

/// <summary>
/// A structure's hull flown kinematically (vessels): one CharacterBody3D whose shapes are the
/// structure's per-chunk colliders, swept with MoveAndCollide so it slides and glances off what
/// it meets. Ported from ship-handling's Vessel.Move.
/// </summary>
public sealed class HullBinding : BodyBinding
{
    public readonly CharacterBody3D Hull;
    readonly List<Contact> _contacts = new();
    readonly HashSet<ulong> _seen = new();

    public HullBinding(CharacterBody3D hull)
    {
        Hull = hull;
        hull.MotionMode = CharacterBody3D.MotionModeEnum.Floating;
        hull.CollisionLayer = Layers.Hull;
        hull.CollisionMask = Layers.VesselMask;
        hull.SafeMargin = 0.02f;
    }

    public override Node3D Node => Hull;
    public override CollisionObject3D Collider => Hull;

    public override void Move(PhysicsHost host, float dt)
    {
        Body b = Body;
        Hull.GlobalTransform = GodotMath.TransformOf(b); // turning, gear lifts and settling happened in the rules
        if (b.Asleep) return;

        Vector3 before = b.Velocity.ToGodot(), velocity = before;
        Vector3 motion = before * dt;
        _contacts.Clear();
        _seen.Clear();
        b.OnFloor = false;

        // Always move at least once, even standing still: that is what pushes a hull back out
        // if turning swung it into a rock.
        for (int i = 0; i < 4; i++)
        {
            KinematicCollision3D hit = Hull.MoveAndCollide(motion, false, Hull.SafeMargin);
            if (hit == null) break;
            Vector3 n = hit.GetNormal();
            GodotObject collider = hit.GetCollider();
            if (_seen.Add(collider.GetInstanceId()))
            {
                Body other = host.BodyOf(collider);
                _contacts.Add(new Contact(hit.GetPosition().ToNumerics(), n.ToNumerics(), other,
                    other != null ? Surface.Body : GodotQuery.SurfaceOf(collider)));
            }
            if (velocity.Dot(n) < 0f) velocity = velocity.Slide(n);
            motion = hit.GetRemainder().Slide(n);
            if (motion.LengthSquared() < 1e-10f) break;
        }

        b.Position = Hull.GlobalPosition.ToNumerics();
        b.Velocity = host.World.ResolveContacts(b, _contacts, before.ToNumerics(), velocity.ToNumerics());
    }

    public override void Teleport()
    {
        Hull.GlobalTransform = GodotMath.TransformOf(Body);
        Hull.ResetPhysicsInterpolation();
    }
}

/// <summary>
/// A free rigid body (crates, wrecks). The engine's own gravity and damping are off: the core
/// hands it the summed force of the one force path each tick, so its acceleration is exactly
/// force / mass. Impacts are found as velocity changes the forces don't explain, along the
/// contact normal, and go through the same damage rules as everything else.
/// </summary>
public sealed class FreeBinding : BodyBinding
{
    public readonly RigidBody3D Rigid;
    Vector3 _expected;
    bool _hasExpected;
    readonly List<Contact> _contacts = new();

    public FreeBinding(RigidBody3D rigid, Body body)
    {
        Rigid = rigid;
        Body = body;
        rigid.GravityScale = 0f;
        rigid.LinearDampMode = RigidBody3D.DampMode.Replace;
        rigid.LinearDamp = 0f;
        rigid.AngularDampMode = RigidBody3D.DampMode.Replace;
        rigid.AngularDamp = 0.1f;
        rigid.ContactMonitor = true;
        rigid.MaxContactsReported = 4;
        rigid.CanSleep = true;
        rigid.Mass = body.Mass;
        rigid.CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom;
        rigid.CenterOfMass = body.CenterOfMass.ToGodot();
        // Free bodies don't bounce, so a velocity change along the normal is the full closing speed.
        rigid.PhysicsMaterialOverride ??= new PhysicsMaterial { Bounce = 0f, Friction = 0.8f };
    }

    public override Node3D Node => Rigid;
    public override CollisionObject3D Collider => Rigid;

    /// <summary>Velocity of the centre of mass, which is what the engine keeps.</summary>
    N.Vector3 ComVelocity(Body b) => b.PointVelocity(b.CenterOfMassPosition);

    public override void Sync(PhysicsHost host, float dt)
    {
        Body b = Body;
        Transform3D t = Rigid.GlobalTransform;
        b.Position = t.Origin.ToNumerics();
        b.Rotation = t.Basis.ToNumerics();
        b.AngularVelocity = Rigid.AngularVelocity.ToNumerics();
        Vector3 vcom = Rigid.LinearVelocity;
        b.Velocity = vcom.ToNumerics() - N.Vector3.Cross(b.AngularVelocity, b.CenterOfMassPosition - b.Position);
        b.Asleep = Rigid.Sleeping;
        b.OnFloor = false;

        if (!_hasExpected) return;
        _hasExpected = false;
        Vector3 unexplained = vcom - _expected;
        if (unexplained.Length() <= host.World.Rules.Contacts.EventSpeed) return;

        _contacts.Clear();
        PhysicsDirectBodyState3D state = PhysicsServer3D.BodyGetDirectState(Rigid.GetRid());
        int count = state?.GetContactCount() ?? 0;
        for (int i = 0; i < count; i++)
        {
            GodotObject collider = state.GetContactColliderObject(i);
            Body other = host.BodyOf(collider);
            _contacts.Add(new Contact(state.GetContactLocalPosition(i).ToNumerics(), state.GetContactLocalNormal(i).ToNumerics(),
                other, other != null ? Surface.Body : GodotQuery.SurfaceOf(collider)));
        }
        host.World.FreeBodyImpact(b, unexplained.ToNumerics(), _contacts);
    }

    public override void Flush(PhysicsHost host, float dt)
    {
        Body b = Body;
        if (b.Asleep && b.PendingTotalForce == N.Vector3.Zero && b.PendingTotalImpulse == N.Vector3.Zero) return;
        if (Rigid.Sleeping) Rigid.Sleeping = false;
        Vector3 force = b.PendingTotalForce.ToGodot(), impulse = b.PendingTotalImpulse.ToGodot();
        if (force != Vector3.Zero) Rigid.ApplyCentralForce(force);
        if (impulse != Vector3.Zero) Rigid.ApplyCentralImpulse(impulse);
        if (b.PendingSpinImpulse != N.Vector3.Zero) Rigid.ApplyTorqueImpulse(b.PendingSpinImpulse.ToGodot());
        _expected = Rigid.LinearVelocity + force / b.Mass * dt + impulse / b.Mass;
        _hasExpected = true;
    }

    public override void Teleport()
    {
        Body b = Body;
        Rigid.GlobalTransform = GodotMath.TransformOf(b);
        Rigid.LinearVelocity = ComVelocity(b).ToGodot();
        Rigid.AngularVelocity = b.AngularVelocity.ToGodot();
        Rigid.Sleeping = false;
        Rigid.ResetPhysicsInterpolation();
        _hasExpected = false;
    }

    /// <summary>Starts the engine body off with the core body's motion (spawning a wreck mid-flight).</summary>
    public void Launch()
    {
        Teleport();
    }

    public override void Wake()
    {
        if (Rigid.Sleeping) Rigid.Sleeping = false;
        Body.Asleep = false;
    }
}

/// <summary>
/// A walking body (players, creatures) on a CharacterBody3D. It rides hulls the shared way
/// (see <see cref="Riding"/>): carried by the hull's motion before it moves, then it walks with
/// the engine's character controller. Engine platform velocity is switched off so nothing
/// carries it twice. Landing and slamming into things are impacts like any other.
/// </summary>
public sealed class CharacterBinding : BodyBinding
{
    public readonly CharacterBody3D Character;

    /// <summary>The hull turned under a rider by this much this tick (radians about up): turn the view with it.</summary>
    public event System.Action<float> Carried;

    public CharacterBinding(CharacterBody3D character)
    {
        Character = character;
        character.MotionMode = CharacterBody3D.MotionModeEnum.Grounded;
        character.PlatformFloorLayers = 0;
        character.PlatformWallLayers = 0;
        character.PlatformOnLeave = CharacterBody3D.PlatformOnLeaveEnum.DoNothing;
        character.FloorMaxAngle = Mathf.DegToRad(50f);
    }

    public override Node3D Node => Character;
    public override CollisionObject3D Collider => Character;
    public override int MoveOrder => 2;

    /// <summary>Floating in zero-g (cabin gravity off): no floor, no snapping.</summary>
    public bool Floating
    {
        get => Character.MotionMode == CharacterBody3D.MotionModeEnum.Floating;
        set => Character.MotionMode = value ? CharacterBody3D.MotionModeEnum.Floating : CharacterBody3D.MotionModeEnum.Grounded;
    }

    public override void Move(PhysicsHost host, float dt)
    {
        Body b = Body;
        if (b.Platform != null)
        {
            float turned = Riding.Carry(b);
            Character.GlobalPosition = b.Position.ToGodot();
            if (turned != 0f) Carried?.Invoke(turned);
        }
        if (b.Asleep) return;

        Vector3 before = b.Velocity.ToGodot();
        N.Vector3 worldBefore = b.WorldVelocity;
        bool wasOnFloor = b.OnFloor;
        Character.Velocity = before;
        Character.MoveAndSlide();
        b.Position = Character.GlobalPosition.ToNumerics();
        b.Velocity = Character.Velocity.ToNumerics();
        b.OnFloor = Character.IsOnFloor();

        // The hardest thing it ran into this move: landing, or being thrown into a wall.
        float hardest = 0f;
        Contact strongest = default;
        Body floorHull = null;
        for (int i = 0; i < Character.GetSlideCollisionCount(); i++)
        {
            KinematicCollision3D hit = Character.GetSlideCollision(i);
            GodotObject collider = hit.GetCollider();
            Body other = host.BodyOf(collider);
            N.Vector3 n = hit.GetNormal().ToNumerics(), point = hit.GetPosition().ToNumerics();
            if (other != null && (other.Kind is BodyKind.Vessel or BodyKind.Wreck) && n.Y > 0.7f) floorHull = other;
            N.Vector3 otherVelocity = other == null || b.Frame != null ? N.Vector3.Zero : other.PointVelocity(point);
            N.Vector3 mine = b.Frame != null ? before.ToNumerics() : worldBefore;
            float closing = -N.Vector3.Dot(mine - otherVelocity, n);
            if (closing <= hardest) continue;
            hardest = closing;
            strongest = new Contact(point, n, other, other != null ? Surface.Body : GodotQuery.SurfaceOf(collider));
        }
        if (hardest > host.World.Rules.Contacts.EventSpeed && (!wasOnFloor || strongest.Normal.Y < 0.7f))
            host.World.ResolveImpact(b, strongest.Other, strongest.Point, strongest.Normal, hardest, strongest.Surface);

        // Riding the outer hull: on it while standing on it, off it as soon as the feet leave it.
        if (b.Frame == null)
        {
            if (b.OnFloor && floorHull != null) Riding.StandOn(b, floorHull);
            else if (b.Platform != null && (!b.OnFloor || floorHull == null && Character.GetSlideCollisionCount() > 0)) Riding.StepOff(b);
            Riding.Remember(b);
        }
    }

    public override void Teleport()
    {
        Character.GlobalPosition = Body.Position.ToGodot();
        Character.Velocity = Body.Velocity.ToGodot();
        Character.ResetPhysicsInterpolation();
    }
}
