using System;
using Godot;

namespace DiegeticUi.Theme;

/// <summary>
/// Drawing helpers for the chunky 1970s screen style: flat colour blocks, big type,
/// segmented bars, hazard stripes. Every page and widget draws with these, so the style is
/// set in one place.
/// </summary>
public static class Paint
{
    static Palette C => UiTheme.Colors;

    /// <summary>Text with its box's top-left at <paramref name="topLeft"/> (not the baseline), optionally aligned in a width.</summary>
    public static void Text(this CanvasItem ci, Font font, int size, Vector2 topLeft, string text, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left, float width = -1)
    {
        ci.DrawString(font, topLeft + new Vector2(0, font.GetAscent(size)), text, align, width, size, color);
    }

    /// <summary>Text vertically centred in a rectangle.</summary>
    public static void TextIn(this CanvasItem ci, Font font, int size, Rect2 rect, string text, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left, float padX = 0)
    {
        float h = font.GetAscent(size) + font.GetDescent(size);
        var pos = new Vector2(rect.Position.X + padX, rect.Position.Y + (rect.Size.Y - h) / 2);
        ci.Text(font, size, pos, text, color, align, rect.Size.X - padX * 2);
    }

    public static float TextWidth(Font font, int size, string text) => font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    public static void Block(this CanvasItem ci, Rect2 rect, Color color) => ci.DrawRect(rect, color);

    public static void Frame(this CanvasItem ci, Rect2 rect, Color color, float width = 3) =>
        ci.DrawRect(rect.Grow(-width / 2), color, false, width);

    /// <summary>
    /// A screen's title band: a solid block of the faction colour with the title knocked out of
    /// it, three racing stripes, and a tag block at the right. The look of a 70s paperback spine.
    /// </summary>
    public static void Header(this CanvasItem ci, float width, string title, string tag, Color? colour = null, float height = 84)
    {
        Color main = colour ?? C.Accent;
        ci.Block(new Rect2(0, 0, width, height), main);
        ci.TextIn(UiTheme.Display, UiTheme.SizeTitle, new Rect2(0, 0, width, height), title, C.Glass, padX: 30);

        float tagWidth = Mathf.Max(170, TextWidth(UiTheme.Display, 30, tag) + 48);
        float x = width - tagWidth;
        ci.Block(new Rect2(x, 0, tagWidth, height), C.Text);
        ci.TextIn(UiTheme.Display, 30, new Rect2(x, 0, tagWidth, height), tag, C.Glass, HorizontalAlignment.Center);

        Color[] stripes = { C.Accent2, C.Danger, C.Good };
        for (int i = 0; i < stripes.Length; i++)
            ci.Block(new Rect2(x - 22 - i * 22, 0, 12, height), stripes[stripes.Length - 1 - i]);
        ci.Block(new Rect2(0, height, width, 4), C.Glass);
    }

    /// <summary>A small caption with a short underline in a colour, to head a section.</summary>
    public static void Caption(this CanvasItem ci, Vector2 at, string text, Color? colour = null, int size = UiTheme.SizeSmall)
    {
        Color c = colour ?? C.Dim;
        ci.Text(UiTheme.Label, size, at, text, c);
        ci.Block(new Rect2(at.X, at.Y + size + 8, 36, 4), c);
    }

    /// <summary>
    /// A segmented bar. Segments up to <paramref name="value"/> are filled; a preview delta
    /// shows as hollow segments in the warning colour (or red if it would go over), so you
    /// see what a switch would cost before you flip it.
    /// </summary>
    public static void Segments(this CanvasItem ci, Rect2 rect, double value, double max, Color fill,
        double preview = 0, int count = 24, float gap = 5, float alpha = 1)
    {
        Color rule = new(C.Rule, C.Rule.A * alpha);
        float w = (rect.Size.X - gap * (count - 1)) / count;
        double per = max / count;
        double after = value + preview;
        for (int i = 0; i < count; i++)
        {
            var seg = new Rect2(rect.Position.X + i * (w + gap), rect.Position.Y, w, rect.Size.Y);
            double lo = i * per;
            bool on = value > lo + per * 0.25;
            bool willBeOn = after > lo + per * 0.25;
            if (on && !willBeOn)
            {
                ci.Block(seg, rule);                                // would switch off: dimmed
                ci.Frame(seg, fill, 3);
            }
            else if (on) ci.Block(seg, fill);
            else if (willBeOn) ci.Frame(seg, after > max ? C.Danger : C.Warn, 4); // would switch on: hollow
            else ci.Block(seg, rule);
        }
        if (after > max * 1.0001)
        {
            // Over the limit: a red block past the end of the bar says so without a pop-up.
            var over = new Rect2(rect.End.X + 8, rect.Position.Y, 14, rect.Size.Y);
            ci.Block(over, C.Danger);
        }
    }

