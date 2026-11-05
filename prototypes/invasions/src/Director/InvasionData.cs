using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Invasions.Director;

// The shapes of data/server.json, data/invasions.json and data/factions.json. Defaults here are only
// fallbacks; the JSON files are the source of truth for every tunable.

public sealed class ServerSettings
{
    public double InvasionIntervalMinutes { get; set; } = 45;
    public double IntervalJitter { get; set; } = 0.25;
    public bool ShipRaids { get; set; } = true;
    /// <summary>The host's on/off for invasions; the world's own setting (<see cref="WorldSettings"/>) overrides it.</summary>
    public bool InvasionsEnabled { get; set; } = true;
    public double FastTimeScale { get; set; } = 60;
    public ulong Seed { get; set; } = 1977;

    public static ServerSettings FromJson(string json) => JsonSerializer.Deserialize<ServerSettings>(json, Json.Options)!;
    public ServerSettings Clone() => (ServerSettings)MemberwiseClone();
}

/// <summary>
/// Settings that belong to one world and are saved with it, as opposed to the server's defaults. Null
/// means "not set here": the server's value applies.
/// </summary>
public sealed class WorldSettings
{
    public bool? InvasionsEnabled { get; set; }

    public static WorldSettings FromJson(string json) =>
        string.IsNullOrWhiteSpace(json) ? new WorldSettings() : JsonSerializer.Deserialize<WorldSettings>(json, Json.Options)!;

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    /// <summary>The server's settings with this world's own on top.</summary>
    public ServerSettings Apply(ServerSettings server)
    {
        ServerSettings s = server.Clone();
        if (InvasionsEnabled is bool on) s.InvasionsEnabled = on;
        return s;
    }
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
}

/// <summary>How big a force a kind of source fields. What it rides in is up to the faction.</summary>
public sealed class SourceKindData
{
    public string Label { get; set; } = "";
    public double ForceMultiplier { get; set; } = 1;
    public int MaxTroops { get; set; } = 40;
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
    public TimelineData Timeline { get; set; } = new();
    public PresenceData Presence { get; set; } = new();
    public SourceRules Sources { get; set; } = new();
    public Dictionary<string, SourceKindData> SourceKinds { get; set; } = new();
    public ScalingData Scaling { get; set; } = new();
    public List<TierData> Tiers { get; set; } = new();
    public ApproachData Approach { get; set; } = new();
    public PlannerData Planner { get; set; } = new();
    public Dictionary<string, string> Messages { get; set; } = new();

    /// <summary>Who invades and how: data/factions.json, attached by <see cref="Load"/>.</summary>
    [JsonIgnore] public FactionsData Factions { get; set; } = new();

    /// <summary>Warnings in the order they fire: furthest from arrival first.</summary>
    public IReadOnlyList<WarningStep> OrderedWarnings =>
        _ordered ??= Timeline.Warnings.OrderByDescending(w => w.SecondsBeforeArrival).ToList();
    List<WarningStep> _ordered;

    public SourceKindData SourceKind(SourceKind kind) =>
        SourceKinds.TryGetValue(Key(kind), out var data)
            ? data
            : throw new InvalidOperationException($"invasions.json has no sourceKinds entry for {kind}");

    public static string Key(SourceKind kind) => kind.ToString().ToLowerInvariant();

    public FactionData Faction(string id) => Factions.Get(id);

    /// <summary>A message template: the faction's own wording if it has one, else the shared one.</summary>
    public string Message(string key, FactionData faction = null) =>
        faction != null && faction.Messages.TryGetValue(key, out string own) ? own
        : Messages.TryGetValue(key, out string text) ? text : key;

    /// <summary>A warning's text, in the invading faction's words if it has its own.</summary>
    public string WarningMessage(WarningStep step, FactionData faction) =>
        faction != null && faction.Messages.TryGetValue($"warning{step.Level}", out string own) ? own : step.Message;

    public static InvasionData FromJson(string json) => JsonSerializer.Deserialize<InvasionData>(json, Json.Options)!;

    /// <summary>Reads invasions.json and factions.json together, and checks the factions make sense.</summary>
    public static InvasionData Load(string invasionsJson, string factionsJson)
    {
        InvasionData data = FromJson(invasionsJson);
        data.Factions = FactionsData.FromJson(factionsJson);
        data.Factions.Validate(data);
        return data;
    }
}

// ---- Factions ---------------------------------------------------------------------------------

