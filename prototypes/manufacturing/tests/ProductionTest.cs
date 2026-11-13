using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Items.Production;
using GalHack.Structures;
using Manufacturing.Game;

namespace Manufacturing.Tests;

/// <summary>
/// Headless test of the machines and survival building: constructor queues and tiers, ammo
/// restock, the deconstructor, the blueprint factory, and placing blocks in survival and creative.
///   godot --headless --path prototypes/manufacturing res://tests/ProductionTest.tscn
/// </summary>
public partial class ProductionTest : TestBase
{
    ItemCatalogue _items;
    Catalogue _blocks;

    protected override void RunAll()
    {
        _items = DataPaths.ItemCatalogue;
        _blocks = DataPaths.BlockCatalogue;
        Run("Queues build sub-parts in build order", TestBuildOrder);
        Run("Orders run first come, first served", TestFifo);
        Run("Missing raw materials, then resuming", TestMissing);
        Run("Constructor tiers", TestTiers);
        Run("Cancelling refunds, draining storage replans", TestCancelAndDrain);
        Run("Ammo restock: up to the threshold, never above", TestRestockThreshold);
        Run("Ammo restock: the toggle", TestRestockToggle);
        Run("Ammo restock: player jobs keep their order", TestRestockPriority);
        Run("Ammo restock: reserved materials, pausing and resuming", TestRestockReserved);
        Run("Deconstructor return rate", TestDeconstructor);
        Run("Build then deconstruct never creates material", TestRoundTrip);
        Run("Blueprint factory: exact bill and refusal", TestFactory);
        Run("Survival and creative placement", TestPlacementModes);
        Run("Craft a block, place it, take it down", TestCraftPlaceRemove);
        Run("Auto sub-parts through block items", TestAutoSubParts);
        Run("Block families go on their classes of structure", TestClasses);
        Run("Full storage spills to a crate", TestSpill);
        Run("Upgrading in place conserves materials", TestUpgrade);
        Run("Creative machines", TestCreativeMachines);
        Run("Performance (best of three windows)", TestPerformance);
    }

    // ---- Helpers --------------------------------------------------------------------------

    SharedStorage Storage(double capacity = 1_000_000, Bill contents = null)
    {
        var s = new SharedStorage(_items, "test hold", capacity);
        if (contents != null) Check(s.TryAdd(contents), "the test storage takes its starting contents");
        return s;
    }

    Constructor Fab(string tier, SharedStorage storage, WorldRules rules = null) =>
        new(_items, _items.Machines.Get(tier), storage, rules ?? new WorldRules());

    static Bill Ores(int n) => Bill.Of(("iron-ore", n), ("copper-ore", n), ("silicon-ore", n), ("carbon-ore", n), ("ice", n), ("stone", n),
        ("cobalt-ore", n), ("magnesium-ore", n), ("neodymium-ore", n), ("gold-ore", n));

    /// <summary>Raw ore value of a bill (a part counts as the ore it was made from).</summary>
    Dictionary<string, double> Raw(Bill bill) => _items.Graph.RawCost(bill);

    Dictionary<string, double> Raw(IReadOnlyDictionary<string, double> fractions)
    {
        var r = new Dictionary<string, double>();
        foreach (var (id, n) in fractions)
            foreach (var (raw, per) in _items.Graph.RawCost(_items.Item(id)))
                r[raw] = r.GetValueOrDefault(raw) + n * per;
        return r;
    }

    static Dictionary<string, double> Sum(params Dictionary<string, double>[] parts)
    {
        var r = new Dictionary<string, double>();
        foreach (var p in parts) foreach (var (k, v) in p) r[k] = r.GetValueOrDefault(k) + v;
        return r;
    }

    static bool Same(Dictionary<string, double> a, Dictionary<string, double> b) =>
        a.Keys.Union(b.Keys).All(k => Math.Abs(a.GetValueOrDefault(k) - b.GetValueOrDefault(k)) < 1e-6);

    static bool AtMost(Dictionary<string, double> a, Dictionary<string, double> b) =>
        a.All(kv => kv.Value <= b.GetValueOrDefault(kv.Key) + 1e-6);

    /// <summary>Ticks until the constructor is idle (or a time limit), recording which item each run started.</summary>
    static List<(string Item, Lane Lane)> RunUntilIdle(Constructor fab, double maxSeconds = 3600, double step = 0.25)
    {
        var starts = new List<(string, Lane)>();
        Job last = null;
        int lastRuns = -1;
        // Always tick once: the restock lane is planned on a tick, so an idle queue may have work.
        for (double t = 0; t < maxSeconds && (t == 0 || fab.Busy); t += step)
        {
            fab.Tick(step);
            Job now = fab.Current;
            if (now != null && (now != last || now.RunsDone != lastRuns)) starts.Add((now.Item.Id, now.Lane));
            last = now;
            lastRuns = now?.RunsDone ?? -1;
            if (now == null && !fab.AllJobs.Any(fab.CanStart)) break;
        }
        return starts;
    }

    // ---- Constructor queues ---------------------------------------------------------------------

    void TestBuildOrder()
    {
        SharedStorage storage = Storage(contents: Ores(400));
        var before = Raw(storage.Contents);
        Constructor fab = Fab("small", storage);
        OrderResult r = fab.Order("power-unit", 2);
        Check(r.Ok, "ordering two power units is accepted");

        List<Job> lines = fab.SubParts.ToList();
        Note("auto sub-parts: " + string.Join(" > ", lines.Select(j => $"{j.Item.Name} x{j.Count}")));
        Check(lines.Select(j => j.Item).Distinct().Count() == lines.Count, "one sub-part line per part for the order");
        bool ordered = true;
        for (int i = 0; i < lines.Count; i++)
            foreach (var (input, _) in lines[i].Recipe.Inputs)
            {
                int maker = lines.FindIndex(l => l.Item == input);
                if (maker >= i) ordered = false;
            }
        Check(ordered, "every sub-part comes after the sub-parts it is made from");
        Check(lines.All(l => l.Feeds == r.Job && l.Auto), "each sub-part says which order it feeds");
        Check(fab.Missing.IsEmpty, "nothing is missing when the ore is all there");

        List<(string Item, Lane Lane)> starts = RunUntilIdle(fab);
        Check(!fab.Busy, "the queue empties");
        Check(storage.Count("power-unit") == 2, $"two power units are in storage ({storage.Count("power-unit")})");
        int firstOrderRun = starts.FindIndex(s => s.Lane == Lane.Order);
        Check(firstOrderRun > 0 && starts.Take(firstOrderRun).All(s => s.Lane == Lane.SubPart), "parts were built before the order that uses them");
        Check(_items.Graph.Level(_items.Item(starts[0].Item)) == 1, $"the first run refines ore ({starts[0].Item})");
        Check(Same(Raw(storage.Contents), before), "the constructor turned ore into parts without making or losing any (leftovers stay in storage)");
    }

