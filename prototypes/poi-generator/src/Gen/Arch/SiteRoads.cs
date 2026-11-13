#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>A composed site's roads as planned (<see cref="SiteRoads"/>): what they were to reach and how they ran, for the tests.</summary>
public sealed class RoadPlan
{
    public readonly List<RoadRun> Runs = new();
    /// <summary>What vehicles use, each with the ground cells a road reaches it by.</summary>
    public readonly List<RoadServe> Serves = new();
    /// <summary>The doors people use from the yard, each with its way out.</summary>
    public readonly List<RoadDoor> Doors = new();
    /// <summary>Ground cells (y -1) paved by roads, by footpaths, and as what the roads serve (aprons, parking, turning places, pads, runways).</summary>
    public readonly HashSet<Int3> Road = new(), Path = new(), Apron = new();
}

/// <summary>One run: its centre line from where it leaves the network (or the gate) to where it ends, the ground it paved, and what it ends at.</summary>
public sealed record RoadRun(string Kind, List<Int3> Cells, HashSet<Int3> Paved, string End);

public sealed record RoadServe(string Kind, List<Int3> Cells, bool Reached);

/// <summary>A door onto the yard: its cells and the way out of it. A building's main door also wants a forecourt a road comes to.</summary>
public sealed record RoadDoor(List<Int3> Cells, Dir Out, bool Forecourt = false);

/// <summary>
/// A composed site's roads, planned once everything in it stands, from what is actually there. A road comes in at
/// the gate square on the world's road, then grows a branch to each thing vehicles use, nearest first, from whatever
/// of the network is already laid: around what stands on the ground (a centre line keeps three metres clear and
/// prefers its whole width), through fences only at their gates, never under anything. Each branch ends on what it
/// serves (an apron at a vehicle door, a pad, a runway, parking, a gate) or on a turning place; people's doors get a
/// footpath that leaves the door square on and joins the nearest paving. Surface and width by the world's age and
/// what the place is. Runways are authored, but end in clear ground: cut back from anything standing in their line.
/// </summary>
public static class SiteRoads
{
    /// <summary>Off for the tests that measure what the planner costs (and for comparing with the place unplanned).</summary>
    public static bool Enabled = true;

    /// <summary>Something vehicles use: the ground cells a road may end on (y -1), what it paves once reached, and whether the road ends in a turning place.</summary>
    public sealed record Want(string Kind, List<Int3> Goal, List<Int3> Pave, bool Turn = false, int Clear = 3);

    public sealed record Runway(int X, int Z, int Len, int Width, bool AlongX, bool Lights);

    static readonly Rgb Concrete = new(0x8a, 0x88, 0x82), Line = new(0xe8, 0xe4, 0xd8), Hazard = new(0xe0, 0xa8, 0x00), Tar = new(0x3a, 0x3a, 0x3c);
    static readonly int[] Dx = { 0, 1, 0, -1 }, Dz = { -1, 0, 1, 0 };   // North, East, South, West, as Dir

