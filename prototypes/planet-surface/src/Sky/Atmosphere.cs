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
        float cover = Math.Clamp(c.CloudCover, 0f, 1f);
        float dark = Math.Clamp(c.CloudDarkness, 0f, 1f);
        float overcast = Smooth(0.6f, 0.95f, cover) * (0.55f + 0.45f * dark);
        float visibility = Math.Clamp(c.Visibility, 10f, 30000f);

        float haze = 0.00006f * sky.Density;
        float fog = Math.Max(haze, 2.3f / visibility * 0.85f);
        float skyAffect = Math.Clamp(1.25f - MathF.Log10(visibility) / 3.4f, 0.1f, 0.95f);

        // Morning fog: strongest just after sunrise, gone by mid-morning; damp air holds it.
        float damp = sky.Air switch { AirKind.Humid => 1f, AirKind.Breathable => 0.75f, AirKind.Exotic => 0.6f, AirKind.Thin => 0.25f, _ => 0.1f };
        float dawn = Smooth(-0.12f, 0.02f, sunHeight) * Smooth(0.4f, 0.12f, sunHeight);
        float valley = Math.Clamp(Math.Max(c.GroundFog, dawn * damp * (1f - c.Dust)), 0f, 1f);

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
        };
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
