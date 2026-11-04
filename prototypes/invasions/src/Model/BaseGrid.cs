using System;
using System.Collections.Generic;
using System.Linq;
using Invasions.Director;

namespace Invasions.Model;

public enum BlockKind { Wall, Door, Container, Generator, Core, Turret, Console }

/// <summary>A cell on the base grid. X runs east, Z runs south, like the world.</summary>
public readonly record struct Cell(int X, int Z)
{
    public static Cell operator +(Cell a, Cell b) => new(a.X + b.X, a.Z + b.Z);
    public int ManhattanTo(Cell o) => Math.Abs(o.X - X) + Math.Abs(o.Z - Z);
}

/// <summary>One placed block with hit points. Destroyed blocks stay in the list (dead) for the record.</summary>
public sealed class Block
{
    public int Id { get; init; }
    public BlockKind Kind { get; init; }
    public Cell Cell { get; init; }
    public double MaxHp { get; init; }
    public double Hp { get; internal set; }
    public bool Alive => Hp > 0;
    public double Health01 => MaxHp > 0 ? Hp / MaxHp : 0;
    /// <summary>For containers: how many item units they hold.</summary>
    public int Capacity { get; init; }
}

/// <summary>
/// The player base as a grid of blocks, with empty ground around it (the margin) so invaders can
/// plan a way in from outside. Layout cell (0,0) is the north-west corner of the base; the layout is
/// centred on the base position.
/// </summary>
public sealed class BaseGrid
{
    public int Width { get; }
    public int Depth { get; }
    public double CellSize { get; }
    public int Margin { get; }
    /// <summary>World position of the north-west corner of grid cell (0,0).</summary>
    public GroundPos Origin { get; }

    public IReadOnlyList<Block> Blocks => _blocks;
    public Cell CoreSlot { get; private set; }
    /// <summary>Floor cells of the room around the core, as built (used to tell when invaders got in).</summary>
    public IReadOnlySet<Cell> CoreRoom => _coreRoom;

    readonly Block[,] _cells;
    readonly int[,] _obstacles;
    readonly List<Block> _blocks = new();
    readonly Dictionary<BlockKind, BlockSpec> _specs = new();
    readonly HashSet<Cell> _coreRoom = new();
    int _nextId = 1;

    BaseGrid(int width, int depth, double cellSize, int margin, GroundPos origin)
    {
        Width = width;
        Depth = depth;
        CellSize = cellSize;
        Margin = margin;
        Origin = origin;
        _cells = new Block[width, depth];
        _obstacles = new int[width, depth];
    }

    public static BaseGrid FromLayout(BaseData data, GroundPos centre)
    {
        int layoutW = data.Layout.Max(r => r.Length), layoutD = data.Layout.Count;
        var origin = new GroundPos(centre.X - (layoutW / 2.0 + data.Margin) * data.CellSize,
                                   centre.Z - (layoutD / 2.0 + data.Margin) * data.CellSize);
        var grid = new BaseGrid(layoutW + 2 * data.Margin, layoutD + 2 * data.Margin, data.CellSize, data.Margin, origin);

        foreach (var (symbol, spec) in data.Legend)
            grid._specs[ParseKind(spec.Kind)] = spec;

        for (int z = 0; z < layoutD; z++)
        {
            string row = data.Layout[z];
            for (int x = 0; x < row.Length; x++)
            {
                if (!data.Legend.TryGetValue(row[x].ToString(), out BlockSpec spec)) continue;
                var cell = new Cell(x + data.Margin, z + data.Margin);
                Block b = grid.Place(ParseKind(spec.Kind), cell);
                if (b.Kind == BlockKind.Core) grid.CoreSlot = cell;
            }
        }
        grid.FindCoreRoom();
        return grid;
    }

    static BlockKind ParseKind(string kind) => Enum.Parse<BlockKind>(kind, ignoreCase: true);

