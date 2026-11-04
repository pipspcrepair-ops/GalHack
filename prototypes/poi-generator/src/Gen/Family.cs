#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

public enum Rarity : byte { Common, Uncommon, Rare }

public enum PoiState : byte { Active, Abandoned, Infested, Ruined }

public enum Biome : byte { Desert, Rock, Ash, Ice, Steppe, Fungal }

/// <summary>One step of a critical path: a module kind from a kit alias, repeated Min..Max times.</summary>
public readonly record struct PathStep(string Alias, ModuleKind Kind, int Min, int Max);

/// <summary>A module kind from a kit alias ("main" or "deep").</summary>
public readonly record struct KindRef(string Alias, ModuleKind Kind)
{
    public override string ToString() => Alias == "main" ? Kind.ToString().ToLowerInvariant() : $"{Alias}:{Kind.ToString().ToLowerInvariant()}";
}

/// <summary>
/// One named layout of a family. Its seed is fixed, so "Brood Spine" is always the same
/// building; only the dressing (state, loot, enemies, logs) changes between spawns.
/// </summary>
public sealed class VariantDef
{
    public FamilyDef Family = null!;
    public int Index;
    public string Name = "";
    public ulong Seed;
    public List<PathStep> Path = new();
    public int BranchMin, BranchMax;
    public List<List<KindRef>> BranchChains = new();
    public List<KindRef> Grow = new();

    /// <summary>Identifies the layout across a system, for the no-repeats rule.</summary>
    public string Key => $"{Family.Id}/{Index}";
    public override string ToString() => $"{Family.Name}: {Name}";
}

public readonly record struct KitRef(string KitId, string Tag);

/// <summary>A near or avoid rule. Feature is "deposits", "territory:&lt;faction&gt;" or a family id.</summary>
public readonly record struct ProximityRule(string Feature, float Radius);

/// <summary>How far away a place shows on the map. Landmarks show from orbit.</summary>
public readonly record struct RevealRule(float Distance, bool Landmark);

public sealed class FamilyDef
{
    public string Id = "";
    public string Name = "";
    public Faction Faction;
    public Rarity Rarity;
    public Dictionary<string, KitRef> Kits = new();
    public List<Biome> Biomes = new();
    public List<string> Territories = new();   // faction names, "wild", or "any"
    public List<ProximityRule> Near = new();
    public List<ProximityRule> Avoid = new();
    public float Spacing = 4f;                 // map cells to the nearest place of the same family
    public RevealRule Reveal = new(300f, false);
    public float[] StateWeights = { 1f, 1f, 1f, 1f };
    public float Damage;                       // baseline wreckage, even when active (crash sites)
    public List<VariantDef> Variants = new();
    string? _roster;

    /// <summary>The rosters.txt section its defenders come from; the faction id unless set.</summary>
    public string Roster
    {
        get => _roster ?? Faction.Id();
        set => _roster = value;
    }

    public bool Underground => Kits.ContainsKey("deep") || Variants.Any(v => v.Path.Any(s => s.Kind == ModuleKind.Descent));
    public override string ToString() => Name;
}

