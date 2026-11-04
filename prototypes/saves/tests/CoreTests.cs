using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using GalHack.Persistence;
using GalHack.Records;
using GalHack.Tests;

namespace Saves.Tests;

/// <summary>
/// The persistence core's tests. Pure C#: no nodes, no frames. Every way of losing data is a
/// release blocker, so each group tries to lose some and checks it can't.
/// </summary>
public sealed class CoreTests
{
    const string Committed = "manifest.json replaced: committed";

    readonly TestLog _log;
    readonly string _root;
    readonly RecordSchemas _schemas = GameRecords.Schemas();

    public CoreTests(TestLog log, string root)
    {
        _log = log;
        _root = root;
    }

    /// <summary>tests/SaveWorker's build output, for the process-kill test.</summary>
    public string WorkerDll { get; init; }

    /// <summary>tests/fixtures/save-v1: a save written in the previous format, checked in, never changed.</summary>
    public string FixtureV1 { get; init; }

    public void Run(Func<string, bool> wanted)
    {
        void Group(string name, Action test)
        {
            if (!wanted(name)) return;
            try
            {
                test();
            }
            catch (Exception e)
            {
                _log.Fail($"{name}: unexpected {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }
        Group("crash", CrashPoints);
        Group("restore", RestoreUnderCrash);
        Group("kills", ProcessKills);
        Group("soak", Soak);
        Group("corruption", Corruption);
        Group("cleanup", NoSilentCleanup);
        Group("coremode", CoreModeSwitch);
        Group("retention", Retention);
        Group("migration", Migration);
        Group("perf", Performance);
    }

    // ---- Core mode switch --------------------------------------------------------------

    /// <summary>
    /// Bases and capital ships share every block, and a core can switch modes. The mode is one
    /// field in the structure's payload; switching must not drop, convert or move anything.
    /// </summary>
    void CoreModeSwitch()
    {
        _log.Section("Core mode switch");
        string pristine = Dir("coremode/pristine");
        var (store, table) = NewWorld(pristine);
        // Something stored in the base, to prove a switch leaves it where it is.
        WorldOps.MoveItems(table, StarterWorld.CrateB, StarterWorld.CrateA, ItemKinds.Plate, 4);
        Commit(store, table);
        Dictionary<string, SaveRecord> before = table.Snapshot();
        StructureData baseBefore = GameRecords.Read<StructureData>(before[StarterWorld.BaseId]);

        bool switched = WorldOps.SwitchCoreMode(table, StarterWorld.BaseId, CoreModes.CapitalShip);
        Dictionary<string, SaveRecord> after = table.Snapshot();
        SaveRecord core = after[StarterWorld.BaseId];
        StructureData coreData = GameRecords.Read<StructureData>(core);
        bool onlyModeChanged =
            coreData.Mode == CoreModes.CapitalShip
            && core.Type == RecordTypes.Structure && core.ZoneId == before[StarterWorld.BaseId].ZoneId
            && core.Blocks == before[StarterWorld.BaseId].Blocks && core.Items == before[StarterWorld.BaseId].Items
            && SameBlocks(coreData, baseBefore)
            && table.PendingChanges == 1
            && before.Where(kv => kv.Key != StarterWorld.BaseId).All(kv => after[kv.Key] == kv.Value);
        _log.Check(switched && onlyModeChanged,
            $"switching the base's core to capital-ship mode changes one field of one record: the same {coreData.Blocks.Count} blocks in the same cells, every other record untouched");

        Commit(store, table);
        LoadResult reloaded = new SaveStore(pristine, _schemas).Load();
        _log.Check(reloaded.Ok && Same(reloaded.Table.Snapshot(), after) && reloaded.Found.Items == AuditCounts.Of(before.Values).Items,
            $"saved and reloaded in capital-ship mode: all {reloaded.Found?.Blocks} blocks and {reloaded.Found?.Items} items come back, crates included");

        bool typeChangeRefused = false;
        try { table.Put(core with { Type = RecordTypes.Ship }); }
        catch (ArgumentException) { typeChangeRefused = true; }
        _log.Check(typeChangeRefused, "a record can never change type, so a mode switch can't turn into a copy-and-delete");

        // Crash the mode-switch commit at every step: either mode, but never a block or item short.
        string switchedBack = Copy(pristine, "coremode/traced");
        var trace = new List<string>();
        var tracedStore = new SaveStore(switchedBack, _schemas);
        RecordTable t0 = tracedStore.Load().Table;
        WorldOps.SwitchCoreMode(t0, StarterWorld.BaseId, CoreModes.Base);
        tracedStore.Commit(t0.PrepareCommit(), new FaultInjector { Trace = trace });
        int wrong = 0;
        for (int i = 0; i < trace.Count; i++)
        {
            string dir = Copy(pristine, $"coremode/point-{i:D2}");
            var s = new SaveStore(dir, _schemas);
            RecordTable t = s.Load().Table;
            WorldOps.SwitchCoreMode(t, StarterWorld.BaseId, CoreModes.Base);
            try { s.Commit(t.PrepareCommit(), new FaultInjector { CrashAt = i }); }
            catch (SimulatedCrash) { }
            LoadResult r = new SaveStore(dir, _schemas).Load();
            StructureData d = r.Ok ? GameRecords.Read<StructureData>(r.Table.Get(StarterWorld.BaseId)) : null;
            if (!r.Ok || !SameBlocks(d, baseBefore) || r.Found.Items != reloaded.Found.Items
                || d.Mode != (i >= trace.IndexOf(Committed) ? CoreModes.Base : CoreModes.CapitalShip))
            {
                wrong++;
                _log.Fail($"mode switch crashed at point {i} ({trace[i]}): {(r.Ok ? "wrong mode or blocks" : r.Summary())}");
            }
        }
        _log.Check(wrong == 0, $"a switch back to base mode crashed at each of its {trace.Count} steps: always one mode or the other, never a block or item short");
    }

    static bool SameBlocks(StructureData a, StructureData b) =>
        a != null && a.Blocks.Count == b.Blocks.Count && a.Blocks.Zip(b.Blocks).All(p => p.First.SequenceEqual(p.Second));

    // ---- Exhaustive crash points -------------------------------------------------------

    /// <summary>One commit that changes several records, adds one, removes one and crosses a zone.</summary>
    static void CrashTestEdits(RecordTable t)
    {
        WorldOps.MoveItems(t, StarterWorld.CrateA, StarterWorld.CrateB, ItemKinds.Ore, 7);
        WorldOps.MoveBlock(t, StarterWorld.BaseId, 0, StarterWorld.ShelfId, new[] { 1, 0, 0 });
        WorldOps.PickUp(t, StarterWorld.DropOre, StarterWorld.PlayerId);
        WorldOps.Drop(t, StarterWorld.PlayerId, ItemKinds.Plate, 3, "drop-new", ZoneIds.Surface, null, new[] { 1.0, 0, 15 });
        WorldOps.Place(t, StarterWorld.ShipId, ZoneIds.Orbit, null, new[] { 0.0, 640, 0 }, rotation: new double[] { 0, 0, 0, 1 });
        WorldOps.Place(t, StarterWorld.CrateB, ZoneIds.Cabin(StarterWorld.ShipId), StarterWorld.ShipId, new[] { 2.0, 0, 6 });
    }

    void CrashPoints()
    {
        _log.Section("Exhaustive crash points");
        string pristine = Dir("crash/pristine");
        var (_, start) = NewWorld(pristine);
        Dictionary<string, SaveRecord> before = start.Snapshot();

        // Run the commit under test once, all the way through, to list its crash points in order.
        var trace = new List<string>();
        string traced = Copy(pristine, "crash/traced");
        var tracedStore = new SaveStore(traced, _schemas);
        RecordTable table = tracedStore.Load().Table;
        CrashTestEdits(table);
        CommitPlan plan = table.PrepareCommit();
        tracedStore.Commit(plan, new FaultInjector { Trace = trace });
        table.CompleteCommit(plan);
        Dictionary<string, SaveRecord> after = table.Snapshot();
        int commitPoint = trace.IndexOf(Committed);
        _log.Info($"the commit under test writes {plan.Changed.Count} records and removes {plan.Removed.Count}: " +
                  $"{trace.Count} crash points, the commit point is #{commitPoint}");

        int rolledBack = 0, keptNew = 0, wrong = 0;
        for (int i = 0; i < trace.Count; i++)
        {
            string dir = Copy(pristine, $"crash/point-{i:D2}");
            var store = new SaveStore(dir, _schemas);
            RecordTable t = store.Load().Table;
            CrashTestEdits(t);
            bool crashed = false;
            try { store.Commit(t.PrepareCommit(), new FaultInjector { CrashAt = i }); }
            catch (SimulatedCrash) { crashed = true; }

            // The restart: a brand-new store reads whatever the crash left on disk.
            var restarted = new SaveStore(dir, _schemas);
            LoadResult recovered = restarted.Load();
            bool landed = i >= commitPoint;
            string problem =
                !crashed ? "it never crashed" :
                !recovered.Ok ? recovered.Summary() :
                !Same(recovered.Table.Snapshot(), landed ? after : before)
                    ? (Same(recovered.Table.Snapshot(), landed ? before : after) ? "came back in the wrong state" : "came back as a mix, or with something missing") :
                Leftovers(dir).Count > 0 ? "left stray files: " + string.Join(", ", Leftovers(dir)) : null;

            if (problem == null && !landed)
            {
                // Carry on from the recovered state: the same edits commit and load back exactly.
                CrashTestEdits(recovered.Table);
                Commit(restarted, recovered.Table);
                LoadResult again = new SaveStore(dir, _schemas).Load();
                if (!again.Ok || !Same(again.Table.Snapshot(), after)) problem = "could not carry on after recovering";
            }

            if (problem != null)
            {
                wrong++;
                _log.Fail($"crash at point {i} ({trace[i]}): {problem}");
            }
            else if (landed) keptNew++;
            else rolledBack++;
        }
        _log.Check(wrong == 0,
            $"crashed after each of the {trace.Count} write steps in turn: {rolledBack} came back exactly as before the save, " +
            $"{keptNew} exactly as after it; none mixed, nothing missing, no stray files left");

        // Recovery can be interrupted too: crash at each step of the sweep that tidies up after a crash.
        string halfDone = Copy(pristine, "crash/sweep-pristine");
        {
            var store = new SaveStore(halfDone, _schemas);
            RecordTable t = store.Load().Table;
            CrashTestEdits(t);
            try { store.Commit(t.PrepareCommit(), new FaultInjector { CrashAt = commitPoint - 1 }); }
            catch (SimulatedCrash) { }
        }
        int leftovers = Leftovers(halfDone).Count, sweepWrong = 0;
        for (int j = 0; j < leftovers; j++)
        {
            string dir = Copy(halfDone, $"crash/sweep-{j:D2}");
            bool crashed = false;
            try { new SaveStore(dir, _schemas).Load(new FaultInjector { CrashAt = j }); }
            catch (SimulatedCrash) { crashed = true; }
            LoadResult r = new SaveStore(dir, _schemas).Load();
            if (!crashed || !r.Ok || !Same(r.Table.Snapshot(), before) || Leftovers(dir).Count > 0)
            {
                sweepWrong++;
                _log.Fail($"recovery crashed at its step {j}, and the next start did not recover exactly");
            }
        }
        _log.Check(leftovers > 0 && sweepWrong == 0,
            $"the recovery itself crashed at each of its {leftovers} clean-up steps, and the next start still came back exactly as before the save");
    }

    // ---- Restore under crash -----------------------------------------------------------

    void RestoreUnderCrash()
    {
        _log.Section("Restore under crash");
        string pristine = Dir("restore/pristine");
        var (store, table) = NewWorld(pristine);
        string backupName = store.CreateBackup("manual").Name;
        Dictionary<string, SaveRecord> backup = table.Snapshot();
        CrashTestEdits(table);
        Commit(store, table);
        Dictionary<string, SaveRecord> live = table.Snapshot();

        var trace = new List<string>();
        string traced = Copy(pristine, "restore/traced");
        LoadResult restored = new SaveStore(traced, _schemas).Restore(backupName, new FaultInjector { Trace = trace });
        _log.Check(restored.Ok && Same(restored.Table.Snapshot(), backup),
            $"restoring {backupName} brings back exactly its {restored.Found?.Records} records");
        _log.Check(new SaveStore(traced, _schemas).ListBackups().Any(b => b.Kind == "prerestore"),
            "what was live is kept as a 'prerestore' backup before the restore replaces it");

        int commitPoint = trace.IndexOf(Committed), wrong = 0, old = 0, back = 0;
        for (int i = 0; i < trace.Count; i++)
        {
            string dir = Copy(pristine, $"restore/point-{i:D2}");
            bool crashed = false;
            try { new SaveStore(dir, _schemas).Restore(backupName, new FaultInjector { CrashAt = i }); }
            catch (SimulatedCrash) { crashed = true; }
            LoadResult r = new SaveStore(dir, _schemas).Load();
            bool landed = i >= commitPoint;
            if (!crashed || !r.Ok || !Same(r.Table.Snapshot(), landed ? backup : live))
            {
                wrong++;
                _log.Fail($"restore crashed at point {i} ({trace[i]}): {(r.Ok ? "wrong or mixed state" : r.Summary())}");
            }
            else if (landed) back++;
            else old++;
        }
        _log.Check(wrong == 0,
            $"a restore crashed at each of its {trace.Count} steps: {old} times the live save was untouched, {back} times the backup was fully in place; never a mix");
    }

    // ---- Real process kills -------------------------------------------------------------

    void ProcessKills()
    {
        _log.Section("Real process kills");
        if (WorkerDll == null || !File.Exists(WorkerDll))
        {
            _log.Fail($"the save worker isn't built ({WorkerDll}); build Saves.csproj, which builds it too");
            return;
        }

        const int Target = 200, Minimum = 50;
        var budget = TimeSpan.FromSeconds(240); // past this, stop at the target or the minimum, whichever comes first
        string dir = Dir("kills");
        var (_, table) = NewWorld(dir, extraContainers: 24);
        AuditCounts start = AuditCounts.Of(table.All);
        string dotnet = DotnetHost();
        var rng = new Random(2026);
        var watch = Stopwatch.StartNew();
        int kills = 0, midSave = 0, failures = 0;
        long firstGeneration = -1, lastGeneration = 1;
        double startupTotal = 0;

        while (kills < Target && (kills < Minimum || watch.Elapsed < budget))
        {
            var info = new ProcessStartInfo(dotnet)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add(WorkerDll);
            info.ArgumentList.Add(dir);
            info.ArgumentList.Add(kills.ToString());
            var startup = Stopwatch.StartNew();
            using Process process = Process.Start(info);
            var ready = process.StandardOutput.ReadLineAsync();
            if (!ready.Wait(TimeSpan.FromSeconds(30)) || ready.Result == null || !ready.Result.StartsWith("READY", StringComparison.Ordinal))
            {
                _log.Fail($"kill {kills}: the worker did not start ({(ready.IsCompleted ? ready.Result : "timed out")})");
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                failures++;
                break;
            }
            startupTotal += startup.Elapsed.TotalMilliseconds;

            Thread.Sleep(rng.Next(1, 160)); // let it save for a random while, then pull the plug
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            kills++;

            string backups = Path.Combine(dir, "backups");
            if (Leftovers(dir).Count > 0 || (Directory.Exists(backups) && Directory.GetFiles(backups, "*.tmp").Length > 0)) midSave++;

            LoadResult load = new SaveStore(dir, _schemas).Load();
            List<string> broken = load.Ok ? Invariants.Check(load.Table.All, start.Items, start.Blocks) : load.Problems;
            if (!load.Ok || broken.Count > 0 || load.Generation < lastGeneration)
            {
                _log.Fail($"after kill {kills}: {(load.Ok ? string.Join("; ", broken) : load.Summary())} (generation {load.Generation})");
                failures++;
                break;
            }
            if (firstGeneration < 0) firstGeneration = load.Generation;
            lastGeneration = load.Generation;
        }

        var final = new SaveStore(dir, _schemas);
        final.Load();
        List<BackupInfo> list = final.ListBackups();
        LoadResult newest = list.Count > 0 ? final.ReadBackup(list[0].Name) : null;
        _log.Check(newest != null && newest.Ok && Invariants.Check(newest.Table.All, start.Items, start.Blocks).Count == 0,
            $"the newest of the {list.Count} backups the worker took between kills is whole and adds up");
        _log.Check(failures == 0 && kills >= Minimum,
            $"killed a saving process {kills} times ({midSave} of them mid-save or mid-backup, judging by what was left behind): " +
            $"every reload verified every checksum and passed the audit with all {start.Items} items and {start.Blocks} blocks; " +
            $"generation {firstGeneration} -> {lastGeneration}, {watch.Elapsed.TotalSeconds:F0} s (worker start-up {startupTotal / Math.Max(1, kills):F0} ms on average)");
    }

    static string DotnetHost()
    {
        string root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        string exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        return root != null && File.Exists(Path.Combine(root, exe)) ? Path.Combine(root, exe) : "dotnet";
    }

    // ---- Soak --------------------------------------------------------------------------

    void Soak()
    {
        _log.Section("Soak");
        const int Cycles = 5000;
        string dir = Dir("soak");
        var (store, table) = NewWorld(dir, extraContainers: 6);
        AuditCounts start = AuditCounts.Of(table.All);
        Dictionary<string, SaveRecord> committed = table.Snapshot();
        var rng = new Random(77);
        int nextId = 0, saves = 0, loads = 0, crashes = 0, rolledBack = 0, wrong = 0;
        var kinds = new SortedDictionary<RandomEdits.Kind, int>();
        var watch = Stopwatch.StartNew();

        for (int cycle = 0; cycle < Cycles && wrong == 0; cycle++)
        {
            int edits = 1 + rng.Next(3);
            for (int e = 0; e < edits; e++)
            {
                RandomEdits.Kind kind = RandomEdits.Apply(table, rng, ref nextId);
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
            }
            List<string> broken = Invariants.Check(table.All, start.Items, start.Blocks);
            if (broken.Count > 0)
            {
                wrong++;
                _log.Fail($"cycle {cycle}: {string.Join("; ", broken)}");
                break;
            }

            double roll = rng.NextDouble();
            if (roll < 0.30)
            {
                Commit(store, table);
                committed = table.Snapshot();
                saves++;
            }
            else if (roll < 0.36)
            {
                // A save that crashes at a random step, then a restart.
                var trace = new List<string>();
                Dictionary<string, SaveRecord> attempted = table.Snapshot();
                bool crashed = false;
                try
                {
                    CommitPlan plan = table.PrepareCommit();
                    store.Commit(plan, new FaultInjector { CrashAt = rng.Next(0, 3 * plan.Changed.Count + 2 * plan.Removed.Count + 6), Trace = trace });
                }
                catch (SimulatedCrash) { crashed = true; }
                bool landed = trace.Contains(Committed);
                store = new SaveStore(dir, _schemas);
                LoadResult r = store.Load();
                Dictionary<string, SaveRecord> expected = landed ? attempted : committed;
                if (!r.Ok || !Same(r.Table.Snapshot(), expected))
                {
                    wrong++;
                    _log.Fail($"cycle {cycle}: after a crash {(landed ? "after" : "before")} the commit point: {(r.Ok ? "wrong state" : r.Summary())}");
                    break;
                }
                table = r.Table;
                committed = expected;
                if (crashed) crashes++;
                if (!landed) rolledBack++;
            }

            if (rng.NextDouble() < 0.06)
            {
                // A reload: unsaved edits go, and the last commit comes back exactly.
                store = new SaveStore(dir, _schemas);
                LoadResult r = store.Load();
                if (!r.Ok || !Same(r.Table.Snapshot(), committed))
                {
                    wrong++;
                    _log.Fail($"cycle {cycle}: a reload did not match the last commit: {(r.Ok ? "wrong state" : r.Summary())}");
                    break;
                }
                table = r.Table;
                loads++;
            }
        }

        Commit(store, table);
        LoadResult last = new SaveStore(dir, _schemas).Load();
        bool finalOk = last.Ok && Same(last.Table.Snapshot(), table.Snapshot())
            && Invariants.Check(last.Table.All, start.Items, start.Blocks).Count == 0;
        _log.Check(wrong == 0 && finalOk,
            $"{Cycles} random edit, save and load cycles ({saves} saves, {loads} reloads, {crashes} crashes mid-save of which {rolledBack} rolled back): " +
            $"items stayed {start.Items} and blocks {start.Blocks} throughout, and every reload matched the last commit exactly ({watch.Elapsed.TotalSeconds:F1} s, ended at generation {last.Generation})");
        _log.Info("edits: " + string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}")));
    }

    // ---- Corruption --------------------------------------------------------------------

    void Corruption()
    {
        _log.Section("Corruption");
        string pristine = Dir("corrupt/pristine");
        var (store, table) = NewWorld(pristine);
        store.CreateBackup("manual");
        Dictionary<string, SaveRecord> backedUp = table.Snapshot();
        WorldOps.MoveItems(table, StarterWorld.CrateA, StarterWorld.CrateB, ItemKinds.Ore, 5);
        Commit(store, table);

        void Case(string name, Action<string> damage, string expect)
        {
            string dir = Copy(pristine, "corrupt/" + name.Replace(' ', '-'));
            damage(dir);
            string before = Fingerprint(dir);
            LoadResult r = new SaveStore(dir, _schemas).Load();
            bool refused = !r.Ok && r.Problems.Any(p => p.Contains(expect, StringComparison.Ordinal));
            _log.Check(refused && r.Backups.Count > 0 && Fingerprint(dir) == before,
                $"{name}: the load refuses (\"{r.Problems.FirstOrDefault() ?? "no reason given"}\"), lists {r.Backups.Count} backup(s), and changes nothing on disk");
        }

        Case("deleted record file", d => File.Delete(RecordPath(d, StarterWorld.CrateA)), "is missing");
        Case("flipped byte", d =>
        {
            string f = RecordPath(d, StarterWorld.CrateB);
            byte[] b = File.ReadAllBytes(f);
            b[b.Length / 2] ^= 0x20;
            File.WriteAllBytes(f, b);
        }, "fails its checksum");
        Case("record cut short", d =>
        {
            string f = RecordPath(d, StarterWorld.BaseId);
            File.WriteAllBytes(f, File.ReadAllBytes(f)[..100]);
        }, "cut short");
        Case("manifest cut short", d =>
        {
            string f = Path.Combine(d, SaveStore.ManifestFileName);
            File.WriteAllBytes(f, File.ReadAllBytes(f)[..300]);
        }, "manifest.json can't be used");
        Case("deleted manifest", d => File.Delete(Path.Combine(d, SaveStore.ManifestFileName)), "manifest.json is missing");
        Case("manifest lost an entry", d =>
        {
            // As a buggy writer would: a valid manifest whose list no longer matches its own audit.
            string f = Path.Combine(d, SaveStore.ManifestFileName);
            Manifest m = Manifest.Decode(File.ReadAllBytes(f));
            m.Records.RemoveAll(e => e.Id == StarterWorld.CrateA);
            File.WriteAllBytes(f, m.Encode());
        }, "the manifest says");
        Case("record from a newer build", d =>
        {
            string f = Path.Combine(d, SaveStore.ManifestFileName);
            Manifest m = Manifest.Decode(File.ReadAllBytes(f));
            int index = m.Records.FindIndex(e => e.Id == StarterWorld.CrateB);
            SaveRecord newer = RecordFile.Decode(File.ReadAllBytes(Path.Combine(d, "records", m.Records[index].File))) with { Version = 99 };
            byte[] bytes = RecordFile.Encode(newer);
            string file = RecordFile.FileName(newer.Id, m.Generation + 1);
            File.WriteAllBytes(Path.Combine(d, "records", file), bytes);
            m.Records[index] = m.Records[index] with { File = file, Bytes = bytes.Length, Sha256 = Framing.Hash(bytes) };
            File.WriteAllBytes(f, m.Encode());
        }, "newer build");

        // Restore a backup over a damaged save.
        string damagedDir = Copy(pristine, "corrupt/restore");
        File.Delete(RecordPath(damagedDir, StarterWorld.CrateA));
        var damaged = new SaveStore(damagedDir, _schemas);
        LoadResult refusedLoad = damaged.Load();
        string pick = refusedLoad.Backups.FirstOrDefault()?.Name;
        LoadResult restored = pick == null ? refusedLoad : damaged.Restore(pick);
        _log.Check(!refusedLoad.Ok && restored.Ok && Same(restored.Table.Snapshot(), backedUp),
            $"restoring {pick} over the damaged save loads cleanly, with exactly the backup's {restored.Found?.Items} items");
        string quarantine = Path.Combine(damagedDir, "quarantine");
        int kept = Directory.Exists(quarantine) ? Directory.GetFiles(quarantine, "*", SearchOption.AllDirectories).Length : 0;
        _log.Check(kept > 0, $"the damaged save's {kept} files were copied into quarantine/ first, not thrown away");
        LoadResult reopened = new SaveStore(damagedDir, _schemas).Load();
        _log.Check(reopened.Ok && Same(reopened.Table.Snapshot(), backedUp), "and it keeps loading cleanly afterwards");
    }

    // ---- No silent cleanup ---------------------------------------------------------------

    void NoSilentCleanup()
    {
        _log.Section("No silent cleanup");
        string dir = Dir("cleanup");
        var (store, table) = NewWorld(dir);
        int records = table.Count;

        string owned = table.Cleanup(StarterWorld.BaseId);
        _log.Check(owned != null && table.Contains(StarterWorld.BaseId), $"cleaning up the player's base is refused: \"{owned}\"");

        var fresh = new RecordTable();
        StarterWorld.Create(fresh);
        _log.Check(fresh.Cleanup(StarterWorld.Wreck) == null && !fresh.Contains(StarterWorld.Wreck),
            "an untouched, world-generated wreck is the only kind of thing a cleanup may remove");

        WorldOps.MoveItems(table, StarterWorld.Wreck, StarterWorld.PlayerId, ItemKinds.Ice, 1);
        string touched = table.Cleanup(StarterWorld.Wreck);
        _log.Check(touched != null && table.Contains(StarterWorld.Wreck), $"once a player has touched it, it is refused too: \"{touched}\"");

        List<SaveRecord> protectedOnes = table.All.Where(r => r.IsProtected).ToList();
        int refused = protectedOnes.Count(r => table.Cleanup(r.Id) != null);
        _log.Check(refused == protectedOnes.Count && table.Count == records,
            $"all {refused} owned or touched records refuse cleanup, and all {records} records are still there");

        bool threw = false;
        try { table.Remove(StarterWorld.CrateA); }
        catch (InvalidOperationException) { threw = true; }
        _log.Check(threw && table.Contains(StarterWorld.CrateA), "removing a crate that still holds items is refused");

        Commit(store, table);
        long generation = store.Generation;
        SaveRecord crate = table.Get(StarterWorld.CrateA);
        var data = GameRecords.Read<ContainerData>(crate);
        data.Stacks[0].Count -= 1;
        table.Put(GameRecords.With(crate, data)); // a bug: one ore vanishes without being declared
        string refusal = null;
        try { table.PrepareCommit(); }
        catch (SaveRefusedException e) { refusal = e.Message; }
        _log.Check(refusal != null && store.Generation == generation,
            $"a bug that loses one item can't be saved over the last good save: \"{refusal}\"");

        table.Put(crate); // the ore is back
        bool built = WorldOps.PlaceBlock(table, StarterWorld.BaseId, new[] { 2, 2, 2 }, StarterWorld.Orange, StarterWorld.PlayerId);
        Commit(store, table);
        LoadResult reloaded = new SaveStore(dir, _schemas).Load();
        _log.Check(built && reloaded.Ok && store.Generation == generation + 1 && Same(reloaded.Table.Snapshot(), table.Snapshot()),
            "building a block from a plate is declared, so it saves and loads back fine");
    }

    // ---- Backup retention on a simulated clock --------------------------------------------

    void Retention()
    {
        _log.Section("Backup retention");
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new SimulatedClock(origin);
        string dir = Dir("retention");
        var (store, table) = NewWorld(dir, clock: clock);
        var scheduler = new BackupScheduler(store);
        int made = 0, ticks = 0;
        var watch = Stopwatch.StartNew();

        // 40 days of evening sessions, 18:00 to 23:00 UTC, with the game ticking the scheduler every minute.
        const int Days = 40;
        for (int day = 0; day < Days; day++)
        {
            clock.UtcNow = origin.AddDays(day).AddHours(18);
            for (int minute = 0; minute <= 300; minute++)
            {
                if (minute % 20 == 0)
                {
                    WorldOps.MoveItems(table, StarterWorld.CrateA, StarterWorld.CrateB, ItemKinds.Ore, 1);
                    WorldOps.MoveItems(table, StarterWorld.CrateB, StarterWorld.CrateA, ItemKinds.Ore, 1);
                    Commit(store, table);
                }
                if (scheduler.Tick() != null) made++;
                ticks++;
                clock.Advance(TimeSpan.FromMinutes(1));
            }
        }
        DateTime now = clock.UtcNow;

        // Expected: every snapshot of the last day (18:00 to 23:00 on day 39, 21 of them), plus the
        // newest of each earlier day still under 30 days old (23:00 on days 10 to 38, 29 of them).
        var expected = new SortedSet<DateTime>();
        for (int m = 0; m <= 300; m += 15) expected.Add(origin.AddDays(Days - 1).AddHours(18).AddMinutes(m));
        for (int day = Days - 30 + 0; day < Days - 1; day++)
            if (now - origin.AddDays(day).AddHours(23) < TimeSpan.FromDays(30)) expected.Add(origin.AddDays(day).AddHours(23));
        var actual = new SortedSet<DateTime>(store.ListBackups().Select(b => b.TakenAtUtc));
        _log.Check(actual.SetEquals(expected),
            $"after {Days} simulated days ({ticks} scheduler ticks, {made} snapshots taken every 15 minutes of play), exactly the right {actual.Count} are kept: " +
            $"all {expected.Count(t => now - t < TimeSpan.FromDays(1))} from the last day and one per day for the {expected.Count(t => now - t >= TimeSpan.FromDays(1))} days before, none older than 30 days");
        if (!actual.SetEquals(expected))
            _log.Info($"expected {string.Join(", ", expected.Except(actual))} / unexpected {string.Join(", ", actual.Except(expected))}");

        int readable = store.ListBackups().Count(b => store.ReadBackup(b.Name).Ok);
        _log.Check(readable == actual.Count, $"every kept backup reads back whole ({readable}/{actual.Count})");

        // A long break: 90 days later, the newest few survive, so the save is never left with no backups.
        clock.Advance(TimeSpan.FromDays(90));
        scheduler.Tick();
        List<BackupInfo> afterBreak = store.ListBackups();
        _log.Check(afterBreak.Count == 3 && afterBreak[0].TakenAtUtc == clock.UtcNow && afterBreak[1].TakenAtUtc == expected.Max,
            $"after a 90-day break, a fresh snapshot is taken and the newest 3 are kept however old ({string.Join(", ", afterBreak.Select(b => b.TakenAtUtc.ToString("MM-dd HH:mm")))})");
        _log.Info($"retention simulation took {watch.Elapsed.TotalSeconds:F1} s");
    }

    // ---- Migration -----------------------------------------------------------------------

    void Migration()
    {
        _log.Section("Migration");
        if (FixtureV1 == null || !File.Exists(Path.Combine(FixtureV1, SaveStore.ManifestFileName)))
        {
            _log.Fail($"the v1 fixture is missing ({FixtureV1})");
            return;
        }
        string dir = Dir("migration");
        CopyInto(FixtureV1, dir);

        // What the v1 files hold, read raw: the item names in every v1 container.
        Manifest manifest = Manifest.Decode(File.ReadAllBytes(Path.Combine(dir, SaveStore.ManifestFileName)));
        var v1Items = new Dictionary<string, List<string>>();
        foreach (ManifestEntry e in manifest.Records)
        {
            SaveRecord raw = RecordFile.Decode(File.ReadAllBytes(Path.Combine(dir, "records", e.File)));
            if (raw.Type == RecordTypes.Container && raw.Version == 1)
                v1Items[raw.Id] = JsonDocument.Parse(raw.Payload).RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetString()).ToList();
        }

        var store = new SaveStore(dir, _schemas);
        LoadResult r = store.Load();
        _log.Check(r.Ok && v1Items.Count > 0 && r.Migrated == v1Items.Count,
            $"a v1 save (checked in at tests/fixtures/save-v1) loads, migrating {r.Migrated} of its {r.Checked} records from container v1 to v2");
        if (!r.Ok)
        {
            _log.Info(r.Summary());
            return;
        }

        int exact = 0;
        foreach (var (id, names) in v1Items)
        {
            SaveRecord now = r.Table.Get(id);
            var stacks = GameRecords.Read<ContainerData>(now).Stacks;
            bool same = now.Version == 2
                && names.GroupBy(n => n).All(g => Stacks.CountOf(stacks, g.Key) == g.Count())
                && Stacks.Total(stacks) == names.Count;
            if (same) exact++;
            else _log.Info($"{id}: v1 {string.Join(",", names)} became {Stacks.Describe(stacks)}");
        }
        _log.Check(exact == v1Items.Count && r.Found.Items == r.Expected.Items,
            $"every item in every v1 container is in its v2 stacks, nothing more or less ({r.Found.Items} items, as the v1 manifest promised)");

        Commit(store, r.Table);
        LoadResult again = new SaveStore(dir, _schemas).Load();
        _log.Check(again.Ok && again.Migrated == 0 && Same(again.Table.Snapshot(), r.Table.Snapshot()),
            "saved again it is v2 on disk, and loads back identical with nothing left to migrate");
    }

    /// <summary>
    /// Writes tests/fixtures/save-v1 as the previous build would have: containers kept one name per
    /// item. Run once with --write-v1-fixture; the files are then checked in and never regenerated.
    /// </summary>
    public static void WriteV1Fixture(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var clock = new SimulatedClock(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
        var store = new SaveStore(dir, new RecordSchemas(), clock); // knows no types: writes exactly what it is given
        store.Create();
        var t = new RecordTable();
        var modern = new RecordTable();
        StarterWorld.Create(modern);
        foreach (SaveRecord r in modern.All.Where(r => r.Type != RecordTypes.Container)) t.Put(r);

        void V1Crate(string id, string zone, string ship, string owner, double x, double y, double z, params string[] items) =>
            t.Put(new SaveRecord
            {
                Id = id,
                Type = RecordTypes.Container,
                ZoneId = zone,
                ShipId = ship,
                Owner = owner,
                Touched = owner != null,
                Version = 1,
                Items = items.Length,
                Payload = JsonSerializer.Serialize(new { x, y, z, items }),
            });
        V1Crate(StarterWorld.CrateA, ZoneIds.Surface, null, StarterWorld.Owner, 0, 0, 26.5, "ore", "ore", "ice", "ore", "ice", "ore", "fuel");
        V1Crate(StarterWorld.CrateB, ZoneIds.Surface, null, StarterWorld.Owner, 4.5, 0, 18, "plate", "plate", "plate", "fuel");
        V1Crate(StarterWorld.CrateC, ZoneIds.Cabin(StarterWorld.ShipId), StarterWorld.ShipId, StarterWorld.Owner, -2.2, 0, 7.5, "fuel", "fuel", "ice");
        V1Crate("crate-empty", ZoneIds.Surface, null, StarterWorld.Owner, 3, 0, 30);
        V1Crate(StarterWorld.Wreck, ZoneIds.Orbit, null, null, 26, 612, -48, "ice", "ice", "ice");

        AuditCounts created = AuditCounts.Of(t.All);
        t.Declare(created.Items, created.Blocks); // a new world: everything in it was created just now
        CommitPlan plan = t.PrepareCommit();
        store.Commit(plan);
        t.CompleteCommit(plan);
    }

    // ---- Performance -------------------------------------------------------------------

    void Performance()
    {
        _log.Section("Performance");
        const int Records = 20000, Dirty = 100;
        string dir = Dir("perf");
        var store = new SaveStore(dir, _schemas);
        store.Create();
        var table = new RecordTable();
        StarterWorld.Create(table);
        int starter = table.Count;
        table = new RecordTable();
        StarterWorld.Create(table, extraContainers: Records - starter);

        var watch = Stopwatch.StartNew();
        Commit(store, table);
        _log.Info($"first full save of {table.Count} records (every record a new, flushed file): {watch.Elapsed.TotalSeconds:F1} s");

        List<string> crates = table.All.Where(r => r.Id.StartsWith("crate-x", StringComparison.Ordinal)).Select(r => r.Id).ToList();
        var rng = new Random(5);
        var times = new List<double>();
        for (int round = 0; round < 5; round++)
        {
            List<string> picked = crates.OrderBy(_ => rng.Next()).Take(Dirty).ToList();
            for (int i = 0; i < Dirty; i += 2)
            {
                SaveRecord from = table.Get(picked[i]);
                if (from.Items == 0) (picked[i], picked[i + 1]) = (picked[i + 1], picked[i]);
                ItemStack stack = WorldOps.StacksOf(table.Get(picked[i])).First();
                WorldOps.MoveItems(table, picked[i], picked[i + 1], stack.Item, 1);
            }
            int dirty = table.PendingChanges;
            watch.Restart();
            CommitResult result = Commit(store, table);
            times.Add(watch.Elapsed.TotalMilliseconds);
            _log.Info($"incremental save {round + 1}: {dirty} of {table.Count} records dirty, {result}");
        }
        double median = times.OrderBy(t => t).ElementAt(times.Count / 2);
        long manifestBytes = new FileInfo(Path.Combine(dir, SaveStore.ManifestFileName)).Length;

        watch.Restart();
        LoadResult load = new SaveStore(dir, _schemas).Load();
        double loadMs = watch.Elapsed.TotalMilliseconds;

        _log.Check(median < 2000,
            $"incremental save of {Dirty} dirty records out of {Records}: median {median:F0} ms over {times.Count} saves (each also rewrites the {manifestBytes / 1024} KB manifest)");
        _log.Check(load.Ok && load.Checked == Records && loadMs < 20000,
            $"full load of {load.Checked} records, every checksum verified and the audit passed: {loadMs:F0} ms");
    }

    // ---- Helpers -----------------------------------------------------------------------

    string Dir(string name)
    {
        string path = Path.Combine(_root, name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    string Copy(string from, string name)
    {
        string to = Dir(name);
        CopyInto(from, to);
        return to;
    }

    static void CopyInto(string from, string to)
    {
        foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }

    (SaveStore Store, RecordTable Table) NewWorld(string dir, int extraContainers = 0, IClock clock = null)
    {
        var store = new SaveStore(dir, _schemas, clock);
        store.Create();
        var table = new RecordTable();
        StarterWorld.Create(table, extraContainers);
        Commit(store, table);
        return (store, table);
    }

    static CommitResult Commit(SaveStore store, RecordTable table, FaultInjector faults = null)
    {
        CommitPlan plan = table.PrepareCommit();
        CommitResult result = store.Commit(plan, faults);
        table.CompleteCommit(plan);
        return result;
    }

    static bool Same(Dictionary<string, SaveRecord> a, Dictionary<string, SaveRecord> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out SaveRecord other) && other == kv.Value);

    static string RecordPath(string dir, string id)
    {
        Manifest m = Manifest.Decode(File.ReadAllBytes(Path.Combine(dir, SaveStore.ManifestFileName)));
        return Path.Combine(dir, "records", m.Records.First(e => e.Id == id).File);
    }

    /// <summary>What a crash leaves behind: a temp manifest, and record files the manifest doesn't list.</summary>
    static List<string> Leftovers(string dir)
    {
        var list = new List<string>();
        string manifest = Path.Combine(dir, SaveStore.ManifestFileName);
        if (File.Exists(manifest + ".tmp")) list.Add("manifest.json.tmp");
        var referenced = File.Exists(manifest)
            ? Manifest.Decode(File.ReadAllBytes(manifest)).Records.Select(e => e.File).ToHashSet()
            : new HashSet<string>();
        string records = Path.Combine(dir, "records");
        if (Directory.Exists(records))
            list.AddRange(Directory.GetFiles(records).Select(Path.GetFileName).Where(f => !referenced.Contains(f)));
        return list;
    }

    /// <summary>Every file in a folder and its hash, to prove a refused load changed nothing.</summary>
    static string Fingerprint(string dir) =>
        string.Join("\n", Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Path.GetRelativePath(dir, f) + " " + Framing.Hash(File.ReadAllBytes(f))));
}
