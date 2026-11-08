#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>The weather a house was built against: it picks steep roofs, stilts, thick walls or courts.</summary>
public enum Climate : byte { Temperate, Cold, Hot, Wet }

/// <summary>
/// What a street or a city tells its houses: whose culture built them (a seed every house on it shares), the
/// weather they stand in and how well off the street was (0 poor to 1 rich). Family: a house family to build
/// whatever the culture would pick (views and proof frames), its parts still rolled by the rules.
/// </summary>
public sealed record HomeHints(int Culture, Climate Climate = Climate.Temperate, float Wealth = 0.5f, string? Family = null)
{
    /// <summary>The climate from a world map's temperature and humidity (both 0 to 1).</summary>
    public static Climate ClimateOf(float temperature, float humidity) =>
        temperature < 0.33f ? Climate.Cold
        : temperature > 0.62f && humidity < 0.42f ? Climate.Hot
        : temperature > 0.5f && humidity > 0.6f ? Climate.Wet
        : Climate.Temperate;
}

/// <summary>Where a power line's service drop fixes to a house: the bracket's cell (outside the wall or up a mast), the way it faces (toward the street).</summary>
public readonly record struct ServicePoint(Int3 Cell, Dir Facing);

/// <summary>What one generated house turned out as, for the tests: its family, the shape you see from the street, and its garage door.</summary>
public sealed record HomeNote(string Family, string Silhouette, int Storeys, string Roof, string Garage, string GarageDoor, int DoorWide, int DoorTall, Int3 DoorCell, Dir DoorFacing, bool Driveway);

/// <summary>One house family of data/homes.txt: weights over the parts a house is built from.</summary>
public sealed class HouseFamily
{
    public string Id = "", Name = "";
    public HashSet<Climate> Climates = new();
    public HashSet<TechLevel> Techs = new();
    public string Era = "any";
    public float Weight = 1;
    public bool Odd;
    public readonly Dictionary<string, List<(string Value, float Weight)>> Choices = new();
    public readonly Dictionary<string, float> Chances = new();
    public readonly Dictionary<string, (int Min, int Max)> Sizes = new();

    public string Pick(string key, Rng r, string fallback, Func<string, bool>? allowed = null)
    {
        if (!Choices.TryGetValue(key, out var list) || list.Count == 0) return fallback;
        var ok = allowed == null ? list : list.Where(c => allowed(c.Value)).ToList();
        if (ok.Count == 0) return fallback;
        return ok[r.PickWeighted(ok.Select(c => c.Weight).ToList())].Value;
    }

    public bool Allows(string key, string value) => Choices.TryGetValue(key, out var list) && list.Any(c => c.Value == value);
    public float Chance(string key) => Chances.GetValueOrDefault(key);
    public (int Min, int Max) Size(string key, (int, int) fallback) => Sizes.GetValueOrDefault(key, fallback);
}

/// <summary>Every house family, read from data/homes.txt.</summary>
public sealed class HomeBook
{
    public readonly List<HouseFamily> Families = new();

    /// <summary>The book the library last loaded: the house rules read it wherever they are drawn.</summary>
    public static HomeBook Current = new();

    public HouseFamily? Find(string id) => Families.FirstOrDefault(f => f.Id == id);

    public static HomeBook Parse(string text, string name)
    {
        var book = new HomeBook();
        HouseFamily? f = null;
        int line = 0;
        foreach (string raw in text.Split('\n'))
        {
            line++;
            string s = raw.Split('#')[0].Trim();
            if (s.Length == 0) continue;
            string[] p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            FormatException Error(string why) => new($"{name}:{line}: {why}");
            if (p[0] == "family")
            {
                f = new HouseFamily { Id = p[1], Name = p[1] };
                book.Families.Add(f);
                continue;
            }
            if (f == null) throw Error("a line before any family");
            switch (p[0])
            {
                case "name": f.Name = string.Join(' ', p.Skip(1)); break;
                case "climate":
                    foreach (string c in p.Skip(1))
                        if (c == "any") foreach (Climate k in Enum.GetValues<Climate>()) f.Climates.Add(k);
                        else f.Climates.Add(Enum.Parse<Climate>(c, true));
                    break;
                case "tech": foreach (string t in p.Skip(1)) f.Techs.Add(WorldProfile.Parse(t)); break;
                case "era": f.Era = p[1]; break;
                case "weight": f.Weight = float.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "odd": f.Odd = p[1] == "yes"; break;
                case "width" or "depth" or "units" or "unit":
                    f.Sizes[p[0]] = (int.Parse(p[1], CultureInfo.InvariantCulture), int.Parse(p[2], CultureInfo.InvariantCulture));
                    break;
                default:
                    if (p.Length == 2 && !p[1].Contains(':') && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float chance)) { f.Chances[p[0]] = chance; break; }
                    var list = new List<(string, float)>();
                    foreach (string c in p.Skip(1))
                    {
                        string[] kv = c.Split(':');
                        list.Add((kv[0], kv.Length > 1 ? float.Parse(kv[1], CultureInfo.InvariantCulture) : 1f));
                    }
                    f.Choices[p[0]] = list;
                    break;
            }
        }
        foreach (HouseFamily h in book.Families)
        {
            if (h.Climates.Count == 0) foreach (Climate k in Enum.GetValues<Climate>()) h.Climates.Add(k);
            if (h.Techs.Count == 0) foreach (TechLevel t in Enum.GetValues<TechLevel>()) h.Techs.Add(t);
        }
        Current = book;
        return book;
    }
}

