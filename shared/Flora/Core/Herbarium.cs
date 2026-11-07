using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GalHack.Plants;

/// <summary>
/// What a world's plants have in common: the pigments its sunlight and air push them to, the bark
/// they share, the build its climate favours, and which body plans have won here.
/// </summary>
public sealed class FloraStyle
{
    public Tone[] Leaves, Barks, Blooms, Caps;
    public Tone Stalk;

    /// <summary>Multipliers on height, droop, how flat crowns are and how slender stems are.</summary>
    public float Stature, Droop, Flat, Slender;

    /// <summary>A lean most of this world's trees share (its wind), and the way it runs.</summary>
    public float Lean, LeanAngle;
}

/// <summary>
/// The source of every plant on a world. Rolled once from a <see cref="FloraClimate"/> and the
/// <see cref="Habitat"/>s the world has: each family gets a small pool of species with genomes,
/// colours and bounded meshes, and each habitat a few of them, spread along a region axis so a
/// walker meets different plants as they go. Scatter, ruins, ground cover, weeds, farms and maps all
/// read from here, and nothing else decides what a plant looks like. Pure and deterministic:
/// the same climate and habitats roll the same plants, point for point, on any machine. Read-only
/// once rolled, so any thread may read it.
/// </summary>
public sealed class Herbarium
{
    /// <summary>Near meshes a species has, so mesh variety stays bounded and instanced.</summary>
    public const int NearMeshes = 2;

    public FloraClimate Climate { get; }
    public FloraStyle Style { get; }
    public IReadOnlyList<Plant> Plants => _plants;

    /// <summary>Nothing grows here.</summary>
    public bool Barren => _plants.Count == 0;

    /// <summary>The colour green land takes on this world (whatever its green is), or null where nothing grows.</summary>
    public Tone? Verdure => Barren ? null : Style.Leaves[0] * 1.08f;

    readonly List<Plant> _plants = new();
    readonly Dictionary<(int, PlantFamily), Stand> _stands = new();

    /// <summary>One habitat's plants of one family, each at its own place along the region axis.</summary>
    sealed class Stand
    {
        public Plant[] Plants;
        public float[] Centre;
    }

    /// <summary>How wide a species' range is along the region axis: wide enough that neighbours mix, narrow enough that each region has its own.</summary>
    const float RegionWidth = 0.2f;

    Herbarium(FloraClimate climate, FloraStyle style)
    {
        Climate = climate;
        Style = style;
    }

    /// <summary>The families planted, sized: how many species a world keeps of each.</summary>
    static int PoolSize(PlantFamily f) => f switch
    {
        PlantFamily.Conifer => 4, PlantFamily.Broadleaf => 5, PlantFamily.Jungle => 4, PlantFamily.Palm => 3, PlantFamily.Shrub => 5,
        PlantFamily.Cactus => 3, PlantFamily.Fungus => 3, PlantFamily.GiantFungus => 4, PlantFamily.Crystal => 2, PlantFamily.Ice => 2,
        PlantFamily.Grass => 3, PlantFamily.Forb => 3, PlantFamily.Crop => 3, _ => 2,
    };

    public static Herbarium Roll(FloraClimate c, IEnumerable<Habitat> habitats)
    {
        var dice = new Dice(Dice.Of(c.Seed, 0x464C4F52));
        var book = new Herbarium(c, RollStyle(c, dice));
        if (c.Life <= 0f || c.Air <= 0f) return book;

        List<Habitat> all = habitats.ToList();
        var wanted = new SortedDictionary<PlantFamily, List<Habitat>>();
        foreach (Habitat h in all)
            foreach (PlantFamily f in h.Families.Distinct())
            {
                if (!wanted.TryGetValue(f, out List<Habitat> list)) wanted[f] = list = new List<Habitat>();
                list.Add(h);
            }
        if (c.Life >= 0.3f) wanted[PlantFamily.Crop] = new List<Habitat>();

        foreach (var (family, homes) in wanted)
        {
            var d = new Dice(Dice.Of(c.Seed, 0x53504543, (long)family));
            // A world keeps only some of a family's body plans: the heart of why two worlds differ.
            (PlantForm Form, float Weight)[] open = Forms(family, c);
            var kept = open.Select(o => (o.Form, Weight: o.Weight * (0.15f + 3f * d.Unit() * d.Unit()))).OrderByDescending(o => o.Weight).Take(Math.Max(2, open.Length / 2 + 1)).ToList();
            int size = Math.Max(2, (int)MathF.Round(PoolSize(family) * MathF.Min(1f, 0.5f + c.Life * 0.7f)));
            var used = new List<PlantForm>();
            for (int i = 0; i < size; i++)
            {
                Habitat home = homes.Count == 0 ? null : homes[d.Int(0, homes.Count - 1)];
                float warm = Math.Clamp((home?.Warmth ?? c.Warmth) + d.Range(-0.08f, 0.08f), 0f, 1f);
                float wet = Math.Clamp((home?.Wetness ?? c.Wetness) + d.Range(-0.08f, 0.08f), 0f, 1f);
                PlantForm form = PickForm(kept, used, warm, wet, d);
                used.Add(form);
                book._plants.Add(Make(book, family, form, warm, wet, d));
            }
            foreach (Habitat home in homes) book.Settle(home, family);
        }
        return book;
    }

