using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Terrain;
using PhysicsSandbox.Bridges;
using TerrainDigging.Terrain;
using N = System.Numerics;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace CapitalBase.World;

/// <summary>Why a piece of ground was removed, for the log.</summary>
public enum Cut { Dig, Drill, Build, LiftOff, Crater }

/// <summary>One removal, as the session's terrain log keeps it.</summary>
public sealed record GroundEdit(Cut Why, string By, int Removed, int Refused, Dictionary<CellMaterial, int> Materials);

/// <summary>
/// Every change to the planet's ground goes through here, and so through planet-surface's
/// <see cref="PlanetTerrain"/> and terrain-digging's rules (remove-only, bedrock and POI anchors
/// refuse, every copy across a seam). Each removal is logged, the patch is told which chunks to
/// rebuild, and the physics world wakes whatever stood nearby. It is also the physics module's
/// crater editor.
///
/// It keeps the footings of bases too: the ground under each landing foot of an anchored base
/// refuses the dig tool, so a base never looks undermined.
/// </summary>
public sealed class Ground : ITerrainEditor
{
    public readonly PlanetTerrain Terrain;
    public readonly TerrainPatch Patch;

    /// <summary>Set once the physics world exists, so edits wake and check bodies nearby.</summary>
    public PhysicsWorld World { get; set; }

    /// <summary>Every removal made this session, in order.</summary>
    public readonly List<GroundEdit> History = new();

    public event Action<PlanetEdit, Cut> Edited;

    readonly Dictionary<string, HashSet<TInt3>> _footings = new(StringComparer.Ordinal);

    public Ground(PlanetTerrain terrain, TerrainPatch patch)
    {
        Terrain = terrain;
        Patch = patch;
    }

    public ChunkStore Store => Terrain.Store;

    // ---- Footings --------------------------------------------------------------------------

    public void SetFootings(string owner, IEnumerable<TInt3> cells) => _footings[owner] = new HashSet<TInt3>(cells);

    public void ClearFootings(string owner) => _footings.Remove(owner);

    public IReadOnlyCollection<TInt3> FootingsOf(string owner) => _footings.TryGetValue(owner, out var set) ? set : Array.Empty<TInt3>();

    public bool IsFooting(TInt3 cell)
    {
        foreach (HashSet<TInt3> set in _footings.Values)
            if (set.Contains(cell)) return true;
        return false;
    }

    // ---- Edits -----------------------------------------------------------------------------

    /// <summary>The dig tool: a ball of cells, footings left alone.</summary>
    public PlanetEdit Dig(TInt3 center, float radius, string by, Cut why = Cut.Dig)
    {
        IEnumerable<TInt3> cells = ChunkStore.SphereCells(center, Math.Clamp(radius, 0f, ChunkStore.MaxToolRadius));
        return Remove(cells, why, by, keepFootings: true);
    }

    /// <summary>Removes particular cells under the tool's rules (lift-off and building clear their own cells this way).</summary>
    public PlanetEdit Remove(IEnumerable<TInt3> cells, Cut why, string by, bool keepFootings)
    {
        int footing = 0;
        var list = new List<TInt3>();
        foreach (TInt3 c in cells)
        {
            if (keepFootings && IsFooting(c) && Store.IsSolid(c)) footing++;
            else list.Add(c);
        }
        PlanetEdit edit = Terrain.DigCells(list);
        edit.Anchored += footing;
        Commit(edit, why, by, notify: true);
        return edit;
    }

    /// <summary>A crash: a capped bowl of cells, through the same log (the physics world notifies for its own craters).</summary>
    public CraterResult Crater(N.Vector3 center, float radius)
    {
        PlanetEdit edit = Terrain.Crater(center, radius);
        Region changed = Commit(edit, Cut.Crater, "impact", notify: false);
        return new CraterResult(edit.Removed.Count, edit.Bedrock + edit.Anchored + edit.Outside, changed);
    }

    /// <summary>What the terrain says about removing one cell (bedrock, POI anchors); footings are checked separately.</summary>
    public Verdict Check(TInt3 cell) => Terrain.Check(cell, EditKind.Tool);

    Region Commit(PlanetEdit edit, Cut why, string by, bool notify)
    {
        var materials = new Dictionary<CellMaterial, int>();
        foreach (RemovedCell r in edit.Removed) materials[r.Material] = materials.GetValueOrDefault(r.Material) + 1;
        History.Add(new GroundEdit(why, by, edit.Removed.Count, edit.Bedrock + edit.Anchored + edit.Outside, materials));
        if (edit.Removed.Count == 0) return default;
        Patch?.MarkDirty(edit.DirtyChunks);
        N.Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (RemovedCell r in edit.Removed)
        {
            N.Vector3 c = r.Cell.ToVector();
            min = N.Vector3.Min(min, c);
            max = N.Vector3.Max(max, c + N.Vector3.One);
        }
        Edited?.Invoke(edit, why);
        var region = new Region(min, max);
        if (notify) World?.NotifyChanged(region);
        return region;
    }

    /// <summary>Everything ever removed from this planet, as terrain-digging's canonical edit log.</summary>
    public EditLog Export() => Store.ExportEdits();

    /// <summary>Puts a saved edit log back: the store is rebuilt from the generator plus the log, and the patch remeshes what changed.</summary>
    public void Restore(EditLog log)
    {
        var fresh = new ChunkStore(Store.Base);
        fresh.ApplyDiff(log);
        HashSet<TInt3> dirty = Store.ReplaceStateWith(fresh);
        Patch?.MarkDirty(dirty);
        _footings.Clear();
    }

    public static string Describe(Dictionary<CellMaterial, int> materials) =>
        materials.Count == 0 ? "nothing" : string.Join(", ", materials.OrderBy(kv => kv.Key).Select(kv => $"{kv.Value} {kv.Key.Label().ToLowerInvariant()}"));
}

/// <summary>
/// Keeps bodies out of the ground (physics-sandbox's terrain field over the planet's store), except
/// structures that are pinned on purpose: an anchored base stands where it was built, partly in
/// the ground if its builder put it there, and a parked vessel rides its host.
/// </summary>
public sealed class GroundField : IStuckField
{
    readonly TerrainField _inner;
    readonly Func<Body, bool> _pinned;

    public GroundField(ChunkStore store, float ceiling, Func<Body, bool> pinned)
    {
        _inner = new TerrainField(store) { Ceiling = ceiling };
        _pinned = pinned;
    }

    public string Name => "ground";
    public Body Owner => null;
    public bool Applies(Body body) => !_pinned(body) && _inner.Applies(body);
    public bool IsInside(Body body, N.Vector3 feet, StuckShape shape) => _inner.IsInside(body, feet, shape);
    public bool Fits(Body body, N.Vector3 feet, StuckShape shape) => _inner.Fits(body, feet, shape);
    public bool IsSolid(Body body, N.Vector3 point) => _inner.IsSolid(body, point);
    public bool Supports(Body body, N.Vector3 feet) => _inner.Supports(body, feet);
    public float TopAt(Body body, float x, float z) => _inner.TopAt(body, x, z);
    public bool TryFindOpen(Body body, N.Vector3 feet, StuckShape shape, out N.Vector3 openFeet) => _inner.TryFindOpen(body, feet, shape, out openFeet);
}
