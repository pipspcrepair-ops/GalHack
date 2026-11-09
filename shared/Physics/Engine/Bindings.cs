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

    /// <summary>
    /// The body was carried this tick (a landed vessel riding its host): put the node there,
    /// smoothly, without resetting its interpolation.
    /// </summary>
    public virtual void Follow() => Node.GlobalTransform = GodotMath.TransformOf(Body);

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

    /// <summary>Its rotation after the last tick's move.</summary>
    N.Quaternion _turned;
    int _turns;

    /// <summary>The transform last checked for overlap while asleep.</summary>
    Transform3D _cleared;

    /// <summary>How deep an overlap is left alone, m (resting contact).</summary>
    public const float Slop = 0.005f;

    public override void Move(PhysicsHost host, float dt)
    {
        Body b = Body;
        Hull.GlobalTransform = GodotMath.TransformOf(b); // turning, gear lifts and settling happened in the rules
        if (b.Asleep)
        {
            // At rest it isn't swept, but what set it where it stands (settling onto its legs, a placement, a toss
            // lying as it landed) turned or put it there without a sweep: it is pushed clear, still asleep.
            if (Hull.GlobalTransform != _cleared && Clear()) b.Position = Hull.GlobalPosition.ToNumerics();
            _cleared = Hull.GlobalTransform;
            return;
        }

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
        // The sweep stops it at what it meets, but turning (the flight model's, the hover's tilt to the ground, a
        // bump's kick) happened before it, unswept, and can swing a corner into the ground: nothing is left inside.
        // (Every fourth tick when it has only turned: a tick's turn swings a corner in by millimetres, and the sweep stops it going further.)
        if (_contacts.Count > 0 || b.Rotation != _turned && ++_turns % 4 == 0) Clear();
        _turned = b.Rotation;

        b.Position = Hull.GlobalPosition.ToNumerics();
        b.Velocity = host.World.ResolveContacts(b, _contacts, before.ToNumerics(), velocity.ToNumerics());
    }

    public override void Teleport()
    {
        Hull.GlobalTransform = GodotMath.TransformOf(Body);
        Hull.ResetPhysicsInterpolation();
    }

    /// <summary>
    /// Pushes the hull out of anything solid it overlaps (the ground's collision, a place's blocks, another hull)
    /// along the deepest contact, a pass at a time, until nothing is deeper than <see cref="Slop"/>. A contact whose
    /// normal points down (a hull sunk past a face of the ground, which the engine would push deeper still) lifts it
    /// instead. True if it moved.
    /// </summary>
    public bool Clear()
    {
        PhysicsDirectSpaceState3D space = Hull.GetWorld3D()?.DirectSpaceState;
        if (space == null) return false;
        // Nothing near at all (in the air): one query of its bounds, and done.
        if (Hull.GetChildCount() != _childCount)
        {
            // Its shapes, and their bounds in its frame, gathered again only when they change (an edit swaps chunks).
            _childCount = Hull.GetChildCount();
            _shapes.Clear();
            bool first = true;
            foreach (Node n in Hull.GetChildren())
                if (n is CollisionShape3D cs && cs.Shape != null)
                {
                    _shapes.Add(cs);
                    Aabb box = cs.Transform * BoundsOf(cs.Shape);
                    _local = first ? box : _local.Merge(box);
                    first = false;
                }
        }
        if (_shapes.Count == 0) return false;
        Aabb bounds = _local;
        _near ??= new PhysicsShapeQueryParameters3D { Shape = new BoxShape3D(), CollisionMask = Hull.CollisionMask, Margin = 0f };
        ((BoxShape3D)_near.Shape).Size = bounds.Size + Vector3.One * 0.02f;
        _near.Transform = Hull.GlobalTransform * new Transform3D(Basis.Identity, bounds.GetCenter());
        _near.Exclude = _self ??= new Godot.Collections.Array<Rid> { Hull.GetRid() };
        if (space.IntersectShape(_near, 1).Count == 0) return false;

        bool moved = false;
        for (int pass = 0; pass < 8; pass++)
        {
            // The deepest overlap of any of its shapes: the way out of it is from the point on the hull to the point on what it overlaps.
            Vector3 push = Vector3.Zero, up = Vector3.Zero;
            float deepest = Slop, deepestUp = Slop;
            foreach (CollisionShape3D cs in _shapes)
            {
                if (!GodotObject.IsInstanceValid(cs) || cs.GetParent() != Hull)
                {
                    _childCount = -1; // swapped by an edit: gathered again next time
                    continue;
                }
                if (cs.Disabled || cs.Shape == null) continue;
                _shape ??= new PhysicsShapeQueryParameters3D { CollisionMask = Hull.CollisionMask, Margin = 0f };
                _shape.Shape = cs.Shape;
                _shape.Transform = cs.GlobalTransform;
                _shape.Exclude = _self;
                Godot.Collections.Array<Vector3> points = space.CollideShape(_shape, 8);
                for (int i = 0; i + 1 < points.Count; i += 2)
                {
                    Vector3 way = points[i + 1] - points[i];
                    float depth = way.Length();
                    if (way.Y > 0.7f * depth && depth > deepestUp) { deepestUp = depth; up = way; }
                    if (depth <= deepest) continue;
                    deepest = depth;
                    push = way;
                }
            }
            // Out the top first: sunk into a floor or the ground it comes up onto it, not out through the side of a
            // block (a mesh of blocks offers a short way out sideways at every seam); a wall it then still meets, next pass.
            if (up != Vector3.Zero) push = up;
            if (push == Vector3.Zero) break;
            // Pointing down while it is the hull's underside that is in (sunk past a face of the ground, where the
            // shortest way out is through it): out the top instead.
            if (push.Y < -0.2f * push.Length()) push = Vector3.Up * System.MathF.Min(push.Length(), 0.5f);
            Hull.GlobalPosition += push * 1.02f;
            LastPush = push;
            Pushes++;
            moved = true;
        }
        return moved;
    }

    /// <summary>The last push out of an overlap, and how many there have been (for tests).</summary>
    public Vector3 LastPush;
    public int Pushes;

    PhysicsShapeQueryParameters3D _near, _shape;
    readonly List<CollisionShape3D> _shapes = new();
    int _childCount = -1;
    Aabb _local;
    Godot.Collections.Array<Rid> _self;
    readonly Dictionary<Shape3D, Aabb> _bounds = new();

    /// <summary>A hull shape's own bounds (boxes and convex hulls: what a structure's colliders are).</summary>
    Aabb BoundsOf(Shape3D shape)
    {
        if (_bounds.TryGetValue(shape, out Aabb a)) return a;
        if (shape is BoxShape3D box) a = new Aabb(-box.Size * 0.5f, box.Size);
        else if (shape is ConvexPolygonShape3D hull && hull.Points.Length > 0)
        {
            a = new Aabb(hull.Points[0], Vector3.Zero);
            foreach (Vector3 p in hull.Points) a = a.Expand(p);
        }
        else a = shape.GetDebugMesh().GetAabb();
        if (_bounds.Count > 4096) _bounds.Clear(); // shapes swapped by edits: never grows without end
        _bounds[shape] = a;
        return a;
    }
}

