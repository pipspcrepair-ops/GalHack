using System.Collections.Generic;
using System;
using GalHack.Terrain;
using Planets.Gen;
using Planets.Look;
using TerrainDigging.Terrain;
using Godot;

namespace PlanetSurface.Terrain;

/// <summary>
/// The colours of the ground, shared by the voxel chunks near the player and the heightfield
/// rings beyond them so the two meet without a visible line. Soil takes its biome's colour,
/// blended across wandering biome borders exactly the way the planet generator blends its map;
/// rock, deep rock and bedrock have one colour per world; ore takes its deposit's colour.
///
/// Colours are grounded and readable (soil, sand, ash, snow, stone), each world keeping its own
/// character: red sandstone canyons, black basalt, violet crystal rock, pale mycelium flats.
/// Read-only after construction; mesh workers share it.
/// </summary>
public sealed class GroundPalette
{
    public readonly WorldMap Map;
    public readonly Rgb Rock, DeepRock, Bedrock, Seabed, RiverBed, Snow;

    /// <summary>
    /// A blast's dead zone (ground zero in world metres, the dead radius and where it has faded back
    /// to the land's own colours): the ground goes ash-grey and lifeless toward the crater.
    /// </summary>
    public readonly List<(double X, double Z, float Dead, float Fade)> DeadZones = new();

