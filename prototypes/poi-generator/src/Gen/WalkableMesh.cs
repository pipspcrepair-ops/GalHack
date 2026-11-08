#nullable enable
namespace PoiGenerator.Gen;

/// <summary>
/// How a place is meshed where you walk into it. A door block is a doorway's state, not a solid: it never
/// collides, so a leaf the mesher draws there (a shut door, or the top of a jammed one hanging into the opening)
/// is a panel you walk straight through. Where a view has a door system of its own (the slice's places: leaves
/// that block until you open them with E) it draws the doors itself; where it has none (a roadside ruin, the
/// planet-surface view) nothing is drawn in a doorway at all. Either way nothing hangs in an opening without
/// collision.
/// </summary>
public static class WalkableMesh
{
    public static MeshOptions On(Int3 min)
    {
        MeshOptions o = MeshOptions.OnPlanet(min);
        o.Doors = false;
        return o;
    }
}
