using System;
using Planets.Look;
using Weather.Model;

namespace PlanetSurface.Skies;

/// <summary>What the air looks like right now: the values the sky, fog and lights are set from.</summary>
public readonly record struct AtmosphereState(
    float CloudCover, float CloudDarkness, float CloudBase, float CloudThickness, float Overcast,
    float FogDensity, float FogSkyAffect, float ValleyFog, float ValleyFogDepth,
    float SunScale, float AmbientScale, float Saturation, float Visibility)
{
    /// <summary>
    /// The weather's own ground fog (0..1), on its own. <see cref="ValleyFog"/> is the larger of
    /// it and the morning fog damp air holds around sunrise; by evening that has burnt off.
    /// </summary>
    public float GroundFog { get; init; }

    /// <summary>Layered grey cloud instead of heaped cumulus, 0 to 1: steady damp weather, not storms.</summary>
    public float Stratus { get; init; }

    /// <summary>The thin high streaks above everything else, 0 to 1.</summary>
    public float Cirrus { get; init; }
}

/// <summary>
/// Turns the weather model's conditions, the world's air and the time of day into fog, cloud and
/// light values. Pure, so each weather state's look is testable headless:
/// - <b>Clouds</b>: cover and darkness come from the weather; heavier weather lowers and thickens
///   the deck; overcast greys the whole sky.
/// - <b>Fog</b>: depth fog from the weather's visibility (about 90 % fogged at that distance) over
///   the air's own haze (thick air hazes the distance more).
/// - <b>Valley fog</b>: banks that pool in low ground. The weather's ground fog sets them, and on
///   damp worlds they also form around dawn and burn off as the sun climbs; dry and thin air
///   rarely holds them.
/// - <b>Light</b>: the weather's solar output dims the sun and lifts the ambient light (an
///   overcast day is flat and soft, not dark).
/// </summary>
public static class Atmosphere
{
    public static AtmosphereState Compute(SkySystem sky, in Conditions c, float sunHeight)
    {
        // Where the weather is cells, the sky between them opens: the dark is theirs, not the whole deck's.
        float cellular = Math.Clamp(c.Cellular, 0f, 1f);
        float cover = Math.Clamp(c.CloudCover, 0f, 1f) * (1f - 0.6f * cellular);
        // The cloud shade follows the weather's severity, the same values that set the rain: a drizzle
        // greys the cloud a little, heavy rain darkens it, a thunderstorm is near slate. (Its towers are darker still.)
        float dark = Math.Clamp(Math.Clamp(c.CloudDarkness, 0f, 1f) * (1f - 0.2f * cellular) + 0.15f * Math.Clamp(c.Rain, 0f, 1f), 0f, 1f);
        float overcast = Smooth(0.6f, 0.95f, Math.Clamp(c.CloudCover, 0f, 1f) * (1f - 0.1f * cellular)) * // the light is dimmed by the whole storm, not only where its cells open the sky
             (0.55f + 0.45f * dark);
        float visibility = Math.Clamp(c.Visibility, 10f, 30000f);

        float haze = 0.00006f * sky.Density;
        float fog = Math.Max(haze, 2.3f / visibility * 0.85f);
        float skyAffect = Math.Clamp(1.25f - MathF.Log10(visibility) / 3.4f, 0.1f, 0.95f);

        // Morning fog: strongest just after sunrise, gone by mid-morning; damp air holds it.
        float damp = sky.Air switch { AirKind.Humid => 1f, AirKind.Breathable => 0.75f, AirKind.Exotic => 0.6f, AirKind.Thin => 0.25f, _ => 0.1f };
        float dawn = Smooth(-0.12f, 0.02f, sunHeight) * Smooth(0.4f, 0.12f, sunHeight);
        float valley = Math.Clamp(Math.Max(c.GroundFog, dawn * damp * (1f - c.Dust)), 0f, 1f);

        // Which clouds the air makes: damp, warm air heaps cumulus; cold air lies in grey stratus; dry air
        // and wind comb out high cirrus. (The shader varies them across the map and over time.)
        float humid = Math.Clamp(sky.Air switch { AirKind.Humid => 0.9f, AirKind.Breathable => 0.6f, AirKind.Exotic => 0.5f, AirKind.Sulfurous => 0.4f, AirKind.Ash => 0.3f, _ => 0.15f } + 0.5f * c.Rain + 0.3f * c.GroundFog, 0f, 1f);
        float cold = Smooth(12f, -2f, c.Temperature);
        float wind = Smooth(3f, 16f, c.WindSpeed);
        float solar = Math.Clamp(c.Solar, 0f, 1f);
        // The cloud layer sits between the peaks and the top of the atmosphere (about 1,060 m, on
        // Empyrion's scale): its base at 600 to 650 m, its top at about 1,000 m at the most.
        float baseHeight = 650f - 50f * cover * (0.4f + 0.6f * dark);
        return new AtmosphereState(
            CloudCover: cover,
            CloudDarkness: dark,
            CloudBase: baseHeight,
            CloudThickness: 200f + 150f * cover,
            Overcast: overcast,
            FogDensity: fog,
            FogSkyAffect: skyAffect,
            ValleyFog: valley,
            ValleyFogDepth: ValleyFogDepthOf(valley),
            SunScale: 0.12f + 0.88f * solar,
            AmbientScale: 1f + overcast * 0.6f,
            Saturation: 1.05f - 0.25f * overcast - 0.2f * c.Dust,
            Visibility: visibility)
        {
            GroundFog = Math.Clamp(c.GroundFog, 0f, 1f),
            Stratus = MathF.Max(Smooth(0.5f, 0.85f, cover), cold * Smooth(0.2f, 0.6f, cover) * 0.85f) * (1f - cellular),
            Cirrus = (1f - humid) * (1f - humid) * (0.15f + 0.85f * wind) * (1f - Smooth(0.3f, 0.7f, cover)), // mostly none: only dry, windy, fair skies comb it out
        };
    }

    /// <summary>How deep the haze lies, m: its density falls by e over this much height.</summary>
    public const float HazeDepth = 180f;

    /// <summary>The most depth fog that is the air's haze, lying low (a visibility of about 5.6 km); past it the weather's murk fills the whole air.</summary>
    public const float HazeMost = 0.00035f;

    /// <summary>
    /// The depth fog for an eye <paramref name="height"/> m over the ground under it. The haze lies
    /// low, thinning with height (<see cref="HazeDepth"/>), so from high up the far land is seen
    /// through less of it than along the ground: the density is its mean along a ray from the eye
    /// down to the ground. From 600 m a clear day's haze is under a third of what it is on foot, so the
    /// land reads out to the horizon, hazed but never erased. A storm's or a fog's own murk is the
    /// weather's and stays as thick at every height.
    /// </summary>
    public static float FogFrom(float fog, float height)
    {
        if (height <= 1f) return fog;
        float haze = Math.Min(fog, HazeMost);
        return haze * HazeDepth * (1f - MathF.Exp(-height / HazeDepth)) / height + fog - haze;
    }

    /// <summary>How deep valley fog of this strength (0..1) lies over the low ground, m.</summary>
    public static float ValleyFogDepthOf(float valley) => 18f + 46f * valley;

    /// <summary>The look of a clear day, for worlds before their weather starts and for tests.</summary>
    public static Conditions Clear()
    {
        var c = new Conditions();
        c[Param.CloudCover] = 0.15f;
        c[Param.VisibilityLog] = 4.1f;
        c[Param.Solar] = 1f;
        c[Param.MoveSpeed] = 1f;
        return c;
    }

    static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
