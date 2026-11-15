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

    /// <summary>The rooms a person lives or works in, as against ways (stairs, lifts) and the outside.</summary>
    bool Inside(int m) => Modules[m].Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault or ModuleKind.Corridor;

    /// <summary>
    /// A door in every door-sized opening between two rooms (the owner: inner doors in doorways): a gap through a wall the
    /// standard door's width and height (<see cref="DoorSize"/>) under a solid head, a room's floor either side, becomes a
    /// doorway and so gets its door like any other: opened by hand, never locked.
    /// </summary>
    void InnerDoors()
    {
        var of = new Dictionary<Int3, int>();
        foreach (PlacedModule m in Modules) foreach (Int3 c in m.Cells) of.TryAdd(c, m.Index);
        var seen = new HashSet<Int3>(Doorways.SelectMany(d => d.Cells).Concat(ExitCells));
        bool Way(Int3 p) => Get(p) is var b && (b.IsAir || b.Type == BlockType.Plate && b.Role == Role.Floor) && !of.ContainsKey(p);
        foreach (PlacedModule m in Modules.Where(m => Inside(m.Index)).ToList())
            foreach (Int3 c in m.Cells.Where(c => c.Y == m.Min.Y).ToList())
                foreach (Dir h in Dirs.Horizontal)
                {
                    Int3 o = h.Vec(), along = h.RotateCw(1).Vec();
                    bool Opening(Int3 p) => Way(p) && Way(p + Int3.Up) && Solid(p + Int3.Up * DoorSize.Tall) && of.GetValueOrDefault(p - o, -1) == m.Index
                        && of.TryGetValue(p + o, out int to) && to != m.Index && Inside(to);
                    Int3 gap = c + o;
                    if (seen.Contains(gap) || !Opening(gap)) continue;
                    var cells = new List<Int3> { gap };
                    for (Int3 p = gap + along; Opening(p) && cells.Count <= DoorSize.Wide; p += along) cells.Add(p);
                    for (Int3 p = gap - along; Opening(p) && cells.Count <= DoorSize.Wide; p -= along) cells.Add(p);
                    seen.UnionWith(cells);
                    if (cells.Count != DoorSize.Wide || cells.Select(p => of[p + o]).Distinct().Count() > 1) continue;
                    // (Recorded as it stands: the floor's finish run through the opening stays under the door.)
                    Doorways.Add(new Doorway { Cells = cells.SelectMany(p => Enumerable.Range(0, DoorSize.Tall).Select(k => p + Int3.Up * k)).ToList(), Facing = h, ModuleA = m.Index, ModuleB = of[gap + o] });
                    Edges.Add((m.Index, of[gap + o]));
                }
    }

    /// <summary>
    /// A lamp in every room that has none, under its ceiling over the middle of its floor, in its kit's light: no room is
    /// left dark because its builder forgot it (the decorations library hangs the fitting of the room's age there).
    /// </summary>
    void Lamps()
    {
        var lit = new HashSet<int>(X.Lights.Select(l => l.Module));
        foreach (PlacedModule m in Modules.Where(m => Inside(m.Index) && m.Kind != ModuleKind.Corridor && !lit.Contains(m.Index)))
        {
            var floor = m.Cells.Where(c => c.Y == m.Min.Y).ToList();
            if (floor.Count < 4) continue;
            double ax = floor.Average(f => f.X), az = floor.Average(f => f.Z);
            Int3 mid = floor.MinBy(c => Math.Abs(c.X - ax) + Math.Abs(c.Z - az));
            int up = 1;
            while (up < 12 && !Solid(mid + Int3.Up * (up + 1))) up++;
            if (up < 2) continue;
            int wide = Math.Max(m.Max.X - m.Min.X, m.Max.Z - m.Min.Z) + 1;
            X.Lights.Add(new LightSpot { Cell = mid + Int3.Up * up, Color = Style.Light.Colors[0], Module = m.Index, Storey = m.Storey, Energy = 1.1f, Range = Math.Min(wide * 0.9f + 2f, 18f), Mode = LightMode.Steady });
        }
    }

    /// <summary>The rooms whose ceilings carry a building's services (refs 03: plant, boiler and pump rooms, workshops).</summary>
    static bool Service(string purpose) => purpose.Contains("plant") || purpose.Contains("generator") || purpose.Contains("boiler") || purpose.Contains("pump")
        || purpose.Contains("workshop") || purpose.Contains("engine") || purpose.Contains("turbine") || purpose.Contains("works");

    /// <summary>
    /// What a room's ceiling shows by what the room is for (refs 01: a frame on a 3 to 4 m structural module): a service
    /// room's pipes along its long wall under the slab; elsewhere, under a slab spanning 6 m or more, the frame's downstand
    /// beams across the short span every 4 m, wall to wall. A house finishes its own (<see cref="Homes"/>); a hall taller
    /// than 5 m is open to its roof and keeps it.
    /// </summary>
    void Ceilings()
    {
        if (X.Homes.Count > 0) return;
        foreach (PlacedModule m in Modules.Where(m => Inside(m.Index) && m.Kind != ModuleKind.Corridor))
        {
            var floor = m.Cells.Where(c => c.Y == m.Min.Y).ToHashSet();
            if (floor.Count < 12) continue;
            int x0 = floor.Min(c => c.X), x1 = floor.Max(c => c.X), z0 = floor.Min(c => c.Z), z1 = floor.Max(c => c.Z);
            bool alongX = x1 - x0 >= z1 - z0, service = Service(m.Purpose);
            int span = alongX ? z1 - z0 + 1 : x1 - x0 + 1;
            if (span < 6 && !service) continue;
            Int3 mid = floor.MinBy(c => Math.Abs(c.X - (x0 + x1) / 2) + Math.Abs(c.Z - (z0 + z1) / 2));
            int h = 1;
            while (h < 7 && !Solid(mid + Int3.Up * h)) h++;
            if (h < 3 || h > 5 || Get(mid + Int3.Up * h).Role is not (Role.Floor or Role.Roof)) continue;
            Block slab = Get(mid + Int3.Up * h);
            bool Under(Int3 c) => floor.Contains(c with { Y = m.Min.Y }) && Get(c).IsAir && Solid(c + Int3.Up);
            // Each line runs from wall to wall: every cell of it under the slab, and the cells past its ends solid.
            void Line(Int3 from, Int3 step, Block b)
            {
                var line = new List<Int3>();
                for (Int3 c = from; Under(c); c += step) line.Add(c);
                if (line.Count >= 3 && Solid(line[0] - step) && Solid(line[^1] + step)) foreach (Int3 c in line) Set(c, b);
            }
            int y = m.Min.Y + h - 1;
            if (service)
                Line(alongX ? new Int3(x0, y, z1) : new Int3(x1, y, z0), alongX ? new Int3(1, 0, 0) : new Int3(0, 0, 1),
                    new Block(BlockType.Pipe, Material.Steel, new Rgb(0x6a, 0x6e, 0x70), Role.Decor, alongX ? Dir.East : Dir.South));
            else
                for (int a = (alongX ? x0 : z0) + 3; a <= (alongX ? x1 : z1) - 3; a += 4)
                    Line(alongX ? new Int3(a, y, z0) : new Int3(x0, y, a), alongX ? new Int3(0, 0, 1) : new Int3(1, 0, 0),
                        new Block(BlockType.Beam, slab.Material, slab.Color.Scale(0.92f), Role.Decor, alongX ? Dir.South : Dir.East));
        }
    }

    public Layout Finish(FamilyDef family, VariantDef? variant, Rarity rarity, ulong seed, string name)
    {
        if (EntranceModule < 0) EntranceModule = 0;
        if (VaultModule < 0) VaultModule = Modules.Count - 1;
        InnerDoors();
        Lamps();
        Ceilings();

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
            Blocks = new BlockGrid(min, size) { Rounds = X.Rounds }, Occupied = new CellMask(min, size),
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
