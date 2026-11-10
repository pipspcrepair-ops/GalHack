using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;

namespace AssetViewer.Providers.Nature;

/// <summary>
/// Every weather and hazard the weather model has, read off its data file (data/weather.json): each state,
/// each shape of whirl, strikes and fronts, and the ground after them, on any of its world climates.
/// </summary>
public sealed partial class WeatherProvider : IAssetProvider
{
    public string Category => "Weather";
    public IEnumerable<(string Prototype, string Folder)> Mounts => new[] { ("planet-surface", "shaders") };

    /// <summary>The planet a climate of the weather data stands on: its type, as the game maps them.</summary>
    public static PlanetSpec WorldFor(string climate, ulong seed)
    {
        (Archetype a, int v, Modifier add) = climate switch
        {
            "temperate" => (Archetype.Temperate, 0, Modifier.None),
            "swamp" => (Archetype.Temperate, 1, Modifier.None),
            "ocean" => (Archetype.Temperate, 0, Modifier.Archipelago),
            "ice" => (Archetype.Ice, 0, Modifier.None),
            "arid" => (Archetype.Arid, 0, Modifier.None),
            "storm" => (Archetype.Alien, 0, Modifier.PerpetualStorm),
            "lava" => (Archetype.Lava, 0, Modifier.None),
            _ => (Archetype.Temperate, 0, Modifier.None),
        };
        PlanetSpec spec = NatureWorlds.SpecOf(a, v, seed);
        return add == Modifier.None ? spec : spec with { Modifiers = spec.Modifiers | add };
    }

    static AssetOption[] CommonOptions(WeatherData data, string defaultClimate)
    {
        string[] climates = data.Archetypes.Select(a => a.Id).ToArray();
        return new[]
        {
            new AssetOption("Climate", climates) { Default = Math.Max(0, Array.IndexOf(climates, defaultClimate)) },
            new AssetOption("Time", SurfaceView.Times) { Default = 2 },
        };
    }

    static AssetOption[] WithView(AssetOption[] common, bool wide) =>
        common.Append(new AssetOption("View", "Ground", "Wide") { Default = wide ? 1 : 0 }).ToArray();

    static AssetInstance Make(BuildRequest r, WeatherKind kind, string sceneKind, TornadoShape? shape = null)
    {
        string climate = r.Get("Climate", "temperate");
        bool ground = r.Get("View", sceneKind == WeatherScene.State ? "Ground" : "Wide") == "Ground";
        ulong seed = r.Seed == 0 ? 1UL : r.Seed;
        PlanetSpec spec = WorldFor(climate, seed);
        var scene = new WeatherScene(sceneKind, kind, climate, shape, ground);
        return new WeatherView(NatureWorlds.Get(spec), scene, seed, SurfaceView.TimeValue(r.Get("Time", "Noon")));
    }

    public IEnumerable<AssetEntry> Entries()
    {
        WeatherData data = WeatherView.Data;
        // Which climate most often brings a weather: its home world in the picker.
        string HomeOf(WeatherKind k) => data.Archetypes.OrderByDescending(a => a.Weights.GetValueOrDefault(k)).First().Id;
        foreach (WeatherState state in data.States)
        {
            WeatherKind kind = state.Kind;
            bool wide = state.Cellular > 0f || state.Lightning > 0f;
            yield return new AssetEntry
            {
                Id = "state-" + kind, Group = "Weather/" + (state.Severe ? "Severe" : "Everyday"), Name = state.Name, Randomisable = true,
                Options = WithView(CommonOptions(data, HomeOf(kind)), wide),
                Build = r => Make(r, kind, WeatherScene.State),
            };
        }
        yield return new AssetEntry
        {
            Id = "strike-lightning", Group = "Hazards", Name = "Lightning strike", Randomisable = true,
            Options = CommonOptions(data, "storm"), Build = r => Make(r, WeatherKind.Thunderstorm, WeatherScene.Strike),
        };
        foreach (TornadoShape shape in data.Tornado.Shapes.Select(s => s.Shape))
        {
            TornadoShape sh = shape;
            yield return new AssetEntry
            {
                Id = "funnel-" + shape, Group = "Hazards/Funnels", Name = $"Tornado, {shape.ToString().ToLowerInvariant()}", Randomisable = true,
                Options = CommonOptions(data, "temperate"), Build = r => Make(r, WeatherKind.Tornado, WeatherScene.Funnel, sh),
            };
        }
        yield return new AssetEntry
        {
            Id = "waterspouts", Group = "Hazards/Funnels", Name = "Waterspouts", Randomisable = true,
            Options = CommonOptions(data, "ocean"), Build = r => Make(r, WeatherKind.Rain, WeatherScene.Spouts),
        };
        yield return new AssetEntry
        {
            Id = "dust-devil", Group = "Hazards/Funnels", Name = "Dust devil", Randomisable = true,
            Options = CommonOptions(data, "arid"), Build = r => Make(r, WeatherKind.Clear, WeatherScene.Devil),
        };
        foreach ((string id, string name, WeatherKind kind, string climate) in new[]
        {
            ("front-storm", "Storm front", WeatherKind.Thunderstorm, "storm"),
            ("front-dust", "Dust wall", WeatherKind.DustStorm, "arid"),
        })
        {
            WeatherKind k = kind;
            yield return new AssetEntry
            {
                Id = id, Group = "Hazards/Fronts", Name = name, Randomisable = true,
                Options = CommonOptions(data, climate), Build = r => Make(r, k, WeatherScene.Front),
            };
        }
        foreach ((string id, string name, WeatherKind kind, string climate) in new[]
        {
            ("cover-rain", "After rain", WeatherKind.Rain, "temperate"),
            ("cover-snow", "Snow on the ground", WeatherKind.Snow, "ice"),
            ("cover-dust", "Dust on the ground", WeatherKind.DustStorm, "arid"),
        })
        {
            WeatherKind k = kind;
            yield return new AssetEntry
            {
                Id = id, Group = "Ground after weather", Name = name, Randomisable = true,
                Options = WithView(CommonOptions(data, climate), false), Build = r => Make(r, k, WeatherScene.Cover),
            };
        }
    }
}
