using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GalHack.Items;

/// <summary>
/// The whole item catalogue as data: items, recipes, block bills and machines, loaded from the
/// JSON files in shared/Items/data. Nothing about an item is hard-coded; a new ore, part or
/// recipe is a new line in a data file. Load throws on anything malformed (unknown ids, a recipe
/// cycle); <see cref="Validate"/> lists the softer design rules the tests hold it to.
/// </summary>
public sealed class ItemCatalogue
{
    public const string ItemsFile = "items.json", RecipesFile = "recipes.json", BlocksFile = "blocks.json", MachinesFile = "machines.json";

    readonly List<ItemDef> _items = new();
    readonly Dictionary<string, ItemDef> _byId = new(StringComparer.Ordinal);
    readonly List<Recipe> _recipes = new();

    public IReadOnlyList<ItemDef> Items => _items;
    public IReadOnlyList<Recipe> Recipes => _recipes;
    public BlockBills Blocks { get; } = new();
    public MachineCatalogue Machines { get; } = new();
    public RecipeGraph Graph { get; private set; }

    public ItemDef Item(string id) => _byId.TryGetValue(id, out ItemDef item) ? item : throw new KeyNotFoundException($"unknown item '{id}'");
    public bool TryGet(string id, out ItemDef item) => _byId.TryGetValue(id, out item);
    public bool Has(string id) => _byId.ContainsKey(id);

    public IEnumerable<ItemDef> InCategory(ItemCategory c) => _items.Where(i => i.Category == c);

    /// <summary>Loads the four data files from a folder on disk.</summary>
    public static ItemCatalogue LoadDirectory(string dir) => Load(name => File.ReadAllText(Path.Combine(dir, name)));

    /// <summary>Loads through a file reader, so an engine can supply its own paths.</summary>
    public static ItemCatalogue Load(Func<string, string> readFile)
    {
        var c = new ItemCatalogue();
        c.LoadItems(DataJson.Parse(readFile(ItemsFile), "galhack.items"));
        c.LoadRecipes(DataJson.Parse(readFile(RecipesFile), "galhack.recipes"));
        c.LoadBlocks(DataJson.Parse(readFile(BlocksFile), "galhack.blocks"));
        c.LoadMachines(DataJson.Parse(readFile(MachinesFile), "galhack.machines"));
        List<string> cycle = RecipeGraph.FindCycle(c._items);
        if (cycle != null) throw new InvalidDataException($"recipe cycle: {string.Join(" -> ", cycle)}");
        c.Graph = new RecipeGraph(c);
        return c;
    }

    /// <summary>"12 Iron ore, 3 Copper ore", or "nothing".</summary>
    public string Describe(Bill bill, int max = int.MaxValue)
    {
        if (bill == null || bill.IsEmpty) return "nothing";
        var parts = bill.Take(max).Select(kv => $"{kv.Value} {(TryGet(kv.Key, out ItemDef i) ? i.Name : kv.Key)}").ToList();
        if (bill.Kinds > max) parts.Add($"+{bill.Kinds - max} more");
        return string.Join(", ", parts);
    }

    /// <summary>Total storage units a bill takes up.</summary>
    public double VolumeOf(Bill bill) => bill.Sum(kv => Item(kv.Key).VolumeSu * kv.Value);
    public double MassOf(Bill bill) => bill.Sum(kv => Item(kv.Key).MassKg * kv.Value);

    // ---- Loading ------------------------------------------------------------------------