    public Block Place(BlockKind kind, Cell cell)
    {
        if (!InBounds(cell) || _cells[cell.X, cell.Z] != null) return null;
        _specs.TryGetValue(kind, out BlockSpec spec);
        var b = new Block
        {
            Id = _nextId++,
            Kind = kind,
            Cell = cell,
            MaxHp = spec?.Hp ?? 500,
            Hp = spec?.Hp ?? 500,
            Capacity = spec?.Capacity ?? 0,
        };
        _cells[cell.X, cell.Z] = b;
        _blocks.Add(b);
        return b;
    }

    /// <summary>Takes a destroyed block off the grid. Its cell becomes open ground.</summary>
    internal void Remove(Block b)
    {
        if (InBounds(b.Cell) && _cells[b.Cell.X, b.Cell.Z] == b) _cells[b.Cell.X, b.Cell.Z] = null;
    }

    public bool InBounds(Cell c) => c.X >= 0 && c.Z >= 0 && c.X < Width && c.Z < Depth;
    public Block At(Cell c) => InBounds(c) ? _cells[c.X, c.Z] : null;
    /// <summary>No block in the cell (it may still hold an obstacle).</summary>
    public bool IsOpen(Cell c) => InBounds(c) && _cells[c.X, c.Z] == null;
    /// <summary>Something that can't be breached sits in the cell: a crate, a vehicle hulk, rubble.</summary>
    public bool IsObstacle(Cell c) => InBounds(c) && _obstacles[c.X, c.Z] > 0;
    /// <summary>An agent can stand here: no block and no obstacle.</summary>
    public bool IsWalkable(Cell c) => IsOpen(c) && _obstacles[c.X, c.Z] == 0;
    /// <summary>Blocks movement in a straight line: a block or an obstacle. Off the grid is open ground.</summary>
    public bool BlocksMovement(Cell c) => InBounds(c) && (_cells[c.X, c.Z] != null || _obstacles[c.X, c.Z] > 0);

    /// <summary>
    /// Marks the cells under an obstacle's footprint, a box rotated by <paramref name="yaw"/> (radians, as
    /// Godot turns about Y), grown by <paramref name="inflate"/> metres: the agent's radius plus a margin,
    /// so planned paths never hug it or squeeze past it.
    /// </summary>
    public int MarkObstacle(GroundPos centre, double sizeX, double sizeZ, double yaw, double inflate, int delta = 1)
    {
        double hx = sizeX / 2 + inflate, hz = sizeZ / 2 + inflate;
        double ux = Math.Cos(yaw), uz = -Math.Sin(yaw), vx = Math.Sin(yaw), vz = Math.Cos(yaw);
        double reach = Math.Sqrt(hx * hx + hz * hz);
        Cell lo = CellAt(new GroundPos(centre.X - reach, centre.Z - reach));
        Cell hi = CellAt(new GroundPos(centre.X + reach, centre.Z + reach));
        double half = CellSize / 2;
        int marked = 0;
        for (int z = Math.Max(0, lo.Z); z <= Math.Min(Depth - 1, hi.Z); z++)
            for (int x = Math.Max(0, lo.X); x <= Math.Min(Width - 1, hi.X); x++)
            {
                GroundPos cc = CellCentre(new Cell(x, z));
                double dx = cc.X - centre.X, dz = cc.Z - centre.Z;
                // Separating axes: the world axes, then the box's own.
                bool overlap =
                    Math.Abs(dx) <= half + hx * Math.Abs(ux) + hz * Math.Abs(vx) &&
                    Math.Abs(dz) <= half + hx * Math.Abs(uz) + hz * Math.Abs(vz) &&
                    Math.Abs(dx * ux + dz * uz) <= hx + half * (Math.Abs(ux) + Math.Abs(uz)) &&
                    Math.Abs(dx * vx + dz * vz) <= hz + half * (Math.Abs(vx) + Math.Abs(vz));
                if (!overlap) continue;
                _obstacles[x, z] = Math.Max(0, _obstacles[x, z] + delta);
                marked++;
            }
        return marked;
    }

