using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SurvivalVitals.Model;

/// <summary>
/// Everything survival reads from the data folder: vitals.json, armor.json, food.json and
/// planet.json. The model never hard-codes a rate; tests read the same files the game does.
/// </summary>
public sealed class SurvivalData
{
    public VitalsRules Vitals { get; set; }
    public ArmorCatalog Armor { get; set; }
    public FoodCatalog Food { get; set; }
    public PlanetData Planet { get; set; }

    /// <summary>
    /// Comments and trailing commas are allowed so the data can explain itself. Unknown fields
    /// are an error: a typo in a data file should fail loudly, not silently use a default.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static SurvivalData Parse(string vitals, string armor, string food, string planet) => new()
    {
        Vitals = JsonSerializer.Deserialize<VitalsRules>(vitals, Json),
        Armor = JsonSerializer.Deserialize<ArmorCatalog>(armor, Json),
        Food = JsonSerializer.Deserialize<FoodCatalog>(food, Json),
        Planet = JsonSerializer.Deserialize<PlanetData>(planet, Json),
    };
}

public sealed class Band
{
    public double Low { get; set; }
    public double High { get; set; }
    public bool Contains(double v) => v >= Low && v <= High;
}

// ---- vitals.json --------------------------------------------------------------------------

public sealed class VitalsRules
{
    public HealthRules Health { get; set; } = new();
    public FoodRules Food { get; set; } = new();
    public StaminaRules Stamina { get; set; } = new();
    public OxygenRules Oxygen { get; set; } = new();
    public TemperatureRules Temperature { get; set; } = new();
    public RadiationRules Radiation { get; set; } = new();
    public FallRules Fall { get; set; } = new();
    public LavaRules Lava { get; set; } = new();
    public HealingRules Healing { get; set; } = new();
    public DeathRules Death { get; set; } = new();
    public MovementRules Movement { get; set; } = new();
    public double MinimumWarningSeconds { get; set; }
}

public sealed class HealthRules
{
    public double Max { get; set; }
    public double WarnBelow { get; set; }
}

public sealed class PaceTable
{
    public double Idle { get; set; } = 1;
    public double Walk { get; set; } = 1;
    public double Run { get; set; } = 1;
    public double Sprint { get; set; } = 1;

    public double For(Pace pace) => pace switch
    {
        Pace.Idle => Idle,
        Pace.Walk => Walk,
        Pace.Run => Run,
        _ => Sprint,
    };
}

public sealed class FoodRules
{
    public double Max { get; set; }
    public double DrainPerMinute { get; set; }
    public PaceTable Pace { get; set; } = new();
    public double MiningMultiplier { get; set; } = 1;
    public double WarnBelow { get; set; }
    public double StarvingDamagePerSecond { get; set; }
}

public sealed class StaminaRules
{
    public double Max { get; set; }
    public double RunDrainPerSecond { get; set; }
    public double SprintDrainPerSecond { get; set; }
    public double HeatDrainPerDegree { get; set; }
    public double RestIdlePerSecond { get; set; }
    public double RestWalkPerSecond { get; set; }
    public double RunAgainAt { get; set; }
    public double WarnBelow { get; set; }
}

public sealed class OxygenRules
{
    public double Max { get; set; }
    public double DrainPerSecond { get; set; }
    public double BreathablePercent { get; set; }
    public double RefillPerSecond { get; set; }
    public double SmallBottle { get; set; }
    public double LargeBottle { get; set; }
    public double WarnBelow { get; set; }
    public double HoldBreathSeconds { get; set; }
    public double SuffocationDamagePerSecond { get; set; }
    public bool AutoHelmet { get; set; }
    public bool AutoBottle { get; set; }
}

public sealed class TemperatureRules
{
    public double Normal { get; set; }
    public Band ComfortBand { get; set; } = new();
    public Band WarnBand { get; set; } = new();
    public Band AmbientComfort { get; set; } = new();
    public double BodyDegreesPerDegree { get; set; }
    public double DriftPerSecond { get; set; }
    public double RecoverPerSecond { get; set; }
    public double DamagePerSecond { get; set; }
    public double DamagePerDegree { get; set; }
}

