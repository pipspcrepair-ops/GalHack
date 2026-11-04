using System;
using System.Threading.Tasks;
using Planets.Gen;

namespace Planets.Look;

/// <summary>
/// The planet's colour map, drawn from the world map: biome colours blended across borders,
/// liquid by depth, rivers, relief shading and polar caps. One builder serves the orbit globe
/// (equirectangular: the map is exactly 2:1), the flat map view and the blind-test atlas.
///
/// Coastlines are decided per pixel from interpolated elevation rather than per cell, so they
/// stay crisp when the map is drawn larger than the 32 m grid.
/// </summary>
public sealed class ColorMap
{
    public readonly int Width, Height;
    public readonly byte[] Albedo;  // RGBA8; alpha is 255 over liquid, so the globe can make seas glossy
    public readonly byte[] Glow;    // RGB8 self-lit colour: lava, glowing moss, luminous nights

    ColorMap(int width, int height)
    {
        Width = width;
        Height = height;
        Albedo = new byte[width * height * 4];
        Glow = new byte[width * height * 3];
    }

    static readonly (float x, float y, float z) Light = Normalize(-1f, -1f, 1.1f); // from the north-west
    const float Relief = 2.6f;   // vertical exaggeration for shading

    public static ColorMap Build(WorldMap map, ArchetypeLook look, int width)
    {
        int height = width / 2;
        var result = new ColorMap(width, height);
        int n = map.Count;

        // Per-cell inputs, so per-pixel work is just blending.
        Rgb[] color = CellColors(map, look, out Rgb[] glow);
        var shade = new float[n];
        float flat = Light.z;
        for (int k = 0; k < n; k++)
        {
            int i = k % map.Width, j = k / map.Width;
            float east = map.Elevation[map.Index(i + 1, j)], west = map.Elevation[map.Index(i - 1, j)];
            float south = map.Elevation[map.Index(i, Math.Min(j + 1, map.Height - 1))];
            float north = map.Elevation[map.Index(i, Math.Max(j - 1, 0))];
            float gx = Math.Max(east, 0f) - Math.Max(west, 0f), gy = Math.Max(south, 0f) - Math.Max(north, 0f);
            var nrm = Normalize(-gx * Relief / (2f * map.CellSize), -gy * Relief / (2f * map.CellSize), 1f);
            float s = nrm.x * Light.x + nrm.y * Light.y + nrm.z * Light.z;
            shade[k] = Math.Clamp(1f + (s - flat) * 1.5f, 0.5f, 1.35f);
        }

        float[] rivers = RasterizeRivers(map, width, height);
        var jitter = new Fractal(Hash.Of(map.Spec.Seed, 0x6A6974), 160, 3);
        Rgb riverColor = Rgb.Lerp(look.ShallowSea, look.DeepSea, 0.25f);
        bool lava = map.Def.Liquid == Liquid.Lava;
        double sx = map.WidthMeters / width, sy = map.HeightMeters / height;

        Parallel.For(0, height, py =>
        {
            for (int px = 0; px < width; px++)
            {
                double x = (px + 0.5) * sx, y = (py + 0.5) * sy;
                double gx = x / map.CellSize - 0.5, gy = Math.Clamp(y / map.CellSize - 0.5, 0, map.Height - 1.001);
                int i0 = (int)Math.Floor(gx), j0 = (int)Math.Floor(gy);
                float tx = (float)(gx - i0), ty = (float)(gy - j0);
                int a = map.Index(i0, j0), b = map.Index(i0 + 1, j0);
                int c = map.Index(i0, Math.Min(j0 + 1, map.Height - 1)), d = map.Index(i0 + 1, Math.Min(j0 + 1, map.Height - 1));
                float wa = (1 - tx) * (1 - ty), wb = tx * (1 - ty), wc = (1 - tx) * ty, wd = tx * ty;
                float e = map.Elevation[a] * wa + map.Elevation[b] * wb + map.Elevation[c] * wc + map.Elevation[d] * wd;
                bool cap = map.Has(a, CellFlags.Cap) && map.Has(d, CellFlags.Cap);
                bool liquid = e < 0f && !cap;

                Rgb col, emit = default;
                if (liquid)
                {
                    float frozen = 0f;
                    if (map.Biome[a] == (byte)Biome.FrozenSea) frozen += wa;
                    if (map.Biome[b] == (byte)Biome.FrozenSea) frozen += wb;
                    if (map.Biome[c] == (byte)Biome.FrozenSea) frozen += wc;
                    if (map.Biome[d] == (byte)Biome.FrozenSea) frozen += wd;
                    col = Rgb.Lerp(look.Sea(Biome.ShallowSea, -e), look.FrozenSea, Mathx.Smooth(0.35f, 0.65f, frozen));
                    // Lava glows its own colour shifted towards red: adding orange to orange would wash out to yellow.
                    if (look.LiquidGlow > 0) emit = new Rgb(col.R, col.G * 0.55f, col.B * 0.4f) * look.LiquidGlow * 0.35f;
                }
                else
                {
                    // Borders wander a little so biome edges never trace the grid.
                    P3 p = map.Model.Cylinder.At(x, y);
                    double jx = gx + jitter.Fbm(p, 16) * 0.9, jy = Math.Clamp(gy + jitter.Fbm(new P3(p.Z, p.Y, p.X), 16) * 0.9, 0, map.Height - 1.001);
                    int ji = (int)Math.Floor(jx), jj = (int)Math.Floor(jy);
                    float ux = (float)(jx - ji), uy = (float)(jy - jj);
                    col = default;
                    float total = 0f;
                    Blend(map, color, glow, ji, jj, (1 - ux) * (1 - uy), ref col, ref emit, ref total);
                    Blend(map, color, glow, ji + 1, jj, ux * (1 - uy), ref col, ref emit, ref total);
                    Blend(map, color, glow, ji, jj + 1, (1 - ux) * uy, ref col, ref emit, ref total);
                    Blend(map, color, glow, ji + 1, jj + 1, ux * uy, ref col, ref emit, ref total);
                    if (total > 0f)
                    {
                        col *= 1f / total;
                        emit *= 1f / total;
                    }
                    else col = color[a];

                    float relief = shade[a] * wa + shade[b] * wb + shade[c] * wc + shade[d] * wd;
                    col *= relief;
                    if (look.GlowingNights) emit += col.Saturate(1.4f) * 0.1f;

                    float r = rivers[py * width + px];
                    if (r > 0f)
                    {
                        col = Rgb.Lerp(col, riverColor, r);
                        if (lava) emit = Rgb.Lerp(emit, look.DeepSea * look.LiquidGlow * 0.5f, r);
                    }
                }

                int o = (py * width + px) * 4;
                result.Albedo[o] = Rgb.Byte(col.R);
                result.Albedo[o + 1] = Rgb.Byte(col.G);
                result.Albedo[o + 2] = Rgb.Byte(col.B);
                result.Albedo[o + 3] = liquid ? (byte)255 : (byte)0;
                int g = (py * width + px) * 3;
                result.Glow[g] = Rgb.Byte(emit.R);
                result.Glow[g + 1] = Rgb.Byte(emit.G);
                result.Glow[g + 2] = Rgb.Byte(emit.B);
            }
        });
        return result;
    }

