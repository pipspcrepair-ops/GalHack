using System;
using System.Collections.Generic;
using Godot;

namespace Boarding;

public enum ShooterClass { Infantry, SmallCraft, Capital }

public enum TargetClass { OnFoot, Hover, SmallVessel, Capital }

/// <summary>A weapon type's effect on hostile fire (data/hostile_fire.txt, [weapon ...]).</summary>
public sealed class FireProfile
{
    public string Name = "";
    public float Spread = 1f;
    public float Rate = 1f;
    public float Speed = 400f;
    public float Tracking;
    public float[] Vs = { 1f, 1f, 1f, 1f };
}

/// <summary>
/// The hostile-fire table: accuracy ratings, the shooter × target baseline, situational
/// multipliers, weapon types and the warning/ramp timings. All of it is data.
/// </summary>
public sealed class HostileFire
{
    public const string DefaultPath = "res://data/hostile_fire.txt";

    public int WarningShots = 3;
    public float WarningSeconds = 0.75f;
    public float WarningMissMin = 1f;
    public float WarningMissMax = 2.4f;
    public float RampSeconds = 2f;
    public float RampStart = 3f;
    public float LosResetSeconds = 0.4f;

    public float TargetSprinting = 1.4f;
    public float TargetCrouched = 1.15f;
    public float LightsOut = 1.4f;

    public readonly Dictionary<string, float> Ratings = new();
    public readonly Dictionary<ShooterClass, string[]> Baseline = new();
    public readonly Dictionary<string, FireProfile> Weapons = new();

    public static HostileFire Load(string path = DefaultPath)
    {
        var data = DataFile.Load(path);
        var fire = new HostileFire();
        var ramp = data.Section("ramp");
        fire.WarningShots = int.Parse(ramp["warning_shots"]);
        fire.WarningSeconds = DataFile.Float(ramp["warning_seconds"]);
        fire.WarningMissMin = DataFile.Float(ramp["warning_miss_min"]);
        fire.WarningMissMax = DataFile.Float(ramp["warning_miss_max"]);
        fire.RampSeconds = DataFile.Float(ramp["ramp_seconds"]);
        fire.RampStart = DataFile.Float(ramp["ramp_start"]);
        fire.LosResetSeconds = DataFile.Float(ramp["los_reset_seconds"]);

        var conditions = data.Section("conditions");
        fire.TargetSprinting = DataFile.Float(conditions["target_sprinting"]);
        fire.TargetCrouched = DataFile.Float(conditions["target_crouched"]);
        fire.LightsOut = DataFile.Float(conditions["lights_out"]);

        foreach (var (rating, degrees) in data.Section("ratings")) fire.Ratings[rating] = DataFile.Float(degrees);
        foreach (var (shooter, row) in data.Section("baseline"))
        {
            ShooterClass cls = shooter switch
            {
                "infantry" => ShooterClass.Infantry,
                "small_craft" => ShooterClass.SmallCraft,
                "capital" => ShooterClass.Capital,
                _ => throw new FormatException($"Unknown shooter class `{shooter}`"),
            };
            string[] ratings = DataFile.List(row);
            if (ratings.Length != 4) throw new FormatException($"Baseline `{shooter}` needs 4 ratings");
            foreach (string r in ratings)
                if (!fire.Ratings.ContainsKey(r)) throw new FormatException($"Unknown accuracy rating `{r}`");
            fire.Baseline[cls] = ratings;
        }
        foreach (string section in data.SectionOrder)
        {
            if (!section.StartsWith("weapon ")) continue;
            var profile = new FireProfile { Name = section["weapon ".Length..].Trim() };
            DataFile.ApplyTo(profile, data.Section(section));
            fire.Weapons[profile.Name] = profile;
        }
        return fire;
    }

    public FireProfile Weapon(string name) =>
        Weapons.TryGetValue(name, out var w) ? w : throw new KeyNotFoundException($"No hostile weapon type `{name}`");

    /// <summary>The settled spread (degrees) for this shooter, target and weapon.</summary>
    public float BaselineSpread(ShooterClass shooter, TargetClass target, FireProfile weapon) =>
        Ratings[Baseline[shooter][(int)target]] * weapon.Spread * weapon.Vs[(int)target];
}