/// <summary>
/// A culture's way of building, shared by every house on its streets: two or three families it builds in
/// (one it builds most), the materials it favours and its paints. A house takes its family from the culture and
/// each part from the family, the favoured material more often than not, so no two neighbours are one model
/// but the street is one place.
/// </summary>
public sealed class HomeCulture
{
    public readonly List<(HouseFamily Family, float Weight)> Families = new();
    public readonly List<HouseFamily> Odd = new();
    public readonly Dictionary<string, string> Favoured = new();
    public Rgb[] Render = Array.Empty<Rgb>(), Paint = Array.Empty<Rgb>(), Brick = Array.Empty<Rgb>(), Stone = Array.Empty<Rgb>(), Wood = Array.Empty<Rgb>(), Trim = Array.Empty<Rgb>();
    public Rgb Tile, Tile2, Slate, Metal, Metal2, Thatch, Shingle, Glazed;
    public Climate Climate;
    public TechLevel Tech;
    public float Wealth;
    public int Seed;

    static readonly Dictionary<(int, Climate, TechLevel, int), HomeCulture> Cache = new();

    public static HomeCulture Of(HomeBook book, HomeHints hints, TechLevel tech)
    {
        var key = (hints.Culture, hints.Climate, tech, (int)(hints.Wealth * 8));
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var c)) return c;
            c = Make(book, hints, tech);
            if (Cache.Count > 4096) Cache.Clear();
            Cache[key] = c;
            return c;
        }
    }

    static HomeCulture Make(HomeBook book, HomeHints hints, TechLevel tech)
    {
        var r = new Rng(Hash.Combine((ulong)(uint)hints.Culture, 0xC0175UL + (ulong)hints.Climate * 31 + (ulong)tech));
        var c = new HomeCulture { Climate = hints.Climate, Tech = tech, Wealth = hints.Wealth, Seed = hints.Culture };
        float age = r.Float();
        float Weight(HouseFamily f) => f.Weight * (f.Era == "old" ? 0.4f + 1.2f * age : f.Era == "new" ? 1.6f - 1.2f * age : 1f);
        var fit = book.Families.Where(f => !f.Odd && f.Techs.Contains(tech) && f.Climates.Contains(hints.Climate)).ToList();
        if (fit.Count == 0) fit = book.Families.Where(f => !f.Odd && f.Techs.Contains(tech)).ToList();
        if (fit.Count == 0) fit = book.Families.Where(f => !f.Odd).ToList();
        // A main family, and one or two more it builds less of.
        int n = Math.Min(fit.Count, 2 + (r.Chance(0.5f) ? 1 : 0));
        var order = r.WeightedOrder(fit, Weight);
        for (int i = 0; i < n; i++) c.Families.Add((order[i], i == 0 ? 3f : 1.2f));
        c.Odd.AddRange(book.Families.Where(f => f.Odd && f.Techs.Contains(tech)));
        // The materials it favours, among what its families build in.
        foreach (string key in new[] { "wall", "cover", "fence", "door", "window" })
        {
            var all = c.Families.SelectMany(f => f.Family.Choices.GetValueOrDefault(key) ?? new()).Select(x => x.Value).Distinct().ToList();
            if (all.Count > 0) c.Favoured[key] = r.Pick(all);
        }
        // Paints: a hue the street keeps to, in a few shades.
        Rgb[] Shades(Rgb[] from, int k) => r.WeightedOrder(from, _ => 1f).Take(k).ToArray();
        c.Render = Shades(hints.Climate switch
        {
            Climate.Hot => new Rgb[] { new(0xe8, 0xe2, 0xd4), new(0xe0, 0xc8, 0x9c), new(0xd4, 0xa8, 0x78), new(0xf0, 0xec, 0xe4), new(0xc8, 0x8c, 0x64), new(0xd8, 0xbc, 0x8c) },
            Climate.Wet => new Rgb[] { new(0xe4, 0xd8, 0xa8), new(0xa8, 0xc8, 0xb0), new(0xe0, 0xb0, 0xa0), new(0xa8, 0xc0, 0xd0), new(0xf0, 0xe8, 0xd8), new(0xd8, 0xc0, 0x88) },
            Climate.Cold => new Rgb[] { new(0xd8, 0xc4, 0x8c), new(0xb8, 0x60, 0x48), new(0xe8, 0xe4, 0xdc), new(0xc8, 0xb0, 0x90), new(0x9c, 0xa8, 0xb0) },
            _ => new Rgb[] { new(0xe4, 0xdc, 0xc8), new(0xd8, 0xcc, 0xb0), new(0xe8, 0xd0, 0xc4), new(0xc8, 0xcc, 0xc4), new(0xf0, 0xec, 0xe0), new(0xd8, 0xc8, 0x98) },
        }, 3);
        c.Paint = Shades(new Rgb[] { new(0xe8, 0xe8, 0xe0), new(0x9c, 0xa4, 0xa8), new(0x6c, 0x84, 0x9c), new(0x7c, 0x94, 0x7c), new(0x9c, 0x44, 0x34), new(0xd8, 0xd0, 0xb4), new(0x5c, 0x64, 0x6c), new(0xb8, 0xc8, 0xc8) }, 3);
        c.Brick = Shades(new Rgb[] { new(0x8c, 0x48, 0x36), new(0x9c, 0x5c, 0x40), new(0x7a, 0x44, 0x38), new(0xb0, 0x8c, 0x64), new(0x6c, 0x3c, 0x34), new(0xa0, 0x6c, 0x50) }, 2);
        c.Stone = Shades(new Rgb[] { new(0x8c, 0x88, 0x80), new(0x9c, 0x94, 0x84), new(0x7c, 0x78, 0x74), new(0xa8, 0x9c, 0x84), new(0x84, 0x7c, 0x70) }, 2);
        c.Wood = Shades(new Rgb[] { new(0x7c, 0x5c, 0x40), new(0x6c, 0x50, 0x38), new(0x8c, 0x6c, 0x4c), new(0x5c, 0x44, 0x30), new(0x94, 0x7c, 0x5c) }, 2);
        c.Trim = Shades(new Rgb[] { new(0x2e, 0x4a, 0x3a), new(0x7a, 0x2a, 0x24), new(0x2a, 0x3a, 0x5a), new(0xe8, 0xe4, 0xd8), new(0x3a, 0x34, 0x30), new(0x5a, 0x7c, 0x8c), new(0x8c, 0x6c, 0x30) }, 3);
        c.Tile = r.Pick(new Rgb[] { new(0xa0, 0x4c, 0x30), new(0x8c, 0x40, 0x2c), new(0xb4, 0x60, 0x3c), new(0x5c, 0x40, 0x34), new(0x4c, 0x4c, 0x50) });
        c.Tile2 = c.Tile.Scale(0.88f);
        c.Slate = r.Pick(new Rgb[] { new(0x44, 0x48, 0x54), new(0x3c, 0x40, 0x48), new(0x50, 0x54, 0x5c) });
        c.Metal = r.Pick(new Rgb[] { new(0x7c, 0x30, 0x2c), new(0x3c, 0x5c, 0x48), new(0x84, 0x8c, 0x90), new(0x3c, 0x4c, 0x64), new(0x5c, 0x60, 0x64) });
        c.Metal2 = r.Pick(new Rgb[] { new(0x84, 0x8c, 0x90), new(0x6c, 0x74, 0x78) });
        c.Thatch = r.Pick(new Rgb[] { new(0x9c, 0x88, 0x58), new(0x8c, 0x7c, 0x54), new(0xa8, 0x94, 0x64) });
        c.Shingle = r.Pick(new Rgb[] { new(0x5c, 0x54, 0x4c), new(0x4c, 0x4c, 0x4c), new(0x6c, 0x5c, 0x4c) });
        c.Glazed = r.Pick(new Rgb[] { new(0x3c, 0x48, 0x50), new(0x2c, 0x5c, 0x50), new(0x34, 0x44, 0x6c), new(0x6c, 0x34, 0x30) });
        return c;
    }
}

