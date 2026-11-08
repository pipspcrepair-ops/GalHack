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
        // Deco pieces are block items on the tech tree's deco tab (nodes and gallery entries); the one universal deco block places any of them.
        var decoLibrary = GalHack.Items.DecoCatalogue.LoadDirectory(SharedPath("Items", "data"));
        decoLibrary.AddBillsTo(items);
        decoLibrary.AddUniversalBlockTo(items);
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
            Wildlife = WildlifeData.FromJson(File.ReadAllText(Proto("..", "shared", "Bestiary", "data", "wildlife.json"))),
            Rules = SliceRules.Parse(File.ReadAllText(Own("data/slice.json"))),
            Arms = LoadArms(items),
        };
    }

    /// <summary>shared/Arms' weapon rows, each checked against the items: the weapon is a weapon item, and its ammo is an item.</summary>
    public GalHack.Arms.Armoury Arms { get; private init; }

    static GalHack.Arms.Armoury LoadArms(GalHack.Items.ItemCatalogue items)
    {
        var arms = GalHack.Arms.Armoury.LoadFile(SharedPath("Arms", "data", "weapons.json"));
        foreach (GalHack.Arms.WeaponDef w in arms.All)
        {
            if (!items.TryGet(w.Item, out var item) || item.Category != GalHack.Items.ItemCategory.Weapon)
                throw new InvalidDataException($"weapon '{w.Id}': '{w.Item}' is not a weapon item");
            foreach (GalHack.Arms.WeaponDef m in w.Modes)
                if (!items.TryGet(m.Ammo, out _)) throw new InvalidDataException($"weapon '{w.Id}': no ammo item '{m.Ammo}'");
        }
        foreach (var item in items.InCategory(GalHack.Items.ItemCategory.Weapon))
            if (arms.OfItem(item.Id) == null) throw new InvalidDataException($"weapon item '{item.Id}' has no row in shared/Arms/data/weapons.json");
        return arms;
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
    public ClimateNodeRules ClimateNode { get; set; } = new();
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
        public int OrePerCell { get; set; } = 3;
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

        /// <summary>The weather's exposure (weather.json) at which the visor warns and at which an exposed body loses health, and how fast: health per second per exposure.</summary>
        public double StormWarnFrom { get; set; } = 0.5;
        public double StormHarmFrom { get; set; } = 1;
        public double StormHealthPerSecond { get; set; } = 3;

        /// <summary>Water colder than this is ice water: past the warning it takes health at this rate, whatever the suit.</summary>
        public double IceWaterC { get; set; } = 2;
        public double IceWaterHealthPerSecond { get; set; } = 7;
        public double IceWaterWarnSeconds { get; set; } = 1.5;
    }

    public sealed class ClimateNodeRules
    {
        public float Radius { get; set; } = 9f;
        public double LowC { get; set; } = 12;
        public double HighC { get; set; } = 26;

        /// <summary>Seconds a unit of each fuel burns, tried in this order.</summary>
        public Dictionary<string, double> Fuels { get; set; } = new();
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

        /// <summary>Gathering works at a rate: damage per second to the cell or plant under the beam at tier 1 (the survival tool), against each thing's durability.</summary>
        public float GatherRate { get; set; } = 3f;
        public DurabilityRules Durability { get; set; } = new();
        /// <summary>Each hand tool by item id: the faster drills the tech tree unlocks.</summary>
        public Dictionary<string, ToolTier> Tiers { get; set; } = new();

        public ToolTier TierOf(string itemId) => itemId != null && Tiers.TryGetValue(itemId, out ToolTier t) ? t : new ToolTier();
    }

    /// <summary>How much a hand tool does: its gather rate as a multiple of the survival tool's, the ball of ground its beam reaches, its shots' damage as a multiple.</summary>
    public sealed class ToolTier
    {
        public string Name { get; set; } = "Survival tool";
        public float Rate { get; set; } = 1f;
        public float Radius { get; set; } = 1.42f;
        public float Shot { get; set; } = 1f;
    }

    /// <summary>How much work each thing takes before it is gone (damage units; a pulse does rate x interval of them).</summary>
    public sealed class DurabilityRules
    {
        public float Soil { get; set; } = 1f;
        public float Rock { get; set; } = 2f;
        public float DeepRock { get; set; } = 3f;
        public float Ore { get; set; } = 3f;
        /// <summary>A plant, a tree and a boulder, at scale 1 (they grow with the plant's size).</summary>
        public float Plant { get; set; } = 1.5f;
        public float Tree { get; set; } = 9f;
        public float Boulder { get; set; } = 5f;
        /// <summary>A tree gives this many times a plant's materials.</summary>
        public float TreeYield { get; set; } = 1.5f;
        public float BoulderStone { get; set; } = 3f;
    }

    public sealed class BuildRules
    {
        public float Reach { get; set; } = 9f;
        public float BlueprintPlacesPerSecond { get; set; } = 8f;
        public string StarterBlueprint { get; set; } = "starter.gbp";
        /// <summary>The BLUEPRINTS tab's starter set (data/blueprints), Large and Small together.</summary>
        public List<string> Library { get; set; } = new() { "starter.gbp" };
        /// <summary>Where blueprints the player saves go (the library's Yours).</summary>
        public string Saved { get; set; } = "user://blueprints";
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
        public float Attenuation { get; set; } = 1f;
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
        /// <summary>Server setting: whether block-breaking creatures can damage players' bases.</summary>
        public bool BreakBases { get; set; } = true;

        /// <summary>The most wild creatures a patch holds on the most livable world.</summary>
        public int MostPerPatch { get; set; } = 70;

        /// <summary>Groups of each species a patch holds on the most livable world, against its natural number (giants 1, the rest 2).</summary>
        public float Density { get; set; } = 1f;

        /// <summary>
        /// How much the world's layers (a dead or barren world, a rich one) move its numbers: 0 not at
        /// all, 1 fully. Most of the way: a dead world plainly quiet, a living one alive.
        /// </summary>
        public float LayerWeight { get; set; } = 0.8f;

        /// <summary>
        /// Topping up as you go: every few seconds, once you have moved on, a group comes back out of
        /// sight while fewer than this share of the patch's most live within 250 m of you.
        /// </summary>
        public float NearShare { get; set; } = 0.6f;

        /// <summary>How livable a world of each archetype is, 0..1: lush worlds busiest, lava all but dead.</summary>
        public Dictionary<string, float> Livable { get; set; } = new() { ["Temperate"] = 1f, ["Alien"] = 0.6f, ["Arid"] = 0.35f, ["Ice"] = 0.3f, ["Lava"] = 0.08f };

        /// <summary>An airless cratered body (barren rock) holds this share of that.</summary>
        public float Cratered { get; set; } = 0.4f;

        public float LivableOn(Planets.Gen.PlanetSpec spec) =>
            Livable.GetValueOrDefault(spec.Archetype.ToString(), 0.5f) * (spec.Airless ? Cratered : 1f);
    }
}
