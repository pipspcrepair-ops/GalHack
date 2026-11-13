using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Items.Deposits;
using GalHack.Items.Production;
using GalHack.Structures;
using Manufacturing.Game;

namespace Manufacturing.Tests;

/// <summary>
/// Headless test of the item catalogue, the recipe tree, the starter vessel's price and the ore
/// deposit shapes. Run with:
///   godot --headless --path prototypes/manufacturing res://tests/ItemsTest.tscn
/// </summary>
public partial class ItemsTest : TestBase
{
    ItemCatalogue _items;
    Catalogue _blocks;

    // The deposit kinds the planet generator places today (Planets.Gen.DepositKind), and the commons
    // it guarantees on every planet near any start.
    static readonly string[] PlanetKinds = { "Iron", "Copper", "Silicon", "Carbon", "Water", "Burnstone", "Magnesium", "Cobalt", "Neodymium", "Gold", "Gravium", "Crystal", "Resin", "WarpFuel" };
    static readonly string[] EveryPlanet = { "Iron", "Copper", "Silicon", "Carbon", "Water", "Burnstone" };

    protected override void RunAll()
    {
        Run("Catalogue loads and keeps the design rules", TestCatalogue);
        Run("Blocks are craftable items", TestBlockItems);
        Run("Deco pieces join as block items", TestDecoItems);
        Run("No cycles in the tree", TestNoCycles);
        Run("Rarity is never lower than any input", TestRarity);
        Run("Every building block has a recipe path from ore, or is unique-only", TestBlocksHavePaths);
        Run("Unique finds are never mined", TestUniques);
        Run("Names are GalHack's own", TestNames);
        Run("Breaking down never makes material", TestDeconstructionValue);
        Run("The constructor ladder builds itself", TestLadder);
        Run("Starter small vessel: price and time", TestStarterVessel);
        Run("Ore deposit shapes", TestDeposits);
        Run("Deposit placement rules", TestDepositRules);
        Run("Performance (best of three windows)", TestPerformance);
    }

    // ---- Catalogue ------------------------------------------------------------------------

    void TestCatalogue()
    {
        _items = DataPaths.ItemCatalogue;
        _blocks = DataPaths.BlockCatalogue;
        List<string> problems = _items.Validate();
        foreach (string p in problems) Note("rule broken: " + p);
        Check(problems.Count == 0, $"the catalogue keeps every design rule ({problems.Count} broken)");

        Note($"{_items.Items.Count} items, {_items.Recipes.Count} recipes, {_items.Blocks.Count} block bills, {_items.Machines.All.Count} machines");
        foreach (Rarity r in Enum.GetValues<Rarity>())
        {
            var list = _items.Items.Where(i => i.Rarity == r).ToList();
            Note($"{Rarities.Name(r),-9} {list.Count,3}  (#{Rarities.Color(r):x6})  " +
                 string.Join(", ", Enum.GetValues<ItemCategory>().Select(c => (c, n: list.Count(i => i.Category == c))).Where(x => x.n > 0).Select(x => $"{x.n} {x.c.ToString().ToLowerInvariant()}")));
        }
        Check(Enum.GetValues<Rarity>().All(r => _items.Items.Any(i => i.Rarity == r)), "every step of the rarity ladder has items");
        Check(Rarities.Color(Rarity.Exotic) == 0xa64dff && Rarities.Color(Rarity.Unique) == 0xffbf2e, "exotic is purple and unique is gold");
        // Hand weapons are the slice's own items (their rows are in shared/Arms), so the shared catalogue has none.
        foreach (ItemCategory c in Enum.GetValues<ItemCategory>().Where(c => c != ItemCategory.Weapon))
            Check(_items.InCategory(c).Any(), $"the catalogue has {c} items");

        // A broken file is refused at load, not discovered later.
        string Read(string name) => File.ReadAllText(Path.Combine(DataPaths.Items, name));
        bool Refused(Func<string, string> patch)
        {
            try { ItemCatalogue.Load(name => patch(name) ?? Read(name)); return false; }
            catch (InvalidDataException) { return true; }
        }
        Check(Refused(n => n == ItemCatalogue.RecipesFile ? Read(n).Replace("\"iron-ore\": 2 }", "\"unobtainium\": 2 }") : null), "a recipe naming an unknown item is refused");
        Check(Refused(n => n == ItemCatalogue.BlocksFile ? Read(n).Replace("\"bill\": {\"polymer\": 1}", "\"bill\": {\"polymer\": 0}") : null), "a zero count in a bill is refused");
    }

