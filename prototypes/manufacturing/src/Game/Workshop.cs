using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Items.Production;
using GalHack.Structures;

namespace Manufacturing.Game;

/// <summary>A blueprint the factory can build.</summary>
public sealed record BlueprintEntry(string Name, string Note, Structure Structure)
{
    public List<BlockCount> Blocks { get; } = BuildingBridge.BlocksOf(Structure);
}

/// <summary>
/// Everything in the workshop that isn't drawing: one base structure with its single shared
/// storage, the constructor, deconstructor and blueprint factory working from it, the wreck on
/// the salvage pad, spill crates, and the game mode they all read. Engine-free, so the probe can
/// drive it exactly as the scene does.
/// </summary>
public sealed class Workshop
{
    public ItemCatalogue Items { get; }
    public Catalogue Blocks { get; }
    public WorldRules Rules { get; } = new(GameMode.Survival);
    public CrateYard Crates { get; }
    public BlockPlacer Placer { get; }
    public Structure Base { get; }
    public SharedStorage Storage { get; }
    public Constructor Fab { get; }
    public Deconstructor Decon { get; }
    public BlueprintFactory Factory { get; }
    public Structure Wreck { get; private set; }
    public List<BlueprintEntry> Blueprints { get; } = new();
    public int Version { get; private set; }

    /// <summary>Raised when the factory finishes a blueprint: the scene gives it a body on the pad.</summary>
    public event Action<BlueprintEntry> Built;
    /// <summary>A short line for the HUD.</summary>
    public event Action<string> Toast;

    int _weaponsSeen = -1;
    double _salvageClock;
    readonly Queue<Int3> _salvage = new();

    public Workshop(ItemCatalogue items, Catalogue blocks, Structure baseStructure, Structure wreck, IEnumerable<BlueprintEntry> blueprints)
    {
        Items = items;
        Blocks = blocks;
        Crates = new CrateYard(items);
        Placer = new BlockPlacer(items, Rules, Crates);
        Base = baseStructure;
        Storage = new SharedStorage(items, "BASE STORAGE", BuildingBridge.StorageCapacity(Base));
        Wreck = wreck;
        Blueprints.AddRange(blueprints);

        // The base's machines are blocks on it; each runs from the base's one storage.
        var machines = BuildingBridge.MachinesOn(items, Base).Select(m => m.Machine).ToList();
        MachineDef fab = machines.Where(m => m.Kind == MachineKind.Constructor).OrderByDescending(m => m.Tier).FirstOrDefault() ?? items.Machines.Get("small");
        Fab = new Constructor(items, fab, Storage, Rules);
        Decon = new Deconstructor(items, items.Machines.Deconstructor, Storage, Rules);
        Factory = new BlueprintFactory(items, items.Machines.Factory, Storage, Rules);
        Factory.Finished += q =>
        {
            BlueprintEntry e = Blueprints.FirstOrDefault(b => b.Name == q.Name);
            if (e != null) Built?.Invoke(e);
            Say($"{q.Name.ToUpperInvariant()} IS READY ON THE PAD");
        };
        Fab.Restock.Enabled = true;
        SyncWeapons();
    }

    /// <summary>A starting stock: enough to build and order things straight away, and ammo below its thresholds.</summary>
    public void Stock()
    {
        Storage.TryAdd(Bill.Of(("iron-ore", 420), ("carbon-ore", 300), ("copper-ore", 160), ("silicon-ore", 140), ("ice", 40), ("stone", 60),
            ("cobalt-ore", 24), ("magnesium-ore", 16), ("gold-ore", 9),
            ("iron-plate", 64), ("glass", 12), ("winding", 6), ("servo", 3), ("circuit-board", 4),
            ("shells-40mm", 3), ("o2-bottle", 4), ("medkit", 2), ("echo-shard", 1)));
        // Blocks are items: a few made already, so the multi-tool can build straight away.
        Storage.TryAdd(Bill.Of(("iron-plated", 24), ("glass-plated", 6), ("container-t1", 1), ("generator-t1", 1), ("turret", 1)));
        Version++;
    }

