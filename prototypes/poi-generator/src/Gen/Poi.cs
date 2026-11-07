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
/// <summary>
/// How old what's in a place is, for loot and dressing (and the decorations library's disorder
/// pass): <c>New</c> for places in use now (worked today: fresh food, hay, crops), <c>OldWorld</c>
/// for the long-abandoned (organics rotted away, metal rusted, plastics brittle), <c>Preserved</c>
/// for sealed, frozen or airless rooms, where frozen moments look sharpest.
/// </summary>
public enum Age : byte { New, OldWorld, Preserved }

/// <summary>Whether a room is out of the weather: in old-world places only what was left outdoors or under a breach has rotted away; sturdy indoor things survive faded and dusty.</summary>
public enum Exposure : byte { Indoors, Outdoors, Breached }

public sealed class PoiInstance
{
    /// <summary>The place's age as a whole; <see cref="AgeOf"/> gives a room's (a sealed vault keeps what's in it).</summary>
    public Age Age;

    public Age AgeOf(int module)
    {
        if (Age != Age.OldWorld || module < 0 || module >= Layout.Modules.Count) return Age;
        var m = Layout.Modules[module];
        bool sealedRoom = m.Kind == ModuleKind.Vault || m.Purpose is "cryo-bay" or "cold-store" or "vault" || Layout.Extras.Secrets.Any(s => s.Module == module)
                          || Layout.Extras.Locks.Any(l => l.Blast && (Layout.Doorways[l.Doorway].ModuleA == module || Layout.Doorways[l.Doorway].ModuleB == module));
        return sealedRoom ? Age.Preserved : Age.OldWorld;
    }

    /// <summary>Out in the weather: a yard, or a room open to the sky (a collapse breach, a ruin that lost its roof).</summary>
    /// <summary>A lived-in place's temper (settlements roll it; faction civilian places are peaceful until provoked).</summary>
    public Mood Mood;
    /// <summary>Not hostile by default: its guards keep order and turn on you only if you start shooting (the standing system).</summary>
    public bool Friendly;
    /// <summary>Townsfolk as data for the ambient routine to come; null where nobody lives.</summary>
    public Townsfolk? Townsfolk;
    /// <summary>How hard a nearby blast hit this place (0 untouched); set by the planet's siting.</summary>
    public float Blasted;
    /// <summary>Where something could have rooted, best first, and how many trees it wants at most (see <see cref="GrowthSpot"/>).</summary>
    public readonly List<GrowthSpot> Growth = new();
    public int GrowthWanted;
    /// <summary>The world it stands on, when placed on one: its tech levels decide the living's animals and gear.</summary>
    public WorldProfile? World;
    /// <summary>"New on old": held by people of a higher level than the ruins round it (the world round it shows the old level).</summary>
    public bool NewOnOld => World != null && World.NewOnOld(Layout.Family, State);
    /// <summary>The tech level it is built at on its world (the living at the post-collapse level, the old world at the pre-collapse one), or null off a world.</summary>
    public TechLevel? Tech => World?.LevelFor(Layout.Family, State);

    public Exposure ExposureOf(int module)
    {
        if (module < 0 || module >= Layout.Modules.Count) return Exposure.Outdoors;
        var m = Layout.Modules[module];
        if (m.Kind == ModuleKind.Outdoor) return Exposure.Outdoors;
        if (Layout.Extras.Breaches.Any(b => b.Module == module)) return Exposure.Breached;
        int top = Blocks.Min.Y + Blocks.Size.Y;
        foreach (Int3 c in m.Cells.Where(c => c.Y == m.Min.Y).Take(12))
        {
            bool sky = true;
            for (int y = c.Y + 1; y < top && sky; y++) sky = Blocks.Get(c with { Y = y }).IsAir;
            if (sky) return Exposure.Breached;
        }
        return Exposure.Indoors;
    }

    /// <summary>The whole place's age from its family and state: in use now, frozen or airless, or long abandoned.</summary>
    public static Age AgeFor(FamilyDef family, PoiState state, bool inSpace)
    {
        if (state == PoiState.Active && (family.Faction != Faction.None || family.Tags.Contains("settlement") || family.Tags.Contains("civilian"))) return Age.New;
        if (inSpace || family.Tags.Contains("ice") || family.Tags.Contains("preserved")) return Age.Preserved;
        return Age.OldWorld;
    }
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
    public static Layout BuildLayout(PoiLibrary lib, FamilyDef family, int variant, Rarity rarity, ulong? customSeed = null, LayoutHints? hints = null)
    {
        var v = family.Variants[variant % family.Variants.Count];
        Layout layout;
        using (Prof.Time("assemble"))
            layout = family.Generator == Generator.Kit
                ? Assembler.Build(lib, family, v, rarity, customSeed ?? v.Seed)
                : Architect.Build(lib, family, v, rarity, customSeed ?? v.Seed, hints);
        if (customSeed != null) layout.Name = $"{v.Name} (seed {customSeed})";
        Facade.Apply(layout, lib);
        return layout;
    }

