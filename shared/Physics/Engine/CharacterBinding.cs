using System.Collections.Generic;
using Godot;
using N = System.Numerics;

namespace GalHack.Physics.Engine;

/// <summary>
/// A walking body (players, creatures) on a CharacterBody3D, in whatever world it is in: the
/// zone's, or a structure's cabin world while it rides that structure (see <see cref="Riding"/>).
/// Its capsule and the controller's up follow the body's <see cref="Body.Up"/>. Landing and
/// slamming into things are impacts like any other. Engine platform velocity is off: a rider is
/// never carried by the engine, only by living in its structure's frame.
///
/// Anything up to a step tall (<see cref="PhysicsRules.WalkingData.StepHeight"/>) is stepped up
/// onto, and a ledge up to a step deep is stepped down without leaving the floor, whichever way
/// up is: on terrain, a cabin floor or an outer hull.
///
/// In mag boots (<see cref="MagBoots"/>) the up follows the face underfoot, and two probes carry
/// the body round the hull: an outside edge (walked off a face: look a little below it and back
/// for the next face) and, if switched on, an inside corner (walked into a wall taller than a step:
/// climb onto it). Both turn the body, its velocity and its walk frame by the same smallest
/// rotation, so walking forward stays forward; the camera eases its roll separately.
/// </summary>
public sealed class CharacterBinding : BodyBinding
{
    public readonly CharacterBody3D Character;

    /// <summary>The game sets this while the body rides a structure's outer hull and boots are on.</summary>
    public bool MagBoots;

    /// <summary>In the zone's world: the registered hull this move touched, if any, and whether it held the body up.</summary>
    public Body TouchedHull { get; private set; }
    public bool TouchedHullUnderfoot { get; private set; }

    /// <summary>The touched hull face's normal, in the world.</summary>
    public N.Vector3 TouchedHullNormal { get; private set; }

    /// <summary>Touching anything this move (in a cabin world, that is its structure).</summary>
    public bool Touching { get; private set; }

    /// <summary>How many times the boots have carried the body round an outside edge / onto a wall. For tests and the HUD.</summary>
    public int EdgesCrossed { get; private set; }
    public int WallsClimbed { get; private set; }

    /// <summary>How many times the body has stepped up onto something a step tall. For tests.</summary>
    public int StepsTaken { get; private set; }

    readonly PhysicsRayQueryParameters3D _ray = new();
    readonly PhysicsPointQueryParameters3D _point = new();
    List<N.Vector3> _touched = new(), _touching = new();

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

    /// <summary>Floating in zero-g: no floor, no snapping.</summary>
    public bool Floating
    {
        get => Character.MotionMode == CharacterBody3D.MotionModeEnum.Floating;
        set => Character.MotionMode = value ? CharacterBody3D.MotionModeEnum.Floating : CharacterBody3D.MotionModeEnum.Grounded;
    }

    float Radius => Character.GetChildCount() > 0 && Character.GetChild(0) is CollisionShape3D { Shape: CapsuleShape3D c } ? c.Radius : 0.35f;