    void TestBlockItems()
    {
        BuildingBridge.NameBlockItems(_items, _blocks);
        int blocks = 0;
        foreach (BlockBill b in _items.Blocks.All)
        {
            ItemDef item = b.Item;
            blocks++;
            bool ok = item != null && item.Id == b.BlockId && item.Category == ItemCategory.Block && item.Recipe != null && item.StackSize > 0 && item.MassKg > 0
                      && item.VolumeSu > 0 && item.Unlock != null && item.BlockId == b.BlockId && item.Recipe.InputsFor(1).Equals(b.Bill);
            if (!ok) Check(false, $"block {b.BlockId} is one block item, by its own id, with recipe, stack, mass, volume and unlock");
        }
        Check(_items.BlockItems.Count() == blocks, $"one item per block: {blocks} block items, each with a recipe, stack, mass, volume and unlock");
        Check(_items.BlockItems.All(i => !i.Id.Contains('@') && !i.Name.Contains("small", StringComparison.OrdinalIgnoreCase) && !i.Name.Contains("large", StringComparison.OrdinalIgnoreCase)),
            "no block item is per grid or named small or large");
        ItemDef steel = _items.BlockItem("iron-plated");
        Check(steel.Name == "Iron plated block" && steel.StackSize > _items.BlockItem("thruster-t1").StackSize,
            $"{steel.Name} stacks deep ({steel.StackSize}), devices don't ({_items.BlockItem("thruster-t1").StackSize})");
        Check(Math.Abs(steel.MassKg - _items.MassOf(_items.Blocks.For("iron-plated"))) < 1e-9 && Math.Abs(steel.VolumeSu - _items.VolumeOf(_items.Blocks.For("iron-plated"))) < 1e-9,
            $"a block item weighs and packs as what it is made of ({steel.MassKg} kg, {steel.VolumeSu} SU)");

        // Old ids, the per-grid items among them, read as the items that replaced them.
        var was = new (string old, string now)[]
        {
            ("steel@large", "iron-plated"), ("steel@small", "iron-plated"), ("cpuext@large", "iron-plated"), ("cpuext4@small", "iron-plated"),
            ("hardened@large", "hardened-plated"), ("glass@small", "glass-plated"), ("thruster@small", "thruster-t1"), ("thruster@large", "thruster-t3"),
            ("generator@large", "generator-t3"), ("container@large", "container-t4"), ("gear@large", "pad-t3"), ("door@small", "door-1x2x1"),
            ("chair@large", "command-chair"), ("smallfab@large", "fabricator-t1"), ("big-core", "core"), ("small-core", "core"), ("hover-core", "core"),
            ("hull-plate", "iron-plate"),
        };
        foreach (var (old, now) in was)
            Check(_items.TryGet(old, out ItemDef item) && item.Id == now, $"the old id {old} loads as {now}");
        var kept = new SharedStorage(_items, "old save", 500);
        kept.Add("big-core", 1);
        kept.Add("small-core", 1);
        Check(kept.Count("core") == 2 && kept.Stacks.Count() == 1, "stacks saved under the two old cores land on the one core's stack");
        var cores = _items.BlockItems.Where(i => _blocks.TryGetType(i.BlockId, out BlockType t) && t.IsCore).Select(i => i.Id).ToList();
        Check(cores.SequenceEqual(new[] { "core" }) && _items.Item("core").Name == "Core", $"there is one core: {string.Join(", ", cores)}");

        // Plates: a plated block is its base block's bill plus its plates.
        foreach (BlockType t in _blocks.Types.Skip(1).Where(t => t.IsPlated))
        {
            Bill want = _items.Blocks.For(t.Base.Id).Add(t.Plate, t.Plates);
            Check(_items.Blocks.For(t.Id).Equals(want) && _items.Item(t.Plate).Category == ItemCategory.Part, $"{t.Id} is {t.Base.Id} plus {t.Plates} {t.Plate}");
        }
        // One door item places every door size, a bigger door taking more of it.
        BlockType door = _blocks.Type("door-1x2x1");
        Check(_blocks.VariantsOf(door).All(d => !_items.Blocks.TryGet(d.Id, out _) == (d != door)) && _items.Item("door-1x2x1").Name == "Door",
            "the door family is one item, Door, and one bill");
        var s = new Structure(_blocks);
        Block big = Building.Make(s, _blocks.Type("door-3x3x1"));
        Check(BuildingBridge.ItemOf(_items, s, big)?.Id == "door-1x2x1" && BuildingBridge.ItemsOf(_items, s, big).Equals(Bill.Of(("door-1x2x1", 5)))
              && BuildingBridge.BillOf(_items, s, big).Equals(new Bill().Add(_items.Blocks.For("door-1x2x1"), 5)),
            "a 3x3 door is placed from the door item and takes 5 of them");
        Check(_items.Blocks.Get("door-1x2x1").Cells == 2 && _items.Blocks.Get("iron-plated").Xp == 1 && _items.Blocks.Get("thruster-t5").Xp == 8
              && Math.Abs(_items.Blocks.Get("core").AssemblySeconds - 0.4) < 1e-9 && Math.Abs(_items.Blocks.Get("fabricator-t2").AssemblySeconds - 1.6) < 1e-9,
            "assembly time and XP go by the cells a block fills (0.4 s at one cell, 1.6 s at eight; 1 XP an armour block, up to 8 a device)");

        // The fabricator tiers make the right blocks.
        Constructor Make(string tier) => new(_items, _items.Machines.Get(tier), SharedStorage.Unbounded(_items, "t"), new WorldRules());
        Constructor survival = Make("survival"), smallFab = Make("small"), large = Make("large");
        foreach (string id in new[] { "base-block", "iron-plated", "glass-plated", "concrete-plated", "container-t1", "generator-t1", "fuel-tank-t1", "fabricator-t1", "light", "ladder", "core" })
            Check(survival.Knows(_items.BlockItem(id)), $"the survival constructor makes {_items.BlockItem(id).Name}");
        foreach (string id in new[] { "thruster-t1", "command-chair", "gyro-t1", "pad-t1", "hover-t1" })
            Check(!survival.Knows(_items.BlockItem(id)) && smallFab.Knows(_items.BlockItem(id)), $"{_items.BlockItem(id).Name} needs Fabricator T1");
        foreach (string id in new[] { "hardened-plated", "combat-plated", "thruster-t3" })
            Check(!smallFab.Knows(_items.BlockItem(id)) && large.Knows(_items.BlockItem(id)), $"{_items.BlockItem(id).Name} needs Fabricator T2");
        Check(_items.Item("warp-cell").Recipe.Tier <= 1 && _items.Item("warp-cell").Recipe.Inputs.All(i => i.Item.Rarity == Rarity.Common), "warp cells are made in constructors from common materials");
        Check(_items.Item("fold-crystal").Uses.Contains("Hyperdrive") && _items.Item("fold-crystal").Uses.Contains("shields"), "fold crystal is hyperdrive fuel and still feeds shields");
    }

