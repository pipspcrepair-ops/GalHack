#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A kit's palette seen by role, so the architect can build any faction's or theme's walls,
/// floors, lights and props. Symbols follow the kits' habits (# walls, _ floor, = roof, * light,
/// w window, k accent band, m machine); anything missing falls back to the first entry with the role.
/// </summary>
public sealed class Style
{
    public readonly Kit Kit;
    public readonly PaletteEntry Wall, InnerWall, Floor, Roof, Light, Window, Accent, Stripe, Machine, Ramp;

    public Style(Kit kit)
    {
        Kit = kit;
        PaletteEntry By(char c, Role r, BlockType? t = null) =>
            kit.BySymbol(c) ?? kit.Palette.FirstOrDefault(p => p.Role == r && (t == null || p.Type == t)) ?? kit.Palette[0];
        Wall = By('#', Role.Wall, BlockType.Cube);
        InnerWall = kit.BySymbol('%') is { Type: BlockType.Cube } iw ? iw : Wall;
        Floor = By('_', Role.Floor, BlockType.Cube);
        Roof = By('=', Role.Roof, BlockType.Cube);
        Light = By('*', Role.Light);
        Window = By('w', Role.Window);
        Accent = By('k', Role.Decor);
        Stripe = kit.BySymbol('t') ?? Accent;
        Machine = kit.Palette.FirstOrDefault(p => p.Type == BlockType.Machine) ?? Accent;
        Ramp = kit.Palette.FirstOrDefault(p => p.Type == BlockType.Ramp) ?? Floor;
    }

    public Rgb AccentColor => Accent.Colors[0];

    public Block Make(PaletteEntry e, Int3 cell, Dir facing = Dir.North) => e.Make(cell, facing);

    public Block Stair(Int3 cell, Dir rises)
    {
        var b = Floor.Make(cell, rises);
        b.Type = BlockType.Stair;
        b.Role = Role.Traverse;
        b.Color = b.Color.Scale(1.08f);
        return b;
    }
}

/// <summary>
/// A canvas's blocks by cell. Like <see cref="BlockGrid.Set"/>, a floor's finish stays under whatever is set down
/// on it (<see cref="Under"/>) and comes back when that goes; taking a cell out takes its finish with it.
/// </summary>
public sealed class BlockMap : Dictionary<Int3, Block>
{
    /// <summary>The floor finishes under what stands on them.</summary>
    public readonly Dictionary<Int3, Block> Under = new();

    /// <summary>The object being built now (0: the place as a whole); every block set is recorded as its.</summary>
    public int Object;

    /// <summary>Which object each block was built as (<see cref="Layout.Objects"/>), where one was begun.</summary>
    public readonly Dictionary<Int3, int> Objects = new();

    public new Block this[Int3 p]
    {
        get => base[p];
        set
        {
            Block was = TryGetValue(p, out Block w) ? w : Block.Air;
            if (BlockInfo.IsFinish(value.Type)) Under.Remove(p);
            else if (BlockInfo.IsFinish(was.Type) && !value.IsAir) Under[p] = was;
            else if (value.IsAir && Under.Remove(p, out Block floor)) value = floor;
            base[p] = value;
            // (A block stays the object's that built it: a neighbour painting it, a tank's band round the next tank, doesn't take it.)
            if (value.IsAir) Objects.Remove(p);
            else if (Object != 0 && (was.IsAir || !Objects.ContainsKey(p))) Objects[p] = Object;
        }
    }

    public new bool Remove(Int3 p)
    {
        Under.Remove(p);
        Objects.Remove(p);
        return base.Remove(p);
    }

    public new void Clear()
    {
        Under.Clear();
        Objects.Clear();
        base.Clear();
    }

    /// <summary>Lays a floor's finish in an open cell, or under what already stands there (never under a full cube).</summary>
    public void Pave(Int3 p, Block finish)
    {
        if (!TryGetValue(p, out Block at)) base[p] = finish;
        else if (!BlockInfo.IsFinish(at.Type) && !BlockInfo.IsFullCube(at.Type)) Under.TryAdd(p, finish);
    }
}