/// <summary>
/// One shooter's aim at its current target. An engagement starts the moment it has line of
/// sight: the first shots (and everything in the first fraction of a second) are warning
/// shots that are guaranteed to miss; then the cone tightens from wide to the baseline.
/// Losing sight for a moment ends the engagement, so the next one starts with warnings again.
/// </summary>
public sealed class AimRamp
{
    readonly HostileFire _fire;
    float _unseen;
    float _warningEndedAt = -1f;

    public bool Engaged { get; private set; }
    public int Shots { get; private set; }
    public float Seconds { get; private set; }

    public AimRamp(HostileFire fire) => _fire = fire;

    /// <summary>True while shots are warnings: guaranteed, visible misses.</summary>
    public bool Warning => !Engaged || Shots < _fire.WarningShots || Seconds < _fire.WarningSeconds;

    /// <summary>Spread multiplier over the baseline: RampStart right after the warnings, 1 once settled.</summary>
    public float Multiplier
    {
        get
        {
            if (Warning) return _fire.RampStart;
            float since = Seconds - WarningEndTime;
            return Mathf.Lerp(_fire.RampStart, 1f, Mathf.Clamp(since / _fire.RampSeconds, 0f, 1f));
        }
    }

    /// <summary>When (engagement seconds) the warning shots ended.</summary>
    public float WarningEndTime => _warningEndedAt >= 0f ? _warningEndedAt : Mathf.Max(_fire.WarningSeconds, Seconds);

    /// <summary>Call every tick with whether the shooter can see its target.</summary>
    public void Observe(float dt, bool lineOfSight)
    {
        if (lineOfSight)
        {
            if (!Engaged) Begin();
            _unseen = 0f;
            Seconds += dt;
            NoteWarningEnd();
            return;
        }
        if (!Engaged) return;
        _unseen += dt;
        if (_unseen >= _fire.LosResetSeconds) Reset();
    }

    /// <summary>Records a shot. Call after aiming it.</summary>
    public void Shot()
    {
        if (!Engaged) Begin();
        Shots++;
        NoteWarningEnd();
    }

    public void Reset()
    {
        Engaged = false;
        Shots = 0;
        Seconds = 0f;
        _unseen = 0f;
        _warningEndedAt = -1f;
    }

    void Begin()
    {
        Reset();
        Engaged = true;
    }

    void NoteWarningEnd()
    {
        if (_warningEndedAt < 0f && !Warning) _warningEndedAt = Seconds;
    }

    /// <summary>
    /// Where to aim the next shot. Warnings aim at a point off to the side of the target, at
    /// least WarningMissMin outside its outline; real shots sample the current cone.
    /// </summary>
    public Vector3 AimPoint(Vector3 muzzle, Vector3 targetCenter, float targetRadius, float baselineDegrees, Random rng, out bool warning)
    {
        warning = Warning;
        Vector3 dir = (targetCenter - muzzle).Normalized();
        Vector3 side = dir.Cross(Vector3.Up);
        if (side.LengthSquared() < 1e-4f) side = dir.Cross(Vector3.Right);
        side = side.Normalized();
        Vector3 up = side.Cross(dir).Normalized();

        if (warning)
        {
            // Mostly beside and above: a miss you see go past, not one buried at your feet.
            float a = Mathf.Lerp(-0.35f, Mathf.Pi + 0.35f, (float)rng.NextDouble());
            float offset = targetRadius + Mathf.Lerp(_fire.WarningMissMin, _fire.WarningMissMax, (float)rng.NextDouble());
            return targetCenter + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * offset;
        }

        float distance = muzzle.DistanceTo(targetCenter);
        float half = Mathf.DegToRad(baselineDegrees * Multiplier) * 0.5f;
        float r = Mathf.Tan(half) * Mathf.Sqrt((float)rng.NextDouble());
        float t = (float)rng.NextDouble() * Mathf.Tau;
        return muzzle + (dir + (side * Mathf.Cos(t) + up * Mathf.Sin(t)) * r).Normalized() * distance;
    }
}
