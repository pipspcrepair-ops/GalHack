#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>Which table of data/world-layers.txt a record belongs to (docs/world-layers.md, section 2). The last few are rules and records rather than layers of a world.</summary>
public enum LayerKind : byte
{
    Zone, Type, Air, Climate, Land, Mod, Pre, Post, Port, People, Trade, Ran, Phase, Age, Fall, Moment, Theme, Build, Infra, Running,
    Presence, Creed, Occ, Feature, Hazard, Flora, Fauna, Sky, Weather, Palette, Sound, Marks, Dress, Rule, Clash, Fam, Old,
}

/// <summary>A condition on a world's facts: fact=a,b or fact!=a,b. A fact not rolled yet blocks nothing.</summary>
public readonly record struct LayerReq(string Fact, bool In, string[] Values)
{
    public bool Holds(IReadOnlyDictionary<string, string> facts) => !facts.TryGetValue(Fact, out string? have) || Values.Contains(have) == In;
}

/// <summary>like layer.id*N: multiplies that value's odds on later rolls; under 1 it's shunned.</summary>
public readonly record struct LayerLike(LayerKind Layer, string Id, float By);

/// <summary>One record of data/world-layers.txt: a value of a layer, or a rule. A record, never code.</summary>
public sealed class WorldLayer
{
    public LayerKind Kind;
    public string Id = "", Name = "";
    public float Weight = 1f, Chance = 1f, Break = -1f, Settle = 1f;
    public int Streets;
    public bool Start, Blank, Soft;
    public string? Group, Speed, Base, Arch, Kit, Net, Stamp;
    /// <summary>What a value is made of: each word has a rule. On a clash, the second word.</summary>
    public List<string> Is = new(), Needs = new(), Tags = new(), Orbit = new();
    /// <summary>On an old record: the parts its theme broke into, as layer.id, so it can be forced onto a roll.</summary>
    public List<string> Parts = new();
    public readonly List<LayerReq> Req = new();
    public readonly List<LayerLike> Like = new();
    public readonly List<(LayerKind Layer, string Tag)> Only = new();
    public readonly List<(string Fact, string Value)> Sets = new();
    /// <summary>On a world type: what it allows, by fact (air, temp, water, life, land).</summary>
    public readonly Dictionary<string, List<string>> Allows = new();
    public readonly Dictionary<string, float> Regions = new(), Hub = new(), Industry = new(), Plant = new(), Farm = new(), Poi = new(), Occupancy = new();
    public readonly Dictionary<string, string> Style = new();
    public string? Depot;
    public float[]? States;

    public bool Has(string tag) => Is.Contains(tag) || Group != null && tag == "group." + Group;
}

public static class LayerParser
{
    static readonly Dictionary<string, LayerKind> Kinds = Enum.GetValues<LayerKind>().ToDictionary(k => k.ToString().ToLowerInvariant());

    /// <summary>Plain words for people reading the data, or links to other documents: the game has no use for them.</summary>
    static readonly HashSet<string> ForReaders = new() { "identity", "why", "shows", "keeps", "was", "zones", "new", "n", "to", "wear", "list", "dress" };

