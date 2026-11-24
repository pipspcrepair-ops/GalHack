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
    /// under 5% of the median place's build and no more than 5% of any place's own; each builder's vocabulary is its own.
    /// </summary>
    void OrnamentCheck()
    {
        var rand = new Random();
        int places = 0, broken = 0, floating = 0, blocked = 0, irregular = 0, orphans = 0, freeEnds = 0, thinParts = 0, standOff = 0, flat = 0, faceBars = 0;
        string unjoined = "";
        var build = new List<double>();
        double worstShare = 0;
        string worstPlace = "";
        var cost = new List<double>();
        var added = new List<int>();
        var kinds = new SortedDictionary<string, int>();
        var vocab = new Dictionary<ulong, string>();
        int unlike = 0;
        var perTech = new SortedDictionary<TechLevel, (int Places, int Pieces, int Carry3)>();
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
                double share = cost[^1] / Math.Max(1, build[^1]);
                if (cost[^1] > 15) GD.Print($"      ornament on {f.Id}: {cost[^1]:0.0} ms of a {build[^1]:0} ms build ({100 * share:0.0}%)");
                // (A pass of under 1 ms is inside the timer's noise on a 2 ms shed: its share says nothing.)
                if (cost[^1] >= 1 && share > worstShare) { worstShare = share; worstPlace = $"{f.Id} {cost[^1]:0.0} of {build[^1]:0} ms"; }
                added.Add(orn.Ornaments.Sum(p => p.Cells.Count));
                int elements = orn.Ornaments.Count(p => p.Kind != "bars");
                var tt = perTech.GetValueOrDefault(tech);
                perTech[tech] = (tt.Places + 1, tt.Pieces + elements, tt.Carry3 + (elements >= 3 ? 1 : 0));
                string sig = Ornament.For(orn).Sig;
                ulong who = Ornament.Builder(orn);
                if (vocab.TryGetValue(who, out string had) && had != sig) unlike++;
                vocab[who] = sig;
                foreach (var p in orn.Ornaments)
                {
                    kinds[p.Kind] = kinds.GetValueOrDefault(p.Kind) + 1;
                    if (p.Cells.Any(c => orn.Blocks.Get(c).IsAir)) broken++;
                    if (p.At.Count >= 2 && p.At.Zip(p.At.Skip(1), (a, b) => b - a).Distinct().Count() > 1) irregular++;
                    if (p.Kind is "beams" or "corbels" && p.At.Count < 2) orphans++;
                    // (A bar comes out of an edge: nothing solid beside its root but the concrete behind it.)
                    if (p.Kind == "bars") faceBars += p.Cells.Count(c => Dirs.All.Any(h => h != orn.Blocks.Get(c).Facing && BlockInfo.Collides(orn.Blocks.Get(c + h.Vec()).Type)));
                    var (free, thin, off) = Joined(orn.Blocks, orn.Layout, p);
                    (freeEnds, thinParts, standOff) = (freeEnds + free, thinParts + thin, standOff + off);
                    if (free + thin + off > 0 && unjoined.Length == 0) unjoined = $"{p.Kind} at {p.Cells[0]} in {f.Id}";
                }
                var mine = orn.Ornaments.SelectMany(p => p.Cells).ToHashSet();
                // Every face of it finished as its material is, where that material has a finish: none drawn in flat colour.
                var finished = orn.Ornaments.Where(p => p.Kind != "bars").SelectMany(p => p.Cells)
                    .Where(c => orn.Blocks.Look is { } look && look.Pick(orn.Blocks.Get(c), PoiGenerator.Gen.Side.Out, false, 0).Finish != PoiGenerator.Gen.Finish.Plain).ToHashSet();
                if (finished.Count > 0)
                    foreach (ChunkMesh m in Mesher.Build(orn.Blocks, inst.Layout.Occupied, new MeshOptions { Rock = false, Collision = false, Only = finished.Contains }))
                        foreach (Surface s in new[] { Surface.Matte, Surface.Metal })
                            for (int i = 0; i < m.Surfaces[(int)s].VertexCount; i++) if (m.Surfaces[(int)s].Codes[i * 2] == 0f) flat++;
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
                    blocked += before.Count(c => !after.Contains(c) && !mine.Contains(c)); // (its own cells aside: a rib on a wall's foot stands where feet could be, in the open, and cuts off nothing)
                }
            }
        double Median(List<double> l) => l.Count == 0 ? 0 : l.OrderBy(x => x).ElementAt(l.Count / 2);
        double medBuild = Median(build), medCost = Median(cost), worst = cost.DefaultIfEmpty(0).Max();
        var sigs = vocab.Values.ToList();
        int clashes = sigs.Count - sigs.Distinct().Count();
        GD.Print($"      ornament: {places} places, {string.Join(", ", kinds.Select(k => $"{k.Value} {k.Key}"))}; blocks added per place median {added.OrderBy(x => x).ElementAtOrDefault(added.Count / 2)}, most {added.DefaultIfEmpty(0).Max()}");
        GD.Print("      ornament elements per tech (not counting bars): " + string.Join("; ", perTech.Select(t => $"{t.Key} {t.Value.Pieces} on {t.Value.Places} places ({t.Value.Carry3} carry 3 or more)")));
        GD.Print($"      ornament cost: median {medCost:0.00} ms against a median build of {medBuild:0} ms ({100 * medCost / Math.Max(1, medBuild):0.0}%); slowest {worst:0.0} ms; highest share {100 * worstShare:0.0}% ({worstPlace})");
        GD.Print($"      ornament vocabularies: {vocab.Count} builders, {sigs.Distinct().Count()} vocabularies; e.g. " + string.Join(" | ", sigs.Distinct().Take(3)));
        Check(places > 0 && broken == 0 && floating == 0 && blocked == 0 && irregular == 0 && orphans == 0,
            $"every ornament is whole ({broken} broken), held ({floating} cells floating), clear of every way and walk ({blocked} cells lost), regular ({irregular} irregular), never alone ({orphans} orphans) (Ornament)");
        Check(places > 0 && freeEnds == 0 && thinParts == 0 && standOff == 0 && faceBars == 0 && flat == 0,
            $"every ornament is joined to the building it grows from: no free ends ({freeEnds}), none thinner than a beam ({thinParts}), none standing off a face ({standOff}), no bar rooted in a face ({faceBars}), no face drawn in flat colour where its material has a finish ({flat} face corners){(unjoined.Length > 0 ? $"; first: {unjoined}" : "")} (Ornament)");
        Check(medCost < 0.05 * medBuild && worstShare <= 0.05, $"the ornament costs under 5% of the median place's build ({medCost:0.00} of {medBuild:0} ms) and no more than 5% of any place's own build ({100 * worstShare:0.0}%: {worstPlace}) (Ornament)");
        Check(unlike == 0 && clashes == 0, $"each builder's vocabulary is the same on all its places ({unlike} unlike) and its own ({clashes} shared) (Ornament)");
    }

    /// <summary>
    /// How an ornament joins the building (bars, broken off as they are, aside): its ends that meet nothing that holds them
    /// flush (a beam's two ends, a corbel's back and head, the foot of what rises: a chimney, a spire), its parts thinner than
    /// a beam (0.3 m), and its faces beside the building's own that its shape doesn't reach (standing off them).
    /// </summary>
    static (int Free, int Thin, int Off) Joined(BlockGrid g, Layout layout, Ornament.Piece p)
    {
        if (p.Kind == "bars") return (0, 0, 0);
        var cells = p.Cells.ToHashSet();
        // (The ground is held too: a rib or a pylon stands on the rock below the place.)
        bool Grounds(Block n, Int3 at) => Holds(n) || n.IsAir && !layout.Extras.InSpace && at.Y <= Assembler.GroundY && !layout.Occupied[at];
        static bool Holds(Block n) => !n.IsAir && n.Role != Role.Decor && (BlockInfo.IsFullCube(n.Type) || BlockInfo.IsShaped(n.Type) || BlockInfo.IsSlope(n.Type) || BlockInfo.IsCornerSlope(n.Type));
        bool Reaches(Block b, Int3 c, Dir d) => Shapes.FlatToward(b, d) || b.Type == BlockType.Spike && d == Dir.Down
            || Shapes.For(b, c, g).Any(x => d switch { Dir.North => x.Lo.Z < 0.001f, Dir.South => x.Hi.Z > 0.999f, Dir.West => x.Lo.X < 0.001f, Dir.East => x.Hi.X > 0.999f, Dir.Up => x.Hi.Y > 0.999f, _ => x.Lo.Y < 0.001f });
        int free = 0, thin = 0, off = 0;
        foreach (Int3 c in p.Cells)
        {
            Block b = g.Get(c);
            // (A box's second-widest side is its thickness.)
            if (Shapes.For(b, c, g).Any(x => new[] { x.Hi.X - x.Lo.X, x.Hi.Y - x.Lo.Y, x.Hi.Z - x.Lo.Z }.OrderBy(s => s).ElementAt(1) < 0.29f)) thin++;
            foreach (Dir d in Dirs.Horizontal)
                if (!cells.Contains(c + d.Vec()) && Holds(g.Get(c + d.Vec())) && !Reaches(b, c, d)) off++;
            // (A cornice is held by the wall it stands against: some side of it meets one.)
            if (p.Kind == "cornice") { if (!Dirs.Horizontal.Any(d => Holds(g.Get(c + d.Vec())))) free++; continue; }
            IEnumerable<Dir> ends = p.Kind switch
            {
                "beams" => new[] { b.Facing, b.Facing.Opposite() },
                "corbels" => new[] { b.Facing, Dir.Up },
                _ => new[] { Dir.Down },
            };
            foreach (Dir d in ends)
            {
                Int3 n = c + d.Vec();
                if (cells.Contains(n)) continue;
                if (g.Get(n).IsAir && d == Dir.Down && Grounds(g.Get(n), n)) continue;
                if (!Reaches(b, c, d) || !Holds(g.Get(n)) || !Shapes.FlatToward(g.Get(n), d.Opposite()) && !BlockInfo.IsFullCube(g.Get(n).Type)) free++;
            }
        }
        return (free, thin, off);
    }
}
