using System;
using System.Collections.Generic;
using System.Numerics;
using TerrainDigging.Terrain;

namespace GalHack.Terrain;

/// <summary>What one edit did, in the frame it was made in.</summary>
public sealed class PlanetEdit
{
    public readonly EditKind Kind;

    /// <summary>Cells removed, once each, where the edit was made (not their copies across a seam).</summary>
    public readonly List<RemovedCell> Removed = new();

    /// <summary>Every chunk whose mesh changed, copies across the seams included.</summary>
    public readonly HashSet<Int3> DirtyChunks = new();

    public int AlreadyAir, Bedrock, Anchored, Outside;

    public PlanetEdit(EditKind kind) => Kind = kind;

    public Verdict Headline =>
        Removed.Count > 0 ? Verdict.Removable
        : Bedrock > 0 ? Verdict.Bedrock
        : Anchored > 0 ? Verdict.Anchored
        : Outside > 0 ? Verdict.OutsideWorld
        : Verdict.AlreadyAir;
}

/// <summary>
/// A planet's diggable terrain: terrain-digging's <see cref="ChunkStore"/> (all its rules:
/// remove-only, bedrock, anchors, grow-only diffs, audited saves) over a
/// <see cref="PlanetTerrainSource"/>, made to work on a torus.
///
/// The store works in one flat frame: the planet itself (x in [0, width), z in [0, height)) plus
/// a band of <see cref="GhostChunks"/> chunks beyond each edge. A ghost chunk is a second copy of
/// the chunk on the far side of the seam: the source generates identical cells for it, and every
/// edit is applied to every copy of each cell it touches. So a hole dug right on a seam shows on
/// both sides, meshes on either side of a seam read the same neighbours, and a player near a seam
/// can be simulated entirely in the frame, then moved by a whole planet width when they cross.
///
/// Main thread only, like the store.
/// </summary>
public sealed class PlanetTerrain
{
    public const int S = ChunkStore.Size;

    /// <summary>Ghost chunks beyond each seam: more than the voxel streaming radius plus slack.</summary>
    public const int GhostChunks = 8;

    /// <summary>Lowest and highest chunk y any planet uses (-1 km to 2.5 km).</summary>
    public const int MinChunkY = -64, MaxChunkY = 160;

    /// <summary>The largest planet, in chunks (64 x 32 km).</summary>
    const int MaxChunksX = 4000, MaxChunksZ = 2000;

    public readonly PlanetTerrainSource Source;
    public readonly ChunkStore Store;
    public readonly int Width, Height, ChunksX, ChunksZ;

    /// <summary>Chunks every edit has marked dirty, by canonical column: streaming must mesh these even below the surface.</summary>
    readonly Dictionary<long, SortedSet<int>> _touched = new();

    /// <summary>
    /// Widens terrain-digging's world bounds once, for every planet: big enough for the largest
    /// planet plus its ghost band. Edits never land outside a planet's own frame because every
    /// position goes through this class.
    /// </summary>
    public static void ConfigureWorldBounds()
    {
        var bounds = new CellBox(new Int3(-GhostChunks, MinChunkY, -GhostChunks), new Int3(MaxChunksX + GhostChunks, MaxChunkY, MaxChunksZ + GhostChunks));
        if (!ChunkStore.WorldChunks.Equals(bounds)) ChunkStore.SetWorldBounds(bounds);
    }

    public PlanetTerrain(PlanetTerrainSource source)
    {
        ConfigureWorldBounds();
        Source = source;
        Store = new ChunkStore(source);
        Width = source.Width;
        Height = source.Height;
        ChunksX = source.ChunksX;
        ChunksZ = source.ChunksZ;
    }

    // ---- Frame and copies -----------------------------------------------------------------

    /// <summary>Whether a cell lies in this planet's frame: the planet plus its ghost band.</summary>
    public bool InFrame(Int3 cell) =>
        cell.X >= -GhostChunks * S && cell.X < Width + GhostChunks * S &&
        cell.Z >= -GhostChunks * S && cell.Z < Height + GhostChunks * S &&
        cell.Y >= MinChunkY * S && cell.Y < MaxChunkY * S;

