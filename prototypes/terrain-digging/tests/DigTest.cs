using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Godot;
using TerrainDigging.Terrain;
using NVector3 = System.Numerics.Vector3;

namespace TerrainDigging;

/// <summary>
/// Headless proof of the digging rules. Run with:
///   godot --headless --path prototypes/terrain-digging res://tests/DigTest.tscn
/// Exits 0 if every check passes, 1 if any fails.
///
/// The first half drives the pure terrain core directly (no scene). The second half runs the
/// real scene: physics, the stuck guard, crouching, the mesh pipeline and its timings.
/// </summary>
public partial class DigTest : Node
{
    const int Seed = Main.WorldSeed;

    Main _main;
    int _failures, _checks;
    string _saveDir;

    public override void _Ready()
    {
        _saveDir = ProjectSettings.GlobalizePath("res://saves/test");
        RunAll();
    }

    async void RunAll()
    {
        var total = Stopwatch.StartNew();
        try
        {
            Section("Terrain core");
            TestRemoveOnly();
            TestBedrock();
            TestAnchors();
            TestBlocksClearTerrain();
            TestCraterCap();
            TestWaterStaysPut();
            TestGeneratorCaves();
            Section("Edit log, replication and saves");
            TestReplication();
            TestRoundTrip();
            TestAtomicSave();
            TestLoadAudit();
            Section("Mesher");
            TestSeams();
            TestMeshTimingSingleThread();

            Section("Scene");
            _main = new Main { Interactive = false, SavePath = Path.Combine(_saveDir, "scene.ghs") };
            AddChild(_main);
            while (!_main.IsReady) await NextFrame();
            Info($"startup: {_main.View.MeshedChunks} chunk meshes, {_main.View.Triangles:N0} triangles in {_main.StartupSeconds:F2} s");
            await TestStuckSafe();
            await TestCrouch();
            await TestGameActions();
            await TestFloatingIsland();
            await TestRebuildIdentical();
            Section("Performance");
            await TestPerformance();
        }
        catch (Exception e)
        {
            Fail($"exception: {e}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(_saveDir)) Directory.Delete(_saveDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        GD.Print("");
        GD.Print(_failures == 0
            ? $"ALL PASSED ({_checks} checks in {total.Elapsed.TotalSeconds:F1} s)"
            : $"{_failures} OF {_checks} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- Terrain core -----------------------------------------------------------------

    /// <summary>Rule 1: a thousand random operations of every kind, and solid cells never increase.</summary>
    void TestRemoveOnly()
    {
        var store = NewStore();
        var rng = new Random(20261104);
        var region = new CellBox(new Int3(-48, -40, -48), new Int3(48, 64, 48));
        int initial = store.CountSolid(region);
        int increased = 0, misaccounted = 0, removedTotal = 0;
        var kinds = new int[6];

        for (int op = 0; op < 1000; op++)
        {
            Int3 at = RandomSurfaceCell(store, rng, 40);
            at = new Int3(at.X, at.Y - rng.Next(0, 34), at.Z); // some deep enough to hit bedrock
            var box = new CellBox(at - new Int3(7, 7, 7), at + new Int3(8, 8, 8));
            int before = store.CountSolid(box);

            int kind = rng.Next(100) switch { < 55 => 0, < 70 => 1, < 80 => 2, < 90 => 3, < 95 => 4, _ => 5 };
            kinds[kind]++;
            int removed = kind switch
            {
                0 => store.Dig(at, (float)rng.NextDouble() * 3f).Removed.Count,
                1 => store.DigCells(ChunkStore.BoxCells(CellBox.FromSize(at, new Int3(rng.Next(1, 4), rng.Next(1, 4), rng.Next(1, 4))))).Removed.Count,
                2 => store.Crater(at.Center, (float)rng.NextDouble() * 9f).Removed.Count,
                3 => store.PlaceStructure(CellBox.FromSize(at, new Int3(2, 2, 2))) is { Verdict: PlaceVerdict.Placed } p ? p.Cleared.Removed.Count : 0,
                4 => store.ApplyDiff(RandomDiff(store, rng, at)).Applied,
                _ => store.DigCells(new[] { at, at, at }).Removed.Count, // duplicates in one edit
            };
            int after = store.CountSolid(box);
            if (after > before) increased++;
            if (before - after != removed) misaccounted++;
            removedTotal += removed;
        }

        int final = store.CountSolid(region);
        Check(increased == 0 && misaccounted == 0 && final <= initial,
            $"remove-only: 1000 random edits never added a solid cell ({removedTotal:N0} removed; " +
            $"{kinds[0]} digs, {kinds[1]} boxes, {kinds[2]} craters, {kinds[3]} blocks, {kinds[4]} diffs, {kinds[5]} duplicates)");
        Check(initial - final == removedTotal && store.RemovedCount == removedTotal,
            $"every removal is accounted for: {initial:N0} → {final:N0} solid cells in a 96×104×96 m region");
    }

    EditLog RandomDiff(ChunkStore store, Random rng, Int3 near)
    {
        // A replicated diff: mostly real removals, some repeats, never anything else.
        var log = new EditLog();
        for (int i = 0; i < 12; i++)
        {
            var cell = new Int3(near.X + rng.Next(-2, 3), near.Y + rng.Next(-2, 3), near.Z + rng.Next(-2, 3));
            CellMaterial m = store.BaseMaterialAt(cell);
            if (m != CellMaterial.Air && m != CellMaterial.Bedrock) log.Add(cell);
        }
        return log;
    }

    /// <summary>Rule 2: bedrock about 30 m below the original surface refuses every kind of edit.</summary>
    void TestBedrock()
    {
        var store = NewStore();
        int checkedColumns = 0, wrongDepth = 0;
        Int3 sample = default;
        for (int x = -60; x <= 60; x += 15)
            for (int z = -60; z <= 60; z += 15)
            {
                float h = store.Source.SurfaceHeight(x + 0.5f, z + 0.5f);
                int y = (int)MathF.Floor(h);
                while (store.BaseMaterialAt(new Int3(x, y, z)) != CellMaterial.Bedrock) y--;
                float depth = h - (y + 0.5f);
                if (depth < 29f || depth > 31.5f) wrongDepth++;
                checkedColumns++;
                sample = new Int3(x, y, z);
            }
        Check(wrongDepth == 0, $"bedrock starts 30 m below the original surface ({checkedColumns} columns checked)");

        EditResult dig = store.DigCells(new[] { sample });
        Check(dig.Headline == Verdict.Bedrock && dig.Removed.Count == 0 && store.IsSolid(sample),
            $"the tool is refused on bedrock at {sample} (result: {dig.Headline})");

        EditResult crater = store.Crater(sample.Center, ChunkStore.MaxCraterRadius);
        bool bedrockIntact = ChunkStore.SphereCells(sample, 4f).All(c => store.BaseMaterialAt(c) != CellMaterial.Bedrock || store.IsSolid(c));
        Check(crater.Bedrock > 0 && bedrockIntact, $"a crater at bedrock depth leaves bedrock intact ({crater.Bedrock} bedrock cells refused, {crater.Removed.Count} rock removed)");

        PlaceResult block = store.PlaceStructure(CellBox.FromSize(sample - new Int3(0, 0, 0), new Int3(2, 2, 2)));
        Check(block.Verdict == PlaceVerdict.Bedrock, $"a block can't be placed into bedrock (result: {block.Verdict})");
    }

    /// <summary>Rule 3: ground under and around structures and POIs refuses digs.</summary>
    void TestAnchors()
    {
        var store = NewStore();
        Int3 top = SurfaceCell(store, 30, 34);
        var box = CellBox.FromSize(new Int3(top.X, top.Y, top.Z), new Int3(2, 2, 2));
        PlaceResult placed = store.PlaceStructure(box);
        Check(placed.Verdict == PlaceVerdict.Placed, $"block placed at {box.Min} for the anchor test");

        Int3 under = new(box.Min.X, box.Min.Y - 1, box.Min.Z);
        Int3 beside = new(box.Max.X + 1, box.Min.Y - 1, box.Min.Z);
        Int3 deep = new(box.Min.X, box.Min.Y - ChunkStore.AnchorBelow, box.Min.Z);
        var refused = new[] { under, beside, deep }.Select(c => (c, r: store.DigCells(new[] { c }))).ToList();
        Check(refused.All(t => t.r.Headline == Verdict.Anchored && store.IsSolid(t.c)),
            "ground under, beside and 6 m below a block refuses the tool");

        Int3 away = SurfaceCell(store, box.Max.X + ChunkStore.AnchorSide + 2, box.Min.Z) - Int3.Up;
        Check(store.DigCells(new[] { away }).Removed.Count == 1, $"ground {ChunkStore.AnchorSide + 2} m from the block digs normally");

        CellBox zone = ChunkStore.AnchorBoxFor(box);
        var anchoredSolid = ChunkStore.BoxCells(zone).Where(store.IsSolid).ToList();
        EditResult crater = store.Crater(new NVector3(box.Max.X + 1.5f, box.Min.Y, box.Min.Z + 1f), ChunkStore.MaxCraterRadius);
        Check(crater.Anchored > 0 && crater.Removed.Count > 0 && anchoredSolid.All(store.IsSolid),
            $"a crater beside the block stops at its anchor zone ({crater.Anchored} cells held, {crater.Removed.Count} removed outside)");

        PoiSite poi = store.Source.PointsOfInterest[0];
        Int3 poiGround = new(poi.Footprint.Min.X + 2, poi.Footprint.Min.Y + 1, poi.Footprint.Min.Z + 2);
        while (!store.IsSolid(poiGround)) poiGround -= Int3.Up;
        EditResult poiDig = store.DigCells(new[] { poiGround });
        Check(poiDig.Headline == Verdict.Anchored, $"ground under the {poi.Name} POI refuses the tool");
        PlaceResult poiBlock = store.PlaceStructure(CellBox.FromSize(poiGround - Int3.Up, new Int3(2, 2, 2)));
        Check(poiBlock.Verdict == PlaceVerdict.AnchoredByPoi, "a block can't cut into a POI's anchored ground");
    }

    /// <summary>Rule 4: placing a block removes the terrain inside it.</summary>
    void TestBlocksClearTerrain()
    {
        var store = NewStore();
        Int3 top = SurfaceCell(store, -18, 40);
        var box = CellBox.FromSize(new Int3(top.X, top.Y - 1, top.Z), new Int3(2, 2, 2));
        int before = store.CountSolid(box);
        PlaceResult r = store.PlaceStructure(box);
        Check(r.Verdict == PlaceVerdict.Placed && before > 0 && store.CountSolid(box) == 0 && r.Cleared.Removed.Count == before,
            $"placing a half-sunk block clears the {before} solid cells inside it");
        Check(store.PlaceStructure(CellBox.FromSize(box.Min + new Int3(1, 0, 0), new Int3(2, 2, 2))).Verdict == PlaceVerdict.Overlaps,
            "a block can't overlap another block");
        var neighbour = CellBox.FromSize(box.Min + new Int3(2, 0, 0), new Int3(2, 2, 2));
        int neighbourSolid = store.CountSolid(neighbour);
        PlaceResult n = store.PlaceStructure(neighbour);
        Check(n.Verdict == PlaceVerdict.Placed && store.CountSolid(neighbour) == 0,
            $"a block next to a block clears its own ground, even inside the first one's anchor zone ({neighbourSolid} cells)");
    }

    /// <summary>Rule 10: crash and liftoff craters are capped spheres, remove-only.</summary>
    void TestCraterCap()
    {
        var store = NewStore();
        Int3 top = SurfaceCell(store, 50, -10);
        NVector3 center = top.Center;
        EditResult r = store.Crater(center, 40f);
        float farthest = r.Removed.Count == 0 ? 0 : r.Removed.Max(c => NVector3.Distance(c.Cell.Center, center));
        Check(r.Removed.Count > 0 && r.Removed.Count <= ChunkStore.MaxCraterCells && farthest <= ChunkStore.MaxCraterRadius + 0.01f,
            $"a 40 m crater request is capped: {r.Removed.Count} cells (cap {ChunkStore.MaxCraterCells}), none beyond {ChunkStore.MaxCraterRadius} m");
    }

    /// <summary>Rule 6: water is static. A pit dug beside a lake stays dry.</summary>
    void TestWaterStaysPut()
    {
        var store = NewStore();
        Int3 shore = default, lake = default;
        bool found = false;
        for (int r = 0; r < 60 && !found; r++)
            for (int dx = -r; dx <= r && !found; dx++)
            {
                int x = (int)SimpleTerrainSource.LakeCenter.X + dx, z = (int)SimpleTerrainSource.LakeCenter.Y - r;
                if (!store.IsWaterColumn(x, z) && store.IsWaterColumn(x, z + 1))
                {
                    shore = new Int3(x, 0, z);
                    lake = new Int3(x, 0, z + 1);
                    found = true;
                }
            }
        if (!found)
        {
            Fail("no lake shore found for the water test");
            return;
        }

        float level = store.Source.WaterLevel;
        int top = (int)MathF.Floor(store.Source.SurfaceHeight(shore.X + 0.5f, shore.Z + 0.5f));
        var pit = new CellBox(new Int3(shore.X - 1, (int)level - 4, shore.Z - 2), new Int3(shore.X + 2, top + 2, shore.Z + 1));
        // Only land columns: a hole in the lake bed is under the (static) lake, as it should be.
        EditResult dug = store.DigCells(ChunkStore.BoxCells(pit).Where(c => !store.IsWaterColumn(c.X, c.Z)));
        int wet = dug.Removed.Count(c => store.IsWater(c.Cell));
        var lakeCell = new Int3(lake.X, (int)level - 1, lake.Z);
        Check(dug.Removed.Count > 0 && wet == 0 && (store.IsWater(lakeCell) || store.IsSolid(lakeCell)),
            $"a pit dug 4 m below the water line beside the lake stays dry ({dug.Removed.Count} cells, {wet} wet)");
    }

    /// <summary>Rule 9: caves come from the generator (and only player tools change terrain).</summary>
    void TestGeneratorCaves()
    {
        var store = NewStore();
        int caveCells = 0;
        for (int x = -60; x < 60; x++)
            for (int z = -10; z < 60; z++)
            {
                float h = store.Source.SurfaceHeight(x + 0.5f, z + 0.5f);
                for (int y = (int)h - 18; y < (int)h - 3; y++)
                    if (store.BaseMaterialAt(new Int3(x, y, z)) == CellMaterial.Air) caveCells++;
            }
        Check(caveCells > 500 && store.RemovedCount == 0,
            $"the generator carves caves ({caveCells:N0} open cells 3–18 m underground) with no player edits");
    }

    // ---- Edit log, replication, saves -------------------------------------------------

    /// <summary>Rule 8: the same edits on two separate terrain instances give identical chunks.</summary>
    void TestReplication()
    {
        var a = NewStore();
        var rng = new Random(77);
        var diffs = new List<EditLog>();
        for (int i = 0; i < 400; i++)
        {
            Int3 at = RandomSurfaceCell(a, rng, 56) - new Int3(0, rng.Next(0, 4), 0);
            EditResult r = i % 25 == 0 ? a.Crater(at.Center, 4f) : a.Dig(at, 1.42f);
            diffs.Add(r.ToDiff());
        }
        var merged = new EditLog();
        foreach (EditLog d in diffs) merged.Merge(d);
        byte[] wire = merged.Serialize();

        // B: a fresh process, as far as the terrain knows. Its own source, its own base cache.
        var b = NewStore();
        ApplyReport report = b.ApplyDiff(EditLog.Deserialize(wire));

        // C: the individual diffs, shuffled, some delivered twice.
        var c = NewStore();
        foreach (EditLog d in diffs.OrderBy(_ => rng.Next()).Concat(diffs.Take(50))) c.ApplyDiff(d);

        var chunks = new HashSet<Int3>();
        foreach (Int3 cell in merged.Cells()) ChunkStore.AddDirty(cell, chunks);
        int cellDiff = 0, meshDiff = 0;
        var meshA = new SurfaceNetsMesher();
        var meshB = new SurfaceNetsMesher();
        foreach (Int3 chunk in chunks)
        {
            ulong ha = a.ChunkHash(chunk);
            if (ha != b.ChunkHash(chunk) || ha != c.ChunkHash(chunk)) cellDiff++;
            ulong ma = meshA.Build(a.CaptureNeighborhood(chunk)).ComputeHash();
            if (ma != meshB.Build(b.CaptureNeighborhood(chunk)).ComputeHash() || ma != meshB.Build(c.CaptureNeighborhood(chunk)).ComputeHash()) meshDiff++;
        }
        Check(report.Invalid.Count == 0 && cellDiff == 0 && meshDiff == 0 && a.EditStateHash() == b.EditStateHash() && a.EditStateHash() == c.EditStateHash(),
            $"replication: 400 edits applied to separate instances give identical hashes for all {chunks.Count} touched chunks (cells and meshes)");
        Info($"merged log: {merged.CellCount:N0} cells in {merged.ChunkCount} chunks = {wire.Length:N0} bytes ({(double)wire.Length / merged.CellCount:F2} bytes/cell); " +
             $"order-shuffled and duplicated diffs converge too");
    }

    /// <summary>The edit log has exactly one byte form: it round-trips byte for byte.</summary>
    void TestRoundTrip()
    {
        var store = NewStore();
        var rng = new Random(5);
        for (int i = 0; i < 200; i++) store.Dig(RandomSurfaceCell(store, rng, 50), 1.42f);
        // One fully dug chunk exercises the bitset encoding.
        var dense = CellBox.FromSize(new Int3(-64, 0, -64), new Int3(16, 16, 16));
        store.DigCells(ChunkStore.BoxCells(dense));

        EditLog log = store.ExportEdits();
        byte[] first = log.Serialize();
        EditLog back = EditLog.Deserialize(first);
        byte[] second = back.Serialize();
        Check(first.AsSpan().SequenceEqual(second) && back.ContentEquals(log),
            $"edit log round-trips byte for byte ({log.CellCount:N0} cells, {log.ChunkCount} chunks, {first.Length:N0} bytes)");

        byte[] empty = new EditLog().Serialize();
        Check(EditLog.Deserialize(empty).Serialize().AsSpan().SequenceEqual(empty), $"an empty log round-trips ({empty.Length} bytes)");

        byte[] save = TerrainSave.Encode(SaveData.From(store, 9));
        Check(TerrainSave.Encode(TerrainSave.Decode(save)).AsSpan().SequenceEqual(save), $"a save file round-trips byte for byte ({save.Length:N0} bytes)");
    }

    /// <summary>Rule 11: a crash between writing the temp file and swapping it in loses nothing.</summary>
    void TestAtomicSave()
    {
        string path = Path.Combine(_saveDir, "crash.ghs");
        var store = NewStore();
        var rng = new Random(11);
        for (int i = 0; i < 60; i++) store.Dig(RandomSurfaceCell(store, rng, 40), 1.42f);
        store.PlaceStructure(CellBox.FromSize(SurfaceCell(store, 10, 40), new Int3(2, 2, 2)));
        ulong hashA = store.EditStateHash();
        SaveReport first = TerrainSave.Save(store, path, 1);
        Check(first.Ok, $"saved state A ({first.Cells} cells, {first.Bytes:N0} bytes, {first.Milliseconds:F1} ms)");

        for (int i = 0; i < 60; i++) store.Dig(RandomSurfaceCell(store, rng, 40), 1.42f);
        ulong hashB = store.EditStateHash();
        bool crashed = false;
        try
        {
            TerrainSave.Save(store, path, 2, SaveCrashPoint.AfterTempWritten);
        }
        catch (SimulatedCrashException)
        {
            crashed = true;
        }
        LoadResult afterCrash = TerrainSave.Load(path, NewStore().Base);
        Check(crashed && File.Exists(TerrainSave.TempPath(path)) && afterCrash.Ok && afterCrash.Data.Sequence == 1 && afterCrash.Store.EditStateHash() == hashA,
            "killed after the temp file was written, before the swap: the old save is intact and loads");

        // A crash halfway through writing the temp file leaves garbage there: still harmless.
        File.WriteAllBytes(TerrainSave.TempPath(path), new byte[] { 0x47, 0x48, 0x53 });
        Check(TerrainSave.Load(path, NewStore().Base).Ok, "a torn temp file from an earlier crash doesn't affect loading");

        SaveReport second = TerrainSave.Save(store, path, 3);
        LoadResult latest = TerrainSave.Load(path, NewStore().Base);
        LoadResult backup = TerrainSave.Load(TerrainSave.BackupPath(path), NewStore().Base);
        Check(second.Ok && latest.Ok && latest.Store.EditStateHash() == hashB && backup.Ok && backup.Store.EditStateHash() == hashA,
            "the next save swaps in cleanly and keeps the previous one as .bak");
    }

    /// <summary>Rule 11: the load audit refuses corrupted saves rather than silently dropping edits.</summary>
    void TestLoadAudit()
    {
        string path = Path.Combine(_saveDir, "audit.ghs");
        var store = NewStore();
        var rng = new Random(23);
        for (int i = 0; i < 80; i++) store.Dig(RandomSurfaceCell(store, rng, 40), 1.42f);
        store.PlaceStructure(CellBox.FromSize(SurfaceCell(store, -30, 44), new Int3(2, 2, 2)));
        SaveData good = SaveData.From(store, 4);
        byte[] bytes = TerrainSave.Encode(good);
        BaseCache baseCache = NewStore().Base;

        string Refusal(byte[] data)
        {
            File.WriteAllBytes(path, data);
            LoadResult r = TerrainSave.Load(path, baseCache);
            return r.Ok ? null : r.Error;
        }

        byte[] flipped = (byte[])bytes.Clone();
        flipped[bytes.Length / 2] ^= 0x10;
        string e1 = Refusal(flipped);
        Check(e1 != null, $"a flipped bit is refused: \"{e1}\"");

        string e2 = Refusal(bytes[..(bytes.Length * 2 / 3)]);
        Check(e2 != null, $"a truncated file is refused: \"{e2}\"");

        // A buggy writer that dropped a chunk but kept the header: every checksum is valid.
        var lossy = SaveData.From(store, 4);
        Int3 dropped = lossy.Edits.Chunks.First();
        var trimmed = new EditLog();
        foreach (Int3 cell in lossy.Edits.Cells())
            if (ChunkStore.ChunkOf(cell) != dropped) trimmed.Add(cell);
        lossy.Edits = trimmed;
        string e3 = Refusal(TerrainSave.Encode(lossy));
        Check(e3 != null, $"a save that silently lost a chunk of edits is refused: \"{e3}\"");

        // An edit that can't exist on this terrain (bedrock), again with valid checksums.
        var impossible = SaveData.From(store, 4);
        Int3 bedrock = SurfaceCell(store, 0, 0) - new Int3(0, 34, 0);
        impossible.Edits.Add(bedrock);
        impossible.RemovedCells++;
        string e4 = Refusal(TerrainSave.Encode(impossible));
        Check(e4 != null, $"a save with an edit on bedrock is refused: \"{e4}\"");

        var buried = SaveData.From(store, 4);
        buried.Structures.Add(new Structure(999, CellBox.FromSize(SurfaceCell(store, 40, 40) - new Int3(0, 6, 0), new Int3(2, 2, 2))));
        string e5 = Refusal(TerrainSave.Encode(buried));
        Check(e5 != null, $"a save with a structure buried in terrain is refused: \"{e5}\"");

        // The main save is corrupt, the backup is fine: loading falls back to it and says so.
        File.WriteAllBytes(TerrainSave.BackupPath(path), bytes);
        File.WriteAllBytes(path, flipped);
        LoadResult fallback = TerrainSave.LoadLatest(path, baseCache);
        Check(fallback.Ok && fallback.Store.EditStateHash() == good.StateHash && fallback.Error != null,
            "with a corrupt save and a good backup, loading uses the backup and reports why");
    }

    // ---- Mesher -----------------------------------------------------------------------

    /// <summary>Chunk seams: shared vertices match bit for bit and no edge is left open.</summary>
    void TestSeams()
    {
        var store = NewStore();
        // The corner where eight chunks meet, near the surface, dug through in every direction.
        const int X = 16, Z = 16;
        float h = store.Source.SurfaceHeight(X, Z);
        int cy = (int)MathF.Round(h / ChunkStore.Size) * ChunkStore.Size;
        var corner = new Int3(X, cy, Z);
        var cells = new List<Int3>(ChunkStore.SphereCells(corner, 3.2f));
        for (int y = cy - 4; y < (int)h + 2; y++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    cells.Add(new Int3(X + dx, y, Z + dz));
        for (int x = X - 9; x < X + 9; x++) cells.Add(new Int3(x, cy + 1, Z));
        EditResult dug = store.DigCells(cells);

        Int3 c0 = ChunkStore.ChunkOf(corner) - Int3.One;
        var meshes = new List<ChunkMesh>();
        var mesher = new SurfaceNetsMesher();
        for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
                for (int x = 0; x < 2; x++)
                    meshes.Add(mesher.Build(store.CaptureNeighborhood(c0 + new Int3(x, y, z)), recordCubes: true));

        int shared = 0, mismatched = 0;
        foreach (ChunkMesh a in meshes)
            foreach (ChunkMesh b in meshes)
            {
                if (a == b) continue;
                var byCube = new Dictionary<Int3, int>();
                for (int i = 0; i < b.Cubes.Length; i++) byCube[b.Cubes[i]] = i;
                for (int i = 0; i < a.Cubes.Length; i++)
                {
                    if (!byCube.TryGetValue(a.Cubes[i], out int j)) continue;
                    shared++;
                    if (!SameBits(a.Positions[i], b.Positions[j]) || !SameBits(a.Normals[i], b.Normals[j]) || a.Materials[i] != b.Materials[j])
                        mismatched++;
                }
            }
        Check(shared > 0 && mismatched == 0,
            $"seams: {shared / 2} vertices shared across the 8 chunks around {corner} match bit for bit ({dug.Removed.Count} cells dug across the borders)");

        // Weld by exact position: an edge used by one triangle only would be a crack.
        var edges = new Dictionary<(NVector3, NVector3), int>();
        int triangles = 0;
        foreach (ChunkMesh m in meshes)
            for (int t = 0; t < m.Indices.Length; t += 3)
            {
                triangles++;
                for (int e = 0; e < 3; e++)
                {
                    NVector3 p = m.Positions[m.Indices[t + e]], q = m.Positions[m.Indices[t + (e + 1) % 3]];
                    var key = Less(p, q) ? (p, q) : (q, p);
                    edges[key] = edges.GetValueOrDefault(key) + 1;
                }
            }
        NVector3 lo = ChunkStore.ChunkOrigin(c0).ToVector() + NVector3.One * 1.5f;
        NVector3 hi = ChunkStore.ChunkOrigin(c0 + new Int3(2, 2, 2)).ToVector() - NVector3.One * 1.5f;
        bool Inside(NVector3 v) => v.X > lo.X && v.Y > lo.Y && v.Z > lo.Z && v.X < hi.X && v.Y < hi.Y && v.Z < hi.Z;
        int interior = 0, open = 0;
        foreach (((NVector3 p, NVector3 q), int count) in edges)
        {
            if (!Inside(p) || !Inside(q)) continue;
            interior++;
            if (count == 1) open++;
        }
        Check(interior > 0 && open == 0, $"no cracks: all {interior:N0} interior edges of {triangles:N0} triangles are closed across the seams");

        // Godot draws clockwise triangles as front faces: check every triangle faces out of the ground.
        int inward = 0, faces = 0;
        foreach (ChunkMesh m in meshes)
            for (int t = 0; t < m.Indices.Length; t += 3)
            {
                NVector3 a = m.Positions[m.Indices[t]], b = m.Positions[m.Indices[t + 1]], c = m.Positions[m.Indices[t + 2]];
                NVector3 n = m.Normals[m.Indices[t]] + m.Normals[m.Indices[t + 1]] + m.Normals[m.Indices[t + 2]];
                NVector3 cross = NVector3.Cross(b - a, c - a);
                if (cross.LengthSquared() < 1e-10f) continue;
                faces++;
                if (NVector3.Dot(cross, n) > 0f) inward++;
            }
        Check(inward * 100 < faces, $"triangle winding agrees with the normals ({faces - inward:N0} of {faces:N0} outward)");
    }

    static bool SameBits(NVector3 a, NVector3 b) =>
        BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X) &&
        BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y) &&
        BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);

    static bool Less(NVector3 a, NVector3 b) => a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

    /// <summary>Remesh cost on one thread, with nothing else running: the low-end PC baseline.</summary>
    void TestMeshTimingSingleThread()
    {
        var store = NewStore();
        var rng = new Random(3);
        var chunks = new HashSet<Int3>();
        for (int i = 0; i < 300; i++)
        {
            EditResult r = store.Dig(RandomSurfaceCell(store, rng, 100), 1.42f);
            chunks.UnionWith(r.DirtyChunks);
        }
        var mesher = new SurfaceNetsMesher();
        // Generation is a one-off cost, not a remesh: build every base chunk the meshes will read.
        foreach (Int3 c in chunks)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        store.Base.Get(c + new Int3(dx, dy, dz));
        foreach (Int3 c in chunks.Take(20)) mesher.Build(store.CaptureNeighborhood(c)); // warm up the JIT

        var stats = new Stats();
        int triangles = 0;
        foreach (Int3 c in chunks)
        {
            long t0 = Stopwatch.GetTimestamp();
            ChunkMesh m = mesher.Build(store.CaptureNeighborhood(c));
            stats.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            triangles += m.TriangleCount;
        }
        Info($"single-thread remesh of {stats.Count} dug chunks: avg {stats.Average:F3} ms, max {stats.Max:F3} ms ({triangles / Math.Max(1, stats.Count)} triangles/chunk)");
        Check(stats.Average < 5.0, $"single-thread remesh averages well under 5 ms ({stats.Average:F3} ms)");
    }

    // ---- Scene ------------------------------------------------------------------------

    /// <summary>Rule 7: bodies found inside terrain are moved to open space within a few frames.</summary>
    async System.Threading.Tasks.Task TestStuckSafe()
    {
        ChunkStore store = _main.Store;
        Player player = _main.Player;

        // No false alarms: standing on a slope next to a mesa for half a second.
        Vector3 standing = player.GlobalPosition;
        int rescuesBefore = _main.Guard.Rescues;
        for (int i = 0; i < 30; i++) await NextPhysics();
        Check(_main.Guard.Rescues == rescuesBefore && player.GlobalPosition.DistanceTo(standing) < 0.5f,
            "a player standing on the ground is left alone");

        Int3 surface = SurfaceCell(store, 4, 10);
        await Rescue("player", () => player.Teleport(new Vector3(surface.X + 0.5f, surface.Y - 4f, surface.Z + 0.5f)),
            () => player.GlobalPosition, new BodyShape(player.CurrentHeight, Player.Radius));

        Int3 crateSpot = SurfaceCell(store, -6, 2);
        await Rescue("supply crate (stand-in for creatures and vehicles)", () =>
        {
            _main.Crate.GlobalPosition = new Vector3(crateSpot.X + 0.5f, crateSpot.Y - 2.5f, crateSpot.Z + 0.5f);
            _main.Crate.LinearVelocity = Vector3.Zero;
        }, () => _main.Crate.GlobalPosition - Vector3.Up * 0.5f, new BodyShape(1f, 0.5f));
    }

    async System.Threading.Tasks.Task Rescue(string label, Action bury, Func<Vector3> feet, BodyShape shape)
    {
        await NextPhysics();
        int before = _main.Guard.Rescues;
        bury();
        bool buried = StuckResolver.IsInsideTerrain(_main.Store, ToN(feet()), shape);
        int frames = 0;
        while (_main.Guard.Rescues == before && frames < 30)
        {
            await NextPhysics();
            frames++;
        }
        for (int i = 0; i < 20; i++) await NextPhysics(); // let it settle and make sure it stays out
        bool free = !StuckResolver.IsInsideTerrain(_main.Store, ToN(feet()), shape);
        Check(buried && _main.Guard.Rescues > before && frames <= 5 && free,
            $"a {label} buried in solid ground is moved to open space in {frames} physics frames, and stays out");
    }

    /// <summary>Crouching: a smaller capsule, no standing up under an overhang, and no false rescues.</summary>
    async System.Threading.Tasks.Task TestCrouch()
    {
        ChunkStore store = _main.Store;
        Player player = _main.Player;

        // Out in the open: crouch shrinks the capsule; letting go stands straight back up.
        await NextPhysics();
        player.CrouchOverride = true;
        for (int i = 0; i < 5; i++) await NextPhysics();
        float crouched = player.CapsuleHeight;
        Check(player.IsCrouching && Mathf.IsEqualApprox(crouched, Player.CrouchHeight),
            $"holding crouch shrinks the capsule ({Player.StandHeight:F2} → {crouched:F2} m) and lowers the eye");
        player.CrouchOverride = false;
        for (int i = 0; i < 3; i++) await NextPhysics();
        Check(!player.IsCrouching && Mathf.IsEqualApprox(player.CapsuleHeight, Player.StandHeight), "letting go in the open stands back up");

        // A wide crawlspace 1 m high, 5 m underground: crouched fits, standing doesn't.
        Int3 top = SurfaceCell(store, -20, 24);
        int floorY = top.Y - 6;
        var crawl = new CellBox(new Int3(top.X - 3, floorY, top.Z - 3), new Int3(top.X + 4, floorY + 1, top.Z + 4));
        EditResult dug = store.DigCells(ChunkStore.BoxCells(crawl));
        _main.View.MarkDirty(dug.DirtyChunks);
        while (!_main.View.IsIdle) await NextFrame();
        await NextPhysics();

        int rescues = _main.Guard.Rescues;
        player.ForceCrouch();
        player.CrouchOverride = false; // Ctrl released straight away
        var inside = new Vector3(top.X + 0.5f, floorY + 0.05f, top.Z + 0.5f);
        player.Teleport(inside);
        bool everStood = false, couldStand = false;
        for (int i = 0; i < 60; i++)
        {
            await NextPhysics();
            everStood |= !player.IsCrouching;
            couldStand |= player.CanStand();
        }
        Check(!everStood && !couldStand && player.IsCrouching,
            "under a 1 m overhang, releasing crouch doesn't stand up: there's no room (60 frames)");
        Check(_main.Guard.Rescues == rescues && player.GlobalPosition.DistanceTo(inside) < 0.6f,
            "the stuck guard leaves a crouched player in a crawlspace alone (no false rescue)");

        // Still crouched and unable to stand, now genuinely buried: rescued to standing room.
        player.Teleport(inside - Vector3.Up * 3f);
        int frames = 0;
        while (_main.Guard.Rescues == rescues && frames < 30)
        {
            await NextPhysics();
            frames++;
        }
        for (int i = 0; i < 20; i++) await NextPhysics();
        bool out_ = !StuckResolver.IsInsideTerrain(store, ToN(player.GlobalPosition), new BodyShape(player.CurrentHeight, Player.Radius));
        Check(_main.Guard.Rescues > rescues && frames <= 5 && out_ && !player.IsCrouching && player.CanStand(),
            $"a crouched player buried below the crawlspace is rescued in {frames} frames to room where they stand up again");
    }

    /// <summary>
    /// The keys, end to end: dig and build with the tool, save, dig on, load, and the player
    /// left standing in a hole the load filled back in is rescued. Then a crater and a ping.
    /// </summary>
    async System.Threading.Tasks.Task TestGameActions()
    {
        ChunkStore store = _main.Store;
        Player player = _main.Player;
        GatherTool tool = _main.Tool;

        Int3 spot = SurfaceCell(store, 14, -16);
        player.Teleport(new Vector3(spot.X + 0.5f, spot.Y + 1.2f, spot.Z + 0.5f));
        player.SetLook(0f, -0.75f);
        for (int i = 0; i < 20; i++) await NextPhysics();

        int gathered = tool.Gathered.Sum(), dug = 0;
        for (int i = 0; i < 3; i++)
        {
            await NextPhysics();
            dug += tool.DigAtTarget()?.Removed.Count ?? 0;
        }
        Check(dug > 0 && tool.Gathered.Sum() == gathered + dug, $"the tool digs under the crosshair and counts what it gathers ({dug} cells)");

        player.SetLook(Mathf.Pi, -0.6f);
        for (int i = 0; i < 3; i++) await NextPhysics();
        PlaceResult block = tool.PlaceBlock();
        Check(block?.Verdict == PlaceVerdict.Placed && _main.Structures.Count == store.Structures.Count,
            $"the tool places a block ({block?.Verdict.ToString() ?? "no target"}, {block?.Cleared?.Removed.Count ?? 0} cells cleared)");

        SaveReport saved = _main.Save();
        ulong savedHash = store.EditStateHash();
        int savedStructures = store.Structures.Count;

        // Walk clear of the block's anchored ground, then dig straight down: the player drops
        // into a hole the save knows nothing about.
        Int3 away = SurfaceCell(store, spot.X + 12, spot.Z - 6);
        player.Teleport(new Vector3(away.X + 0.5f, away.Y + 1.2f, away.Z + 0.5f));
        spot = away;
        for (int i = 0; i < 10; i++) await NextPhysics();
        player.SetLook(0f, -1.5f);
        for (int i = 0; i < 12; i++)
        {
            await NextPhysics();
            await NextPhysics();
            tool.DigAtTarget();
        }
        for (int i = 0; i < 20; i++) await NextPhysics();
        float sunk = spot.Y + 1f - player.GlobalPosition.Y;

        int rescues = _main.Guard.Rescues;
        LoadResult loaded = _main.Load();
        Check(saved.Ok && loaded.Ok && store.EditStateHash() == savedHash && store.Structures.Count == savedStructures && _main.Structures.Count == savedStructures,
            $"F5 then F9: the load audit passes and the world is exactly as saved ({saved.Cells} cells, {savedStructures} blocks)");
        for (int i = 0; i < 30; i++) await NextPhysics();
        bool inside = StuckResolver.IsInsideTerrain(store, ToN(player.GlobalPosition), new BodyShape(player.CurrentHeight, Player.Radius));
        Check(sunk > 2f && _main.Guard.Rescues > rescues && !inside,
            $"the player, {sunk:F1} m down a hole the load filled in, is moved out of the ground");

        player.SetLook(0.4f, -0.35f);
        await NextPhysics();
        await NextPhysics();
        EditResult crater = tool.Crater();
        Check(crater != null && crater.Removed.Count > 0 && crater.Removed.Count <= ChunkStore.MaxCraterCells,
            $"C blasts a capped crater at the crosshair ({crater?.Removed.Count ?? 0} cells)");

        var deposits = _main.PingOre();
        Info($"Q detector: {deposits.Count} deposits within {OreDetector.Range:F0} m" +
             (deposits.Count > 0 ? $", nearest {deposits[0].Distance:F1} m ({deposits[0].Cells} cells)" : ""));
        while (!_main.View.IsIdle) await NextFrame();
    }

    /// <summary>Rule 5: no terrain physics. A floating island stays exactly where it is.</summary>
    async System.Threading.Tasks.Task TestFloatingIsland()
    {
        ChunkStore store = _main.Store;
        Int3 top = SurfaceCell(store, 26, 12);
        var island = new CellBox(new Int3(top.X, top.Y - 3, top.Z), new Int3(top.X + 3, top.Y - 1, top.Z + 3));
        var shell = island.Grow(2, 2, 6);
        EditResult carved = store.DigCells(ChunkStore.BoxCells(shell).Where(c => !island.Contains(c)));
        _main.View.MarkDirty(carved.DirtyChunks);
        int solid = store.CountSolid(island);
        int removed = store.RemovedCount;
        while (!_main.View.IsIdle) await NextFrame();
        for (int i = 0; i < 120; i++) await NextPhysics();
        Check(solid == island.Volume && store.CountSolid(island) == solid && store.RemovedCount == removed,
            $"a {solid}-cell island carved free of the ground floats in place for 2 s (no cave-ins, no falling)");
    }

    /// <summary>The proof key: wipe every chunk mesh, regenerate from the seed plus edits, compare.</summary>
    async System.Threading.Tasks.Task TestRebuildIdentical()
    {
        int compared = -1, mismatched = -1;
        double seconds = 0;
        _main.View.RebuildVerified += (c, m, s) =>
        {
            compared = c;
            mismatched = m;
            seconds = s;
        };
        _main.View.WipeAndRebuild();
        while (compared < 0) await NextFrame();
        Check(mismatched == 0 && compared == ChunkStore.WorldChunks.Volume,
            $"wipe and rebuild from seed + edits: all {compared} chunk meshes identical ({seconds:F2} s)");
    }

    /// <summary>1000 random digs through the real pipeline: per-chunk remesh time and the worst frame.</summary>
    async System.Threading.Tasks.Task TestPerformance()
    {
        ChunkStore store = _main.Store;
        TerrainView view = _main.View;
        var rng = new Random(1000);
        while (!view.IsIdle) await NextFrame();
        await NextFrame();
        view.ResetFrameStats();
        long blocksBefore = view.BlocksRebuilt, trianglesBefore = view.BlockTriangles;

        const int Edits = 1000, PerFrame = 10;
        var frame = new Stats();
        var edit = new Stats();
        double lastEditMs = 0;
        int done = 0, frames = 0, cells = 0, chunks = 0;
        var wall = Stopwatch.StartNew();
        while (done < Edits || !view.IsIdle)
        {
            await NextFrame();
            // The previous frame's whole terrain cost: its edits plus the view's work after them.
            if (frames > 0) frame.Add(lastEditMs + view.LastFrameMs);
            frames++;

            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < PerFrame && done < Edits; i++, done++)
            {
                Int3 at = RandomSurfaceCell(store, rng, 60) - new Int3(0, rng.Next(0, 6), 0);
                EditResult r = done % 50 == 49 ? store.Crater(at.Center, 4f) : store.Dig(at, GatherTool.DigRadius);
                view.MarkDirty(r.DirtyChunks);
                cells += r.Removed.Count;
                chunks += r.DirtyChunks.Count;
            }
            lastEditMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (lastEditMs > 0) edit.Add(lastEditMs / PerFrame);
        }

        Info($"{Edits} random edits ({cells:N0} cells, 20 of them craters) over {frames} frames in {wall.Elapsed.TotalSeconds:F2} s; {chunks:N0} chunk remeshes requested");
        Info($"remesh per chunk (worker threads): avg {view.RemeshMs.Average:F3} ms, max {view.RemeshMs.Max:F3} ms over {view.RemeshMs.Count:N0} remeshes, {view.RemeshMs.Over5} over 5 ms " +
             $"(Surface Nets alone avg {view.SurfaceNetsMs.Average:F3} / max {view.SurfaceNetsMs.Max:F3} ms)");
        Info($"mesh swap-in per chunk (main thread): avg {view.ApplyMs.Average:F3} ms, max {view.ApplyMs.Max:F3} ms " +
             $"(render mesh avg {view.ApplyMeshMs.Average:F3} / max {view.ApplyMeshMs.Max:F3}, collision avg {view.ApplyShapeMs.Average:F3} / max {view.ApplyShapeMs.Max:F3})");
        Info($"collision blocks rebuilt: {view.BlocksRebuilt - blocksBefore:N0} ({(double)(view.BlockTriangles - trianglesBefore) / Math.Max(1, view.BlocksRebuilt - blocksBefore):F0} triangles each)");
        Info($"edit per dig (main thread): avg {edit.Average:F3} ms, max {edit.Max:F3} ms");
        Info($"terrain cost per frame (main thread): avg {frame.Average:F3} ms, worst {frame.Max:F3} ms");
        Check(view.RemeshMs.Average < 5.0, $"remesh averages well under 5 ms per chunk ({view.RemeshMs.Average:F3} ms)");
        Check(frame.Max < 8.0, $"no terrain hitch: the worst frame spends {frame.Max:F2} ms on terrain at {PerFrame} digs per frame");
    }

    // ---- Helpers ----------------------------------------------------------------------

    static ChunkStore NewStore() => new(new SimpleTerrainSource(Seed));

    static Int3 SurfaceCell(ChunkStore store, int x, int z) => new(x, store.TopSolid(x, z), z);

    static Int3 RandomSurfaceCell(ChunkStore store, Random rng, int range)
    {
        int x = rng.Next(-range, range), z = rng.Next(-range, range);
        return new Int3(x, (int)MathF.Floor(store.Source.SurfaceHeight(x + 0.5f, z + 0.5f)), z);
    }

    static NVector3 ToN(Vector3 v) => new(v.X, v.Y, v.Z);

    SignalAwaiter NextFrame() => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    SignalAwaiter NextPhysics() => ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

    static void Section(string name) => GD.Print($"\n== {name}");
    static void Info(string what) => GD.Print($"      {what}");

    void Check(bool ok, string what)
    {
        if (ok) Pass(what);
        else Fail(what);
    }

    void Pass(string what)
    {
        _checks++;
        GD.Print($"PASS  {what}");
    }

    void Fail(string what)
    {
        _checks++;
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
