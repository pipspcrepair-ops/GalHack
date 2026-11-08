using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>Where a vessel is flying: the planet's gravity (for lift margin) and whether it is in air.</summary>
public readonly record struct FlightZone(float PlanetGravity, bool InAtmosphere)
{
    /// <summary>What actually pulls: nothing above the atmosphere ceiling.</summary>
    public float Gravity => InAtmosphere ? PlanetGravity : 0f;
}

/// <summary>The few flight numbers the physics rules need from the handling model.</summary>
public readonly record struct VesselStats(bool IsHover, bool CanLiftOff, float RideHeight, string LiftWarning);

/// <summary>A vessel's motion going into and coming out of one tick of the flight model.</summary>
public struct FlightMotion
{
    /// <summary>World velocity of the vessel's origin, m/s.</summary>
    public Vector3 Velocity;
    public Quaternion Rotation;

    /// <summary>Ship-local spin, rad/s: x pitch, y yaw, z roll.</summary>
    public Vector3 LocalSpin;

    /// <summary>
    /// Landed and left there, until a pilot lifts off. A parked vessel that something knocks
    /// loose brakes back to a stop instead of the autopilot holding course: it must never sail
    /// away by itself.
    /// </summary>
    public bool Parked;
}

/// <summary>
/// The flight rules, as the physics module sees them: implemented by whoever owns flight (in
/// the sandbox, a bridge to ship-handling's handling model). The rules say what velocity and
/// orientation the pilot's controls ask for this tick; physics turns the difference into a
/// thrust force on the one force path, so wind, gravity and thrust all add up the same way.
/// </summary>
public interface IFlightRules
{
    /// <summary>The numbers for this build here (air or space). Called every tick; cache inside.</summary>
    VesselStats Stats(Body body, FlightZone zone);

    /// <summary>One tick of the flight model: thrust, assist, boost, turning.</summary>
    void Step(Body body, ref FlightMotion motion, FlightZone zone, float dt);

    /// <summary>Someone is at the controls (false: the autopilot holds course).</summary>
    bool Seated { get; }

    /// <summary>The pilot is asking for thrust: wakes a landed vessel.</summary>
    bool AnyThrust { get; }

    /// <summary>Hover Mode: hop this tick.</summary>
    bool Jump { get; }

    /// <summary>The vessel has landed: drop cruise and boost.</summary>
    void Halt();
}

/// <summary>
/// One vessel's body rules (ported from ship-handling's Vessel): it flies by the flight rules,
/// rides on hover engines or lands on gear, and stops simulating once landed (zero cost) until
/// thrust or a knock wakes it. A vessel with no hull points left loses power and becomes a free
/// wreck the engine tumbles. The world resolves its contacts with the same rules as everything.
/// </summary>
public sealed class VesselController : IBodyController, IContactListener, IForceReactor
{
    /// <summary>A landed vessel wakes when weather pushes it harder than this share of its weight (a tornado, not a breeze).</summary>
    public const float WeatherWakeShare = 0.3f;

    public IFlightRules Flight { get; }
    public HoverController Hover { get; }
    public LandingGear Gear { get; }

    public VesselStats Stats { get; private set; }
    public FlightZone Zone { get; private set; }

    /// <summary>Ship-local spin, rad/s: x pitch, y yaw, z roll.</summary>
    public Vector3 LocalSpin;

    /// <summary>Landed and settled: not simulated at all until thrust wakes it.</summary>
    public bool Frozen { get; private set; }

    /// <summary>Landed and not flown off since (see <see cref="FlightMotion.Parked"/>).</summary>
    public bool Parked { get; private set; }
    public bool Settling => _settleLeft > 0f;

    /// <summary>Ticks this vessel actually simulated. Frozen ticks don't count.</summary>
    public long SimulatedTicks { get; private set; }

    /// <summary>Distance straight down to ground or water from the vessel's origin.</summary>
    public float GroundDistance { get; private set; } = float.PositiveInfinity;

    /// <summary>The last thing the console had to say ("too heavy to take off..."), and how long ago.</summary>
    public string ConsoleMessage { get; private set; }
    public float ConsoleMessageAge { get; private set; } = float.PositiveInfinity;

    float _idle, _settleLeft;

    // Placed down landed (or docked) but not yet looked under: the first tick checks whether its
    // feet are on a host, and parks it there.
    bool _lookForHost;