/// <summary>Standing runs from Min to Max; at or below HostileAt a faction is hostile.</summary>
public sealed class StandingRules
{
    public double Min { get; set; } = -100;
    public double Max { get; set; } = 100;
    public double HostileAt { get; set; } = -40;
    public double FriendlyAt { get; set; } = 40;
}

/// <summary>Where a faction's standing starts, and the floor and ceiling it can move between.</summary>
public sealed class StandingRange
{
    public double Start { get; set; }
    public double Floor { get; set; } = -100;
    public double Ceiling { get; set; } = 100;
}

/// <summary>A crewed carrier: a driver or pilot plus a gunner per gun. Body picks its model and how it moves.</summary>
public sealed class VehicleTypeData
{
    public string Label { get; set; } = "";
    public string Body { get; set; } = "apc";
    public int Capacity { get; set; } = 6;
    /// <summary>One weapon (from fire_control.json) per gunner.</summary>
    public List<string> Guns { get; set; } = new();
    public int Gunners => Guns.Count;
    public bool Flies { get; set; }
    public double Speed { get; set; } = 15;
    /// <summary>How close to the base it sets troops down; null for the approach default.</summary>
    public double? DropDistance { get; set; }
    /// <summary>Seconds between troops climbing out; null for the default.</summary>
    public double? UnloadSeconds { get; set; }
}

/// <summary>A kind of trooper: weapon, reach, pace, toughness, and how (if at all) they break walls.</summary>
public sealed class RoleData
{
    /// <summary>Fraction of the force drawn in this role. 0: never drawn (crew who bail out, say).</summary>
    public double Share { get; set; }
    public string Weapon { get; set; } = "rifle";
    public double Range { get; set; } = 70;
    public double Speed { get; set; } = 5;
    public double Hp { get; set; } = 100;
    /// <summary>A personal energy shield that soaks hits before health and recharges.</summary>
    public double Shield { get; set; }
    /// <summary>"" (shoots the wall like anyone), "charge", "cut" or "burst". Breaching roles walk right up to the block.</summary>
    public string Breach { get; set; } = "";
    /// <summary>Per charge or burst, or per second for a cut.</summary>
    public double BreachDamage { get; set; }
    /// <summary>The model to build; the role's own name if empty.</summary>
    public string Look { get; set; } = "";
}

public sealed class TacticsData
{
    public string Summary { get; set; } = "";
    /// <summary>Overrides the planner's wall cost: higher means walls are cheaper to go through.</summary>
    public double? HpPerCellOfDetour { get; set; }
    /// <summary>"core", or "storage": smash the storage containers first, then the core.</summary>
    public string Targets { get; set; } = "core";
    /// <summary>Non-breachers at a breach hold fire on the wall this long, waiting for the charges.</summary>
    public double StackUpSeconds { get; set; }
    /// <summary>Grab and run: seconds after the first trooper lands that they run (0: fight to the end).</summary>
    public double RaidSeconds { get; set; }
    /// <summary>More (or fewer) bodies than the base alone would draw: a swarm is over 1.</summary>
    public double TroopMultiplier { get; set; } = 1;
    public double ShieldRechargeDelay { get; set; } = 3;
    public double ShieldRechargePerSecond { get; set; } = 30;
}

public sealed class FactionData
{
    /// <summary>The design id (vharn, kessari, ...): the key in factions.json.</summary>
    [JsonIgnore] public string Id { get; internal set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Short upper-case tag for the scan and consoles.</summary>
    public string Tag { get; set; } = "";
    /// <summary>Whether it ever sends invasions. Those that do, do so only while hostile.</summary>
    public bool Invades { get; set; }
    public StandingRange Standing { get; set; } = new();
    /// <summary>How much an accidental wrong counts. Deliberate wrongs always count in full.</summary>
    public double AccidentWeight { get; set; } = 1;
    /// <summary>Its share of ship raids when no hostile base is in range. 0: it never comes by ship.</summary>
    public double ShipRaidWeight { get; set; }
    public List<string> ShipNames { get; set; } = new();
    /// <summary>Its names for the force sizes, in the order of invasions.json's tiers.</summary>
    public List<string> TierNames { get; set; } = new();
    /// <summary>Its words for its base kinds ("infestation" for a Hollow Bloom garrison).</summary>
    public Dictionary<string, string> BaseLabels { get; set; } = new();
    /// <summary>Vehicle mix (weights) per source kind.</summary>
    public Dictionary<string, Dictionary<string, double>> Carriers { get; set; } = new();
    public Dictionary<string, VehicleTypeData> Vehicles { get; set; } = new();
    public Dictionary<string, RoleData> Roles { get; set; } = new();
    public string BreacherRole { get; set; } = "";
    public string CrewRole { get; set; } = "";
    public int MinBreachersPerVehicle { get; set; } = 1;
    /// <summary>Sideways spacing of its vehicles on the approach; null for the approach default.</summary>
    public double? VehicleSpacing { get; set; }
    public TacticsData Tactics { get; set; } = new();
    /// <summary>Tracer and scan colour, as hex.</summary>
    public string Tracer { get; set; } = "ffffff";
    public Dictionary<string, string> Messages { get; set; } = new();

    public bool CanRaidByShip => Invades && ShipRaidWeight > 0 && Carriers.ContainsKey("ship");

    public RoleData Role(string role) =>
        Roles.TryGetValue(role, out RoleData data) ? data : throw new InvalidOperationException($"factions.json: {Id} has no role '{role}'");

    /// <summary>The faction's word for a size of force.</summary>
    public string TierName(int tierIndex, string fallback) =>
        tierIndex >= 0 && tierIndex < TierNames.Count ? TierNames[tierIndex] : fallback;

    public string BaseLabel(SourceKind kind, InvasionData data) =>
        BaseLabels.TryGetValue(InvasionData.Key(kind), out string label) ? label : data.SourceKind(kind).Label;

    public override string ToString() => Id;
}

public sealed class FactionsData
{
    public StandingRules Standing { get; set; } = new();
    public Dictionary<string, FactionData> Factions { get; set; } = new();

