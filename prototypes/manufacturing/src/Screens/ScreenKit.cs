using System;
using System.IO;
using GalHack.Items;
using Godot;
using Manufacturing.Game;

namespace Manufacturing.Screens;

/// <summary>
/// The diegetic screen look, as prototypes/diegetic-ui defines it: dark glass, bold colour blocks
/// in the Helios palette, a header like a 70s paperback spine, segmented bars. The fonts (OFL,
/// credited in CREDITS.md) and the glass shader are read from diegetic-ui's folder where they
/// live, so nothing is copied; if they can't be found the screens fall back to a system font.
/// </summary>
public static class Kit
{
    // diegetic-ui's Palette.Helios.
    public static readonly Color Glass = new("0b111c"), Panel = new("172238"), Rule = new("2a3a58"), Cream = new("f6ead0"),
        Dim = new("8d97ab"), Accent = new("ff6a13"), Accent2 = new("ffb400"), Good = new("1fd6c4"), Warn = new("ffb400"), Danger = new("ff3344");

    public const int SizeTitle = 40, SizeHeading = 30, SizeLabel = 26, SizeSmall = 20, SizeTiny = 16;

    static Font _display, _label, _mono;
    public static Font Display => _display ??= Load("Michroma/Michroma-Regular.ttf", embolden: 1.3f);
    public static Font Label => _label ??= Load("Oxanium/Oxanium-Variable.ttf", weight: 700);
    public static Font Mono => _mono ??= Load("SpaceMono/SpaceMono-Bold.ttf");

    public static Color Of(Rarity r) => new((Rarities.Color(r) << 8) | 0xFF);

    static Font Load(string file, float embolden = 0, int weight = 0)
    {
        string path = Path.Combine(DataPaths.DiegeticUi, "fonts", file);
        if (!File.Exists(path)) return new SystemFont { FontNames = new[] { "Bahnschrift", "Segoe UI", "Arial" } };
        var font = new FontFile { Data = File.ReadAllBytes(path), Antialiasing = TextServer.FontAntialiasing.Gray, Hinting = TextServer.Hinting.Light };
        if (embolden > 0) return new FontVariation { BaseFont = font, VariationEmbolden = embolden };
        if (weight > 0)
        {
            long wght = TextServerManager.GetPrimaryInterface().NameToTag("wght");
            return new FontVariation { BaseFont = font, VariationOpentype = new Godot.Collections.Dictionary { { wght, weight } } };
        }
        return font;
    }

    /// <summary>diegetic-ui's glass shader (scanlines, glow, vignette), read in place; null if it isn't there.</summary>
    public static Shader SurfaceShader()
    {
        string path = Path.Combine(DataPaths.DiegeticUi, "shaders", "screen_surface.gdshader");
        return File.Exists(path) ? new Shader { Code = File.ReadAllText(path) } : null;
    }

    // ---- Drawing --------------------------------------------------------------------------

    public static void Text(this CanvasItem ci, Font font, int size, Vector2 topLeft, string text, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left, float width = -1) =>
        ci.DrawString(font, topLeft + new Vector2(0, font.GetAscent(size)), text, align, width, size, color);

    public static void TextIn(this CanvasItem ci, Font font, int size, Rect2 rect, string text, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left, float padX = 0)
    {
        float h = font.GetAscent(size) + font.GetDescent(size);
        ci.Text(font, size, new Vector2(rect.Position.X + padX, rect.Position.Y + (rect.Size.Y - h) / 2), text, color, align, rect.Size.X - padX * 2);
    }

    public static float Width(Font font, int size, string text) => font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    /// <summary>Cuts text to fit a width, with an ellipsis.</summary>
    public static string Fit(Font font, int size, string text, float width)
    {
        if (Width(font, size, text) <= width) return text;
        while (text.Length > 1 && Width(font, size, text + "...") > width) text = text[..^1];
        return text.TrimEnd() + "...";
    }

    public static void Block(this CanvasItem ci, Rect2 rect, Color color) => ci.DrawRect(rect, color);

    public static void Frame(this CanvasItem ci, Rect2 rect, Color color, float width = 3) => ci.DrawRect(rect.Grow(-width / 2), color, false, width);

