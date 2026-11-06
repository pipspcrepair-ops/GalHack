using System;

namespace Planets.Gen;

/// <summary>
/// The relief recipe of a landform: how the shared terrain engine in <see cref="TerrainModel"/>
/// shapes ground where it holds. Every field is an amplitude, a cover or a multiplier, so two
/// recipes blend linearly at a border and the ground changes smoothly from one to the other.
/// Heights are metres; covers are 0-1.
/// </summary>
public struct LandformRelief
{
    // The base surface
    public float Flatten;       // replace the continent's shape with its low-passed self: nothing under ~2 km survives
    public float Compress;      // and squeeze that broad shape lower (0 keeps it, 1 takes it to the waterline): basins and plains
    public float Squash;        // squeeze heights down toward the waterline (wetlands, deltas)
    public float Lift;          // metres added inland (steppe, tablelands)
    public float Swell;         // metres of broad, slow swell

    // Hills and mountains
    public float Hill;          // metres of rolling hills
    public float Billow;        // 0 soft hills .. 1 round domes with creases
    public float ValleyFloor;   // 0..1: flatten the low ground between hills into valley floors
    public float Massif;        // metres of broad lift under a range
    public float Mountain;      // metres of ridged peaks
    public float Sharpness;     // ridge sharpness (blended with the weight)

    // Structural landforms
    public float Plateau;       // metres per tier of tablelands and mesas
    public float PlateauCover;  // share of ground raised onto the first tier
    public float Terrace;       // cover of stepped risers
    public float TerraceStep;   // metres per step
    public float Canyon;        // metres deep
    public float CanyonWidth;   // noise units
    public float CanyonFlat;    // 0 V-shaped gorge .. 1 flat floor with ledges

    // Aeolian and erosional
    public float Dune;          // metres
    public float Badland;       // metres of dense gullies
    public float Tower;         // metres: inselbergs, karst towers
    public float TowerCover;    // how much of the ground they cover
    public float Spire;         // metres of crystal spires

    // Point features (weighed where each feature stands)
    public float Cone;          // cinder cone field presence
    public float Volcano;       // big volcanoes (lava worlds) presence
    public float Ring;          // metres of ring ridges

    // Water and coast
    public float Lake;          // metres a lake basin dips below the waterline
    public float LakeCover;     // share of ground under lakes or pools
    public float Cliff;         // metres of sea cliff at the shore
    public float Fjord;         // 0..1 drowned glacial troughs
    public float Delta;         // 0..1 distributary channels and wetland
    public float Islands;       // 0..1 islands raised on the shelf (before sea level)

    // Fine detail, rivers and climate
    public float Roughness;     // multiplier on fine detail
    public float RiverArea;     // multiplier on the catchment a stream needs to count as a river
    public float Rain;          // multiplier on rainfall
    public float Meander;       // multiplier on how far rivers wander off their course
    public float Dry;           // how much drier (+) or wetter (-) the air is

    // How much of the blend has each kind of feature at all (0-1), so a cover, a step or a
    // width can be read as the average over the landforms that have it, not watered down at a border.
    public float PlateauOn, TerraceOn, CanyonOn, TowerOn, LakeOn;

