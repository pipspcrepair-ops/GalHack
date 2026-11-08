#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Where a dressing part goes, by the rules of data/world-dressing.txt: across a street, beside it, in
/// its lane, in a town's open square, in a house's front garden or on its front wall by the door, by a
/// highway or road out of town, in its lane, out in the fields beside it, or at a place's way in.
/// </summary>
public enum DressSite : byte { Span, Kerb, Lane, Square, Garden, Wall, Verge, Road, Field, Gate }

/// <summary>One part of the dressing table: what a world can leave lying about, and where and how often.</summary>
public sealed class DressPart
{
    public string Id = "";
    /// <summary>The model that draws it (its own id unless the record says).</summary>
    public string Model = "";
    /// <summary>A style the model draws in (a wall mark's sign, a tent's use).</summary>
    public string Style = "";
    public readonly List<DressSite> At = new();
    /// <summary>Metres between them along a way, at a strength of one.</summary>
    public float Every = 40f;
    /// <summary>The share of spots that take one, at a strength of one (squares, gardens, walls, gates).</summary>
    public float Chance = 0.3f;
    /// <summary>How far from a settlement it has thinned to a third, metres.</summary>
    public float Far = 700f;
    /// <summary>How far it reaches round its spot, metres: what keeps two apart.</summary>
    public float Reach = 2f;
    public bool Solid;
    /// <summary>Only a garden, wall or gate of a building with this family tag (a shop's awning), when set.</summary>
    public string? Only;
    /// <summary>Made by the living after the fall (spikes, braziers, a barricade of junk): in their era, whoever leans to it.</summary>
    public bool Now;
}

/// <summary>
/// The dressing table (docs/DESIGN.md section 8, "Every world is dressed by its layers"): each layer
/// value with a visible meaning leans toward parts, and a world's dressing is what its rolled values
/// add up to. A record, never code: the planner places by the parts' rules, the models draw them.
/// </summary>
public sealed class DressBook
{
    public readonly Dictionary<string, DressPart> Parts = new(StringComparer.Ordinal);
    /// <summary>Who leans: a selector (layer.id, tag.word, group.word, or all) and the parts it adds, with how much.</summary>
    public readonly List<(string Selector, Dictionary<string, float> Leans)> Leans = new();

    /// <summary>The most any part's strength adds up to: a world thick with it, not wall to wall.</summary>
    public const float Most = 2.5f;

    /// <summary>How much a theme's leans count over the rest, and a fall group's under.</summary>
    public const float ThemeWeight = 1.5f, GroupWeight = 0.6f;

    public static DressBook Parse(string text, string source)
    {
        var book = new DressBook();
        int n = 0;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            n++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            FormatException Err(string m) => new($"{source}:{n}: {m}");
            string[] fields = line.Split(" | ");
            string[] head = fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (head.Length < 2) throw Err($"'{fields[0]}' isn't a record");
            switch (head[0])
            {
                case "part":
                {
                    var p = new DressPart { Id = head[1], Model = head[1] };
                    foreach (string f in fields.Skip(1))
                    {
                        string[] w = f.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (w.Length < 2) throw Err($"'{f}' needs a value");
                        switch (w[0])
                        {
                            case "model": p.Model = w[1]; break;
                            case "style": p.Style = w[1]; break;
                            case "at":
                                foreach (string s in w.Skip(1))
                                    p.At.Add(Enum.TryParse(s, true, out DressSite site) ? site : throw Err($"'{s}' isn't a site"));
                                break;
                            case "every": p.Every = Num(w[1]); break;
                            case "chance": p.Chance = Num(w[1]); break;
                            case "far": p.Far = Num(w[1]); break;
                            case "reach": p.Reach = Num(w[1]); break;
                            case "solid": p.Solid = w[1] == "yes"; break;
                            case "only": p.Only = w[1]; break;
                            case "era": p.Now = w[1] == "now" ? true : w[1] == "old" ? false : throw Err($"era is old or now, not '{w[1]}'"); break;
                            default: throw Err($"unknown keyword '{w[0]}'");
                        }
                    }
                    if (p.At.Count == 0) throw Err($"part '{p.Id}' goes nowhere");
                    book.Parts[p.Id] = p;
                    break;
                }
                case "lean":
                {
                    var leans = new Dictionary<string, float>(StringComparer.Ordinal);
                    foreach (string f in fields.Skip(1))
                    {
                        string[] w = f.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i + 1 < w.Length; i += 2) leans[w[i]] = leans.GetValueOrDefault(w[i]) + Num(w[i + 1]);
                    }
                    book.Leans.Add((head[1], leans));
                    break;
                }
                default: throw Err($"'{head[0]}' isn't a record (part or lean)");
            }
        }
        foreach (var (sel, leans) in book.Leans)
            foreach (string part in leans.Keys)
                if (!book.Parts.ContainsKey(part)) throw new FormatException($"{source}: '{sel}' leans to '{part}', which isn't a part");
        return book;
    }

    static float Num(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>Whether a selector holds on a world: a value it rolled (layer.id), a tag its values carry (tag.word), a fall's group (group.word), or every settled world (all).</summary>
    public static bool Holds(string selector, WorldRoll roll)
    {
        if (selector == "all") return roll.Fact("settled") == "yes";
        int dot = selector.IndexOf('.');
        if (dot < 1) return false;
        string kind = selector[..dot], id = selector[(dot + 1)..];
        return kind switch
        {
            "tag" => roll.Tags.Contains(id),
            "group" => roll.All(LayerKind.Fall).Any(f => f.Group == id),
            _ => Enum.TryParse(kind, true, out LayerKind k) && roll.Rolled.Any(l => l.Kind == k && l.Id == id),
        };
    }

    /// <summary>
    /// How strongly a world leans to each part: every selector that holds adds its leans, capped at <see cref="Most"/>.
    /// Living people's leans (a creed's, a presence's) are counted apart: what they put up is kept, and in their own era.
    /// </summary>
    public (Dictionary<string, float> Old, Dictionary<string, float> Now) Strengths(WorldRoll? roll)
    {
        var old = new Dictionary<string, float>(StringComparer.Ordinal);
        var now = new Dictionary<string, float>(StringComparer.Ordinal);
        if (roll == null) return (old, now);
        foreach (var (sel, leans) in Leans)
        {
            if (!Holds(sel, roll)) continue;
            var to = sel.StartsWith("creed.", StringComparison.Ordinal) || sel.StartsWith("presence.", StringComparison.Ordinal) ? now : old;
            // The theme is the world's mood and leads; a fall's group only colours what its own fall says.
            float k = sel.StartsWith("theme.", StringComparison.Ordinal) ? ThemeWeight : sel.StartsWith("group.", StringComparison.Ordinal) ? GroupWeight : 1f;
            foreach (var (part, v) in leans) to[part] = to.GetValueOrDefault(part) + v * k;
        }
        foreach (var t in new[] { old, now })
            foreach (string k in t.Keys.ToList()) t[k] = Math.Clamp(t[k], 0f, Most);
        return (old, now);
    }
}
