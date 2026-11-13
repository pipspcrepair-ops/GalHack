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

/// <summary>One kind of thing a place type has: what it is, its sizes, and how it relates to the things before it.</summary>
public sealed class TypePart
{
    public string Id = "", Kind = "", What = "";
    public readonly Dictionary<string, Span> Size = new();
    /// <summary>Relations by name: the part (or parts) each names, and its distance range where it has one.</summary>
    public readonly List<(string Rel, string[] To, Span? Gap)> Rel = new();
    public Span Get(string key, float fallback) => Size.TryGetValue(key, out Span s) ? s : new Span(fallback, fallback);
    public (string Rel, string[] To, Span? Gap)? Find(string rel) => Rel.FirstOrDefault(r => r.Rel == rel) is { Rel: not null } f ? f : null;
}

/// <summary>A kind of place as data (data/place-types.txt): its parts, their real sizes and their relations. Never a layout.</summary>
public sealed class PlaceType
{
    public string Id = "", Name = "";
    public readonly List<string> Techs = new();
    public float Share = 0.05f;
    public readonly List<TypePart> Parts = new();
    public TypePart Part(string id) => Parts.First(p => p.Id == id);

    static readonly HashSet<string> Relations = new() { "along", "between", "beside", "ends", "faces", "near", "far", "sees", "from", "round", "away", "gap" };

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
                case "part":
                {
                    var p = new TypePart { Id = w[1], Kind = w[2] };
                    int i = 3;
                    if (i < w.Length && !char.IsDigit(w[i][0]) && !Relations.Contains(w[i]) && !IsSize(w, i)) p.What = w[i++];
                    while (i < w.Length)
                    {
                        string k = w[i++];
                        switch (k)
                        {
                            case "along" or "beside":
                            {
                                var to = new List<string> { w[i++] };
                                if (i + 1 < w.Length && w[i] == "away") { i++; to.Add(w[i++]); }
                                Span? gap = null;
                                if (i + 1 < w.Length && w[i] == "gap") { gap = Span.Parse(w[i + 1]); i += 2; }
                                p.Rel.Add((k, to.ToArray(), gap));
                                break;
                            }
                            case "gap": p.Rel.Add(("gap", Array.Empty<string>(), Span.Parse(w[i++]))); break;
                            case "between": p.Rel.Add((k, new[] { w[i], w[i + 1] }, null)); i += 2; break;
                            case "near" or "far": p.Rel.Add((k, new[] { w[i] }, Span.Parse(w[i + 1]))); i += 2; break;
                            case "ends" or "faces" or "sees" or "from": p.Rel.Add((k, new[] { w[i++] }, null)); break;
                            case "round": p.Rel.Add((k, Array.Empty<string>(), Span.Parse(w[i++]))); break;
                            default:
                                if (i >= w.Length) throw Bad($"'{k}' wants a value");
                                p.Size[k] = Span.Parse(w[i++]);
                                break;
                        }
                    }
                    t.Parts.Add(p);
                    break;
                }
                default: throw Bad($"'{w[0]}' is not a type line");
            }
        }
        foreach (PlaceType type in types)
            foreach (TypePart p in type.Parts)
                foreach (var r in p.Rel)
                    foreach (string to in r.To)
                        if (!type.Parts.TakeWhile(q => q != p).Any(q => q.Id == to)) throw new FormatException($"{file}: {type.Id}.{p.Id} {r.Rel} {to}: no such part before it");
        return types;
    }

    static bool IsSize(string[] w, int i) => i + 1 < w.Length && (char.IsDigit(w[i + 1][0]));
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

/// <summary>One thing of a sketch: which part of the type it is, its box, height, front, and the sizes it rolled.</summary>
public sealed record SketchItem(TypePart Part, SketchBox Box, float H, int FaceX, int FaceZ, Dictionary<string, float> Rolled);

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
    public float Hectares => Footprint.W * Footprint.D / 10000f;
}

/// <summary>
/// Rolls a sketch of a place type from its relations: the first part at the middle along x, each after it off the parts
/// it names (along, beside, between, ends, round, from), tried again elsewhere when it would overlap anything, with fewer
/// of a counted part where there isn't room. The world's size caps the long parts (the type's share of the world's area).
/// </summary>
public static class PlaceTypes
{
    const float Clear = 6f;
    const int Tries = 24;

    public static Sketch Plan(PlaceType type, ulong seed, double worldWidth, double worldHeight)
    {
        double area = worldWidth * worldHeight;
        // Rolled at full size first; shortened toward the low ends when the place would take more than its share.
        Sketch? best = null;
        for (float fit = 1f; fit >= 0f; fit -= 0.25f)
        {
            Sketch s = Roll(type, seed, fit);
            best = s;
            if (s.Footprint.W * s.Footprint.D <= type.Share * area) return s;
        }
        best!.TooBig = true;
        return best;
    }