    void TestDecoItems()
    {
        ItemCatalogue items = ItemCatalogue.LoadDirectory(DataPaths.Items);
        DecoCatalogue deco = DecoCatalogue.LoadDirectory(DataPaths.Items);
        int before = items.Items.Count;
        deco.AddBillsTo(items);
        Check(items.Items.Count == before + deco.Pieces.Count, $"every intact deco piece is one block item ({deco.Pieces.Count})");
        DecoPiece first = deco.Pieces[0];
        ItemDef d = items.BlockItem(first.Id);
        Check(d != null && d.Family == BlockFamily.Deco && d.Name == first.Name && d.Group == first.Set && d.Unlock?.Tab == "deco" && d.Recipe.InputsFor(1).Equals(first.Bill),
            $"{d?.Name}: a deco block item, made from its bill, on the deco tab, in its set ({d?.Group})");
        Check(d.Id == first.Id && d.Unlock == first.Unlock, "its unlock is the piece's node");
        Check(!items.Has(first.BlockId(DecoState.Broken)) && items.BlockItem(first.BlockId(DecoState.Broken)) == null, "damaged states are Creative and POI only: no item");
        List<string> problems = items.Validate();
        foreach (string p in problems.Take(10)) Note("rule broken: " + p);
        Check(problems.Count == 0, $"the catalogue with deco keeps every design rule ({problems.Count} broken)");
    }

    void TestNoCycles()
    {
        Check(RecipeGraph.FindCycle(_items.Items) == null, "the recipe tree has no cycle");
        Check(_items.Items.All(i => i.Recipe == null ? _items.Graph.Level(i) == 0 : i.Recipe.Inputs.All(x => _items.Graph.Level(x.Item) < _items.Graph.Level(i))),
            "every item sits above all of its inputs");
        Note($"deepest chain: {_items.Items.Max(i => _items.Graph.Level(i))} steps ({_items.Items.OrderByDescending(i => _items.Graph.Level(i)).First().Name})");

        // A cycle slipped into the data is caught at load and named.
        string recipes = File.ReadAllText(Path.Combine(DataPaths.Items, ItemCatalogue.RecipesFile))
            .Replace("\"from\": { \"iron-ore\": 2 }", "\"from\": { \"iron-plate\": 2 }");
        try
        {
            ItemCatalogue.Load(n => n == ItemCatalogue.RecipesFile ? recipes : File.ReadAllText(Path.Combine(DataPaths.Items, n)));
            Check(false, "a recipe cycle is refused");
        }
        catch (InvalidDataException e)
        {
            Check(e.Message.Contains("cycle") && e.Message.Contains("iron-bar") && e.Message.Contains("iron-plate"), $"a recipe cycle is refused and named ({e.Message})");
        }
    }