    /// <summary>Adds <paramref name="w"/> times another recipe: blending is a weighted sum.</summary>
    public void Add(in LandformRelief r, float w)
    {
        Flatten += r.Flatten * w; Compress += r.Compress * w; Squash += r.Squash * w; Lift += r.Lift * w; Swell += r.Swell * w;
        Hill += r.Hill * w; Billow += r.Billow * w; ValleyFloor += r.ValleyFloor * w;
        Massif += r.Massif * w; Mountain += r.Mountain * w; Sharpness += r.Sharpness * w;
        Plateau += r.Plateau * w; PlateauCover += r.PlateauCover * w; Terrace += r.Terrace * w; TerraceStep += r.TerraceStep * w;
        Canyon += r.Canyon * w; CanyonWidth += r.CanyonWidth * w; CanyonFlat += r.CanyonFlat * w;
        Dune += r.Dune * w; Badland += r.Badland * w; Tower += r.Tower * w; TowerCover += r.TowerCover * w; Spire += r.Spire * w;
        Cone += r.Cone * w; Volcano += r.Volcano * w; Ring += r.Ring * w;
        Lake += r.Lake * w; LakeCover += r.LakeCover * w; Cliff += r.Cliff * w; Fjord += r.Fjord * w; Delta += r.Delta * w; Islands += r.Islands * w;
        Roughness += r.Roughness * w; RiverArea += r.RiverArea * w; Rain += r.Rain * w; Meander += r.Meander * w; Dry += r.Dry * w;
        PlateauOn += r.PlateauOn * w; TerraceOn += r.TerraceOn * w; CanyonOn += r.CanyonOn * w; TowerOn += r.TowerOn * w; LakeOn += r.LakeOn * w;
    }
}

/// <summary>
/// Every landform's recipe, and each archetype's version of it. Recipes are written for a
/// temperate world; an archetype keeps its own flavour by scaling them with its own numbers
/// (a fungal world's mountains are lower and softer than an ice world's, its hills are domes).
/// </summary>
public static class Landforms
{
    /// <summary>The recipe for one landform on one archetype variant.</summary>
    public static LandformRelief Recipe(Landform landform, ArchetypeDef def)
    {
        LandformRelief r = Base(landform);
        // Hills and mountains in the archetype's own height and style.
        r.Hill *= def.HillHeight;
        r.Massif *= def.MountainHeight;
        r.Mountain *= def.MountainHeight;
        r.Sharpness = def.RidgeSharpness;
        if (landform != Landform.DomeFields) r.Billow = Math.Max(r.Billow, def.Billow);
        r.Roughness *= def.Roughness;
        if (r.Dune > 0 && def.DuneHeight > 0) r.Dune = def.DuneHeight;
        if (r.Canyon > 0 && landform == Landform.Canyonlands && def.CanyonDepth > r.Canyon) r.Canyon = def.CanyonDepth;
        if (r.Spire > 0 && def.SpireHeight > 0) r.Spire = def.SpireHeight;
        if (landform == Landform.CoastalCliffs && def.ShelfHeight > 0) r.Cliff = Math.Max(r.Cliff, def.ShelfHeight * 1.2f);
        if (landform == Landform.Mesas && def.TerraceStep > 12f) r.TerraceStep = def.TerraceStep;
        if (landform == Landform.Plateau && def.PlateauHeight > 0) r.Plateau = Math.Max(r.Plateau, def.PlateauHeight * 1.8f);
        // Big volcanoes only exist on worlds that have them (lava); the recipe says where they stand.
        if (def.VolcanoSpacing <= 0) r.Volcano = 0f;
        r.PlateauOn = r.Plateau > 0 ? 1f : 0f;
        r.TerraceOn = r.Terrace > 0 ? 1f : 0f;
        r.CanyonOn = r.Canyon > 0 ? 1f : 0f;
        r.TowerOn = r.Tower > 0 ? 1f : 0f;
        r.LakeOn = r.Lake > 0 ? 1f : 0f;
        return r;
    }

