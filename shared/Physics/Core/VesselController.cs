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
/// rides on hover engines, sets down on its gear or its hull, and stops simulating once landed (zero cost) until
/// thrust or a knock wakes it. A vessel with no hull points left loses power and becomes a free
/// wreck the engine tumbles. The world resolves its contacts with the same rules as everything.
/// </summary>
public sealed class VesselController : IBodyController, IContactListener, IForceReactor
{
    /// <summary>A landed vessel wakes when weather pushes it harder than this share of its weight (a tornado, not a breeze).</summary>
    public const float WeatherWakeShare = 0.3f;

    /// <summary>
    /// A vessel the weather pulls with more than this share of its weight is past what its thrusters can
    /// hold (<see cref="Body.Tossed"/>), for <see cref="TossSeconds"/> after the last such pull. A light
    /// craft can be; a hover tank or a capital ship, whose share of a tornado's pull is tiny, never is.
    /// A pilot at the controls takes it back <see cref="RecoverAfter"/> seconds after the last one.
    /// </summary>
    public const float TossShare = 0.5f, TossSeconds = 5f, RecoverAfter = 0.7f;

    /// <summary>
    /// The most angular acceleration a craft's gyros can put against a tumble, rad/s², and its hover
    /// plates' on top within their ride height of the ground (none higher: no ground under them).
    /// Past these the weather's turn wins, and it tips, rolls and flips.
    /// </summary>
    public const float GyroLevel = 1.2f, PlateLevel = 4f;

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

    float _idle, _settleLeft, _restFor, _tossedFor;

    // Woken off its landing by the world changing under it (<see cref="Wake"/>): until a pilot asks for thrust or it lands
    // again, its lift idles as it did landed, so it comes down onto whatever is left under it instead of hanging there.
    bool _footingGone;

