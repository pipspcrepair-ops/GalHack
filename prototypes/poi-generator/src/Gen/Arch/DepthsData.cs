#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What one purpose asks of a place below ground (data/depths.txt): which shared plan parts it draws on and
/// how much, its sizes, the rooms each band of levels holds, its zones and the locks between them, and what a
/// century of neglect does to it. None of it is a layout: <see cref="Depths"/> rolls a plan from these rules,
/// and the place's builders (their tech and habits), its age and its fall tilt every weight.
/// </summary>
public sealed class DepthDef
{
    public string Id = "";
    public string Name = "";
    public int LevelsMin = 2, LevelsMax = 3, FirstMin = 6, FirstMax = 10, RockMin, RockMax = 1;
    public List<(string Key, float Weight)> Head = new(), Down = new(), Between = new();
    public float Stacked, Lift;
    public int MainW0 = 3, MainW1 = 4, MainH0 = 3, MainH1 = 3, SideW0 = 2, SideW1 = 2, SideH0 = 2, SideH1 = 2, RoomH0 = 2, RoomH1 = 2;
    /// <summary>The plan parts each band of levels draws on: top, upper, deep, deepest, any.</summary>
    public readonly Dictionary<string, List<(string Key, float Weight)>> Plans = new();
    /// <summary>A structure through every level (a silo's shaft, a mine's shaft) and the chance of it.</summary>
    public string Span = "";
    public float SpanChance;
    public readonly Dictionary<string, List<Demand>> Rooms = new();
    public List<string> Fill = new();
    public List<string> Zones = new();
    /// <summary>What parts one zone from the next: door, blast, airlock, decon.</summary>
    public string Lock = "door";
    public float Crawl, Blast, Raw;
    public int EndsMin, EndsMax = 1, SecretsMin, SecretsMax = 1;
    public bool Alien;
    public string Lining = "wall";
    /// <summary>What the years do to it: collapse, cave, squat, sealed; scaled by the world's age and how violent its fall was.</summary>
    public readonly Dictionary<string, float> Age = new();
    /// <summary>Something the purpose always has, whatever else is rolled: "ops" (an operations hall with a gallery over it).</summary>
    public string Feature = "";

    public IEnumerable<string> RoomIds => Rooms.Values.SelectMany(b => b).Select(d => d.Room).Concat(Fill);
}

/// <summary>Reads data/depths.txt. Unknown keywords fail with file and line.</summary>
public static class DepthParser
{
    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    public static Dictionary<string, DepthDef> Parse(string text, string source)
    {
        var all = new Dictionary<string, DepthDef>();
        DepthDef? d = null;
        string[] lines = text.Replace("\r", "").Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            string t = lines[n];
            int hash = t.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0) t = t[..hash];
            t = t.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            string[] w = t.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            FormatException Err(string m) => new($"{source}:{n + 1}: {m}");
            DepthDef D() => d ?? throw Err("needs 'depth <id>' first");
            List<(string, float)> Weighted(int from)
            {
                var list = new List<(string, float)>();
                for (int i = from; i < w.Length; i++)
                {
                    string key = w[i];
                    float wt = 1f;
                    if (i + 1 < w.Length && float.TryParse(w[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) { wt = parsed; i++; }
                    list.Add((key, wt));
                }
                return list;
            }
            try
            {
                switch (w[0])
                {
                    case "depth": d = new DepthDef { Id = w[1], Name = w[1] }; all[d.Id] = d; break;
                    case "name": D().Name = string.Join(' ', w.Skip(1)); break;
                    case "levels": D().LevelsMin = I(w[1]); D().LevelsMax = I(w[2]); break;
                    case "first": D().FirstMin = I(w[1]); D().FirstMax = I(w[2]); break;
                    case "rock": D().RockMin = I(w[1]); D().RockMax = I(w[2]); break;
                    case "head": D().Head = Weighted(1); break;
                    case "down": D().Down = Weighted(1); break;
                    case "between": D().Between = Weighted(1); break;
                    case "stacked": D().Stacked = F(w[1]); break;
                    case "lift": D().Lift = F(w[1]); break;
                    case "main": D().MainW0 = I(w[1]); D().MainW1 = I(w[2]); D().MainH0 = I(w[3]); D().MainH1 = I(w[4]); break;
                    case "side": D().SideW0 = I(w[1]); D().SideW1 = I(w[2]); D().SideH0 = I(w[3]); D().SideH1 = I(w[4]); break;
                    case "room": D().RoomH0 = I(w[1]); D().RoomH1 = I(w[2]); break;
                    case "plan": D().Plans[w[1]] = Weighted(2); break;
                    case "span": D().Span = w[1]; D().SpanChance = F(w[2]); break;
                    case "rooms":
                    {
                        var list = new List<Demand>();
                        foreach (string token in w.Skip(2))
                        {
                            string id = token;
                            bool must = false, optional = false;
                            int min = 1, max = 1;
                            if (id.EndsWith('?')) { optional = true; id = id[..^1]; }
                            if (id.EndsWith('!')) { must = true; id = id[..^1]; }
                            int star = id.IndexOf('*');
                            if (star >= 0)
                            {
                                string[] r = id[(star + 1)..].Split('-');
                                min = I(r[0]);
                                max = r.Length > 1 ? I(r[1]) : min;
                                id = id[..star];
                            }
                            if (optional) min = 0;
                            list.Add(new Demand(id, min, max, must));
                        }
                        D().Rooms[w[1]] = list;
                        break;
                    }
                    case "fill": D().Fill = w.Skip(1).ToList(); break;
                    case "zones": D().Zones = w.Skip(1).ToList(); break;
                    case "lock": D().Lock = w[1]; break;
                    case "crawl": D().Crawl = F(w[1]); break;
                    case "blast": D().Blast = F(w[1]); break;
                    case "raw": D().Raw = F(w[1]); break;
                    case "ends": D().EndsMin = I(w[1]); D().EndsMax = I(w[2]); break;
                    case "secrets": D().SecretsMin = I(w[1]); D().SecretsMax = I(w[2]); break;
                    case "alien": D().Alien = true; break;
                    case "lining": D().Lining = w[1]; break;
                    case "feature": D().Feature = w[1]; break;
                    case "age":
                        for (int i = 1; i + 1 < w.Length; i += 2) D().Age[w[i]] = F(w[i + 1]);
                        break;
                    default: throw Err($"unknown keyword '{w[0]}'");
                }
            }
            catch (Exception e) when (e is IndexOutOfRangeException || e is FormatException && !e.Message.StartsWith(source, StringComparison.Ordinal))
            {
                throw Err($"bad line '{t}': {e.Message}");
            }
        }
        return all;
    }
}
