using System.Collections.Generic;
using Godot;
using PlanetSurface.World;
using Wildlife;
using FloraSolid = PlanetSurface.Terrain.FloraSolid;

namespace Slice.Surface;

/// <summary>
/// The patch's trees and rocks, for its creatures and people to walk round: each patch is baked with the ground's own
/// scatter round it (<see cref="ScatterSolids"/>, worked out by chunk, nothing streamed in) in its nav grid, the
/// footprint circles of each trunk, low limb, stalk and rock, so steering slides round them and a path goes round a
/// grove. All of it on the bake's worker; a tree felled goes from the grid with it.
/// </summary>
public partial class WildlifePatch
{
    /// <summary>Each footprint's obstacle with its plant's scatter id; whether this patch was baked with its woods.</summary>
    List<(long Id, Obstacle O)> _trunks = new();
    bool _wooded;

    /// <summary>Tests: keep the woods out of the grid (how it was before, to measure against).</summary>
    public static bool NoWoods;

    /// <summary>Whether this patch's woods are in its grid, and how many footprints (tests).</summary>
    public bool Wooded => _wooded;
    public int Trunks => _trunks.Count;

    /// <summary>The woods' footprints read into a new grid round its origin (on the bake's worker); null with no woods to read.</summary>
    static List<(long, Obstacle)> Wood(PlanetWorld planet, NavGrid nav, Vector3 origin, float half, List<FloraSolid> near)
    {
        if (near == null) return null;
        var feet = new List<(long Id, Vector3 At, float R)>();
        ScatterSolids.Footprints(planet, near, origin, feet);
        var trunks = new List<(long, Obstacle)>();
        foreach (var (id, at, r) in feet)
        {
            Vector3 local = at - origin;
            if (Mathf.Abs(local.X) <= half && Mathf.Abs(local.Z) <= half) trunks.Add((id, nav.AddObstacle(local with { Y = 0f }, r)));
        }
        return trunks;
    }

    /// <summary>A new patch's woods take over; any tree felled since they were read goes at once.</summary>
    void TakeWoods(List<(long Id, Obstacle O)> trunks, NavGrid nav)
    {
        _wooded = trunks != null;
        _trunks = trunks ?? new();
        foreach (var (id, o) in _trunks)
            if (_planet.Felled.Contains(id)) nav.SetActive(o, false);
    }

    /// <summary>A tree felled: nothing walks round it any more.</summary>
    public void Felled(long id)
    {
        if (Sim == null) return;
        foreach (var (of, o) in _trunks)
            if (of == id) Sim.Nav.SetActive(o, false);
    }
}
