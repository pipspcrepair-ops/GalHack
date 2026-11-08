using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace GalHack.Testing;

/// <summary>
/// A last resort for a test or screenshot run that has been told to quit and doesn't. Windowed runs
/// were found hanging after their last frame was saved, each holding a couple of gigabytes until
/// somebody ended it; whatever blocks the engine's way out, the process must still go. Arming this
/// starts a plain background thread that waits <see cref="Seconds"/> and, if the process is still
/// here, ends it, keeping the exit code the run meant to give. It is the process ending itself, no
/// other process is touched. <see cref="Stage"/> notes how far the orderly way out got, so a hang
/// says where.
/// </summary>
public static class Backstop
{
    /// <summary>How long after quit is asked for the process has to be gone: a long teardown of a big world takes a few seconds.</summary>
    public const double Seconds = 25;

    static int _armed;
    static volatile string _stage = "not started";

    /// <summary>Notes the last step of the way out that was reached (read only when the backstop fires).</summary>
    public static void Stage(string stage) => _stage = stage;

    [DllImport("kernel32.dll")]
    static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    static extern bool TerminateProcess(nint process, uint exitCode);

    /// <summary>Asks the engine to quit with this exit code, and arms the countdown to the process ending itself if it does not.</summary>
    public static void Quit(Node anyNode, int exitCode = 0)
    {
        Stage("quit asked of the engine");
        Arm(exitCode);
        anyNode.GetTree().Quit(exitCode);
    }

    /// <summary>Starts the countdown to the process ending itself (once; later calls do nothing).</summary>
    public static void Arm(int exitCode = 0, double seconds = Seconds)
    {
        if (Interlocked.Exchange(ref _armed, 1) == 1) return;
        int pid = OS.GetProcessId();
        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            try { Console.Error.WriteLine($"quit backstop: still running {seconds:F0} s after quit was asked for (last step reached: {_stage}); the process ends itself with code {exitCode}"); } catch { }
            try
            {
                if (OperatingSystem.IsWindows() && TerminateProcess(GetCurrentProcess(), (uint)exitCode)) return;
            }
            catch { }
            try { OS.Kill(pid); } catch { }
            try { Process.GetCurrentProcess().Kill(); } catch { }
        }) { IsBackground = true, Name = "Quit backstop" }.Start();
    }
}
