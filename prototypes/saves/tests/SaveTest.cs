using System;
using System.Globalization;
using System.Linq;
using Godot;

namespace Saves.Tests;

/// <summary>
/// Headless tests of the save system and zones. Run with:
///   godot --headless --path prototypes/saves res://tests/SaveTest.tscn
/// Options, after --:
///   --only=crash,kills,soak,...   run some groups (see CoreTests.Run, plus "zones")
///   --watchdog-seconds=N          fail if the run takes longer than this (default 25 minutes)
///   --write-v1-fixture[=PATH]     a helper, not a test: write a v1-format save to PATH (default
///                                 tests/fixtures/save-v1, which it refuses to overwrite)
/// Every mode exits 0 on success and 1 on failure, including when something throws or hangs:
/// a watchdog thread ends the process with code 1 if it runs past its limit.
/// </summary>
public partial class SaveTest : Node
{
    static readonly TimeSpan SuiteLimit = TimeSpan.FromMinutes(25);
    static readonly TimeSpan FixtureLimit = TimeSpan.FromMinutes(2);
    static readonly TimeSpan QuitLimit = TimeSpan.FromSeconds(60);

    readonly TestLog _log = new();
    DateTime _started;
    Watchdog _watchdog;
    bool _finished;

    public override void _Ready()
    {
        _started = DateTime.UtcNow;
        // Armed before anything else can go wrong, so no mode of this scene can ever sit forever.
        _watchdog = new Watchdog(SuiteLimit, "SaveTest");
        try
        {
            Run();
        }
        catch (Exception e)
        {
            // An exception in _Ready is otherwise only logged, and the scene would run on forever.
            _log.Fail($"unexpected {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            Finish();
        }
    }

    void Run()
    {
        string only = null;
        string fixturePath = null;
        TimeSpan limit = SuiteLimit;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--only=")) only = arg["--only=".Length..];
            else if (arg.StartsWith("--watchdog-seconds=")) limit = TimeSpan.FromSeconds(double.Parse(arg["--watchdog-seconds=".Length..], CultureInfo.InvariantCulture));
            else if (arg == "--write-v1-fixture") fixturePath = ProjectSettings.GlobalizePath("res://tests/fixtures/save-v1");
            else if (arg.StartsWith("--write-v1-fixture=")) fixturePath = arg["--write-v1-fixture=".Length..];
        }

        if (fixturePath != null)
        {
            _watchdog.Rearm(limit < SuiteLimit ? limit : FixtureLimit, "--write-v1-fixture");
            CoreTests.WriteV1Fixture(fixturePath);
            _log.Check(true, $"wrote a v1-format save to {fixturePath}");
            Finish();
            return;
        }

        _watchdog.Rearm(limit, "the test suite");
        string[] groups = only?.Split(',');
        bool Wanted(string group) => groups == null || groups.Contains(group);

        var core = new CoreTests(_log, DataPaths.Fresh("test-run"))
        {
            WorkerDll = ProjectSettings.GlobalizePath("res://tests/SaveWorker/bin/Debug/net10.0/SaveWorker.dll"),
            FixtureV1 = ProjectSettings.GlobalizePath("res://tests/fixtures/save-v1"),
        };
        core.Run(Wanted);

        if (Wanted("zones"))
        {
            var zones = new ZoneTests(_log, DataPaths.Fresh("test-zones"));
            zones.Finished += Finish;
            AddChild(zones);
        }
        else Finish();
    }

    void Finish()
    {
        if (_finished) return;
        _finished = true;
        // Quitting happens at the end of the frame; if even that never comes, the watchdog ends it.
        _watchdog.Rearm(QuitLimit, "quitting");
        GD.Print($"\n{_log.Passes} passed, {_log.Failures} failed in {(DateTime.UtcNow - _started).TotalSeconds:F0} s");
        GD.Print(_log.Failures == 0 ? "ALL PASSED" : $"{_log.Failures} CHECK(S) FAILED");
        GetTree().Quit(_log.Failures == 0 ? 0 : 1);
    }
}
