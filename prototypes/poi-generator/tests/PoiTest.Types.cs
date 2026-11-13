using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>Fresh seeds for a run: never pinned.</summary>
    static ulong FreshSeed(Random r) => (ulong)r.NextInt64(1, long.MaxValue);

    /// <summary>
    /// Every spawn of a type is its own place: several fresh sketches of each type all differ from each other by at least
    /// <see cref="FreshMargin"/> of their footprint's long side in where and how big their parts are (summed over the parts
    /// both have), and their counts or sizes are never all the same.
    /// </summary>
    void Fresh()
    {
        var rand = new Random();
        foreach (PlaceType t in _lib.Types)
        {
            var sketches = Enumerable.Range(0, 6).Select(_ => PlaceTypes.Plan(t, FreshSeed(rand), 16000, 8000)).ToList();
            double least = double.MaxValue;
            int same = 0;
            for (int a = 0; a < sketches.Count; a++)
                for (int b = a + 1; b < sketches.Count; b++)
                {
                    double d = Difference(sketches[a], sketches[b]) / Math.Max(sketches[a].Footprint.Long, 1f);
                    least = Math.Min(least, d);
                    if (d < FreshMargin) same++;
                }
            Check(same == 0, $"{t.Id}: 6 fresh spawns all differ (least difference {least:P0} of the long side, at least {FreshMargin:P0}); runways {string.Join(", ", sketches.Select(s => $"{s.Of("runway").First().Box.W:F0}"))} m, hangars {string.Join(", ", sketches.Select(s => s.Of("hangar").Count()))}");
        }
    }

    const double FreshMargin = 0.05;

    static string Boxes(Sketch s) => string.Join("; ", s.Items.Select(i => $"{i.Part.Id} {i.Box.X0:F0},{i.Box.Z0:F0}..{i.Box.X1:F0},{i.Box.Z1:F0}"));

    /// <summary>How far apart two sketches' parts are: each part's boxes matched in order, centre and size apart, summed; a part one has more of counts its whole size.</summary>
    static double Difference(Sketch a, Sketch b)
    {
        double sum = 0;
        foreach (TypePart p in a.Type.Parts)
        {
            var x = a.Of(p.Id).ToList();
            var y = b.Of(p.Id).ToList();
            for (int i = 0; i < Math.Max(x.Count, y.Count); i++)
            {
                if (i >= x.Count || i >= y.Count) { sum += (i < x.Count ? x[i] : y[i]).Box.Long; continue; }
                SketchBox p1 = x[i].Box, p2 = y[i].Box;
                sum += Math.Abs(p1.Cx - p2.Cx) + Math.Abs(p1.Cz - p2.Cz) + Math.Abs(p1.W - p2.W) + Math.Abs(p1.D - p2.D);
            }
        }
        return sum / Math.Max(1, a.Type.Parts.Count);
    }

    /// <summary>
    /// The airport is the size of a real one, on fresh seeds: a 1,500 to 2,500 m runway 45 m wide with shoulders; a 23 m
    /// taxiway; an apron at least 300 x 150 m; a terminal 120 to 200 m long; a tower 30 to 50 m tall that sees the runway;
    /// hangars 45 to 60 m across and 15 m or more high. And every type's parts hold their own ranges.
    /// </summary>
    void Scale()
    {
        var rand = new Random();
        var bad = new List<string>();
        int n = 0;
        foreach (PlaceType t in _lib.Types)
            for (int k = 0; k < 8; k++)
            {
                Sketch s = PlaceTypes.Plan(t, FreshSeed(rand), 16000, 8000);
                n++;
                foreach (SketchItem i in s.Items)
                    foreach (var (key, span) in i.Part.Size)
                        if (key != "len" && !span.Holds(i.Rolled[key]) || key == "len" && i.Rolled[key] < span.Lo - 0.5f) bad.Add($"{t.Id}.{i.Part.Id} {key} {i.Rolled[key]:F0} outside {span}");
                foreach (SketchItem i in s.Items.Where(i => i.Part.Kind == "building"))
                    if (Math.Abs(i.Box.W - i.Rolled["len"]) > 1 || Math.Abs(i.Box.D - i.Rolled["depth"]) > 1) bad.Add($"{t.Id}.{i.Part.Id} drawn {i.Box.W:F0} x {i.Box.D:F0}, rolled {i.Rolled["len"]:F0} x {i.Rolled["depth"]:F0}");
                // The tower sees the runway: nothing as tall stands between its cab and the runway's nearest edge.
                foreach (SketchItem tower in s.Of("tower"))
                {
                    SketchBox rw = s.Of("runway").First().Box;
                    float tz = tower.Box.Cz < rw.Cz ? rw.Z0 : rw.Z1;
                    foreach (SketchItem o in s.Items.Where(o => o.Part.Kind == "building" && o != tower && o.H >= tower.H))
                        if (o.Box.Crosses(tower.Box.Cx, tower.Box.Cz, Math.Clamp(tower.Box.Cx, rw.X0, rw.X1), tz)) bad.Add($"{t.Id}: {o.Part.Id} ({o.H:F0} m) blocks the tower's ({tower.H:F0} m) view of the runway");
                }
                if (t.Id != "airport") continue;
                SketchItem runway = s.Of("runway").First(), apron = s.Of("apron").First(), terminal = s.Of("terminal").First();
                void Real(bool ok, string what) { if (!ok) bad.Add($"airport: {what}"); }
                Real(runway.Rolled["len"] is >= 1500 and <= 2500 && runway.Rolled["w"] == 45 && runway.Box.D >= 60, $"runway {runway.Rolled["len"]:F0} x {runway.Rolled["w"]:F0} m ({runway.Box.D:F0} with shoulders)");
                Real(s.Of("taxiway").All(x => Math.Abs(x.Box.D - 23) < 0.5f), "taxiway 23 m");
                Real(apron.Box.W >= 300 && apron.Box.D >= 150, $"apron {apron.Box.W:F0} x {apron.Box.D:F0} m");
                Real(terminal.Box.W is >= 120 and <= 200, $"terminal {terminal.Box.W:F0} m long");
                Real(s.Of("tower").All(x => x.H is >= 30 and <= 50), $"tower {s.Of("tower").First().H:F0} m");
                Real(s.Of("hangar").Any() && s.Of("hangar").All(x => x.Box.W is >= 45 and <= 60 && x.H >= 15), $"hangars {string.Join(", ", s.Of("hangar").Select(x => $"{x.Box.W:F0} m span {x.H:F0} m high"))}");
                Real(s.Of("fuel").All(f => f.Box.Gap(terminal.Box) >= 300), $"fuel farm {s.Of("fuel").Min(f => f.Box.Gap(terminal.Box)):F0} m from the terminal ({Boxes(s)})");
                Real(s.Of("lights").Count() == 2, "approach lights off each end");
            }
        foreach (string b in bad.Distinct().Take(6)) GD.Print($"      {b}");
        Check(bad.Count == 0, $"every type's parts in their real ranges on {n} fresh sketches ({bad.Count} out)");
    }

    /// <summary>
    /// Yards: nothing standing (a building, a tank farm) shares ground with another or with the laid ground (runway, taxiways,
    /// apron, car park), and every building's front is on the apron or a car park within a short walk.
    /// </summary>
    void Yards()
    {
        var rand = new Random();
        int shared = 0, cut = 0, n = 0;
        string first = "";
        foreach (PlaceType t in _lib.Types)
            for (int k = 0; k < 8; k++)
            {
                Sketch s = PlaceTypes.Plan(t, FreshSeed(rand), 16000, 8000);
                n++;
                var standing = s.Items.Where(i => i.Part.Kind is "building" or "tanks").ToList();
                var ground = s.Items.Where(i => i.Part.Kind is "surface" or "road").ToList();
                foreach (SketchItem a in standing)
                {
                    foreach (SketchItem b in standing.Concat(ground))
                        if (a != b && a.Box.Overlaps(b.Box))
                        {
                            shared++;
                            if (first == "") first = $"{t.Id}: {a.Part.Id} on {b.Part.Id}";
                        }
                    if (a.Part.Kind == "building" && ground.Where(g => g.Part.Kind == "surface").Min(g => g.Box.Gap(a.Box)) > 40)
                    {
                        cut++;
                        if (first == "") first = $"{t.Id}: {a.Part.Id} {ground.Min(g => g.Box.Gap(a.Box)):F0} m from any laid ground ({Boxes(s)})";
                    }
                }
            }
        Check(shared == 0 && cut == 0, $"yards on {n} fresh sketches: {shared} things on each other or on laid ground, {cut} buildings cut off{(first != "" ? $" (first: {first})" : "")}");
    }

    /// <summary>What each world size gets of each type (for the owner to approve), and what a sketch costs: it is all a type adds to a world's load.</summary>
    void TypeSizes()
    {
        var rand = new Random();
        var clock = new Stopwatch();
        int sketches = 0;
        foreach (PlaceType t in _lib.Types)
        {
            GD.Print($"    {t.Id} (at most {t.Share:P0} of a world): world | runway m | footprint km | share | shortened | too big");
            foreach (int km in new[] { 8, 16, 32, 64 })
            {
                var all = new List<Sketch>();
                for (int k = 0; k < 20; k++)
                {
                    clock.Start();
                    all.Add(PlaceTypes.Plan(t, FreshSeed(rand), km * 1000.0, km * 500.0));
                    clock.Stop();
                    sketches++;
                }
                var rw = all.Select(s => s.Of("runway").First().Box.W).ToList();
                var fp = all.OrderBy(s => s.Hectares).ToList();
                double share = fp[^1].Hectares / 100.0 / (km * km * 0.5);
                GD.Print($"      {km} x {km / 2} km | {rw.Min():F0}..{rw.Max():F0} | {fp[0].Footprint.W / 1000:F1} x {fp[0].Footprint.D / 1000:F1} to {fp[^1].Footprint.W / 1000:F1} x {fp[^1].Footprint.D / 1000:F1} | up to {share:P1} | {all.Count(s => s.Fitted < 1)} of 20 | {all.Count(s => s.TooBig)}");
            }
        }
        double each = clock.Elapsed.TotalMilliseconds / sketches;
        Check(each < 5, $"a sketch costs {each:F2} ms ({sketches} made): what a type adds to a world's load");
    }
}