/// <summary>
/// Where the architect builds a place before it becomes a <see cref="Layout"/>: blocks, the cells
/// the place occupies (carved out of the ground below it), modules (rooms, corridors, stairs,
/// lifts), doorways, slots and the extras. Finish turns it into a layout the rest of the generator
/// (fixtures, states, dressing, validator, mesher) uses unchanged.
/// </summary>
public sealed class Canvas
{
    public readonly BlockMap Blocks = new();
    public readonly HashSet<Int3> Occ = new();
    public readonly List<PlacedModule> Modules = new();
    public readonly List<Doorway> Doorways = new();
    public readonly List<Slot> Slots = new();
    public readonly LayoutExtras X = new();
    /// <summary>The tech level it is built at, when the world says (high tech plans rooms three to four times larger: rule P2).</summary>
    public TechLevel? Tech;
    public readonly List<(int A, int B)> Edges = new();
    public Int3 EntranceCell;
    public Dir EntranceFacing = Dir.West;
    public List<Int3> ExitCells = new();
    public int EntranceModule = -1, VaultModule = -1, CoreModule = -1;
    public readonly Kit Kit;
    public readonly Style Style;
    readonly Dictionary<string, ModuleTemplate> _templates = new();

    public Canvas(Kit kit)
    {
        Kit = kit;
        Style = new Style(kit);
    }

    public void Set(Int3 p, Block b)
    {
        Blocks[p] = b;
        Occ.Add(p);
    }

    int _objects;

    /// <summary>Begins a new object (a building with all its wings, one tank, one container): every block set from here is its, until the next.</summary>
    public void Begin() => Blocks.Object = ++_objects;

    /// <summary>Open air that belongs to the place (carved out of the ground below ground level).</summary>
    public void Clear(Int3 p)
    {
        Blocks.Remove(p);
        Occ.Add(p);
    }

    /// <summary>Standing water over a cell's floor, claiming the cell: a surface drawn over the floor, never a block (<see cref="LayoutExtras.Water"/>).</summary>
    public void Wet(Int3 p)
    {
        X.Water.Add(p);
        Occ.Add(p);
    }

    /// <summary>Removes a block without claiming the cell (outside, open ground).</summary>
    public void Remove(Int3 p) => Blocks.Remove(p);

    public Block Get(Int3 p) => Blocks.TryGetValue(p, out var b) ? b : Block.Air;
    public bool Solid(Int3 p) => Blocks.TryGetValue(p, out var b) ? BlockInfo.Collides(b.Type) : p.Y <= Assembler.GroundY && !Occ.Contains(p) && !X.InSpace;

    public int AddModule(ModuleKind kind, string purpose, int storey, IReadOnlyCollection<Int3> cells, Kit? kit = null)
    {
        if (cells.Count == 0) throw new InvalidOperationException($"module {purpose} has no cells");
        Int3 min = cells.Aggregate(Int3.Min), max = cells.Aggregate(Int3.Max);
        string key = $"{(kit ?? Kit).Id}:{purpose}:{kind}";
        if (!_templates.TryGetValue(key, out var t))
            _templates[key] = t = new ModuleTemplate { Id = purpose, Kind = kind, Kit = kit ?? Kit };
        var shape = new ModuleShape { W = max.X - min.X + 1, H = max.Y - min.Y + 1, D = max.Z - min.Z + 1 };
        shape.Occupied = cells.Select(c => c - min).ToList();
        var m = new PlacedModule
        {
            Index = Modules.Count, Template = t, Shape = shape, Offset = min, Storey = storey, Purpose = purpose,
            SocketUsed = Array.Empty<bool>(), Step = -1, Object = Blocks.Object,
        };
        Modules.Add(m);
        foreach (Int3 c in cells) Occ.Add(c);
        return m.Index;
    }

    /// <summary>Gives a module its final cells (a big room, once what stands inside it is known).</summary>
    public void Reshape(int module, IReadOnlyCollection<Int3> cells)
    {
        var m = Modules[module];
        if (cells.Count == 0) throw new InvalidOperationException($"module {m.Purpose} has no cells");
        Int3 min = cells.Aggregate(Int3.Min), max = cells.Aggregate(Int3.Max);
        m.Offset = min;
        m.Shape = new ModuleShape { W = max.X - min.X + 1, H = max.Y - min.Y + 1, D = max.Z - min.Z + 1, Occupied = cells.Select(c => c - min).ToList() };
        foreach (Int3 c in cells) Occ.Add(c);
    }

    public int AddDoorway(List<Int3> cells, Dir facing, int a, int b)
    {
        Doorways.Add(new Doorway { Cells = cells, Facing = facing, ModuleA = a, ModuleB = b });
        foreach (Int3 c in cells) Clear(c);
        Edges.Add((a, b));
        return Doorways.Count - 1;
    }