    public Int3 Canonical(Int3 cell) => new(Mod(cell.X, Width), cell.Y, Mod(cell.Z, Height));
    public Int3 CanonicalChunk(Int3 chunk) => new(Mod(chunk.X, ChunksX), chunk.Y, Mod(chunk.Z, ChunksZ));

    /// <summary>The cell itself and every copy of it across a seam that lies in the frame (up to four).</summary>
    public void Copies(Int3 cell, List<Int3> into)
    {
        into.Clear();
        Int3 c = Canonical(cell);
        for (int a = -1; a <= 1; a++)
        for (int b = -1; b <= 1; b++)
        {
            var copy = new Int3(c.X + a * Width, c.Y, c.Z + b * Height);
            if (InFrame(copy)) into.Add(copy);
        }
    }

    /// <summary>Offsets (a whole planet width or height, or both) from a box to each of its copies in the frame, itself excluded.</summary>
    IEnumerable<Int3> CopyOffsets(CellBox box)
    {
        for (int a = -1; a <= 1; a++)
        for (int b = -1; b <= 1; b++)
        {
            if (a == 0 && b == 0) continue;
            var offset = new Int3(a * Width, 0, b * Height);
            Int3 min = box.Min + offset, max = box.Max + offset;
            bool overlaps = max.X > -GhostChunks * S && min.X < Width + GhostChunks * S && max.Z > -GhostChunks * S && min.Z < Height + GhostChunks * S;
            if (overlaps) yield return offset;
        }
    }

    // ---- Edits ----------------------------------------------------------------------------

    /// <summary>The survival tool: a small ball of cells around one cell.</summary>
    public PlanetEdit Dig(Int3 center, float radius) =>
        Remove(ChunkStore.SphereCells(center, Math.Clamp(radius, 0f, ChunkStore.MaxToolRadius)), EditKind.Tool);

    /// <summary>Removes specific cells under the tool's rules.</summary>
    public PlanetEdit DigCells(IEnumerable<Int3> cells) => Remove(cells, EditKind.Tool);

    /// <summary>A crash or liftoff: capped like terrain-digging's crater (radius and cell count).</summary>
    public PlanetEdit Crater(Vector3 center, float radius)
    {
        radius = Math.Clamp(radius, 0.5f, ChunkStore.MaxCraterRadius);
        int r = (int)MathF.Ceiling(radius);
        Int3 c = Int3.Floor(center);
        var cells = new List<(float d2, Int3 cell)>();
        for (int y = -r; y <= r; y++)
        for (int z = -r; z <= r; z++)
        for (int x = -r; x <= r; x++)
        {
            var cell = new Int3(c.X + x, c.Y + y, c.Z + z);
            float d2 = Vector3.DistanceSquared(cell.Center, center);
            if (d2 <= radius * radius) cells.Add((d2, cell));
        }
        cells.Sort((a, b) => a.d2 != b.d2 ? a.d2.CompareTo(b.d2) : a.cell.CompareTo(b.cell));
        var list = new List<Int3>(Math.Min(cells.Count, ChunkStore.MaxCraterCells));
        for (int i = 0; i < cells.Count && i < ChunkStore.MaxCraterCells; i++) list.Add(cells[i].cell);
        return Remove(list, EditKind.Crater);
    }

    /// <summary>
    /// Whether a cell may be removed, judged on every copy of it: the canonical copy carries the
    /// POI anchors, and a structure placed by a seam anchors all its own copies.
    /// </summary>
    public Verdict Check(Int3 cell, EditKind kind)
    {
        if (!InFrame(cell)) return Verdict.OutsideWorld;
        Verdict v = Store.Check(cell, kind);
        if (v != Verdict.Removable) return v;
        var copies = new List<Int3>(4);
        Copies(cell, copies);
        foreach (Int3 copy in copies)
            if (copy != cell && Store.Check(copy, kind) == Verdict.Anchored) return Verdict.Anchored;
        return Verdict.Removable;
    }