    void TestRarity()
    {
        int pairs = 0, bad = 0;
        foreach (Recipe r in _items.Recipes)
            foreach (var (input, _) in r.Inputs)
            {
                pairs++;
                if (input.Rarity > r.Output.Rarity) { bad++; Note($"{r.Output.Id} ({r.Output.Rarity}) < {input.Id} ({input.Rarity})"); }
            }
        Check(bad == 0, $"no item is less rare than an input ({pairs} recipe inputs checked)");
        // Raw rarity follows the design table.
        Check(_items.Item("iron-ore").Rarity == Rarity.Common && _items.Item("cobalt-ore").Rarity == Rarity.Uncommon
            && _items.Item("fold-crystal-raw").Rarity == Rarity.Rare && _items.Item("voidglass-shard").Rarity == Rarity.Exotic
            && _items.Item("lattice-core").Rarity == Rarity.Unique, "ore rarities follow the design's ladder");
        // Rarity reaches all the way up: an item made from an exotic is exotic, from a unique is unique.
        Check(_items.Item("voidglass-lens").Rarity == Rarity.Exotic && _items.Item("lattice-processor").Rarity == Rarity.Unique, "rarity carries up the tree");
    }

    void TestBlocksHavePaths()
    {
        int checkedBlocks = 0, uniqueOnly = 0;
        foreach (BlockType t in _blocks.Types.Skip(1).Where(t => t.Head == null || t.Head == t)) // a door of any size is placed from the family's one item
        {
            bool hasBill = Check(_items.Blocks.TryGet(t.Id, out BlockBill bill), $"{t.Id} has a bill");
            if (!hasBill) continue;
            checkedBlocks++;
            {
                Bill b = bill.Bill;
                var blockers = new List<string>();
                foreach (string id in b.Ids)
                {
                    _items.Graph.FromGathered(_items.Item(id), out List<string> found);
                    blockers.AddRange(found);
                }
                if (bill.UniqueOnly)
                {
                    Check(blockers.Count > 0 && blockers.All(id => _items.Item(id).IsUnique), $"{t.Id} is unique-only and needs only unique finds beyond ore");
                    uniqueOnly++;
                }
                else
                {
                    Check(blockers.Count == 0, $"{t.Id} is made from mined ore alone{(blockers.Count > 0 ? ": blocked by " + string.Join(", ", blockers) : "")}");
                    int tier = b.Ids.Max(id => _items.Graph.TierNeeded(_items.Item(id)));
                    Check(tier <= 3, $"{t.Id} can be made by some constructor (needs {_items.Machines.TierName(tier)})");
                }
            }
        }
        Note($"{checkedBlocks} block types priced ({uniqueOnly} unique-only: precursor plating)");
        // Every plating up keeps the base under it, so upgrading never loses the block.
        foreach (BlockType t in _blocks.Types.Skip(1).Where(t => t.Upgrade != null))
            Check(_items.Blocks.For(t.Upgrade.Id)["polymer"] >= _items.Blocks.For(t.Id)["polymer"], $"{t.Upgrade.Id} is the same base block under new plates as {t.Id}");
        // Weapons say what they fire.
        Check(_items.Blocks.AmmoFor("turret") == "shells-40mm" && _items.Blocks.AmmoFor("gatling") == "belt-12mm" && _items.Blocks.AmmoFor("rocketpod") == "rocket",
            "every gun names its ammo");
    }

    void TestUniques()
    {
        var uniques = _items.InCategory(ItemCategory.Unique).ToList();
        Check(uniques.Count >= 4, $"the design's unique finds are in ({string.Join(", ", uniques.Select(u => u.Name))})");
        Check(uniques.All(u => u.Recipe == null && u.Source == ItemSource.Found && u.DepositKind == null), "unique finds have no recipe, no deposit, and come from exploring");
        Check(!_items.Items.Any(i => i.IsGathered && i.Rarity == Rarity.Unique), "nothing gathered from the ground is unique");
        Check(!_items.Recipes.Any(r => r.Output.IsUnique), "no recipe makes a unique find");
    }

