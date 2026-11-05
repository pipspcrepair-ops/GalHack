using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GalHack.Physics;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using SurvivalVitals.Model;
using Wildlife;
using Weather.Model;

namespace Slice.Game;

/// <summary>
/// Every data file the slice plays from, read where each prototype keeps it (never copied), plus
/// the slice's own few numbers in data/slice.json. Loaded once and shared: the game and the tests
/// read exactly the same data.
/// </summary>
public sealed class GameData
{
    public PhysicsRules Physics { get; private init; }
    public Tunables Handling { get; private init; }
    public Catalogue Catalogue { get; private init; }
    public WeatherData Weather { get; private init; }
    public WildlifeData Wildlife { get; private init; }
    public SliceRules Rules { get; private init; }
    readonly string[] _survival;

    static GameData _shared;
    public static GameData Shared => _shared ??= Load();

    public static string PrototypesRoot => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
    public static string Proto(params string[] parts) => Path.Combine(PrototypesRoot, Path.Combine(parts));
    public static string Own(string relative) => ProjectSettings.GlobalizePath("res://" + relative);

    GameData(string[] survival) => _survival = survival;

    static GameData Load()
    {
        string[] survival =
        {
            File.ReadAllText(Proto("survival-vitals", "data", "vitals.json")),
            File.ReadAllText(Proto("survival-vitals", "data", "armor.json")),
            File.ReadAllText(Proto("survival-vitals", "data", "food.json")),
            File.ReadAllText(Own("data/survival.json")),
        };
        return new GameData(survival)
        {
            Physics = PhysicsRules.FromJson(File.ReadAllText(Path.Combine(PrototypesRoot, "..", "shared", "Physics", "physics.json"))),
            Handling = Tunables.FromJson(File.ReadAllText(Proto("ship-handling", "data", "handling.json"))),
            // Building's catalogue with the vessel parts (gyros, hover pads, landing legs) the
            // physics sandbox appends to it, both read in place.
            Catalogue = StructureBridge.LoadCatalogue(Proto("building", "data"), Proto("physics-sandbox", "data", "vessel-parts.json")),
            Weather = WeatherData.Parse(File.ReadAllText(Proto("weather", "data", "weather.json"))),
            Wildlife = WildlifeData.FromJson(File.ReadAllText(Proto("wildlife", "data", "wildlife.json"))),
            Rules = SliceRules.Parse(File.ReadAllText(Own("data/slice.json"))),
        };
    }

    /// <summary>
    /// A fresh copy of survival's data (the game and each test get their own, since the model
    /// keeps state in some of it). The visor's fall warning uses the physics module's safe
    /// speed: the one damage formula starts hurting there, so that is where the warning belongs.
    /// </summary>
    public SurvivalData Survival()
    {
        SurvivalData data = SurvivalData.Parse(_survival[0], _survival[1], _survival[2], _survival[3]);
        data.Vitals.Fall.SafeSpeed = Physics.Contacts.SafeSpeed;
        return data;
    }

    public Structure Blueprint(string file)
    {
        Structure s = GalHack.Structures.Blueprint.Load(File.ReadAllText(Own("data/blueprints/" + file)), Catalogue, out List<string> errors);
        if (errors.Count > 0 || s == null) throw new InvalidDataException($"{file}: {string.Join("; ", errors)}");
        s.DirtyChunks.Clear();
        return s;
    }
}

/// <summary>The slice's own numbers (data/slice.json). Everything marked placeholder there is.</summary>
public sealed class SliceRules
{
    public PlanetRules Planet { get; set; } = new();
    public MaterialRules Materials { get; set; } = new();
    public ClimateRules Climate { get; set; } = new();
    public ToolRules Tool { get; set; } = new();
    public BuildRules Build { get; set; } = new();
    public WildlifeRules Wildlife { get; set; } = new();

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static SliceRules Parse(string json) =>
        JsonSerializer.Deserialize<SliceRules>(json, Json) ?? throw new InvalidDataException("empty slice.json");

    public sealed class PlanetRules
    {
        public ulong Seed { get; set; } = 7;
        public string Archetype { get; set; } = "Temperate";
        public int Variant { get; set; }
        public float Gravity { get; set; } = 9.8f;
        public float Ceiling { get; set; } = 4000f;
        public double StartTimeOfDay { get; set; } = 0.27;
        public float PodDropHeight { get; set; } = 260f;
        public float PodDropSeconds { get; set; } = 7f;
    }

    public sealed class MaterialRules
    {
        public Dictionary<string, int> OrePerCell { get; set; } = new();
        public double OrganicsPerPlant { get; set; } = 1.5;
        public int CarbonPerPlant { get; set; } = 2;
        public Dictionary<string, int> PodLocker { get; set; } = new();
    }

    public sealed class ClimateRules
    {
        public double ColdestC { get; set; } = -45;
        public double HottestC { get; set; } = 65;
        public double DayNightSwing { get; set; } = 7;
        public double LapsePerKm { get; set; } = 6.5;
        public Dictionary<string, double> OxygenByAir { get; set; } = new();
        public Dictionary<string, double> RadiationByArchetype { get; set; } = new();
        public double CockpitC { get; set; } = 20;
    }

    public sealed class ToolRules
    {
        public float Reach { get; set; } = 7.5f;
        public float DigRadius { get; set; } = 1.42f;
        public float DigInterval { get; set; } = 0.18f;
        public float CutReach { get; set; } = 4f;
        public float ShotDamage { get; set; } = 18f;
        public float ShotInterval { get; set; } = 0.22f;
        public float ShotRange { get; set; } = 120f;
    }

    public sealed class BuildRules
    {
        public float Reach { get; set; } = 9f;
        public float BlueprintPlacesPerSecond { get; set; } = 8f;
        public string StarterBlueprint { get; set; } = "starter.gbp";
    }

    public sealed class WildlifeRules
    {
        public float PatchHalfSize { get; set; } = 300f;
        public float SafeRadius { get; set; } = 60f;
        public float Alienness { get; set; } = 0.25f;
    }
}
