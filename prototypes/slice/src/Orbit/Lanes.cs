using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Stars;
using V = System.Numerics.Vector3;

namespace Slice.Orbit;

/// <summary>
/// The hyperdrive's lanes: a lane joins two stars within jump range of each other (true 3D
/// distance), and a route is the shortest chain of lanes between two stars. The range is one
/// number, <see cref="Range"/>, which is the base drive's until a ship's own drive says otherwise.
/// </summary>
public sealed class Lanes
{
    /// <summary>One jump, light years: the base drive's reach (<see cref="Galaxy.JumpT1"/>). A fitted hyperdrive would set this from its tier.</summary>
    public static float Range = Galaxy.JumpT1;

    /// <summary>Fold crystal per jump per this many light years, rounded up: the route planner's preview of what a route burns.</summary>
    public const float LyPerCrystal = 10f;

    /// <summary>How far out the network is drawn, in jumps.</summary>
    public const int Jumps = 3;

    public readonly int From;
    public readonly List<int> Stars;
    /// <summary>The lanes drawn: of every pair in range, those with no third star between them (the Gabriel graph). It keeps every chain a jump route can take, and leaves a network to follow instead of a hairball.</summary>
    public readonly List<(int a, int b)> Edges = new();
    public readonly HashSet<int> Connected = new();

    /// <summary>The network round a star: every star within a few jumps (and any extra ones asked for), with a lane between each pair in range of each other.</summary>
    public Lanes(Galaxy galaxy, int from, IEnumerable<int> also = null, int cap = 700)
    {
        From = from;
        var set = new HashSet<int>(galaxy.Within(galaxy.Stars[from].Position, Range * Jumps).Take(cap)) { from };
        if (also != null) set.UnionWith(also);
        Stars = set.ToList();
        var all = new List<(int a, int b)>();
        foreach (int id in Stars)
            foreach (int other in galaxy.Neighbours(id, Range))
                if (id < other && set.Contains(other)) all.Add((id, other));
        foreach (var (a, b) in all)
        {
            V pa = galaxy.Stars[a].Position, pb = galaxy.Stars[b].Position;
            float half = V.Distance(pa, pb) / 2f * 0.999f;
            if (!galaxy.Within((pa + pb) / 2f, half).Any(o => o != a && o != b)) Edges.Add((a, b));
        }
        var next = new Dictionary<int, List<int>>();
        foreach (var (a, b) in all)
        {
            if (!next.TryGetValue(a, out var la)) next[a] = la = new();
            if (!next.TryGetValue(b, out var lb)) next[b] = lb = new();
            la.Add(b);
            lb.Add(a);
        }
        var queue = new Queue<int>();
        Connected.Add(from);
        queue.Enqueue(from);
        while (queue.Count > 0)
            foreach (int n in next.GetValueOrDefault(queue.Dequeue()) ?? new())
                if (Connected.Add(n)) queue.Enqueue(n);
    }

    /// <summary>The shortest chain of jumps from one star to another over the whole catalogue (stars in order, both ends included), or null if no chain of lanes reaches.</summary>
    public static List<int> Route(Galaxy galaxy, int from, int to, float range, int settleCap = 30000)
    {
        if (from == to) return new List<int> { from };
        var dist = new Dictionary<int, float> { [from] = 0f };
        var back = new Dictionary<int, int>();
        var done = new HashSet<int>();
        var open = new PriorityQueue<int, float>();
        open.Enqueue(from, 0f);
        while (open.TryDequeue(out int at, out float d) && done.Count < settleCap)
        {
            if (!done.Add(at)) continue;
            if (at == to)
            {
                var path = new List<int> { to };
                while (path[^1] != from) path.Add(back[path[^1]]);
                path.Reverse();
                return path;
            }
            foreach (int n in galaxy.Neighbours(at, range))
            {
                float nd = d + V.Distance(galaxy.Stars[at].Position, galaxy.Stars[n].Position);
                if (nd < dist.GetValueOrDefault(n, float.MaxValue))
                {
                    dist[n] = nd;
                    back[n] = at;
                    open.Enqueue(n, nd);
                }
            }
        }
        return null;
    }

    /// <summary>Total light years along a route.</summary>
    public static float Length(Galaxy galaxy, IReadOnlyList<int> route)
    {
        float sum = 0f;
        for (int i = 1; i < route.Count; i++) sum += V.Distance(galaxy.Stars[route[i - 1]].Position, galaxy.Stars[route[i]].Position);
        return sum;
    }

    /// <summary>Fold crystal a route burns: each jump costs its length over <see cref="LyPerCrystal"/>, rounded up.</summary>
    public static int Crystal(Galaxy galaxy, IReadOnlyList<int> route)
    {
        int sum = 0;
        for (int i = 1; i < route.Count; i++) sum += (int)MathF.Ceiling(V.Distance(galaxy.Stars[route[i - 1]].Position, galaxy.Stars[route[i]].Position) / LyPerCrystal);
        return sum;
    }
}