    static PlantForm PickForm(List<(PlantForm Form, float Weight)> kept, List<PlantForm> used, float warm, float wet, Dice d)
    {
        float total = 0f;
        var weights = new float[kept.Count];
        for (int i = 0; i < kept.Count; i++)
        {
            // Fit the niche, and don't repeat a form while another remains.
            weights[i] = kept[i].Weight * Fit(kept[i].Form, warm, wet) * (used.Contains(kept[i].Form) ? 0.06f : 1f);
            total += weights[i];
        }
        float roll = d.Unit() * total;
        for (int i = 0; i < kept.Count; i++)
            if ((roll -= weights[i]) < 0f) return kept[i].Form;
        return kept[^1].Form;
    }

    /// <summary>How well a form suits its niche: spires for the cold, bottles for the dry, curtains for the wet.</summary>
    static float Fit(PlantForm form, float warm, float wet)
    {
        float cold = 1f - warm, dry = 1f - wet;
        return form switch
        {
            PlantForm.Spire or PlantForm.Spindle => 0.4f + 2f * cold,
            PlantForm.Pagoda => 0.5f + 1.2f * cold + wet,
            PlantForm.Umbrella or PlantForm.SpikeTree => 0.3f + 2f * dry + warm,
            PlantForm.Bottle or PlantForm.Rosette or PlantForm.Thorn => 0.15f + 2.5f * dry,
            PlantForm.Weeping or PlantForm.Fern => 0.2f + 2.5f * wet,
            PlantForm.Palm or PlantForm.FanPalm => 0.3f + 2f * warm,
            _ => 1f,
        };
    }

    /// <summary>The body plans open to a family on a world: alien worlds trade the familiar for the strange.</summary>
    static (PlantForm, float)[] Forms(PlantFamily family, FloraClimate c)
    {
        float a = c.Alien, known = 1f - 0.82f * a;
        return family switch
        {
            PlantFamily.Conifer => new[] { (PlantForm.Spire, 3f * known), (PlantForm.Pagoda, 2.2f * known), (PlantForm.Spindle, 1.8f * known), (PlantForm.Stacked, 1f + 3f * a), (PlantForm.Pole, 1f), (PlantForm.CloudPads, 1.3f * known), (PlantForm.Antler, 4f * a) },
            PlantFamily.Broadleaf => new[] { (PlantForm.Domed, 3f * known), (PlantForm.Umbrella, 2f), (PlantForm.Weeping, 1.6f), (PlantForm.CloudPads, 1.5f * known), (PlantForm.Spindle, 0.8f * known), (PlantForm.Bottle, 1f), (PlantForm.Pole, 0.6f + a), (PlantForm.Antler, 4f * a), (PlantForm.Lantern, 4f * a), (PlantForm.Stacked, 3f * a) },
            PlantFamily.Jungle => new[] { (PlantForm.Domed, 2.4f * known), (PlantForm.Umbrella, 2.4f), (PlantForm.Weeping, 1.2f), (PlantForm.Pole, 1.6f), (PlantForm.FanPalm, 1f), (PlantForm.CloudPads, 1f * known), (PlantForm.Snag, 0.5f), (PlantForm.Lantern, 3f * a), (PlantForm.Antler, 2f * a) },
            PlantFamily.Palm => new[] { (PlantForm.Palm, 3.5f * known + 0.6f), (PlantForm.FanPalm, 1.8f), (PlantForm.SpikeTree, 1.6f), (PlantForm.Bottle, 1f), (PlantForm.Pole, 0.8f), (PlantForm.Lantern, 3f * a) },
            PlantFamily.Cactus => new[] { (PlantForm.Candelabra, 3f), (PlantForm.Barrel, 2f), (PlantForm.Whips, 1.6f), (PlantForm.Paddles, 2f), (PlantForm.SpikeTree, 1f) },
            PlantFamily.GiantFungus => new[] { (PlantForm.Capped, 3f), (PlantForm.Parasol, 2f), (PlantForm.Trumpet, 1.6f), (PlantForm.Organ, 1.6f) },
            PlantFamily.Fungus => new[] { (PlantForm.Capped, 3f), (PlantForm.Trumpet, 1.6f), (PlantForm.Organ, 1.6f) },
            PlantFamily.Shrub => new[] { (PlantForm.Bush, 4f), (PlantForm.Fern, 1.6f), (PlantForm.Thorn, 1.6f), (PlantForm.Rosette, 1.4f) },
            PlantFamily.Crystal or PlantFamily.Ice => new[] { (PlantForm.Shards, 1f) },
            PlantFamily.Grass => new[] { (PlantForm.Tussock, 1f) },
            PlantFamily.Forb => new[] { (PlantForm.Flowers, 1f) },
            PlantFamily.Twig => new[] { (PlantForm.Twigs, 1f) },
            PlantFamily.Reed => new[] { (PlantForm.Reeds, 1f) },
            PlantFamily.Moss => new[] { (PlantForm.Lichen, 1f) },
            PlantFamily.Pins => new[] { (PlantForm.PinStalks, 1f) },
            _ => new[] { (PlantForm.GrainCrop, 1f), (PlantForm.LeafCrop, 1f), (PlantForm.VineCrop, 1f) },
        };
    }