    void TestFifo()
    {
        SharedStorage storage = Storage(contents: Bill.Of(("iron-bar", 20), ("ice", 20), ("copper-bar", 10), ("graphite", 10)));
        Constructor fab = Fab("survival", storage);
        Check(fab.Order("o2-bottle", 3).Ok && fab.Order("energy-cell", 4).Ok, "two orders queue up");
        List<(string Item, Lane Lane)> starts = RunUntilIdle(fab);
        int lastO2 = starts.FindLastIndex(s => s.Item == "o2-bottle"), firstCell = starts.FindIndex(s => s.Item == "energy-cell");
        Check(lastO2 >= 0 && firstCell > lastO2, "the first order finishes before the second starts");
        Check(storage.Count("o2-bottle") == 3 && storage.Count("energy-cell") == 4, "both orders are delivered");
        Check(Math.Abs(fab.RunSeconds(_items.Item("o2-bottle").Recipe) - 2) < 1e-9, "the survival constructor runs at the recipe's own speed (a 2 s recipe takes 2 s)");
    }

    void TestMissing()
    {
        SharedStorage storage = Storage();
        Constructor fab = Fab("small", storage);
        fab.Order("winding", 2);
        // Two windings: 8 wire (2 copper bars = 4 copper ore) and 2 iron bars (4 iron ore).
        Check(fab.Missing.Equals(Bill.Of(("copper-ore", 4), ("iron-ore", 4))), $"it reports exactly the raw ore it lacks ({fab.Missing})");
        Check(fab.Status == "WAITING ON RAW MATERIALS", "the screen says it is waiting on raw materials");
        RunUntilIdle(fab, 60);
        Check(storage.Count("winding") == 0, "nothing is made without the ore");

        storage.TryAdd(Bill.Of(("copper-ore", 2)));
        Check(fab.Missing.Equals(Bill.Of(("copper-ore", 2), ("iron-ore", 4))), "dropping some ore in shrinks the missing list");
        storage.TryAdd(Bill.Of(("copper-ore", 2), ("iron-ore", 4)));
        Check(fab.Missing.IsEmpty, "with all the ore there nothing is missing");
        RunUntilIdle(fab);
        Check(storage.Count("winding") == 2 && !fab.Busy, "it resumes and finishes by itself");
    }

    void TestTiers()
    {
        SharedStorage storage = Storage(contents: Ores(100));
        Constructor survival = Fab("survival", storage), small = Fab("small", storage), large = Fab("large", storage);
        Check(survival.Order("circuit-board").Status == OrderStatus.NeedsHigherTier, "the survival constructor can't make circuit boards");
        Check(survival.Order("iron-plate", 4).Ok, "the survival constructor makes hull plates from ore");
        Check(small.Order("thrust-assembly").Status == OrderStatus.NeedsHigherTier, "a small constructor can't make a thrust assembly");
        Check(large.Order("thrust-assembly").Ok, "a large constructor can");
        Check(large.Order("warp-coil").Status == OrderStatus.NeedsHigherTier, "only an advanced constructor makes warp coils");
        Check(small.Order("lattice-core").Status == OrderStatus.NoRecipe, "unique finds can't be ordered");
        Check(small.Order("iron-ore").Status == OrderStatus.NoRecipe, "raw ore can't be ordered");
        double t = _items.Item("iron-plate").Recipe.Seconds;
        Check(Math.Abs(large.RunSeconds(_items.Item("iron-plate").Recipe) - t / 4) < 1e-9 && Math.Abs(survival.RunSeconds(_items.Item("iron-plate").Recipe) - t) < 1e-9,
            "a large constructor is four times as fast as the survival one");
        double full = small.RunSeconds(_items.Item("iron-plate").Recipe);
        Check(Math.Abs(full - t / 2) < 1e-9, "a small constructor is twice as fast as the recipe");
    }

    void TestCancelAndDrain()
    {
        SharedStorage storage = Storage(contents: Ores(100));
        var before = Raw(storage.Contents);
        Constructor fab = Fab("small", storage);
        OrderResult r = fab.Order("servo", 3);
        fab.Tick(5.3); // part way through, a run in progress
        Check(fab.Current != null, "a run is in progress");
        Check(fab.Cancel(r.Job), "the order can be cancelled");
        fab.Tick(1);
        Check(!fab.Busy && fab.SubParts.Count == 0, "the order and its sub-parts are gone");
        Check(Same(Raw(Sum(Raw(storage.Contents), Raw(fab.Tray))), before), "cancelling mid-run loses nothing: the run's inputs went back to storage");

        // Something the plan counted on leaves storage (a player places it as a block): it replans.
        storage = Storage(contents: Bill.Of(("copper-wire", 8), ("iron-ore", 10), ("copper-ore", 10)));
        fab = Fab("small", storage);
        fab.Order("winding", 2);
        Check(fab.SubParts.All(j => j.Item.Id != "copper-wire"), "wire in storage is used, not remade");
        storage.Remove("copper-wire", 8);
        RunUntilIdle(fab);
        Check(storage.Count("winding") == 2, "after the wire was taken, it made more wire and still finished");
    }

    // ---- Ammo restock -------------------------------------------------------------------------

    Constructor RestockFab(SharedStorage storage, int threshold, params string[] ammo)
    {
        Constructor fab = Fab("small", storage);
        fab.Restock.InUse = ammo;
        foreach (string a in ammo) fab.Restock.SetThreshold(a, threshold);
        fab.Restock.Enabled = true;
        return fab;
    }

