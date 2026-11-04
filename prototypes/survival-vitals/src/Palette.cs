using Godot;

namespace SurvivalVitals;

/// <summary>A 1970s paperback-cover palette, shared with the walkable-ship prototype.</summary>
public static class Palette
{
    public static readonly Color Hull = new("e9dfc4");
    public static readonly Color Orange = new("e85d04");
    public static readonly Color Yellow = new("f4a100");
    public static readonly Color Red = new("c1121f");
    public static readonly Color Teal = new("0a9396");
    public static readonly Color Dark = new("2b2d42");
    public static readonly Color Deck = new("3a3f47");
    public static readonly Color Cream = new("ffe8c2");
    public static readonly Color Sand = new("d08850");
    public static readonly Color Rust = new("a0522d");
    public static readonly Color Snow = new("b8d2f6");
    public static readonly Color Ice = new("74c6e6");
    public static readonly Color Basalt = new("2a2422");
    public static readonly Color Lava = new("ff5a1f");
    public static readonly Color RadGlow = new("9cff57");
    public static readonly Color RadGround = new("7b8f3a");
    public static readonly Color Gas = new("8f6fb0");
    public static readonly Color Foliage = new("5b3f8c");
    public static readonly Color Fruit = new("ff7b39");
    public static readonly Color Glass = new(0.55f, 0.8f, 0.9f, 0.22f);

    public static StandardMaterial3D Solid(Color color, float roughness = 0.8f, float metallic = 0f) =>
        new() { AlbedoColor = color, Roughness = roughness, Metallic = metallic };

    /// <summary>Lit from within: lamps, screens, lava, crystals.</summary>
    public static StandardMaterial3D Glow(Color color, float energy = 2f) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
    };

    public static StandardMaterial3D Clear(Color color) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.1f,
        Metallic = 0.2f,
    };

    /// <summary>Unshaded and additive: beams and gas that should read from far away.</summary>
    public static StandardMaterial3D Beam(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        DisableFog = true,
    };
}

/// <summary>Physics layers.</summary>
public static class Layers
{
    /// <summary>The ground, structures, rocks: anything fixed in the world.</summary>
    public const uint World = 1u << 0;
}
