using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace TerrainDigging.Terrain;

/// <summary>
/// The terrain as it is now: seed-generated base chunks plus, per chunk, the set of cells
/// players have removed. That set is the only thing edits can change, and it only ever grows,
/// so terrain can never appear inside a player, vehicle or structure (rule 1).
///
/// Removed sets are 4096-bit masks that are copied before they change and never modified
/// afterwards. A mesh worker can hold on to the masks it was given while the main thread
/// keeps digging, without locks and without seeing half an edit.
///
/// Ground can be dug anywhere but bedrock (rule 3), under and around structures and POIs too.
/// Structures standing in the ground register their volumes here (rule 4): the cells inside
/// are cleared, and the ground around is drawn flush against their faces.
///
/// Main thread only, apart from the immutable data it hands out.
/// </summary>
public sealed class ChunkStore
{
    public const int Size = 16, Shift = 4, LocalMask = Size - 1;
    public const int Volume = Size * Size * Size;
    public const int MaskWords = Volume / 64;

    /// <summary>
    /// How far outside a chunk (in cells) a structure volume can still change its mesh: its
    /// one-cell padding, and the padding's own neighbours, which decide how a cleared cell is drawn.
    /// </summary>
    public const int VolumeReach = 2;

    /// <summary>The playable world, in chunks. Edits outside it are refused.</summary>
    public static CellBox WorldChunks { get; private set; } = new(new Int3(-8, -3, -8), new Int3(8, 5, 8));
    public static CellBox WorldCells { get; private set; } = new(WorldChunks.Min * Size, WorldChunks.Max * Size);

    /// <summary>
    /// Widens (or narrows) the playable world. A streamed planet is far bigger than this
    /// prototype's box; it sets its bounds once at startup, before any store is used.
    /// </summary>
    public static void SetWorldBounds(CellBox chunks)
    {
        WorldChunks = chunks;
        WorldCells = new CellBox(chunks.Min * Size, chunks.Max * Size);
    }

    /// <summary>
    /// Densities are clamped to ±this. Removed cells are fully open (−Limit), so a dug face sits
    /// exactly on the cell boundary next to deep ground (+Limit), and the range is wide enough
    /// that smooth slopes never hit the clamp where the mesh needs them.
    /// </summary>
    public const float DensityLimit = 2f;

    public const float MaxToolRadius = 2f;
    public const float MaxCraterRadius = 6f;
    public const int MaxCraterCells = 900;

    /// <summary>The largest block <see cref="PlaceStructure"/> takes, in cells on a side.</summary>
    public const int MaxStructureSize = 8;

    public BaseCache Base { get; }
    public ITerrainSource Source => Base.Source;

    readonly Dictionary<Int3, ulong[]> _removed = new();
    readonly List<Structure> _structures = new();
    int _nextStructureId = 1;

    // Structure volumes (rule 4), and per chunk the ones near enough to change its mesh: a sorted
    // key set, and an immutable array snapshot that mesh workers are handed.
    readonly Dictionary<VolumeKey, StructureVolume> _volumes = new();
    readonly Dictionary<Int3, SortedSet<VolumeKey>> _volumeKeys = new();
    readonly Dictionary<Int3, StructureVolume[]> _volumeSnapshots = new();

    public int RemovedCount { get; private set; }
    public IReadOnlyList<Structure> Structures => _structures;
    public IEnumerable<Int3> EditedChunks => _removed.Keys;

    /// <summary>Every registered structure volume, by key.</summary>
    public IReadOnlyDictionary<VolumeKey, StructureVolume> Volumes => _volumes;

    public ChunkStore(ITerrainSource source) : this(new BaseCache(source)) { }

    /// <summary>Shares an existing base cache: base chunks are immutable, so stores can share them.</summary>
    public ChunkStore(BaseCache baseCache)
    {
        Base = baseCache;
    }

    // ---- Addressing -------------------------------------------------------------------

