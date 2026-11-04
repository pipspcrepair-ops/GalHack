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
/// Main thread only, apart from the immutable data it hands out.
/// </summary>
public sealed class ChunkStore
{
    public const int Size = 16, Shift = 4, LocalMask = Size - 1;
    public const int Volume = Size * Size * Size;
    public const int MaskWords = Volume / 64;

    /// <summary>The playable world, in chunks. Edits outside it are refused.</summary>
    public static readonly CellBox WorldChunks = new(new Int3(-8, -3, -8), new Int3(8, 5, 8));
    public static readonly CellBox WorldCells = new(WorldChunks.Min * Size, WorldChunks.Max * Size);

    /// <summary>
    /// Densities are clamped to ±this. Removed cells are fully open (−Limit), so a dug face sits
    /// exactly on the cell boundary next to deep ground (+Limit), and the range is wide enough
    /// that smooth slopes never hit the clamp where the mesh needs them.
    /// </summary>
    public const float DensityLimit = 2f;

    public const float MaxToolRadius = 2f;
    public const float MaxCraterRadius = 5f;
    public const int MaxCraterCells = 520;

    // Ground this far around (and below) a structure or POI is anchored.
    public const int AnchorSide = 3, AnchorBelow = 6, AnchorAbove = 3;
    public const int MaxStructureSize = 8;

    public BaseCache Base { get; }
    public ITerrainSource Source => Base.Source;

    readonly Dictionary<Int3, ulong[]> _removed = new();
    readonly List<AnchorZone> _anchors = new();
    readonly Dictionary<Int3, List<AnchorZone>> _anchorIndex = new();
    readonly List<Structure> _structures = new();
    int _nextStructureId = 1;

    public int RemovedCount { get; private set; }
    public IReadOnlyList<Structure> Structures => _structures;
    public IReadOnlyList<AnchorZone> Anchors => _anchors;
    public IEnumerable<Int3> EditedChunks => _removed.Keys;

    public ChunkStore(ITerrainSource source) : this(new BaseCache(source)) { }

    /// <summary>Shares an existing base cache: base chunks are immutable, so stores can share them.</summary>
    public ChunkStore(BaseCache baseCache)
    {
        Base = baseCache;
        AddPoiAnchors();
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

    // ---- Anchors (rule 3) -------------------------------------------------------------

    public bool IsAnchored(Int3 cell) => AnchoredAt(cell, poiOnly: false);

    bool AnchoredAt(Int3 cell, bool poiOnly)
    {
        if (!_anchorIndex.TryGetValue(ChunkOf(cell), out List<AnchorZone> zones)) return false;
        foreach (AnchorZone zone in zones)
            if ((!poiOnly || zone.IsPoi) && zone.Box.Contains(cell))
                return true;
        return false;
    }

    void AddAnchor(AnchorZone zone)
    {
        _anchors.Add(zone);
        Int3 lo = ChunkOf(zone.Box.Min), hi = ChunkOf(zone.Box.Max - Int3.One);
        for (int y = lo.Y; y <= hi.Y; y++)
            for (int z = lo.Z; z <= hi.Z; z++)
                for (int x = lo.X; x <= hi.X; x++)
                {
                    var c = new Int3(x, y, z);
                    if (!_anchorIndex.TryGetValue(c, out List<AnchorZone> list)) _anchorIndex[c] = list = new List<AnchorZone>();
                    list.Add(zone);
                }
    }

    void AddPoiAnchors()
    {
        foreach (PoiSite poi in Source.PointsOfInterest)
            AddAnchor(new AnchorZone(poi.Footprint.Grow(AnchorSide, AnchorBelow, AnchorAbove), true, 0));
    }

    public static CellBox AnchorBoxFor(CellBox structure) => structure.Grow(AnchorSide, AnchorBelow, AnchorAbove);

    // ---- Rules ------------------------------------------------------------------------

    /// <summary>Whether a cell may be removed by this kind of edit (rules 2, 3 and 4).</summary>
    public Verdict Check(Int3 cell, EditKind kind)
    {
        if (!InWorld(cell)) return Verdict.OutsideWorld;
        CellMaterial m = MaterialAt(cell);
        if (m == CellMaterial.Air) return Verdict.AlreadyAir;
        if (m == CellMaterial.Bedrock) return Verdict.Bedrock;
        if (AnchoredAt(cell, poiOnly: kind == EditKind.Build)) return Verdict.Anchored;
        return Verdict.Removable;
    }

    // ---- Edits ------------------------------------------------------------------------

    /// <summary>The survival tool: a small grid-aligned ball of cells around one cell.</summary>
    public EditResult Dig(Int3 center, float radius) =>
        Remove(SphereCells(center, Math.Clamp(radius, 0f, MaxToolRadius)), EditKind.Tool);

    /// <summary>Removes specific cells, under the same rules as the tool.</summary>
    public EditResult DigCells(IEnumerable<Int3> cells) => Remove(cells, EditKind.Tool);

    /// <summary>
    /// A crash or liftoff (rule 10): removes cells within the radius, nearest first. The radius
    /// and the number of cells are both capped, and bedrock and anchored ground still hold.
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
    /// Places a building block (rules 3 and 4): clears all terrain inside it, then anchors the
    /// ground around it. Refused if it would overlap another block, bedrock or a POI's ground.
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
        {
            Verdict v = Check(cell, EditKind.Build);
            if (v == Verdict.Bedrock) return new PlaceResult { Verdict = PlaceVerdict.Bedrock };
            if (v == Verdict.Anchored) return new PlaceResult { Verdict = PlaceVerdict.AnchoredByPoi };
        }

        EditResult cleared = Remove(BoxCells(box), EditKind.Build);
        var structure = new Structure(_nextStructureId++, box);
        _structures.Add(structure);
        AddAnchor(new AnchorZone(AnchorBoxFor(box), false, structure.Id));
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
                case Verdict.Anchored: result.Anchored++; continue;
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
    /// the same holes. Anchors aren't re-checked: the authority already did, and a diff must
    /// never be dropped because a block appeared here a moment earlier. Cells that could never
    /// be removed (bedrock, base air, outside the world) are refused and reported.
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

    /// <summary>Re-creates a saved or replicated structure. Its terrain clearing travels in the edit log.</summary>
    public void RestoreStructure(Structure structure)
    {
        _structures.Add(structure);
        AddAnchor(new AnchorZone(AnchorBoxFor(structure.Box), false, structure.Id));
        _nextStructureId = Math.Max(_nextStructureId, structure.Id + 1);
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
    /// chunks whose meshes changed. The other store must share this one's base terrain.
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

        _structures.Clear();
        _anchors.Clear();
        _anchorIndex.Clear();
        AddPoiAnchors();
        foreach (Structure s in other._structures) RestoreStructure(s);
        _nextStructureId = other._nextStructureId;
        return dirty;
    }

    // ---- Meshing ----------------------------------------------------------------------

    /// <summary>
    /// Snapshots what a mesh worker needs for one chunk. Only references are copied: masks
    /// are never modified once published, so the worker sees exactly this moment's terrain.
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
        return new ChunkNeighborhood(chunk, Base, masks);
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
