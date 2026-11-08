using System;
using System.Collections.Generic;
using Godot;

namespace Weather.Model;

/// <summary>
/// The land and climate at one point of a world, as the weather reads it. The world fills it in
/// from its own map (heights, slope, forest, humidity, warmth, water, soil); the weather never
/// knows how the map was made.
/// </summary>
/// <param name="Height">Metres above sea level (the land's, not the water's).</param>
/// <param name="Slope">Rise over run of the land there.</param>
/// <param name="Forest">How densely wooded, 0 open to 1 closed canopy.</param>
/// <param name="Humidity">How damp the climate is, 0 bone dry to 1 saturated.</param>
/// <param name="Warmth">How warm the climate is, 0 frozen to 1 scorching.</param>
/// <param name="Water">A sea or a lake there.</param>
/// <param name="Loose">How much of the ground is dry loose stuff the wind can lift (sand, ash, silt), 0..1.</param>
public readonly record struct Ground(float Height, float Slope, float Forest, float Humidity, float Warmth, bool Water, float Loose)
{
    /// <summary>A flat open plain, mild and damp enough for anything: what a world with no map gets everywhere.</summary>
    public static readonly Ground Plain = new(40f, 0.02f, 0f, 0.6f, 0.6f, false, 0.5f);
}

/// <summary>
/// What each region's land and climate allow (data/weather.json, "regions"). Every hazard forms only
/// where its rule holds: tornadoes over broad open flat or rolling land in warm humid air, never over
/// high mountains and rarely over dense forest; waterspouts over open water; dust over dry loose
/// ground; blizzards on cold or high ground; fog in damp low ground.
/// </summary>
public sealed class RegionRules
{
    /// <summary>How much colder the air is per kilometre up, °C.</summary>
    public float LapseRate { get; set; } = 6.5f;

    /// <summary>How many °C warmer than the world's middle its warmest ground is (and colder its coldest).</summary>
    public float WarmthDegrees { get; set; } = 22f;

    /// <summary>High ground: mountains from this many metres above sea level.</summary>
    public float MountainHeight { get; set; } = 420f;

    /// <summary>Steep ground: slopes past this.</summary>
    public float SteepSlope { get; set; } = 0.35f;

    /// <summary>How far round a point the land is looked at to call it broad and open, metres.</summary>
    public float BroadRadius { get; set; } = 350f;

    public TornadoGround Tornado { get; set; } = new();
    public DustGround Dust { get; set; } = new();
    public ColdGround Blizzard { get; set; } = new();
    public FogGround Fog { get; set; } = new();
    public WindGround Wind { get; set; } = new();

    /// <summary>What a weather becomes where its own can't form: a dust storm over wet woods is a windy overcast.</summary>
    public Dictionary<WeatherKind, WeatherKind> Fallback { get; set; } = new();

    /// <summary>How wooded each kind of ground is, 0..1, by the world map's biome names (unlisted: open).</summary>
    public Dictionary<string, float> Forest { get; set; } = new();

    /// <summary>How loose each kind of ground is, 0..1, by biome name (unlisted: firm). Dry loose ground lifts as dust.</summary>
    public Dictionary<string, float> Loose { get; set; } = new();
}

public sealed class TornadoGround
{
    /// <summary>Broad flat or rolling land: no slope round it steeper than this.</summary>
    public float MaxSlope { get; set; } = 0.14f;
    /// <summary>Nor this much rise between its lowest and highest points, metres.</summary>
    public float MaxRelief { get; set; } = 90f;
    /// <summary>Dense forest from here: tornadoes rarely form over it, and weaken over it.</summary>
    public float DenseForest { get; set; } = 0.6f;
    /// <summary>The odds one forms over dense forest at all, against open land.</summary>
    public float ForestOdds { get; set; } = 0.12f;
    /// <summary>Warm and humid enough: the region's warmth and humidity at least these.</summary>
    public float MinWarmth { get; set; } = 0.3f;
    public float MinHumidity { get; set; } = 0.3f;
    /// <summary>How strong the storm cell over it must be, 0..1.</summary>
    public float MinCell { get; set; } = 0.7f;
    /// <summary>Never this close to the player when it forms, metres (nothing spawns on top of you for drama).</summary>
    public float MinDistance { get; set; } = 280f;
}

public sealed class DustGround
{
    /// <summary>Dry loose ground: loose at least this, and no damper than that.</summary>
    public float MinLoose { get; set; } = 0.35f;
    public float MaxHumidity { get; set; } = 0.35f;
    /// <summary>Dust devils: hot clear days only, warm ground at least this, cloud cover at most that, wind below this, m/s.</summary>
    public float DevilWarmth { get; set; } = 0.5f;
    public float DevilCloud { get; set; } = 0.35f;
    public float DevilWind { get; set; } = 9f;
    /// <summary>Dust devils a minute over ideal ground.</summary>
    public float DevilsPerMinute { get; set; } = 0.8f;
}

