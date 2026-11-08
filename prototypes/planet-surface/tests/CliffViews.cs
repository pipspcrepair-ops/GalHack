using System;
using System.Collections.Generic;
using Godot;
using Planets.Gen;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

/// <summary>
/// cliff:KIND[:SEED] frames one rock face of a kind on a fresh world (a seed only to replay one a run
/// printed): mesa (a desert mesa or escarpment), canyon (a canyon wall), sea (a sea cliff), crag (a
/// mountain crag). The face is found from the ground itself: the steepest drop of tens of metres in
/// that landform near the landing site (sea cliffs anywhere on the coast). One run takes a series:
/// from 80 m and 20 m out at a person's height, from 120 m off at a slant along it, and from the air,
/// at a time of day that lights the face.
/// </summary>
public static class CliffViews
{
    /// <summary>The face found for the run: its top, its foot, which way it faces and how tall it is.</summary>
    sealed record Face(Vector3 Top, Vector3 Foot, Vector2 Out, string Where);

    static Face _face;

    public static Screenshot.ViewSpec Get(string name)
    {
        string[] a = name.Split(':');
        string kind = a.Length > 1 ? a[1] : "mesa";
        ulong seed = a.Length > 2 ? ulong.Parse(a[2]) : (ulong)Random.Shared.NextInt64(1, int.MaxValue);
        PlanetSpec spec = kind switch
        {
            "mesa" => With(PlanetSpec.Roll(seed, Archetype.Arid, (int)(seed % 2)), Modifier.Mesas),
            "canyon" => With(PlanetSpec.Roll(seed, Archetype.Arid, 1), Modifier.Canyons),
            "crag" => With(PlanetSpec.Roll(seed, seed % 3 == 0 ? Archetype.Ice : Archetype.Temperate, 0), Modifier.Highlands),
            "sea" => SeaWorld(seed),
            _ => null,
        };
        if (spec == null) return null;
        GD.Print($"cliff view: {kind} on {spec}");
        Landform want = kind switch
        {
            "mesa" => Landform.Mesas,
            "canyon" => Landform.Canyonlands,
            "crag" => Landform.Mountains,
            _ => Landform.CoastalCliffs,
        };
        return new Screenshot.ViewSpec(spec, m => (Vector3.Zero, 0f, 0f), 0.4, WeatherKind.Clear,
            TimeFor: Lit, Series: m => Shots(m, want, kind == "sea"));
    }

    static PlanetSpec With(PlanetSpec s, Modifier m) => s with { Modifiers = (s.Modifiers | m) & ~Modifier.Archipelago };

    /// <summary>A world whose landforms include sea cliffs: the first seed on from this one that rolls them.</summary>
    static PlanetSpec SeaWorld(ulong seed)
    {
        for (ulong s = seed; ; s++)
        {
            var spec = PlanetSpec.Roll(s, s % 2 == 0 ? Archetype.Temperate : Archetype.Arid, (int)(s / 2 % 2));
            spec = spec with { Modifiers = spec.Modifiers & ~Modifier.Archipelago };
            if (LandformMix.Roll(spec, Archetypes.Get(spec.Archetype, spec.Variant)).Has(Landform.CoastalCliffs)) return spec;
        }
    }

    /// <summary>The time of day whose sun, at least 20 degrees up, falls most squarely on the face.</summary>
    static double Lit(Main m)
    {
        if (_face == null) return 0.4;
        double best = 0.4, score = double.MinValue;
        var n = new Vector3(_face.Out.X, 0.25f, _face.Out.Y).Normalized();
        for (double t = 0.27; t <= 0.73; t += 0.01)
        {
            Vector3 sun = m.Sky.SunAt(t);
            if (sun.Y < 0.34f) continue;
            float s = sun.Dot(n);
            if (s > score) { score = s; best = t; }
        }
        return best;
    }

