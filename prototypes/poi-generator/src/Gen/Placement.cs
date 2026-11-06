#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

public enum PlanetSize { Small, Medium, Large }

/// <summary>
/// A planet as the placement engine sees it: a 2D grid of cells, each with a biome, an owning
/// faction (or none: "wild") and how rich its deposits are.
/// </summary>
public sealed class PlanetMap
{
    public string Name = "";
    public PlanetSize Size;
    public int Width, Height;
    public float CellSize = 24f;    // metres per cell
    public Biome[] Biomes = Array.Empty<Biome>();
    public string[] Territory = Array.Empty<string>();
    public bool[] Deposit = Array.Empty<bool>();

    public int Index(int x, int y) => y * Width + x;
    public bool In(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
    public Biome BiomeAt(int x, int y) => Biomes[Index(x, y)];
    public string TerritoryAt(int x, int y) => Territory[Index(x, y)];

    public static int SideFor(PlanetSize s) => s switch { PlanetSize.Small => 40, PlanetSize.Medium => 56, _ => 72 };

    public static PlanetMap Generate(ulong seed, PlanetSize size, string name = "") => GenerateRect(seed, size, SideFor(size), SideFor(size), 24f, name);

    /// <summary>A whole planet: a wide rectangle of coarse cells (16 by 8 km at 80 m a cell, say).</summary>
    public static PlanetMap GenerateWorld(ulong seed, int width, int height, float cellSize, string name = "") =>
        GenerateRect(seed, PlanetSize.Large, width, height, cellSize, name);

    static PlanetMap GenerateRect(ulong seed, PlanetSize size, int width, int height, float cellSize, string name)
    {
        int side = Math.Min(width, height);
        var map = new PlanetMap
        {
            Name = name, Size = size, Width = width, Height = height, CellSize = cellSize,
            Biomes = new Biome[width * height], Territory = new string[width * height], Deposit = new bool[width * height],
        };
        float freqScale = 40f / side;
        var rng = new Rng(Hash.Combine(seed, 0x91A4E7UL));
        ulong sTemp = rng.NextU64(), sWet = rng.NextU64(), sAsh = rng.NextU64(), sRock = rng.NextU64(), sOre = rng.NextU64();

        // Two or so capitals per faction; whoever is nearest owns a cell, if it's close enough.
        string[] factions = { "vharn", "meridian", "freeholders", "kessari" };
        var capitals = new List<(float x, float y, string f)>();
        foreach (string f in factions)
            for (int i = rng.Between(1, 2); i > 0; i--)
                capitals.Add((rng.Float(0.1f, 0.9f) * width, rng.Float(0.1f, 0.9f) * height, f));
        float reach = side * 0.3f;

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float temp = Noise.Fbm(sTemp, x, y, 0.045f * freqScale), wet = Noise.Fbm(sWet, x, y, 0.06f * freqScale);
                float ash = Noise.Fbm(sAsh, x, y, 0.05f * freqScale), rock = Noise.Fbm(sRock, x, y, 0.07f * freqScale);
                Biome b = temp < 0.33f ? Biome.Ice
                    : wet > 0.62f ? Biome.Fungal
                    : wet > 0.5f ? Biome.Steppe
                    : ash > 0.6f ? Biome.Ash
                    : rock > 0.52f ? Biome.Rock
                    : Biome.Desert;
                int i = map.Index(x, y);
                map.Biomes[i] = b;
                map.Deposit[i] = Noise.Fbm(sOre, x, y, 0.11f) > 0.66f;

                float jitter = (Noise.Fbm(sRock ^ 0x55, x, y, 0.15f) - 0.5f) * side * 0.12f;
                var nearest = capitals.OrderBy(c => MathF.Sqrt((c.x - x) * (c.x - x) + (c.y - y) * (c.y - y))).First();
                float d = MathF.Sqrt((nearest.x - x) * (nearest.x - x) + (nearest.y - y) * (nearest.y - y)) + jitter;
                map.Territory[i] = d < reach ? nearest.f : "wild";
            }
        return map;
    }
}

