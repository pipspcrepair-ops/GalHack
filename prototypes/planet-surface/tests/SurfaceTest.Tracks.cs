using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GalHack.Terrain;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

public partial class SurfaceTest
{
    /// <summary>
    /// Tracks and markings as the ground paints them, on fresh worlds (the shader's junction rule worked the same way, RoadField.JoinedEdge):
    /// a track that leaves a road is its own width along its length, with no wedge or flare of dirt at the junction; and paint (the
    /// lines, a stop bar) shows only where the road surface is, never on grown-over ground or out at its edge.
    /// Run with --only=Tracks; the same worked with the old rule (every pair of roads rounded their corners, paint under the grass at 8 %) fails both.
    /// </summary>
    async Task TrackChecks()
    {
        var rand = new Random();
        int worlds = 0, junctions = 0, samples = 0, flared = 0, oldFlared = 0, painted = 0, outside = 0, oldOutside = 0;
        float worstRatio = 0f, oldWorst = 0f;
        string first = "";
        for (int k = 0; k < 40 && (junctions < 25 || k < 6); k++)
        {
            PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll((ulong)rand.NextInt64(), Archetype.Temperate, 0), Main.Catalog);
            worlds++;
            // Tracks: across each, 4 to 30 m from the road's edge, the painted width against the track's own.
            foreach (Junctions j in Junctions.Find(w))
            {
                junctions++;
                float hwT = j.Track.HalfWidth, hwR = j.Road.HalfWidth;
                bool hard = j.Road.Surface is StampSurface.Concrete or StampSurface.Tarmac;
                float crossing = 1f - (float)Math.Abs(j.DirX * j.RoadX + j.DirZ * j.RoadZ);
                if (crossing < 0.3f) continue;   // running along the road, not leaving it
                float kerb = RoadField.Kerb(hard, hwR, hwT, crossing);
                float oldKerb = Math.Max((hwR > 5f ? 15f : 12f) * 1.7f * RoadField.Smooth(0.1f, 0.4f, crossing), 0.5f);
                float cos = 1f - crossing, reachIn = hwR / Math.Max(0.3f, MathF.Sqrt(1f - cos * cos)) + 6f;
                for (float s = reachIn; s <= reachIn + 26f; s += 2f)
                {
                    double cx = j.X + j.DirX * s, cz = j.Z + j.DirZ * s;
                    samples++;
                    float ratio = Width(w, j, cx, cz, kerb) / (2f * hwT), oldRatio = Width(w, j, cx, cz, oldKerb) / (2f * hwT);
                    worstRatio = Math.Max(worstRatio, ratio);
                    oldWorst = Math.Max(oldWorst, oldRatio);
                    if (ratio > 1.5f) { flared++; if (first.Length == 0) first = $" (first: at {cx:F0},{cz:F0}, {ratio:F1} times its width)"; }
                    if (oldRatio > 1.5f) oldFlared++;
                }
            }
            // Markings: along paved roads, each line's position, against whether the surface is there (not edge, not grown over).
            foreach (InfraRoute r in w.Infra.Routes.Where(r => r.Kind is RouteKind.Road or RouteKind.Highway && r.Surface is StampSurface.Concrete or StampSurface.Tarmac && r.HalfWidth >= 3.5f))
                for (int i = 2; i + 2 < r.Count; i += 6)
                {
                    if (r.Span[i] != RoadSpan.Ground || r.Span[i - 1] != RoadSpan.Ground) continue;
                    double dx = r.X[i + 1] - r.X[i - 1], dz = r.Z[i + 1] - r.Z[i - 1], len = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
                    float hw = r.HalfWidth, creep = hw * (0.06f + 0.3f * r.Wear * r.Wear);
                    foreach (float x in r.Kind == RouteKind.Highway ? new[] { 0.18f, hw * 0.5f, hw - 0.6f } : hw >= 4f ? new[] { 0.07f, hw - 0.55f } : new[] { 0.07f })
                    {
                        // The surface's distance in from its edge at that line (its own width; a junction only widens it).
                        float inward = hw - x;
                        // Grown over: the land has taken this much of the road back from each edge, and the paint is under it.
                        float over = 1f - RoadField.Smooth(creep - 0.25f, creep + 0.05f, inward);
                        float now = (1f - over) * RoadField.Smooth(0.15f, 0.6f, inward), before = 1f - 0.92f * over;
                        painted++;
                        if (now > 1f - over + 0.05f || now > 0.05f && inward < 0.15f) outside++;
                        if (before > 1f - over + 0.05f || before > 0.05f && inward < 0.15f) oldOutside++;
                    }
                }
            await Frames(1);
        }
        Check(junctions > 0 && flared == 0, $"a track leaves a road as its own strip: {junctions} junctions on {worlds} fresh worlds, {samples} cross-sections, painted width at most {worstRatio:F2} times its own (1.5 allowed; the old rule {oldWorst:F2}, {oldFlared} over), {flared} over{first}");
        Check(painted > 0 && outside == 0, $"road paint only on the road's surface: {painted} marking points, {outside} showing more than the road's share of the ground, or at its edge (the old rule {oldOutside})");
    }

    /// <summary>How far a point lies from a route's line (the nearest of its segments).</summary>
    static float Dist(InfraRoute r, double x, double z)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 1 < r.Count; i++)
        {
            double ax = r.X[i], az = r.Z[i], ex = r.X[i + 1] - ax, ez = r.Z[i + 1] - az, rx = x - ax, rz = z - az;
            if (Math.Abs(rx) > 400 || Math.Abs(rz) > 400) continue;
            double u = Math.Clamp((rx * ex + rz * ez) / Math.Max(ex * ex + ez * ez, 1e-9), 0, 1);
            best = Math.Min(best, (float)Math.Sqrt((rx - ex * u) * (rx - ex * u) + (rz - ez * u) * (rz - ez * u)));
        }
        return best;
    }

    /// <summary>The painted width across a track at a point on its line: the run of ground, level with the middle, that the joined decks cover.</summary>
    static float Width(PlanetWorld w, Junctions j, double cx, double cz, float kerb)
    {
        double nx = -j.DirZ, nz = j.DirX;
        float Covered(float o)
        {
            double x = cx + nx * o, z = cz + nz * o;
            float edge = Dist(j.Track, x, z) - j.Track.HalfWidth, other = Dist(j.Road, x, z) - j.Road.HalfWidth;
            return RoadField.JoinedEdge(edge, other, kerb);
        }
        float lo = 0f, hi = 0f;
        for (float o = 0.1f; o < 16f && Covered(o) < 0f; o += 0.1f) hi = o;
        for (float o = -0.1f; o > -16f && Covered(o) < 0f; o -= 0.1f) lo = o;
        return hi - lo;
    }
}
