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

        /// <summary>Damage = mass (t) × (closing − safe)² × this. Never capped, never scaled down.</summary>
        public float DamageScale { get; set; } = 0.001f;

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
    /// same way, and toughness is all that differs. Hull points are ship-handling's 4 per tonne.
    /// A suited player is far softer per tonne than armour plate: 0.5 per tonne makes a 30 m
    /// fall cost about half their health, as in the weather prototype.
    /// </summary>
    public sealed class IntegrityData
    {
        public float HullPerTonne { get; set; } = 4f;
        public float CratePerTonne { get; set; } = 4f;
        public float PlayerPerTonne { get; set; } = 0.5f;
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
        public float LevelRate { get; set; } = 6f;
        public float MaxSlope { get; set; } = 30f;
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

        /// <summary>A landed ship tilts to its feet on uneven ground, but never more than this, degrees.</summary>
        public float MaxSettleTilt { get; set; } = 12f;
    }

    /// <summary>Riding a structure's cabin world out on its hull: letting go, rejoining and mag boots (see <see cref="GalHack.Physics.Riding"/>).</summary>
    public sealed class RidingData
    {
        /// <summary>An outer-hull rider out of contact this long lets go of the ship, s. A jump lets go at once.</summary>
        public float LetGoAfter { get; set; } = 0.4f;

        /// <summary>An outer-hull rider farther than this from any block lets go at once, m.</summary>
        public float LetGoDistance { get; set; } = 1.5f;

        /// <summary>After letting go, a body can't join the same ship again for this long, s (no flicker at the hull).</summary>
        public float RejoinAfter { get; set; } = 0.5f;

        /// <summary>
        /// Mag boots on the outer hull: down is the hull face under the rider's feet, at <see cref="MagBootGravity"/>,
        /// and outside edges carry them round onto the next face. Off: the outer hull has the cabin's gravity
        /// (the core's, or the zone's when the core is off).
        /// </summary>
        public bool MagBoots { get; set; } = true;
        public float MagBootGravity { get; set; } = 9.8f;

        /// <summary>Mag boots also carry a rider up onto a wall rising from their face, if it is taller than a step (<see cref="WalkingData.StepHeight"/>).</summary>
        public bool InsideCorners { get; set; } = true;

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

    public sealed class StuckData
    {
        /// <summary>A body must be inside something this many ticks in a row before it is moved, so one frame of overlap never teleports anyone.</summary>
        public int TicksBeforeRescue { get; set; } = 2;

        /// <summary>How far to look for open space, m.</summary>
        public float SearchRadius { get; set; } = 14f;
    }
}