/// <summary>Smooth value noise in [0, 1], pure C# so maps are the same everywhere.</summary>
public static class Noise
{
    static float Lattice(ulong seed, int x, int y) =>
        (Hash.Combine(seed, (ulong)(uint)x, (ulong)(uint)y) >> 40) / (float)(1UL << 24);

    static float Smooth(float t) => t * t * (3f - 2f * t);

    public static float Value(ulong seed, float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float tx = Smooth(x - x0), ty = Smooth(y - y0);
        float a = Lattice(seed, x0, y0), b = Lattice(seed, x0 + 1, y0);
        float c = Lattice(seed, x0, y0 + 1), d = Lattice(seed, x0 + 1, y0 + 1);
        return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
    }

    public static float Fbm(ulong seed, float x, float y, float freq)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < 3; o++)
        {
            sum += Value(seed + (ulong)o * 977, x * freq, y * freq) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / norm;
    }
}

/// <summary>A place on a planet: which layout, which state, where, and the rules it lives by.</summary>
public sealed class PlacedPoi
{
    public FamilyDef Family = null!;
    public VariantDef Variant = null!;
    public PoiState State;
    public ulong SpawnSeed;
    public int CellX, CellY;
    public float X, Z;            // metres from the map's corner
    public string Planet = "";
    /// <summary>The world's one main starport: the network's main node.</summary>
    public bool Primary;
    /// <summary>The class this port was built to, if it's a port.</summary>
    public Starport? Port;
    /// <summary>From outside the planet's themed set: something that doesn't belong here.</summary>
    public bool Outlier;

    public Rarity Rarity => Family.Rarity;
    public RevealRule Reveal => Family.Reveal;
    public bool Regrows => Regrowth.Regrows(Rarity);
    public string LayoutKey => Variant.Key;
    public override string ToString() => $"{Variant} ({State}) at {CellX},{CellY}";
}

/// <summary>What one star system has already used: the same layout never appears twice in it.</summary>
public sealed class SystemContext
{
    public readonly HashSet<string> UsedLayouts = new();
}

/// <summary>Layouts this player has already walked through. Unseen ones are picked more often.</summary>
public sealed class PlayerKnowledge
{
    public readonly HashSet<string> Seen = new();
    public float UnseenWeight = 3f;
    public float SeenWeight = 1f;
    public float WeightOf(VariantDef v) => Seen.Contains(v.Key) ? SeenWeight : UnseenWeight;
}

public static class Placement
{
    /// <summary>Map cells between any two places, whatever their family.</summary>
    public const float MinSpacing = 4f;

    /// <summary>Several commons per planet, one or two uncommons, scaled by size.</summary>
    public static (int common, int uncommon) Counts(PlanetSize s) => s switch
    {
        PlanetSize.Small => (3, 1),
        PlanetSize.Medium => (5, 2),
        _ => (7, 2),
    };

    /// <summary>Places a whole system. A rare place turns up in about one system in three.</summary>
    public static List<(PlanetMap Map, List<PlacedPoi> Pois)> PlaceSystem(PoiLibrary lib, ulong systemSeed, IReadOnlyList<PlanetSize> planets,
        PlayerKnowledge knowledge, float rareChance = 0.35f)
    {
        var rng = new Rng(Hash.Combine(systemSeed, 0x5157E3UL));
        var sys = new SystemContext();
        int rarePlanet = rng.Chance(rareChance) ? rng.Range(0, planets.Count) : -1;
        var result = new List<(PlanetMap, List<PlacedPoi>)>();
        for (int i = 0; i < planets.Count; i++)
        {
            var map = PlanetMap.Generate(Hash.Combine(systemSeed, (ulong)i), planets[i], $"Planet {(char)('A' + i)}");
            result.Add((map, PlacePlanet(lib, map, sys, knowledge, rng.Fork("planet" + i), i == rarePlanet)));
        }
        return result;
    }

