#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>A size range of a place type's part, metres (or a count): each spawn rolls its own within it.</summary>
public readonly record struct Span(float Lo, float Hi)
{
    public float Roll(Rng r) => Hi > Lo ? r.Float(Lo, Hi) : Lo;
    public bool Holds(float v, float slack = 0.5f) => v >= Lo - slack && v <= Hi + slack;
    public static Span Parse(string s)
    {
        int dots = s.IndexOf("..", StringComparison.Ordinal);
        float F(string t) => float.Parse(t, CultureInfo.InvariantCulture);
        if (dots < 0) return new Span(F(s), F(s));
        string hi = s[(dots + 2)..];
        return new Span(F(s[..dots]), hi.Length == 0 ? float.MaxValue : F(hi));
    }
    public override string ToString() => Hi >= float.MaxValue ? $"{Lo:F0}+" : Lo == Hi ? $"{Lo:F0}" : $"{Lo:F0}..{Hi:F0}";
}

/// <summary>One kind of thing a place type has: what it is and its sizes (where it goes is its type's plan's rule).</summary>
public sealed class TypePart
{
    public string Id = "", Kind = "", What = "";
    public readonly Dictionary<string, Span> Size = new();
    public Span Get(string key, float fallback) => Size.TryGetValue(key, out Span s) ? s : new Span(fallback, fallback);
}

/// <summary>A kind of place as data (data/place-types.txt): its plan, its parts and their real sizes. Never a layout.</summary>
public sealed class PlaceType
{
    public string Id = "", Name = "";
    public readonly List<string> Techs = new();
    /// <summary>The starport classes it is built for (a spaceport's), none for any.</summary>
    public readonly List<Starport> Classes = new();
    public float Share = 0.05f;
    /// <summary>Its plan: the rules that lay its parts out (airport, hub, rosettes, grid: PlaceTypes.Airport, PlaceTypes.Ports).</summary>
    public string Plan = "";
    /// <summary>The biggest craft it takes, m: what its pads, halls and doors are sized by.</summary>
    public float Craft = 30f;
    public readonly List<TypePart> Parts = new();
    public TypePart Part(string id) => Parts.First(p => p.Id == id);

    public static List<PlaceType> Parse(string text, string file)
    {
        var types = new List<PlaceType>();
        PlaceType? t = null;
        int n = 0;
        foreach (string raw in text.Split('\n'))
        {
            n++;
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            string[] w = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Exception Bad(string why) => new FormatException($"{file}:{n}: {why}");
            if (w[0] == "type") { types.Add(t = new PlaceType { Id = w[1], Name = w[1] }); continue; }
            if (t == null) throw Bad("a type line first");
            switch (w[0])
            {
                case "name": t.Name = string.Join(' ', w.Skip(1)); break;
                case "tech": t.Techs.AddRange(w.Skip(1)); break;
                case "share": t.Share = float.Parse(w[1], CultureInfo.InvariantCulture); break;
                case "class": t.Classes.AddRange(w.Skip(1).Select(c => Enum.Parse<Starport>(c, true))); break;
                case "plan": t.Plan = w[1]; break;
                case "craft": t.Craft = float.Parse(w[1], CultureInfo.InvariantCulture); break;
                case "part":
                {
                    var p = new TypePart { Id = w[1], Kind = w[2] };
                    int i = 3;
                    if (i < w.Length && !(i + 1 < w.Length && char.IsDigit(w[i + 1][0]))) p.What = w[i++];
                    for (; i < w.Length; i += 2)
                    {
                        if (i + 1 >= w.Length) throw Bad($"'{w[i]}' wants a value");
                        p.Size[w[i]] = Span.Parse(w[i + 1]);
                    }
                    t.Parts.Add(p);
                    break;
                }
                default: throw Bad($"'{w[0]}' is not a type line");
            }
        }
        if (types.FirstOrDefault(x => x.Plan == "") is { } planless) throw new FormatException($"{file}: {planless.Id} has no plan");
        return types;
    }
}

