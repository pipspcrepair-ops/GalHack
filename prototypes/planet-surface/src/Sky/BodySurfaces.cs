using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace PlanetSurface.Skies;

/// <summary>
/// What every other world looks like from afar: its own surface, not a plain ball. A coarse run of
/// the very generator and spec the world will use when it is visited, so what you see from here is
/// what you would land on, and a banded map for a giant.
///
/// One thread paints them, one at a time, biggest in the sky first, and hands each back to be
/// uploaded on its own frame, so none of it stalls anything. They live in one texture array of a
/// fixed size, kept for as long as the system is, so memory is bounded however many bodies there are:
/// past <see cref="Layers"/> the rest stay plain, and those are the ones too small to see.
///
/// Each pixel is colour; alpha is how much of it glows by itself (lava, luminous nights).
/// </summary>
public sealed class BodySurfaces
{
    public const int Width = 256, Height = 128, Layers = 32;

    /// <summary>What the array holds, mipmaps and all: the same whatever the system.</summary>
    public const long MemoryBytes = Layers * (long)Width * Height * 4 * 4 / 3;

    /// <summary>The array every sky samples.</summary>
    public Texture2DArray Texture { get; }

    /// <summary>How many there are to paint, how many are up, and what they took on their thread, ms.</summary>
    public int Wanted { get; private set; }
    public int Done { get; private set; }
    public double TotalMs { get; private set; }
    public double WorstMs { get; private set; }

    public bool Finished => Done >= Wanted;

    readonly Dictionary<Orbiter, int> _ready = new();
    readonly ConcurrentQueue<(Orbiter Body, int Layer, Image Image, double Ms)> _painted = new();

    public BodySurfaces(StarSystem model, D3 from)
    {
        // A headless run draws nothing: no worker, and nothing to upload to.
        if (DisplayServer.GetName() == "headless") return;
        var blank = Image.CreateEmpty(Width, Height, true, Image.Format.Rgba8);
        blank.Fill(new Color(0.5f, 0.5f, 0.5f, 0f));
        var images = new Godot.Collections.Array<Image>();
        for (int i = 0; i < Layers; i++) images.Add(blank);
        Texture = new Texture2DArray();
        Texture.CreateFromImages(images);

        var order = new List<Sighting>();
        foreach (Orbiter b in model.Bodies)
            if (b.Spec != null && b != model.Home || b.Kind == OrbiterKind.GasGiant) order.Add(model.See(b, 0.0, from));
        order.Sort((x, y) => y.Angular.CompareTo(x.Angular));
        if (order.Count > Layers) order.RemoveRange(Layers, order.Count - Layers);
        Wanted = order.Count;
        new Thread(() => Work(order)) { IsBackground = true, Name = "Body surfaces", Priority = ThreadPriority.BelowNormal }.Start();
    }