    public static List<WorldLayer> Parse(string text, string source)
    {
        var layers = new List<WorldLayer>();
        int n = 0;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            n++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            FormatException Err(string m) => new($"{source}:{n}: {m}");
            string[] fields = line.Split(" | ");
            string[] head = fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (head.Length < 2 || !Kinds.TryGetValue(head[0], out LayerKind kind)) throw Err($"'{fields[0]}' isn't a record");
            var l = new WorldLayer { Id = head[1], Kind = kind };
            l.Is.AddRange(head.Skip(2));
            foreach (string field in fields.Skip(1))
            {
                string[] p = field.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length > 0) Field(l, p, field, Err);
            }
            layers.Add(l);
        }
        return layers;
    }

    static float Num(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    static void Field(WorldLayer l, string[] p, string field, Func<string, FormatException> err)
    {
        void Table(Dictionary<string, float> t)
        {
            for (int i = 1; i + 1 < p.Length; i += 2) t[p[i]] = Num(p[i + 1]);
        }
        string One() => p.Length > 1 ? p[1] : throw err($"'{p[0]}' needs a value");
        switch (p[0])
        {
            case "name": l.Name = field["name".Length..].Trim(); break;
            case "weight": l.Weight = Num(One()); break;
            case "chance": l.Chance = Num(One()); break;
            case "break": l.Break = Num(One()); break;
            case "settle": l.Settle = Num(One()); break;
            case "streets": l.Streets = int.Parse(One(), CultureInfo.InvariantCulture); break;
            case "start": l.Start = One() == "yes"; break;
            case "blank": l.Blank = One() == "yes"; break;
            case "soft": l.Soft = One() == "yes"; break;
            case "group": l.Group = One(); break;
            case "speed": l.Speed = One(); break;
            case "base": l.Base = One(); break;
            case "arch": l.Arch = One(); break;
            case "kit": l.Kit = One(); break;
            case "net": l.Net = One(); break;
            case "stamp": l.Stamp = One(); break;
            case "depot": l.Depot = One(); break;
            case "is": l.Is.AddRange(p.Skip(1)); break;
            case "needs": l.Needs.AddRange(p.Skip(1)); break;
            case "tags": l.Tags.AddRange(p.Skip(1)); break;
            case "orbit": l.Orbit.AddRange(p.Skip(1)); break;
            case "parts": l.Parts.AddRange(p.Skip(1)); break;
            case "air" or "temp" or "water" or "life" or "land": l.Allows[p[0]] = p.Skip(1).ToList(); break;
            case "req":
                foreach (string q in p.Skip(1))
                {
                    int eq = q.IndexOf('=');
                    if (eq < 1) throw err($"'{q}' isn't a condition");
                    bool not = q[eq - 1] == '!';
                    l.Req.Add(new LayerReq(q[..(not ? eq - 1 : eq)], !not, q[(eq + 1)..].Split(',')));
                }
                break;
            case "like":
                foreach (string q in p.Skip(1))
                {
                    string[] a = q.Split('.', 2), b = a.Length == 2 ? a[1].Split('*') : Array.Empty<string>();
                    if (b.Length != 2 || !Kinds.TryGetValue(a[0], out LayerKind k)) throw err($"'{q}' isn't a like");
                    l.Like.Add(new LayerLike(k, b[0], Num(b[1])));
                }
                break;
            case "only":
                foreach (string q in p.Skip(1))
                {
                    string[] a = q.Split('.', 2);
                    if (a.Length != 2 || !Kinds.TryGetValue(a[0], out LayerKind k)) throw err($"'{q}' isn't an only");
                    l.Only.Add((k, a[1]));
                }
                break;
            case "sets":
                foreach (string q in p.Skip(1))
                {
                    string[] a = q.Split('=', 2);
                    if (a.Length != 2) throw err($"'{q}' isn't a sets");
                    l.Sets.Add((a[0], a[1]));
                }
                break;
            case "region": Table(l.Regions); break;
            case "hub": Table(l.Hub); break;
            case "industry": Table(l.Industry); break;
            case "plant": Table(l.Plant); break;
            case "farm": Table(l.Farm); break;
            case "poi": Table(l.Poi); break;
            case "occ": Table(l.Occupancy); break;
            case "style":
                for (int i = 1; i + 1 < p.Length; i += 2) l.Style[p[i]] = p[i + 1];
                break;
            case "state":
                l.States = new float[4];
                for (int i = 1; i + 1 < p.Length; i += 2) l.States[(int)Enum.Parse<PoiState>(p[i], true)] = Num(p[i + 1]);
                break;
            default:
                // An old record is a note of where a theme went, and may end in plain words.
                if (l.Kind != LayerKind.Old && !ForReaders.Contains(p[0])) throw err($"unknown keyword '{p[0]}'");
                break;
        }
    }
}

