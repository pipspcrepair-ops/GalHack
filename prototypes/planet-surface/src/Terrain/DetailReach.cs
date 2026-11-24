using System;

namespace PlanetSurface.Terrain;

/// <summary>
/// How far land, flora, places and roadside ruins draw in detail: one distance (a game's detail
/// setting), from <see cref="Default"/> out to the horizon (<see cref="Most"/>). Everything that
/// reaches by distance takes its own reach from it: the far rings' radius and their levels of
/// detail, flora's reaches (flora.gdshader's, set on its material), places' merged blocks and full
/// structures, and the roadside tiles (<see cref="PlanetSurface.Infra.InfraLayer.Radius"/>). It can change while
/// streaming: <see cref="Version"/> goes up and each layer lays itself out again.
/// </summary>
public static class DetailReach
{
    /// <summary>The detail distance a game starts at, m: large growth stands to about here, the far rings run to it.</summary>
    public const float Default = 1280f;

    /// <summary>The most it can be: the horizon, where the far field takes over (<see cref="TerrainStreamer.HorizonDistance"/>).</summary>
    public const float Most = TerrainStreamer.HorizonDistance;

    public static float Distance { get; private set; }

    /// <summary>The least trees' distance can be set to, m.</summary>
    public const float LeastTrees = 300f;

    /// <summary>The distance trees and large plants were asked to draw to, m (kept between <see cref="LeastTrees"/> and the detail distance).</summary>
    public static float Trees { get; private set; } = Default;

    /// <summary>How far the roadside tiles are built (<see cref="PlanetSurface.Infra.InfraLayer.Radius"/>), m.</summary>
    public static float InfraRadius { get; private set; }
    public static int Version { get; private set; }

    /// <summary>The distance as a multiple of the default.</summary>
    public static float Scale => Distance / Default;

    /// <summary>The far rings, in 128 m chunks each way: all of them, and the last of level 0 and of level 1.</summary>
    public static int FarRadius { get; private set; }
    public static int Lod0Radius { get; private set; }
    public static int Lod1Radius { get; private set; }

    /// <summary>
    /// Flora's reaches from the eye and the bands they fade over, m. Every large plant (tree, bush, boulder) has one
    /// drawing, its own mesh, out to the detail distance, and dissolves over the last <see cref="LargeBand"/> of it; small
    /// growth shrinks into the ground over the last <see cref="SmallBand"/> of its reach. Nothing swaps or appears whole.
    /// </summary>
    public static float Large, LargeBand, Small, SmallBand;

    /// <summary>How far along from the default to the horizon the distance is set, 0..1.</summary>
    public static float Along => (Distance - Default) / (Most - Default);

    /// <summary>A distance that is <paramref name="atDefault"/> at the default and the horizon at the top (places' merged blocks).</summary>
    public static float Toward(float atDefault) => atDefault + (Most - atDefault) * Along;

    /// <summary>Places' full structures reach this multiple of their own radii: at most twice, as they carry colliders, people and creatures.</summary>
    public static float StandScale => MathF.Min(Scale, 2f);

    static DetailReach() => Set(Default, Default);

    /// <summary>Sets the detail distance, m (kept between the default and the horizon), and how far trees draw within it (never past it).</summary>
    public static void Set(float metres, float trees = float.MaxValue)
    {
        float d = Math.Clamp(float.IsFinite(metres) ? metres : Default, Default, Most);
        float t = Math.Clamp(float.IsNaN(trees) ? d : trees, LeastTrees, d);
        if (d == Distance && t == Trees) return;
        Distance = d;
        Trees = t;
        float k = Scale;
        FarRadius = (int)MathF.Ceiling(d / FarBuilder.Size);
        Lod0Radius = k >= 2f ? 2 : 1;
        Lod1Radius = Math.Clamp((int)MathF.Round(3f * k), 3, 12);
        Large = t;
        LargeBand = t * 0.15f;
        Small = 200f * MathF.Min(k, 2f);
        SmallBand = Small * 0.3f;
        // What stands on the land is built as far out as the far rings show its pads, corners included.
        InfraRadius = (FarRadius + 0.3f) * FarBuilder.Size * MathF.Sqrt(2f);
        Version++;
    }
}
