using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using N = System.Numerics;

namespace CapitalBase.World;

/// <summary>A place to set a ship down: its centre on the planet and the ground under its legs.</summary>
public readonly record struct Spot(float X, float Z, float Heading, float Tilt, float Roughness, float Ground)
{
    public N.Vector2 Flat => new(X, Z);
    public override string ToString() => $"({X:F0}, {Z:F0}) ground {Ground:F1} m, slope {Tilt * 57.2958f:F1}°, bumps {Roughness:F2} m";
}

/// <summary>
/// Finds landing spots on the real terrain near the landing site: gentle enough for a capital
/// ship's legs to settle on (a plane through the feet tilts less than the gear allows), above the
/// water, out of caves, but never perfectly flat: the ground under the legs is uneven. Spots are
/// apart from each other, and chosen the same way every time.
/// </summary>
public static class Sites
{
    /// <param name="feet">leg positions relative to the ship's centre (x, z), metres</param>
    public static List<Spot> Find(PlanetTerrainSource source, N.Vector2 centre, float radius, IReadOnlyList<N.Vector2> feet,
        float heading, int count, float apart, float maxTilt)
    {
        var candidates = new List<Spot>();
        float cos = MathF.Cos(heading), sin = MathF.Sin(heading);
        for (float dz = -radius; dz <= radius; dz += 6f)
        for (float dx = -radius; dx <= radius; dx += 6f)
        {
            float x = centre.X + dx, z = centre.Y + dz;
            var heights = new List<N.Vector3>();
            float lowest = float.MaxValue;
            foreach (N.Vector2 f in feet)
            {
                // Yaw by the heading: a ship facing heading h has its -z forward turned by h.
                float fx = x + f.X * cos + f.Y * sin, fz = z - f.X * sin + f.Y * cos;
                float h = source.SurfaceHeight(fx, fz);
                heights.Add(new N.Vector3(fx - x, h, fz - z));
                lowest = MathF.Min(lowest, h);
            }
            if (lowest < 2f) continue; // keep out of the water
            (float tilt, float rough, float mean) = Plane(heights);
            if (tilt > maxTilt) continue;
            // Ground under the middle of the hull, every 4 m: no cliff or cave mouth in between.
            bool sound = true;
            for (int i = -2; i <= 2 && sound; i++)
            for (int j = -4; j <= 4 && sound; j++)
            {
                float px = x + i * 4f, pz = z + j * 4f;
                float h = source.SurfaceHeight(px, pz);
                if (MathF.Abs(h - mean) > 4f) sound = false;
                else if (source.CellDensity((int)MathF.Floor(px), (int)MathF.Floor(h - 1.5f), (int)MathF.Floor(pz)) <= 0f) sound = false;
            }
            if (!sound) continue;
            candidates.Add(new Spot(x, z, heading, tilt, rough, mean));
        }
        // Gentle but uneven: the best are tilted a little and bumpy under the feet, never dead flat.
        candidates.Sort((a, b) => Score(a).CompareTo(Score(b)) is int c && c != 0 ? c : (a.X, a.Z).CompareTo((b.X, b.Z)));
        var chosen = new List<Spot>();
        foreach (Spot s in candidates)
        {
            bool far = true;
            foreach (Spot o in chosen) far &= N.Vector2.Distance(s.Flat, o.Flat) >= apart;
            if (!far) continue;
            chosen.Add(s);
            if (chosen.Count == count) break;
        }
        return chosen;
    }

    /// <summary>
    /// Where to set up near the landing site (which is always by water): the nearest stretch of
    /// dry, gentle ground about <paramref name="size"/> metres across that holds at least
    /// <paramref name="count"/> landing spots. Tried nearest first on a coarse grid, so it is quick
    /// and the same every time.
    /// </summary>
    public static (N.Vector2 centre, List<Spot> spots) FindArea(PlanetTerrainSource source, N.Vector2 near, float size,
        IReadOnlyList<N.Vector2> feet, int count, float apart, float maxTilt)
    {
        var areas = new List<(float score, N.Vector2 at)>();
        for (float dz = -1280f; dz <= 1280f; dz += 64f)
        for (float dx = -1280f; dx <= 1280f; dx += 64f)
        {
            var at = new N.Vector2(near.X + dx, near.Y + dz);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int j = -4; j <= 4; j++)
            for (int i = -4; i <= 4; i++)
            {
                float h = source.SurfaceHeight(at.X + i * size / 8f, at.Y + j * size / 8f);
                lo = MathF.Min(lo, h);
                hi = MathF.Max(hi, h);
            }
            if (lo < 3f) continue;
            areas.Add(((hi - lo) + N.Vector2.Distance(at, near) * 0.01f, at));
        }
        areas.Sort((a, b) => a.score.CompareTo(b.score) is int c && c != 0 ? c : (a.at.X, a.at.Y).CompareTo((b.at.X, b.at.Y)));
        foreach (var (_, at) in areas.Take(12))
        {
            List<Spot> spots = Find(source, at, size * 0.32f, feet, 0f, count, apart, maxTilt);
            if (spots.Count >= count) return (at, spots);
        }
        return (near, Find(source, near, size * 0.32f, feet, 0f, count, apart, maxTilt));
    }

    static float Score(Spot s) => MathF.Abs(s.Tilt - 0.05f) * 4f + MathF.Abs(s.Roughness - 0.35f);

    /// <summary>A least-squares plane through the points: its tilt, the worst point's distance from it, and the mean height.</summary>
    static (float tilt, float rough, float mean) Plane(List<N.Vector3> p)
    {
        double sx = 0, sz = 0, sy = 0, sxx = 0, sxz = 0, szz = 0, sxy = 0, szy = 0;
        foreach (N.Vector3 v in p)
        {
            sx += v.X; sz += v.Z; sy += v.Y;
            sxx += v.X * v.X; sxz += v.X * v.Z; szz += v.Z * v.Z; sxy += v.X * v.Y; szy += v.Z * v.Y;
        }
        int n = p.Count;
        double mx = sx / n, mz = sz / n, my = sy / n;
        double cxx = sxx / n - mx * mx, cxz = sxz / n - mx * mz, czz = szz / n - mz * mz;
        double cxy = sxy / n - mx * my, czy = szy / n - mz * my;
        double det = cxx * czz - cxz * cxz;
        double a = Math.Abs(det) < 1e-9 ? 0 : (cxy * czz - czy * cxz) / det, b = Math.Abs(det) < 1e-9 ? 0 : (czy * cxx - cxy * cxz) / det;
        float rough = 0f;
        foreach (N.Vector3 v in p)
            rough = MathF.Max(rough, MathF.Abs((float)(v.Y - (my + a * (v.X - mx) + b * (v.Z - mz)))));
        float tilt = MathF.Atan(MathF.Sqrt((float)(a * a + b * b)));
        return (tilt, rough, (float)my);
    }
}
