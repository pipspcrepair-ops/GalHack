using GalHack.Stars;
using Godot;
using PlanetSurface.Skies;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>Where the sky's stars stand from where you are: one answer for the map, the visor and the tests.</summary>
public partial class Spaceflight
{
    /// <summary>The direction to look, in the world, to see a direction of the catalogue.</summary>
    public Vector3 LookAlong(N.Vector3 catalogue)
    {
        SkyRig sky = Game.Surface.Sky;
        return (sky.Stars.Field * sky.SkyToStars).Inverse() * new Vector3(catalogue.X, catalogue.Y, catalogue.Z);
    }

    /// <summary>The direction to look, in the world, to see a catalogue star.</summary>
    public Vector3 LookToward(int star) => LookAlong(Game.Surface.Sky.Stars.View.Direction[star]);
}
