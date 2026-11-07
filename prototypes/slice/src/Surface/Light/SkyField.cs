using System;

namespace Slice.Surface;

/// <summary>
/// How much open sky reaches each metre of air around the eye: the block games' flood fill. Every
/// air cell with nothing above it is fully open; from those the light creeps sideways and round
/// corners, losing a step a metre, so a cave mouth, a door or a breach fades out over
/// <see cref="Reach"/> metres and a sealed room stays at exactly zero, at any hour.
/// It says nothing about time of day: the sky's own colour and strength are multiplied in later.
/// </summary>
public sealed class SkyField
{
    public const int SizeX = 128, SizeY = 64, SizeZ = 128;
    public const int Count = SizeX * SizeY * SizeZ;

    /// <summary>How far daylight gets from an opening, metres.</summary>
    public const int Reach = 14;
    const int Step = 255 / Reach;

    public int MinX, MinY, MinZ;

    /// <summary>1 where light can't pass.</summary>
    public readonly byte[] Solid = new byte[Count];

    /// <summary>0 (no sky reaches here) to 255 (open sky), layered by height: one layer a texture slice.</summary>
    public readonly byte[] Open = new byte[Count];

    /// <summary>Columns under something taller than the grid reaches: a mountain, a tower's upper floors.</summary>
    public readonly bool[] Roofed = new bool[SizeX * SizeZ];

    readonly int[] _queue = new int[Count];

    public static int Index(int x, int y, int z) => (y * SizeZ + z) * SizeX + x;

    public bool Holds(int x, int y, int z) =>
        (uint)(x - MinX) < SizeX && (uint)(y - MinY) < SizeY && (uint)(z - MinZ) < SizeZ;

    /// <summary>Starts over for a grid with its low corner here.</summary>
    public void Clear(int minX, int minY, int minZ)
    {
        (MinX, MinY, MinZ) = (minX, minY, minZ);
        Array.Clear(Solid);
        Array.Clear(Roofed);
    }

    /// <summary>Fills <see cref="Open"/> from <see cref="Solid"/> and <see cref="Roofed"/>.</summary>
    public void Flood()
    {
        Array.Clear(Open);
        int head = 0, tail = 0;
        for (int z = 0; z < SizeZ; z++)
        for (int x = 0; x < SizeX; x++)
        {
            if (Roofed[z * SizeX + x]) continue;
            for (int y = SizeY - 1; y >= 0; y--)
            {
                int i = Index(x, y, z);
                if (Solid[i] != 0) break;
                Open[i] = 255;
                _queue[tail++] = i;
            }
        }
        const int Layer = SizeX * SizeZ;
        while (head < tail)
        {
            int i = _queue[head++], left = Open[i] - Step;
            if (left <= 0) continue;
            int x = i % SizeX, z = i / SizeX % SizeZ, y = i / Layer;
            if (x > 0) Spread(i - 1, left, ref tail);
            if (x < SizeX - 1) Spread(i + 1, left, ref tail);
            if (z > 0) Spread(i - SizeX, left, ref tail);
            if (z < SizeZ - 1) Spread(i + SizeX, left, ref tail);
            if (y > 0) Spread(i - Layer, left, ref tail);
            if (y < SizeY - 1) Spread(i + Layer, left, ref tail);
        }
    }

    void Spread(int i, int level, ref int tail)
    {
        if (Solid[i] != 0 || Open[i] >= level) return;
        Open[i] = (byte)level;
        _queue[tail++] = i;
    }

    /// <summary>0 to 1 at a cell, the best of its neighbours if it is itself solid. Null outside the grid.</summary>
    public float? At(int x, int y, int z)
    {
        if (!Holds(x, y, z)) return null;
        int i = Index(x - MinX, y - MinY, z - MinZ);
        if (Solid[i] == 0) return Open[i] / 255f;
        int best = 0;
        for (int d = 0; d < 6; d++)
        {
            int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0), nz = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
            if (Holds(nx, ny, nz)) best = Math.Max(best, Open[Index(nx - MinX, ny - MinY, nz - MinZ)]);
        }
        return best / 255f;
    }

    public bool IsSolid(int x, int y, int z) => Holds(x, y, z) && Solid[Index(x - MinX, y - MinY, z - MinZ)] != 0;
}
