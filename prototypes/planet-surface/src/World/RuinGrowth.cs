using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using Planets.Look;
using Planets.Surface;
using PlanetSurface.Poi;
using PlanetSurface.Terrain;
using PoiGenerator.Gen;
using Hash = Planets.Gen.Hash;
using Int3 = PoiGenerator.Gen.Int3;

namespace PlanetSurface.World;

/// <summary>
/// The trees through a planet's ruins. The POI generator leaves only spots (<see cref="GrowthSpot"/>);
/// this picks what stands on each from the same flora rules that scatter the biome round it, sized to
/// fit between the walls, and hands it to the far builder as one more scatter instance: the same meshes,
/// shadows, colliders, felling and yields as every other tree. A tree stands on the ground or a ground
/// floor, never on an upper storey or a buried one, and never in a block; a biome with no trees grows none.
/// </summary>
public static class RuinGrowth
{
    /// <summary>The smallest a ruin's tree may be, against the biome's own smallest (0.7): stunted, but itself.</summary>
    const float SmallestScale = 0.5f;

    /// <summary>The trees that fit the planet's ruins, by the scatter id each one takes.</summary>
    public static Dictionary<long, PlantedFlora> Plan(IReadOnlyList<PoiSite> sites, PlanetTerrainSource source, GroundPalette pal, double width, double height)
    {
        var planted = new Dictionary<long, PlantedFlora>();
        foreach (PoiSite site in sites)
        {
            PoiInstance inst = site.Instance;
            int stood = 0;
            foreach (GrowthSpot spot in inst.Growth)
            {
                Int3 c = spot.Cell;
                double x = Mathx.WrapX(site.Origin.X + c.X + 0.5, width), z = Mathx.Wrap(site.Origin.Z + c.Z + 0.5, height);
                FloraRule[] trees = BiomeLook.FloraOf(pal.BiomeAt(x, z)).Where(r => FloraMeshes.IsTree(r.Kind)).ToArray();
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

                // The biome's own size if it fits, else smaller.
                float scale = Mathx.Lerp(0.7f, 1.35f, Hash.Unit(h, 3)) * FloraMeshes.BaseScale(rule.Kind);
                while (scale >= SmallestScale && !Fits(inst.Blocks, c, rule.Kind, scale)) scale *= 0.85f;
                if (scale < SmallestScale) continue;

                long id = FarBuilder.ScatterIdAt(x, z);
                if (planted.ContainsKey(id)) continue;
                planted[id] = new PlantedFlora(rule.Kind, x, z, foot, scale, Hash.Unit(h, 4) * MathF.Tau, FarBuilder.Tint(rule, Hash.Unit(h, 5)));
                stood++;
            }
        }
        return planted;
    }

    /// <summary>Whether a tree of a kind and scale stands clear of every wall, roof and floor round its root.</summary>
    public static bool Fits(BlockGrid blocks, Int3 root, Flora kind, float scale)
    {
        foreach (var (from, to, radius) in FloraMeshes.Reach(kind))
            if (!Growth.ClearBand(blocks, root, from * scale, to * scale, radius * scale)) return false;
        return true;
    }
}
