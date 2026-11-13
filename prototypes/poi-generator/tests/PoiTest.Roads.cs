using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// A composed site's roads (<see cref="SiteRoads"/>), over fresh places of every family that has them: every run ends
    /// at the gate, at what it serves (a vehicle door's apron, a pad, a runway, parking, a yard, a forecourt) or on another
    /// run; none ends against a wall (nothing standing within two metres straight on; a runway, six) and no road passes
    /// under anything; everything vehicles use is reached from the gate over paving; every door onto the yard has a path
    /// that leaves it square on and reaches the gate. Counted per family; ends at a wall must be 0.
    /// </summary>
    void Roads()
    {
        var rand = new Random();
        int places = 0, runs = 0, atWall = 0, loose = 0, under = 0, unserved = 0, serves = 0, doors = 0, pathless = 0;
        var byFam = new Dictionary<string, int[]>();
        string first = "", firstUnserved = "", firstPathless = "";
        foreach (FamilyDef f in _lib.Families)
        {
            for (int i = 0; i < 3; i++)
            {
                TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
                Layout l;
                try { l = PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech), out _); }
                catch (Exception) { continue; }
                if (l?.Extras.Roads is not { } plan) break;
                places++;
                var n = byFam.TryGetValue(f.Id, out var a) ? a : byFam[f.Id] = new int[6];
                n[0]++;
                bool Stands(Int3 p) => l.Blocks.Get(p) is { IsAir: false } b && BlockInfo.Collides(b.Type);
                bool Wall(Int3 p) => l.Blocks.Get(p) is { IsAir: false } b && BlockInfo.Collides(b.Type) && b.Role is Role.Wall or Role.Roof or Role.Window;
                var paved = new HashSet<Int3>(plan.Road.Concat(plan.Path).Concat(plan.Apron));
                // Reached from the gate over paving, four ways.
                var reached = new HashSet<Int3>();
                var q = new Queue<Int3>();
                foreach (Int3 p in paved.Where(p => Math.Abs(p.X - l.EntranceCell.X) + Math.Abs(p.Z - l.EntranceCell.Z) <= 1)) { reached.Add(p); q.Enqueue(p); }
                while (q.Count > 0)
                {
                    Int3 p = q.Dequeue();
                    foreach (Dir d in Dirs.Horizontal) if (paved.Contains(p + d.Vec()) && reached.Add(p + d.Vec())) q.Enqueue(p + d.Vec());
                }
                var gates = l.Extras.Markers.Where(m => m.Kind == "gate").Select(m => m.Cell).Append(l.EntranceCell).ToList();
                foreach (RoadRun r in plan.Runs)
                {
                    runs++;
                    n[1]++;
                    var others = paved.Except(r.Paved).ToHashSet();
                    bool Near(Int3 e, IEnumerable<Int3> cells, int k) => cells.Any(c => Math.Abs(c.X - e.X) <= k && Math.Abs(c.Z - e.Z) <= k);
                    foreach (var (e, back) in new[] { (r.Cells[^1], r.Cells.Count > 1 ? r.Cells[^2] : r.Cells[^1]), (r.Cells[0], r.Cells.Count > 1 ? r.Cells[1] : r.Cells[0]) })
                    {
                        Int3 on = new(Math.Sign(e.X - back.X), 0, Math.Sign(e.Z - back.Z));
                        int reach = r.Kind == "runway" ? 6 : 2;
                        bool wall = on != default && Enumerable.Range(1, reach).Any(k => Enumerable.Range(0, 2).Any(y => Wall(e + on * k + Int3.Up * (y + 1))));
                        bool ok = r.Kind == "runway" || Near(e, gates.Select(g => g - Int3.Up), 3) || Near(e, others, 1)
                                  || plan.Serves.Any(s => s.Cells.Contains(e)) || plan.Doors.Any(d => Near(e, d.Cells.SelectMany(c => Enumerable.Range(1, 10).Select(k => c - Int3.Up + d.Out.Vec() * k)), 1));
                        if (wall) { atWall++; n[2]++; if (first.Length == 0) first = $" (e.g. {f.Id} {r.Kind} to {r.End} at {e})"; }
                        else if (!ok) loose++;
                    }
                    if (r.Kind == "road")
                        foreach (Int3 p in r.Paved)
                            if (Enumerable.Range(1, 6).Any(y => Stands(p + Int3.Up * y))) { under++; n[3]++; break; }
                }
                foreach (RoadServe s in plan.Serves.Where(s => s.Kind is not "gate"))
                {
                    serves++;
                    if (!s.Cells.Any(reached.Contains)) { unserved++; n[4]++; if (firstUnserved.Length == 0) firstUnserved = $" (e.g. {f.Id}: a {s.Kind} near {s.Cells[s.Cells.Count / 2]})"; }
                }
                foreach (RoadDoor d in plan.Doors)
                {
                    doors++;
                    Int3 o = d.Out.Vec(), lo = d.Cells.Aggregate(Int3.Min), f0 = lo - Int3.Up + o;
                    // From the door, or where its own step or apron in front of it ends.
                    if (!Enumerable.Range(0, 10).Any(k => reached.Contains(f0 + o * k))) { pathless++; n[5]++; if (firstPathless.Length == 0) firstPathless = $" (e.g. {f.Id}: the door at {lo} facing {d.Out})"; }
                }
            }
        }
        GD.Print($"    {places} places, {runs} runs, {serves} things vehicles use, {doors} doors onto the yard");
        GD.Print("    family: places runs at-wall under-a-block unserved pathless-doors");
        foreach (var (id, n) in byFam.OrderBy(k => k.Key)) GD.Print($"      {id}: {string.Join(" ", n)}");
        Check(places > 0, $"composed sites have planned roads: {places} places");
        Check(atWall == 0, $"no road, path or runway ends against a wall: {atWall} ends{first}");
        Check(loose == 0, $"every run ends at the gate, at what it serves or on another run: {loose} loose ends");
        Check(under == 0, $"no road passes under anything: {under} runs");
        Check(unserved == 0, $"everything vehicles use is reached from the gate: {unserved} of {serves} not{firstUnserved}");
        Check(pathless == 0, $"every door onto the yard has a path to the gate: {pathless} of {doors} without{firstPathless}");
    }

    /// <summary>
    /// What the road planner costs a place's worker: every composed site laid out, validated and meshed on the same seeds
    /// with the planner on and off, alternating; median and worst per place each way, the slowest named, and the
    /// planner's own time. The planner itself takes no more than a twentieth of the median place, and never 50 ms in any one.
    /// </summary>
    void RoadCost()
    {
        var mats = new BlockMaterials();
        var rand = new Random();
        var on = new List<double>();
        var off = new List<double>();
        var own = new List<double>();
        string worstOn = "", worstOff = "", worstOwn = "";
        double Run(FamilyDef f, int v, ulong seed, bool roads, out string name, bool warm = false)
        {
            SiteRoads.Enabled = roads;
            Prof.Ticks.Clear();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = PoiFactory.Generate(_lib, f, v, f.Rarity, PoiState.Abandoned, seed, customSeed: seed);
            Validator.Check(p);
            foreach (var c in Mesher.Build(p.Blocks, p.Layout.Occupied, new MeshOptions { RockMin = p.Blocks.Min, RockMax = p.Blocks.Max, RockBottom = p.Blocks.Min.Y }))
            {
                MeshBuild.ToMesh(c, mats.BySurface);
                if (c.Collision.Count > 0) MeshBuild.ToShape(c);
            }
            name = $"{f.Id} ({p.Blocks.CountBlocks()} blocks, {p.Blocks.Size.X}x{p.Blocks.Size.Z} m)";
            double mine = Prof.Ticks.TryGetValue("site roads", out long t) ? Prof.Ms(t) : 0;
            if (roads && !warm) { if (own.Count == 0 || mine > own.Max()) worstOwn = name; own.Add(mine); }
            return sw.Elapsed.TotalMilliseconds;
        }
        try
        {
            foreach (FamilyDef f in _lib.Families.Where(f => f.Site != null || f.Variants.Any(v => v.Site != null)))
                for (int i = 0; i < 3; i++)
                {
                    int v = rand.Next(f.Variants.Count);
                    ulong seed = (ulong)rand.NextInt64();
                    SiteRoads.Enabled = true;
                    Layout probe;
                    try { probe = PoiFactory.BuildLayout(_lib, f, v, f.Rarity, seed); } catch (Exception) { continue; }
                    if (probe.Extras.Roads == null) break;
                    if (on.Count == 0) Run(f, v, seed, true, out _, warm: true);   // warm: the first place pays for the JIT
                    bool first = i % 2 == 0;
                    double a = Run(f, v, seed, first, out string na), b = Run(f, v, seed, !first, out string nb);
                    var (t1, n1, t0, n0) = first ? (a, na, b, nb) : (b, nb, a, na);
                    if (on.Count == 0 || t1 > on.Max()) worstOn = n1;
                    if (off.Count == 0 || t0 > off.Max()) worstOff = n0;
                    on.Add(t1);
                    off.Add(t0);
                }
        }
        finally { SiteRoads.Enabled = true; }
        double Median(List<double> x) => x.OrderBy(t => t).ElementAt(x.Count / 2);
        GD.Print($"    {on.Count} places, each laid out, validated and meshed with the road planner and without");
        GD.Print($"    with roads: median {Median(on):F0} ms, worst {on.Max():F0} ms ({worstOn}); without: median {Median(off):F0} ms, worst {off.Max():F0} ms ({worstOff})");
        GD.Print($"    the planner itself: median {Median(own):F1} ms, worst {own.Max():F1} ms ({worstOwn})");
        Check(Median(own) <= Median(off) * 0.05 && own.Max() < 50, $"the road planner itself costs at most a twentieth of the median place and under 50 ms in any (median {Median(own):F1} ms, worst {own.Max():F1} ms)");
    }
}