    /// <summary>Every faction, in id order, so anything that walks them replays the same way.</summary>
    public IReadOnlyList<FactionData> All => _all ??= Factions.Values.OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
    List<FactionData> _all;

    public FactionData Get(string id) =>
        id != null && Factions.TryGetValue(id, out FactionData f) ? f : throw new InvalidOperationException($"factions.json has no faction '{id}'");

    public bool Has(string id) => id != null && Factions.ContainsKey(id);

    public static FactionsData FromJson(string json)
    {
        FactionsData data = JsonSerializer.Deserialize<FactionsData>(json, Json.Options)!;
        foreach (var (id, faction) in data.Factions) faction.Id = id;
        return data;
    }

    /// <summary>
    /// Refuses data that would break an invasion: an invading faction needs carriers for every base kind
    /// (and for ships if it raids by ship), vehicles and roles that exist, a breacher and a crew role.
    /// </summary>
    public void Validate(InvasionData data)
    {
        foreach (FactionData f in All)
        {
            string Where(string what) => $"factions.json: {f.Id} {what}";
            if (f.Standing.Floor > f.Standing.Ceiling) throw new InvalidOperationException(Where("has a standing floor above its ceiling"));
            if (!f.Invades) continue;
            var kinds = new List<string> { "outpost", "garrison", "fortress" };
            if (f.ShipRaidWeight > 0) kinds.Add("ship");
            foreach (string kind in kinds)
            {
                if (!f.Carriers.TryGetValue(kind, out var mix) || mix.Count == 0 || mix.Values.All(w => w <= 0))
                    throw new InvalidOperationException(Where($"has no carriers for {kind}"));
                foreach (string v in mix.Keys)
                    if (!f.Vehicles.ContainsKey(v)) throw new InvalidOperationException(Where($"carries troops in an unknown vehicle '{v}'"));
            }
            if (f.Vehicles.Values.Any(v => v.Capacity < 1)) throw new InvalidOperationException(Where("has a vehicle with no seats"));
            if (!f.Roles.Values.Any(r => r.Share > 0)) throw new InvalidOperationException(Where("draws no troops"));
            if (!f.Roles.ContainsKey(f.BreacherRole)) throw new InvalidOperationException(Where($"has no breacher role '{f.BreacherRole}'"));
            if (!f.Roles.ContainsKey(f.CrewRole)) throw new InvalidOperationException(Where($"has no crew role '{f.CrewRole}'"));
            foreach (var (name, role) in f.Roles)
                if (role.Breach is not ("" or "charge" or "cut" or "burst")) throw new InvalidOperationException(Where($"role {name} has an unknown breach '{role.Breach}'"));
            if (f.Tactics.Targets is not ("core" or "storage")) throw new InvalidOperationException(Where($"targets '{f.Tactics.Targets}'"));
        }
    }

    /// <summary>Every weapon the factions name, for checking against fire_control.json.</summary>
    public IEnumerable<string> Weapons() =>
        All.SelectMany(f => f.Roles.Values.Select(r => r.Weapon).Concat(f.Vehicles.Values.SelectMany(v => v.Guns))).Distinct();
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