    /// <summary>
    /// True if a body of <paramref name="radius"/> can move in a straight line from a to b without
    /// touching a block or an obstacle cell. Path smoothing only shortcuts where this holds.
    /// </summary>
    public bool ClearLine(GroundPos a, GroundPos b, double radius)
    {
        double length = a.DistanceTo(b);
        int steps = Math.Max(1, (int)Math.Ceiling(length / 0.25));
        double half = CellSize / 2;
        for (int i = 0; i <= steps; i++)
        {
            GroundPos p = a.Lerp(b, i / (double)steps);
            // Ignore the body's own footprint: one shoved against a wall must still be able to step away.
            if (p.DistanceTo(a) < radius) continue;
            Cell c = CellAt(p);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = new Cell(c.X + dx, c.Z + dz);
                    if (!BlocksMovement(n)) continue;
                    GroundPos nc = CellCentre(n);
                    double ex = Math.Max(0, Math.Abs(p.X - nc.X) - half), ez = Math.Max(0, Math.Abs(p.Z - nc.Z) - half);
                    if (ex * ex + ez * ez < radius * radius) return false;
                }
        }
        return true;
    }

    /// <summary>The nearest walkable cell to a point, searching outward a few rings; null if none.</summary>
    public Cell? NearestWalkable(GroundPos p, int maxRings = 4, Func<Cell, bool> also = null)
    {
        Cell start = CellAt(p);
        for (int ring = 0; ring <= maxRings; ring++)
        {
            Cell? best = null;
            double bestDistance = double.MaxValue;
            for (int dz = -ring; dz <= ring; dz++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                    var c = new Cell(start.X + dx, start.Z + dz);
                    bool walkable = InBounds(c) ? IsWalkable(c) : true;
                    if (!walkable || (also != null && !also(c))) continue;
                    double d = CellCentre(c).DistanceTo(p);
                    if (d < bestDistance) { bestDistance = d; best = c; }
                }
            if (best != null) return best;
        }
        return null;
    }
    public Block Find(int id) => _blocks.FirstOrDefault(b => b.Id == id);
    public Block Core => _blocks.LastOrDefault(b => b.Kind == BlockKind.Core && b.Alive);
    public IEnumerable<Block> Alive => _blocks.Where(b => b.Alive);
    public int AliveCount => _blocks.Count(b => b.Alive);

    public GroundPos CellCentre(Cell c) => new(Origin.X + (c.X + 0.5) * CellSize, Origin.Z + (c.Z + 0.5) * CellSize);

    public Cell CellAt(GroundPos p) =>
        new((int)Math.Floor((p.X - Origin.X) / CellSize), (int)Math.Floor((p.Z - Origin.Z) / CellSize));

    public Cell Clamp(Cell c) => new(Math.Clamp(c.X, 0, Width - 1), Math.Clamp(c.Z, 0, Depth - 1));

    /// <summary>The layout's own cell (without the margin) for a grid cell.</summary>
    public Cell ToLayout(Cell c) => new(c.X - Margin, c.Z - Margin);
    public Cell FromLayout(int x, int z) => new(x + Margin, z + Margin);

    public static readonly Cell[] Orthogonal = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    void FindCoreRoom()
    {
        _coreRoom.Clear();
        var open = new Queue<Cell>();
        foreach (Cell d in Orthogonal)
            if (IsOpen(CoreSlot + d)) open.Enqueue(CoreSlot + d);
        while (open.Count > 0 && _coreRoom.Count < 400)
        {
            Cell c = open.Dequeue();
            if (!_coreRoom.Add(c)) continue;
            foreach (Cell d in Orthogonal)
                if (IsOpen(c + d) && !_coreRoom.Contains(c + d)) open.Enqueue(c + d);
        }
        // A core standing in the open has no room around it.
        if (_coreRoom.Count >= 400) _coreRoom.Clear();
    }
}
