using System;
using Godot;

namespace PlanetSurface.Skies;

/// <summary>How far and how sharp the sun's shadows are: the player's choice.</summary>
public enum ShadowLevel { Low, Medium, High, Ultra }

/// <summary>
/// The one place the sun's shadows are set, for every scene: each sun and night light is made here (<see cref="Light"/>),
/// and a level and a shadow distance become the engine's numbers here (<see cref="Apply"/>). Nothing else sets a
/// directional light's shadow.
/// </summary>
public static class SunShadows
{
    /// <summary>A level as the engine's numbers.</summary>
    /// <param name="Reach">How far from the eye the sun's shadows are drawn, m.</param>
    /// <param name="SunAtlas">The sun's shadow map, pixels a side; <paramref name="LampAtlas"/> the torch's and lamps'.</param>
    public readonly record struct Setup(float Reach, DirectionalLight3D.ShadowMode Splits, int SunAtlas, int LampAtlas, RenderingServer.ShadowQuality Filter);

    /// <summary>The shadow distance a level reaches by itself, m; past it the far land's own shade (FarShade) takes over.</summary>
    public const float DefaultReach = 320f, MostReach = 1500f;

    public static Setup For(ShadowLevel level) => level switch
    {
        ShadowLevel.Low => new(120f, DirectionalLight3D.ShadowMode.Parallel2Splits, 2048, 1024, RenderingServer.ShadowQuality.SoftVeryLow),
        ShadowLevel.Medium => new(220f, DirectionalLight3D.ShadowMode.Parallel4Splits, 2048, 2048, RenderingServer.ShadowQuality.SoftLow),
        ShadowLevel.Ultra => new(MostReach, DirectionalLight3D.ShadowMode.Parallel4Splits, 8192, 4096, RenderingServer.ShadowQuality.SoftUltra),
        _ => new(DefaultReach, DirectionalLight3D.ShadowMode.Parallel4Splits, 4096, 4096, RenderingServer.ShadowQuality.SoftHigh),
    };

    /// <summary>The sun's shadow normal bias (the engine's is 2, in texels: at a low sun it hid small shadows and set the rest off their bases) and the first split's distance, m.</summary>
    const float NormalBias = 0.6f, FirstSplit = 30f;

    /// <summary>How far the sun moves before its light turns after it, radians (0: every frame).</summary>
    public static float TurnStep = 0f;

    /// <summary>Where the far land's shade is full, as a share of the reach: the real shadows fade from there (PlanetSurface.Terrain.FarShade.JoinFull).</summary>
    const float FadeStart = 0.85f;

    /// <summary>A sun or night light, shadowed as <see cref="ShadowLevel.High"/> until a game applies its own level.</summary>
    public static DirectionalLight3D Light(string name, bool shadows)
    {
        var light = new DirectionalLight3D { Name = name, ShadowEnabled = shadows };
        Shape(light, For(ShadowLevel.High).Splits, DefaultReach);
        return light;
    }

    /// <summary>
    /// A level, and the shadow distance setting (used when it is set above AUTO, <see cref="DefaultReach"/>), applied to the
    /// engine's shadow maps and filters and to each light. The reach it came to (the far land's shade joins there).
    /// </summary>
    public static float Apply(ShadowLevel level, float distance, Viewport viewport, params DirectionalLight3D[] lights)
    {
        Setup s = For(level);
        RenderingServer.DirectionalShadowAtlasSetSize(s.SunAtlas, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(s.Filter);
        RenderingServer.PositionalSoftShadowFilterSetQuality(s.Filter);
        viewport.PositionalShadowAtlasSize = s.LampAtlas;
        float reach = distance > DefaultReach ? distance : s.Reach;
        foreach (DirectionalLight3D light in lights)
            if (light != null) Shape(light, s.Splits, reach);
        return reach;
    }

    /// <summary>
    /// One light's shadow: the splits grow by one ratio from the first (so a texel grows with distance, and a plant's shadow
    /// is as sharp at 300 m as at 80), each hands over to the next across a band, and the last 15% fades as the far shade comes in.
    /// </summary>
    static void Shape(DirectionalLight3D light, DirectionalLight3D.ShadowMode splits, float reach)
    {
        light.DirectionalShadowMode = splits;
        light.DirectionalShadowMaxDistance = reach;
        light.ShadowNormalBias = NormalBias;
        light.DirectionalShadowBlendSplits = true;
        light.DirectionalShadowFadeStart = FadeStart;
        float ratio = MathF.Pow(reach / FirstSplit, splits == DirectionalLight3D.ShadowMode.Parallel4Splits ? 1f / 3f : 1f);
        light.DirectionalShadowSplit1 = FirstSplit / reach;
        light.DirectionalShadowSplit2 = FirstSplit * ratio / reach;
        light.DirectionalShadowSplit3 = FirstSplit * ratio * ratio / reach;
    }
}
