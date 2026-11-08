using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>How advanced a people were or are: low (timber, stone, carts and animals up to rail, diesel, mines and mills), mid (modern: highways, airliners, computers, nuclear power), high (stellar: grav vehicles, fusion, spaceports, maglevs, skyways, arcologies).</summary>
public enum TechLevel : byte { Low, Mid, High }

/// <summary>
/// A world's old starport class (the idea from the Traveller tabletop RPG, in our own words). A: a
/// great port with terminals, shipyards, refined fuel and orbital stations. B: a large port, yards for
/// smaller craft, refined fuel. C: a working port, basic repairs, raw fuel. D: a poor port, a fuel
/// depot and a few pads. E: a frontier field, a marked landing area and a beacon. None: none at all.
/// </summary>
public enum Starport : byte { None, E, D, C, B, A }

/// <summary>How many people live here now: nobody, scattered survivors, a few outposts and frontier bases, or settled country.</summary>
public enum PopulationNow : byte { None, Survivors, Outposts, Settled }

/// <summary>What a world mainly lived by before the fall.</summary>
public enum Trade : byte { Agricultural, Industrial, Mining, Research, Military, Commerce }

/// <summary>
/// A world's profile, rolled first from its seed (ideas from the Traveller tabletop RPG's world
/// profiles, with GalHack's own names): whether it was ever inhabited, how many people lived there,
/// its old starport, its main trade, and two tech levels that are independent of each other.
/// <see cref="Before"/> (pre-collapse) decides the old world: which ruins, POI families,
/// infrastructure, vehicles and decorations appear. <see cref="Now"/> (post-collapse) decides the
/// people living there now: survivors, settlements and any faction holding ground, their gear,
/// vehicles, defences and animals. Survivors often live a level below the ruins round them, and a
/// faction's high-tech base can stand on a modern town's ruins ("new on old").
/// </summary>
public sealed record WorldProfile
{
    /// <summary>Whether anyone ever settled here. Never-inhabited worlds have no old-world ruins, roads or wrecked traffic.</summary>
    public bool Inhabited { get; init; } = true;
    /// <summary>How many people lived here before the fall, as a power of ten (0 none, 6 a million, 9 billions, 10 the most crowded).</summary>
    public int Population { get; init; } = 6;
    /// <summary>The class of its main starport before the fall.</summary>
    public Starport Starport { get; init; } = Starport.C;
    /// <summary>How many lesser ports it had besides the main one: mostly none or a few, many on a big, crowded class A world.</summary>
    public int SecondaryPorts { get; init; }
    /// <summary>An exodus world: at the end as many people as possible tried to leave.</summary>
    public bool Exodus { get; init; }
    /// <summary>On an exodus world, how many scrappy improvised ports crowd round the main one, and how many evacuation camps.</summary>
    public int ScrappyPorts { get; init; }
    public int EvacuationCamps { get; init; }
    /// <summary>How many live here now, which sets how many living places there are and how big.</summary>
    public PopulationNow Current { get; init; } = PopulationNow.Settled;
    public Trade Trade { get; init; } = Trade.Industrial;
    /// <summary>The pre-collapse tech level: the old world.</summary>
    public TechLevel Before { get; init; } = TechLevel.Mid;
    /// <summary>The post-collapse tech level: the living.</summary>
    public TechLevel Now { get; init; } = TechLevel.Low;
    /// <summary>The words of a family's <c>is</c> line this world's facts rule out, from its roll: no open streets or farms where the air was never fit to build under.</summary>
    public IReadOnlySet<string> Barred { get; init; } = Nothing;
    /// <summary>How long ago and how hard the world fell, for its places' decay (from its roll; null: a century of neglect).</summary>
    public RuinAge? Ruin { get; init; }
    /// <summary>The colours its made things take, faded by its years and its fall (from its roll; null: as built).</summary>
    public WorldPaint? Paint { get; init; }
    static readonly HashSet<string> Nothing = new();

    /// <summary>The data tag of a level (low, mid, high), as families' variants and the deco library name them.</summary>
    public static string Tag(TechLevel t) => t switch { TechLevel.Low => "low", TechLevel.High => "high", _ => "mid" };

    public static TechLevel Parse(string tag) => tag switch { "low" => TechLevel.Low, "high" => TechLevel.High, _ => TechLevel.Mid };