    public static List<PlacedPoi> PlacePlanet(PoiLibrary lib, PlanetMap map, SystemContext sys, PlayerKnowledge knowledge, Rng rng, bool withRare)
    {
        var placed = new List<PlacedPoi>();
        var (commons, uncommons) = Counts(map.Size);
        if (withRare) PlaceSome(lib, map, sys, knowledge, rng, placed, Rarity.Rare, 1);
        PlaceSome(lib, map, sys, knowledge, rng, placed, Rarity.Uncommon, uncommons);
        PlaceSome(lib, map, sys, knowledge, rng, placed, Rarity.Common, commons);
        return placed;
    }

    static void PlaceSome(PoiLibrary lib, PlanetMap map, SystemContext sys, PlayerKnowledge knowledge, Rng rng,
        List<PlacedPoi> placed, Rarity rarity, int count)
    {
        var families = lib.Families.Where(f => f.Rarity == rarity).ToList();
        for (int n = 0; n < count; n++)
        {
            // Try families in a random order until one has an unused layout and somewhere to go.
            foreach (var family in rng.WeightedOrder(families, _ => 1f))
            {
                var free = family.Variants.Where(v => !sys.UsedLayouts.Contains(v.Key)).ToList();
                if (free.Count == 0) continue;
                var spots = Candidates(lib, map, family, placed);
                if (spots.Count == 0) continue;

                var variant = ChooseVariant(free, knowledge, rng);
                var (x, y) = rng.Pick(spots);
                int state = rng.PickWeighted(family.StateWeights);
                placed.Add(new PlacedPoi
                {
                    Family = family, Variant = variant, State = (PoiState)Math.Max(0, state), SpawnSeed = rng.NextU64(),
                    CellX = x, CellY = y, X = (x + 0.5f) * map.CellSize, Z = (y + 0.5f) * map.CellSize, Planet = map.Name,
                });
                sys.UsedLayouts.Add(variant.Key);
                break;
            }
        }
    }

    /// <summary>Unseen layouts weigh more, so players keep finding new buildings.</summary>
    public static VariantDef ChooseVariant(IReadOnlyList<VariantDef> free, PlayerKnowledge knowledge, Rng rng) =>
        free[rng.PickWeighted(free.Select(knowledge.WeightOf).ToList())];

    /// <summary>Every map cell where a family may go, given what is placed already.</summary>
    public static List<(int x, int y)> Candidates(PoiLibrary lib, PlanetMap map, FamilyDef family, List<PlacedPoi> placed)
    {
        var spots = new List<(int, int)>();
        for (int y = 2; y < map.Height - 2; y++)
            for (int x = 2; x < map.Width - 2; x++)
                if (Allowed(map, family, x, y, placed)) spots.Add((x, y));
        return spots;
    }

    public static bool Allowed(PlanetMap map, FamilyDef family, int x, int y, IReadOnlyList<PlacedPoi> placed)
    {
        if (!family.Biomes.Contains(map.BiomeAt(x, y))) return false;
        if (!family.Territories.Contains("any") && !family.Territories.Contains(map.TerritoryAt(x, y))) return false;

        foreach (var p in placed)
        {
            float d = Dist(x, y, p.CellX, p.CellY);
            if (d < MinSpacing) return false;
            if (p.Family == family && d < family.Spacing) return false;
            // Avoid rules hold both ways: whoever was placed first, the farm stays away.
            if (p.Family.Avoid.Any(r => r.Feature == family.Id && d < r.Radius)) return false;
        }
        foreach (var r in family.Near)
            if (!FeatureWithin(map, r, x, y, placed)) return false;
        foreach (var r in family.Avoid)
            if (FeatureWithin(map, r, x, y, placed)) return false;
        return true;
    }

