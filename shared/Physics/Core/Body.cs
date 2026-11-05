using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// What a body is. The rules are the same for all of them; only toughness and who moves it differ.
/// A base is a structure in base mode: it never flies and nothing ever moves it (<see cref="Motion.Fixed"/>).
/// </summary>
public enum BodyKind { Vessel, Wreck, Crate, Player, Creature, Base }

/// <summary>
/// A structure's class, for the rules that care which: vessels parked on a base or a capital ship
/// ride their host and feel no wind of their own. None for anything that isn't a vessel.
/// </summary>
public enum StructureClass { None, Hover, Small, Capital }

/// <summary>Who integrates a body's motion.</summary>
public enum Motion
{
    /// <summary>
    /// The core integrates velocity from the summed forces, and a mover sweeps the body through
    /// the world with collisions: vessels (gamey handling), players and creatures (character
    /// controllers). Deterministic and readable.
    /// </summary>
    Kinematic,

    /// <summary>
    /// A rigid body the engine integrates (crates, wrecks): the core hands it the same summed
    /// forces each tick, with the engine's own gravity and damping off, so acceleration is still
    /// exactly force / mass. The engine adds what it is good at: tumbling, stacking, resting.
    /// </summary>
    Free,

    /// <summary>
    /// Nobody: it stays exactly where it was placed (a base). It is registered so things can land
    /// on it, park on it and hit it, but no force, contact or rescue ever moves it.
    /// </summary>
    Fixed,
}

/// <summary>Where a force came from. Every force goes through <see cref="PhysicsWorld.AddForce"/> with one of these.</summary>
public enum ForceKind { Gravity, Thrust, Weather, Walk, Contact, Other, Water }

/// <summary>
/// The room a body needs, for stuck checks: an upright capsule standing on its feet.
/// Feet are <see cref="FeetBelowOrigin"/> below the body's position.
/// </summary>
public readonly record struct StuckShape(float Height, float Radius, float FeetBelowOrigin = 0f)
{
    public static StuckShape Box(float size) => new(size, size * 0.5f, size * 0.5f);
}

/// <summary>Everything needed to register a body. Mass and drag area are the two numbers every body has.</summary>
public sealed class BodySpec
{
    public BodyKind Kind;
    public Motion Motion = Motion.Kinematic;
    public string Label = "";

    /// <summary>kg.</summary>
    public float Mass;

    /// <summary>Drag coefficient × frontal area, m². Wind pushes with ½ρ·CdA·|v|·v.</summary>
    public float DragArea;

    /// <summary>Vessels: hover, small or capital. Capital ships carry the vessels parked on them.</summary>
    public StructureClass Class;

    /// <summary>
    /// kg per cubic metre of the water it displaces: below water's density it floats, above it
    /// sinks. NaN: the rules' density for its kind (physics.json, water.bodies).
    /// </summary>
    public float Density = float.NaN;

    /// <summary>Centre of mass in the body's own frame, m.</summary>
    public Vector3 CenterOfMass;

    public Vector3 Position;
    public Quaternion Rotation = Quaternion.Identity;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;

    /// <summary>How much damage it takes to break it. NaN: the rules' toughness for its kind and mass.</summary>
    public float Integrity = float.NaN;

    public StuckShape Shape;
    public object Tag;
}

/// <summary>Adds the forces a body makes for itself (thrust, walking) and reacts after it has moved.</summary>
public interface IBodyController
{
    /// <summary>Before velocities are integrated: add this tick's own forces, set orientation.</summary>
    void Drive(PhysicsWorld world, Body body, float dt);

    /// <summary>After the body has moved and its contacts are resolved.</summary>
    void AfterMove(PhysicsWorld world, Body body, float dt);
}

/// <summary>
/// Takes damage for a body that keeps its health somewhere else (a player's health with the
/// tornado's fling floor). Returns what was actually dealt. The default just adds it up.
/// </summary>
public interface IDamageSink
{
    float Take(Body body, float raw);
}

