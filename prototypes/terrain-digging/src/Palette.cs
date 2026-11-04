using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>
/// The GalHack look for this world: a 1970s paperback-cover palette after Chris Foss. Warm,
/// saturated ground under an indigo sky, and ore in a cold glowing aqua that nothing else
/// in the scene uses, so it reads instantly.
/// </summary>
public static class Palette
{
    public static readonly Color Soil = new("e8873e");
    public static readonly Color Rock = new("c24a2c");
    public static readonly Color DeepRock = new("7a2f5f");
    public static readonly Color Ore = new("2ff5d2");
    public static readonly Color Bedrock = new("2a2550");
    public static readonly Color Dust = new("f6c487");

    public static readonly Color SkyTop = new("23708f");
    public static readonly Color SkyHorizon = new("ffd6a3");
    public static readonly Color GroundHorizon = new("d9804f");
    public static readonly Color Sun = new("ffe0b0");
    public static readonly Color WaterDeep = new("12586e");
    public static readonly Color WaterShallow = new("2fa7b0");

    public static readonly Color Block = new("f4c22b");
    public static readonly Color BlockTrim = new("262a40");
    public static readonly Color BlockStripe = new("e2402f");
    public static readonly Color Beacon = new("efe4cc");
    public static readonly Color BeaconRed = new("d7263d");

    public static readonly Color Text = new("ffe8c2");
    public static readonly Color Warning = new("ff6b4a");
    public static readonly Color Caution = new("ffb347");
    public static readonly Color Good = new("8ef0a8");

    public static Color Of(CellMaterial m) => m switch
    {
        CellMaterial.Soil => Soil,
        CellMaterial.Rock => Rock,
        CellMaterial.DeepRock => DeepRock,
        CellMaterial.Ore => Ore,
        CellMaterial.Bedrock => Bedrock,
        _ => Colors.White,
    };

    static readonly Color[] LinearByMaterial = BuildLinear();

    static Color[] BuildLinear()
    {
        var colors = new Color[CellMaterials.Count];
        for (int i = 0; i < colors.Length; i++) colors[i] = Of((CellMaterial)i).SrgbToLinear();
        return colors;
    }

    /// <summary>
    /// Vertex attributes for a material: linear colour, plus flags in UV (x = ore glow,
    /// y = bedrock pattern). Flags live in UV rather than alpha so they blend independently.
    /// </summary>
    public static (Color color, Vector2 flags) Vertex(CellMaterial m) =>
        (LinearByMaterial[(int)m], new Vector2(m == CellMaterial.Ore ? 1f : 0f, m == CellMaterial.Bedrock ? 1f : 0f));
}
