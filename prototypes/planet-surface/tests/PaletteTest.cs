using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using TerrainDigging.Terrain;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface.Tests;

/// <summary>
/// Every world's natural colours, measured on fresh worlds rolled for this run (never a pinned seed):
/// - <b>Variety with a basis.</b> Many meadow (temperate) worlds across stars, sizes and grounds: each
///   one's clear sky (zenith and horizon), plants, soil, rock and deep water in CIELAB. Each colour
///   must spread between worlds (median pair dE), few pairs may look the same at a glance, and almost
///   no two worlds may match in every colour at once. The same check runs on every other world type
///   with air (an airless body's ground is proto/vacuum's deep greys, alike by design).
/// - <b>Real light.</b> The night sky stays dark.
/// - <b>Earthy, today's look, vivid, candy and mixed</b> (owner, 23 Nov), over hundreds of palettes of every type with air:
///   each kind common and candy one share, not most; every part looks as its label says; no hue family takes over any part;
///   red water, blue grass and a green sky all turning up. A barren body's rock stays grey.
/// - <b>Ore keeps its colour</b> (owner, 23 Nov): every ore's deposit the same on every world of every look, and standing
///   apart from the ground round it.
/// Prints "world: seed N, type" for each world so a run can be replayed.
/// </summary>
public partial class PaletteTest : Node
{
    int _failures;
    static readonly ulong RunSeed = (ulong)System.Random.Shared.NextInt64(1, long.MaxValue);
    ulong _seeds = RunSeed;

    void Check(bool ok, string text)
    {
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {text}");
        if (!ok) _failures++;
    }

    ulong Fresh() => _seeds = Planets.Gen.Hash.Mix(_seeds + 0x9E37);

