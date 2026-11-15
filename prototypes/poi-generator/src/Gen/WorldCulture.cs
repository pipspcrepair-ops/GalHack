#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A community's culture (docs/research/culture.md): one value on each axis of data/world-layers.txt's
/// culture tables, which sex is side A, which side each role group falls to, and the world's
/// desperation. A world rolls one (<see cref="WorldRoll.Culture"/>); each town drifts from it and each
/// faction keeps its own (<see cref="At"/>). Generators read its traits, never its value names.
/// </summary>
public sealed class CultureReading
{
    /// <summary>The axes, in the order a world rolls them (a value's req only sees the axes before it).</summary>
    public static readonly LayerKind[] Axes =
    {
        LayerKind.Space, LayerKind.Self, LayerKind.Sexes, LayerKind.Arms, LayerKind.Fierce, LayerKind.Young, LayerKind.Caste, LayerKind.Garb,
        LayerKind.Cover, LayerKind.Labour, LayerKind.Rite, LayerKind.Reign, LayerKind.Roam, LayerKind.Guest, LayerKind.Elders, LayerKind.Manner,
    };

    /// <summary>The role groups whose side a world rolls, and the two the culture's own sides use (the hot side, and who wears the decency piece).</summary>
    public static readonly string[] Groups = { "fight", "trade", "make", "care", "lead" };

    public readonly Dictionary<LayerKind, WorldLayer> Values = new();
    /// <summary>The axes a test or a new game set by hand: a town keeps them as given, without drifting.</summary>
    public readonly HashSet<LayerKind> Given = new();
    /// <summary>The sex that is side A ("female" or "male").</summary>
    public string Lead = "female";
    /// <summary>Each role group's side, and "hot" (the side that runs hot): true for side A.</summary>
    public readonly Dictionary<string, bool> SideA = new();
    /// <summary>Who wears the decency piece: "all", "a" or "b".</summary>
    public string CoverFor = "all";
    public float Desperation;
    /// <summary>This community's own seed: a town's culture is its own roll, its world's for the world's.</summary>
    public ulong Seed;
    /// <summary>The world's facts, which a drifted value must still hold under.</summary>
    public IReadOnlyDictionary<string, string> Facts = new Dictionary<string, string>();
    readonly Dictionary<string, string> _traits = new();

    public string Id(LayerKind k) => Values.TryGetValue(k, out WorldLayer? l) ? l.Id : "";

    /// <summary>Every value's traits together (a later axis wins a shared key).</summary>
    public IReadOnlyDictionary<string, string> Traits => _traits;

