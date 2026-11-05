using System;
using System.Collections.Generic;
using System.Linq;
using Slice.Orbit;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The sphere-to-map mapping on its own, with no engine: round trips, descents at eight points
/// (both poles and the east-west seam among them), and continuity over the seam and the poles.
/// Each check is (passed, what it says).
/// </summary>
public static class GlobeChecks
{
    /// <summary>A surface-frame orientation: heading (degrees clockwise from north), pitch up and roll right, degrees.</summary>
    public static N.Quaternion Attitude(double heading, double pitch = 0, double roll = 0)
    {
        float h = (float)(-heading * Math.PI / 180), p = (float)(pitch * Math.PI / 180), r = (float)(-roll * Math.PI / 180);
        return N.Quaternion.Normalize(N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, h)
            * N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, p) * N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, r));
    }

    static double AngleBetween(N.Quaternion a, N.Quaternion b)
    {
        double d = Math.Abs(N.Quaternion.Dot(N.Quaternion.Normalize(a), N.Quaternion.Normalize(b)));
        return 2 * Math.Acos(Math.Min(1, d)) * 180 / Math.PI;
    }

    static double HeadingError(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    public static IEnumerable<(bool ok, string text)> Run(double width, double height)
    {
        var globe = new GlobeMap(width, height, width / (2 * Math.PI));
        var rng = new Random(7);

        // 1. Orbit -> surface -> orbit, anywhere: the same place and the same attitude.
        double worstMetres = 0, worstDegrees = 0;
        for (int i = 0; i < 400; i++)
        {
            var dir = N.Vector3.Normalize(new N.Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)));
            var pos = dir * (float)(globe.Radius + 300 + rng.NextDouble() * 600);
            var rot = N.Quaternion.Normalize(new N.Quaternion((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f));
            var (place, local) = globe.ToSurface(pos, rot);
            var (back, backRot) = globe.ToOrbit(place, local);
            worstMetres = Math.Max(worstMetres, N.Vector3.Distance(back, pos));
            worstDegrees = Math.Max(worstDegrees, AngleBetween(backRot, rot));
        }
        yield return (worstMetres < 3 && worstDegrees < 1,
            $"globe: orbit to surface and back at 400 random points lands within {worstMetres:F2} m and {worstDegrees:F3}° of where it left");

        // 2. Surface -> orbit -> surface: the same map point, the same heading.
        worstMetres = 0;
        worstDegrees = 0;
        for (int i = 0; i < 400; i++)
        {
            var place = new MapPlace(rng.NextDouble() * width, 1 + rng.NextDouble() * (height - 2), 2000);
            double heading = rng.NextDouble() * 360;
            var (pos, rot) = globe.ToOrbit(place, Attitude(heading, rng.NextDouble() * 40 - 20, rng.NextDouble() * 40 - 20));
            var (back, local) = globe.ToSurface(pos, rot);
            double dx = Math.Abs(back.X - place.X);
            dx = Math.Min(dx, width - dx);
            worstMetres = Math.Max(worstMetres, Math.Sqrt(dx * dx + (back.Z - place.Z) * (back.Z - place.Z)));
            worstDegrees = Math.Max(worstDegrees, HeadingError(GlobeMap.HeadingDegrees(local), heading));
        }
        yield return (worstMetres < 3 && worstDegrees < 1,
            $"globe: surface to orbit and back at 400 random places lands within {worstMetres:F2} m on the map and {worstDegrees:F3}° of heading");

        // 3. Descents at eight points: straight down over a map point, with a heading, arrive over it with that heading.
        var points = new (string name, double x, double z, double heading)[]
        {
            ("the equator, nose east", width * 0.25, height * 0.5, 90),
            ("mid-north, nose north", width * 0.6, height * 0.25, 0),
            ("mid-south, nose south-west", width * 0.4, height * 0.75, 225),
            ("near the north pole (89.6°N), nose east", width * 0.1, height * 0.4 / 180, 90),
            ("near the south pole (89.6°S), nose west", width * 0.8, height * (1 - 0.4 / 180), 270),
            ("on the seam's east edge, nose east", width - 2, height * 0.45, 90),
            ("on the seam's west edge, nose east", 2, height * 0.45, 90),
            ("on the seam at 60°N, nose north-east", 0.5, height * 30 / 180, 45),
        };
        foreach (var (name, x, z, heading) in points)
        {
            var (pos, rot) = globe.ToOrbit(new MapPlace(x, z, 800), Attitude(heading, -10, 5));
            // The descent: straight down from orbit, the vessel's attitude unchanged on the way.
            var lower = pos * (float)((globe.Radius + 300) / pos.Length());
            var (place, local) = globe.ToSurface(lower, rot);
            double dx = Math.Abs(place.X - x);
            dx = Math.Min(dx, width - dx);
            double metres = Math.Sqrt(dx * dx + (place.Z - z) * (place.Z - z));
            double err = HeadingError(GlobeMap.HeadingDegrees(local), heading);
            yield return (metres < 1 && err < 1 && Math.Abs(place.Altitude - 300) < 0.5,
                $"globe: a descent {name} arrives {metres:F2} m from the point under it, heading {GlobeMap.HeadingDegrees(local):F1}° (wanted {heading:F0}°)");
        }

        // 4. Across the seam: two vessels a few metres apart either side of it are a few metres apart on the globe too.
        double across = globe.Distance(width - 3, height * 0.45, 3, height * 0.45);
        yield return (across < 6.5, $"globe: either side of the east-west seam, 6 m apart on the map, are {across:F2} m apart on the globe: no seam");

        // 5. Over a pole: a vessel flying north over the north pole keeps flying straight on the globe;
        //    on the map it comes out on the far side, half a world east, flying south.
        var start = globe.ToOrbit(new MapPlace(width * 0.3, 2, 500), Attitude(0));
        var nose = N.Vector3.Transform(-N.Vector3.UnitZ, start.rotation);
        var past = start.position + nose * 8f;
        var (over, overRot) = globe.ToSurface(past, start.rotation);
        double half = Math.Abs(over.X - width * 0.3);
        half = Math.Min(half, width - half);
        yield return (Math.Abs(half - width / 2) < 2 && HeadingError(GlobeMap.HeadingDegrees(overRot), 180) < 1,
            $"globe: flying north over the north pole comes out {half / width:F3} of a turn round, heading {GlobeMap.HeadingDegrees(overRot):F1}° (south), as on a real globe");
    }
}