    /// <summary>Diagonal hazard stripes clipped to a rectangle.</summary>
    public static void Stripes(this CanvasItem ci, Rect2 rect, Color a, Color b, float stripe = 18)
    {
        ci.Block(rect, b);
        float h = rect.Size.Y;
        Vector2[] clip = { rect.Position, new(rect.End.X, rect.Position.Y), rect.End, new(rect.Position.X, rect.End.Y) };
        for (float x = rect.Position.X - h; x < rect.End.X; x += stripe * 2)
        {
            Vector2[] band = { new(x, rect.End.Y), new(x + h, rect.Position.Y), new(x + h + stripe, rect.Position.Y), new(x + stripe, rect.End.Y) };
            foreach (Vector2[] piece in Geometry2D.IntersectPolygons(band, clip))
                ci.DrawColoredPolygon(piece, a);
        }
    }

    /// <summary>A round lamp: lit, or a dark socket.</summary>
    public static void Lamp(this CanvasItem ci, Vector2 centre, float radius, Color colour, bool lit)
    {
        ci.DrawCircle(centre, radius + 3, C.Glass);
        ci.DrawCircle(centre, radius, lit ? colour : C.Rule);
        if (lit) ci.DrawCircle(centre + new Vector2(-radius * 0.3f, -radius * 0.3f), radius * 0.28f, colour.Lightened(0.6f));
    }

    /// <summary>A simple vector glyph for an item category, so a grid reads at a glance.</summary>
    public static void ItemGlyph(this CanvasItem ci, Logic.ItemKind kind, Vector2 c, float r, Color colour)
    {
        switch (kind)
        {
            case Logic.ItemKind.Ore: // a faceted lump
                ci.DrawColoredPolygon(Poly(c, r, 6, 0.4f), colour);
                ci.DrawColoredPolygon(Poly(c + new Vector2(-r * 0.15f, -r * 0.15f), r * 0.45f, 6, 0.4f), colour.Lightened(0.35f));
                break;
            case Logic.ItemKind.Part: // a gear-ish square
                ci.DrawRect(new Rect2(c - Vector2.One * r * 0.75f, Vector2.One * r * 1.5f), colour);
                ci.DrawCircle(c, r * 0.35f, C.Glass);
                for (int i = 0; i < 4; i++)
                {
                    Vector2 d = Vector2.Right.Rotated(i * Mathf.Pi / 2) * r * 0.95f;
                    ci.DrawRect(new Rect2(c + d - Vector2.One * r * 0.18f, Vector2.One * r * 0.36f), colour);
                }
                break;
            case Logic.ItemKind.Consumable: // a capsule
                ci.DrawCircle(c + new Vector2(0, -r * 0.45f), r * 0.45f, colour);
                ci.DrawCircle(c + new Vector2(0, r * 0.45f), r * 0.45f, colour.Darkened(0.25f));
                ci.DrawRect(new Rect2(c.X - r * 0.45f, c.Y - r * 0.45f, r * 0.9f, r * 0.45f), colour);
                ci.DrawRect(new Rect2(c.X - r * 0.45f, c.Y, r * 0.9f, r * 0.45f), colour.Darkened(0.25f));
                break;
            case Logic.ItemKind.Armor: // a shield
                ci.DrawColoredPolygon(new[] { c + new Vector2(-r * 0.8f, -r * 0.8f), c + new Vector2(r * 0.8f, -r * 0.8f), c + new Vector2(r * 0.8f, r * 0.1f), c + new Vector2(0, r), c + new Vector2(-r * 0.8f, r * 0.1f) }, colour);
                ci.DrawRect(new Rect2(c.X - r * 0.12f, c.Y - r * 0.6f, r * 0.24f, r * 1.2f), C.Glass);
                break;
            case Logic.ItemKind.Booster: // a chevron
                ci.DrawColoredPolygon(new[] { c + new Vector2(-r * 0.8f, r * 0.2f), c + new Vector2(0, -r * 0.8f), c + new Vector2(r * 0.8f, r * 0.2f), c + new Vector2(r * 0.8f, r * 0.75f), c + new Vector2(0, -r * 0.2f), c + new Vector2(-r * 0.8f, r * 0.75f) }, colour);
                break;
            default: // a tool: a wrench-ish bar
                ci.DrawSetTransform(c, -0.7f);
                ci.DrawRect(new Rect2(-r * 0.18f, -r * 0.9f, r * 0.36f, r * 1.8f), colour);
                ci.DrawCircle(new Vector2(0, -r * 0.75f), r * 0.38f, colour);
                ci.DrawCircle(new Vector2(0, -r * 0.85f), r * 0.16f, C.Glass);
                ci.DrawSetTransform(Vector2.Zero);
                break;
        }
    }

    static Vector2[] Poly(Vector2 c, float r, int sides, float rotation)
    {
        var points = new Vector2[sides];
        for (int i = 0; i < sides; i++) points[i] = c + Vector2.Right.Rotated(rotation + i * Mathf.Tau / sides) * r;
        return points;
    }

    /// <summary>A colour from 0xRRGGBB (Godot's uint constructor wants 0xRRGGBBAA).</summary>
    public static Color Rgb(uint rgb) => new((rgb << 8) | 0xFF);

    /// <summary>Formats seconds as MM:SS.</summary>
    public static string Clock(double seconds)
    {
        int s = Math.Max(0, (int)Math.Ceiling(seconds));
        return $"{s / 60:00}:{s % 60:00}";
    }
}