    public override void _Ready()
    {
        // A hung run quits on its own rather than wait for someone to kill it.
        _ = System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(15)).ContinueWith(_ =>
        {
            GD.PrintErr("TIMED OUT after 15 minutes: the run quits itself");
            System.Environment.Exit(2);
        });
        CallDeferred(nameof(RunAll));
    }

    async void RunAll()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"palette test run seed {RunSeed}");
            Variety("meadow", 24, () => (Archetype.Temperate, (int)(Fresh() % 2), false));
            foreach (Archetype a in new[] { Archetype.Arid, Archetype.Ice, Archetype.Lava, Archetype.Alien })
                Variety(a.ToString().ToLowerInvariant(), 14, () => (a, (int)(Fresh() % 2), false));
            Range(600);
            Ore(18);
        }
        catch (Exception e)
        {
            Check(false, $"test crashed: {e}");
        }
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    static readonly string[] Channels = { "zenith", "horizon", "plants", "soil", "rock", "sea", "shore" };

    /// <summary>
    /// One world's natural colours, CIELAB, in <see cref="Channels"/> order: null where it has none (no sky with no air,
    /// no plants where nothing grows, no water where the seas are lava, no bare ground on a world all snow).
    /// </summary>
    sealed record Looks(string Name, Lab?[] Colors, Lab Night);

    Looks Measure(ulong seed, Archetype a, int v, bool airless)
    {
        PlanetSpec spec = PlanetSpec.Roll(seed, a, v, PlanetSpec.RollWidthKm(seed));
        spec = spec with { Modifiers = airless ? Modifier.Cratered : spec.Modifiers & ~Modifier.Cratered };
        // The map's climate is the same at any size: an 8 km map keeps the run short, the sky keeps the world's own size.
        WorldMap map = PlanetGenerator.Generate(spec with { WidthKm = 8 });
        SkySystem sky = spec.Airless ? SkySystem.Vacuum(SkySystem.For(spec)) : SkySystem.For(spec);
        PlanetFlora flora = PlanetFlora.For(map, sky);
        var ground = new GroundPalette(map, sky.Palette, flora);
        WorldLook look = WorldLook.For(map, sky);

        // The ground's tone: its soil averaged over the land a player stands on, snow and ice aside (white anywhere).
        Rgb soil = default;
        int n = 0;
        for (int k = 0; k < map.Count; k += 7)
        {
            if (map.Has(k, CellFlags.Water) || (Biome)map.Biome[k] is Biome.Snow or Biome.Snowfield or Biome.Glacier or Biome.BlueIce) continue;
            soil += ground.SoilAt(map.CellX(k % map.Width), map.CellY(k / map.Width), out _);
            n++;
        }
        Lab? bare = n > 0 ? Lab.Of(soil * (1f / n)) : null;
        Lab? plants = flora.Book.Verdure is GalHack.Plants.Tone t ? Lab.Of(new Rgb(t.R, t.G, t.B)) : null;
        string name = $"seed {seed}, {a} {v}{(spec.Airless ? " airless" : "")} {spec.WidthKm} km, {sky.Star} star, {sky.Air} air";
        GD.Print($"world: {name}");
        Lab? Water(Godot.Color c) => look.Lava || spec.Airless ? null : Lab.Of(new Rgb(c.R, c.G, c.B));
        // The sea as the water shader shows it 40 m deep, off a coast (its own shallow-to-deep blend over 120 m).
        float off = Mathf.SmoothStep(0f, 0.6f, 40f / 120f);
        Lab? Air(Rgb c) => spec.Airless ? null : Lab.Of(c);
        return new Looks(name, new[] { Air(sky.Zenith), Air(sky.Horizon), plants, bare, Lab.Of(ground.Rock), Water(look.Shallow.Lerp(look.Deep, off)), Water(look.Shallow) }, Lab.Of(sky.NightZenith));
    }

    /// <summary>
    /// Fresh worlds of one kind: no colour clusters (fewer than half the pairs of worlds within <see cref="Same"/>,
    /// the gap a glance tells apart), and at most <see cref="TwinShare"/> of pairs are twins, within it in every
    /// colour at once. The median pair gap is printed beside each.
    /// </summary>
    void Variety(string kind, int count, Func<(Archetype, int, bool)> roll)
    {
        var worlds = new List<Looks>();
        for (int i = 0; i < count; i++)
        {
            var (a, v, airless) = roll();
            worlds.Add(Measure(Fresh(), a, v, airless));
        }
        var lines = new List<string>();
        bool spread = true;
        for (int c = 0; c < Channels.Length; c++)
        {
            var gaps = new List<float>();
            float chroma = 0f;
            int seen = 0;
            for (int i = 0; i < worlds.Count; i++)
            {
                if (worlds[i].Colors[c] is not Lab x) continue;
                chroma += x.Chroma;
                seen++;
                for (int j = i + 1; j < worlds.Count; j++)
                    if (worlds[j].Colors[c] is Lab y) gaps.Add(x.Gap(y));
            }
            if (gaps.Count == 0) { lines.Add($"{Channels[c]} none"); continue; }
            gaps.Sort();
            float median = gaps[gaps.Count / 2], same = gaps.Count(g => g < Same) / (float)gaps.Count;
            lines.Add($"{Channels[c]} median dE {median:F1}, same {same:P0}, chroma {chroma / seen:F1}");
            spread &= same < 0.5f;
        }
        // Twins in every colour mean something where there are several to match: with only a ground and its rock (an
        // airless body's, the one mineral twice) that is the clustering above again.
        int twins = 0, pairs = 0, colours = Channels.Length - worlds[0].Colors.Count(x => x == null);
        for (int i = 0; i < worlds.Count; i++)
            for (int j = i + 1; j < worlds.Count; j++)
            {
                pairs++;
                bool alike = true;
                for (int c = 0; c < Channels.Length; c++)
                    if (worlds[i].Colors[c] is Lab x && worlds[j].Colors[c] is Lab y && x.Gap(y) >= Same) alike = false;
                if (alike) twins++;
            }
        GD.Print($"PALETTE {kind}: {string.Join("; ", lines)}; twins {twins} of {pairs} pairs");
        Check(spread, $"{count} fresh {kind} worlds spread in every natural colour: none has half its pairs within dE {Same}");
        if (colours >= 4) Check(twins <= pairs * TwinShare, $"{kind} worlds are told apart: {twins} of {pairs} pairs twins in every colour (dE < {Same})");
        float night = worlds.Max(w => w.Night.L);
        Check(night < 8f, $"{kind} night skies stay dark (brightest zenith L* {night:F1})");
    }

    const float Same = 10f, TwinShare = 0.08f;

    /// <summary>Hue families by Oklab hue: pink-magenta, red, orange, yellow, green, cyan, blue, violet (grey under 0.04 chroma, none).</summary>
    static readonly string[] Families = { "pink", "red", "orange", "yellow", "green", "cyan", "blue", "violet" };

    /// <summary>Earthy to the eye: an earth tone (Oklab hue 20° to 100°, at most <see cref="WorldPalette.EarthTone"/>) or near grey.</summary>
    static bool Earthy(Oklab o)
    {
        float d = (o.Hue * 180f / MathF.PI + 360f) % 360f;
        return o.Chroma <= (d is >= 18f and <= 102f ? WorldPalette.EarthTone + 0.005f : WorldPalette.EarthyMost + 0.005f);
    }

    static int FamilyOf(Oklab o)
    {
        float d = (o.Hue * 180f / MathF.PI + 360f) % 360f;
        return o.Chroma < 0.04f ? -1 : d < 15f || d >= 320f ? 0 : d < 45f ? 1 : d < 80f ? 2 : d < 115f ? 3 : d < 170f ? 4 : d < 230f ? 5 : d < 285f ? 6 : 7;
    }

    /// <summary>
    /// <paramref name="count"/> fresh palettes of every type (palettes alone, no maps). Each kind of world is common: wholly earthy,
    /// today's look (about one in five), vivid worlds that pair their strong parts against a deep one, candy (every part at full
    /// strength: one share of the mix, never most: owner, 23 Nov) and mixed. Every part of every world looks as its label says: a
    /// vivid part (sky: its zenith or its band) at least 0.095 Oklab chroma (0.07 when darker than L 0.5), an earthy one an earth tone
    /// or at most 0.09, a deep one dark and dull (at most 0.06).
    /// No hue family holds more than 35% of any part's vivid colours (sky, ground, plants, water), nor 45% of all its colours (earthy
    /// worlds' earth tones may lead). Red water, blue plants and
    /// a green sky each turn up on at least one world in a hundred. Every fourth roll is a barren body, whose rock stays grey.
    /// </summary>
    void Range(int count)
    {
        string[] parts = { "sky", "ground", "plants", "water" };
        Parts[] flags = { Planets.Look.Parts.Sky, Planets.Look.Parts.Ground, Planets.Look.Parts.Plants, Planets.Look.Parts.Water };
        var all = new int[4, 9];
        var vivid = new int[4, 9];
        var kinds = new Dictionary<PaletteKind, int>();
        var wrong = new List<string>();
        int worlds = 0, red = 0, blue = 0, green = 0;
        float greyest = 0f;
        static float Deg(Oklab o) => (o.Hue * 180f / MathF.PI + 360f) % 360f;
        for (int i = 0; i < count; i++)
        {
            var (a, v) = ((Archetype)(Fresh() % 5), (int)(Fresh() % 2));
            PlanetSpec spec = PlanetSpec.Roll(Fresh(), a, v, PlanetSpec.RollWidthKm(_seeds));
            if (i % 4 == 0)
            {
                // A barren body's rock is proto/vacuum's deep grey, whatever its star: the light and shade colour it, not the palette.
                WorldPalette moon = SkySystem.For(spec with { Archetype = Archetype.Barren, Modifiers = Modifier.Cratered }).Palette;
                greyest = MathF.Max(greyest, Oklab.Of(moon.Bedrock).Chroma);
                continue;
            }
            if (spec.Airless) continue;
            WorldPalette p = SkySystem.For(spec).Palette;
            worlds++;
            kinds[p.Kind] = kinds.GetValueOrDefault(p.Kind) + 1;
            if (p.Today) continue;
            Oklab sky = Oklab.Of(p.Zenith), band = Oklab.Of(p.Band);
            Oklab[] these = { sky, Oklab.Of(p.Ground(Biome.Grassland, 0.6f, 0.5f)), Oklab.Of(p.Leaf), Oklab.Of(Rgb.Lerp(p.Shallow, p.Deep, 0.5f)) };
            for (int e = 0; e < 4; e++)
            {
                if (!p.Has.HasFlag(flags[e])) continue;
                Oklab o = these[e];
                int f = FamilyOf(o);
                if (f >= 0) all[e, f]++;
                bool vivid_ = p.VividParts.HasFlag(flags[e]), deep = p.DeepParts.HasFlag(flags[e]);
                if (vivid_ && f >= 0) vivid[e, f]++;
                float strongest = e == 0 ? MathF.Max(o.Chroma, band.Chroma) : o.Chroma;
                // (A dark colour holds less chroma on the screen: a deep jewel tone under L 0.5 reads vivid from 0.07.)
                bool looks = vivid_ ? strongest >= (o.L < 0.5f ? 0.07f : 0.095f) : deep ? o.L <= 0.55f && o.Chroma <= 0.06f : Earthy(o) && (e != 0 || Earthy(band) && Earthy(Oklab.Of(p.Horizon)));
                if (!looks) wrong.Add($"{spec.Seed} {a} {v} {parts[e]} {(vivid_ ? "vivid" : deep ? "deep" : "earthy")} chroma {strongest:F3} L {o.L:F2}");
            }
            if (p.Has.HasFlag(Planets.Look.Parts.Water) && these[3].Chroma > 0.08f && (Deg(these[3]) < 45f || Deg(these[3]) > 345f)) red++;
            if (p.Has.HasFlag(Planets.Look.Parts.Plants) && these[2].Chroma > 0.08f && Deg(these[2]) is > 215f and < 285f) blue++;
            if (sky.Chroma > 0.08f && Deg(sky) is > 120f and < 170f) green++;
        }
        float Share(PaletteKind k) => kinds.GetValueOrDefault(k) / (float)worlds;
        GD.Print($"RANGE {worlds} worlds with air: " + string.Join(", ", Enum.GetValues<PaletteKind>().Select(k => $"{k} {Share(k):P0}")));
        Check(Share(PaletteKind.Earthy) >= 0.12f && Share(PaletteKind.Mixed) >= 0.25f && Share(PaletteKind.Today) is >= 0.12f and <= 0.3f
              && Share(PaletteKind.Vivid) >= 0.04f && Share(PaletteKind.Candy) is >= 0.04f and <= 0.25f,
            $"every kind common, candy one share of the mix: earthy {Share(PaletteKind.Earthy):P0}, today {Share(PaletteKind.Today):P0}, vivid {Share(PaletteKind.Vivid):P0}, candy {Share(PaletteKind.Candy):P0}, mixed {Share(PaletteKind.Mixed):P0}");
        foreach (string w in wrong.Take(8)) GD.Print($"  looks unlike its label: {w}");
        Check(wrong.Count <= worlds / 100, $"every part looks as its label says: {wrong.Count} parts unlike theirs over {worlds} worlds");
        for (int e = 0; e < 4; e++)
        {
            int n = Enumerable.Range(0, 8).Sum(f => all[e, f]), nv = Enumerable.Range(0, 8).Sum(f => vivid[e, f]);
            int most = Enumerable.Range(0, 8).Max(f => all[e, f]), mostVivid = Enumerable.Range(0, 8).Max(f => vivid[e, f]);
            GD.Print($"RANGE {parts[e]}: " + string.Join(" ", Enumerable.Range(0, 8).Select(f => $"{Families[f]} {all[e, f]}/{vivid[e, f]}")) + $" (all/vivid of {n}/{nv})");
            // (Earthy worlds' own earth tones may lead a part's colours, never more than 45% of them.)
            Check(most <= n * 0.45f && mostVivid <= nv * 0.35f, $"no hue family takes over the {parts[e]}: the commonest holds {most * 100 / Math.Max(n, 1)}% of all, {mostVivid * 100 / Math.Max(nv, 1)}% of the vivid");
        }
        Check(red >= worlds / 100 && blue >= worlds / 100 && green >= worlds / 100, $"red water on {red}, blue plants on {blue} and a green sky on {green} of {worlds} worlds (each at least {worlds / 100})");
        Check(greyest < 0.04f, $"a barren body's rock stays grey: highest Oklab chroma {greyest:F3}");
    }

    /// <summary>
    /// Ore keeps its colour (owner, 23 Nov: "ore deposits we find keep their colors regardless of palette, so the player can tell the
    /// ore at a glance"), over <paramref name="count"/> fresh worlds of every look: each ore's deposit, deep in the ground, is the same
    /// colour on every one and marked as the ore's own (the shader keeps the palette off it), and every deposit breaking the surface
    /// stands at least <see cref="DepositLook.Margin"/> (Oklab) from the soil round it and from the rock of its outcrop.
    /// </summary>
    void Ore(int count)
    {
        var seen = new Dictionary<DepositKind, Color>();
        var kinds = new HashSet<PaletteKind>();
        int differ = 0, unmarked = 0, deposits = 0, pushed = 0, close = 0;
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var (a, v) = (new[] { Archetype.Temperate, Archetype.Arid, Archetype.Ice, Archetype.Alien }[i % 4], (int)(Fresh() % 2));
            PlanetSpec spec = PlanetSpec.Roll(Fresh(), a, v, 8);
            if (spec.Airless) continue;
            WorldMap map = PlanetGenerator.Generate(spec);
            SkySystem sky = SkySystem.For(spec);
            kinds.Add(sky.Palette.Kind);
            var ground = new GroundPalette(map, sky.Palette, PlanetFlora.For(map, sky));
            foreach (DepositKind k in Enum.GetValues<DepositKind>())
            {
                Color c = ground.Vertex(CellMaterial.Ore, 100.0, 100.0, -1000f, out Vector2 f, k); // deep: the same place on every world
                if (f.Y > -0.99f) unmarked++;
                if (seen.TryGetValue(k, out Color was) && !was.IsEqualApprox(c)) differ++;
                seen[k] = c;
            }
            foreach (Planets.Gen.Deposit d in map.Deposits.All())
            {
                if (d.Depth > 0.01f) continue;
                deposits++;
                Rgb ore = DepositLook.Color(d.Kind), soil = ground.SoilAt(d.X, d.Y, out _), rock = Rgb.Lerp(ground.Rock, soil, 0.12f);
                if (Gap(soil, ore) < DepositLook.Margin) pushed++;
                float gap = MathF.Min(Gap(DepositLook.Around(soil, d.Kind), ore), Gap(DepositLook.Around(rock, d.Kind), ore));
                nearest = MathF.Min(nearest, gap);
                if (gap < DepositLook.Margin * 0.97f) close++;
            }
        }
        GD.Print($"ORE {seen.Count} ores over worlds of {string.Join(", ", kinds)}; {deposits} deposits breaking the surface, {pushed} whose soil gave way; nearest ground {nearest:F3}");
        Check(differ == 0 && unmarked == 0, $"every ore's deposit the same colour on every world and look, and marked its own: {differ} differ, {unmarked} unmarked");
        Check(deposits > 0 && close == 0, $"every deposit stands apart from the ground round it: {close} of {deposits} within {DepositLook.Margin} (Oklab)");
    }

    static float Gap(Rgb x, Rgb y)
    {
        Oklab a = Oklab.Of(x), b = Oklab.Of(y);
        return MathF.Sqrt((a.L - b.L) * (a.L - b.L) + (a.A - b.A) * (a.A - b.A) + (a.B - b.B) * (a.B - b.B));
    }

    /// <summary>A colour in CIELAB (D65), from sRGB.</summary>
    readonly record struct Lab(float L, float A, float B)
    {
        public float Chroma => MathF.Sqrt(A * A + B * B);
        public float Gap(Lab o) => MathF.Sqrt((L - o.L) * (L - o.L) + (A - o.A) * (A - o.A) + (B - o.B) * (B - o.B));

        public static Lab Of(Rgb c)
        {
            static float Lin(float v) { v = Math.Clamp(v, 0f, 1f); return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f); }
            float r = Lin(c.R), g = Lin(c.G), b = Lin(c.B);
            float x = (0.4124f * r + 0.3576f * g + 0.1805f * b) / 0.95047f, y = 0.2126f * r + 0.7152f * g + 0.0722f * b, z = (0.0193f * r + 0.1192f * g + 0.9505f * b) / 1.08883f;
            static float F(float t) => t > 0.008856f ? MathF.Cbrt(t) : 7.787f * t + 16f / 116f;
            return new Lab(116f * F(y) - 16f, 500f * (F(x) - F(y)), 200f * (F(y) - F(z)));
        }
    }
}