    public static Int3 ChunkOf(Int3 cell) => new(cell.X >> Shift, cell.Y >> Shift, cell.Z >> Shift);

    /// <summary>Index within a chunk: x fastest, then z, then y.</summary>
    public static int LocalIndex(Int3 cell) =>
        (cell.X & LocalMask) | ((cell.Z & LocalMask) << Shift) | ((cell.Y & LocalMask) << (2 * Shift));

    public static Int3 CellAt(Int3 chunk, int index) => new(
        chunk.X * Size + (index & LocalMask),
        chunk.Y * Size + (index >> (2 * Shift)),
        chunk.Z * Size + ((index >> Shift) & LocalMask));

    public static Int3 ChunkOrigin(Int3 chunk) => chunk * Size;
    public static bool InWorld(Int3 cell) => WorldCells.Contains(cell);
    public static bool IsWorldChunk(Int3 chunk) => WorldChunks.Contains(chunk);

    static bool Bit(ulong[] mask, int i) => (mask[i >> 6] & (1ul << (i & 63))) != 0;

    // ---- Queries ----------------------------------------------------------------------

    /// <summary>The chunk's removed cells, or null: never changed once published, so a worker can keep it.</summary>
    public ulong[] RemovedMask(Int3 chunk) => _removed.TryGetValue(chunk, out ulong[] m) ? m : null;

    public bool IsRemoved(Int3 cell) => _removed.TryGetValue(ChunkOf(cell), out ulong[] m) && Bit(m, LocalIndex(cell));

    public CellMaterial BaseMaterialAt(Int3 cell) => Base.Get(ChunkOf(cell)).MaterialAt(LocalIndex(cell));

    public CellMaterial MaterialAt(Int3 cell)
    {
        Int3 c = ChunkOf(cell);
        int i = LocalIndex(cell);
        if (_removed.TryGetValue(c, out ulong[] m) && Bit(m, i)) return CellMaterial.Air;
        return Base.Get(c).MaterialAt(i);
    }

    public bool IsSolid(Int3 cell) => MaterialAt(cell) != CellMaterial.Air;

    /// <summary>Density at a cell centre after edits: removed cells are fully open.</summary>
    public float DensityAt(Int3 cell)
    {
        Int3 c = ChunkOf(cell);
        int i = LocalIndex(cell);
        if (_removed.TryGetValue(c, out ulong[] m) && Bit(m, i)) return -DensityLimit;
        return Base.Get(c).DensityAt(i);
    }

    /// <summary>
    /// Trilinear density at any point, interpolating cell-centre samples. Positive means inside
    /// the ground. Its zero surface is what the mesher draws, so this agrees with what players see.
    /// </summary>
    public float SampleDensity(Vector3 p)
    {
        float qx = p.X - 0.5f, qy = p.Y - 0.5f, qz = p.Z - 0.5f;
        int x0 = (int)MathF.Floor(qx), y0 = (int)MathF.Floor(qy), z0 = (int)MathF.Floor(qz);
        float fx = qx - x0, fy = qy - y0, fz = qz - z0;

        float d000 = DensityAt(new Int3(x0, y0, z0)), d100 = DensityAt(new Int3(x0 + 1, y0, z0));
        float d010 = DensityAt(new Int3(x0, y0 + 1, z0)), d110 = DensityAt(new Int3(x0 + 1, y0 + 1, z0));
        float d001 = DensityAt(new Int3(x0, y0, z0 + 1)), d101 = DensityAt(new Int3(x0 + 1, y0, z0 + 1));
        float d011 = DensityAt(new Int3(x0, y0 + 1, z0 + 1)), d111 = DensityAt(new Int3(x0 + 1, y0 + 1, z0 + 1));

        float a = d000 + (d100 - d000) * fx, b = d010 + (d110 - d010) * fx;
        float c = d001 + (d101 - d001) * fx, d = d011 + (d111 - d011) * fx;
        float e = a + (b - a) * fy, f = c + (d - c) * fy;
        return e + (f - e) * fz;
    }

