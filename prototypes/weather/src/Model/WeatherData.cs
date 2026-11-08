using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weather.Model;

/// <summary>Every kind of weather. The data file has one state for each.</summary>
public enum WeatherKind
{
    Clear, Clouds, Rain, Monsoon, Fog, Snow, Blizzard, DustStorm, Thunderstorm, FireStorm, Hurricane, Tornado,
}

/// <summary>
/// What one kind of weather settles to once it has fully arrived. Loaded from
/// data/weather.json; the model blends between these, it never hard-codes a number.
/// </summary>
public sealed class WeatherState
{
    public WeatherKind Kind { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Severe weather always gets a visor warning before it arrives.</summary>
    public bool Severe { get; set; }
    public string Warning { get; set; } = "";

    public float CloudCover { get; set; }
    public float CloudDarkness { get; set; }
    public float Rain { get; set; }
    public float Snow { get; set; }
    public float Dust { get; set; }
    public float Embers { get; set; }
    public float GroundFog { get; set; }

    /// <summary>How far you can see, in metres.</summary>
    public float Visibility { get; set; } = 10000f;
    public float WindSpeed { get; set; }
    public float Gustiness { get; set; }

    /// <summary>Offset from the planet's base temperature, °C.</summary>
    public float Temperature { get; set; }

    /// <summary>Solar panel output, 0..1 of a clear day.</summary>
    public float Solar { get; set; } = 1f;

    /// <summary>A hint for the creature AI: 1 is normal, lower hides, higher hunts.</summary>
    public float CreatureActivity { get; set; } = 1f;
    public string CreatureHint { get; set; } = "";

    /// <summary>Hazard rates, per minute at full strength.</summary>
    public float Lightning { get; set; }
    public float Tornadoes { get; set; }
    public float Debris { get; set; }

    /// <summary>Walking speed factor (deep snow, sand).</summary>
    public float MoveSpeed { get; set; } = 1f;

    /// <summary>Extra suit drain from cold or heat, % per second.</summary>
    public float Exposure { get; set; }
    public string ExposureLabel { get; set; } = "";

    /// <summary>How much of its rain falls only under storm cells, 0..1.</summary>
    public float Cellular { get; set; }

    public float DurationMin { get; set; } = 120f;
    public float DurationMax { get; set; } = 200f;

    /// <summary>How long before it arrives the visor forecasts it.</summary>
    public float ForecastLead { get; set; } = 60f;

    /// <summary>How long before it arrives a severe weather warning goes up.</summary>
    public float WarningLead { get; set; }
}

/// <summary>A kind of planet and how often each weather visits it.</summary>
public sealed class Archetype
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float BaseTemperature { get; set; }
    public Dictionary<WeatherKind, float> Weights { get; set; } = new();
}

public sealed class TransitionRules
{
    /// <summary>How long a forecast change takes to roll in, at least.</summary>
    public float NaturalSeconds { get; set; } = 40f;
    public float MinSeconds { get; set; } = 6f;

    /// <summary>The fastest each parameter may change, per second. Transitions stretch to respect these.</summary>
    public Dictionary<string, float> MaxRates { get; set; } = new();
}

public sealed class GroundRules
{
    public float WetRate { get; set; }
    public float DryRate { get; set; }
    public float SnowRate { get; set; }
    public float MeltRate { get; set; }
    public float DustRate { get; set; }
    public float DustClearRate { get; set; }
}

/// <summary>Lightning and burning debris share a shape: a visible build-up, then a hit.</summary>
public sealed class StrikeRules
{
    public float BuildUpSeconds { get; set; }
    public float FallSeconds { get; set; }
    public float Radius { get; set; }
    public float StrikeOffset { get; set; }
    public float Damage { get; set; }
    public float DamageRadius { get; set; }
    public int MaxConcurrent { get; set; } = 1;

    public float WarningSeconds => BuildUpSeconds > 0f ? BuildUpSeconds : FallSeconds;
}

/// <summary>A tornado's path and lift profile.</summary>
public sealed class TornadoRules
{
    public float FormSeconds { get; set; }
    public float Lifetime { get; set; }
    public float DissipateSeconds { get; set; }
    public float Speed { get; set; }
    public float TurnRate { get; set; }
    public float SpawnDistance { get; set; }
    public int MaxActive { get; set; }
    public float CoreRadius { get; set; }
    public float CaptureRadius { get; set; }
    public float InfluenceRadius { get; set; }
    public float OrbitRadius { get; set; }
    public float OrbitSpeed { get; set; }
    public float InflowSpeed { get; set; }
    public float LiftSpeed { get; set; }
    public float ReleaseHeight { get; set; }
    public float MaxCarrySeconds { get; set; }
    public float FlingSpeed { get; set; }
    public float ReleaseImmunity { get; set; }

