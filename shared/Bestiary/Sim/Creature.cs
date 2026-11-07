using System.Collections.Generic;
using Godot;

namespace Wildlife;

public enum CState { Idle, Wander, Graze, Flee, Stalk, Chase, Hidden, Lurk, Warn, ReturnHome, Scavenge, Eat, Follow, Freeze, Root, Rest, Patrol, Tell, Guard, InWalls }

/// <summary>Every attack runs wind-up, lunge, recover. The wind-up is the telegraph.</summary>
public enum AttackPhase { None, Windup, Lunge, Recover }

public enum HopPhase { Ground, Crouch, Air, Land }

/// <summary>Something creatures can see, flee from and attack: a player on foot or in a vessel.</summary>
public interface ITarget
{
    string Name { get; }
    /// <summary>Feet position.</summary>
    Vector3 Position { get; }
    /// <summary>Where it is looking (living boulders only move while you look away).</summary>
    Vector3 Forward { get; }
    Health Health { get; }
    /// <summary>On foot and alive. A player in the hovercraft scares creatures but can't be bitten.</summary>
    bool Attackable { get; }
    /// <summary>Multiplies flee distances: a roaring hovercraft is scarier than someone walking.</summary>
    float ThreatScale { get; }
    /// <summary>The light they carry, 0 none to 1: a torch gives you away however dark it is.</summary>
    float Lamp => 0f;
    /// <summary>Low to the ground (a player holding crouch): quieter to the ear, and a smaller target to the eye.</summary>
    bool Crouched => false;
}

/// <summary>A target the tests (and the screenshot staging) can place by hand.</summary>
public sealed class DummyTarget : ITarget
{
    public string Name { get; set; } = "Dummy";
    public Vector3 Position { get; set; }
    public Vector3 Forward { get; set; } = Vector3.Forward;
    public Health Health { get; set; } = new(100f);
    public bool Attackable { get; set; } = true;
    public float ThreatScale { get; set; } = 1f;
    public bool Crouched { get; set; }
}

/// <summary>
/// One living (or dead) animal. Plain data plus a rig: the brain writes intentions into it
/// (where to go, whether to crouch, rear up or lunge), locomotion moves it over the terrain,
/// and the rig turns all of that into a pose. No nodes, so 200 of them simulate headless.
/// </summary>
public sealed class Creature
{
    public int Id;
    public Species Species;
    public Rig Rig;
    public Herd Herd;
    public Pack Pack;
    public int HerdSlot;
    public Rng Rng;

    // Motion. Position is the ground point under the body (or the body itself for fliers).
    public Vector3 Position, Velocity, Home;
    public float Yaw;
    public float Age;

    // Health and death.
    public float Health;
    public bool Dead, Removed, Harvested;
    public float DeadTime, MeatLeft;
    public float HurtFlash;
    public ITarget ProvokedBy;
    public float ProvokedAt = -999f;

    // Brain.
    public CState State = CState.Wander;
    public float StateTime, Think;
    /// <summary>The brain runs on its own timer: time since it last thought, how long until it next does, and how long its last think covered.</summary>
    public float ThinkClock, ThinkEvery, ThinkDt = 1f / 60f;
    /// <summary>When a hunter next looks round for something to eat.</summary>
    public float PreyLookAt;
    public Vector3 Goal;
    public bool HasGoal;
    public ITarget Target;
    /// <summary>The horizontal velocity the brain asks for.</summary>
    public Vector3 Desired;
    public Vector3? FacePoint;
    /// <summary>Idling: for how long, and how far round it is looking (0 if it is feeding). Wandering: how far its heading has drifted.</summary>
    public float IdleFor, IdleLook, WanderTurn;

    // Attacks. The first attack on each target is telegraphed with the species' long wind-up.
    public AttackPhase Phase;
    public float PhaseTime, PhaseLength, WindupLength;
    public bool FirstAttack, Committed;
    public ITarget AttackTarget;
    public Vector3 AimPoint, LungeFrom, LungeTo;
    /// <summary>From its middle to the tip of its jaws, standing (see <see cref="Attacks.Jaws"/>); 0 until measured.</summary>
    public float JawsAtRest;
    /// <summary>Where its jaws end up when the lunge lands.</summary>
    public Vector3 StrikePoint;
    readonly Dictionary<ITarget, float> _lastAttack = new();

    // Pose intentions, 0..1, smoothed by the rig. Brace is a charge's wind-up: head down,
    // pawing the ground.
    public float WantCrouch, WantHidden, WantGraze, Rear, Lunge, Alert, Brace;
    public Vector3? LookAt;

