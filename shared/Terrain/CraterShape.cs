using System;

namespace GalHack.Terrain;

/// <summary>
/// The one shape of a crater, a war's blast or a natural impact: a bowl sunk below the land round it, its
/// thrown rim, and the rim's fall back to that land. A strike's bowl stamp (<see cref="TerrainStamp"/>,
/// <see cref="StampShape.Bowl"/>) and the generator's impact craters (Planets.Gen.TerrainModel) both read it,
/// so the two never drift apart. Natural impacts add what a big one has and a shell hole doesn't: a flat
/// floor and a central peak (complex craters), and a long ejecta fall.
/// </summary>
public static class CraterShape
{
    /// <summary>
    /// The height a crater adds at <paramref name="t"/> = distance from its centre over its radius, metres
    /// against the land round it: <paramref name="depth"/> under it at the floor, <paramref name="rim"/> over
    /// it at the rim (t = 1), falling back to it by t = 1 + <paramref name="reach"/>. <paramref name="floor"/>
    /// is the share of the radius that stays flat; <paramref name="peak"/> the height of a central peak.
    /// </summary>
    public static float Profile(float t, float depth, float rim, float reach = 0.5f, float floor = 0f, float peak = 0f)
    {
        if (t >= 1f + reach) return 0f;
        // The rim falls away steepest at its crest and eases into the land, so no ring marks where it ends.
        if (t > 1f) return rim * Square(1f - (t - 1f) / reach);
        // A parabolic bowl from the floor up to the rim's crest (a flat floor first, on a big one).
        float u = floor > 0f ? MathF.Max(0f, (t - floor) / (1f - floor)) : t;
        float h = rim - (depth + rim) * (1f - u * u);
        // Rebound: a rounded peak in the middle of the floor.
        if (peak > 0f) h += peak * MathF.Exp(-t * t / (floor > 0f ? floor * floor * 0.35f : 0.02f));
        return h;
    }

    static float Square(float v) => v * v;
}
