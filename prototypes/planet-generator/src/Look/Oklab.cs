using System;

namespace Planets.Look;

/// <summary>
/// Colours in Oklab (Ottosson 2020, "A perceptual color space for image processing"): lightness L 0..1, and a, b,
/// whose length is chroma and whose angle is hue. Even steps in it look even, and changing chroma at a fixed hue
/// never drifts the hue, so a colour can be made bolder or quieter without turning into another one. The palette's
/// art direction (<see cref="WorldPalette"/>) and the screen's grade work in it.
/// </summary>
public readonly record struct Oklab(float L, float A, float B)
{
    public float Chroma => MathF.Sqrt(A * A + B * B);

    /// <summary>Hue angle, radians.</summary>
    public float Hue => MathF.Atan2(B, A);

    public static Oklab Lch(float l, float c, float h) => new(l, c * MathF.Cos(h), c * MathF.Sin(h));

    /// <summary>From linear sRGB.</summary>
    public static Oklab FromLinear(float r, float g, float b)
    {
        float l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        float m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        float s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
        return new(0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                   1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                   0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    /// <summary>To linear sRGB (out of gamut where a channel leaves 0..1).</summary>
    public (float R, float G, float B) ToLinear()
    {
        float l = L + 0.3963377774f * A + 0.2158037573f * B;
        float m = L - 0.1055613458f * A - 0.0638541728f * B;
        float s = L - 0.0894841775f * A - 1.2914855480f * B;
        (l, m, s) = (l * l * l, m * m * m, s * s * s);
        return (4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
    }

    public static Oklab Of(Rgb c) => FromLinear(Linear(c.R), Linear(c.G), Linear(c.B));

    /// <summary>
    /// Back to sRGB, inside the screen's gamut: where the colour is beyond it, chroma gives way at its own hue
    /// (never a channel clipped on its own, which turns orange to yellow and blue to cyan), and a colour too
    /// bright for its chroma comes down toward middle lightness rather than going white.
    /// </summary>
    public Rgb ToRgb()
    {
        Oklab c = Fit();
        var (r, g, b) = c.ToLinear();
        return new Rgb(Encode(r), Encode(g), Encode(b));
    }

    /// <summary>
    /// The nearest colour of the same hue inside the gamut, found along the line toward a grey part of the way to the
    /// lightness where that hue holds the most chroma (its cusp: Ottosson 2021, "sRGB gamut clipping"). So a bright orange
    /// too strong for the screen comes down toward the orange it can show, not out to a pale peach.
    /// </summary>
    public Oklab Fit()
    {
        float l = Math.Clamp(L, 0f, 1f);
        if (Inside(new Oklab(l, A, B))) return new Oklab(l, A, B);
        float l0 = l + (CuspAt(Hue) - l) * 0.6f, lo = 0f, hi = 1f;
        for (int i = 0; i < 14; i++)
        {
            float t = (lo + hi) * 0.5f;
            if (Inside(new Oklab(l0 + (l - l0) * t, A * t, B * t))) lo = t; else hi = t;
        }
        return new Oklab(l0 + (l - l0) * lo, A * lo, B * lo);
    }

    /// <summary>Each degree of hue's cusp lightness: where the screen shows that hue at its most chroma.</summary>
    static readonly float[] Cusps = BuildCusps();

    static float[] BuildCusps()
    {
        var cusps = new float[360];
        for (int d = 0; d < 360; d++)
        {
            float h = d * MathF.PI / 180f, best = 0f;
            for (float l = 0.2f; l <= 1f; l += 0.01f)
            {
                // The most chroma inside at this lightness and hue.
                float lo = 0f, hi = 0.4f;
                for (int i = 0; i < 12; i++)
                {
                    float c = (lo + hi) * 0.5f;
                    if (Inside(Lch(l, c, h))) lo = c; else hi = c;
                }
                if (lo > best) (best, cusps[d]) = (lo, l);
            }
        }
        return cusps;
    }

    static float CuspAt(float hue)
    {
        float d = (hue * 180f / MathF.PI + 360f) % 360f;
        int i = (int)d;
        return At(i) + (At(i + 1) - At(i)) * (d - i);
        static float At(int k) => Cusps[k % 360];
    }

    static bool Inside(Oklab c)
    {
        var (r, g, b) = c.ToLinear();
        const float e = 1e-4f;
        return r >= -e && g >= -e && b >= -e && r <= 1f + e && g <= 1f + e && b <= 1f + e;
    }

    /// <summary>
    /// This colour made bolder at its own hue: chroma times <paramref name="gain"/>, raised to at least
    /// <paramref name="floor"/> (a near-grey takes <paramref name="hue"/> then, given one) and held to at most
    /// <paramref name="most"/>, lightness times <paramref name="light"/>; then fitted into the gamut.
    /// </summary>
    public static Rgb Vivid(Rgb c, float gain, float floor = 0f, float light = 1f, float? hue = null, float most = 1f)
    {
        Oklab o = Of(c);
        float chroma = o.Chroma, h = chroma > 0.004f || hue == null ? o.Hue : hue.Value;
        return Lch(o.L * light, MathF.Min(MathF.Max(chroma * gain, floor), most), h).ToRgb();
    }

    /// <summary>How far apart two hues are, 0 (the same) to π (opposite), radians.</summary>
    public static float HueGap(float a, float b)
    {
        float d = MathF.Abs(a - b) % MathF.Tau;
        return d > MathF.PI ? MathF.Tau - d : d;
    }

    public static float Linear(float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    }

    public static float Encode(float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        return v <= 0.0031308f ? 12.92f * v : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;
    }
}