    void LoadItems(JsonElement root)
    {
        foreach (JsonElement e in root.GetProperty("items").EnumerateArray())
        {
            var category = DataJson.Enum<ItemCategory>(e, "category");
            string source = DataJson.Str(e, "source", "");
            var item = new ItemDef
            {
                Id = DataJson.Str(e, "id"),
                Name = DataJson.Str(e, "name"),
                Short = DataJson.Str(e, "short", null),
                Category = category,
                Rarity = Rarities.Parse(DataJson.Str(e, "rarity")),
                Source = source switch
                {
                    "" => ItemSource.Crafted,
                    "mine" => ItemSource.Mined,
                    "dig" => ItemSource.Dug,
                    "harvest" => ItemSource.Harvested,
                    "explore" => ItemSource.Found,
                    _ => throw new InvalidDataException($"item '{DataJson.Str(e, "id")}': unknown source '{source}'"),
                },
                MassKg = DataJson.Num(e, "mass"),
                VolumeSu = DataJson.Num(e, "volume"),
                Where = DataJson.Str(e, "where", ""),
                Uses = DataJson.Str(e, "uses", ""),
                DepositKind = DataJson.Str(e, "deposit", null),
                FuelUnits = DataJson.Int(e, "fuel", 0),
                Rounds = DataJson.Int(e, "rounds", 0),
                RestockDefault = DataJson.Int(e, "restock", 0),
            };
            if (_byId.ContainsKey(item.Id)) throw new InvalidDataException($"duplicate item id '{item.Id}'");
            if (item.VolumeSu <= 0 || item.MassKg <= 0) throw new InvalidDataException($"item '{item.Id}': mass and volume must be positive");
            item.Index = _items.Count;
            _items.Add(item);
            _byId[item.Id] = item;
        }
    }

    void LoadRecipes(JsonElement root)
    {
        foreach (JsonElement e in root.GetProperty("recipes").EnumerateArray())
        {
            ItemDef output = Known(DataJson.Str(e, "make"), "recipe output");
            if (output.Recipe != null) throw new InvalidDataException($"two recipes make '{output.Id}'");
            var inputs = new List<(ItemDef, int)>();
            foreach (JsonProperty p in e.GetProperty("from").EnumerateObject())
            {
                int n = p.Value.GetInt32();
                if (n <= 0) throw new InvalidDataException($"recipe for '{output.Id}': input counts must be positive");
                inputs.Add((Known(p.Name, $"recipe for '{output.Id}'"), n));
            }
            if (inputs.Count == 0) throw new InvalidDataException($"recipe for '{output.Id}' has no inputs");
            var recipe = new Recipe
            {
                Output = output,
                Count = DataJson.Int(e, "count", 1),
                Inputs = inputs,
                Seconds = DataJson.Num(e, "seconds"),
                Tier = DataJson.Int(e, "tier", 0),
            };
            if (recipe.Count <= 0 || recipe.Seconds <= 0) throw new InvalidDataException($"recipe for '{output.Id}': count and seconds must be positive");
            output.Recipe = recipe;
            _recipes.Add(recipe);
        }
    }

    void LoadBlocks(JsonElement root)
    {
        if (root.TryGetProperty("assembly", out JsonElement a))
        {
            Blocks.AssemblySmall = DataJson.Num(a, "small", 0.4);
            Blocks.AssemblyLarge = DataJson.Num(a, "large", 1.6);
        }
        foreach (JsonProperty p in root.GetProperty("blocks").EnumerateObject())
        {
            JsonElement e = p.Value;
            string ammo = DataJson.Str(e, "ammo", null);
            if (ammo != null && Known(ammo, $"block '{p.Name}' ammo").Category != ItemCategory.Ammo)
                throw new InvalidDataException($"block '{p.Name}': '{ammo}' is not ammo");
            Blocks.Add(new BlockBill
            {
                BlockId = p.Name,
                Small = ReadBill(e.GetProperty("small"), $"block '{p.Name}'"),
                Large = ReadBill(e.GetProperty("large"), $"block '{p.Name}'"),
                UniqueOnly = DataJson.Bool(e, "uniqueOnly", false),
                Ammo = ammo,
            });
        }
    }

