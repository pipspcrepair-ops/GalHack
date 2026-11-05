#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A planet theme: the POIs that usually belong on a kind of world (its themed set, weighted),
/// and the share of outliers, places from outside the set that don't belong there and so feel
/// like the something-unexpected around the next corner. From data/themes.txt.
/// </summary>
public sealed class ThemeDef
{
    public string Id = "";
    public List<string> Match = new();         // "ice", "ice.glacier", "fallen", "default"
    public float OutlierMin = 0.04f, OutlierMax = 0.1f;
    public List<(string Family, float Weight)> Set = new();
    /// <summary>POIs per square kilometre.</summary>
    public float Density = 1.4f;
    /// <summary>The world's tech level: low, mid or high. Places with layouts for several tech levels pick theirs.</summary>
    public string Tech = "mid";
    /// <summary>
    /// A start world: no faction holds ground here. Faction places turn up only as ruins of the old
    /// days (no core, no defenders); wrecks, abandoned and infested places, the Bloom and the
    /// factionless gangs all stay.
    /// </summary>
    public bool Start;

    /// <summary>The same theme for a start world.</summary>
    public ThemeDef AsStart() => new() { Id = Id + "+start", Match = Match, OutlierMin = OutlierMin, OutlierMax = OutlierMax, Set = Set, Density = Density, Tech = Tech, Start = true };

    /// <summary>Whether a family belongs to a faction (its people, its core): on a start world it may only lie in ruins.</summary>
    public static bool FactionOwned(FamilyDef f) => f.Core == CoreRule.Owned || f.Faction != Faction.None;
}

public static class ThemeParser
{
    public static List<ThemeDef> Parse(string text, string source)
    {
        var themes = new List<ThemeDef>();
        ThemeDef? t = null;
        int n = 0;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            n++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] p = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            FormatException Err(string m) => new($"{source}:{n}: {m}");
            ThemeDef T() => t ?? throw Err("needs 'theme <id>' first");
            switch (p[0])
            {
                case "theme": t = new ThemeDef { Id = p[1] }; themes.Add(t); break;
                case "match": T().Match = p.Skip(1).ToList(); break;
                case "outliers":
                    T().OutlierMin = float.Parse(p[1], CultureInfo.InvariantCulture);
                    T().OutlierMax = float.Parse(p[^1], CultureInfo.InvariantCulture);
                    break;
                case "density": T().Density = float.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "tech": T().Tech = p[1]; break;
                case "set":
                    for (int i = 1; i + 1 < p.Length; i += 2) T().Set.Add((p[i], float.Parse(p[i + 1], CultureInfo.InvariantCulture)));
                    break;
                default: throw Err($"unknown keyword '{p[0]}'");
            }
        }
        return themes;
    }

    /// <summary>The theme for a world: its archetype.variant, then its archetype, then default. A start world (<paramref name="start"/>) keeps the theme with no faction holding ground.</summary>
    public static ThemeDef For(PoiLibrary lib, string archetype, string variant, bool start = false)
    {
        var theme = Find(lib, archetype, variant);
        return start ? theme.AsStart() : theme;
    }

    static ThemeDef Find(PoiLibrary lib, string archetype, string variant)
    {
        string full = $"{archetype}.{variant}".ToLowerInvariant(), arch = archetype.ToLowerInvariant();
        return lib.Themes.FirstOrDefault(t => t.Match.Contains(full))
               ?? lib.Themes.FirstOrDefault(t => t.Match.Contains(arch))
               ?? lib.Themes.FirstOrDefault(t => t.Match.Contains("default"))
               ?? new ThemeDef { Id = "default", Set = lib.Families.Where(f => !f.Space).Select(f => (f.Id, 1f)).ToList() };
    }
}

/// <summary>
/// Places POIs across a whole planet (a torus: distances wrap at both edges), not just near the
/// landing site. Sites are scattered by Poisson-disc sampling so they cover the map evenly with
/// some jitter; each gets a family from the planet's themed set by its biome and territory, and a
/// small data-tuned share are outliers from outside the set, rarer and more ominous.
/// </summary>
public static class WorldPlacement
{
    public sealed class Result
    {
        public ThemeDef Theme = null!;
        public List<PlacedPoi> Pois = new();
        public float OutlierRate;
        public int Outliers => Pois.Count(p => p.Outlier);
    }

