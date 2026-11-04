#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Someone waiting in a place. Shooter class and weapon come from the roster line, for the
/// combat code. Defender means the place's own faction, awake and on duty.
/// </summary>
public sealed record EnemySpawn(Int3 Cell, string Name, EnemyKind Kind, EnemyDef Def, int Module, bool Defender)
{
    public ShooterClass Shooter => Def.Shooter;
    public string Weapon => Def.Weapon;
}

public sealed class LootCache
{
    public Int3 Cell;
    public int Module;
    public bool InVault;
    public bool PickedOver;   // someone got here first
    public List<LootItem> Items = new();
    public int Value => Items.Sum(i => i.Value);
}

public sealed class TerminalLog
{
    public Int3 Cell;
    public int Module;
    public string Text = "";
    public bool MidSentence;
}

public sealed record StoryProp(string Kind, Int3 Cell, string Note);

/// <summary>One spawn of a layout: a state, its damage, and its dressing.</summary>
public sealed class PoiInstance
{
    public Layout Layout = null!;
    public PoiState State;
    public ulong SpawnSeed;
    public BlockGrid Blocks = null!;
    public CellMask Protected = null!;
    public Int3 CoreCell;
    public List<Int3> CriticalPath = new();
    public List<LootCache> Caches = new();
    public List<TerminalLog> Terminals = new();
    public List<EnemySpawn> Enemies = new();
    public List<StoryProp> Story = new();
    public List<Int3> Growths = new();
    public int StructureBefore, StructureAfter;

    public FamilyDef Family => Layout.Family;
    public Kit Kit => Layout.Kit;
    public Rarity Rarity => Layout.Rarity;
    public int LootValue => Caches.Sum(c => c.Value);
    public NavGrid Nav() => new(Blocks, Layout.Occupied);
    public float RemovedFraction => StructureBefore == 0 ? 0f : 1f - StructureAfter / (float)StructureBefore;
}

/// <summary>The front door of the generator: layout, then state, then dressing.</summary>
public static class PoiFactory
{
    /// <summary>A family's authored layout, or a fresh one from <paramref name="customSeed"/>.</summary>
    public static Layout BuildLayout(PoiLibrary lib, FamilyDef family, int variant, Rarity rarity, ulong? customSeed = null)
    {
        var v = family.Variants[variant % family.Variants.Count];
        Layout layout;
        using (Prof.Time("assemble")) layout = Assembler.Build(lib, family, v, rarity, customSeed ?? v.Seed);
        if (customSeed != null) layout.Name = $"{v.Name} (seed {customSeed})";
        return layout;
    }

    public static PoiInstance Spawn(PoiLibrary lib, Layout layout, PoiState state, ulong spawnSeed)
    {
        var inst = new PoiInstance { Layout = layout, State = state, SpawnSeed = spawnSeed, Blocks = layout.Blocks.Clone() };
        var rng = new Rng(Hash.Combine(layout.Seed ^ (ulong)layout.Rarity, spawnSeed, (ulong)state + 11));

        using (Prof.Time("fixtures")) Fixtures.Place(inst, rng.Fork("fixtures"));
        using (Prof.Time("protect")) Fixtures.Protect(inst);
        inst.StructureBefore = CountStructure(inst.Blocks);
        using (Prof.Time("state"))
        {
            if (layout.Family.Damage > 0f) StateTransforms.Wreck(inst, rng.Fork("wreck"), layout.Family.Damage);
            StateTransforms.Apply(inst, rng.Fork("state"));
        }
        inst.StructureAfter = CountStructure(inst.Blocks);
        using (Prof.Time("dressing")) Dressing.Apply(lib, inst, rng.Fork("dressing"));
        return inst;
    }

    public static PoiInstance Generate(PoiLibrary lib, FamilyDef family, int variant, Rarity rarity, PoiState state, ulong spawnSeed, ulong? customSeed = null) =>
        Spawn(lib, BuildLayout(lib, family, variant, rarity, customSeed), state, spawnSeed);

    public static int CountStructure(BlockGrid g)
    {
        int n = 0;
        foreach (var (_, b) in g.Blocks()) if (BlockInfo.IsStructure(b.Role)) n++;
        return n;
    }
}

/// <summary>Things every spawn has: the core, loot caches, terminals and doors.</summary>
public static class Fixtures
{
    public static readonly Rgb CoreColor = new(0x3d, 0xe8, 0xff);
    public static readonly Rgb CacheGold = new(0xe8, 0xb0, 0x30);

