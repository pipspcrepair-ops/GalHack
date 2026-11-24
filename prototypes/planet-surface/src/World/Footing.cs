using System;
using Planets.Gen;

namespace PlanetSurface.World;

/// <summary>
/// What the ground will carry. Sand (a beach, dunes, an erg) shifts and gives under weight: nobody set a tower, a
/// block of flats, a works or a base on it. Rock, clay and firm soil carry anything. So heavy buildings look for
/// firm ground; sandy ground is left open, or takes only light buildings (a hut, a house, a shed).
/// </summary>
public static class Footing
{
    /// <summary>Loose sand: no footing for anything heavy.</summary>
    public static bool Sandy(Biome b) => b is Biome.Beach or Biome.Dunes or Biome.Erg;

    /// <summary>How much of a box of ground (centre and half sizes, metres) is sand, 0 to 1, from a grid of samples across it.</summary>
    public static float SandShare(WorldMap map, double cx, double cz, double halfX, double halfZ, int across = 5)
    {
        int sand = 0, n = 0;
        double w = map.WidthMeters, h = map.HeightMeters;
        for (int j = 0; j < across; j++)
        for (int i = 0; i < across; i++)
        {
            double x = cx + halfX * (2.0 * i / (across - 1) - 1), z = cz + halfZ * (2.0 * j / (across - 1) - 1);
            n++;
            if (Sandy((Biome)map.Biome[map.CellAt(Mathx.WrapX(x, w), Mathx.Wrap(z, h))])) sand++;
        }
        return n == 0 ? 0f : sand / (float)n;
    }

    /// <summary>Whether a place is heavy enough to need firm ground: a town, a city, a base or a port, or anything big or tall.</summary>
    public static bool Heavy(PoiGenerator.Gen.PoiTier tier, double sizeX, double sizeZ, double height) =>
        tier is PoiGenerator.Gen.PoiTier.Town or PoiGenerator.Gen.PoiTier.City or PoiGenerator.Gen.PoiTier.Base or PoiGenerator.Gen.PoiTier.Spaceport
        || Math.Max(sizeX, sizeZ) >= 40 || height >= 12;
}