    // Fliers and floaters: target height above ground. 0 means land.
    public float Altitude;
    public bool Landed;

    // Winged bodies in the air (see Flight). A bank's turn, in radians a second, and the roll that
    // goes with it; whether the wings are working (the rig follows this); on the take-off run.
    public float TurnRate, Bank;
    public bool Flapping, Climbing, TakingOff;
    public float FlapFor, LandedAt = -999f;
    /// <summary>A dive's aim, and when the next pass may start.</summary>
    public Vector3 AirAim;
    public bool AirAimOn;
    public float NextPass = -999f;
    /// <summary>Something ahead: when next to look, how long to keep clear of it, which way, and whether up.</summary>
    public float ProbeIn, AvoidFor, AvoidSide;
    public Vector3 AvoidFrom;
    public bool AvoidClimb;

    // Hoppers.
    public HopPhase Hop;
    public float HopTime, HopCooldown;

    // Swarms: how many members are still flying, from health.
    public int SwarmAlive;

    // Zero-g (space fauna): a full orientation, since there is no ground to stand level on, and
    // for clingers the hull face they hold on to and the spot they go back to after a strike.
    public Basis Facing = Basis.Identity;
    public Hull ClingHull, HomeHull;
    public int ClingFace, HomeFace;
    public Vector3 Anchor;
    public bool Clinging => ClingHull != null;
    public List<SpaceDrops.Loot> Loot;

    // Paths (only when the straight line is blocked).
    public readonly List<Vector3> Path = new();
    public int PathIndex;
    public Vector3 PathGoal;
    public float PathAge;

    /// <summary>Rig update interval in frames, set by the view by distance (1 near, more far away).</summary>
    public int RigInterval = 1;
    public float RigPending;

    // Day and night: where it rests outside its hours, and when it was last woken.
    public RestSite Rest;
    public float AwakeUntil = -999f;

    // Caves and places. A creature with a Space moves on that space's surfaces (floors, and
    // walls and ceilings if its species climbs) instead of the terrain's heights; Position is
    // the point on the surface under its body and Facing.Y the surface normal.
    public ISolidSpace Space;
    /// <summary>How far its body is held off the surface it holds.</summary>
    public float Clearance = 0.25f;
    public float CrawlSpeed;
    /// <summary>Let go (or knocked off): falling until it lands on something.</summary>
    public bool Falling;
    /// <summary>Coming down from a drop: it goes all the way to the floor, and catches no wall on the way.</summary>
    public bool Dropping;
    /// <summary>Last place it stood firmly, for the stuck-safety.</summary>
    public Vector3 LastFirm;
    public Vector3 LastFirmNormal = Vector3.Up;
    public readonly List<Vector3> SpacePath = new();
    public int SpacePathIndex;
    public Vector3 SpacePathGoal;
    public float SpacePathAge;

    /// <summary>Hidden in the walls: in at <see cref="InCrack"/>, out at <see cref="CrackExit"/> after <see cref="WallLength"/> seconds.</summary>
    public Crack InCrack, CrackExit;
    public float WallTime, WallLength, ScratchTimer;
    public bool InWalls => InCrack != null;

    public AmbushPoint Ambush;
    public Nest Nest;
    /// <summary>A prowler's beat: the points it walks, back and forth.</summary>
    public List<Vector3> Route;
    public int RouteIndex, RouteStep = 1;
    /// <summary>What it is hunting among the place's creatures, and how hungry it is (0..1).</summary>
    public Creature Prey;
    public float Hunger;
    /// <summary>An ambush or a hunt giving itself away: which tell, and since when.</summary>
    public TellKind TellKind;
    public float TellStart = -999f;
    public ITarget TellTarget;
    /// <summary>The player it has noticed, and when.</summary>
    public ITarget Noticed;
    public float NoticedAt = -999f;
    /// <summary>0..1: how far it has got to noticing a player (see <see cref="Senses"/>); at 1 it has.</summary>
    public float Awareness;
    /// <summary>When a hunter by voice next clicks.</summary>
    public float NextClick;
    /// <summary>Footfalls the view has already played.</summary>
    public int StepsHeard;
    /// <summary>Inside a place: when it next makes a small sound, if it's doing anything.</summary>
    public float QuietIn;
    /// <summary>A breaker's wind-up on the block in its way, s.</summary>
    public float BreakWind;
    /// <summary>The young at play: how long this dash lasts, when the next, which way.</summary>
    public float FriskFor, FriskIn;
    public Vector3 FriskDir;
    public Creature FriskAfter;
    /// <summary>Whether it could see or hear a player at its last think.</summary>
    public bool Sensing;
    /// <summary>Where it last knew a player, or a loud noise, to be.</summary>
    public Vector3 LastSeen;
    public float HeardAt = -999f;
    /// <summary>0..1: eyes opening, a lurker waking (the view brightens its eyes).</summary>
    public float Wake;
    // Domestic animals: the pen or town area they keep to, and (pets) who they follow.
    public Vector3 PenCenter;
    public float PenRadius;
    public ITarget Owner;
    /// <summary>A wild creature inside a settlement (the town has been told).</summary>
    public bool InTown;
    CreatureTarget _asTarget;
    /// <summary>This creature as prey for another.</summary>
    public CreatureTarget AsTarget => _asTarget ??= new CreatureTarget(this);