    public static void Place(PoiInstance inst, Rng rng)
    {
        var layout = inst.Layout;
        var g = inst.Blocks;
        var kit = inst.Kit;
        Rgb accent = (kit.BySymbol('k') ?? kit.FirstWithRole(Role.Decor)).Colors[0];

        // The core: hijack it to take the base, or destroy it and place your own.
        var coreSlot = layout.Slots.FirstOrDefault(s => s.Module == layout.CoreModule && s.Kind == SlotKind.Core);
        Int3 core = coreSlot?.Cell ?? FreeCell(inst, layout.CoreModule, rng);
        g.Set(core, new Block(BlockType.Core, Material.Lumen, CoreColor, Role.Core));
        inst.CoreCell = core;

        // Loot: every slot in the vault, about half of the others.
        foreach (var slot in layout.Slots.Where(s => s.Kind == SlotKind.Loot))
        {
            bool vault = slot.Module == layout.VaultModule;
            if (!vault && !rng.Chance(0.55f)) continue;
            g.Set(slot.Cell, new Block(BlockType.Cache, Material.Alloy, vault ? CacheGold : accent, Role.Loot, Dir.North, (byte)(vault ? 1 : 0)));
            inst.Caches.Add(new LootCache { Cell = slot.Cell, Module = slot.Module, InVault = vault });
        }
        if (!inst.Caches.Any(c => c.InVault))
        {
            Int3 c = FreeCell(inst, layout.VaultModule, rng);
            g.Set(c, new Block(BlockType.Cache, Material.Alloy, CacheGold, Role.Loot, Dir.North, 1));
            inst.Caches.Add(new LootCache { Cell = c, Module = layout.VaultModule, InVault = true });
        }

        // Terminals: most terminal slots get one; every place gets at least one.
        var termSlots = layout.Slots.Where(s => s.Kind == SlotKind.Terminal).ToList();
        var chosen = termSlots.Where(_ => rng.Chance(0.75f)).ToList();
        if (chosen.Count == 0 && termSlots.Count > 0) chosen.Add(termSlots[0]);
        if (chosen.Count == 0)
        {
            var prop = layout.Slots.FirstOrDefault(s => s.Kind == SlotKind.Prop && layout.CriticalPath.Contains(s.Module))
                       ?? layout.Slots.FirstOrDefault(s => s.Kind == SlotKind.Prop);
            if (prop != null) chosen.Add(prop);
        }
        foreach (var s in chosen)
        {
            g.Set(s.Cell, new Block(BlockType.Terminal, Material.Alloy, accent, Role.Terminal, FaceOpen(inst, s.Cell)));
            inst.Terminals.Add(new TerminalLog { Cell = s.Cell, Module = s.Module });
        }

        // Doors in every doorway, and at the way in. They never block a path.
        foreach (var d in layout.Doorways)
        {
            bool vaultDoor = d.ModuleB == layout.VaultModule;
            var b = new Block(BlockType.Door, Material.Steel, vaultDoor ? CacheGold.Scale(0.8f) : accent.Scale(0.85f), Role.Door, d.Facing,
                vaultDoor ? (byte)1 : (byte)0);
            foreach (Int3 c in d.Cells) g.Set(c, b);
        }
        foreach (Int3 c in layout.ExitCells)
            g.Set(c, new Block(BlockType.Door, Material.Steel, accent.Scale(0.85f), Role.Door, layout.EntranceFacing));
    }

    /// <summary>Faces a terminal toward open floor, so its screen looks into the room.</summary>
    static Dir FaceOpen(PoiInstance inst, Int3 c)
    {
        var nav = inst.Nav();
        foreach (Dir d in Dirs.Horizontal)
            if (nav.Standable(c + d.Vec())) return d;
        return Dir.North;
    }

    /// <summary>A standable floor cell in a module, nearest its middle.</summary>
    public static Int3 FreeCell(PoiInstance inst, int module, Rng rng)
    {
        var m = inst.Layout.Modules[module];
        var nav = inst.Nav();
        Int3 mid = new((m.Min.X + m.Max.X) / 2, m.Min.Y + 1, (m.Min.Z + m.Max.Z) / 2);
        return m.Cells.Where(c => inst.Blocks.Get(c).IsAir && nav.Standable(c))
            .OrderBy(c => c.Manhattan(mid)).FirstOrDefault(new Int3(m.Min.X + 1, m.Min.Y + 1, m.Min.Z + 1));
    }

    /// <summary>
    /// Marks the cells that keep every key place reachable: shortest paths from the way in to
    /// the vault, the core, each cache, each terminal and each key room, with headroom and the
    /// floor under them. Later passes may scar a place but never touch these cells.
    /// </summary>
    public static void Protect(PoiInstance inst)
    {
        var layout = inst.Layout;
        var nav = inst.Nav();
        var mask = new CellMask(inst.Blocks.Min, inst.Blocks.Size);
        inst.Protected = mask;
        var tree = nav.Tree(layout.EntranceCell);
        var reach = new HashSet<Int3>(tree.Keys);

        var targets = new List<Int3>();
        Int3? vaultAccess = null;
        foreach (var cache in inst.Caches.OrderByDescending(c => c.InVault))
        {
            mask[cache.Cell] = true;
            var a = nav.AccessCell(cache.Cell, reach);
            if (a is Int3 ac)
            {
                targets.Add(ac);
                if (cache.InVault) vaultAccess ??= ac;
            }
        }
        foreach (Int3 fixture in inst.Terminals.Select(t => t.Cell).Append(inst.CoreCell))
        {
            mask[fixture] = true;
            if (nav.AccessCell(fixture, reach) is Int3 a) targets.Add(a);
        }
        foreach (int k in layout.KeyModules)
            if (RoomCell(layout.Modules[k], reach) is Int3 rc) targets.Add(rc);

        foreach (Int3 t in targets)
        {
            var path = NavGrid.PathInTree(tree, t);
            if (path == null) continue;
            foreach (Int3 p in path)
            {
                mask[p] = true;
                mask[p + Int3.Up] = true;
                mask[p + Int3.Up * 2] = true;
                mask[p + Int3.Down] = true;
            }
            if (t == vaultAccess) inst.CriticalPath = path;
        }
        foreach (Int3 c in layout.ExitCells) mask[c] = true;
    }

    /// <summary>A reachable cell inside a module, nearest its middle: "you've been in this room".</summary>
    public static Int3? RoomCell(PlacedModule m, HashSet<Int3> reach)
    {
        Int3 mid = new((m.Min.X + m.Max.X) / 2, 0, (m.Min.Z + m.Max.Z) / 2);
        Int3? best = null;
        int bestD = int.MaxValue;
        foreach (Int3 c in m.Cells)
        {
            if (!reach.Contains(c)) continue;
            int d = System.Math.Abs(c.X - mid.X) + System.Math.Abs(c.Z - mid.Z) + System.Math.Abs(c.Y - m.Min.Y - 1);
            if (d < bestD)
            {
                bestD = d;
                best = c;
            }
        }
        return best;
    }
}
