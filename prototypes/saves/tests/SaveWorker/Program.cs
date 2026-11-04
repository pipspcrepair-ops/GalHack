using System;
using GalHack.Persistence;
using GalHack.Records;
using GalHack.Tests;

namespace SaveWorker;

/// <summary>
/// Saves in a loop until killed. tests/SaveTest.tscn starts it, waits for "READY", kills it at
/// a random moment (often in the middle of a commit or a backup), then loads what it left and
/// checks nothing was lost.
///   dotnet SaveWorker.dll <save folder> <seed>
/// Exit codes: 2 = the save failed to load (which the test treats as a failure).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: SaveWorker <save folder> <seed>");
            return 64;
        }
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
            int edits = 1 + rng.Next(6);
            for (int i = 0; i < edits; i++) RandomEdits.Apply(table, rng, ref nextId);
            CommitPlan plan = table.PrepareCommit();
            store.Commit(plan);
            table.CompleteCommit(plan);
            if (n % 6 == 5) store.CreateBackup("auto"); // so kills land in backups too
        }
    }
}
