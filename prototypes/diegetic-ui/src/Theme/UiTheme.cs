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
    /// Wide display face for titles: Michroma, in the spirit of the extended grotesques on
    /// 1970s science-fiction paperbacks.
    /// </summary>
    public static Font Display => _display ??= Emboldened(LoadFont("res://fonts/Michroma/Michroma-Regular.ttf"));

    /// <summary>Labels and body text: Oxanium at bold weight.</summary>
    public static Font Label => _label ??= Weighted(OxaniumFile, 700);

    /// <summary>Oxanium at its heaviest, for dark text knocked out of a bright block (thin strokes would fill in).</summary>
    public static Font Heavy => _heavy ??= Weighted(OxaniumFile, 800);

    /// <summary>Lighter Oxanium, for longer reading like the codex.</summary>
    public static Font Body => _body ??= Weighted(OxaniumFile, 500);

    /// <summary>Numbers: Space Mono Bold, so digits don't jitter as values change.</summary>
    public static Font Mono => _mono ??= LoadFont("res://fonts/SpaceMono/SpaceMono-Bold.ttf");

    /// <summary>A signed-distance-field copy of the display face for 3D labels, sharp at any distance.</summary>
    public static Font Display3D => _display3D ??= Emboldened(LoadFont("res://fonts/Michroma/Michroma-Regular.ttf", msdf: true));
    public static Font Label3D => _label3D ??= Weighted(LoadFont("res://fonts/Oxanium/Oxanium-Variable.ttf", msdf: true), 700);

    // Type sizes for a screen roughly 1280 px wide seen from arm's length. Large on purpose.
    public const int SizeTitle = 40, SizeHeading = 30, SizeLabel = 26, SizeSmall = 20, SizeBig = 56;

    static Font _display, _label, _heavy, _body, _mono, _display3D, _label3D;
    static FontFile _oxanium;
    static FontFile OxaniumFile => _oxanium ??= LoadFont("res://fonts/Oxanium/Oxanium-Variable.ttf");

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

    /// <summary>A Godot Theme carrying the fonts, for any stock Control placed on a screen.</summary>
    public static Godot.Theme GodotTheme => _theme ??= BuildTheme();
    static Godot.Theme _theme;

    static Godot.Theme BuildTheme()
    {
        var theme = new Godot.Theme { DefaultFont = Label, DefaultFontSize = SizeLabel };
        theme.SetColor("font_color", "Label", Colors.Text);
        return theme;
    }

    /// <summary>Formats a number with thin grouping, like 1 240.</summary>
    public static string Credits(int credits) => credits.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

    /// <summary>A colour for a 0..1 load: teal when fine, amber when high, red when over.</summary>
    public static Color LoadColor(double fraction) =>
        fraction > 1.0001 ? Colors.Danger : fraction >= 0.85 ? Colors.Warn : Colors.Good;
}