    /// <summary>Solid cells in a box, counted cell by cell (independent of any bookkeeping).</summary>
    public int CountSolid(CellBox box)
    {
        int count = 0;
        for (int y = box.Min.Y; y < box.Max.Y; y++)
            for (int z = box.Min.Z; z < box.Max.Z; z++)
                for (int x = box.Min.X; x < box.Max.X; x++)
                    if (IsSolid(new Int3(x, y, z))) count++;
        return count;
    }

    /// <summary>A static water plane only covers columns whose natural surface is below it (rule 6).</summary>
    public bool IsWaterColumn(int x, int z) => Source.SurfaceHeight(x + 0.5f, z + 0.5f) < Source.WaterLevel;

    /// <summary>
    /// Whether a cell is under water. Decided by the base terrain alone, so digging never
    /// moves water: a pit beside a lake stays dry.
    /// </summary>
    public bool IsWater(Int3 cell) =>
        cell.Y + 0.5f < Source.WaterLevel && IsWaterColumn(cell.X, cell.Z) && !IsSolid(cell);

    // ---- Rules ------------------------------------------------------------------------

    /// <summary>
    /// Whether a cell may be removed (rules 2 and 3). Every kind of edit has the same rule: any
    /// ground in the world but bedrock, under and around structures and POIs included.
    /// </summary>
    public Verdict Check(Int3 cell, EditKind kind)
    {
        if (!InWorld(cell)) return Verdict.OutsideWorld;
        CellMaterial m = MaterialAt(cell);
        if (m == CellMaterial.Air) return Verdict.AlreadyAir;
        if (m == CellMaterial.Bedrock) return Verdict.Bedrock;
        return Verdict.Removable;
    }

    // ---- Structures in the ground (rule 4) ---------------------------------------------

    /// <summary>
    /// Whether a cell's centre lies inside a registered structure volume. A removed cell that is
    /// covered is drawn as the ground it was, cut off flush at the structure's faces, for as long
    /// as the structure stands (unless dug ground beside it is open to it).
    /// </summary>
    public bool IsCovered(Int3 cell)
    {
        if (!_volumeSnapshots.TryGetValue(ChunkOf(cell), out StructureVolume[] near)) return false;
        foreach (StructureVolume v in near)
            if (v.ContainsCell(cell)) return true;
        return false;
    }

    /// <summary>
    /// Registers a structure's volume, or moves it if the key is already known. Nothing is
    /// removed (see <see cref="ClearForStructure(VolumeKey, StructureVolume)"/> for building);
    /// this is for re-registering a standing structure after a load, or a copy across a seam.
    /// Returns every chunk whose mesh it changes.
    /// </summary>
    public HashSet<Int3> AddVolume(VolumeKey key, StructureVolume volume)
    {
        var dirty = new HashSet<Int3>();
        if (_volumes.TryGetValue(key, out StructureVolume old))
        {
            if (old.Equals(volume)) return dirty;
            Unindex(key, old, dirty);
        }
        _volumes[key] = volume;
        Index(key, volume, dirty);
        return dirty;
    }

    /// <summary>
    /// Forgets a volume: a block taken down, or a structure gone. Terrain is remove-only, so the
    /// cells it cleared stay cleared, and from now on are drawn as the dug hole they are.
    /// Returns every chunk whose mesh changes.
    /// </summary>
    public HashSet<Int3> RemoveVolume(VolumeKey key)
    {
        var dirty = new HashSet<Int3>();
        for (int n = 0; n < VolumeKey.MaxCopies; n++)
            if (_volumes.Remove(key.AsCopy(n), out StructureVolume old)) Unindex(key.AsCopy(n), old, dirty);
        return dirty;
    }

