using System;
using System.Collections.Generic;
using Godot;
using Planets.Gen;
using FloraSolid = PlanetSurface.Terrain.FloraSolid;
using N = System.Numerics;

namespace Slice.Surface;

/// <summary>
/// The trees, rocks and crowns the terrain's own scatter puts on the ground, worked out a far chunk at a time
/// on a worker as the terrain does and kept by chunk: nothing is streamed in for it, and a lookup only reads the
/// chunks round a point. What a landing ship clears its spot of, and what creatures and people walk round.
/// </summary>
public sealed class ScatterSolids
{
    readonly SurfaceZone _zone;
    readonly Dictionary<(int, int), System.Threading.Tasks.Task<List<FloraSolid>>> _chunks = new();

    public ScatterSolids(SurfaceZone zone) => _zone = zone;

    /// <summary>Those within a reach of a point (frame metres); null while their chunks are still being worked out.</summary>
    public List<FloraSolid> Near(N.Vector3 p, float reach)
    {
        var planet = _zone.Planet;
        double cx = Mathx.WrapX(p.X, planet.Width), cz = Mathx.Wrap(p.Z, planet.Height);
        int size = PlanetSurface.Terrain.FarBuilder.Size;
        int nx = PlanetSurface.Terrain.FarBuilder.Count((int)planet.Width), nz = PlanetSurface.Terrain.FarBuilder.Count((int)planet.Height);
        var found = new List<FloraSolid>();
        bool ready = true;
        for (int fz = (int)Math.Floor((cz - reach) / size); fz <= (int)Math.Floor((cz + reach) / size); fz++)
        for (int fx = (int)Math.Floor((cx - reach) / size); fx <= (int)Math.Floor((cx + reach) / size); fx++)
        {
            var key = (((fx % nx) + nx) % nx, ((fz % nz) + nz) % nz);
            if (!_chunks.TryGetValue(key, out var job)) _chunks[key] = job = _zone.Streamer.SolidsOfChunk(key.Item1, key.Item2);
            if (!job.IsCompleted) { ready = false; continue; }
            if (job.IsFaulted) continue;
            foreach (FloraSolid s in job.Result)
                if (Math.Abs(Mathx.WrapDelta(s.Center.X - cx, planet.Width)) < reach + Reach(s) && Math.Abs(Mathx.WrapDelta(s.Center.Z - cz, planet.Height)) < reach + Reach(s)) found.Add(s);
        }
        return ready ? found : null;
    }

    /// <summary>A solid's reach round its centre, flat, m: a trunk's radius, a box's half diagonal, a crown's at its tree's scale.</summary>
    public static float Reach(FloraSolid s) =>
        s.IsTrunk ? s.Extents.X : new Vector2(s.Extents.X, s.Extents.Z).Length() * (s.IsCanopy ? s.Scale : 1f);

    /// <summary>Whether it stands on the ground in a walker's way: a trunk or a rock, not a crown overhead.</summary>
    public static bool InTheWay(FloraSolid s) => !s.IsCanopy;

    /// <summary>Where a solid stands, flat, in the frame nearest a point (the planet wraps).</summary>
    public Vector3 Nearest(FloraSolid s, Vector3 near)
    {
        var planet = _zone.Planet;
        return new Vector3(near.X + (float)Mathx.WrapDelta(s.Center.X - Mathx.WrapX(near.X, planet.Width), planet.Width), 0f,
            near.Z + (float)Mathx.WrapDelta(s.Center.Z - Mathx.Wrap(near.Z, planet.Height), planet.Height));
    }
}