    // Where its hull last rested on a floor: still there, it still rests on it, though the assist holding a craft let go
    // there presses it on nothing any more (no contact is met without a move).
    Vector3? _restAt;

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
        _footingGone = false;
        _restAt = null;
        Parked = false;
        Hover?.Reset();
        Gear?.Reset(false);
    }

    /// <summary>
    /// Puts the vessel down landed: gear out, settled, not simulating. Put down with its gear on
    /// another structure's deck, it parks there on its first tick.
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
    /// Docks the vessel to a host (any other structure) where it is now: it stops simulating,
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

    /// <summary>
    /// Knocked loose by something (a ram, the ground dug out from under it): simulate again, without a lift-off.
    /// <paramref name="footingGone"/>: what it stood on may be gone (dug out, a tree broken): it comes down again.
    /// </summary>
    public void Wake(Body body, bool footingGone = false)
    {
        // Only what stood on something can lose it: one put down landed where nothing touches it yet is as it was.
        _footingGone |= footingGone && (Frozen || Settling) && (Gear is { Contact: true } || _restAt != null);
        Frozen = false;
        body.Asleep = false;
        _settleLeft = 0f;
        _idle = 0f;
        _restAt = null;
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
                world.Park(body, Gear?.StandingOn);
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

        if (body.Tossed > 0f && Tossed(world, body, dt)) return;

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
        Gear?.Step(world, body, dt);

        // The flight rules said what velocity they want; the engines make the force for it. The
        // world adds gravity, water and weather on top, so the thrust here cancels the weight it
        // holds up. In water a vessel that can hold itself up holds only what the water doesn't
        // (its buoyancy carries the rest), so it flies underwater as in air, slowed by the drag; one
        // that can't (no power, too heavy) has its engines flat out already, and sinks or floats by
        // its density.
        // Knocked off its footing with nobody asking for thrust: no lift, and no assist braking its fall, till it stands again.
        if (Flight.Seated && Flight.AnyThrust) _footingGone = false;
        bool dropping = _footingGone && Hover == null && Gear is not { Attached: true } && !body.OnFloor;
        if (dropping) v.Y = v0.Y;
        Vector3 gravity = world.GravityAt(body);
        bool holding = !dropping && (Stats.CanLiftOff || Zone.Gravity <= 0f);
        Vector3 buoyed = holding ? world.BuoyancyOn(body) : Vector3.Zero;
        world.AddForce(body, body.Mass * ((v - v0) / dt - (dropping ? Vector3.Zero : gravity)) - buoyed, ForceKind.Thrust);
    }

    public void AfterMove(PhysicsWorld world, Body body, float dt)
    {
        if (body.Asleep || body.Motion != Motion.Kinematic) return;
        CheckLanding(world, body, dt);

        // Attached to another structure by its gear (or resting on it on its hull), with nobody
        // pushing the controls: parked there, out of the wind. Moving under its own power, it isn't.
        Body host = Flight.Seated && Flight.AnyThrust ? null : RestingOn(body);
        if (!world.Park(body, host)) world.Unpark(body);
    }

    /// <summary>The registered body the vessel rests on: under its feet, or under its hull.</summary>
    Body RestingOn(Body body) => Gear?.StandingOn ?? body.Underfoot;

    /// <summary>
    /// Thrown by the weather: no thrust, no assist, no hover cushion holding it. It flies and turns as it
    /// was thrown (<see cref="React"/> spins it), falls under gravity and the wind, and its landings hurt
    /// by the closing speed like any other hit (face-on hard, glancing little). Where it comes to rest it
    /// lies as it landed, on its side or its roof if so, until a pilot lifts it off. A pilot at the
    /// controls takes it back once out of the strongest pull, if there is room. Returns false once back
    /// under its own power.
    /// </summary>
    bool Tossed(PhysicsWorld world, Body body, float dt)
    {
        _tossedFor += dt;
        if (Flight.Seated && Flight.AnyThrust && body.Tossed <= TossSeconds - RecoverAfter && GroundDistance > 3f)
        {
            body.Tossed = 0f;
            _tossedFor = _restFor = 0f;
            return false; // the pilot has it again: the flight model rights and flies it from here
        }
        StrikeUnstreamedGround(world, body, dt);
        bool resting = body.Velocity.Length() < 1.5f && (body.OnFloor || GroundDistance < body.Shape.FeetBelowOrigin + 1.5f);
        _restFor = resting ? _restFor + dt : 0f;
        if (_restFor > 0.6f || _tossedFor > 40f)
        {
            // Come to rest: it lies where and as it landed. Its engines stay off until a pilot asks.
            body.Tossed = 0f;
            body.Velocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
            LocalSpin = Vector3.Zero;
            _tossedFor = _restFor = 0f;
            Frozen = true;
            Parked = true;
            Flight.Halt();
            world.RaiseLanded(body);
            return true;
        }
        body.Tossed = MathF.Max(body.Tossed - dt, resting ? 0f : 0.5f); // airborne or rolling, it stays the weather's
        Quaternion spun = Quaternion.Normalize(body.Rotation * MathX.AxisAngle(Vector3.UnitY, LocalSpin.Y * dt)
            * MathX.AxisAngle(Vector3.UnitX, LocalSpin.X * dt) * MathX.AxisAngle(Vector3.UnitZ, LocalSpin.Z * dt));
        body.AngularVelocity = AngularVelocity(body.Rotation, spun, dt);
        body.Rotation = spun;
        Flight.Halt();
        return true;
    }

    /// <summary>
    /// What turns a tossed craft: the weather's push acts above its middle (the wind is stronger higher up,
    /// and lifts it unevenly), so it tips it; a vortex holding it turns it round with it. Against that its
    /// gyros, and its hover plates when the ground is within their reach, right it, each up to its limit.
    /// </summary>
    void Tumble(Body body, float dt)
    {
        Vector3 push = body.PendingForceFrom(ForceKind.Weather);
        float h = MathF.Max(body.Shape.Height, 0.5f), r = MathF.Max(body.Shape.Radius, 0.5f);
        float inertia = 0.3f * body.Mass * (r * r + 0.25f * h * h);
        var flat = new Vector3(push.X, 0f, push.Z);
        // Shear tips it (the push acts above its middle); uneven lift across its width turns it end over end
        // about its own beam, whichever way the flow round it pushes; the vortex turns it round with it.
        Vector3 torque = Vector3.Cross(Vector3.UnitY * (0.35f * h), push) + MathX.Rotate(body.Rotation, Vector3.UnitX) * (push.Length() * r * 0.2f)
            + Vector3.UnitY * (flat.Length() * r * 0.3f);
        Vector3 spin = MathX.Rotate(body.Rotation, LocalSpin);
        Vector3 up = MathX.Up(body.Rotation);
        Vector3 axis = Vector3.Cross(up, Vector3.UnitY);
        float tilt = MathF.Atan2(axis.Length(), Vector3.Dot(up, Vector3.UnitY));
        axis = axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : Vector3.Zero;
        bool plates = Hover != null && GroundDistance < MathF.Max(Stats.RideHeight, 1f) * 1.5f;
        Vector3 level = MathX.LimitLength(axis * (3f * tilt) - spin * 1.5f, GyroLevel + (plates ? PlateLevel : 0f));
        // On the ground its turning stops against it: it rolls to rest rather than spinning in place.
        float drag = body.OnFloor || GroundDistance < body.Shape.FeetBelowOrigin + 0.5f ? 6f : 0.2f;
        spin = MathX.LimitLength((spin + (torque / inertia + level) * dt) * MathF.Exp(-drag * dt), 5f);
        LocalSpin = MathX.Unrotate(body.Rotation, spin);
    }

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
        float top = float.NegativeInfinity;
        foreach (IStuckField f in world.StuckFields)
            if (f.Applies(body)) top = MathF.Max(top, f.TopAt(body, keel.X, keel.Z));
        if (!float.IsFinite(top) || keel.Y > top) return;
        if (world.Query.Ray(body.Position, keel - Vector3.UnitY, Layers.ProbeMask, body, out _)) return; // streamed: the hull's own sweep meets it
        var point = new Vector3(keel.X, top, keel.Z);
        world.ResolveImpact(body, null, point, Vector3.UnitY, -v.Y, Surface.Ground);
        body.Position = new Vector3(body.Position.X + v.X * dt, top + below, body.Position.Z + v.Z * dt);
        body.Velocity = new Vector3(v.X * 0.3f, 0f, v.Z * 0.3f);
    }

    /// <summary>Nothing is silently immune to weather: a landed ship a tornado pulls hard enough is knocked loose.</summary>
    public void React(PhysicsWorld world, Body body, float dt)
    {
        if (body.Tossed > 0f && !Frozen) Tumble(body, dt);
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
        if (body.Tossed > 0f) return; // it comes to rest as it lies (Tossed), not settled level on its gear
        PhysicsRules.GearData t = world.Rules.Gear;
        bool idle = !Flight.Seated || !Flight.AnyThrust;
        _idle = idle ? _idle + dt : 0f;

        bool tooHeavy = !Stats.CanLiftOff && Zone.Gravity > 0f;
        // Attached by its gear, or resting on its hull: nothing floats. A craft riding working hover
        // plates never sets down on its hull: they hold it off whatever it touches.
        if (body.OnFloor) _restAt = body.Position;
        else if (_restAt is Vector3 rest && Vector3.DistanceSquared(rest, body.Position) > 1e-4f) _restAt = null;
        bool onGround = Gear is { Attached: true } || _restAt != null && (Hover == null || tooHeavy);
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
    /// Comes to rest where it is, then stops simulating. It rights itself level as it settles, on its
    /// gear or its hull: a craft that came down nose first stands on its feet again, so its lift
    /// thrust points up when its pilot next takes off, not into the ground ahead (it never gets
    /// stuck there). Each telescoping leg reaches its own ground, so it needs no tilt to stand on
    /// uneven ground. Turned unswept, it is pushed clear of the ground as it lies (the binding's
    /// clear-out). A craft the weather threw lies as it landed instead (<see cref="Tossed"/>).
    /// </summary>
    void Settle(PhysicsWorld world, Body body, float dt)
    {
        body.Velocity = Vector3.Zero;
        body.AngularVelocity = Vector3.Zero;
        LocalSpin = Vector3.Zero;
        body.Rotation = Quaternion.Normalize(Quaternion.Slerp(body.Rotation, MathX.Level(body.Rotation), 1f - MathF.Exp(-10f * dt)));
        _settleLeft -= dt;
        if (_settleLeft > 0f) return;
        _settleLeft = 0f;
        _footingGone = false;
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