/// <summary>An axis-aligned box of a sketch, site metres (x along the main axis), with the way its front looks (a unit step, or 0, 0).</summary>
public readonly record struct SketchBox(float X0, float Z0, float X1, float Z1)
{
    public float W => X1 - X0;
    public float D => Z1 - Z0;
    public float Cx => (X0 + X1) * 0.5f;
    public float Cz => (Z0 + Z1) * 0.5f;
    public float Long => MathF.Max(W, D);
    public float Short => MathF.Min(W, D);
    public bool Overlaps(SketchBox o, float clear = 0f) => X0 < o.X1 + clear && o.X0 < X1 + clear && Z0 < o.Z1 + clear && o.Z0 < Z1 + clear;
    /// <summary>Edge to edge, metres (0 where they touch or overlap).</summary>
    public float Gap(SketchBox o)
    {
        float dx = MathF.Max(0f, MathF.Max(o.X0 - X1, X0 - o.X1)), dz = MathF.Max(0f, MathF.Max(o.Z0 - Z1, Z0 - o.Z1));
        return MathF.Sqrt(dx * dx + dz * dz);
    }
    public SketchBox Grow(float m) => new(X0 - m, Z0 - m, X1 + m, Z1 + m);
    public SketchBox Union(SketchBox o) => new(MathF.Min(X0, o.X0), MathF.Min(Z0, o.Z0), MathF.Max(X1, o.X1), MathF.Max(Z1, o.Z1));
    /// <summary>Whether the segment between two points crosses it.</summary>
    public bool Crosses(float ax, float az, float bx, float bz)
    {
        for (int i = 0; i <= 32; i++)
        {
            float t = i / 32f, x = ax + (bx - ax) * t, z = az + (bz - az) * t;
            if (x > X0 && x < X1 && z > Z0 && z < Z1) return true;
        }
        return false;
    }
}

/// <summary>One thing of a sketch: which part of the type it is, its box, height, front, and the sizes it rolled; laid ground off the axes or round has its shape too.</summary>
public sealed record SketchItem(TypePart Part, SketchBox Box, float H, int FaceX, int FaceZ, Dictionary<string, float> Rolled)
{
    public Shape? Lay { get; init; }
    /// <summary>What it covers on the ground: its shape, or its box.</summary>
    public Shape Outline => Lay ?? Shape.Of(Box);
}

/// <summary>
/// A place of a type as first rolled, cheaply: every part's box and height, the footprint, the way in and the gate.
/// Siting, levelling, the world's road and the far block all work from this; the buildings are laid out inside it later.
/// </summary>
public sealed class Sketch
{
    public PlaceType Type = null!;
    public ulong Seed;
    public readonly List<SketchItem> Items = new();
    public SketchBox Footprint;
    /// <summary>Where the access road leaves the place for the world's road, and the way out there.</summary>
    public float GateX, GateZ;
    public int OutX, OutZ;
    /// <summary>The long parts were shortened to fit the world (the type's share of it), and by how much at most (1 = as rolled).</summary>
    public float Fitted = 1f;
    /// <summary>Even at the low ends the place takes more of its world than the type's share.</summary>
    public bool TooBig;
    public IEnumerable<SketchItem> Of(string part) => Items.Where(i => i.Part.Id == part);
    /// <summary>Round every box of a part (a row of pads, the hangars).</summary>
    public SketchBox Box(string part) => Of(part).Select(i => i.Box).Aggregate((a, b) => a.Union(b));
    public float Hectares => Footprint.W * Footprint.D / 10000f;
    /// <summary>What must stand on dry, even ground: everything but the approach lights and the fence round it all.</summary>
    public SketchBox Core => Items.Where(i => i.Part.Kind is not ("lights" or "fence")).Select(i => i.Box).Aggregate((a, b) => a.Union(b));
}

/// <summary>
/// Rolls a sketch of a place type by its plan's rules (PlaceTypes.Airport, PlaceTypes.Ports): every count, size, angle and
/// spacing its own spawn's. The world's size caps the long parts (the type's share of the world's area).
/// </summary>
public static partial class PlaceTypes
{
    /// <summary>Shots and tests: parts some spawns have that every spawn has (the second runway, to show its junction).</summary>
    public static readonly HashSet<string> Always = new();

    /// <summary>A sketch of a type for a world, rolled at most <paramref name="most"/> of the way up its long parts' ranges (0: their low ends).</summary>
    public static Sketch Plan(PlaceType type, ulong seed, double worldWidth, double worldHeight, float most = 1f)
    {
        double area = worldWidth * worldHeight;
        // Rolled at full size first; shortened toward the low ends when the place would take more than its share.
        Sketch? best = null;
        for (float fit = most; fit >= 0f; fit -= 0.25f)
        {
            Sketch s = RollPlan(type, seed, fit);
            best = s;
            if (s.Footprint.W * s.Footprint.D <= type.Share * area) return s;
        }
        best!.TooBig = true;
        return best;
    }
}