    void TestNames()
    {
        Check(_items.Items.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == _items.Items.Count, "every item name is distinct");
        // Distinctive names from the game that inspired GalHack, which must never turn up here.
        string[] theirs = { "pentaxid", "erestrum", "zascosium", "sathium", "promethium", "optronic", "oscillator", "cobalt alloy",
            "steel plate", "mechanical component", "energy matrix", "power coil", "thruster component", "plastic material", "quantum processor" };
        var clashes = _items.Items.Where(i => theirs.Any(t => i.Name.Contains(t, StringComparison.OrdinalIgnoreCase))).Select(i => i.Name).ToList();
        Check(clashes.Count == 0, "no item borrows another game's names" + (clashes.Count > 0 ? ": " + string.Join(", ", clashes) : ""));
        Check(_items.Items.Where(i => i.Rarity == Rarity.Common && i.IsRaw).All(i => i.Name.Split(' ').Length <= 2), "common raw goods have plain names");
    }

    void TestDeconstructionValue()
    {
        double rate = _items.Machines.DeconstructorReturn;
        int checkedItems = 0;
        var storage = SharedStorage.Unbounded(_items, "bench");
        var decon = new Deconstructor(_items, _items.Machines.Deconstructor, storage, new WorldRules());
        foreach (ItemDef item in _items.Items.Where(i => i.Recipe != null))
        {
            var before = _items.Graph.RawCost(item);
            var after = new Dictionary<string, double>();
            foreach (var (id, amount) in decon.ReturnsOf(item, 1))
                foreach (var (raw, per) in _items.Graph.RawCost(_items.Item(id)))
                    after[raw] = after.GetValueOrDefault(raw) + amount * per;
            bool ok = after.All(kv => kv.Value <= before.GetValueOrDefault(kv.Key) * rate + 1e-9);
            Check(ok, $"breaking down {item.Id} gives back no more than {rate:P0} of its ore");
            checkedItems++;
        }
        Note($"{checkedItems} craftable items: each breaks down to exactly {rate:P0} of its ore value, never more");
        Check(rate is > 0.33 - 1e-9 and < 0.8 + 1e-9, $"the return rate ({rate:P0}) sits inside the design's salvage range, 33% to 80%");
    }

    void TestLadder()
    {
        // Each machine's bill needs only recipes a lower tier knows (Validate checks it; say it here).
        foreach (MachineDef m in _items.Machines.All)
        {
            Bill bill = m.ItemId != null ? _items.Item(m.ItemId).Recipe.InputsFor(1) : _items.Blocks.For(m.BlockId);
            int need = bill.Ids.Max(id => _items.Graph.TierNeeded(_items.Item(id)));
            string what = m.Kind == MachineKind.Constructor ? $"tier {m.Tier}" : m.Kind == MachineKind.Deconstructor ? "returns " + _items.Machines.DeconstructorReturn.ToString("P0") : "factory";
            Note($"{m.Name,-22} {what,-12} speed {m.Speed:0.##}x  built with a {_items.Machines.TierName(need)} constructor");
        }
        // The foothold base needs nothing the carried survival constructor can't make.
        Bill foothold = _items.Blocks.Total(_items.Machines.Reference.Foothold);
        Expansion e = _items.Graph.Expand(foothold);
        Check(e.MaxTier == 0, $"the foothold base (core, power, fuel, small constructor, storage, floor) is built from survival-constructor recipes (needs tier {e.MaxTier})");
    }

    // ---- The starter small vessel ---------------------------------------------------------

