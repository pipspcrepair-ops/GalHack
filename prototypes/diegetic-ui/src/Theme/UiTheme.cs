using System.Collections.Generic;
using Godot;

namespace DiegeticUi.Theme;

/// <summary>
/// A faction's colours, as they appear on its screens. Screens are dark glass with bold
/// blocks of colour on them, like the paint on its ships: swap the palette and every screen,
/// the visor and the hologram follow.
/// </summary>
public sealed record Palette(
    string Faction,
    Color Glass,     // the screen's own background
    Color Panel,     // raised blocks on the glass
    Color Rule,      // thin lines, empty segments
    Color Text,      // cream phosphor
    Color Dim,       // secondary labels
    Color Accent,    // the faction's signature block colour
    Color Accent2,   // its second colour
    Color Good,      // nominal
    Color Warn,      // look at this
    Color Danger,    // act now
    Color Holo)      // hologram tint
{
    /// <summary>The player's starting faction: cream hulls, orange and yellow livery, teal trim.</summary>
    public static readonly Palette Helios = new(
        Faction: "HELIOS",
        Glass: new Color("0b111c"),
        Panel: new Color("172238"),
        Rule: new Color("2a3a58"),
        Text: new Color("f6ead0"),
        Dim: new Color("8d97ab"),
        Accent: new Color("ff6a13"),
        Accent2: new Color("ffb400"),
        Good: new Color("1fd6c4"),
        Warn: new Color("ffb400"),
        Danger: new Color("ff3344"),
        Holo: new Color("5fe8ff"));
}

/// <summary>
/// The shared look of every diegetic screen: fonts, palette, sizes. Other prototypes can use
/// it as is.
///
/// Fonts are read straight from the .ttf bytes rather than through Godot's importer, so a
/// fresh checkout runs (and the headless tests pass) before the editor has ever imported
/// anything.
/// </summary>
public static class UiTheme
{
    public static Palette Colors { get; set; } = Palette.Helios;

    /// <summary>
    /// Where the fonts folder is. diegetic-ui's own res://fonts by default; a project that
    /// compiles this theme in from here and reads the fonts in place points it at that folder
    /// (an absolute path works) before anything draws.
    /// </summary>
    public static string FontRoot { get; set; } = "res://fonts";

    /// <summary>
    /// Wide display face for titles: Michroma, in the spirit of the extended grotesques on
    /// 1970s science-fiction paperbacks.
    /// </summary>
    public static Font Display => _display ??= Emboldened(LoadFont(FontPath("Michroma/Michroma-Regular.ttf")));

    /// <summary>Labels and body text: Oxanium at bold weight.</summary>
    public static Font Label => _label ??= Weighted(OxaniumFile, 700);

    /// <summary>Oxanium at its heaviest, for dark text knocked out of a bright block (thin strokes would fill in).</summary>
    public static Font Heavy => _heavy ??= Weighted(OxaniumFile, 800);

    /// <summary>Lighter Oxanium, for longer reading like the codex.</summary>
    public static Font Body => _body ??= Weighted(OxaniumFile, 500);

    /// <summary>Numbers: Space Mono Bold, so digits don't jitter as values change.</summary>
    public static Font Mono => _mono ??= LoadFont(FontPath("SpaceMono/SpaceMono-Bold.ttf"));

    /// <summary>A signed-distance-field copy of the display face for 3D labels, sharp at any distance.</summary>
    public static Font Display3D => _display3D ??= Emboldened(LoadFont(FontPath("Michroma/Michroma-Regular.ttf"), msdf: true));
    public static Font Label3D => _label3D ??= Weighted(LoadFont(FontPath("Oxanium/Oxanium-Variable.ttf"), msdf: true), 700);

    // Type sizes for a screen roughly 1280 px wide seen from arm's length. Large on purpose.
    public const int SizeTitle = 40, SizeHeading = 30, SizeLabel = 26, SizeSmall = 20, SizeBig = 56;

    static Font _display, _label, _heavy, _body, _mono, _display3D, _label3D;
    static FontFile _oxanium;
    static FontFile OxaniumFile => _oxanium ??= LoadFont(FontPath("Oxanium/Oxanium-Variable.ttf"));

    static string FontPath(string file) => FontRoot.TrimEnd('/', '\\') + "/" + file;

    public static FontFile LoadFont(string path, bool msdf = false)
    {
        var font = new FontFile
        {
            Data = FileAccess.GetFileAsBytes(path),
            Antialiasing = TextServer.FontAntialiasing.Gray,
            Hinting = TextServer.Hinting.Light,
            GenerateMipmaps = msdf,
            MultichannelSignedDistanceField = msdf,
        };
        if (msdf)
        {
            font.MsdfPixelRange = 12;
            font.MsdfSize = 64;
        }
        return font;
    }

    /// <summary>Michroma has one (light) weight; chunky screens want it heavier.</summary>
    static FontVariation Emboldened(Font baseFont) => new() { BaseFont = baseFont, VariationEmbolden = 1.3f };