    /// <summary>One roll: <paramref name="fit"/> 1 rolls a long part's length over its whole range, 0 holds it to its low end.</summary>
    static Sketch Roll(PlaceType type, ulong seed, float fit)
    {
        var rng = new Rng(Hash.Combine(seed, Hash.Of(type.Id)));
        var s = new Sketch { Type = type, Seed = seed, Fitted = fit };
        int side = rng.Chance(0.5f) ? 1 : -1;   // which side of the runway the landside lies
        foreach (TypePart p in type.Parts)
        {
            var r = rng.Fork(p.Id);
            float Roll(string k, float fb) => p.Get(k, fb).Roll(r);
            float RollLong(string k, float fb) { Span sp = p.Get(k, fb); return sp.Lo + (MathF.Min(sp.Hi, sp.Lo * 4f) - sp.Lo) * fit * r.Float(); }
            var rolled = new Dictionary<string, float>();
            foreach (var (k, sp) in p.Size) rolled[k] = k == "len" ? RollLong(k, 0) : sp.Roll(r);
            float h = rolled.GetValueOrDefault("h");
            SketchItem Item(SketchBox b, int fx = 0, int fz = 0) => new(p, b, h, fx, fz, rolled);

            if (p.Find("round") is { } round)
            {
                // Round everything: the fence (and what becomes the footprint).
                SketchBox all = s.Items.Select(i => i.Box).Aggregate((a, b) => a.Union(b)).Grow(round.Gap!.Value.Roll(r));
                s.Items.Add(Item(all));
                continue;
            }
            if (p.Find("from") is { } from)
            {
                // A road from it straight out, away from the runway, to the edge the fence will run on.
                SketchBox a = s.Items.First(i => i.Part.Id == from.To[0]).Box;
                float w = rolled.GetValueOrDefault("w", 7f), edge = Edge(s, side) + side * 40f;
                float x = a.Cx;
                s.Items.Add(Item(side > 0 ? new SketchBox(x - w / 2, a.Z1, x + w / 2, edge) : new SketchBox(x - w / 2, edge, x + w / 2, a.Z0), 0, side));
                continue;
            }
            if (s.Items.Count == 0)
            {
                // The first part: the main axis.
                float len = rolled.GetValueOrDefault("len", 100f), w = rolled.GetValueOrDefault("w", 20f) + 2f * rolled.GetValueOrDefault("shoulder");
                s.Items.Add(Item(new SketchBox(-len / 2, -w / 2, len / 2, w / 2)));
                continue;
            }
            if (p.Find("along") is { } along)
            {
                SketchBox a = s.Items.First(i => i.Part.Id == along.To[0]).Box;
                float sep = along.Gap?.Roll(r) ?? 100f, w = rolled.GetValueOrDefault("w", 20f), len = a.W * r.Float(0.85f, 1f);
                float cz = a.Cz + side * sep, x0 = a.Cx - len / 2 + r.Float(-1f, 1f) * (a.W - len) / 2;
                s.Items.Add(Item(new SketchBox(x0, cz - w / 2, x0 + len, cz + w / 2)));
                continue;
            }
            if (p.Find("between") is { } between)
            {
                SketchBox a = s.Items.First(i => i.Part.Id == between.To[0]).Box, b = s.Items.First(i => i.Part.Id == between.To[1]).Box;
                float w = rolled.GetValueOrDefault("w", 20f), lo = MathF.Max(a.X0, b.X0) + w, hi = MathF.Min(a.X1, b.X1) - w;
                int count = (int)MathF.Round(rolled.GetValueOrDefault("count", 2f));
                // From the edge of one that faces the other, to the other's.
                float z0 = b.Cz > a.Cz ? a.Z1 : b.Z1, z1 = b.Cz > a.Cz ? b.Z0 : a.Z0;
                for (int k = 0; k < count; k++)
                {
                    // Spread along the overlap, the ends always linked, the rest wherever this spawn puts them.
                    float t = count == 1 ? 0.5f : k == 0 ? 0f : k == count - 1 ? 1f : (k + r.Float(-0.3f, 0.3f)) / (count - 1);
                    float x = lo + (hi - lo) * t;
                    s.Items.Add(Item(new SketchBox(x - w / 2, z0, x + w / 2, z1)));
                }
                continue;
            }
            if (p.Find("ends") is { } ends)
            {
                SketchBox a = s.Items.First(i => i.Part.Id == ends.To[0]).Box;
                float len = rolled.GetValueOrDefault("len", 420f), w = MathF.Min(a.D, 30f);
                s.Items.Add(Item(new SketchBox(a.X0 - len, a.Cz - w / 2, a.X0, a.Cz + w / 2), -1, 0));
                s.Items.Add(Item(new SketchBox(a.X1, a.Cz - w / 2, a.X1 + len, a.Cz + w / 2), 1, 0));
                continue;
            }
            if (p.Find("beside") is { } beside)
            {
                SketchBox a = s.Items.First(i => i.Part.Id == beside.To[0]).Box;
                // The side away from the second part named (the landside), or the landside itself.
                int way = beside.To.Length > 1 ? (s.Items.First(i => i.Part.Id == beside.To[1]).Box.Cz < a.Cz ? 1 : -1) : side;
                float len = rolled.GetValueOrDefault("len", 10f), depth = rolled.GetValueOrDefault("depth", 10f);
                int count = (int)MathF.Round(rolled.GetValueOrDefault("count", 1f));
                if (p.Kind == "tanks")
                {
                    // A tank farm: its tanks in rows inside one bunded yard, a tank's width apart.
                    float cell = rolled.GetValueOrDefault("r", 5f) * 2f * 2f;
                    int cols = (int)MathF.Ceiling(MathF.Sqrt(count));
                    (len, depth, count) = (cols * cell + 8f, MathF.Ceiling(count / (float)cols) * cell + 8f, 1);
                }
                Span gapSpan = beside.Gap ?? p.Find("gap")?.Gap ?? new Span(0f, 0f);
                var faces = p.Find("faces");
                int placed = 0;
                for (int k = 0; k < count; k++)
                    for (int t = 0; t < Tries; t++)
                    {
                        float off = gapSpan.Roll(r) + (p.Kind == "surface" ? 0f : Clear + 1f);
                        float z0 = way > 0 ? a.Z1 + off : a.Z0 - off - depth;
                        float x0 = r.Float(a.X0, MathF.Max(a.X0, a.X1 - len));
                        var box = new SketchBox(x0, z0, x0 + len, z0 + depth);
                        if (!Fits(s, p, box, a)) continue;
                        int fz = faces != null ? (s.Items.First(i => i.Part.Id == faces.Value.To[0]).Box.Cz < box.Cz ? -1 : 1) : 0;
                        s.Items.Add(Item(box, 0, fz));
                        placed++;
                        break;
                    }
                // No luck rolling: the first spot along it that fits, from a rolled end; fewer where there is no room, never none of a part the type has.
                if (placed == 0)
                {
                    float off = gapSpan.Lo + (p.Kind == "surface" ? 0f : Clear + 1f), z0 = way > 0 ? a.Z1 + off : a.Z0 - off - depth;
                    bool fromLow = r.Chance(0.5f);
                    SketchBox? spot = null;
                    for (float x = 0; x <= a.W - len && spot == null; x += MathF.Max(4f, len / 4))
                    {
                        float x0 = fromLow ? a.X0 + x : a.X1 - len - x;
                        var box = new SketchBox(x0, z0, x0 + len, z0 + depth);
                        if (Fits(s, p, box, a)) spot = box;
                    }
                    s.Items.Add(Item(spot ?? Fallback(s, a, way, len, depth)));
                }
                continue;
            }
            throw new InvalidOperationException($"{type.Id}.{p.Id}: no relation places it");
        }
        s.Footprint = s.Items.Select(i => i.Box).Aggregate((a, b) => a.Union(b));
        // The way in: the access road runs on to the fence, and the gate is where it meets it.
        int road = s.Items.FindLastIndex(i => i.Part.Kind == "road");
        if (road >= 0)
        {
            SketchItem a = s.Items[road];
            float edge = side > 0 ? s.Footprint.Z1 : s.Footprint.Z0;
            s.Items[road] = a with { Box = side > 0 ? a.Box with { Z1 = edge } : a.Box with { Z0 = edge } };
            (s.GateX, s.GateZ, s.OutX, s.OutZ) = (a.Box.Cx, edge, 0, side);
        }
        return s;
    }

