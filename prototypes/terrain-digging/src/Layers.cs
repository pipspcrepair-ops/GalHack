namespace TerrainDigging;

/// <summary>Physics layers.</summary>
public static class Layers
{
    /// <summary>Terrain chunks and generated POIs.</summary>
    public const uint Terrain = 1u << 0;

    /// <summary>Placed building blocks. Separate so the tool can tell "hit a block" from "hit ground".</summary>
    public const uint Structure = 1u << 1;

    public const uint Player = 1u << 2;

    /// <summary>Crates, creatures and vehicles: anything the stuck guard watches besides players.</summary>
    public const uint Props = 1u << 3;

    public const uint Solid = Terrain | Structure;
}
