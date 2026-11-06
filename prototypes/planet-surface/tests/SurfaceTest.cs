using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Player;
using PlanetSurface.Poi;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using PoiGenerator.Gen;
using TerrainDigging.Terrain;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;
using TInt3 = TerrainDigging.Terrain.Int3;
using PInt3 = PoiGenerator.Gen.Int3;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface;

/// <summary>
/// Headless tests of the planet surface. Run with:
///   godot --headless --path prototypes/planet-surface res://tests/SurfaceTest.tscn
/// Drives the real scene (Main) through streaming, digging, saving, flying, walking across a
/// seam and the weather, and checks the pure parts directly. Exits 0 if every check passes.
/// -- --only=Heights,Flight runs just the named checks.
/// </summary>
public partial class SurfaceTest : Node
{
    int _failures;
    Main _main;
    readonly HashSet<string> _only = new();
    readonly Stopwatch _total = Stopwatch.StartNew();

    public override async void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--only=")) _only.UnionWith(arg["--only=".Length..].Split(','));
        // Planets save their edits when you leave them: start from untouched planets every run, in a
        // save folder of this run's own. Another run saving the same test planet into a shared
        // folder at the same moment made this run land on its edits ("dug 0 cells": already dug).
        // env.ps1 now gives each checkout its own user://, but two runs in one checkout (the gate
        // and a run by hand) still share it, so each run keeps its own folder.
        PlanetWorld.SaveDirectory = $"user://test-planets/{OS.GetProcessId()}";
        string saves = ProjectSettings.GlobalizePath(PlanetWorld.SaveDirectory);
        if (System.IO.Directory.Exists(saves)) System.IO.Directory.Delete(saves, recursive: true);
        try
        {
            await Run("Weather", WeatherChecks);
            await Run("Relief", ReliefChecks);
            await Run("Pois", PoiChecks);
            await Run("Profile", ProfileChecks);
            await Run("Infra", InfraChecks);
            await Run("StartTown", StartTownChecks);
            await Run("Under", UnderChecks);
            await Run("Water", WaterChecks);
            await Run("Stitches", StitchChecks);
            await Run("Blast", BlastChecks);
            if (_only.Contains("InfraScan")) await Run("InfraScan", InfraScan);
            _main = new Main { Interactive = false };
            AddChild(_main);
            await Run("Heights", Heights);
            await Run("Seams", Seams);
            await Run("Persistence", Persistence);
            await Run("Stuck", Stuck);
            await Run("Steps", Steps);
            await Run("Water", Water);
            await Run("Solids", Solids);
            await Run("Flight", Flight);
            await Run("NorthSeam", NorthSeam);
            await Run("WalkAcross", WalkAcross);
        }
        catch (Exception e)
        {
            Fail($"test crashed: {e}");
        }
        GD.Print($"total test time {_total.Elapsed.TotalSeconds:F1} s");
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        try { System.IO.Directory.Delete(saves, recursive: true); }
        catch (Exception) { } // already gone, or a file still held: it is this run's own folder, nothing else reads it
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async Task Run(string name, Func<Task> check)
    {
        if (_only.Count > 0 && !_only.Contains(name)) return;
        var watch = Stopwatch.StartNew();
        await check();
        GD.Print($"      ({name}: {watch.Elapsed.TotalSeconds:F1} s)");
    }

    // ---- Helpers --------------------------------------------------------------------------

    Task Frames(int n) => FramesAsync(n);

