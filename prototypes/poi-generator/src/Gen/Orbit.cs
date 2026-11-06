#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A kind of place in space (data and siting only for now; orbit play and space layouts come
/// later): what it is, how dangerous it is, what salvage it holds, and who holds it while it's
/// live. From data/orbit.txt.
/// </summary>
public sealed class SpaceSiteDef
{
    public string Id = "", Name = "", Pitch = "";
    /// <summary>wreck, field (a wreck field), hulk (a drifting hulk), broken (a broken station), station or outpost.</summary>
    public string Kind = "";
    public Rarity Rarity;
    /// <summary>0 calm .. 4 deadly.</summary>
    public int Danger;
    /// <summary>What it's worth stripping: scrap, salvage, military or rare.</summary>
    public string Salvage = "scrap";
    public Faction Faction = Faction.None;
    /// <summary>A faction holds it while it's live (its core, its defenders).</summary>
    public bool Held;
    /// <summary>Where it turns up: near (close to the start world), orbit, belt, moon, deep.</summary>
    public List<string> Zones = new();
    public List<string> Tags = new();
    /// <summary>Only over a world with a main starport of this class (data: port a): its highport and yards. None: anywhere.</summary>
    public Starport Port = Starport.None;
    public bool IsWreck => Kind is "wreck" or "field" or "hulk" or "broken";
}

/// <summary>How a system's space is filled: how many places, how many of them wrecks, how dangerous.</summary>
public sealed class OrbitTheme
{
    public string Id = "";
    public List<string> Match = new();
    public int SitesMin = 6, SitesMax = 12;
    /// <summary>The share of the system's places that are wrecks, and a floor on their number.</summary>
    public float WreckShare = 0.35f;
    public int WrecksMin;
    public int DangerMax = 4;
    /// <summary>No live faction anywhere in the system (a start system): faction places only as dead wrecks.</summary>
    public bool NoFactions;
    public List<(string Site, float Weight)> Set = new();
}

/// <summary>One place sited in a system's space.</summary>
public sealed class OrbitPoi
{
    public SpaceSiteDef Site = null!;
    public string Zone = "";
    /// <summary>Distance from the system's start world (or its star), in kilometres, and the bearing in degrees.</summary>
    public float RadiusKm, Bearing;
    public PoiState State;
    public ulong Seed;
    /// <summary>Held by a faction right now: its people are home and its core is live.</summary>
    public bool FactionHeld => Site.Held && State == PoiState.Active;
    public override string ToString() => $"{Site.Id} ({State}) in {Zone} at {RadiusKm:F0} km";
}

public static class OrbitParser
{
    public static void Parse(string text, string source, List<SpaceSiteDef> sites, List<OrbitTheme> themes)
    {
        SpaceSiteDef? s = null;
        OrbitTheme? t = null;
        int n = 0;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            n++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] p = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            FormatException Err(string m) => new($"{source}:{n}: {m}");
            float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
            int I(int i) => int.Parse(p[i], CultureInfo.InvariantCulture);
            switch (p[0])
            {
                case "space": s = new SpaceSiteDef { Id = p[1] }; t = null; sites.Add(s); break;
                case "orbit": t = new OrbitTheme { Id = p[1] }; s = null; themes.Add(t); break;
                default:
                    if (s != null)
                        switch (p[0])
                        {
                            case "name": s.Name = string.Join(' ', p.Skip(1)); break;
                            case "pitch": s.Pitch = string.Join(' ', p.Skip(1)); break;
                            case "kind": s.Kind = p[1]; break;
                            case "rarity": s.Rarity = Enum.Parse<Rarity>(p[1], true); break;
                            case "danger": s.Danger = I(1); break;
                            case "salvage": s.Salvage = p[1]; break;
                            case "faction": s.Faction = Enum.Parse<Faction>(p[1], true); break;
                            case "held": s.Held = true; break;
                            case "zones": s.Zones = p.Skip(1).ToList(); break;
                            case "port": s.Port = Enum.Parse<Starport>(p[1], true); break;
                            case "tags": s.Tags = p.Skip(1).ToList(); break;
                            default: throw Err($"unknown keyword '{p[0]}'");
                        }
                    else if (t != null)
                        switch (p[0])
                        {
                            case "match": t.Match = p.Skip(1).ToList(); break;
                            case "sites": t.SitesMin = I(1); t.SitesMax = I(p.Length - 1); break;
                            case "wrecks": t.WreckShare = F(1); t.WrecksMin = p.Length > 3 && p[2] == "min" ? I(3) : 0; break;
                            case "danger": t.DangerMax = I(1); break;
                            case "nofactions": t.NoFactions = true; break;
                            case "set":
                                for (int i = 1; i + 1 < p.Length; i += 2) t.Set.Add((p[i], F(i + 1)));
                                break;
                            default: throw Err($"unknown keyword '{p[0]}'");
                        }
                    else throw Err("needs 'space <id>' or 'orbit <id>' first");
                    break;
            }
        }
    }
}