    /// <summary>How many spawns were rolled again because decay cut the way to the vault or the core (tests read it).</summary>
    public static int Respawns;

    /// <summary>
    /// Spawns a layout in a state, then flood-fills it from the way in: if decay cut the way to the
    /// vault or the core (it shouldn't: those paths are protected), the spawn is rolled again from a
    /// new sub-seed, up to three times.
    /// </summary>
    public static PoiInstance Spawn(PoiLibrary lib, Layout layout, PoiState state, ulong spawnSeed, WorldProfile? world = null)
    {
        PoiInstance inst = SpawnOnce(lib, layout, state, spawnSeed, world);
        for (int attempt = 1; attempt <= 3 && !Reaches(inst); attempt++)
        {
            System.Threading.Interlocked.Increment(ref Respawns);
            inst = SpawnOnce(lib, layout, state, Hash.Combine(spawnSeed, (ulong)attempt, 0x5EEDUL), world);
        }
        return inst;
    }

    /// <summary>
    /// Repair after the fixtures go in: a side cache that ended up walled in by the other fixtures
    /// (a cache in a pocket behind another) is taken out rather than left unreachable.
    /// </summary>
    /// <summary>
    /// Repair after the fixtures go in: no cache or terminal stands on the plan's own way from the
    /// door to the vault (a stash set down in a narrow pass would close it). Those few move aside or go.
    /// </summary>
    static void ClearPath(PoiInstance inst)
    {
        var layout = inst.Layout;
        if (layout.VaultModule < 0) return;
        var plan = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
        var vault = layout.Modules[layout.VaultModule];
        Int3 goal = vault.Cells.Where(c => c.Y == vault.Min.Y && plan.Standable(c)).DefaultIfEmpty(layout.EntranceCell).First();
        var path = plan.FindPath(layout.EntranceCell, goal);
        if (path == null) return;
        foreach (Int3 c in path.Take(System.Math.Max(0, path.Count - 2)))
            foreach (Int3 q in new[] { c, c + Int3.Up })
            {
                var b = inst.Blocks.Get(q);
                if (b.Type is not (BlockType.Cache or BlockType.Terminal) || inst.Caches.Any(k => k.Cell == q && k.InVault)) continue;
                inst.Blocks.Set(q, Block.Air);
                inst.Caches.RemoveAll(k => k.Cell == q && !k.InVault);
                inst.Terminals.RemoveAll(t => t.Cell == q);
            }
    }

    static void Unblock(PoiInstance inst)
    {
        var nav = inst.Nav();
        if (!nav.Standable(inst.Layout.EntranceCell)) return;
        var reach = nav.Reachable(inst.Layout.EntranceCell);
        // A vault keeps at least one cache: only its extra ones can go.
        foreach (var c in inst.Caches.Where(c => !c.Secret).ToList())
            if (nav.AccessCell(c.Cell, reach) is not Int3 && (!c.InVault || inst.Caches.Count(o => o.InVault && o != c && nav.AccessCell(o.Cell, reach) is Int3) > 0))
            { inst.Blocks.Set(c.Cell, Block.Air); inst.Caches.Remove(c); }
        // A vault whose caches all ended up walled in by each other keeps one, moved where it can be reached;
        // and a reachable vault cache comes first, the one the vault is judged by.
        var vaultCaches = inst.Caches.Where(c => c.InVault).ToList();
        if (vaultCaches.Count > 0 && vaultCaches.All(c => nav.AccessCell(c.Cell, reach) is not Int3))
        {
            var keep = vaultCaches[0];
            var room = inst.Layout.Modules[keep.Module];
            foreach (Int3 cell in room.Cells.Where(c => c.Y == room.Min.Y && inst.Blocks.Get(c).IsAir && reach.Contains(c)).OrderBy(c => Hash.Cell(c, 433)))
            {
                var block = inst.Blocks.Get(keep.Cell);
                inst.Blocks.Set(keep.Cell, Block.Air);
                inst.Blocks.Set(cell, block);
                if (nav.AccessCell(cell, reach) is Int3) { keep.Cell = cell; break; }
                inst.Blocks.Set(cell, Block.Air);
                inst.Blocks.Set(keep.Cell, block);
            }
        }
        inst.Caches = inst.Caches.OrderBy(c => c.InVault && nav.AccessCell(c.Cell, reach) is not Int3).ToList();
    }