/// <summary>
/// One registered body: a vessel, wreck, crate, player or creature. It registers once, with
/// mass and drag area, and from then on every force on it goes through the world's one force
/// path. Plain data plus a few derived values; the world and the movers change it.
/// </summary>
public sealed class Body
{
    /// <summary>Registration order. Bodies always step in this order, so runs repeat exactly.</summary>
    public int Id { get; internal set; }

    public BodyKind Kind { get; internal set; }
    public Motion Motion { get; internal set; }
    public string Label { get; set; } = "";

    /// <summary>kg. Changes when a structure is edited or severed.</summary>
    public float Mass { get; set; }

    /// <summary>CdA, m².</summary>
    public float DragArea { get; set; }

    public StructureClass Class { get; set; }

    /// <summary>kg/m³ of displaced water: buoyancy is water's density / this × weight × the share under water.</summary>
    public float Density { get; set; }

    /// <summary>
    /// How the body sits in water this tick (the world updates it before anything reacts to it):
    /// the share of it under the surface, and whether it is wholly under, the cue for a vitals
    /// system that a suit must breathe its own oxygen.
    /// </summary>
    public WaterState Water { get; internal set; } = WaterState.Dry;

    /// <summary>
    /// The host this vessel is parked on: a base or a capital ship it has landed on, rests on with
    /// gear or contact, or is docked to. A parked body feels no wind and, while landed, moves with its
    /// host. Null when it isn't parked. See <see cref="PhysicsWorld.Park"/>.
    /// </summary>
    public Body ParkedOn { get; internal set; }

    // The host's pose at the start of this tick, so a landed body can be carried by exactly the
    // host's motion over the tick (turning and moving alike), whatever order the two step in.
    internal Vector3 HostStartPosition;
    internal Quaternion HostStartRotation = Quaternion.Identity;

    /// <summary>Centre of mass in the body's own frame, m.</summary>
    public Vector3 CenterOfMass { get; set; }

    /// <summary>
    /// The body's origin. In the world frame, or in <see cref="Frame"/>'s frame while it rides
    /// that structure (inside its cabin or out on its hull: both are its cabin world).
    /// </summary>
    public Vector3 Position;

    /// <summary>
    /// Orientation, in the same frame. For a walking body it is its walk frame: its Y is
    /// <see cref="Up"/>, and its heading is the way "forward" points.
    /// </summary>
    public Quaternion Rotation = Quaternion.Identity;

    /// <summary>Velocity of <see cref="Position"/>, m/s, in the frame's axes.</summary>
    public Vector3 Velocity;

    /// <summary>Spin, rad/s, in the same frame as the velocity.</summary>
    public Vector3 AngularVelocity;

    public float Integrity { get; set; }

    /// <summary>Damage taken so far. Never capped: it can go past <see cref="Integrity"/>.</summary>
    public float Damage { get; internal set; }

    public bool Broken => Damage >= Integrity;

    /// <summary>Not simulated this tick: a landed vessel or a sleeping rigid body. Costs nothing.</summary>
    public bool Asleep { get; set; }

    public StuckShape Shape { get; set; }

    /// <summary>
    /// The structure whose cabin world this body rides in (inside, or out on the hull), or null
    /// for the zone's world. See <see cref="Riding"/>.
    /// </summary>
    public Body Frame { get; internal set; }

    /// <summary>
    /// Riding a structure but outside its interior: on the outer hull. Weather reaches it, losing
    /// contact lets it go, and mag boots (if on) hold it to the face under its feet.
    /// </summary>
    public bool OnOuterHull { get; set; }

    /// <summary>
    /// A walking body's up, in its frame: the way its feet push off. Gravity's up normally; the
    /// hull face under its feet when mag boots hold it.
    /// </summary>
    public Vector3 Up = Vector3.UnitY;

    // How long an outer-hull rider has been out of contact with the hull (see Riding.LetGo).
    internal float LostContactFor;

    /// <summary>Ship gravity, for structures with a core. Null for everything else.</summary>
    public CoreGravity Core { get; set; }

    public IBodyController Controller { get; set; }
    public IDamageSink DamageSink { get; set; }

    /// <summary>Whatever the game hangs on the body (its node binding).</summary>
    public object Tag { get; set; }

