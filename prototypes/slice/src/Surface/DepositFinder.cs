using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using TerrainDigging.Terrain;

namespace Slice.Surface;

/// <summary>
/// The suit's ore scanner: the nearest deposit of each ore within 600 m (the planet generator
/// promises each common one within about 400 m of any walkable spot, and scatters the uncommon
/// ones so a walk finds them), at its ore body's centre, of
/// those the survival tool can reach (dry, and near the surface). A
/// deposit dug out is no longer shown: its label leaves the compass and the next nearest takes its
/// place. Asked often, recomputed a few times a second.
/// </summary>
public sealed class DepositFinder
{
    public static readonly DepositKind[] Kinds = DepositField.EveryPlanet().ToArray();

    /// <summary>The deepest an ore body's centre may lie under the ground and still be shown, m.</summary>
    public const float MaxDepth = 9f;

    /// <summary>How far round an ore body's centre its cells are counted, m.</summary>
    const int BodyReach = 6;

    readonly List<(string kind, Vector3 at)> _near = new();
    ulong _lastFrame;
    static readonly Dictionary<(int, int, int), (int edits, int left)> Remaining = new();

    /// <summary>The ground <see cref="Remaining"/> was counted in: another world's (a NEW GAME, a test's next world) starts it afresh, and it never holds the last one.</summary>
    static readonly System.WeakReference<object> CountedIn = new(null);

    public IReadOnlyList<(string kind, Vector3 at)> Near(SurfaceZone zone, Vector3 from)
    {
        ulong frame = Engine.GetPhysicsFrames();
        if (zone.Planet == null) _near.Clear(); // in orbit: no ground under you, no deposits
        if (zone.Planet == null || frame - _lastFrame < 20 && _near.Count > 0) return _near;
        _lastFrame = frame;
        _near.Clear();
        foreach (DepositKind kind in Kinds)
            if (Nearest(zone, kind, from, 600f) is Vector3 at) _near.Add((kind == DepositKind.Water ? "ice" : kind.ToString().ToLowerInvariant(), at));
        return _near;
    }

    /// <summary>The nearest deposit of a kind still holding ore (its ore body's centre, frame coordinates), or null.</summary>
    public static Vector3? Nearest(SurfaceZone zone, DepositKind kind, Vector3 from, float within)
    {
        var planet = zone.Planet;
        double x = Mathx.WrapX(from.X, planet.Width), z = Mathx.Wrap(from.Z, planet.Height);
        var candidates = new List<(float dist, Vector3 at, double ox, double oy, double oz)>();
        foreach (Deposit d in planet.Map.Deposits.InRect(x - within, z - within, x + within, z + within))
        {
            if (d.Kind != kind) continue;
            planet.Source.OreCentre(d, out double ox, out double oy, out double oz);
            Vector3 at = planet.NearestCopy(new Vector3((float)ox, (float)oy, (float)oz), from);
            float dist = new Vector2(at.X - from.X, at.Z - from.Z).Length();
            // Only what the survival tool can reach: dry, and its ore body near the surface (it can't cut deep rock).
            float ground = zone.GroundAt(at.X, at.Z).Y;
            if (dist < within && ground > 0.5f && ground - at.Y <= MaxDepth) candidates.Add((dist, at, ox, oy, oz));
        }
        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
        // The nearest one that still holds ore (only the near ones are ever counted).
        foreach (var c in candidates)
            if (!Depleted(zone, kind, c.ox, c.oy, c.oz)) return c.at;
        return null;
    }

    /// <summary>
    /// True once a deposit's ore body has been dug out: no ore cell of its kind left round its
    /// centre. Counted from the voxel store, and only again after the ground has been edited.
    /// </summary>
    public static bool Depleted(SurfaceZone zone, DepositKind kind, double ox, double oy, double oz)
    {
        var store = zone.Planet.Terrain.Store;
        if (!CountedIn.TryGetTarget(out object counted) || !ReferenceEquals(counted, store))
        {
            Remaining.Clear();
            CountedIn.SetTarget(store);
        }
        int cx = (int)System.Math.Floor(ox), cy = (int)System.Math.Floor(oy), cz = (int)System.Math.Floor(oz);
        var key = (cx, cy, cz);
        int edits = store.RemovedCount;
        if (Remaining.TryGetValue(key, out var seen) && (seen.edits == edits || seen.left == 0)) return seen.left == 0;
        int left = 0;
        for (int y = -BodyReach; y <= BodyReach; y++)
        for (int z = -BodyReach; z <= BodyReach; z++)
        for (int x = -BodyReach; x <= BodyReach; x++)
        {
            var cell = new Int3(cx + x, cy + y, cz + z);
            if (store.MaterialAt(cell) == CellMaterial.Ore && zone.Planet.Source.OreKindAt(cell) == kind) left++;
        }
        Remaining[key] = (edits, left);
        return left == 0;
    }
}