    public Stats Stats => Species.Stats;
    public float Radius => Species.Stats.Radius;
    public bool Alive => !Dead;
    public Vector3 Forward => new(-Mathf.Sin(Yaw), 0f, -Mathf.Cos(Yaw));
    public bool Attacking => Phase != AttackPhase.None;

    /// <summary>Whether its next attack on this target must be the telegraphed first one.</summary>
    public bool MustTelegraph(ITarget target, float now, CombatRules rules) =>
        !_lastAttack.TryGetValue(target, out float last) || now - last > rules.TelegraphMemorySeconds;

    public void RememberAttack(ITarget target, float now) => _lastAttack[target] = now;

    /// <summary>State changes, and those straight back to the one before within half a second: flicker (tests: there should be few).</summary>
    public int StateChanges, StateFlips;
    CState _before = CState.Wander;

    public void SetState(CState state)
    {
        if (State == state) return;
        StateChanges++;
        if (state == _before && StateTime < 0.5f) StateFlips++;
        _before = State;
        State = state;
        StateTime = 0f;
        HasGoal = false;
    }

    public override string ToString() => $"#{Id} {Species.Name} {State}{(Dead ? " dead" : "")}";
}

/// <summary>A herd (or flock, or pack) moving as one: a shared goal, a shared alarm, a shared hunt.</summary>
public sealed class Herd
{
    public readonly List<Creature> Members = new();
    public Vector3 Home, Goal, Center, Velocity;
    public float GoalTimer;
    public bool Alarmed;
    public float AlarmedAt;
    public Vector3 ThreatPos;
    public ITarget Hunting;
    public int UpdatedFrame = -1; // space shoals
    public float UpdatedAt = -999f;
    public float Spread;

    public int AliveCount
    {
        get
        {
            int n = 0;
            foreach (var m in Members) if (m.Alive) n++;
            return n;
        }
    }

    public void Alarm(Vector3 threat, float now)
    {
        Alarmed = true;
        AlarmedAt = now;
        ThreatPos = threat;
    }
}

public enum PackPhase { Roam, Stalk, Surround, Engage, Retreat }

/// <summary>
/// A hunting pack: 3 to 6 predators that roam behind a leader and share one prey. They stalk
/// it together, spread round it, and while one feints from the front the others close from
/// the flanks. Their lunges are scheduled through the pack, so no two commit closer together
/// than the telegraph: you are never hit by several at once without warning. They break off
/// together if the leader falls or too many are hurt, and regroup later.
/// </summary>
public sealed class Pack
{
    public readonly List<Creature> Members = new();
    public Creature Leader;
    public PackPhase Phase = PackPhase.Roam;
    public float PhaseTime;
    public ITarget Prey;
    public Vector3 Home, Center;
    /// <summary>No new hunt before this time: a beaten pack regroups first.</summary>
    public float RestUntil = -999f;
    public Vector3 RetreatGoal;

    /// <summary>The side of the prey the pack came from (a yaw angle round it); the feinter holds it.</summary>
    public float FrontAngle;
    public Creature Feinter;
    /// <summary>Each member's place round the prey, as an angle offset from the front.</summary>
    public readonly Dictionary<Creature, float> Slots = new();

    /// <summary>The earliest moment the next member may commit to a lunge.</summary>
    public float NextCommitAt = -999f;
    public float UpdatedAt = -999f;

    public void SetPhase(PackPhase phase)
    {
        if (Phase == phase) return;
        Phase = phase;
        PhaseTime = 0f;
    }

    public int AliveCount
    {
        get
        {
            int n = 0;
            foreach (var m in Members) if (m.Alive && !m.Removed) n++;
            return n;
        }
    }

    /// <summary>Is any member winding up or mid-lunge? Only one at a time.</summary>
    public bool Busy(Creature except)
    {
        foreach (Creature m in Members)
            if (m != except && m.Alive && m.Phase is AttackPhase.Windup or AttackPhase.Lunge) return true;
        return false;
    }
}
