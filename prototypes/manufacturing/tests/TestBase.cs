using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace Manufacturing.Tests;

/// <summary>
/// A headless test scene: runs named sections of checks, prints each failure, and quits with 0 if
/// all passed or 1 if any failed. A section that throws counts as one failure and the rest go on.
/// </summary>
public abstract partial class TestBase : Node
{
    int _passes, _failures;
    string _section = "";

    protected abstract void RunAll();

    public override void _Ready()
    {
        var clock = Stopwatch.StartNew();
        RunAll();
        GD.Print($"\n{_passes} checks passed, {_failures} failed ({clock.Elapsed.TotalSeconds:0.0} s)");
        GD.Print(_failures == 0 ? "ALL PASSED" : "SOME CHECKS FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    protected void Run(string name, Action section)
    {
        _section = name;
        GD.Print($"\n== {name}");
        try { section(); }
        catch (Exception e)
        {
            _failures++;
            GD.PrintErr($"  FAIL {name} threw: {e}");
        }
    }

    protected bool Check(bool ok, string what)
    {
        if (ok) _passes++;
        else
        {
            _failures++;
            GD.PrintErr($"  FAIL [{_section}] {what}");
        }
        return ok;
    }

    protected static void Note(string line) => GD.Print("  " + line);

    /// <summary>
    /// Times an action over three windows and returns the best (smallest) per-run milliseconds:
    /// other agents run tests in parallel, so a check must not depend on how busy the machine is.
    /// </summary>
    protected static double BestOfThree(Action action, int runsPerWindow = 1)
    {
        action(); // warm the JIT
        double best = double.MaxValue;
        for (int w = 0; w < 3; w++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < runsPerWindow; i++) action();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / runsPerWindow);
        }
        return best;
    }
}