    public void Tick(double dt)
    {
        Fab.Tick(dt);
        Decon.Tick(dt);
        Factory.Tick(dt);
        TickSalvage(dt);
        if (Base.Version != _weaponsSeen) SyncWeapons();
    }

    /// <summary>The guns aboard decide which ammo the constructor keeps stocked.</summary>
    void SyncWeapons()
    {
        _weaponsSeen = Base.Version;
        Fab.Restock.InUse = BuildingBridge.AmmoInUse(Items, Base);
        Placer.Sync(Base, Storage, 0, 2, 0);
    }

    void Say(string line) => Toast?.Invoke(line);

    // ---- Player actions -------------------------------------------------------------------

    /// <summary>Stands in for mining: one trip's worth of the common ores.</summary>
    public void Gather()
    {
        Bill load = Bill.Of(("iron-ore", 60), ("carbon-ore", 45), ("copper-ore", 30), ("silicon-ore", 25), ("ice", 10), ("stone", 15));
        Bill left = Storage.AddAll(load);
        Crates.Spill(left, 0, 1, 6); // on the floor by the core
        Say(left.IsEmpty ? "GATHERED 185 ORE" : "STORAGE FULL: THE REST WENT INTO A CRATE");
        Version++;
    }

    /// <summary>Test-fires every gun aboard: each spends a box of its ammo, if there is one.</summary>
    public void FireGuns()
    {
        var spent = new List<string>();
        foreach (var (_, b) in Base.Blocks())
        {
            string ammo = Items.Blocks.AmmoFor(Base.TypeOf(b).Id);
            if (ammo != null && Storage.Remove(ammo, 1) == 1) spent.Add(Items.Item(ammo).Name);
        }
        Say(spent.Count == 0 ? "THE GUNS ARE DRY" : "GUNS FIRED: " + string.Join(", ", spent.GroupBy(s => s).Select(g => $"{g.Count()} x {g.Key}")).ToUpperInvariant());
        Version++;
    }

    public void ToggleMode()
    {
        Rules.Mode = Rules.Creative ? GameMode.Survival : GameMode.Creative;
        Say(Rules.Creative ? "CREATIVE: PLACING IS FREE, MACHINES NEED NO MATERIALS" : "SURVIVAL: BLOCKS AND MACHINES USE SHARED STORAGE");
        Version++;
    }

    /// <summary>Breaks the wreck on the salvage pad down a block at a time.</summary>
    public bool StartSalvage()
    {
        if (Wreck == null || Wreck.Count == 0 || _salvage.Count > 0) return false;
        foreach (var (p, _) in Wreck.Blocks().OrderByDescending(x => x.pos.Y).ThenBy(x => x.pos.X).ThenBy(x => x.pos.Z)) _salvage.Enqueue(p);
        Say($"SALVAGING THE WRECK: {Wreck.Count} BLOCKS");
        return true;
    }

    public int SalvageLeft => _salvage.Count;

    void TickSalvage(double dt)
    {
        if (_salvage.Count == 0) return;
        _salvageClock += dt;
        while (_salvage.Count > 0)
        {
            if (!Wreck.TryGet(_salvage.Peek(), out Block b)) { _salvage.Dequeue(); continue; }
            double fit = BuildingBridge.AssemblySeconds(Items, Wreck, b);
            if (_salvageClock < fit / Decon.Def.Speed) break;
            _salvageClock -= fit / Decon.Def.Speed;
            Int3 p = _salvage.Dequeue();
            Bill bill = BuildingBridge.Priced(Items, Wreck, b) ? BuildingBridge.BillOf(Items, Wreck, b) : new Bill();
            Wreck.Clear(p);
            Decon.BreakBlocks(bill, fit, $"wreck {Wreck.TypeOf(b).Name.ToLowerInvariant()}");
        }
        if (_salvage.Count == 0) _salvageClock = 0;
        Version++;
    }
}