/// <summary>
/// What one world rolled, layer by layer, in the order of docs/world-layers.md section 8.2, and the tables
/// that adds up to. Deterministic from the world's seed, and each layer forks the seed by its own name, so
/// a new layer leaves the rest of a world alone. Nothing in the generators names a value: they read these
/// tables, so a world is whatever its layers add up to. No name here is ever shown to a player.
/// </summary>
public sealed class WorldRoll
{
    /// <summary>What's already decided before the roll, as the planet has it or a test forces it: layer to ids. Anything left out is rolled.</summary>
    public sealed class Given
    {
        public readonly Dictionary<LayerKind, List<string>> Values = new();
        public Given Set(LayerKind kind, params string[] ids)
        {
            Values[kind] = ids.ToList();
            return this;
        }
        public string? First(LayerKind kind) => Values.TryGetValue(kind, out var v) && v.Count > 0 ? v[0] : null;
        public List<string> All(LayerKind kind) => Values.GetValueOrDefault(kind) ?? new List<string>();
    }

    public WorldLayer? Type;
    public WorldLayer Theme = null!;
    public readonly List<WorldLayer> Features = new(), Presences = new();
    /// <summary>Everything rolled, in the order it was rolled, so later layers win where one value does.</summary>
    public readonly List<WorldLayer> Rolled = new();
    public readonly Dictionary<string, string> Facts = new();
    /// <summary>Soft rules this world broke: the one world in 25 that holds something rare, kept so a survey can show it.</summary>
    public readonly List<string> Breaks = new();
    /// <summary>What the orbit layer reads off this world (section 8.5).</summary>
    public readonly List<string> Orbit = new();
    /// <summary>The words of a family's <c>is</c> line this world rules out (a family that's <c>open</c> on a world built sealed).</summary>
    public readonly HashSet<string> Barred = new();

    public readonly Dictionary<string, float> Regions = new(), Hub = new(), Industry = new(), Plant = new(), Farm = new(), Poi = new(), Occupancy = new();
    public readonly List<string> Needs = new(), Tags = new();
    public string? Depot;
    public float[]? States;
    /// <summary>The share of a world's places that break from the theme.</summary>
    public float Break = 0.1f;
    /// <summary>Streets of houses a town gets: every layer's add up (two where none says).</summary>
    public int Streets = 2;
    public string Road = "quiet", Rail = "none", Power = "intact";

    public IEnumerable<WorldLayer> Layers => Rolled;

    public string? Fact(string name) => Facts.GetValueOrDefault(name);
    public WorldLayer? One(LayerKind kind) => Rolled.FirstOrDefault(l => l.Kind == kind);
    public IEnumerable<WorldLayer> All(LayerKind kind) => Rolled.Where(l => l.Kind == kind);

    public int RegionCount(string kind) => (int)MathF.Round(Regions.GetValueOrDefault(kind));

    public override string ToString() => string.Join(" + ", Rolled.Select(l => $"{l.Kind.ToString().ToLowerInvariant()}.{l.Id}"));

    static string Band(TechLevel t) => t switch { TechLevel.Low => "L", TechLevel.High => "H", _ => "M" };

    static string PortCode(Starport s) => s == Starport.None ? "0" : s.ToString();

    static string PeopleCode(PopulationNow p) => p.ToString().ToLowerInvariant();

    /// <summary>A roll in progress: the world so far, and what the rules say of each value.</summary>
    sealed class Roller
    {
        readonly PoiLibrary _lib;
        readonly WorldRoll _w;
        readonly Given _given;
        readonly Dictionary<string, WorldLayer> _rules = new();
        readonly List<(string A, string B)> _clashes = new();
        readonly HashSet<string> _tags = new();
        readonly List<(LayerKind Layer, string Tag)> _only = new();
        readonly ulong _seed;
        int _breakLeft;

        public Roller(PoiLibrary lib, WorldRoll w, ulong seed, Given given)
        {
            _lib = lib;
            _w = w;
            _seed = seed;
            _given = given;
            foreach (WorldLayer l in lib.Layers)
                if (l.Kind == LayerKind.Rule) _rules[l.Id] = l;
                else if (l.Kind == LayerKind.Clash && l.Is.Count > 0) _clashes.Add((l.Id, l.Is[0]));
            _breakLeft = Fork("break").Chance(0.04f) ? 1 : 0;
        }