    void TestRestockThreshold()
    {
        SharedStorage storage = Storage(contents: Ores(500).Add("shells-40mm", 2));
        var before = Raw(storage.Contents);
        Constructor fab = RestockFab(storage, 6, "shells-40mm");
        fab.Tick(0.01);
        Check(fab.RestockJobs.Count > 0 && fab.Restock.State == RestockState.Restocking, $"two boxes under a threshold of six starts a restock ({fab.Restock.Status})");
        Check(fab.Orders.Count == 0 && fab.SubParts.Count == 0, "restock jobs sit in their own lane, not the player's queues");
        RunUntilIdle(fab);
        Check(storage.Count("shells-40mm") == 6, $"it makes exactly enough to reach the threshold ({storage.Count("shells-40mm")} boxes)");
        Check(fab.Restock.State == RestockState.Stocked && fab.Restock.Status == "Ammo stocked", "then it says the ammo is stocked");
        for (int i = 0; i < 40; i++) fab.Tick(1);
        Check(storage.Count("shells-40mm") == 6 && fab.RestockJobs.Count == 0, "at the threshold nothing more is made");

        storage.TryAdd(Bill.Of(("shells-40mm", 3)));
        for (int i = 0; i < 40; i++) fab.Tick(1);
        Check(storage.Count("shells-40mm") == 9, "above the threshold nothing is made either");

        storage.Remove("shells-40mm", 7); // the guns fire
        fab.Tick(0.01);
        Check(fab.RestockJobs.Count(j => j.Item.Id == "shells-40mm") == 1 && fab.RestockJobs.Single(j => j.Item.Id == "shells-40mm").Count == 4,
            "firing down to two boxes queues exactly four");
        RunUntilIdle(fab);
        Check(storage.Count("shells-40mm") == 6, "and the stock is back at the threshold");

        // Conservation: what's in storage now is what went in, less the shells fired, plus the three added.
        var expected = Sum(before, Raw(Bill.Of(("shells-40mm", 3))));
        foreach (var (k, v) in Raw(Bill.Of(("shells-40mm", 7)))) expected[k] -= v;
        Check(Same(Raw(storage.Contents), expected), "restocking turns ore into ammo without making or losing any");

        // Two guns, two ammo types, each to its own threshold.
        storage = Storage(contents: Ores(500));
        fab = RestockFab(storage, 0, "belt-12mm", "rocket");
        fab.Restock.SetThreshold("belt-12mm", 3);
        fab.Restock.SetThreshold("rocket", 2);
        RunUntilIdle(fab);
        Check(storage.Count("belt-12mm") == 3 && storage.Count("rocket") == 2, "each ammo type stops at its own threshold");
    }

    void TestRestockToggle()
    {
        SharedStorage storage = Storage(contents: Ores(300));
        Constructor fab = RestockFab(storage, 5, "belt-12mm");
        fab.Restock.Enabled = false;
        for (int i = 0; i < 120; i++) fab.Tick(1);
        Check(storage.Count("belt-12mm") == 0 && fab.RestockJobs.Count == 0, "with the toggle off nothing is queued or made");
        Check(fab.Restock.State == RestockState.Off && fab.Restock.Status == "Ammo restock off", "and it says so");
        fab.Restock.Enabled = true;
        RunUntilIdle(fab);
        Check(storage.Count("belt-12mm") == 5, "switched on, it restocks");
        fab.Restock.Enabled = false;
        storage.Remove("belt-12mm", 5);
        for (int i = 0; i < 120; i++) fab.Tick(1);
        Check(storage.Count("belt-12mm") == 0, "switched off again, an empty magazine stays empty");
        fab.Restock.InUse = Array.Empty<string>();
        fab.Restock.Enabled = true;
        fab.Tick(1);
        Check(fab.Restock.State == RestockState.NoWeapons, "with no guns aboard there is nothing to restock");
    }

    void TestRestockPriority()
    {
        SharedStorage storage = Storage(contents: Ores(600));
        Constructor fab = RestockFab(storage, 4, "shells-40mm");
        OrderResult a = fab.Order("gun-barrel", 2), b = fab.Order("servo", 2);
        fab.Tick(0.01);
        Check(fab.RestockJobs.Count > 0, "restock jobs are queued alongside the player's");
        var orderSnapshot = fab.Orders.ToList();

        bool orderKept = true;
        var starts = new List<(string Item, Lane Lane)>();
        Job last = null;
        for (int i = 0; i < 4000 && fab.Busy; i++)
        {
            fab.Tick(0.25);
            Job now = fab.Current;
            if (now != null && now != last) starts.Add((now.Item.Id, now.Lane));
            last = now;
            var current = fab.Orders.ToList();
            if (!current.SequenceEqual(orderSnapshot.Where(current.Contains))) orderKept = false;
        }
        int firstRestock = starts.FindIndex(s => s.Lane == Lane.Restock), lastPlayer = starts.FindLastIndex(s => s.Lane != Lane.Restock);
        Note($"{starts.Count} jobs started; last player job at {lastPlayer}, first restock job at {firstRestock}");
        Check(firstRestock > lastPlayer, "no ammo was made while a job the player queued could run");
        Check(orderKept, "the player's orders kept their order the whole time");
        int lastA = starts.FindLastIndex(s => s.Item == "gun-barrel" && s.Lane == Lane.Order), firstB = starts.FindIndex(s => s.Item == "servo" && s.Lane == Lane.Order);
        Check(lastA >= 0 && firstB > lastA, "the first order still finished before the second");
        Check(storage.Count("gun-barrel") == 2 && storage.Count("servo") == 2 && storage.Count("shells-40mm") == 4, "everything was delivered, ammo last");
    }

    void TestRestockReserved()
    {
        // The player's order needs all nine iron bars but is stuck waiting for carbon. The bars
        // are reserved: the restock must not touch them.
        SharedStorage storage = Storage(contents: Bill.Of(("iron-bar", 9), ("propellant", 30)));
        Constructor fab = RestockFab(storage, 4, "shells-40mm");
        OrderResult barrels = fab.Order("gun-barrel", 3);
        for (int i = 0; i < 60; i++) fab.Tick(1);
        Check(storage.Count("iron-bar") == 9, "the reserved iron bars are untouched");
        Check(storage.Count("shells-40mm") == 0, "no ammo was made from reserved materials");
        Check(fab.Restock.State == RestockState.Paused, "the restock is paused");
        Check(fab.Restock.Status == "Ammo restock paused: need 24 iron ore", $"it says exactly what it needs: \"{fab.Restock.Status}\"");
        Check(fab.Missing.Equals(Bill.Of(("carbon-ore", 6))), "and the player's order still says what it needs");
        Check(fab.Orders.Single() == barrels.Job, "the player's order is still queued");

        storage.TryAdd(Bill.Of(("iron-ore", 12)));
        for (int i = 0; i < 300 && storage.Count("shells-40mm") < 2; i++) fab.Tick(1);
        Check(storage.Count("shells-40mm") == 2 && storage.Count("iron-bar") == 9, "half the ore makes two boxes, still without touching the reserved bars");
        Check(fab.Restock.Status == "Ammo restock paused: need 12 iron ore", $"and it pauses again for the rest: \"{fab.Restock.Status}\"");
        storage.TryAdd(Bill.Of(("iron-ore", 12)));
        for (int i = 0; i < 300 && storage.Count("shells-40mm") < 4; i++) fab.Tick(1);
        Check(storage.Count("shells-40mm") == 4 && fab.Restock.State == RestockState.Stocked, "the rest of the ore finishes the restock");

        storage.TryAdd(Bill.Of(("carbon-ore", 6)));
        RunUntilIdle(fab);
        Check(storage.Count("gun-barrel") == 3 && storage.Count("iron-bar") == 0, "and the player's order completes with its own bars");
    }

