using System;
using PlanetSurface.Infra;

namespace PlanetSurface.Terrain;

/// <summary>
/// How far land, flora, places and roadside ruins draw in detail: one distance (a game's detail
/// setting), from <see cref="Default"/> out to the horizon (<see cref="Most"/>). Everything that
/// reaches by distance takes its own reach from it: the far rings' radius and their levels of
/// detail, flora's reaches (flora.gdshader's, set on its material), places' merged blocks and full
/// structures, and the roadside tiles (<see cref="InfraLayer.Radius"/>). It can change while
/// streaming: <see cref="Version"/> goes up and each layer lays itself out again.
/// </summary>
public static class DetailReach
{
    /// <summary>The detail distance a game starts at, m: large growth stands to about here, the far rings run to it.</summary>
    public const float Default = 1280f;

    /// <summary>The most it can be: the horizon, where the far field takes over (<see cref="TerrainStreamer.HorizonDistance"/>).</summary>
    public const float Most = TerrainStreamer.HorizonDistance;

    /// <summary>A share of the large growth (those of its first near mesh, half) also has a distant drawing, the same far drawing, out to the detail distance; the rest go at the far reach.</summary>
    public const float DistantShare = 0.5f, DistantGrow = 1f;

    public static float Distance { get; private set; }

    /// <summary>How far the roadside tiles are built (<see cref="InfraLayer.Radius"/>), m.</summary>
    public static float InfraRadius { get; private set; }
    public static int Version { get; private set; }

    /// <summary>The distance as a multiple of the default.</summary>
    public static float Scale => Distance / Default;

    /// <summary>The far rings, in 128 m chunks each way: all of them, and the last of level 0 and of level 1.</summary>
    public static int FarRadius { get; private set; }
    public static int Lod0Radius { get; private set; }
    public static int Lod1Radius { get; private set; }

    /// <summary>
    /// Flora's reaches and the bands they dissolve over, m: the near drawings of large growth, small
    /// growth, large growth's far drawings, its thinning share, and the distant drawings.
    /// </summary>
    public static float Near, NearBand, Small, SmallBand, Far, FarBand, Thin, ThinBand, Distant, DistantBand;

    /// <summary>How far along from the default to the horizon the distance is set, 0..1.</summary>
    public static float Along => (Distance - Default) / (Most - Default);

    /// <summary>A distance that is <paramref name="atDefault"/> at the default and the horizon at the top (places' merged blocks).</summary>
    public static float Toward(float atDefault) => atDefault + (Most - atDefault) * Along;

    /// <summary>Places' full structures reach this multiple of their own radii: at most twice, as they carry colliders, people and creatures.</summary>
    public static float StandScale => MathF.Min(Scale, 2f);

    /// <summary>
    /// How far a plant's drawings take to hand over or go, m, short of the detail distance: next to
    /// nothing, so a plant is always whole, one drawing or the other, never half shown (it swaps in a frame). A wood still
    /// thins gradually, as each plant has its own reach (<see cref="TerrainStreamer.FloraShows"/>).
    /// Only the distant drawings fade out at the end, over <see cref="DistantBand"/>.
    /// </summary>
    public const float Swap = 0.02f;

    static DetailReach() => Set(Default);

    /// <summary>Sets the detail distance, m (kept between the default and the horizon).</summary>
    public static void Set(float metres)
    {
        float d = Math.Clamp(float.IsFinite(metres) ? metres : Default, Default, Most);
        if (d == Distance) return;
        Distance = d;
        float k = Scale;
        FarRadius = (int)MathF.Ceiling(d / FarBuilder.Size);
        Lod0Radius = k >= 2f ? 2 : 1;
        Lod1Radius = Math.Clamp((int)MathF.Round(3f * k), 3, 12);
        Near = 120f * Lod0Radius;
        NearBand = Swap;
        Small = 200f * MathF.Min(k, 2f);
        SmallBand = Swap;
        Far = 370f * Lod1Radius / 3f;
        FarBand = Swap;
        Thin = Far * 200f / 370f;
        ThinBand = Swap;
        Distant = d;
        DistantBand = d * 0.15f;
        // What stands on the land is built as far out as the far rings show its pads, corners included.
        InfraRadius = (FarRadius + 0.3f) * FarBuilder.Size * MathF.Sqrt(2f);
        Version++;
    }
}
