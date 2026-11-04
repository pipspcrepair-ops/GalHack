using System;
using System.Collections.Generic;
using System.Text.Json;
using Invasions.Director;

namespace Invasions.Model;

/// <summary>What a shot is aimed at, for the accuracy matrix.</summary>
public enum TargetClass { OnFoot, Hover, SmallVessel, Capital }

// The shape of data/fire_control.json.

public sealed class WarningData
{
    public int Shots { get; set; } = 3;
    public double Seconds { get; set; } = 0.75;
    public double MissDistanceMin { get; set; } = 1.2;
    public double MissDistanceMax { get; set; } = 3;
}

public sealed class RampData
{
    public double Seconds { get; set; } = 2;
    public double StartSpreadDegrees { get; set; } = 7;
    public bool RampAgainstNonPlayers { get; set; } = true;
}

public sealed class WeaponData
{
    public string Class { get; set; } = "infantry";
    public double SpreadMultiplier { get; set; } = 1;
    public Dictionary<string, double> TargetSpreadMultipliers { get; set; } = new();
    public double ProjectileSpeed { get; set; } = 400;
    public double Damage { get; set; } = 10;
    public double Interval { get; set; } = 0.5;
    public double TurnRateDegrees { get; set; }
    /// <summary>Spread when a player aims it by hand (the player's rifle, a manned turret).</summary>
    public double AimedSpreadDegrees { get; set; } = 0.3;
}

public sealed class FireControlData
{
    public WarningData Warning { get; set; } = new();
    public RampData Ramp { get; set; } = new();
    public Dictionary<string, double> Grades { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Matrix { get; set; } = new();
    public Dictionary<string, WeaponData> Weapons { get; set; } = new();

    public static FireControlData FromJson(string json) => JsonSerializer.Deserialize<FireControlData>(json, Json.Options)!;

    public WeaponData Weapon(string name) =>
        Weapons.TryGetValue(name, out WeaponData w) ? w : throw new InvalidOperationException($"fire_control.json has no weapon '{name}'");

    public static string Key(TargetClass c) => c switch
    {
        TargetClass.OnFoot => "on_foot",
        TargetClass.Hover => "hover",
        TargetClass.SmallVessel => "small_vessel",
        _ => "capital",
    };

    /// <summary>The weapon's settled spread against a class of target: matrix grade × weapon modifier.</summary>
    public double BaselineSpread(WeaponData weapon, TargetClass target)
    {
        string key = Key(target);
        string grade = Matrix.TryGetValue(weapon.Class, out var row) && row.TryGetValue(key, out string g) ? g : "fair";
        double spread = Grades.TryGetValue(grade, out double degrees) ? degrees : 2;
        double modifier = weapon.TargetSpreadMultipliers.TryGetValue(key, out double m) ? m : weapon.SpreadMultiplier;
        return spread * modifier;
    }
}

/// <summary>How one shot should be fired: a deliberate warning miss, or aimed with a spread cone.</summary>
public readonly record struct ShotSolution(bool Warning, double SpreadDegrees);

/// <summary>
/// One AI gun's aim over an engagement. Plain C#, so the rules are tested without a scene.
/// - Warning shots: against a player, the first shots (at least <c>warning.shots</c> rounds and
///   <c>warning.seconds</c> of firing) are deliberate misses that land close.
/// - Ramp: after that, spread shrinks from the ramp start to the baseline over <c>ramp.seconds</c>.
/// - Losing sight of the target, or switching target, starts a fresh engagement.
/// - Baseline: shooter class × target class from the matrix, times the weapon's modifier.
/// Damage is not this class's business and is never scaled.
/// </summary>
public sealed class FireControl
{
    readonly FireControlData _data;
    public WeaponData Weapon { get; }

    object _target;
    bool _targetIsPlayer;
    double _engagedAt, _warningEndedAt = double.NaN;
    int _shots;

    public FireControl(FireControlData data, WeaponData weapon)
    {
        _data = data;
        Weapon = weapon;
    }

    public object Target => _target;
    public int ShotsThisEngagement => _shots;

    /// <summary>Line of sight to the target was lost: the next shot starts a new engagement.</summary>
    public void LostSight() => _target = null;

    /// <summary>Decides how the next shot at <paramref name="target"/> is fired, at simulated time <paramref name="now"/>.</summary>
    public ShotSolution NextShot(object target, TargetClass targetClass, bool targetIsPlayer, double now)
    {
        if (!ReferenceEquals(target, _target))
        {
            _target = target;
            _targetIsPlayer = targetIsPlayer;
            _engagedAt = now;
            _shots = 0;
            _warningEndedAt = targetIsPlayer ? double.NaN : now;
        }
        _shots++;

        double baseline = _data.BaselineSpread(Weapon, targetClass);
        if (_targetIsPlayer && double.IsNaN(_warningEndedAt))
        {
            bool stillWarning = _shots <= _data.Warning.Shots || now - _engagedAt < _data.Warning.Seconds;
            if (stillWarning) return new ShotSolution(true, 0);
            _warningEndedAt = now;
        }

        if (!_targetIsPlayer && !_data.Ramp.RampAgainstNonPlayers) return new ShotSolution(false, baseline);
        return new ShotSolution(false, SpreadAt(now, baseline));
    }

    /// <summary>The ramped spread: wide when the warning ends, the baseline <c>ramp.seconds</c> later.</summary>
    double SpreadAt(double now, double baseline)
    {
        double t = _data.Ramp.Seconds <= 0 ? 1 : Math.Clamp((now - _warningEndedAt) / _data.Ramp.Seconds, 0, 1);
        double start = Math.Max(_data.Ramp.StartSpreadDegrees, baseline);
        return start + (baseline - start) * t;
    }
}
