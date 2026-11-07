using GalHack.Stars;
using Godot;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>Where the sky's stars stand from where you are: one answer for the map, the visor and the tests.</summary>
public partial class Spaceflight
{
    /// <summary>The directions of the zone you are in, into the globe's.</summary>
    public N.Quaternion ToGlobeHere => Game.InOrbit ? N.Quaternion.Conjugate(Orbit.Turn) : Game.Surface.Sky.ToGlobe;

    /// <summary>The direction to look, in the world, to see a catalogue star.</summary>
    public Vector3 LookToward(int star)
    {
        GalaxySky stars = Game.Surface.Sky.Stars;
        N.Vector3 d = stars.Toward(stars.View.Direction[star], ToGlobeHere);
        return new Vector3(d.X, d.Y, d.Z);
    }
}