    /// <summary>A habitat keeps up to three of a family's species, the nearest its own climate, each with a place on the region axis.</summary>
    void Settle(Habitat home, PlantFamily family)
    {
        var d = new Dice(Dice.Of(Climate.Seed, 0x53544E44, home.Id * 64L + (long)family));
        Plant[] chosen = _plants.Where(p => p.Family == family)
            .OrderBy(p => MathF.Abs(p.Warmth - home.Warmth) + MathF.Abs(p.Wetness - home.Wetness) + d.Unit() * 0.5f - (p.Flowers ? 0.3f : 0f))
            .Take(3).ToArray();
        float start = d.Unit();
        var centre = new float[chosen.Length];
        for (int i = 0; i < chosen.Length; i++) centre[i] = (start + i / (float)chosen.Length) % 1f;
        _stands[(home.Id, family)] = new Stand { Plants = chosen, Centre = centre };
    }

    /// <summary>
    /// The plant of a family growing at a spot, or null if there is none here. Region is 0..1 and
    /// wraps, changing slowly over the land; roll is 0..1 and differs from plant to plant. Species fade
    /// into each other along the region axis, so there is never a hard line.
    /// </summary>
    public Plant Pick(int habitat, PlantFamily family, float region, float roll)
    {
        if (!_stands.TryGetValue((habitat, family), out Stand s)) return null;
        if (s.Plants.Length == 1) return s.Plants[0];
        Span<float> weight = stackalloc float[s.Plants.Length];
        float total = 0f;
        for (int i = 0; i < weight.Length; i++)
        {
            float gap = MathF.Abs(region - s.Centre[i]);
            gap = MathF.Min(gap, 1f - gap) / RegionWidth;
            weight[i] = MathF.Exp(-gap * gap) + 0.015f;
            total += weight[i];
        }
        roll *= total;
        for (int i = 0; i < weight.Length; i++)
            if ((roll -= weight[i]) < 0f) return s.Plants[i];
        return s.Plants[^1];
    }

    /// <summary>A world's plants of one family: all its crops, say, or its grasses for a map to show.</summary>
    public IEnumerable<Plant> Of(PlantFamily family) => _plants.Where(p => p.Family == family);

    /// <summary>A hash of everything rolled: two equal fingerprints mean the same plants, point for point.</summary>
    public ulong Fingerprint()
    {
        ulong h = 1469598103934665603UL;
        void Add(float f) => h = (h ^ BitConverter.SingleToUInt32Bits(f)) * 1099511628211UL;
        foreach (Plant p in _plants)
        {
            Add((float)p.Form);
            foreach (float f in p.Genome.Vector()) Add(f);
            Add(p.Leaf.R); Add(p.Leaf.G); Add(p.Leaf.B);
            foreach (PlantMesh m in p.Near)
                foreach (Vector3 v in m.Positions) { Add(v.X); Add(v.Y); Add(v.Z); }
        }
        return h;
    }

    // ---- The world's style --------------------------------------------------------------------

