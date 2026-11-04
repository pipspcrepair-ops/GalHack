namespace Saves;

/// <summary>Physics layers. Ship cabins and zones are worlds of their own, so these only matter within one world.</summary>
public static class Layers
{
    /// <summary>The ground, rocks, and anything built in the zone.</summary>
    public const uint World = 1u << 0;

    /// <summary>A ship's outer hull, as seen by players standing outside it.</summary>
    public const uint ShipHull = 1u << 1;

    /// <summary>Inside a ship's cabin world: the cabin, its cargo and anyone aboard.</summary>
    public const uint Interior = 1u << 2;

    /// <summary>Loose stacks: the look ray finds them, but nobody trips over them.</summary>
    public const uint Pickup = 1u << 3;
}