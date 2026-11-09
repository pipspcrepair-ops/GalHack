using System.Collections.Generic;
using Godot;
using PoiGenerator.Gen;

namespace Slice.Places;

/// <summary>
/// A place's rooms as shelter from the weather's air: a room is closed when its air can't reach
/// past the place's own cells or up into the sky (walls, glass, roof slopes and shut doors stop it;
/// an open or jammed door, a blown wall or a hole in the roof lets it through).
/// </summary>
public partial class Place
{
    /// <summary>The most cells a closed room may have: past it the space is taken as open (a hall that size is all draughts).</summary>
    public const int ClosedRoomCells = 20000;

    /// <summary>What stops air: a full block, glass, a sealed door, the slopes a roof is made of.</summary>
    static bool StopsAir(BlockType t) => t is BlockType.Cube or BlockType.Breakable or BlockType.Pane or BlockType.SealedDoor
        or BlockType.Ramp or BlockType.Corner or BlockType.InvCorner;

    Int3 _airCell;
    long _airDoors = -1;
    double _airAt = -1;
    bool _airClosed;

    /// <summary>Is a spot (a body's feet) in a closed room of this place? Recounted at most twice a second, or when a door moves.</summary>
    public bool Closed(Vector3 feet)
    {
        Int3 cell = CellAt(feet) + Int3.Up;
        long doors = ShutDoors();
        double now = Time.GetTicksMsec() / 1000.0;
        if (cell == _airCell && doors == _airDoors && now - _airAt < 0.5) return _airClosed;
        (_airCell, _airDoors, _airAt) = (cell, doors, now);
        return _airClosed = ClosedRoom(cell);
    }

    /// <summary>Which doors are shut, as a hash (a door opening or shutting changes it).</summary>
    long ShutDoors()
    {
        long h = _doors.Count;
        for (int i = 0; i < _doors.Count; i++)
            if (_doors[i].IsShut) h = h * 31 + i + 1;
        return h;
    }

    /// <summary>A flood of the air from a cell: closed when it stays inside the place's own cells.</summary>
    public bool ClosedRoom(Int3 start)
    {
        CellMask inside = Inst.Layout.Occupied;
        if (!inside[start] || StopsAir(Inst.Blocks.Get(start).Type)) return false;
        var shut = new HashSet<Int3>();
        foreach (Door d in _doors)
            if (d.IsShut && d.Cells != null)
                foreach (Int3 c in d.Cells) shut.Add(c);
        var seen = new HashSet<Int3> { start };
        var open = new Queue<Int3>();
        open.Enqueue(start);
        while (open.Count > 0)
        {
            Int3 c = open.Dequeue();
            if (!inside[c]) return false; // out past the place's walls, or up into the sky
            foreach (Int3 step in Steps)
            {
                Int3 n = c + step;
                if (shut.Contains(n) || StopsAir(Inst.Blocks.Get(n).Type) || !seen.Add(n)) continue;
                if (seen.Count > ClosedRoomCells) return false;
                open.Enqueue(n);
            }
        }
        return true;
    }

    static readonly Int3[] Steps = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };

    /// <summary>The two cells a door stands between, at its foot (tests: a room behind a door).</summary>
    public (Int3 A, Int3 B) DoorSides(Door d)
    {
        Int3 low = d.Cells[0];
        foreach (Int3 c in d.Cells)
            if (c.Y < low.Y) low = c;
        Int3 across = Mathf.Abs(d.Along.X) > 0.5f ? new Int3(0, 0, 1) : new Int3(1, 0, 0);
        return (low + across, low - across);
    }
}