public sealed class ColdGround
{
    /// <summary>Cold ground: warmth at most this, or high ground (above <see cref="RegionRules.MountainHeight"/>).</summary>
    public float MaxWarmth { get; set; } = 0.3f;
    /// <summary>Snow falls instead of rain below this local temperature, °C (and rain instead of snow above it plus two).</summary>
    public float SnowBelow { get; set; } = 1f;
}

public sealed class FogGround
{
    /// <summary>Damp ground: humidity at least this.</summary>
    public float MinHumidity { get; set; } = 0.45f;
    /// <summary>Low ground: this many metres below the land round it, or this close above the sea.</summary>
    public float ValleyDepth { get; set; } = 25f;
    public float LowHeight { get; set; } = 60f;
}

public sealed class WindGround
{
    /// <summary>The wind is this much stronger on high ground at its strongest (mountain tops), and this much weaker under dense forest.</summary>
    public float HighBoost { get; set; } = 0.45f;
    public float ForestCut { get; set; } = 0.4f;
}

/// <summary>
/// One world's own weather (data/weather.json, "worlds"): how stormy it is, how prone to tornadoes,
/// how dry or wet, how foggy and how cold, rolled from its world layers and climate on top of its
/// type's weights. Each dial scales the weights of the weathers it names.
/// </summary>
public sealed class WorldWeather
{
    public float Storminess { get; init; } = 1f;
    public float Tornadoes { get; init; } = 1f;
    public float Wetness { get; init; } = 1f;
    public float Dryness { get; init; } = 1f;
    public float Fog { get; init; } = 1f;
    public float Cold { get; init; } = 1f;

    public static readonly WorldWeather Neutral = new();

    public float Dial(string name) => name switch
    {
        "storminess" => Storminess,
        "tornadoes" => Tornadoes,
        "wetness" => Wetness,
        "dryness" => Dryness,
        "fog" => Fog,
        "cold" => Cold,
        _ => throw new FormatException($"no weather dial '{name}'"),
    };

    /// <summary>
    /// Rolls a world's dials: each fact (temp=hot, water=sea...) and each of its layers scales them by
    /// the data's numbers, then each takes a random lean of its own, so two worlds of a kind differ.
    /// </summary>
    public static WorldWeather Roll(WorldRules rules, IReadOnlyDictionary<string, string> facts, IEnumerable<string> layers, ulong seed)
    {
        var dials = new Dictionary<string, float>();
        foreach (string d in rules.Dials.Keys) dials[d] = 1f;
        void Apply(Dictionary<string, float> by)
        {
            foreach (var (d, k) in by) dials[d] = dials.GetValueOrDefault(d, 1f) * k;
        }
        if (facts != null)
            foreach (var (fact, value) in facts)
                if (rules.Facts.TryGetValue($"{fact}={value}", out var by)) Apply(by);
        if (layers != null)
            foreach (string id in layers)
                if (rules.Layers.TryGetValue(id, out var by)) Apply(by);
        var rng = new Rng(seed ^ 0x57EA_7E12_D1A1_5UL);
        foreach (string d in rules.Dials.Keys)
            dials[d] = Math.Clamp(dials[d] * MathF.Exp(rng.Range(-rules.Spread, rules.Spread)), rules.Min, rules.Max);
        return new WorldWeather
        {
            Storminess = dials.GetValueOrDefault("storminess", 1f), Tornadoes = dials.GetValueOrDefault("tornadoes", 1f),
            Wetness = dials.GetValueOrDefault("wetness", 1f), Dryness = dials.GetValueOrDefault("dryness", 1f),
            Fog = dials.GetValueOrDefault("fog", 1f), Cold = dials.GetValueOrDefault("cold", 1f),
        };
    }

    /// <summary>A weather's weight on this world: its type's, scaled by every dial that names it.</summary>
    public float Weigh(WorldRules rules, WeatherKind kind, float weight)
    {
        if (rules == null) return weight;
        foreach (var (d, kinds) in rules.Dials)
            if (kinds.Contains(kind)) weight *= Dial(d);
        return weight;
    }

    public override string ToString() =>
        $"storm {Storminess:F2} tornado {Tornadoes:F2} wet {Wetness:F2} dry {Dryness:F2} fog {Fog:F2} cold {Cold:F2}";
}

public sealed class WorldRules
{
    /// <summary>How far each dial may lean at random from what its layers say, as a natural log (0.4: × 0.67 to × 1.5).</summary>
    public float Spread { get; set; } = 0.4f;
    public float Min { get; set; } = 0.05f;
    public float Max { get; set; } = 4f;

    /// <summary>Each dial and the weathers whose weights it scales.</summary>
    public Dictionary<string, List<WeatherKind>> Dials { get; set; } = new();

