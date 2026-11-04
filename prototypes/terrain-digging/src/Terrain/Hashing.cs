using System;

namespace TerrainDigging.Terrain;

/// <summary>CRC-32 (IEEE 802.3) for detecting corrupted save files and network messages.</summary>
public static class Crc32
{
    static readonly uint[] Table = BuildTable();

    static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}

/// <summary>
/// FNV-1a, 64-bit. Used for state and mesh fingerprints that must be identical on every
/// machine (string.GetHashCode and HashCode are randomised per process, so they can't be).
/// </summary>
public struct Fnv64
{
    ulong _hash;

    public static Fnv64 Create() => new() { _hash = 0xCBF29CE484222325ul };

    public readonly ulong Value => _hash;

    public void Add(ReadOnlySpan<byte> data)
    {
        ulong h = _hash;
        foreach (byte b in data) h = (h ^ b) * 0x100000001B3ul;
        _hash = h;
    }

    public void Add(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        Add(bytes);
    }

    public void Add(ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        Add(bytes);
    }

    public void Add(Int3 v)
    {
        Add(v.X);
        Add(v.Y);
        Add(v.Z);
    }
}
