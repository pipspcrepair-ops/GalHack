using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GalHack.Items.Deposits;

/// <summary>
/// The four ore-body shapes (ORE-NOTES.md): pockets joined by thin passages, winding veins that
/// swell and pinch, layered beds that follow the ground, and rich deep clusters with a stringer
/// rising towards the surface.
/// </summary>
public enum DepositShape { Pocket, Vein, Bed, Deep }

/// <summary>Richness of an ore cell. A body starts poor or medium; its hot spots build up to bountiful.</summary>
public enum Grade { Poor, Medium, Rich, Bountiful }

/// <summary>Volume and depth of one shape (deposits.json).</summary>
public sealed class ShapeRule
{
    public DepositShape Shape { get; init; }
    /// <summary>Cubic metres of ore at Amount 1.</summary>
    public double Volume { get; init; }
    public double TopDepthMin { get; init; }
    public double TopDepthMax { get; init; }
}

/// <summary>Which shapes each ore grows in, how big, how deep and how rich: shared/Items/data/deposits.json.</summary>
public sealed class DepositRules
{
    public const string File = "deposits.json";

    readonly Dictionary<DepositShape, ShapeRule> _shapes = new();
    readonly Dictionary<string, List<(DepositShape Shape, double Weight)>> _ores = new(StringComparer.Ordinal);
    readonly double[] _yield = { 0.6, 1.0, 1.5, 2.2 };
    readonly double[] _baseChance = { 0.45, 0.55, 0, 0 };

    /// <summary>No ore lies deeper than this below the surface above it.</summary>
    public double MaxDepth { get; private set; } = 12;
    public IEnumerable<string> Ores => _ores.Keys;

    public ShapeRule Shape(DepositShape s) => _shapes[s];
    public double Yield(Grade g) => _yield[(int)g];

    public IReadOnlyList<(DepositShape Shape, double Weight)> ShapesFor(string ore) =>
        _ores.TryGetValue(ore, out var list) ? list : new List<(DepositShape, double)> { (DepositShape.Pocket, 1) };

    /// <summary>Picks a shape for an ore from a 0..1 roll, by the weights in the data.</summary>
    public DepositShape PickShape(string ore, double roll)
    {
        var list = ShapesFor(ore);
        double total = list.Sum(s => s.Weight), at = roll * total;
        foreach (var (shape, w) in list)
        {
            if (at < w) return shape;
            at -= w;
        }
        return list[^1].Shape;
    }

    /// <summary>A body's base grade from a 0..1 roll.</summary>
    public Grade PickBaseGrade(double roll)
    {
        double at = roll;
        for (int g = 0; g < _baseChance.Length; g++)
        {
            if (at < _baseChance[g]) return (Grade)g;
            at -= _baseChance[g];
        }
        return Grade.Medium;
    }

    public static DepositRules LoadDirectory(string dir) => Load(System.IO.File.ReadAllText(Path.Combine(dir, File)));

    public static DepositRules Load(string json)
    {
        JsonElement root = DataJson.Parse(json, "galhack.deposits");
        var r = new DepositRules { MaxDepth = DataJson.Num(root, "maxDepth", 12) };
        foreach (JsonProperty p in root.GetProperty("shapes").EnumerateObject())
        {
            DepositShape shape = ParseShape(p.Name);
            double[] top = p.Value.GetProperty("topDepth").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            r._shapes[shape] = new ShapeRule { Shape = shape, Volume = DataJson.Num(p.Value, "volume"), TopDepthMin = top[0], TopDepthMax = top[1] };
        }
        foreach (DepositShape s in Enum.GetValues<DepositShape>())
            if (!r._shapes.ContainsKey(s)) throw new InvalidDataException($"deposits.json: no rule for shape {s}");
        foreach (JsonElement g in root.GetProperty("grades").EnumerateArray())
        {
            var grade = Enum.Parse<Grade>(DataJson.Str(g, "grade"), ignoreCase: true);
            r._yield[(int)grade] = DataJson.Num(g, "yield");
            r._baseChance[(int)grade] = DataJson.Num(g, "baseChance", 0);
        }
        foreach (JsonProperty ore in root.GetProperty("ores").EnumerateObject())
            r._ores[ore.Name] = ore.Value.EnumerateObject().Select(s => (ParseShape(s.Name), s.Value.GetDouble())).ToList();
        return r;
    }

    static DepositShape ParseShape(string s) => s switch
    {
        "pocket" => DepositShape.Pocket,
        "vein" => DepositShape.Vein,
        "bed" => DepositShape.Bed,
        "deep" => DepositShape.Deep,
        _ => throw new InvalidDataException($"deposits.json: unknown shape '{s}'"),
    };

    /// <summary>The ore id the planet generator's deposit kind (its enum name, like "Iron") stands for, from items.json.</summary>
    public static string OreForKind(ItemCatalogue items, string depositKind) =>
        items.Items.FirstOrDefault(i => i.DepositKind == depositKind)?.Id;
}