    /// <summary>Forgets every volume one owner registered (a whole structure taken down or flown off).</summary>
    public HashSet<Int3> RemoveVolumes(int owner)
    {
        var dirty = new HashSet<Int3>();
        foreach (VolumeKey key in _volumes.Keys.Where(k => k.Owner == owner).ToList())
            dirty.UnionWith(RemoveVolume(key));
        return dirty;
    }

    /// <summary>
    /// The one call for a structure reaching into the ground, any shape of block at any angle:
    /// removes every cell whose centre lies inside the volume (only bedrock refuses), and
    /// registers the volume so the ground around it meets its faces flush, with no sag and no gap.
    /// The result's dirty chunks cover both. Calling it again for a standing block is harmless.
    /// </summary>
    public EditResult ClearForStructure(VolumeKey key, StructureVolume volume)
    {
        HashSet<Int3> dirty = AddVolume(key, volume);
        EditResult result = Remove(volume.CellsInside(), EditKind.Build);
        result.DirtyChunks.UnionWith(dirty);
        return result;
    }

    /// <summary>Several parts of a structure at once (a whole base, or a blueprint), as one edit.</summary>
    public EditResult ClearForStructure(IEnumerable<(VolumeKey key, StructureVolume volume)> parts)
    {
        var dirty = new HashSet<Int3>();
        var cells = new List<Int3>();
        foreach ((VolumeKey key, StructureVolume volume) in parts)
        {
            dirty.UnionWith(AddVolume(key, volume));
            cells.AddRange(volume.CellsInside());
        }
        EditResult result = Remove(cells, EditKind.Build);
        result.DirtyChunks.UnionWith(dirty);
        return result;
    }

    /// <summary>The chunks a volume can change the mesh of: those within <see cref="VolumeReach"/> cells of it.</summary>
    public static IEnumerable<Int3> ChunksNear(StructureVolume volume)
    {
        CellBox b = volume.CellBounds;
        Int3 lo = ChunkOf(b.Min - Int3.One * VolumeReach), hi = ChunkOf(b.Max - Int3.One + Int3.One * VolumeReach);
        for (int y = lo.Y; y <= hi.Y; y++)
            for (int z = lo.Z; z <= hi.Z; z++)
                for (int x = lo.X; x <= hi.X; x++)
                    yield return new Int3(x, y, z);
    }

    void Index(VolumeKey key, StructureVolume volume, HashSet<Int3> dirty)
    {
        foreach (Int3 c in ChunksNear(volume))
        {
            if (!_volumeKeys.TryGetValue(c, out SortedSet<VolumeKey> keys)) _volumeKeys[c] = keys = new SortedSet<VolumeKey>();
            keys.Add(key);
            Snapshot(c, keys);
            dirty.Add(c);
        }
    }

    void Unindex(VolumeKey key, StructureVolume volume, HashSet<Int3> dirty)
    {
        foreach (Int3 c in ChunksNear(volume))
        {
            if (!_volumeKeys.TryGetValue(c, out SortedSet<VolumeKey> keys) || !keys.Remove(key)) continue;
            if (keys.Count == 0)
            {
                _volumeKeys.Remove(c);
                _volumeSnapshots.Remove(c);
            }
            else Snapshot(c, keys);
            dirty.Add(c);
        }
    }

    /// <summary>A fresh array, never changed afterwards: workers may still hold the old one.</summary>
    void Snapshot(Int3 chunk, SortedSet<VolumeKey> keys)
    {
        var array = new StructureVolume[keys.Count];
        int i = 0;
        foreach (VolumeKey k in keys) array[i++] = _volumes[k];
        _volumeSnapshots[chunk] = array;
    }

    // ---- Edits ------------------------------------------------------------------------

    /// <summary>The survival tool: a small grid-aligned ball of cells around one cell.</summary>
    public EditResult Dig(Int3 center, float radius) =>
        Remove(SphereCells(center, Math.Clamp(radius, 0f, MaxToolRadius)), EditKind.Tool);

