using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalHack.Items;
using GalHack.Persistence;
using Godot;
using Slice.Orbit;

namespace Slice.Tests;

/// <summary>
/// The zone ledger on its own, no engine in play: a crossing crashed at every one of its commit's
/// points (saves' fault injector), and the save reloaded each time. Whatever the point, the
/// crosser is in exactly one zone on disk: the old one before the commit point, the new one from
/// it on, with the totals intact and whoever rides it still in its cabin. Then the ledger's
/// refusals: a commit that fails leaves the record where it was, and the move audit catches
/// anything lost, doubled or in the wrong zone.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/ZoneTest.tscn
/// </summary>
public partial class ZoneTest : Node
{
    int _failures, _passes;
    string _root;

    public override void _Ready()
    {
        var clock = Stopwatch.StartNew();
        _root = Path.Combine(OS.GetUserDataDir(), "zone-test");
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        try
        {
            CrashAtEveryPoint();
            OffThreadCommit();
            Audit();
        }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    static List<Holder> World(string shipZone) => new()
    {
        new Holder(ZoneLedger.Sleeper, "sleeper", ZoneLedger.Cabin("skiff"), "skiff", "Sleeper", Bill.Of(("air-cell", 2), ("hull-plate", 7)), 0),
        new Holder("skiff", "structure", shipZone, null, "Skiff", Bill.Of(("iron-bar", 3)), 174),
        new Holder("bunker", "structure", ZoneLedger.Surface, null, "Cryo Bunker", Bill.Of(("trail-ration", 4)), 400),
        new Holder("wreck-one", "structure", ZoneLedger.Orbit, null, "Derelict", Bill.Of(("circuit-board", 5)), 377),
    };

    string Fresh(string name)
    {
        string folder = Path.Combine(_root, name);
        var ledger = new ZoneLedger(folder);
        Check(ledger.Snapshot(World(ZoneLedger.Surface)), $"a fresh ledger commits ({name})");
        return folder;
    }

    /// <summary>The trace of one crossing's commit: every point a crash could land on.</summary>
    List<string> TraceOf()
    {
        string folder = Fresh("trace");
        var ledger = new ZoneLedger(folder, fresh: false);
        var faults = new FaultInjector { Trace = new List<string>() };
        ledger.Cross("skiff", ZoneLedger.Orbit, faults);
        return faults.Trace.ToList();
    }

    void CrashAtEveryPoint()
    {
        List<string> trace = TraceOf();
        int commitAt = trace.FindIndex(t => t.Contains("committed"));
        Check(trace.Count > 4 && commitAt >= 0, $"a crossing's commit has {trace.Count} points a crash can land on, its commit point the rename: {trace[commitAt]}");
        int rightZone = 0, oneZone = 0, totals = 0, riders = 0;
        for (int i = 0; i < trace.Count; i++)
        {
            string folder = Fresh($"crash{i}");
            var ledger = new ZoneLedger(folder, fresh: false);
            var faults = new FaultInjector { CrashAt = i };
            bool crashed = false;
            try { ledger.Cross("skiff", ZoneLedger.Orbit, faults); }
            catch (SimulatedCrash) { crashed = true; }
            Check(crashed, $"the crossing crashed at '{trace[i]}'");
            LoadResult load = new SaveStore(folder, ZoneLedger.Schemas()).Load();
            if (!load.Ok)
            {
                Check(false, $"the save loads after a crash at '{trace[i]}': {string.Join("; ", load.Problems)}");
                continue;
            }
            SaveRecord ship = load.Table.Get("skiff");
            if (ship.ZoneId is ZoneLedger.Surface or ZoneLedger.Orbit) oneZone++;
            if (ship.ZoneId == (i >= commitAt ? ZoneLedger.Orbit : ZoneLedger.Surface)) rightZone++;
            var (items, blocks) = ZoneLedger.Totals(World(ZoneLedger.Surface));
            AuditCounts counts = AuditCounts.Of(load.Table.All);
            if (counts.Items == items && counts.Blocks == blocks) totals++;
            if (load.Table.Get(ZoneLedger.Sleeper).ZoneId == ZoneLedger.Cabin("skiff")) riders++;
        }
        int n = trace.Count;
        Check(oneZone == n, $"crashed at each of {n} points, the crosser is in exactly one zone on disk ({oneZone} of {n})");
        Check(rightZone == n, $"the old zone before the commit point, the new one from it on ({rightZone} of {n})");
        Check(totals == n, $"nothing lost or doubled at any of them ({totals} of {n})");
        Check(riders == n, $"its rider stays in its cabin, unwritten ({riders} of {n})");
    }

    /// <summary>The crossing as the game runs it: the commit on a worker thread, nothing moved until it lands.</summary>
    void OffThreadCommit()
    {
        var ledger = new ZoneLedger(Path.Combine(_root, "worker"));
        ledger.Snapshot(World(ZoneLedger.Surface));
        Check(ledger.Begin(World(ZoneLedger.Surface), "skiff", ZoneLedger.Orbit), "a crossing's commit starts on a worker thread");
        while (ledger.Committing) System.Threading.Thread.Sleep(1);
        Check(ledger.Finish() && ledger.ZoneOf("skiff") == ZoneLedger.Orbit, $"and lands: the Skiff is in orbit, {ledger.LastCommit.Written} record written");
        LoadResult load = new SaveStore(Path.Combine(_root, "worker"), ZoneLedger.Schemas()).Load();
        Check(load.Ok && load.Table.Get("skiff").ZoneId == ZoneLedger.Orbit, "the disk agrees");
    }

    void Audit()
    {
        var ledger = new ZoneLedger(Path.Combine(_root, "audit"));
        List<Holder> world = World(ZoneLedger.Surface);
        ledger.Snapshot(world);
        var totals = ZoneLedger.Totals(world);
        Check(ledger.Audit(world, totals).Count == 0, "the audit passes a clean move");
        List<Holder> lost = World(ZoneLedger.Surface);
        lost[1] = lost[1] with { Items = Bill.Of(("iron-bar", 2)) };
        Check(ledger.Audit(lost, totals).Any(p => p.Contains("items")), "it catches an item lost");
        List<Holder> doubled = World(ZoneLedger.Surface);
        doubled[3] = doubled[3] with { Blocks = 378 };
        Check(ledger.Audit(doubled, totals).Any(p => p.Contains("blocks")), "and a block from nowhere");
        List<Holder> wrong = World(ZoneLedger.Orbit);
        Check(ledger.Audit(wrong, totals).Any(p => p.Contains("record says")), "and something in a zone its record doesn't say");
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        if (!ok || !what.StartsWith("a fresh") && !what.StartsWith("the crossing crashed")) GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }
}
