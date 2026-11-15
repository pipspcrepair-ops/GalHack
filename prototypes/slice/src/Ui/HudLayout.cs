using System;
using System.Collections.Generic;
using DiegeticUi.Theme;
using Godot;

namespace Slice.Ui;

/// <summary>
/// The visor's layout (design: Visor HUD layout), worked out in one place so nothing overlaps at any resolution or UI
/// scale. Bottom left, the sleeper's gauges with their icon row above them and the hazards' row above that; bottom
/// right, seated, the structure's gauges and icon row, mirroring them; the upper left, the surroundings. Bottom middle,
/// from the bottom up: the toolbar with its name at its left, the selected item's line, the key hints, the shape strip
/// with a block family in hand. The suit's lines sit above it all at the left; the jetpack's gauge stands alone at the
/// right edge, a little above the middle.
/// </summary>
public readonly record struct HudLayout(float Scale, Rect2 Sleeper, Rect2 SleeperIcons, Rect2 Surroundings, Rect2 Structure, Rect2 StructureIcons,
    Rect2 Toolbar, Rect2 ItemLine, Rect2 Hints, Rect2 Shapes, Rect2 Lines, Rect2 Jet, Rect2 Compass, Rect2 Alerts, Rect2 MenuAlerts, Rect2 BarName,
    Rect2 Flight, Rect2 Mode, Rect2 Notices)
{
    public const int Slots = 9;
    public const float Cell = 52, CellGap = 4, WideRow = 720, JetW = 84, JetH = 150, Edge = 24, Foot = 12, BarNameW = 110;
    /// <summary>A gauge panel (the sleeper's, the structure's): its width, each row's height, the gap between rows and the padding round them.</summary>
    public const float PanelW = 270, GaugeH = 26, GaugeGap = 4, GaugePad = 6;
    /// <summary>The rows each gauge panel holds: the sleeper's Health, Stamina, Food, Air and Rads; the structure's capacitor, fuel, warp and fold.</summary>
    public const int SleeperRows = 5, StructureRows = 4;
    /// <summary>An icon in a panel's row above it, and the gap between icons.</summary>
    public const float IconCell = 34, IconGap = 6;
    /// <summary>The surroundings' readouts at the upper left: the box's width and each row's height.</summary>
    public const float SurroundW = 190, SurroundRowH = 28;
    /// <summary>The compass: its strip's width and the strip's top; in COMPASS markers mode, how many rows of labels it may grow below its strip.</summary>
    public const float CompassW = 640, CompassTop = 34;
    public const int CompassRows = 2;
    /// <summary>The suit's lines shown at once.</summary>
    public const int LinesShown = 3;
    /// <summary>An alert's sign (the toolbar's height), the room for its timer above it, and the width each alert takes in the row.</summary>
    public const float AlertIcon = Cell, AlertTimerH = 22, AlertCell = Cell + 14;
    /// <summary>The row's width: room for the most alerts at once and the last timer's overhang, no more (the You page's bag column starts just past it).</summary>
    public const float AlertRow = AlertIcon + (Ui.Alerts.MostHazards - 1) * AlertCell + 4;
    /// <summary>With a menu up, how far above the bottom the alerts' signs stand: the corner every menu keeps clear of its panels.</summary>
    public const float MenuAlertsFoot = 74;
    /// <summary>The notices' slot: its width, each row's height and the gap between rows (one row per notice shown at once).</summary>
    public const float NoticeW = 280, NoticeRowH = 30, NoticeGap = 4;

    /// <summary>
    /// The notices' slot while a menu is up, in the menus' design units (1600 x 900): the top right of the menu's header,
    /// which both menus keep clear of their own text and widgets. The same corner as on the visor.
    /// </summary>
    public static readonly Rect2 MenuNotices = new(1290, 4, 300, 66);

    /// <summary>A menu's design-unit rectangle in a window's pixels (the menus fit 1600 x 900 into the window, centred).</summary>
    public static Rect2 MenuToScreen(Rect2 design, Vector2 size)
    {
        float k = MathF.Min(size.X / 1600f, size.Y / 900f);
        Vector2 offset = (size - new Vector2(1600f, 900f) * k) * 0.5f;
        return new Rect2(offset + design.Position * k, design.Size * k);
    }

    /// <summary>Every box the visor draws in, by name, for the overlap check (the menus' alerts' slot shows only while the visor is hidden).</summary>
    public IEnumerable<(string Name, Rect2 Box)> Boxes => new[]
    {
        ("sleeper gauges", Sleeper), ("sleeper icons", SleeperIcons), ("surroundings", Surroundings), ("structure gauges", Structure), ("structure icons", StructureIcons),
        ("toolbar", Toolbar), ("item line", ItemLine), ("hints", Hints), ("shapes", Shapes), ("lines", Lines), ("jetpack gauge", Jet),
        ("compass", Compass), ("alerts", Alerts), ("bar name", BarName), ("flight line", Flight), ("mode plate", Mode), ("notices", Notices),
    };

    /// <summary>A gauge panel's rows, top to bottom.</summary>
    public static Rect2[] GaugeRows(Rect2 panel, float s, int count)
    {
        var rows = new Rect2[count];
        for (int i = 0; i < count; i++)
            rows[i] = new Rect2(panel.Position.X + GaugePad * s, panel.Position.Y + (GaugePad + i * (GaugeH + GaugeGap)) * s, panel.Size.X - 2 * GaugePad * s, GaugeH * s);
        return rows;
    }

    /// <summary>The <paramref name="i"/>th icon in an icon row: from its left end, or from its right (the structure's, mirroring the sleeper's).</summary>
    public static Rect2 IconAt(Rect2 row, int i, float s, bool fromRight = false)
    {
        float side = IconCell * s, x = i * (IconCell + IconGap) * s;
        return new Rect2(fromRight ? row.End.X - x - side : row.Position.X + x, row.Position.Y, side, side);
    }

    /// <summary>The layout for a window size and a UI scale (1 is the default; it shrinks to fit a narrow window).</summary>
    /// <param name="compassLabels">COMPASS markers mode: the compass has its label rows below the strip, and the labels' overhang (a label is 220 wide, centred on a mark that may sit at the strip's end).</param>
    public static HudLayout Compute(Vector2 size, float uiScale = 1f, bool compassLabels = false)
    {
        float barW = Slots * Cell + (Slots - 1) * CellGap;
        // The bottom band's width: a gauge panel at each side and the middle column between (the toolbar and its name, or the wide lines).
        float bandW = 2 * (Edge + PanelW + 10) + MathF.Max(WideRow, barW + 2 * (BarNameW + 10));
        float s = MathF.Min(size.Y / 900f * uiScale, MathF.Min(size.X / bandW, size.Y / 520f));
        float cx = size.X / 2f;
        Rect2 Row(float y, float w, float h) => new(cx - w * s / 2f, y, w * s, h * s);
        Rect2 toolbar = Row(size.Y - (Foot + Cell) * s, barW, Cell);
        Rect2 item = Row(toolbar.Position.Y - (4 + 22) * s, WideRow, 22);
        Rect2 hints = Row(item.Position.Y - (2 + 16) * s, WideRow, 16);
        Rect2 shapes = Row(hints.Position.Y - (4 + 58) * s, barW, 58);
        var barName = new Rect2(toolbar.Position.X - (BarNameW + 8) * s, toolbar.Position.Y + Cell * s / 2f - 9 * s, BarNameW * s, 18 * s);
        // The gauge panels in the bottom corners, each with its icon row above it.
        float PanelH(int rows) => (rows * GaugeH + (rows - 1) * GaugeGap + 2 * GaugePad) * s;
        var sleeper = new Rect2(Edge * s, size.Y - Foot * s - PanelH(SleeperRows), PanelW * s, PanelH(SleeperRows));
        var structure = new Rect2(size.X - (Edge + PanelW) * s, size.Y - Foot * s - PanelH(StructureRows), PanelW * s, PanelH(StructureRows));
        Rect2 IconsOver(Rect2 panel) => new(panel.Position.X, panel.Position.Y - (8 + IconCell) * s, panel.Size.X, IconCell * s);
        Rect2 sleeperIcons = IconsOver(sleeper), structureIcons = IconsOver(structure);
        // The hazards' row above the sleeper's icons, each sign the toolbar's height with its timer above it; with a menu
        // up, the corner every menu keeps clear of its panels (where the row stood before the gauges took the bottom left).
        var alerts = new Rect2(Edge * s, sleeperIcons.Position.Y - (8 + Cell + AlertTimerH) * s, AlertRow * s, (Cell + AlertTimerH) * s);
        var menuAlerts = new Rect2(Edge * s, size.Y - (MenuAlertsFoot + Cell + AlertTimerH) * s, AlertRow * s, (Cell + AlertTimerH) * s);
        float linesH = LinesShown * 26 * s;
        var jet = new Rect2(size.X - (Edge + JetW) * s, size.Y * 0.45f - JetH * s / 2f, JetW * s, JetH * s);
        var surroundings = new Rect2(Edge * s, 14 * s, SurroundW * s, 2 * SurroundRowH * s);
        // The compass strip, its N/E/S/W letters (40 wide at a strip's end) and the heading under it.
        float compassW = CompassW + (compassLabels ? 220 : 40);
        var compass = new Rect2(cx - compassW * s / 2f, 14 * s, compassW * s, (CompassTop + 44 + (compassLabels ? CompassRows * 16 : 0) - 14) * s);
        // Seated: the flight line, its message under it, and the mode plate under that (beside it, it ran off a narrow window).
        var flight = new Rect2(cx - 360 * s, compass.End.Y + 14 * s, 720 * s, 68 * s);
        var mode = new Rect2(cx - 96 * s, flight.End.Y + 6 * s, 192 * s, 34 * s);
        // The suit's lines above it all at the left, up to the jetpack's gauge; where a large UI scale lifts them as high as
        // the seated flight line or mode plate, they stop short of those.
        float linesY = MathF.Min(alerts.Position.Y, shapes.Position.Y) - 8 * s - linesH, linesEnd = jet.Position.X - 12 * s;
        foreach (Rect2 seated in new[] { flight, mode })
            if (linesY < seated.End.Y + 8 * s) linesEnd = MathF.Min(linesEnd, seated.Position.X - 12 * s);
        var lines = new Rect2(Edge * s, linesY, linesEnd - Edge * s, linesH);
        // Notices: the top right, beside the compass; where the window is too narrow for that, under the compass's
        // right end, past the flight line (narrower and shorter if it must be), above the jetpack's gauge.
        float nw = NoticeW * s, nh = (Ui.Notices.MostShown * NoticeRowH + (Ui.Notices.MostShown - 1) * NoticeGap) * s, right = size.X - Edge * s;
        Rect2 notices = right - nw >= compass.End.X + 12 * s
            ? new Rect2(right - nw, compass.Position.Y, nw, nh)
            : new Rect2(right - MathF.Min(nw, right - flight.End.X - 12 * s), compass.End.Y + 8 * s, MathF.Min(nw, right - flight.End.X - 12 * s),
                MathF.Min(nh, jet.Position.Y - 8 * s - (compass.End.Y + 8 * s)));
        return new HudLayout(s, sleeper, sleeperIcons, surroundings, structure, structureIcons, toolbar, item, hints, shapes, lines, jet, compass, alerts, menuAlerts,
            barName, flight, mode, notices);
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