    // ---- Deconstructor ------------------------------------------------------------------------

    void TestDeconstructor()
    {
        var rules = new WorldRules();
        SharedStorage storage = Storage(contents: Bill.Of(("winding", 10), ("iron-ore", 5), ("lattice-core", 1)));
        var decon = new Deconstructor(_items, _items.Machines.Deconstructor, storage, rules);
        Check(Math.Abs(decon.ReturnRate - 0.5) < 1e-9, "the deconstructor returns 50%");

        decon.BreakItems("winding", 1);
        decon.Tick(60);
        // A winding is 4 wire and 1 iron bar: half is 2 wire and half a bar, which waits in the machine.
        Check(storage.Count("copper-wire") == 2 && storage.Count("iron-bar") == 0 && Math.Abs(decon.Carry.GetValueOrDefault("iron-bar") - 0.5) < 1e-9,
            "one winding gives 2 wire now and half an iron bar carried over");
        decon.BreakItems("winding", 9);
        decon.Tick(600);
        Check(storage.Count("copper-wire") == 20 && storage.Count("iron-bar") == 5 && decon.Carry.Count == 0, "ten windings give exactly half: 20 wire and 5 iron bars");
        Check(storage.Count("winding") == 0, "the windings went into the machine");

        Check(decon.BreakItems("iron-ore").Status == DeconStatus.CantBreak, "raw ore can't be broken down");
        Check(decon.BreakItems("lattice-core").Status == DeconStatus.CantBreak, "unique finds can't be broken down");
        Check(decon.BreakItems("servo", 1).Status == DeconStatus.NotEnough, "it can't break down what storage doesn't hold");

        // Wreck blocks: three iron-plated blocks and a thruster come back as half their parts, whole.
        Bill wreck = _items.Blocks.For("iron-plated").Times(3).Add(_items.Blocks.For("thruster-t1"));

        decon.BreakBlocks(wreck, 4 * 0.4, "wreck section");
        decon.Tick(60);
        Check(storage.Count("iron-plate") == 10 && storage.Count("nozzle") == 1 && storage.Count("polymer") == 1 && Math.Abs(decon.Carry.GetValueOrDefault("polymer") - 0.5) < 1e-9,
            "salvaged wreck blocks come back as whole parts at half: 10 iron plates, a nozzle, a polymer and a half carried");
        var half = Raw(wreck).ToDictionary(kv => kv.Key, kv => kv.Value * 0.5);
        Check(Same(Sum(Raw(Bill.Of(("iron-plate", 10), ("nozzle", 1), ("winding", 1), ("polymer", 1))), Raw(decon.Carry)), half), "salvage gives back exactly half the ore value, counting what the machine carries");

        // A full storage holds the parts in the machine until there is room.
        SharedStorage tiny = new(_items, "tiny", 0.2);
        var d2 = new Deconstructor(_items, _items.Machines.Deconstructor, tiny, rules);
        tiny.CapacitySu = 2;
        tiny.TryAdd(Bill.Of(("servo", 2)));
        d2.BreakItems("servo", 2);
        tiny.CapacitySu = 0.05;
        d2.Tick(60);
        Check(!d2.Tray.IsEmpty && tiny.IsEmpty, "with no room, the parts wait in the machine");
        tiny.CapacitySu = 100;
        d2.Tick(0.1);
        Check(d2.Tray.IsEmpty && tiny.Count("winding") == 1 && tiny.Count("iron-bar") == 2, "and come out when there is room");
    }

    void TestRoundTrip()
    {
        SharedStorage storage = Storage(contents: Ores(200));
        var before = Raw(storage.Contents);
        Constructor fab = Fab("small", storage);
        fab.Order("servo", 4);
        fab.Order("circuit-board", 3);
        RunUntilIdle(fab);
        Check(storage.Count("servo") == 4 && storage.Count("circuit-board") == 3, "the constructor made the parts");
        var decon = new Deconstructor(_items, _items.Machines.Deconstructor, storage, new WorldRules());
        Bill made = Bill.Of(("servo", 4), ("circuit-board", 3));
        decon.BreakItems("servo", 4);
        decon.BreakItems("circuit-board", 3);
        decon.Tick(600);
        var after = Sum(Raw(storage.Contents), Raw(decon.Carry));
        Check(AtMost(after, before), "after building and breaking down, there is never more of any ore than there was");
        var expected = Sum(before);
        foreach (var (k, v) in Raw(made)) expected[k] -= v * 0.5;
        Check(Same(after, expected), "exactly half the parts' ore came back; the rest is the deconstructor's cut");
    }

    // ---- Blueprint factory ----------------------------------------------------------------------