    void LoadMachines(JsonElement root)
    {
        foreach (JsonElement e in root.GetProperty("constructors").EnumerateArray())
            Machines.Add(new MachineDef
            {
                Id = DataJson.Str(e, "id"),
                Name = DataJson.Str(e, "name"),
                Tag = DataJson.Str(e, "tag", "FAB"),
                Kind = MachineKind.Constructor,
                Tier = DataJson.Int(e, "tier", 0),
                Speed = DataJson.Num(e, "speed", 1),
                BlockId = DataJson.Str(e, "block", null),
                ItemId = DataJson.Str(e, "item", null),
                Portable = DataJson.Bool(e, "portable", false),
            });
        if (root.TryGetProperty("tierNames", out JsonElement names))
            Machines.TierNames = names.EnumerateArray().Select(n => n.GetString()).ToArray();

        JsonElement d = root.GetProperty("deconstructor");
        Machines.Add(new MachineDef
        {
            Id = DataJson.Str(d, "id"), Name = DataJson.Str(d, "name"), Tag = DataJson.Str(d, "tag", "DECON"),
            Kind = MachineKind.Deconstructor, Speed = DataJson.Num(d, "speed", 1), BlockId = DataJson.Str(d, "block", null),
        });
        Machines.DeconstructorReturn = DataJson.Num(d, "returnRate", 0.5);
        Machines.DeconstructorSecondsFactor = DataJson.Num(d, "secondsFactor", 0.5);
        if (Machines.DeconstructorReturn is <= 0 or > 1) throw new InvalidDataException("deconstructor returnRate must be in (0, 1]");

        JsonElement f = root.GetProperty("factory");
        Machines.Add(new MachineDef
        {
            Id = DataJson.Str(f, "id"), Name = DataJson.Str(f, "name"), Tag = DataJson.Str(f, "tag", "BP-FAB"),
            Kind = MachineKind.BlueprintFactory, Speed = DataJson.Num(f, "speed", 1), BlockId = DataJson.Str(f, "block", null),
        });

        foreach (MachineDef m in Machines.All)
        {
            if (m.ItemId != null) Known(m.ItemId, $"machine '{m.Id}'");
            if (m.BlockId != null && !Blocks.TryGet(m.BlockId, out _)) throw new InvalidDataException($"machine '{m.Id}': no bill for block '{m.BlockId}'");
        }

        if (root.TryGetProperty("reference", out JsonElement r))
        {
            var foothold = new List<BlockCount>();
            if (r.TryGetProperty("foothold", out JsonElement list))
                foreach (JsonElement b in list.EnumerateArray())
                    foothold.Add(new BlockCount(DataJson.Str(b, "block"), DataJson.Str(b, "grid", "large") == "small", DataJson.Int(b, "count", 1)));
            Machines.Reference = new ReferenceRates
            {
                GatherOrePerMinute = DataJson.Num(r, "gatherOrePerMinute", 45),
                OreUnitsPerCell = DataJson.Int(r, "oreUnitsPerCell", 12),
                PlaceSecondsPerBlock = DataJson.Num(r, "placeSecondsPerBlock", 1.2),
                Foothold = foothold,
                StarterFuel = r.TryGetProperty("starterFuel", out JsonElement fuel) ? ReadBill(fuel, "starterFuel") : new Bill(),
            };
        }
    }

    Bill ReadBill(JsonElement e, string where)
    {
        var bill = new Bill();
        foreach (JsonProperty p in e.EnumerateObject())
        {
            Known(p.Name, where);
            int n = p.Value.GetInt32();
            if (n <= 0) throw new InvalidDataException($"{where}: counts must be positive");
            bill.Add(p.Name, n);
        }
        return bill;
    }

    ItemDef Known(string id, string where) =>
        _byId.TryGetValue(id, out ItemDef item) ? item : throw new InvalidDataException($"{where}: unknown item '{id}'");
}

/// <summary>JSON helpers shared by the loaders: comments and trailing commas allowed, schema checked.</summary>
public static class DataJson
{
    public static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static JsonElement Parse(string json, string schema)
    {
        JsonElement root = JsonDocument.Parse(json, Options).RootElement.Clone();
        string found = Str(root, "schema", "");
        if (found != schema) throw new InvalidDataException($"expected schema '{schema}', found '{found}'");
        if (Int(root, "version", 0) != 1) throw new InvalidDataException($"{schema}: unsupported version");
        return root;
    }

    public static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetString() : throw new InvalidDataException($"missing '{name}'");

    public static string Str(JsonElement e, string name, string fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetString() : fallback;

    public static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetDouble() : throw new InvalidDataException($"missing '{name}'");

    public static double Num(JsonElement e, string name, double fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetDouble() : fallback;

    public static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetInt32() : fallback;

    public static bool Bool(JsonElement e, string name, bool fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetBoolean() : fallback;

    public static T Enum<T>(JsonElement e, string name) where T : struct, System.Enum =>
        System.Enum.TryParse(Str(e, name), ignoreCase: true, out T value) ? value : throw new InvalidDataException($"bad {name} '{Str(e, name)}'");
}