/// <summary>
/// A base: one static body on the hull layer, holding the structure's per-chunk colliders, that
/// nothing ever moves (<see cref="Motion.Fixed"/>). Vessels land and park on it, players walk on it
/// in the zone's own world, and a hit on it is a hit on something fixed to the planet.
/// </summary>
public sealed class FixedBinding : BodyBinding
{
    public readonly StaticBody3D Static;

    public FixedBinding(StaticBody3D body)
    {
        Static = body;
        body.CollisionLayer = Layers.Hull;
        body.CollisionMask = 0;
    }

    public override Node3D Node => Static;
    public override CollisionObject3D Collider => Static;

    public override void Teleport()
    {
        Static.GlobalTransform = GodotMath.TransformOf(Body);
        Static.ResetPhysicsInterpolation();
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
        // A crate thrown at the ground at 40 m/s moves most of its own size in one tick: sweep it,
        // so it can never tunnel through the terrain's thin shell.
        rigid.ContinuousCd = true;
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
        Vector3 before = Rigid.LinearVelocity; // read first: an impulse may change it at once
        Vector3 force = b.PendingTotalForce.ToGodot(), impulse = b.PendingTotalImpulse.ToGodot();
        if (force != Vector3.Zero) Rigid.ApplyCentralForce(force);
        if (impulse != Vector3.Zero) Rigid.ApplyCentralImpulse(impulse);
        if (b.PendingSpinImpulse != N.Vector3.Zero) Rigid.ApplyTorqueImpulse(b.PendingSpinImpulse.ToGodot());
        _expected = before + force / b.Mass * dt + impulse / b.Mass;
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