    void TestStarterVessel()
    {
        Structure gadfly = DataPaths.LoadBlueprint("gadfly.gbp");
        List<BlockCount> blocks = BuildingBridge.BlocksOf(gadfly);
        Bill parts = _items.Blocks.Total(blocks);
        Expansion vessel = _items.Graph.Expand(parts);
        ReferenceRates reference = _items.Machines.Reference;
        Expansion foothold = _items.Graph.Expand(_items.Blocks.Total(reference.Foothold));
        Expansion fuel = _items.Graph.Expand(reference.StarterFuel);

        var stats = StructureStats.Compute(gadfly);
        Note($"Gadfly: {gadfly.Count} small blocks, {stats.Mass * 1000:0} kg, thrust/weight {stats.ThrustToWeightMax:0.0} g, flies: {stats.Flies}");
        Note($"  parts: {_items.Describe(parts)}");
        Note($"  ore:   {_items.Describe(vessel.Leaves)}  = {vessel.Leaves.Total} ore");
        Note($"  craft: {vessel.Runs} constructor runs, {vessel.Seconds / 60:0.0} min on a small constructor");
        Note($"Foothold base: {_items.Describe(foothold.Leaves)} = {foothold.Leaves.Total} ore, {foothold.Seconds / 60 / _items.Machines.Get("survival").Speed:0.0} min on the survival constructor");
        Note($"Fuel (20 energy cells): {fuel.Leaves.Total} ore");

        Check(stats.Flies && stats.ThrustToWeightMax > 1.2, "the Gadfly is a working small vessel (powered, lifts its own weight)");
        var allowed = new HashSet<string>(_items.Items.Where(i => i.IsRaw && (EveryPlanet.Contains(i.DepositKind) || i.Source == ItemSource.Dug)).Select(i => i.Id));
        Check(vessel.Leaves.Ids.All(allowed.Contains) && foothold.Leaves.Ids.All(allowed.Contains) && fuel.Leaves.Ids.All(allowed.Contains),
            "everything for the first vessel comes from what every planet has (iron, copper, silicon, carbon, ice, dug stone)");
        Check(vessel.MaxTier <= 1, "the vessel's parts come from a small constructor");

        double ore = vessel.Leaves.Total + foothold.Leaves.Total + fuel.Leaves.Total;
        double gather = ore / reference.GatherOrePerMinute;
        double craft = foothold.Seconds / 60 / _items.Machines.Get("survival").Speed + (vessel.Seconds + fuel.Seconds) / 60 / _items.Machines.Get("small").Speed;
        double place = (gadfly.Count + reference.Foothold.Sum(b => b.Count)) * reference.PlaceSecondsPerBlock / 60;
        double worst = gather + craft + place;                    // nothing overlaps
        double best = Math.Max(gather, craft) + place;            // the constructor works while the player gathers
        Note($"TOTAL first vessel from the crash: {ore:0} ore; gather {gather:0} min at {reference.GatherOrePerMinute:0}/min, craft {craft:0} min, place {place:0} min");
        Note($"  => {best:0} to {worst:0} minutes for an experienced player (the design asks for 1 to 2 hours)");
        Check(vessel.Leaves.Total is >= 400 and <= 2500, $"the vessel costs a sensible amount of ore ({vessel.Leaves.Total})");
        Check(best >= 30 && worst <= 120, $"the first vessel takes {best:0}-{worst:0} min, inside an hour or two with the rest of survival");

        // For comparison: the physics sandbox's walk-in shuttle, if it is there.
        string corsairPath = Path.Combine(DataPaths.Repo, "prototypes", "physics-sandbox", "data", "blueprints", "corsair.gbp");
        if (File.Exists(corsairPath))
        {
            Structure corsair = Blueprint.Load(File.ReadAllText(corsairPath), _blocks, out List<string> errors);
            if (corsair != null && errors.Count == 0)
            {
                Expansion c = _items.Graph.Expand(_items.Blocks.Total(BuildingBridge.BlocksOf(corsair)));
                Note($"Corsair 9 (walk-in shuttle, {corsair.Count} blocks): {c.Leaves.Total} ore, {c.Seconds / 60:0} min of crafting");
            }
        }
    }

    // ---- Deposits ---------------------------------------------------------------------------

    static double Flat(double x, double z) => 40;
    static double Hilly(double x, double z) => 40 + 6 * Math.Sin(x * 0.11) + 4 * Math.Cos(z * 0.08 + 1);

