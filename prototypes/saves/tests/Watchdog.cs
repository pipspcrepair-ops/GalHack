using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Saves.Tests;

/// <summary>
/// Ends a test process that has stopped making progress, with exit code 1, so a hang fails
/// instead of sitting there for hours. It runs on its own timer thread and ends the process the
/// hard way, so it works even when Godot's main thread is stuck in a loop or a wait.
/// </summary>
public sealed class Watchdog : IDisposable
{
    readonly Timer _timer;
    string _what;
    TimeSpan _limit;

    public Watchdog(TimeSpan limit, string what)
    {
        _what = what;
        _limit = limit;
        _timer = new Timer(_ => Fire(), null, limit, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Starts the countdown again, with a new limit and description.</summary>
    public void Rearm(TimeSpan limit, string what)
    {
        _what = what;
        _limit = limit;
        _timer.Change(limit, Timeout.InfiniteTimeSpan);
    }

    public void Dispose() => _timer.Dispose();

    void Fire()
    {
        Console.Error.WriteLine($"FAIL  watchdog: {_what} was still going after {_limit.TotalSeconds:F0} s; exiting with code 1");
        Console.Error.Flush();
        Console.Out.Flush();
        Exit(1);
    }

    /// <summary>Ends this process now with <paramref name="code"/>, whatever the main thread is doing.</summary>
    public static void Exit(int code)
    {
        // Environment.Exit runs shutdown code that can wait on the stuck main thread; TerminateProcess can't.
        if (OperatingSystem.IsWindows()) TerminateProcess(GetCurrentProcess(), (uint)code);
        Environment.Exit(code);
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateProcess(IntPtr process, uint exitCode);
}
