#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>A module template placed in the world: which rotation, where, and how it joins.</summary>
public sealed class PlacedModule
{
    public int Index;
    public ModuleTemplate Template = null!;
    public ModuleShape Shape = null!;
    public int Rotation;
    public Int3 Offset;
    public string Alias = "main";
    public bool[] SocketUsed = Array.Empty<bool>();
    public int Parent = -1;
    public int Step;            // position on the critical path, or -1 for branches
    public bool OnPath;

    public ModuleKind Kind => Template.Kind;
    public Int3 Min => Offset;
    public Int3 Max => Offset + new Int3(Shape.W - 1, Shape.H - 1, Shape.D - 1);
    public Int3 World(Int3 local) => local + Offset;
    public bool Underground => Max.Y <= Assembler.CoverY;
    public IEnumerable<Int3> Cells => Shape.Occupied.Select(World);
    public override string ToString() => $"#{Index} {Template} r{Rotation} at {Offset}";
}

public readonly record struct Link(int A, int SocketA, int B, int SocketB);

public sealed class Slot
{
    public SlotKind Kind;
    public Int3 Cell;
    public int Module;
}

/// <summary>A doorway where two modules join: the cells on the child's side, facing out of the child.</summary>
public sealed class Doorway
{
    public List<Int3> Cells = new();
    public Dir Facing;
    public int ModuleA, ModuleB;
}

/// <summary>A socket nobody connected to, walled up.</summary>
public sealed class CappedSocket
{
    public int Module;
    public List<Int3> Cells = new();
    public Dir Facing;
}

/// <summary>The assembled building, before any state or dressing.</summary>
public sealed class Layout
{
    public FamilyDef Family = null!;
    public VariantDef? Variant;
    public string Name = "";
    public Rarity Rarity;
    public ulong Seed;
    public List<PlacedModule> Modules = new();
    public List<Link> Links = new();
    public List<int> CriticalPath = new();
    public BlockGrid Blocks = null!;
    public CellMask Occupied = null!;
    public Int3 EntranceCell;      // standing outside the way in
    public Dir EntranceFacing;     // pointing out of the building
    public List<Int3> ExitCells = new();
    public List<Slot> Slots = new();
    public List<Doorway> Doorways = new();
    public List<CappedSocket> Capped = new();
    public int EntranceModule, VaultModule, CoreModule = -1, BossModule = -1;
    public List<int> KeyModules = new();
    public int Attempts;
    public Kit Kit = null!;

    public PlacedModule Vault => Modules[VaultModule];
    public bool Underground => Modules.Any(m => m.Underground);
}

/// <summary>
/// Joins modules through their sockets: a critical path from the entrance to the vault, then
/// optional side branches. Modules never overlap; every failed attempt restarts from a new
/// sub-seed, so the result is still a pure function of the seed.
/// </summary>
public static class Assembler
{
    /// <summary>World y of a surface module's floor layer; you walk at y = 0, the terrain top.</summary>
    public const int GroundY = -1;

    /// <summary>
    /// The highest layer an underground module may use. A roof here sits flush with the
    /// ground, like a bunker slab; nothing may poke above it.
    /// </summary>
    public const int CoverY = -1;

    const int MaxAttempts = 80;
    const int ApronDepth = 4;