    void TestDeposits()
    {
        DepositRules rules = DataPaths.DepositRules;
        foreach (DepositShape shape in Enum.GetValues<DepositShape>())
        {
            int n = 0, ok = 0, okDepth = 0, deterministic = 0;
            double volumeError = 0, shapeMetric = 0;
            int rich = 0, outcrops = 0, fractures = 0;
            foreach (Func<double, double, double> ground in new Func<double, double, double>[] { Flat, Hilly })
                for (ulong seed = 1; seed <= 6; seed++)
                {
                    var s = new DepositSeed(seed * 7919, 100 + seed * 3, 200 - seed * 5, "iron-ore", 1.0, shape);
                    DepositBody body = DepositGrower.Grow(s, ground, rules);
                    var cells = new List<(int x, int y, int z, Grade g)>();
                    body.ForEachCell((x, y, z, g) => cells.Add((x, y, z, g)));
                    n++;
                    if (cells.Count > 0) ok++;
                    volumeError = Math.Max(volumeError, Math.Abs(cells.Count / body.TargetVolume - 1));
                    if (cells.All(c => c.y + 0.5 <= ground(c.x + 0.5, c.z + 0.5) + 1e-6 && ground(c.x + 0.5, c.z + 0.5) - (c.y + 0.5) <= rules.MaxDepth + 1e-6)) okDepth++;
                    if (cells.Any(c => c.g >= Grade.Rich)) rich++;
                    if (body.Tells.Any(t => t.Kind == TellKind.Outcrop)) outcrops++;
                    if (body.Tells.Any(t => t.Kind == TellKind.Fracture)) fractures++;
                    var again = new List<(int, int, int, Grade)>();
                    DepositGrower.Grow(s, ground, rules).ForEachCell((x, y, z, g) => again.Add((x, y, z, g)));
                    if (again.SequenceEqual(cells)) deterministic++;
                    shapeMetric += Metric(shape, cells, ground);
                }
            shapeMetric /= n;
            Note($"{shape,-7} volume within {volumeError:P0} of target, {rich}/{n} with rich ore, {outcrops}/{n} showing at the surface, metric {shapeMetric:0.00}");
            Check(ok == n, $"{shape}: every body has ore");
            Check(volumeError <= 0.3, $"{shape}: bodies hold their amount's volume within 30% (worst {volumeError:P0})");
            Check(okDepth == n, $"{shape}: no ore above the ground or deeper than {rules.MaxDepth} m under it");
            Check(deterministic == n, $"{shape}: the same seed grows the same body");
            Check(rich >= n / 2, $"{shape}: hot spots make rich ore in most bodies ({rich}/{n})");
            switch (shape)
            {
                case DepositShape.Vein: Check(shapeMetric >= 2.5, $"veins are long and thin (length to width {shapeMetric:0.0})"); break;
                case DepositShape.Bed: Check(shapeMetric >= 4, $"beds are broad and flat (spread to thickness {shapeMetric:0.0})"); break;
                case DepositShape.Pocket: Check(shapeMetric <= 3.5 && outcrops >= n / 2, $"pockets are compact and shallow (length to width {shapeMetric:0.0})"); break;
                case DepositShape.Deep: Check(shapeMetric >= 5 && fractures == n, $"deep clusters sit deep (mean {shapeMetric:0.0} m down) and show cracked rock above"); break;
            }
        }

        // Amount scales the body; different seeds differ.
        double Ore(double amount) => DepositGrower.Grow(new DepositSeed(42, 0, 0, "copper-ore", amount, DepositShape.Pocket), Flat, rules).TotalOre(_items.Machines.Reference.OreUnitsPerCell);
        double small = Ore(0.6), big = Ore(1.6);
        Note($"a copper pocket gives {small:0} ore at amount 0.6 and {big:0} at 1.6 ({_items.Machines.Reference.OreUnitsPerCell} per cell before grade)");
        Check(big > small * 1.8, "a bigger deposit holds more ore");
        int a = DepositGrower.Grow(new DepositSeed(1, 0, 0, "iron-ore", 1, DepositShape.Vein), Flat, rules).ForEachCell();
        var cellsA = new List<int>();
        var cellsB = new List<int>();
        DepositGrower.Grow(new DepositSeed(1, 0, 0, "iron-ore", 1, DepositShape.Vein), Flat, rules).ForEachCell((x, y, z, g) => cellsA.Add(x * 7919 + y * 31 + z));
        DepositGrower.Grow(new DepositSeed(2, 0, 0, "iron-ore", 1, DepositShape.Vein), Flat, rules).ForEachCell((x, y, z, g) => cellsB.Add(x * 7919 + y * 31 + z));
        Check(a > 0 && !cellsA.SequenceEqual(cellsB), "different seeds grow different veins");
    }

    /// <summary>Vein and pocket: length over width (principal axes); bed: footprint diameter over mean thickness; deep: mean depth.</summary>
    static double Metric(DepositShape shape, List<(int x, int y, int z, Grade g)> cells, Func<double, double, double> ground)
    {
        if (cells.Count == 0) return 0;
        if (shape == DepositShape.Deep) return cells.Average(c => ground(c.x + 0.5, c.z + 0.5) - (c.y + 0.5));
        if (shape == DepositShape.Bed)
        {
            var columns = cells.GroupBy(c => (c.x, c.z)).ToList();
            double thickness = columns.Average(g => g.Count());
            double diameter = Math.Sqrt(columns.Count * 4 / Math.PI); // of a circle with the same footprint
            return diameter / Math.Max(thickness, 1);
        }
        // Principal axes from the covariance of cell centres.
        double mx = cells.Average(c => c.x), my = cells.Average(c => c.y), mz = cells.Average(c => c.z);
        double xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
        foreach (var c in cells)
        {
            double dx = c.x - mx, dy = c.y - my, dz = c.z - mz;
            xx += dx * dx; yy += dy * dy; zz += dz * dz; xy += dx * dy; xz += dx * dz; yz += dy * dz;
        }
        double[] eig = Eigen3(xx, yy, zz, xy, xz, yz);
        return Math.Sqrt(eig[0] / Math.Max(eig[1], 1e-6));
    }