    public float Trait(string key, float otherwise = 0f) => _traits.TryGetValue(key, out string? v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : otherwise;

    /// <summary>Axes off today's default, and how many are extremes.</summary>
    public int OffDefault => Values.Values.Count(l => !l.Blank);
    public int Extremes => Values.Values.Count(l => l.Trait("extreme") > 0f);

    void Merge()
    {
        _traits.Clear();
        foreach (LayerKind k in Axes)
            if (Values.TryGetValue(k, out WorldLayer? l))
                foreach (var (key, v) in l.Traits) _traits[key] = v;
    }

    public override string ToString() => string.Join(" ", Axes.Where(Values.ContainsKey).Select(k => $"{k.ToString().ToLowerInvariant()}.{Values[k].Id}")) + $" lead {Lead} desperation {Desperation:F2}";

    /// <summary>The facts its values hold, with the world's: what a value's req reads.</summary>
    Dictionary<string, string> FactsNow()
    {
        var f = new Dictionary<string, string>(Facts);
        foreach (var (k, l) in Values) f[k.ToString().ToLowerInvariant()] = l.Id;
        return f;
    }

    CultureReading Copy(ulong seed)
    {
        var c = new CultureReading { Lead = Lead, CoverFor = CoverFor, Desperation = Desperation, Seed = seed, Facts = Facts };
        foreach (var (k, v) in Values) c.Values[k] = v;
        foreach (var (k, v) in SideA) c.SideA[k] = v;
        c.Given.UnionWith(Given);
        return c;
    }

    /// <summary>How likely a town is to step off its world's value on each axis: more at a crossroads, where outsiders mix.</summary>
    public const float TownDrift = 0.2f, HubDrift = 0.35f;

    /// <summary>
    /// The culture of one place: a faction's own set wherever it lives (the world's on any axis its set leaves
    /// out), and then the place's drift, by its own seed (the one its houses use): on each axis, now and then,
    /// one value along the table, never onto one the rules forbid there or onto an extreme its world didn't roll.
    /// </summary>
    public CultureReading At(PoiLibrary lib, ulong place, Faction faction = Faction.None, bool hub = false)
    {
        var c = Copy(Hash.Combine(Seed, place));
        var rng = new Rng(Hash.Combine(place, 0xC017UL));
        float drift = hub ? HubDrift : TownDrift;
        string own = faction.ToString().ToLowerInvariant();
        if (faction != Faction.None && lib.Layers.FirstOrDefault(l => l.Kind == LayerKind.Culture && l.Id == own) is { } set)
        {
            drift = set.Trait("drift", drift);
            foreach (var (axis, ids) in set.Sets)
            {
                if (!Enum.TryParse(axis, true, out LayerKind k)) continue;
                string id = rng.Fork(axis).Pick(ids.Split(','));
                if (lib.Layers.FirstOrDefault(l => l.Kind == k && l.Id == id) is { } v) c.Values[k] = v;
            }
        }
        foreach (LayerKind k in Axes)
        {
            Rng r = rng.Fork("drift." + k);
            if (!c.Values.TryGetValue(k, out WorldLayer? now) || c.Given.Contains(k) || !r.Chance(drift)) continue;
            var table = lib.Layers.Where(l => l.Kind == k && !l.Off).ToList();
            int i = table.IndexOf(now), step = r.Chance(0.5f) ? 1 : -1;
            if (i < 0) continue;
            if (i + step < 0 || i + step >= table.Count) step = -step;
            if (i + step < 0 || i + step >= table.Count) continue;
            WorldLayer to = table[i + step];
            Dictionary<string, string> facts = c.FactsNow();
            if (to.Trait("extreme") > 0f && Id(k) != to.Id || !to.Req.All(q => q.Holds(facts))) continue;
            c.Values[k] = to;
        }
        // A step that broke a later axis's rule: that axis back to a value that holds (its default where it can).
        foreach (LayerKind k in Axes)
        {
            if (!c.Values.TryGetValue(k, out WorldLayer? v)) continue;
            Dictionary<string, string> facts = c.FactsNow();
            if (v.Req.All(q => q.Holds(facts))) continue;
            var table = lib.Layers.Where(l => l.Kind == k && !l.Off && l.Req.All(q => q.Holds(facts))).OrderByDescending(l => l.Blank).ToList();
            if (table.Count > 0) c.Values[k] = table[0];
        }
        c.Sides(new Rng(Hash.Combine(c.Seed, 0x51DEUL)), keepRolled: true);
        c.Merge();
        return c;
    }

    /// <summary>
    /// Which side each role group falls to: rolled, then held by the values that say (the armed side's
    /// fighters are its own; a secluded side B keeps only the care of children; the hot side is the armed one).
    /// </summary>
    void Sides(Rng rng, bool keepRolled = false)
    {
        Merge();
        foreach (string g in Groups.Append("hot"))
            if (!keepRolled || !SideA.ContainsKey(g)) SideA[g] = rng.Fork("side." + g).Chance(0.5f);
        if (Trait("armed_a") > 0f && Trait("armed_b", 1f) <= 0f) SideA["fight"] = SideA["hot"] = true;
        if (Trait("secluded") > 0f)
            foreach (string g in Groups) SideA[g] = g != "care";
    }

    /// <summary>Rolled on a settled world after its creed (WorldRoll.Roller.Culture).</summary>
    internal void Rolled(Rng rng, Dictionary<string, string> facts)
    {
        Facts = facts;
        Lead = rng.Fork("lead").Chance(0.5f) ? "female" : "male";
        float c = rng.Fork("cover.for").Float();
        CoverFor = c < 0.7f ? "all" : c < 0.85f ? "a" : "b";
        Sides(rng);
        facts["lead"] = Lead;
        foreach (var (g, a) in SideA) facts["side." + g] = a ? "a" : "b";
    }
}

public sealed partial class WorldRoll
{
    /// <summary>The world's culture: null where nobody lives.</summary>
    public CultureReading? Culture;
    /// <summary>How desperate the world is, 0 to 1: what its rolled values' despair adds up to, with a little noise.</summary>
    public float Desperation;