    /// <summary>A fling and its landing never take a player below this share of max health.</summary>
    public float FlingHealthFloor { get; set; }
    public float LiftMassLimit { get; set; }
    public float Response { get; set; }
    public float VisualHeight { get; set; }

    /// <summary>A copy to scale for a weaker kind of whirl (a waterspout, a dust devil).</summary>
    public TornadoRules Copy() => (TornadoRules)MemberwiseClone();

    /// <summary>
    /// The same whirl scaled to a weaker kind: its pulls by <paramref name="pull"/>, its lift by
    /// <paramref name="lift"/>, its reach by <paramref name="reach"/>, its life as given, seconds.
    /// </summary>
    public TornadoRules Scaled(float pull, float lift, float reach, float form, float life, float dissipate)
    {
        TornadoRules r = Copy();
        r.OrbitSpeed *= pull; r.InflowSpeed *= pull; r.FlingSpeed *= pull;
        r.LiftSpeed *= lift; r.LiftMassLimit *= lift; r.ReleaseHeight *= lift;
        r.CoreRadius *= reach; r.CaptureRadius *= reach; r.InfluenceRadius *= reach; r.OrbitRadius *= reach;
        r.FormSeconds = form; r.Lifetime = life; r.DissipateSeconds = dissipate;
        return r;
    }
}

/// <summary>Everything in data/weather.json.</summary>
public sealed class WeatherData
{
    public TransitionRules Transitions { get; set; } = new();
    public GroundRules Ground { get; set; } = new();
    public StrikeRules Lightning { get; set; } = new();
    public StrikeRules Debris { get; set; } = new();
    public TornadoRules Tornado { get; set; } = new();
    public CellRules Cells { get; set; } = new();
    public List<WeatherState> States { get; set; } = new();
    public List<Archetype> Archetypes { get; set; } = new();

    /// <summary>Max change per second for each <see cref="Param"/>, from <see cref="TransitionRules.MaxRates"/>.</summary>
    [JsonIgnore] public float[] MaxRates { get; private set; } = Array.Empty<float>();

    readonly Dictionary<WeatherKind, WeatherState> _byKind = new();

    public WeatherState State(WeatherKind kind) => _byKind[kind];

    public Archetype Archetype(string id) =>
        Archetypes.Find(a => a.Id == id) ?? throw new ArgumentException($"no archetype '{id}'");

    public static WeatherData Parse(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var data = JsonSerializer.Deserialize<WeatherData>(json, options)
            ?? throw new FormatException("empty weather data");
        data.Index();
        return data;
    }

    /// <summary>Indexes and checks the data, so a typo fails loudly at load and not mid-storm.</summary>
    void Index()
    {
        foreach (var state in States) _byKind[state.Kind] = state;
        foreach (WeatherKind kind in Enum.GetValues<WeatherKind>())
            if (!_byKind.ContainsKey(kind)) throw new FormatException($"weather data has no state for {kind}");

        foreach (var state in States)
        {
            if (state.DurationMax < state.DurationMin) throw new FormatException($"{state.Kind}: durationMax < durationMin");
            if (state.Severe && (state.WarningLead <= 0f || state.WarningLead > state.ForecastLead))
                throw new FormatException($"{state.Kind}: severe weather needs 0 < warningLead <= forecastLead");
        }

        MaxRates = new float[Conditions.Count];
        for (int i = 0; i < Conditions.Count; i++)
        {
            string name = ((Param)i).ToString();
            if (!Transitions.MaxRates.TryGetValue(name, out float rate) || rate <= 0f)
                throw new FormatException($"transitions.maxRates needs a positive rate for {name}");
            MaxRates[i] = rate;
        }

        foreach (var archetype in Archetypes)
            if (archetype.Weights.Count < 2) throw new FormatException($"archetype {archetype.Id} needs at least two weathers");
    }

    /// <summary>The longest any forecast or warning can lead its weather by.</summary>
    public float MaxLead
    {
        get
        {
            float lead = 0f;
            foreach (var state in States) lead = MathF.Max(lead, MathF.Max(state.ForecastLead, state.WarningLead));
            return lead;
        }
    }
}
