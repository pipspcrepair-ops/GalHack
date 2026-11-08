#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What a room is for: its size, what furnishes it, what it likes to be next to, what it holds
/// and how it is lit. Read from data/rooms.txt.
/// </summary>
public sealed class RoomDef
{
    public string Id = "";
    public string Name = "";
    public ModuleKind Kind = ModuleKind.Room;
    public int WMin = 4, WMax = 10;
    public List<(string Prop, float Weight)> Props = new();
    public float Fill = 0.35f;
    public List<string> Island = new();
    public List<string> Near = new();
    public List<string> Avoid = new();
    public string Prefer = "any";      // any, entrance, end, core
    public int LootMin, LootMax;
    public int SpawnMin, SpawnMax = 1;
    public int TerminalMin, TerminalMax;
    public string Light = "warm";      // warm, cold, dim, red, green, none, glyph
    public string? Lock;               // the keycard clearance its door needs
    public bool Blast;                 // its door is a blast door
    public List<string> Tags = new();  // keyspot, core, secure, quarantine, story...
    public List<string> Story = new(); // environmental scenes it can host
    /// <summary>The decorations library's set or theme for this room (wired in after proto/deco merges).</summary>
    public string Deco = "";
    public bool Has(string tag) => Tags.Contains(tag);
    public override string ToString() => Id;
}

/// <summary>A room a storey band asks for: "lab*2-3" or "vault!".</summary>
public readonly record struct Demand(string Room, int Min, int Max, bool Must);

/// <summary>
/// A building program: what the building is for, so its plan follows it. Massing (footprint,
/// storeys up and down), vertical cores, and the rooms each band of storeys holds.
/// </summary>
public sealed class ProgramDef
{
    public string Id = "";
    public string Name = "";
    public string Style = "concrete";
    public int WMin = 20, WMax = 28, DMin = 16, DMax = 20;
    public int FloorsMin = 1, FloorsMax = 1;   // storeys at and above ground (0: none)
    public int LevelsMin, LevelsMax;           // storeys below ground
    public string Surface = "full";            // full: the building stands; hut: only a stair house shows
    public float Lift;                         // chance of a lift core
    public string Roof = "flat";               // flat, access (stair to the roof), pad (landing pad), none
    /// <summary>The skyline: empty, or broken (the top torn off: girders and slab stubs stand ragged against the sky).</summary>
    public string Crown = "";
    public float Windows = 0.4f;
    public float Cavern;                       // chance a natural cave breaks into the deepest storey
    public float Breach;                       // chance of a collapse breach into the top basement
    public int SecretsMin, SecretsMax = 1;
    public float Collapse;                     // share of upper rooms whose floor has fallen through (ruined towers)
    public string Ground = "plain";            // what's around it: plain, snow, overgrown, scrap
    public string Stair = "switch";            // switch (a switchback stairwell) or ladder (a ladderway)
    public bool Cab;                           // the top storey is a glass cab (control towers)
    /// <summary>The type's layout grammar: footprint shapes, circulation patterns, basement outlines (weighted).</summary>
    public List<(string Key, float Weight)> Shapes = new(), Plans = new(), Basements = new();
    public float Setback;                      // chance an upper storey drops its wings
    public float SecondStair;                  // chance of a second stair core in a long building
    public List<(string Band, string Room)> Halls = new();
    public Dictionary<string, List<Demand>> Bands = new();
    public List<string> Fill = new();          // rooms used when a band asks for nothing more
    public List<string> Hazards = new();       // radiation, cold, toxic, unstable
    public List<string> Tags = new();
}

