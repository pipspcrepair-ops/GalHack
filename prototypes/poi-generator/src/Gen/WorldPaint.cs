#nullable enable
using System;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The colours made things take on one world (docs/DESIGN.md's palette rule): paint, an accent for
/// signs and trim, roofs, cloth and the colour of its lamps, from the palette its layers rolled (the
/// theme leans that roll; it never fixes it). Time and the fall fade it and dirty it. Natural things,
/// stone, earth, plants and water, never take it: they come from the world itself.
/// </summary>
public sealed record WorldPaint(Rgb Main, Rgb Accent, Rgb Roof, Rgb Cloth, Rgb Light, float Fade, float Dirt)
{
    static readonly Rgb Grime = new(0x4a, 0x42, 0x38);

    /// <summary>A made colour as this world has it: drawn toward one of its paints at the original's lightness, then faded and dirtied.</summary>
    public Rgb Made(Rgb was, Rgb paint, float pull)
    {
        float l = Luma(was), p = MathF.Max(Luma(paint), 1f);
        // Keep the original's light and shade, so a dark trim stays dark: only its colour moves.
        Rgb lit = paint.Scale(Math.Clamp(MathF.Sqrt(l / p), 0.55f, 1.5f));
        return Weather(was.Lerp(lit, pull));
    }

    /// <summary>What years and the fall do to paint: it goes grey and chalky, then grimy.</summary>
    public Rgb Weather(Rgb c)
    {
        float l = Luma(c);
        var grey = new Rgb((byte)l, (byte)l, (byte)l);
        return c.Lerp(grey, Fade * 0.55f).Lerp(Grime, Dirt * 0.3f);
    }

    static float Luma(Rgb c) => c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;

    /// <summary>A shade of a paint for one building or piece, so a street isn't one colour: a hash picks lighter, darker or warmer.</summary>
    static Rgb Shade(Rgb c, uint h) => (h % 4) switch { 0 => c, 1 => c.Scale(0.86f), 2 => c.Scale(1.1f), _ => c.Lerp(new Rgb(0xb0, 0x90, 0x70), 0.18f) };

    /// <summary>
    /// Paints a placed place's made things: roofs, signs, cloth, painted trim and props, and its
    /// working lamps' colour. Walls of stone, earth, concrete and glass keep their own; growth,
    /// debris, remains and anything of the land are left alone. Alien and Hollow places keep what
    /// they grew. Runs once, after decay and occupancy.
    /// </summary>
    public void Apply(PoiInstance inst, ulong seed)
    {
        inst.Paint = this;
        if ((inst.Family.Style ?? "").StartsWith("alien_", StringComparison.Ordinal) || inst.Family.Faction == Faction.Hollow) return;
        uint pick = (uint)(Hash.Combine(seed, 0x9A1A7) & 0xffff);
        BlockGrid g = inst.Blocks;
        foreach (Int3 c in g.Cells())
        {
            ref Block b = ref g.At(c);
            if (b.IsAir || b.Role is Role.Debris or Role.Growth or Role.Rock || b.Material is Material.Rock or Material.Soil or Material.Plant
                    or Material.Bloom or Material.Flesh or Material.Glass or Material.Lumen or Material.DeadLumen or Material.Water or Material.Crystal or Material.Glyph
                    or Material.Bone or Material.Chitin or Material.Resin or Material.Ash || b.Type is BlockType.Remains or BlockType.Plant or BlockType.Vine)
                continue;
            // One shade per building-sized patch, so neighbours differ and a wall reads as one wall.
            uint h = (uint)Hash.Cell(new Int3(c.X >> 3, 0, c.Z >> 3), 0x2A1) + pick;
            if (b.Type == BlockType.Sign) b.Color = Made(b.Color, Shade(Accent, h), 0.8f);
            else if (b.Material == Material.Cloth) b.Color = Made(b.Color, Shade(h % 3 == 0 ? Accent : Cloth, h), 0.7f);
            else if (b.Role == Role.Roof) b.Color = Made(b.Color, Shade(Roof, h), 0.55f);
            else if (b.Role is Role.Decor or Role.Prop && b.Material is Material.Steel or Material.Alloy or Material.Timber or Material.Ceramic or Material.Scrap or Material.Rust)
                b.Color = Made(b.Color, Shade(h % 2 == 0 ? Main : Accent, h), 0.45f);
            else if (b.Role == Role.Wall && b.Material is Material.Timber or Material.Ceramic or Material.Alloy) b.Color = Made(b.Color, Shade(Main, h), 0.3f);
            else b.Color = Weather(b.Color);
        }
        // Lamps the place still lights burn in the world's own colour; warnings and the growth's glow keep theirs.
        foreach (LightSpot l in inst.Lights.Where(l => l.Mode is LightMode.Steady or LightMode.Flicker))
            if (!Reddish(l.Color)) l.Color = l.Color.Lerp(Light, 0.5f);
    }

    static bool Reddish(Rgb c) => c.R > 180 && c.G < 90 && c.B < 90;
}