public sealed class RadiationRules
{
    public double WarnAbove { get; set; }
    public double DamageAbove { get; set; }
    public double RisePerSecond { get; set; }
    public double ClearPerSecond { get; set; }
    public double DamagePerSecond { get; set; }
    public double DamagePerRad { get; set; }
}

public sealed class FallRules
{
    public double SafeSpeed { get; set; }
    public double DamagePerMetrePerSecond { get; set; }
}

public sealed class LavaRules
{
    public double DamagePerSecond { get; set; }
    public double WarnMetres { get; set; }

    /// <summary>This close to the edge the heat alone does harm, health per second.</summary>
    public double BesideMetres { get; set; }
    public double BesideDamagePerSecond { get; set; }
}

public sealed class HealingRules
{
    public double MedkitHealth { get; set; }
}

public enum DropRule { Everything, Inventory, Nothing }

public sealed class DeathRules
{
    public DropRule Drop { get; set; }
    public double ArmorDurabilityLoss { get; set; }
    public double RespawnFoodFloor { get; set; }
}

public sealed class MovementRules
{
    public double WalkSpeed { get; set; }
    public double RunSpeed { get; set; }
    public double SprintSpeed { get; set; }
    public double CrouchSpeed { get; set; }
    public double JumpSpeed { get; set; }
    public double Gravity { get; set; }
    public double JetpackThrust { get; set; }
    public double MiningSecondsPerOre { get; set; }
    public double MiningReach { get; set; }
}

// ---- armor.json ---------------------------------------------------------------------------

public sealed class SuitKind
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int Slots { get; set; }
    public Modifiers Modifiers { get; set; } = new();
}

public sealed class BoosterKind
{
    public string Id { get; set; }
    public string Name { get; set; }
    public Modifiers Modifiers { get; set; } = new();
}

public sealed class ArmorCatalog
{
    public double BrokenSuitEffect { get; set; } = 1;
    public List<SuitKind> Suits { get; set; } = new();
    public List<BoosterKind> Boosters { get; set; } = new();

    public SuitKind Suit(string id) => Suits.First(s => s.Id == id);
    public BoosterKind Booster(string id) => Boosters.First(b => b.Id == id);
}

// ---- food.json ----------------------------------------------------------------------------

public sealed class FoodKind
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int Tier { get; set; }
    public double Nourishment { get; set; }
    /// <summary>Null means it never spoils.</summary>
    public double? ShelfLifeHours { get; set; }
}

public sealed class ReplicatorTier
{
    public int Tier { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
    public bool Portable { get; set; }
    public double NourishmentPerOrganic { get; set; }
    public double EnergyPerNourishment { get; set; }
    /// <summary>Own fuel tank, for replicators that carry one.</summary>
    public double Fuel { get; set; }
    /// <summary>Ore it takes to build one, for replicators the player builds in the field.</summary>
    public int BuildOre { get; set; }
}

public sealed class ForageRules
{
    public int FruitPerBush { get; set; }
    public double OrganicsPerBush { get; set; }
    public double RegrowMinutes { get; set; }
}

public sealed class FoodCatalog
{
    public double MinFreshness { get; set; }
    public List<FoodKind> Foods { get; set; } = new();
    public List<ReplicatorTier> Replicators { get; set; } = new();
    public ForageRules Forage { get; set; } = new();

    public FoodKind Food(string id) => Foods.First(f => f.Id == id);
    public ReplicatorTier Tier(int tier) => Replicators.First(r => r.Tier == tier);

    /// <summary>
    /// Share of its nourishment a food keeps at this age: it falls in a straight line over its
    /// shelf life, then stays at the floor. Food that never spoils always keeps all of it.
    /// </summary>
    public double Freshness(FoodKind kind, double ageSeconds)
    {
        if (kind.ShelfLifeHours is not double hours || hours <= 0) return 1;
        double t = System.Math.Clamp(ageSeconds / (hours * 3600), 0, 1);
        return 1 - (1 - MinFreshness) * t;
    }