    /// <summary>Gives a module its purpose once the planner has decided it.</summary>
    public void Retag(int module, ModuleKind kind, string purpose)
    {
        if (module < 0) return;
        var m = Modules[module];
        string key = $"{Kit.Id}:{purpose}:{kind}";
        if (!_templates.TryGetValue(key, out var t)) _templates[key] = t = new ModuleTemplate { Id = purpose, Kind = kind, Kit = Kit };
        m.Template = t;
        m.Purpose = purpose;
    }

    public void Link(int a, int b)
    {
        if (a >= 0 && b >= 0 && a != b) Edges.Add((a, b));
    }

    public void AddSlot(SlotKind kind, Int3 cell, int module) =>
        Slots.Add(new Slot { Kind = kind, Cell = cell, Module = module, Sure = true });

    /// <summary>Breadth-first module path through the doorway graph.</summary>
    public List<int> ModulePath(int from, int to)
    {
        var adj = new Dictionary<int, List<int>>();
        foreach (var (a, b) in Edges)
        {
            (adj.TryGetValue(a, out var la) ? la : adj[a] = new List<int>()).Add(b);
            (adj.TryGetValue(b, out var lb) ? lb : adj[b] = new List<int>()).Add(a);
        }
        var prev = new Dictionary<int, int> { [from] = from };
        var q = new Queue<int>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            int m = q.Dequeue();
            if (m == to) break;
            if (!adj.TryGetValue(m, out var next)) continue;
            foreach (int n in next.OrderBy(n => n))
                if (prev.TryAdd(n, m)) q.Enqueue(n);
        }
        if (!prev.ContainsKey(to)) return new List<int> { from, to };
        var path = new List<int> { to };
        while (path[^1] != from) path.Add(prev[path[^1]]);
        path.Reverse();
        return path;
    }

    public Layout Finish(FamilyDef family, VariantDef? variant, Rarity rarity, ulong seed, string name)
    {
        if (EntranceModule < 0) EntranceModule = 0;
        if (VaultModule < 0) VaultModule = Modules.Count - 1;

        Int3 min = EntranceCell, max = EntranceCell;
        foreach (Int3 p in Blocks.Keys) { min = Int3.Min(min, p); max = Int3.Max(max, p); }
        foreach (Int3 p in Occ) { min = Int3.Min(min, p); max = Int3.Max(max, p); }
        min -= new Int3(4, 2, 4);
        max += new Int3(4, 8, 4);
        if (!X.InSpace) max = Int3.Max(max, new Int3(max.X, 2, max.Z));
        var size = max - min + new Int3(1, 1, 1);

        var layout = new Layout
        {
            Family = family, Variant = variant, Name = name, Rarity = rarity, Seed = seed,
            Modules = Modules, Kit = Kit, Extras = X,
            Blocks = new BlockGrid(min, size), Occupied = new CellMask(min, size),
            EntranceCell = EntranceCell, EntranceFacing = EntranceFacing, ExitCells = ExitCells,
            Doorways = Doorways, Slots = Slots, EntranceModule = EntranceModule, VaultModule = VaultModule,
        };
        foreach (var (p, b) in Blocks) layout.Blocks.Set(p, b);
        foreach (var (p, f) in Blocks.Under) layout.Blocks.SetUnder(p, f);
        foreach (var (p, o) in Blocks.Objects) layout.Objects[p] = o;
        // Water stands only where nothing solid was built over it after (a column, a bridge pier).
        X.Water.RemoveWhere(p => BlockInfo.IsFullCube(Get(p).Type));
        foreach (Int3 p in Occ) layout.Occupied[p] = true;
        foreach (var (a, b) in Edges) layout.Links.Add(new Link(a, -1, b, -1));

        layout.CriticalPath = ModulePath(EntranceModule, VaultModule);
        for (int i = 0; i < layout.CriticalPath.Count; i++)
        {
            var m = Modules[layout.CriticalPath[i]];
            m.OnPath = true;
            m.Step = i;
        }
        layout.KeyModules = layout.CriticalPath.Where(i => Modules[i].Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault or ModuleKind.Cave).Distinct().ToList();
        layout.CoreModule = CoreModule;
        if (CoreModule >= 0 && !layout.KeyModules.Contains(CoreModule)) layout.KeyModules.Add(CoreModule);
        if (rarity >= Rarity.Rare)
        {
            int boss = layout.CriticalPath.Take(layout.CriticalPath.Count - 1)
                .LastOrDefault(i => Modules[i].Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Cave, -1);
            layout.BossModule = boss >= 0 ? boss : VaultModule;
        }
        return layout;
    }
}