    /// <summary>Removes specific cells, under the same rules as the tool.</summary>
    public EditResult DigCells(IEnumerable<Int3> cells) => Remove(cells, EditKind.Tool);

    /// <summary>
    /// A crash or liftoff (rule 10): removes cells within the radius, nearest first. The radius
    /// and the number of cells are both capped, and bedrock still holds. It cuts right up to and
    /// under structures and POIs; they stay where they are.
    /// </summary>
    public EditResult Crater(Vector3 center, float radius)
    {
        radius = Math.Clamp(radius, 0.5f, MaxCraterRadius);
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
        return Remove(cells.Take(MaxCraterCells).Select(p => p.cell), EditKind.Crater);
    }

    /// <summary>
    /// Places an axis-aligned building block (rule 4) through <see cref="ClearForStructure(VolumeKey, StructureVolume)"/>:
    /// the cells inside it are cleared and the ground around meets it flush. Refused if it would
    /// overlap another block or cut bedrock. POI ground takes blocks like any other.
    /// </summary>
    public PlaceResult PlaceStructure(CellBox box)
    {
        Int3 s = box.Size;
        if (s.X < 1 || s.Y < 1 || s.Z < 1 || s.X > MaxStructureSize || s.Y > MaxStructureSize || s.Z > MaxStructureSize)
            return new PlaceResult { Verdict = PlaceVerdict.InvalidSize };
        if (!InWorld(box.Min) || !InWorld(box.Max - Int3.One))
            return new PlaceResult { Verdict = PlaceVerdict.OutsideWorld };
        foreach (Structure other in _structures)
            if (other.Box.Intersects(box))
                return new PlaceResult { Verdict = PlaceVerdict.Overlaps };
        foreach (Int3 cell in BoxCells(box))
            if (Check(cell, EditKind.Build) == Verdict.Bedrock)
                return new PlaceResult { Verdict = PlaceVerdict.Bedrock };

        var structure = new Structure(_nextStructureId++, box);
        _structures.Add(structure);
        EditResult cleared = ClearForStructure(VolumeKey.ForBlock(structure.Id), StructureVolume.Box(box));
        return new PlaceResult { Verdict = PlaceVerdict.Placed, Structure = structure, Cleared = cleared };
    }

    EditResult Remove(IEnumerable<Int3> cells, EditKind kind)
    {
        var result = new EditResult(kind);
        var copied = new HashSet<Int3>();
        foreach (Int3 cell in cells)
        {
            Verdict v = Check(cell, kind);
            switch (v)
            {
                case Verdict.AlreadyAir: result.AlreadyAir++; continue;
                case Verdict.Bedrock: result.Bedrock++; continue;
                case Verdict.OutsideWorld: result.Outside++; continue;
            }
            result.Removed.Add(new RemovedCell(cell, MaterialAt(cell)));
            SetRemoved(cell, copied);
            AddDirty(cell, result.DirtyChunks);
        }
        return result;
    }

    /// <summary>
    /// Copy-on-write: the first change to a chunk within one operation copies its mask, so
    /// masks already handed to mesh workers never change under them.
    /// </summary>
    void SetRemoved(Int3 cell, HashSet<Int3> copied)
    {
        Int3 c = ChunkOf(cell);
        int i = LocalIndex(cell);
        if (!_removed.TryGetValue(c, out ulong[] mask))
        {
            _removed[c] = mask = new ulong[MaskWords];
            copied.Add(c);
        }
        else if (copied.Add(c))
        {
            _removed[c] = mask = (ulong[])mask.Clone();
        }
        mask[i >> 6] |= 1ul << (i & 63);
        RemovedCount++;
    }