/// <summary>
/// Sites a system's space: a count of places from its orbit theme, a share of them (and at least
/// a floor of) wrecks, the rest stations and outposts, each in a zone at a distance and bearing.
/// A start system (<c>start</c>) gets plenty of salvage close by (derelicts, wreck fields, broken
/// stations, drifting hulks; mostly salvage-grade and lower danger) and no live faction.
/// </summary>
public static class OrbitPlacement
{
    public static OrbitTheme Theme(PoiLibrary lib, bool start) =>
        lib.OrbitThemes.FirstOrDefault(t => t.Match.Contains(start ? "start" : "default"))
        ?? lib.OrbitThemes.FirstOrDefault()
        ?? new OrbitTheme { Id = "default", Set = lib.SpaceSites.Select(s => (s.Id, 1f)).ToList() };

    /// <param name="port">The class of the main starport below, if known: its orbital works hang over it.</param>
    public static List<OrbitPoi> Place(PoiLibrary lib, ulong systemSeed, bool start, Starport port = Starport.None)
    {
        var theme = Theme(lib, start);
        var rng = new Rng(Hash.Combine(systemSeed, 0x0B17UL));
        var set = theme.Set.Select(s => (Site: lib.SpaceSites.FirstOrDefault(d => d.Id == s.Site), s.Weight))
            .Where(s => s.Site != null && s.Site.Danger <= theme.DangerMax && s.Site.Port == Starport.None).Select(s => (Site: s.Site!, s.Weight)).ToList();
        int n = rng.Between(theme.SitesMin, theme.SitesMax);
        int wrecks = Math.Min(n, Math.Max(theme.WrecksMin, (int)MathF.Round(n * theme.WreckShare)));
        var result = new List<OrbitPoi>();
        float Weight((SpaceSiteDef Site, float Weight) s) => s.Weight / (1f + (int)s.Site.Rarity * (int)s.Site.Rarity * 0.6f);
        void Add(List<(SpaceSiteDef Site, float Weight)> from)
        {
            int k = rng.PickWeighted(from.Select(Weight).ToList());
            if (k < 0) return;
            var site = from[k].Site;
            PoiState state = site.IsWreck ? (rng.Chance(0.1f) ? PoiState.Infested : PoiState.Abandoned)
                : site.Held && !theme.NoFactions && rng.Chance(0.7f) ? PoiState.Active
                : rng.Chance(0.5f) ? PoiState.Abandoned : PoiState.Ruined;
            if (theme.NoFactions && site.Held && state == PoiState.Active) state = PoiState.Ruined;
            string zone = site.Zones.Count == 0 ? "orbit" : site.Zones[rng.Range(0, site.Zones.Count)];
            if (start && site.IsWreck && rng.Chance(0.5f) && site.Zones.Contains("near")) zone = "near"; // salvage close to home
            float radius = zone switch
            {
                "near" => rng.Float(200f, 2000f),
                "orbit" => rng.Float(1000f, 30000f),
                "moon" => rng.Float(150000f, 600000f),
                "belt" => rng.Float(2e6f, 9e6f),
                _ => rng.Float(1e7f, 5e7f),
            };
            result.Add(new OrbitPoi { Site = site, Zone = zone, RadiusKm = radius, Bearing = rng.Float(0f, 360f), State = state, Seed = rng.NextU64() });
        }
        var wreckSet = set.Where(s => s.Site.IsWreck).ToList();
        // In a start system nothing live is held: held places may still turn up, only as dead wrecks of themselves.
        var otherSet = set.Where(s => !s.Site.IsWreck).ToList();
        for (int i = 0; i < wrecks && wreckSet.Count > 0; i++) Add(wreckSet);
        for (int i = wrecks; i < n && otherSet.Count > 0; i++) Add(otherSet);
        // The great port's orbital works, dead or alive, over the world they served.
        foreach (SpaceSiteDef works in lib.SpaceSites.Where(s => s.Port != Starport.None && port >= s.Port)) Add(new List<(SpaceSiteDef Site, float Weight)> { (works, 1f) });
        return result;
    }
}
