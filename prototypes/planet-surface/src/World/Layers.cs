namespace PlanetSurface.World;

/// <summary>Physics layers.</summary>
public static class Layers
{
    public const uint Terrain = 1 << 0;
    public const uint Structure = 1 << 1;
    public const uint Player = 1 << 2;
    public const uint Props = 1 << 3;
    public const uint Poi = 1 << 4;

    /// <summary>
    /// Solid flora: tree trunks and boulders (in the shared physics module's table these are
    /// fixed to the planet, its World layer). Separate here so ground probes (heights, digging)
    /// see only the ground.
    /// </summary>
    public const uint Flora = GalHack.Physics.Layers.Flora;

    /// <summary>Everything a body stands on and walks into.</summary>
    public const uint Solid = Terrain | Structure | Poi | Flora;
}
