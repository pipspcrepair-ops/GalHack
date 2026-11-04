using System;
using System.Buffers.Binary;
using System.IO;

namespace TerrainDigging.Terrain;

/// <summary>Thrown when bytes don't parse as a valid edit log or save. The message says what was wrong.</summary>
public sealed class CorruptDataException : Exception
{
    public CorruptDataException(string message) : base(message) { }
}

/// <summary>Little-endian writer: file and network bytes must not depend on the machine.</summary>
public sealed class ByteWriter
{
    readonly MemoryStream _stream = new();

    public int Length => (int)_stream.Length;
    public ReadOnlySpan<byte> Written => _stream.GetBuffer().AsSpan(0, Length);
    public byte[] ToArray() => _stream.ToArray();

    public void U8(byte v) => _stream.WriteByte(v);

    public void U16(ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        _stream.Write(b);
    }

    public void U32(uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        _stream.Write(b);
    }

    public void I32(int v) => U32(unchecked((uint)v));

    public void U64(ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, v);
        _stream.Write(b);
    }

    public void Bytes(ReadOnlySpan<byte> data) => _stream.Write(data);

    public void VarUInt(uint v)
    {
        while (v >= 0x80)
        {
            U8((byte)(v | 0x80));
            v >>= 7;
        }
        U8((byte)v);
    }
}

/// <summary>Bounds-checked little-endian reader. Every overrun is a CorruptDataException, never garbage.</summary>
public ref struct ByteReader
{
    readonly ReadOnlySpan<byte> _data;
    int _pos;

    public ByteReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    public int Position => _pos;
    public int Remaining => _data.Length - _pos;
    public ReadOnlySpan<byte> Slice(int start, int length) => _data.Slice(start, length);

    ReadOnlySpan<byte> Take(int n)
    {
        if (n < 0 || _pos + n > _data.Length) throw new CorruptDataException($"unexpected end of data at byte {_pos}");
        ReadOnlySpan<byte> s = _data.Slice(_pos, n);
        _pos += n;
        return s;
    }

    public byte U8() => Take(1)[0];
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public ReadOnlySpan<byte> Bytes(int n) => Take(n);

    public uint VarUInt()
    {
        uint result = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte b = U8();
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                // Reject padded encodings so every value has exactly one byte form.
                if (b == 0 && shift > 0) throw new CorruptDataException($"non-canonical varint at byte {_pos - 1}");
                return result;
            }
        }
        throw new CorruptDataException($"varint too long at byte {_pos}");
    }
}
