using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Wildlife;

namespace Slice.Surface;

/// <summary>
/// The patch's trees and rocks, for its creatures and people to walk round: once a new patch is swapped in, the
/// ground's own scatter round it (<see cref="ScatterSolids"/>, worked out by chunk, nothing streamed in) is read into
/// its nav grid as obstacles, the footprint circles of each trunk, low limb, stalk and rock, so steering slides round
/// them and a path goes round a grove. A tree felled goes from the grid with it.
/// </summary>
public partial class WildlifePatch
{
    /// <summary>Whether this patch's woods are in its grid yet, and each footprint's obstacle with its plant's scatter id.</summary>
    bool _wooded;
    readonly List<(long Id, Obstacle O)> _trunks = new();
    readonly List<(long Id, Vector3 At, float R)> _feet = new();

    /// <summary>Tests: keep the woods out of the grid (how it was before, to measure against).</summary>
    public static bool NoWoods;

    /// <summary>Footprints of trunks and rocks in the grid now, and how long reading them in took, ms (tests).</summary>
    public int Wooded => _trunks.Count;
    public double WoodMs { get; private set; }

    /// <summary>Reads the woods into a new patch's grid once their chunks are ready (a lookup a tick until then).</summary>
    void Wood()
    {
        if (_wooded || Sim == null || NoWoods) return;
        long t0 = Stopwatch.GetTimestamp();
        float half = Sim.Terrain.HalfSize;
        var near = Zone.Scatter.Near(Origin.ToNumerics(), half);
        if (near == null) return;
        _trunks.Clear();
        Zone.Scatter.Footprints(near, Origin, _feet);
        foreach (var (id, at, r) in _feet)
        {
            Vector3 local = at - Origin;
            if (Mathf.Abs(local.X) > half || Mathf.Abs(local.Z) > half || _planet.Felled.Contains(id)) continue;
            _trunks.Add((id, Sim.Nav.AddObstacle(local with { Y = 0f }, r)));
        }
        _wooded = true;
        WoodMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    /// <summary>A tree felled: nothing walks round it any more.</summary>
    public void Felled(long id)
    {
        if (Sim == null) return;
        foreach (var (of, o) in _trunks)
            if (of == id) Sim.Nav.SetActive(o, false);
    }
}
