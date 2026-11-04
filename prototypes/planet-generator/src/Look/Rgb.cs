using System;
using System.Globalization;

namespace Planets.Look;

/// <summary>A plain sRGB colour (0-1 floats), so the look layer stays engine-free and testable headless.</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    public static Rgb Hex(string hex)
    {
        hex = hex.TrimStart('#');
        int v = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgb((v >> 16 & 255) / 255f, (v >> 8 & 255) / 255f, (v & 255) / 255f);
    }

    public static Rgb Lerp(Rgb a, Rgb b, float t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    public static Rgb operator *(Rgb c, float k) => new(c.R * k, c.G * k, c.B * k);
    public static Rgb operator *(Rgb a, Rgb b) => new(a.R * b.R, a.G * b.G, a.B * b.B);
    public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);

    public float Luma => 0.299f * R + 0.587f * G + 0.114f * B;

    /// <summary>Pushes saturation up or down around the colour's own brightness.</summary>
    public Rgb Saturate(float amount)
    {
        float l = Luma;
        return new Rgb(l + (R - l) * amount, l + (G - l) * amount, l + (B - l) * amount);
    }

    public static byte Byte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
}