    public VesselController(IFlightRules flight, Vector3[] hoverEngines, GearLeg[] gear)
    {
        Flight = flight;
        if (hoverEngines is { Length: > 0 }) Hover = new HoverController(hoverEngines);
        if (gear is { Length: > 0 }) Gear = new LandingGear(gear);
    }

    // ---- Placing ----------------------------------------------------------------------

    /// <summary>Puts the vessel somewhere, at rest, flying. Damage is kept.</summary>
    public void Place(Body body, Vector3 position, float headingRadians)
    {
        body.Position = position;
        body.Rotation = MathX.Yaw(headingRadians);
        body.Velocity = Vector3.Zero;
        body.AngularVelocity = Vector3.Zero;
        LocalSpin = Vector3.Zero;
        Frozen = false;
        body.Asleep = false;
        _settleLeft = 0f;
        _idle = 0f;
        _lookForHost = false;
        Parked = false;
        Hover?.Reset();
        Gear?.Reset(false);
    }

    /// <summary>
    /// Puts the vessel down landed: gear out, settled, not simulating. Put down on a base or a
    /// capital ship's deck, it parks there on its first tick.
    /// </summary>
    public void PlaceLanded(Body body, Vector3 position, float headingRadians)
    {
        Place(body, position, headingRadians);
        Gear?.Reset(true);
        Frozen = true;
        Parked = true;
        body.Asleep = true;
        _lookForHost = true;
    }

    /// <summary>
    /// Docks the vessel to a host (a base or a capital ship) where it is now: it stops simulating,
    /// parked on the host, riding it and out of the wind, until its pilot applies thrust.
    /// </summary>
    public void Dock(PhysicsWorld world, Body body, Body host)
    {
        body.Velocity = host.PointVelocity(body.Position);
        body.AngularVelocity = host.AngularVelocity;
        LocalSpin = Vector3.Zero;
        _settleLeft = 0f;
        _idle = 0f;
        Frozen = true;
        Parked = true;
        body.Asleep = true;
        Flight.Halt();
        world.Park(body, host);
    }

    /// <summary>Knocked loose by something (a ram, the ground dug out from under it): simulate again, without a lift-off.</summary>
    public void Wake(Body body)
    {
        Frozen = false;
        body.Asleep = false;
        _settleLeft = 0f;
        _idle = 0f;
    }

    public void ToggleGear()
    {
        if (Gear == null) Say(Stats.IsHover ? "No landing gear in Hover Mode" : "No landing gear fitted");
        else Gear.Toggle();
    }

    public void Say(string message)
    {
        ConsoleMessage = message;
        ConsoleMessageAge = 0f;
    }

    // ---- Each tick --------------------------------------------------------------------

