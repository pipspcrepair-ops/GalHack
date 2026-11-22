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
    /// <param name="Filter">How many samples soften an edge: the stairs of a shadow-map texel, long at a low sun, blend into a ramp
    /// that slides as the sun turns, where a few samples left them stepped and flipping whole pixels a frame (PopRoute edges: on a
    /// dune world, edge pixels changing a frame as the sun moves 2.6% at SoftLow, 0.3% at SoftHigh with <see cref="Blur"/>).</param>
    public readonly record struct Setup(float Reach, DirectionalLight3D.ShadowMode Splits, int SunAtlas, int LampAtlas, RenderingServer.ShadowQuality Filter);

    /// <summary>The shadow distance a level reaches by itself, m; past it the far land's own shade (FarShade) takes over.</summary>
    public const float DefaultReach = 320f, MostReach = 1500f;

    /// <summary>
    /// High reaches 320 m, and the far land's own shade takes over past it. (High at 1500 m spread one map over five times the
    /// ground: past 400 m a texel was 2 m, so every far tree's shadow crawled at a low sun and sharpened in steps as you came in.)
    /// </summary>
    public static Setup For(ShadowLevel level) => level switch
    {
        ShadowLevel.Low => new(120f, DirectionalLight3D.ShadowMode.Parallel2Splits, 2048, 1024, RenderingServer.ShadowQuality.SoftVeryLow),
        ShadowLevel.Medium => new(220f, DirectionalLight3D.ShadowMode.Parallel4Splits, 2048, 2048, RenderingServer.ShadowQuality.SoftLow),
        ShadowLevel.Ultra => new(MostReach, DirectionalLight3D.ShadowMode.Parallel4Splits, 8192, 4096, RenderingServer.ShadowQuality.SoftUltra),
        _ => new(DefaultReach, DirectionalLight3D.ShadowMode.Parallel4Splits, 4096, 4096, RenderingServer.ShadowQuality.SoftHigh),
    };

    /// <summary>The sun's shadow normal bias (the engine's is 2, in texels: at a low sun it hid small shadows and set the rest off their bases) and the first split's distance, m.</summary>
    const float NormalBias = 0.6f, FirstSplit = 30f;

    /// <summary>How wide the filter spreads an edge, in texels (the engine's is 1).</summary>
    const float Blur = 1.5f;

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
    /// is as sharp at 300 m as at 80), each hands over to the next across a band, and the last 15% fades as the far shade
    /// comes in (at every level: Low and Medium kept the engine's splits and fade before, which the far shade did not meet).
    /// </summary>
    static void Shape(DirectionalLight3D light, DirectionalLight3D.ShadowMode splits, float reach)
    {
        light.DirectionalShadowMode = splits;
        light.DirectionalShadowMaxDistance = reach;
        light.ShadowNormalBias = NormalBias;
        light.ShadowBlur = Blur;
        light.DirectionalShadowBlendSplits = true;
        light.DirectionalShadowFadeStart = FadeStart;
        float ratio = MathF.Pow(reach / FirstSplit, splits == DirectionalLight3D.ShadowMode.Parallel4Splits ? 1f / 3f : 1f);
        light.DirectionalShadowSplit1 = FirstSplit / reach;
        light.DirectionalShadowSplit2 = FirstSplit * ratio / reach;
        light.DirectionalShadowSplit3 = FirstSplit * ratio * ratio / reach;
    }
}