    void Work(List<Sighting> order)
    {
        for (int i = 0; i < order.Count; i++)
        {
            long started = Stopwatch.GetTimestamp();
            Image image = Paint(order[i].Body);
            _painted.Enqueue((order[i].Body, i, image, Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        }
    }

    /// <summary>Uploads one finished surface: call it once a frame.</summary>
    public void Pump()
    {
        if (!_painted.TryDequeue(out var p)) return;
        Texture.UpdateLayer(p.Image, p.Layer);
        _ready[p.Body] = p.Layer;
        Done++;
        TotalMs += p.Ms;
        WorstMs = Math.Max(WorstMs, p.Ms);
    }

    /// <summary>Which layer shows a body, or -1 while it is plain.</summary>
    public int LayerOf(Orbiter b) => _ready.TryGetValue(b, out int layer) ? layer : -1;

    /// <summary>A body's surface as an image with its mipmaps: any thread.</summary>
    public static Image Paint(Orbiter b)
    {
        byte[] pixels = b.Kind == OrbiterKind.GasGiant ? Giant(b) : World(b.Spec);
        Image image = Image.CreateFromData(Width, Height, false, Image.Format.Rgba8, pixels);
        image.GenerateMipmaps();
        return image;
    }

    /// <summary>A coarse run of the world's own generator: one map cell to a pixel, from the spec it will be visited with.</summary>
    public static WorldMap Coarse(PlanetSpec spec) => PlanetGenerator.Generate(spec, (float)(spec.WidthMeters / Width));

    static byte[] World(PlanetSpec spec)
    {
        WorldMap map = Coarse(spec);
        ArchetypeLook look = ArchetypeLook.For(spec);
        ColorMap colors = ColorMap.Build(map, look, Width);
        byte[] clouds = CloudMap.Build(spec, look, Width, out _);
        var pixels = new byte[Width * Height * 4];
        for (int k = 0; k < Width * Height; k++)
        {
            int o = k * 4, g = k * 3;
            float cover = clouds[o + 3] / 255f * 0.8f;
            for (int c = 0; c < 3; c++) pixels[o + c] = (byte)(colors.Albedo[o + c] + (clouds[o + c] - colors.Albedo[o + c]) * cover);
            int glow = Math.Max(colors.Glow[g], Math.Max(colors.Glow[g + 1], colors.Glow[g + 2]));
            pixels[o + 3] = (byte)(glow * (1f - cover));
        }
        return Capped(pixels);
    }

    /// <summary>
    /// The map is a torus, so wrapped on a sphere its columns close to a point at each pole and the
    /// land fans out like spokes. Over the poles it is laid flat instead, the same way the globe's
    /// sheet does (<c>Globe.SheetCode</c>): a piece of the map well clear of its edges, at its own scale.
    /// </summary>
    static byte[] Capped(byte[] source)
    {
        var pixels = (byte[])source.Clone();
        for (int py = 0; py < Height; py++)
        {
            double col = (py + 0.5) / Height * Math.PI, cos = Math.Cos(col), sin = Math.Sin(col);
            float t = Math.Clamp(((float)Math.Abs(cos) - 0.80f) / 0.13f, 0f, 1f), cap = t * t * (3f - 2f * t);
            if (cap <= 0f) continue;
            double middle = cos > 0.0 ? 0.25 : 0.75;
            for (int px = 0; px < Width; px++)
            {
                double lon = (px + 0.5) / Width * Math.Tau;
                double u = (middle + sin * Math.Sin(lon) / Math.Tau) * Width - 0.5, v = (middle + sin * Math.Cos(lon) / Math.PI) * Height - 0.5;
                int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
                float fx = (float)(u - x0), fy = (float)(v - y0);
                int o = (py * Width + px) * 4;
                for (int c = 0; c < 4; c++)
                {
                    float At(int x, int y) => source[((y % Height + Height) % Height * Width + (x % Width + Width) % Width) * 4 + c];
                    float top = At(x0, y0) + (At(x0 + 1, y0) - At(x0, y0)) * fx, under = At(x0, y0 + 1) + (At(x0 + 1, y0 + 1) - At(x0, y0 + 1)) * fx;
                    pixels[o + c] = (byte)(source[o + c] + (top + (under - top) * fy - source[o + c]) * cap);
                }
            }
        }
        return pixels;
    }

    /// <summary>A giant: its colour and band, in bands that wander and shear, with one great storm.</summary>
    static byte[] Giant(Orbiter b)
    {
        var drift = new Fractal(Hash.Of(b.Seed, 0x5752), 0.8, 4);
        var streaks = new Fractal(Hash.Of(b.Seed, 0x5354), 0.3, 4, 0.55f);
        var rng = new Rng(Hash.Of(b.Seed, 0x4F56));
        float phase = rng.NextFloat() * 6f, count = 15f + rng.NextFloat() * 10f;
        double stormLat = (rng.NextFloat() - 0.5f) * 1.1, stormLon = rng.NextFloat() * Math.Tau, stormSize = 0.1 + 0.1 * rng.NextFloat();
        var pixels = new byte[Width * Height * 4];
        System.Threading.Tasks.Parallel.For(0, Height, py =>
        {
            double lat = (0.5 - (py + 0.5) / Height) * Math.PI;
            for (int px = 0; px < Width; px++)
            {
                double lon = (px + 0.5) / Width * Math.Tau;
                var p = new P3(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon));
                float w = drift.Fbm(p, 1e-4) - 0.5f;
                float y = (float)p.Y + w * 0.09f;
                float bands = MathF.Sin(y * count + MathF.Sin(y * 7f + phase) * 1.6f) * 0.5f + 0.5f;
                float fine = streaks.Fbm(new P3(p.X * 0.5, p.Y * 6.0 + w * 0.8, p.Z * 0.5), 1e-4);
                float mix = Math.Clamp(bands * 0.72f + (fine - 0.5f) * 0.9f, 0f, 1f);
                Rgb c = Rgb.Lerp(b.Color, b.Band, mix);
                double dLon = Math.IEEERemainder(lon - stormLon, Math.Tau) * 0.55, dLat = lat - stormLat;
                double storm = Math.Sqrt(dLon * dLon + dLat * dLat * 3.0) / stormSize;
                if (storm < 1.0) c = Rgb.Lerp(c, Rgb.Lerp(b.Band, new Rgb(0.9f, 0.5f, 0.35f), 0.5f), (float)Math.Clamp((1.0 - storm) * 3.0, 0.0, 1.0) * (0.75f + 0.25f * (float)Math.Sin(storm * 12.0)));
                int o = (py * Width + px) * 4;
                pixels[o] = Rgb.Byte(c.R);
                pixels[o + 1] = Rgb.Byte(c.G);
                pixels[o + 2] = Rgb.Byte(c.B);
            }
        });
        return pixels;
    }
}