    public void Drive(PhysicsWorld world, Body body, float dt)
    {
        Zone = new FlightZone(world.Zone.Strength, world.Zone.InAtmosphere(body.Position));
        Stats = Flight.Stats(body, Zone);
        ConsoleMessageAge += dt;

        if ((Frozen || Settling) && !TryWake(world, body))
        {
            if (Settling) Settle(world, body, dt);
            if (_lookForHost)
            {
                _lookForHost = false;
                Gear?.Probe(world, body);
                if (PhysicsWorld.IsHost(Gear?.StandingOn)) world.Park(body, Gear.StandingOn);
            }
            body.Asleep = true;
            return; // landed: no physics at all
        }
        body.Asleep = false;

        if (body.Broken)
        {
            world.MakeWreck(body); // no hull points left: no power, no assist, no autopilot
            return;
        }

        SimulatedTicks++;
        GroundDistance = world.Query.Ray(body.Position, body.Position - Vector3.UnitY * 5000f, Layers.ProbeMask, body, out RayHit ground)
            ? body.Position.Y - ground.Point.Y : float.PositiveInfinity;

        if (body.Tossed > 0f)
        {
            // Thrown by a tornado: no thrust, no assist, no hover cushion. It tumbles as it was thrown
            // and falls under gravity and the wind until it has come down, and its landing hurts by the
            // closing speed like any other hit (face-on hard, glancing little).
            body.Tossed = MathF.Max(0f, body.Tossed - dt);
            StrikeUnstreamedGround(world, body, dt);
            Quaternion spun = Quaternion.Normalize(body.Rotation * MathX.AxisAngle(Vector3.UnitY, LocalSpin.Y * dt)
                * MathX.AxisAngle(Vector3.UnitX, LocalSpin.X * dt) * MathX.AxisAngle(Vector3.UnitZ, LocalSpin.Z * dt));
            body.AngularVelocity = AngularVelocity(body.Rotation, spun, dt);
            body.Rotation = spun;
            Flight.Halt();
            return;
        }

        Vector3 v0 = body.Velocity;
        Quaternion before = body.Rotation;
        var motion = new FlightMotion { Velocity = v0, Rotation = before, LocalSpin = LocalSpin, Parked = Parked };
        Flight.Step(body, ref motion, Zone, dt);
        LocalSpin = motion.LocalSpin;
        Vector3 v = motion.Velocity;
        Quaternion rotation = motion.Rotation;

        if (Hover != null)
        {
            bool canHover = Stats.CanLiftOff || Zone.Gravity <= 0f;
            Hover.Step(world, body, ref v, ref rotation, Stats.RideHeight, canHover, Zone.Gravity, Flight.Seated && Flight.Jump, dt);
        }
        else if (Stats.IsHover) v.Y -= Zone.Gravity * dt; // Hover Mode with no plates fitted: nothing holds it up
        body.Rotation = rotation;
        body.AngularVelocity = AngularVelocity(before, rotation, dt);
        Gear?.Step(world, body, ref v, dt);

        // The flight rules said what velocity they want; the engines make the force for it. The
        // world adds gravity, water and weather on top, so the thrust here cancels the weight it
        // holds up. In water a vessel that can hold itself up holds only what the water doesn't
        // (its buoyancy carries the rest), so it flies underwater as in air, slowed by the drag; one
        // that can't (no power, too heavy) has its engines flat out already, and sinks or floats by
        // its density.
        Vector3 gravity = world.GravityAt(body);
        bool holding = Stats.CanLiftOff || Zone.Gravity <= 0f;
        Vector3 buoyed = holding ? world.BuoyancyOn(body) : Vector3.Zero;
        world.AddForce(body, body.Mass * ((v - v0) / dt - gravity) - buoyed, ForceKind.Thrust);
    }

    public void AfterMove(PhysicsWorld world, Body body, float dt)
    {
        if (body.Asleep || body.Motion != Motion.Kinematic) return;
        CheckLanding(world, body, dt);

        // Resting on a base or a capital ship (on its gear, or on its belly), with nobody pushing
        // the controls: parked there, out of the wind. Under thrust, or off it, it isn't.
        Body host = Flight.Seated && Flight.AnyThrust ? null : RestingOn(body);
        if (PhysicsWorld.IsHost(host)) world.Park(body, host);
        else world.Unpark(body);
    }

    /// <summary>The registered body the vessel rests on: under its feet, or under its hull.</summary>
    Body RestingOn(Body body) => Gear?.StandingOn ?? body.Underfoot;

    /// <summary>
    /// A tossed hull far from anyone falls toward ground whose collision isn't streamed there (it is
    /// only round the player): where its keel would go into solid ground this tick, it strikes the
    /// ground there instead, by the one impact rule, and comes to rest on it.
    /// </summary>
    static void StrikeUnstreamedGround(PhysicsWorld world, Body body, float dt)
    {
        Vector3 v = body.Velocity;
        if (v.Y >= 0f) return;
        float below = body.Shape.FeetBelowOrigin;
        Vector3 com = body.CenterOfMassPosition;
        Vector3 keel = new Vector3(com.X, body.Position.Y - below, com.Z) + v * dt; // under its middle, at its lowest
        Vector3 shift = keel - (body.Position + v * dt - Vector3.UnitY * below);
        if (!SolidAt(world, body, keel)) return;
        if (world.Query.Ray(body.Position, keel - Vector3.UnitY, Layers.ProbeMask, body, out _)) return; // streamed: the hull's own sweep meets it
        float lift = 0f;
        while (lift < 30f && SolidAt(world, body, keel + Vector3.UnitY * lift)) lift += 0.25f;
        Vector3 point = keel + Vector3.UnitY * lift;
        world.ResolveImpact(body, null, point, Vector3.UnitY, -v.Y, Surface.Ground);
        body.Position = point - shift + Vector3.UnitY * below;
        body.Velocity = new Vector3(v.X * 0.3f, 0f, v.Z * 0.3f);
        body.Tossed = 0f;
    }

