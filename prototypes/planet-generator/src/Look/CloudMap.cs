using System;
using System.Threading.Tasks;
using Planets.Gen;

namespace Planets.Look;

/// <summary>
/// Cloud wisps for the orbit globe, as an equirectangular RGBA map (alpha = density). Sampled on
/// the unit sphere so nothing pinches at the poles, and stretched east-west so clouds streak along
/// the prevailing winds like brush strokes rather than speckle.
/// </summary>
public static class CloudMap
{
    public static byte[] Build(PlanetSpec spec, ArchetypeLook look, int width, out int height)
    {
        int h = width / 2;
        height = h;
        var data = new byte[width * h * 4];
        var shape = new Fractal(Hash.Of(spec.Seed, 0x636C6F75), 0.55, 6, 0.52f);
        var warp = new Fractal(Hash.Of(spec.Seed, 0x77617270), 0.9, 3);
        var mask = new Fractal(Hash.Of(spec.Seed, 0x6D61736B), 1.4, 2);
        // Keep most of the surface visible: from orbit, the land is the point.
        float threshold = 0.55f - look.CloudCover * 0.75f;

        Parallel.For(0, h, py =>
        {
            double lat = (0.5 - (py + 0.5) / h) * Math.PI;
            for (int px = 0; px < width; px++)
            {
                double lon = (px + 0.5) / width * 2 * Math.PI;
                var p = new P3(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon));
                float w = warp.Fbm(p, 1e-4);
                var q = new P3(p.X + w * 0.35, p.Y * 2.4 + w * 0.2, p.Z - w * 0.35);
                float n = shape.Fbm(q, 1e-4) + mask.Fbm(p, 1e-4) * 0.35f;
                // A narrow ramp gives clean, graphic cloud edges rather than soft smears.
                float density = Mathx.Smooth(threshold, threshold + 0.12f, n);
                // A touch of self-shading: thick cores slightly darker than wispy edges reads as volume.
                Rgb c = look.CloudColor * (1f - 0.12f * Mathx.Smooth(0.6f, 1f, density));
                int o = (py * width + px) * 4;
                data[o] = Rgb.Byte(c.R);
                data[o + 1] = Rgb.Byte(c.G);
                data[o + 2] = Rgb.Byte(c.B);
                data[o + 3] = Rgb.Byte(density * 0.92f);
            }
        });
        return data;
    }
}
