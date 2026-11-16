using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using Slice.Places;
using Int3 = GalHack.Structures.Int3;

namespace Slice.Vessels;

/// <summary>
/// One deco piece set on a structure: which piece, the grid cells it takes (it goes on the grid, on a
/// floor, a wall or a ceiling, and never overlaps a block or another piece), and its node.
/// </summary>
public sealed class DecoPlaced
{
    public string Piece;
    public Int3 Anchor;
    public int Yaw;
    public List<Int3> Cells = new();
    public DecoNode Node;
    /// <summary>Hit points worn off it by the Sleeper tool's blasts (someone else's structure).</summary>
    public float Worn;
}

public sealed partial class Vessel
{
    /// <summary>The deco pieces on this structure.</summary>
    public readonly List<DecoPlaced> Decos = new();
    readonly Dictionary<Int3, DecoPlaced> _decoCells = new();
    Node3D _decoRoot;

    /// <summary>The piece taking a cell, if any.</summary>
    public bool DecoAt(Int3 cell, out DecoPlaced placed) => _decoCells.TryGetValue(cell, out placed);

    /// <summary>Sets a piece on the grid: its node under the hull at <paramref name="local"/> (structure frame), the cells it takes marked.</summary>
    public DecoPlaced AddDeco(string piece, Int3 anchor, int yaw, IEnumerable<Int3> cells, Transform3D local, DecoLook? worn = null)
    {
        Decor decor = Decor.Shared;
        if (decor == null || !decor.Catalogue.TryGet(piece, out DecoPiece def)) return null;
        if (_decoRoot == null)
        {
            _decoRoot = new Node3D { Name = "Deco" };
            Node.AddChild(_decoRoot);
        }
        DecoLook look = worn ?? new DecoLook(piece, DecoState.Intact, DecoFall.None, true, DecoCover.Vines, def.Palette);
        // Scenery: no collision (the hull's colliders are the structure's blocks), but real lights where it still has power.
        DecoNode node = DecoNode.Create(decor.Meshes, look, local, collide: false, lights: look.Powered);
        _decoRoot.AddChild(node);
        var placed = new DecoPlaced { Piece = piece, Anchor = anchor, Yaw = yaw, Cells = cells.ToList(), Node = node };
        Decos.Add(placed);
        foreach (Int3 c in placed.Cells) _decoCells[c] = placed;
        foreach (Node child in node.GetChildren())
            if (child is Light3D light) light.Visible = _lightsOn;
        return placed;
    }

    /// <summary>
    /// Sets a wreck's dressing on its grid (HullVessel): each piece as its room's plan left it, worn as the wreck is, a
    /// step at a time inside a slice of the frame (yielding between), so hundreds of pieces never stall a frame.
    /// </summary>
    public IEnumerable<int> DressSteps(IEnumerable<HullVessel.Dressing> pieces, double budgetMs = 1.5)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (HullVessel.Dressing d in pieces ?? Enumerable.Empty<HullVessel.Dressing>())
        {
            AddDeco(d.Look.Piece, d.Anchor, -1, d.Cells, new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(d.Yaw)), new Vector3(d.At.X, d.At.Y, d.At.Z)), d.Look);
            if (clock.Elapsed.TotalMilliseconds < budgetMs) continue;
            yield return 0;
            clock.Restart();
        }
    }

    /// <summary>Takes a piece off the grid.</summary>
    public bool RemoveDeco(DecoPlaced placed)
    {
        if (!Decos.Remove(placed)) return false;
        foreach (Int3 c in placed.Cells) _decoCells.Remove(c);
        placed.Node?.QueueFree();
        return true;
    }

    /// <summary>The lights deco pieces give follow the structure's lights switch (the CORE tab).</summary>
    void SetDecoLights(bool on)
    {
        foreach (DecoPlaced d in Decos)
            if (d.Node != null)
                foreach (Node child in d.Node.GetChildren())
                    if (child is Light3D light) light.Visible = on;
    }
}
