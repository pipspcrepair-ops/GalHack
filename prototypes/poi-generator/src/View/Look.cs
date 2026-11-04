using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// The 1970s sci-fi paperback look: flat fields of strong colour, hazy horizons, a huge
/// world hanging in the sky. Each state gets its own sky so a place reads at a glance.
/// </summary>
public static class Look
{
    public static Color ToColor(Rgb c) => new(c.R / 255f, c.G / 255f, c.B / 255f);
    public static Rgb ToRgb(Color c) => new((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255));

    public static Vector3 ToVector(Dir d)
    {
        Int3 v = d.Vec();
        return new Vector3(v.X, v.Y, v.Z);
    }

    public static Rgb Ground(Biome b) => b switch
    {
        Biome.Desert => new Rgb(0xc9, 0x87, 0x4e),
        Biome.Rock => new Rgb(0x8f, 0x5a, 0x3e),
        Biome.Ash => new Rgb(0x5e, 0x58, 0x56),
        Biome.Ice => new Rgb(0xb4, 0xc8, 0xd2),
        Biome.Steppe => new Rgb(0x9c, 0x94, 0x4c),
        _ => new Rgb(0x6e, 0x4a, 0x78), // fungal flats
    };

    public static Rgb Rock(Biome b) => Ground(b).Lerp(new Rgb(0x3a, 0x2a, 0x24), 0.45f);

    /// <summary>A state's sky, sun and fog.</summary>
    public record Atmosphere(
        Color SkyTop, Color Horizon, Color GroundHorizon, Color GroundBottom,
        Color Sun, float SunEnergy, Vector3 SunAngles, float Ambient,
        Color Fog, float FogDensity, Color Planet, float PlanetScale, Vector3 PlanetAt, bool Ash);

    // Low ambient light everywhere: shadow sides go dark, so silhouettes read against the sky
    // and anything that glows carries the picture.
    public static Atmosphere For(PoiState s) => s switch
    {
        // Dusk, a burning horizon under deep blue, a low hard sun: the place is alive.
        PoiState.Active => new(new Color("0b1636"), new Color("f2873a"), new Color("8a3a22"), new Color("1a0c0a"),
            new Color("ffb878"), 1.7f, new Vector3(-11f, 35f, 0f), 0.3f,
            new Color("c86a3a"), 0.0011f, new Color("7fb7be"), 1f, new Vector3(-2300f, 1500f, -3800f), false),
        // Blue hour by moonlight, cold and still. Nobody has been here for a long time.
        PoiState.Abandoned => new(new Color("040914"), new Color("2c4c6a"), new Color("1a2a38"), new Color("06090c"),
            new Color("a8c4f0"), 0.85f, new Vector3(-22f, -40f, 0f), 0.2f,
            new Color("1e3044"), 0.0024f, new Color("dfe8ea"), 1.3f, new Vector3(2600f, 1900f, -3600f), false),
        // Bruised violet, a thin sick-green horizon. The pink flesh and the green glow do the rest.
        PoiState.Infested => new(new Color("10061a"), new Color("6e8a2c"), new Color("2a1a2e"), new Color("0a050c"),
            new Color("c8d890"), 0.7f, new Vector3(-10f, 120f, 0f), 0.22f,
            new Color("30243a"), 0.0026f, new Color("b0306a"), 1.1f, new Vector3(-3200f, 1300f, -2900f), false),
        // Ash light: a pale sun through dust, and ash still falling.
        _ => new(new Color("3a3634"), new Color("c08a58"), new Color("6a5040"), new Color("1e1a18"),
            new Color("f2dab0"), 1.15f, new Vector3(-28f, -60f, 0f), 0.36f,
            new Color("9a806a"), 0.003f, new Color("e8c89a"), 0.8f, new Vector3(1800f, 1200f, -4200f), true),
    };
}
