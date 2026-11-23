using System;
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
    /// <summary>How the scene's light comes down to the screen's: one of the engine's tonemappers, or <see cref="Shoulder"/>.</summary>
    public enum Curve { Filmic, Agx, Aces, Linear, Shoulder }

    /// <summary>
    /// The game's curve: the engine's linear tonemapper at half exposure (the Mobile renderer's scene light runs to 2), and
    /// the lookup table brings it back with a shoulder of its own on Oklab lightness, chroma kept in step, so bright colours
    /// roll off at their own hue instead of each channel flattening on its own (per-channel curves wash bright colours out).
    /// </summary>
    public const Curve Game = Curve.Shoulder;

    /// <summary>How much chroma the grade adds at its fullest, Oklab chroma times this.</summary>
    public const float Boost = 1.35f;

    /// <summary>Lookup table cells a side: input in sRGB, each cell holding the colour for its own centre.</summary>
    const int Size = 33;

    /// <summary>Tests and shots: the curve to use instead of the game's (null: the game's).</summary>
    public static Curve? Force;

    static ImageTexture3D _plain, _shoulder;

    /// <summary>The game's tonemapper and the grade on an environment (its adjustments turned on).</summary>
    public static void Apply(Godot.Environment env)
    {
        Curve curve = Force ?? Game;
        env.TonemapMode = curve switch
        {
            Curve.Filmic => Godot.Environment.ToneMapper.Filmic, Curve.Agx => Godot.Environment.ToneMapper.Agx,
            Curve.Aces => Godot.Environment.ToneMapper.Aces, _ => Godot.Environment.ToneMapper.Linear,
        };
        env.TonemapWhite = 6f;
        env.TonemapExposure = curve == Curve.Shoulder ? 0.5f : 1f;
        env.AdjustmentEnabled = true;
        env.AdjustmentColorCorrection = curve == Curve.Shoulder ? _shoulder ??= Build(true) : _plain ??= Build(false);
    }

    /// <summary>What the grade does to one sRGB colour (0..1) the tonemapper gave it: the table's contents, testable without an engine.</summary>
    public static Rgb Of(Rgb c, bool shoulder = false)
    {
        Oklab o = Oklab.Of(c);
        if (shoulder)
        {
            // Back to the scene's light (half exposure), then its lightness rolled off above Knee toward 1, chroma with it.
            var (r, g, b) = (2f * Oklab.Linear(c.R), 2f * Oklab.Linear(c.G), 2f * Oklab.Linear(c.B));
            o = Oklab.FromLinear(r, g, b);
            float l = o.L <= Knee ? o.L : Knee + (1f - Knee) * (1f - MathF.Exp(-(o.L - Knee) / (1f - Knee)));
            float s = o.L > 1e-5f ? l / o.L : 1f;
            o = new Oklab(l, o.A * s, o.B * s);
        }
        // Full strength through the middle tones, easing off into black and the brightest whites. A colour already at the
        // screen's edge has nothing more to give, and keeps what it has.
        float k = 1f + (Boost - 1f) * Mathx.Smooth(0.03f, 0.2f, o.L) * (1f - 0.5f * Mathx.Smooth(0.85f, 1f, o.L));
        Oklab bold = new Oklab(o.L, o.A * k, o.B * k).Fit();
        return !shoulder && bold.Chroma < o.Chroma ? c : bold.ToRgb();
    }

    /// <summary>Where the shoulder starts, Oklab lightness: below it the scene's light shows as it is.</summary>
    const float Knee = 0.75f;

    static ImageTexture3D Build(bool shoulder)
    {
        var slices = new Godot.Collections.Array<Image>();
        var data = new byte[Size * Size * 4];
        for (int b = 0; b < Size; b++)
        {
            for (int g = 0, i = 0; g < Size; g++)
                for (int r = 0; r < Size; r++, i += 4)
                {
                    Rgb c = Of(new Rgb((r + 0.5f) / Size, (g + 0.5f) / Size, (b + 0.5f) / Size), shoulder);
                    (data[i], data[i + 1], data[i + 2], data[i + 3]) = (Rgb.Byte(c.R), Rgb.Byte(c.G), Rgb.Byte(c.B), 255);
                }
            slices.Add(Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, data));
        }
        var lut = new ImageTexture3D();
        lut.Create(Image.Format.Rgba8, Size, Size, Size, false, slices);
        return lut;
    }
}
