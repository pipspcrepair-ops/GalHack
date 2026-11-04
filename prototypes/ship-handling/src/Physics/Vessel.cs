using System.Collections.Generic;
using Godot;
using ShipHandling.Handling;

namespace ShipHandling.Physics;

/// <summary>
/// One vessel's body: a single kinematic CharacterBody3D whose shape is a handful of boxes
/// from the structure's solid blocks. It flies by the handling model's rules, slides and glances
/// off what it touches, takes damage only from real impacts, and stops simulating once landed.
///
/// Only <see cref="FlightPhysics"/> steps it, so every vessel moves in a fixed order.
/// </summary>
public partial class Vessel : CharacterBody3D
{
    public IVesselStructure Structure { get; private set; }
    public FlightPhysics World { get; private set; }
    public HandlingStats Stats { get; private set; }
    public FlightConditions Conditions { get; private set; }

    /// <summary>Motion between ticks: velocity, spin, orientation, boost and cruise.</summary>
    public FlightState State;

    /// <summary>The pilot's controls, set every tick by whoever flies it.</summary>
    public PilotInput Input = PilotInput.Autopilot;

    public HoverController Hover { get; private set; }
    public LandingGear Gear { get; private set; }

    public float Damage { get; private set; }
    public float HullPoints => ImpactRules.HullPoints(Structure, World.Tunables);

    /// <summary>Landed and settled: not simulated at all until thrust wakes it.</summary>
    public bool Frozen { get; private set; }
    public bool Settling => _settleLeft > 0f;

    /// <summary>Ticks this vessel actually simulated. Frozen ticks don't count.</summary>
    public long SimulatedTicks { get; private set; }

    /// <summary>Distance straight down to ground or water from the vessel's origin.</summary>
    public float GroundDistance { get; private set; } = float.PositiveInfinity;

    /// <summary>The last thing the console had to say ("too heavy to take off..."), and how long ago.</summary>
    public string ConsoleMessage { get; private set; }
    public float ConsoleMessageAge { get; private set; } = float.PositiveInfinity;

    /// <summary>Length along the keel, metres. For tests that care about "its own length".</summary>
    public float Length { get; private set; }

    public float Speed => State.Velocity.Length();
    public Vector3 WorldAngularVelocity => GlobalBasis * State.AngularVelocity;

    /// <summary>The vessel's velocity at a world point, spin included. Passengers stepping off need this.</summary>
    public Vector3 PointVelocity(Vector3 worldPoint) =>
        State.Velocity + WorldAngularVelocity.Cross(worldPoint - GlobalPosition);

    PhysicsDirectSpaceState3D _space;
    PhysicsRayQueryParameters3D _ray;
    int _statsRevision = -1;
    bool _statsInAir;
    float _idle, _settleLeft;
    bool _floorContact;
    readonly Dictionary<GodotObject, (Vector3 point, Vector3 normal)> _contacts = new();

    Tunables T => World.Tunables;

