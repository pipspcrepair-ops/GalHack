using System;
using Planets.Gen;
using Planets.Look;

namespace Planets.Surface;

/// <summary>
/// Colours and biomes at any point on the surface, blended from the world map's cells exactly the
/// way the orbit map blends them, so the ground under you matches what you saw from space.
/// Read-only after construction and safe to share between chunk-building threads.
/// </summary>
sealed class SurfacePalette
{
    public readonly WorldMap Map;
    public readonly SurfaceSampler Sampler;
    public readonly ArchetypeLook Look;
    public readonly Rgb Cliff, Seabed, RiverBed;
    public readonly Rgb GlowColor;

    readonly Rgb[] _cells;
    readonly float[] _glow;
    readonly Fractal _jitter, _clump;

    public SurfacePalette(WorldMap map, ArchetypeLook look)
    {
        Map = map;
        Look = look;
        Sampler = new SurfaceSampler(map);
        _cells = ColorMap.CellColors(map, look, out Rgb[] glow);
        _glow = new float[map.Count];
        for (int k = 0; k < map.Count; k++) _glow[k] = Math.Max(glow[k].R, Math.Max(glow[k].G, glow[k].B));
        _jitter = new Fractal(Hash.Of(map.Spec.Seed, 0x6A6974), ColorMap.BorderWave, 4);
        _clump = new Fractal(Hash.Of(map.Spec.Seed, 0x636C756D), 70, 3);

        (Cliff, GlowColor) = (map.Def.Archetype, map.Def.Variant) switch
        {
            (Archetype.Temperate, _) => (BiomeLook.Color(Biome.Cliff), Rgb.Hex("ffffff")),
            (Archetype.Arid, _) => (BiomeLook.Color(Biome.RedRock), Rgb.Hex("ffffff")),
            (Archetype.Ice, _) => (BiomeLook.Color(Biome.BlueIce), Rgb.Hex("40fff0")),
            (Archetype.Lava, _) => (BiomeLook.Color(Biome.Obsidian), Rgb.Hex("ff4a10")),
            (Archetype.Alien, 0) => (BiomeLook.Color(Biome.CrystalSpires), Rgb.Hex("e070ff")),
            _ => (BiomeLook.Color(Biome.Mycelium), Rgb.Hex("30ffc0")),
        };
        Seabed = Rgb.Lerp(look.DeepSea, Rgb.Hex("c8b890"), 0.35f) * 0.8f;
        RiverBed = Rgb.Lerp(look.DeepSea, Rgb.Hex("5a4a3a"), 0.6f);
    }

    /// <summary>Blended land colour at a point and how strongly it glows (0-1).</summary>
    public Rgb ColorAt(double x, double y, out float glow)
    {
        Jitter(x, y, out double gx, out double gy);
        // By the shore a border can wander right out over water: the colour here, then.
        if (Land(gx, gy, out Rgb c, out glow) || Land(x / Map.CellSize - 0.5, y / Map.CellSize - 0.5, out c, out glow)) return c;
        return Seabed;
    }

    /// <summary>The land colours round a grid position, blended; false if there is only water there.</summary>
    bool Land(double gx, double gy, out Rgb color, out float glow)
    {
        int i0 = (int)Math.Floor(gx), j0 = (int)Math.Floor(gy);
        float tx = (float)(gx - i0), ty = (float)(gy - j0);
        Rgb c = default;
        float g = 0f, total = 0f;
        Add(i0, j0, (1 - tx) * (1 - ty), ref c, ref g, ref total);
        Add(i0 + 1, j0, tx * (1 - ty), ref c, ref g, ref total);
        Add(i0, j0 + 1, (1 - tx) * ty, ref c, ref g, ref total);
        Add(i0 + 1, j0 + 1, tx * ty, ref c, ref g, ref total);
        color = total > 0f ? c * (1f / total) : default;
        glow = total > 0f ? g / total : 0f;
        return total > 0f;
    }

    /// <summary>The biome at a point, with the same wandering borders as the colours.</summary>
    public Biome BiomeAt(double x, double y)
    {
        Jitter(x, y, out double gx, out double gy);
        int k = Map.Index((int)Math.Round(gx), (int)Math.Round(gy));
        return (Biome)Map.Biome[k];
    }

    /// <summary>Scatter density multiplier in about [0.2, 1.8]: trees grow in groves, rocks in fields.</summary>
    public float Clump(double x, double y) =>
        Math.Clamp(1f + 1.4f * _clump.Fbm(Map.Model.Torus.At(x, y), 4), 0.2f, 1.8f);

    void Jitter(double x, double y, out double gx, out double gy)
    {
        P4 p = Map.Model.Torus.At(x, y);
        gx = x / Map.CellSize - 0.5 + _jitter.Fbm(p, 4) * ColorMap.BorderWander;
        gy = y / Map.CellSize - 0.5 + _jitter.Fbm(p.Swap, 4) * ColorMap.BorderWander;
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
