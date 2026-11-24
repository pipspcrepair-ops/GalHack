using Godot;

namespace PlanetSurface.Fx;

/// <summary>
/// One allowance a frame for the loading many things do a little a frame on the main thread (a place's
/// stand-up, its full-detail cubes, its rooms' dressing, its creatures' bodies). Each of them had its own few
/// milliseconds, so a city's sixty places loading at once took a few hundred milliseconds a frame between them;
/// now they share this, and what doesn't fit goes on next frame. Whoever asks first in a frame goes first (the
/// first places stood up, the nearest); as they finish they stop asking, so the rest are never kept waiting for ever.
/// </summary>
public static class LoadBudget
{
    /// <summary>The main-thread time all of them may spend in a frame, ms.</summary>
    public static double FrameMs = 4.0;

    static ulong _frame = ulong.MaxValue;
    static double _spent;

    static void Roll()
    {
        ulong f = Engine.GetProcessFrames();
        if (f == _frame) return;
        _frame = f;
        _spent = 0;
    }

    /// <summary>What is left of this frame's allowance, ms (at most <paramref name="own"/>, a caller's own cap).</summary>
    public static double Left(double own = double.MaxValue)
    {
        Roll();
        return System.Math.Min(own, FrameMs - _spent);
    }

    /// <summary>Counts time spent against this frame's allowance.</summary>
    public static void Spend(double ms)
    {
        Roll();
        _spent += ms;
    }
}
