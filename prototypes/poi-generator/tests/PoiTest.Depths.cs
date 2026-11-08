using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Places below ground differ: between families and within one. Fresh worlds every run (a random seed, a random
/// way round, tech level and age per place): each family's places are generated many times, their plans measured
/// (how much they branch, how many loops, how many levels, how far room sizes and corridor widths spread, which plan
/// parts their levels use), and the families compared. Every place is walked from its way in: every room and level
/// reached, every spawn valid.
/// </summary>
public partial class PoiTest
{
    IEnumerable<FamilyDef> Below => _lib.Families.Where(f => f.Variants.Any(v => (v.Site ?? f.Site) is { } s && _lib.Sites[s].Parts.Any(p => p.Kind == "below" || p.Kind == "grand" && p.Arg("what") == "below")));

    /// <summary>What one place's plan measures.</summary>
    sealed record Shape(float Branching, float Loops, float Levels, float RoomSpread, float WidthSpread, float Rooms, string Parts)
    {
        public float[] Vector => new[] { Branching, Loops, Levels, RoomSpread, WidthSpread, Rooms };
    }

    static Shape Measure(Layout l)
    {
        var mods = l.Modules.Where(m => m.Kind != ModuleKind.Outdoor && m.Cells.Any(c => c.Y < 0)).ToList();
        var ids = new HashSet<int>(mods.Select(m => m.Index));
        var adj = ids.ToDictionary(i => i, _ => new HashSet<int>());
        foreach (var link in l.Links)
            if (ids.Contains(link.A) && ids.Contains(link.B) && link.A != link.B) { adj[link.A].Add(link.B); adj[link.B].Add(link.A); }
        int edges = adj.Values.Sum(a => a.Count) / 2, nodes = ids.Count;
        // Connected parts, for the cycle count.
        var seen = new HashSet<int>();
        int parts = 0;
        foreach (int i in ids)
        {
            if (!seen.Add(i)) continue;
            parts++;
            var q = new Queue<int>();
            q.Enqueue(i);
            while (q.Count > 0) foreach (int n in adj[q.Dequeue()]) if (seen.Add(n)) q.Enqueue(n);
        }
        float loops = Math.Max(0, edges - nodes + parts);
        float branching = nodes == 0 ? 0 : adj.Values.Count(a => a.Count >= 3) / (float)nodes;
        int levels = l.Extras.Levels.Count(v => v.WalkY < 0);
        var rooms = mods.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault or ModuleKind.Cave).Select(m => (float)m.Cells.Count(c => c.Y == m.Min.Y)).Where(a => a > 0).ToList();
        float Cv(List<float> xs) { if (xs.Count < 2) return 0; float mean = xs.Average(); return mean <= 0 ? 0 : MathF.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / xs.Count) / mean; }
        var widths = mods.Where(m => m.Kind == ModuleKind.Corridor).Select(m => (float)Math.Min(m.Shape.W, m.Shape.D)).ToList();
        string partsUsed = string.Join("+", l.Extras.Plans.Select(p => p.Signature.Split('/')).Where(p => p.Length > 1).Select(p => p[1]).Distinct().OrderBy(p => p));
        return new Shape(branching, loops, levels, Cv(rooms), Cv(widths), rooms.Count, partsUsed);
    }

    void Depths()
    {
        var rand = new Random();
        var families = Below.ToList();
        // "-- --depths-only=a,b" looks at just those families (a plan being fixed).
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--depths-only=")) is { } only) families = families.Where(f => only["--depths-only=".Length..].Split(',').Contains(f.Id)).ToList();
        Architect.Stats.Reset();
        int told = 0;
        Architect.Rejected = (layout, why) =>
        {
            if (told++ >= 3 || families.Count > 2) return;
            GD.Print("      rejected: " + why[0]);
            var nav = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
            var reach = nav.Reachable(layout.EntranceCell);
            foreach (var m in layout.Modules)
                GD.Print($"        {(m.Cells.Any(reach.Contains) ? "  " : "XX")} #{m.Index} {m.Purpose} {m.Kind} storey {m.Storey} {m.Min}..{m.Max} links {string.Join(",", layout.Links.Where(l => l.A == m.Index || l.B == m.Index).Select(l => l.A == m.Index ? l.B : l.A))}");
        };
        var shapes = new Dictionary<string, List<Shape>>();
        var mixes = new Dictionary<string, Dictionary<string, int>>();
        int places = 0, failed = 0, unreached = 0, invalid = 0, repeated = 0;
        string first = "";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int perFamily = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--depths-n=")) is { } n ? int.Parse(n["--depths-n=".Length..]) : 16;
        foreach (var f in families)
        {
            var list = shapes[f.Id] = new List<Shape>();
            var mix = mixes[f.Id] = new Dictionary<string, int>();
            var sigs = new HashSet<string>();
            for (int k = 0; k < perFamily; k++)
            {
                var age = new RuinAge((float)rand.NextDouble(), false, (float)rand.NextDouble(), 0.3f);
                var hints = new LayoutHints(Dirs.Horizontal[rand.Next(4)], (TechLevel)rand.Next(3), Ruin: age);
                Layout layout;
                try { layout = PoiFactory.BuildLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), hints); }
                catch (Exception e) { failed++; if (first.Length == 0) first = $" (first: {f.Id}: {e.Message})"; continue; }
                places++;
                var shape = Measure(layout);
                list.Add(shape);
                foreach (string p in shape.Parts.Split('+')) mix[p] = mix.GetValueOrDefault(p) + 1;
                string sig = string.Join("|", layout.Extras.Plans.Select(p => p.Signature));
                if (!sigs.Add(sig)) repeated++;
                // Walked from its way in: every room, every level.
                var nav = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
                var reach = nav.Reachable(layout.EntranceCell);
                var hidden = new HashSet<int>(layout.Extras.Secrets.Select(s => s.Module));
                var lost = layout.Modules.Where(m => !hidden.Contains(m.Index) && m.Shape.Occupied.Count > 0 && !m.Cells.Any(reach.Contains)).ToList();
                if (lost.Count > 0 || layout.Extras.Levels.Any(v => !reach.Any(c => c.Y == v.WalkY)))
                {
                    unreached++;
                    if (first.Length == 0) first = $" (first: {f.Id}: {lost.FirstOrDefault()?.Purpose ?? "a level"} unreached)";
                }
                var state = (PoiState)rand.Next(4);
                if (f.StateWeights[(int)state] <= 0f) state = PoiState.Abandoned;
                var inst = PoiFactory.Spawn(_lib, layout, state, (ulong)rand.NextInt64());
                if (Validator.Check(inst) is { Count: > 0 } vp)
                {
                    invalid++;
                    if (first.Length == 0)
                    {
                        first = $" (first: {f.Id} {state}: {vp[0]})";
                        var vc = inst.Caches.FirstOrDefault(c => c.InVault);
                        var vm = layout.VaultModule >= 0 ? layout.Modules[layout.VaultModule] : null;
                        var inav = inst.Nav();
                        var ireach = inav.Reachable(layout.EntranceCell);
                        GD.Print($"      vault module {vm?.Purpose} {vm?.Kind} {vm?.Min}..{vm?.Max}, reached {vm?.Cells.Count(ireach.Contains)} of {vm?.Cells.Count()}, cache {vc?.Cell}: "
                            + string.Join(" ", Dirs.Horizontal.Select(d => vc == null ? "" : $"{d}:{inst.Blocks.Get(vc.Cell + d.Vec()).Type}/{inav.Standable(vc.Cell + d.Vec())}/{ireach.Contains(vc.Cell + d.Vec())}")));
                        GD.Print("      plans: " + string.Join(" | ", layout.Extras.Plans.Select(p => p.Signature.Length > 60 ? p.Signature[..60] : p.Signature)));
                        // Where the layout's own way to the vault breaks in the spawn.
                        if (vm != null)
                        {
                            Int3 goal = vm.Cells.First(c => nav.Standable(c) && reach.Contains(c));
                            var path = nav.FindPath(layout.EntranceCell, goal);
                            Int3? broken = path?.FirstOrDefault(c => !inav.Standable(c) || !ireach.Contains(c));
                            if (broken is Int3 bc)
                            {
                                GD.Print($"      the way breaks at {bc}: layout {layout.Blocks.Get(bc).Type}/{layout.Blocks.Get(bc + Int3.Up).Type}, spawn {inst.Blocks.Get(bc).Type}/{inst.Blocks.Get(bc + Int3.Up).Type}/{inst.Blocks.Get(bc + Int3.Down).Type} {inst.Blocks.Get(bc).Role}, module {layout.Modules.FirstOrDefault(m => m.Cells.Contains(bc))?.Purpose}");
                                // Step by step, as a spawn goes, to see which pass takes it.
                                var t = new PoiInstance { Layout = layout, State = state, SpawnSeed = 1, Blocks = layout.Blocks.Clone(), HasCore = f.HasCore(state) };
                                t.Age = PoiInstance.AgeFor(f, state, false);
                                var rr = new Rng(5);
                                void Was(string step) { if (!t.Blocks.Get(bc).Equals(layout.Blocks.Get(bc))) GD.Print($"        after {step}: {t.Blocks.Get(bc).Type}"); }
                                Fixtures.Place(t, rr.Fork("f")); Was("fixtures");
                                Fixtures.Protect(t); Was("protect");
                                Gen.StateTransforms.Apply(t, rr.Fork("s")); Was("state");
                                Gen.StateTransforms.Weather(t, rr.Fork("w")); Was("weather");
                                Gen.Decay.Apply(t, rr.Fork("d")); Was("decay");
                                Dressing.Apply(_lib, t, rr.Fork("dr")); Was("dressing");
                                Gen.StateTransforms.Aftermath(t, rr.Fork("a")); Was("aftermath");
                                Structure.DropUntethered(t.Blocks); Was("untethered");
                                foreach (Int3 p in Structure.Floating(t.Blocks, layout.Occupied, null, false)) t.Blocks.Set(p, Block.Air);
                                Was("floating");
                                var neighbours = string.Join(" ", Dirs.All.Select(d => $"{d}:{layout.Blocks.Get(bc + d.Vec()).Type}/{layout.Blocks.Get(bc + d.Vec()).Role}>{t.Blocks.Get(bc + d.Vec()).Type}"));
                                GD.Print($"        neighbours {neighbours}");
                            }
                        }
                    }
                }
            }
        }
        // Each family's shape: mean and spread of every measure, and its mix of plan parts.
        string[] names = { "branching", "loops", "levels", "room spread", "width spread", "rooms" };
        var mean = new Dictionary<string, float[]>();
        var sd = new Dictionary<string, float[]>();
        foreach (var (id, list) in shapes.Where(s => s.Value.Count > 1))
        {
            mean[id] = Enumerable.Range(0, 6).Select(i => list.Average(s => s.Vector[i])).ToArray();
            sd[id] = Enumerable.Range(0, 6).Select(i => MathF.Sqrt(list.Sum(s => (s.Vector[i] - mean[id][i]) * (s.Vector[i] - mean[id][i])) / list.Count)).ToArray();
            string m = string.Join(", ", names.Select((nm, i) => $"{nm} {mean[id][i]:F2}±{sd[id][i]:F2}"));
            string parts = string.Join(" ", mixes[id].OrderByDescending(x => x.Value).Select(x => $"{x.Key}:{x.Value}"));
            GD.Print($"      {id,-18} {m}; parts {parts}");
        }
        // Between families: their mixes of plan parts overlap little, or their measures sit apart.
        var ids = mean.Keys.ToList();
        int pairs = 0, alike = 0;
        string closest = "";
        float worst = float.MaxValue;
        foreach (string a in ids)
            foreach (string b in ids.Where(b => string.CompareOrdinal(a, b) > 0))
            {
                pairs++;
                float ta = mixes[a].Values.Sum(), tb = mixes[b].Values.Sum();
                float overlap = mixes[a].Keys.Union(mixes[b].Keys).Sum(k => Math.Min(mixes[a].GetValueOrDefault(k) / ta, mixes[b].GetValueOrDefault(k) / tb));
                // Distance between centroids, each measure scaled by the two families' pooled spread.
                float d2 = 0;
                for (int i = 0; i < 6; i++)
                {
                    float pooled = MathF.Max(0.05f, (sd[a][i] + sd[b][i]) / 2f);
                    float z = (mean[a][i] - mean[b][i]) / pooled;
                    d2 += z * z;
                }
                float distance = MathF.Sqrt(d2) + 3f * (1f - overlap);
                if (distance < worst) { worst = distance; closest = $"{a} and {b}: parts overlap {overlap:P0}, measures {MathF.Sqrt(d2):F2} apart"; }
                if (distance < 1.5f) alike++;
            }
        // Within a family: no two plans the same, and the measures spread.
        var flat = shapes.Where(s => s.Value.Count > 1 && sd[s.Key].Count(x => x > 0.01f) < 3).Select(s => s.Key).ToList();
        Architect.Rejected = null;
        GD.Print($"      {places} places in {sw.ElapsedMilliseconds} ms, {Architect.Stats.Regenerated} regenerated");
        foreach (string why in Architect.Stats.FirstFailures.Take(8)) GD.Print("      regenerated after: " + why);
        Check(failed == 0 && places > 0, $"places below ground lay out on fresh worlds: {places} over {families.Count} families ({failed} failed){first}");
        Check(unreached == 0 && invalid == 0, $"every place below ground is walkable from its way in: every room and level reached ({unreached} not), every spawn valid ({invalid} not){first}");
        Check(alike == 0 && pairs > 10, $"places below ground differ between families: {pairs} pairs, {alike} alike; the closest {closest}");
        Check(repeated == 0 && flat.Count == 0, $"places below ground differ within a family: {repeated} repeated plans, {flat.Count} families whose measures barely vary{(flat.Count > 0 ? " (" + string.Join(", ", flat) + ")" : "")}");
    }
}
