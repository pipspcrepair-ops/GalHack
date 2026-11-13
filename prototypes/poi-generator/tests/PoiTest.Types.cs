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
                Real(s.Of("fuel").All(f => f.Box.Gap(terminal.Box) >= 300), $"fuel farm {s.Of("fuel").Min(f => f.Box.Gap(terminal.Box)):F0} m from the terminal");
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
                        if (first == "") first = $"{t.Id}: {a.Part.Id} {ground.Min(g => g.Box.Gap(a.Box)):F0} m from any laid ground";
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