    /// <summary>The landside edge of what stands so far, metres along z.</summary>
    static float Edge(Sketch s, int side) => side > 0 ? s.Items.Max(i => i.Box.Z1) : s.Items.Min(i => i.Box.Z0);

    /// <summary>Clear of everything placed (its anchor it may touch, if it is laid ground), and within its near and far ranges.</summary>
    static bool Fits(Sketch s, TypePart p, SketchBox box, SketchBox anchor)
    {
        foreach (SketchItem i in s.Items)
        {
            bool ground = p.Kind == "surface" && i.Part.Kind == "surface";
            if (box.Overlaps(i.Box, ground ? -0.01f : Clear)) return false;
        }
        foreach (var (rel, to, gap) in p.Rel)
        {
            if (rel is not ("near" or "far")) continue;
            float d = s.Items.Where(i => i.Part.Id == to[0]).Select(i => i.Box.Gap(box)).DefaultIfEmpty(float.MaxValue).Min();
            if (!gap!.Value.Holds(d, 0f)) return false;
        }
        return true;
    }

    /// <summary>Past everything on that side: where a part goes when the anchor's side has no room for it.</summary>
    static SketchBox Fallback(Sketch s, SketchBox a, int way, float len, float depth)
    {
        float z0 = way > 0 ? Edge(s, 1) + Clear : Edge(s, -1) - Clear - depth;
        return new SketchBox(a.Cx - len / 2, z0, a.Cx + len / 2, z0 + depth);
    }
}
