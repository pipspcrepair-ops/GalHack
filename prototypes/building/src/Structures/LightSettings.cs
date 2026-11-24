using System;
using System.Globalization;

namespace GalHack.Structures;

/// <summary>Where a glowing paint lights its face: all of it, a strip across the middle, an inset panel, or a border round the edge.</summary>
public enum LampShape : byte { None, Full, Strip, Panel, Border }

/// <summary>How a light moves: held, slowly breathing, failing, flashing, or drifting between two colours.</summary>
public enum LightAnim : byte { Steady, Pulse, Flicker, Strobe, Fade }

/// <summary>
/// A light's colour and animation, one small record for every light in the game: a player's light
/// block, a ceiling strip, a lamp in a POI. Colours are 0xRRGGBB. <see cref="Color2"/> is the
/// second colour a Fade drifts to. <see cref="Seed"/> offsets the animation so neighbours don't
/// move in step. The shader (see BlockMaterials) and <see cref="Intensity"/> / <see cref="ColorAt"/>
/// use one formula, so a light's glow and its real light stay together.
///
/// The record is stored in its quantised form (speed in eighths of a hertz, seed in 7 bits, the second
/// colour in 4 bits a channel) because that is what a mesh can carry; always make one with <see cref="Make"/>.
/// </summary>
public readonly record struct LightSettings(uint Color, uint Color2, LightAnim Anim, float Speed, byte Seed)
{
    /// <summary>The player's palette: values whose channels are multiples of 0x11, so a second colour survives the mesh exactly.</summary>
    public static readonly uint[] Palette =
    {
        0xffffff, 0xffddaa, 0xffaa44, 0xff3322, 0xff66aa, 0xaa55ff, 0x4477ff, 0x33ddff, 0x44ff88, 0xbbff44,
    };

    public static readonly float[] Speeds = { 0.25f, 0.5f, 1f };
    public static readonly string[] SpeedNames = { "SLOW", "MEDIUM", "FAST" };

    public static LightSettings Make(uint color, LightAnim anim = LightAnim.Steady, float speed = 0.5f, int seed = 0, uint? color2 = null)
    {
        uint c2 = anim == LightAnim.Fade ? color2 ?? color : color;
        // Squash the second colour to 4 bits a channel.
        uint q = ((c2 >> 20 & 15) << 20) | ((c2 >> 20 & 15) << 16) | ((c2 >> 12 & 15) << 12) | ((c2 >> 12 & 15) << 8) | ((c2 >> 4 & 15) << 4) | (c2 >> 4 & 15);
        return new LightSettings(color & 0xffffff, q, anim, SpeedFrom(SpeedQ(speed)), (byte)((seed & 127) << 1));
    }

    public static LightSettings Steady(uint color) => Make(color);

    static int SpeedQ(float speed) => Math.Clamp((int)MathF.Round(speed * 8f), 1, 31);
    static float SpeedFrom(int q) => q / 8f;

    float Phase => (Seed >> 1) / 128f;

    /// <summary>The same light with another animation.</summary>
    public LightSettings With(LightAnim anim) => Make(Color, anim, Speed, Seed >> 1, Color2);
    public LightSettings WithSpeed(float speed) => Make(Color, Anim, speed, Seed >> 1, Color2);
    public LightSettings WithColor(uint color) => Make(color, Anim, Speed, Seed >> 1, Anim == LightAnim.Fade ? Color2 : color);
    public LightSettings WithColor2(uint color2) => Make(Color, Anim, Speed, Seed >> 1, color2);
    public LightSettings WithSeed(int seed) => Make(Color, Anim, Speed, seed, Color2);

    /// <summary>How bright the light is at a time in seconds: 0 off to 1 full.</summary>
    public float Intensity(double seconds)
    {
        float x = (float)(seconds * Speed) + Phase;
        switch (Anim)
        {
            case LightAnim.Pulse:
                return 0.12f + 0.88f * (0.5f + 0.5f * MathF.Sin(MathF.Tau * x));
            case LightAnim.Flicker:
            {
                float v = Hash(MathF.Floor(x * 7f)) * 0.6f + Hash(MathF.Floor(x * 3.1f) + 31f) * 0.4f;
                return v < 0.28f ? 0.06f : 0.45f + 0.55f * v;
            }
            case LightAnim.Strobe:
                return Fract(x * 2f) < 0.14f ? 1f : 0.03f;
            default:
                return 1f;
        }
    }

    /// <summary>Where a Fade is between its two colours (0 = first, 1 = second); 0 for every other mode.</summary>
    public float Blend(double seconds) =>
        Anim == LightAnim.Fade ? 0.5f + 0.5f * MathF.Sin(MathF.Tau * ((float)(seconds * Speed) + Phase)) : 0f;

    /// <summary>The colour at a time, 0xRRGGBB.</summary>
    public uint ColorAt(double seconds)
    {
        float m = Blend(seconds);
        if (m <= 0f) return Color;
        static uint Ch(uint a, uint b, float m, int shift) => (uint)MathF.Round(((a >> shift & 255) * (1f - m)) + ((b >> shift & 255) * m)) << shift;
        return Ch(Color, Color2, m, 16) | Ch(Color, Color2, m, 8) | Ch(Color, Color2, m, 0);
    }

    // The shader's share, packed beside the glow bit: the second colour (12 bits), speed (5) and phase (7) in the
    // accent value, the mode in three spare flag bits.
    public const int AnimShift = 20;
    public uint PackAccent()
    {
        uint c2 = ((Color2 >> 20 & 15) << 8) | ((Color2 >> 12 & 15) << 4) | (Color2 >> 4 & 15);
        return c2 | ((uint)SpeedQ(Speed) << 12) | ((uint)(Seed >> 1) << 17);
    }

    static float Fract(float v) => v - MathF.Floor(v);
    static float Hash(float p) => Fract(MathF.Sin(p * 127.1f) * 43758.5453f);

    // ---- Text (blueprints, saves) ------------------------------------------------------------

    /// <summary>"ff3322:pulse:0.5:12" or, for a fade, "ff3322+2244ff:fade:0.25:40": colour(s), mode, speed in hertz, seed.</summary>
    public string ToText()
    {
        string col = Anim == LightAnim.Fade ? $"{Color:x6}+{Color2:x6}" : $"{Color:x6}";
        return string.Create(CultureInfo.InvariantCulture, $"{col}:{Anim.ToString().ToLowerInvariant()}:{Speed:0.###}:{Seed >> 1}");
    }

    public static bool TryParse(string text, out LightSettings s)
    {
        s = default;
        string[] p = text.Split(':');
        if (p.Length < 2 || p.Length > 4) return false;
        string[] cols = p[0].Split('+');
        if (!uint.TryParse(cols[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint c1) || c1 > 0xffffff) return false;
        uint c2 = c1;
        if (cols.Length == 2 && (!uint.TryParse(cols[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out c2) || c2 > 0xffffff)) return false;
        if (cols.Length > 2 || !Enum.TryParse(p[1], true, out LightAnim anim)) return false;
        float speed = 0.5f;
        int seed = 0;
        if (p.Length > 2 && !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out speed)) return false;
        if (p.Length > 3 && !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) return false;
        s = Make(c1, anim, speed, seed, c2);
        return true;
    }
}