    public static Layout Build(PoiLibrary lib, FamilyDef family, VariantDef? variant, Rarity rarity, ulong seed)
    {
        variant ??= family.Variants[0];
        var root = new Rng(Hash.Combine(seed, (ulong)rarity + 1, 0xA55E4B1EUL));
        List<KindRef> steps = ExpandPath(variant, rarity, root.Fork("path"));

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var rng = root.Fork("attempt" + attempt);
            var state = new State(lib, family);
            if (!PlacePath(state, steps, rng)) continue;
            AddBranches(state, variant, rarity, rng);
            var layout = Finish(state, family, variant, rarity, seed, rng);
            layout.Attempts = attempt + 1;
            return layout;
        }
        throw new InvalidOperationException($"{family.Id} '{variant.Name}' ({rarity}, seed {seed}): no layout after {MaxAttempts} attempts");
    }

    /// <summary>Rolls repeat counts, then grows the path before the vault as rarity rises.</summary>
    static List<KindRef> ExpandPath(VariantDef v, Rarity rarity, Rng rng)
    {
        var steps = new List<KindRef>();
        foreach (var s in v.Path)
        {
            int n = rng.Between(s.Min, s.Max);
            for (int i = 0; i < n; i++) steps.Add(new KindRef(s.Alias, s.Kind));
        }
        int grow = rarity switch { Rarity.Uncommon => 2, Rarity.Rare => 4, _ => 0 };
        if (v.Grow.Count > 0)
            for (int i = 0; i < grow; i++)
                steps.Insert(steps.Count - 1, v.Grow[i % v.Grow.Count]);
        return steps;
    }

    sealed class State
    {
        public readonly PoiLibrary Lib;
        public readonly FamilyDef Family;
        public readonly List<PlacedModule> Modules = new();
        public readonly List<Link> Links = new();
        public readonly HashSet<Int3> Taken = new();
        public readonly List<int> Path = new();

        public State(PoiLibrary lib, FamilyDef family)
        {
            Lib = lib;
            Family = family;
        }
    }

    static bool PlacePath(State s, List<KindRef> steps, Rng rng)
    {
        // The entrance sits at the origin, floor layer on the ground.
        var entrances = s.Lib.Modules(s.Family, steps[0].Alias, ModuleKind.Entrance);
        var tmpl = rng.WeightedOrder(entrances, m => m.Weight)[0];
        int rot = rng.Range(0, 4);
        var shape = tmpl.Rotations[rot];
        var entrance = new PlacedModule
        {
            Index = 0, Template = tmpl, Shape = shape, Rotation = rot, Alias = steps[0].Alias,
            Offset = new Int3(-shape.W / 2, GroundY, -shape.D / 2),
            SocketUsed = new bool[shape.Sockets.Count], Step = 0, OnPath = true,
        };
        Add(s, entrance);
        ReserveApron(s, entrance);
        s.Path.Add(0);

        PlacedModule current = entrance;
        for (int i = 1; i < steps.Count; i++)
        {
            var placed = TryAttach(s, current, steps[i], rng, i);
            if (placed == null) return false;
            placed.Step = i;
            placed.OnPath = true;
            s.Path.Add(placed.Index);
            current = placed;
        }
        return true;
    }

    /// <summary>Nothing may be built across the ground in front of the way in.</summary>
    static void ReserveApron(State s, PlacedModule entrance)
    {
        var exit = entrance.Shape.Sockets.First(k => k.Exterior);
        Int3 out1 = exit.Facing.Vec();
        Int3 side = exit.Facing.RotateCw(1).Vec();
        foreach (Int3 c in exit.Cells)
            for (int d = 1; d <= ApronDepth; d++)
                for (int l = -2; l <= 2; l++)
                    for (int h = 0; h < 4; h++)
                        s.Taken.Add(entrance.World(c) + out1 * d + side * l + Int3.Up * h);
    }

    static void Add(State s, PlacedModule m)
    {
        s.Modules.Add(m);
        foreach (Int3 c in m.Cells) s.Taken.Add(c);
    }

    /// <summary>
    /// Tries every open socket of <paramref name="from"/> against every candidate module,
    /// rotation and matching socket, in a seeded order, and keeps the first that fits.
    /// </summary>
    static PlacedModule? TryAttach(State s, PlacedModule from, KindRef kind, Rng rng, int step)
    {
        var candidates = s.Lib.Modules(s.Family, kind.Alias, kind.Kind);
        if (candidates.Count == 0) return null;

        var openSockets = Enumerable.Range(0, from.Shape.Sockets.Count)
            .Where(i => !from.SocketUsed[i] && !from.Shape.Sockets[i].Exterior).ToList();
        rng.Shuffle(openSockets);

        foreach (int si in openSockets)
        {
            SocketDef socket = from.Shape.Sockets[si];
            var cells = socket.Cells.Select(from.World).ToList();
            Int3 target = cells.Aggregate(Int3.Min) + socket.Facing.Vec();
            bool belowGround = target.Y < 0;

            foreach (var tmpl in rng.WeightedOrder(candidates, m => m.Weight))
            {
                var rotations = new List<int> { 0, 1, 2, 3 };
                rng.Shuffle(rotations);
                foreach (int rot in rotations)
                {
                    var shape = belowGround ? tmpl.Buried[rot] : tmpl.Rotations[rot];
                    for (int ti = 0; ti < shape.Sockets.Count; ti++)
                    {
                        var t = shape.Sockets[ti];
                        if (t.Exterior || t.Facing != socket.Facing.Opposite()) continue;
                        if (t.Width != socket.Width || t.Height != socket.Height) continue;
                        // A descent is always entered from its top, so paths only go down.
                        if (tmpl.Kind == ModuleKind.Descent && t.Min.Y != shape.Sockets.Max(k => k.Min.Y)) continue;

                        Int3 offset = target - t.Min;
                        if (!Fits(s, shape, offset, belowGround)) continue;

                        var placed = new PlacedModule
                        {
                            Index = s.Modules.Count, Template = tmpl, Shape = shape, Rotation = rot, Offset = offset,
                            Alias = kind.Alias, SocketUsed = new bool[shape.Sockets.Count], Parent = from.Index, Step = step,
                        };
                        placed.SocketUsed[ti] = true;
                        from.SocketUsed[si] = true;
                        Add(s, placed);
                        s.Links.Add(new Link(from.Index, si, placed.Index, ti));
                        return placed;
                    }
                }
            }
        }
        return null;
    }

    static bool Fits(State s, ModuleShape shape, Int3 offset, bool belowGround)
    {
        // Below ground everything stays under at least a layer of rock.
        if (belowGround && offset.Y + shape.H - 1 > CoverY) return false;
        foreach (Int3 c in shape.Occupied)
            if (s.Taken.Contains(c + offset)) return false;
        return true;
    }

    static void AddBranches(State s, VariantDef v, Rarity rarity, Rng rng)
    {
        if (v.BranchChains.Count == 0) return;
        int count = rng.Between(v.BranchMin, v.BranchMax) + (v.BranchMax > 0 ? (int)rarity : 0);
        for (int b = 0; b < count; b++)
        {
            var chain = rng.Pick(v.BranchChains);
            // Below ground, side rooms hang off the deep levels, not the shaft house.
            var hosts = s.Path.Select(i => s.Modules[i])
                .Where(m => m.Kind != ModuleKind.Vault && m.Kind != ModuleKind.Descent)
                .Where(m => !s.Family.Underground || m.Underground)
                .Where(m => m.Shape.Sockets.Where((k, i) => !k.Exterior && !m.SocketUsed[i]).Any())
                .ToList();
            if (hosts.Count == 0) return;
            rng.Shuffle(hosts);
            foreach (var host in hosts)
            {
                PlacedModule? at = host;
                bool any = false;
                foreach (var kind in chain)
                {
                    at = TryAttach(s, at!, kind, rng, -1);
                    if (at == null) break;
                    at.Step = -1;
                    any = true;
                }
                if (any) break;
            }
        }
    }

    static Layout Finish(State s, FamilyDef family, VariantDef v, Rarity rarity, ulong seed, Rng rng)
    {
        var layout = new Layout
        {
            Family = family, Variant = v, Name = v.Name, Rarity = rarity, Seed = seed,
            Modules = s.Modules, Links = s.Links, CriticalPath = s.Path,
            Kit = s.Lib.KitFor(family),
            EntranceModule = 0, VaultModule = s.Path[^1],
        };

        // Bounds: every module, the apron, and room around for growths and debris.
        Int3 min = s.Modules.Select(m => m.Min).Aggregate(Int3.Min);
        Int3 max = s.Modules.Select(m => m.Max).Aggregate(Int3.Max);
        foreach (Int3 t in s.Taken)
        {
            min = Int3.Min(min, t);
            max = Int3.Max(max, t);
        }
        min -= new Int3(3, 2, 3);
        max += new Int3(3, 8, 3);
        layout.Blocks = new BlockGrid(min, max - min + new Int3(1, 1, 1));
        layout.Occupied = new CellMask(min, max - min + new Int3(1, 1, 1));

        foreach (var m in s.Modules) Stamp(layout, m);

        // Doorways and the way in.
        foreach (var link in s.Links)
        {
            var child = s.Modules[link.B];
            var sock = child.Shape.Sockets[link.SocketB];
            layout.Doorways.Add(new Doorway
            {
                Cells = sock.Cells.Select(child.World).ToList(),
                Facing = sock.Facing,
                ModuleA = link.A,
                ModuleB = link.B,
            });
        }
        var entrance = s.Modules[0];
        var exit = entrance.Shape.Sockets.First(k => k.Exterior);
        layout.ExitCells = exit.Cells.Select(entrance.World).ToList();
        layout.EntranceFacing = exit.Facing;
        Int3 exitMin = layout.ExitCells.Aggregate(Int3.Min);
        layout.EntranceCell = exitMin + exit.Facing.Vec() * 2;

        // Seal every socket nobody used.
        foreach (var m in s.Modules)
        {
            var kit = m.Template.Kit;
            for (int i = 0; i < m.Shape.Sockets.Count; i++)
            {
                var sock = m.Shape.Sockets[i];
                if (sock.Exterior || m.SocketUsed[i]) continue;
                var cap = new CappedSocket { Module = m.Index, Facing = sock.Facing };
                foreach (Int3 c in sock.Cells)
                {
                    Int3 w = m.World(c);
                    layout.Blocks.Set(w, kit.Palette[kit.CapEntry].Make(w, Dir.North));
                    cap.Cells.Add(w);
                }
                layout.Capped.Add(cap);
            }
        }

        foreach (var m in s.Modules)
            foreach (var slot in m.Shape.Slots)
                layout.Slots.Add(new Slot { Kind = slot.Kind, Cell = m.World(slot.Cell), Module = m.Index });

        // Key rooms: the rooms and halls the path runs through, and the vault.
        layout.KeyModules = s.Path.Where(i => s.Modules[i].Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault).ToList();

        // The core goes in the deepest hall (or room) on the path that has a core slot.
        var withCore = s.Path.Where(i => i != layout.VaultModule && layout.Slots.Any(sl => sl.Module == i && sl.Kind == SlotKind.Core))
            .OrderByDescending(i => s.Modules[i].Kind == ModuleKind.Hall).ThenByDescending(i => s.Modules[i].Step).ToList();
        if (withCore.Count == 0)
            withCore = layout.Slots.Where(sl => sl.Kind == SlotKind.Core).Select(sl => sl.Module).Distinct().ToList();
        layout.CoreModule = withCore.Count > 0 ? withCore[0] : layout.VaultModule;
        if (!layout.KeyModules.Contains(layout.CoreModule)) layout.KeyModules.Add(layout.CoreModule);

        // Rare places have a boss room: the last room or hall before the vault.
        if (rarity == Rarity.Rare)
        {
            var boss = s.Path.Take(s.Path.Count - 1).LastOrDefault(i => s.Modules[i].Kind is ModuleKind.Room or ModuleKind.Hall, -1);
            layout.BossModule = boss >= 0 ? boss : layout.VaultModule;
        }
        return layout;
    }

    static void Stamp(Layout layout, PlacedModule m)
    {
        var kit = m.Template.Kit;
        var shape = m.Shape;
        foreach (Int3 local in shape.Occupied)
        {
            Int3 w = m.World(local);
            layout.Occupied[w] = true;
            var cell = shape.At(local.X, local.Y, local.Z);
            if (cell.Palette < 0) continue;
            Block b = kit.Palette[cell.Palette].Make(w, cell.Facing);
            if (cell.Type == BlockType.Ramp) b.Type = BlockType.Ramp;
            layout.Blocks.Set(w, b);
        }
    }
}