    /// <summary>How dead the land is at a point: 1 inside a blast's dead zone, easing to 0 at its fade, 0 elsewhere.</summary>
    public float Deadness(double x, double z)
    {
        float dead = 0f;
        foreach (var (gx, gz, d0, d1) in DeadZones)
        {
            double dx = Mathx.WrapDelta(x - gx, Map.WidthMeters), dz = Mathx.WrapDelta(z - gz, Map.HeightMeters);
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d >= d1) continue;
            dead = Math.Max(dead, d <= d0 ? 1f : 1f - SmoothStep(0f, 1f, (d - d0) / (d1 - d0)));
        }
        return dead;
    }

    /// <summary>How strongly rock faces show horizontal strata (0-1): canyons and mesas show them most.</summary>
    public readonly float Strata;

    readonly Rgb[] _cells;
    readonly float[] _glow;
    readonly Fractal _jitter, _tone;

    public GroundPalette(WorldMap map)
    {
        Map = map;
        _jitter = new Fractal(Hash.Of(map.Spec.Seed, 0x6A6974), ColorMap.BorderWave, 4);
        _tone = new Fractal(Hash.Of(map.Spec.Seed, 0x746F6E), 700, 3);
        _cells = new Rgb[map.Count];
        _glow = new float[map.Count];
        for (int k = 0; k < map.Count; k++)
        {
            var biome = (Biome)map.Biome[k];
            _cells[k] = Soil(biome);
            _glow[k] = biome switch { Biome.LavaField => 1f, Biome.HotSpring => 0.12f, Biome.GlowMoss => 0.1f, _ => 0f };
        }
        _cells = ColorMap.Soften(map, _cells);

        (Rock, Strata) = (map.Def.Archetype, map.Def.Variant) switch
        {
            (Archetype.Temperate, _) => (Rgb.Hex("726b61"), 0.25f),
            (Archetype.Arid, 0) => (Rgb.Hex("a2725a"), 0.55f),
            (Archetype.Arid, _) => (Rgb.Hex("9c5436"), 0.9f),
            (Archetype.Ice, _) => (Rgb.Hex("5d646e"), 0.3f),
            (Archetype.Lava, _) => (Rgb.Hex("2f2a2b"), 0.35f),
            (Archetype.Alien, 0) => (Rgb.Hex("6f6185"), 0.5f),
            _ => (Rgb.Hex("6b5a52"), 0.3f),
        };
        DeepRock = Rock * 0.72f;
        Bedrock = Rgb.Hex("26262b");
        Seabed = Rgb.Lerp(Rock, Rgb.Hex("b0a48a"), 0.45f) * 0.85f;
        RiverBed = Rgb.Lerp(Rock, Rgb.Hex("4a4034"), 0.6f);
        Snow = Rgb.Hex("eef2f6");
    }

    /// <summary>A grounded colour for each biome's topsoil, sand, ash or snow.</summary>
    public static Rgb Soil(Biome b) => b switch
    {
        Biome.Beach => Rgb.Hex("c8b386"),
        Biome.Grassland => Rgb.Hex("5b7838"),
        Biome.Meadow => Rgb.Hex("76863c"),
        Biome.Forest => Rgb.Hex("40592c"),
        Biome.PineForest => Rgb.Hex("3e4e34"),
        Biome.Tundra => Rgb.Hex("7d7a5c"),
        Biome.Snow => Rgb.Hex("e4e9ee"),
        Biome.Cliff => Rgb.Hex("6e675e"),
        Biome.Riverbank => Rgb.Hex("4d6c33"),
        Biome.Jungle => Rgb.Hex("3b6829"),
        Biome.Rainforest => Rgb.Hex("345d2b"),
        Biome.Swamp => Rgb.Hex("4f5a31"),
        Biome.CloudForest => Rgb.Hex("486848"),
        Biome.Dunes => Rgb.Hex("c99860"),
        Biome.Erg => Rgb.Hex("d6ad76"),
        Biome.SaltFlat => Rgb.Hex("dcd4c2"),
        Biome.Scrub => Rgb.Hex("998a56"),
        Biome.RedRock => Rgb.Hex("9c5638"),
        Biome.Badlands => Rgb.Hex("ad6e47"),
        Biome.MesaTop => Rgb.Hex("b78a5a"),
        Biome.Oasis => Rgb.Hex("55783a"),
        Biome.Glacier => Rgb.Hex("dde7ef"),
        Biome.Snowfield => Rgb.Hex("edf1f5"),
        Biome.IceTundra => Rgb.Hex("888e96"),
        Biome.BlueIce => Rgb.Hex("9dc1da"),
        Biome.HotSpring => Rgb.Hex("6aa59c"),
        Biome.Sinter => Rgb.Hex("bf8f5e"),
        Biome.Basalt => Rgb.Hex("3a3436"),
        Biome.Ash => Rgb.Hex("5b5553"),
        Biome.LavaField => Rgb.Hex("2e2220"),
        Biome.Obsidian => Rgb.Hex("201b24"),
        Biome.Sulfur => Rgb.Hex("c2ae4a"),
        Biome.CrystalPlain => Rgb.Hex("6c5c86"),
        Biome.CrystalSpires => Rgb.Hex("8a6ca6"),
        Biome.Chalk => Rgb.Hex("d2c6d0"),
        Biome.GlowMoss => Rgb.Hex("3a8676"),
        Biome.FungalForest => Rgb.Hex("874a3c"),
        Biome.SporeMarsh => Rgb.Hex("5e6a35"),
        Biome.Mycelium => Rgb.Hex("cbbfa4"),
        _ => Rgb.Hex("6a6a5a"),
    };

    /// <summary>Blended soil colour at a point (canonical metres) and how much it glows (lava fields, glow moss).</summary>
    public Rgb SoilAt(double x, double z, out float glow)
    {
        Jitter(x, z, out double gx, out double gz);
        Rgb c = default;
        float g = 0f, total = 0f;
        Around(gx, gz, ref c, ref g, ref total);
        if (total <= 0f) Around(x / Map.CellSize - 0.5, z / Map.CellSize - 0.5, ref c, ref g, ref total); // by the shore a border can wander right out over water
        if (total <= 0f)
        {
            glow = 0f;
            return Seabed;
        }
        glow = g / total;
        float tone = _tone.Fbm(Map.Model.Torus.At(x, z), 32);
        return c * ((1f / total) * (1f + 0.08f * tone));
    }

    /// <summary>The biome at a point, with the same wandering borders as the colours.</summary>
    public Biome BiomeAt(double x, double z)
    {
        Jitter(x, z, out double gx, out double gz);
        return (Biome)Map.Biome[Map.Index((int)Math.Round(gx), (int)Math.Round(gz))];
    }

    /// <summary>
    /// The colour a ground vertex shows. <paramref name="rock"/> is how bare the ground is (0
    /// soil, 1 rock face): the shader adds strata and rock detail by it. <paramref name="flags"/>
    /// carries glow (x) and bedrock (y).
    /// </summary>
    public Color Vertex(CellMaterial material, double x, double z, float height, out Vector2 flags, DepositKind ore = DepositKind.Iron)
    {
        flags = Vector2.Zero;
        Rgb c;
        float rock = 0f;
        switch (material)
        {
            case CellMaterial.Ore:
            {
                // Ore shows in the rock as speckle and streaks of its colour, not a flat stain: a
                // texture over the rock, read by its pattern first, and only a faint glow.
                Rgb oreColour = DepositLook.Color(ore);
                float speck = SmoothStep(0.55f, 0.85f, ValueNoise(x * 1.7, z * 1.7 + height * 0.9, 41));
                float streak = MathF.Abs(ValueNoise(x * 0.45 + height * 0.3, z * 0.45, 43) - 0.5f) < 0.1f ? 1f : 0f;
                c = Rgb.Lerp(Rock, oreColour, 0.22f + 0.38f * speck + 0.25f * streak);
                flags.X = DepositLook.Glow(ore) * 0.12f;
                rock = 0.8f;
                break;
            }
            case CellMaterial.Bedrock:
                c = Bedrock;
                flags.Y = 1f;
                rock = 1f;
                break;
            case CellMaterial.DeepRock:
                c = DeepRock;
                rock = 1f;
                break;
            case CellMaterial.Rock:
                c = Rock;
                rock = 1f;
                break;
            default:
                c = SoilAt(x, z, out float glow);
                flags.X = glow;
                if (height < 0.6f) c = Rgb.Lerp(c, Seabed, Math.Clamp(0.6f - height, 0f, 1f)); // wet sand and silt at the waterline
                break;
        }
        return new Color(c.R, c.G, c.B, rock);
    }

    /// <summary>
    /// The colour of undug ground: soil over rock by <paramref name="cover"/> (0 bare rock, 1 all
    /// soil), continuous so the near voxels and the far rings blend the same way.
    /// </summary>
    public Color Natural(double x, double z, float height, float cover, out Vector2 flags)
    {
        Rgb soil = SoilAt(x, z, out float glow);
        // In a blast's dead zone the soil is burnt to ash, greyer and darker toward ground zero.
        if (DeadZones.Count > 0)
        {
            float dead = Deadness(x, z);
            if (dead > 0f)
            {
                float l = soil.R * 0.3f + soil.G * 0.59f + soil.B * 0.11f;
                var ash = new Rgb(l * 0.78f + 0.06f, l * 0.74f + 0.055f, l * 0.7f + 0.05f);
                soil = Rgb.Lerp(soil, ash, dead * (0.75f + 0.2f * ValueNoise(x * 0.05, z * 0.05, 47)));
                glow *= 1f - dead;
            }
        }
        Rgb c = Rgb.Lerp(Rock, soil, cover);
        if (height < 0.6f) c = Rgb.Lerp(c, Seabed, Math.Clamp(0.6f - height, 0f, 1f));
        flags = new Vector2(glow * cover, 0f);
        return new Color(c.R, c.G, c.B, 1f - cover);
    }

    // ---- made ground ----------------------------------------------------------------------

    /// <summary>
    /// A stamped place's made ground over the land's colour. Concrete aprons in slabs, tarmac with
    /// its cracks, gravel or packed-dirt yards; or the land's own ground, broken up so a pad is never
    /// a uniform carpet. It shows on the pad and its apron and gives way early in the blend, along
    /// a ragged edge, never a hard rectangle. A century of weather (<paramref name="wear"/>) cracks it
    /// and drifts the land's own sand, soil and weeds over it in patches.
    /// </summary>
    public Color Made(Color land, StampSurface surface, float weight, float wear, double x, double z)
    {
        if (weight <= 0f) return land;
        float edge = ValueNoise(x * 0.23, z * 0.23, 11) - 0.5f;
        float w = SmoothStep(0.5f + edge * 0.4f, 0.92f, weight);
        if (w <= 0f) return land;
        var landRgb = new Rgb(land.R, land.G, land.B);
        Rgb made;
        float hard = 1f;
        switch (surface)
        {
            case StampSurface.Natural:
                // The land's own ground, trodden and patchy: soil broken by stone and bare earth.
                made = Rgb.Lerp(landRgb, Rgb.Lerp(Rock, Seabed, 0.3f), 0.2f + 0.35f * ValueNoise(x * 0.11, z * 0.11, 23));
                hard = 0.4f;
                break;
            case StampSurface.Tarmac:
                made = new Rgb(0.21f, 0.21f, 0.22f) * (0.88f + 0.24f * ValueNoise(x * 0.6, z * 0.6, 5));
                break;
            case StampSurface.Gravel:
                made = new Rgb(0.5f, 0.47f, 0.42f) * (0.85f + 0.3f * ValueNoise(x * 1.3, z * 1.3, 7));
                break;
            case StampSurface.Dirt:
                made = Rgb.Lerp(new Rgb(0.43f, 0.34f, 0.25f), landRgb, 0.25f) * (0.85f + 0.3f * ValueNoise(x * 0.4, z * 0.4, 9));
                hard = 0.7f;
                break;
            default:
            {
                made = new Rgb(0.56f, 0.55f, 0.52f) * (0.9f + 0.18f * ValueNoise(x * 0.5, z * 0.5, 3));
                // Slab joints every six metres.
                double jx = x / 6 - Math.Floor(x / 6), jz = z / 6 - Math.Floor(z / 6);
                if (jx < 0.04 || jz < 0.04) made *= 0.8f;
                break;
            }
        }
        if (surface is StampSurface.Concrete or StampSurface.Tarmac)
        {
            // Cracks: thin dark lines along a ridge of noise, more of them the older it is.
            float ridge = MathF.Abs(ValueNoise(x * 0.19, z * 0.19, 17) - 0.5f);
            if (ridge < 0.015f + 0.035f * wear) made *= 0.55f;
        }
        // The weather takes it back in drifts: the land's own ground over it in patches.
        float drift = ValueNoise(x * 0.07, z * 0.07, 29);
        float over = SmoothStep(0.8f - 0.5f * wear, 1.05f, drift) * (0.3f + 0.7f * wear);
        made = Rgb.Lerp(made, landRgb, over);
        Rgb c = Rgb.Lerp(landRgb, made, w);
        return new Color(c.R, c.G, c.B, land.A + (1f - land.A) * w * hard * (1f - over));
    }

    static float SmoothStep(float a, float b, float v)
    {
        float t = Math.Clamp((v - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Smooth value noise in [0, 1] on a unit lattice: cheap, seeded, the same everywhere it is asked.</summary>
    static float ValueNoise(double x, double z, int salt)
    {
        double fx = Math.Floor(x), fz = Math.Floor(z);
        float tx = (float)(x - fx), tz = (float)(z - fz);
        tx = tx * tx * (3 - 2 * tx);
        tz = tz * tz * (3 - 2 * tz);
        long ix = (long)fx, iz = (long)fz;
        float a = Lattice(ix, iz, salt), b = Lattice(ix + 1, iz, salt), c = Lattice(ix, iz + 1, salt), d = Lattice(ix + 1, iz + 1, salt);
        return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
    }

    static float Lattice(long x, long z, int salt)
    {
        ulong h = (ulong)(x * 73856093L ^ z * 19349663L ^ salt * 83492791L);
        h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    void Around(double gx, double gz, ref Rgb c, ref float g, ref float total)
    {
        int i0 = (int)Math.Floor(gx), j0 = (int)Math.Floor(gz);
        float tx = (float)(gx - i0), tz = (float)(gz - j0);
        Add(i0, j0, (1 - tx) * (1 - tz), ref c, ref g, ref total);
        Add(i0 + 1, j0, tx * (1 - tz), ref c, ref g, ref total);
        Add(i0, j0 + 1, (1 - tx) * tz, ref c, ref g, ref total);
        Add(i0 + 1, j0 + 1, tx * tz, ref c, ref g, ref total);
    }

    void Jitter(double x, double z, out double gx, out double gz)
    {
        P4 p = Map.Model.Torus.At(x, z);
        gx = x / Map.CellSize - 0.5 + _jitter.Fbm(p, 4) * ColorMap.BorderWander;
        gz = z / Map.CellSize - 0.5 + _jitter.Fbm(p.Swap, 4) * ColorMap.BorderWander;
    }

    void Add(int i, int j, float w, ref Rgb c, ref float g, ref float total)
    {
        int k = Map.Index(i, j);
        if (Map.Has(k, CellFlags.Water)) return;
        c += _cells[k] * w;
        g += _glow[k] * w;
        total += w;
    }
}
