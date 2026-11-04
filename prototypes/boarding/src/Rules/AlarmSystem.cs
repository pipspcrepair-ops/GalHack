using System;
using Godot;

namespace Boarding;

public enum AlarmLevel { Unaware = 0, Alert = 1, Lockdown = 2, LastStand = 3 }

/// <summary>
/// The ship's alarm, as rules only. Things report what happened (a sighting, a loud
/// breach, combat time, hijack stages, hull damage) and the level only ever climbs, except
/// that an Alert with no new reports calms back down to Unaware. Reaching Last Stand arms
/// the self-destruct; capturing the ship or killing its core cancels it.
///
/// Positions are ship-local, so the alarm keeps working if the ship moves.
/// </summary>
public sealed class AlarmSystem
{
    readonly Tunables _t;

    public AlarmLevel Level { get; private set; } = AlarmLevel.Unaware;
    public Vector3 LastKnownPosition { get; private set; }
    public bool HasLastKnown { get; private set; }
    public float CombatSeconds { get; private set; }
    public SelfDestruct SelfDestruct { get; }

    /// <summary>The ship is no longer the Vharn's (hijacked or derelict): nothing escalates.</summary>
    public bool StoodDown { get; private set; }

    /// <summary>(old, new) whenever the level changes.</summary>
    public event Action<AlarmLevel, AlarmLevel> LevelChanged;

    /// <summary>A new last known position: guards converge on it.</summary>
    public event Action<Vector3> Converge;

    float _quiet;

    public AlarmSystem(Tunables tunables)
    {
        _t = tunables;
        SelfDestruct = new SelfDestruct();
    }

    /// <summary>A guard saw the player at this ship-local position.</summary>
    public void ReportSighting(Vector3 local) => Report(local, AlarmLevel.Alert);

    /// <summary>Something loud happened (a breaching charge, gunfire a guard heard).</summary>
    public void ReportLoudNoise(Vector3 local) => Report(local, AlarmLevel.Alert);

    /// <summary>Called every tick there's a firefight aboard.</summary>
    public void ReportCombat(float dt)
    {
        if (StoodDown) return;
        CombatSeconds += dt;
        _quiet = 0f;
        if (CombatSeconds >= _t.AlarmCombatSeconds) Raise(AlarmLevel.Lockdown);
    }

    public void ReportHijackStarted() => Raise(AlarmLevel.Lockdown);

    public void ReportHijackStage(int completedStages)
    {
        if (completedStages >= _t.LastStandHijackStage) Raise(AlarmLevel.LastStand);
    }

    public void ReportHull(float integrity)
    {
        if (integrity < _t.LastStandHullFraction) Raise(AlarmLevel.LastStand);
    }

    /// <summary>The hijack finished or the core died: cancel the countdown, stop escalating.</summary>
    public void StandDown()
    {
        StoodDown = true;
        SelfDestruct.Cancel();
    }

    public void Tick(float dt)
    {
        _quiet += dt;
        if (Level == AlarmLevel.Alert && _quiet >= _t.AlarmAlertCooldown) Set(AlarmLevel.Unaware);
        SelfDestruct.Tick(dt);
    }

    void Report(Vector3 local, AlarmLevel atLeast)
    {
        if (StoodDown) return;
        _quiet = 0f;
        LastKnownPosition = local;
        HasLastKnown = true;
        Raise(atLeast);
        Converge?.Invoke(local);
    }

    void Raise(AlarmLevel level)
    {
        if (StoodDown || level <= Level) return;
        Set(level);
        if (level == AlarmLevel.LastStand) SelfDestruct.Start(_t.SelfDestructSeconds);
    }

    void Set(AlarmLevel level)
    {
        AlarmLevel old = Level;
        Level = level;
        LevelChanged?.Invoke(old, level);
    }
}

/// <summary>
/// The Last Stand countdown. It is shown from the moment it starts, long before it ends,
/// so the player always has time to leave.
/// </summary>
public sealed class SelfDestruct
{
    public bool Active { get; private set; }
    public bool Cancelled { get; private set; }
    public bool Exploded { get; private set; }
    public float Remaining { get; private set; }

    public event Action Detonated;

    public void Start(float seconds)
    {
        if (Active || Exploded || Cancelled) return;
        Active = true;
        Remaining = seconds;
    }

    public void Cancel()
    {
        if (Exploded) return;
        if (Active) Cancelled = true;
        Active = false;
    }

    public void Tick(float dt)
    {
        if (!Active) return;
        Remaining = Mathf.Max(0f, Remaining - dt);
        if (Remaining > 0f) return;
        Active = false;
        Exploded = true;
        Detonated?.Invoke();
    }
}
