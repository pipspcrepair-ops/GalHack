using System;
using System.Collections.Generic;
using GalHack.Physics;
using TerrainDigging.Terrain;
using N = System.Numerics;

namespace PhysicsSandbox.Bridges;

/// <summary>
/// The bridge between terrain-digging's core and the shared physics module. Craters go through
/// the terrain's own edit API (remove-only, capped, bedrock and anchors respected) and every
/// edit's diff is merged into this session's edit log, exactly as it would be sent to other
/// players. The terrain view listens to <see cref="Edited"/> and rebuilds what changed.
/// </summary>
public sealed class TerrainBridge : ITerrainEditor
{
    public ChunkStore Store { get; }

    /// <summary>Every removal made through this bridge, as the canonical diff (rule 8).</summary>
    public EditLog Log { get; } = new();

    /// <summary>An edit went through: rebuild these chunks.</summary>
    public event Action<EditResult> Edited;

    /// <summary>Set once the physics world exists, so edits wake and check bodies nearby.</summary>
    public PhysicsWorld World { get; set; }

    public TerrainBridge(ChunkStore store) => Store = store;

    /// <summary>A crash: a capped bowl of cells, nearest the centre first (ChunkStore.Crater).</summary>
    public CraterResult Crater(N.Vector3 center, float radius)
    {
        EditResult result = Store.Crater(center, radius);
        Region changed = Commit(result, notify: false); // the physics world notifies for its own craters
        return new CraterResult(result.Removed.Count, result.Refused, changed);
    }

    /// <summary>The survival tool's dig, through the same log and notifications.</summary>
    public EditResult Dig(Int3 center, float radius)
    {
        EditResult result = Store.Dig(center, radius);
        Commit(result, notify: true);
        return result;
    }

    /// <summary>Removes particular cells (a trench), under the tool's rules.</summary>
    public EditResult DigCells(IEnumerable<Int3> cells)
    {
        EditResult result = Store.DigCells(cells);
        Commit(result, notify: true);
        return result;
    }

    Region Commit(EditResult result, bool notify)
    {
        if (result.Removed.Count == 0) return default;
        Log.Merge(result.ToDiff());
        N.Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (RemovedCell r in result.Removed)
        {
            N.Vector3 c = r.Cell.ToVector();
            min = N.Vector3.Min(min, c);
            max = N.Vector3.Max(max, c + N.Vector3.One);
        }
        Edited?.Invoke(result);
        var region = new Region(min, max);
        if (notify) World?.NotifyChanged(region);
        return region;
    }
}

/// <summary>
/// Keeps bodies out of the terrain, using terrain-digging's own stuck resolver and density
/// field (the same zero surface the mesh is drawn from, never the collision mesh).
/// </summary>
public sealed class TerrainField : IStuckField
{
    readonly ChunkStore _store;

    public TerrainField(ChunkStore store) => _store = store;

    public string Name => "terrain";

    public bool Applies(Body body) => body.Frame == null;

    static BodyShape Of(StuckShape s) => new(s.Height, s.Radius);

    public bool IsInside(Body body, N.Vector3 feet, StuckShape shape) => StuckResolver.IsInsideTerrain(_store, feet, Of(shape));

    public bool Fits(Body body, N.Vector3 feet, StuckShape shape) => StuckResolver.Fits(_store, feet, Of(shape));

    public bool IsSolid(Body body, N.Vector3 point) =>
        !ChunkStore.InWorld(Int3.Floor(point)) ? point.Y < ChunkStore.WorldCells.Min.Y : _store.SampleDensity(point) > StuckResolver.InsideThreshold;

    public bool Supports(Body body, N.Vector3 feet) => _store.IsSolid(Int3.Floor(feet - N.Vector3.UnitY * 0.3f));

    public float TopAt(Body body, float x, float z)
    {
        int top = _store.TopSolid((int)MathF.Floor(x), (int)MathF.Floor(z));
        return top < ChunkStore.WorldCells.Min.Y ? float.NegativeInfinity : top + 1f;
    }

    public bool TryFindOpen(Body body, N.Vector3 feet, StuckShape shape, out N.Vector3 openFeet)
    {
        OpenSpace space = StuckResolver.FindOpenSpace(_store, feet, Of(shape));
        openFeet = space.Feet;
        return !space.Fallback;
    }
}
