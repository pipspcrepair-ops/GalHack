namespace ShipHandling.Physics;

/// <summary>Physics layers used by the flight module.</summary>
public static class Layers
{
    /// <summary>Terrain, rocks and anything else fixed to the planet.</summary>
    public const uint World = 1u << 0;

    /// <summary>Vessel hulls: one body per vessel.</summary>
    public const uint ShipHull = 1u << 1;

    /// <summary>
    /// Water surfaces. Only probes see them: hover engines ride on water as if it were ground,
    /// and landing gear can set down on it, but hulls pass through.
    /// </summary>
    public const uint Water = 1u << 2;

    public const uint Solid = World | ShipHull;
    public const uint Probe = World | ShipHull | Water;
}
