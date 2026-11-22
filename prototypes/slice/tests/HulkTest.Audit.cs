using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;

namespace Slice.Tests;

/// <summary>
/// What a wreck's blocks measure, the way docs/place-patterns.md measured Empyrion's (section 1): a walkable cell is
/// open space with at least 2 m of headroom over a solid floor and a roof within 10 m over it; a corridor cell is one
/// whose clear width is 3 m or less one way with a run of 4 m or more the other; machines and containers are the
/// devices that store or make something, counted once each. From the blocks alone, so it measures any hull.
/// </summary>
public sealed class WreckAudit
{
    public int Blocks, Length, Beam, Height, Floor, Clear4, Low, Corridor, Machines, Lights, Armour, Shaped;
    /// <summary>Walking levels with a real deck's worth of floor (50 m² or more), from the keel up.</summary>
    public List<int> Levels = new();
    /// <summary>The walkable cells under 4 m of headroom (structure cells).</summary>
    public readonly HashSet<Int3> LowCells = new();
    public readonly HashSet<Int3> FloorCells = new(), CorridorCells = new();
    /// <summary>Floor a deck's worth (40 m² or more) that no door or stair gets into, each part's cells.</summary>
    public readonly List<List<Int3>> Unreached = new();

    public float CorridorShare => Floor == 0 ? 0 : Corridor / (float)Floor;
    public float MachinesPer100 => Floor == 0 ? 0 : Machines * 100f / Floor;
    public float ShapedShare => Armour == 0 ? 0 : Shaped / (float)Armour;

    static readonly Int3[] Around = { Int3.UnitX, -Int3.UnitX, Int3.UnitZ, -Int3.UnitZ };

    static readonly HashSet<string> Stores = new() { "container", "generator", "gyro", "warp-drive", "fold-drive", "constructor", "deconstructor", "capacitor", "forecast", "fuel", "warp-tank" };

