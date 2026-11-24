using System;
using Godot;
using Planets.Gen;
using Wildlife;
using Rng = Wildlife.Rng;

namespace Slice.Surface;

/// <summary>
/// Life follows the land. Each spot of the world has a richness from its ground (its biome, water
/// near it, how steep and how high it is), against the world's own average, so an arid world's
/// oasis is as rich for it as a meadow is for a green one: more lives in the valleys, the woods and
/// by water, less on bare rock and high ground. And each kind keeps to its own ground: herds on open
/// grass, small things in scrub and at wood edges, hunters in the woods and hills, ambushers in thick
/// cover, fliers by trees, water and cliffs. Spots are kept by both, so what you meet where you are is
/// what would live there, in the numbers the land would hold.
/// </summary>
public partial class WildlifePatch
{
    /// <summary>The world's mean richness over its land (set with the world).</summary>
    float _meanRich = 1f;

    enum Ground { Water, Open, Wooded, Wet, Rough, Scrub }

    static Ground GroundOf(Biome b) => b switch
    {
        Biome.DeepSea or Biome.ShallowSea or Biome.FrozenSea => Ground.Water,
        Biome.Forest or Biome.PineForest or Biome.Jungle or Biome.Rainforest or Biome.CloudForest or Biome.FungalForest or Biome.GlowMoss => Ground.Wooded,
        Biome.Swamp or Biome.Riverbank or Biome.Oasis or Biome.SporeMarsh or Biome.HotSpring or Biome.Beach => Ground.Wet,
        Biome.Cliff or Biome.RedRock or Biome.Badlands or Biome.MesaTop or Biome.CrystalSpires or Biome.Obsidian or Biome.Glacier or Biome.BlueIce or Biome.LavaField or Biome.Sulfur => Ground.Rough,
        Biome.Scrub or Biome.Mycelium or Biome.Sinter => Ground.Scrub,
        _ => Ground.Open,
    };

    /// <summary>How much life a biome carries, roughly: thick wet growth most, ice, salt and lava least.</summary>
    static float LifeOf(Biome b) => b switch
    {
        Biome.Riverbank or Biome.Rainforest or Biome.Oasis => 1.3f,
        Biome.Jungle or Biome.Forest => 1.2f,
        Biome.Meadow or Biome.Swamp or Biome.FungalForest => 1.1f,
        Biome.Grassland or Biome.CloudForest or Biome.SporeMarsh => 1f,
        Biome.PineForest or Biome.GlowMoss => 0.9f,
        Biome.HotSpring or Biome.Mycelium => 0.8f,
        Biome.Scrub => 0.6f,
        Biome.Beach or Biome.CrystalPlain => 0.5f,
        Biome.Tundra or Biome.Chalk or Biome.CrystalSpires => 0.42f,
        Biome.IceTundra or Biome.Cliff or Biome.MesaTop or Biome.RedRock or Biome.Badlands or Biome.Sinter => 0.32f,
        Biome.Dunes or Biome.Snowfield or Biome.Basalt => 0.22f,
        Biome.Erg or Biome.Snow or Biome.Ash => 0.15f,
        Biome.SaltFlat or Biome.Glacier or Biome.BlueIce or Biome.Obsidian or Biome.Sulfur => 0.1f,
        Biome.LavaField => 0.04f,
        _ => 0f,
    };

    /// <summary>A spot's richness (zone frame), as its ground has it, not yet against the world's.</summary>
    float RichAt(double x, double z)
    {
        WorldMap map = _planet?.Map;
        if (map == null) return 1f;
        double wx = Mathx.WrapX(x, map.WidthMeters), wz = Mathx.Wrap(z, map.HeightMeters);
        int cell = map.CellAt(wx, wz);
        float r = LifeOf((Biome)map.Biome[cell]);
        if (map.Has(cell, CellFlags.River) || map.Has(cell, CellFlags.Riparian)) r *= 1.3f;
        else if (map.Has(cell, CellFlags.Coast)) r *= 1.1f;
        float slope = map.Sample(map.Slope, wx, wz), height = map.Sample(map.Elevation, wx, wz);
        r *= 1f - Math.Clamp((slope - 0.25f) * 0.9f, 0f, 0.55f);
        r *= 1f - Math.Clamp((height - 400f) / 900f, 0f, 0.5f);
        return r;
    }

