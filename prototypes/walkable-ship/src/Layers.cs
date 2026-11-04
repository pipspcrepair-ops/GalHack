namespace WalkableShip;

/// <summary>Physics layers. The outside world and the ghost cabins never touch each other.</summary>
public static class Layers
{
    /// <summary>The ground, rocks and anything else fixed in the world.</summary>
    public const uint World = 1u << 0;

    /// <summary>A ship's outer hull, as seen by players standing outside it.</summary>
    public const uint ShipHull = 1u << 1;

    /// <summary>Ghost cabins: the motionless copies of ship interiors that players aboard walk on.</summary>
    public const uint Interior = 1u << 2;
}
