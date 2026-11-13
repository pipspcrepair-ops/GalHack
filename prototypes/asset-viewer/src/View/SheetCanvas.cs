using System;
using System.Collections.Generic;
using Godot;

namespace AssetViewer;

/// <summary>The marks drawn over one tile of a sheet, in the tile's pixels.</summary>
public sealed class TileMarks
{
    public string Label = "", View = "";
    public readonly List<(Vector2 Target, Vector2 Drawn, bool Planted)> Feet = new();
}

/// <summary>
/// Puts a filmstrip together: the rendered tiles, a line of facts across the top, a label on each tile, and on each foot a dot at the
/// rig's target (green planted, orange in the air), a ring where the foot is drawn, and a line between them when they differ.
/// It is tool furniture drawn with the engine's default font, not game content.
/// </summary>
public partial class SheetCanvas : Control
{
    public SheetLayout Layout;
    public string Title = "";
    public Texture2D[,] Tiles;       // [phase, view]
    public TileMarks[,] Marks;

    static readonly Color Planted = new(0.15f, 0.9f, 0.25f), InAir = new(1f, 0.6f, 0.1f), Off = new(1f, 0.15f, 0.85f);

    void Text(Font font, Vector2 at, string text, Color colour)
    {
        DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, 11, 4, new Color(0f, 0f, 0f));
        DrawString(font, at, text, HorizontalAlignment.Left, -1, 11, colour);
    }

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(0, 0, Layout.Width, Layout.Height), new Color(0.1f, 0.1f, 0.11f));
        DrawString(font, new Vector2(6, 19), Title, HorizontalAlignment.Left, Layout.Width - 12, 13, new Color(0.92f, 0.92f, 0.92f));
        for (int p = 0; p < Layout.Phases; p++)
            for (int v = 0; v < Layout.Views; v++)
            {
                Vector2I o = Layout.Origin(p, v);
                if (Tiles[p, v] != null) DrawTexture(Tiles[p, v], o);
                TileMarks m = Marks[p, v];
                foreach (var (target, drawn, planted) in m.Feet)
                {
                    Vector2 t = o + target, d = o + drawn;
                    if (t.DistanceTo(d) > 3f) DrawLine(t, d, Off, 2f, true);
                    DrawArc(d, 6f, 0f, Mathf.Tau, 16, new Color(0f, 0f, 0f), 3.5f, true);
                    DrawArc(d, 6f, 0f, Mathf.Tau, 16, Colors.White, 1.5f, true);
                    DrawCircle(t, 3.5f, planted ? Planted : InAir);
                }
                Text(font, new Vector2(o.X + 4, o.Y + 13), m.Label, Colors.White);
                if (p == 0) Text(font, new Vector2(o.X + 4, o.Y + Layout.TileH - 5), m.View, new Color(1f, 0.9f, 0.4f));
            }
    }
}
