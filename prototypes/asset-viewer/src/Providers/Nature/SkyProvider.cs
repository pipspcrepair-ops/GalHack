using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using PoiGenerator.Gen;
using Archetype = Planets.Gen.Archetype;

namespace AssetViewer.Providers.Nature;

/// <summary>
/// Skies from the ground of a world, by world type, star class, time of day and cloud, with the bodies the
/// game's systems roll (rings, moons, giants), eclipses, and the galaxy's depth (the bulge, the central black
/// hole). Each is the game's sky rig over a streamed stretch of ground, the camera looking at the point.
/// </summary>
public sealed partial class SkyProvider : IAssetProvider
{
    public string Category => "Sky";
    public IEnumerable<(string Prototype, string Folder)> Mounts => new[] { ("planet-surface", "shaders") };

    static AssetOption[] Options(string time, string cloud = "Clear") => new[]
    {
        new AssetOption("Time", SurfaceView.Times) { Default = Math.Max(0, Array.IndexOf(SurfaceView.Times, time)) },
        new AssetOption("Cloud", "Clear", "Scattered", "Overcast") { Default = Math.Max(0, Array.IndexOf(new[] { "Clear", "Scattered", "Overcast" }, cloud)) },
    };

    /// <summary>A world whose sky has what is looked for, found by rolling worlds (their skies are cheap to roll).</summary>
    static PlanetSpec Find(ulong seed, Func<PlanetSpec, SkySystem, bool> has, int? type = null)
    {
        var types = NatureWorlds.Types().ToList();
        for (int k = 0; k < 900; k++)
        {
            var t = types[type ?? k % types.Count];
            PlanetSpec spec = NatureWorlds.SpecOf(t.Archetype, t.Variant, seed + (ulong)k * 7919);
            if (has(spec, SkySystem.For(spec))) return spec;
        }
        var d = types[type ?? 0];
        return NatureWorlds.SpecOf(d.Archetype, d.Variant, seed);
    }

    static ulong SeedOf(BuildRequest r) => r.Seed == 0 ? 1UL : r.Seed;

    static AssetInstance Make(BuildRequest r, PlanetSpec spec, string focus, bool eclipse = false, float? depth = null)
    {
        var world = depth is float d ? NatureWorlds.Get(spec, d) : NatureWorlds.Get(spec);
        return new SkyView(world, new SkyView.Scene(focus, r.Get("Time", "Dusk"), r.Get("Cloud", "Clear"), depth, eclipse));
    }

    /// <summary>The home-star depths of the galaxy's zones, read off the game's own zone function.</summary>
    static IEnumerable<(string Zone, float Depth)> Zones()
    {
        string path = NatureWorlds.Prototypes + "poi-generator/data/world-layers.txt";
        if (!File.Exists(path)) yield break;
        foreach (WorldLayer l in LayerParser.Parse(File.ReadAllText(path), "world-layers.txt").Where(l => l.Kind == LayerKind.Zone && !l.Off))
        {
            float? depth = null;
            for (float d = 0.02f; d <= 0.99f && depth == null; d += 0.02f)
                if (WorldForce.ZoneAt(d) == l.Id) depth = d;
            if (depth is float dd) yield return (l.Id, dd + 0.02f);
        }
    }

    public IEnumerable<AssetEntry> Entries()
    {
        foreach (var t in NatureWorlds.Types())
        {
            var type = t;
            yield return new AssetEntry
            {
                Id = $"type-{type.Archetype}-{type.Variant}", Group = "From the ground/World types", Name = type.Label, Randomisable = true,
                Options = Options("Dusk"),
                Build = r => Make(r, NatureWorlds.SpecOf(type.Archetype, type.Variant, SeedOf(r)), "sun"),
            };
        }
        foreach (StarClass star in Enum.GetValues<StarClass>())
        {
            StarClass s = star;
            yield return new AssetEntry
            {
                Id = "sun-" + s, Group = "From the ground/Suns", Name = $"{s} sun", Randomisable = true, Options = Options("Morning"),
                Build = r => Make(r, Find(SeedOf(r), (spec, sky) => sky.Star == s), "sun"),
            };
        }
        yield return new AssetEntry
        {
            Id = "bodies-rings", Group = "From the ground/Bodies", Name = "Ringed planet", Randomisable = true, Options = Options("Dusk"),
            Build = r => Make(r, Find(SeedOf(r), (spec, sky) => sky.HasRingedBody), "rings"),
        };
        yield return new AssetEntry
        {
            Id = "bodies-moons", Group = "From the ground/Bodies", Name = "Moons", Randomisable = true, Options = Options("Dusk"),
            Build = r => Make(r, Find(SeedOf(r), (spec, sky) => sky.Bodies.Count(b => b.Kind == BodyKind.Moon) >= 2), "moon"),
        };
        yield return new AssetEntry
        {
            Id = "bodies-giant", Group = "From the ground/Bodies", Name = "Neighbouring giant", Randomisable = true, Options = Options("Dusk"),
            Build = r => Make(r, Find(SeedOf(r), (spec, sky) => sky.Bodies.Any(b => b.Kind == BodyKind.GasGiant)), "giant"),
        };
        yield return new AssetEntry
        {
            Id = "events-eclipse", Group = "From the ground/Events", Name = "Eclipse", Randomisable = true, Options = Options("Noon"),
            Build = r => Make(r, Find(SeedOf(r), (spec, sky) => sky.Bodies.Any(b => b.Kind == BodyKind.Moon && b.Radius > 0.02f)), "sun", eclipse: true),
        };
        yield return new AssetEntry
        {
            Id = "events-stars-through-cloud", Group = "From the ground/Events", Name = "Stars through cloud", Randomisable = true, Options = Options("Night", "Scattered"),
            Build = r => Make(r, NatureWorlds.SpecOf(Archetype.Temperate, 0, SeedOf(r)), "stars"),
        };
        foreach (var (zone, depth) in Zones())
        {
            string z = zone;
            float dpt = depth;
            yield return new AssetEntry
            {
                Id = "galaxy-" + z, Group = "From the ground/Galaxy depth", Name = $"Star in the {z} zone", Randomisable = true, Options = Options("Night"),
                Build = r => Make(r, NatureWorlds.SpecOf(Archetype.Temperate, 0, SeedOf(r)), z == "core" ? "hole" : "stars", depth: dpt),
            };
        }
    }
}