    static bool SolidAt(PhysicsWorld world, Body body, Vector3 p)
    {
        foreach (IStuckField f in world.StuckFields) if (f.IsSolid(body, p)) return true;
        return false;
    }

    /// <summary>Nothing is silently immune to weather: a landed ship a tornado pulls hard enough is knocked loose.</summary>
    public void React(PhysicsWorld world, Body body, float dt)
    {
        if (!Frozen && !Settling) return;
        float weight = body.Mass * world.Zone.Strength;
        if (body.PendingForceFrom(ForceKind.Weather).Length() > WeatherWakeShare * weight) Wake(body);
    }

    public void OnContact(PhysicsWorld world, Body body, in Contact contact, float closingSpeed)
    {
        if (Hover != null && contact.Other == null && closingSpeed > 0f)
            body.Rotation = HoverController.Kick(body.Rotation, contact.Normal, closingSpeed);
    }

    /// <summary>The world-frame spin that turns one orientation into the next over a tick.</summary>
    static Vector3 AngularVelocity(Quaternion from, Quaternion to, float dt)
    {
        Quaternion delta = Quaternion.Normalize(to * Quaternion.Conjugate(from));
        if (delta.W < 0f) delta = -delta;
        float sinHalf = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
        if (sinHalf < 1e-8f) return Vector3.Zero;
        float angle = 2f * MathF.Atan2(sinHalf, delta.W);
        return new Vector3(delta.X, delta.Y, delta.Z) / sinHalf * (angle / dt);
    }

    // ---- Landing ----------------------------------------------------------------------

    void CheckLanding(PhysicsWorld world, Body body, float dt)
    {
        PhysicsRules.GearData t = world.Rules.Gear;
        bool idle = !Flight.Seated || !Flight.AnyThrust;
        _idle = idle ? _idle + dt : 0f;

        bool tooHeavy = !Stats.CanLiftOff && Zone.Gravity > 0f;
        bool onGear = Gear != null && Gear.Deployed && Gear.Contact;
        bool onGround = onGear || (tooHeavy && body.OnFloor);
        // Slow against whatever it is standing on: a deck can be moving.
        Vector3 under = RestingOn(body) is Body deck ? deck.PointVelocity(body.Position) : Vector3.Zero;
        bool slow = (body.Velocity - under).Length() < t.LandSpeed;
        if (onGround && slow && (tooHeavy || _idle >= t.IdleBeforeLanding))
        {
            _settleLeft = t.SettleTime;
            Flight.Halt();
        }
    }

    /// <summary>
    /// Settles onto the gear: tilts to the ground under its feet (level on flat ground, never
    /// more than the settle limit on dug or rocky ground), feet down, then stops simulating.
    /// </summary>
    void Settle(PhysicsWorld world, Body body, float dt)
    {
        body.Velocity = Vector3.Zero;
        body.AngularVelocity = Vector3.Zero;
        LocalSpin = Vector3.Zero;
        if (Gear != null && Gear.Deployed)
        {
            float k = 1f - MathF.Exp(-10f * dt);
            Gear.Probe(world, body);
            Vector3 up = Gear.GroundUp(body, Units.Radians(world.Rules.Gear.MaxSettleTilt));
            Quaternion target = MathX.TiltToward(MathX.Level(body.Rotation), up, 1f);
            body.Rotation = Quaternion.Normalize(Quaternion.Slerp(body.Rotation, target, k));
            Gear.Probe(world, body);
            if (float.IsFinite(Gear.MinClearance)) body.Position -= Vector3.UnitY * Gear.MinClearance * k;
        }

        _settleLeft -= dt;
        if (_settleLeft > 0f) return;
        _settleLeft = 0f;
        Frozen = true;
        Parked = true;
        world.RaiseLanded(body);
    }

    /// <summary>
    /// A landed vessel wakes when its pilot applies thrust, unless it is too heavy to fly: then
    /// the console says why and it stays put.
    /// </summary>
    bool TryWake(PhysicsWorld world, Body body)
    {
        if (!Flight.Seated || !Flight.AnyThrust) return false;
        if (!Stats.CanLiftOff && Zone.Gravity > 0f)
        {
            if (ConsoleMessageAge > 2f) Say(Stats.LiftWarning);
            return false;
        }
        bool wasLanded = Frozen;
        Wake(body);
        Parked = false;
        world.Unpark(body); // lifting off its host: from here on the wind reaches it
        if (wasLanded) world.RaiseLiftedOff(body);
        return true;
    }
}
