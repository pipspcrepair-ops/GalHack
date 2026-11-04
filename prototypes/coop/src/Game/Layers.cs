namespace Coop;

/// <summary>Physics layers. Cabin interiors live in worlds of their own, so they never meet the rest.</summary>
public static class Layers
{
    /// <summary>The ground, rocks and anything else fixed in the world.</summary>
    public const uint World = 1u << 0;

    /// <summary>A ship's outer hull, as seen by players standing outside it.</summary>
    public const uint ShipHull = 1u << 1;

    /// <summary>Inside a ship's cabin world: the cabin, its cargo and anyone aboard.</summary>
    public const uint Interior = 1u << 2;
}