    /// <summary>
    /// Base colour and glow of every cell: the biome's colour with a gentle large-scale tone drift,
    /// which keeps big single-biome areas from looking like flat paint. Shared by the surface terrain.
    /// </summary>
    public static Rgb[] CellColors(WorldMap map, ArchetypeLook look, out Rgb[] glow)
    {
        var color = new Rgb[map.Count];
        var lit = new Rgb[map.Count];
        var tone = new Fractal(Hash.Of(map.Spec.Seed, 0x746F6E65), 900, 3);
        for (int k = 0; k < map.Count; k++)
        {
            int i = k % map.Width, j = k / map.Width;
            var biome = (Biome)map.Biome[k];
            Rgb c = biome is Biome.StormCap or Biome.IceCap ? look.CapColor : BiomeLook.Color(biome);
            float drift = tone.Fbm(map.Model.Cylinder.At(map.CellX(i), map.CellY(j)), 64);
            color[k] = c * (1f + 0.07f * drift);
            lit[k] = BiomeLook.Glow(biome) * (look.GlowingNights ? 3f : 1f);
        }
        glow = lit;
        return color;
    }

    /// <summary>Albedo with the glow added on top, as RGB8: how the map looks lit and flat.</summary>
    public byte[] Composite()
    {
        var rgb = new byte[Width * Height * 3];
        for (int p = 0; p < Width * Height; p++)
        for (int c = 0; c < 3; c++)
            rgb[p * 3 + c] = (byte)Math.Min(255, Albedo[p * 4 + c] + Glow[p * 3 + c]);
        return rgb;
    }

    static void Blend(WorldMap map, Rgb[] color, Rgb[] glow, int i, int j, float w, ref Rgb col, ref Rgb emit, ref float total)
    {
        int k = map.Index(i, Math.Clamp(j, 0, map.Height - 1));
        if (map.Has(k, CellFlags.Water)) return; // land colours never bleed into the coastline
        col += color[k] * w;
        emit += glow[k] * w;
        total += w;
    }

    /// <summary>Draws river polylines as anti-aliased lines; small streams fade out rather than vanish.</summary>
    static float[] RasterizeRivers(WorldMap map, int width, int height)
    {
        var mask = new float[width * height];
        double sx = width / map.WidthMeters, sy = height / map.HeightMeters;
        foreach (River r in map.Rivers)
        {
            for (int k = 0; k + 1 < r.Count; k++)
            {
                double ax = r.X[k] * sx, ay = r.Y[k] * sy, bx = r.X[k + 1] * sx, by = r.Y[k + 1] * sy;
                float widthPx = r.Width[k] * (float)sx;
                float half = Math.Max(widthPx * 0.5f, 0.45f);
                float alpha = Math.Clamp(widthPx * 1.5f + 0.25f, 0.3f, 1f);
                int x0 = (int)Math.Floor(Math.Min(ax, bx) - half - 1), x1 = (int)Math.Ceiling(Math.Max(ax, bx) + half + 1);
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - half - 1)), y1 = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(ay, by) + half + 1));
                double vx = bx - ax, vy = by - ay, len2 = vx * vx + vy * vy;
                for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    double cx = px + 0.5, cy = py + 0.5;
                    double t = len2 > 0 ? Math.Clamp(((cx - ax) * vx + (cy - ay) * vy) / len2, 0, 1) : 0;
                    double dx = cx - (ax + vx * t), dy = cy - (ay + vy * t);
                    float cover = Math.Clamp(half + 0.5f - (float)Math.Sqrt(dx * dx + dy * dy), 0f, 1f) * alpha;
                    if (cover <= 0f) continue;
                    int wx = (px % width + width) % width;
                    ref float m = ref mask[py * width + wx];
                    m = Math.Max(m, cover);
                }
            }
        }
        return mask;
    }

    static (float x, float y, float z) Normalize(float x, float y, float z)
    {
        float l = MathF.Sqrt(x * x + y * y + z * z);
        return (x / l, y / l, z / l);
    }
}
