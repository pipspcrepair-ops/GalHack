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
            foreach (Dir d in Dirs.All)
                if (d != Dir.NegY && s.AttachCoverage(block, d) == Coverage.Partial && outside.Contains(pos + d.Vec())) found.Add((pos, d));
        }
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