    async Task FramesAsync(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async Task PhysicsFrames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>
    /// Waits physics ticks until <paramref name="condition"/> holds, at most <paramref name="ticks"/>:
    /// for checks whose limit is simulation time ("out within four ticks"), which load can't stretch.
    /// </summary>
    async Task<bool> UntilTicks(Func<bool> condition, int ticks)
    {
        for (int i = 0; i < ticks && !condition(); i++) await PhysicsFrames(1);
        return condition();
    }

    async Task<bool> Until(Func<bool> condition, double seconds)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > seconds) return false;
            await Frames(1);
        }
        return true;
    }

    /// <summary>Lands on a planet (optionally at a spot) and waits until the player can walk.</summary>
    async Task<bool> Land(PlanetSpec spec, Vector3? at = null)
    {
        _main.SpawnOverride = at;
        _main.Arrive(spec);
        bool ok = await Until(() => _main.IsReady, 90);
        if (!ok) Fail($"landing on {spec} did not finish (pending {_main.Streamer.Pending})");
        return ok;
    }

    /// <summary>Waits for every voxel column around the focus to be meshed.</summary>
    Task<bool> Streamed(double seconds = 60) => Until(() => _main.Streamer.CompleteRadius >= _main.Streamer.VoxelRadius && _main.Streamer.Pending == 0, seconds);

    static System.Numerics.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>The surface of the density field in a column: where it crosses zero, coming down from the sky.</summary>
    static float FieldSurface(ChunkStore store, int x, int z, int top, int bottom)
    {
        float above = store.DensityAt(new TInt3(x, top, z));
        for (int y = top - 1; y >= bottom; y--)
        {
            float d = store.DensityAt(new TInt3(x, y, z));
            if (d > 0f && above <= 0f) return y + 0.5f + d / (d - above);
            above = d;
        }
        return float.NaN;
    }

    // ---- 1. Streamed heights match the generator -----------------------------------------

    async Task Heights()
    {
        var spec = PlanetSpec.Roll(4242, Archetype.Temperate, 0);
        if (!await Land(spec)) return;
        await Streamed();
        PlanetWorld w = _main.World;
        ChunkStore store = w.Terrain.Store;
        Vector3 p = _main.Player.GlobalPosition;
        var rng = new Planets.Gen.Rng(99);
        int compared = 0, skipped = 0;
        double worstField = 0, sumField = 0, worstMesh = 0, sumMesh = 0;
        int meshCompared = 0;
        var space = _main.GetWorld3D().DirectSpaceState;
        for (int k = 0; k < 300; k++)
        {
            float x = p.X + (rng.NextFloat() - 0.5f) * 140f, z = p.Z + (rng.NextFloat() - 0.5f) * 140f;
            int cx = (int)MathF.Floor(x), cz = (int)MathF.Floor(z);
            double wx = Mathx.WrapX(cx + 0.5, w.Width), wz = Mathx.Wrap(cz + 0.5, w.Height);
            // Compare natural ground only: pads, cave mouths and river channels are meant to differ.
            float natural = w.Source.NaturalHeight(wx, wz);
            GroundSample g = w.Source.Sampler.Sample(wx, wz, 1);
            // (A stamp changes the ground there: a pad, its blend, a road's bed and banks.)
            bool stamped = w.Source.StampWeight(wx, wz) > 0f || Math.Abs(w.Source.SurfaceHeight(wx, wz) - natural) > 0.001f;
            if (stamped || g.InRiver || w.Source.CellDensity(cx, (int)MathF.Floor(natural - 0.5f), cz) <= 0f) { skipped++; continue; }
            float field = FieldSurface(store, cx, cz, (int)MathF.Ceiling(natural) + 6, (int)MathF.Floor(natural) - 6);
            if (float.IsNaN(field)) { skipped++; continue; }
            double d = Math.Abs(field - natural);
            worstField = Math.Max(worstField, d);
            sumField += d;
            compared++;
            // The mesh players walk on: ray down onto the collision within reach of the player.
            if (Math.Abs(x - p.X) < 28 && Math.Abs(z - p.Z) < 28)
            {
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(cx + 0.5f, natural + 20f, cz + 0.5f), new Vector3(cx + 0.5f, natural - 20f, cz + 0.5f), Layers.Terrain));
                if (hit.Count > 0)
                {
                    double m = Math.Abs(((Vector3)hit["position"]).Y - natural);
                    worstMesh = Math.Max(worstMesh, m);
                    sumMesh += m;
                    meshCompared++;
                }
            }
        }
        Check(compared > 150 && worstField < 0.05 && meshCompared > 20 && sumMesh / Math.Max(1, meshCompared) < 0.15 && worstMesh < 0.6,
            $"streamed heights match the planet generator: {compared} columns (density surface off by {sumField / Math.Max(1, compared):F4} m mean, " +
            $"{worstField:F4} m worst); {meshCompared} rays onto the walkable mesh off by {sumMesh / Math.Max(1, meshCompared):F3} m mean, {worstMesh:F3} m worst " +
            $"({skipped} skipped on pads, caves and rivers)");

        // Far rings: their samples are the generator's at their own spacing.
        float far = w.Source.SurfaceHeight(p.X + 400, p.Z + 400, 8), gen = w.Source.Sampler.Sample(Mathx.WrapX(p.X + 400, w.Width), Mathx.Wrap(p.Z + 400, w.Height), 8).Height;
        Check(Math.Abs(far - gen) < 1e-3 || w.Source.StampWeight(p.X + 400, p.Z + 400) > 0, $"far rings sample the generator directly ({far:F2} vs {gen:F2} m at 8 m spacing)");
    }

    // ---- 2. Relief per archetype -----------------------------------------------------------

    /// <summary>The same floors as the planet generator's relief check, measured on the surface's own ground (pads included).</summary>
    static readonly (float range, float s30, float s45, float canyon)[] Floors =
    {
        (450, 0.3f, 0.15f, 35), (300, 0.3f, 0.15f, 30),   // temperate
        (150, 0.12f, 0.04f, 18), (220, 0.15f, 0.1f, 60),  // arid
        (450, 0.3f, 0.15f, 35), (250, 0.2f, 0.08f, 22),   // ice
        (300, 0.2f, 0.08f, 22), (250, 0.25f, 0.1f, 18),   // lava
        (250, 0.25f, 0.1f, 30), (140, 0.15f, 0.06f, 20),  // alien: crystal, and gentler domed fungal country
    };

    async Task ReliefChecks()
    {
        for (int a = 0; a < 5; a++)
        for (int v = 0; v < 2; v++)
        {
            var spec = PlanetSpec.Roll((ulong)(510 + a * 3 + v), (Archetype)a, v);
            PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
            Relief.Stats r = Relief.Measure(w.Map, (x, y, s) => w.Source.SurfaceHeight(x, y, s), 1, windows: 6, slopePoints: 2000, incisionPoints: 400);
            var f = Floors[a * 2 + v];
            Check(r.RangeMedian >= f.range && r.Slope30 >= f.s30 && r.Slope45 >= f.s45 && r.Incision95 >= f.canyon,
                $"relief on the surface {(Archetype)a}/{w.Map.Def.VariantName}: {r}");
            await Frames(1);
        }
    }

    // ---- 3. POIs are seated ----------------------------------------------------------------

    async Task PoiChecks()
    {
        int sites = 0, floating = 0, doors = 0, corners = 0, overlaps = 0, gates = 0, steep = 0;
        int roomsFilled = 0;
        int madeChecked = 0, wrongGround = 0, grassInDesert = 0, hardEdges = 0;
        var shapes = new Dictionary<StampShape, int>();
        double widest = 1;
        bool same = false;
        var kinds = new Dictionary<string, int>();
        var byKind = new Dictionary<string, int>();
        var families = new HashSet<string>();
        var notes = new List<string>();
        var blockedWays = new List<string>();
        for (int a = 0; a < 5; a++)
        {
            var spec = PlanetSpec.Roll((ulong)(800 + a * 7), (Archetype)a, 0);
            PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
            ChunkStore store = w.Terrain.Store;
            // Spread over the planet: the farthest seated place from the landing site, as a share of the way to its far side.
            double far = 0;
            foreach (PoiSite site in w.Pois)
            {
                double fx = Mathx.WrapDelta(site.Center.X - w.Landing.X, w.Width), fz = Mathx.WrapDelta(site.Center.Z - w.Landing.Y, w.Height);
                far = Math.Max(far, Math.Sqrt(fx * fx + fz * fz) / (0.5 * Math.Sqrt(w.Width * w.Width + w.Height * w.Height)));
                families.Add(site.Family.Id);
            }
            widest = Math.Min(widest, far);
            if (a == 0)
            {
                // The same planet seats the same places, cell for cell.
                PlanetWorld again = PlanetWorld.Build(spec, Main.Catalog);
                same = again.Pois.Count == w.Pois.Count && again.Pois.Zip(w.Pois).All(p => p.First.Family.Id == p.Second.Family.Id && p.First.Origin.Equals(p.Second.Origin) && p.First.Gates.Count == p.Second.Gates.Count);
            }
            foreach (PoiSite site in w.Pois)
            {
                sites++;
                kinds[site.Siting] = kinds.TryGetValue(site.Siting, out int n) ? n + 1 : 1;
                PoiInstance inst = site.Instance;
                CellMask occ = inst.Layout.Occupied;
                TerrainStamp t = site.Stamp;
                int pad = site.Origin.Y;

                // Corners: the ground meets the building at floor level at every corner of the footprint.
                foreach (var (cx, cz) in new[] { (t.MinX - 1, t.MinZ - 1), (t.MaxX, t.MinZ - 1), (t.MinX - 1, t.MaxZ), (t.MaxX, t.MaxZ) })
                {
                    float ground = FieldSurface(store, cx, cz, pad + 40, pad - 40);
                    if (float.IsNaN(ground) || (!site.HalfBuried && Math.Abs(ground - pad) > 0.35f)) corners++;   // a half-buried ruin sits in rising ground by design
                }
                // Nothing floats: everywhere the POI generator builds on rock (below floor level and
                // outside the place's own rooms), there is solid ground, all the way down its deepest level.
                for (int y = occ.Min.Y; y < 0; y++)
                for (int z = 0; z < occ.Size.Z; z++)
                for (int x = 0; x < occ.Size.X; x++)
                {
                    var local = new PInt3(occ.Min.X + x, y, occ.Min.Z + z);
                    if (occ[local]) continue;
                    if (!store.IsSolid(site.World(local))) floating++;
                }
                // A half-buried ruin keeps every room open however deep the ground rises round it.
                if (site.HalfBuried)
                    for (int y = 0; y < occ.Min.Y + occ.Size.Y; y++)
                    for (int z = 0; z < occ.Size.Z; z++)
                    for (int x = 0; x < occ.Size.X; x++)
                    {
                        var local = new PInt3(occ.Min.X + x, y, occ.Min.Z + z);
                        if (occ[local] && inst.Blocks.Get(local).IsAir && store.IsSolid(site.World(local))) roomsFilled++;
                    }
                foreach (TerrainStamp extraStamp in site.Extra) shapes[extraStamp.Shape] = shapes.TryGetValue(extraStamp.Shape, out int sc) ? sc + 1 : 1;
                // Made ground: the surface its purpose calls for; never grass on a desert world; eased in, never a hard edge.
                madeChecked++;
                if (t.Surface != PoiSiting.GroundFor(site.Family, (Archetype)a)) wrongGround++;
                var (mcx, mcz) = site.Center;
                Color centre = w.Palette.Made(w.Palette.Natural(mcx, mcz, pad, 1f, out _), t.Surface, 1f, t.Wear, mcx, mcz);
                if (a == (int)Archetype.Arid && centre.G > centre.R * 1.08f && centre.G > centre.B * 1.08f) grassInDesert++;
                for (int ray = 0; ray < 8; ray++)
                {
                    double ra = ray * Math.PI / 4, first = -1, last = -1;
                    for (double d = 0; d < Math.Max(t.ReachX, t.ReachZ); d += 0.5)
                    {
                        // The place's own pad (a road leaving its gate has edges of its own).
                        float mw = t.Weight(mcx + Math.Cos(ra) * d, mcz + Math.Sin(ra) * d);
                        if (mw < 0.97f && first < 0) first = d;
                        if (mw <= 0.5f) { last = d; break; }
                    }
                    if (first >= 0 && last >= 0 && last - first < 2.5) hardEdges++;
                }
                // Nothing buried: no block above the pad shares a cell with ground.
                for (int y = 0; y < occ.Min.Y + occ.Size.Y; y++)
                for (int z = 0; z < occ.Size.Z; z++)
                for (int x = 0; x < occ.Size.X; x++)
                {
                    var local = new PInt3(occ.Min.X + x, y, occ.Min.Z + z);
                    if (!site.HalfBuried && !inst.Blocks.Get(local).IsAir && store.IsSolid(site.World(local))) overlaps++;
                }
                // The way in: open from the floor to head height, with ground under it.
                TInt3 e = site.Entrance;
                bool open = !store.IsSolid(e) && !store.IsSolid(e + TInt3.Up) && !store.IsSolid(e + TInt3.Up + TInt3.Up);
                bool footing = store.IsSolid(e - TInt3.Up) || !inst.Blocks.Get(inst.Layout.EntranceCell + PInt3.Down).IsAir;
                if (!open || !footing) { doors++; if (blockedWays.Count < 4)
                    {
                        double ex = e.X + 0.5, ez = e.Z + 0.5;
                        float hh = w.Source.NaturalHeight(ex, ez);
                        var steps = new List<string> { $"natural {hh:F2}" };
                        foreach (TerrainStamp st in w.Source.Stamps)
                        {
                            float before = hh;
                            hh = st.Apply(hh, ex, ez);
                            if (hh != before) steps.Add($"{st.Name}/{st.Shape} {hh:F2}");
                        }
                        blockedWays.Add($"{site.Name} way in {(open ? "has no footing" : "is not open")} at {e} (solid {store.IsSolid(e)} {store.IsSolid(e + TInt3.Up)} {store.IsSolid(e + TInt3.Up + TInt3.Up)}; {string.Join(", ", steps)}; surface {w.Source.SurfaceHeight(ex, ez):F2})");
                    }
                }
                // Every way in can be walked up to from outside: straight out from it, past the apron
                // and the blend, the ground never climbs or drops more than a body can walk (45 degrees)
                // except where the natural ground beyond the stamp is that steep anyway.
                foreach (PoiGate g in site.Gates)
                {
                    if (g.Kind == "breach") continue; // a breach is a climb down a rubble slope by design
                    gates++;
                    double reach = Math.Max(t.ReachX, t.ReachZ) + 10;
                    // A way up to it: a walk on a 2 m grid from the gate out past everything this place
                    // stamped, never stepping more than a body walks (45 degrees). Natural cliffs are
                    // obstacles to walk round, as they would be for a player.
                    const int Cell = 2;
                    int half = (int)Math.Ceiling(reach / Cell) + 2, size = 2 * half + 1;
                    var seen = new bool[size * size];
                    var heightAt = new float[size * size];
                    for (int j = 0; j < size; j++)
                        for (int i = 0; i < size; i++)
                            heightAt[j * size + i] = w.Source.SurfaceHeight(g.Cell.X + 0.5 + (i - half) * Cell, g.Cell.Z + 0.5 + (j - half) * Cell, Cell);
                    var queue = new Queue<int>();
                    queue.Enqueue(half * size + half);
                    seen[half * size + half] = true;
                    bool walkable = false;
                    string worst = "boxed in";
                    while (queue.Count > 0 && !walkable)
                    {
                        int cur = queue.Dequeue(), ci = cur % size, cj = cur / size;
                        double away = Math.Sqrt((ci - half) * (ci - half) + (cj - half) * (cj - half)) * Cell;
                        if (away >= reach) { walkable = true; break; }
                        foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int ni = ci + di, nj = cj + dj;
                            if (ni < 0 || nj < 0 || ni >= size || nj >= size) continue;
                            int next = nj * size + ni;
                            if (seen[next] || Math.Abs(heightAt[next] - heightAt[cur]) > 1.05f * Cell) continue;
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                    if (!walkable) { steep++; if (notes.Count < 16) notes.Add($"no walkable approach to {site.Family.Id} {g.Kind}: {worst}"); }
                }
                byKind[site.Siting] = byKind.TryGetValue(site.Siting, out int gk) ? gk + site.Gates.Count : site.Gates.Count;
                if (notes.Count < 12) notes.Add($"{site.Family.Id} on {site.Siting} ground (cut/fill {site.CutFill:F1} m)");
            }
            await Frames(1);
        }
        GD.Print("      " + string.Join("; ", notes));
        Check(wrongGround == 0 && grassInDesert == 0 && hardEdges == 0,
            $"made ground: {madeChecked} pads laid in the surface their purpose calls for (tarmac and concrete aprons, gravel and dirt yards, the land's own ground for ruins); no grass on a desert world ({grassInDesert}); every edge eased over metres, never a hard line ({hardEdges})");
        Check(roomsFilled == 0 && shapes.Count >= 2, $"places sit in their land, not on flat pads: {string.Join(", ", shapes.Select(s => $"{s.Value} {s.Key.ToString().ToLowerInvariant()}"))} stamps beyond the pads; half-buried ruins keep their rooms open ({roomsFilled} room cells filled)");
        Check(sites >= 10 && floating == 0 && overlaps == 0 && doors == 0 && corners == 0,
            $"POIs seated: {sites} places on 5 worlds ({string.Join(", ", kinds.Select(k => $"{k.Value} {k.Key}"))}); " +
            $"{corners} corners off floor level, {floating} cells of missing ground under and around their rooms, {overlaps} blocks buried in ground, {doors} blocked ways in{(blockedWays.Count > 0 ? ": " + string.Join("; ", blockedWays) : "")}");
        Check(gates >= sites && steep == 0,
            $"every way in can be walked up to: {gates} entrances and gates ({string.Join(", ", byKind.Select(k => $"{k.Value} on {k.Key} ground"))}), each with a walk on foot out past everything the place stamped, no step steeper than 45 degrees");
        Check(same, "the same planet seats the same places at the same cells with the same ways in");
        Check(widest >= 0.45, $"places spread over the whole planet, not just the landing site: on every world the farthest is {widest:P0} or more of the way to the far side; {families.Count} families seated");
    }

    // ---- 4. Seams: chunk borders, ghost copies, the corner ---------------------------------

    async Task Seams()
    {
        PlanetWorld w = _main.World;
        if (w == null) return;
        // Neighbouring chunks share their border vertices bit for bit.
        Vector3 p = _main.Player.GlobalPosition;
        int fcx = (int)MathF.Floor(p.X / 16), fcz = (int)MathF.Floor(p.Z / 16);
        int pairs = 0, shared = 0, mismatched = 0;
        foreach (var (dx, dz) in new[] { (0, 0), (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1) })
        for (int cy = -8; cy < 40; cy++)
        {
            var a = new TInt3(fcx + dx, cy, fcz + dz);
            var b = new TInt3(a.X + 1, cy, a.Z);
            ArrayMesh ma = _main.Streamer.MeshOf(a, out Vector3 oa), mb = _main.Streamer.MeshOf(b, out Vector3 ob);
            if (ma == null || mb == null || oa != Vector3.Zero || ob != Vector3.Zero) continue;
            pairs++;
            // Surface Nets puts one vertex in each dual cube the surface crosses. The cubes that
            // straddle the border belong to both chunks: wherever both built one, it must be the same.
            int border = b.X * 16 - 1;
            var inB = new Dictionary<(int, int, int), Vector3>();
            foreach (Vector3 v in (Vector3[])mb.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex])
            {
                var cube = ((int)MathF.Floor(v.X - 0.5f), (int)MathF.Floor(v.Y - 0.5f), (int)MathF.Floor(v.Z - 0.5f));
                if (cube.Item1 == border) inB[cube] = v;
            }
            foreach (Vector3 v in (Vector3[])ma.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex])
            {
                var cube = ((int)MathF.Floor(v.X - 0.5f), (int)MathF.Floor(v.Y - 0.5f), (int)MathF.Floor(v.Z - 0.5f));
                if (cube.Item1 != border || !inB.TryGetValue(cube, out Vector3 other)) continue;
                shared++;
                if (other != v) mismatched++;
            }
        }
        Check(pairs > 4 && shared > 50 && mismatched == 0,
            $"chunk seams: {pairs} neighbouring chunk pairs share {shared} border vertices, {mismatched} differ by even a bit");
        await Frames(1);
    }

    // ---- 5. Edits persist when chunks unload and reload, and across planet switches ---------

    async Task Persistence()
    {
        PlanetWorld w = _main.World;
        if (w == null) return;
        Walker player = _main.Player;
        // The ground round the player streamed in with its collision, and the player standing on it.
        await Until(() => _main.Streamer.ReadyAround(player.GlobalPosition, 1, needCollision: true) && player.IsOnFloor(), 60);
        Vector3 at = player.GlobalPosition + new Vector3(6, 0, 0);
        var target = new TInt3((int)MathF.Floor(at.X), (int)MathF.Floor(w.GroundHeight(at.X, at.Z)) - 1, (int)MathF.Floor(at.Z));
        PlanetEdit edit = _main.Dig(target, 1.42f);
        TInt3 chunk = ChunkStore.ChunkOf(target);
        bool remeshed = await Until(() => _main.Streamer.TryGetHash(chunk, out _) , 10);
        _main.Streamer.TryGetHash(chunk, out ulong before);
        int removed = w.Terrain.Store.RemovedCount;

        // Fly far away: the chunk's mesh and generated cells are dropped...
        _main.ToggleFly();
        _main.Fly.Begin(player.GlobalPosition + new Vector3(900, 60, 0), 0, 0);
        bool unloaded = await Until(() => !_main.Streamer.IsLoaded(chunk), 20);
        int passes = _main.Streamer.EvictPasses;
        await Until(() => _main.Streamer.EvictPasses > passes, 10); // past an eviction pass
        int baseChunks = w.Terrain.Store.Base.Count;
        // ...and come back: it regenerates from the seed plus the edit set, identically.
        _main.Fly.Begin(player.GlobalPosition + Vector3.Up * 2, 0, 0);
        await Streamed();
        bool back = await Until(() => _main.Streamer.TryGetHash(chunk, out _), 20);
        _main.Streamer.TryGetHash(chunk, out ulong after);
        Check(edit.Removed.Count > 0 && remeshed && unloaded && back && before == after && w.Terrain.Store.IsRemoved(target),
            $"an edit survives unload and reload: dug {edit.Removed.Count} cells, chunk {chunk} unloaded {unloaded} " +
            $"({baseChunks} generated chunks kept while away), mesh hash {before:X16} before and {after:X16} after");

        // Through terrain-digging's edit log: the edits round-trip byte for byte.
        EditLog log = w.Terrain.Store.ExportEdits();
        EditLog again = EditLog.Deserialize(log.Serialize());
        Check(again.ContentEquals(log) && log.Contains(target), $"edit log round trip: {log.CellCount} cells in {log.ChunkCount} chunks, identical after serialising");

        // Leave the planet and come back: the save made on the way out restores every edit.
        _main.ToggleFly();
        PlanetSpec home = w.Spec;
        Vector3 spot = player.GlobalPosition;
        if (!await Land(PlanetSpec.Roll(17, Archetype.Lava, 0))) return;
        if (!await Land(home, spot)) return;
        Check(_main.World.Terrain.Store.IsRemoved(target) && _main.World.Terrain.Store.RemovedCount == removed,
            $"leaving the planet and coming back restores its edits ({_main.World.Terrain.Store.RemovedCount} of {removed} cells)");
    }

    // ---- 6. Stuck safety --------------------------------------------------------------------

    async Task Stuck()
    {
        PlanetWorld w = _main.World;
        if (w == null) return;
        Walker player = _main.Player;
        ChunkStore store = w.Terrain.Store;
        await Streamed();

        // Buried by a teleport (a bad load, a network correction): out within a few ticks.
        Vector3 here = player.GlobalPosition;
        player.Teleport(new Vector3(here.X, w.GroundHeight(here.X, here.Z) - 3f, here.Z));
        bool freed = await UntilTicks(() => !_main.Guard.IsInside(store), 4);

        // Standing in a dug hole when a load brings back an older terrain without it.
        _main.Save();
        Vector3 spot = player.GlobalPosition + new Vector3(3, 0, 3);
        var floor = new TInt3((int)MathF.Floor(spot.X), (int)MathF.Floor(w.GroundHeight(spot.X, spot.Z)) - 2, (int)MathF.Floor(spot.Z));
        int rescuesBefore = _main.Guard.Rescues;
        // A deeper pit than the tool digs in one go: dig it cell by cell.
        var pit = new List<TInt3>();
        for (int y = 0; y < 4; y++) for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) pit.Add(new TInt3(floor.X + x, floor.Y + y, floor.Z + z));
        PlanetEdit dug = w.Terrain.DigCells(pit);
        _main.Streamer.MarkDirty(dug.DirtyChunks);
        await Until(() => _main.Streamer.Pending == 0, 10);
        // The pit's own collision rebuilt (its chunk's shape matches its new mesh) before stepping in.
        await Until(() => _main.Streamer.ReadyAround(new Vector3(floor.X + 0.5f, floor.Y, floor.Z + 0.5f), 0, needCollision: true), 10);
        player.Teleport(new Vector3(floor.X + 0.5f, floor.Y + 0.05f, floor.Z + 0.5f));
        // Standing on the pit floor, and a tick after it, so the guard has looked at them there.
        bool landed = await UntilTicks(() => player.IsOnFloor() && MathF.Abs(player.GlobalPosition.Y - floor.Y) < 0.3f, 10);
        await PhysicsFrames(1);
        bool inPit = landed && !_main.Guard.IsInside(store) && _main.Guard.Rescues == rescuesBefore;
        LoadResult loaded = _main.Load();
        bool afterLoad = await UntilTicks(() => !_main.Guard.IsInside(store) && _main.Guard.Rescues > rescuesBefore, 4);
        Check(freed && inPit && loaded.Ok && afterLoad && _main.Guard.Rescues > rescuesBefore,
            $"stuck safety: a buried player is freed ({freed}), standing in a fresh pit is left alone ({inPit}), " +
            $"a load that fills the pit moves them out ({afterLoad}); {_main.Guard.Rescues} rescues so far");
    }

    // ---- 6a. Steps -------------------------------------------------------------------------------

    /// <summary>
    /// The walker steps up onto anything a step tall, by the shared physics module's step-up and
    /// step height: walked into a sheer slab 0.5 m tall on level ground, it is up on top without a
    /// jump or a stop; the same slab 1 m tall is a wall.
    /// </summary>
    async Task Steps()
    {
        PlanetWorld w = _main.World;
        if (w == null) return;
        Walker player = _main.Player;
        await Streamed();

        // Level, dry, open ground near the player, 8 m of it eastward.
        Vector3 here = player.GlobalPosition;
        Vector3? found = null;
        for (float r = 0f; r < 300f && found == null; r += 6f)
            for (int k = 0; k < 16 && found == null; k++)
            {
                var p = new Vector3(here.X + MathF.Cos(k * MathF.Tau / 16) * r, 0f, here.Z + MathF.Sin(k * MathF.Tau / 16) * r);
                float lo = float.MaxValue, hi = float.MinValue;
                // Level along the walk and across it (not a hillside that happens to run level along x).
                for (float m = 0f; m <= 8f; m += 0.5f)
                for (float side = -0.6f; side <= 0.6f; side += 0.6f)
                {
                    float h = w.GroundHeight(Mathx.WrapX(p.X + m, w.Width), Mathx.Wrap(p.Z + side, w.Height));
                    lo = MathF.Min(lo, h);
                    hi = MathF.Max(hi, h);
                }
                // Open ground, or an old road's level deck (clear of its poles).
                bool ground = w.Source.StampWeight(p.X, p.Z) <= 0f
                              || w.Source.OnRoad(Mathx.WrapX(p.X, w.Width), Mathx.Wrap(p.Z, w.Height), out _) && w.Source.OnRoad(Mathx.WrapX(p.X + 8f, w.Width), Mathx.Wrap(p.Z, w.Height), out _)
                                 && !w.Infra.Power.Poles.Any(pp => Math.Abs(Mathx.WrapDelta(pp.X - p.X - 4, w.Width)) < 7 && Math.Abs(Mathx.WrapDelta(pp.Z - p.Z, w.Height)) < 4);
                bool open = hi - lo < 0.2f && lo > 0.5f && w.WaterSurfaceAt(p) == null && ground
                            && !_main.Streamer.SolidsNear(p + new Vector3(4f, 0f, 0f), 7f).Any();
                if (open) found = new Vector3(p.X, hi, p.Z);
            }
        if (found is not Vector3 start)
        {
            Fail("no level open ground within 300 m for the step check");
            return;
        }
        var slab = new StaticBody3D { Name = "Slab", CollisionLayer = Layers.Solid, CollisionMask = 0 };
        slab.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5f, 1.5f, 3f) } });
        _main.AddChild(slab);

        async Task<(float rise, bool past, int steps)> WalkAt(float height)
        {
            slab.GlobalPosition = start + new Vector3(5f, -2f, 0f); // out of the way, under the ground
            player.Teleport(start + Vector3.Up * 0.1f);
            player.SetLook(-MathF.PI / 2f, 0f); // facing east, at the slab
            // Held still until the ground there has streamed in, so it can't fall through ground not yet built.
            var mode = player.ProcessMode;
            player.ProcessMode = ProcessModeEnum.Disabled;
            await Until(() => _main.Streamer.ReadyAround(start, 1, needCollision: true), 60);
            player.ProcessMode = mode;
            await SolidsIn();
            await PhysicsFrames(2); // the teleport's stale floor contact gone
            await UntilTicks(() => player.IsOnFloor() && MathF.Abs(player.Velocity.Y) < 0.05f, 60);
            float y0 = player.GlobalPosition.Y;
            // The slab sunk into the ground so it stands `height` above where the player stands, from 2.5 m ahead.
            slab.GlobalPosition = new Vector3(start.X + 5f, y0 + height - 0.75f, start.Z);
            await PhysicsFrames(1);
            int before = player.StepsTaken;
            await WalkForward(1.0f);
            return (player.GlobalPosition.Y - y0, player.GlobalPosition.X > start.X + 3f, player.StepsTaken - before);
        }
        var (rise, past, steps) = await WalkAt(0.5f);
        var (wallRise, wallPast, wallSteps) = await WalkAt(1.0f);
        slab.QueueFree();
        Check(steps >= 1 && past && MathF.Abs(rise - 0.5f) < 0.1f && wallSteps == 0 && !wallPast && wallRise < 0.1f,
            $"the walker steps up onto anything a step tall ({player.StepHeight:0.0#} m, the shared physics module's): walked into a sheer 0.5 m slab it is up on top " +
            $"({rise:F2} m up, {steps} step) without a jump; the same slab 1 m tall stops it ({wallRise:F2} m up)");
    }

    // ---- 6b. Water is a volume ------------------------------------------------------------------

    /// <summary>
    /// A shore near a point: a spot in the sea or a river at least <paramref name="depth"/> metres
    /// deep, and dry, walkable ground 6 to 30 m from it that slopes gently down into it. Returns the
    /// dry spot and the deep one, or null.
    /// </summary>
    static (Vector3 shore, Vector3 deep)? FindShore(PlanetWorld w, Vector3 near, float depth)
    {
        for (double r = 40; r < 4000; r += 30)
        {
            int steps = (int)Math.Ceiling(r * Math.Tau / 30);
            for (int k = 0; k < steps; k++)
            {
                double a = k * Math.Tau / steps;
                var p = new Vector3((float)(near.X + Math.Cos(a) * r), 0f, (float)(near.Z + Math.Sin(a) * r));
                if (w.WaterSurfaceAt(p) is not float surface || surface - w.GroundHeight(p.X, p.Z) < depth) continue;
                for (int d = 0; d < 16; d++)
                {
                    var dir = new Vector3(MathF.Cos(d * MathF.Tau / 16), 0f, MathF.Sin(d * MathF.Tau / 16));
                    float prev = w.GroundHeight(p.X, p.Z);
                    for (int m = 1; m <= 30; m++)
                    {
                        Vector3 q = p + dir * m;
                        float h = w.GroundHeight(q.X, q.Z);
                        if (MathF.Abs(h - prev) > 0.6f || w.Source.StampWeight(q.X, q.Z) > 0f) break; // too steep to walk, or a pad
                        prev = h;
                        if (m < 6 || w.WaterSurfaceAt(q) != null || h < surface + 0.3f) continue;
                        Vector3 shore = q + dir * 2f;
                        if (w.WaterSurfaceAt(shore) != null || MathF.Abs(w.GroundHeight(shore.X, shore.Z) - h) > 1.2f) break;
                        return (new Vector3(shore.X, w.GroundHeight(shore.X, shore.Z) + 0.1f, shore.Z), new Vector3(p.X, surface, p.Z));
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Water is a volume, not a surface: the player walks from a shore into the sea (or a river)
    /// until they swim, dives until their head is under (the vitals flag set, the view tinted and
    /// fogged) and comes back up. The stuck guard never takes them for stuck.
    /// </summary>
    async Task Water()
    {
        PlanetWorld w = _main.World;
        if (w == null) return;
        Walker player = _main.Player;
        var spot = FindShore(w, player.GlobalPosition, 4f);
        if (spot == null)
        {
            Fail("no shore with water 4 m deep within 4 km of the landing site");
            return;
        }
        var (shore, deep) = spot.Value;
        Vector3 toWater = deep - shore;
        player.Teleport(shore);
        player.SetLook(MathF.Atan2(-toWater.X, -toWater.Z), 0f);
        await Streamed();
        await SolidsIn(); // the shore's ground and its trees have their colliders
        await Until(() => player.IsOnFloor(), 10);
        int rescues = _main.Guard.Rescues;
        bool everInside = false, belowGround = false;
        void Watch()
        {
            everInside |= _main.Guard.IsInside(w.Terrain.Store);
            Vector3 p = player.GlobalPosition;
            belowGround |= p.Y < w.GroundHeight(Mathx.WrapX(p.X, w.Width), Mathx.Wrap(p.Z, w.Height)) - 0.5f;
        }

        // Walk in until the water is deep enough to swim, then swim on out over deep water.
        player.MoveOverride = new Vector2(0, -1);
        int walked = 0;
        while (!player.Swimming && walked++ < 1800)
        {
            await PhysicsFrames(1);
            Watch();
        }
        bool swam = player.Swimming;
        float DepthHere()
        {
            Vector3 p = player.GlobalPosition;
            return (w.WaterSurfaceAt(p) ?? float.NegativeInfinity) - w.GroundHeight(Mathx.WrapX(p.X, w.Width), Mathx.Wrap(p.Z, w.Height));
        }
        int swimOut = 0;
        while (DepthHere() < 3.5f && swimOut++ < 1800)
        {
            await PhysicsFrames(1);
            Watch();
        }
        player.MoveOverride = null;
        // Left alone, the swimmer settles at its floating depth: wait until it has stopped bobbing.
        int calm = 0, drifted = 0;
        while (calm < 20 && drifted++ < 900)
        {
            await PhysicsFrames(1);
            Watch();
            calm = Mathf.Abs(player.Velocity.Y) < 0.05f ? calm + 1 : 0;
        }
        bool floats = player.Swimming && !player.Underwater;

        // Dive until the head is under, and a little more.
        player.VerticalOverride = -1f;
        int dived = 0;
        while (!player.Underwater && dived++ < 600)
        {
            await PhysicsFrames(1);
            Watch();
        }
        // On down until the eye is under too (the camera sees the murk), and the head stays under.
        bool tinted = false;
        int deeper = 0;
        while (!tinted && deeper++ < 600)
        {
            await Frames(1);
            Watch();
            tinted = _main.CameraUnderwater;
        }
        bool under = player.Underwater;
        float depth = (w.WaterSurfaceAt(player.GlobalPosition) ?? player.GlobalPosition.Y) - player.GlobalPosition.Y;

        // Back up.
        player.VerticalOverride = 1f;
        int rose = 0;
        while (player.Underwater && rose++ < 600)
        {
            await PhysicsFrames(1);
            Watch();
        }
        player.VerticalOverride = null;
        // Floating up the last of the way, until the eye is out and the camera sees the sky again.
        await Until(() => !_main.CameraUnderwater, 10);
        bool surfaced = !player.Underwater && !_main.CameraUnderwater;
        Check(swam && floats && under && tinted && surfaced && !everInside && !belowGround && _main.Guard.Rescues == rescues,
            $"water is a volume: from a shore the player walks into the water for {walked / 60f:F1} s until they swim, swims out {swimOut / 60f:F1} s to deep water (floating with their head out: {floats}), " +
            $"dives until their head is under ({under}, feet {depth:F1} m down: the suit on its own oxygen, the view tinted {tinted}), and comes back up ({surfaced}); " +
            $"never inside the ground ({!everInside}), never below it ({!belowGround}), never rescued ({_main.Guard.Rescues - rescues} rescues)");
    }

    // ---- 6c. Solid trees and boulders -------------------------------------------------------------

    /// <summary>The nearest pine forest (trees and rocks both) to a point, searching outward on a 40 m lattice.</summary>
    static Vector3? FindForest(PlanetWorld w, Vector3 near)
    {
        for (double r = 0; r < 6000; r += 40)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(r * Math.Tau / 40));
            for (int k = 0; k < steps; k++)
            {
                double a = k * Math.Tau / steps;
                double x = Mathx.WrapX(near.X + Math.Cos(a) * r, w.Width), z = Mathx.Wrap(near.Z + Math.Sin(a) * r, w.Height);
                if (w.Palette.BiomeAt(x, z) != Planets.Gen.Biome.PineForest || w.Source.StampWeight(x, z) > 0f) continue;
                // The middle of the forest: pine forest all round, so its trees and rocks are near.
                bool deep = true;
                for (int d = 0; d < 8 && deep; d++)
                    deep = w.Palette.BiomeAt(Mathx.WrapX(x + Math.Cos(d * Math.Tau / 8) * 60, w.Width), Mathx.Wrap(z + Math.Sin(d * Math.Tau / 8) * 60, w.Height)) == Planets.Gen.Biome.PineForest;
                if (deep) return new Vector3((float)x, w.GroundHeight(x, z) + 0.3f, (float)z);
            }
        }
        return null;
    }

    /// <summary>
    /// Somewhere to stand <paramref name="distance"/> m from a solid, facing it, with gentle open
    /// ground between: no other solid within reach of the path, no water. Null if there is none.
    /// </summary>
    (Vector3 from, float yaw)? Approach(PlanetWorld w, FloraSolid s, Vector3 offset, float distance)
    {
        Vector3 c = s.Center + offset;
        for (int d = 0; d < 12; d++)
        {
            var dir = new Vector3(MathF.Cos(d * MathF.Tau / 12), 0f, MathF.Sin(d * MathF.Tau / 12));
            Vector3 from = new(c.X + dir.X * distance, 0f, c.Z + dir.Z * distance);
            bool clear = true;
            float prev = float.NaN;
            for (float m = 0f; m <= distance + 2.5f && clear; m += 0.5f)
            {
                Vector3 q = from - dir * m;
                float h = w.GroundHeight(Mathx.WrapX(q.X, w.Width), Mathx.Wrap(q.Z, w.Height));
                clear = float.IsNaN(prev) || MathF.Abs(h - prev) < 0.35f;
                clear &= w.WaterSurfaceAt(q) == null;
                prev = h;
            }
            foreach (var (other, o) in _main.Streamer.SolidsNear(c, distance + 4f))
            {
                if (other.Id == s.Id || !clear) continue;
                // Anything else within 2 m of the walk, out to the far side: it would get in the way.
                Vector3 oc = other.Center + o;
                Vector3 rel = new(oc.X - from.X, 0f, oc.Z - from.Z);
                float along = rel.Dot(-dir), across = (rel + dir * along).Length();
                if (along > -1f && along < distance + 3f && across < 2f + other.Extents.X) clear = false;
            }
            if (!clear) continue;
            from.Y = w.GroundHeight(Mathx.WrapX(from.X, w.Width), Mathx.Wrap(from.Z, w.Height)) + 0.1f;
            return (from, MathF.Atan2(dir.X, dir.Z)); // facing -dir
        }
        return null;
    }

    /// <summary>
    /// Waits (for the state, not for time) until the ground and the solid flora round where the
    /// player now stands have their colliders: the streamer has taken the player's column as its
    /// focus, every column in reach has its trunks and boulders, and the ground under and round the
    /// player its collision. Then one physics step, so the engine has the new bodies.
    /// </summary>
    async Task SolidsIn()
    {
        Walker player = _main.Player;
        (int, int) Column(Vector3 p) => ((int)MathF.Floor(p.X / 16f), (int)MathF.Floor(p.Z / 16f));
        bool ready = await Until(() =>
        {
            Vector3 p = player.GlobalPosition;
            return _main.Streamer.SolidsFocus == Column(p) && _main.Streamer.SolidsReady && _main.Streamer.ReadyAround(p, 1, needCollision: true);
        }, 60);
        if (!ready)
        {
            Vector3 p = player.GlobalPosition;
            Fail($"the colliders round the player never came in: player's column {Column(p)}, solids worked out for {_main.Streamer.SolidsFocus} " +
                 $"(ready {_main.Streamer.SolidsReady}), ground with collision {_main.Streamer.ReadyAround(p, 1, needCollision: true)}");
        }
        await PhysicsFrames(1);
    }

    /// <summary>Walks the player forward for a time; returns how far they got along their facing, and their last position.</summary>
    async Task<(float progress, Vector3 end)> WalkForward(float seconds)
    {
        Walker player = _main.Player;
        Vector3 start = player.GlobalPosition, facing = player.LookDirection with { Y = 0 };
        facing = facing.Normalized();
        player.MoveOverride = new Vector2(0, -1);
        await PhysicsFrames((int)(seconds * 60));
        player.MoveOverride = null;
        int still = 0; // coming to rest: horizontal speed gone
        while (new Vector2(player.Velocity.X, player.Velocity.Z).Length() > 0.05f && still++ < 60) await PhysicsFrames(1);
        return ((player.GlobalPosition - start).Dot(facing), player.GlobalPosition);
    }

    /// <summary>
    /// Trees and boulders are solid, near the player only: walking into a trunk or a big boulder
    /// stops the player, a pebble is walked over, a vessel-sized hull swept through a grove or into a
    /// boulder is stopped; the survival tool fells a tree, whose collider goes that frame and which
    /// stays felled through unloading and a save and load; and a player inside a trunk is moved out.
    /// </summary>
    async Task Solids()
    {
        PlanetWorld w = _main.World;
        if (w == null) return;
        Walker player = _main.Player;
        TerrainStreamer streamer = _main.Streamer;

        // The landing site, where a player first spawns: never inside a trunk or a boulder.
        var landing = new Vector3((float)w.Landing.X, 0f, (float)w.Landing.Y);
        landing.Y = w.GroundHeight(landing.X, landing.Z) + 0.3f;
        player.Teleport(_main.World.NearestCopy(landing, player.GlobalPosition));
        await Streamed();
        await SolidsIn();
        int landingSolids = streamer.SolidsNear(player.GlobalPosition, 30f).Count();
        bool landingClear = streamer.OutOfSolids(player.GlobalPosition, Walker.StandHeight, Walker.Radius) == null;

        Vector3? forest = FindForest(w, player.GlobalPosition);
        if (forest == null)
        {
            Fail("no pine forest within 6 km of the landing site");
            return;
        }
        player.Teleport(_main.World.NearestCopy(forest.Value, player.GlobalPosition));
        await Streamed();
        await SolidsIn();
        Vector3 here = player.GlobalPosition;

        // Colliders only near: every solid body within the ground's collision reach, far fewer
        // colliders than the solid flora the finest far rings hold.
        int loaded = streamer.SolidsNear(here, 192f).Count();
        Check(streamer.SolidColliders > 0 && streamer.SolidReach <= streamer.CollisionRadius + 1 && loaded > 3 * streamer.SolidColliders,
            $"solid flora has colliders in the near rings only: {streamer.SolidColliders} trunks and boulders in {streamer.SolidBodies} bodies, none more than {streamer.SolidReach} columns out " +
            $"(collision reach {streamer.CollisionRadius}), of {loaded} solids the finest far rings hold");

        // A trunk stops a walker.
        var trees = streamer.SolidsNear(here, 30f).Where(t => t.solid.Fellable && t.solid.Kind == Flora.Conifer).OrderBy(t => (t.solid.Center + t.offset).DistanceTo(here)).ToList();
        (FloraSolid solid, Vector3 offset) tree = default;
        (Vector3 from, float yaw)? path = null;
        foreach (var t in trees)
            if ((path = Approach(w, t.solid, t.offset, 3.5f)) != null) { tree = t; break; }
        if (path == null) { Fail("no conifer with open ground in front of it"); return; }
        player.Teleport(path.Value.from);
        player.SetLook(path.Value.yaw, 0f);
        await SolidsIn();
        var (progress, end) = await WalkForward(2f);
        Vector3 axis = tree.solid.Center + tree.offset;
        float gap = new Vector2(end.X - axis.X, end.Z - axis.Z).Length();
        bool blocked = streamer.BlockedAt(axis, Walker.Radius) && !streamer.BlockedAt(path.Value.from, Walker.Radius);
        Check(progress < 3.5f - tree.solid.Extents.X - Walker.Radius + 0.1f && gap > tree.solid.Extents.X + Walker.Radius - 0.08f && blocked,
            $"walking into a {tree.solid.Kind} trunk (radius {tree.solid.Extents.X:F2} m) stops the player: {progress:F2} m in 2 s from 3.5 m away, their axis {gap:F2} m from the trunk's; " +
            $"a navigation grid would mark the trunk blocked and the open ground before it not ({blocked})");

        // A big boulder stops a walker; a pebble under the step height is walked over.
        var boulders = streamer.SolidsNear(here, 60f).Where(b => !b.solid.IsTrunk && b.solid.Center.Y + b.solid.Extents.Y - w.GroundHeight(Mathx.WrapX(b.solid.Center.X, w.Width), Mathx.Wrap(b.solid.Center.Z, w.Height)) > 1f)
            .OrderBy(b => (b.solid.Center + b.offset).DistanceTo(here)).ToList();
        (FloraSolid solid, Vector3 offset) boulder = default;
        path = null;
        foreach (var b in boulders)
            if ((path = Approach(w, b.solid, b.offset, 4f)) != null) { boulder = b; break; }
        bool boulderStops = false;
        string boulderNote = "no big boulder with open ground before it";
        if (path != null)
        {
            player.Teleport(path.Value.from);
            player.SetLook(path.Value.yaw, 0f);
            await SolidsIn();
            (progress, end) = await WalkForward(2f);
            boulderStops = !boulder.solid.Contains(end + Vector3.Up * 0.5f - boulder.offset) && progress < 4f;
            boulderNote = $"walking into a boulder {boulder.solid.Extents.X * 2f:F1} m across stops the player {4f - progress:F1} m short of its middle";
        }
        FloraSolidRules rules = FarBuilder.Solids;
        // A pebble with open ground either side of it, walked over northward (-z).
        bool OpenPath(Vector3 a, Vector3 b) => !streamer.SolidsNear((a + b) * 0.5f, 6f).Any(t =>
        {
            Vector3 c = t.solid.Center + t.offset;
            return Mathf.Abs(c.X - a.X) < 1.6f + t.solid.Extents.X && c.Z > Mathf.Min(a.Z, b.Z) - 1.6f && c.Z < Mathf.Max(a.Z, b.Z) + 1.6f;
        });
        Transform3D pebble = streamer.FloraNear(Flora.Rock, here, 150f).FirstOrDefault(t =>
            (0.25f + 0.6f) * t.Basis.Y.Length() - FloraSolidRules.Sink <= rules.MinRockHeight
            && OpenPath(t.Origin + Vector3.Back * 3f, t.Origin + Vector3.Forward * 3f) && w.WaterSurfaceAt(t.Origin) == null);
        bool pebbleWalked = false;
        string pebbleNote = "no rock under the step height nearby";
        if (pebble.Basis.Y.Length() > 0f)
        {
            Vector3 dir = Vector3.Forward;
            Vector3 from = pebble.Origin - dir * 3f;
            from.Y = w.GroundHeight(Mathx.WrapX(from.X, w.Width), Mathx.Wrap(from.Z, w.Height)) + 0.1f;
            player.Teleport(from);
            player.SetLook(0f, 0f); // facing -z, over the pebble
            await SolidsIn();
            await Until(() => player.IsOnFloor(), 10);
            (progress, _) = await WalkForward(1.5f);
            pebbleWalked = progress > 4.5f;
            pebbleNote = $"a rock {(0.85f * pebble.Basis.Y.Length() - FloraSolidRules.Sink):F2} m tall (under {rules.MinRockHeight} m) is walked over: {progress:F1} m in 1.5 s";
        }
        bool rule = !rules.TryMake(Flora.Rock, 1, Vector3.Zero, 0.95f, 0f, out _) && rules.TryMake(Flora.Rock, 1, Vector3.Zero, 1.4f, 0f, out _);
        Check(boulderStops && rule && (pebbleWalked || pebble.Basis.Y.Length() == 0f),
            $"{boulderNote}; {pebbleNote}; by the rule a rock at scale 0.95 (0.56 m tall) is a pebble and one at 1.4 (0.94 m) is solid");

        // A vessel-sized hull, swept the way the shared physics module moves vessels (MoveAndCollide),
        // is stopped by a grove and by a boulder.
        var hull = new CharacterBody3D { Name = "HullProbe", CollisionLayer = Layers.Props, CollisionMask = Layers.Flora, MotionMode = CharacterBody3D.MotionModeEnum.Floating };
        hull.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4f, 1.5f, 7f) } });
        _main.AddChild(hull);
        async Task<(bool stopped, string what)> Sweep(Vector3 start, Vector3 dir, float length, float clearance, float? level = null)
        {
            // Riding the ground at a set clearance (and seeing only trunks and boulders: the ground is not the point here).
            float GroundAt(Vector3 p) => w.GroundHeight(Mathx.WrapX(p.X, w.Width), Mathx.Wrap(p.Z, w.Height));
            float Ride(Vector3 p) => (level ?? GroundAt(p)) + clearance + 0.75f;
            // Colliders live only near the player: stand them beside the middle of the sweep first.
            Vector3 middle = start + dir * (length * 0.5f) + new Vector3(-dir.Z, 0f, dir.X) * 8f;
            player.Teleport(middle with { Y = w.GroundHeight(Mathx.WrapX(middle.X, w.Width), Mathx.Wrap(middle.Z, w.Height)) + 0.3f });
            await SolidsIn();
            hull.GlobalTransform = new Transform3D(Basis.LookingAt(dir, Vector3.Up), start with { Y = Ride(start) });
            // No settling wait: MoveAndCollide sweeps from the transform just set, and every step below waits its tick first.
            for (float gone = 0f; gone < length; gone += 0.5f)
            {
                await PhysicsFrames(1); // one move a physics tick, as vessels move
                hull.GlobalPosition = hull.GlobalPosition with { Y = Ride(hull.GlobalPosition) }; // riding the ground, as a hover servo would
                KinematicCollision3D hit = hull.MoveAndCollide(dir * 0.5f);
                if (hit == null) continue;
                bool flora = PhysicsServer3D.BodyGetCollisionLayer(hit.GetColliderRid()) == Layers.Flora;
                if (flora) return (true, $"after {gone:F1} m");
                return (false, $"hit something else after {gone:F1} m");
            }
            return (false, $"passed {length:F0} m (ended {hull.GlobalPosition.DistanceTo(start with { Y = hull.GlobalPosition.Y }):F1} m along)");
        }
        Vector3 groveDir = (axis - here) with { Y = 0 };
        groveDir = groveDir.LengthSquared() > 1f ? groveDir.Normalized() : Vector3.Forward;
        var (throughGrove, groveNote) = await Sweep(axis - groveDir * 14f, groveDir, 28f, 1.5f);
        (bool intoBoulder, string boulderSweep) = (false, "no boulder");
        if (path != null)
        {
            Vector3 b = boulder.solid.Center + boulder.offset;
            Vector3 toward = (b - path.Value.from) with { Y = 0 };
            float boulderGround = w.GroundHeight(Mathx.WrapX(b.X, w.Width), Mathx.Wrap(b.Z, w.Height));
            (intoBoulder, boulderSweep) = await Sweep(b - toward.Normalized() * 9f, toward.Normalized(), 14f, 0.3f, boulderGround); // level, 0.3 m over the ground at the boulder
        }
        hull.GetParent().RemoveChild(hull);
        hull.QueueFree();
        Check(throughGrove && intoBoulder,
            $"a hover-vessel-sized hull (4 x 1.5 x 7 m) can't pass through a grove: riding 1.5 m up it hits a trunk {groveNote}; low over the ground it hits the boulder {boulderSweep}");

        // Fell the conifer with the survival tool: its collider goes that frame.
        player.Teleport(path?.from ?? player.GlobalPosition);
        var stand = Approach(w, tree.solid, tree.offset, 2.2f);
        if (stand == null) { Fail("nowhere to stand to fell the tree"); return; }
        player.Teleport(stand.Value.from);
        player.SetLook(stand.Value.yaw, 0f);
        await SolidsIn();
        int wood = _main.Tool.Gathered.TryGetValue("wood", out int n0) ? n0 : 0;
        _main.Tool.Aim();
        bool aimed = _main.Tool.AimingAtTree && _main.Tool.TargetTree.Id == tree.solid.Id;
        bool felled = _main.Tool.FellAtTarget();
        Vector3 eye = player.EyePosition;
        var ray = PhysicsRayQueryParameters3D.Create(eye, eye + player.LookDirection * 4f, Layers.Flora);
        bool goneAtOnce = player.GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
        (progress, _) = await WalkForward(1.5f);
        bool walkThrough = progress > 3.2f;
        int gotWood = (_main.Tool.Gathered.TryGetValue("wood", out int n1) ? n1 : 0) - wood;

        // Unload it (walk 700 m away and stream there) and come back: still felled.
        Vector3 back = player.GlobalPosition;
        player.Teleport(back + new Vector3(700f, 0f, 0f) with { Y = w.GroundHeight(Mathx.WrapX(back.X + 700f, w.Width), Mathx.Wrap(back.Z, w.Height)) + 0.5f });
        await Streamed();
        bool unloaded = !streamer.SolidsNear(axis, 2f).Any();
        player.Teleport(stand.Value.from);
        await Streamed();
        await SolidsIn();
        bool StillFelled() => !streamer.SolidsNear(axis, 1f).Any(t => t.solid.Id == tree.solid.Id)
                              && player.GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
        bool afterReload = StillFelled();

        // Saved, then a load brings it back from disk (the felled set cleared first, so only the file can).
        _main.Save();
        w.Felled.Clear();
        streamer.RefreshFelled();
        await Streamed();
        await SolidsIn();
        bool stoodAgain = streamer.SolidsNear(axis, 1f).Any(t => t.solid.Id == tree.solid.Id);
        _main.Load();
        await Streamed();
        await SolidsIn();
        bool afterLoad = StillFelled() && w.Felled.Contains(tree.solid.Id);
        Check(aimed && felled && goneAtOnce && walkThrough && gotWood > 0 && unloaded && afterReload && stoodAgain && afterLoad,
            $"the survival tool fells a conifer for {gotWood} wood: its collider is gone that frame ({goneAtOnce}) and the player walks through where it stood ({progress:F1} m); " +
            $"it stays felled after unloading and reloading its ground ({afterReload}) and after a save and load ({afterLoad}; with the felled set cleared it stood again: {stoodAgain})");

        // Inside a trunk after a teleport (a load, a spawn): moved out within a few ticks. The
        // landing site itself is never inside one.
        var standing = streamer.SolidsNear(player.GlobalPosition, 30f).FirstOrDefault(t => t.solid.IsTrunk && t.solid.Id != tree.solid.Id);
        int rescues = _main.Guard.Rescues;
        Vector3 inside = standing.solid.Center + standing.offset;
        inside.Y = w.GroundHeight(Mathx.WrapX(inside.X, w.Width), Mathx.Wrap(inside.Z, w.Height)) + 0.05f;
        Vector3? guardSays = streamer.OutOfSolids(inside, Walker.StandHeight, Walker.Radius);
        player.Teleport(inside);
        await Until(() => _main.Guard.InsideSolid() == null, 5); // moved out (by the guard, or the character controller first)
        bool movedOut = guardSays is Vector3 to && !standing.solid.Contains(to + Vector3.Up * 0.9f - standing.offset)
                        && _main.Guard.InsideSolid() == null && !standing.solid.Contains(player.GlobalPosition + Vector3.Up * 0.9f - standing.offset);
        Check(standing.solid.Extents.X > 0f && movedOut && landingClear,
            $"a player put inside a {standing.solid.Kind} trunk is moved out beside it ({_main.Guard.Rescues - rescues} stuck rescue; the character controller may push them out first), " +
            $"and nothing spawns inside one: the landing site is clear of trunks and boulders ({landingSolids} solids within 30 m of it)");
    }

    // ---- 7. Flying at 100 m/s ---------------------------------------------------------------

    async Task Flight()
    {
        PlanetWorld w = _main.World;
        if (w == null) return;
        Engine.MaxFps = 60; // a real frame rate, so streaming has the time a player's machine gives it
        _main.ToggleFly();
        Vector3 start = _main.Player.GlobalPosition + Vector3.Up * 40f;
        _main.Fly.Begin(start, 0, -0.1f);
        await Streamed(30);
        _main.Streamer.ResetStats();
        _main.WorstMainMs = 0;
        _main.Fly.VelocityOverride = new Vector3(100f, 0f, 30f).Normalized() * 100f;
        var watch = Stopwatch.StartNew();
        double worstFrame = 0, frameSum = 0, worstProcess = 0, worstPhysics = 0;
        int frames = 0;
        long last = Stopwatch.GetTimestamp();
        int shiftsBefore = _main.FrameShifts;
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        TimeSpan pausedBefore = GC.GetTotalPauseDuration();
        long allocatedBefore = GC.GetTotalAllocatedBytes();
        double worstPause = 0;
        string worstGc = "";
        var probe = new FrameProbe(this);
        bool probing = false;
        // Three windows of the flight, judged on the best: a machine busy with other work stalls
        // some frames, but a real slowdown shows in every window.
        var windowWorst = new double[3];
        var windowBreakdown = new string[3];
        int window = -1;
        while (watch.Elapsed.TotalSeconds < 20)
        {
            double at = watch.Elapsed.TotalSeconds;
            int windowNow = at < 1 ? -1 : at < 7 ? 0 : at < 13.5 ? 1 : 2;
            if (windowNow != window)
            {
                if (window >= 0)
                {
                    windowWorst[window] = _main.Streamer.WorstFrameMs;
                    windowBreakdown[window] = _main.Streamer.WorstBreakdown;
                }
                _main.Streamer.ResetStats();
                window = windowNow;
            }
            await Frames(1);
            long now = Stopwatch.GetTimestamp();
            double ms = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
            last = now;
            var info = GC.GetGCMemoryInfo(GCKind.Any);
            foreach (TimeSpan pause in info.PauseDurations)
                if (pause.TotalMilliseconds > worstPause)
                {
                    worstPause = pause.TotalMilliseconds;
                    worstGc = $"gen{info.Generation} {(info.Concurrent ? "background" : "blocking")}, {info.PromotedBytes / 1e6:F1} MB promoted, heap {info.HeapSizeBytes / 1e6:F0} MB, compacted {info.Compacted}";
                }
            if (watch.Elapsed.TotalSeconds < 1) continue;
            if (!probing)
            {
                probe.Reset();
                probing = true;
            }
            worstFrame = Math.Max(worstFrame, ms);
            worstProcess = Math.Max(worstProcess, Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
            worstPhysics = Math.Max(worstPhysics, Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000);
            frameSum += ms;
            frames++;
        }
        _main.Fly.VelocityOverride = null;
        windowWorst[2] = _main.Streamer.WorstFrameMs;
        windowBreakdown[2] = _main.Streamer.WorstBreakdown;
        double frameWork = probe.WorstMs, frameGap = probe.WorstGapMs;
        probe.Remove();
        double flown = (_main.Camera.GlobalPosition - start).Length() + (_main.FrameShifts - shiftsBefore) * 0; // display only
        int best = Array.IndexOf(windowWorst, windowWorst.Min());
        double worstStream = windowWorst[best];
        GD.Print($"      garbage collections during the flight: gen0 {GC.CollectionCount(0) - gc0}, gen1 {GC.CollectionCount(1) - gc1}, gen2 {GC.CollectionCount(2) - gc2}; " +
                 $"paused {(GC.GetTotalPauseDuration() - pausedBefore).TotalMilliseconds:F0} ms in all, longest pause {worstPause:F1} ms ({worstGc}); " +
                 $"{(GC.GetTotalAllocatedBytes() - allocatedBefore) / 1e6 / 20:F0} MB allocated per second");
        bool caughtUp = await Streamed(30);
        Engine.MaxFps = 0;
        Check(worstStream < 8.0 && caughtUp,
            $"flying 2 km at 100 m/s: terrain streaming's main-thread cost worst {worstStream:F2} ms per frame in the best of three windows " +
            $"(windows {string.Join(", ", windowWorst.Select(v => v.ToString("F2")))} ms; best window: {windowBreakdown[best]}; slowest far chunk {_main.Streamer.WorstFarNodeMs:F1} ms: {_main.Streamer.WorstFarNode}; " +
            $"GC pauses inside streaming, left out, up to {_main.Streamer.WorstGcPauseMs:F1} ms); " +
            $"whole frames: game code worst {frameWork:F2} ms, GC pauses included, longest wait between frames {frameGap:F1} ms (Godot's monitors, a second at a time: process {worstProcess:F1}, physics {worstPhysics:F1}; Main {_main.WorstMainMs:F1} ms: {_main.WorstMain}); " +
            $"caught up after landing {caughtUp} (headless, 60 fps cap)");
        _main.ToggleFly();
        await Until(() => _main.IsReady, 30);
    }

    // ---- 8. The north-south seam -------------------------------------------------------------

    /// <summary>A walkable column near the north edge: dry, gentle ground for 30 m either side of the seam.</summary>
    static double FindSeamCrossing(PlanetWorld w)
    {
        for (double x = 400; x < w.Width - 400; x += 37)
        {
            bool good = true;
            float prev = w.GroundHeight(x, w.Height - 40);
            for (double z = -40; z <= 40 && good; z += 2)
            {
                float h = w.GroundHeight(x, Mathx.Wrap(z, w.Height));
                good = h > 2f && Math.Abs(h - prev) < 0.7f && w.Source.StampWeight(x, Mathx.Wrap(z, w.Height)) == 0f;
                prev = h;
            }
            if (good) return x;
        }
        return -1;
    }

    async Task NorthSeam()
    {
        var spec = PlanetSpec.Roll(4242, Archetype.Temperate, 0);
        PlanetWorld probe = _main.World?.Spec == spec ? _main.World : PlanetWorld.Build(spec, Main.Catalog);
        double x = FindSeamCrossing(probe);
        if (x < 0) { Fail("no walkable ground across the north-south seam on the test planet"); return; }
        var spot = new Vector3((float)x, 0, 6f);
        if (!await Land(spec, spot)) return;
        await Streamed();
        PlanetWorld w = _main.World;
        PlanetTerrain t = w.Terrain;

        // Heights and normals across the seam and at the corner: a ghost chunk past the north edge
        // meshes exactly like its canonical twin at the south edge, moved by the planet's height.
        Vector3 p = _main.Player.GlobalPosition;
        int cx = (int)MathF.Floor(p.X / 16);
        int compared = 0, off = 0;
        double worst = 0, tilt = 0;
        for (int cy = -8; cy < 40; cy++)
        {
            ArrayMesh ghost = _main.Streamer.MeshOf(new TInt3(cx, cy, -1), out _);
            if (ghost == null) continue;
            var built = BuildChunk(t, new TInt3(cx, cy, t.ChunksZ - 1));
            Vector3[] gv = (Vector3[])ghost.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex];
            Vector3[] gn = (Vector3[])ghost.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Normal];
            if (built.Positions.Length != gv.Length) { off++; continue; }
            for (int i = 0; i < gv.Length; i++)
            {
                var twin = new Vector3(built.Positions[i].X, built.Positions[i].Y, built.Positions[i].Z - t.Height);
                worst = Math.Max(worst, (twin - gv[i]).Length());
                var tn = new Vector3(built.Normals[i].X, built.Normals[i].Y, built.Normals[i].Z);
                tilt = Math.Max(tilt, Mathf.RadToDeg(tn.AngleTo(gn[i])));
                compared++;
            }
        }
        // The corner: the chunk beyond both seams against the canonical one at the far corner.
        var corner = BuildChunk(t, new TInt3(-1, ChunkStore.ChunkOf(TInt3.Floor(N(p))).Y, -1));
        var twinCorner = BuildChunk(t, new TInt3(t.ChunksX - 1, ChunkStore.ChunkOf(TInt3.Floor(N(p))).Y, t.ChunksZ - 1));
        bool cornerOk = corner.Positions.Length == twinCorner.Positions.Length;
        for (int i = 0; cornerOk && i < corner.Positions.Length; i++)
            cornerOk = (corner.Positions[i] - twinCorner.Positions[i] + new System.Numerics.Vector3(t.Width, 0, t.Height)).Length() < 0.004f;
        Check(compared > 100 && off == 0 && worst < 0.004 && tilt < 0.05 && cornerOk,
            $"north-south seam: {compared} vertices of chunks beyond the north edge match their twins at the south edge to {worst * 1000:F2} mm, " +
            $"normals to {tilt:F3} deg; the corner chunk beyond both seams matches the far corner: {cornerOk}");

        // The far rings tile the planet across the seam too: the last strip before it (64 m on an
        // 8 km planet, 62.5 far chunks) has its far ground and its flora like any other.
        await Until(() => _main.Streamer.FarComplete, 30);
        int farGround = 0, farSamples = 0;
        for (float fxo = -800f; fxo <= 800f; fxo += 100f)
            for (float fzo = -60f; fzo < 0f; fzo += 20f)
            {
                farSamples++;
                if (_main.Streamer.FarGroundAt(new Vector3(p.X + fxo, 0f, fzo))) farGround++;
            }
        int beyond = 0, before = 0;
        foreach (Flora kind in Enum.GetValues<Flora>())
            foreach (Transform3D f in _main.Streamer.FloraNear(kind, new Vector3(p.X, 0f, 0f), 120f))
            {
                if (f.Origin.Z >= -64f && f.Origin.Z < 0f) beyond++;
                else if (f.Origin.Z >= 0f && f.Origin.Z < 64f) before++;
            }
        Check(farGround == farSamples && beyond > 0 && beyond > before / 3,
            $"the far grid tiles the planet across the north-south seam: far ground under all {farSamples} points of the last 64 m strip before it, " +
            $"and {beyond} flora there beside {before} in the 64 m just past it");

        // An edit straddling the seam shows on both sides, and survives a save and load.
        var centre = new TInt3((int)MathF.Floor(p.X) + 3, (int)MathF.Floor(w.GroundHeight(p.X + 3, 0)) - 1, 0);
        PlanetEdit edit = _main.Dig(centre, 1.42f);
        var north = new TInt3(centre.X, centre.Y, -1);
        var twinNorth = new TInt3(centre.X, centre.Y, t.Height - 1);
        var south = new TInt3(centre.X, centre.Y, 0);
        var twinSouth = new TInt3(centre.X, centre.Y, t.Height);
        bool both = t.Store.IsRemoved(north) && t.Store.IsRemoved(twinNorth) && t.Store.IsRemoved(south) && t.Store.IsRemoved(twinSouth);
        _main.Save();
        LoadResult loaded = _main.Load();
        bool kept = loaded.Ok && t.Store.IsRemoved(north) && t.Store.IsRemoved(twinNorth) && t.Store.IsRemoved(south) && t.Store.IsRemoved(twinSouth);
        await Until(() => _main.Streamer.Pending == 0, 10);
        ulong ghostHash = 0;
        bool meshed = _main.Streamer.TryGetHash(ChunkStore.ChunkOf(north), out ghostHash);
        var ghostMesh = BuildChunk(t, ChunkStore.ChunkOf(north));
        var twinMesh = BuildChunk(t, ChunkStore.ChunkOf(twinNorth));
        bool sameShape = ghostMesh.Positions.Length == twinMesh.Positions.Length;
        Check(edit.Removed.Count > 0 && both && kept && meshed && sameShape,
            $"an edit straddling the north-south seam: {edit.Removed.Count} cells dug, removed on both sides {both}, kept through save and load {kept}, " +
            $"meshes beyond and before the seam agree {sameShape}");
    }

    static TerrainDigging.Terrain.ChunkMesh BuildChunk(PlanetTerrain t, TInt3 chunk) => new SurfaceNetsMesher().Build(t.Store.CaptureNeighborhood(chunk));

    // ---- 9. Walking across the north edge ---------------------------------------------------

    async Task WalkAcross()
    {
        PlanetWorld w = _main.World;
        if (w == null) return;
        double x = FindSeamCrossing(w);
        if (x < 0) { Fail("no walkable ground across the north-south seam"); return; }
        // Three walks. What the walk does (crosses, stays on the ground, keeps its speed) is
        // simulation and must hold in every walk. The frame budgets are wall-clock and judged on the
        // best walk: a machine busy with other work can stall one walk's frames, but a real slowdown
        // stalls every walk. All three are printed, so a regression stays visible.
        var walks = new List<string>();
        bool walkedEvery = true;
        double bestBudget = double.MaxValue;
        int bestWalk = 0;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var (walked, budget, report) = await WalkAcrossOnce(w, x);
            walks.Add($"walk {attempt}: {report}");
            walkedEvery &= walked;
            if (budget < bestBudget) (bestBudget, bestWalk) = (budget, attempt);
        }
        Check(walkedEvery && bestBudget < 1.0,
            $"walking north across the edge: on the ground at speed in every walk ({walkedEvery}), frame budgets met in the best of three " +
            $"(walk {bestWalk}, worst {bestBudget * 100:F0}% of a budget): {string.Join(" | ", walks)}");
    }

    /// <summary>
    /// One walk across the edge. Returns whether the walk itself went right, the worst of its frame
    /// costs as a fraction of its budget (under 1 is within every budget), and a report.
    /// </summary>
    async Task<(bool walked, double budget, string report)> WalkAcrossOnce(PlanetWorld w, double x)
    {
        Walker player = _main.Player;
        player.Teleport(new Vector3((float)x, w.GroundHeight(x, 14) + 0.2f, 14f));
        await Streamed();
        // A jump across the planet is an arrival: settle the heap as landing does (Main.BecomeReady),
        // so a full collection owed by the checks before this one doesn't fall inside the walk.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        await Until(() => _main.Streamer.Settled, 30); // far rings and horizon in too, so the walk starts on a quiet frame
        await SolidsIn(); // the ground (and trees) round the start have their colliders
        await Until(() => player.IsOnFloor(), 10);
        player.SetLook(0f, 0f); // facing north (-z)
        Engine.MaxFps = 60;
        int shifts = _main.FrameShifts;
        double lowest = double.MaxValue, worstStream = 0, crossingWork = 0;
        float minSpeed = float.MaxValue;
        _main.Streamer.ResetStats();
        _main.WorstMainMs = 0;
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        double longestPause = 0;
        long gcIndexBefore = GC.GetGCMemoryInfo(GCKind.Any).Index;
        var probe = new FrameProbe(this);
        player.MoveOverride = new Vector2(0, -1);
        int ticks = 0;
        bool crossing = false;
        // Seven seconds of simulation, counted in ticks so a busy machine can't shorten the walk.
        while (ticks < 7 * 60)
        {
            await PhysicsFrames(1);
            ticks++;
            GCMemoryInfo gcInfo = GC.GetGCMemoryInfo(GCKind.Any);
            if (gcInfo.Index > gcIndexBefore)
                foreach (TimeSpan pause in gcInfo.PauseDurations)
                    longestPause = Math.Max(longestPause, pause.TotalMilliseconds);
            Vector3 p = player.GlobalPosition;
            float ground = w.GroundHeight(Mathx.WrapX(p.X, w.Width), Mathx.Wrap(p.Z, w.Height));
            lowest = Math.Min(lowest, p.Y - ground);
            // The first half second is getting into stride; after it, every frame counts.
            if (ticks == 30) probe.Reset();
            if (ticks > 30) minSpeed = Math.Min(minSpeed, new Vector2(player.Velocity.X, player.Velocity.Z).Length());
            // The frame shifts during a physics tick; the probe's last frame is that whole frame by
            // the next tick, when this loop first sees the shift.
            if (_main.FrameShifts > shifts && !crossing)
            {
                crossing = true;
                crossingWork = probe.LastMs;
            }
        }
        player.MoveOverride = null;
        double worstWork = probe.WorstMs, worstGap = probe.WorstGapMs;
        int frames = probe.Frames;
        probe.Remove();
        worstStream = _main.Streamer.WorstFrameMs;
        Engine.MaxFps = 0;
        Vector3 end = player.GlobalPosition;
        bool crossed = _main.FrameShifts > shifts && end.Z > w.Height * 0.5f;
        // At the 60 fps cap a frame starts every 16.7 ms: a longer wait between one frame's last
        // callback and the next one's first means the engine itself stalled for the difference.
        double budget = Math.Max(Math.Max(worstStream / 8.0, worstWork / 8.0), worstGap / (1000.0 / 60 + 8.0));
        return (crossed && lowest > -0.3 && minSpeed > 3.5f, budget,
            $"crossed {crossed} (now {end.Z:F0} m S of {w.Height:F0}), lowest {lowest:F2} m relative to the ground, " +
            $"slowest {minSpeed:F1} m/s once walking; game code on the main thread worst {worstWork:F2} ms in {frames} frames, {crossingWork:F2} ms on the frame of the crossing " +
            $"(Main {_main.WorstMainMs:F1} ms: {_main.WorstMain}); longest wait between frames {worstGap:F1} ms (16.7 at the cap, so no engine stall over {Math.Max(0, worstGap - 1000.0 / 60):F1} ms); " +
            $"collections while walking gen0 {GC.CollectionCount(0) - gc0}, gen1 {GC.CollectionCount(1) - gc1}, gen2 {GC.CollectionCount(2) - gc2} (longest pause {longestPause:F1} ms); " +
            $"terrain streaming worst {worstStream:F2} ms (headless, 60 fps cap)");
    }

    // ---- 10. Weather --------------------------------------------------------------------------

    async Task WeatherChecks()
    {
        WeatherData data = WeatherView.WeatherRig.Data;
        // Deterministic from its seed: the same timeline, conditions and hazards.
        var a = new WeatherSim(data, "temperate", 777);
        var b = new WeatherSim(data, "temperate", 777);
        var c = new WeatherSim(data, "temperate", 778);
        bool same = true, differs = false;
        int strikes = 0;
        for (int i = 0; i < 7200; i++)
        {
            a.Step(1f, Vector3.Zero);
            b.Step(1f, Vector3.Zero);
            c.Step(1f, Vector3.Zero);
            for (int k = 0; k < Conditions.Count; k++)
            {
                same &= a.Current[k] == b.Current[k];
                differs |= a.Current[k] != c.Current[k];
            }
            same &= a.Hazards.Count == b.Hazards.Count;
            strikes += a.Events.Count;
        }
        Check(same && differs, $"weather is deterministic from its seed: two hours identical for the same seed ({strikes} hazard events), different for another");

        // Every weather sets its own fog and cloud values.
        SkySystem sky = SkySystem.For(PlanetSpec.Roll(1, Archetype.Temperate, 1));
        var sim = new WeatherSim(data, "temperate", 5);
        var looks = new Dictionary<WeatherKind, AtmosphereState>();
        foreach (WeatherKind kind in Enum.GetValues<WeatherKind>())
        {
            sim.Snap(kind);
            looks[kind] = Atmosphere.Compute(sky, sim.Current, 0.8f);
        }
        var lines = looks.Select(l => $"{l.Key} cloud {l.Value.CloudCover:F2}/{l.Value.CloudDarkness:F2} fog {l.Value.FogDensity:F4} valley {l.Value.ValleyFog:F2}");
        GD.Print("      " + string.Join("; ", lines));
        AtmosphereState clear = looks[WeatherKind.Clear];
        bool distinct = looks.All(l => l.Key == WeatherKind.Clear || l.Value != clear);
        bool ok = clear.CloudCover < 0.3f && clear.FogDensity < 0.0005f && clear.ValleyFog < 0.15f
            && looks[WeatherKind.Clouds].CloudCover > clear.CloudCover
            && looks[WeatherKind.Rain].Overcast > 0.3f && looks[WeatherKind.Rain].FogDensity > clear.FogDensity
            && looks[WeatherKind.Fog].ValleyFog > 0.9f && looks[WeatherKind.Fog].FogDensity > 0.02f
            && looks[WeatherKind.Thunderstorm].CloudDarkness > 0.8f && looks[WeatherKind.Thunderstorm].Overcast > 0.8f
            && looks[WeatherKind.Blizzard].FogDensity > 0.04f && looks[WeatherKind.DustStorm].FogDensity > 0.04f
            && looks[WeatherKind.Monsoon].CloudBase < clear.CloudBase;
        Check(ok && distinct, $"each weather sets its fog and cloud values ({looks.Count} states, each different from clear skies)");

        // Valley fog: morning banks on a damp world that burn off by noon, and none in dry air.
        sim.Snap(WeatherKind.Clear);
        float dawn = Atmosphere.Compute(sky, sim.Current, 0.02f).ValleyFog, noon = Atmosphere.Compute(sky, sim.Current, 0.8f).ValleyFog;
        float dry = Atmosphere.Compute(SkySystem.For(PlanetSpec.Roll(1, Archetype.Arid, 0)), sim.Current, 0.02f).ValleyFog;
        Check(dawn > 0.5f && noon < 0.05f && dry < 0.2f, $"valley fog pools at dawn on a humid world ({dawn:F2}), burns off by noon ({noon:F2}), stays thin in dusty air ({dry:F2})");
        await Frames(1);
    }

    // ---- Reporting ---------------------------------------------------------------------------

    void Check(bool ok, string what)
    {
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
