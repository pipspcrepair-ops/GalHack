using System;
using System.Collections.Generic;
using GalHack.Structures;

namespace PoiGenerator.Gen;

/// <summary>The feel a place's lights keep. A kit can name one (<c>mood warm</c> in its .kit file); otherwise the place's state chooses.</summary>
public enum LightMood { Warm, Dying, Hostile, Cold, Sealed }

/// <summary>A block face painted to glow: a whole face, a strip, an inset panel or a border, lit with a settings record.</summary>
public sealed record LitFace(Int3 Cell, Dir Face, LampShape Shape, LightSettings Style);

/// <summary>
/// The POI generator's lighting API. The generator, the rollout and any later pass call it to give a
/// place's lights a colour and an animation (steady, pulse, flicker, strobe, fade) and to paint
/// glowing faces. Everything is data on the <see cref="PoiInstance"/>: PoiView draws it, and the slice
/// seats it with the place.
///
/// <code>
/// PoiLights.SetMood(poi, LightMood.Dying);                                   // a dying corridor flickers
/// PoiLights.Set(spot, LightSettings.Make(0xff3322, LightAnim.Pulse, 0.5f));  // one lamp, red pulse
/// PoiLights.PaintFace(poi, wallCell, Dir.North, LampShape.Border, style);    // a warning band
/// </code>
/// </summary>
public static class PoiLights
{
    static uint Hex(Rgb c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    /// <summary>The settings a mood gives a light at a cell (the cell varies the animation so neighbours don't move in step).</summary>
    public static LightSettings Style(LightMood mood, Rgb kitLight, Int3 cell) => Style(mood, kitLight, Hash(cell));

    public static LightSettings Style(LightMood mood, Rgb kitLight, int seed) => mood switch
    {
        LightMood.Warm => LightSettings.Make(Hex(kitLight), LightAnim.Steady, 0.5f, seed),
        LightMood.Dying => LightSettings.Make(Hex(kitLight), LightAnim.Flicker, 0.5f, seed),
        LightMood.Hostile => LightSettings.Make(0xff3322, LightAnim.Pulse, 1f, seed),
        LightMood.Sealed => LightSettings.Make(0xff3322, LightAnim.Pulse, 0.5f, seed),
        _ => LightSettings.Make(0x8fd0ff, LightAnim.Steady, 0.5f, seed),
    };

    /// <summary>Sets one lamp's colour and animation. Its real light and its glow follow.</summary>
    public static void Set(LightSpot spot, LightSettings style)
    {
        spot.Style = style;
        spot.Color = new Rgb((byte)(style.Color >> 16), (byte)(style.Color >> 8), (byte)style.Color);
    }

    /// <summary>Adds a lamp to the place with these settings and returns it.</summary>
    public static LightSpot Add(PoiInstance poi, Int3 cell, LightSettings style, float energy = 1.2f, float range = 8f)
    {
        var spot = new LightSpot { Cell = cell, Energy = energy, Range = range };
        Set(spot, style);
        poi.Lights.Add(spot);
        return spot;
    }

    /// <summary>Gives every lamp (or those <paramref name="which"/> picks) a mood. Returns how many it set.</summary>
    public static int SetMood(PoiInstance poi, LightMood mood, Func<LightSpot, bool>? which = null)
    {
        int n = 0;
        foreach (LightSpot s in poi.Lights)
        {
            if (which != null && !which(s)) continue;
            Set(s, Style(mood, poi.Layout.Lamp ?? poi.Kit.Light, s.Cell));
            n++;
        }
        return n;
    }

    /// <summary>The mood the kit names, else the one the place's state suggests: running places glow warm, dead ones flicker, infested ones pulse red.</summary>
    public static LightMood MoodFor(PoiInstance poi)
    {
        if (Enum.TryParse(poi.Kit.Mood, true, out LightMood named)) return named;
        return poi.State switch
        {
            PoiState.Active => LightMood.Warm,
            PoiState.Infested => LightMood.Hostile,
            _ => LightMood.Dying,
        };
    }

    /// <summary>Applies <see cref="MoodFor"/> to every lamp, but only when the kit names a mood (so existing kits look as they did).</summary>
    public static void ApplyKitMood(PoiInstance poi)
    {
        if (poi.Kit.Mood.Length > 0) SetMood(poi, MoodFor(poi));
    }

    /// <summary>
    /// Paints the <paramref name="face"/> side of the solid block at <paramref name="cell"/> to glow. A glowing paint only
    /// glows (it casts no real light).
    /// </summary>
    public static LitFace PaintFace(PoiInstance poi, Int3 cell, Dir face, LampShape shape, LightSettings style)
    {
        poi.LitFaces.RemoveAll(f => f.Cell == cell && f.Face == face);
        var lit = new LitFace(cell, face, shape, style);
        poi.LitFaces.Add(lit);
        return lit;
    }

    /// <summary>A glowing strip along a run of blocks, such as down a corridor wall: every cell from <paramref name="from"/> to <paramref name="to"/> (one axis).</summary>
    public static int PaintStrip(PoiInstance poi, Int3 from, Int3 to, Dir face, LightSettings style)
    {
        int n = 0;
        Int3 min = Int3.Min(from, to), max = Int3.Max(from, to);
        for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
                for (int z = min.Z; z <= max.Z; z++)
                {
                    PaintFace(poi, new Int3(x, y, z), face, LampShape.Strip, style);
                    n++;
                }
        return n;
    }

    static int Hash(Int3 c) => (int)(((uint)c.X * 73856093u ^ (uint)c.Y * 19349663u ^ (uint)c.Z * 83492791u) % 127u);
}