    public override string ToString() =>
        Inhabited ? $"inhabited, population {Population}{(Exodus ? ", exodus" : "")}, class {Starport} starport{(SecondaryPorts > 0 ? $" and {SecondaryPorts} more" : "")}, {Trade}, tech {Tag(Before)} before the fall and {Tag(Now)} now, {Current} now" : $"never inhabited, tech {Tag(Now)} now";

    // ---- what is living and what is old ---------------------------------------------------------

    /// <summary>A place people live in or hold now (a settlement, or a faction's place it still holds): it is built at the post-collapse level.</summary>
    public static bool Living(FamilyDef f, PoiState state) =>
        f.Tags.Contains("settlement") || (ThemeDef.FactionOwned(f) && state == PoiState.Active);

    /// <summary>Places no one from this world built: alien and precursor sites, and wrecks that fell from the sky. Any world can have them.</summary>
    public static bool Unbuilt(FamilyDef f) => f.Tags.Contains("alien") || f.Tags.Contains("precursor") || f.Tags.Contains("wreck");

    /// <summary>Built by survivors after the fall (a scrap fort, a camp), lived in or not: it is of the living's age, never the old world's.</summary>
    public static bool SurvivorBuilt(FamilyDef f) => f.Tags.Contains("settlement") || f.Tags.Contains("survivor");

    /// <summary>The tech level a place in a state is built at: the living (and whatever survivors built) at <see cref="Now"/>, the old world at <see cref="Before"/>.</summary>
    public TechLevel LevelFor(FamilyDef f, PoiState state) => Living(f, state) || SurvivorBuilt(f) ? Now : Before;

    /// <summary>Whether a family's tech fits this world in any state it can take (its data tech line, any when empty).</summary>
    public bool TechFits(FamilyDef f) =>
        Unbuilt(f) || f.Techs.Count == 0 || Enumerable.Range(0, f.StateWeights.Length).Any(s => f.StateWeights[s] > 0f && f.Techs.Contains(Tag(LevelFor(f, (PoiState)s))));

    /// <summary>
    /// What stands for a family on this world: the family itself where its tech fits, else the first of its
    /// <c>instead</c> line that does (its age's equivalent: a caravan camp where a low-tech world has no launch
    /// field), else null. By rule, so any fall, theme or plan that asks for it gets the right thing.
    /// </summary>
    public FamilyDef? ForAge(PoiLibrary lib, FamilyDef f, ulong salt = 0)
    {
        if (TechFits(f)) return f;
        var fits = f.Instead.Select(id => lib.Families.FirstOrDefault(x => x.Id == id)).Where(x => x != null && TechFits(x)).ToList();
        return fits.Count == 0 ? null : fits[(int)(Hash.Mix(salt) % (ulong)fits.Count)];
    }

    /// <summary>
    /// Whether a family can stand here in a state: its tech levels (data: tech, any when empty)
    /// include the level that state is built at; old-world places only where anyone ever lived; a
    /// spaceport ruin only where there was a starport; and the living only as far as how many live
    /// here now allows.
    /// </summary>
    public bool Fits(FamilyDef f, PoiState state)
    {
        if (Unbuilt(f)) return true;
        if (f.Is.Any(Barred.Contains)) return false;
        bool living = Living(f, state);
        if (!living && !Inhabited) return false;
        if (f.Tier == PoiTier.Spaceport && !living && Starport == Starport.None) return false;
        if (living && !LivingFits(f)) return false;
        return f.Techs.Count == 0 || f.Techs.Contains(Tag(LevelFor(f, state)));
    }

    /// <summary>Whether this many people could keep a living place of this size: hamlets for survivors, nothing past a base for outposts.</summary>
    public bool LivingFits(FamilyDef f) => Current switch
    {
        PopulationNow.None => false,
        PopulationNow.Survivors => !ThemeDef.FactionOwned(f) && f.Tier <= PoiTier.Camp,
        PopulationNow.Outposts => f.Tier <= PoiTier.Base,
        _ => true,
    };

    /// <summary>The most living places a world this empty has: a few hamlets, a few outposts, or no limit.</summary>
    public int LivingCap => Current switch { PopulationNow.None => 0, PopulationNow.Survivors => 3, PopulationNow.Outposts => 6, _ => int.MaxValue };

