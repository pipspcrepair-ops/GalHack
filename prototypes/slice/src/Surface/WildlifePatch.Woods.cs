using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Wildlife;

namespace Slice.Surface;

/// <summary>
/// The patch's trees and rocks, for its creatures and people to walk round: once a new patch is swapped in, the
/// ground's own scatter round it (<see cref="ScatterSolids"/>, worked out by chunk, nothing streamed in) is read into
/// its nav grid as obstacles, a trunk or a rock each, so steering slides round them and a path goes round a grove.
/// A tree felled goes from the grid with it.
/// </summary>
public partial class WildlifePatch
{
    /// <summary>Whether this patch's woods are in its grid yet, and each trunk's obstacle by its scatter id.</summary>
    bool _wooded;
    readonly Dictionary<long, Obstacle> _trunks = new();

    /// <summary>Tests: keep the woods out of the grid (how it was before, to measure against).</summary>
    public static bool NoWoods;

    /// <summary>Trunks and rocks in the grid now (tests).</summary>
    public int Wooded => _trunks.Count;

    /// <summary>Reads the woods into a new patch's grid once their chunks are ready (a lookup a tick until then).</summary>
    void Wood()
    {
        if (_wooded || Sim == null || NoWoods) return;
        float half = Sim.Terrain.HalfSize;
        var near = Zone.Scatter.Near(Origin.ToNumerics(), half);
        if (near == null) return;
        _trunks.Clear();
        foreach (var s in near)
        {
            if (!ScatterSolids.InTheWay(s) || _planet.Felled.Contains(s.Id)) continue;
            Vector3 local = Zone.Scatter.Nearest(s, Origin) - Origin;
            if (Mathf.Abs(local.X) > half || Mathf.Abs(local.Z) > half) continue;
            _trunks[s.Id] = Sim.Nav.AddObstacle(local with { Y = 0f }, ScatterSolids.Reach(s));
        }
        _wooded = true;
    }

    /// <summary>A tree felled: nothing walks round it any more.</summary>
    public void Felled(long id)
    {
        if (Sim != null && _trunks.Remove(id, out Obstacle o)) Sim.Nav.SetActive(o, false);
    }
}