    static FloraStyle RollStyle(FloraClimate c, Dice d)
    {
        // Leaves answer the light: under a red star they go dark and take all they can get; under a hard white
        // one they turn pale and throw light back. Alien chemistry may be anything, and only that earns odd hues.
        float warmLight = c.Light.R - c.Light.B;
        (float H, float S, float V)[] pigments =
            warmLight > 0.55f ? new[] { (335f, 0.5f, 0.3f), (290f, 0.38f, 0.28f), (15f, 0.55f, 0.3f), (90f, 0.38f, 0.22f) }
            : warmLight > 0.3f ? new[] { (36f, 0.62f, 0.44f), (14f, 0.58f, 0.38f), (78f, 0.5f, 0.32f), (348f, 0.42f, 0.34f), (122f, 0.42f, 0.27f), (164f, 0.4f, 0.29f), (56f, 0.55f, 0.4f) }
            : warmLight < -0.12f ? new[] { (168f, 0.36f, 0.42f), (196f, 0.32f, 0.46f), (138f, 0.42f, 0.38f), (22f, 0.5f, 0.46f) }
            : new[] { (96f, 0.62f, 0.4f), (122f, 0.52f, 0.33f), (76f, 0.66f, 0.46f), (154f, 0.5f, 0.32f), (172f, 0.42f, 0.34f), (52f, 0.58f, 0.46f), (108f, 0.42f, 0.3f) };
        // A world that suits life as we know it stays green under any ordinary sun, in a wide range of greens,
        // warmer under a warm star and cooler under a hard one. Only a rare one goes its own way.
        bool familiar = Familiar(c);
        if (familiar && d.Chance(0.88f))
        {
            pigments = new[] { (96f, 0.62f, 0.4f), (122f, 0.52f, 0.33f), (78f, 0.66f, 0.44f), (146f, 0.5f, 0.32f), (164f, 0.44f, 0.33f), (108f, 0.42f, 0.3f), (88f, 0.5f, 0.36f), (132f, 0.58f, 0.27f) };
            warmLight = Math.Clamp(warmLight, -0.2f, 0.3f);
        }
        else familiar = false;
        (float H, float S, float V) p = d.Pick(pigments);
        p.H += d.Range(-9f, 9f) - (familiar ? warmLight * 20f : 0f);
        if (c.Alien > 0.4f)
        {
            p = (d.Range(0f, 360f), d.Range(0.34f, 0.62f), d.Range(0.34f, 0.56f));
        }
        // Cold greys and darkens them; drought bleaches them; thick wet air deepens them; thin air pales them.
        float cold = 1f - c.Warmth, dry = 1f - c.Wetness;
        p.S *= 1f - 0.25f * cold - 0.28f * dry;
        p.V *= (1f + 0.22f * dry) * (c.Wetness > 0.6f ? 0.92f : 1f) * (c.Air < 0.7f ? 1.12f : 1f);
        if (c.Air < 0.7f) p.S *= 0.82f;
        float swing = d.Range(14f, 34f) * (d.Chance(0.5f) ? 1f : -1f) * (c.Alien > 0.4f ? 2f : 1f);
        var leaves = new[]
        {
            Tone.Hsv(p.H, p.S, p.V),
            Tone.Hsv(p.H + swing, p.S * 0.92f, p.V * 1.14f),
            Tone.Hsv(p.H - swing * 0.6f, p.S * 1.05f, p.V * 0.86f),
            Tone.Hsv(p.H + swing * 1.7f, p.S * 0.8f, p.V * 1.05f),
        };

        // Bark: each world's trees share a family of it.
        (float H, float S, float V) bark = d.Int(0, 5) switch
        {
            0 => (d.Range(20f, 32f), d.Range(0.35f, 0.5f), d.Range(0.26f, 0.36f)),   // warm brown
            1 => (d.Range(14f, 24f), d.Range(0.28f, 0.42f), d.Range(0.16f, 0.24f)),  // dark
            2 => (d.Range(34f, 46f), d.Range(0.12f, 0.2f), d.Range(0.56f, 0.72f)),   // pale, papery
            3 => (d.Range(6f, 16f), d.Range(0.5f, 0.62f), d.Range(0.28f, 0.38f)),    // red
            4 => (d.Range(200f, 240f), d.Range(0.05f, 0.14f), d.Range(0.28f, 0.42f)),// grey
            _ => (d.Range(26f, 38f), d.Range(0.2f, 0.3f), d.Range(0.34f, 0.44f)),    // dun
        };
        if (c.Alien > 0.5f) bark = (p.H + d.Range(120f, 240f), d.Range(0.18f, 0.4f), d.Range(0.25f, 0.5f));
        var barks = new[] { Tone.Hsv(bark.H, bark.S, bark.V), Tone.Hsv(bark.H + 8f, bark.S * 0.8f, bark.V * 1.2f), Tone.Hsv(bark.H - 6f, bark.S * 1.1f, bark.V * 0.78f) };

        // Flowers stand off the leaves: a bright, small accent, and that's all the brightness a plant gets.
        float bloom = p.H + d.Range(130f, 230f);
        var blooms = new[] { Tone.Hsv(bloom, 0.68f, 0.92f), Tone.Hsv(bloom + d.Range(24f, 60f), 0.5f, 0.97f), Tone.Hsv(d.Range(40f, 58f), 0.62f, 0.95f) };

        float capHue = c.Alien > 0.4f ? p.H + d.Range(60f, 300f) : d.Range(8f, 46f);
        var caps = new[] { Tone.Hsv(capHue, 0.56f, 0.66f), Tone.Hsv(capHue + 18f, 0.42f, 0.74f), Tone.Hsv(capHue - 22f, 0.6f, 0.5f), Tone.Hsv(capHue + 150f, 0.36f, 0.62f) };

        // Build: low gravity isn't known to the module; cold, thin air and drought all make for small and tough.
        float stature = (0.72f + 0.62f * MathF.Sqrt(c.Warmth * MathF.Max(c.Wetness, 0.2f)) + d.Range(-0.1f, 0.2f)) * (c.Air < 0.7f ? 0.78f : 1f) * (c.Air > 1.25f ? 1.1f : 1f);
        return new FloraStyle
        {
            Leaves = leaves, Barks = barks, Blooms = blooms, Caps = caps,
            Stalk = Tone.Lerp(Tone.Hsv(p.H + 30f, 0.18f, 0.74f), new Tone(0.86f, 0.82f, 0.72f), 0.5f),
            Stature = MathF.Min(stature, 1.12f),
            Droop = (0.5f + c.Wetness * 0.8f) * (c.Air < 0.7f ? 0.7f : 1f) * d.Range(0.75f, 1.25f),
            Flat = d.Range(0.72f, 1.25f) * (1f + (c.Warmth - 0.5f) * 0.3f),
            Slender = d.Range(0.8f, 1.25f) * (1f + (0.5f - c.Warmth) * 0.3f),
            Lean = d.Chance(0.4f) ? d.Range(0.06f, 0.16f) : d.Range(0f, 0.04f),
            LeanAngle = d.Range(0f, MathF.Tau),
        };
    }