        public Rng Fork(string name) => new(Hash.Combine(_seed, Hash.Of(name)));

        IEnumerable<WorldLayer> Table(LayerKind k) => _lib.Layers.Where(l => l.Kind == k);

        public void Add(WorldLayer l)
        {
            _w.Rolled.Add(l);
            _tags.UnionWith(l.Is);
            if (l.Group != null) _tags.Add("group." + l.Group);
            if (l.Kind is LayerKind.Zone or LayerKind.Type or LayerKind.Pre or LayerKind.Post or LayerKind.Port or LayerKind.People or LayerKind.Phase or LayerKind.Age)
                _w.Facts[l.Kind.ToString().ToLowerInvariant()] = l.Id;
            if (l.Speed != null) _w.Facts["speed"] = l.Speed;
            foreach (var (fact, value) in l.Sets) _w.Facts[fact] = value;
            _only.AddRange(l.Only);
        }

        public void OnlyTheme(string tag) => _only.Add((LayerKind.Theme, tag));

        /// <summary>Whether a value holds under the hard rules, and under the soft ones too.</summary>
        public (bool Hard, bool Soft) Rules(WorldLayer l, bool clashes = true)
        {
            bool hard = l.Req.All(r => r.Holds(_w.Facts)), soft = true;
            foreach (string word in l.Is)
            {
                if (!_rules.TryGetValue(word, out WorldLayer? rule) || rule.Req.All(r => r.Holds(_w.Facts))) continue;
                if (rule.Soft) soft = false;
                else hard = false;
            }
            if (clashes)
                foreach (var (a, b) in _clashes)
                    if (l.Is.Contains(a) && _tags.Contains(b) || l.Is.Contains(b) && _tags.Contains(a)) soft = false;
            return (hard, soft);
        }

        (bool Hard, bool Soft) Legal(WorldLayer l)
        {
            if (_only.Any(o => o.Layer == l.Kind && !l.Has(o.Tag))) return (false, false);
            if (_w.Type is { } t)
            {
                if (l.Kind == LayerKind.Air && t.Allows.TryGetValue("air", out var airs) && !airs.Contains(l.Id)) return (false, false);
                if (l.Kind == LayerKind.Land && t.Allows.TryGetValue("land", out var lands) && !lands.Contains(l.Id)) return (false, false);
                if (l.Kind == LayerKind.Climate && !l.Sets.All(s => !t.Allows.TryGetValue(s.Fact, out var ok) || ok.Contains(s.Value))) return (false, false);
            }
            if (l.Kind == LayerKind.Type && _w.Fact("start") == "yes" && !l.Start) return (false, false);
            return Rules(l);
        }

        float Odds(WorldLayer l)
        {
            float w = l.Weight;
            foreach (WorldLayer r in _w.Rolled)
                foreach (LayerLike like in r.Like)
                    if (like.Layer == l.Kind && like.Id == l.Id) w *= like.By;
            // A theme fits by how well it suits what has already been rolled.
            if (l.Kind == LayerKind.Theme)
                foreach (LayerLike like in l.Like)
                    if (_w.Rolled.Any(r => r.Kind == like.Layer && r.Id == like.Id)) w *= like.By;
            return Math.Max(w, 1e-6f);
        }

        /// <summary>One value of a layer: what fits, by its odds; the rare world breaks a soft rule. Null where nothing can stand.</summary>
        public WorldLayer? Pick(LayerKind kind, string fork, Func<WorldLayer, bool>? where = null)
        {
            var pool = new List<WorldLayer>();
            var rare = new List<WorldLayer>();
            foreach (WorldLayer l in Table(kind))
            {
                if (where != null && !where(l)) continue;
                var (hard, soft) = Legal(l);
                if (hard && soft) pool.Add(l);
                else if (hard) rare.Add(l);
            }
            Rng r = Fork(fork);
            if (_breakLeft > 0 && rare.Count > 0 && r.Chance(0.25f))
            {
                _breakLeft--;
                WorldLayer broke = r.Pick(rare);
                _w.Breaks.Add($"{kind.ToString().ToLowerInvariant()}.{broke.Id}");
                return broke;
            }
            if (pool.Count == 0) return null;
            return pool[Math.Max(0, r.PickWeighted(pool.Select(Odds).ToList()))];
        }