    /// <summary>Is the feature (deposits, a territory, or a family) within the rule's radius?</summary>
    public static bool FeatureWithin(PlanetMap map, ProximityRule rule, int x, int y, IReadOnlyList<PlacedPoi> placed)
    {
        if (rule.Feature == "deposits" || rule.Feature.StartsWith("territory:", StringComparison.Ordinal))
        {
            string? territory = rule.Feature == "deposits" ? null : rule.Feature["territory:".Length..];
            int r = (int)MathF.Ceiling(rule.Radius);
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = x + dx, cy = y + dy;
                    if (!map.In(cx, cy) || dx * dx + dy * dy > rule.Radius * rule.Radius) continue;
                    if (territory == null ? map.Deposit[map.Index(cx, cy)] : map.TerritoryAt(cx, cy) == territory) return true;
                }
            return false;
        }
        return placed.Any(p => p.Family.Id == rule.Feature && Dist(x, y, p.CellX, p.CellY) < rule.Radius);
    }

    public static float Dist(int ax, int ay, int bx, int by) => MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
}

/// <summary>
/// The map shows a place once you have been close enough: its reveal distance. Landmarks show
/// from orbit (and from far off on the ground). Once shown, a place stays on the map.
/// </summary>
public sealed class MapReveal
{
    public const float OrbitAltitude = 800f;
    public const float LandmarkGroundDistance = 2000f;
    public readonly Dictionary<PlacedPoi, float> Revealed = new();

    public static bool CanSee(PlacedPoi p, float x, float z, float altitude, out float distance)
    {
        float flat = MathF.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));
        distance = MathF.Sqrt(flat * flat + altitude * altitude);
        if (p.Reveal.Landmark) return altitude >= OrbitAltitude || flat <= LandmarkGroundDistance;
        return distance <= p.Reveal.Distance;
    }

    /// <summary>Returns the places revealed by this observer position, and remembers them.</summary>
    public List<PlacedPoi> Update(IEnumerable<PlacedPoi> pois, float x, float z, float altitude)
    {
        var fresh = new List<PlacedPoi>();
        foreach (var p in pois)
        {
            if (Revealed.ContainsKey(p) || !CanSee(p, x, z, altitude, out float d)) continue;
            Revealed[p] = d;
            fresh.Add(p);
        }
        return fresh;
    }

    public bool IsRevealed(PlacedPoi p) => Revealed.ContainsKey(p);
}

/// <summary>
/// Common places fill up again: once cleared, a timer runs while no player is near, and when
/// it ends the place respawns with fresh dressing (same layout). Rare and unique places stay
/// cleared for good.
/// </summary>
public sealed class Regrowth
{
    public float RegrowSeconds = 1800f;
    public float PlayerRadius = 600f;
    readonly Dictionary<PlacedPoi, float> _cleared = new();

    public static bool Regrows(Rarity r) => r == Rarity.Common;

    public void MarkCleared(PlacedPoi p) => _cleared[p] = 0f;
    public bool IsCleared(PlacedPoi p) => _cleared.ContainsKey(p);

    /// <summary>Advances every timer; returns the places that just regrew.</summary>
    public List<PlacedPoi> Tick(float dt, IReadOnlyList<(float X, float Z)> players)
    {
        var regrown = new List<PlacedPoi>();
        foreach (var p in _cleared.Keys.ToList())
        {
            if (!p.Regrows) continue;
            bool near = players.Any(q => MathF.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z)) < PlayerRadius);
            float t = near ? 0f : _cleared[p] + dt; // someone nearby resets the clock
            if (t < RegrowSeconds)
            {
                _cleared[p] = t;
                continue;
            }
            _cleared.Remove(p);
            p.SpawnSeed = Hash.Combine(p.SpawnSeed, 0x6E6F7721UL);
            regrown.Add(p);
        }
        return regrown;
    }
}
