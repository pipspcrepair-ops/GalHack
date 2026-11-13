using System;
using System.Text.Json;

namespace GalHack.Physics;

/// <summary>
/// Every number the shared physics module uses, loaded from <c>shared/Physics/physics.json</c>.
/// The defaults here are the same numbers, so a test or tool can run without the file.
///
/// The contact, hover and gear numbers started life in ship-handling's handling.json and the
/// crater cap in terrain-digging's ChunkStore. Anything built on the shared module reads them
/// from here; the handling model keeps only flight numbers (classes, turning, assist, boost,
/// parts).
/// </summary>
public sealed class PhysicsRules
{
    public ContactData Contacts { get; set; } = new();
    public CraterData Craters { get; set; } = new();
    public IntegrityData Integrity { get; set; } = new();
    public HoverData Hover { get; set; } = new();
    public GearData Gear { get; set; } = new();
    public StuckData Stuck { get; set; } = new();
    public RidingData Riding { get; set; } = new();
    public WalkingData Walking { get; set; } = new();
    public WaterData Water { get; set; } = new();
    public FloraData Flora { get; set; } = new();

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Parses the rules file. The caller reads the file, so this stays engine-free.</summary>
    public static PhysicsRules FromJson(string json) =>
        JsonSerializer.Deserialize<PhysicsRules>(json, Options) ?? throw new InvalidOperationException("empty physics rules");

    public sealed class ContactData
    {
        /// <summary>Closing speeds at or below this do no damage at all: landing, docking, nudging. m/s.</summary>
        public float SafeSpeed { get; set; } = 8f;

        /// <summary>
        /// A player's own safe speed, m/s. A suited player lands on their legs: hops, jetpack
        /// landings and drops up to about 12 m (15.5 m/s) never hurt. Only the safe speed differs;
        /// the damage formula is the same one.
        /// </summary>
        public float PlayerSafeSpeed { get; set; } = 15.5f;

        /// <summary>Damage = mass (t) × (closing − safe)² × this. Never capped, never scaled down.</summary>
        public float DamageScale { get; set; } = 0.001f;

        /// <summary>The safe speed for a body of this kind: a player's own, everything else's <see cref="SafeSpeed"/>.</summary>
        public float SafeSpeedFor(BodyKind kind) => kind == BodyKind.Player ? PlayerSafeSpeed : SafeSpeed;

        /// <summary>Kinematic hulls bounce off fixed things with this much of the closing speed.</summary>
        public float Restitution { get; set; } = 0.2f;

        /// <summary>Share of the sliding speed lost on a contact that did damage.</summary>
        public float Scrape { get; set; } = 0.3f;

        /// <summary>Contacts slower than this raise no event (resting, nudging). m/s.</summary>
        public float EventSpeed { get; set; } = 2f;
    }

    public sealed class CraterData
    {
        /// <summary>Ground hits faster than this blast a crater. m/s.</summary>
        public float Speed { get; set; } = 25f;

        /// <summary>Radius = scale × ∛(kinetic energy in MJ), m.</summary>
        public float Scale { get; set; } = 2f;

        /// <summary>
        /// Never wider than this, m. It is terrain-digging's own crater cap (ChunkStore.MaxCraterRadius),
        /// which also caps the cells one crater may remove, so the decal and the hole always agree.
        /// </summary>
        public float MaxRadius { get; set; } = 6f;

        /// <summary>Smaller blasts would remove a cell or none; they make no crater, m.</summary>
        public float MinRadius { get; set; } = 1f;

        /// <summary>The blast sphere's centre sits this share of the radius above the contact point, so it cuts a bowl.</summary>
        public float Lift { get; set; } = 0.25f;
    }

    /// <summary>
    /// How much damage a body can take, per tonne of it: the one formula hurts everything the
    /// same way, and toughness (with a player's own safe speed) is all that differs. Hull points
    /// are ship-handling's 4 per tonne. A suited player is far softer per tonne than armour plate:
    /// 0.6 per tonne, over their 15.5 m/s safe speed, makes a 30 m fall cost about an eighth of
    /// their health and a 50 m one about two fifths; only a fall of 80 m or so kills.
    /// </summary>
    public sealed class IntegrityData
    {
        public float HullPerTonne { get; set; } = 4f;
        public float CratePerTonne { get; set; } = 4f;
        public float PlayerPerTonne { get; set; } = 0.6f;
        public float CreaturePerTonne { get; set; } = 0.5f;

