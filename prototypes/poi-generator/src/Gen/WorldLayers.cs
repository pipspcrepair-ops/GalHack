#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>Which of the four layers a block of data/world-layers.txt is.</summary>
public enum LayerKind : byte { Type, Theme, Feature, Presence }

/// <summary>One record of data/world-layers.txt: a world type, a theme, a feature or a presence. A record, never code.</summary>
public sealed class WorldLayer
{
    public LayerKind Kind;
    public string Id = "", Name = "";
    public float Weight = 1f, Chance = 1f, Break = -1f;
    public int Streets;
    public bool Start;
    public List<string> Worlds = new(), Pre = new(), Post = new(), Ports = new(), Needs = new(), Tags = new();
    public readonly Dictionary<string, float> Regions = new(), Hub = new(), Industry = new(), Plant = new(), Farm = new(), Poi = new(), Occupancy = new();
    public readonly Dictionary<string, string> Style = new();
    public string? Depot;
    public float[]? States;
}

public static class LayerParser
{
    public static List<WorldLayer> Parse(string text, string source)
    {
        var layers = new List<WorldLayer>();
        WorldLayer? l = null;
        int n = 0;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            n++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] p = line.Replace('|', ' ').Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            FormatException Err(string m) => new($"{source}:{n}: {m}");
            WorldLayer L() => l ?? throw Err("needs a layer first");
            void Table(Dictionary<string, float> t)
            {
                for (int i = 1; i + 1 < p.Length; i += 2) t[p[i]] = float.Parse(p[i + 1], CultureInfo.InvariantCulture);
            }
            switch (p[0])
            {
                case "type" or "theme" or "feature" or "presence":
                    l = new WorldLayer { Id = p[1], Kind = p[0] switch { "type" => LayerKind.Type, "theme" => LayerKind.Theme, "feature" => LayerKind.Feature, _ => LayerKind.Presence } };
                    layers.Add(l);
                    break;
                case "name": L().Name = line["name".Length..].Trim(); break;
                case "weight": L().Weight = float.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "chance": L().Chance = float.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "break": L().Break = float.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "streets": L().Streets = int.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "start": L().Start = p[1] == "yes"; break;
                case "worlds": L().Worlds = p.Skip(1).ToList(); break;
                case "pre": L().Pre = p.Skip(1).ToList(); break;
                case "post": L().Post = p.Skip(1).ToList(); break;
                case "ports": L().Ports = p.Skip(1).ToList(); break;
                case "needs": L().Needs = p.Skip(1).ToList(); break;
                case "tags": L().Tags = p.Skip(1).ToList(); break;
                case "depot": L().Depot = p[1]; break;
                case "region": Table(L().Regions); break;
                case "hub": Table(L().Hub); break;
                case "industry": Table(L().Industry); break;
                case "plant": Table(L().Plant); break;
                case "farm": Table(L().Farm); break;
                case "poi": Table(L().Poi); break;
                case "occ": Table(L().Occupancy); break;
                case "style":
                    for (int i = 1; i + 1 < p.Length; i += 2) L().Style[p[i]] = p[i + 1];
                    break;
                case "state":
                    L().States = new float[4];
                    for (int i = 1; i + 1 < p.Length; i += 2)
                        L().States![(int)Enum.Parse<PoiState>(p[i], true)] = float.Parse(p[i + 1], CultureInfo.InvariantCulture);
                    break;
                default: throw Err($"unknown keyword '{p[0]}'");
            }
        }
        return layers;
    }
}

/// <summary>
/// What one world rolled: a type, a theme, its features and presences, and the tables they make
/// together. Deterministic from the world's seed. Nothing in the generators names a theme: they read
/// these tables, so a world is whatever its layers add up to.
/// </summary>
public sealed class WorldRoll
{
    public WorldLayer? Type;
    public WorldLayer Theme = null!;
    public readonly List<WorldLayer> Features = new(), Presences = new();

    public readonly Dictionary<string, float> Regions = new(), Hub = new(), Industry = new(), Plant = new(), Farm = new(), Poi = new(), Occupancy = new();
    public readonly List<string> Needs = new(), Tags = new();
    public string? Depot;
    public float[]? States;
    /// <summary>The share of a world's places that break from the theme.</summary>
    public float Break = 0.1f;
    public int Streets = 2;
    public string Road = "quiet", Rail = "none", Power = "intact";

    public IEnumerable<WorldLayer> Layers
    {
        get
        {
            if (Type != null) yield return Type;
            yield return Theme;
            foreach (WorldLayer f in Features) yield return f;
            foreach (WorldLayer p in Presences) yield return p;
        }
    }

    public int RegionCount(string kind) => (int)MathF.Round(Regions.GetValueOrDefault(kind));

