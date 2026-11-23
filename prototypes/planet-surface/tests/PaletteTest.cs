using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface.Tests;

/// <summary>
/// Every world's natural colours, measured on fresh worlds rolled for this run (never a pinned seed):
/// - <b>Variety with a basis.</b> Many meadow (temperate) worlds across stars, sizes and grounds: each
///   one's clear sky (zenith and horizon), plants, soil, rock and deep water in CIELAB. Each colour
///   must spread between worlds (median pair dE), few pairs may look the same at a glance, and almost
///   no two worlds may match in every colour at once. The same check runs on every other world type,
///   airless ones included.
/// - <b>Real light.</b> The night sky stays dark.
/// - <b>Bold, as the art direction has it</b> (owner, 23 Nov: exaggerated, saturated colour like 1970s
///   science fiction art), over hundreds of palettes of every type with air: each element's hues spread round
///   the wheel (no one sixth of it holding most worlds), each element's chroma over its floor, every world's
///   land standing well apart in hue from its sky, and red water, blue grass and a green sky all turning up.
///   An airless body's rock stays grey.
/// - <b>The grade</b> (<see cref="Grade.Of"/>) adds chroma at each colour's own hue and greys nothing.
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
            Variety("airless", 14, () => (new[] { Archetype.Arid, Archetype.Ice, Archetype.Lava }[Fresh() % 3], (int)(Fresh() % 2), true));
            Bold(600);
            Graded();
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

    static readonly string[] Elements = { "zenith", "band", "plants", "water", "soil" };

    /// <summary>The least median chroma (Oklab) of each element over worlds with air, in <see cref="Elements"/> order (a deep zenith has less room for chroma).</summary>
    static readonly float[] Floors = { 0.09f, 0.12f, 0.1f, 0.08f, 0.06f };

    /// <summary>
    /// <paramref name="count"/> fresh palettes of every type (palettes alone, no maps): per element over the worlds with air, its
    /// median chroma against its floor and its hues spread round the wheel (at least five of its six sixths in use, none holding
    /// over 45% of worlds); the land's hue (plants on a living world, soil on the rest) at least 60 degrees from its sky's on three
    /// worlds in four, 90 at the median; red water, blue plants and a green sky each on at least one world in a hundred. Every
    /// fourth roll is an airless body, whose rock stays grey.
    /// </summary>
    void Bold(int count)
    {
        var colours = Elements.Select(_ => new List<Oklab>()).ToArray();
        var gaps = new List<float>();
        int red = 0, blue = 0, green = 0;
        float greyest = 0f;
        static float Deg(Oklab o) => (o.Hue * 180f / MathF.PI + 360f) % 360f;
        for (int i = 0; i < count; i++)
        {
            var (a, v) = ((Archetype)(Fresh() % 5), (int)(Fresh() % 2));
            PlanetSpec spec = PlanetSpec.Roll(Fresh(), a, v, PlanetSpec.RollWidthKm(_seeds));
            if (i % 4 == 0)
            {
                spec = spec with { Archetype = PlanetSpec.Craters(a) ? a : Archetype.Arid, Modifiers = Modifier.Cratered };
                greyest = MathF.Max(greyest, Oklab.Of(SkySystem.For(spec).Palette.Bedrock).Chroma);
                continue;
            }
            if (spec.Airless) continue;
            WorldPalette p = SkySystem.For(spec).Palette;
            Oklab zenith = Oklab.Of(p.Zenith), band = Oklab.Of(p.Band), plants = Oklab.Of(p.Leaf), water = Oklab.Of(Rgb.Lerp(p.Shallow, p.Deep, 0.5f)), soil = Oklab.Of(p.Ground(Biome.Grassland, 0.6f, 0.5f));
            Oklab[] these = { zenith, band, plants, water, soil };
            for (int e = 0; e < Elements.Length; e++)
                if ((e != 2 || p.Life > 0f) && (e != 3 || a != Archetype.Lava)) colours[e].Add(these[e]);
            gaps.Add(Oklab.HueGap(MathF.Atan2(zenith.B + band.B, zenith.A + band.A), (p.Life >= 0.8f ? plants : soil).Hue) * 180f / MathF.PI);
            if (a != Archetype.Lava && water.Chroma > 0.08f && (Deg(water) < 45f || Deg(water) > 345f)) red++;
            if (p.Life > 0f && plants.Chroma > 0.08f && Deg(plants) is > 215f and < 285f) blue++;
            if (zenith.Chroma > 0.08f && Deg(zenith) is > 120f and < 170f) green++;
        }
        int worlds = gaps.Count;
        for (int e = 0; e < Elements.Length; e++)
        {
            var c = colours[e];
            float median = c.Select(o => o.Chroma).Order().ElementAt(c.Count / 2);
            int[] sixths = new int[6];
            foreach (Oklab o in c) sixths[(int)(Deg(o) / 60f) % 6]++;
            int used = sixths.Count(n => n > 0), most = sixths.Max();
            GD.Print($"BOLD {Elements[e]}: median chroma {median:F3}; hues by sixth from red {string.Join(" ", sixths)}");
            Check(median >= Floors[e], $"{Elements[e]} is bold: median Oklab chroma {median:F3} over {c.Count} worlds with air (at least {Floors[e]})");
            Check(used >= 5 && most <= c.Count * 0.45f, $"{Elements[e]} hues spread round the wheel: {used} of 6 sixths in use, the fullest holding {most} of {c.Count}");
        }
        gaps.Sort();
        float apart = gaps.Count(g => g >= 60f) / (float)worlds;
        Check(apart >= 0.75f && gaps[worlds / 2] >= 90f, $"land stands apart from its sky: hue gap at least 60 deg on {apart:P0} of {worlds} worlds, median {gaps[worlds / 2]:F0} deg");
        Check(red >= worlds / 100 && blue >= worlds / 100 && green >= worlds / 100, $"red water on {red}, blue plants on {blue} and a green sky on {green} of {worlds} worlds (each at least {worlds / 100})");
        Check(greyest < 0.04f, $"airless rock stays grey: highest Oklab chroma {greyest:F3}");
    }

    /// <summary>
    /// The grade adds chroma at each colour's own hue, never takes it, and greys stay grey. With its shoulder (the game's curve:
    /// the scene's light at half exposure in, <see cref="Grade.Game"/>), each scene colour keeps its hue however bright, the
    /// middle grey stays where it was, and the brightest white comes down under the screen's white.
    /// </summary>
    void Graded()
    {
        var rng = new Random((int)(RunSeed % int.MaxValue));
        float worstHue = 0f, lost = 0f, grey = 0f, worstScene = 0f;
        for (int i = 0; i < 4000; i++)
        {
            var c = new Rgb(rng.NextSingle(), rng.NextSingle(), rng.NextSingle());
            Oklab a = Oklab.Of(c), b = Oklab.Of(Grade.Of(c));
            if (a.Chroma > 0.03f) worstHue = MathF.Max(worstHue, Oklab.HueGap(a.Hue, b.Hue) * 180f / MathF.PI);
            lost = MathF.Max(lost, a.Chroma - b.Chroma);
            // The same scene colour (0..2 a channel) through the shoulder.
            Oklab scene = Oklab.FromLinear(2f * Oklab.Linear(c.R), 2f * Oklab.Linear(c.G), 2f * Oklab.Linear(c.B)), shown = Oklab.Of(Grade.Of(c, true));
            if (scene.Chroma > 0.05f && shown.Chroma > 0.03f) worstScene = MathF.Max(worstScene, Oklab.HueGap(scene.Hue, shown.Hue) * 180f / MathF.PI);
        }
        for (int i = 0; i <= 10; i++)
            grey = MathF.Max(grey, MathF.Max(Oklab.Of(Grade.Of(new Rgb(i / 10f, i / 10f, i / 10f))).Chroma, Oklab.Of(Grade.Of(new Rgb(i / 10f, i / 10f, i / 10f), true)).Chroma));
        float mid = Oklab.Linear(Grade.Of(new Rgb(Oklab.Encode(0.09f), Oklab.Encode(0.09f), Oklab.Encode(0.09f)), true).G), top = Oklab.Linear(Grade.Of(new Rgb(1f, 1f, 1f), true).G);
        Check(worstHue < 3f && lost < 0.01f && grey < 0.002f, $"the grade keeps hue (worst {worstHue:F1} deg) and never greys (most chroma lost {lost:F3}; greys stay grey, {grey:F4})");
        Check(worstScene < 4f && MathF.Abs(mid - 0.18f) < 0.012f && top < 0.98f, $"the shoulder keeps each scene colour's hue (worst {worstScene:F1} deg), middle grey at {mid:F3} (0.18 in), the brightest white at {top:F2}");
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
