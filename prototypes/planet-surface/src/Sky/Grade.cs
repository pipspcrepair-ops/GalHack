using Godot;
using Planets.Gen;
using Planets.Look;

namespace PlanetSurface.Skies;

/// <summary>
/// The picture's last colour step, for the art direction (owner, 23 Nov: exaggerated, saturated colour like 1970s
/// science fiction art): the tonemapper, and after it a lookup table in the engine's own tonemap pass (its colour
/// correction, so no extra full-screen pass) that raises chroma in Oklab at each colour's own hue. Near black it
/// barely acts (no coloured noise in the shadows); a colour pushed past what the screen can show gives up chroma
/// at its hue and comes toward middle lightness rather than going white, so bright skies and lit cloud keep colour.
/// Nothing here greys anything: overcast, dust and night change the colours the world gives it, never drain them.
/// </summary>
public static class Grade
{
    /// <summary>How much chroma the grade adds at its fullest, Oklab chroma times this.</summary>
    public const float Boost = 1.35f;

    /// <summary>Lookup table cells a side: input in sRGB, each cell holding the colour for its own centre.</summary>
    const int Size = 33;

    /// <summary>Tests and shots: the tonemapper to use instead of the game's (null: the game's).</summary>
    public static Godot.Environment.ToneMapper? Force;

    static ImageTexture3D _lut;

    /// <summary>The game's tonemapper and the grade on an environment (its adjustments turned on).</summary>
    public static void Apply(Godot.Environment env)
    {
        env.TonemapMode = Force ?? Godot.Environment.ToneMapper.Filmic;
        env.TonemapWhite = 6f;
        env.TonemapExposure = 1f;
        env.AdjustmentEnabled = true;
        env.AdjustmentColorCorrection = _lut ??= Build();
    }

    /// <summary>What the grade does to one sRGB colour (0..1): the table's contents, testable without an engine.</summary>
    public static Rgb Of(Rgb c)
    {
        Oklab o = Oklab.Of(c);
        // Full strength through the middle tones, easing off into black and the brightest whites.
        float k = 1f + (Boost - 1f) * Mathx.Smooth(0.03f, 0.2f, o.L) * (1f - 0.5f * Mathx.Smooth(0.85f, 1f, o.L));
        return new Oklab(o.L, o.A * k, o.B * k).ToRgb();
    }

    static ImageTexture3D Build()
    {
        var slices = new Godot.Collections.Array<Image>();
        var data = new byte[Size * Size * 4];
        for (int b = 0; b < Size; b++)
        {
            for (int g = 0, i = 0; g < Size; g++)
                for (int r = 0; r < Size; r++, i += 4)
                {
                    Rgb c = Of(new Rgb((r + 0.5f) / Size, (g + 0.5f) / Size, (b + 0.5f) / Size));
                    (data[i], data[i + 1], data[i + 2], data[i + 3]) = (Rgb.Byte(c.R), Rgb.Byte(c.G), Rgb.Byte(c.B), 255);
                }
            slices.Add(Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, data));
        }
        var lut = new ImageTexture3D();
        lut.Create(Image.Format.Rgba8, Size, Size, Size, false, slices);
        return lut;
    }
}
