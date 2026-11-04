using System.Collections.Generic;
using Godot;

namespace Invasions.World;

/// <summary>
/// A 1970s paperback-cover palette after Chris Foss: chalky cream hulls with bold orange, yellow and
/// teal for the settlers; oxblood, bone and black for the Vharn. Materials are shared so a base of a
/// few hundred blocks stays cheap to draw.
/// </summary>
public static class Palette
{
    // Settlers.
    public static readonly Color Cream = new("e9dfc4");
    public static readonly Color CreamShade = new("cfc3a2");
    public static readonly Color Orange = new("e85d04");
    public static readonly Color Yellow = new("f4a100");
    public static readonly Color Teal = new("0a9396");
    public static readonly Color TealDeep = new("005f73");
    public static readonly Color Dark = new("2b2d42");
    public static readonly Color Deck = new("3a3f47");
    public static readonly Color Amber = new("ffb703");

    // Vharn.
    public static readonly Color Oxblood = new("a4161a");
    public static readonly Color Maroon = new("660708");
    public static readonly Color Bone = new("e5d3b3");
    public static readonly Color Black = new("161a1d");
    public static readonly Color Gunmetal = new("3d4148");
    public static readonly Color Acid = new("b5e48c");

    // Effects.
    public static readonly Color SirenRed = new("ff2a1a");
    public static readonly Color TracerSettler = new("ff9e2c");
    public static readonly Color TracerVharn = new("4dff7a");
    public static readonly Color Fire = new("ff7b00");

    static readonly Dictionary<(Color, float, float, bool), StandardMaterial3D> Cache = new();

    /// <summary>A shared matte material.</summary>
    public static StandardMaterial3D Mat(Color color, float roughness = 0.8f, float metallic = 0f) =>
        Get(color, roughness, metallic, 0f, false);

    /// <summary>A shared glowing material.</summary>
    public static StandardMaterial3D Glow(Color color, float energy = 2.5f) => Get(color, 0.5f, 0f, energy, false);

    /// <summary>A shared flat, unlit material for effects.</summary>
    public static StandardMaterial3D Unlit(Color color) => Get(color, 1f, 0f, 0f, true);

    static StandardMaterial3D Get(Color color, float roughness, float metallic, float glow, bool unlit)
    {
        var key = (color, roughness, metallic + glow * 10f, unlit);
        if (Cache.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
        if (glow > 0f)
        {
            m.EmissionEnabled = true;
            m.Emission = color;
            m.EmissionEnergyMultiplier = glow;
        }
        if (unlit)
        {
            m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            if (color.A < 1f) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }
        Cache[key] = m;
        return m;
    }

    /// <summary>Yellow and black hazard chevrons, for doors and spill crates.</summary>
    public static Texture2D Hazard => _hazard ??= MakeStripes(Yellow, Black, 32);
    static Texture2D _hazard;

    /// <summary>A bone and oxblood check, the Vharn livery.</summary>
    public static Texture2D VharnCheck => _check ??= MakeCheck(Bone, Black);
    static Texture2D _check;

    static Texture2D MakeStripes(Color a, Color b, int size)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                image.SetPixel(x, y, ((x + y) / (size / 4)) % 2 == 0 ? a : b);
        return ImageTexture.CreateFromImage(image);
    }

    static Texture2D MakeCheck(Color a, Color b)
    {
        var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgb8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                image.SetPixel(x, y, ((x / 2) + (y / 2)) % 2 == 0 ? a : b);
        return ImageTexture.CreateFromImage(image);
    }

    public static StandardMaterial3D Textured(Texture2D texture, Vector3 uvScale, float roughness = 0.7f)
    {
        return new StandardMaterial3D
        {
            AlbedoTexture = texture,
            Uv1Scale = uvScale,
            Roughness = roughness,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
        };
    }

    /// <summary>A box mesh child, the building block of everything in this prototype.</summary>
    public static MeshInstance3D Box(Node3D parent, Vector3 centre, Vector3 size, Material material)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = centre };
        parent.AddChild(mesh);
        return mesh;
    }

    public static MeshInstance3D Cylinder(Node3D parent, Vector3 centre, float radius, float height, Material material, int sides = 12)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = sides, Rings = 1 },
            MaterialOverride = material,
            Position = centre,
        };
        parent.AddChild(mesh);
        return mesh;
    }

    /// <summary>The font for signs, screens and the HUD: a narrow DIN-style face if the system has one.</summary>
    public static Font Font => _font ??= new SystemFont
    {
        FontNames = new[] { "Bahnschrift", "Eurostile", "DIN Alternate", "Arial Narrow", "Liberation Sans Narrow" },
        FontWeight = 600,
    };
    static Font _font;
}