    /// <summary>The title band: a solid block with the title knocked out, three racing stripes, a tag block at the right.</summary>
    public static void Header(this CanvasItem ci, float width, string title, string tag, Color? colour = null, float height = 84)
    {
        Color main = colour ?? Accent;
        ci.Block(new Rect2(0, 0, width, height), main);
        ci.TextIn(Display, SizeTitle, new Rect2(0, 0, width, height), title, Glass, padX: 30);
        float tagWidth = Mathf.Max(170, Width(Display, 30, tag) + 48), x = width - tagWidth;
        ci.Block(new Rect2(x, 0, tagWidth, height), Cream);
        ci.TextIn(Display, 30, new Rect2(x, 0, tagWidth, height), tag, Glass, HorizontalAlignment.Center);
        Color[] stripes = { Accent2, Danger, Good };
        for (int i = 0; i < stripes.Length; i++) ci.Block(new Rect2(x - 22 - i * 22, 0, 12, height), stripes[stripes.Length - 1 - i]);
        ci.Block(new Rect2(0, height, width, 4), Glass);
    }

    public const float StripTop = 88, StripHeight = 50;

    /// <summary>The status strip under the header, with a right-hand tag (the game mode).</summary>
    public static void Strip(this CanvasItem ci, float width, string text, Color colour, string right = null, Color? rightColour = null)
    {
        var strip = new Rect2(0, StripTop, width, StripHeight);
        ci.Block(strip, Panel);
        ci.Block(new Rect2(0, StripTop, 14, StripHeight), colour);
        ci.TextIn(Label, SizeLabel, strip, Fit(Label, SizeLabel, text, width - 340), colour, padX: 36);
        if (right != null) ci.TextIn(Display, SizeSmall, strip, right, rightColour ?? Dim, HorizontalAlignment.Right, 28);
    }

    public static void Caption(this CanvasItem ci, Vector2 at, string text, Color? colour = null, int size = SizeSmall)
    {
        Color c = colour ?? Dim;
        ci.Text(Label, size, at, text, c);
        ci.Block(new Rect2(at.X, at.Y + size + 6, 36, 4), c);
    }

    /// <summary>A segmented bar; past the end, a red block says it is over.</summary>
    public static void Segments(this CanvasItem ci, Rect2 rect, double value, double max, Color fill, int count = 24, float gap = 5)
    {
        float w = (rect.Size.X - gap * (count - 1)) / count;
        double per = max / count;
        for (int i = 0; i < count; i++)
        {
            var seg = new Rect2(rect.Position.X + i * (w + gap), rect.Position.Y, w, rect.Size.Y);
            ci.Block(seg, value > i * per + per * 0.25 ? fill : Rule);
        }
        if (value > max * 1.0001) ci.Block(new Rect2(rect.End.X + 8, rect.Position.Y, 14, rect.Size.Y), Danger);
    }

    /// <summary>A round lamp: lit, or a dark socket.</summary>
    public static void Lamp(this CanvasItem ci, Vector2 centre, float radius, Color colour, bool lit)
    {
        ci.DrawCircle(centre, radius + 3, Glass);
        ci.DrawCircle(centre, radius, lit ? colour : Rule);
        if (lit) ci.DrawCircle(centre + new Vector2(-radius * 0.3f, -radius * 0.3f), radius * 0.28f, colour.Lightened(0.6f));
    }

    /// <summary>A row in a list: panel block, coloured edge, highlight frame when selected.</summary>
    public static void Row(this CanvasItem ci, Rect2 r, Color edge, bool selected, bool dim = false)
    {
        ci.Block(r, selected ? Panel.Lightened(0.12f) : Panel);
        ci.Block(new Rect2(r.Position, new Vector2(8, r.Size.Y)), dim ? edge.Darkened(0.5f) : edge);
        if (selected) ci.Frame(r, Accent2, 3);
    }

    /// <summary>Key hints along the bottom edge, in small dim type.</summary>
    public static void Hints(this CanvasItem ci, float width, float height, string hints) =>
        ci.TextIn(Label, SizeTiny, new Rect2(0, height - 30, width, 26), hints, Dim, padX: 24);

    public static string Clock(double seconds)
    {
        int s = Math.Max(0, (int)Math.Ceiling(seconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
    }

    /// <summary>"1.5" or "2": a fraction readable on a screen.</summary>
    public static string Amount(double v) => Math.Abs(v - Math.Round(v)) < 1e-6 ? $"{Math.Round(v):0}" : $"{v:0.##}";
}
