using System;
using GalHack.Music;
using GalHack.Stars;
using Godot;
using Planets.Gen;
using PoiGenerator.Gen;
using Slice.Orbit;
using Slice.Places;
using Slice.Vessels;
using Biome = Planets.Gen.Biome;

namespace Slice.Game;

/// <summary>
/// Reads the world for the music: what kind of world it is, and where on it the listener stands.
/// Only the world's own layers and the place and time are read here. Nothing about danger, fights
/// or finds reaches the music, by design.
/// </summary>
public static class MusicScenes
{
    /// <summary>A world's mood. Airless and Bloom-taken worlds aren't in the data yet, so the caller says.</summary>
    public static DroneMood MoodOf(PlanetSpec spec, bool airless = false, bool bloom = false)
    {
        if (bloom) return DroneMood.Hollow;
        if (airless) return DroneMood.Airless;
        return spec.Archetype switch
        {
            Archetype.Arid => DroneMood.Arid,
            Archetype.Ice => DroneMood.Ice,
            Archetype.Lava => DroneMood.Lava,
            Archetype.Alien => spec.Variant == 1 ? DroneMood.Swamp : DroneMood.Ice,
            _ => spec.Has(Modifier.Archipelago) ? DroneMood.Ocean : DroneMood.Temperate,
        };
    }

    public static MusicGround GroundOf(Biome biome) => biome switch
    {
        Biome.Forest or Biome.PineForest or Biome.Jungle or Biome.Rainforest or Biome.CloudForest => MusicGround.Forest,
        Biome.Swamp or Biome.Riverbank or Biome.Oasis or Biome.HotSpring or Biome.SporeMarsh => MusicGround.Wet,
        Biome.Dunes or Biome.Erg or Biome.SaltFlat or Biome.Scrub => MusicGround.Dry,
        Biome.Cliff or Biome.RedRock or Biome.Badlands or Biome.MesaTop or Biome.Chalk => MusicGround.Stone,
        Biome.Tundra or Biome.Snow or Biome.Glacier or Biome.Snowfield or Biome.IceTundra or Biome.BlueIce or Biome.FrozenSea => MusicGround.Snow,
        Biome.Basalt or Biome.Ash or Biome.LavaField or Biome.Obsidian or Biome.Sulfur or Biome.Sinter => MusicGround.Fire,
        Biome.Beach or Biome.DeepSea or Biome.ShallowSea => MusicGround.Shore,
        Biome.CrystalPlain or Biome.CrystalSpires or Biome.GlowMoss or Biome.FungalForest or Biome.Mycelium => MusicGround.Strange,
        _ => MusicGround.Open,
    };

    /// <summary>Where the day has got to: time of day runs 0 to 1 from midnight.</summary>
    public static DayPart DayOf(double time) => time switch
    {
        < 0.2 => DayPart.Night,
        < 0.3 => DayPart.Dawn,
        < 0.72 => DayPart.Day,
        < 0.82 => DayPart.Dusk,
        _ => DayPart.Night,
    };

    public static float TechOf(TechLevel level) => level switch { TechLevel.Low => 0.1f, TechLevel.High => 0.9f, _ => 0.5f };

    /// <summary>The fixed facts of the world being played.</summary>
    public static MusicWorld WorldOf(Game game)
    {
        var planet = game.Surface.Planet;
        WorldMap map = planet.Map;
        float heat = 0f, damp = 0f;
        int cells = map.Temperature.Length, step = Math.Max(1, cells / 512), count = 0;
        for (int i = 0; i < cells; i += step, count++)
        {
            heat += map.Temperature[i];
            damp += map.Humidity[i];
        }
        var star = planet.StarSystem.Star.Color;
        WorldProfile profile = planet.Profile;
        WorldReading layers = planet.Reading;
        return new MusicWorld(MoodOf(planet.Spec, bloom: layers.HollowWorld), game.World.Seed)
        {
            Heat = heat / Math.Max(1, count),
            Damp = damp / Math.Max(1, count),
            Star = Mathf.Clamp(0.5f + (star.B - star.R) * 0.8f, 0f, 1f),
            Tech = TechOf(profile.Before),
            Inhabited = profile.Inhabited,
            Theme = ThemeOf(profile, layers),
            Desolate = layers.Desolate,
            Strife = layers.Strife,
            Living = layers.Living,
        };
    }

    /// <summary>What became of the world, as its layers read: everyone gone, ash in the air, grown over, or twisted.</summary>
    public static MusicTheme ThemeOf(WorldProfile profile, WorldReading layers) =>
        profile.Exodus ? MusicTheme.Exodus
        : layers.Ash > 0.6f ? MusicTheme.Nuclear
        : MathF.Max(layers.Mutagen, layers.Swollen) > 0.5f ? MusicTheme.Mutagenic
        : layers.Overgrown > 0.5f || layers.HollowWorld ? MusicTheme.Overgrown
        : MusicTheme.None;

    /// <summary>Where on the surface the listener is now. Called about once a second, so it stays cheap.</summary>
    public static MusicScene Ground(Game g)
    {
        var s = g.Survivor;
        Vector3 eye = s.WorldEye();
        var planet = g.Surface.Planet;
        WorldMap map = planet.Map;
        int cell = map.Index((int)(eye.X / map.WidthMeters * map.Width), (int)(eye.Z / map.HeightMeters * map.Height));
        MusicGround ground = (map.Flags[cell] & CellFlags.Coast) != 0 ? MusicGround.Shore : GroundOf((Biome)map.Biome[cell]);
        DayPart day = DayOf(g.Surface.Sky.TimeOfDay);

        Place inside = null;
        foreach (Place p in g.Places.Standing)
            if (p.Roofed(eye))
            {
                inside = p;
                break;
            }
        float hollow = inside?.Inst.State == PoiState.Infested ? 0.7f : 0f;

        MusicPlace place = MusicPlace.Open;
        Vessel aboard = s.Aboard;
        if (g.Bunker != null && g.Bunker.Inside(eye, g.Surface)) place = MusicPlace.Bunker;
        else if (aboard != null && !s.OnOuterHull) place = aboard.IsBase ? MusicPlace.Base : MusicPlace.Ship;
        else if (s.Legs.Swimming && eye.Y < 0f) place = MusicPlace.Underwater;
        else if (inside != null) place = MusicPlace.Ruin;
        else if (g.Places.TownAt(eye) != null) place = MusicPlace.Town;
        else if (eye.Y < planet.GroundHeight(eye.X, eye.Z) - 3f) place = MusicPlace.Cave;
        return new MusicScene(place, ground, day, hollow);
    }

    /// <summary>Where in space: near the world, out beyond it, or wherever this star sits in the galaxy.</summary>
    public static MusicScene Sky(Game g)
    {
        GalaxySky stars = g.Surface.Sky.Stars;
        if (stars?.Galaxy != null && stars.Here >= 0)
        {
            Star here = stars.Galaxy.Stars[stars.Here];
            if (here.Region == GalHack.Stars.Region.Core) return new MusicScene(MusicPlace.GalacticCore);
            if (here.Nebula >= 0) return new MusicScene(MusicPlace.Nebula);
        }
        if (g.Zone is OrbitZone orbit && orbit.AboveShell(g.Survivor.WorldEye()) > orbit.GlobeRadius * 3f) return new MusicScene(MusicPlace.DeepSpace);
        return new MusicScene(MusicPlace.HighOrbit);
    }
}
