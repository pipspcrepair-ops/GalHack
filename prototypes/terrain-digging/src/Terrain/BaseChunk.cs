using System;
using System.Collections.Concurrent;
using System.Threading;

namespace TerrainDigging.Terrain;

/// <summary>
/// One chunk of seed-generated cells, before any edits. Immutable once built, so worker
/// threads can mesh from it while the main thread digs. Chunks that are all one thing
/// (open sky, solid bedrock) keep a single value instead of arrays.
/// </summary>
public sealed class BaseChunk
{
    public readonly float[] Density;
    public readonly CellMaterial[] Material;
    public readonly float UniformDensity;
    public readonly CellMaterial UniformMaterial;
    public readonly int SolidCount;

    public bool IsUniform => Density == null;

    BaseChunk(float[] density, CellMaterial[] material, float uniformDensity, CellMaterial uniformMaterial, int solidCount)
    {
        Density = density;
        Material = material;
        UniformDensity = uniformDensity;
        UniformMaterial = uniformMaterial;
        SolidCount = solidCount;
    }

    public float DensityAt(int index) => Density != null ? Density[index] : UniformDensity;
    public CellMaterial MaterialAt(int index) => Material != null ? Material[index] : UniformMaterial;

    // Per-thread scratch: most chunks are uniform (open sky, solid rock) and keep no arrays, so
    // generating into fresh arrays only to drop them made streamed worlds churn the GC.
    [ThreadStatic] static float[] _scratchDensity;
    [ThreadStatic] static CellMaterial[] _scratchMaterial;

    public static BaseChunk Generate(ITerrainSource source, Int3 chunk)
    {
        float[] density = _scratchDensity ??= new float[ChunkStore.Volume];
        CellMaterial[] material = _scratchMaterial ??= new CellMaterial[ChunkStore.Volume];
        Array.Clear(density);
        Array.Clear(material);
        source.GenerateChunk(chunk, density, material);

        int solid = 0;
        bool uniform = true;
        for (int i = 0; i < density.Length; i++)
        {
            if (material[i] != CellMaterial.Air) solid++;
            if (density[i] != density[0] || material[i] != material[0]) uniform = false;
        }
        if (uniform) return new BaseChunk(null, null, density[0], material[0], solid);
        // Kept cells go on the pinned heap: they live for seconds while a streamed world passes,
        // and there the collector never copies them from generation to generation.
        float[] keptDensity = GC.AllocateUninitializedArray<float>(ChunkStore.Volume, pinned: true);
        CellMaterial[] keptMaterial = GC.AllocateUninitializedArray<CellMaterial>(ChunkStore.Volume, pinned: true);
        density.CopyTo(keptDensity, 0);
        material.CopyTo(keptMaterial, 0);
        return new BaseChunk(keptDensity, keptMaterial, 0f, CellMaterial.Air, solid);
    }
}

/// <summary>
/// Thread-safe, lazily generated base chunks. Generation happens on whichever thread asks
/// first (normally a mesh worker), and every other thread waits for that one result.
/// </summary>
public sealed class BaseCache
{
    readonly ConcurrentDictionary<Int3, Lazy<BaseChunk>> _chunks = new();

    public ITerrainSource Source { get; }

    public BaseCache(ITerrainSource source) => Source = source;

    public int Count => _chunks.Count;

    /// <summary>
    /// Forgets every generated chunk so they are regenerated from the seed. Workers still holding
    /// old chunks keep valid (identical) data, so this is safe at any time.
    /// </summary>
    public void Clear() => _chunks.Clear();

    /// <summary>
    /// Forgets the generated chunks the predicate picks, so a streamed world can drop chunks far
    /// from everyone. Like Clear, safe at any time: they regenerate identically when next asked for.
    /// </summary>
    public int Forget(Func<Int3, bool> drop)
    {
        int forgotten = 0;
        foreach (Int3 chunk in _chunks.Keys)
            if (drop(chunk) && _chunks.TryRemove(chunk, out _)) forgotten++;
        return forgotten;
    }

    public BaseChunk Get(Int3 chunk)
    {
        if (_chunks.TryGetValue(chunk, out Lazy<BaseChunk> lazy)) return lazy.Value;
        return _chunks.GetOrAdd(chunk, static (c, self) => new Lazy<BaseChunk>(
            () => BaseChunk.Generate(self.Source, c), LazyThreadSafetyMode.ExecutionAndPublication), this).Value;
    }
}
