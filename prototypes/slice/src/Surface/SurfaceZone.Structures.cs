using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using PlanetSurface.Infra;
using Slice.Vessels;
using Structure = GalHack.Structures.Structure;

namespace Slice.Surface;

/// <summary>
/// The network's block structures in play (bridges, tunnel mouths, seawalls, railways: <see cref="InfraLayer.BlockPiece"/>).
/// One that is harmed (a shot, a blast, the Sleeper tool on it) is taken out of its tiles as a wreck of its own, the same blocks
/// where they stood, and from then on it is a structure like any other: its blocks' damage, salvage and splitting are a vessel's,
/// a section cut off its footing breaks away and falls. A support that no longer holds (a pier cut through, its cap come down)
/// drops what rests on it, which falls as a wreck under the world's own gravity.
/// </summary>
public partial class SurfaceZone
{
    readonly Dictionary<InfraLayer.BlockPiece, Vessel> _takenInfra = new();
    readonly Dictionary<InfraLayer.BlockPiece, int> _supportSeen = new();
    readonly HashSet<InfraLayer.BlockPiece> _dropped = new();

    /// <summary>Block structures taken into play, and spans dropped by their supports (tests).</summary>
    public int InfraTaken => _takenInfra.Count;
    public int InfraDropped { get; private set; }

    /// <summary>The wreck a block structure is in play as (a dropped span: the wreck it fell as), or null.</summary>
    public Vessel InfraVessel(InfraLayer.BlockPiece p) => _takenInfra.GetValueOrDefault(p);

    /// <summary>A block structure taken into play where it stands (once: after that, the same wreck), or null if it has no blocks.</summary>
    public Vessel TakeInfra(InfraLayer.BlockPiece p, Transform3D at)
    {
        if (_takenInfra.TryGetValue(p, out Vessel v)) return v;
        Structure s = p.Build(Planet);
        if (s.Count == 0) return null;
        v = Vessel.SpawnGroundWreck(this, s, at.Origin.ToNumerics(), at.Basis.GetRotationQuaternion().ToNumerics());
        Infra.Take(p, s);
        _takenInfra[p] = v;
        return v;
    }

    /// <summary>The block structure a hit on a collider struck, taken into play, and the cell it struck; or null.</summary>
    public Vessel InfraStruck(GodotObject collider, Vector3 point, Vector3 normal, out Int3 cell)
    {
        cell = default;
        if (Infra?.BlockAt(collider, out Transform3D at) is not { } p || TakeInfra(p, at) is not { } v) return null;
        cell = StructureBridge.CellAt(v.Structure, v.Body.ToLocal((point - normal * 0.05f).ToNumerics()));
        return v;
    }

    /// <summary>A blast's harm to the block structures in its reach not yet in play: each is taken and the blast goes into it as into any structure. Whether any was.</summary>
    public bool BlastInfra(Vector3 at, float radius, float damage)
    {
        bool any = false;
        if (Infra == null) return false;
        foreach (var (p, where) in Infra.BlocksNear(at, radius))
            if (TakeInfra(p, where) is { } v)
            {
                v.Hits.Blast(at.ToNumerics(), radius, damage);
                any = true;
            }
        return any;
    }

    /// <summary>Each tick: a support in play whose blocks changed and no longer hold drops the spans it carried.</summary>
    void DropUnheld()
    {
        List<InfraLayer.BlockPiece> failed = null;
        foreach (var (p, v) in _takenInfra)
        {
            if (p.Carries.Count == 0 || _supportSeen.TryGetValue(p, out int seen) && seen == v.Structure.Version) continue;
            _supportSeen[p] = v.Structure.Version;
            if (!p.Holds) (failed ??= new()).Add(p);
        }
        if (failed == null) return;
        foreach (InfraLayer.BlockPiece support in failed)
            foreach (InfraLayer.BlockPiece span in support.Carries)
                Drop(span);
    }

    /// <summary>A span its support let go of: taken into play and let fall whole, a free wreck.</summary>
    void Drop(InfraLayer.BlockPiece span)
    {
        if (!_dropped.Add(span) || TakeInfra(span, Infra.Where(span)) is not { } v || v.Structure.Count == 0) return;
        Structure s = v.Structure;
        _takenInfra[span] = v.Sever(Connectivity.Extract(s, s.Cells().Select(c => c.pos).ToList(), s.Name));
        Remove(v);
        InfraDropped++;
    }
}
