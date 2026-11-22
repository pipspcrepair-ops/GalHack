using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface.Tests;

/// <summary>
/// Every world's natural colours, measured on fresh worlds rolled for this run (never a pinned seed):
/// - <b>Variety with a basis.</b> Many meadow (temperate) worlds across stars, sizes and grounds: each
///   one's clear sky (zenith and horizon), plants, soil, rock and deep water in CIELAB. Each colour
///   must spread between worlds (median pair dE), few pairs may look the same at a glance, and almost
///   no two worlds may match in every colour at once. The same check runs on every other world type,
///   airless ones included.
/// - <b>Real light.</b> The night sky stays dark, and on a world of familiar chemistry no plant or
///   soil goes neon.
/// - <b>What greys the picture.</b> The grade's saturation through a day of weather on a meadow world
///   (Atmosphere's cut by overcast and dust), printed so its cause can be seen.
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
            Grade();
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
        Lab? Air(Rgb c) => spec.Airless ? null : Lab.Of(c);
        return new Looks(name, new[] { Air(sky.Zenith), Air(sky.Horizon), plants, bare, Lab.Of(ground.Rock), Water(look.Deep), Water(look.Shallow) }, Lab.Of(sky.NightZenith));
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
        if (kind == "meadow")
        {
            float loud = worlds.SelectMany(w => new[] { w.Colors[2], w.Colors[3] }).Where(x => x != null).Max(x => x.Value.Chroma);
            Check(loud < 62f, $"meadow plants and soil never go neon (highest chroma {loud:F0})");
        }
    }

    const float Same = 10f, TwinShare = 0.08f;

    /// <summary>
    /// The grade's saturation (<see cref="AtmosphereState.Saturation"/>) through six hours of a meadow world's
    /// own weather, and the dust and overcast behind it: printed as evidence, not checked (its rule is the weather's).
    /// </summary>
    void Grade()
    {
        WeatherData data = WeatherView.WeatherRig.Data;
        ulong seed = Fresh();
        SkySystem sky = SkySystem.For(PlanetSpec.Roll(seed, Archetype.Temperate, 0));
        var sim = new WeatherSim(data, "temperate", seed);
        var sat = new List<float>();
        float dust = 0f, overcast = 0f;
        for (int s = 0; s < 6 * 360; s++)
        {
            sim.Step(10f, default);
            AtmosphereState a = Atmosphere.Compute(sky, sim.Current, 0.7f);
            sat.Add(a.Saturation);
            dust += sim.Current.Dust;
            overcast += a.Overcast;
        }
        sat.Sort();
        GD.Print($"GRADE meadow seed {seed}: saturation median {sat[sat.Count / 2]:F2}, low {sat[sat.Count / 10]:F2}, high {sat[^1]:F2}; mean dust {dust / sat.Count:F2}, mean overcast {overcast / sat.Count:F2}; clear air {Atmosphere.Compute(sky, Atmosphere.Clear(), 0.7f).Saturation:F2}");
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
