using System;
using System.Diagnostics;
using GalHack.Persistence;
using GalHack.Records;
using GalHack.Tests;

namespace SaveWorker;

/// <summary>
/// Saves in a loop until killed. tests/SaveTest.tscn starts it, waits for "READY", kills it at
/// a random moment (often in the middle of a commit or a backup), then loads what it left and
/// checks nothing was lost.
///   dotnet SaveWorker.dll <save folder> <seed> [parent process id]
/// It never outlives its purpose: it stops by itself after <see cref="MaxLifetime"/>, or as soon
/// as the parent test process is gone, so a crashed or stopped test can't leave it running.
/// Exit codes: 2 = the save failed to load, 3 = stopped by itself (lifetime over or parent gone).
/// </summary>
public static class Program
{
    static readonly TimeSpan MaxLifetime = TimeSpan.FromSeconds(60);

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: SaveWorker <save folder> <seed> [parent process id]");
            return 64;
        }
        Process parent = null;
        if (args.Length > 2)
        {
            try { parent = Process.GetProcessById(int.Parse(args[2])); }
            catch (ArgumentException) { return 3; } // the parent is already gone
        }
        var alive = Stopwatch.StartNew();

        var store = new SaveStore(args[0], GameRecords.Schemas());
        LoadResult load = store.Load();
        if (!load.Ok)
        {
            Console.WriteLine("LOADFAIL " + string.Join(" | ", load.Problems));
            return 2;
        }

        RecordTable table = load.Table;
        var rng = new Random(int.Parse(args[1]));
        int nextId = rng.Next(1_000_000) * 100; // drop ids unique across runs
        Console.WriteLine($"READY {load.Generation}");
        Console.Out.Flush();

        for (long n = 0; ; n++)
        {
            if (alive.Elapsed > MaxLifetime || (parent != null && parent.HasExited))
            {
                Console.WriteLine("STOPPED by itself: " + (parent != null && parent.HasExited ? "the test that started it is gone" : "its lifetime is over"));
                return 3;
            }
            int edits = 1 + rng.Next(6);
            for (int i = 0; i < edits; i++) RandomEdits.Apply(table, rng, ref nextId);
            CommitPlan plan = table.PrepareCommit();
            store.Commit(plan);
            table.CompleteCommit(plan);
            if (n % 6 == 5) store.CreateBackup("auto"); // so kills land in backups too
        }
    }
}
