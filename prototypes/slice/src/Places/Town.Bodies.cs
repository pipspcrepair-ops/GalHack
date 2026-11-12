using System;
using GalHack.Physics;
using Godot;
using Wildlife;

namespace Slice.Places;

/// <summary>The town's dead: what their limp bodies come to rest on, and those of whom nothing is left.</summary>
public sealed partial class Town
{
    /// <summary>
    /// The top of whatever is under a point in the place's frame (floor, step, a crate or a bed), for a
    /// body coming to rest on it; with nothing solid within reach, the floor they stood on.
    /// </summary>
    float FloorUnder(Vector3 local, float feet)
    {
        if (_place == null || !_place.IsInsideTree()) return feet;
        Vector3 from = _place.ToWorld(local + Vector3.Up * 0.6f);
        var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 2.2f, Layers.World | Layers.Hull | Layers.Interior);
        var hit = _place.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count == 0) return feet;
        return _place.FromWorld((Vector3)hit["position"]).Y;
    }

    /// <summary>A body's limp fall comes to rest on what is under it.</summary>
    void GroundBody(Person p)
    {
        if (p.Rig != null) p.Rig.Ground = q => FloorUnder(q, p.Pos.Y);
    }

    /// <summary>Blown apart or burned away: nothing left to draw or pose (what they carried is left where they were).</summary>
    public void Unbody(Person p)
    {
        p.Gone = true;
        if (p.View != null) p.View.Visible = false;
    }
}
