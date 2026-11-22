using System;

namespace Planets.Look;

/// <summary>
/// Light as a spectrum: sixteen bands across the visible, 400 to 700 nm. A world's palette works on
/// these, light times what the air, water, leaves and minerals pass or throw back band by band, and
/// only at the end turns them into colours as the eye sees them (CIE 1931 through the analytic fit of
/// Wyman, Sloan and Shirley 2013, then sRGB). Engine-free, so the palette is testable headless.
/// </summary>
public readonly struct Spectrum
{
    public const int Bands = 16;
    readonly float[] _v;

    Spectrum(float[] v) { _v = v; }

    /// <summary>A band's middle, nm.</summary>
    public static float Nm(int i) => 400f + 20f * i;

    public float this[int i] => _v[i];

    public static Spectrum Of(Func<float, float> f)
    {
        var v = new float[Bands];
        for (int i = 0; i < Bands; i++) v[i] = f(Nm(i));
        return new Spectrum(v);
    }

    public static Spectrum Flat(float k) => Of(_ => k);

    public Spectrum Map(Func<float, float> f)
    {
        var v = new float[Bands];
        for (int i = 0; i < Bands; i++) v[i] = f(_v[i]);
        return new Spectrum(v);
    }

    static Spectrum Zip(Spectrum a, Spectrum b, Func<float, float, float> f)
    {
        var v = new float[Bands];
        for (int i = 0; i < Bands; i++) v[i] = f(a._v[i], b._v[i]);
        return new Spectrum(v);
    }

    public static Spectrum operator *(Spectrum a, Spectrum b) => Zip(a, b, (x, y) => x * y);
    public static Spectrum operator /(Spectrum a, Spectrum b) => Zip(a, b, (x, y) => y > 0f ? x / y : 0f);
    public static Spectrum operator +(Spectrum a, Spectrum b) => Zip(a, b, (x, y) => x + y);
    public static Spectrum operator -(Spectrum a, Spectrum b) => Zip(a, b, (x, y) => x - y);
    public static Spectrum operator *(Spectrum a, float k) => a.Map(x => x * k);
    public static Spectrum Exp(Spectrum a) => a.Map(MathF.Exp);
    public static Spectrum Lerp(Spectrum a, Spectrum b, float t) => Zip(a, b, (x, y) => x + (y - x) * t);

    /// <summary>The band where it peaks.</summary>
    public int Peak
    {
        get
        {
            int best = 0;
            for (int i = 1; i < Bands; i++) if (_v[i] > _v[best]) best = i;
            return best;
        }
    }

    /// <summary>A soft band, 1 at <paramref name="centre"/> nm falling off over <paramref name="width"/> nm: a pigment's or a gas's absorption.</summary>
    public static Spectrum Band(float centre, float width) => Of(l => MathF.Exp(-0.5f * (l - centre) * (l - centre) / (width * width)));

    /// <summary>A soft step, 1 below <paramref name="edge"/> nm and 0 above: an iron oxide's absorption edge.</summary>
    public static Spectrum Below(float edge, float width) => Of(l => 1f / (1f + MathF.Exp((l - edge) / width)));

    /// <summary>Planck's law: a body's light at <paramref name="kelvin"/> (relative).</summary>
    public static Spectrum Blackbody(float kelvin) => Of(l => MathF.Pow(l / 550f, -5f) / (MathF.Exp(1.4388e7f / (l * kelvin)) - 1f));

    /// <summary>The white sRGB is balanced to (D65's correlated temperature, 6504 K).</summary>
    static readonly Spectrum White = Blackbody(6504f);

    static float Lobe(float l, float mu, float s1, float s2)
    {
        float t = (l - mu) / (l < mu ? s1 : s2);
        return MathF.Exp(-0.5f * t * t);
    }

    /// <summary>Linear sRGB of a light, against a white of luminance <paramref name="whiteY"/>.</summary>
    static (float R, float G, float B) Linear(Spectrum s, float whiteY)
    {
        float x = 0f, y = 0f, z = 0f;
        for (int i = 0; i < Bands; i++)
        {
            float l = Nm(i), v = s._v[i];
            x += v * (1.056f * Lobe(l, 599.8f, 37.9f, 31.0f) + 0.362f * Lobe(l, 442.0f, 16.0f, 26.7f) - 0.065f * Lobe(l, 501.1f, 20.4f, 26.2f));
            y += v * (0.821f * Lobe(l, 568.8f, 46.9f, 40.5f) + 0.286f * Lobe(l, 530.9f, 16.3f, 31.1f));
            z += v * (1.217f * Lobe(l, 437.0f, 11.8f, 36.0f) + 0.681f * Lobe(l, 459.0f, 26.0f, 13.8f));
        }
        x /= whiteY; y /= whiteY; z /= whiteY;
        return (3.2406f * x - 1.5372f * y - 0.4986f * z, -0.9689f * x + 1.8758f * y + 0.0415f * z, 0.0557f * x - 0.2040f * y + 1.0570f * z);
    }

    static float Luminance(Spectrum s)
    {
        float y = 0f;
        for (int i = 0; i < Bands; i++) y += s._v[i] * (0.821f * Lobe(Nm(i), 568.8f, 46.9f, 40.5f) + 0.286f * Lobe(Nm(i), 530.9f, 16.3f, 31.1f));
        return y;
    }

    // In this order: a static field's initialiser runs where it stands.
    static readonly float WhiteY = Luminance(White);

    /// <summary>Its luminance against white light's (CIE Y).</summary>
    public float Y => Luminance(this) / WhiteY;
    static readonly (float R, float G, float B) WhiteRgb = Linear(White, WhiteY);

    /// <summary>
    /// The colour of a light, relative to white light of luminance 1 (so a sky's brightness carries over),
    /// balanced so that 6504 K light is exactly neutral, and sRGB encoded. A screen shows daylight scenes a
    /// fiftieth as bright as they are, and colours look less colourful the dimmer they are (the Hunt effect):
    /// their chroma is raised by <see cref="Hunt"/> so the screen shows them as colourful as the eye saw them.
    /// </summary>
    public Rgb Light(float scale = 1f)
    {
        var (r, g, b) = Linear(this, WhiteY / scale);
        (r, g, b) = (r / WhiteRgb.R, g / WhiteRgb.G, b / WhiteRgb.B);
        float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        return Encode(y + (r - y) * Hunt, y + (g - y) * Hunt, y + (b - y) * Hunt);
    }

    /// <summary>
    /// How much more colourful daylight looks than its screen copy: CIECAM02's colourfulness goes as the fourth root
    /// of its luminance-level factor F_L, about 1.35 from a sunlit scene's adapting luminance to a screen's.
    /// </summary>
    const float Hunt = 1.35f;

    /// <summary>The colour of a light at its brightest channel 1: its hue alone (a star, a glow).</summary>
    public Rgb Hue()
    {
        var (r, g, b) = Linear(this, WhiteY);
        (r, g, b) = (MathF.Max(r / WhiteRgb.R, 0f), MathF.Max(g / WhiteRgb.G, 0f), MathF.Max(b / WhiteRgb.B, 0f));
        float m = MathF.Max(r, MathF.Max(g, b));
        return m > 0f ? Encode(r / m, g / m, b / m) : default;
    }

    /// <summary>The colour of a surface that throws back this share of the light, band by band, seen under white light: an albedo.</summary>
    public Rgb Reflectance() => (this * White).Light();

    static Rgb Encode(float r, float g, float b)
    {
        static float E(float v)
        {
            v = Math.Clamp(v, 0f, 1f);
            return v <= 0.0031308f ? 12.92f * v : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;
        }
        return new Rgb(E(r), E(g), E(b));
    }
}
