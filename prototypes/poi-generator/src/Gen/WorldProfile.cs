using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>How advanced a people were or are: low (timber, stone, carts and animals up to rail, diesel, mines and mills), mid (modern: highways, airliners, computers, nuclear power), high (stellar: grav vehicles, fusion, spaceports, maglevs, skyways, arcologies).</summary>
public enum TechLevel : byte { Low, Mid, High }

/// <summary>The size of a world's old starport, which sets how big its spaceport ruins are.</summary>
public enum Starport : byte { None, Strip, Field, Port, Major, Grand }

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
    public Starport Starport { get; init; } = Starport.Port;
    public Trade Trade { get; init; } = Trade.Industrial;
    /// <summary>The pre-collapse tech level: the old world.</summary>
    public TechLevel Before { get; init; } = TechLevel.Mid;
    /// <summary>The post-collapse tech level: the living.</summary>
    public TechLevel Now { get; init; } = TechLevel.Low;

    /// <summary>The data tag of a level (low, mid, high), as families' variants and the deco library name them.</summary>
    public static string Tag(TechLevel t) => t switch { TechLevel.Low => "low", TechLevel.High => "high", _ => "mid" };

    public static TechLevel Parse(string tag) => tag switch { "low" => TechLevel.Low, "high" => TechLevel.High, _ => TechLevel.Mid };

    public override string ToString() =>
        Inhabited ? $"inhabited, population {Population}, {Starport} starport, {Trade}, tech {Tag(Before)} before the fall and {Tag(Now)} now" : $"never inhabited, tech {Tag(Now)} now";

    // ---- what is living and what is old ---------------------------------------------------------

    /// <summary>A place people live in or hold now (a settlement, or a faction's place it still holds): it is built at the post-collapse level.</summary>
    public static bool Living(FamilyDef f, PoiState state) =>
        f.Tags.Contains("settlement") || (ThemeDef.FactionOwned(f) && state == PoiState.Active);

    /// <summary>Places no one from this world built: alien and precursor sites, and wrecks that fell from the sky. Any world can have them.</summary>
    public static bool Unbuilt(FamilyDef f) => f.Tags.Contains("alien") || f.Tags.Contains("precursor") || f.Tags.Contains("wreck");

    /// <summary>The tech level a place in a state is built at: the living at <see cref="Now"/>, the old world at <see cref="Before"/>.</summary>
    public TechLevel LevelFor(FamilyDef f, PoiState state) => Living(f, state) ? Now : Before;

    /// <summary>
    /// Whether a family can stand here in a state: its tech levels (data: tech, any when empty)
    /// include the level that state is built at; old-world places only where anyone ever lived; a
    /// spaceport ruin only where the old starport was a port or bigger.
    /// </summary>
    public bool Fits(FamilyDef f, PoiState state)
    {
        if (Unbuilt(f)) return true;
        bool living = Living(f, state);
        if (!living && !Inhabited) return false;
        if (f.Tier == PoiTier.Spaceport && !living && Starport < Starport.Field) return false;
        TechLevel level = living ? Now : Before;
        return f.Techs.Count == 0 || f.Techs.Contains(Tag(level));
    }

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
        return w;
    }

    /// <summary>The variant of a family for a level (and, for a spaceport, the old starport's size): one built for it, else any.</summary>
    public int Variant(FamilyDef f, PoiState state, Rng rng)
    {
        string tag = Tag(LevelFor(f, state));
        var fitting = f.Variants.Where(v => v.Tech.Length == 0 || v.Tech == tag).ToList();
        if (fitting.Count == 0) fitting = f.Variants;
        if (f.Tier == PoiTier.Spaceport && fitting.Any(v => v.Port != null))
        {
            // The old starport's size picks the spaceport: the variant built for the nearest size.
            var bySize = fitting.Where(v => v.Port != null).OrderBy(v => Math.Abs((int)v.Port!.Value - (int)Starport)).ToList();
            int best = Math.Abs((int)bySize[0].Port!.Value - (int)Starport);
            fitting = bySize.Where(v => Math.Abs((int)v.Port!.Value - (int)Starport) == best).ToList();
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
        int portRoll = D6() + D6() + (pop - 5) / 2;
        Starport port = !inhabited ? Starport.None : portRoll switch
        {
            <= 3 => Starport.None, <= 5 => Starport.Strip, <= 7 => Starport.Field, <= 9 => Starport.Port, <= 11 => Starport.Major, _ => Starport.Grand,
        };
        Trade trade = a switch
        {
            "temperate" => rng.Pick(new[] { Trade.Agricultural, Trade.Agricultural, Trade.Industrial, Trade.Commerce, Trade.Research, Trade.Military }),
            "arid" => rng.Pick(new[] { Trade.Mining, Trade.Mining, Trade.Industrial, Trade.Agricultural, Trade.Military }),
            "ice" => rng.Pick(new[] { Trade.Research, Trade.Research, Trade.Mining, Trade.Military }),
            "lava" => rng.Pick(new[] { Trade.Mining, Trade.Mining, Trade.Industrial, Trade.Research }),
            "alien" => rng.Pick(new[] { Trade.Research, Trade.Research, Trade.Mining, Trade.Commerce }),
            _ => rng.Pick(Enum.GetValues<Trade>()),
        };
        // The old world's level: what its starport, people and trade could support, and luck.
        int before = D6() + (int)port - 1 + (pop >= 8 ? 1 : pop <= 2 ? -1 : 0) + (trade is Trade.Industrial or Trade.Research ? 1 : trade == Trade.Agricultural ? -1 : 0);
        TechLevel old = before <= 3 ? TechLevel.Low : before <= 5 ? TechLevel.Mid : TechLevel.High;
        // The living: survivors usually fall a level or two below the ruins; now and then a faction
        // brings stellar tech to a world whose ruins are older (never on a start world, where no faction holds ground).
        int drop = rng.Range(0, 3);
        TechLevel now = (TechLevel)Math.Max(0, (int)old - drop);
        if (!start && rng.Chance(0.15f)) now = TechLevel.High;
        if (!inhabited) now = rng.Chance(0.35f) ? TechLevel.High : TechLevel.Low;
        return new WorldProfile { Inhabited = inhabited, Population = pop, Starport = port, Trade = trade, Before = inhabited ? old : TechLevel.Low, Now = now };
    }
}
