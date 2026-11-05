using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CapitalBase.Core;

/// <summary>
/// Every number this prototype adds (data/capital-base.json). Physics, handling, building, weather
/// and terrain keep their own numbers in their own files.
/// </summary>
public sealed class CapitalRules
{
    public sealed class PlanetData
    {
        public ulong Seed { get; set; } = 47;
        public string Archetype { get; set; } = "Arid";
        public int Variant { get; set; }
        public int WidthKm { get; set; } = 8;
        public int PatchColumns { get; set; } = 14;
    }

    public sealed class LiftoffData
    {
        public float Margin { get; set; } = 0.75f;
        public float MinClimb { get; set; } = 3f;
        public float ClearBand { get; set; } = 3f;
        public float MaxClimb { get; set; } = 40f;
        public float ClimbSpeed { get; set; } = 3f;
        public int MaxCells { get; set; } = 6000;
        public float DebrisSeconds { get; set; } = 2.5f;
    }

    public sealed class FootingData
    {
        public int Radius { get; set; } = 1;
        public int Depth { get; set; } = 2;
    }

    public sealed class BuildData
    {
        public float Margin { get; set; } = 0.1f;
    }

    public sealed class TransmitData
    {
        public float Radius { get; set; } = 1000f;
        public float Interval { get; set; } = 0.5f;
    }

    public sealed class DrillData
    {
        public float Radius { get; set; } = 2f;
        public float Reach { get; set; } = 6f;
    }

    public sealed class ItemData
    {
        public double Su { get; set; } = 1;
    }

    public string Schema { get; set; }
    public int Version { get; set; }
    public PlanetData Planet { get; set; } = new();
    public LiftoffData Liftoff { get; set; } = new();
    public FootingData Footings { get; set; } = new();
    public BuildData Build { get; set; } = new();
    public TransmitData AutoTransmit { get; set; } = new();
    public DrillData Drill { get; set; } = new();
    public Dictionary<string, ItemData> Items { get; set; } = new();

    /// <summary>Terrain cell material (terrain-digging's names) to the item one dug cell gives.</summary>
    public Dictionary<string, string> Yield { get; set; } = new();

    [JsonIgnore] public ItemCatalog Catalog { get; private set; }

    public static CapitalRules FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        CapitalRules rules = JsonSerializer.Deserialize<CapitalRules>(json, options) ?? throw new FormatException("empty capital-base rules");
        if (rules.Schema != "galhack.capital-base" || rules.Version != 1)
            throw new FormatException($"expected galhack.capital-base version 1, got {rules.Schema} {rules.Version}");
        var su = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, item) in rules.Items) su[id] = item.Su;
        rules.Catalog = new ItemCatalog(su);
        foreach (string item in rules.Yield.Values)
            if (!su.ContainsKey(item)) throw new FormatException($"yield names unknown item '{item}'");
        return rules;
    }

    /// <summary>The item a dug cell of this material gives, or null (air, bedrock).</summary>
    public string ItemFor(string material) => Yield.TryGetValue(material, out string item) ? item : null;
}