    public override void Move(PhysicsHost host, float dt)
    {
        Body b = Body;
        TouchedHull = null;
        TouchedHullUnderfoot = false;
        Touching = false;
        if (b.Asleep) return;

        // Up: gravity's, unless mag boots hold the body to the face underfoot (kept by the probes).
        if (!MagBoots)
        {
            N.Vector3 g = host.World.GravityAt(b);
            if (g.LengthSquared() > 1e-6f)
            {
                N.Vector3 gravityUp = -N.Vector3.Normalize(g);
                if (N.Vector3.Dot(gravityUp, b.Up) < 0.99999f) Riding.SetUp(b, gravityUp);
            }
        }
        Align();

        bool wasOnFloor = b.OnFloor;
        if (MagBoots && wasOnFloor && b.Controller is not WalkController { Jumped: true })
        {
            // In mag boots a body on a face stays on it. As its up follows a curving face its
            // velocity picks up a little lift, which would stop the controller snapping to the
            // face beyond; a real push off (a bump, a blast) is far more and is kept.
            float lift = N.Vector3.Dot(b.Velocity, b.Up);
            if (lift > 0f && lift < 0.5f) b.Velocity -= b.Up * lift;
        }
        Vector3 before = b.Velocity.ToGodot();
        N.Vector3 worldBefore = b.WorldVelocity;
        float step = host.World.Rules.Walking.StepHeight;
        Character.FloorSnapLength = step + 0.05f; // steps down a ledge up to a step deep without leaving the floor
        Character.Velocity = before;
        Character.MoveAndSlide();
        b.Position = Character.GlobalPosition.ToNumerics();
        b.Velocity = Character.Velocity.ToNumerics();
        b.OnFloor = Character.IsOnFloor();
        if (b.OnFloor || Floating) _screeFor = 0f;
        else Scree(host.World.Rules.Walking.SlideSpeed, dt);
        N.Vector3 up = b.Up;
        bool stepped = (wasOnFloor || b.OnFloor) && !Floating && b.Controller is not WalkController { Jumped: true }
                       && StepUp(step, before.ToNumerics(), dt);

        // The hardest thing it ran into this move: landing, or being thrown into a wall. A new
        // contact counts its full closing speed. A surface it was already touching last move
        // counts only as far as this move really stopped it: the character controller can leave
        // a velocity pressing into a steep slope tick after tick, and that is leaning, not hitting.
        N.Vector3 change = Character.Velocity.ToNumerics() - before.ToNumerics();
        float hardest = 0f;
        Contact strongest = default;
        _touching.Clear();
        int count = Character.GetSlideCollisionCount();
        for (int i = 0; i < count; i++)
        {
            KinematicCollision3D hit = Character.GetSlideCollision(i);
            GodotObject collider = hit.GetCollider();
            Body other = host.BodyOf(collider);
            N.Vector3 n = hit.GetNormal().ToNumerics(), point = hit.GetPosition().ToNumerics();
            _touching.Add(n);
            if (other != null && (other.Kind is BodyKind.Vessel or BodyKind.Wreck))
            {
                bool underfoot = N.Vector3.Dot(n, up) > 0.7f;
                if (TouchedHull == null || underfoot && !TouchedHullUnderfoot)
                {
                    TouchedHull = other;
                    TouchedHullUnderfoot = underfoot;
                    TouchedHullNormal = n;
                }
            }
            if (stepped && Steps.Blocks(Character, n.ToGodot(), up.ToGodot())) continue; // the step's riser or edge: stepped up, not hit
            N.Vector3 otherVelocity = other == null || b.Frame != null ? N.Vector3.Zero : other.PointVelocity(point);
            N.Vector3 mine = b.Frame != null ? before.ToNumerics() : worldBefore;
            float closing = -N.Vector3.Dot(mine - otherVelocity, n);
            if (WasTouching(n)) closing = System.MathF.Min(closing, N.Vector3.Dot(change, n));
            if (closing <= hardest) continue;
            hardest = closing;
            strongest = new Contact(point, n, other, other != null ? Surface.Body : GodotQuery.SurfaceOf(collider));
        }
        (_touched, _touching) = (_touching, _touched);
        Touching = count > 0 || b.OnFloor;
        if (hardest > host.World.Rules.Contacts.EventSpeed && (!wasOnFloor || N.Vector3.Dot(strongest.Normal, up) < 0.7f))
            host.World.ResolveImpact(b, strongest.Other, strongest.Point, strongest.Normal, hardest, strongest.Surface);

        if (MagBoots) Boots(host, before.ToNumerics(), dt, stepped);
    }

    /// <summary>
    /// Walked into something no taller than a step: up by the step height (or the headroom there
    /// is), on by this move's walk, and down onto it, if all three are clear and it lands on a
    /// floor. Anything taller stays a wall (in mag boots, one the boots may climb). The same on
    /// terrain, a cabin floor or an outer hull, whichever way up is.
    /// </summary>
    bool StepUp(float height, N.Vector3 wanted, float dt)
    {
        Body b = Body;
        if (!Steps.Up(Character, b.Up.ToGodot(), wanted.ToGodot(), height, Radius, dt)) return false;
        b.Position = Character.GlobalPosition.ToNumerics();
        b.Velocity = Character.Velocity.ToNumerics();
        b.OnFloor = true;
        StepsTaken++;
        return true;
    }

