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
using Structure = GalHack.Structures.Structure;

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

    /// <summary>shared/Items' catalogue with the slice's additions (data/items.json): items, recipes, block bills, machines and tech-tree nodes.</summary>
    public GalHack.Items.ItemCatalogue Items { get; private init; }

    /// <summary>What using each consumable does.</summary>
    public Dictionary<string, Slice.Items.ConsumableEffect> Consumables { get; private init; }

    /// <summary>Levels and experience (data/progress.json).</summary>
    public Slice.Items.ProgressRules Progress { get; private init; }

    public static string SharedPath(params string[] parts) => Path.GetFullPath(Path.Combine(PrototypesRoot, "..", "shared", Path.Combine(parts)));
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
        var items = Slice.Items.SliceItems.Load(SharedPath("Items", "data"), Own("data/items.json"), out var consumables);
        // Deco pieces are craftable block items too, unlocked on the tech tree's deco tab.
        GalHack.Items.DecoCatalogue.LoadDirectory(SharedPath("Items", "data")).AddBillsTo(items);
        var catalogue = GalHack.Items.Bridge.BuildingBridge.LoadCatalogue(Proto("building", "data"),
            new[] { Proto("physics-sandbox", "data", "vessel-parts.json"), SharedPath("Items", "data", "building-parts.json") },
            new[] { SharedPath("Items", "data", "building-materials.json") });
        // Block items are named after building's blocks: "Steel block (large)".
        GalHack.Items.Bridge.BuildingBridge.NameBlockItems(items, catalogue);
        return new GameData(survival)
        {
            Physics = PhysicsRules.FromJson(File.ReadAllText(SharedPath("Physics", "physics.json"))),
            Handling = Tunables.FromJson(File.ReadAllText(Proto("ship-handling", "data", "handling.json"))),
            // Building's catalogue with the vessel parts (gyros, hover pads, landing legs) the
            // physics sandbox appends to it, and shared/Items' production machines, guns and
            // extra armour, all read in place.
            Catalogue = catalogue,
            Items = items,
            Consumables = consumables,
            Progress = Slice.Items.ProgressRules.Parse(File.ReadAllText(Own("data/progress.json"))),
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
    public TorchRules Torch { get; set; } = new();
    public ContainerRules Containers { get; set; } = new();

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
        public float Gravity { get; set; } = 9.8f;
        public float Ceiling { get; set; } = 4000f;
        public double StartTimeOfDay { get; set; } = 0.27;
        public float ValleyFogScale { get; set; } = 0.45f;
    }

    public sealed class MaterialRules
    {
        /// <summary>Raw ore items per 1 m cell of ore dug (shared/Items' pacing reference).</summary>
        public int OrePerCell { get; set; } = 12;
        /// <summary>Stone per cell of rock dug.</summary>
        public int StonePerRockCell { get; set; } = 1;
        public double OrganicsPerPlant { get; set; } = 1.5;
        public int BiomassPerPlant { get; set; } = 2;
        /// <summary>What the escape pod's locker holds, by item id.</summary>
        /// <summary>The starting kit, in the bunker's locker.</summary>
        public Dictionary<string, int> StartKit { get; set; } = new();
        /// <summary>What the castaway starts with on the toolbar, by item id, in slot order.</summary>
        public List<string> Toolbar { get; set; } = new();
    }

    public sealed class ClimateRules
    {
        public double ColdestC { get; set; } = -45;
        public double HottestC { get; set; } = 65;
        public double DayNightSwing { get; set; } = 7;
        public double LapsePerKm { get; set; } = 6.5;
        public Dictionary<string, double> OxygenByAir { get; set; } = new();
        public Dictionary<string, double> RadiationByArchetype { get; set; } = new();
        public double SealedRoomC { get; set; } = 20;
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
        public float SmallVesselMaxTurn { get; set; } = 110f;
        public AreaRules Area { get; set; } = new();
    }

    /// <summary>A structure's area: its bounding box plus a margin per class and a share of its size, m.</summary>
    public sealed class AreaRules
    {
        public float Base { get; set; } = 30f;
        public float Capital { get; set; } = 30f;
        public float Small { get; set; } = 12f;
        public float Hover { get; set; } = 12f;
        public float SizeShare { get; set; } = 0.5f;

        public float Margin(GalHack.Items.StructureClass c) => c switch
        {
            GalHack.Items.StructureClass.Base => Base,
            GalHack.Items.StructureClass.Capital => Capital,
            GalHack.Items.StructureClass.Small => Small,
            _ => Hover,
        };
    }

    public sealed class TorchRules
    {
        public float Range { get; set; } = 45f;
        public float Angle { get; set; } = 32f;
        public float Energy { get; set; } = 5.5f;
    }

    public sealed class ContainerRules
    {
        public int[] Corpse { get; set; } = { 4, 4 };
        public int[] Backpack { get; set; } = { 8, 8 };
        public int[] Drop { get; set; } = { 8, 8 };
    }

    public sealed class WildlifeRules
    {
        public float PatchHalfSize { get; set; } = 300f;
        public float SafeRadius { get; set; } = 60f;
        public float Alienness { get; set; } = 0.25f;
    }
}