    void TestFactory()
    {
        var rules = new WorldRules();
        SharedStorage storage = Storage();
        var factory = new BlueprintFactory(_items, _items.Machines.Factory, storage, rules);
        var blocks = new List<BlockCount>
        {
            new("core", 1), new("iron-plated", 3), new("thruster-t1", 1), new("light", 1),
        };
        FactoryQuote q = factory.Quote("test pod", blocks);
        // Worked out by hand from blocks.json and recipes.json (see the README):
        //   parts: 36 iron plates, 3 polymer, 6 windings, 2 nozzles, 1 glass pane, 14 copper wire
        //   36 plates = 18 runs: 18 iron bars, 18 graphite; 6 windings: 24 wire, 6 iron bars;
        //   2 nozzles: 4 iron bars, 2 ceramic; 38 wire = 10 runs: 10 copper bars (2 over)
        Bill parts = Bill.Of(("iron-plate", 36), ("polymer", 3), ("winding", 6), ("nozzle", 2), ("glass", 1), ("copper-wire", 14));
        Bill bom = Bill.Of(("iron-bar", 28), ("graphite", 18), ("polymer", 3), ("ceramic", 2), ("glass", 1), ("copper-bar", 10));
        Bill spare = Bill.Of(("copper-wire", 2));
        double seconds = (18 * 2 + 6 * 3 + 2 * 4 + 10 * 1.5 + 6 * 0.4) / 4.0;
        Check(q.Parts.Equals(parts), $"the blocks' parts are summed exactly ({q.Parts})");
        Check(q.Materials.Equals(bom), $"the bill of materials is exact, in refined materials ({q.Materials})");
        Check(q.Byproducts.Equals(spare), $"rounding leftovers are known up front ({q.Byproducts})");
        Check(Math.Abs(q.Seconds - seconds) < 1e-9, $"the time is the parts' recipes plus fitting each block, at 4x ({q.Seconds:0.00} s)");
        Check(q.Materials.Ids.All(id => _items.Item(id).Category == ItemCategory.Refined), "it pays in refined materials, never ore");
        Check(Same(Raw(q.Materials), Sum(Raw(q.Parts), Raw(q.Byproducts))), "materials in = parts built + leftovers, exactly");

        // Refusal with the missing list.
        storage.TryAdd(Bill.Of(("iron-bar", 6), ("graphite", 5), ("glass", 1)));
        FactoryStart refused = factory.Start("test pod", blocks);
        Check(refused.Status == FactoryStartStatus.Missing, "short of materials, it refuses");
        Check(refused.Quote.Missing.Equals(Bill.Of(("iron-bar", 22), ("graphite", 13), ("polymer", 3), ("ceramic", 2), ("copper-bar", 10))),
            $"and lists exactly what is missing ({refused.Quote.Missing})");
        Check(storage.Count("iron-bar") == 6 && factory.Current == null, "a refusal takes nothing");

        storage.TryAdd(Bill.Of(("iron-bar", 22), ("graphite", 13), ("polymer", 3), ("ceramic", 2), ("copper-bar", 10)));
        FactoryQuote finished = null;
        factory.Finished += f => finished = f;
        FactoryStart started = factory.Start("test pod", blocks);
        Check(started.Ok && storage.IsEmpty, "with everything there it starts, taking the whole bill at once");
        Check(factory.Start("another", blocks).Status == FactoryStartStatus.Busy, "one blueprint at a time");
        factory.Tick(seconds - 0.5);
        Check(finished == null && factory.Progress > 0.9, "it takes its time");
        factory.Tick(1);
        Check(finished?.Name == "test pod", "then the structure is done");
        Check(storage.Contents.Equals(spare), "and the leftovers are back in storage");

        // The starter vessel, for the screen.
        var gadfly = BuildingBridge.BlocksOf(DataPaths.LoadBlueprint(DataPaths.Slice("starter.gbp")));
        FactoryQuote g = factory.Quote("Gadfly", gadfly);
        Note($"Gadfly from the factory: {_items.Describe(g.Materials)}; {g.Seconds / 60:0.0} min");
        Check(g.Problems.Count == 0 && g.BlockTotal == gadfly.Sum(b => b.Count), "the factory can price the whole starter vessel");
        Check(factory.Start("nothing", new List<BlockCount>()).Status == FactoryStartStatus.Empty, "an empty blueprint is refused");
        Check(factory.Start("odd", new List<BlockCount> { new("nosuchblock", 1) }).Status == FactoryStartStatus.UnknownBlocks, "a blueprint with an unknown block is refused");
    }

    // ---- Placement -----------------------------------------------------------------------------

    Structure Base(out Int3 container)
    {
        // A core and one large container, set up directly (not part of the sequence under test).
        var s = new Structure(_blocks);
        Building.Place(s, Int3.Zero, Building.Make(s, _blocks.CoreType));
        container = new Int3(0, 1, 0);
        Building.Place(s, container, Building.Make(s, _blocks.Type("container")));
        return s;
    }

    /// <summary>The item a block (by any id it has had) is placed from.</summary>
    ItemDef BlockItemOf(string block) => _items.BlockItem(BuildingBridge.ItemBlockOf(_blocks.Type(block)));
    string ItemOf(string block) => BlockItemOf(block).Id;

    void TestPlacementModes()
    {
        // Blocks are items: the source holds block items, and placing one uses one.
        Bill start = Bill.Of((ItemOf("steel"), 4), (ItemOf("glass"), 1), (ItemOf("thruster"), 1), (ItemOf("container"), 1), ("iron-plate", 60));
        var steps = new List<string>();
        foreach (GameMode mode in new[] { GameMode.Survival, GameMode.Creative })
        {
            var rules = new WorldRules(mode);
            var crates = new CrateYard(_items);
            var placer = new BlockPlacer(_items, rules, crates);
            Structure s = Base(out _);
            SharedStorage storage = new(_items, "base", BuildingBridge.StorageCapacity(s));
            storage.TryAdd(start);

            // The same sequence in each mode.
            var log = new List<(string what, PlaceStatus status, Bill paid)>();
            void Place(int x, int y, int z, string type, string shape = null)
            {
                Block b = Building.Make(s, _blocks.Type(type), shape != null ? _blocks.Shape(shape) : null);
                PlaceResult r = placer.Place(s, new Int3(x, y, z), b, storage);
                log.Add(($"{type}@{x},{y},{z}", r.Status, r.Paid));
            }
            Place(1, 0, 0, "steel");
            Place(2, 0, 0, "steel", "slope");
            Place(-1, 0, 0, "glass");
            Place(0, 0, 1, "thruster");
            Place(1, 1, 0, "container");
            Place(-1, 1, 0, "turret"); // on the glass block; nobody made a turret
            TakeDownResult down1 = placer.Remove(s, new Int3(2, 0, 0), storage);
            TakeDownResult down2 = placer.Remove(s, new Int3(1, 1, 0), storage);
            Place(2, 0, 0, "steel");

            if (mode == GameMode.Survival)
            {
                Check(log.Take(5).All(l => l.status == PlaceStatus.Placed), "survival: the blocks whose items are in storage are placed");
                Check(log[0].paid.Equals(Bill.Of((ItemOf("steel"), 1))) && log[1].paid.Equals(Bill.Of((ItemOf("steel"), 1))) && log[6].paid.Equals(Bill.Of((ItemOf("steel"), 1))),
                    "survival: a cube and a slope of steel each use one large steel block item (one family, any shape)");
                Check(log[3].paid.Equals(Bill.Of((ItemOf("thruster"), 1))), "survival: a thruster uses one thruster item");
                Check(log[5].status == PlaceStatus.NotEnoughMaterials, "survival: a turret with no turret item in storage is refused");
                Check(!s.Has(new Int3(-1, 1, 0)), "survival: and isn't placed");
                Check(down1.Refunded.Equals(Bill.Of((ItemOf("steel"), 1))) && down2.Refunded.Equals(Bill.Of((ItemOf("container"), 1))), "survival: taking a block down gives its item back");
                Bill placedNow = Bill.Of((ItemOf("steel"), 2), (ItemOf("glass"), 1), (ItemOf("thruster"), 1));
                Check(storage.Contents.Equals(start.Minus(placedNow)), $"survival: storage holds exactly the start less what is still built ({storage.Contents})");
                Check(storage["iron-plate"] == 60, "survival: placing never touches raw parts: only block items");
                Check(crates.Crates.Count == 0, "survival: with room in storage nothing spills");
            }
            else
            {
                Check(log.All(l => l.status == PlaceStatus.Placed), "creative: every placement succeeds, the turret too");
                Check(log.All(l => l.paid.IsEmpty) && down1.Refunded.IsEmpty && down2.Refunded.IsEmpty, "creative: nothing is used and nothing given back");
                Check(storage.Contents.Equals(start), "creative: storage is untouched");
                var empty = new SharedStorage(_items, "empty", 0);
                Check(placer.Place(s, new Int3(3, 0, 0), Building.Make(s, _blocks.Type("precursor")), empty).Ok, "creative: even unique-only blocks place with an empty storage");
            }
            steps.Add($"{mode}: {string.Join(", ", log.Select(l => $"{l.what} {l.status}"))}");
        }
        foreach (string line in steps) Note(line);
    }

