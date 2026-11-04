using System;
using System.Collections.Generic;

namespace Encounters.Director;

public enum ShooterClass { Infantry, VesselGun, CapitalTurret }

public enum TargetClass { OnFoot, Hover, SmallVessel, Capital }

/// <summary>
/// How well NPC guns shoot at players, as data (data/gunnery.json). A hook only: no encounter
/// in this prototype shoots at a player, so nothing calls it yet. When combat lands, a gun asks
/// <see cref="HitChance"/> before each shot and <see cref="IsWarningShot"/> to make the first
/// ones visibly wide.
/// </summary>
public sealed class GunneryConfig
{
    public WarningShotConfig WarningShots { get; set; } = new();
    public float RampSeconds { get; set; } = 2f;
    public Dictionary<string, float> Grades { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Baseline { get; set; } = new();
    public Dictionary<string, WeaponConfig> Weapons { get; set; } = new();
    public float BigSlowSpeed { get; set; } = 40f;
    public float BigSlowPenalty { get; set; } = 0.2f;

    public static GunneryConfig Parse(string json)
    {
        var config = Json.Parse<GunneryConfig>(json, "gunnery");
        foreach (ShooterClass shooter in Enum.GetValues<ShooterClass>())
            foreach (TargetClass target in Enum.GetValues<TargetClass>())
                config.BaselineFor(shooter, target); // throws if the table has a hole
        return config;
    }

    public float BaselineFor(ShooterClass shooter, TargetClass target)
    {
        if (!Baseline.TryGetValue(Key(shooter), out var row) || !row.TryGetValue(Key(target), out string grade) || !Grades.TryGetValue(grade, out float value))
            throw new FormatException($"gunnery: no baseline for {shooter} against {target}");
        return value;
    }

    static string Key<T>(T value) where T : Enum
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

public sealed class WarningShotConfig
{
    public int Shots { get; set; } = 3;
    public float Seconds { get; set; } = 0.75f;
    public float[] MissOffsetDeg { get; set; } = { 5f, 12f };
}

public sealed class WeaponConfig
{
    public float Accuracy { get; set; } = 1f;
    public float SpreadDeg { get; set; } = 1f;
    /// <summary>m/s; 0 is hitscan. Capital-class guns are slow enough for fast ships to dodge.</summary>
    public float ProjectileSpeed { get; set; } = 600f;
    public float TurnRateDeg { get; set; }
    public bool BigSlowOnly { get; set; }
}

/// <summary>One shooter's aim on one player: how long it has had them, how many shots so far.</summary>
public sealed class AimTrack
{
    public double AcquiredAt = double.NaN;
    public int Shots;

    /// <summary>Losing line of sight starts the warning shots and the ramp over.</summary>
    public void LineOfSightBroken()
    {
        AcquiredAt = double.NaN;
        Shots = 0;
    }
}

public static class Gunnery
{
    /// <summary>True while the shooter is still firing its guaranteed wide warning shots.</summary>
    public static bool IsWarningShot(GunneryConfig cfg, AimTrack track, double now)
    {
        if (double.IsNaN(track.AcquiredAt)) return true;
        return track.Shots < cfg.WarningShots.Shots || now - track.AcquiredAt < cfg.WarningShots.Seconds;
    }

    /// <summary>
    /// The chance this shot hits: zero during warning shots, then ramping from zero to the
    /// shooter-against-target baseline (scaled by the weapon) over the ramp time.
    /// </summary>
    public static float HitChance(GunneryConfig cfg, ShooterClass shooter, TargetClass target, string weapon,
        float targetSpeed, AimTrack track, double now)
    {
        if (double.IsNaN(track.AcquiredAt)) track.AcquiredAt = now;
        bool warning = IsWarningShot(cfg, track, now);
        track.Shots++;
        if (warning) return 0f;

        WeaponConfig w = cfg.Weapons.TryGetValue(weapon, out WeaponConfig found) ? found : new WeaponConfig();
        float baseline = cfg.BaselineFor(shooter, target) * w.Accuracy;
        if (w.BigSlowOnly && (target != TargetClass.Capital || targetSpeed > cfg.BigSlowSpeed)) baseline *= cfg.BigSlowPenalty;

        double rampStart = track.AcquiredAt + cfg.WarningShots.Seconds;
        float ramp = (float)Math.Clamp((now - rampStart) / cfg.RampSeconds, 0.0, 1.0);
        return Math.Clamp(baseline * ramp, 0f, 0.98f);
    }
}
