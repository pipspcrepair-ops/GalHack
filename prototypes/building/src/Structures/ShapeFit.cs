using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GalHack.Structures;

/// <summary>
/// The shaped block that best fills a cell a smooth solid passes through, as a player shapes a hull
/// with slopes and corners: which of the cell's eight corners the solid takes in is its mask, and each
/// shape in each of the 24 turns covers a set of corners (a cube all eight, a slope six, an inverted
/// corner seven, a corner four). A mask gets the shape and turn whose corners differ from it least,
/// never one that leaves a corner the solid takes in uncovered when another fits as well. Built from
/// the catalogue's own shapes, so a new shape in shapes.json joins without a change here.
/// </summary>
public sealed class ShapeFit
{
    readonly (ShapeDef Shape, int Rot)?[] _best = new (ShapeDef, int)?[256];

    public ShapeFit(Catalogue catalogue, params string[] shapes)
    {
        var fits = new List<(ShapeDef Shape, int Rot, int Mask)> { (null, 0, 0) };
        foreach (string id in shapes)
        {
            ShapeDef shape = catalogue.Shape(id);
            for (int rot = 0; rot < (shape.IsCube ? 1 : Orientations.Count); rot++)
            {
                Mat3 m = Orientations.Matrix(rot);
                int mask = 0;
                for (int i = 0; i < 8; i++)
                    if (shape.HullPoints.Any(p => Vector3.DistanceSquared(m.Apply(p), Corner(i)) < 1e-6f)) mask |= 1 << i;
                // A shape whose points aren't all cell corners (a wedge's half height) can't be told by its corners.
                if (shape.HullPoints.All(p => Enumerable.Range(0, 8).Any(i => Vector3.DistanceSquared(m.Apply(p), Corner(i)) < 1e-6f)) && fits.All(f => f.Mask != mask))
                    fits.Add((shape, rot, mask));
            }
        }
        for (int mask = 0; mask < 256; mask++)
        {
            // A cell the solid only touches (its corners in one face, an edge or a point) holds none of it.
            if (Flat(mask)) continue;
            var best = fits.OrderBy(f => System.Numerics.BitOperations.PopCount((uint)(f.Mask ^ mask)))
                .ThenBy(f => (mask & ~f.Mask) != 0 ? 1 : 0)
                .ThenByDescending(f => System.Numerics.BitOperations.PopCount((uint)f.Mask)).First();
            _best[mask] = best.Shape == null ? null : (best.Shape, best.Rot);
        }
    }

    /// <summary>All the mask's corners lie on one face of the cell (or there are none): it has no volume.</summary>
    static bool Flat(int mask)
    {
        for (int axis = 0; axis < 3; axis++)
            foreach (int side in new[] { 0, 1 })
            {
                bool all = true;
                for (int i = 0; i < 8 && all; i++)
                    if ((mask & 1 << i) != 0 && (i >> axis & 1) != side) all = false;
                if (all) return true;
            }
        return false;
    }

    /// <summary>Corner <paramref name="i"/> of a cell (bit 0 is +x, bit 1 +y, bit 2 +z), from its middle.</summary>
    public static Vector3 Corner(int i) => new((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f);

    /// <summary>Which of a cell's corners lie inside a solid (cell coordinates: the cell's middle is the cell).</summary>
    public static int Mask(Int3 cell, Func<Vector3, bool> inside)
    {
        int mask = 0;
        for (int i = 0; i < 8; i++)
            if (inside(cell.ToVector3() + Corner(i))) mask |= 1 << i;
        return mask;
    }

    /// <summary>The shape and turn for a mask, or null to leave the cell empty.</summary>
    public (ShapeDef Shape, int Rot)? For(int mask) => _best[mask & 255];
}
