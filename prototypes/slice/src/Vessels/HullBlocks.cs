using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Surface;
using Wildlife;
using DamageType = GalHack.Structures.DamageType;
using Int3 = GalHack.Structures.Int3;

namespace Slice.Vessels;

/// <summary>
/// The bestiary's block-breakers meet the slice's structures here: what hull block stands in a
/// creature's way, and its blows landing through the same block damage gunfire uses (armour first,
/// so a scrap wall falls and combat steel holds). Frames: the sim's in, the zone's out.
/// </summary>
public sealed class HullBlocks : IBlockWorld
{
    readonly SurfaceZone _zone;
    readonly System.Func<Vector3> _origin;

    public HullBlocks(SurfaceZone zone, System.Func<Vector3> origin)
    {
        _zone = zone;
        _origin = origin;
    }

    public bool Blocking(Vector3 from, Vector3 to, out BlockAt block)
    {
        block = default;
        Vector3 o = _origin(), d = to - from;
        float len = d.Length();
        if (len < 0.1f || !WorldRay.Cast(_zone, null, from + o, d / len, len, GalHack.Physics.Layers.Hull, out LookHit hit) || !hit.OnBlock) return false;
        block = new BlockAt { Point = hit.Point - o, Normal = hit.Normal, PlayerBase = hit.Vessel.IsBase, Key = (hit.Vessel, hit.Cell) };
        return true;
    }

    public void Strike(in BlockAt block, float amount, BreakerKind kind)
    {
        if (block.Key is not (Vessel v, Int3 cell) || v.Hits == null) return;
        DamageType type = kind == BreakerKind.Corroder ? DamageType.Energy : DamageType.Kinetic;
        v.Hits.Shot(cell, (block.Point + _origin()).ToNumerics(), block.Normal.ToNumerics(), amount, type);
    }
}