    /// <summary>The ground a vehicle may stand on: open columns, how far each is from anything standing (Chebyshev, capped).</summary>
    sealed class Grid
    {
        public readonly int X0, Z0, W, D;
        public readonly byte[] Dist;
        /// <summary>The route search's tables, made once per grid and reused by every search over it.</summary>
        public float[]? Cost;
        public int[]? Prev;
        /// <summary>Blocked where anything stands up to <paramref name="top"/> (5 for a vehicle, 1 for a person on foot).</summary>
        public Grid(Canvas c, Int3 entrance, int top)
        {
            int x0 = entrance.X, z0 = entrance.Z, x1 = x0, z1 = z0;
            foreach (Int3 p in c.Blocks.Keys) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z); }
            const int M = 12;
            X0 = x0 - M; Z0 = z0 - M; W = x1 - x0 + 2 * M + 1; D = z1 - z0 + 2 * M + 1;
            Dist = new byte[W * D];
            Array.Fill(Dist, (byte)255);
            var q = new Queue<int>();
            void Block(int x, int z) { int i = Idx(x, z); if (i >= 0 && Dist[i] != 0) { Dist[i] = 0; q.Enqueue(i); } }
            // Anything standing in a vehicle's height, a hole in the ground, standing water, the inside of any room or hall.
            foreach (var (p, b) in c.Blocks)
                if (p.Y >= 0 && p.Y <= top && BlockInfo.Collides(b.Type) || p.Y == -1 && !(b.Role == Role.Floor && BlockInfo.IsFullCube(b.Type))) Block(p.X, p.Z);
            foreach (Int3 p in c.Occ) if (p.Y == -1 && !c.Blocks.ContainsKey(p)) Block(p.X, p.Z);
            foreach (Int3 p in c.X.Water) Block(p.X, p.Z);
            foreach (PlacedModule m in c.Modules.Where(m => m.Kind != ModuleKind.Outdoor))
                foreach (Int3 o in m.Shape.Occupied) { Int3 p = o + m.Offset; if (p.Y >= -1 && p.Y <= top) Block(p.X, p.Z); }
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % W, z = i / W;
                if (Dist[i] >= 10) continue;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int n = Idx(X0 + x + dx, Z0 + z + dz);
                        if (n >= 0 && Dist[n] > Dist[i] + 1) { Dist[n] = (byte)(Dist[i] + 1); q.Enqueue(n); }
                    }
            }
        }
        public int Idx(int x, int z) => x < X0 || z < Z0 || x >= X0 + W || z >= Z0 + D ? -1 : (z - Z0) * W + (x - X0);
        public int Idx(Int3 p) => Idx(p.X, p.Z);
        public Int3 Cell(int i) => new(X0 + i % W, -1, Z0 + i / W);
        /// <summary>Clearance at a ground cell: 0 where something stands, large off the grid (open ground).</summary>
        public int Clear(Int3 p) { int i = Idx(p); return i < 0 ? 255 : Dist[i]; }
    }

    sealed class Surface
    {
        public Material Mat;
        public Rgb Col;
        public int Width;
        public bool Marked;
    }

    sealed class Ctx
    {
        public Canvas C = null!;
        public Grid G = null!, Walk = null!;
        public RoadPlan Plan = null!;
        public Surface Road = null!, Path = null!;
        public float Cracks;
        public readonly List<(Int3 Cell, Rgb Plain, bool AlongX)> Marks = new();
    }

    public static RoadPlan Plan(Canvas c, FamilyDef fam, TechLevel tech, Int3 entrance, Dir facing, List<Runway> runways, List<Want> wants, List<RoadDoor> doors)
    {
        bool track = tech == TechLevel.Low || fam.Tags.Contains("farm") || fam.Tags.Contains("settlement") || fam.Tags.Contains("gang");
        bool air = fam.Tags.Contains("aviation") || fam.Tags.Contains("space");
        var x = new Ctx
        {
            C = c, G = new Grid(c, entrance, 5), Walk = new Grid(c, entrance, 1), Plan = new RoadPlan(),
            Road = track ? new Surface { Mat = Material.Soil, Col = new Rgb(0x6e, 0x5e, 0x48), Width = 4 }
                : new Surface { Mat = Material.Concrete, Col = air ? Concrete : tech == TechLevel.High ? new Rgb(0x9a, 0x9c, 0x98) : Tar, Width = 6, Marked = true },
            Path = track ? new Surface { Mat = Material.Soil, Col = new Rgb(0x7c, 0x6c, 0x54), Width = 2 } : new Surface { Mat = Material.Rock, Col = new Rgb(0x84, 0x80, 0x78), Width = 2 },
            Cracks = tech switch { TechLevel.Low => 0.3f, TechLevel.Mid => 0.16f, _ => 0.06f },
        };
        c.X.Roads = x.Plan;
        var all = wants.ToList();
        foreach (Runway r in runways) LayRunway(x, r, all);
        // A building's main door wants a forecourt: somewhere in front of it a road can come to and turn.
        foreach (RoadDoor d in doors.Where(d => d.Forecourt))
        {
            Int3 o = d.Out.Vec(), side = new(-o.Z, 0, o.X), lo = d.Cells.Aggregate(Int3.Min);
            var goal = new List<Int3>();
            for (int k = 4; k <= 10; k++)
                for (int l = -4; l <= 5; l++) goal.Add(new Int3(lo.X + o.X * k + side.X * l, -1, lo.Z + o.Z * k + side.Z * l));
            all.Add(new Want("forecourt", goal, new List<Int3>(), Turn: true));
        }

        // In at the gate, square on the world's road: from three metres outside to one in; the first branch carries straight on from it.
        Int3 inward = facing.Opposite().Vec();
        var stub = Enumerable.Range(-3, 5).Select(k => new Int3(entrance.X + inward.X * k, -1, entrance.Z + inward.Z * k)).ToList();
        Lay(x, "road", stub, x.Road, "gate");
        bool first = true;

        foreach (Want w in all.OrderBy(w => w.Goal.Count == 0 ? 0 : w.Goal.Min(g => Math.Abs(g.X - entrance.X) + Math.Abs(g.Z - entrance.Z))))
        {
            var goal = w.Goal.Where(g => x.G.Clear(g) >= w.Clear).ToList();
            bool reached = goal.Any(g => x.Plan.Road.Contains(g) || x.Plan.Apron.Contains(g));
            if (!reached && goal.Count > 0)
            {
                var targets = new HashSet<int>(goal.Select(x.G.Idx).Where(i => i >= 0));
                var from = first ? new List<int> { x.G.Idx(stub[^1]) } : x.Plan.Road.Concat(x.Plan.Apron).Select(x.G.Idx).Where(i => i >= 0).Distinct().ToList();
                var route = Route(x.G, from, targets, Math.Min(2, w.Clear), x.Road.Width / 2 + 1, first ? (int)facing.Opposite() : -1);
                first &= route == null;
                if (route != null)
                {
                    Lay(x, "road", route, x.Road, w.Kind);
                    if (w.Turn)
                    {
                        int h = x.Road.Width / 2 + 1;
                        Int3 e = route[^1];
                        var turn = new List<Int3>();
                        for (int dz = -h; dz <= h; dz++)
                            for (int dx = -h; dx <= h; dx++) turn.Add(e + new Int3(dx, 0, dz));
                        Pave(x, turn, x.Road, x.Plan.Apron);
                    }
                    reached = true;
                }
            }
            if (reached)
            {
                Pave(x, w.Pave, x.Road, x.Plan.Apron);
                // What was already paved (a pad, a runway) is served ground too.
                if (w.Kind is "pad" or "runway") foreach (Int3 g in w.Goal) if (c.Get(g) is { Role: Role.Floor }) x.Plan.Apron.Add(g);
            }
            x.Plan.Serves.Add(new RoadServe(w.Kind, w.Goal, reached));
        }

        // People's doors: a footpath out of each, square on for two metres, then the shortest way to the paving.
        foreach (RoadDoor d in doors)
        {
            x.Plan.Doors.Add(d);
            Int3 o = d.Out.Vec(), lo = d.Cells.Where(q => q.Y == d.Cells.Min(r => r.Y)).Aggregate(Int3.Min);
            // From where the building's own step or apron in front of the door ends.
            Int3 f0 = new(lo.X + o.X, -1, lo.Z + o.Z);
            for (int k = 0; k < 8 && (x.Walk.Clear(f0) == 0 || x.C.Blocks.ContainsKey(f0) && !x.Plan.Road.Contains(f0) && !x.Plan.Apron.Contains(f0)); k++) f0 += o;
            Int3 f1 = f0 + o;
            var paved = x.Plan.Road.Concat(x.Plan.Apron).Concat(x.Plan.Path).Select(x.Walk.Idx).Where(i => i >= 0).ToHashSet();
            int s = x.Walk.Idx(f1);
            if (s < 0 || x.Walk.Dist[s] == 0) continue;
            var route = paved.Contains(s) ? new List<Int3> { f1 } : Route(x.Walk, new[] { s }, paved, 1, 2, (int)d.Out);
            if (route == null) continue;
            route.Insert(0, f0);
            Lay(x, "path", route, x.Path, "road");
        }
        StopMarks(x);
        return x.Plan;
    }

    /// <summary>
    /// The cheapest way over open ground from any of the sources to any target: straight runs (a turn costs), kept clear
    /// of what stands. A* toward the targets' bounding box, so a search goes only as far as its way needs.
    /// </summary>
    static List<Int3>? Route(Grid g, IEnumerable<int> from, HashSet<int> targets, int minClear, int want, int only = -1)
    {
        int n = g.W * g.D;
        var cost = g.Cost ??= new float[n * 4];
        var prev = g.Prev ??= new int[n * 4];
        Array.Fill(cost, float.MaxValue);
        int tx0 = int.MaxValue, tz0 = int.MaxValue, tx1 = int.MinValue, tz1 = int.MinValue;
        foreach (int t in targets) { int x = t % g.W, z = t / g.W; tx0 = Math.Min(tx0, x); tx1 = Math.Max(tx1, x); tz0 = Math.Min(tz0, z); tz1 = Math.Max(tz1, z); }
        float H(int i) { int x = i % g.W, z = i / g.W; return Math.Max(0, Math.Max(tx0 - x, x - tx1)) + Math.Max(0, Math.Max(tz0 - z, z - tz1)); }
        var open = new PriorityQueue<int, float>();
        foreach (int s in from)
            for (int d = 0; d < 4; d++)
                if (only < 0 || d == only) { cost[s * 4 + d] = 0; prev[s * 4 + d] = -1; open.Enqueue(s * 4 + d, H(s)); }
        while (open.TryDequeue(out int st, out float f))
        {
            int cell = st >> 2, dir = st & 3;
            float at = cost[st];
            if (f > at + H(cell) + 0.001f) continue;
            if (targets.Contains(cell))
            {
                var path = new List<Int3>();
                for (int k = st; k >= 0; k = prev[k]) path.Add(g.Cell(k >> 2));
                path.Reverse();
                return path;
            }
            int cx = g.X0 + cell % g.W, cz = g.Z0 + cell / g.W;
            for (int nd = 0; nd < 4; nd++)
            {
                if (nd == (dir + 2) % 4) continue;
                int nb = g.Idx(cx + Dx[nd], cz + Dz[nd]);
                if (nb < 0 || g.Dist[nb] < minClear && !targets.Contains(nb)) continue;
                float c = at + 1 + (nd != dir ? 5 : 0) + Math.Max(0, want - g.Dist[nb]) * 2;
                int ns = nb * 4 + nd;
                if (c >= cost[ns]) continue;
                cost[ns] = c;
                prev[ns] = st;
                open.Enqueue(ns, c + H(nb));
            }
        }
        return null;
    }

    /// <summary>Paves a run along its centre line, its whole width where the ground is open, and records it.</summary>
    static void Lay(Ctx x, string kind, List<Int3> line, Surface s, string end)
    {
        if (line.Count == 0) return;
        int lo = -(s.Width / 2), hi = lo + s.Width - 1;
        var band = new List<Int3>();
        foreach (Int3 p in line)
            for (int b = lo; b <= hi; b++)
                for (int a = lo; a <= hi; a++) band.Add(p + new Int3(a, 0, b));
        var paved = kind == "path" ? Pave(x, band, s, x.Plan.Path, x.Walk) : Pave(x, band, s, x.Plan.Road);
        x.Plan.Runs.Add(new RoadRun(kind, line, paved, end));
        if (!s.Marked || kind != "road") return;
        // A dashed centre line down the straight stretches.
        for (int i = 1; i + 1 < line.Count; i++)
        {
            Int3 a = line[i - 1], c = line[i + 1];
            bool alongX = a.Z == c.Z && a.X != c.X;
            if (!(alongX || a.X == c.X) || i % 8 >= 4) continue;
            Int3 m = line[i];
            if (x.C.Get(m) is { Type: BlockType.Cube, Role: Role.Floor } f && paved.Contains(m)) { x.Marks.Add((m, f.Color, alongX)); f.Color = Hazard; x.C.Blocks[m] = f; }
        }
    }

    /// <summary>Lays the surface in every open ground cell given (never over anything already there), weeds through the cracks.</summary>
    static HashSet<Int3> Pave(Ctx x, IEnumerable<Int3> cells, Surface s, HashSet<Int3> into, Grid? on = null)
    {
        var done = new HashSet<Int3>();
        foreach (Int3 c in cells)
        {
            if ((on ?? x.G).Clear(c) == 0 || x.C.Blocks.ContainsKey(c) || x.Plan.Road.Contains(c) || x.Plan.Path.Contains(c) || x.Plan.Apron.Contains(c) || !done.Add(c)) continue;
            into.Add(c);
            uint h = Hash.Cell(c, 44);
            if ((h % 1000) / 1000f < x.Cracks)
            {
                if (h % 3 == 0 && !x.C.Blocks.ContainsKey(c + Int3.Up)) x.C.Blocks[c + Int3.Up] = new Block(BlockType.Plant, Material.Plant, new Rgb(0x5a, 0x6a, 0x30), Role.Debris);
                continue;
            }
            x.C.Set(c, new Block(BlockType.Cube, s.Mat, s.Col.Scale(0.9f + (h % 100) / 1000f), Role.Floor));
        }
        return done;
    }

    /// <summary>
    /// An authored runway, laid in the longest stretch of its line that nothing stands in, then cut back till six metres of
    /// open ground lie beyond each end: a runway runs out into the open, never on to a wall.
    /// </summary>
    static void LayRunway(Ctx x, Runway r, List<Want> wants)
    {
        Int3 At(int i, int j) => r.AlongX ? new Int3(r.X + i, -1, r.Z + j) : new Int3(r.X + j, -1, r.Z + i);
        bool Open(int i) => Enumerable.Range(0, r.Width).All(j => x.G.Clear(At(i, j)) > 0 && !x.C.Blocks.ContainsKey(At(i, j)));
        int a = 0, b = -1, run = 0;
        for (int i = 0; i < r.Len; i++)
        {
            run = Open(i) ? run + 1 : 0;
            if (run > b - a + 1) (a, b) = (i - run + 1, i);
        }
        bool Clear(int end, int step) => Enumerable.Range(1, 6).All(k => Enumerable.Range(0, r.Width).All(j => x.G.Clear(At(end + step * k, j)) > 0));
        while (a <= b && !Clear(a, -1)) a++;
        while (b >= a && !Clear(b, 1)) b--;
        if (b - a + 1 < 30) return;
        var cells = new List<Int3>();
        var line = new List<Int3>();
        for (int i = a; i <= b; i++)
        {
            line.Add(At(i, r.Width / 2));
            for (int j = 0; j < r.Width; j++)
            {
                Int3 c = At(i, j);
                cells.Add(c);
                uint h = Hash.Cell(c, 44);
                if ((h % 1000) / 1000f < x.Cracks)
                {
                    if (h % 3 == 0) x.C.Blocks[c + Int3.Up] = new Block(BlockType.Plant, Material.Plant, new Rgb(0x5a, 0x6a, 0x30), Role.Debris);
                    continue;
                }
                Rgb col = Concrete.Scale(0.9f + (h % 100) / 1000f), plain = col;
                bool marked = j == r.Width / 2 && (i - a) % 8 < 4 || (i - a < 4 || b - i < 4) && j % 3 == 1 || j == 0 || j == r.Width - 1;
                if (marked) { col = Line; x.Marks.Add((c, plain, r.AlongX)); }
                x.C.Set(c, new Block(BlockType.Cube, Material.Concrete, col, Role.Floor));
            }
        }
        if (r.Lights)
            for (int i = a + 2; i <= b; i += 10)
            {
                Int3 l = At(i, -1) + Int3.Up;
                if (x.G.Clear(l) == 0 || x.C.Blocks.ContainsKey(l)) continue;
                x.C.Blocks[l] = new Block(BlockType.Pillar, Material.Steel, new Rgb(0x50, 0x50, 0x50), Role.Decor);
                if (x.G.Idx(l) is int li and >= 0) x.G.Dist[li] = 0;   // a light stands here now: nothing paves under it
            }
        x.Plan.Runs.Add(new RoadRun("runway", line, cells.ToHashSet(), "open"));
        wants.Add(new Want("runway", cells, new List<Int3>()));
    }

    /// <summary>A painted line ends clear of whatever stands on the paving: no marking runs on to a wall.</summary>
    static void StopMarks(Ctx x)
    {
        const int Clear = 4;
        foreach (var (c, plain, alongX) in x.Marks)
        {
            Int3 run = alongX ? new Int3(1, 0, 0) : new Int3(0, 0, 1);
            bool near = false;
            for (int k = -Clear; k <= Clear && !near; k++)
                near = x.C.Get(c + Int3.Up + run * k) is { IsAir: false } b && BlockInfo.Collides(b.Type);
            if (near && x.C.Blocks.TryGetValue(c, out Block f)) { f.Color = plain; x.C.Blocks[c] = f; }
        }
    }
}