/// <summary>The parts one house is built from, rolled from its culture and family. Sizes in cells (1 m).</summary>
public sealed class HouseStyle
{
    public HouseFamily Family = null!;
    public string Plan = "rect", Roof = "gable", Cover = "tile", Wall = "render", Ground = "", Upper = "", Window = "plain";
    public string Garage = "none", GarageDoor = "panel", Fence = "none";
    public int Storeys = 1, W = 12, D = 10, Over = 2, Units = 1, UnitW = 8;
    public bool GableFront, Bay, Dormer, Porch, Verandah, Chimney, Balcony, Double, Hoist, Vigas, Lift, Berm, Flare, Shutters;
    public float Wealth = 0.5f;
    public Rgb WallC, GroundC, UpperC, RoofC, TrimC, GarageC, ShutterC;

    static int Even(int m) => m / 2 * 2;

    /// <summary>A house of a culture: a family, then each of its parts.</summary>
    public static HouseStyle Roll(HomeCulture c, Rng r, string? family = null)
    {
        HouseFamily f;
        HomeBook book = HomeBook.Current;
        if (family != null && book.Find(family) is { } named) f = named;
        else if (c.Odd.Count > 0 && r.Chance(0.035f)) f = r.Pick(c.Odd);
        else f = c.Families[r.PickWeighted(c.Families.Select(x => x.Weight).ToList())].Family;
        var s = new HouseStyle { Family = f, Wealth = Math.Clamp(c.Wealth + r.Float(-0.25f, 0.25f), 0f, 1f) };
        string Pick(string key, string fallback, Func<string, bool>? ok = null)
        {
            if (c.Favoured.TryGetValue(key, out string? fav) && f.Allows(key, fav) && (ok == null || ok(fav)) && r.Chance(0.55f)) return fav;
            return f.Pick(key, r, fallback, ok);
        }
        bool Has(string key) => r.Chance(f.Chance(key) * (0.7f + 0.6f * s.Wealth));
        bool low = c.Tech == TechLevel.Low, high = c.Tech == TechLevel.High;
        s.Storeys = int.Parse(f.Pick("storeys", r, "1"), CultureInfo.InvariantCulture);
        if (s.Storeys == 1 && s.Wealth > 0.8f && f.Allows("storeys", "2") && r.Chance(0.5f)) s.Storeys = 2;
        s.Plan = f.Pick("plan", r, "rect");
        s.Roof = f.Pick("roof", r, "gable", ro => ro != "mansard" || s.Storeys >= 2);
        // An A-frame's roof comes down to the ground: one storey inside it, a narrow plan, its triangle to the street.
        bool aframe = s.Roof == "aframe";
        s.Cover = Pick("cover", s.Roof is "flat" ? "membrane" : "tile", co => !(low && co is "membrane" or "metal" && f.Allows("cover", "thatch")));
        s.Wall = Pick("wall", "render", w => !(high && w is "halftimber" or "adobe" && f.Choices["wall"].Count > 1));
        s.Ground = f.Pick("ground", r, "");
        s.Upper = f.Pick("upper", r, "");
        s.Window = Pick("window", "plain");
        s.Shutters = s.Window == "shutters";
        s.Over = int.Parse(f.Pick("overhang", r, "2"), CultureInfo.InvariantCulture);
        s.GableFront = r.Chance(f.Chance("gablefront"));
        s.Bay = Has("bay");
        s.Dormer = Has("dormer");
        s.Porch = Has("porch");
        s.Verandah = Has("verandah");
        s.Chimney = r.Chance(f.Chance("chimney")) && !high;
        s.Balcony = Has("balcony") && s.Storeys >= 2;
        s.Hoist = r.Chance(f.Chance("hoist"));
        s.Vigas = r.Chance(f.Chance("vigas"));
        s.Lift = r.Chance(f.Chance("lift"));
        s.Berm = r.Chance(f.Chance("berm"));
        s.Flare = s.Roof == "pagoda";
        // A low world's garage is a cart shed: timber doors or an open shelter.
        s.Garage = f.Pick("garage", r, "none", g => !(low && g == "front"));
        if (s.Garage != "none" && s.Wealth < 0.2f && r.Chance(0.5f)) s.Garage = "carport";
        s.GarageDoor = Pick("door", "panel", d => !low || d is "timber" or "hinged");
        if (low && s.GarageDoor is not ("timber" or "hinged")) s.GarageDoor = r.Chance(0.5f) ? "timber" : "hinged";
        s.Double = r.Chance(f.Chance("double") * (0.5f + s.Wealth));
        s.Fence = Pick("fence", "none");
        var (w0, w1) = f.Size("width", (12, 16));
        var (d0, d1) = f.Size("depth", (10, 14));
        float rich = Math.Clamp(s.Wealth + r.Float(-0.3f, 0.3f), 0f, 1f);
        s.W = Even(w0 + (int)MathF.Round((w1 - w0) * rich / 2) * 2);
        s.D = Even(d0 + (int)MathF.Round((d1 - d0) * Math.Clamp(rich + r.Float(-0.3f, 0.3f), 0f, 1f) / 2) * 2);
        if (aframe)
        {
            s.Storeys = 1;
            s.W = Math.Min(s.W, 10);
            s.GableFront = true;
            s.Plan = "rect";
            s.Balcony = false;
            if (s.Garage != "front") s.Garage = "none";
        }
        var (u0, u1) = f.Size("units", (3, 4));
        s.Units = r.Between(u0, u1);
        var (n0, n1) = f.Size("unit", (8, 8));
        s.UnitW = Even(n0 + 2 * r.Range(0, (n1 - n0) / 2 + 1));
        // Paints.
        Rgb Jit(Rgb x, float k = 0.06f) => x.Scale(1f + r.Float(-k, k));
        Rgb Skin(string wall) => wall switch
        {
            "brick" => Jit(r.Pick(c.Brick)),
            "board" => Jit(r.Pick(c.Paint), 0.03f),
            "stone" => Jit(r.Pick(c.Stone)),
            "timber" => Jit(r.Pick(c.Wood)),
            "adobe" => Jit(r.Pick(new Rgb[] { new(0xc4, 0x8c, 0x5c), new(0xb8, 0x80, 0x58), new(0xcc, 0xa0, 0x70), new(0xa8, 0x74, 0x50) })),
            "panel" => Jit(r.Pick(new Rgb[] { new(0xe4, 0xe8, 0xe8), new(0xc8, 0xcc, 0xcc), new(0x5c, 0x60, 0x64), new(0xd8, 0xd4, 0xcc) }), 0.03f),
            "glass" => new Rgb(0x6a, 0x84, 0x8c),
            _ => Jit(r.Pick(c.Render), 0.03f),
        };
        s.WallC = Skin(s.Wall);
        s.GroundC = s.Ground.Length > 0 ? Skin(s.Ground) : s.WallC;
        s.UpperC = s.Upper.Length > 0 ? Skin(s.Upper) : s.WallC;
        s.RoofC = s.Cover switch
        {
            "slate" => Jit(c.Slate, 0.04f),
            "metal" => r.Chance(0.7f) ? c.Metal : c.Metal2,
            "thatch" => Jit(c.Thatch),
            "shingle" => Jit(c.Shingle),
            "membrane" => new Rgb(0x48, 0x4a, 0x4c),
            "glazed" => c.Glazed,
            "cap" => r.Pick(new Rgb[] { new(0xb0, 0x34, 0x2c), new(0x9c, 0x5c, 0x34), new(0x7c, 0x54, 0x7c), new(0xc4, 0x8c, 0x34) }),
            "glass" => new Rgb(0x6a, 0x84, 0x8c),
            _ => Jit(r.Chance(0.75f) ? c.Tile : c.Tile2, 0.04f),
        };
        s.TrimC = r.Pick(c.Trim);
        s.ShutterC = r.Pick(c.Trim);
        s.GarageC = r.Chance(0.5f) ? s.TrimC : r.Pick(new Rgb[] { new(0xe0, 0xe0, 0xd8), new(0xb8, 0xbc, 0xbc), new(0x6c, 0x70, 0x74), new(0x8c, 0x6c, 0x4c), new(0x4c, 0x5c, 0x6c) });
        return s;
    }
}
