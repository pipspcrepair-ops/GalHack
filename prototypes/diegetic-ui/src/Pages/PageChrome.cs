using DiegeticUi.Logic;
using DiegeticUi.Theme;
using Godot;

namespace DiegeticUi.Pages;

/// <summary>The parts every console page shares: the status strip under the header, which turns into the alert countdown.</summary>
public static class PageChrome
{
    static Palette C => UiTheme.Colors;

    public const float StripTop = 88, StripHeight = 50;

    /// <summary>
    /// Draws the strip. During a threat every console shows the same red countdown, so the
    /// warning is on whichever screen you happen to be at.
    /// </summary>
    public static void Strip(CanvasItem ci, float width, WarningTimeline warnings, string text, Color colour)
    {
        Threat threat = warnings.Next;
        if (threat != null)
        {
            ci.Stripes(new Rect2(0, StripTop, 70, StripHeight), C.Glass, C.Danger, 14);
            ci.Block(new Rect2(70, StripTop, width - 70, StripHeight), C.Danger);
            ci.TextIn(UiTheme.Display, 26, new Rect2(96, StripTop, width - 300, StripHeight), threat.Title, C.Glass);
            ci.TextIn(UiTheme.Mono, 34, new Rect2(0, StripTop, width - 32, StripHeight), "T-" + Paint.Clock(warnings.Remaining(threat)),
                C.Glass, HorizontalAlignment.Right);
            return;
        }
        var strip = new Rect2(0, StripTop, width, StripHeight);
        ci.Block(strip, C.Panel);
        ci.Block(new Rect2(0, StripTop, 14, StripHeight), colour);
        ci.TextIn(UiTheme.Label, UiTheme.SizeLabel, strip, text, colour, padX: 36);
    }
}