/// <summary>Reads data/families.txt. Indentation is for people; keywords decide the context.</summary>
public static class FamilyParser
{
    public static List<FamilyDef> Parse(string text, string source)
    {
        var families = new List<FamilyDef>();
        FamilyDef? f = null;
        VariantDef? v = null;
        string[] lines = text.Replace("\r", "").Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            string line = lines[n].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int lineNo = n + 1;
            FormatException Error(string msg) => new($"{source}:{lineNo}: {msg}");
            string[] p = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            string rest = string.Join(' ', p.Skip(1));
            FamilyDef F() => f ?? throw Error("needs a 'family <id>' first");
            VariantDef V() => v ?? throw Error("needs a 'variant <name>' first");

            switch (p[0])
            {
                case "family":
                    f = new FamilyDef { Id = p[1] };
                    families.Add(f);
                    v = null;
                    break;
                case "name": F().Name = rest; break;
                case "faction": F().Faction = Enum<Faction>(p[1], Error); break;
                case "roster": F().Roster = p[1]; break;
                case "kit": F().Kits["main"] = new KitRef(p[1], p.Length > 2 ? p[2] : ""); break;
                case "deep": F().Kits["deep"] = new KitRef(p[1], p.Length > 2 ? p[2] : ""); break;
                case "rarity": F().Rarity = Enum<Rarity>(p[1], Error); break;
                case "biomes": F().Biomes = p.Skip(1).Select(b => Enum<Biome>(b, Error)).ToList(); break;
                case "territory": F().Territories = p.Skip(1).ToList(); break;
                case "near": F().Near.Add(new ProximityRule(p[1], Num(p[2]))); break;
                case "avoid": F().Avoid.Add(new ProximityRule(p[1], Num(p[2]))); break;
                case "spacing": F().Spacing = Num(p[1]); break;
                case "damage": F().Damage = Num(p[1]); break;
                case "reveal":
                    F().Reveal = p[1] == "landmark" ? new RevealRule(float.PositiveInfinity, true) : new RevealRule(Num(p[1]), false);
                    break;
                case "states":
                    for (int i = 1; i + 1 < p.Length; i += 2)
                        F().StateWeights[(int)Enum<PoiState>(p[i], Error)] = Num(p[i + 1]);
                    break;
                case "variant":
                    v = new VariantDef { Family = F(), Index = F().Variants.Count, Name = rest };
                    F().Variants.Add(v);
                    break;
                case "seed": V().Seed = ulong.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "path":
                    V().Path = p.Skip(1).Select(t => ParseStep(t, Error)).ToList();
                    break;
                case "branches":
                {
                    string[] range = p[1].Split('-');
                    V().BranchMin = int.Parse(range[0], CultureInfo.InvariantCulture);
                    V().BranchMax = int.Parse(range[^1], CultureInfo.InvariantCulture);
                    V().BranchChains = p.Skip(2).Select(c => c.Split('>').Select(k => ParseKind(k, Error)).ToList()).ToList();
                    break;
                }
                case "grow": V().Grow = p.Skip(1).Select(k => ParseKind(k, Error)).ToList(); break;
                default: throw Error($"unknown keyword '{p[0]}'");
            }
        }

        foreach (var fam in families)
        {
            if (!fam.Kits.ContainsKey("main")) throw new FormatException($"{source}: family {fam.Id} has no kit");
            if (fam.Variants.Count is < 3 or > 6) throw new FormatException($"{source}: family {fam.Id} needs 3 to 6 variants, has {fam.Variants.Count}");
            foreach (var vr in fam.Variants)
            {
                if (vr.Path.Count < 2 || vr.Path[0].Kind != ModuleKind.Entrance || vr.Path[^1].Kind != ModuleKind.Vault)
                    throw new FormatException($"{source}: {fam.Id} '{vr.Name}': a path runs from an entrance to a vault");
                if (vr.Seed == 0) vr.Seed = Hash.Of(vr.Key);
            }
        }
        return families;
    }

    static PathStep ParseStep(string token, Func<string, FormatException> error)
    {
        int min = 1, max = 1;
        int star = token.IndexOf('*');
        if (star >= 0)
        {
            string[] range = token[(star + 1)..].Split('-');
            min = int.Parse(range[0], CultureInfo.InvariantCulture);
            max = int.Parse(range[^1], CultureInfo.InvariantCulture);
            token = token[..star];
        }
        var k = ParseKind(token, error);
        return new PathStep(k.Alias, k.Kind, min, max);
    }

    static KindRef ParseKind(string token, Func<string, FormatException> error)
    {
        string alias = "main";
        int colon = token.IndexOf(':');
        if (colon >= 0)
        {
            alias = token[..colon];
            token = token[(colon + 1)..];
        }
        return new KindRef(alias, Enum<ModuleKind>(token, error));
    }

    static T Enum<T>(string s, Func<string, FormatException> error) where T : struct, System.Enum =>
        System.Enum.TryParse(s, true, out T v) ? v : throw error($"'{s}' is not a {typeof(T).Name}");

    static float Num(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}