    void TestCraftPlaceRemove()
    {
        // Craft a block item in a constructor, place it, take it down: the item is back.
        var rules = new WorldRules();
        var crates = new CrateYard(_items);
        var placer = new BlockPlacer(_items, rules, crates);
        Structure s = Base(out _);
        SharedStorage storage = new(_items, "base", BuildingBridge.StorageCapacity(s));
        storage.TryAdd(Bill.Of(("iron-ore", 200), ("carbon-ore", 100), ("stone", 10))); // polymer for the base blocks is made from stone
        Constructor fab = Fab("survival", storage, rules);
        string steel = ItemOf("steel");
        Check(fab.Order(steel, 2).Ok && fab.SubParts.Count > 0, $"the survival constructor takes an order for 2 iron-plated blocks and plans their parts ({fab.SubParts.Count} lines)");
        RunUntilIdle(fab);
        Check(storage[steel] == 2, "two iron-plated block items come out into the structure's storage");
        Block slope = Building.Make(s, _blocks.Type("steel"), _blocks.Shape("slope"));
        Check(placer.Place(s, new Int3(1, 0, 0), slope, storage).Ok && storage[steel] == 1, "placed as a slope, one item used");
        Check(placer.Remove(s, new Int3(1, 0, 0), storage).Ok && storage[steel] == 2, "taken down: the item is back");
        var empty = new SharedStorage(_items, "empty stack", 100);
        PlaceResult none = placer.Place(s, new Int3(1, 0, 0), slope, empty);
        Check(none.Status == PlaceStatus.NotEnoughMaterials && none.Missing.Equals(Bill.Of((steel, 1))) && !s.Has(new Int3(1, 0, 0)), "an empty stack can't place: the block isn't built");
        foreach (string shape in new[] { "cube", "slope", "corner", "invcorner", "wedge", "half", "plate" })
        {
            if (!_blocks.TryGetShape(shape, out ShapeDef sh)) continue;
            Check(placer.Place(s, new Int3(1, 0, 0), Building.Make(s, _blocks.Type("steel"), sh), storage).Ok && placer.Remove(s, new Int3(1, 0, 0), storage).Ok,
                $"one steel family item places as a {shape}");
        }
        Check(storage[steel] == 2 && crates.Crates.Count == 0, "and every shape gave the same item back");
    }

