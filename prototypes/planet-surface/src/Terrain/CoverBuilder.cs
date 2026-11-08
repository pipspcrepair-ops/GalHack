using System;
using System.Collections.Generic;
using GalHack.Plants;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace PlanetSurface.Terrain;

/// <summary>One 16 m square of ground cover: instance buffers by species, far-reaching instances first.</summary>
public sealed class CoverCell
{
    public int Cx, Cz;
    public int Version;

    /// <summary>Species index, and its instances (sixteen floats each, in metres from the cell's corner).</summary>
    public readonly List<(int Plant, float[] Buffer, int Count)> Plants = new();
}

/// <summary>
/// Lays out ground cover: grass, flowers, low scrub, reeds and the like, by biome, on workers, one
/// 16 m cell at a time. Every instance rolls how far from the player it still shows, most of them
/// close: so cover is thick round your feet and thins smoothly with distance, the far end sparse. The
/// streamer draws nothing beyond <see cref="FarReach"/>, and the shader shrinks each tuft away at
/// its own reach, so nothing pops. It keeps off roads, pads, dug ground, water and bare rock.
/// </summary>
public static class CoverBuilder
{
    public const int CellSize = 16;
    public const float NearReach = 30f, FarReach = 112f;
    const float Step = 0.62f;
    static readonly int PerSide = (int)(CellSize / Step);

    /// <summary>How far an instance with this roll shows, m. Keep in step with shaders/flora_cover.gdshader.</summary>
    public static float ReachOf(float roll)
    {
        float k = 1f - roll;
        return NearReach + (FarReach - NearReach) * k * k * k * k;
    }

    readonly record struct Spot(int Plant, float Roll, Transform3D Place, Rgb Tint);