/// <summary>A part of a site plan: a building, a runway, a tank farm, a wreck...</summary>
public sealed class SitePart
{
    public string Kind = "";
    public int X, Z, Rot;
    public Dictionary<string, string> Args = new();
    public string Arg(string k, string d = "") => Args.TryGetValue(k, out var v) ? v : d;
    public int Int(string k, int d = 0) => Args.TryGetValue(k, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : d;
    public float Num(string k, float d = 0f) => Args.TryGetValue(k, out var v) ? float.Parse(v, CultureInfo.InvariantCulture) : d;
    public override string ToString() => $"{Kind} at {X},{Z}";

    /// <summary>
    /// This part as one spawn builds it: any whole-number argument written as a range ("count 3..6",
    /// "len 50..90") rolled from the spawn's own seed, so no two spawns of a site lay it out alike.
    /// </summary>
    public SitePart Rolled(Rng rng)
    {
        if (!Args.Values.Any(v => v.Contains(".."))) return this;
        var p = new SitePart { Kind = Kind, X = X, Z = Z, Rot = Rot };
        foreach (var (k, v) in Args.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            int at = v.IndexOf("..", StringComparison.Ordinal);
            if (at < 0) { p.Args[k] = v; continue; }
            int n = rng.Range(int.Parse(v[..at], CultureInfo.InvariantCulture), int.Parse(v[(at + 2)..], CultureInfo.InvariantCulture) + 1);
            if (k == "@x") p.X = n;
            else if (k == "@z") p.Z = n;
            else p.Args[k] = n.ToString(CultureInfo.InvariantCulture);
        }
        return p;
    }
}

/// <summary>
/// A site plan: a hand-authored landmark's parts and where they stand. Buildings inside it are
/// planned from their programs with the site's seed, so the landmark is always the same place.
/// </summary>
public sealed class SiteDef
{
    public string Id = "";
    public string Name = "";
    public string Style = "concrete";
    public int EntranceX, EntranceZ;
    public Dir EntranceFacing = Dir.West;
    public List<SitePart> Parts = new();
    public string Ground = "plain";
    public bool Space;
}

/// <summary>Reads rooms.txt, programs.txt and sites.txt. Unknown keywords fail with file and line.</summary>
public static class ArchParser
{
    static string[] Words(string line) => line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    static IEnumerable<(int Line, string[] Words)> Lines(string text)
    {
        string[] lines = text.Replace("\r", "").Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            string t = lines[n];
            int hash = t.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0) t = t[..hash];
            t = t.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            yield return (n + 1, Words(t));
        }
    }

    public static Dictionary<string, RoomDef> Rooms(string text, string source)
    {
        var rooms = new Dictionary<string, RoomDef>();
        RoomDef? r = null;
        foreach (var (line, p) in Lines(text))
        {
            FormatException Err(string m) => new($"{source}:{line}: {m}");
            RoomDef R() => r ?? throw Err("needs 'room <id>' first");
            switch (p[0])
            {
                case "room": r = new RoomDef { Id = p[1], Name = p[1] }; rooms[p[1]] = r; break;
                case "name": R().Name = string.Join(' ', p.Skip(1)); break;
                case "kind": R().Kind = Enum.Parse<ModuleKind>(p[1], true); break;
                case "width": R().WMin = I(p[1]); R().WMax = I(p[^1]); break;
                case "props":
                    for (int i = 1; i + 1 < p.Length; i += 2) R().Props.Add((p[i], F(p[i + 1])));
                    break;
                case "fill": R().Fill = F(p[1]); break;
                case "island": R().Island = p.Skip(1).ToList(); break;
                case "near": R().Near = p.Skip(1).ToList(); break;
                case "avoid": R().Avoid = p.Skip(1).ToList(); break;
                case "prefer": R().Prefer = p[1]; break;
                case "loot": R().LootMin = I(p[1]); R().LootMax = I(p[^1]); break;
                case "spawns": R().SpawnMin = I(p[1]); R().SpawnMax = I(p[^1]); break;
                case "terminals": R().TerminalMin = I(p[1]); R().TerminalMax = I(p[^1]); break;
                case "light": R().Light = p[1]; break;
                case "lock": R().Lock = p[1]; break;
                case "blast": R().Blast = true; break;
                case "tags": R().Tags = p.Skip(1).ToList(); break;
                case "story": R().Story = p.Skip(1).ToList(); break;
                case "deco": R().Deco = p[1]; break;
                default: throw Err($"unknown keyword '{p[0]}'");
            }
        }
        return rooms;
    }

    public static Dictionary<string, ProgramDef> Programs(string text, string source)
    {
        var programs = new Dictionary<string, ProgramDef>();
        ProgramDef? g = null;
        foreach (var (line, p) in Lines(text))
        {
            FormatException Err(string m) => new($"{source}:{line}: {m}");
            ProgramDef P() => g ?? throw Err("needs 'program <id>' first");
            switch (p[0])
            {
                case "program": g = new ProgramDef { Id = p[1], Name = p[1] }; programs[p[1]] = g; break;
                case "name": P().Name = string.Join(' ', p.Skip(1)); break;
                case "style": P().Style = p[1]; break;
                case "width": P().WMin = I(p[1]); P().WMax = I(p[^1]); break;
                case "depth": P().DMin = I(p[1]); P().DMax = I(p[^1]); break;
                case "floors": P().FloorsMin = I(p[1]); P().FloorsMax = I(p[^1]); break;
                case "levels": P().LevelsMin = I(p[1]); P().LevelsMax = I(p[^1]); break;
                case "surface": P().Surface = p[1]; break;
                case "lift": P().Lift = F(p[1]); break;
                case "roof": P().Roof = p[1]; break;
                case "crown": P().Crown = p[1]; break;
                case "windows": P().Windows = F(p[1]); break;
                case "cavern": P().Cavern = F(p[1]); break;
                case "breach": P().Breach = F(p[1]); break;
                case "secrets": P().SecretsMin = I(p[1]); P().SecretsMax = I(p[^1]); break;
                case "collapse": P().Collapse = F(p[1]); break;
                case "ground": P().Ground = p[1]; break;
                case "stair": P().Stair = p[1]; break;
                case "cab": P().Cab = true; break;
                case "shapes": P().Shapes = Weighted(p); break;
                case "plans": P().Plans = Weighted(p); break;
                case "basement": P().Basements = Weighted(p); break;
                case "setback": P().Setback = F(p[1]); break;
                case "stairs2": P().SecondStair = F(p[1]); break;
                case "hall": P().Halls.Add((p[1], p[2])); break;
                case "hazards": P().Hazards = p.Skip(1).ToList(); break;
                case "tags": P().Tags = p.Skip(1).ToList(); break;
                case "fill": P().Fill = p.Skip(1).ToList(); break;
                case "rooms":
                {
                    if (!P().Bands.TryGetValue(p[1], out var list)) P().Bands[p[1]] = list = new List<Demand>();
                    foreach (string token in p.Skip(2)) list.Add(ParseDemand(token));
                    break;
                }
                default: throw Err($"unknown keyword '{p[0]}'");
            }
        }
        return programs;
    }

    static List<(string, float)> Weighted(string[] p)
    {
        var list = new List<(string, float)>();
        for (int i = 1; i < p.Length; i++)
        {
            float w = i + 1 < p.Length && float.TryParse(p[i + 1], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 1f;
            list.Add((p[i], w));
            if (i + 1 < p.Length && float.TryParse(p[i + 1], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out _)) i++;
        }
        return list;
    }

    static Demand ParseDemand(string t)
    {
        bool must = t.EndsWith('!');
        if (must) t = t[..^1];
        int min = 1, max = 1;
        int star = t.IndexOf('*');
        if (star >= 0)
        {
            string[] r = t[(star + 1)..].Split('-');
            min = I(r[0]);
            max = I(r[^1]);
            t = t[..star];
        }
        bool optional = t.EndsWith('?');
        if (optional)
        {
            t = t[..^1];
            min = 0;
        }
        return new Demand(t, min, max, must);
    }

    public static Dictionary<string, SiteDef> Sites(string text, string source)
    {
        var sites = new Dictionary<string, SiteDef>();
        SiteDef? s = null;
        foreach (var (line, p) in Lines(text))
        {
            FormatException Err(string m) => new($"{source}:{line}: {m}");
            SiteDef S() => s ?? throw Err("needs 'site <id>' first");
            switch (p[0])
            {
                case "site": s = new SiteDef { Id = p[1], Name = p[1] }; sites[p[1]] = s; break;
                case "name": S().Name = string.Join(' ', p.Skip(1)); break;
                case "style": S().Style = p[1]; break;
                case "ground": S().Ground = p[1]; break;
                case "space": S().Space = true; break;
                case "entrance":
                    S().EntranceX = I(p[1]);
                    S().EntranceZ = I(p[2]);
                    S().EntranceFacing = Enum.Parse<Dir>(p[3], true);
                    break;
                case "part":
                {
                    // part <kind> <x> <z> [rot <n>] [key value]...
                    // (x or z written lo..hi stands where each spawn rolls it: kept as "@x"/"@z" until then.)
                    int Lo(string s) => I(s.Contains("..") ? s[..s.IndexOf("..", StringComparison.Ordinal)] : s);
                    var part = new SitePart { Kind = p[1], X = Lo(p[2]), Z = Lo(p[3]) };
                    if (p[2].Contains("..")) part.Args["@x"] = p[2];
                    if (p[3].Contains("..")) part.Args["@z"] = p[3];
                    for (int i = 4; i + 1 < p.Length; i += 2)
                    {
                        if (p[i] == "rot") part.Rot = I(p[i + 1]);
                        else part.Args[p[i]] = p[i + 1];
                    }
                    S().Parts.Add(part);
                    break;
                }
                default: throw Err($"unknown keyword '{p[0]}'");
            }
        }
        return sites;
    }
}
