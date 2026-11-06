#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>The design's five-step rarity ladder, the same one shared/Items uses.</summary>
/// <summary>A place's weight in the world's infrastructure (roads, rail, power): the higher, the more lines reach it.</summary>
public enum PoiTier { Wreck, Site, Camp, Outpost, Base, Town, Spaceport, City }

public enum Rarity : byte { Common, Uncommon, Rare, Exotic, Unique }

public enum PoiState : byte { Active, Abandoned, Infested, Ruined }

public enum Biome : byte { Desert, Rock, Ash, Ice, Steppe, Fungal }

/// <summary>Which generator builds a family's places.</summary>
public enum Generator : byte
{
    Kit,        // the socket assembler joins faction-kit modules
    Building,   // the architect plans one building from a purpose program
    Site,       // the architect composes a whole site: buildings, yards, wrecks and features
}

/// <summary>
/// Who holds the core. Only places a faction owns have one: a garrison, an outpost someone
/// still runs. Wrecks, ruins, gang camps and places nobody lives in have none (a wreck can be
/// restored by fitting one).
/// </summary>
public enum CoreRule : byte { Owned, None }

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
    /// <summary>Spaceports: the old starport size this layout is built for (data: port field|port|major|grand); the world's starport picks it.</summary>
    public Starport? Port;
    /// <summary>Architect families: a site plan or program for this layout only (else the family's).</summary>
    public string? Site, Program;
    /// <summary>The world tech level this layout belongs to (low, mid, high; empty fits any).</summary>
    public string Tech = "";

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

    // ---- added with the architect ---------------------------------------------------------
    public string? Program;                    // building program (data/programs.txt)
    public string? Site;                       // site plan (data/sites.txt)
    public CoreRule Core = CoreRule.Owned;
    bool _coreSet;
    /// <summary>"faction", "gang" (always hostile, no faction standing) or "none".</summary>
    public string Owner = "faction";
    /// <summary>How much worse it gets deeper down: lights fail, Bloom thickens, enemies and loot climb.</summary>
    public float Ominous;
    /// <summary>The share of spawns that are deliberately wrong (a layout that breaks its own rules).</summary>
    public float Wrong;
    /// <summary>Theme tags placement sets match on: ice, fallen, military, alien, industrial...</summary>
    public List<string> Tags = new();
    /// <summary>Hand-authored landmark (a designed site) rather than a procgen type.</summary>
    public bool Landmark;
    /// <summary>A space POI: placed in orbit zones, never on a planet's surface.</summary>
    public bool Space;
    /// <summary>One line for the catalogue and the discovery card.</summary>
    public string Pitch = "";
    /// <summary>
    /// The ground it wants on a planet: peak, ridge, hillside, valley, flat, open or any (planet
    /// surface siting reads it; empty means it falls back to its own guess).
    /// </summary>
    public string Siting = "";
    /// <summary>How far its landmark should stand above the ground it sits on, in metres (tests).</summary>
    public int Rise = 6;
    /// <summary>
    /// How much the place matters to a road network that will join places up (most first): city,
    /// spaceport, town, base, outpost, camp, site, wreck. Data: tier.
    /// </summary>
    public PoiTier Tier = PoiTier.Site;
    /// <summary>
    /// A style kit that re-skins the family's program or site (data: style). This is how a flavour
    /// such as alien works: the same rooms and plans built in another kit.
    /// </summary>
    public string? Style;
    /// <summary>The made ground of its pad on a planet (concrete, tarmac, gravel, dirt or natural); empty picks by purpose.</summary>
    public string? Ground;
    /// <summary>The tech levels it belongs to (low, mid, high; data: tech before any variant). Empty: any level.</summary>
    public List<string> Techs = new();

    public Generator Generator => Site != null ? Generator.Site : Program != null ? Generator.Building : Generator.Kit;

    /// <summary>The rosters.txt section its defenders come from; the faction id unless set.</summary>
    public string Roster
    {
        get => _roster ?? (Faction == Faction.None ? "none" : Faction.Id());
        set => _roster = value;
    }

    /// <summary>Whether a spawn in this state has a core: only while a faction holds the place.</summary>
    public bool HasCore(PoiState state) => Core == CoreRule.Owned && state != PoiState.Ruined;

    internal void SetCore(CoreRule c)
    {
        Core = c;
        _coreSet = true;
    }

    internal void DefaultCore()
    {
        if (_coreSet) return;
        Core = Faction == Faction.None || Owner != "faction" ? CoreRule.None : CoreRule.Owned;
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
                case "pitch": F().Pitch = rest; break;
                case "siting": F().Siting = p[1]; break;
                case "rise": F().Rise = int.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "tier": F().Tier = Enum<PoiTier>(p[1], Error); break;
                case "style": F().Style = p[1]; break;
                case "ground": F().Ground = p[1]; break;
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
                case "program":
                    if (v != null) V().Program = p[1];
                    else F().Program = p[1];
                    break;
                case "site":
                    if (v != null) V().Site = p[1];
                    else F().Site = p[1];
                    break;
                case "core": F().SetCore(p[1] == "none" ? CoreRule.None : CoreRule.Owned); break;
                case "owner": F().Owner = p[1]; break;
                case "ominous": F().Ominous = Num(p[1]); break;
                case "wrong": F().Wrong = Num(p[1]); break;
                case "tags": F().Tags = p.Skip(1).ToList(); break;
                case "landmark": F().Landmark = true; break;
                case "space": F().Space = true; break;
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
                case "port": V().Port = Enum<Starport>(p[1], Error); break;
                case "tech":
                    if (v != null) V().Tech = p[1];
                    else F().Techs = p.Skip(1).ToList();
                    break;
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
            fam.DefaultCore();
            bool kit = fam.Generator == Generator.Kit;
            if (kit && !fam.Kits.ContainsKey("main")) throw new FormatException($"{source}: family {fam.Id} has no kit");
            if (fam.Variants.Count is < 3 or > 6) throw new FormatException($"{source}: family {fam.Id} needs 3 to 6 variants, has {fam.Variants.Count}");
            foreach (var vr in fam.Variants)
            {
                if (kit && (vr.Path.Count < 2 || vr.Path[0].Kind != ModuleKind.Entrance || vr.Path[^1].Kind != ModuleKind.Vault))
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
