using System;
using System.Diagnostics;
using System.Threading.Tasks;
using GalHack.Testing;
using Godot;

namespace Wildlife;

/// <summary>
/// Quitting in a fixed order, so nothing is left running or half-freed when the engine tears
/// itself down. Every way out goes through here: the test, the screenshot harness and closing
/// the game window.
///
/// The prototype starts no threads of its own, but the engine's audio server mixes on its own
/// thread (even headless, where the dummy driver runs one), and a looping stream such as the
/// hovercraft's hum is only retired by that thread on its next mix step after it is stopped.
/// Quitting straight after freeing the scene could leave a live playback on the mixer while the
/// engine shut down, an intermittent crash on exit. Static caches of meshes and materials, and
/// any C# wrappers waiting for the garbage collector, are released here on the main thread too,
/// rather than by the finalizer thread while the engine is half gone.
///
/// The finalizer thread is waited for from a worker while the main loop goes on: it frees dropped
/// engine objects, and some of those frees are handed back to the main thread (the renderer's),
/// so a main thread parked in WaitForPendingFinalizers can wait on a thread that is waiting on it.
/// And whatever else blocks the way out, <see cref="Backstop"/> ends the process in the end.
/// </summary>
public static class Shutdown
{
    /// <summary>How long to let the audio thread retire stopped playbacks: a few mix steps.</summary>
    const double AudioDrainSeconds = 0.25;

    /// <summary>The longest to wait for the finalizer thread before going on without it, seconds.</summary>
    const double FinalizerSeconds = 5;

    /// <summary>Stops all sound, waits for the mixer to let go, and releases shared resources.</summary>
    public static async Task Quiet(Node anyNode)
    {
        SceneTree tree = anyNode.GetTree();
        foreach (Node n in tree.Root.FindChildren("*", "", true, false))
        {
            switch (n)
            {
                case AudioStreamPlayer p: p.Stop(); break;
                case AudioStreamPlayer2D p: p.Stop(); break;
                case AudioStreamPlayer3D p: p.Stop(); break;
            }
        }
        Backstop.Stage("sound stopped");
        // Real time, not frames: headless frames can pass faster than the mixer steps.
        await anyNode.ToSignal(tree.CreateTimer(AudioDrainSeconds, true, false, true), SceneTreeTimer.SignalName.Timeout);
        Backstop.Stage("audio drained");
        await anyNode.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        MeshBuilder.ReleaseCache();
        CreatureMaterials.Release();
        VeinView.Release();
        Backstop.Stage("caches released");
        // Anything else the scripts dropped is finalized now, while the engine is whole and the
        // main thread waits, instead of racing the engine's own teardown.
        GC.Collect();
        Backstop.Stage("collected, waiting for the finalizer thread");
        Task finalizing = Task.Run(GC.WaitForPendingFinalizers);
        long from = Stopwatch.GetTimestamp();
        while (!finalizing.IsCompleted && Stopwatch.GetElapsedTime(from).TotalSeconds < FinalizerSeconds)
            await anyNode.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Backstop.Stage(finalizing.IsCompleted ? "finalized" : "finalizer thread still busy");
        if (finalizing.IsCompleted) GC.Collect();
    }

    /// <summary>Quiets everything, then quits with the given exit code. The process ends itself if the engine does not get out.</summary>
    public static async void Quit(Node anyNode, int exitCode = 0)
    {
        SceneTree tree = anyNode.GetTree();
        Backstop.Arm(exitCode);
        await Quiet(anyNode);
        Backstop.Stage("quit asked of the engine");
        tree.Quit(exitCode);
    }
}