public partial class PoiTest
{
    /// <summary>A sketch's piece laid out as the world lays it out (its front, its world's age, its own plan).</summary>
    Layout BuildPiece(TypePiece p, ulong seed, TechLevel tech)
    {
        try { return PoiFactory.BuildLayout(_lib, _lib.Family("airfield")!, 0, Rarity.Rare, Hash.Combine(seed, Hash.Of(p.Plan.Id), (ulong)(p.OffsetX * 7919 + p.OffsetZ)), new LayoutHints(p.Front, tech, Site: p.Plan)); }
        catch (InvalidOperationException e) { throw new InvalidOperationException($"{p.Plan.Parts[0].Arg("what")} facing {p.Front} ({string.Join(" ", p.Plan.Parts[0].Args.Select(a => a.Key + " " + a.Value))}): {e.Message}"); }
    }

    /// <summary>A built piece's blocks in the turned sketch's frame (metres): every solid cell at or above the ground.</summary>
    static IEnumerable<(int X, int Y, int Z)> Standing(Layout l, TypePiece p) =>
        l.Blocks.Blocks().Where(b => b.Cell.Y >= 0 && BlockInfo.Collides(b.Block.Type)).Select(b => (b.Cell.X + p.OffsetX, b.Cell.Y, b.Cell.Z + p.OffsetZ));

