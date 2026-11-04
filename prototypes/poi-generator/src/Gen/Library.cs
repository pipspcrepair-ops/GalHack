#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

public enum EnemyKind : byte { Guard, Turret, Hover, Boss, Corrupted, Bloom, Fauna }

/// <summary>
/// Who is firing, for the combat system: it ramps accuracy toward a class-versus-target
/// baseline after warning shots, and capital-class rounds fly slowly. Combat itself lives
/// elsewhere; places only tag their defenders.
/// </summary>
public enum ShooterClass : byte { None, Infantry, SmallVessel, Capital }

/// <summary>One roster line from data/rosters.txt.</summary>
public sealed record EnemyDef(string Name, EnemyKind Kind, ShooterClass Shooter, string Weapon)
{
    public bool Armed => Shooter != ShooterClass.None;
}

public sealed record LootItem(string Name, int Tier, int Value, bool Unique);

/// <summary>Every piece of authored data the generator reads, parsed and cross-checked.</summary>
public sealed class PoiLibrary
{
    public Dictionary<string, Kit> Kits = new();
    public List<FamilyDef> Families = new();
    public Dictionary<string, List<EnemyDef>> Rosters = new();
    public List<LootItem> Loot = new();
    public Dictionary<string, List<string>> LogSections = new();

    public FamilyDef Family(string id) => Families.First(f => f.Id == id);

    /// <summary>Loads from (file name, text) pairs, so the caller decides where files come from.</summary>
    public static PoiLibrary Load(IEnumerable<(string Name, string Text)> files)
    {
        var lib = new PoiLibrary();
        foreach (var (name, text) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (name.EndsWith(".kit", StringComparison.Ordinal))
            {
                var kit = KitParser.Parse(text, name);
                lib.Kits.Add(kit.Id, kit);
            }
            else if (name.EndsWith("families.txt", StringComparison.Ordinal)) lib.Families = FamilyParser.Parse(text, name);
            else if (name.EndsWith("rosters.txt", StringComparison.Ordinal)) lib.Rosters = ParseRosters(text, name);
            else if (name.EndsWith("loot.txt", StringComparison.Ordinal)) lib.Loot = ParseLoot(text, name);
            else if (name.EndsWith("logs.txt", StringComparison.Ordinal)) lib.LogSections = ParseSections(text);
        }
        lib.Check();
        return lib;
    }

    /// <summary>The modules a family can use for one step.</summary>
    public List<ModuleTemplate> Modules(FamilyDef family, string alias, ModuleKind kind)
    {
        if (!family.Kits.TryGetValue(alias, out KitRef r)) r = family.Kits["main"];
        return Kits[r.KitId].Modules.Where(m => m.Kind == kind && m.HasTag(r.Tag)).ToList();
    }

    public Kit KitFor(FamilyDef family, string alias = "main") =>
        Kits[(family.Kits.TryGetValue(alias, out KitRef r) ? r : family.Kits["main"]).KitId];

    public List<EnemyDef> Roster(string section) => Rosters.TryGetValue(section, out var r) ? r : new List<EnemyDef>();

    /// <summary>Fails loudly at load time if a family asks for something no kit provides.</summary>
    void Check()
    {
        foreach (var f in Families)
        {
            foreach (var (alias, r) in f.Kits)
                if (!Kits.ContainsKey(r.KitId)) throw new FormatException($"family {f.Id}: no kit '{r.KitId}'");
            foreach (var v in f.Variants)
            {
                var kinds = v.Path.Select(s => new KindRef(s.Alias, s.Kind)).Concat(v.Grow).Concat(v.BranchChains.SelectMany(c => c));
                foreach (var k in kinds)
                    if (Modules(f, k.Alias, k.Kind).Count == 0)
                        throw new FormatException($"family {f.Id} '{v.Name}': no {k} modules in its kits");
            }
            string roster = f.Faction.ToString().ToLowerInvariant();
            if (!Rosters.ContainsKey(roster)) throw new FormatException($"family {f.Id}: no roster [{roster}]");
        }
    }

    static Dictionary<string, List<string>> ParseSections(string text)
    {
        var sections = new Dictionary<string, List<string>>();
        List<string>? current = null;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string name = line[1..^1];
                if (!sections.TryGetValue(name, out current)) sections[name] = current = new List<string>();
                continue;
            }
            current?.Add(line);
        }
        return sections;
    }

    // kind  shooter  weapon  name...
    static Dictionary<string, List<EnemyDef>> ParseRosters(string text, string source)
    {
        var result = new Dictionary<string, List<EnemyDef>>();
        foreach (var (section, lines) in ParseSections(text))
        {
            var list = new List<EnemyDef>();
            foreach (string line in lines)
            {
                string[] p = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4) throw new FormatException($"{source} [{section}]: '{line}' is not 'kind shooter weapon name'");
                var kind = Enum.Parse<EnemyKind>(p[0], true);
                var shooter = p[1] switch
                {
                    "none" => ShooterClass.None,
                    "infantry" => ShooterClass.Infantry,
                    "small-vessel" => ShooterClass.SmallVessel,
                    "capital" => ShooterClass.Capital,
                    _ => throw new FormatException($"{source} [{section}]: unknown shooter class '{p[1]}'"),
                };
                list.Add(new EnemyDef(string.Join(' ', p.Skip(3)), kind, shooter, p[2]));
            }
            result[section] = list;
        }
        return result;
    }

    // tier  value  [unique]  name...
    static List<LootItem> ParseLoot(string text, string source)
    {
        var items = new List<LootItem>();
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] p = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            bool unique = p[2] == "unique";
            items.Add(new LootItem(string.Join(' ', p.Skip(unique ? 3 : 2)),
                int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture), unique));
        }
        if (items.Count == 0) throw new FormatException($"{source}: no loot");
        return items;
    }
}
