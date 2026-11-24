using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Plants;
using GalHack.Terrain;
using Planets.Gen;
using Planets.Look;
using Planets.Surface;
using PlanetSurface.Poi;
using PlanetSurface.Terrain;
using PoiGenerator.Gen;
using Biome = Planets.Gen.Biome;
using Hash = Planets.Gen.Hash;
using Int3 = PoiGenerator.Gen.Int3;

namespace PlanetSurface.World;

/// <summary>
/// The trees through a planet's ruins. The POI generator leaves only spots (<see cref="GrowthSpot"/>);
/// this picks what stands on each from the same flora rules that scatter the biome round it, sized to
/// fit between the walls, and hands it to the far builder as one more scatter instance of the world's own
/// species: the same meshes, shadows, colliders, felling and yields as every other tree. A tree stands on the ground or a ground
/// floor, never on an upper storey or a buried one, and never in a block; a biome with no trees grows none.
/// </summary>
public static class RuinGrowth
{
    /// <summary>The smallest a ruin's tree may be, against the biome's own smallest (0.7): stunted, but itself.</summary>
    const float SmallestScale = 0.5f;

    /// <summary>The trees that fit one ruin, as it is built, into the palette's planted set by the scatter id each one takes.</summary>
    public static void Plant(PoiSite site, PoiInstance inst, PlanetTerrainSource source, GroundPalette pal, double width, double height)
    {
        var planted = pal.Planted;
        {
            int stood = 0;
            foreach (GrowthSpot spot in inst.Growth)
            {
                Int3 c = spot.Cell;
                // A typed place's open ground is its made ground, graded and laid (a yard, a pad, the way in): nothing roots
                // there (no tree in a fuel farm's bund or a pad's pit).
                if (site.Whole != null && Open(inst.Blocks, c)) continue;
                double x = Mathx.WrapX(site.Origin.X + c.X + 0.5, width), z = Mathx.Wrap(site.Origin.Z + c.Z + 0.5, height);
                Biome biome = pal.BiomeAt(x, z);
                FloraRule[] trees = pal.Flora.RulesOf(biome).Where(r => FloraMeshes.IsTree(r.Kind)).ToArray();
                if (trees.Length == 0) continue;
                float total = trees.Sum(r => r.Density);
                // A thin woodland's ruins hold fewer trees than a forest's.
                if (stood >= (int)MathF.Ceiling(inst.GrowthWanted * Math.Clamp(total / 3f, 0.34f, 1f))) break;

                ulong h = Hash.Of(0x7133, site.Origin.X * 4099L + c.X, site.Origin.Y * 4099L + c.Y, site.Origin.Z * 4099L + c.Z);
                if (pal.DeadZones.Count > 0 && Hash.Unit(h, 1) < pal.Deadness(x, z)) continue;

                // On the ground, or the ground floor laid on it (the land under that may be cut away for a cellar):
                // nothing roots on a storey in the air, or under the land.
                float floor = site.Origin.Y + c.Y, ground = source.SurfaceHeight(x, z);
                bool floored = !inst.Blocks.Get(c + Int3.Down).IsAir;
                if (Math.Abs(c.Y) > 1 || ground > floor + 1f || (!floored && ground < floor - 1.5f)) continue;
                float foot = floored ? floor : ground;

                FloraRule rule = trees[^1];
                float acc = Hash.Unit(h, 2) * total;
                foreach (FloraRule r in trees)
                    if ((acc -= r.Density) < 0f) { rule = r; break; }

                // The species growing round here.
                Plant plant = pal.Flora.Pick(rule.Kind, biome, x, z, Hash.Unit(h, 6));
                if (plant == null) continue;

                // Its own size if it fits, else smaller.
                float scale = pal.Flora.TreeScale(plant, Hash.Unit(h, 3), Hash.Unit(h, 7)) ?? Mathx.Lerp(0.78f, 1.24f, Hash.Unit(h, 3));
                while (scale >= SmallestScale && !Fits(inst.Blocks, c, plant, scale)) scale *= 0.85f;
                if (scale < SmallestScale) continue;

                long id = FarBuilder.ScatterIdAt(x, z);
                if (!planted.TryAdd(id, new PlantedFlora(rule.Kind, x, z, foot, scale, Hash.Unit(h, 4) * MathF.Tau, pal.Flora.Tint(plant, Hash.Unit(h, 5), x, z), plant))) continue;
                stood++;
            }
        }
    }

    /// <summary>Whether a cell is under open sky: nothing over it as high as a place stands.</summary>
    static bool Open(BlockGrid blocks, Int3 c)
    {
        for (int y = 1; y < 80; y++)
            if (!blocks.Get(c + Int3.Up * y).IsAir) return false;
        return true;
    }

    /// <summary>Whether a tree of a kind and scale stands clear of every wall, roof and floor round its root.</summary>
    public static bool Fits(BlockGrid blocks, Int3 root, Plant plant, float scale)
    {
        foreach (var (from, to, radius) in plant.Reach)
            if (!Growth.ClearBand(blocks, root, from * scale, to * scale, radius * scale)) return false;
        return true;
    }
}