    public static WreckAudit Of(Structure s)
    {
        var a = new WreckAudit { Blocks = s.Count };
        var (lo, hi) = s.Bounds();
        a.Length = hi.Z - lo.Z + 1;
        a.Beam = hi.X - lo.X + 1;
        a.Height = hi.Y - lo.Y + 1;
        bool Empty(Int3 p) => !s.TryGet(p, out Block b) || s.TypeOf(b).Collide == Collide.None;
        bool Door(Int3 p) => s.TryGet(p, out Block b) && s.TypeOf(b).IsDoor;
        bool Solid(Int3 p) => s.TryGet(p, out Block b) && s.TypeOf(b).Collide != Collide.None;
        // Inside the hull: walled both ways across one axis at least (a ledge on the outside under the flank's overhang is
        // not a deck; a passage that runs out to a torn face or a hole in the shell still is).
        bool Walled(Int3 p, Int3 d)
        {
            Int3 q = p + d;
            while (q.X >= lo.X && q.X <= hi.X && q.Z >= lo.Z && q.Z <= hi.Z && !Solid(q)) q += d;
            return q.X >= lo.X && q.X <= hi.X && q.Z >= lo.Z && q.Z <= hi.Z;
        }
        bool Enclosed(Int3 p) => Walled(p, Around[0]) && Walled(p, Around[1]) || Walled(p, Around[2]) && Walled(p, Around[3]);
        // A doorway: an opening up to 3 m wide through a wall, under its lintel, with more headroom either side of it than in
        // it (a door the years took, a landing's opening, the catwalk's way through a bulkhead): lower than a deck by its nature.
        int Head(Int3 p)
        {
            int h = 0;
            while (h < 11 && Empty(p + Int3.UnitY * h)) h++;
            return h;
        }
        int Span(Int3 p, Int3 d)
        {
            int n = 1;
            for (Int3 q = p + d; n <= 3 && Empty(q); q += d) n++;
            for (Int3 q = p - d; n <= 3 && Empty(q); q -= d) n++;
            return n;
        }
        bool Opening(Int3 p, Int3 across, Int3 through) => Span(p, across) <= 3 && Head(p + through) > Head(p) && Head(p - through) > Head(p);
        bool Doorway(Int3 p) => Opening(p, Int3.UnitX, Int3.UnitZ) || Opening(p, Int3.UnitZ, Int3.UnitX);
        var candidates = new Dictionary<Int3, int>();
        var doors = new HashSet<Int3>();
        foreach (var (p, b) in s.Blocks())
        {
            BlockType t = s.TypeOf(b);
            if (t.IsArmor)
            {
                a.Armour++;
                if (!s.ShapeOf(b).IsCube) a.Shaped++;
            }
            if (!b.IsFiller && Stores.Contains(t.Function)) a.Machines++;
            if (!b.IsFiller && t.IsLight) a.Lights++;
            // The floor over a full block: open, or a doorway between two floors.
            Int3 up = p + Int3.UnitY;
            if (!t.IsArmor || !s.ShapeOf(b).IsCube) continue;
            if (Door(up)) { doors.Add(up); continue; }
            if (!Empty(up)) continue;
            int h = Head(up);
            if (h >= 2 && h < 11 && Enclosed(up)) candidates[up] = h;
        }
        // Walkable floor is what joins up into a deck's worth and is got into, by a door or a stair: a cargo stack's top, a cranny
        // or the void left inside a mass of solid is not.
        bool Stairs(Int3 p) => s.TryGet(p, out Block b) && s.TypeOf(b).Function == "stairs";
        var seen = new HashSet<Int3>();
        foreach (Int3 start in candidates.Keys)
        {
            if (seen.Contains(start)) continue;
            var part = new List<Int3>();
            var q = new Queue<Int3>();
            q.Enqueue(start);
            seen.Add(start);
            bool way = false;
            while (q.Count > 0)
            {
                Int3 c = q.Dequeue();
                if (candidates.ContainsKey(c)) part.Add(c);
                // (On a stair, beside one, or stepping off a flight's top onto its landing.)
                way |= doors.Contains(c) || Stairs(c - Int3.UnitY) || Around.Any(d => Stairs(c + d) || Stairs(c + d - Int3.UnitY));
                foreach (Int3 d in Around)
                    if ((candidates.ContainsKey(c + d) || doors.Contains(c + d)) && seen.Add(c + d)) q.Enqueue(c + d);
            }
            if (part.Count >= 40 && !way) a.Unreached.Add(part);
            if (part.Count < 40 || !way) continue;
            foreach (Int3 c in part)
            {
                a.FloorCells.Add(c);
                if (candidates[c] >= 4 || Doorway(c)) a.Clear4++;
                else a.LowCells.Add(c);
            }
        }
        a.Floor = a.FloorCells.Count;
        a.Low = a.LowCells.Count;
        int Run(Int3 p, Int3 step)
        {
            int n = 1;
            for (Int3 q = p + step; a.FloorCells.Contains(q); q += step) n++;
            for (Int3 q = p - step; a.FloorCells.Contains(q); q -= step) n++;
            return n;
        }
        foreach (Int3 p in a.FloorCells)
        {
            int x = Run(p, Int3.UnitX), z = Run(p, Int3.UnitZ);
            if (Math.Min(x, z) <= 3 && Math.Max(x, z) >= 4) a.CorridorCells.Add(p);
        }
        a.Corridor = a.CorridorCells.Count;
        a.Levels = a.FloorCells.GroupBy(p => p.Y).Where(g => g.Count() >= 50).Select(g => g.Key).OrderBy(y => y).ToList();
        return a;
    }

    public override string ToString() =>
        $"{Blocks} blocks, {Length} x {Beam} x {Height} m, levels {string.Join("/", Levels)}, floor {Floor} m², {Clear4 * 100f / Math.Max(1, Floor):F0}% of it 4 m clear ({Low} m² lower), " +
        $"corridors {CorridorShare:P0}, {Machines} machines and containers ({MachinesPer100:F1} per 100 m²), {Lights} lights, shaped {ShapedShare:P0} of {Armour} armour";
}
