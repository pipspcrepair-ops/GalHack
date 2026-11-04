namespace TerrainDigging.Terrain;

/// <summary>
/// What a cell is made of. Stored as a byte in chunk arrays, saves and network messages,
/// so values must never be renumbered.
/// </summary>
public enum CellMaterial : byte
{
    Air = 0,
    Soil = 1,
    Rock = 2,
    DeepRock = 3,
    Ore = 4,
    Bedrock = 5,
}

public static class CellMaterials
{
    public const int Count = 6;

    public static bool IsSolid(this CellMaterial m) => m != CellMaterial.Air;

    public static string Label(this CellMaterial m) => m switch
    {
        CellMaterial.Soil => "Soil",
        CellMaterial.Rock => "Rock",
        CellMaterial.DeepRock => "Deep rock",
        CellMaterial.Ore => "Ore",
        CellMaterial.Bedrock => "Bedrock",
        _ => "Air",
    };
}