    /// <summary>A core that cut the way to the vault (it stood in a narrow pass) moves to the first spot in a big room that doesn't.</summary>
    static void RelocateCore(PoiInstance inst)
    {
        var g = inst.Blocks;
        Int3 original = inst.CoreCell;
        var core = g.Get(original);
        g.Set(original, Block.Air);
        var nav = inst.Nav();
        var rooms = inst.Layout.Modules.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall).OrderByDescending(m => m.Cells.Count()).Take(6);
        foreach (var m in rooms)
            foreach (Int3 c in m.Cells.Where(c => c.Y == m.Min.Y && g.Get(c).IsAir && nav.Standable(c)).OrderBy(c => Hash.Cell(c, 401)).Take(8))
            {
                g.Set(c, core);
                inst.CoreCell = c;
                if (Reaches(inst)) return;
                g.Set(c, Block.Air);
            }
        inst.CoreCell = original;
        g.Set(original, core);
    }

    static bool Reaches(PoiInstance inst)
    {
        var nav = inst.Nav();
        if (!nav.Standable(inst.Layout.EntranceCell)) return false;
        var reach = nav.Reachable(inst.Layout.EntranceCell);
        if (inst.Caches.Any(c => !c.Secret && nav.AccessCell(c.Cell, reach) is not Int3)) return false;
        return !inst.HasCore || nav.AccessCell(inst.CoreCell, reach) is Int3;
    }

    static PoiInstance SpawnOnce(PoiLibrary lib, Layout layout, PoiState state, ulong spawnSeed, WorldProfile? world)
    {
        var inst = new PoiInstance
        {
            Layout = layout, State = state, SpawnSeed = spawnSeed, Blocks = layout.Blocks.Clone(),
            HasCore = layout.Family.HasCore(state), World = world,
        };
        inst.Age = PoiInstance.AgeFor(layout.Family, state, layout.Extras.InSpace);
        var rng = new Rng(Hash.Combine(layout.Seed ^ (ulong)layout.Rarity, spawnSeed, (ulong)state + 11));

        using (Prof.Time("fixtures")) Fixtures.Place(inst, rng.Fork("fixtures"));
        ClearPath(inst);
        if (inst.HasCore && !Reaches(inst)) RelocateCore(inst);
        Unblock(inst);
        using (Prof.Time("protect")) Fixtures.Protect(inst);
        inst.StructureBefore = CountStructure(inst.Blocks);
        using (Prof.Time("state"))
        {
            if (layout.Family.Damage > 0f) StateTransforms.Wreck(inst, rng.Fork("wreck"), layout.Family.Damage);
            StateTransforms.Apply(inst, rng.Fork("state"));
        }
        inst.StructureAfter = CountStructure(inst.Blocks);
        StateTransforms.Weather(inst, rng.Fork("weather")); // a century or two after the fall
        using (Prof.Time("decay")) Decay.Apply(inst, rng.Fork("decay"));
        using (Prof.Time("dressing")) Dressing.Apply(lib, inst, rng.Fork("dressing"));
        Structure.DropUntethered(inst.Blocks); // cables and tendrils always hang from something
        // Repair after decay: anything the passes above left hanging in the air (a canopy cut off by a
        // ruin, a wall stub after the timber rotted) comes down. The flood fill in Spawn then checks the way in.
        var anchors = layout.Extras.InSpace ? LayoutCheck.StationAnchors(layout) : null;
        foreach (Int3 p in Structure.Floating(inst.Blocks, layout.Occupied, anchors, layout.Extras.Plain.Count > 0)) inst.Blocks.Set(p, Block.Air);
        Structure.DropUntethered(inst.Blocks);
        // A plain ruin's weeds and moss come last, on what's left.
        if (layout.Extras.Plain.Count > 0 && state != PoiState.Active) StateTransforms.Overgrow(inst, rng.Fork("overgrow"));
        if (state != PoiState.Active) StateTransforms.Rest(inst);
        inst.Growth.RemoveAll(s => !Growth.Rootable(inst, s.Cell));
        return inst;
    }

    /// <param name="world">The world's profile, when it stands on one: the living's animals follow its post-collapse tech level.</param>
    public static PoiInstance Generate(PoiLibrary lib, FamilyDef family, int variant, Rarity rarity, PoiState state, ulong spawnSeed, ulong? customSeed = null, WorldProfile? world = null) =>
        Spawn(lib, BuildLayout(lib, family, variant, rarity, customSeed), state, spawnSeed, world);

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
            var b = layout.Extras.PlainDoor is Rgb leaf ? new Block(BlockType.Door, Material.Timber, leaf, Role.Door, d.Facing, DoorState.Closed) : new Block(BlockType.Door, Material.Steel, color, Role.Door, d.Facing, state);
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