    /// <summary>A world where life runs on the lines we know: lush, and nothing alien about it.</summary>
    static bool Familiar(FloraClimate c) => c.Alien < 0.1f && c.Life >= 0.9f;

    // ---- A species ----------------------------------------------------------------------------

    static Plant Make(Herbarium book, PlantFamily family, PlantForm form, float warm, float wet, Dice d)
    {
        FloraStyle st = book.Style;
        var p = new Plant { Index = book._plants.Count, Family = family, Form = form, Warmth = warm, Wetness = wet };
        p.Genome = RollGenome(family, form, warm, wet, st, d);

        bool fungal = family is PlantFamily.Fungus or PlantFamily.GiantFungus or PlantFamily.Pins;
        Tone leaf = fungal ? d.Pick(st.Caps) : st.Leaves[d.Int(0, family == PlantFamily.Crop ? 3 : 2)];
        leaf = Tone.Lerp(leaf, leaf * (0.86f + d.Unit() * 0.3f), 0.8f);
        // A plant's own climate shows in it: dry country is duller, wet country deeper.
        float grey = leaf.Luma;
        leaf = Tone.Lerp(leaf, new Tone(grey, grey, grey * 0.9f), Math.Clamp((0.45f - wet) * 0.8f, 0f, 0.4f));
        if (family == PlantFamily.Cactus) leaf = Tone.Lerp(leaf, Tone.Hsv(150f, 0.3f, 0.4f), 0.35f);
        // On a green world the odd tree has turned: a species, so russet woods come and go with the region.
        if (Familiar(book.Climate) && family == PlantFamily.Broadleaf && d.Chance(0.16f)) leaf = Tone.Hsv(d.Range(16f, 44f), d.Range(0.46f, 0.6f), d.Range(0.4f, 0.5f));
        p.Leaf = leaf;
        p.Bark = fungal ? st.Stalk : d.Pick(st.Barks);
        p.Bloom = d.Pick(st.Blooms);
        p.Flowers = p.Genome.Bloom > 0f;

        Paints paints = PaintsOf(p, st, d);
        ulong seed = d.Next();
        bool cover = p.Cover || family == PlantFamily.Crop;
        int meshes = cover ? 1 : NearMeshes;
        p.Near = new PlantMesh[meshes];
        var body = new Anatomy();
        for (int i = 0; i < meshes; i++)
        {
            var s = new Sculptor();
            PlantForms.Build(p, s, new Dice(Dice.Of(seed, i)), paints, false, i == 0 ? body : new Anatomy());
            p.Near[i] = s.Finish();
        }
        if (p.Large)
        {
            var s = new Sculptor();
            PlantForms.Build(p, s, new Dice(Dice.Of(seed, 0)), paints, true, new Anatomy());
            p.Far = s.Finish();
        }
        else p.Far = p.Near[0];
        Measure(p, body);
        return p;
    }