    /// <summary>A share of settled worlds near today's culture (at most two axes off); the rest have four to six off.</summary>
    public const float NearDefault = 0.3f;
    /// <summary>Worlds with one extreme value at least, and with two.</summary>
    public const float OneExtreme = 1f / 8f, TwoExtremes = 1f / 60f;

    sealed partial class Roller
    {
        /// <summary>
        /// The culture, after the creed (docs/research/culture.md 4.2): how strong it is (how many axes come off
        /// the default, the axes the world leans most likeliest), how many extremes, then each axis in order:
        /// what's given, an extreme, an off-default value by its odds, or the default.
        /// </summary>
        public void Culture()
        {
            float despair = _w.Rolled.Sum(l => l.Trait("despair"));
            _w.Desperation = Math.Clamp(despair + Fork("desperation").Float(-0.1f, 0.1f), 0f, 1f);
            _w.Facts["desperation"] = _w.Desperation.ToString("F2", CultureInfo.InvariantCulture);
            var c = new CultureReading { Desperation = _w.Desperation, Seed = Hash.Combine(_seed, 0xC0170EUL) };
            Rng r = Fork("culture");
            int off = r.Chance(NearDefault) ? r.Between(0, 2) : r.Between(4, 6);
            float e = r.Float();
            int extremes = e < TwoExtremes ? 2 : e < OneExtreme ? 1 : 0;
            bool Extreme(WorldLayer l) => l.Trait("extreme") > 0f;
            IEnumerable<WorldLayer> Open(LayerKind k, bool extreme) => Table(k).Where(l => !l.Off && !l.Blank && Extreme(l) == extreme && Rules(l).Hard);
            // How much the world leans an axis: its off-default values' odds against their weights.
            float Leans(LayerKind k)
            {
                var vs = Open(k, false).ToList();
                float w = vs.Sum(l => l.Weight);
                return w > 0f ? vs.Sum(Odds) / w : 0f;
            }
            var axes = CultureReading.Axes.ToList();
            var offAxes = r.Fork("axes").WeightedOrder(axes, k => MathF.Max(1e-4f, Leans(k))).Take(off).ToList();
            var wild = r.Fork("extremes").WeightedOrder(axes.Where(k => Open(k, true).Any()).ToList(), k => Open(k, true).Sum(Odds)).Take(extremes).ToList();
            // Each extreme takes the place of an off-default axis, or is the near-default world's one.
            foreach (LayerKind k in wild)
                if (!offAxes.Contains(k))
                {
                    var plain = offAxes.Where(a => !wild.Contains(a)).ToList();
                    if (plain.Count > 0 && offAxes.Count >= Math.Max(1, off)) offAxes.Remove(plain[0]);
                    offAxes.Add(k);
                }
            foreach (LayerKind k in CultureReading.Axes)
            {
                WorldLayer? v = null;
                if (_given.First(k) is { } id && Table(k).FirstOrDefault(l => l.Id == id) is { } set) { v = set; c.Given.Add(k); }
                if (v == null && wild.Contains(k)) v = Pick(k, k.ToString(), Extreme);
                if (v == null && offAxes.Contains(k)) v = Pick(k, k.ToString(), l => !l.Blank && !Extreme(l));
                if (v == null && Table(k).FirstOrDefault(l => l.Blank) is { } blank && Rules(blank).Hard) v = blank;
                v ??= Pick(k, k + ".any", l => !Extreme(l));
                if (v == null) continue;
                Add(v);
                c.Values[k] = v;
            }
            c.Rolled(Fork("culture.sides"), _w.Facts);
            _w.Culture = c;
        }
    }
}
