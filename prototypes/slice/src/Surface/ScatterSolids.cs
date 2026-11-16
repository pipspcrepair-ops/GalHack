using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using PlanetSurface.World;
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
    readonly Dictionary<(int, int), Task<List<FloraSolid>>> _chunks = new();

    public ScatterSolids(SurfaceZone zone) => _zone = zone;

    /// <summary>Those within a reach of a point (frame metres); null while their chunks are still being worked out.</summary>
    public List<FloraSolid> Near(N.Vector3 p, float reach)
    {
        var jobs = Jobs(p, reach);
        foreach (var job in jobs)
            if (!job.IsCompleted) return null;
        return Within(jobs, p, reach);
    }

    /// <summary>The same once their chunks are worked out, for a worker to wait on (the lookup is made here, on the main thread).</summary>
    public Task<List<FloraSolid>> Later(N.Vector3 p, float reach)
    {
        var jobs = Jobs(p, reach);
        return Task.WhenAll(jobs).ContinueWith(_ => Within(jobs, p, reach));
    }

    /// <summary>The chunks round a point, each worked out once and kept.</summary>
    List<Task<List<FloraSolid>>> Jobs(N.Vector3 p, float reach)
    {
        var planet = _zone.Planet;
        double cx = Mathx.WrapX(p.X, planet.Width), cz = Mathx.Wrap(p.Z, planet.Height);
        int size = PlanetSurface.Terrain.FarBuilder.Size;
        int nx = PlanetSurface.Terrain.FarBuilder.Count((int)planet.Width), nz = PlanetSurface.Terrain.FarBuilder.Count((int)planet.Height);
        var jobs = new List<Task<List<FloraSolid>>>();
        for (int fz = (int)Math.Floor((cz - reach) / size); fz <= (int)Math.Floor((cz + reach) / size); fz++)
        for (int fx = (int)Math.Floor((cx - reach) / size); fx <= (int)Math.Floor((cx + reach) / size); fx++)
        {
            var key = (((fx % nx) + nx) % nx, ((fz % nz) + nz) % nz);
            if (!_chunks.TryGetValue(key, out var job)) _chunks[key] = job = _zone.Streamer.SolidsOfChunk(key.Item1, key.Item2);
            jobs.Add(job);
        }
        return jobs;
    }

    List<FloraSolid> Within(List<Task<List<FloraSolid>>> jobs, N.Vector3 p, float reach)
    {
        var planet = _zone.Planet;
        double cx = Mathx.WrapX(p.X, planet.Width), cz = Mathx.Wrap(p.Z, planet.Height);
        var found = new List<FloraSolid>();
        foreach (var job in jobs)
        {
            if (!job.IsCompletedSuccessfully) continue;
            foreach (FloraSolid s in job.Result)
                if (Math.Abs(Mathx.WrapDelta(s.Center.X - cx, planet.Width)) < reach + Reach(s) && Math.Abs(Mathx.WrapDelta(s.Center.Z - cz, planet.Height)) < reach + Reach(s)) found.Add(s);
        }
        return found;
    }

    /// <summary>A solid's reach round its centre, flat, m: a trunk's radius, a box's half diagonal, a crown's at its tree's scale.</summary>
    public static float Reach(FloraSolid s) =>
        s.IsTrunk ? s.Extents.X : new Vector2(s.Extents.X, s.Extents.Z).Length() * (s.IsCanopy ? s.Scale : 1f);

    /// <summary>How high over its plant's foot a part still stands in a walker's way, m: a limb or a cap higher up is walked under.</summary>
    public const float Underfoot = 2f;

    /// <summary>
    /// Where solids stand in a walker's way, as flat circles in the frame nearest a point (the planet wraps): a rock whole;
    /// a trunk, a limb, a pipe or a stalk along as much of it as comes within <see cref="Underfoot"/> of its plant's foot,
    /// a circle every radius; never a crown, nor a limb or a cap overhead. Worker-safe.
    /// </summary>
    public static void Footprints(PlanetWorld planet, List<FloraSolid> solids, Vector3 near, List<(long Id, Vector3 At, float R)> into)
    {
        into.Clear();
        var feet = new Dictionary<long, float>();
        foreach (FloraSolid s in solids)
            if (s.IsTrunk) feet[s.Id] = MathF.Min(feet.GetValueOrDefault(s.Id, float.MaxValue), Low(s).Y);
        foreach (FloraSolid s in solids)
        {
            if (s.IsCanopy) continue;
            var at = new Vector3(near.X + (float)Mathx.WrapDelta(s.Center.X - Mathx.WrapX(near.X, planet.Width), planet.Width), 0f,
                near.Z + (float)Mathx.WrapDelta(s.Center.Z - Mathx.Wrap(near.Z, planet.Height), planet.Height));
            if (!s.IsTrunk) { into.Add((s.Id, at, Reach(s))); continue; }
            Vector3 low = Low(s), run = (s.Center - low) * 2f;
            float above = feet[s.Id] + Underfoot - low.Y;
            if (above < 0f) continue;
            Vector3 flat = new Vector3(run.X, 0f, run.Z) * (run.Y > 1e-3f ? MathF.Min(1f, above / run.Y) : 1f);
            Vector3 from = at + new Vector3(low.X - s.Center.X, 0f, low.Z - s.Center.Z);
            int n = (int)MathF.Ceiling(flat.Length() / MathF.Max(0.25f, s.Extents.X));
            for (int i = 0; i <= n; i++) into.Add((s.Id, from + flat * (n == 0 ? 0f : (float)i / n), s.Extents.X));
        }
    }

    /// <summary>A trunk's lower end (canonical): its foot, or a limb's end nearer the ground.</summary>
    static Vector3 Low(FloraSolid s)
    {
        Vector3 half = s.Basis.Y * s.Extents.Y;
        return half.Y > 0f ? s.Center - half : s.Center + half;
    }
}
