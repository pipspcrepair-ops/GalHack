using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Poi;
using PlanetSurface.World;
using PoiGenerator.Gen;

namespace PlanetSurface;

public partial class SurfaceTest
{
    /// <summary>
    /// The airfield on real worlds, fresh seeds, a world each: sited from its sketch with no layout made while the world
    /// loads; graded level across all of it and eased out to the land with no wall or pit at its edge; the world's road
    /// at its gate; then every part built as the builder builds it, measured on its blocks against its type's real ranges,
    /// nothing of one part in another or on the laid ground, and what the builder spends on it.
    /// </summary>
    async Task AirportChecks()
    {
        var rand = new Random();
        string forced = PoiSiting.ForceFamily;
        int worlds = 0, sited = 0, loadLayouts = 0, roads = 0, shared = 0, onGround = 0, walks = 0, walked = 0;
        float worstOff = 0f, worstGrade = 0f;
        string worstAt = "", firstOn = "";
        var bad = new List<string>();
        var builder = new List<double>();
        var notes = new List<string>();
        try
        {
            PoiSiting.ForceFamily = "airfield";
            for (int k = 0; k < 3; k++)
            {
                var spec = PlanetSpec.Roll((ulong)rand.NextInt64(1, long.MaxValue), Archetype.Temperate, 0);
                PoiSiting.ForceType = k == 1 ? "airstrip" : "airport";
                PoiSite.LoadLayouts = 0;
                PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
                worlds++;
                loadLayouts += PoiSite.LoadLayouts;
                TypedPlace place = w.Pois.Select(p => p.Whole).FirstOrDefault(t => t != null);
                if (place == null) { notes.Add($"{spec}: no airfield sited (last search: {PoiSiting.LastTypedRejects})"); continue; }
                sited++;
                notes.Add($"{spec} ({w.Map.WidthMeters / 1000:F0} x {w.Map.HeightMeters / 1000:F0} km): {place.Why}; sited in {PoiSiting.LastTypedMs:F0} ms; {place.Parts.Count} parts, {place.Parts.Count(p => p.Pending)} not laid out at load");
                if (w.Infra.Routes.Any(r => r.From == place.Anchor || r.To == place.Anchor)) roads++;
                else notes.Add($"{spec}: no road to the gate");
                // Level across it; eased out past its edge, never steeper than a bank you could drive up.
                SketchBox fb = PlaceTypes.Turn(place.Sketch.Footprint, place.Turn);
                PlanetTerrainSource g = w.Source;
                double W = w.Map.WidthMeters;
                for (int i = 0; i <= 40; i++)
                    for (int j = 0; j <= 10; j++)
                    {
                        double x = Mathx.WrapX(place.X + fb.X0 + fb.W * i / 40.0, W), z = place.Z + fb.Z0 + fb.D * j / 10.0;
                        worstOff = MathF.Max(worstOff, MathF.Abs(g.SurfaceHeight(x, z) - place.PadY));
                    }
                // The grading alone (the world's road to the gate has banks of its own).
                var graded = new PlanetTerrainSource(w.Map, place.Parts.SelectMany(p => p.Stamps).Distinct().ToList());
                float reach = place.Stamp.Blend + 20f;
                for (int a = 0; a < 96; a++)
                {
                    // Straight out from every side, 24 lines a side, from just past its apron.
                    double t = (a % 24) / 23.0;
                    var (ux, uz, ex, ez) = (a / 24) switch
                    {
                        0 => (0.0, -1.0, fb.X0 + fb.W * t, fb.Z0 - 4.0), 1 => (0.0, 1.0, fb.X0 + fb.W * t, fb.Z1 + 4.0),
                        2 => (-1.0, 0.0, fb.X0 - 4.0, fb.Z0 + fb.D * t), _ => (1.0, 0.0, fb.X1 + 4.0, fb.Z0 + fb.D * t),
                    };
                    (ex, ez) = (place.X + ex, place.Z + ez);
                    // (Measured against the land's own fall there: a canyon past it is the land's, not the grading's.)
                    float last = graded.SurfaceHeight(Mathx.WrapX(ex, W), ez), lastNatural = g.NaturalHeight(Mathx.WrapX(ex, W), ez);
                    for (float d = 4f; d <= reach; d += 4f)
                    {
                        double px = Mathx.WrapX(ex + ux * d, W), pz = ez + uz * d;
                        if (pz < 320 || pz > w.Map.HeightMeters - 320 || px < 320 || px > W - 320) break; // (a seam's ghost band is the seam's)
                        float h = graded.SurfaceHeight(px, pz), n = g.NaturalHeight(px, pz);
                        float over = MathF.Abs(h - last) / 4f - MathF.Abs(n - lastNatural) / 4f;
                        if (over > worstGrade) (worstGrade, worstAt) = (over, $"{px:F0},{pz:F0} {d:F0} m out");
                        (last, lastNatural) = (h, n);
                    }
                }
                // Built as the builder builds them, one by one on a worker.
                var clock = Stopwatch.StartNew();
                await Task.Run(() => { foreach (PoiSite p in place.Parts) p.Build(); });
                builder.Add(clock.Elapsed.TotalMilliseconds);
                var ground = place.Grounds(0f).ToList();
                var owner = new Dictionary<(int, int, int), PoiSite>();
                foreach (PoiSite p in place.Parts)
                {
                    PoiInstance inst = p.Built;
                    if (inst == null) { bad.Add($"{p.Name}: not built"); continue; }
                    var cells = inst.Blocks.Blocks().Where(b => b.Cell.Y >= 0 && BlockInfo.Collides(b.Block.Type) && b.Block.Role is not (Role.Debris or Role.Prop or Role.Growth)).Select(b => (X: p.Origin.X + b.Cell.X, Y: b.Cell.Y, Z: p.Origin.Z + b.Cell.Z)).ToList();
                    if (cells.Count == 0) continue;
                    int top = cells.Max(c => c.Y) + 1;
                    int sx = cells.Max(c => c.X) - cells.Min(c => c.X) + 1, sz = cells.Max(c => c.Z) - cells.Min(c => c.Z) + 1;
                    string kind = PlaceTypes.Pieces(place.Sketch, place.Turn)[place.Parts.IndexOf(p)].Plan.Parts[0].Arg("what");
                    // Walked as a body walks it: from the way in to the apron through the terminal, onto a hangar's floor, up the tower to its cab.
                    string goal = kind switch { "terminal" => "gate stand", "hangar" => "hangar floor", "tower" => "control cab", _ => null };
                    if (goal != null && inst.Layout.Modules.FirstOrDefault(md => md.Purpose == goal) is { } target)
                    {
                        var reach = inst.Nav().Reachable(inst.Layout.EntranceCell);
                        walks++;
                        if (target.Cells.Any(reach.Contains)) walked++;
                        else bad.Add($"{kind}: no way from its way in to its {goal}");
                    }
                    if (place.Sketch.Type.Id == "airport")
                    {
                        int along = Math.Max(sx, sz);
                        if (kind == "terminal" && !(along is >= 120 and <= 202 && top is >= 12 and <= 20)) bad.Add($"terminal {sx} x {sz} m, {top} m high");
                        if (kind == "tower" && top is < 30 or > 50) bad.Add($"tower {top} m high");
                        if (kind == "hangar" && !(Math.Min(sx, sz) >= 44 && top >= 15)) bad.Add($"hangar {sx} x {sz} m, {top} m high");
                    }
                    foreach (var c in cells)
                    {
                        if (owner.TryGetValue(c, out PoiSite o) && o != p) shared++;
                        owner[c] = p;
                        if (kind is "fence" or "lights" || c.Y > 2) continue;
                        if (ground.FirstOrDefault(q => Math.Abs(Mathx.WrapDelta(c.X + 0.5 - q.X, W) * q.UX + (c.Z + 0.5 - q.Z) * q.UZ) < q.HalfU - 1
                                         && Math.Abs(-Mathx.WrapDelta(c.X + 0.5 - q.X, W) * q.UZ + (c.Z + 0.5 - q.Z) * q.UX) < q.HalfV - 1) is { } on)
                        {
                            onGround++;
                            if (firstOn == "") firstOn = $" (first: {kind} at {c}, its stamp {p.Stamp.MinX}..{p.Stamp.MaxX} x {p.Stamp.MinZ}..{p.Stamp.MaxZ}, on the {on.Cover} {on.Pattern} layer {on.Layer} patch at {on.X:F0},{on.Z:F0} half {on.HalfU:F0} x {on.HalfV:F0})";
                        }
                    }
                }
                // The laid ground at its real sizes.
                var runway = ground.Where(q => q.Pattern == GroundPattern.Runway || q.Layer == 4).OrderByDescending(q => q.HalfU).FirstOrDefault();
                if (place.Sketch.Type.Id == "airport" && runway != null && !(runway.HalfU * 2 is >= 1500 and <= 2501 && runway.HalfV * 2 is >= 44 and <= 46))
                    bad.Add($"runway {runway.HalfU * 2:F0} x {runway.HalfV * 2:F0} m");
            }
        }
        finally { (PoiSiting.ForceFamily, PoiSiting.ForceType) = (forced, null); }
        foreach (string n in notes) GD.Print($"      {n}");
        foreach (string b in bad.Distinct().Take(8)) GD.Print($"      {b}");
        Check(sited == worlds, $"an airfield sited on {sited} of {worlds} fresh worlds");
        Check(loadLayouts == 0, $"its share of a new world's load is no layouts: {loadLayouts} made while the worlds loaded");
        Check(roads == sited, $"the world's road comes to its gate on {roads} of {sited}");
        Check(worstOff < 1.01f && worstGrade < 0.35f, $"graded: the ground across it within {worstOff:F2} m of its level, the run out to the land at most {worstGrade:P0} steeper than the land's own ({worstAt})");
        Check(walks > 0 && walked == walks, $"walked: from the landside way in through the terminal to the apron, onto each hangar's floor, up each tower to its cab: {walked} of {walks}");
        Check(bad.Count == 0 && shared == 0 && onGround == 0, $"built on its world at real sizes ({bad.Count} out), {shared} cells of one part in another, {onGround} built on its laid ground{firstOn}; the builder spent {(builder.Count > 0 ? builder.Average() : 0):F0} ms on all of an airfield's parts (worst {(builder.Count > 0 ? builder.Max() : 0):F0})");
    }
}
