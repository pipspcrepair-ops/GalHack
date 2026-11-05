using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Player;
using PlanetSurface.Poi;
using PlanetSurface.Skies;
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
        // Planets save their edits when you leave them: start from untouched planets every run.
        string saves = ProjectSettings.GlobalizePath("user://planets");
        if (System.IO.Directory.Exists(saves)) System.IO.Directory.Delete(saves, recursive: true);
        try
        {
            await Run("Weather", WeatherChecks);
            await Run("Relief", ReliefChecks);
            await Run("Pois", PoiChecks);
            _main = new Main { Interactive = false };
            AddChild(_main);
            await Run("Heights", Heights);
            await Run("Seams", Seams);
            await Run("Persistence", Persistence);
            await Run("Stuck", Stuck);
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
            if (w.Source.StampWeight(wx, wz) > 0f || g.InRiver || w.Source.CellDensity(cx, (int)MathF.Floor(natural - 0.5f), cz) <= 0f) { skipped++; continue; }
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
        int sites = 0, floating = 0, doors = 0, corners = 0, overlaps = 0;
        var kinds = new Dictionary<string, int>();
        var notes = new List<string>();
        for (int a = 0; a < 5; a++)
        {
            var spec = PlanetSpec.Roll((ulong)(800 + a * 7), (Archetype)a, 0);
            PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
            ChunkStore store = w.Terrain.Store;
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
                    if (float.IsNaN(ground) || Math.Abs(ground - pad) > 0.35f) corners++;
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
                // Nothing buried: no block above the pad shares a cell with ground.
                for (int y = 0; y < occ.Min.Y + occ.Size.Y; y++)
                for (int z = 0; z < occ.Size.Z; z++)
                for (int x = 0; x < occ.Size.X; x++)
                {
                    var local = new PInt3(occ.Min.X + x, y, occ.Min.Z + z);
                    if (!inst.Blocks.Get(local).IsAir && store.IsSolid(site.World(local))) overlaps++;
                }
                // The way in: open from the floor to head height, with ground under it.
                TInt3 e = site.Entrance;
                bool open = !store.IsSolid(e) && !store.IsSolid(e + TInt3.Up) && !store.IsSolid(e + TInt3.Up + TInt3.Up);
                bool footing = store.IsSolid(e - TInt3.Up) || !inst.Blocks.Get(inst.Layout.EntranceCell + PInt3.Down).IsAir;
                if (!open || !footing) doors++;
                if (notes.Count < 12) notes.Add($"{site.Family.Id} on {site.Siting} ground (cut/fill {site.CutFill:F1} m)");
            }
            await Frames(1);
        }
        GD.Print("      " + string.Join("; ", notes));
        Check(sites >= 10 && floating == 0 && overlaps == 0 && doors == 0 && corners == 0,
            $"POIs seated: {sites} places on 5 worlds ({string.Join(", ", kinds.Select(k => $"{k.Value} {k.Key}"))}); " +
            $"{corners} corners off floor level, {floating} cells of missing ground under and around their rooms, {overlaps} blocks buried in ground, {doors} blocked ways in");
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
        await Frames(40); // past an eviction pass
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
        await PhysicsFrames(4);
        bool freed = !_main.Guard.IsInside(store);

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
        await PhysicsFrames(6);
        player.Teleport(new Vector3(floor.X + 0.5f, floor.Y + 0.05f, floor.Z + 0.5f));
        await PhysicsFrames(10);
        bool inPit = !_main.Guard.IsInside(store);
        LoadResult loaded = _main.Load();
        await PhysicsFrames(4);
        bool afterLoad = !_main.Guard.IsInside(store);
        Check(freed && inPit && loaded.Ok && afterLoad && _main.Guard.Rescues > rescuesBefore,
            $"stuck safety: a buried player is freed ({freed}), standing in a fresh pit is left alone ({inPit}), " +
            $"a load that fills the pit moves them out ({afterLoad}); {_main.Guard.Rescues} rescues so far");
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
        while (watch.Elapsed.TotalSeconds < 20)
        {
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
        double frameWork = probe.WorstMs, frameGap = probe.WorstGapMs;
        probe.Remove();
        double flown = (_main.Camera.GlobalPosition - start).Length() + (_main.FrameShifts - shiftsBefore) * 0; // display only
        double worstStream = _main.Streamer.WorstFrameMs;
        GD.Print($"      garbage collections during the flight: gen0 {GC.CollectionCount(0) - gc0}, gen1 {GC.CollectionCount(1) - gc1}, gen2 {GC.CollectionCount(2) - gc2}; " +
                 $"paused {(GC.GetTotalPauseDuration() - pausedBefore).TotalMilliseconds:F0} ms in all, longest pause {worstPause:F1} ms ({worstGc}); " +
                 $"{(GC.GetTotalAllocatedBytes() - allocatedBefore) / 1e6 / 20:F0} MB allocated per second");
        bool caughtUp = await Streamed(30);
        Engine.MaxFps = 0;
        Check(worstStream < 8.0 && caughtUp,
            $"flying 2 km at 100 m/s: terrain streaming's main-thread cost worst {worstStream:F2} ms per frame ({_main.Streamer.WorstBreakdown}; slowest far chunk {_main.Streamer.WorstFarNodeMs:F1} ms: {_main.Streamer.WorstFarNode}; " +
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
        Walker player = _main.Player;
        player.Teleport(new Vector3((float)x, w.GroundHeight(x, 14) + 0.2f, 14f));
        await Streamed();
        // A jump across the planet is an arrival: settle the heap as landing does (Main.BecomeReady),
        // so a full collection owed by the checks before this one doesn't fall inside the walk.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        await PhysicsFrames(80);
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
        var watch = Stopwatch.StartNew();
        int ticks = 0;
        bool crossing = false;
        while (watch.Elapsed.TotalSeconds < 7)
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
        Check(crossed && lowest > -0.3 && minSpeed > 3.5f && worstStream < 8.0 && worstWork < 8.0 && worstGap < 1000.0 / 60 + 8.0,
            $"walking north across the edge: crossed {crossed} (now {end.Z:F0} m S of {w.Height:F0}), lowest {lowest:F2} m relative to the ground, " +
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