    static Paints PaintsOf(Plant p, FloraStyle st, Dice d)
    {
        // Multiplied into the plant's tint: warm light on top, cool shade below.
        var paints = new Paints
        {
            Top = new Vector4(0.98f, 0.97f, 0.84f, 1f),
            Mid = new Vector4(0.78f, 0.8f, 0.76f, 1f),
            Under = new Vector4(0.6f, 0.63f, 0.66f, 1f),
            Bark = p.Bark.Own(),
            BarkLow = (p.Bark * 0.84f).Own(),
            Petal = p.Bloom.Own(),
            Heart = Tone.Lerp(p.Bloom, new Tone(1f, 0.9f, 0.5f), 0.7f).Own(),
            Stalk = Tone.Lerp(p.Leaf, st.Stalk, 0.25f).Own(),
            Gill = Tone.Lerp(st.Stalk, p.Leaf, 0.25f).Own(),
            Tip = Tone.Lerp(p.Bloom, p.Leaf, 0.45f).Own(),
        };
        // Low growth is lit as the ground it stands in: dark roots would read as a field of spikes.
        if (p.Cover)
        {
            paints.Under = new Vector4(0.62f, 0.64f, 0.62f, 1f);
            paints.Mid = new Vector4(0.76f, 0.77f, 0.72f, 1f);
            if (p.Family == PlantFamily.Forb) paints.Under = (p.Leaf * 0.8f).Own();
        }
        if (p.Family is PlantFamily.Crop) paints.Tip = Tone.Hsv(d.Range(24f, 56f), 0.6f, 0.82f).Own();
        return paints;
    }

    /// <summary>Reads the finished meshes for what gameplay needs: how tall, how wide, and the room each band takes.</summary>
    static void Measure(Plant p, Anatomy body)
    {
        float height = 0f, crown = 0f;
        foreach (PlantMesh m in p.Near)
            foreach (Vector3 v in m.Positions)
            {
                height = MathF.Max(height, v.Y);
                crown = MathF.Max(crown, MathF.Sqrt(v.X * v.X + v.Z * v.Z));
            }
        p.Height = height;
        p.CrownRadius = crown;
        const int Bands = 5;
        var reach = new (float, float, float)[Bands];
        for (int b = 0; b < Bands; b++)
        {
            float from = height * b / Bands, to = height * (b + 1) / Bands, r = 0.2f;
            foreach (PlantMesh m in p.Near)
                foreach (Vector3 v in m.Positions)
                    if (v.Y >= from && v.Y <= to) r = MathF.Max(r, MathF.Sqrt(v.X * v.X + v.Z * v.Z));
            reach[b] = (from, to, r);
        }
        p.Reach = reach;
        p.FootRadius = MathF.Max(0.12f, body.Foot);
        p.Solid = p.Tree || p.Mineral;
        if (!p.Solid) return;
        Vector3 top = body.StemTop;
        p.TrunkAxis = Vector3.Normalize(top);
        p.TrunkHeight = MathF.Max(top.Length(), 1.4f);
        p.TrunkRadius = MathF.Max(body.StemRadius * 0.92f, 0.12f);
    }