    void TestAutoSubParts()
    {
        // A deep item: a small thruster block is hull plates, a nozzle and a winding, each made
        // from refined materials made from ore.
        ItemDef thruster = BlockItemOf("thruster");
        SharedStorage storage = Storage(contents: Ores(400));
        Constructor fab = Fab("small", storage);
        Check(_items.Graph.Level(thruster) >= 3, $"a small thruster is {_items.Graph.Level(thruster)} levels deep");
        Check(fab.Order(thruster.Id, 1).Ok, "ordering a small thruster is accepted");
        var lines = fab.SubParts.Select(j => j.Item.Id).ToList();
        Check(lines.Contains("iron-plate") && lines.Contains("nozzle") && lines.Contains("winding") && lines.Contains("iron-bar"),
            $"it plans every intermediate, all the way down: {string.Join(", ", lines)}");
        Check(fab.SubParts.All(j => j.Feeds == fab.Orders[0]), "each auto sub-part says which order it feeds");
        RunUntilIdle(fab);
        Check(storage[thruster.Id] == 1, "and the block comes out");

        // Stock is used first.
        SharedStorage stocked = Storage(contents: _items.Blocks.For("thruster-t1"));
        Constructor fab2 = Fab("small", stocked);
        fab2.Order(thruster.Id, 1);
        Check(fab2.SubParts.Count == 0 && fab2.Missing.IsEmpty, "with the parts in stock it queues no sub-parts");
        RunUntilIdle(fab2);
        Check(stocked.Contents.Equals(Bill.Of((thruster.Id, 1))), "it uses them, and only them");

        // A raw material short: paused, with what and how much.
        SharedStorage poor = Storage(contents: Bill.Of(("iron-ore", 1)));
        Constructor fab3 = Fab("small", poor);
        fab3.Order(ItemOf("steel"), 1);
        Check(!fab3.Missing.IsEmpty && fab3.Blockers.Any(b => b.Contains("Iron ore") && b.Contains("not in storage")), $"a missing raw material is reported with its amount: {string.Join("; ", fab3.Blockers)}");

        // A sub-part this tier can't make: exactly which, and which constructor it needs.
        Constructor fab4 = Fab("survival", Storage(contents: Ores(400)));
        Check(!fab4.Order(thruster.Id, 1).Ok, "the survival constructor refuses a block it doesn't know");
        ItemDef hardened = BlockItemOf("hardened");
        Constructor fab5 = new(_items, new MachineDef { Id = "t", Name = "Test", Tag = "T", Kind = MachineKind.Constructor, Tier = 2, Speed = 1 }, Storage(contents: Ores(400)), new WorldRules());
        Check(fab5.Knows(hardened), "a large constructor knows hardened steel");
        // A part only a higher tier makes, deep inside something this tier knows: the order says
        // which part and which constructor it needs. Forced here by a block item whose recipe sits
        // at a lower tier than its parts, as the data can do with an explicit "tier".
        ItemDef over = _items.Items.FirstOrDefault(i => i.Recipe != null && _items.Graph.TierNeeded(i) > i.Recipe.Tier);
        if (over != null)
        {
            Constructor gated = new(_items, new MachineDef { Id = "t2", Name = "Test", Tag = "T", Kind = MachineKind.Constructor, Tier = over.Recipe.Tier, Speed = 1 }, Storage(contents: Ores(400)), new WorldRules());
            Check(gated.Order(over.Id, 1).Ok && gated.Blockers.Any(b => b.Contains("constructor")), $"{over.Name}: a sub-part beyond this tier is reported exactly: {string.Join("; ", gated.Blockers)}");
        }
        else
        {
            Constructor gated = Fab("small", Storage(contents: Ores(400)));
            gated.Lock = i => i.Recipe != null && i.Recipe.Tier > 0 ? "needs a large constructor" : null;
            gated.Order(thruster.Id, 1);
            Check(gated.Blockers.Any(b => b.Contains("nozzle") && b.Contains("constructor")), $"a sub-part this machine can't make is reported exactly: {string.Join("; ", gated.Blockers)}");
        }
        Constructor lowFab = Fab("small", Storage(contents: Ores(400)));
        Check(lowFab.Order(ItemOf("thruster-t3"), 1).Status == OrderStatus.NeedsHigherTier, "Thruster T3 needs a large constructor: the order says so");
        Constructor locked = Fab("small", Storage(contents: Ores(400)));
        locked.Lock = i => i.Id == "nozzle" ? "locked: level 9" : null;
        locked.Order(thruster.Id, 1);
        Check(!locked.SubParts.Any(j => j.Item.Id == "nozzle") && locked.Blockers.Any(b => b.Contains("nozzle") && b.Contains("locked: level 9")),
            $"a locked sub-part is not queued, and the order says which and why: {string.Join("; ", locked.Blockers)}");

        // Cancelling the parent cancels its unstarted auto-parts.
        SharedStorage again = Storage(contents: Ores(400));
        Constructor fab6 = Fab("small", again);
        OrderResult r = fab6.Order(thruster.Id, 1);
        Check(fab6.SubParts.Count > 0 && fab6.Cancel(r.Job) && fab6.SubParts.Count == 0 && fab6.Orders.Count == 0, "cancelling the parent cancels its auto sub-parts");
        Check(again.Contents.Equals(Ores(400)), "and storage is untouched");

    }

    void TestClasses()
    {
        var rules = new WorldRules();
        var placer = new BlockPlacer(_items, rules, new CrateYard(_items));
        SharedStorage storage = Storage();
        storage.TryAdd(Bill.Of((ItemOf("steel"), 4), (ItemOf("decon"), 1), (ItemOf("concrete"), 2), (ItemOf("thruster"), 1), (ItemOf("steel"), 2), (ItemOf("concrete"), 1)));
        Structure baseS = Base(out _);
        baseS.Mode = CoreMode.Base;
        Check(BuildingBridge.ClassOf(baseS) == StructureClass.Base, "a core in base mode is a base");
        Check(placer.Place(baseS, new Int3(1, 0, 0), Building.Make(baseS, _blocks.Type("concrete")), storage).Ok, "concrete goes on a base");
        Check(placer.Place(baseS, new Int3(-2, 0, 0), Building.Make(baseS, _blocks.Type("decon")), storage).Ok, "a deconstructor goes on a base");
        Check(placer.Place(baseS, new Int3(0, 0, 1), Building.Make(baseS, _blocks.Type("thruster")), storage).Ok, "a thruster goes on a base, idle until the core is switched");
        Check(Building.ToggleCoreMode(baseS) && BuildingBridge.ClassOf(baseS) == StructureClass.Capital, "in Capital Mode it is a capital vessel, with every block as it was");
        Structure capital = Base(out _);
        Check(BuildingBridge.ClassOf(capital) == StructureClass.Capital, "a new structure reads as Capital Mode until set");
        Check(placer.Place(capital, new Int3(1, 0, 0), Building.Make(capital, _blocks.Type("concrete")), storage).Ok, "concrete goes on a capital vessel: bases and capital vessels share every block");
        capital.Mode = CoreMode.Vessel;
        Check(BuildingBridge.ClassOf(capital) == StructureClass.Capital && placer.Place(capital, new Int3(2, 0, 0), Building.Make(capital, _blocks.Type("steel")), storage).Ok,
            "old data's Vessel Mode flies as Capital Mode, and takes the same blocks");
        capital.Mode = CoreMode.Hover;
        Check(BuildingBridge.ClassOf(capital) == StructureClass.Hover, "in Hover Mode it is a hover vessel");
    }