    /// <summary>
    /// Marks every chunk whose mesh depends on this cell: its own, plus the neighbours that
    /// read it as one-cell padding when it lies on a chunk face, edge or corner.
    /// </summary>
    public static void AddDirty(Int3 cell, HashSet<Int3> dirty)
    {
        Int3 c = ChunkOf(cell);
        int lx = cell.X & LocalMask, ly = cell.Y & LocalMask, lz = cell.Z & LocalMask;
        int x0 = lx == 0 ? -1 : 0, x1 = lx == LocalMask ? 1 : 0;
        int y0 = ly == 0 ? -1 : 0, y1 = ly == LocalMask ? 1 : 0;
        int z0 = lz == 0 ? -1 : 0, z1 = lz == LocalMask ? 1 : 0;
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    dirty.Add(new Int3(c.X + x, c.Y + y, c.Z + z));
    }

    public static IEnumerable<Int3> SphereCells(Int3 center, float radius)
    {
        int r = (int)MathF.Floor(radius);
        float r2 = radius * radius + 1e-4f;
        for (int y = -r; y <= r; y++)
            for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y + z * z <= r2)
                        yield return new Int3(center.X + x, center.Y + y, center.Z + z);
    }

    public static IEnumerable<Int3> BoxCells(CellBox box)
    {
        for (int y = box.Min.Y; y < box.Max.Y; y++)
            for (int z = box.Min.Z; z < box.Max.Z; z++)
                for (int x = box.Min.X; x < box.Max.X; x++)
                    yield return new Int3(x, y, z);
    }

    // ---- Diffs, replication and saves (rules 8 and 11) ---------------------------------

    /// <summary>
    /// Applies removals made elsewhere (another player, or a save). Removal sets only grow,
    /// so diffs can arrive in any order, twice, or late, and every player still ends up with
    /// the same holes. Cells that could never be removed (bedrock, base air, outside the world)
    /// are refused and reported.
    /// </summary>
    public ApplyReport ApplyDiff(EditLog diff)
    {
        var report = new ApplyReport();
        var copied = new HashSet<Int3>();
        foreach (Int3 cell in diff.Cells())
        {
            CellMaterial baseMaterial = InWorld(cell) ? BaseMaterialAt(cell) : CellMaterial.Bedrock;
            if (baseMaterial == CellMaterial.Air || baseMaterial == CellMaterial.Bedrock)
            {
                report.Invalid.Add(cell);
                continue;
            }
            if (IsRemoved(cell))
            {
                report.AlreadyRemoved++;
                continue;
            }
            SetRemoved(cell, copied);
            AddDirty(cell, report.DirtyChunks);
            report.Applied++;
        }
        return report;
    }

    /// <summary>
    /// Re-creates a saved or replicated structure, and registers its volume. Its terrain clearing
    /// travels in the edit log. Returns the chunks whose meshes its volume changes.
    /// </summary>
    public HashSet<Int3> RestoreStructure(Structure structure)
    {
        _structures.Add(structure);
        _nextStructureId = Math.Max(_nextStructureId, structure.Id + 1);
        return AddVolume(VolumeKey.ForBlock(structure.Id), StructureVolume.Box(structure.Box));
    }

    /// <summary>Every edit, as a canonical log (sorted chunks, sorted cells).</summary>
    public EditLog ExportEdits()
    {
        var log = new EditLog();
        foreach ((Int3 chunk, ulong[] mask) in _removed) log.SetChunk(chunk, mask);
        return log;
    }

    /// <summary>A fingerprint of everything players changed: removed cells and structures.</summary>
    public ulong EditStateHash()
    {
        var h = Fnv64.Create();
        foreach (Int3 chunk in _removed.Keys.OrderBy(c => c))
        {
            h.Add(chunk);
            foreach (ulong word in _removed[chunk]) h.Add(word);
        }
        h.Add(_structures.Count);
        foreach (Structure s in _structures.OrderBy(s => s.Id))
        {
            h.Add(s.Id);
            h.Add(s.Box.Min);
            h.Add(s.Box.Max);
        }
        return h.Value;
    }

    /// <summary>A fingerprint of one chunk's final cells (base plus edits).</summary>
    public ulong ChunkHash(Int3 chunk)
    {
        Span<byte> cells = stackalloc byte[Volume];
        BaseChunk b = Base.Get(chunk);
        _removed.TryGetValue(chunk, out ulong[] mask);
        for (int i = 0; i < Volume; i++)
            cells[i] = mask != null && Bit(mask, i) ? (byte)CellMaterial.Air : (byte)b.MaterialAt(i);
        var h = Fnv64.Create();
        h.Add(chunk);
        h.Add(cells);
        return h.Value;
    }

    /// <summary>
    /// Replaces all edits and structures with another store's (a loaded save), and returns the
    /// chunks whose meshes changed. The other store must share this one's base terrain. Volumes
    /// registered by callers (vessels and bases the terrain doesn't save) are kept: they belong
    /// to whatever still stands, and the caller re-registers or removes them as it reloads its own.
    /// </summary>
    public HashSet<Int3> ReplaceStateWith(ChunkStore other)
    {
        if (other.Base != Base) throw new ArgumentException("Stores must share a base cache.");
        var dirty = new HashSet<Int3>();
        foreach (Int3 chunk in _removed.Keys.Union(other._removed.Keys))
        {
            _removed.TryGetValue(chunk, out ulong[] a);
            other._removed.TryGetValue(chunk, out ulong[] b);
            for (int w = 0; w < MaskWords; w++)
            {
                ulong diff = (a?[w] ?? 0ul) ^ (b?[w] ?? 0ul);
                while (diff != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(diff);
                    diff &= diff - 1;
                    AddDirty(CellAt(chunk, w * 64 + bit), dirty);
                }
            }
        }

        _removed.Clear();
        foreach ((Int3 chunk, ulong[] mask) in other._removed) _removed[chunk] = (ulong[])mask.Clone();
        RemovedCount = other.RemovedCount;

        // Blocks: forget the ones the save doesn't have (or has elsewhere), then restore its own.
        // A block in both, unchanged, keeps its volume, so its chunks aren't remeshed for nothing.
        var kept = new HashSet<int>();
        foreach (Structure s in other._structures)
            if (_structures.Contains(s)) kept.Add(s.Id);
        foreach (Structure s in _structures)
            if (!kept.Contains(s.Id)) dirty.UnionWith(RemoveVolume(VolumeKey.ForBlock(s.Id)));
        _structures.Clear();
        foreach (Structure s in other._structures) dirty.UnionWith(RestoreStructure(s));
        _nextStructureId = other._nextStructureId;
        return dirty;
    }

    // ---- Meshing ----------------------------------------------------------------------

    /// <summary>
    /// Snapshots what a mesh worker needs for one chunk. Only references are copied: masks
    /// and volume arrays are never modified once published, so the worker sees exactly this
    /// moment's terrain.
    /// </summary>
    public ChunkNeighborhood CaptureNeighborhood(Int3 chunk)
    {
        var masks = new ulong[27][];
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    _removed.TryGetValue(new Int3(chunk.X + dx, chunk.Y + dy, chunk.Z + dz), out ulong[] mask);
                    masks[ChunkNeighborhood.NeighborIndex(dx, dy, dz)] = mask;
                }
        _volumeSnapshots.TryGetValue(chunk, out StructureVolume[] volumes);
        return new ChunkNeighborhood(chunk, Base, masks, volumes);
    }

    // ---- Lookups for gameplay ---------------------------------------------------------

    /// <summary>
    /// Steps through cells along a ray (Amanatides–Woo) and returns the first solid one, with
    /// the face it was entered through. Works on the grid itself, so it never depends on a
    /// collision mesh that is still being rebuilt.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out Int3 hit, out Int3 face, out float distance)
    {
        hit = Int3.Floor(origin);
        face = Int3.Zero;
        distance = 0f;
        direction = Vector3.Normalize(direction);

        int stepX = Math.Sign(direction.X), stepY = Math.Sign(direction.Y), stepZ = Math.Sign(direction.Z);
        float tDeltaX = stepX != 0 ? MathF.Abs(1f / direction.X) : float.MaxValue;
        float tDeltaY = stepY != 0 ? MathF.Abs(1f / direction.Y) : float.MaxValue;
        float tDeltaZ = stepZ != 0 ? MathF.Abs(1f / direction.Z) : float.MaxValue;
        float tMaxX = stepX != 0 ? ((stepX > 0 ? hit.X + 1 - origin.X : origin.X - hit.X) * tDeltaX) : float.MaxValue;
        float tMaxY = stepY != 0 ? ((stepY > 0 ? hit.Y + 1 - origin.Y : origin.Y - hit.Y) * tDeltaY) : float.MaxValue;
        float tMaxZ = stepZ != 0 ? ((stepZ > 0 ? hit.Z + 1 - origin.Z : origin.Z - hit.Z) * tDeltaZ) : float.MaxValue;

        if (IsSolid(hit)) return true;
        while (true)
        {
            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                distance = tMaxX;
                hit = new Int3(hit.X + stepX, hit.Y, hit.Z);
                face = new Int3(-stepX, 0, 0);
                tMaxX += tDeltaX;
            }
            else if (tMaxY < tMaxZ)
            {
                distance = tMaxY;
                hit = new Int3(hit.X, hit.Y + stepY, hit.Z);
                face = new Int3(0, -stepY, 0);
                tMaxY += tDeltaY;
            }
            else
            {
                distance = tMaxZ;
                hit = new Int3(hit.X, hit.Y, hit.Z + stepZ);
                face = new Int3(0, 0, -stepZ);
                tMaxZ += tDeltaZ;
            }
            if (distance > maxDistance) return false;
            if (IsSolid(hit)) return true;
        }
    }

    /// <summary>Remaining (undug) cells of one material within a radius, nearest first.</summary>
    public List<Int3> FindCells(CellMaterial material, Vector3 center, float radius, int max)
    {
        var found = new List<(float d2, Int3 cell)>();
        Int3 lo = ChunkOf(Int3.Floor(center - new Vector3(radius))), hi = ChunkOf(Int3.Floor(center + new Vector3(radius)));
        float r2 = radius * radius;
        for (int cy = lo.Y; cy <= hi.Y; cy++)
            for (int cz = lo.Z; cz <= hi.Z; cz++)
                for (int cx = lo.X; cx <= hi.X; cx++)
                {
                    var chunk = new Int3(cx, cy, cz);
                    BaseChunk b = Base.Get(chunk);
                    if (b.IsUniform && b.UniformMaterial != material) continue;
                    _removed.TryGetValue(chunk, out ulong[] mask);
                    for (int i = 0; i < Volume; i++)
                    {
                        if (b.MaterialAt(i) != material || (mask != null && Bit(mask, i))) continue;
                        Int3 cell = CellAt(chunk, i);
                        float d2 = Vector3.DistanceSquared(cell.Center, center);
                        if (d2 <= r2) found.Add((d2, cell));
                    }
                }
        found.Sort((a, b) => a.d2 != b.d2 ? a.d2.CompareTo(b.d2) : a.cell.CompareTo(b.cell));
        return found.Take(max).Select(f => f.cell).ToList();
    }

    /// <summary>The highest solid cell in a column, or WorldCells.Min.Y - 1 if there is none.</summary>
    public int TopSolid(int x, int z)
    {
        for (int y = WorldCells.Max.Y - 1; y >= WorldCells.Min.Y; y--)
            if (IsSolid(new Int3(x, y, z))) return y;
        return WorldCells.Min.Y - 1;
    }
}
