using System;
using System.Collections.Generic;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using N = System.Numerics;

namespace Slice.Vessels;

/// <summary>A loose vessel's real shape, as the world sees it: the corners of what collides.</summary>
public sealed partial class Vessel
{
    /// <summary>The corners, in the world, of every box that makes up the body's shape: solid cells whole, fittings and parts as their parts.</summary>
    public IEnumerable<Vector3> ShapeCorners()
    {
        float cell = Structure.Grid.CellSize;
        foreach (var (p, b) in Structure.Blocks())
        {
            N.Vector3 centre = StructureBridge.CellCenter(Structure, p);
            BlockType t = Structure.TypeOf(b);
            Mat3 rot = Orientations.Matrix(b.Rot);
            var boxes = new List<(N.Vector3 Min, N.Vector3 Max)>();
            if (Structure.IsSolid(b) && t.Collide != Collide.Parts) boxes.Add((new N.Vector3(-0.5f), new N.Vector3(0.5f)));
            else
                foreach (Part part in t.Parts)
                {
                    if (part.ClosedOnly && b.Open) continue;
                    N.Vector3 lo = rot.Apply(part.Min), hi = rot.Apply(part.Max);
                    boxes.Add((N.Vector3.Min(lo, hi), N.Vector3.Max(lo, hi)));
                }
            foreach (var (min, max) in boxes)
                for (int i = 0; i < 8; i++)
                    yield return ToWorld(centre + new N.Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z) * cell);
        }
    }

    /// <summary>
    /// How far the deepest corner of the shape is below the ground under it, m (negative: it is all above). Lets a loose body
    /// be set down on ground that has no collision to land on.
    /// </summary>
    public float DepthBelow(Func<float, float, float> groundY)
    {
        float deepest = float.MinValue;
        foreach (Vector3 w in ShapeCorners()) deepest = MathF.Max(deepest, groundY(w.X, w.Z) - w.Y);
        return deepest;
    }
}