    /// <summary>A spot's richness against the world's own average (patch frame): 1 is the world's usual, more in its rich country.</summary>
    public float Richness(Vector3 local)
    {
        Vector3 z = FromPatch(local);
        return Math.Clamp(RichAt(z.X, z.Z) / MathF.Max(0.05f, _meanRich), 0.15f, 1.8f);
    }

    /// <summary>The world's mean richness over its land, from a spread of samples (once, with the world).</summary>
    void MeasureRichness()
    {
        WorldMap map = _planet?.Map;
        if (map == null) return;
        float sum = 0f;
        int n = 0;
        for (int i = 0; i < 600; i++)
        {
            double x = (i * 0.618034 % 1.0) * map.WidthMeters, z = ((i * 0.7548777 + 0.1) % 1.0) * map.HeightMeters;
            int cell = map.CellAt(x, z);
            if (map.Has(cell, CellFlags.Water)) continue;
            sum += RichAt(x, z);
            n++;
        }
        _meanRich = n == 0 ? 1f : MathF.Max(0.05f, sum / n);
    }

    /// <summary>How well a spot suits a kind (patch frame), 0.2 to 1.5: each to its own ground.</summary>
    float Suits(Species s, Vector3 local)
    {
        WorldMap map = _planet?.Map;
        if (map == null) return 1f;
        Vector3 z = FromPatch(local);
        double wx = Mathx.WrapX(z.X, map.WidthMeters), wz = Mathx.Wrap(z.Z, map.HeightMeters);
        int cell = map.CellAt(wx, wz);
        Ground g = GroundOf((Biome)map.Biome[cell]);
        bool water = map.Has(cell, CellFlags.River) || map.Has(cell, CellFlags.Riparian) || map.Has(cell, CellFlags.Coast);
        float slope = map.Sample(map.Slope, wx, wz);
        if (s.Flies)
            return g is Ground.Wooded or Ground.Wet or Ground.Rough || water || slope > 0.5f ? 1.4f : 0.6f;
        if (s.Plan == BodyPlan.Giant)
            return g == Ground.Open && slope < 0.3f ? 1.3f : 0.5f;
        float small = s.Body.Size < 0.7f ? 1f : 0f;
        return s.Behaviour switch
        {
            Behaviour.Herd => g == Ground.Open && slope < 0.25f ? 1.5f : g is Ground.Scrub or Ground.Wet ? 0.8f : 0.3f,
            Behaviour.Grazer or Behaviour.HarmlessOddity => small > 0f
                ? g is Ground.Scrub or Ground.Wooded or Ground.Wet ? 1.4f : g == Ground.Open ? 0.8f : 0.4f
                : g is Ground.Open or Ground.Wet or Ground.Scrub ? 1.2f : 0.6f,
            Behaviour.Predator or Behaviour.PackHunter => g is Ground.Wooded or Ground.Scrub || slope is > 0.15f and < 0.6f ? 1.3f : g == Ground.Open ? 0.8f : 0.5f,
            Behaviour.Ambusher => g is Ground.Wooded or Ground.Wet ? 1.5f : g == Ground.Scrub ? 0.9f : 0.3f,
            Behaviour.Swarm => g == Ground.Wet || water ? 1.5f : g == Ground.Wooded ? 0.9f : 0.4f,
            Behaviour.Scavenger => g is Ground.Open or Ground.Scrub ? 1.2f : 0.7f,
            _ => 1f,
        };
    }

    /// <summary>Whether to keep a spot for a kind: by its richness and how well it suits the kind (a roll, so poorer ground still holds some).</summary>
    bool Keep(Species s, Vector3 local, Rng rng) => rng.Chance(Math.Clamp(Richness(local) * Suits(s, local) / 1.4f, 0.04f, 1f));
}