    static LandformRelief Base(Landform landform) => landform switch
    {
        Landform.Plains => new() { Flatten = 0.97f, Compress = 0.85f, Swell = 5f, Hill = 0.04f, Roughness = 0.28f, RiverArea = 0.8f, Rain = 1f, Meander = 1.8f },
        Landform.Steppe => new() { Flatten = 0.97f, Compress = 0.85f, Lift = 35f, Swell = 9f, Hill = 0.04f, Roughness = 0.3f, RiverArea = 1.7f, Rain = 0.7f, Meander = 1.3f, Dry = 0.18f },
        Landform.Hills => new() { Flatten = 0.8f, Compress = 0.4f, Hill = 1f, Swell = 6f, ValleyFloor = 1f, Mountain = 0.04f, Roughness = 0.7f, RiverArea = 1f, Rain = 1.05f, Meander = 1f },
        Landform.Mountains => new() { Massif = 0.3f, Mountain = 1f, Hill = 0.35f, Roughness = 1.1f, RiverArea = 1.2f, Rain = 1.3f, Meander = 0.4f, Dry = -0.05f },
        Landform.Plateau => new() { Flatten = 0.8f, Compress = 0.35f, Plateau = 85f, PlateauCover = 0.6f, Swell = 4f, Hill = 0.08f, Roughness = 0.8f, RiverArea = 1.3f, Rain = 0.9f, Meander = 0.9f, Dry = 0.05f },
        Landform.Mesas => new() { Flatten = 0.9f, Compress = 0.45f, Plateau = 70f, PlateauCover = 0.22f, Terrace = 0.9f, TerraceStep = 34f, Roughness = 0.5f, RiverArea = 1.8f, Rain = 0.6f, Meander = 0.8f, Dry = 0.15f },
        Landform.Canyonlands => new() { Flatten = 0.8f, Compress = 0.3f, Plateau = 55f, PlateauCover = 0.8f, Canyon = 190f, CanyonWidth = 0.07f, CanyonFlat = 1f, Terrace = 0.35f, TerraceStep = 30f, Roughness = 0.9f, RiverArea = 1.5f, Rain = 0.7f, Meander = 0.6f, Dry = 0.15f },
        Landform.RiverValleys => new() { Flatten = 0.9f, Compress = 0.7f, Lift = 28f, Swell = 6f, Hill = 0.15f, Canyon = 38f, CanyonWidth = 0.2f, CanyonFlat = 0.8f, Roughness = 0.45f, RiverArea = 0.45f, Rain = 1.25f, Meander = 2.2f, Dry = -0.12f },
        Landform.DuneSea => new() { Flatten = 0.97f, Compress = 0.8f, Dune = 34f, Swell = 5f, Roughness = 0.3f, RiverArea = 4f, Rain = 0.25f, Meander = 1.4f, Dry = 0.35f },
        Landform.RockyDesert => new() { Flatten = 0.97f, Compress = 0.8f, Tower = 75f, TowerCover = 0.045f, Swell = 4f, Roughness = 0.35f, RiverArea = 3f, Rain = 0.35f, Meander = 1.2f, Dry = 0.3f },
        Landform.Badlands => new() { Flatten = 0.8f, Compress = 0.4f, Badland = 42f, Terrace = 0.4f, TerraceStep = 7f, Roughness = 1f, RiverArea = 2f, Rain = 0.5f, Meander = 0.8f, Dry = 0.2f },
        Landform.Wetlands => new() { Flatten = 1f, Squash = 0.96f, Lake = 2.2f, LakeCover = 0.25f, Swell = 1.2f, Roughness = 0.2f, RiverArea = 0.35f, Rain = 1.5f, Meander = 2.4f, Dry = -0.35f },
        Landform.LakeDistrict => new() { Flatten = 0.85f, Compress = 0.75f, Squash = 0.7f, Hill = 0.1f, Lake = 8f, LakeCover = 0.32f, Swell = 5f, Roughness = 0.7f, RiverArea = 0.6f, Rain = 1.2f, Meander = 1.4f, Dry = -0.2f },
        Landform.Delta => new() { Flatten = 1f, Squash = 1f, Delta = 1f, Lake = 1.4f, LakeCover = 0.2f, Roughness = 0.2f, RiverArea = 0.4f, Rain = 1.2f, Meander = 2.6f, Dry = -0.3f },
        Landform.IcePlain => new() { Flatten = 0.98f, Compress = 0.9f, Swell = 4f, Roughness = 0.15f, RiverArea = 3f, Rain = 0.6f, Meander = 1f },
        Landform.Fjords => new() { Massif = 0.3f, Mountain = 0.8f, Fjord = 1f, Hill = 0.2f, Roughness = 1f, RiverArea = 1f, Rain = 1.3f, Meander = 0.5f, Dry = -0.1f },
        Landform.CoastalCliffs => new() { Flatten = 0.7f, Compress = 0.3f, Cliff = 42f, Swell = 6f, Hill = 0.18f, Roughness = 0.9f, RiverArea = 1f, Rain = 1f, Meander = 1f },
        Landform.Archipelago => new() { Flatten = 0.75f, Compress = 0.6f, Islands = 1f, Volcano = 0.6f, Hill = 0.12f, Swell = 3f, Roughness = 0.4f, RiverArea = 1f, Rain = 1.1f, Meander = 1f, Dry = -0.05f },
        Landform.VolcanicField => new() { Flatten = 0.75f, Compress = 0.4f, Cone = 1f, Volcano = 1f, Terrace = 0.3f, TerraceStep = 9f, Badland = 5f, Roughness = 1.4f, RiverArea = 1.15f, Rain = 0.8f, Meander = 0.8f, Dry = 0.1f },
        Landform.BasaltPlains => new() { Flatten = 0.97f, Compress = 0.85f, Terrace = 0.65f, TerraceStep = 10f, Badland = 2f, Volcano = 0.35f, Roughness = 0.4f, RiverArea = 1.1f, Rain = 0.8f, Meander = 1f, Dry = 0.1f },
        Landform.Karst => new() { Flatten = 0.97f, Compress = 0.8f, Tower = 90f, TowerCover = 0.26f, Swell = 3f, Roughness = 0.35f, RiverArea = 0.8f, Rain = 1.3f, Meander = 1.4f, Dry = -0.15f },
        Landform.ShardFields => new() { Flatten = 0.7f, Compress = 0.3f, Spire = 130f, Hill = 0.3f, Roughness = 1f, RiverArea = 1.2f, Rain = 0.9f, Meander = 1f },
        Landform.DomeFields => new() { Flatten = 0.6f, Compress = 0.3f, Hill = 1f, Billow = 0.92f, Swell = 4f, Roughness = 0.9f, RiverArea = 0.9f, Rain = 1.1f, Meander = 1f },
        Landform.Ringlands => new() { Flatten = 0.97f, Compress = 0.8f, Ring = 24f, Swell = 3f, Roughness = 0.35f, RiverArea = 1.3f, Rain = 0.9f, Meander = 1.2f, Dry = 0.05f },
        _ => new() { Flatten = 0.9f, Roughness = 1f, RiverArea = 1f, Rain = 1f, Meander = 1f },
    };
}