    static FontVariation Weighted(Font baseFont, int weight)
    {
        var variation = new FontVariation { BaseFont = baseFont };
        long wght = TextServerManager.GetPrimaryInterface().NameToTag("wght");
        variation.VariationOpentype = new Godot.Collections.Dictionary { { wght, weight } };
        return variation;
    }

    /// <summary>
    /// The Godot Theme every screen gives a panel that brings none, and the one a full-screen
    /// menu should use too, so a panel looks the same in both. It carries the fonts and dresses
    /// the stock controls in the palette: buttons are dark blocks with a colour tab that light
    /// under the cursor and go solid when pressed or toggled on; sliders are segmented-looking
    /// tracks with a cream grab handle. Type variations for labels: Title, Heading, Caption, Readout.
    /// </summary>
    public static Godot.Theme GodotTheme => _theme ??= BuildTheme();
    static Godot.Theme _theme;

    static Godot.Theme BuildTheme()
    {
        Palette c = Colors;
        var t = new Godot.Theme { DefaultFont = Label, DefaultFontSize = SizeLabel };

        t.SetColor("font_color", "Label", c.Text);
        Variation(t, "Title", "Label", Display, SizeTitle, c.Accent);
        Variation(t, "Heading", "Label", Label, SizeHeading, c.Text);
        Variation(t, "Caption", "Label", Label, SizeSmall, c.Dim);
        Variation(t, "Readout", "Label", Mono, SizeLabel, c.Text);

        t.SetStylebox("normal", "Button", Box(c.Panel, tab: c.Accent));
        t.SetStylebox("hover", "Button", Box(c.Panel.Lightened(0.15f), tab: c.Accent2, frame: 3));
        t.SetStylebox("pressed", "Button", Box(c.Good, tab: c.Good.Darkened(0.35f)));
        t.SetStylebox("hover_pressed", "Button", Box(c.Good.Lightened(0.12f), tab: c.Accent2, frame: 3));
        t.SetStylebox("disabled", "Button", Box(c.Glass, tab: c.Rule));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", c.Text);
        t.SetColor("font_hover_color", "Button", c.Text);
        t.SetColor("font_focus_color", "Button", c.Text);
        t.SetColor("font_pressed_color", "Button", c.Glass);
        t.SetColor("font_hover_pressed_color", "Button", c.Glass);
        t.SetColor("font_disabled_color", "Button", c.Dim);

        t.SetStylebox("slider", "HSlider", Box(c.Rule, margin: 8));
        t.SetStylebox("grabber_area", "HSlider", Box(c.Accent2, margin: 8));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(c.Accent2.Lightened(0.2f), margin: 8));
        t.SetIcon("grabber", "HSlider", Handle(c.Text));
        t.SetIcon("grabber_highlight", "HSlider", Handle(c.Accent2.Lightened(0.4f)));
        t.SetIcon("grabber_disabled", "HSlider", Handle(c.Dim));

        t.SetStylebox("background", "ProgressBar", Box(c.Rule));
        t.SetStylebox("fill", "ProgressBar", Box(c.Good));
        t.SetStylebox("panel", "PanelContainer", Box(c.Glass, margin: 0));
        t.SetConstant("separation", "VBoxContainer", 12);
        t.SetConstant("separation", "HBoxContainer", 16);
        return t;
    }

    static void Variation(Godot.Theme t, string name, string baseType, Font font, int size, Color colour)
    {
        t.SetTypeVariation(name, baseType);
        t.SetFont("font", name, font);
        t.SetFontSize("font_size", name, size);
        t.SetColor("font_color", name, colour);
    }

    /// <summary>A flat block with a thick colour tab down its left edge, and optionally a frame in that colour.</summary>
    static StyleBoxFlat Box(Color fill, Color? tab = null, int frame = 0, int margin = 12)
    {
        var box = new StyleBoxFlat
        {
            BgColor = fill,
            ContentMarginLeft = margin + (tab != null ? 14 : 0), ContentMarginRight = margin,
            ContentMarginTop = margin * 0.6f, ContentMarginBottom = margin * 0.6f,
        };
        if (tab is { } edge)
        {
            box.BorderColor = edge;
            box.BorderWidthLeft = 10;
            box.BorderWidthTop = box.BorderWidthRight = box.BorderWidthBottom = frame;
        }
        return box;
    }

    /// <summary>A slider's grab handle: a fat upright block.</summary>
    static ImageTexture Handle(Color colour)
    {
        Image image = Image.CreateEmpty(26, 48, false, Image.Format.Rgba8);
        image.Fill(colour);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Formats a number with thin grouping, like 1 240.</summary>
    public static string Credits(int credits) => credits.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

    /// <summary>A colour for a 0..1 load: teal when fine, amber when high, red when over.</summary>
    public static Color LoadColor(double fraction) =>
        fraction > 1.0001 ? Colors.Danger : fraction >= 0.85 ? Colors.Warn : Colors.Good;
}