    public double NourishmentOf(FoodItem item, double now)
    {
        FoodKind kind = Food(item.Kind);
        return kind.Nourishment * Freshness(kind, now - item.MadeAt);
    }
}

// ---- planet.json --------------------------------------------------------------------------

public sealed class WeatherKind
{
    public string Name { get; set; }
    public double Weight { get; set; }
    public double Temperature { get; set; }
}

public sealed class WeatherRules
{
    public double IntervalSeconds { get; set; }
    public List<WeatherKind> Kinds { get; set; } = new();
}

public sealed class TerrainHint
{
    public string Shape { get; set; }
    public double Depth { get; set; }
    public double Width { get; set; }
}

public sealed class Zone
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Sign { get; set; }
    public Pos Center { get; set; }
    public double Radius { get; set; }
    public double Falloff { get; set; }
    public double Temperature { get; set; }
    public double Radiation { get; set; }
    public double? OxygenPercent { get; set; }
    public double LavaRadius { get; set; }
    public TerrainHint Terrain { get; set; }
}

public enum VolumeKind { Room, Cockpit }

public sealed class Volume
{
    public string Id { get; set; }
    public string Name { get; set; }
    public VolumeKind Kind { get; set; }
    public Pos Min { get; set; }
    public Pos Max { get; set; }
    public double Temperature { get; set; }

    public bool Contains(Pos p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y - 0.5 && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;
}

public sealed class Territory
{
    public string Id { get; set; }
    public string Name { get; set; }
    public Pos Center { get; set; }
    public double Radius { get; set; }

    public bool Contains(Pos p) => p.FlatDistanceTo(Center) <= Radius;
}

public sealed class RespawnSpot
{
    public string Id { get; set; }
    public string Name { get; set; }
    public RespawnKind Kind { get; set; }
    public Pos Position { get; set; }
    public Pos Stand { get; set; }
}

public sealed class PlacedReplicator
{
    public int Tier { get; set; }
    public Pos Position { get; set; }
}

public sealed class StorageData
{
    public Pos Position { get; set; }
    public double Organics { get; set; }
}

public sealed class PodData
{
    public Pos Position { get; set; }
    public int Rations { get; set; }
}

public sealed class StartingInventory
{
    public double Organics { get; set; }
    public int SmallBottles { get; set; }
    public int LargeBottles { get; set; }
    public int Medkits { get; set; }
    public int ReplicatorKits { get; set; }
    public List<string> Suits { get; set; } = new();
    public List<string> Boosters { get; set; } = new();
}

public sealed class StartData
{
    public Pos Position { get; set; }
    public double YawDegrees { get; set; }
    public double Food { get; set; } = 1;
    public string Suit { get; set; }
    public StartingInventory Inventory { get; set; } = new();
}

public sealed class PlanetData
{
    public string Name { get; set; }
    public double DayLengthSeconds { get; set; }
    public double StartHour { get; set; }
    public double BaseTemperature { get; set; }
    public double DayNightSwing { get; set; }
    public double OxygenPercent { get; set; }
    public WeatherRules Weather { get; set; } = new();
    public List<Zone> Zones { get; set; } = new();
    public List<Volume> Volumes { get; set; } = new();
    public List<Territory> Territories { get; set; } = new();
    public List<RespawnSpot> RespawnPoints { get; set; } = new();
    public List<PlacedReplicator> Replicators { get; set; } = new();
    public StorageData Storage { get; set; } = new();
    public double BasePower { get; set; }
    public PodData EscapePod { get; set; } = new();
    public Band FreshStartRing { get; set; } = new();
    public List<Pos> Bushes { get; set; } = new();
    public List<double[]> Rocks { get; set; } = new();
    public StartData Start { get; set; } = new();

    public Zone Zone(string id) => Zones.First(z => z.Id == id);
}
