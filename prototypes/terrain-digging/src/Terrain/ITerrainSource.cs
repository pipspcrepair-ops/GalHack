using System;
using System.Collections.Generic;

namespace TerrainDigging.Terrain;

/// <summary>
/// The seed-generated base terrain that player edits sit on top of. Everything here must be a
/// pure function of the seed: edits are stored as differences against it, so a source that
/// generated different cells on another machine (or after an update) would put holes in the
/// wrong places. The planet-generator prototype will plug in behind this interface.
/// </summary>
public interface ITerrainSource
{
    int Seed { get; }

    /// <summary>
    /// Names the generator and its version. Saves record it and refuse to load against a
    /// different one, because their edit lists only make sense on the terrain they were dug from.
    /// </summary>
    string GeneratorId { get; }

    /// <summary>Height of the static water plane. Only columns whose natural surface is lower hold water.</summary>
    float WaterLevel { get; }

    /// <summary>Height of the natural (undug) surface at a point.</summary>
    float SurfaceHeight(float x, float z);

    /// <summary>
    /// Fills one chunk of base cells, indexed x + Size·(z + Size·y). Density is in ±ChunkStore.DensityLimit,
    /// roughly the distance to the surface in metres, positive inside solid ground, and must be
    /// positive exactly where the material is solid.
    /// The smooth mesh passes through density zero, so density shapes the surface between cells.
    /// </summary>
    void GenerateChunk(Int3 chunk, Span<float> density, Span<CellMaterial> material);

    /// <summary>Generated points of interest. The ground under and around them is anchored.</summary>
    IReadOnlyList<PoiSite> PointsOfInterest { get; }
}

/// <summary>A generated point of interest (ruin, wreck, beacon). Footprint covers the structure itself.</summary>
public sealed record PoiSite(string Name, CellBox Footprint);