        /// <summary>A layer of one value: what's given, or a roll, narrowed by <paramref name="where"/> if anything passes.</summary>
        public bool One(LayerKind kind, Func<WorldLayer, bool>? where = null)
        {
            if (_given.First(kind) is { } id)
            {
                Take(kind, id);
                return true;
            }
            WorldLayer? l = (where != null ? Pick(kind, kind.ToString(), where) : null) ?? Pick(kind, kind.ToString());
            if (l != null) Add(l);
            return l != null;
        }

        /// <summary>Takes a value already decided, whatever the rules would have said.</summary>
        public void Take(LayerKind kind, string id)
        {
            if (Table(kind).FirstOrDefault(v => v.Id == id) is { } l) Add(l);
        }

        /// <summary>A layer of several values: what's given, then a roll for the rest.</summary>
        public List<WorldLayer> Many(LayerKind kind, int lo, int hi, float chance = 1f)
        {
            Rng r = Fork(kind + ".count");
            int n = r.Between(lo, hi);
            if (!r.Chance(chance)) n = lo;
            var got = new List<WorldLayer>();
            foreach (string id in _given.All(kind))
                if (Table(kind).FirstOrDefault(v => v.Id == id) is { } l)
                {
                    got.Add(l);
                    Add(l);
                }
            while (got.Count < n)
            {
                WorldLayer? l = Pick(kind, $"{kind}.{got.Count}", v => !got.Contains(v));
                if (l == null) break;
                got.Add(l);
                Add(l);
            }
            return got;
        }

        /// <summary>The words of a family's <c>is</c> line this world's facts rule out.</summary>
        public void Bars()
        {
            foreach (var (word, rule) in _rules)
                if (!rule.Soft && !rule.Req.All(r => r.Holds(_w.Facts))) _w.Barred.Add(word);
        }
    }