    internal void Setup(IVesselStructure structure, FlightPhysics world)
    {
        Structure = structure;
        World = world;
        MotionMode = MotionModeEnum.Floating;
        CollisionLayer = Layers.ShipHull;
        CollisionMask = Layers.Solid;
        SafeMargin = 0.02f;

        float front = float.MaxValue, back = float.MinValue;
        foreach (var block in structure.Blocks)
        {
            front = Mathf.Min(front, block.Center.Z - block.Size.Z / 2f);
            back = Mathf.Max(back, block.Center.Z + block.Size.Z / 2f);
            if (!block.Solid) continue;
            AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = block.Size },
                Position = block.Center,
                RotationDegrees = block.RotationDegrees,
            });
        }
        Length = back - front;

        if (structure.HoverEngines.Count > 0) Hover = new HoverController(this);
        if (structure.LandingGear.Count > 0) Gear = new LandingGear(this);

        _ray = new PhysicsRayQueryParameters3D { Exclude = new Godot.Collections.Array<Rid> { GetRid() } };
        State = FlightState.At(Basis.Identity);
        RefreshStats();
    }

    /// <summary>Puts the vessel somewhere, at rest, flying. Damage is kept.</summary>
    public void Place(Vector3 position, float yawDegrees)
    {
        var basis = new Basis(Vector3.Up, Mathf.DegToRad(yawDegrees));
        GlobalTransform = new Transform3D(basis, position);
        State = FlightState.At(basis);
        Frozen = false;
        _settleLeft = 0f;
        _idle = 0f;
        Hover?.Reset();
        Gear?.Reset(false);
        RefreshStats();
        ResetPhysicsInterpolation();
    }

    /// <summary>Puts the vessel down landed: gear out, settled, not simulating.</summary>
    public void PlaceLanded(Vector3 position, float yawDegrees)
    {
        Place(position, yawDegrees);
        Gear?.Reset(true);
        Frozen = true;
    }

    public void ResetDamage() => Damage = 0f;

    public void ToggleGear()
    {
        if (Gear == null) Say("No landing gear on a hover vessel");
        else Gear.Toggle();
    }

    public void ToggleCruise() => FlightModel.ToggleCruise(ref State);

    /// <summary>Knocked loose by something else (a ram): simulate again, without a lift-off.</summary>
    public void Wake()
    {
        Frozen = false;
        _settleLeft = 0f;
        _idle = 0f;
    }

    internal void Step(float dt)
    {
        _space = GetWorld3D().DirectSpaceState;
        RefreshStats();
        ConsoleMessageAge += dt;

        if ((Frozen || Settling) && !TryWake())
        {
            if (Settling) Settle(dt);
            return; // landed: no physics at all
        }

        SimulatedTicks++;
        GroundDistance = Ray(GlobalPosition, GlobalPosition + Vector3.Down * 5000f, Layers.Probe, out Vector3 ground, out _)
            ? GlobalPosition.Y - ground.Y : float.PositiveInfinity;

        FlightModel.Step(ref State, Input, Stats, Conditions, T, dt);
        Hover?.Step(dt);
        Gear?.Step(dt);
        Move(dt);
        CheckLanding(dt);
    }

    /// <summary>The four numbers depend on the build and on air versus space; refresh when either changes.</summary>
    void RefreshStats()
    {
        Conditions = World.Planet.At(GlobalPosition.Y);
        if (Stats != null && _statsRevision == Structure.Revision && _statsInAir == Conditions.InAtmosphere) return;
        Stats = HandlingModel.Compute(Structure, Conditions, T);
        _statsRevision = Structure.Revision;
        _statsInAir = Conditions.InAtmosphere;
    }

    // ---- Moving and contacts --------------------------------------------------------------

    /// <summary>
    /// Moves with collision, sliding along whatever it meets: low-speed contacts glance and slide
    /// instead of stopping the vessel dead. Each thing touched is resolved once per tick, with
    /// the velocity from before the move, so the impact speed is the real closing speed.
    /// </summary>
    void Move(float dt)
    {
        GlobalBasis = State.Orientation;
        Vector3 before = State.Velocity;
        Vector3 velocity = before;
        Vector3 motion = before * dt;
        _contacts.Clear();
        _floorContact = false;

        // Always move at least once, even standing still: that is what pushes a hull back out
        // if turning swung it into a rock.
        for (int i = 0; i < 4; i++)
        {
            KinematicCollision3D hit = MoveAndCollide(motion, false, SafeMargin);
            if (hit == null) break;
            Vector3 n = hit.GetNormal();
            GodotObject collider = hit.GetCollider();
            if (!_contacts.ContainsKey(collider)) _contacts[collider] = (hit.GetPosition(), n);
            if (collider is not Vessel && n.Y > 0.6f) _floorContact = true;
            if (velocity.Dot(n) < 0f) velocity = velocity.Slide(n);
            motion = hit.GetRemainder().Slide(n);
            if (motion.LengthSquared() < 1e-10f) break;
        }

        foreach (var (collider, (point, normal)) in _contacts)
        {
            if (collider is Vessel other) velocity = Ram(other, point, normal, before, velocity);
            else velocity = HitFixed(point, normal, before, velocity);
        }
        State.Velocity = velocity;
    }

    /// <summary>Something fixed: bounce a little, scrape only if it was a real impact.</summary>
    Vector3 HitFixed(Vector3 point, Vector3 normal, Vector3 before, Vector3 velocity)
    {
        float closing = -before.Dot(normal);
        if (closing <= 0f) return velocity;

        var c = T.Contacts;
        if (closing > 0.5f) velocity += normal * closing * c.Restitution; // glance off
        float damage = ImpactRules.Damage(Stats.Mass, closing, T);
        if (damage > 0f)
        {
            Vector3 along = velocity - normal * velocity.Dot(normal);
            velocity -= along * c.Scrape;
        }
        if (Hover != null) Hover.Kick(normal, closing);
        GroundImpact(point, normal, closing, damage);
        return velocity;
    }

    /// <summary>
    /// Another vessel: both take the same damage (from the reduced mass, so ramming always
    /// hurts the rammer too) and trade momentum along the contact normal.
    /// </summary>
    Vector3 Ram(Vessel other, Vector3 point, Vector3 normal, Vector3 before, Vector3 velocity)
    {
        float closing = -(before - other.State.Velocity).Dot(normal);
        if (closing <= 0f) return velocity;

        float m = Stats.Mass, mo = other.Stats.Mass;
        float mu = ImpactRules.ReducedMass(m, mo);
        float damage = ImpactRules.Damage(mu, closing, T);
        float impulse = (1f + T.Contacts.Restitution) * mu * closing;

        // The slide already took out this vessel's normal speed; put back the exchange instead.
        velocity += normal * (before.Dot(normal) + impulse / m);
        other.State.Velocity -= normal * (impulse / mo);
        if (other.Frozen || other.Settling) other.Wake();

        Damage += damage;
        other.Damage += damage;
        if (closing > 1f) World.RaiseImpact(new ImpactInfo(this, other, point, normal, closing, damage, 0f));
        return velocity;
    }

    /// <summary>A hit on the ground or a rock (hull or gear). Fast ground hits blast a capped crater.</summary>
    internal void GroundImpact(Vector3 point, Vector3 normal, float closing, float damage)
    {
        Damage += damage;
        float crater = normal.Y > 0.5f ? ImpactRules.CraterRadius(Stats.Mass, closing, T) : 0f;
        if (closing > 1f) World.RaiseImpact(new ImpactInfo(this, null, point, normal, closing, damage, crater));
    }

    // ---- Landing ----------------------------------------------------------------------------

    void CheckLanding(float dt)
    {
        bool idle = !Input.Seated || !Input.AnyThrust;
        _idle = idle ? _idle + dt : 0f;

        bool tooHeavy = !Stats.CanLiftOff && Conditions.Gravity > 0f;
        bool onGear = Gear != null && Gear.Deployed && Gear.Contact;
        bool onGround = onGear || (tooHeavy && _floorContact);
        bool slow = Speed < T.Gear.LandSpeed;
        if (onGround && slow && (tooHeavy || _idle >= T.Gear.IdleBeforeLanding))
        {
            _settleLeft = T.Gear.SettleTime;
            State.Cruise = false;
        }
    }

    /// <summary>Settles onto the gear: levels out (keeping heading), feet down, then stops simulating.</summary>
    void Settle(float dt)
    {
        State.Velocity = Vector3.Zero;
        State.AngularVelocity = Vector3.Zero;
        if (Gear != null && Gear.Deployed)
        {
            float k = 1f - Mathf.Exp(-10f * dt);
            Basis level = FlightModel.Frame(State.Orientation, true);
            State.Orientation = State.Orientation.Slerp(level, k).Orthonormalized();
            GlobalBasis = State.Orientation;
            Gear.Probe();
            if (float.IsFinite(Gear.MinClearance)) GlobalPosition += Vector3.Down * Gear.MinClearance * k;
        }

        _settleLeft -= dt;
        if (_settleLeft > 0f) return;
        _settleLeft = 0f;
        Frozen = true;
        World.RaiseLanded(this);
    }

    /// <summary>
    /// A landed vessel wakes when its pilot applies thrust, unless it is too heavy to fly: then
    /// the console says why and it stays put.
    /// </summary>
    bool TryWake()
    {
        if (!Input.Seated || !Input.AnyThrust) return false;
        if (!Stats.CanLiftOff && Conditions.Gravity > 0f)
        {
            if (ConsoleMessageAge > 2f) Say(Stats.LiftWarning);
            return false;
        }

        bool wasLanded = Frozen;
        Wake();
        if (wasLanded) World.RaiseLiftedOff(this);
        return true;
    }

    public void Say(string message)
    {
        ConsoleMessage = message;
        ConsoleMessageAge = 0f;
    }

    // ---- Probes -----------------------------------------------------------------------------

    /// <summary>A ray that never sees this vessel itself. Hover engines and gear feet probe with it.</summary>
    internal bool Ray(Vector3 from, Vector3 to, uint mask, out Vector3 point, out Vector3 normal)
    {
        _space ??= GetWorld3D().DirectSpaceState;
        _ray.From = from;
        _ray.To = to;
        _ray.CollisionMask = mask;
        var hit = _space.IntersectRay(_ray);
        if (hit.Count == 0)
        {
            point = to;
            normal = Vector3.Up;
            return false;
        }
        point = hit["position"].AsVector3();
        normal = hit["normal"].AsVector3();
        return true;
    }
}
