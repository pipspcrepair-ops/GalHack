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
        c.Rebuild();
        return c;
    }

    /// <summary>Checks for cycles and rebuilds the recipe graph after items were added (block items from deco, say).</summary>
    internal void Rebuild()
    {
        List<string> cycle = RecipeGraph.FindCycle(_items);
        if (cycle != null) throw new InvalidDataException($"recipe cycle: {string.Join(" -> ", cycle)}");
        Graph = new RecipeGraph(this);
    }

    /// <summary>Every block family item (one per block and grid it goes on).</summary>
    public IEnumerable<ItemDef> BlockItems => _items.Where(i => i.IsBlock);

    /// <summary>The block family item that places a block on a grid, or null.</summary>
    public ItemDef BlockItem(string blockId, bool smallGrid) => Blocks.ItemFor(blockId, smallGrid);

    /// <summary>
    /// Names block items from building's catalogue (<paramref name="baseName"/> gives a block's name,
    /// "Steel block" or "Thruster", or null to leave it): "Steel block (large)". Items named in the
    /// data (the cores) keep their names.
    /// </summary>
    public void NameBlockItems(Func<string, string> baseName)
    {
        foreach (ItemDef item in BlockItems)
        {
            if (item.Named) continue;
            string n = baseName(item.BlockId);
            if (string.IsNullOrEmpty(n)) continue;
            item.Name = $"{n} ({(item.SmallGrid ? "small" : "large")})";
            item.Short = BlockShort(n, item.SmallGrid);
        }
    }

    static string BlockShort(string name, bool small)
    {
        string word = new string(name.Split(' ', '-')[0].Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (word.Length > 6) word = word[..6];
        return $"{word}-{(small ? "S" : "L")}";
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
                StackSize = DataJson.Int(e, "stack", DefaultStack(category)),
                Unlock = ReadUnlock(e, $"item '{DataJson.Str(e, "id")}'"),
            };
            if (item.StackSize <= 0) throw new InvalidDataException($"item '{item.Id}': stack must be positive");
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

    // Block item defaults per family and grid (blocks.json "items"): stack size and craft seconds.
    readonly Dictionary<(BlockFamily, bool), int> _blockStack = new()
    {
        [(BlockFamily.Armour, true)] = 500, [(BlockFamily.Armour, false)] = 250,
        [(BlockFamily.Device, true)] = 25, [(BlockFamily.Device, false)] = 10,
        [(BlockFamily.Deco, true)] = 20, [(BlockFamily.Deco, false)] = 10,
    };
    readonly Dictionary<(BlockFamily, bool), double> _blockSeconds = new()
    {
        [(BlockFamily.Armour, true)] = 1, [(BlockFamily.Armour, false)] = 2,
        [(BlockFamily.Device, true)] = 3, [(BlockFamily.Device, false)] = 6,
        [(BlockFamily.Deco, true)] = 2, [(BlockFamily.Deco, false)] = 4,
    };

    /// <summary>What the data says about one grid's block item beyond its bill (all optional).</summary>
    internal sealed record BlockItemSpec(string Id = null, string Name = null, string Short = null, int? Stack = null, double? Seconds = null,
        int? Tier = null, Unlock Unlock = null, string Group = null, IReadOnlyList<StructureClass> Places = null);

    static BlockFamily ParseFamily(string s, string where) => s switch
    {
        "armour" => BlockFamily.Armour,
        "device" => BlockFamily.Device,
        "deco" => BlockFamily.Deco,
        _ => throw new InvalidDataException($"{where}: family is armour, device or deco, not '{s}'"),
    };

    static List<StructureClass> ParseClasses(JsonElement list, string where)
    {
        var classes = new List<StructureClass>();
        foreach (JsonElement c in list.EnumerateArray())
            classes.Add(StructureClasses.TryParse(c.GetString(), out StructureClass sc) ? sc
                : throw new InvalidDataException($"{where}: structure classes are base, capital, small and hover, not '{c.GetString()}'"));
        if (classes.Count == 0) throw new InvalidDataException($"{where}: it must go on at least one class of structure");
        return classes;
    }

    void LoadBlocks(JsonElement root)
    {
        if (root.TryGetProperty("assembly", out JsonElement a))
        {
            Blocks.AssemblySmall = DataJson.Num(a, "small", 0.4);
            Blocks.AssemblyLarge = DataJson.Num(a, "large", 1.6);
        }
        if (root.TryGetProperty("items", out JsonElement defaults))
        {
            foreach (BlockFamily f in new[] { BlockFamily.Armour, BlockFamily.Device, BlockFamily.Deco })
            {
                string key = f.ToString().ToLowerInvariant();
                if (defaults.TryGetProperty("stack", out JsonElement st) && st.TryGetProperty(key, out JsonElement sf))
                    foreach (bool small in new[] { true, false }) _blockStack[(f, small)] = DataJson.Int(sf, small ? "small" : "large", _blockStack[(f, small)]);
                if (defaults.TryGetProperty("seconds", out JsonElement se) && se.TryGetProperty(key, out JsonElement sef))
                    foreach (bool small in new[] { true, false }) _blockSeconds[(f, small)] = DataJson.Num(sef, small ? "small" : "large", _blockSeconds[(f, small)]);
            }
        }
        foreach (JsonProperty p in root.GetProperty("blocks").EnumerateObject())
        {
            JsonElement e = p.Value;
            string where = $"block '{p.Name}'";
            string ammo = DataJson.Str(e, "ammo", null);
            if (ammo != null && Known(ammo, $"{where} ammo").Category != ItemCategory.Ammo)
                throw new InvalidDataException($"{where}: '{ammo}' is not ammo");
            bool unlocks = e.TryGetProperty("unlock", out JsonElement u);
            string[] grids = e.TryGetProperty("grids", out JsonElement gr) ? gr.EnumerateArray().Select(x => x.GetString()).ToArray() : new[] { "small", "large" };
            bool onSmall = grids.Contains("small"), onLarge = grids.Contains("large");
            var bill = new BlockBill
            {
                BlockId = p.Name,
                Small = onSmall ? ReadBill(e.GetProperty("small"), where) : new Bill(),
                Large = onLarge ? ReadBill(e.GetProperty("large"), where) : new Bill(),
                OnSmall = onSmall,
                OnLarge = onLarge,
                UniqueOnly = DataJson.Bool(e, "uniqueOnly", false),
                Ammo = ammo,
                Family = ParseFamily(DataJson.Str(e, "family", "device"), where),
                UnlockSmall = unlocks && onSmall ? ReadUnlock(u, where, "small") : null,
                UnlockLarge = unlocks && onLarge ? ReadUnlock(u, where, "large") : null,
            };
            AddBlock(bill, small =>
            {
                string grid = small ? "small" : "large";
                JsonElement item = e.TryGetProperty("item", out JsonElement it) && it.TryGetProperty(grid, out JsonElement ig) ? ig : default;
                bool has = item.ValueKind == JsonValueKind.Object;
                int? stack = has && item.TryGetProperty("stack", out _) ? DataJson.Int(item, "stack", 1)
                    : e.TryGetProperty("stack", out JsonElement es) && es.TryGetProperty(grid, out JsonElement esg) ? esg.GetInt32() : null;
                double? seconds = e.TryGetProperty("seconds", out JsonElement sec) && sec.TryGetProperty(grid, out JsonElement secg) ? secg.GetDouble() : null;
                int? tier = e.TryGetProperty("tier", out JsonElement t) ? t.GetInt32() : null;
                IReadOnlyList<StructureClass> places = e.TryGetProperty("places", out JsonElement pl) && pl.TryGetProperty(grid, out JsonElement plg)
                    ? ParseClasses(plg, where) : null;
                return new BlockItemSpec(has ? DataJson.Str(item, "id", null) : null, has ? DataJson.Str(item, "name", null) : null,
                    has ? DataJson.Str(item, "short", null) : null, stack, seconds, tier, Places: places);
            });
        }
    }

    /// <summary>
    /// Adds a block's bill and makes its family items, one per grid it is on: crafted from the bill
    /// (one item a run), by the lowest constructor tier that can make everything in it, as heavy
    /// and as bulky as what it is made of, of its rarest input's rarity, unlocked by the block's
    /// tech-tree node on that grid, and placeable on the structure classes of that grid (large:
    /// bases and capital vessels; small: small and hover vessels) unless the data narrows them.
    /// </summary>
    internal void AddBlock(BlockBill bill, Func<bool, BlockItemSpec> spec)
    {
        Blocks.Add(bill);
        foreach (bool small in new[] { true, false })
        {
            if (!bill.On(small)) continue;
            Bill made = bill.For(small);
            if (made.IsEmpty) continue; // Validate reports it
            BlockItemSpec s = spec?.Invoke(small) ?? new BlockItemSpec();
            var inputs = made.Select(kv => (Item(kv.Key), kv.Value)).ToList();
            string grid = small ? "small" : "large";
            string id = s.Id ?? BlockBill.NodeId(bill.BlockId, small);
            string fallback = $"{char.ToUpperInvariant(bill.BlockId[0])}{bill.BlockId[1..]} ({grid})";
            IReadOnlyList<StructureClass> places = s.Places ?? StructureClasses.OfGrid(small);
            foreach (StructureClass c in places)
                if (StructureClasses.IsSmallGrid(c) != small) throw new InvalidDataException($"block item '{id}': a {grid}-grid block can't go on a {StructureClasses.Name(c)}");
            var item = new ItemDef
            {
                Id = id,
                Name = s.Name ?? fallback,
                Short = s.Short ?? BlockShort(s.Name ?? bill.BlockId, small),
                Named = s.Name != null,
                Category = ItemCategory.Block,
                Rarity = inputs.Select(i => i.Item1.Rarity).Aggregate(Rarity.Common, Rarities.Max),
                Source = ItemSource.Crafted,
                MassKg = MassOf(made),
                VolumeSu = VolumeOf(made),
                Uses = bill.Family == BlockFamily.Armour ? "Placed as any of its shapes" : "Placed as a block",
                StackSize = s.Stack ?? _blockStack[(bill.Family, small)],
                Unlock = s.Unlock ?? bill.UnlockFor(small),
                BlockId = bill.BlockId,
                SmallGrid = small,
                Family = bill.Family,
                Group = s.Group,
                Places = places,
            };
            if (item.StackSize <= 0) throw new InvalidDataException($"block item '{id}': stack must be positive");
            if (_byId.ContainsKey(id)) throw new InvalidDataException($"block item '{id}' clashes with an item id");
            int tier = s.Tier ?? Math.Max(0, inputs.Max(i => TierOf(i.Item1, null)));
            item.Recipe = new Recipe { Output = item, Count = 1, Inputs = inputs, Seconds = s.Seconds ?? _blockSeconds[(bill.Family, small)], Tier = tier };
            item.Index = _items.Count;
            _items.Add(item);
            _byId[id] = item;
            _recipes.Add(item.Recipe);
            if (small) bill.ItemSmall = item;
            else bill.ItemLarge = item;
        }
    }

    /// <summary>The lowest constructor tier that can make an item and everything it is made of.</summary>
    static int TierOf(ItemDef item, HashSet<ItemDef> path = null)
    {
        if (item.Recipe == null) return 0;
        path ??= new HashSet<ItemDef>();
        if (!path.Add(item)) return 0; // a cycle: loading refuses it once everything is in
        int tier = item.Recipe.Tier;
        foreach (var (input, _) in item.Recipe.Inputs) tier = Math.Max(tier, TierOf(input, path));
        path.Remove(item);
        return tier;
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

    /// <summary>How many units of a category one slot holds when the data doesn't say: bulk goods stack deep, devices and tools don't.</summary>
    public static int DefaultStack(ItemCategory c) => c switch
    {
        ItemCategory.Raw or ItemCategory.Refined => 500,
        ItemCategory.Part => 200,
        ItemCategory.Component => 50,
        ItemCategory.Consumable or ItemCategory.Ammo => 25,
        ItemCategory.Unique => 10,
        _ => 1,
    };

    /// <summary>A tech-tree node (<c>{ "level", "cost", "parent", "tab" }</c>) under <paramref name="key"/>, or null if there is none.</summary>
    static Unlock ReadUnlock(JsonElement e, string where, string key = "unlock")
    {
        if (!e.TryGetProperty(key, out JsonElement u) || u.ValueKind != JsonValueKind.Object) return null;
        int level = DataJson.Int(u, "level", 1), cost = DataJson.Int(u, "cost", 0);
        if (level < 1 || cost < 0) throw new InvalidDataException($"{where}: unlock level must be at least 1 and cost not negative");
        return new Unlock(level, cost, DataJson.Str(u, "parent", null), DataJson.Str(u, "tab", "misc"));
    }

    /// <summary>Turns a data unlock record into the shared one (deco's nodes have the same shape).</summary>
    internal static Unlock ToUnlock(int level, int cost, string parent, string tab) => new(level, cost, parent, tab);

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
