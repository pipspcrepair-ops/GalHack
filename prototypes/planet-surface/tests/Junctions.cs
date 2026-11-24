using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PlanetSurface.Infra;

namespace PlanetSurface;

/// <summary>A track's end that runs into another route on open ground (tests): where it meets, which way it leads off, and which way that route runs.</summary>
public sealed class Junctions
{
    public InfraRoute Track, Road;
    public double X, Z, DirX, DirZ, RoadX, RoadZ;

    /// <summary>Every dirt track end within a few metres of a road, highway or other track's line, with ground under both, and no place near.</summary>
    public static List<Junctions> Find(World.PlanetWorld w)
    {
        var found = new List<Junctions>();
        foreach (InfraRoute t in w.Infra.Routes.Where(r => r.Kind == RouteKind.Track && r.Count > 12))
            foreach (bool start in new[] { true, false })
            {
                int i = start ? 0 : t.Count - 1, k = start ? 8 : t.Count - 9;
                if (t.Span[start ? 0 : t.Count - 2] != RoadSpan.Ground) continue;
                double x = t.X[i], z = t.Z[i];
                if (w.Pois.Any(q => Math.Sqrt(Math.Pow(Mathx.WrapDelta(q.Center.X - x, w.Width), 2) + Math.Pow(Mathx.WrapDelta(q.Center.Z - z, w.Height), 2)) < Math.Max(q.Stamp.ReachX, q.Stamp.ReachZ) + 30)) continue;
                foreach (InfraRoute r in w.Infra.Routes)
                {
                    if (r == t || r.Kind == RouteKind.Rail) continue;
                    for (int j = 1; j + 1 < r.Count; j++)
                    {
                        double dx = Mathx.WrapDelta(r.X[j] - x, w.Width), dz = Mathx.WrapDelta(r.Z[j] - z, w.Height);
                        if (dx * dx + dz * dz > 9 || r.Span[j] != RoadSpan.Ground || r.Span[j - 1] != RoadSpan.Ground) continue;
                        double ox = Mathx.WrapDelta(t.X[k] - x, w.Width), oz = Mathx.WrapDelta(t.Z[k] - z, w.Height), ol = Math.Max(1e-6, Math.Sqrt(ox * ox + oz * oz));
                        double rx = r.X[j + 1] - r.X[j - 1], rz = r.Z[j + 1] - r.Z[j - 1], rl = Math.Max(1e-6, Math.Sqrt(rx * rx + rz * rz));
                        found.Add(new Junctions { Track = t, Road = r, X = x, Z = z, DirX = ox / ol, DirZ = oz / ol, RoadX = rx / rl, RoadZ = rz / rl });
                        goto next;
                    }
                }
                next:;
            }
        return found;
    }
}
