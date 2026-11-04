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
    public bool IsOpen(Cell c) => InBounds(c) && _cells[c.X, c.Z] == null;
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
