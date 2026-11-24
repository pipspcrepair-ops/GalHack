using System;

namespace GalHack.Terrain;

/// <summary>
/// The one shape of a crater, a war's blast or a natural impact: a bowl sunk below the land round it, its
/// thrown rim, and the rim's fall back to that land. A strike's bowl stamp (<see cref="TerrainStamp"/>,
/// <see cref="StampShape.Bowl"/>) and the generator's impact craters (Planets.Gen.TerrainModel) both read it,
/// so the two never drift apart. Natural impacts add what a big one has and a shell hole doesn't: a flat
/// floor, a central peak and slumped terraces down the walls (complex craters), a long ejecta fall, and a
/// crest worn round with age.
/// </summary>
public static class CraterShape
{
    /// <summary>
    /// The height a crater adds at <paramref name="t"/> = distance from its centre over its radius, metres
    /// against the land round it: <paramref name="depth"/> under it at the floor, <paramref name="rim"/> over
    /// it at the rim (t = 1), falling back to it by t = 1 + <paramref name="reach"/>. <paramref name="floor"/>
    /// is the share of the radius that stays flat; <paramref name="peak"/> the height of a central peak. <paramref name="ease"/>:
    /// the rim falls away steepest at its crest and eases into the land (a natural impact's long ejecta fall, where a
    /// ring would show where it ends), else in a straight run (a shell hole's thrown rim, inside its stamp's own blend).
    /// <paramref name="terraces"/>: how far the wall has slumped into steps (0 a smooth bowl, 1 three clear treads);
    /// <paramref name="soft"/>: how much height, metres, the crest is worn round over (0 a sharp crest).
    /// </summary>
    public static float Profile(float t, float depth, float rim, float reach = 0.5f, float floor = 0f, float peak = 0f, bool ease = false, float terraces = 0f, float soft = 0f)
    {
        if (t >= 1f + reach) return 0f;
        // The fall outside the crest, carried on inside it too, so a worn crest can round the bowl and the fall together.
        float fall = 1f - (t - 1f) / reach;
        float outer = ease ? rim * fall * fall : rim * fall;
        if (t > 1f && soft <= 0f) return outer;
        // A parabolic bowl from the floor up to the rim's crest (a flat floor first, on a big one).
        float u = floor > 0f ? MathF.Max(0f, (t - floor) / (1f - floor)) : t;
        // Slumped terraces: treads that lean back gently, risers between, the lowest running into the floor.
        if (terraces > 0f && u > 0f && u < 1f)
        {
            float s = u * Treads, step = MathF.Floor(s), f = s - step;
            float rise = f < 1f - Riser ? 0f : Smooth((f - (1f - Riser)) / Riser);
            float stepped = (step + rise + Lean * MathF.Min(f, 1f - Riser) * (1f - rise)) / Treads;
            u += (stepped - u) * MathF.Min(terraces, 1f);
        }
        float h = rim - (depth + rim) * (1f - u * u);
        // Rebound: a rounded peak in the middle of the floor.
        if (peak > 0f) h += peak * MathF.Exp(-t * t / (floor > 0f ? floor * floor * 0.35f : 0.02f));
        // The crest: the lower of the bowl and the fall, worn round by a smooth minimum.
        float apart = MathF.Abs(h - outer);
        return soft > 0f && apart < soft ? MathF.Min(h, outer) - Square(soft - apart) / (4f * soft) : MathF.Min(h, outer);
    }

    /// <summary>How many treads a fully slumped wall has, the share of each step that is riser, and how far a tread leans back.</summary>
    const float Treads = 3f, Riser = 0.45f, Lean = 0.25f;

    static float Square(float v) => v * v;

    static float Smooth(float v) => v * v * (3f - 2f * v);
}