    /// <summary>
    /// The airfields as built, on fresh seeds: every building laid out at its sketch's size and front (Scale: the terminal,
    /// tower and hangars measured on their blocks, a hangar's door a craft fits through), no two terminals or towers one plan
    /// (Fresh), and nothing built on anything else or on the laid ground (Yards), every way in on laid ground or open ground
    /// beside it, and what each costs the builder.
    /// </summary>
    void TypesBuilt()
    {
        var rand = new Random();
        var bad = new List<string>();
        var plans = new Dictionary<string, List<string>>();
        var cost = new Dictionary<string, List<double>>();
        int pieces = 0, shared = 0, onGround = 0;
        foreach (PlaceType t in _lib.Types)
            for (int k = 0; k < 3; k++)
            {
                ulong seed = FreshSeed(rand);
                Sketch s = PlaceTypes.Plan(t, seed, 32000, 16000);
                int turn = rand.Next(4);
                TechLevel tech = WorldProfile.Parse(t.Techs[rand.Next(t.Techs.Count)]);
                var ground = PlaceTypes.Grounds(s, turn).Select(g => g.Box).ToList();
                var owner = new Dictionary<(int, int, int), string>();
                foreach (TypePiece p in PlaceTypes.Pieces(s, turn))
                {
                    var clock = Stopwatch.StartNew();
                    Layout l = BuildPiece(p, seed, tech);
                    clock.Stop();
                    pieces++;
                    string what = p.Plan.Parts[0].Arg("what");
                    if (!cost.TryGetValue($"{t.Id}.{what}", out var list)) cost[$"{t.Id}.{what}"] = list = new List<double>();
                    list.Add(clock.Elapsed.TotalMilliseconds);
                    var cells = Standing(l, p).ToList();
                    if (cells.Count == 0) { bad.Add($"{t.Id}.{what}: nothing built"); continue; }
                    int x0 = cells.Min(c => c.X), x1 = cells.Max(c => c.X) + 1, z0 = cells.Min(c => c.Z), z1 = cells.Max(c => c.Z) + 1, top = cells.Max(c => c.Y) + 1;
                    // Fills its box (the tower's cab stands a block proud of its shaft).
                    float slack = what == "tower" ? 4.5f : 0.5f;
                    if (x0 < p.Box.X0 - slack || x1 > p.Box.X1 + slack || z0 < p.Box.Z0 - slack || z1 > p.Box.Z1 + slack || what is "terminal" or "hangar" && (x1 - x0 < p.Box.W - 2 || z1 - z0 < p.Box.D - 2))
                        bad.Add($"{t.Id}.{what} ({p.Front}) built {x0}..{x1} x {z0}..{z1}, its box {p.Box.X0:F0}..{p.Box.X1:F0} x {p.Box.Z0:F0}..{p.Box.Z1:F0}");
                    float L = p.Front is Dir.North or Dir.South ? x1 - x0 : z1 - z0;
                    if (t.Id == "airport")
                    {
                        void Real(bool ok, string why) { if (!ok) bad.Add($"airport.{what}: {why}"); }
                        if (what == "terminal") Real(L is >= 120 and <= 200 && top is >= 12 and <= 20, $"{L} m long, {top} m high");
                        if (what == "tower") Real(top is >= 30 and <= 50, $"{top} m high");
                        if (what == "hangar")
                        {
                            // The door: the widest run of open cells along its front at 4 m, and how high it stays open.
                            int door = 0, run = 0, high = 0;
                            var set = cells.ToHashSet();
                            for (int a = 0; a < L; a++)
                            {
                                var (cx, cz) = Front(p, a);
                                bool open = !set.Contains((cx, 4, cz));
                                run = open ? run + 1 : 0;
                                door = Math.Max(door, run);
                                if (open && a == (int)L / 2) while (!set.Contains((cx, high, cz)) && high < 40) high++;
                            }
                            Real(L is >= 45 and <= 60 && top >= 15 && door >= 30 && high >= 8, $"{L} m span, {top} m high, door {door} m wide and {high} m high");
                        }
                    }
                    if (what is "terminal" or "tower")
                    {
                        if (!plans.TryGetValue($"{t.Id}.{what}", out var sigs)) plans[$"{t.Id}.{what}"] = sigs = new List<string>();
                        sigs.Add(string.Join(" ", l.Modules.Select(m => $"{m.Purpose}{m.Shape.Occupied.Count}")));
                    }
                    foreach (var c in cells)
                    {
                        if (owner.TryGetValue(c, out string? o) && o != p.Plan.Id + p.OffsetX + "," + p.OffsetZ) shared++;
                        owner[c] = p.Plan.Id + p.OffsetX + "," + p.OffsetZ;
                    }
                    if (what is not ("fence" or "lights"))
                        onGround += cells.Count(c => c.Y <= 2 && ground.Any(g => c.X >= g.X0 && c.X < g.X1 && c.Z >= g.Z0 && c.Z < g.Z1));
                }
            }
        foreach (var (k, sigs) in plans)
            if (sigs.Distinct().Count() < sigs.Count) bad.Add($"{k}: {sigs.Count - sigs.Distinct().Count()} of {sigs.Count} spawns share a plan");
        foreach (string b in bad.Distinct().Take(8)) GD.Print($"      {b}");
        GD.Print("      builder time per piece: " + string.Join(", ", cost.Select(kv => $"{kv.Key} {kv.Value.Average():F0} ms (worst {kv.Value.Max():F0})")));
        Check(bad.Count == 0 && shared == 0 && onGround == 0, $"{pieces} pieces of fresh airfields built at their sketches' sizes ({bad.Count} out), {shared} cells shared between pieces, {onGround} built on laid ground");
    }

    /// <summary>A cell along a piece's front edge, a metres along it, one cell inside.</summary>
    static (int X, int Z) Front(TypePiece p, int a) => p.Front switch
    {
        Dir.North => ((int)p.Box.X0 + a, (int)p.Box.Z0),
        Dir.South => ((int)p.Box.X1 - 1 - a, (int)p.Box.Z1 - 1),
        Dir.East => ((int)p.Box.X1 - 1, (int)p.Box.Z0 + a),
        _ => ((int)p.Box.X0, (int)p.Box.Z1 - 1 - a),
    };
}