    public static CoverCell Build(int cx, int cz, PlanetTerrainSource source, GroundPalette pal, IReadOnlySet<long> dug)
    {
        var cell = new CoverCell { Cx = cx, Cz = cz };
        PlanetFlora flora = pal.Flora;
        if (flora == null || flora.Book.Barren) return cell;
        double w = pal.Map.WidthMeters, h = pal.Map.HeightMeters;
        double x0 = cx * (double)CellSize, z0 = cz * (double)CellSize;
        float region = flora.RegionAt(x0 + CellSize * 0.5, z0 + CellSize * 0.5);
        var rng = new Rng(Hash.Of(pal.Map.Spec.Seed, cx, cz, 0xC07E));
        var spots = new List<Spot>(PerSide * PerSide / 2);
        for (int j = 0; j < PerSide; j++)
        for (int i = 0; i < PerSide; i++)
        {
            float lx = (i + rng.NextFloat()) * Step, lz = (j + rng.NextFloat()) * Step;
            float pick = rng.NextFloat(), kin = rng.NextFloat(), size = rng.NextFloat(), turn = rng.NextFloat(), roll = rng.NextFloat(), lean = rng.NextFloat();
            double x = x0 + lx, z = z0 + lz;
            Biome biome = pal.BiomeAt(x, z);
            CoverMix mix = PlanetFlora.CoverOf(biome);
            float field = flora.CoverField(x, z);
            // Grass lies in drifts; flowers gather in the drifts' thick parts; the rest thinly everywhere.
            float grass = mix.Grass * Math.Clamp(0.55f + field * 1.1f, 0f, 1.2f), forb = mix.Forb * Math.Clamp((field - 0.35f) * 3.2f, 0f, 1.6f);
            float at = pick / flora.CoverDensity;
            PlantFamily family;
            if ((at -= forb) < 0f) family = PlantFamily.Forb;
            else if ((at -= mix.Reed * Math.Clamp((0.7f - field) * 2.4f, 0.2f, 1f)) < 0f) family = PlantFamily.Reed;
            else if ((at -= mix.Pins) < 0f) family = PlantFamily.Pins;
            else if ((at -= mix.Moss) < 0f) family = PlantFamily.Moss;
            else if ((at -= mix.Twig) < 0f) family = PlantFamily.Twig;
            else if ((at -= grass) < 0f) family = PlantFamily.Grass;
            else continue;
            Plant plant = flora.Book.Pick((int)biome, family, region, flora.GroveRoll(x, z, kin));
            if (plant == null) continue;

            GroundSample s = source.Ground(x, z, 1);
            float ground = s.Height;
            bool reed = family == PlantFamily.Reed;
            if (reed ? s.InRiver || ground < 0.1f || (ground > 1.6f && biome is not (Biome.Swamp or Biome.SporeMarsh or Biome.HotSpring or Biome.Oasis or Biome.Riverbank)) : s.InRiver || ground < 0.6f) continue;
            if (pal.DeadZones.Count > 0 && roll < pal.Deadness(x, z)) continue;
            if (source.StampWeight(x, z) > 0f && !(size < 0.4f && source.OnRoad(x, z, out float wear) && wear > 0.6f) && !source.OnWild(x, z, out _)) continue;
            int gx = (int)Math.Floor(x), gz = (int)Math.Floor(z);
            if (FarBuilder.Dug(dug, gx, gz)) continue;
            float hx = (source.SurfaceHeight(Mathx.WrapX(x + 0.7, w), z) - ground) / 0.7f, hz = (source.SurfaceHeight(x, Mathx.Wrap(z + 0.7, h)) - ground) / 0.7f;
            float slope2 = hx * hx + hz * hz;
            if (slope2 > 0.8f || source.SoilCover(x, z, slope2) < 0.45f) continue;
            if (source.CellDensity(gx, (int)Math.Floor(ground - 0.6f), gz) <= 0f) continue; // a cave mouth: nothing to root in

            // Roots under the low side of the slope, leaning with the land by half, so no tuft stands proud of it.
            float scale = Mathx.Lerp(0.72f, 1.28f, size);
            var basis = new Basis(Vector3.Up, turn * Mathf.Tau).Scaled(new Vector3(scale, Mathx.Lerp(0.8f, 1.25f, lean) * scale, scale));
            if (slope2 > 0.0025f)
            {
                float slope = MathF.Sqrt(slope2);
                basis = new Basis(new Vector3(-hz, 0f, hx) / slope, -MathF.Atan(slope) * 0.5f) * basis;
            }
            float y = ground - MathF.Sqrt(slope2) * 0.32f * scale;
            Rgb tint = family is PlantFamily.Pins ? flora.Tint(plant, size, x, z) : Rgb.Lerp(flora.Tint(plant, size, x, z), pal.SoilAt(x, z, out _), family == PlantFamily.Grass ? 0.34f : 0.25f);
            spots.Add(new Spot(plant.Index, roll, new Transform3D(basis, new Vector3(lx, y, lz)), tint));
        }

        // Far-reaching first (lowest roll), per species: the streamer takes as many of each as a cell's distance allows.
        spots.Sort(static (a, b) => a.Plant != b.Plant ? a.Plant.CompareTo(b.Plant) : a.Roll.CompareTo(b.Roll));
        for (int a = 0; a < spots.Count;)
        {
            int b = a;
            while (b < spots.Count && spots[b].Plant == spots[a].Plant) b++;
            var buffer = new float[(b - a) * 16];
            for (int k = a; k < b; k++)
            {
                Spot t = spots[k];
                int o = (k - a) * 16;
                Basis m = t.Place.Basis;
                buffer[o] = m.X.X; buffer[o + 1] = m.Y.X; buffer[o + 2] = m.Z.X; buffer[o + 3] = t.Place.Origin.X;
                buffer[o + 4] = m.X.Y; buffer[o + 5] = m.Y.Y; buffer[o + 6] = m.Z.Y; buffer[o + 7] = t.Place.Origin.Y;
                buffer[o + 8] = m.X.Z; buffer[o + 9] = m.Y.Z; buffer[o + 10] = m.Z.Z; buffer[o + 11] = t.Place.Origin.Z;
                buffer[o + 12] = t.Tint.R; buffer[o + 13] = t.Tint.G; buffer[o + 14] = t.Tint.B; buffer[o + 15] = t.Roll;
            }
            cell.Plants.Add((spots[a].Plant, buffer, b - a));
            a = b;
        }
        return cell;
    }
}
