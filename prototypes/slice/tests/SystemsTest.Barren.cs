using System;
using System.Collections.Generic;
using System.Linq;
using Planets.Gen;
using PlanetSurface.Skies;
using Slice.Surface;

namespace Slice.Tests;

/// <summary>
/// Barren bodies (owner, 22 Nov: "Wouldn't the airless vacuum moons be grey cratered moonscapes?"): every airless body
/// but the start world (the type the player chose) is barren, icy past the cold line and metal on a heavy world,
/// unless it is a giant's tidally heated moon keeping its lava or warmed ice; never a desert. Each has the one vacuum
/// rule: a black sky at noon with the stars out, no weather (its air under <see cref="SurfaceWeather.NoAir"/>), no
/// cloud, haze or fog at any hour. On the mapped ones: no dunes or other weathered ground, only barren ground, and
/// craters of every size.
/// </summary>
public partial class SystemsTest
{
    void Barren(List<StarSystem> systems)
    {
        int airless = 0, tidal = 0, notBarren = 0, wrongVariant = 0, deserts = 0, lit = 0, weather = 0;
        var variants = new int[Archetypes.Variants(Archetype.Barren)];
        foreach (StarSystem s in systems)
            foreach (Orbiter b in Landable(s))
            {
                if (b == s.Home || !b.Spec.Airless) continue;
                airless++;
                if (b.Spec.Archetype == Archetype.Arid) deserts++;
                if (b.Spec.Archetype == Archetype.Barren)
                {
                    variants[b.Spec.Variant]++;
                    if (b.Spec.Variant != Archetypes.BarrenVariant(b.Zone >= 5, b.Spec.HighGravity)) wrongVariant++;
                }
                else if (StarSystem.TidallyHeated(b) && b.Spec.Archetype is Archetype.Lava or Archetype.Ice) tidal++;
                else notBarren++;

                // Its sky as the game makes it, seen from it: the start world's system, with no air to scatter its star.
                SkySystem sky = SkySystem.Vacuum(SkySystem.For(s.Home.Spec));
                if (sky.Density >= SurfaceWeather.NoAir || b.Spec.Has(Modifier.PerpetualStorm)) weather++;
                if (Bright(sky.Zenith) > 0.01f || Bright(sky.Horizon) > 0.01f || sky.StarsByDay < 1f) lit++;
                foreach (float sun in new[] { 0.9f, 0.05f, -0.1f }) // noon, dawn (when damp air fogs) and night
                {
                    AtmosphereState a = Atmosphere.Compute(sky, Atmosphere.Clear(), sun);
                    if (a.CloudCover > 0f || a.Cirrus > 0f || a.Stratus > 0f || a.FogDensity > 0f || a.ValleyFog > 0f) weather++;
                }
            }
        Check(airless > 1000 && notBarren == 0 && deserts == 0 && wrongVariant == 0,
            $"barren: all {airless} airless bodies are barren ({string.Join(", ", variants.Select((n, v) => $"{n} {Archetypes.Get(Archetype.Barren, v).VariantName}"))}) or a giant's tidally heated moon ({tidal}); {deserts} deserts, {notBarren} other kinds, {wrongVariant} of the wrong variant for what they are");
        Check(variants.All(n => n > 0), "barren: every variant turns up: regolith, icy past the cold line, metal on a heavy world");
        Check(lit == 0 && weather == 0, $"barren: every one has the vacuum sky, black at noon with the stars out ({lit} lit), and no weather, storm, cloud, haze or fog at any hour ({weather} had some)");
    }

    /// <summary>The mapped airless bodies: no dunes or other weathered ground, barren ground only, craters at every size.</summary>
    void BarrenMaps(List<(StarSystem System, Orbiter Body, WorldMap Map)> coarse)
    {
        var weathered = new[] { Landform.DuneSea, Landform.Mesas, Landform.Canyonlands, Landform.Delta, Landform.Wetlands, Landform.LakeDistrict, Landform.RiverValleys, Landform.Fjords };
        int maps = 0, dunes = 0, ground = 0, cratered = 0;
        foreach (var (system, body, map) in coarse)
        {
            if (body == system.Home || body.Spec.Archetype != Archetype.Barren) continue;
            maps++;
            bool dune = false, other = false;
            for (int k = 0; k < map.Count; k++)
            {
                var biome = (Biome)map.Biome[k];
                dune |= weathered.Contains((Landform)map.Landform[k]) || biome is Biome.Dunes or Biome.Erg;
                other |= Minerals.Of(biome) == null;
            }
            if (dune) dunes++;
            if (other) ground++;
            // Craters from metres to kilometres: some of every size, a few hundred metres and up seen in the map itself.
            bool sizes = new[] { (3f, 10f), (10f, 40f), (40f, 150f), (150f, 2000f) }.All(band => map.Model.CratersOver(band.Item1).Any(c => c.Radius < band.Item2));
            // The big ones the map's cells can show (three across their radius), their floor (half way out: a big one has a peak in
            // its middle) well under their rim.
            var big = map.Model.CratersOver(MathF.Max(120f, map.CellSize * 3f)).Where(c => c.Fresh > 0.6f).OrderByDescending(c => c.Radius).Take(8).ToList();
            float Ring((double X, double Y, float Radius, float Fresh) c, float t) => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Average(d => map.Sample(map.Elevation, c.X + d.Item1 * c.Radius * t, c.Y + d.Item2 * c.Radius * t));
            int bowls = big.Count(c => Ring(c, 1f) - Ring(c, 0.5f) > c.Radius * 0.08f * map.Def.CraterRelief);
            if (sizes && big.Count > 0 && bowls * 10 >= big.Count * 7) cratered++;
        }
        Check(maps > 20 && dunes == 0 && ground == 0, $"barren: none of {maps} mapped barren bodies has dunes or other weathered ground ({dunes} had), or any but barren ground ({ground} had)");
        Check(cratered == maps, $"barren: every one is cratered at every size from metres to kilometres, its big craters bowls in its map ({cratered} of {maps})");
    }
}