    /// <summary>Eigenvalues of a symmetric 3x3 matrix, largest first (closed form).</summary>
    static double[] Eigen3(double a, double b, double c, double d, double e, double f)
    {
        double p1 = d * d + e * e + f * f, q = (a + b + c) / 3;
        double p2 = (a - q) * (a - q) + (b - q) * (b - q) + (c - q) * (c - q) + 2 * p1, p = Math.Sqrt(p2 / 6);
        if (p < 1e-12) return new[] { a, b, c };
        double B(double x) => x / p;
        double ba = B(a - q), bb = B(b - q), bc = B(c - q), bd = B(d), be = B(e), bf = B(f);
        double r = (ba * (bb * bc - bf * bf) - bd * (bd * bc - bf * be) + be * (bd * bf - bb * be)) / 2;
        double phi = r <= -1 ? Math.PI / 3 : r >= 1 ? 0 : Math.Acos(r) / 3;
        double e1 = q + 2 * p * Math.Cos(phi), e3 = q + 2 * p * Math.Cos(phi + 2 * Math.PI / 3);
        return new[] { e1, 3 * q - e1 - e3, e3 };
    }

    void TestDepositRules()
    {
        DepositRules rules = DataPaths.DepositRules;
        foreach (string ore in rules.Ores)
        {
            bool known = _items.TryGet(ore, out ItemDef item);
            Check(known && item.IsRaw && item.Source == ItemSource.Mined, $"{ore} is a mined raw ore");
        }
        Check(_items.Items.Where(i => i.Source == ItemSource.Mined).All(i => rules.Ores.Contains(i.Id)), "every mined ore has deposit shapes");
        Check(!rules.Ores.Any(o => _items.Item(o).IsUnique), "no unique find has a deposit");
        foreach (string kind in PlanetKinds)
        {
            string ore = DepositRules.OreForKind(_items, kind);
            Check(ore != null, $"the planet generator's {kind} deposits yield {ore ?? "nothing"}");
        }
        Check(EveryPlanet.All(k => _items.Item(DepositRules.OreForKind(_items, k)).Rarity == Rarity.Common), "the common kinds on every planet are common ores");
        // Commons favour shallow shapes; the rarest ores grow deep.
        Check(rules.ShapesFor("gravium-ore").All(s => s.Shape == DepositShape.Deep) && rules.ShapesFor("starsilver-ore").All(s => s.Shape == DepositShape.Deep), "the rarest metals only grow deep");
        Check(rules.ShapesFor("carbon-ore").All(s => s.Shape == DepositShape.Bed), "carbon grows in beds, like coal seams");
        foreach (string ore in new[] { "burnstone", "cobalt-ore", "magnesium-ore", "neodymium-ore", "gold-ore", "gravium-ore" })
            Check(PlanetKinds.Contains(_items.Item(ore).DepositKind), $"{ore} has a deposit the planet generator places");
    }

    void TestPerformance()
    {
        double load = BestOfThree(() => ItemCatalogue.LoadDirectory(DataPaths.Items));
        Structure lancer = Blueprint.Load(File.ReadAllText(Path.Combine(DataPaths.BuildingData, "blueprints", "lancer.gbp")), _blocks, out _);
        List<BlockCount> lancerBlocks = BuildingBridge.BlocksOf(lancer);
        double bom = BestOfThree(() => _items.Graph.Expand(_items.Blocks.Total(lancerBlocks), BlueprintFactory.PaysIn), 20);
        DepositRules rules = DataPaths.DepositRules;
        double grow = BestOfThree(() => DepositGrower.Grow(new DepositSeed(9, 10, 10, "iron-ore", 1.3, DepositShape.Vein), Hilly, rules), 5);
        DepositBody body = DepositGrower.Grow(new DepositSeed(9, 10, 10, "iron-ore", 1.3, DepositShape.Vein), Hilly, rules);
        int hits = 0;
        double chunk = BestOfThree(() =>
        {
            hits = 0;
            for (int z = 0; z < 16; z++)
                for (int y = 30; y < 46; y++)
                    for (int x = 0; x < 16; x++)
                        if (body.Contains(x + 2.5, y + 0.5, z + 2.5)) hits++;
        }, 5);
        Note($"catalogue load {load:0.0} ms; lancer bill of materials ({lancer.Count} blocks) {bom:0.000} ms; grow a vein {grow:0.00} ms; a 16^3 chunk of ore tests {chunk:0.00} ms ({hits} ore cells)");
        Check(load < 60, $"the catalogue loads in under 60 ms ({load:0.0})");
        Check(bom < 5, $"a whole capital ship's bill of materials takes under 5 ms ({bom:0.000})");
        Check(grow < 8, $"growing a deposit takes under 8 ms ({grow:0.00})");
        Check(chunk < 3, $"testing a 16^3 chunk against a deposit takes under 3 ms ({chunk:0.00})");
    }
}