    /// <summary>
    /// Rolls a world from its seed, in the chain's order. The planet says what it already is through
    /// <paramref name="given"/>, and the profile gives the tech bands, port, people and trade, so the sky, the
    /// ground and the tables can't disagree. A family's own tech levels and <see cref="WorldProfile.Fits"/>
    /// still apply on top. Null where the library has no themes.
    /// </summary>
    public static WorldRoll? Roll(PoiLibrary lib, ulong seed, string archetype, WorldProfile profile, bool start, Given? given = null)
    {
        if (!lib.Layers.Any(l => l.Kind == LayerKind.Theme)) return null;
        given ??= new Given();
        var roll = new WorldRoll();
        var w = new Roller(lib, roll, Hash.Combine(seed, 0x7EEEA5UL), given);
        roll.Facts["start"] = start ? "yes" : "no";
        string a = archetype.ToLowerInvariant() + ".";

        // The body.
        w.One(LayerKind.Zone);
        w.One(LayerKind.Type, t => t.Arch != null && t.Arch.StartsWith(a, StringComparison.Ordinal));
        roll.Type = roll.One(LayerKind.Type);
        w.One(LayerKind.Air);
        w.One(LayerKind.Climate);
        roll.Facts["life"] = w.Fork("life").Pick(roll.Type?.Allows.GetValueOrDefault("life") ?? new List<string> { "mid" });
        w.Many(LayerKind.Land, 2, 2);

        if (profile.Inhabited)
        {
            roll.Facts["settled"] = "yes";
            // The profile, as the planet's old story has it.
            w.Take(LayerKind.Pre, Band(profile.Before));
            w.Take(LayerKind.Post, Band(profile.Now));
            int fell = (int)profile.Before - (int)profile.Now;
            roll.Facts["drop"] = fell >= 0 ? fell.ToString(CultureInfo.InvariantCulture) : "up";
            w.Take(LayerKind.Port, PortCode(profile.Starport));
            w.Take(LayerKind.People, PeopleCode(profile.Current));
            string trade = profile.Trade.ToString();
            w.One(LayerKind.Trade, t => t.Base == trade);
            w.One(LayerKind.Ran);
            // The fall. An exodus world's profile is its fall: the two can't disagree.
            w.One(LayerKind.Phase);
            w.One(LayerKind.Age);
            w.One(LayerKind.Fall, profile.Exodus ? f => f.Id is "exodus" or "decreed_exodus" : null);
            w.Many(LayerKind.Mod, 0, 2, 0.6f);
            w.One(LayerKind.Moment);
            WorldLayer? fall = roll.One(LayerKind.Fall);
            if (w.Fork("scar").Chance(0.33f) && w.Pick(LayerKind.Fall, "scar.pick", f => f != fall) is { } scar) w.Add(scar);
            // The mood, then the old world's look.
            w.One(LayerKind.Theme);
            roll.Theme = roll.One(LayerKind.Theme)!;
            if (w.Fork("second").Chance(0.25f) && w.Pick(LayerKind.Theme, "second.pick", t => t != roll.Theme && !t.Blank) is { } second) w.Add(second);
            w.One(LayerKind.Build);
            w.One(LayerKind.Infra);
            w.One(LayerKind.Running);
        }
        else
        {
            roll.Facts["settled"] = "no";
            foreach (var (fact, value) in new[] { ("pre", "none"), ("post", "none"), ("port", "0"), ("people", "none"), ("age", "none"), ("drop", "0") }) roll.Facts[fact] = value;
            w.Take(LayerKind.Phase, "untouched");
            w.OnlyTheme("wild");
            w.One(LayerKind.Theme);
            roll.Theme = roll.One(LayerKind.Theme)!;
            w.Many(LayerKind.Mod, 0, 2, 0.6f);
        }

        // The present.
        roll.Features.AddRange(w.Many(LayerKind.Feature, 1, 3));
        roll.Presences.AddRange(w.Many(LayerKind.Presence, 0, 2, 0.75f));
        if (profile.Inhabited) w.One(LayerKind.Creed);
        w.Many(LayerKind.Hazard, 1, 3);
        // The surface.
        foreach (LayerKind k in new[] { LayerKind.Flora, LayerKind.Fauna, LayerKind.Sky, LayerKind.Weather, LayerKind.Palette, LayerKind.Sound, LayerKind.Marks }) w.One(k);

        w.Bars();
        roll.Combine(lib, w);
        return roll;
    }

    /// <summary>Adds the layers' tables together. Families the library doesn't have (yet) are left out, and occupants the facts forbid.</summary>
    void Combine(PoiLibrary lib, Roller w)
    {
        static void Add(Dictionary<string, float> to, Dictionary<string, float> from)
        {
            foreach (var (k, v) in from) to[k] = to.GetValueOrDefault(k) + v;
        }
        foreach (WorldLayer l in Rolled)
        {
            Add(Regions, l.Regions);
            Add(Hub, l.Hub);
            Add(Industry, l.Industry);
            Add(Plant, l.Plant);
            Add(Farm, l.Farm);
            Add(Poi, l.Poi);
            Add(Occupancy, l.Occupancy);
            Needs.AddRange(l.Needs.Where(n => !Needs.Contains(n)));
            Tags.AddRange(l.Tags.Where(t => !Tags.Contains(t)));
            Orbit.AddRange(l.Orbit.Where(t => !Orbit.Contains(t)));
            Depot = l.Depot ?? Depot;
            States = l.States ?? States;
            if (l.Break >= 0f) Break = l.Break;
            if (l.Style.TryGetValue("road", out string? road)) Road = road;
            if (l.Style.TryGetValue("rail", out string? rail)) Rail = rail;
            if (l.Style.TryGetValue("power", out string? power)) Power = power;
        }
        if (Rolled.Any(l => l.Streets > 0)) Streets = Rolled.Sum(l => l.Streets);
        var occupants = lib.Layers.Where(l => l.Kind == LayerKind.Occ).ToDictionary(l => l.Id);
        foreach (string code in Occupancy.Keys.ToList())
            if (Occupancy[code] <= 0f || !occupants.TryGetValue(code, out WorldLayer? occ) || w.Rules(occ, false) is not (true, true)) Occupancy.Remove(code);
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
