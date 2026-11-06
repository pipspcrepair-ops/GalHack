using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Slice.Game;

namespace Slice.Orbit;

/// <summary>The slice's orbit numbers (data/orbit.json): the globe, the crossings, the wrecks and space itself.</summary>
public sealed class OrbitRules
{
    public GlobeRules Globe { get; set; } = new();
    public CrossingRules Crossing { get; set; } = new();
    public WreckRules Wrecks { get; set; } = new();
    public SpaceRules Space { get; set; } = new();

    public sealed class GlobeRules
    {
        public double RadiusScale { get; set; } = 1.0;
        public double Atmosphere { get; set; } = 0.075;
        public int Segments { get; set; } = 192;
        public int Rings { get; set; } = 96;
        public int TextureWidth { get; set; } = 4096;
        public int CloudWidth { get; set; } = 1024;
        public double CloudHeight { get; set; } = 0.012;
        public double CloudOpacity { get; set; } = 0.55;
    }

    public sealed class CrossingRules
    {
        public float PreloadAt { get; set; } = 0.55f;
        public float ArriveAbove { get; set; } = 60f;
        public float ArriveBelow { get; set; } = 150f;
        public float TunnelFrom { get; set; } = 220f;
        public float TunnelHold { get; set; } = 0.45f;
        public float TunnelOut { get; set; } = 0.7f;
    }

    public sealed class WreckRules
    {
        public float[] Near { get; set; } = { 260f, 1500f };
        public float[] Orbit { get; set; } = { 1500f, 4200f };
        public float MaxLatitude { get; set; } = 55f;
        public double SalvageReturn { get; set; } = 0.33;
        public float SalvageSecondsPerTonne { get; set; } = 0.6f;
        public float SalvageMinSeconds { get; set; } = 1.2f;
        public float SalvageReach { get; set; } = 4f;
        /// <summary>Per salvage grade: (item, least, most) rolls for each container block.</summary>
        public Dictionary<string, List<JsonElement[]>> Loot { get; set; } = new();
    }

    public sealed class SpaceRules
    {
        public float CameraFar { get; set; } = 60000f;
        public float MoonDistance { get; set; } = 40000f;
        public float[] Ambient { get; set; } = { 0.035f, 0.038f, 0.055f };
        public float SunEnergy { get; set; } = 1.6f;
        public double VacuumC { get; set; } = 4;
        public double Radiation { get; set; } = 0.4;
    }

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static OrbitRules Parse(string json) =>
        JsonSerializer.Deserialize<OrbitRules>(json, Json) ?? throw new InvalidDataException("empty orbit.json");

    static OrbitRules _shared;
    /// <summary>data/orbit.json, read once.</summary>
    public static OrbitRules Shared => _shared ??= Parse(File.ReadAllText(GameData.Own("data/orbit.json")));

    /// <summary>The loot rolls for a salvage grade (scrap if the grade has none).</summary>
    public IEnumerable<(string item, int least, int most)> LootFor(string grade)
    {
        if (!Wrecks.Loot.TryGetValue(grade ?? "", out List<JsonElement[]> rows) && !Wrecks.Loot.TryGetValue("scrap", out rows)) yield break;
        foreach (JsonElement[] row in rows)
            yield return (row[0].GetString(), row[1].GetInt32(), row[2].GetInt32());
    }
}