/// <summary>One entry of an archetype's landform palette: how likely a world is to have it.</summary>
public readonly record struct LandformOdds(Landform Landform, float Weight);

/// <summary>
/// The landforms of one world and how much of its land each takes. About one world in five is a
/// single landform throughout (a dune world, an endless ice plain); the rest combine two to five,
/// with a dominant one, and always at least one open landform so there is room to build, drive
/// and fly low. Coastal landforms (deltas, fjords, cliffs, archipelagos) line coasts rather than
/// taking inland regions, so their share is of the coast.
/// </summary>
public sealed class LandformMix
{
    public readonly Landform[] Landforms;
    public readonly float[] Shares;

    public LandformMix(Landform[] landforms, float[] shares)
    {
        Landforms = landforms;
        Shares = shares;
    }

    public bool Single => Landforms.Length == 1;
    public Landform Dominant => Landforms[0];
    public int Count => Landforms.Length;

    public bool Has(Landform l) => Array.IndexOf(Landforms, l) >= 0;
    public float Share(Landform l)
    {
        int i = Array.IndexOf(Landforms, l);
        return i < 0 ? 0f : Shares[i];
    }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Landforms.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(LandformInfo.Name(Landforms[i])).Append(' ').Append((Shares[i] * 100).ToString("F0")).Append('%');
        }
        return sb.ToString();
    }

    /// <summary>The world's mix, from its seed, its variant's palette and its terrain modifiers.</summary>
    public static LandformMix Roll(PlanetSpec spec, ArchetypeDef def)
    {
        var rng = new Rng(Hash.Of(spec.Seed, 0x4C4D4958, (long)spec.Archetype, spec.Variant));
        LandformOdds[] palette = def.Landforms;
        if (palette == null || palette.Length == 0) return new LandformMix(new[] { Landform.Plains }, new[] { 1f });

        // One landform throughout: the variant's own single-landform worlds (an archipelago
        // world when the planet rolled the archipelago modifier).
        float singleRoll = rng.NextFloat();
        int singlePick = rng.Next(1 << 16);
        if (def.SingleLandforms.Length > 0 && singleRoll < def.SingleChance)
        {
            Landform only = spec.Has(Modifier.Archipelago) && Array.IndexOf(def.SingleLandforms, Landform.Archipelago) >= 0
                ? Landform.Archipelago
                : def.SingleLandforms[singlePick % def.SingleLandforms.Length];
            return new LandformMix(new[] { only }, new[] { 1f });
        }

        // How many: two to five, three the most common.
        float c = rng.NextFloat();
        int count = c < 0.3f ? 2 : c < 0.63f ? 3 : c < 0.87f ? 4 : 5;

        var weights = new float[LandformInfo.Count];
        foreach (LandformOdds o in palette) weights[(int)o.Landform] += o.Weight;
        // Terrain modifiers push their landform in.
        var forced = new System.Collections.Generic.List<Landform>();
        void Force(Modifier m, Landform l)
        {
            if (!spec.Has(m)) return;
            forced.Add(l);
            weights[(int)l] = Math.Max(weights[(int)l], 1f);
        }
        Force(Modifier.Archipelago, Landform.Archipelago);
        Force(Modifier.Mesas, Landform.Mesas);
        Force(Modifier.Canyons, Landform.Canyonlands);
        Force(Modifier.Highlands, Landform.Mountains);

        // The dominant landform is an inland one (or the archipelago an archipelago modifier asks for).
        var picks = new System.Collections.Generic.List<Landform>();
        Landform dominant = spec.Has(Modifier.Archipelago) && weights[(int)Landform.Archipelago] > 0f
            ? Landform.Archipelago
            : Pick(ref rng, DominantWeights(weights), l => !LandformInfo.IsCoastal(l), squared: true);
        picks.Add(dominant);
        foreach (Landform f in forced)
            if (!picks.Contains(f) && picks.Count < count) picks.Add(f);
        while (picks.Count < count)
        {
            Landform next = Pick(ref rng, weights, l => !picks.Contains(l), squared: false);
            if (picks.Contains(next)) break; // palette exhausted
            picks.Add(next);
        }

        // Ranges come with their foothills.
        if (picks.Contains(Landform.Mountains) && !picks.Contains(Landform.Hills))
        {
            int slot = picks.Count - 1;
            while (slot > 0 && (forced.Contains(picks[slot]) || picks[slot] == Landform.Mountains)) slot--;
            if (picks.Count >= 5 && slot > 0) picks[slot] = Landform.Hills;
            else picks.Add(Landform.Hills);
        }

        // At least one open landform, so there is room to build: the palette's likeliest one.
        bool open = false;
        foreach (Landform l in picks) open |= LandformInfo.Openness(l) == Openness.Open && !LandformInfo.IsCoastal(l);
        if (!open)
        {
            Landform best = Landform.Plains;
            float bestWeight = -1f;
            foreach (LandformOdds o in palette)
                if (LandformInfo.Openness(o.Landform) == Openness.Open && !LandformInfo.IsCoastal(o.Landform) && o.Weight > bestWeight)
                {
                    best = o.Landform;
                    bestWeight = o.Weight;
                }
            int slot = picks.Count - 1;
            while (slot > 0 && (forced.Contains(picks[slot]) || picks[slot] is Landform.Mountains or Landform.Hills)) slot--;
            if (slot > 0 && picks.Count >= Math.Max(count, 5)) picks[slot] = best;
            else picks.Add(best);
        }

        // Shares: the dominant takes 38-55%, the rest split what is left by their odds.
        var shares = new float[picks.Count];
        shares[0] = rng.Range(0.38f, 0.55f);
        float rest = 0f;
        for (int i = 1; i < picks.Count; i++)
        {
            shares[i] = Math.Max(0.05f, weights[(int)picks[i]]) * rng.Range(0.6f, 1.4f);
            rest += shares[i];
        }
        for (int i = 1; i < picks.Count; i++) shares[i] *= (1f - shares[0]) / rest;

        // Flat and open ground is as common as rugged ground: open landforms (rolling ones count a
        // quarter) take at least half the inland share, so the dramatic places stand out.
        float openPart = 0f, otherPart = 0f, inland = 0f;
        for (int i = 0; i < picks.Count; i++)
        {
            if (LandformInfo.IsCoastal(picks[i])) continue;
            float o = OpenWeight(picks[i]);
            inland += shares[i];
            openPart += shares[i] * o;
            otherPart += shares[i] * (1f - o);
        }
        if (inland > 0f && openPart > 0f && openPart < 0.5f * inland)
        {
            // Grow the open part and shrink the rest until the floor is met, keeping the total.
            float targetOpen = 0.5f * inland;
            float grow = targetOpen / openPart;
            float shrink = otherPart > 0f ? (inland - targetOpen) / otherPart : 1f;
            for (int i = 0; i < picks.Count; i++)
            {
                if (LandformInfo.IsCoastal(picks[i])) continue;
                float o = OpenWeight(picks[i]);
                shares[i] = shares[i] * o * grow + shares[i] * (1f - o) * shrink;
            }
        }
        // The dominant landform first, then the rest by share.
        var order = new int[picks.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) => a == b ? 0 : a == 0 ? -1 : b == 0 ? 1 : shares[b] != shares[a] ? shares[b].CompareTo(shares[a]) : a.CompareTo(b));
        var sortedPicks = new Landform[picks.Count];
        var sortedShares = new float[picks.Count];
        for (int i = 0; i < order.Length; i++)
        {
            sortedPicks[i] = picks[order[i]];
            sortedShares[i] = shares[order[i]];
        }
        return new LandformMix(sortedPicks, sortedShares);
    }

    /// <summary>Open landforms lead a world more often than rugged ones.</summary>
    static float[] DominantWeights(float[] weights)
    {
        var w = (float[])weights.Clone();
        for (int i = 0; i < w.Length; i++) w[i] *= 0.6f + 0.8f * OpenWeight((Landform)i);
        return w;
    }

    /// <summary>How much of a landform counts as open ground: open 1, rolling a quarter, rugged 0.</summary>
    static float OpenWeight(Landform l) => LandformInfo.Openness(l) switch { Openness.Open => 1f, Openness.Rolling => 0.25f, _ => 0f };

    static Landform Pick(ref Rng rng, float[] weights, Func<Landform, bool> allowed, bool squared)
    {
        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
            if (weights[i] > 0f && allowed((Landform)i)) total += squared ? weights[i] * weights[i] : weights[i];
        float r = rng.NextFloat() * total;
        Landform last = Landform.Plains;
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0f || !allowed((Landform)i)) continue;
            last = (Landform)i;
            r -= squared ? weights[i] * weights[i] : weights[i];
            if (r <= 0f) return last;
        }
        return last;
    }
}
