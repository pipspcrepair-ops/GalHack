using System;
using System.Collections.Generic;
using DiegeticUi.Theme;
using Godot;

namespace Slice.Ui;

/// <summary>
/// The visor's bottom layout, worked out in one place so nothing overlaps at any resolution or
/// UI scale. From the bottom of the glass up, centred: the vitals row, the nine-slot toolbar,
/// the selected item's line (name, mode or shape, count, source), the key hints on one quiet
/// line, the shape strip with a block family in hand, and above them the suit's lines. The right
/// and left of the bottom band stay clear for the held tool. The jetpack's gauge stands alone at
/// the right edge, a little above the middle, and the suit's lines stop short of it.
/// </summary>
public readonly record struct HudLayout(float Scale, Rect2 Vitals, Rect2 Toolbar, Rect2 ItemLine, Rect2 Hints, Rect2 Shapes, Rect2 Lines, Rect2 Jet,
    Rect2 Compass, Rect2 Alerts, Rect2 BarName, Rect2 Flight, Rect2 Mode)
{
    public const int Slots = 9;
    public const float VitalW = 150, VitalH = 54, VitalGap = 14, Cell = 52, CellGap = 4, WideRow = 760, JetW = 84, JetH = 150;
    /// <summary>The compass: its width, and how many rows of labels it may grow below its strip.</summary>
    public const float CompassW = 640, CompassTop = 34;
    public const int CompassRows = 2;
    /// <summary>The suit's lines shown at once.</summary>
    public const int LinesShown = 3;
    /// <summary>An alert's sign (the toolbar's height), the room for its timer above it, and the width each alert takes in the row.</summary>
    public const float AlertIcon = Cell, AlertTimerH = 22, AlertCell = Cell + 14;

    /// <summary>Every box, by name, for the overlap check.</summary>
    public IEnumerable<(string Name, Rect2 Box)> Boxes => new[]
    {
        ("vitals", Vitals), ("toolbar", Toolbar), ("item line", ItemLine), ("hints", Hints), ("shapes", Shapes), ("lines", Lines), ("jetpack gauge", Jet),
        ("compass", Compass), ("alerts", Alerts), ("bar name", BarName), ("flight line", Flight), ("mode plate", Mode),
    };

    /// <summary>The layout for a window size and a UI scale (1 is the default; it shrinks to fit a narrow window).</summary>
    public static HudLayout Compute(Vector2 size, float uiScale = 1f)
    {
        float vitalsW = 6 * VitalW + 5 * VitalGap;
        float s = MathF.Min(size.Y / 900f * uiScale, MathF.Min(size.X / (vitalsW + 40f), size.Y / 520f));
        float cx = size.X / 2f;
        Rect2 Row(float y, float w, float h) => new(cx - w * s / 2f, y, w * s, h * s);
        Rect2 vitals = Row(size.Y - (12 + VitalH) * s, vitalsW, VitalH);
        float barW = Slots * Cell + (Slots - 1) * CellGap;
        Rect2 toolbar = Row(vitals.Position.Y - (8 + Cell) * s, barW, Cell);
        Rect2 item = Row(toolbar.Position.Y - (4 + 22) * s, WideRow, 22);
        Rect2 hints = Row(item.Position.Y - (2 + 16) * s, WideRow, 16);
        Rect2 shapes = Row(hints.Position.Y - (4 + 58) * s, barW, 58);
        float linesH = LinesShown * 26 * s;
        var jet = new Rect2(size.X - (24 + JetW) * s, size.Y * 0.45f - JetH * s / 2f, JetW * s, JetH * s);
        float linesX = 24 * s;
        var lines = new Rect2(linesX, shapes.Position.Y - 8 * s - linesH, jet.Position.X - 12 * s - linesX, linesH);
        // The compass strip and its labels (a label is 220 wide, centred on a mark that may sit at the strip's end).
        float compassW = CompassW + 220;
        var compass = new Rect2(cx - compassW * s / 2f, 14 * s, compassW * s, (CompassTop + 44 + CompassRows * 16 - 14) * s);
        var barName = new Rect2(toolbar.Position.X - 150 * s, toolbar.Position.Y + Cell * s / 2f - 9 * s, 142 * s, 18 * s);
        // Seated: the flight line, its message under it, and the mode plate under that (beside it, it ran off a narrow window).
        var flight = new Rect2(cx - 360 * s, compass.End.Y + 14 * s, 720 * s, 68 * s);
        var mode = new Rect2(cx - 96 * s, flight.End.Y + 6 * s, 192 * s, 34 * s);
        // Alerts in a row along the toolbar's line, from the far left edge to the bar's name, each sign
        // the toolbar's height with its timer above it: a corner no menu uses, so they stay put over menus too.
        var alerts = new Rect2(24 * s, toolbar.Position.Y - AlertTimerH * s, MathF.Max(0f, barName.Position.X - 8 * s - 24 * s), (Cell + AlertTimerH) * s);
        return new HudLayout(s, vitals, toolbar, item, hints, shapes, lines, jet, compass, alerts, barName, flight, mode);
    }

    /// <summary>The first two boxes that overlap or leave the window, or null if the layout is sound.</summary>
    public string Problem(Vector2 size)
    {
        var screen = new Rect2(Vector2.Zero, size);
        var boxes = new List<(string Name, Rect2 Box)>(Boxes);
        for (int i = 0; i < boxes.Count; i++)
        {
            if (!screen.Encloses(boxes[i].Box)) return $"{boxes[i].Name} leaves the {size.X}x{size.Y} window: {boxes[i].Box}";
            for (int k = i + 1; k < boxes.Count; k++)
                if (boxes[i].Box.Intersects(boxes[k].Box)) return $"{boxes[i].Name} overlaps {boxes[k].Name} at {size.X}x{size.Y}: {boxes[i].Box} and {boxes[k].Box}";
        }
        return null;
    }

    /// <summary>Text cut to fit a width, with "..." if it had to be.</summary>
    public static string Fit(Font font, int size, string text, float width)
    {
        if (string.IsNullOrEmpty(text) || Paint.TextWidth(font, size, text) <= width) return text ?? "";
        int low = 0, high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (Paint.TextWidth(font, size, text[..mid].TrimEnd() + "...") <= width) low = mid;
            else high = mid - 1;
        }
        return text[..low].TrimEnd() + "...";
    }
}
