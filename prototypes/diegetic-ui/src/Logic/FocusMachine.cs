using System;

namespace DiegeticUi.Logic;

public enum FocusState
{
    /// <summary>Walking around. Mouse looks, no cursor anywhere.</summary>
    Free,
    /// <summary>The view is easing in to frame a screen. Still no cursor.</summary>
    Entering,
    /// <summary>Framed and in use: the mouse drives a cursor on the screen's surface.</summary>
    Using,
    /// <summary>Esc pressed: easing back out. The cursor is already gone.</summary>
    Leaving,
}

/// <summary>
/// Using a screen, as four states. The camera blend, the input routing and the cursor all
/// read from here, so they can never disagree (a cursor that shows while the view is still
/// flying in, or mouse-look that fights a framed screen).
///
/// Generic over the target so it stays engine-free and testable headless.
/// </summary>
public sealed class FocusMachine<T> where T : class
{
    public FocusState State { get; private set; } = FocusState.Free;
    public T Target { get; private set; }

    /// <summary>0 at the walking view, 1 fully framed on the screen.</summary>
    public double Blend { get; private set; }

    /// <summary>Seconds to ease in or out. Short: using a screen should feel quick.</summary>
    public double EaseSeconds { get; set; } = 0.35;

    public bool CursorVisible => State == FocusState.Using;
    public bool LookLocked => State != FocusState.Free;
    public bool Busy => State != FocusState.Free;

    /// <summary>Smoothstep of <see cref="Blend"/>, for the camera.</summary>
    public double EasedBlend => Blend * Blend * (3 - 2 * Blend);

    /// <summary>Starts using a screen. Only from Free: you step back before using another.</summary>
    public bool Enter(T target)
    {
        if (State != FocusState.Free || target == null) return false;
        Target = target;
        State = FocusState.Entering;
        Blend = 0;
        return true;
    }

    /// <summary>Esc: back out from wherever the ease has got to.</summary>
    public bool Back()
    {
        if (State is not (FocusState.Entering or FocusState.Using)) return false;
        State = FocusState.Leaving;
        return true;
    }

    public void Tick(double dt)
    {
        double step = EaseSeconds <= 0 ? 1 : dt / EaseSeconds;
        switch (State)
        {
            case FocusState.Entering:
                Blend = Math.Min(1, Blend + step);
                if (Blend >= 1) State = FocusState.Using;
                break;
            case FocusState.Leaving:
                Blend = Math.Max(0, Blend - step);
                if (Blend <= 0)
                {
                    State = FocusState.Free;
                    Target = null;
                }
                break;
        }
    }
}
