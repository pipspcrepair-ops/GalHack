using System;

namespace GalHack.Terrain;

/// <summary>
/// Seeded 3D gradient noise that repeats exactly every planet width in x and every planet height
/// in z, so anything built from it (caves, ore lumps) joins across both seams of a torus world.
/// The lattice spacing is nudged so a whole number of cells fits each way; y does not repeat.
/// Pure integer hashing and float arithmetic: identical on every machine.
/// </summary>
public sealed class PeriodicNoise
{
    readonly uint _seed;
    readonly int _periodX, _periodZ;
    readonly double _fx, _fy, _fz;

    public PeriodicNoise(ulong seed, double width, double height, double wavelength)
    {
        _seed = (uint)(seed ^ (seed >> 32)) * 0x9E3779B1u + 0x7F4A7C15u;
        _periodX = Math.Max(1, (int)Math.Round(width / wavelength));
        _periodZ = Math.Max(1, (int)Math.Round(height / wavelength));
        _fx = _periodX / width;
        _fz = _periodZ / height;
        _fy = 1.0 / wavelength;
    }

    /// <summary>Roughly in [-1, 1].</summary>
    public float Sample(double x, double y, double z)
    {
        double px = x * _fx, py = y * _fy, pz = z * _fz;
        double fx = Math.Floor(px), fy = Math.Floor(py), fz = Math.Floor(pz);
        int ix = (int)(long)fx, iy = (int)(long)fy, iz = (int)(long)fz;
        float dx = (float)(px - fx), dy = (float)(py - fy), dz = (float)(pz - fz);
        float u = Fade(dx), v = Fade(dy), w = Fade(dz);

        int x0 = Wrap(ix, _periodX), x1 = Wrap(ix + 1, _periodX);
        int z0 = Wrap(iz, _periodZ), z1 = Wrap(iz + 1, _periodZ);
        float n000 = Grad(Lattice(x0, iy, z0), dx, dy, dz);
        float n100 = Grad(Lattice(x1, iy, z0), dx - 1, dy, dz);
        float n010 = Grad(Lattice(x0, iy + 1, z0), dx, dy - 1, dz);
        float n110 = Grad(Lattice(x1, iy + 1, z0), dx - 1, dy - 1, dz);
        float n001 = Grad(Lattice(x0, iy, z1), dx, dy, dz - 1);
        float n101 = Grad(Lattice(x1, iy, z1), dx - 1, dy, dz - 1);
        float n011 = Grad(Lattice(x0, iy + 1, z1), dx, dy - 1, dz - 1);
        float n111 = Grad(Lattice(x1, iy + 1, z1), dx - 1, dy - 1, dz - 1);

        float a = n000 + u * (n100 - n000), b = n010 + u * (n110 - n010);
        float c = n001 + u * (n101 - n001), d = n011 + u * (n111 - n011);
        float e = a + v * (b - a), f = c + v * (d - c);
        return e + w * (f - e);
    }

    static int Wrap(int i, int period) => (i % period + period) % period;

    static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    uint Lattice(int x, int y, int z)
    {
        uint h = _seed ^ (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)z * 0xCB1AB31Fu;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        return h ^ (h >> 15);
    }

    static float Grad(uint hash, float x, float y, float z)
    {
        // Perlin's twelve edge gradients, four of them repeated to make sixteen.
        switch (hash & 15)
        {
            case 0: return x + y;
            case 1: return -x + y;
            case 2: return x - y;
            case 3: return -x - y;
            case 4: return x + z;
            case 5: return -x + z;
            case 6: return x - z;
            case 7: return -x - z;
            case 8: return y + z;
            case 9: return -y + z;
            case 10: return y - z;
            case 11: return -y - z;
            case 12: return x + y;
            case 13: return -y + z;
            case 14: return -x + y;
            default: return -y - z;
        }
    }
}
