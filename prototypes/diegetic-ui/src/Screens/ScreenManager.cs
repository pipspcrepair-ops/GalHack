using System.Diagnostics;
using Godot;

namespace DiegeticUi.Screens;

/// <summary>
/// Decides, once per frame, which world screens draw. A screen draws only when its content
/// changed and the camera could read it; the one in use goes first, and the rest share a
/// small per-frame budget so a burst of changes (every console flipping to an alert at
/// once) is spread over a few frames instead of spiking one.
///
/// Runs after the camera has been placed for the frame (high process priority).
/// </summary>
public partial class ScreenManager : Node
{
    public Camera3D Camera { get; set; }

    /// <summary>Redraws per frame, besides the screen in use.</summary>
    public int Budget { get; set; } = 2;

    /// <summary>
    /// The fastest a screen that is not in use redraws, even while something on it animates
    /// (a lever someone else threw). The screen in use always redraws at once.
    /// </summary>
    public double BackgroundHz { get; set; } = 30;

    // Stats for the HUD and the performance test.
    public int RedrawsLastFrame { get; private set; }
    public double CpuMsLastFrame { get; private set; }
    public long TotalRedraws { get; private set; }

    public override void _Ready() => ProcessPriority = 1000;

    public override void _Process(double delta)
    {
        long start = Stopwatch.GetTimestamp();
        double now = Time.GetTicksUsec() / 1e6;
        int budget = Budget, redraws = 0;

        foreach (WorldScreen screen in WorldScreen.All)
        {
            screen.Rest();
            screen.Poll(now);
        }

        // The screen in use always draws when it changed: it is what the player is looking at.
        foreach (WorldScreen screen in WorldScreen.All)
        {
            if (!screen.InUse || !screen.Dirty) continue;
            screen.Render(now);
            redraws++;
        }

        // Then everything else that changed and can be seen. A screen that has never drawn
        // draws once regardless, so nothing ever comes into view black.
        foreach (WorldScreen screen in WorldScreen.All)
        {
            if (budget <= 0) break;
            if (!screen.Dirty || (screen.EverDrawn && !screen.IsSeenBy(Camera))) continue;
            if (now - screen.LastRender < 1.0 / BackgroundHz) continue;
            screen.Render(now);
            redraws++;
            budget--;
        }

        RedrawsLastFrame = redraws;
        TotalRedraws += redraws;
        CpuMsLastFrame = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}