    static Genome RollGenome(PlantFamily family, PlantForm form, float warm, float wet, FloraStyle st, Dice d)
    {
        float H = family switch
        {
            PlantFamily.Conifer => d.Range(11f, 22f), PlantFamily.Broadleaf => d.Range(9f, 18f), PlantFamily.Jungle => d.Range(16f, 26f),
            PlantFamily.Palm => d.Range(7f, 15f), PlantFamily.Cactus => d.Range(2.5f, 6.5f), PlantFamily.GiantFungus => d.Range(5f, 14f),
            PlantFamily.Fungus => d.Range(0.5f, 1.2f), PlantFamily.Shrub => d.Range(1f, 2.1f), PlantFamily.Crystal => d.Range(3f, 6f),
            PlantFamily.Ice => d.Range(3.5f, 7f), _ => 1f,
        };
        bool stands = family < PlantFamily.Grass;
        if (stands && family != PlantFamily.Crystal && family != PlantFamily.Ice) H *= st.Stature * (0.86f + 0.18f * MathF.Sqrt(warm * wet));
        float slender = st.Slender;
        var g = new Genome
        {
            Height = H,
            TrunkRadius = H * d.Range(0.016f, 0.027f) / slender,
            Stem = d.Range(0.28f, 0.5f),
            Lean = st.Lean * d.Range(0.5f, 1.3f) + d.Range(0f, 0.035f),
            LeanAngle = st.LeanAngle + d.Range(-0.5f, 0.5f),
            Bend = d.Range(-0.06f, 0.1f),
            Flare = d.Range(1.12f, 1.5f),
            Crown = d.Range(0.27f, 0.4f),
            Flat = d.Range(0.55f, 1f) * st.Flat,
            Droop = Math.Clamp(d.Range(0.08f, 0.4f) * st.Droop, 0f, 0.7f),
            Jag = d.Range(0.12f, 0.42f),
            Taper = d.Range(0.08f, 0.3f),
            Spread = d.Range(0.2f, 0.7f),
            Arch = d.Range(0.12f, 0.5f) * (0.6f + st.Droop * 0.5f),
            Ribs = d.Range(0.05f, 0.14f),
            Layers = d.Int(3, 7),
            Forks = d.Int(2, 4),
            Lobes = d.Int(3, 6),
            Fronds = d.Int(8, 12),
            Sides = d.Int(5, 7),
            Clump = d.Int(1, 3),
        };
        g.FrondLength = H * d.Range(0.3f, 0.46f);
        g.FrondWidth = g.FrondLength * d.Range(0.14f, 0.26f);

        switch (form)
        {
            case PlantForm.Spire: g.Crown = d.Range(0.18f, 0.3f); g.Stem = d.Range(0.08f, 0.24f); g.Layers = d.Int(4, 6); g.Flat = d.Range(0.2f, 0.8f); break;
            case PlantForm.Pagoda: g.Crown = d.Range(0.25f, 0.4f); g.Stem = d.Range(0.2f, 0.36f); g.Layers = d.Int(3, 6); g.Taper = d.Range(0.2f, 0.42f); g.Droop *= 0.7f; break;
            case PlantForm.Spindle: g.Crown = d.Range(0.22f, 0.34f); g.Stem = d.Range(0.1f, 0.3f); break;
            case PlantForm.Stacked: g.Crown = d.Range(0.26f, 0.42f); g.Stem = d.Range(0.2f, 0.4f); g.Layers = d.Int(3, 6); break;
            case PlantForm.Domed: g.Stem = d.Range(0.3f, 0.5f); g.Flat = d.Range(0.68f, 1.05f); g.Lobes = d.Int(4, 7); break;
            case PlantForm.Umbrella: g.Stem = d.Range(0.52f, 0.7f); g.Crown = d.Range(0.3f, 0.44f); g.Flat = d.Range(0.2f, 0.8f); break;
            case PlantForm.Weeping: g.Stem = d.Range(0.36f, 0.52f); g.Crown = d.Range(0.3f, 0.42f); g.Fronds = d.Int(9, 13); break;
            case PlantForm.CloudPads: g.Stem = d.Range(0.25f, 0.42f); g.Bend = d.Range(-0.14f, 0.14f); g.Lean += d.Range(0.03f, 0.1f); break;
            case PlantForm.Bottle: g.Height = H *= 0.68f; g.TrunkRadius = H * d.Range(0.07f, 0.1f); g.Crown = d.Range(0.28f, 0.44f); g.Stem = d.Range(0.58f, 0.74f); break;
            case PlantForm.Pole: g.Stem = d.Range(0.64f, 0.8f); g.FrondLength = H * d.Range(0.16f, 0.24f); g.FrondWidth = g.FrondLength * 0.3f; break;
            case PlantForm.Palm: g.TrunkRadius = H * d.Range(0.018f, 0.03f); g.FrondLength = H * d.Range(0.28f, 0.44f); g.FrondWidth = g.FrondLength * d.Range(0.18f, 0.3f); g.Bend = d.Range(0.02f, 0.18f); g.Arch = d.Range(0.2f, 0.62f); break;
            case PlantForm.FanPalm: g.TrunkRadius = H * d.Range(0.028f, 0.04f); g.Stem = d.Range(0.4f, 0.62f); g.FrondWidth = H * d.Range(0.1f, 0.16f); g.Fronds = d.Int(8, 12); break;
            case PlantForm.SpikeTree: g.Stem = d.Range(0.34f, 0.56f); g.FrondLength = H * d.Range(0.2f, 0.32f); g.FrondWidth = g.FrondLength * 0.3f; g.Crown = d.Range(0.24f, 0.4f); break;
            case PlantForm.Antler: g.Stem = d.Range(0.14f, 0.3f); g.Forks = d.Int(2, 3); break;
            case PlantForm.Lantern: g.Stem = d.Range(0.3f, 0.5f); g.Crown = d.Range(0.34f, 0.5f); g.Forks = d.Int(2, 4); break;
            case PlantForm.Snag: g.Forks = d.Int(2, 3); break;
            case PlantForm.Capped: g.TrunkRadius = H * d.Range(0.05f, 0.09f); g.Crown = d.Range(0.28f, 0.48f); g.Flat = d.Range(0.6f, 1.3f); break;
            case PlantForm.Parasol: g.TrunkRadius = H * d.Range(0.035f, 0.06f); g.Crown = d.Range(0.2f, 0.34f); g.Layers = d.Int(2, 4); break;
            case PlantForm.Trumpet: g.TrunkRadius = H * d.Range(0.05f, 0.09f); g.Crown = d.Range(0.3f, 0.5f); g.Ribs = d.Range(0f, 0.08f); break;
            case PlantForm.Organ: g.TrunkRadius = H * d.Range(0.07f, 0.12f); g.Clump = d.Int(2, 4); g.Crown = d.Range(0.1f, 0.3f); break;
            case PlantForm.Candelabra: g.TrunkRadius = H * d.Range(0.07f, 0.12f); g.Forks = d.Int(2, 4); break;
            case PlantForm.Barrel: g.Height = H *= 0.7f; g.Clump = d.Int(1, 3); g.Bloom = d.Chance(0.6f) ? d.Range(0.3f, 0.8f) : 0f; break;
            case PlantForm.Whips: g.TrunkRadius = H * d.Range(0.03f, 0.05f); g.Fronds = d.Int(5, 9); break;
            case PlantForm.Paddles: g.Forks = d.Int(2, 3); break;
            case PlantForm.Bush:
                g.Crown = d.Range(0.42f, 0.6f);
                g.FrondLength = H * d.Range(0.2f, 0.27f);
                g.FrondWidth = g.FrondLength * d.Range(0.5f, 0.68f);
                g.Forks = d.Int(2, 3);
                g.Lobes = d.Int(2, 5);
                g.Bloom = d.Chance(0.85f) ? d.Range(0.1f, 0.9f) : 0f;
                break;
            case PlantForm.Fern: g.Height = H *= 0.9f; g.FrondLength = H * d.Range(0.8f, 1.2f); g.FrondWidth = g.FrondLength * d.Range(0.18f, 0.28f); g.Fronds = d.Int(7, 11); g.Arch = d.Range(0.28f, 0.6f); break;
            case PlantForm.Thorn: g.FrondLength = H * d.Range(0.08f, 0.14f); g.FrondWidth = g.FrondLength * 0.5f; g.Crown = d.Range(0.4f, 0.65f); g.Forks = d.Int(2, 4); break;
            case PlantForm.Rosette: g.FrondWidth = H * d.Range(0.08f, 0.13f); g.Fronds = d.Int(8, 12); g.Arch = d.Range(0.05f, 0.4f); g.Bloom = d.Chance(0.5f) ? d.Range(0.4f, 1f) : 0f; break;
            case PlantForm.Shards: g.TrunkRadius = H * d.Range(0.08f, 0.13f); g.Clump = d.Int(2, 4); g.Sides = d.Int(4, 6); g.Spread = d.Range(0.2f, 0.7f); g.Lean = d.Range(0.04f, 0.3f); break;
            case PlantForm.Tussock: g.Height = d.Range(0.5f, 0.95f); g.Fronds = d.Int(6, 8); g.FrondWidth = d.Range(0.1f, 0.18f); g.Arch = d.Range(0.05f, 0.28f); g.Spread = d.Range(0.12f, 0.24f); g.Flare = d.Range(0.25f, 0.6f); break;
            case PlantForm.Flowers: g.Height = d.Range(0.2f, 0.42f); g.Fronds = d.Int(3, 4); g.FrondLength = d.Range(0.03f, 0.05f); g.FrondWidth = d.Range(0.06f, 0.1f); g.Spread = d.Range(0.25f, 0.45f); g.Bloom = 1f; break;
            case PlantForm.Twigs: g.Height = d.Range(0.28f, 0.6f); g.Fronds = d.Int(3, 4); g.FrondLength = 0.1f; g.FrondWidth = 0.05f; g.Spread = d.Range(0.25f, 0.5f); break;
            case PlantForm.Reeds: g.Height = d.Range(1.2f, 2.2f); g.Fronds = d.Int(6, 8); g.FrondWidth = d.Range(0.06f, 0.1f); g.Spread = d.Range(0.28f, 0.45f); break;
            case PlantForm.Lichen: g.Height = d.Range(0.16f, 0.28f); g.Fronds = d.Int(7, 10); g.FrondWidth = d.Range(0.09f, 0.14f); g.Spread = d.Range(0.3f, 0.5f); break;
            case PlantForm.PinStalks: g.Height = d.Range(0.22f, 0.5f); g.Fronds = d.Int(3, 4); g.FrondLength = d.Range(0.05f, 0.1f); g.FrondWidth = d.Range(0.012f, 0.02f); g.Spread = d.Range(0.25f, 0.45f); break;
            case PlantForm.GrainCrop: g.Height = d.Range(0.8f, 1.5f); g.Fronds = d.Int(6, 9); g.FrondWidth = d.Range(0.04f, 0.07f); g.Spread = 0.32f; break;
            case PlantForm.LeafCrop: g.Height = d.Range(0.3f, 0.6f); g.Fronds = d.Int(8, 12); g.FrondWidth = d.Range(0.14f, 0.26f); break;
            case PlantForm.VineCrop: g.Height = d.Range(1.2f, 2f); g.Fronds = d.Int(7, 10); g.FrondLength = 0.22f; g.FrondWidth = 0.12f; break;
        }
        return g;
    }
}
