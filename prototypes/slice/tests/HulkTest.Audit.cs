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
    public readonly HashSet<Int3> FloorCells = new();

    public float CorridorShare => Floor == 0 ? 0 : Corridor / (float)Floor;
    public float MachinesPer100 => Floor == 0 ? 0 : Machines * 100f / Floor;
    public float ShapedShare => Armour == 0 ? 0 : Shaped / (float)Armour;

    static readonly HashSet<string> Stores = new() { "container", "generator", "gyro", "warp-drive", "hyperdrive", "constructor", "deconstructor", "capacitor", "forecast", "fuel", "warp-tank" };

    public static WreckAudit Of(Structure s)
    {
        var a = new WreckAudit { Blocks = s.Count };
        var (lo, hi) = s.Bounds();
        a.Length = hi.Z - lo.Z + 1;
        a.Beam = hi.X - lo.X + 1;
        a.Height = hi.Y - lo.Y + 1;
        bool Solid(Int3 p) => s.TryGet(p, out Block b) && s.IsSolid(b);
        bool Empty(Int3 p) => !s.TryGet(p, out Block b) || s.TypeOf(b).Collide == Collide.None;
        foreach (var (p, b) in s.Blocks())
        {
            BlockType t = s.TypeOf(b);
            if (t.IsArmor)
            {
                a.Armour++;
                if (!s.ShapeOf(b).IsCube) a.Shaped++;
            }
            if (b.IsFiller) continue;
            if (Stores.Contains(t.Function)) a.Machines++;
            if (t.IsLight) a.Lights++;
            // The floor over a full block.
            Int3 up = p + Int3.UnitY;
            if (!t.IsArmor || !s.ShapeOf(b).IsCube || !Empty(up)) continue;
            int h = 0;
            while (h < 11 && Empty(up + Int3.UnitY * h)) h++;
            if (h < 2 || h >= 11) continue;
            a.FloorCells.Add(up);
            if (h >= 4) a.Clear4++;
            else a.LowCells.Add(up);
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
            if (Math.Min(x, z) <= 3 && Math.Max(x, z) >= 4) a.Corridor++;
        }
        a.Levels = a.FloorCells.GroupBy(p => p.Y).Where(g => g.Count() >= 50).Select(g => g.Key).OrderBy(y => y).ToList();
        return a;
    }

    public override string ToString() =>
        $"{Blocks} blocks, {Length} x {Beam} x {Height} m, levels {string.Join("/", Levels)}, floor {Floor} m², {Clear4 * 100f / Math.Max(1, Floor):F0}% of it 4 m clear ({Low} m² lower), " +
        $"corridors {CorridorShare:P0}, {Machines} machines and containers ({MachinesPer100:F1} per 100 m²), {Lights} lights, shaped {ShapedShare:P0} of {Armour} armour";
}
