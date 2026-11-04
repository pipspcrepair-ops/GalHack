using Godot;

namespace Wildlife;

public enum WorldKind { Temperate, Alien, Bloom }

/// <summary>
/// How a planet looks and how alien its life is. Colours lean on 1970s paperback skies: hazy
/// gradients, bold flat colour, a big neighbouring world overhead. Keys 1-3 switch between
/// these. The Bloom world shares the alien world's seed, so its creatures are the alien
/// world's fauna, corrupted.
/// </summary>
public sealed class WorldStyle
{
    public WorldKind Kind;
    public string Label = "";
    public ulong FaunaSeed;
    public float Alienness;
    public bool Bloom;

    public Color SkyTop, SkyHorizon, GroundHorizon, NightTop, NightHorizon, Fog, Sun, Moon;
    public Color GroundA, GroundB, GrassBase, GrassTip, GrassGlow;
    public Color RockA, RockB;
    public float FogDensity, GrassGlowStrength;
    public TreeStyle Trees;

    public static WorldStyle For(WorldKind kind) => kind switch
    {
        WorldKind.Alien => new WorldStyle
        {
            Kind = kind, Label = "alien world", FaunaSeed = 20202, Alienness = 0.85f,
            SkyTop = new Color("2c1a5c"), SkyHorizon = new Color("ff9e6b"), GroundHorizon = new Color("e05a7a"),
            NightTop = new Color("07051a"), NightHorizon = new Color("3a1a4a"), Fog = new Color("f2a07a"),
            Sun = new Color("ffd1a6"), Moon = new Color("6fe3d0"),
            GroundA = new Color("1d5c66"), GroundB = new Color("3a8c80"),
            GrassBase = new Color("1d5e6e"), GrassTip = new Color("e86fa8"), GrassGlow = new Color("ff7ad9"),
            RockA = new Color("c9a6e0"), RockB = new Color("8f6fb8"),
            FogDensity = 0.004f, GrassGlowStrength = 0.6f, Trees = TreeStyle.Alien,
        },
        WorldKind.Bloom => new WorldStyle
        {
            Kind = kind, Label = "Bloom-infested world", FaunaSeed = 20202, Alienness = 0.85f, Bloom = true,
            SkyTop = new Color("1a0a1c"), SkyHorizon = new Color("b8466e"), GroundHorizon = new Color("5a1838"),
            NightTop = new Color("050208"), NightHorizon = new Color("2a0818"), Fog = new Color("8a3256"),
            Sun = new Color("ffb0c8"), Moon = new Color("ff4fa8"),
            GroundA = new Color("2a1622"), GroundB = new Color("45213a"),
            GrassBase = new Color("1a0c16"), GrassTip = new Color("6a2a4e"), GrassGlow = new Color("ff3cac"),
            RockA = new Color("3a2a36"), RockB = new Color("5e3a50"),
            FogDensity = 0.007f, GrassGlowStrength = 1.0f, Trees = TreeStyle.Bloom,
        },
        _ => new WorldStyle
        {
            Kind = kind, Label = "temperate world", FaunaSeed = 10101, Alienness = 0.1f,
            SkyTop = new Color("3f78b5"), SkyHorizon = new Color("f3dcae"), GroundHorizon = new Color("c9a86a"),
            NightTop = new Color("060b1c"), NightHorizon = new Color("1c2a48"), Fog = new Color("dfd2b0"),
            Sun = new Color("fff0d0"), Moon = new Color("e8e2c8"),
            GroundA = new Color("587a26"), GroundB = new Color("8a8a36"),
            GrassBase = new Color("3f6a22"), GrassTip = new Color("d6c25e"), GrassGlow = new Color("000000"),
            RockA = new Color("9a8f80"), RockB = new Color("c2a578"),
            FogDensity = 0.0025f, GrassGlowStrength = 0f, Trees = TreeStyle.Temperate,
        },
    };
}

public enum TreeStyle { Temperate, Alien, Bloom }