    /// <summary>A world fact ("temp=hot") and what it does to the dials.</summary>
    public Dictionary<string, Dictionary<string, float>> Facts { get; set; } = new();

    /// <summary>A world layer by id ("thunderheads") and what it does to the dials.</summary>
    public Dictionary<string, Dictionary<string, float>> Layers { get; set; } = new();
}

/// <summary>Reads a region's land against the rules: what may form there, and how its air differs.</summary>
public static class Regions
{
    /// <summary>High ground, 0 below the mountain height to 1 well above it.</summary>
    public static float High(RegionRules r, in Ground g) => Front.Smooth(r.MountainHeight * 0.75f, r.MountainHeight * 1.25f, g.Height);

    /// <summary>How well a tornado can stand on this one point: 0 never (mountain, steep ground, water), up to 1.</summary>
    public static float TornadoPoint(RegionRules r, in Ground g)
    {
        if (g.Water) return 0f;
        TornadoGround t = r.Tornado;
        if (g.Height > r.MountainHeight || g.Slope > r.SteepSlope) return 0f;
        float flat = 1f - Front.Smooth(t.MaxSlope * 0.6f, t.MaxSlope, g.Slope);
        float open = g.Forest >= t.DenseForest ? t.ForestOdds : 1f - (1f - t.ForestOdds) * Front.Smooth(t.DenseForest * 0.6f, t.DenseForest, g.Forest);
        float air = Front.Smooth(t.MinWarmth * 0.7f, t.MinWarmth, g.Warmth) * Front.Smooth(t.MinHumidity * 0.7f, t.MinHumidity, g.Humidity);
        return flat * open * air;
    }

    /// <summary>
    /// How well a tornado can form over a region, 0..1: the point itself and a ring round it, so it
    /// takes broad open flat or rolling land (no mountain or steep ground anywhere near, little rise
    /// across it) in warm humid air.
    /// </summary>
    public static float TornadoGround(RegionRules r, Func<float, float, Ground> land, Vector3 p)
    {
        if (land == null) return 1f;
        Ground g = land(p.X, p.Z);
        float here = TornadoPoint(r, g);
        if (here <= 0f) return 0f;
        float lo = g.Height, hi = g.Height, worst = here;
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.Tau / 8f;
            Ground o = land(p.X + MathF.Cos(a) * r.BroadRadius, p.Z + MathF.Sin(a) * r.BroadRadius);
            if (o.Height > r.MountainHeight || o.Slope > r.SteepSlope) return 0f;
            if (!o.Water) worst = MathF.Min(worst, MathF.Max(TornadoPoint(r, o), here * 0.5f));
            lo = MathF.Min(lo, o.Height);
            hi = MathF.Max(hi, o.Height);
        }
        float relief = 1f - Front.Smooth(r.Tornado.MaxRelief * 0.6f, r.Tornado.MaxRelief, hi - lo);
        return MathF.Min(here, worst) * relief;
    }

    /// <summary>Dry loose ground the wind can lift, 0..1.</summary>
    public static float DustGround(RegionRules r, in Ground g) =>
        g.Water ? 0f : Front.Smooth(r.Dust.MinLoose * 0.6f, r.Dust.MinLoose, g.Loose) * (1f - Front.Smooth(r.Dust.MaxHumidity, r.Dust.MaxHumidity * 1.4f, g.Humidity));

    /// <summary>Cold or high ground, 0..1: where a blizzard can blow.</summary>
    public static float ColdGround(RegionRules r, in Ground g) =>
        MathF.Max(1f - Front.Smooth(r.Blizzard.MaxWarmth, r.Blizzard.MaxWarmth * 1.5f, g.Warmth), High(r, g));

    /// <summary>Damp low ground, 0..1: where fog pools. <paramref name="around"/> is the mean height of the land round it.</summary>
    public static float FogGround(RegionRules r, in Ground g, float around)
    {
        FogGround f = r.Fog;
        float damp = Front.Smooth(f.MinHumidity * 0.7f, f.MinHumidity, g.Humidity);
        float low = MathF.Max(Front.Smooth(0f, f.ValleyDepth, around - g.Height), 1f - Front.Smooth(f.LowHeight, f.LowHeight * 2.5f, g.Height));
        return damp * low;
    }

    /// <summary>How much of a weather's own conditions a region gets, 0 (all its fallback's) to 1.</summary>
    public static float Suits(RegionRules r, WeatherKind kind, in Ground g, float around) => kind switch
    {
        WeatherKind.DustStorm => DustGround(r, g),
        WeatherKind.Blizzard => ColdGround(r, g),
        WeatherKind.Fog => FogGround(r, g, around),
        WeatherKind.Tornado => MathF.Min(1f, TornadoPoint(r, g) * 1.5f),
        _ => 1f,
    };
}
