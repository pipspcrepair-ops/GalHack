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
///
/// The terrain only has collision inside its world box, so anything that leaves it (thrown
/// over the rim by a tornado, knocked off the edge) would fall forever. Off the edge or under
/// the floor counts as stuck too, and the rescue brings the body back to open ground inside.
/// Flown vessels may cross the edge in the air; only falling out of the bottom brings them back,
/// and so may anyone riding one (inside its cabin or out on its hull), or drifting above the
/// atmosphere, where nothing falls.
///
/// Riders out on a hull are checked too: their ship's cabin world holds no terrain, so a ship
/// flown low could scrape one through a hill. Riders inside the interior are the hull's to keep safe.
/// </summary>
public sealed class TerrainField : IStuckField
{
    readonly ChunkStore _store;

    /// <summary>Rescues land at least this far inside the world's edge, m.</summary>
    const float EdgeMargin = 20f;

    public TerrainField(ChunkStore store) => _store = store;

    /// <summary>The top of the atmosphere: above it nothing falls, so leaving the world's column there is no danger.</summary>
    public float Ceiling = float.PositiveInfinity;

    public string Name => "terrain";

    public Body Owner => null;

    public bool Applies(Body body) => body.Frame == null || body.OnOuterHull;

    static BodyShape Of(StuckShape s) => new(s.Height, s.Radius);

    static bool InColumn(N.Vector3 p) =>
        p.X >= ChunkStore.WorldCells.Min.X && p.X < ChunkStore.WorldCells.Max.X && p.Z >= ChunkStore.WorldCells.Min.Z && p.Z < ChunkStore.WorldCells.Max.Z;

    static bool BelowWorld(N.Vector3 p) => p.Y < ChunkStore.WorldCells.Min.Y;

    static bool IsStructure(Body b) => b.Kind is BodyKind.Vessel or BodyKind.Wreck;

    /// <summary>Off the edge, this body would fall forever: it isn't riding anything and it is under the atmosphere's top.</summary>
    bool Falls(Body body, N.Vector3 p) => body.Frame == null && p.Y < Ceiling;

    /// <summary>
    /// A rigid box is stuck as soon as its centre is below the ground surface: from there the
    /// engine cannot push it back out (the mesh is a thin shell), it sinks. Resting on the ground
    /// its centre is half its size above it, so this never fires on a crate that is merely lying there.
    /// </summary>
    public const float FreeBodyDepth = 0f;

    public bool IsInside(Body body, N.Vector3 feet, StuckShape shape)
    {
        // Off the world's edge only matters to something falling: a rider on a hull is carried.
        if ((!InColumn(feet) && Falls(body, feet)) || BelowWorld(feet)) return true;
        if (!InColumn(feet)) return false;
        if (body.Motion == GalHack.Physics.Motion.Free)
            return _store.SampleDensity(feet + N.Vector3.UnitY * shape.FeetBelowOrigin) > FreeBodyDepth;
        return StuckResolver.IsInsideTerrain(_store, feet, Of(shape));
    }

    public bool Fits(Body body, N.Vector3 feet, StuckShape shape) => InColumn(feet) && StuckResolver.Fits(_store, feet, Of(shape));

    public bool IsSolid(Body body, N.Vector3 point)
    {
        if (BelowWorld(point)) return true;
        if (!InColumn(point)) return !IsStructure(body) && Falls(body, point);
        return _store.SampleDensity(point) > StuckResolver.InsideThreshold;
    }

    public bool Supports(Body body, N.Vector3 feet) => _store.IsSolid(Int3.Floor(feet - N.Vector3.UnitY * 0.3f));

    public float TopAt(Body body, float x, float z)
    {
        if (!InColumn(new N.Vector3(x, 0f, z))) return float.NegativeInfinity;
        int top = _store.TopSolid((int)MathF.Floor(x), (int)MathF.Floor(z));
        return top < ChunkStore.WorldCells.Min.Y ? float.NegativeInfinity : top + 1f;
    }

    public bool TryFindOpen(Body body, N.Vector3 feet, StuckShape shape, out N.Vector3 openFeet)
    {
        // Off the edge or under the floor: come back inside, onto the ground in that direction.
        if (!InColumn(feet) || BelowWorld(feet))
        {
            float x = Math.Clamp(feet.X, ChunkStore.WorldCells.Min.X + EdgeMargin, ChunkStore.WorldCells.Max.X - EdgeMargin);
            float z = Math.Clamp(feet.Z, ChunkStore.WorldCells.Min.Z + EdgeMargin, ChunkStore.WorldCells.Max.Z - EdgeMargin);
            feet = new N.Vector3(x, _store.TopSolid((int)MathF.Floor(x), (int)MathF.Floor(z)) + 1.1f, z);
        }
        OpenSpace space = StuckResolver.FindOpenSpace(_store, feet, Of(shape));
        openFeet = space.Feet;
        return true;
    }
}