    /// <summary>
    /// On ground too steep to stand on the body slides, as on scree: downhill along the slope it goes no faster than
    /// <paramref name="most"/> m/s, so a long hillside is slid down, not fallen down. A slide bounces off rough ground, and
    /// a walker holding forward is carried off it in the air: for a while after a touch the slope counts while it is still
    /// just under the feet. Never on ground it can stand on, a wall or an overhang, and never uphill.
    /// </summary>
    void Scree(float most, float dt)
    {
        Body b = Body;
        float floor = Mathf.Cos(Character.FloorMaxAngle);
        bool Steep(N.Vector3 n) => N.Vector3.Dot(n, b.Up) >= 0.2f && N.Vector3.Dot(n, b.Up) < floor;
        bool near = false;
        for (int i = 0; i < Character.GetSlideCollisionCount() && !near; i++)
        {
            N.Vector3 n = Character.GetSlideCollision(i).GetNormal().ToNumerics();
            if (!Steep(n)) continue;
            _scree = n;
            _screeFor = ScreeHold;
            near = true;
        }
        if (_screeFor <= 0f) return;
        _screeFor -= dt;
        if (!near)
        {
            if (!Ray(b.Position + b.Up * 0.1f, b.Position - b.Up * ScreeReach, out _, out N.Vector3 under) || !Steep(under)) return;
            _scree = under;
        }
        N.Vector3 v = b.Velocity, across = _scree * N.Vector3.Dot(v, _scree), along = v - across;
        if (N.Vector3.Dot(along, b.Up) >= 0f || along.LengthSquared() <= most * most) return;
        b.Velocity = across + N.Vector3.Normalize(along) * most;
        Character.Velocity = b.Velocity.ToGodot();
    }

    /// <summary>How long after touching a slope it still counts when just underfoot, s, and how far under, m.</summary>
    const float ScreeHold = 1.5f, ScreeReach = 1.5f;
    N.Vector3 _scree;
    float _screeFor;

    /// <summary>Turns the capsule (and the controller's idea of up) to the body's up.</summary>
    void Align()
    {
        Vector3 up = Body.Up.ToGodot();
        Character.UpDirection = up;
        Character.GlobalBasis = Riding.FromTo(N.Vector3.UnitY, Body.Up).ToBasis();
    }

    // ---- Mag boots ---------------------------------------------------------------------------

    void Boots(PhysicsHost host, N.Vector3 before, float dt, bool stepped)
    {
        Body b = Body;
        PhysicsRules.RidingData rules = host.World.Rules.Riding;
        bool jumped = b.Controller is WalkController { Jumped: true };
        if (jumped) return; // a deliberate jump is never held back

        if (b.OnFloor)
        {
            // Walked into a wall too tall to be a step: climb onto it. Otherwise follow the face
            // underfoot, eased so a seam between blocks never jolts.
            if (!stepped && rules.InsideCorners && InsideCorner(host.World.Rules.Walking.StepHeight, before)) WallsClimbed++;
            else
            {
                // The velocity turns with the up, so it stays along the face and the feet stay on it.
                N.Vector3 n = Character.GetFloorNormal().ToNumerics();
                float k = 1f - System.MathF.Exp(-rules.UpFollow * dt);
                N.Vector3 up = MathX.Slerp(b.Up, n, k);
                b.Velocity = MathX.Rotate(Riding.FromTo(b.Up, up), b.Velocity);
                Riding.SetUp(b, up);
            }
        }
        else if (OutsideEdge(rules)) EdgesCrossed++;
        Align();
    }

    /// <summary>
    /// Walked off a face: look just below it and back the way the body came for the next face
    /// round the edge. Found: turn onto it, feet on it, still walking forward. Only past a real
    /// edge: with anything still underfoot it is a slope or a seam, and the probe must start in
    /// open air that the feet can see, so it never finds a face inside the hull.
    /// </summary>
    bool OutsideEdge(PhysicsRules.RidingData rules)
    {
        Body b = Body;
        N.Vector3 up = b.Up, v = b.Velocity, feet = b.Position;
        N.Vector3 along = v - up * N.Vector3.Dot(v, up);
        if (along.LengthSquared() < 0.04f) along = MathX.Forward(b.Rotation); // drifting off slowly: look ahead
        along = N.Vector3.Normalize(along - up * N.Vector3.Dot(along, up));
        N.Vector3 above = feet + up * 0.05f;
        if (Ray(above, feet - up * rules.EdgeProbe, out _, out _)) return false;
        N.Vector3 from = feet - up * rules.EdgeProbe + along * 0.05f;
        if (Solid(from) || Ray(above, from, out _, out _)) return false;
        if (!Ray(from, from - along * 1.2f, out N.Vector3 point, out N.Vector3 normal)) return false;
        if (N.Vector3.Dot(normal, up) > 0.5f || N.Vector3.Dot(normal, along) < 0.5f) return false;
        Carry(normal, point + normal * 0.03f);
        return true;
    }

