using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// The ornament (the owner, 14 Nov: "purely decorative blocks that add appeal"). Over fresh places of every kind at
    /// every tech level, each spawned without it and then given it: every element is whole, held (nothing floats), clear of
    /// every way and walk (what the way in reaches is unchanged), regular in its spacing, never a lone repeat; it costs
    /// under 5% of the median place's build and under 30 ms on any place; each builder's vocabulary is its own.
    /// </summary>
    void OrnamentCheck()
    {
        var rand = new Random();
        int places = 0, broken = 0, floating = 0, blocked = 0, irregular = 0, orphans = 0;
        var build = new List<double>();
        var cost = new List<double>();
        var added = new List<int>();
        var kinds = new SortedDictionary<string, int>();
        var vocab = new Dictionary<ulong, string>();
        int unlike = 0;
        foreach (FamilyDef f in _lib.Families.Where(f => !f.Tags.Contains("city") && !f.Space))
            foreach (TechLevel tech in f.Techs.Count > 0 ? f.Techs.Select(WorldProfile.Parse).Distinct() : Enum.GetValues<TechLevel>())
            {
                PoiInstance inst;
                var sw = Stopwatch.StartNew();
                try
                {
                    Layout layout = PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech), out _);
                    if (layout == null) continue;
                    Ornament.Enabled = false;
                    inst = PoiFactory.Spawn(_lib, layout, rand.Next(2) == 0 ? PoiState.Abandoned : PoiState.Ruined, (ulong)rand.NextInt64());
                }
                catch (Exception) { continue; }
                finally { Ornament.Enabled = true; }
                build.Add(sw.Elapsed.TotalMilliseconds);
                places++;
                // The same place given its ornament.
                var orn = new PoiInstance { Layout = inst.Layout, State = inst.State, SpawnSeed = inst.SpawnSeed, Blocks = inst.Blocks.Clone(), Protected = inst.Protected, World = inst.World, Lights = inst.Lights, Survey = inst.Survey };
                orn.Fallen.AddRange(inst.Fallen);
                GC.Collect(); GC.WaitForPendingFinalizers(); // (the spawns' garbage is theirs: the ornament is timed on its own)
                // (Once on a copy first, so no place pays for compiling the pass: generation compiles it once a run, not once a place.)
                var warm = new PoiInstance { Layout = inst.Layout, State = inst.State, Blocks = inst.Blocks.Clone(), Protected = inst.Protected, Lights = inst.Lights, Survey = inst.Survey };
                warm.Fallen.AddRange(inst.Fallen);
                Ornament.Apply(warm);
                // (Timed on two copies, the faster counting: the machine's own hiccups aren't the pass's.)
                var again = new PoiInstance { Layout = inst.Layout, State = inst.State, Blocks = inst.Blocks.Clone(), Protected = inst.Protected, Lights = inst.Lights, Survey = inst.Survey };
                again.Fallen.AddRange(inst.Fallen);
                sw.Restart();
                Ornament.Apply(again);
                double first = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                Ornament.Apply(orn);
                cost.Add(Math.Min(first, sw.Elapsed.TotalMilliseconds));
                added.Add(orn.Ornaments.Sum(p => p.Cells.Count));
                string sig = Ornament.For(orn).Sig;
                ulong who = Ornament.Builder(orn);
                if (vocab.TryGetValue(who, out string had) && had != sig) unlike++;
                vocab[who] = sig;
                foreach (var p in orn.Ornaments)
                {
                    kinds[p.Kind] = kinds.GetValueOrDefault(p.Kind) + 1;
                    if (p.Cells.Any(c => orn.Blocks.Get(c).IsAir)) broken++;
                    if (p.At.Count >= 2 && p.At.Zip(p.At.Skip(1), (a, b) => b - a).Distinct().Count() > 1) irregular++;
                    if (p.Kind is "fins" or "frames" && p.At.Count < 2 || p.Kind == "masts" && p.Cells.Count < 2) orphans++;
                }
                var mine = orn.Ornaments.SelectMany(p => p.Cells).ToHashSet();
                var anchors = inst.Layout.Extras.InSpace ? LayoutCheck.StationAnchors(inst.Layout) : null;
                var adrift = Structure.Floating(orn.Blocks, inst.Layout.Occupied, anchors, inst.Layout.Extras.Plain.Count > 0).Where(mine.Contains).ToHashSet(); // (as the spawn judges it)
                var adriftBefore = Structure.Floating(inst.Blocks, inst.Layout.Occupied, anchors, inst.Layout.Extras.Plain.Count > 0).ToHashSet();
                foreach (var p in orn.Ornaments.Where(p => p.Cells.Any(c => Dirs.All.Any(d => adriftBefore.Contains(c + d.Vec()))))) adrift.ExceptWith(p.Cells); // (held by what floated already: not the ornament's fault)
                floating += adrift.Count;
                foreach (var p in orn.Ornaments.Where(p => p.Cells.Any(adrift.Contains))) kinds["adrift " + p.Kind] = kinds.GetValueOrDefault("adrift " + p.Kind) + 1;
                NavGrid a = inst.Nav(), b = orn.Nav();
                if (a.Standable(inst.Layout.EntranceCell))
                {
                    var before = a.Reachable(inst.Layout.EntranceCell);
                    var after = b.Standable(inst.Layout.EntranceCell) ? b.Reachable(inst.Layout.EntranceCell) : new HashSet<Int3>();
                    blocked += before.Count(c => !after.Contains(c));
                }
            }
        double Median(List<double> l) => l.Count == 0 ? 0 : l.OrderBy(x => x).ElementAt(l.Count / 2);
        double medBuild = Median(build), medCost = Median(cost), worst = cost.DefaultIfEmpty(0).Max();
        var sigs = vocab.Values.ToList();
        int clashes = sigs.Count - sigs.Distinct().Count();
        GD.Print($"      ornament: {places} places, {string.Join(", ", kinds.Select(k => $"{k.Value} {k.Key}"))}; blocks added per place median {added.OrderBy(x => x).ElementAtOrDefault(added.Count / 2)}, most {added.DefaultIfEmpty(0).Max()}");
        GD.Print($"      ornament cost: median {medCost:0.00} ms against a median build of {medBuild:0} ms ({100 * medCost / Math.Max(1, medBuild):0.0}%), worst {worst:0.0} ms");
        GD.Print($"      ornament vocabularies: {vocab.Count} builders, {sigs.Distinct().Count()} vocabularies; e.g. " + string.Join(" | ", sigs.Distinct().Take(3)));
        Check(places > 0 && broken == 0 && floating == 0 && blocked == 0 && irregular == 0 && orphans == 0,
            $"every ornament is whole ({broken} broken), held ({floating} cells floating), clear of every way and walk ({blocked} cells lost), regular ({irregular} irregular), never alone ({orphans} orphans) (Ornament)");
        Check(medCost < 0.05 * medBuild && worst < 30, $"the ornament costs under 5% of the median place's build ({medCost:0.00} of {medBuild:0} ms) and under 30 ms on any place ({worst:0.0} ms) (Ornament)");
        Check(unlike == 0 && clashes == 0, $"each builder's vocabulary is the same on all its places ({unlike} unlike) and its own ({clashes} shared) (Ornament)");
    }
}