    /// <summary>The states a family can be in here, by their weights (zero where it can't stand in that state).</summary>
    public float[] StateWeights(FamilyDef f)
    {
        var w = (float[])f.StateWeights.Clone();
        for (int s = 0; s < w.Length; s++) if (!Fits(f, (PoiState)s)) w[s] = 0f;
        return w;
    }

    /// <summary>
    /// How much more (or less) often a family turns up for this world's trade and old population:
    /// farms on agricultural worlds, mines on mining worlds, labs on research worlds; cities and
    /// big civic places where many people lived, fewer where few did.
    /// </summary>
    public float Weight(FamilyDef f)
    {
        float w = 1f;
        bool Has(string t) => f.Tags.Contains(t);
        switch (Trade)
        {
            case Trade.Agricultural: if (Has("farm")) w *= 2.5f; break;
            case Trade.Industrial: if (Has("industry")) w *= 2.5f; break;
            case Trade.Mining: if (Has("mine") || Has("ore")) w *= 2.5f; break;
            case Trade.Research: if (Has("science") || Has("medical")) w *= 2.2f; break;
            case Trade.Military: if (Has("military")) w *= 2.2f; break;
            case Trade.Commerce: if (Has("trade") || Has("civilian")) w *= 2.2f; break;
        }
        if (Inhabited && !Unbuilt(f) && (Has("city") || f.Tier is PoiTier.City or PoiTier.Town))
            w *= Math.Clamp(0.4f + Population * 0.12f, 0.4f, 1.6f);
        // Where few live on, the frontier base is what there is.
        if (Has("frontier") && Current == PopulationNow.Outposts) w *= 3f;
        return w;
    }

    /// <summary>The variant of a family for a level (and, for a spaceport, a starport class, the world's own unless named): one built for it, else any.</summary>
    public int Variant(FamilyDef f, PoiState state, Rng rng, Starport? port = null)
    {
        Starport wanted = port ?? Starport;
        string tag = Tag(LevelFor(f, state));
        var fitting = f.Variants.Where(v => v.Tech.Length == 0 || v.Tech == tag).ToList();
        if (fitting.Count == 0) fitting = f.Variants;
        if (f.Tier == PoiTier.Spaceport && fitting.Any(v => v.Port != null))
        {
            // The class picks the spaceport: the variant built for it, or the nearest.
            var bySize = fitting.Where(v => v.Port != null).OrderBy(v => Math.Abs((int)v.Port!.Value - (int)wanted)).ToList();
            int best = Math.Abs((int)bySize[0].Port!.Value - (int)wanted);
            fitting = bySize.Where(v => Math.Abs((int)v.Port!.Value - (int)wanted) == best).ToList();
        }
        return fitting[rng.Range(0, fitting.Count)].Index;
    }

    /// <summary>
    /// "New on old": a place the living hold, built at a higher level than the ruins it stands on.
    /// The world round it shows the old level (flavour ruins, roads, wrecked traffic); the place itself the new.
    /// </summary>
    public bool NewOnOld(FamilyDef f, PoiState state) => Inhabited && Living(f, state) && !Unbuilt(f) && Now > Before;

    // ---- rolling one ----------------------------------------------------------------------------

