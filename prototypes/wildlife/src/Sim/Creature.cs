using System.Collections.Generic;
using Godot;

namespace Wildlife;

public enum CState { Idle, Wander, Graze, Flee, Stalk, Chase, Hidden, Lurk, Warn, ReturnHome, Scavenge, Eat, Follow, Freeze, Root }

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
    public Vector3 Goal;
    public bool HasGoal;
    public ITarget Target;
    /// <summary>The horizontal velocity the brain asks for.</summary>
    public Vector3 Desired;
    public Vector3? FacePoint;

    // Attacks. The first attack on each target is telegraphed with the species' long wind-up.
    public AttackPhase Phase;
    public float PhaseTime, PhaseLength, WindupLength;
    public bool FirstAttack, Committed;
    public ITarget AttackTarget;
    public Vector3 AimPoint, LungeFrom, LungeTo;
    readonly Dictionary<ITarget, float> _lastAttack = new();

    // Pose intentions, 0..1, smoothed by the rig.
    public float WantCrouch, WantHidden, WantGraze, Rear, Lunge, Alert;
    public Vector3? LookAt;

    // Fliers and floaters: target height above ground. 0 means land.
    public float Altitude;
    public bool Landed;

    // Hoppers.
    public HopPhase Hop;
    public float HopTime, HopCooldown;

    // Swarms: how many members are still flying, from health.
    public int SwarmAlive;

    // Paths (only when the straight line is blocked).
    public readonly List<Vector3> Path = new();
    public int PathIndex;
    public Vector3 PathGoal;
    public float PathAge;

    /// <summary>Rig update interval in frames, set by the view by distance (1 near, more far away).</summary>
    public int RigInterval = 1;
    public float RigPending;

    public Stats Stats => Species.Stats;
    public float Radius => Species.Stats.Radius;
    public bool Alive => !Dead;
    public Vector3 Forward => new(-Mathf.Sin(Yaw), 0f, -Mathf.Cos(Yaw));
    public bool Attacking => Phase != AttackPhase.None;

    /// <summary>Whether its next attack on this target must be the telegraphed first one.</summary>
    public bool MustTelegraph(ITarget target, float now, CombatRules rules) =>
        !_lastAttack.TryGetValue(target, out float last) || now - last > rules.TelegraphMemorySeconds;

    public void RememberAttack(ITarget target, float now) => _lastAttack[target] = now;

    public void SetState(CState state)
    {
        if (State == state) return;
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
    public int UpdatedFrame = -1;
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