        public float PerTonne(BodyKind kind) => kind switch
        {
            BodyKind.Vessel or BodyKind.Wreck => HullPerTonne,
            BodyKind.Crate => CratePerTonne,
            BodyKind.Player => PlayerPerTonne,
            _ => CreaturePerTonne,
        };
    }

    /// <summary>Hover engines: see <see cref="HoverController"/>. Same meaning as in ship-handling's handling.json.</summary>
    public sealed class HoverData
    {
        public float VerticalGain { get; set; } = 8f;
        public float MaxRise { get; set; } = 40f;
        public float MaxGrip { get; set; } = 30f;
        public float GripBand { get; set; } = 0.8f;
        public float MaxClimbRate { get; set; } = 14f;
        public float LookAheadTime { get; set; } = 0.45f;
        public float LookAheadMin { get; set; } = 2f;
        public float LookAheadMax { get; set; } = 14f;
        public float StepAllowance { get; set; } = 0.5f;

        /// <summary>The look-ahead never lifts the vessel more than this × its ride height: it clears rocks and steps, it never hops a cliff.</summary>
        public float MaxLift { get; set; } = 1.5f;
        public float LevelRate { get; set; } = 6f;

        /// <summary>
        /// The steepest ground a hover vessel climbs, degrees, as a grade across its own length (the
        /// rise from the ground under it over the run to the ground ahead): a crater's lip is climbed,
        /// a cliff is not. Also the most it tilts to follow the ground.
        /// </summary>
        public float MaxSlope { get; set; } = 45f;
        public float JumpSpeed { get; set; } = 7f;
        public float JumpCooldown { get; set; } = 1.2f;
        public float ProbeDepth { get; set; } = 3f;
    }

    /// <summary>Landing gear and landing: see <see cref="LandingGear"/>.</summary>
    public sealed class GearData
    {
        public float DeployAltitude { get; set; } = 15f;
        public float DeploySpeed { get; set; } = 20f;
        public float Hysteresis { get; set; } = 5f;
        public float ExtendTime { get; set; } = 0.8f;
        public float LandSpeed { get; set; } = 3f;
        public float IdleBeforeLanding { get; set; } = 0.3f;
        public float SettleTime { get; set; } = 0.5f;
    }

    /// <summary>Riding a structure's cabin world out on its hull: letting go, rejoining and mag boots (see <see cref="GalHack.Physics.Riding"/>).</summary>
    public sealed class RidingData
    {
        /// <summary>An outer-hull rider out of contact this long lets go of the ship, s. A jump lets go at once.</summary>
        public float LetGoAfter { get; set; } = 0.4f;

        /// <summary>Out of contact counts toward letting go only this far off the hull, m: dropping off a block beside a deck is no letting go.</summary>
        public float LetGoNear { get; set; } = 0.5f;

        /// <summary>An outer-hull rider farther than this from any block lets go at once, m.</summary>
        public float LetGoDistance { get; set; } = 1.5f;

        /// <summary>After letting go, a body can't join the same ship again for this long, s (no flicker at the hull).</summary>
        public float RejoinAfter { get; set; } = 0.5f;

        /// <summary>
        /// Mag boots on the outer hull, switched off: out on the hull, down is the gravity the rider
        /// is in (the core's, or the zone's when the core is off), so walls and undersides can't be
        /// walked on. On, down would be the hull face under the rider's feet, at <see cref="MagBootGravity"/>,
        /// and outside edges would carry them round onto the next face.
        /// </summary>
        public bool MagBoots { get; set; }
        public float MagBootGravity { get; set; } = 9.8f;

        /// <summary>With mag boots on, they would also carry a rider up onto a wall rising from their face, if taller than a step (<see cref="WalkingData.StepHeight"/>). Off.</summary>
        public bool InsideCorners { get; set; }

