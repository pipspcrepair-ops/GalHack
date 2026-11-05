using Godot;
using TerrainDigging.Terrain;

namespace PhysicsSandbox.World;

/// <summary>
/// The sandbox's colours, reused from the prototypes it brings together: terrain-digging's
/// orange soil and banded rust rock under a teal sky that warms to peach at the horizon, with a
/// ringed neighbour planet, and ship-handling's cream hulls with hot orange, yellow, teal and
/// pillar-box red. A 1970s paperback cover after Chris Foss.
/// </summary>
public static class Palette
{
    // Ground (terrain-digging).
    public static readonly Color Soil = new("e8873e");
    public static readonly Color Rock = new("c24a2c");
    public static readonly Color DeepRock = new("7a2f5f");
    public static readonly Color Ore = new("2ff5d2");
    public static readonly Color Bedrock = new("2a2550");
    public static readonly Color Dust = new("f6c487");

    // Sky (terrain-digging).
    public static readonly Color SkyTop = new("23708f");
    public static readonly Color SkyHorizon = new("ffd6a3");
    public static readonly Color GroundHorizon = new("d9804f");
    public static readonly Color Sun = new("ffe0b0");
    public static readonly Color Haze = new("f2b88a");
    public static readonly Color Shadow = new("6b5a8e");
    public static readonly Color Planet = new("c9a46e");
    public static readonly Color PlanetBand = new("8a5a3c");

    // Hulls and markings (ship-handling).
    public static readonly Color Hull = new("e9dfc4");
    public static readonly Color Orange = new("e85d04");
    public static readonly Color Yellow = new("f4a100");
    public static readonly Color Red = new("c1121f");
    public static readonly Color Teal = new("0a9396");
    public static readonly Color Dark = new("2b2d42");
    public static readonly Color Flame = new("ffb347");
    public static readonly Color HoverGlow = new("5ee0e6");

    // The lake: its surface, and the murk under it.
    public static readonly Color Water = new("1f8a8a");
    public static readonly Color DeepWater = new("0d3b4a");

    // The HUD (terrain-digging).
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

    public static Color Hex(uint rgb) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f);
}
