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
    /// <summary>Danger tier from the place's rarity and how deep this spawn is.</summary>
    public int Tier { get; init; }
}

public sealed class LootCache
{
    public Int3 Cell;
    public int Module;
    public bool InVault;
    public bool PickedOver;   // someone got here first
    public bool Secret;       // behind a breakable wall
    /// <summary>The cache's tier: rarity, depth and danger together (higher is better loot).</summary>
    public int Tier;
    public List<LootItem> Items = new();
    public int Value => Items.Sum(i => i.Value);
}

/// <summary>A terminal: a prop for door control and lifts. No story text (the place tells its own story).</summary>
public sealed class TerminalLog
{
    public Int3 Cell;
    public int Module;
    public string Text = "";
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
    public bool HasCore;
    public Int3 CoreCell;
    public List<Int3> CriticalPath = new();
    public List<LootCache> Caches = new();
    public List<TerminalLog> Terminals = new();
    public List<EnemySpawn> Enemies = new();
    public List<StoryProp> Story = new();
    public List<Int3> Growths = new();
    public List<LightSpot> Lights = new();
    public List<SoundHook> Sounds = new();
    /// <summary>Tags for the view and a later pass: smoke, vents, spawn and patrol points.</summary>
    public List<Marker> Markers = new();
    public int StructureBefore, StructureAfter;