        /// <summary>How far below the face just walked off to look for the next face round an outside edge, m.</summary>
        public float EdgeProbe { get; set; } = 0.35f;

        /// <summary>How fast a mag-booted rider's up follows a gently changing face (1/s).</summary>
        public float UpFollow { get; set; } = 20f;

        /// <summary>How fast a camera eases its roll to a rider's new up (1/s). Presentation reads it.</summary>
        public float ViewEase { get; set; } = 10f;
    }

    /// <summary>Walking bodies (players, creatures), wherever they walk: terrain, a cabin floor, an outer hull.</summary>
    public sealed class WalkingData
    {
        /// <summary>
        /// Anything up to this tall is a step: walking into it steps up onto it, and walking off
        /// a ledge this deep steps down onto the floor below without leaving it. Taller is a wall. m.
        /// </summary>
        public float StepHeight { get; set; } = 0.6f;
    }

    /// <summary>Water as a volume: buoyancy, drag and swimming (see <see cref="WaterForces"/>).</summary>
    public sealed class WaterData
    {
        /// <summary>kg/m³.</summary>
        public float Density { get; set; } = 1000f;

        /// <summary>
        /// Water drag uses a body's CdA times this: anything moving through water goes streamlined,
        /// not broadside as it stands in a wind.
        /// </summary>
        public float DragScale { get; set; } = 0.25f;

        /// <summary>Linear damping in water, 1/s (times the share under): bobbing and drifting settle.</summary>
        public float Damping { get; set; } = 3f;

        /// <summary>
        /// Each kind's density, kg/m³ of the water it displaces, when its spec gives none: below
        /// water's it floats, above it sinks. Keys are body kinds in lower case.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, float> Bodies { get; set; } = new()
        {
            ["vessel"] = 1600f, ["wreck"] = 2200f, ["crate"] = 450f, ["player"] = 800f, ["creature"] = 1000f,
        };

        /// <summary>A walking body swims once this share of its height is under water, and wades again below <see cref="WadeDepth"/>.</summary>
        public float SwimDepth { get; set; } = 0.7f;
        public float WadeDepth { get; set; } = 0.55f;

        /// <summary>Swimming speed, m/s (sprinting: <see cref="SwimSprintSpeed"/>), reached at <see cref="SwimAccel"/> m/s².</summary>
        public float SwimSpeed { get; set; } = 2.1f;
        public float SwimSprintSpeed { get; set; } = 3f;
        public float SwimAccel { get; set; } = 6f;

        /// <summary>How hard strokes can hold against buoyancy and drag, m/s² of the body's mass.</summary>
        public float SwimGrip { get; set; } = 30f;

        public float DensityOf(BodyKind kind) =>
            Bodies != null && Bodies.TryGetValue(kind.ToString().ToLowerInvariant(), out float d) && d > 0f ? d : Density;
    }

    /// <summary>
    /// What a tree takes from a structure before it breaks, by its trunk (<see cref="PhysicsWorld.ResolveContacts"/>): the weight on it
    /// or the blow into it. One rule for every craft and tree, so a heavy ship breaks an ordinary tree and a giant may hold a light one.
    /// </summary>
    public sealed class FloraData
    {
        /// <summary>The load a trunk takes per square metre of its cross-section, Pa.</summary>
        public float Strength { get; set; } = 200_000f;

        /// <summary>A blow is the momentum it brings, stopped over this long, s.</summary>
        public float BlowTime { get; set; } = 0.1f;

        /// <summary>The most a trunk of this radius takes, N.</summary>
        public float Takes(float trunkRadius) => Strength * MathF.PI * trunkRadius * trunkRadius;
    }

    public sealed class StuckData
    {
        /// <summary>A body must be inside something this many ticks in a row before it is moved, so one frame of overlap never teleports anyone.</summary>
        public int TicksBeforeRescue { get; set; } = 2;

        /// <summary>How far to look for open space, m.</summary>
        public float SearchRadius { get; set; } = 14f;
    }
}
