using System.Collections.Generic;
using Godot;
using PoiGenerator.Gen;
using Slice.Items;
using Slice.Vessels;

namespace Slice.Game;

/// <summary>
/// The core scanner: while it is in hand, every core within its range (the item's data) shows through the walls
/// as a marker on the core itself. It asks only what places and structures already know of their cores (a place's
/// core cell, a structure's core), never the blocks.
/// </summary>
public partial class Game
{
    /// <summary>How far the edge of the range fades, m.</summary>
    public const float CoreFade = 15f;

    /// <summary>A core the scanner shows: where it is, and how strongly (fading out over the range's last metres).</summary>
    public readonly record struct CoreSighting(Vector3 At, float Alpha);

    /// <summary>The scanner's range, m, while it is held (0: not held).</summary>
    public float CoreScanRange =>
        Pack.Hand == HandKind.CoreScanner && Pack.HandUsable && Zone?.Data?.Items.TryGet(Pack.HandItem, out var item) == true ? item.Range : 0f;

    /// <summary>The cores within the held scanner's range of <paramref name="from"/>, the live ones only (a shot-out core, a ruin's, a wreck's seat show nothing).</summary>
    public List<CoreSighting> CoresInRange(Vector3 from)
    {
        var found = new List<CoreSighting>();
        float range = CoreScanRange;
        if (range <= 0f || Zone == null) return found;
        void Add(Vector3 at)
        {
            float a = Mathf.Clamp((range - from.DistanceTo(at)) / CoreFade, 0f, 1f);
            if (a > 0f) found.Add(new CoreSighting(at, a));
        }
        if (Places != null)
            foreach (var p in Places.Standing)
                if (p.Inst.HasCore && p.Inst.Blocks.Get(p.Inst.CoreCell).Type == BlockType.Core) Add(p.WorldOf(p.Inst.CoreCell));
        foreach (Vessel v in Zone.Vessels)
            if (v.Structure.Core is GalHack.Structures.Int3 core) Add(v.ToWorld(PhysicsSandbox.Bridges.StructureBridge.CellCenter(v.Structure, core)));
        return found;
    }
}
