using System;
using Godot;

namespace PlanetSurface.Skies;

/// <summary>
/// The sky's cloud noise read on the CPU: the same coverage and height profile as sky.gdshader's
/// cloud_density, without its fine detail. It tells the eye when it is in cloud, and paints the
/// clouds on the orbit's globe, so the clouds you see from space are the ones you fly through.
/// </summary>
public sealed class CloudCoverage
{
    readonly byte[] _data;
    readonly int _width, _height, _stride;

    CloudCoverage(byte[] data, int width, int height)
    {
        _data = data;
        _width = width;
        _height = height;
        _stride = data.Length / (width * height);
    }

    /// <summary>A snapshot of the noise, or null while it is still generating.</summary>
    public static CloudCoverage Of(Texture2D noise)
    {
        if (noise == null || noise.GetWidth() == 0) return null;
        Image image = noise.GetImage();
        if (image == null || image.IsEmpty()) return null;
        if (image.IsCompressed()) return null;
        return new CloudCoverage(image.GetData(), image.GetWidth(), image.GetHeight());
    }

    float Sample(float u, float v)
    {
        float x = (u - MathF.Floor(u)) * _width - 0.5f, y = (v - MathF.Floor(v)) * _height - 0.5f;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        float a = At(x0, y0), b = At(x0 + 1, y0), c = At(x0, y0 + 1), d = At(x0 + 1, y0 + 1);
        return (a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy) / 255f;
    }

    float At(int x, int y)
    {
        x = (x % _width + _width) % _width;
        y = (y % _height + _height) % _height;
        return _data[(y * _width + x) * _stride];
    }

    /// <summary>How much cloud stands over a world point, 0 (clear) and up: the shader's coverage.</summary>
    public float Coverage(float x, float z, float cover, Vector2 drift, Vector2 scale)
    {
        float u = (x + drift.X) / scale.X, v = (z + drift.Y) / scale.Y;
        float n = Sample(u, v) * 0.65f + Sample(u * 2f + 0.31f, v * 2f + 0.31f) * 0.35f;
        return n - (1f - (0.12f + (1.05f - 0.12f) * cover));
    }

    /// <summary>The cloud's density at a point in the air, 0 to 1 (the shader's, without the detail noise).</summary>
    public float Density(float x, float y, float z, float cover, float cloudBase, float thick, Vector2 drift, Vector2 scale)
    {
        float coverage = Coverage(x, z, cover, drift, scale);
        if (coverage <= 0f) return 0f;
        float h = (y - cloudBase) / thick;
        if (h <= 0f || h >= 1f) return 0f;
        float top = Math.Clamp(0.2f + coverage * 2.6f, 0.2f, 1f);
        float profile = Smooth(0f, 0.07f, h) * (1f - Smooth(top * 0.55f, top, h));
        return Math.Clamp(coverage * 3.2f * profile * 1.8f, 0f, 1f);
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