    /// <summary>
    /// Walked into a wall rising from the face: if it is taller than a step, climb onto it (the
    /// wall becomes the floor). Anything lower has already been stepped onto.
    /// </summary>
    bool InsideCorner(float stepHeight, N.Vector3 before)
    {
        Body b = Body;
        N.Vector3 up = b.Up;
        for (int i = 0; i < Character.GetSlideCollisionCount(); i++)
        {
            N.Vector3 n = Character.GetSlideCollision(i).GetNormal().ToNumerics();
            if (N.Vector3.Dot(n, up) > 0.3f) continue; // a floor or a slope, not a wall
            N.Vector3 into = before - up * N.Vector3.Dot(before, up);
            if (N.Vector3.Dot(into, -n) < 0.5f) continue; // not walking into it
            N.Vector3 high = b.Position + up * stepHeight;
            if (!Ray(high, high - n * (Radius + 0.35f), out _, out N.Vector3 wall) || N.Vector3.Dot(wall, n) < 0.9f) continue;
            // Feet onto the wall, lifted off the old face so the turned capsule clears it.
            Carry(n, b.Position - n * Radius + up * (Radius + 0.03f));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Turns body, velocity and walk frame from the old up to a face's normal, and puts the feet
    /// on it: just clear of it, then snapped down onto it, so it is standing there at once.
    /// </summary>
    void Carry(N.Vector3 normal, N.Vector3 feet)
    {
        Body b = Body;
        N.Quaternion turn = Riding.FromTo(b.Up, normal);
        N.Vector3 v = MathX.Rotate(turn, b.Velocity);
        b.Velocity = v - normal * N.Vector3.Dot(v, normal); // along the new face
        Riding.SetUp(b, normal);
        Align();
        Character.GlobalPosition = feet.ToGodot();
        Character.Velocity = b.Velocity.ToGodot();
        Character.ApplyFloorSnap();
        b.Position = Character.GlobalPosition.ToNumerics();
        b.OnFloor = true;
        Touching = true;
    }

    bool Ray(N.Vector3 from, N.Vector3 to, out N.Vector3 point, out N.Vector3 normal)
    {
        point = normal = default;
        PhysicsDirectSpaceState3D space = Character.GetWorld3D()?.DirectSpaceState;
        if (space == null) return false;
        _ray.From = from.ToGodot();
        _ray.To = to.ToGodot();
        _ray.CollisionMask = Character.CollisionMask;
        _ray.Exclude = new Godot.Collections.Array<Rid> { Character.GetRid() };
        Godot.Collections.Dictionary hit = space.IntersectRay(_ray);
        if (hit.Count == 0) return false;
        point = hit["position"].AsVector3().ToNumerics();
        normal = hit["normal"].AsVector3().ToNumerics();
        return true;
    }

    bool Solid(N.Vector3 p)
    {
        PhysicsDirectSpaceState3D space = Character.GetWorld3D()?.DirectSpaceState;
        if (space == null) return false;
        _point.Position = p.ToGodot();
        _point.CollisionMask = Character.CollisionMask;
        _point.Exclude = new Godot.Collections.Array<Rid> { Character.GetRid() };
        return space.IntersectPoint(_point, 1).Count > 0;
    }

    bool WasTouching(N.Vector3 n)
    {
        foreach (N.Vector3 m in _touched)
            if (N.Vector3.Dot(m, n) > 0.95f) return true;
        return false;
    }

    public override void Teleport()
    {
        Align();
        Character.GlobalPosition = Body.Position.ToGodot();
        Character.Velocity = Body.Velocity.ToGodot();
        Character.ResetPhysicsInterpolation();
        _touched.Clear();
    }
}