    void TestSpill()
    {
        var rules = new WorldRules();
        var crates = new CrateYard(_items);
        var placer = new BlockPlacer(_items, rules, crates);
        Structure s = Base(out Int3 firstContainer);
        SharedStorage storage = new(_items, "base", 0);
        placer.Sync(s, storage, 0, 0, 0);
        storage.TryAdd(Bill.Of((ItemOf("container"), 1), (ItemOf("steel"), 1)));
        Check(placer.Place(s, new Int3(1, 0, 0), Building.Make(s, _blocks.Type("container")), storage).Ok, "a second container goes on");
        placer.Sync(s, storage, 0, 0, 0);
        Check(Math.Abs(storage.CapacitySu - 2000) < 1e-6, "two T1 containers make 2,000 SU of shared storage");
        Check(placer.Place(s, new Int3(2, 0, 0), Building.Make(s, _blocks.Type("steel")), storage).Ok, "a steel block goes on");
        storage.TryAdd(Bill.Of(("stone", (int)storage.FreeSu)));
        var total = Sum(Raw(storage.Contents));
        Check(storage.FreeSu < 1, "storage is full");

        TakeDownResult r = placer.Remove(s, new Int3(2, 0, 0), storage, 4, 0, 0);
        Check(r.Ok && r.Spilled.Equals(Bill.Of((ItemOf("steel"), 1))) && r.Crate != null, "taking a block down into a full storage spills its item to a crate");
        Check(Same(Sum(Raw(storage.Contents), Raw(crates.Total)), Sum(total, Raw(Bill.Of((ItemOf("steel"), 1))))), "nothing is lost: storage plus crate hold everything");

        var beforeContainer = Sum(Raw(storage.Contents), Raw(crates.Total));
        TakeDownResult c = placer.Remove(s, new Int3(1, 0, 0), storage, 2, 0, 0);
        placer.Sync(s, storage, 2, 0, 0);
        Check(c.Ok && Math.Abs(storage.CapacitySu - 1000) < 1e-6 && !storage.OverCapacity, "taking a container away shrinks storage, and it is never over-full");
        Check(crates.Crates.Count == 1, "what no longer fits joins the nearby crate rather than littering");
        Check(Same(Sum(Raw(storage.Contents), Raw(crates.Total)), Sum(beforeContainer, Raw(Bill.Of((ItemOf("container"), 1))))),
            "storage plus crate still hold everything, the container's own item too");

        SpillCrate crate = crates.Crates[0];
        storage.Remove("stone", 500);
        Bill moved = crates.Collect(crate, storage);
        Check(!moved.IsEmpty && crates.Crates.Count == 1, "emptying part of a crate leaves the crate where it is: it never despawns");
        storage.Remove("stone", storage.Count("stone"));
        crates.Collect(crate, storage);
        Check(crates.Crates.Count == 0, "only an emptied crate goes away");
    }

    void TestUpgrade()
    {
        var crates = new CrateYard(_items);
        var placer = new BlockPlacer(_items, new WorldRules(), crates);
        Structure s = Base(out _);
        SharedStorage storage = new(_items, "base", BuildingBridge.StorageCapacity(s));
        Bill start = Bill.Of((ItemOf("steel"), 1), (ItemOf("hardened"), 1), (ItemOf("combat"), 1));
        storage.TryAdd(start);
        placer.Place(s, new Int3(1, 0, 0), Building.Make(s, _blocks.Type("steel"), _blocks.Shape("slope")), storage);
        PlaceResult up = placer.Upgrade(s, new Int3(1, 0, 0), storage);
        Check(up.Ok && up.Paid.Equals(Bill.Of((ItemOf("hardened"), 1))) && storage[ItemOf("steel")] == 1,
            "steel to hardened uses one hardened block item and gives the steel one back (Empyrion's upgrade)");
        Check(s.TypeOf(s.Get(new Int3(1, 0, 0))).Id == "hardened-plated" && s.ShapeOf(s.Get(new Int3(1, 0, 0))).Id == "slope", "the block is hardened and keeps its shape");
        placer.Upgrade(s, new Int3(1, 0, 0), storage);
        placer.Remove(s, new Int3(1, 0, 0), storage);
        Check(storage.Contents.Equals(start) && crates.Crates.Count == 0, "place, upgrade twice, take down: storage is exactly as it started");
    }

    void TestCreativeMachines()
    {
        var rules = new WorldRules(GameMode.Creative);
        SharedStorage storage = Storage();
        Constructor fab = Fab("small", storage, rules);
        Check(fab.Order("circuit-board", 3).Ok && fab.SubParts.Count == 0 && fab.Missing.IsEmpty, "creative: an order needs no sub-parts and misses nothing");
        RunUntilIdle(fab);
        Check(storage.Contents.Equals(Bill.Of(("circuit-board", 3))), "creative: the constructor makes parts from nothing, for testing");

        var factory = new BlueprintFactory(_items, _items.Machines.Factory, storage, rules);
        FactoryQuote done = null;
        factory.Finished += f => done = f;
        FactoryStart start = factory.Start("Gadfly", BuildingBridge.BlocksOf(DataPaths.LoadBlueprint(DataPaths.Slice("starter.gbp"))));
        Check(start.Ok && done != null && factory.Current == null, "creative: the factory spawns a blueprint at once");
        Check(storage.Contents.Equals(Bill.Of(("circuit-board", 3))), "creative: and takes nothing for it");

        var decon = new Deconstructor(_items, _items.Machines.Deconstructor, storage, rules);
        Check(decon.BreakItems("winding", 2).Ok, "creative: the deconstructor never refuses for want of the item");
        decon.Tick(60);
        Check(storage.Count("copper-wire") == 4 && storage.Count("iron-bar") == 1, "creative: and still shows what an item breaks into");

        // Switching to survival mid-queue replans with real materials.
        fab.Order("iron-plate", 4);
        rules.Mode = GameMode.Survival;
        fab.Tick(0.01);
        // Four plates are two runs: two iron bars (one is already in storage) and two graphite.
        Check(fab.Missing.Equals(Bill.Of(("iron-ore", 2), ("carbon-ore", 4))), $"switched to survival, the queue asks for real ore, using what storage has ({fab.Missing})");
    }

    void TestPerformance()
    {
        SharedStorage storage = Storage(contents: Ores(100000));
        double plan = BestOfThree(() =>
        {
            Constructor fab = Fab("advanced", storage);
            foreach (string id in new[] { "warp-coil", "fusion-core", "field-emitter", "rocket", "power-unit", "control-unit", "thrust-assembly", "logic-module" })
                fab.Order(id, 25);
        }, 5);
        Constructor busy = Fab("advanced", storage);
        foreach (string id in new[] { "rocket", "power-unit", "control-unit", "thrust-assembly", "logic-module", "servo", "gun-barrel" }) busy.Order(id, 40);
        busy.Restock.InUse = new[] { "shells-40mm", "belt-12mm", "rocket" };
        busy.Restock.Enabled = true;
        double tick = BestOfThree(() => busy.Tick(1.0 / 60), 30);
        Note($"planning eight 25-unit orders {plan:0.00} ms; a frame's tick with {busy.AllJobs.Count()} jobs and restock {tick:0.000} ms");
        Check(plan < 10, $"planning a big queue takes under 10 ms ({plan:0.00})");
        Check(tick < 1, $"a constructor's tick costs under a millisecond a frame ({tick:0.000})");
    }
}