    public FamilyDef Family => Layout.Family;
    public Kit Kit => Layout.Kit;
    public Rarity Rarity => Layout.Rarity;
    public LayoutExtras Extras => Layout.Extras;
    public int LootValue => Caches.Sum(c => c.Value);
    public NavGrid Nav() => new(Blocks, Layout.Occupied, Layout.Extras.Lifts, Layout.Extras.InSpace);
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
        using (Prof.Time("assemble"))
            layout = family.Generator == Generator.Kit
                ? Assembler.Build(lib, family, v, rarity, customSeed ?? v.Seed)
                : Architect.Build(lib, family, v, rarity, customSeed ?? v.Seed);
        if (customSeed != null) layout.Name = $"{v.Name} (seed {customSeed})";
        return layout;
    }

    public static PoiInstance Spawn(PoiLibrary lib, Layout layout, PoiState state, ulong spawnSeed)
    {
        var inst = new PoiInstance
        {
            Layout = layout, State = state, SpawnSeed = spawnSeed, Blocks = layout.Blocks.Clone(),
            HasCore = layout.Family.HasCore(state),
        };
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
        using (Prof.Time("decay")) Decay.Apply(inst, rng.Fork("decay"));
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

/// <summary>Things every spawn has: the core (where a faction holds it), loot caches, terminals and doors.</summary>
public static class Fixtures
{
    public static readonly Rgb CoreColor = new(0x3d, 0xe8, 0xff);
    public static readonly Rgb CacheGold = new(0xe8, 0xb0, 0x30);
    public static readonly Rgb SecretViolet = new(0xa6, 0x4d, 0xff);

    public static void Place(PoiInstance inst, Rng rng)
    {
        var layout = inst.Layout;
        var g = inst.Blocks;
        var kit = inst.Kit;
        Rgb accent = (kit.BySymbol('k') ?? kit.FirstWithRole(Role.Decor)).Colors[0];

        // The core: only where a faction holds the place. Hijack it to take the base, or destroy it.
        if (inst.HasCore)
        {
            var coreSlot = layout.Slots.FirstOrDefault(s => s.Module == layout.CoreModule && s.Kind == SlotKind.Core)
                           ?? layout.Slots.FirstOrDefault(s => s.Kind == SlotKind.Core);
            int coreModule = layout.CoreModule >= 0 ? layout.CoreModule : layout.KeyModules.FirstOrDefault(k => k != layout.VaultModule, layout.VaultModule);
            Int3 core = coreSlot?.Cell ?? FreeCell(inst, coreModule, rng);
            g.Set(core, new Block(BlockType.Core, Material.Lumen, CoreColor, Role.Core));
            inst.CoreCell = core;
        }

        // Loot: every slot in the vault and every planned slot; about half of a kit's others.
        foreach (var slot in layout.Slots.Where(s => s.Kind == SlotKind.Loot))
        {
            bool vault = slot.Module == layout.VaultModule;
            if (!vault && !slot.Sure && !rng.Chance(0.55f)) continue;
            if (!g.Get(slot.Cell).IsAir) continue;
            var color = slot.Secret ? SecretViolet : vault ? CacheGold : accent;
            g.Set(slot.Cell, new Block(BlockType.Cache, Material.Alloy, color, Role.Loot, Dir.North, (byte)(vault || slot.Secret ? 1 : 0)));
            inst.Caches.Add(new LootCache { Cell = slot.Cell, Module = slot.Module, InVault = vault, Secret = slot.Secret });
        }
        if (!inst.Caches.Any(c => c.InVault))
        {
            Int3 c = FreeCell(inst, layout.VaultModule, rng);
            g.Set(c, new Block(BlockType.Cache, Material.Alloy, CacheGold, Role.Loot, Dir.North, 1));
            inst.Caches.Add(new LootCache { Cell = c, Module = layout.VaultModule, InVault = true });
        }

        // Terminals: most terminal slots get one; every kit place gets at least one. They run doors
        // and lifts; they hold no stories.
        var termSlots = layout.Slots.Where(s => s.Kind == SlotKind.Terminal).ToList();
        var chosen = termSlots.Where(s => s.Sure || rng.Chance(0.75f)).ToList();
        if (chosen.Count == 0 && termSlots.Count > 0) chosen.Add(termSlots[0]);
        foreach (var s in chosen)
        {
            if (!g.Get(s.Cell).IsAir) continue;
            g.Set(s.Cell, new Block(BlockType.Terminal, Material.Alloy, accent, Role.Terminal, FaceOpen(inst, s.Cell)));
            inst.Terminals.Add(new TerminalLog { Cell = s.Cell, Module = s.Module });
        }

        // Doors in every doorway, and at the way in. They never block a path; locked ones need a
        // keycard (or a hack, or a charge).
        var locks = layout.Extras.Locks.ToDictionary(l => l.Doorway);
        for (int i = 0; i < layout.Doorways.Count; i++)
        {
            var d = layout.Doorways[i];
            bool vaultDoor = d.ModuleB == layout.VaultModule;
            byte state = locks.TryGetValue(i, out var lk) ? (lk.Blast ? DoorState.Blast : DoorState.Locked) : vaultDoor ? (byte)1 : (byte)0;
            if (vaultDoor && state == 1 && layout.Family.Generator != Generator.Kit) state = DoorState.Locked;
            var color = lk != null ? (lk.Blast ? new Rgb(0x5a, 0x5c, 0x58) : ClearanceColor(lk.Clearance)) : vaultDoor ? CacheGold.Scale(0.8f) : accent.Scale(0.85f);
            var b = new Block(BlockType.Door, Material.Steel, color, Role.Door, d.Facing, state);
            foreach (Int3 c in d.Cells) g.Set(c, b);
        }
        foreach (Int3 c in layout.ExitCells)
            g.Set(c, new Block(BlockType.Door, Material.Steel, accent.Scale(0.85f), Role.Door, layout.EntranceFacing));
    }

    /// <summary>Keycard colours, shared with the doors they open.</summary>
    public static Rgb ClearanceColor(string clearance) => clearance switch
    {
        "staff" => new Rgb(0x3f, 0xcf, 0x5e),
        "science" => new Rgb(0x3d, 0x8b, 0xff),
        "security" => new Rgb(0xff, 0xbf, 0x2e),
        "command" => new Rgb(0xe0, 0x30, 0x30),
        _ => new Rgb(0x9a, 0xa3, 0xad),
    };

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
        return m.Cells.Where(c => inst.Blocks.Get(c).IsAir && nav.Standable(c) && !nav.LadderAt(c) && !nav.LiftAt(c))
            .OrderBy(c => c.Manhattan(mid)).FirstOrDefault(new Int3(m.Min.X + 1, m.Min.Y + 1, m.Min.Z + 1));
    }

    /// <summary>
    /// Marks the cells that keep every key place reachable: shortest paths from the way in to
    /// the vault, the core, each cache, each terminal, each keycard and each key room, with
    /// headroom and the floor under them. Later passes may scar a place but never touch these cells.
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
        var fixtures = inst.Terminals.Select(t => t.Cell).Concat(layout.Extras.Keys.Select(k => k.Cell));
        if (inst.HasCore) fixtures = fixtures.Append(inst.CoreCell);
        foreach (Int3 fixture in fixtures)
        {
            mask[fixture] = true;
            if (nav.AccessCell(fixture, reach) is Int3 a) targets.Add(a);
            else if (reach.Contains(fixture)) targets.Add(fixture);
        }
        foreach (int k in layout.KeyModules)
            if (RoomCell(layout.Modules[k], reach) is Int3 rc) targets.Add(rc);
        // Every room and storey stays reachable, so no state or growth ever seals one off.
        var hidden = new HashSet<int>(layout.Extras.Secrets.Select(s => s.Module));
        foreach (var m in layout.Modules.Where(m => !hidden.Contains(m.Index)))
            if (RoomCell(m, reach) is Int3 rc) targets.Add(rc);

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
        // Stairwells and lift shafts are never damaged, walls and all: flights hang from them.
        foreach (var m in layout.Modules.Where(m => m.Kind is ModuleKind.Stair or ModuleKind.Lift))
            for (int y = m.Min.Y - 1; y <= m.Max.Y + 1; y++)
                for (int z = m.Min.Z - 1; z <= m.Max.Z + 1; z++)
                    for (int x = m.Min.X - 1; x <= m.Max.X + 1; x++) mask[new Int3(x, y, z)] = true;
        // Stairs, ladders and lift shafts are never damaged, nor are hidden rooms' walls.
        foreach (var (p, b) in inst.Blocks.Blocks())
            if (b.Type is BlockType.Stair or BlockType.Ladder or BlockType.Breakable) { mask[p] = true; mask[p + Int3.Down] = true; }
        foreach (var s in layout.Extras.Secrets)
            foreach (Int3 c in layout.Modules[s.Module].Cells) mask[c] = true;
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
