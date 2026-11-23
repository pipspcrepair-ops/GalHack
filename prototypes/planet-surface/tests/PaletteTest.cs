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
/// - <b>Natural, vivid and mixed</b> (owner, 23 Nov), over hundreds of palettes of every type with air: wholly natural,
///   wholly vivid and mixed worlds each common, each part's hues spread round the wheel, red water, blue grass and a green
///   sky all turning up. An airless body's rock stays grey.
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
            Range(600);
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

    static readonly string[] Parts = { "zenith", "plants", "water", "soil" };

    /// <summary>
    /// <paramref name="count"/> fresh palettes of every type (palettes alone, no maps). Each part of a world with air (its sky
    /// straight up, its plants, its sea, its soil) reads vivid over Oklab chroma 0.13 and earthy under 0.09. Wholly natural
    /// worlds (no vivid part), wholly vivid ones (no earthy part, two vivid or more) and mixed ones (both) must each make up a
    /// good share (owner, 23 Nov: all three wanted and common, never every world saturated everywhere); each part's hues spread
    /// round the wheel (at least five of its six sixths in use, none holding half the worlds); red water, blue plants and a green
    /// sky each turn up on at least one world in a hundred. Every fourth roll is an airless body, whose rock (unless it rusted
    /// while it had air) stays grey.
    /// </summary>
    void Range(int count)
    {
        var colours = Parts.Select(_ => new List<Oklab>()).ToArray();
        int natural = 0, mixed = 0, vivid = 0, worlds = 0, red = 0, blue = 0, green = 0;
        float greyest = 0f;
        static float Deg(Oklab o) => (o.Hue * 180f / MathF.PI + 360f) % 360f;
        for (int i = 0; i < count; i++)
        {
            var (a, v) = ((Archetype)(Fresh() % 5), (int)(Fresh() % 2));
            PlanetSpec spec = PlanetSpec.Roll(Fresh(), a, v, PlanetSpec.RollWidthKm(_seeds));
            if (i % 4 == 0)
            {
                spec = spec with { Archetype = PlanetSpec.Craters(a) ? a : Archetype.Arid, Modifiers = Modifier.Cratered };
                // (An iron-rich one rusted while it still had air, and keeps it: its own colour, the vacuum's albedo, not the palette's.)
                WorldPalette moon = SkySystem.For(spec).Palette;
                if (moon.Iron <= 0.75f) greyest = MathF.Max(greyest, Oklab.Of(moon.Bedrock).Chroma);
                continue;
            }
            if (spec.Airless) continue;
            WorldPalette p = SkySystem.For(spec).Palette;
            Oklab?[] these = { Oklab.Of(p.Zenith), p.Life > 0f ? Oklab.Of(p.Leaf) : null, a != Archetype.Lava ? Oklab.Of(Rgb.Lerp(p.Shallow, p.Deep, 0.5f)) : null, Oklab.Of(p.Ground(Biome.Grassland, 0.6f, 0.5f)) };
            int vividParts = 0, earthyParts = 0;
            for (int e = 0; e < Parts.Length; e++)
            {
                if (these[e] is not Oklab o) continue;
                colours[e].Add(o);
                if (o.Chroma > 0.13f) vividParts++;
                if (o.Chroma < 0.09f) earthyParts++;
            }
            worlds++;
            if (vividParts == 0) natural++;
            else if (earthyParts == 0 && vividParts >= 2) vivid++;
            else if (earthyParts > 0) mixed++;
            if (these[2] is Oklab water && water.Chroma > 0.08f && (Deg(water) < 45f || Deg(water) > 345f)) red++;
            if (these[1] is Oklab plants && plants.Chroma > 0.08f && Deg(plants) is > 215f and < 285f) blue++;
            if (these[0] is Oklab zenith && zenith.Chroma > 0.08f && Deg(zenith) is > 120f and < 170f) green++;
        }
        GD.Print($"RANGE {worlds} worlds with air: natural {natural}, mixed {mixed}, vivid {vivid}, between {worlds - natural - mixed - vivid}");
        Check(natural >= worlds * 0.12f && vivid >= worlds * 0.1f && mixed >= worlds * 0.35f,
            $"natural, vivid and mixed worlds all common: {natural * 100 / worlds}% wholly natural, {vivid * 100 / worlds}% wholly vivid, {mixed * 100 / worlds}% mixed of {worlds}");
        for (int e = 0; e < Parts.Length; e++)
        {
            var c = colours[e];
            int[] sixths = new int[6];
            foreach (Oklab o in c) sixths[(int)(Deg(o) / 60f) % 6]++;
            int used = sixths.Count(n => n > 0), most = sixths.Max();
            GD.Print($"RANGE {Parts[e]}: hues by sixth from red {string.Join(" ", sixths)}; earthy {c.Count(o => o.Chroma < 0.09f)}, vivid {c.Count(o => o.Chroma > 0.13f)} of {c.Count}");
            Check(used >= 5 && most < c.Count / 2, $"{Parts[e]} hues spread round the wheel: {used} of 6 sixths in use, the fullest holding {most} of {c.Count}");
        }
        Check(red >= worlds / 100 && blue >= worlds / 100 && green >= worlds / 100, $"red water on {red}, blue plants on {blue} and a green sky on {green} of {worlds} worlds (each at least {worlds / 100})");
        Check(greyest < 0.04f, $"airless rock that never rusted stays grey: highest Oklab chroma {greyest:F3}");
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
