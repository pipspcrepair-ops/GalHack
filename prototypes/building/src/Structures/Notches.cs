using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Structures;

/// <summary>
/// The open triangular sides of ramps: a slope, corner, inverted corner or wedge shows a three-cornered face where it ends,
/// and where that looks onto air outside the structure it is a notch in the hull's lines. <see cref="Open"/> finds them;
/// <see cref="Designer.Cap"/> fills them with the piece that carries the line on.
/// </summary>
public static class Notches
{
    static readonly HashSet<string> Ramps = new() { "slope", "corner", "invcorner", "wedge" };

    /// <summary>The empty cells reached from outside the structure.</summary>
    public static HashSet<Int3> Outside(Structure s)
    {
        var (min, max) = s.Bounds();
        Int3 lo = min - Footprint.One, hi = max + Footprint.One;
        var outside = new HashSet<Int3> { lo };
        var open = new Queue<Int3>();
        open.Enqueue(lo);
        while (open.Count > 0)
        {
            Int3 c = open.Dequeue();
            foreach (Dir d in Dirs.All)
            {
                Int3 n = c + d.Vec();
                if (n.X < lo.X || n.Y < lo.Y || n.Z < lo.Z || n.X > hi.X || n.Y > hi.Y || n.Z > hi.Z || s.Has(n) || !outside.Add(n)) continue;
                open.Enqueue(n);
            }
        }
        return outside;
    }

    /// <summary>Every partly covered face of a ramp that looks onto outside air, undersides left out.</summary>
    public static List<(Int3 pos, Dir face)> Open(Structure s)
    {
        var outside = Outside(s);
        var found = new List<(Int3, Dir)>();
        foreach (var (pos, block) in s.Blocks())
        {
            if (!s.TypeOf(block).IsArmor || !Ramps.Contains(s.ShapeOf(block).Id)) continue;
            // A vertical chamfer's top is a flat facet level with the roof beside it, not a notch.
            bool chamfer = s.ShapeOf(block).Id == "slope" && Orientations.ToWorld(block.Rot, Dir.PosY).Axis() != 1;
            foreach (Dir d in Dirs.All)
                if (d != Dir.NegY && !(chamfer && d == Dir.PosY) && s.AttachCoverage(block, d) == Coverage.Partial && outside.Contains(pos + d.Vec())
                    && !Flush(s, outside, pos, d)) found.Add((pos, d));
        }
        return found;
    }

    /// <summary>A triangular side that is level with a wall continuing it (a gable end on the wall below or beside it) is flush, not a notch.</summary>
    static bool Flush(Structure s, HashSet<Int3> outside, Int3 pos, Dir d)
    {
        foreach (Dir e in Dirs.All)
        {
            if (e.Axis() == d.Axis()) continue;
            Int3 n = pos + e.Vec();
            if (s.TryGet(n, out Block nb) && s.TypeOf(nb).IsArmor && s.AttachCoverage(nb, d) == Coverage.Full && outside.Contains(n + d.Vec())) return true;
        }
        return false;
    }

    /// <summary>Hull pieces hanging on a single neighbour: a sheet, slope or block standing proud of the hull with nothing behind it.</summary>
    public static List<Int3> Strays(Structure s)
    {
        var found = new List<Int3>();
        foreach (var (pos, block) in s.Blocks())
            if (s.TypeOf(block).IsArmor && s.ShapeOf(block).Id is "cube" or "slope" or "corner" or "invcorner" or "wedge"
                && Dirs.All.Count(d => s.Has(pos + d.Vec())) <= 1) found.Add(pos);
        return found;
    }

    /// <summary>The corners of a block's face on the cell side <paramref name="world"/>, flattened to that plane in quarter metres, in order.</summary>
    public static List<(int, int)> Outline(Structure s, Block b, Dir world)
    {
        Dir local = Orientations.ToLocal(b.Rot, world);
        Mat3 m = Orientations.Matrix(b.Rot);
        int axis = world.Axis();
        var pts = new List<(int, int)>();
        foreach (Polygon p in s.ShapeOf(b).Polygons.Where(p => p.Boundary == local))
            foreach (var v in p.Points)
            {
                var w = m.Apply(v);
                float[] c = { w.X, w.Y, w.Z };
                var q = Enumerable.Range(0, 3).Where(i => i != axis).Select(i => (int)MathF.Round(c[i] * 4f)).ToArray();
                if (!pts.Contains((q[0], q[1]))) pts.Add((q[0], q[1]));
            }
        pts.Sort();
        return pts;
    }
}
