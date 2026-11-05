using System;
using System.Threading.Tasks;
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
/// </summary>
public static class Shutdown
{
    /// <summary>How long to let the audio thread retire stopped playbacks: a few mix steps.</summary>
    const double AudioDrainSeconds = 0.25;

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
        // Real time, not frames: headless frames can pass faster than the mixer steps.
        await anyNode.ToSignal(tree.CreateTimer(AudioDrainSeconds, true, false, true), SceneTreeTimer.SignalName.Timeout);
        await anyNode.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        MeshBuilder.ReleaseCache();
        CreatureMaterials.Release();
        // Anything else the scripts dropped is finalized now, while the engine is whole and the
        // main thread waits, instead of racing the engine's own teardown.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>Quiets everything, then quits with the given exit code.</summary>
    public static async void Quit(Node anyNode, int exitCode = 0)
    {
        SceneTree tree = anyNode.GetTree();
        await Quiet(anyNode);
        tree.Quit(exitCode);
    }
}