    public override string ToString() => string.Join(" + ", Layers.Select(l => l.Name));

    static string Band(TechLevel t) => t switch { TechLevel.Low => "L", TechLevel.High => "H", _ => "M" };

    static string PortCode(Starport s) => s == Starport.None ? "0" : s.ToString();

    static bool Fits(WorldLayer l, string archetype, WorldProfile p, bool start) =>
        (l.Worlds.Count == 0 || l.Worlds.Contains("any") || l.Worlds.Contains(archetype))
        && (!start || l.Start || l.Kind != LayerKind.Theme)
        && (l.Pre.Count == 0 || l.Pre.Contains(Band(p.Before)))
        && (l.Post.Count == 0 || l.Post.Contains(Band(p.Now)))
        && (l.Ports.Count == 0 || l.Ports.Contains(PortCode(p.Starport)));

    /// <summary>
    /// Rolls a world's layers. A theme that fits its tech bands and starport comes first; with only a few
    /// themes written so far a world none of them fits takes the nearest by world and start rule, so every
    /// inhabited world has a theme. Null where the library has no themes.
    /// </summary>
    public static WorldRoll? Roll(PoiLibrary lib, ulong seed, string archetype, WorldProfile profile, bool start)
    {
        var rng = new Rng(Hash.Combine(seed, 0x7EEEA5UL));
        string a = archetype.ToLowerInvariant();
        var themes = lib.Layers.Where(l => l.Kind == LayerKind.Theme).ToList();
        if (themes.Count == 0) return null;
        var fit = themes.Where(t => Fits(t, a, profile, start)).ToList();
        if (fit.Count == 0) fit = themes.Where(t => (t.Worlds.Contains("any") || t.Worlds.Contains(a)) && (!start || t.Start)).ToList();
        if (fit.Count == 0) fit = themes;
        var roll = new WorldRoll { Theme = fit[Math.Max(0, rng.PickWeighted(fit.Select(t => t.Weight * (profile.Exodus && t.Regions.ContainsKey("port_hub") ? 4f : 1f)).ToList()))] };
        var types = lib.Layers.Where(l => l.Kind == LayerKind.Type && Fits(l, a, profile, false)).ToList();
        if (types.Count > 0) roll.Type = types[Math.Max(0, rng.PickWeighted(types.Select(t => t.Weight).ToList()))];
        foreach (WorldLayer l in lib.Layers.Where(l => l.Kind == LayerKind.Feature && Fits(l, a, profile, false)))
            if (rng.Chance(l.Chance)) roll.Features.Add(l);
        var presences = lib.Layers.Where(l => l.Kind == LayerKind.Presence && Fits(l, a, profile, false)).ToList();
        rng.Shuffle(presences);
        foreach (WorldLayer l in presences)
            if (roll.Presences.Count == 0 && rng.Chance(l.Chance)) roll.Presences.Add(l);
        roll.Combine(lib);
        return roll;
    }

    /// <summary>Adds the layers' tables together. Families the library doesn't have (yet) are left out.</summary>
    void Combine(PoiLibrary lib)
    {
        static void Add(Dictionary<string, float> to, Dictionary<string, float> from)
        {
            foreach (var (k, v) in from) to[k] = to.GetValueOrDefault(k) + v;
        }
        foreach (WorldLayer l in Layers)
        {
            Add(Regions, l.Regions);
            Add(Hub, l.Hub);
            Add(Industry, l.Industry);
            Add(Plant, l.Plant);
            Add(Farm, l.Farm);
            Add(Poi, l.Poi);
            Add(Occupancy, l.Occupancy);
            Needs.AddRange(l.Needs.Where(n => !Needs.Contains(n)));
            Tags.AddRange(l.Tags);
            Depot = l.Depot ?? Depot;
            States = l.States ?? States;
            if (l.Break >= 0f) Break = l.Break;
            if (l.Streets > 0) Streets = l.Streets;
            if (l.Style.TryGetValue("road", out string? road)) Road = road;
            if (l.Style.TryGetValue("rail", out string? rail)) Rail = rail;
            if (l.Style.TryGetValue("power", out string? power)) Power = power;
        }
        bool Known(string id) => lib.Families.Any(f => f.Id == id);
        foreach (var t in new[] { Hub, Industry, Plant, Farm, Poi })
            foreach (string id in t.Keys.Where(k => !Known(k)).ToList()) t.Remove(id);
        Needs.RemoveAll(n => !Known(n));
        if (Depot != null && !Known(Depot)) Depot = null;
    }

    public string? Pick(Dictionary<string, float> table, Rng rng)
    {
        if (table.Count == 0) return null;
        var keys = table.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        int i = rng.PickWeighted(keys.Select(k => table[k]).ToList());
        return i < 0 ? null : keys[i];
    }
}
