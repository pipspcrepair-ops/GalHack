using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Invasions.Director;

// The shapes of data/server.json and data/invasions.json. Defaults here are only fallbacks;
// the JSON files are the source of truth for every tunable.

public sealed class ServerSettings
{
    public double InvasionIntervalMinutes { get; set; } = 45;
    public double IntervalJitter { get; set; } = 0.25;
    public bool ShipRaids { get; set; } = true;
    public double FastTimeScale { get; set; } = 60;
    public ulong Seed { get; set; } = 1977;

    public static ServerSettings FromJson(string json) => JsonSerializer.Deserialize<ServerSettings>(json, Json.Options)!;
    public ServerSettings Clone() => (ServerSettings)MemberwiseClone();
}

public sealed class WarningStep
{
    public int Level { get; set; }
    public double SecondsBeforeArrival { get; set; }
    public bool LongRangeScan { get; set; }
    public bool Siren { get; set; }
    public string Message { get; set; } = "";
}

public sealed class TimelineData
{
    public List<WarningStep> Warnings { get; set; } = new();

    /// <summary>How long before arrival an invasion is announced: the earliest warning.</summary>
    public double LeadSeconds => Warnings.Count == 0 ? 0 : Warnings.Max(w => w.SecondsBeforeArrival);
}

public sealed class PresenceData
{
    public double AbandonGraceSeconds { get; set; } = 60;
}

public sealed class SourceRules
{
    public double NearbyRange { get; set; } = 1500;
    public double ShipRaidIntervalMultiplier { get; set; } = 1.5;
    public double ShipScanDistance { get; set; } = 6000;
    public List<string> ShipNames { get; set; } = new() { "a Dominion ship" };
}

public sealed class SourceKindData
{
    public string Label { get; set; } = "";
    public double ForceMultiplier { get; set; } = 1;
    public int MaxTroops { get; set; } = 40;
    public Dictionary<string, double> Vehicles { get; set; } = new();
}

public sealed class ScalingData
{
    public double StoredValuePerPoint { get; set; } = 400;
    public double BlocksPerPoint { get; set; } = 20;
    public double PointsPerTurret { get; set; } = 2;
    public double BaseTroops { get; set; } = 4;
    public double TroopsPerPoint { get; set; } = 0.45;
    public List<double> PlayerMultipliers { get; set; } = new() { 1.0 };
    public double ExtraPlayerMultiplier { get; set; } = 0.3;
    public int MinTroops { get; set; } = 4;
    public int MaxTroops { get; set; } = 160;
}

public sealed class TierData
{
    public string Name { get; set; } = "";
    public int MinTroops { get; set; }
}

public sealed class VehicleTypeData
{
    public string Label { get; set; } = "";
    public int Capacity { get; set; } = 6;
    /// <summary>One weapon (from fire_control.json) per gunner.</summary>
    public List<string> Guns { get; set; } = new();
    public int Gunners => Guns.Count;
    public bool Flies { get; set; }
    public double Speed { get; set; } = 15;
}

public sealed class ApproachData
{
    public double DeployDistance { get; set; } = 650;
    public double DropDistance { get; set; } = 210;
    public double VehicleSpacing { get; set; } = 28;
    public double MaxStaggerSeconds { get; set; } = 4;
    public double GoneDistance { get; set; } = 520;
}

public sealed class PlannerData
{
    public double HpPerCellOfDetour { get; set; } = 30;
}

public sealed class InvasionData
{
    public string Attacker { get; set; } = "Vharn Dominion";
    public TimelineData Timeline { get; set; } = new();
    public PresenceData Presence { get; set; } = new();
    public SourceRules Sources { get; set; } = new();
    public Dictionary<string, SourceKindData> SourceKinds { get; set; } = new();
    public ScalingData Scaling { get; set; } = new();
    public List<TierData> Tiers { get; set; } = new();
    public Dictionary<string, VehicleTypeData> Vehicles { get; set; } = new();
    public Dictionary<string, double> TroopRoles { get; set; } = new();
    public int MinBreachersPerVehicle { get; set; } = 1;
    public ApproachData Approach { get; set; } = new();
    public PlannerData Planner { get; set; } = new();
    public Dictionary<string, string> Messages { get; set; } = new();

    /// <summary>Warnings in the order they fire: furthest from arrival first.</summary>
    public IReadOnlyList<WarningStep> OrderedWarnings =>
        _ordered ??= Timeline.Warnings.OrderByDescending(w => w.SecondsBeforeArrival).ToList();
    List<WarningStep> _ordered;

    public SourceKindData SourceKind(SourceKind kind) =>
        SourceKinds.TryGetValue(kind.ToString().ToLowerInvariant(), out var data)
            ? data
            : throw new InvalidOperationException($"invasions.json has no sourceKinds entry for {kind}");

    public string Message(string key) => Messages.TryGetValue(key, out var text) ? text : key;

    public static InvasionData FromJson(string json) => JsonSerializer.Deserialize<InvasionData>(json, Json.Options)!;
}

public static class Json
{
    /// <summary>Lenient enough for hand-edited data files: comments, trailing commas, any key case.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Dictionary entries sorted by key, so weighted picks never depend on hash order.</summary>
    public static List<KeyValuePair<string, double>> Sorted(Dictionary<string, double> weights) =>
        weights.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
}
