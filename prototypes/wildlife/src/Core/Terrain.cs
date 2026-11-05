using System;
using Godot;

namespace Wildlife;

/// <summary>
/// The planet surface as a pure height function: a gentle plain in the middle (where the herd
/// grazes and you start) rising into rolling hills around it, from a few octaves of value
/// noise. Creatures read heights straight from here instead of raycasting, which keeps
/// 200 of them cheap, and the ground mesh and its collider are built from the same numbers so
/// the two can never disagree.
///
/// The noise is baked once into a grid at the ground mesh's own 2 m spacing and read back
/// bilinearly: a creature's legs ask for dozens of heights a frame, and a grid lookup is far
/// cheaper than five octaves of noise (and matches the drawn ground more closely).
/// </summary>
public sealed class Terrain
{
    public const float Step = 2f;
    public readonly float HalfSize;
    readonly uint _seed;
    readonly int _n;
    readonly float[] _grid;

    public Terrain(ulong seed, float halfSize = 240f)
    {
        _seed = (uint)(seed ^ (seed >> 32));
        HalfSize = halfSize;
        _n = (int)(halfSize * 2f / Step) + 1;
        _grid = new float[_n * _n];
        for (int j = 0; j < _n; j++)
        for (int i = 0; i < _n; i++)
            _grid[j * _n + i] = Sample(-halfSize + i * Step, -halfSize + j * Step);
    }

    /// <summary>
    /// Someone else's ground: <paramref name="height"/> (local x, z to height) baked into the same
    /// 2 m grid over ±<paramref name="halfSize"/>, for creatures living on a patch of a real planet.
    /// </summary>
    public Terrain(Func<float, float, float> height, float halfSize)
    {
        HalfSize = halfSize;
        _n = (int)(halfSize * 2f / Step) + 1;
        _grid = new float[_n * _n];
        for (int j = 0; j < _n; j++)
        for (int i = 0; i < _n; i++)
            _grid[j * _n + i] = height(-halfSize + i * Step, -halfSize + j * Step);
    }

    /// <summary>Ground height at a point, from the baked grid (clamped at the edge of the map).</summary>
    public float Height(float x, float z)
    {
        float gx = Math.Clamp((x + HalfSize) / Step, 0f, _n - 1.001f), gz = Math.Clamp((z + HalfSize) / Step, 0f, _n - 1.001f);
        int i = (int)gx, j = (int)gz;
        float tx = gx - i, tz = gz - j;
        int k = j * _n + i;
        float a = _grid[k], b = _grid[k + 1], c = _grid[k + _n], d = _grid[k + _n + 1];
        return a + (b - a) * tx + (c - a) * tz + (a - b - c + d) * tx * tz;
    }

    /// <summary>The height function itself, evaluated directly.</summary>
    public float Sample(float x, float z)
    {
        float h = 3.6f * Noise(x * 0.011f, z * 0.011f)
                + 1.3f * Noise(x * 0.034f + 17.3f, z * 0.034f - 9.1f)
                + 0.3f * Noise(x * 0.12f - 4.2f, z * 0.12f + 3.7f);
        // A broad, flatter plain in the middle, where the herds graze and the camera starts.
        float d = MathF.Sqrt(x * x + z * z);
        float plain = Math.Clamp((d - 30f) / 90f, 0f, 1f);
        // Hills rise beyond it, and settle again near the edge of the map so the land meets
        // the far plain without a cliff.
        float rise = Math.Clamp((d - 35f) / 110f, 0f, 1f);
        float edge = Math.Clamp((HalfSize - 8f - MathF.Max(MathF.Abs(x), MathF.Abs(z))) / 45f, 0f, 1f);
        // Hills only ever rise from the plain: valleys below it would dip under the far ground.
        float n = 0.5f + 0.5f * Noise(x * 0.0062f + 31.7f, z * 0.0062f - 17.2f);
        float hills = 15f * n * n + 3.5f * (0.5f + 0.5f * Noise(x * 0.017f - 8.3f, z * 0.017f + 5.1f));
        return h * (0.35f + 0.65f * plain) + hills * rise * rise * (3f - 2f * rise) * edge;
    }

    public float Height(Vector3 p) => Height(p.X, p.Z);

    public Vector3 Normal(float x, float z)
    {
        const float e = 0.5f;
        float dx = Height(x + e, z) - Height(x - e, z);
        float dz = Height(x, z + e) - Height(x, z - e);
        return new Vector3(-dx, 2f * e, -dz).Normalized();
    }

    public bool Inside(Vector3 p, float margin = 0f) =>
        MathF.Abs(p.X) < HalfSize - margin && MathF.Abs(p.Z) < HalfSize - margin;

    public Vector3 Clamp(Vector3 p, float margin) => new(
        Math.Clamp(p.X, -HalfSize + margin, HalfSize - margin), p.Y,
        Math.Clamp(p.Z, -HalfSize + margin, HalfSize - margin));

    /// <summary>Marches a ray over the heightfield, then refines the crossing.</summary>
    public bool Raycast(Vector3 from, Vector3 dir, float maxDistance, out float distance)
    {
        distance = 0f;
        const float step = 0.5f;
        float prevT = 0f;
        float prevGap = from.Y - Height(from.X, from.Z);
        if (prevGap < 0f) return true;
        for (float t = step; t <= maxDistance; t += step)
        {
            Vector3 p = from + dir * t;
            float gap = p.Y - Height(p.X, p.Z);
            if (gap < 0f)
            {
                float lo = prevT, hi = t;
                for (int i = 0; i < 8; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    Vector3 q = from + dir * mid;
                    if (q.Y - Height(q.X, q.Z) < 0f) hi = mid; else lo = mid;
                }
                distance = hi;
                return true;
            }
            prevT = t;
            prevGap = gap;
        }
        return false;
    }

    // Smooth value noise in [-1, 1] on a hashed lattice.
    float Noise(float x, float z)
    {
        int xi = (int)MathF.Floor(x), zi = (int)MathF.Floor(z);
        float fx = x - xi, fz = z - zi;
        float u = fx * fx * (3f - 2f * fx), v = fz * fz * (3f - 2f * fz);
        float a = Lattice(xi, zi), b = Lattice(xi + 1, zi), c = Lattice(xi, zi + 1), d = Lattice(xi + 1, zi + 1);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    float Lattice(int x, int z)
    {
        uint h = (uint)x * 374761393u + (uint)z * 668265263u + _seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
    }
}