    public static Result Place(PoiLibrary lib, PlanetMap map, ThemeDef theme, ulong seed, int? count = null)
    {
        var rng = new Rng(Hash.Combine(seed, 0x0A71D5UL));
        float km2 = map.Width * map.CellSize * map.Height * map.CellSize / 1e6f;
        int n = count ?? Math.Clamp((int)MathF.Round(km2 * theme.Density), 8, 400);
        // Even cover: a Poisson-disc scatter on the torus.
        float spacing = MathF.Sqrt(map.Width * map.Height / (float)n) * 0.86f;
        var sites = Scatter(map, n, spacing, rng.Fork("scatter"));

        var result = new Result { Theme = theme };
        result.OutlierRate = rng.Float(theme.OutlierMin, theme.OutlierMax);
        int outliers = Math.Clamp((int)MathF.Round(sites.Count * result.OutlierRate),
            Math.Max(1, (int)MathF.Ceiling(sites.Count * theme.OutlierMin - 0.001f)), Math.Max(1, (int)MathF.Floor(sites.Count * theme.OutlierMax + 0.001f)));
        var outlierAt = new HashSet<int>();
        var order = Enumerable.Range(0, sites.Count).ToList();
        rng.Shuffle(order);
        foreach (int i in order.Take(outliers)) outlierAt.Add(i);

        var themed = theme.Set.Where(s => lib.Families.Any(f => f.Id == s.Family)).ToList();
        var inSet = new HashSet<string>(themed.Select(s => s.Family));
        var strangers = lib.Families.Where(f => !f.Space && !inSet.Contains(f.Id)).Where(f => !theme.Start || CanRuin(f)).ToList();
        // A start world: faction places only as ruins (no core, no one home), and less often.
        static bool CanRuin(FamilyDef f) => !ThemeDef.FactionOwned(f) || f.StateWeights[(int)PoiState.Ruined] > 0f;
        // Blast sites are rare on a start world (PoiSiting also keeps them far from the bunker).
        float StartWeight(FamilyDef f) => !theme.Start ? 1f : ThemeDef.FactionOwned(f) ? 0.3f : f.Tags.Contains("radiation") ? 0.2f : 1f;
        var uniqueUsed = new HashSet<string>();
        for (int i = 0; i < sites.Count; i++)
        {
            var (x, y) = sites[i];
            bool outlier = outlierAt.Contains(i);
            FamilyDef? fam = null;
            if (outlier && strangers.Count > 0)
            {
                // Something that doesn't belong: weighted toward the rare and the ominous.
                var w = strangers.Select(f => (1f + (int)f.Rarity * 0.8f + f.Ominous * 2f) * (uniqueUsed.Contains(f.Id) && f.Rarity >= Rarity.Exotic ? 0f : 1f)).ToList();
                int k = rng.PickWeighted(w);
                if (k >= 0) fam = strangers[k];
            }
            else
            {
                var biome = map.BiomeAt(x, y);
                var territory = map.TerritoryAt(x, y);
                var fits = themed.Where(s => !theme.Start || CanRuin(lib.Family(s.Family))).Select(s => (Fam: lib.Family(s.Family), Weight: s.Weight * StartWeight(lib.Family(s.Family))))
                    .Where(s => s.Fam.Biomes.Count == 0 || s.Fam.Biomes.Contains(biome) || s.Fam.Generator != Generator.Kit)
                    .Where(s => s.Fam.Territories.Count == 0 || s.Fam.Territories.Contains("any") || s.Fam.Territories.Contains(territory) || s.Fam.Generator != Generator.Kit)
                    .Where(s => !(s.Fam.Rarity >= Rarity.Exotic && uniqueUsed.Contains(s.Fam.Id)))
                    .ToList();
                if (fits.Count == 0) fits = themed.Where(s => !theme.Start || CanRuin(lib.Family(s.Family))).Select(s => (Fam: lib.Family(s.Family), s.Weight)).ToList();
                // Rarer places turn up less often: the weight falls with the rarity ladder.
                var w = fits.Select(s => s.Weight / (1f + (int)s.Fam.Rarity * (int)s.Fam.Rarity * 0.6f)).ToList();
                int k = rng.PickWeighted(w);
                if (k >= 0) fam = fits[k].Fam;
            }
            if (fam == null) continue;
            if (fam.Rarity >= Rarity.Exotic) uniqueUsed.Add(fam.Id);
            // A layout for the world's tech level, where the family has them.
            var fitting = fam.Variants.Where(v => v.Tech.Length == 0 || v.Tech == theme.Tech).ToList();
            if (fitting.Count == 0) fitting = fam.Variants;
            var variant = fitting[rng.Range(0, fitting.Count)];
            int state = Math.Max(0, rng.PickWeighted(fam.StateWeights));
            if (theme.Start && ThemeDef.FactionOwned(fam)) state = (int)PoiState.Ruined;
            result.Pois.Add(new PlacedPoi
            {
                Family = fam, Variant = variant, State = (PoiState)state, SpawnSeed = rng.NextU64(),
                CellX = x, CellY = y, X = (x + 0.5f) * map.CellSize, Z = (y + 0.5f) * map.CellSize, Planet = map.Name, Outlier = outlier,
            });
        }
        return result;
    }

    public static float TorusDist(PlanetMap map, float ax, float ay, float bx, float by)
    {
        float dx = MathF.Abs(ax - bx), dy = MathF.Abs(ay - by);
        dx = MathF.Min(dx, map.Width - dx);
        dy = MathF.Min(dy, map.Height - dy);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Dart throwing with a grid, wrapping at both edges.</summary>
    static List<(int x, int y)> Scatter(PlanetMap map, int n, float spacing, Rng rng)
    {
        var pts = new List<(int x, int y)>();
        int tries = n * 60;
        float sp = spacing;
        for (int t = 0; t < tries && pts.Count < n; t++)
        {
            int x = rng.Range(0, map.Width), y = rng.Range(0, map.Height);
            bool ok = true;
            foreach (var (px, py) in pts)
                if (TorusDist(map, x, y, px, py) < sp) { ok = false; break; }
            if (ok) pts.Add((x, y));
            // If the map fills up before n, relax the spacing a little.
            if (t % (n * 10) == n * 10 - 1) sp *= 0.9f;
        }
        return pts;
    }
}