    /// <summary>Set by the mover each tick: standing on something.</summary>
    public bool OnFloor { get; set; }

    /// <summary>The registered body this one rested on in its last move (a contact from below), or null.</summary>
    public Body Underfoot { get; internal set; }

    // ---- The force path ---------------------------------------------------------------

    internal Vector3 PendingForce, PendingImpulse, PendingAngularImpulse;
    readonly Vector3[] _forceBy = new Vector3[ForceKinds];
    readonly Vector3[] _lastForceBy = new Vector3[ForceKinds];
    internal const int ForceKinds = 7;

    /// <summary>Total force from each source during the last completed tick, N. For tests and the HUD.</summary>
    public Vector3 LastForce(ForceKind kind) => _lastForceBy[(int)kind];

    /// <summary>The summed force of the last completed tick, N.</summary>
    public Vector3 LastTotalForce { get; private set; }

    /// <summary>The summed impulse of the last completed tick, N·s.</summary>
    public Vector3 LastImpulse { get; private set; }

    /// <summary>What the forces alone did to the velocity last tick: ΣF / m, m/s².</summary>
    public Vector3 LastAcceleration => Mass > 0f ? LastTotalForce / Mass : Vector3.Zero;

    /// <summary>The force from one source so far this tick, N.</summary>
    public Vector3 PendingForceFrom(ForceKind kind) => _forceBy[(int)kind];

    internal void Accumulate(Vector3 force, ForceKind kind)
    {
        PendingForce += force;
        _forceBy[(int)kind] += force;
    }

    internal void Publish()
    {
        LastTotalForce = PendingForce;
        LastImpulse = PendingImpulse;
        Array.Copy(_forceBy, _lastForceBy, ForceKinds);
    }

    /// <summary>This tick's summed force so far, N (engine adapters hand it to free bodies).</summary>
    public Vector3 PendingTotalForce => PendingForce;
    public Vector3 PendingTotalImpulse => PendingImpulse;

    /// <summary>This tick's angular impulse about the centre of mass, N·m·s. Only free bodies spin from hits.</summary>
    public Vector3 PendingSpinImpulse => PendingAngularImpulse;

    internal void ClearForces()
    {
        PendingForce = Vector3.Zero;
        PendingImpulse = Vector3.Zero;
        PendingAngularImpulse = Vector3.Zero;
        Array.Clear(_forceBy);
    }

    // ---- Frames -----------------------------------------------------------------------

    /// <summary>A point in the body's own frame, in the frame its position is in.</summary>
    public Vector3 ToParent(Vector3 local) => Position + Vector3.Transform(local, Rotation);

    /// <summary>A point in the parent frame, in the body's own frame.</summary>
    public Vector3 ToLocal(Vector3 point) => Vector3.Transform(point - Position, Quaternion.Inverse(Rotation));

    /// <summary>The velocity of a point fixed to the body, spin included. Passengers stepping off need this.</summary>
    public Vector3 PointVelocity(Vector3 point) => Velocity + Vector3.Cross(AngularVelocity, point - Position);

    /// <summary>The body's orientation in the world, wherever it is simulated.</summary>
    public Quaternion WorldRotation => Frame == null ? Rotation : Quaternion.Normalize(Frame.Rotation * Rotation);

    /// <summary>Where the body is in the world, wherever it is simulated.</summary>
    public Vector3 WorldPosition => Frame == null ? Position : Frame.ToParent(Position);

    /// <summary>The body's velocity relative to the ground, wherever it is simulated.</summary>
    public Vector3 WorldVelocity
    {
        get
        {
            if (Frame != null) return Frame.PointVelocity(WorldPosition) + Vector3.Transform(Velocity, Frame.Rotation);
            return Velocity;
        }
    }

    /// <summary>The centre of mass, in the frame the body's position is in.</summary>
    public Vector3 CenterOfMassPosition => ToParent(CenterOfMass);

    /// <summary>Linear momentum, kg·m/s: mass times the velocity of the centre of mass.</summary>
    public Vector3 Momentum => Mass * PointVelocity(CenterOfMassPosition);

    public override string ToString() => $"{Kind} #{Id} {Label}";
}