    PlanetEdit Remove(IEnumerable<Int3> cells, EditKind kind)
    {
        var edit = new PlanetEdit(kind);
        var log = new EditLog();
        var copies = new List<Int3>(4);
        foreach (Int3 cell in cells)
        {
            switch (Check(cell, kind))
            {
                case Verdict.AlreadyAir: edit.AlreadyAir++; continue;
                case Verdict.Bedrock: edit.Bedrock++; continue;
                case Verdict.Anchored: edit.Anchored++; continue;
                case Verdict.OutsideWorld: edit.Outside++; continue;
            }
            if (log.Contains(cell)) continue; // listed twice
            edit.Removed.Add(new RemovedCell(cell, Store.MaterialAt(cell)));
            Copies(cell, copies);
            foreach (Int3 copy in copies) log.Add(copy);
        }
        // The checks are done: write every copy as one diff, which only marks cells removed.
        ApplyReport report = Store.ApplyDiff(log);
        Touch(report.DirtyChunks);
        edit.DirtyChunks.UnionWith(report.DirtyChunks);
        return edit;
    }

    /// <summary>
    /// Places a building block (terrain-digging's rules 3 and 4) and gives each of its copies
    /// across a seam the same cleared cells and anchors. Copies get negative ids so they never
    /// collide with real structures, and are saved and audited like any other structure.
    /// </summary>
    public PlaceResult PlaceStructure(CellBox box)
    {
        if (!InFrame(box.Min) || !InFrame(box.Max - Int3.One)) return new PlaceResult { Verdict = PlaceVerdict.OutsideWorld };
        foreach (Int3 offset in CopyOffsets(box))
            foreach (Structure other in Store.Structures)
                if (other.Box.Intersects(new CellBox(box.Min + offset, box.Max + offset)))
                    return new PlaceResult { Verdict = PlaceVerdict.Overlaps };

        PlaceResult result = Store.PlaceStructure(box);
        if (result.Verdict != PlaceVerdict.Placed) return result;
        Touch(result.Cleared.DirtyChunks);
        int copy = 0;
        foreach (Int3 offset in CopyOffsets(box))
        {
            var log = new EditLog();
            foreach (RemovedCell r in result.Cleared.Removed) log.Add(r.Cell + offset);
            ApplyReport report = Store.ApplyDiff(log);
            Touch(report.DirtyChunks);
            result.Cleared.DirtyChunks.UnionWith(report.DirtyChunks);
            Store.RestoreStructure(new Structure(-(result.Structure.Id * 9 + ++copy), new CellBox(box.Min + offset, box.Max + offset)));
        }
        return result;
    }

    // ---- Persistence ----------------------------------------------------------------------

    /// <summary>Atomic, verified save of every edit and structure (copies included), via terrain-digging's save format.</summary>
    public SaveReport Save(string path, ulong sequence) => TerrainSave.Save(Store, path, sequence);

    /// <summary>
    /// Loads and audits a save (falling back to its backup), and only if it passes swaps it in.
    /// Returns the chunks whose meshes changed in <paramref name="dirty"/>.
    /// </summary>
    public LoadResult Load(string path, out HashSet<Int3> dirty)
    {
        dirty = new HashSet<Int3>();
        LoadResult result = TerrainSave.LoadLatest(path, Store.Base);
        if (!result.Ok) return result;
        dirty = Store.ReplaceStateWith(result.Store);
        _touched.Clear();
        // Every edited chunk and its neighbours (which read it as padding) must stay meshed.
        var near = new List<Int3>(27);
        foreach (Int3 chunk in Store.EditedChunks)
        {
            near.Clear();
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                near.Add(new Int3(chunk.X + dx, chunk.Y + dy, chunk.Z + dz));
            Touch(near);
        }
        Touch(dirty);
        return result;
    }

    // ---- Streaming support ----------------------------------------------------------------

    void Touch(IEnumerable<Int3> chunks)
    {
        foreach (Int3 chunk in chunks)
        {
            Int3 c = CanonicalChunk(chunk);
            long key = (long)c.X << 32 | (uint)c.Z;
            if (!_touched.TryGetValue(key, out SortedSet<int> ys)) _touched[key] = ys = new SortedSet<int>();
            ys.Add(c.Y);
        }
    }

    /// <summary>Chunk ys that edits have touched in a column (any copy of it), or null.</summary>
    public SortedSet<int> TouchedYs(int cx, int cz)
    {
        long key = (long)Mod(cx, ChunksX) << 32 | (uint)Mod(cz, ChunksZ);
        return _touched.TryGetValue(key, out SortedSet<int> ys) ? ys : null;
    }

    static int Mod(int v, int m) => (v % m + m) % m;
}