    static List<Screenshot.Shot> Shots(Main m, Landform want, bool sea)
    {
        _face = Find(m, want, sea);
        if (_face == null)
        {
            GD.Print($"cliff view: no {want} face on this world");
            return new List<Screenshot.Shot>();
        }
        var w = m.World;
        Face f = _face;
        float height = f.Top.Y - f.Foot.Y;
        GD.Print($"cliff view: {f.Where}; {height:F0} m face from {f.Top.X:F0},{f.Top.Z:F0} (top {f.Top.Y:F0}) to its foot {f.Foot.Y:F0} m, facing {Mathf.RadToDeg(Mathf.Atan2(f.Out.X, -f.Out.Y)):F0} degrees");
        var o = new Vector3(f.Out.X, 0f, f.Out.Y);
        var side = new Vector3(-o.Z, 0f, o.X);
        Vector3 mid = (f.Top + f.Foot) * 0.5f;
        Vector3 Stand(Vector3 p, float eye)
        {
            float g = w.GroundHeight(p.X, p.Z);
            if (sea || g < 0f) g = Mathf.Max(g, 0f);
            return new Vector3(p.X, g + eye, p.Z);
        }
        Screenshot.Shot At(string name, Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return new Screenshot.Shot(name, from, Mathf.RadToDeg(Mathf.Atan2(d.X, -d.Z)), Mathf.RadToDeg(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length())));
        }
        // Out from the foot along the way it faces, at a person's height; looking at the face's middle.
        Vector3 foot = new(f.Foot.X, 0f, f.Foot.Z);
        return new List<Screenshot.Shot>
        {
            At("80m", Stand(foot + o * 80f, 1.8f), mid),
            At("20m", Stand(foot + o * 20f, 1.8f), mid + Vector3.Down * (height * 0.15f)),
            At("along", Stand(foot + o * 75f + side * 95f, 2.5f), mid),
            At("air", new Vector3(f.Top.X, f.Top.Y, f.Top.Z) + o * 190f + side * 60f + Vector3.Up * 120f, mid),
        };
    }

    /// <summary>
    /// The face: on the coarse map, cells of the landform that are steep (and for sea cliffs, beside the
    /// sea); among them, on the fine ground, the point whose drop within 6 m is the greatest, with at
    /// least 18 m of fall below it within 40 m. Nearest the landing site first, except sea cliffs.
    /// </summary>
    static Face Find(Main m, Landform want, bool sea)
    {
        var w = m.World;
        WorldMap map = w.Map;
        var cells = new List<(int k, float score)>();
        double lx = w.Landing.X, lz = w.Landing.Y;
        for (int j = 0; j < map.Height; j++)
        for (int i = 0; i < map.Width; i++)
        {
            int k = j * map.Width + i;
            if (map.Landform[k] != (byte)want || map.Elevation[k] < (sea ? 6f : 4f)) continue;
            double x = map.CellX(i), z = map.CellY(j);
            double dx = Mathx.WrapDelta(x - lx, w.Width), dz = Mathx.WrapDelta(z - lz, w.Height);
            double away = Math.Sqrt(dx * dx + dz * dz);
            if (!sea && away > 3500) continue;
            if (sea)
            {
                // Beside the sea: water within two cells.
                bool wet = false;
                for (int dj = -2; dj <= 2 && !wet; dj++)
                for (int di = -2; di <= 2 && !wet; di++)
                    wet = map.Elevation[map.Index(i + di, j + dj)] < -2f;
                if (!wet) continue;
            }
            float score = map.Slope[k] - (float)(away / 4000.0) * (sea ? 0f : 0.3f);
            cells.Add((k, score));
        }
        cells.Sort((p, q) => q.score.CompareTo(p.score));
        Face best = null;
        float bestDrop = 0f;
        int looked = 0;
        foreach (var (k, _) in cells)
        {
            if (++looked > 160) break;
            int ci = k % map.Width, cj = k / map.Width;
            double x0 = map.CellX(ci) - map.CellSize * 0.5, z0 = map.CellY(cj) - map.CellSize * 0.5;
            for (double z = z0; z < z0 + map.CellSize; z += 4)
            for (double x = x0; x < x0 + map.CellSize; x += 4)
            {
                float h = w.Source.SurfaceHeight(x, z, 2);
                for (int d = 0; d < 8; d++)
                {
                    float ang = d * Mathf.Pi / 4f;
                    var dir = new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang));
                    float drop = h - w.Source.SurfaceHeight(x + dir.X * 6, z + dir.Y * 6, 2);
                    if (drop <= bestDrop) continue;
                    float below = w.Source.SurfaceHeight(x + dir.X * 40, z + dir.Y * 40, 2);
                    if (h - below < 18f) continue;
                    if (sea && below > 4f) continue;
                    // The foot: on out until the ground stops falling.
                    double fx = x, fz = z;
                    float fy = h;
                    for (int s = 1; s <= 60; s++)
                    {
                        double nx = x + dir.X * s, nz = z + dir.Y * s;
                        float ny = w.Source.SurfaceHeight(nx, nz, 1);
                        if (fy - ny < 0.35f && s > 3) break;
                        (fx, fz, fy) = (nx, nz, ny);
                    }
                    bestDrop = drop;
                    LandformBlend blend = map.LandformsAt(x, z);
                    best = new Face(new Vector3((float)x, h, (float)z), new Vector3((float)fx, fy, (float)fz), dir, $"{blend} at {x:F0},{z:F0}");
                }
            }
        }
        return best;
    }
}
