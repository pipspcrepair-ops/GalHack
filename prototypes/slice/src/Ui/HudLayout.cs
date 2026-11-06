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
/// and left of the bottom band stay clear for the held tool.
/// </summary>
public readonly record struct HudLayout(float Scale, Rect2 Vitals, Rect2 Toolbar, Rect2 ItemLine, Rect2 Hints, Rect2 Shapes, Rect2 Lines)
{
    public const int Slots = 9;
    public const float VitalW = 150, VitalH = 54, VitalGap = 14, Cell = 52, CellGap = 4, WideRow = 760;

    /// <summary>Every box, by name, for the overlap check.</summary>
    public IEnumerable<(string Name, Rect2 Box)> Boxes => new[]
    {
        ("vitals", Vitals), ("toolbar", Toolbar), ("item line", ItemLine), ("hints", Hints), ("shapes", Shapes), ("lines", Lines),
    };

    /// <summary>The layout for a window size and a UI scale (1 is the default; it shrinks to fit a narrow window).</summary>
    public static HudLayout Compute(Vector2 size, float uiScale = 1f)
    {
        float vitalsW = 7 * VitalW + 6 * VitalGap;
        float s = MathF.Min(size.Y / 900f * uiScale, MathF.Min(size.X / (vitalsW + 40f), size.Y / 520f));
        float cx = size.X / 2f;
        Rect2 Row(float y, float w, float h) => new(cx - w * s / 2f, y, w * s, h * s);
        Rect2 vitals = Row(size.Y - (12 + VitalH) * s, vitalsW, VitalH);
        float barW = Slots * Cell + (Slots - 1) * CellGap;
        Rect2 toolbar = Row(vitals.Position.Y - (8 + Cell) * s, barW, Cell);
        Rect2 item = Row(toolbar.Position.Y - (4 + 22) * s, WideRow, 22);
        Rect2 hints = Row(item.Position.Y - (2 + 16) * s, WideRow, 16);
        Rect2 shapes = Row(hints.Position.Y - (4 + 26) * s, barW, 26);
        float linesH = 5 * 26 * s;
        var lines = new Rect2(40 * s, shapes.Position.Y - 8 * s - linesH, size.X - 80 * s, linesH);
        return new HudLayout(s, vitals, toolbar, item, hints, shapes, lines);
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