    /// <summary>
    /// A world's profile from its seed and kind (archetype, as the planet names it: temperate, arid,
    /// ice, lava, alien...). Start worlds were always inhabited. Neighbouring seeds differ widely:
    /// colonies developed unevenly.
    /// </summary>
    public static WorldProfile Roll(ulong seed, string archetype, bool start = false)
    {
        var rng = new Rng(Hash.Combine(seed, 0x50F11EUL));
        string a = archetype.ToLowerInvariant();
        int D6() => rng.Range(1, 7);
        // Harsh worlds were settled less often and less densely.
        int harsh = a switch { "temperate" => 0, "arid" => 1, "ice" => 2, "lava" => 3, "alien" => 2, _ => 1 };
        float never = a switch { "temperate" => 0.06f, "arid" => 0.12f, "ice" => 0.2f, "lava" => 0.3f, "alien" => 0.4f, _ => 0.15f };
        bool inhabited = start || !rng.Chance(never);
        int pop = inhabited ? Math.Clamp(D6() + D6() - 2 - harsh + (start ? 1 : 0), 1, 10) : 0;
        Trade trade = a switch
        {
            "temperate" => rng.Pick(new[] { Trade.Agricultural, Trade.Agricultural, Trade.Industrial, Trade.Commerce, Trade.Research, Trade.Military }),
            "arid" => rng.Pick(new[] { Trade.Mining, Trade.Mining, Trade.Industrial, Trade.Agricultural, Trade.Military }),
            "ice" => rng.Pick(new[] { Trade.Research, Trade.Research, Trade.Mining, Trade.Military }),
            "lava" => rng.Pick(new[] { Trade.Mining, Trade.Mining, Trade.Industrial, Trade.Research }),
            "alien" => rng.Pick(new[] { Trade.Research, Trade.Research, Trade.Mining, Trade.Commerce }),
            _ => rng.Pick(Enum.GetValues<Trade>()),
        };
        // The old world's level: what its people and trade could support, and luck.
        int before = D6() + (pop >= 8 ? 2 : pop >= 5 ? 1 : pop <= 2 ? -1 : 0) + (trade is Trade.Industrial or Trade.Research ? 1 : trade == Trade.Agricultural ? -1 : 0);
        TechLevel old = before <= 2 ? TechLevel.Low : before <= 5 ? TechLevel.Mid : TechLevel.High;
        // Its starport: what those people and that level could build. A and B took a great many people
        // and high or modern tech; a low-tech world had a poor port, a frontier field or none.
        int portRoll = D6() + D6() + (pop - 5) / 2 + (old == TechLevel.High ? 2 : old == TechLevel.Low ? -2 : 0);
        Starport port = !inhabited ? Starport.None : portRoll switch
        {
            <= 3 => Starport.None, <= 5 => Starport.E, <= 7 => Starport.D, <= 9 => Starport.C, <= 11 => Starport.B, _ => Starport.A,
        };
        if (port == Starport.A && (pop < 8 || old != TechLevel.High)) port = Starport.B;
        if (port == Starport.B && (pop < 6 || old == TechLevel.Low)) port = Starport.C;
        if (old == TechLevel.Low && port > Starport.D) port = Starport.D;
        // An exodus: a crowded world that tried to leave, about one inhabited world in six.
        bool exodus = inhabited && pop >= 6 && rng.Chance(0.5f);
        if (exodus && port < Starport.D) port = Starport.D;
        int secondary = port switch
        {
            Starport.None or Starport.E => 0,
            Starport.D => rng.Chance(0.2f) ? 1 : 0,
            Starport.C => rng.Range(0, 2),
            Starport.B => rng.Range(0, 3),
            _ => pop >= 9 ? rng.Range(3, 8) : rng.Range(1, 4),
        };
        int scrappy = exodus ? rng.Range(6, 12) : 0, camps = exodus ? rng.Range(3, 6) : 0;
        // Who lives here now. Once-busy worlds usually sit at the low end, and an exodus emptied its world.
        int left = rng.Range(0, 10) - (pop >= 8 ? 3 : 0);
        PopulationNow current = left switch { < 1 => PopulationNow.None, < 3 => PopulationNow.Survivors, < 5 => PopulationNow.Outposts, _ => PopulationNow.Settled };
        if (exodus) current = rng.Pick(new[] { PopulationNow.None, PopulationNow.None, PopulationNow.None, PopulationNow.Survivors, PopulationNow.Survivors, PopulationNow.Outposts });
        // Nobody ever settled here, so whoever's here came lately: outposts at most. A start world always has someone to find.
        if (!inhabited) current = PopulationNow.Outposts;
        if (start) current = (PopulationNow)Math.Clamp((int)current, (int)PopulationNow.Survivors, (int)PopulationNow.Outposts);
        // The living: survivors usually fall a level or two below the ruins; now and then a faction
        // brings stellar tech to a world whose ruins are older (never on a start world, where no faction holds ground).
        int drop = rng.Range(0, 3);
        TechLevel now = (TechLevel)Math.Max(0, (int)old - drop);
        if (!start && rng.Chance(0.15f)) now = TechLevel.High;
        if (!inhabited) now = rng.Chance(0.35f) ? TechLevel.High : TechLevel.Low;
        return new WorldProfile
        {
            Inhabited = inhabited, Population = pop, Starport = port, SecondaryPorts = secondary, Exodus = exodus, ScrappyPorts = scrappy, EvacuationCamps = camps,
            Current = current, Trade = trade, Before = inhabited ? old : TechLevel.Low, Now = now,
        };
    }
}
