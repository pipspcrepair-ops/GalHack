using System;
using System.Collections.Generic;
using System.Numerics;

namespace TerrainDigging.Terrain;

/// <summary>
/// A set of removed cells, grouped by chunk: the unit of networking and saving (rule 8).
/// A single dig's diff, a batch of diffs and a whole world's edits all use this one type.
///
/// It has exactly one byte form. Chunks are sorted, cells are sorted, the cell encoding is
/// chosen by size alone, and the reader rejects anything else. So equal logs always produce
/// equal bytes, which makes checksums comparable between machines.
///
/// Layout (little-endian):
///   "GHTE" u16 version u16 0 u32 chunkCount u32 cellCount
///   per chunk: i32 x, y, z  u8 encoding  u16 count  payload  u32 crc32(record)
///     encoding 0: ascending cell indices as varint deltas (first one absolute)
///     encoding 1: the 4096-bit mask (64 Ã— u64), used when the list would be 512+ bytes
///   u32 crc32(everything before)
/// </summary>
public sealed class EditLog
{
    public const uint Magic = 0x45544847; // "GHTE"
    public const ushort FormatVersion = 1;
    const int MaskBytes = ChunkStore.MaskWords * 8;

    readonly SortedDictionary<Int3, ulong[]> _chunks = new();

    public int ChunkCount => _chunks.Count;
    public int CellCount { get; private set; }
    public IEnumerable<Int3> Chunks => _chunks.Keys;

    public bool Add(Int3 cell)
    {
        Int3 chunk = ChunkStore.ChunkOf(cell);
        int i = ChunkStore.LocalIndex(cell);
        if (!_chunks.TryGetValue(chunk, out ulong[] mask)) _chunks[chunk] = mask = new ulong[ChunkStore.MaskWords];
        ulong bit = 1ul << (i & 63);
        if ((mask[i >> 6] & bit) != 0) return false;
        mask[i >> 6] |= bit;
        CellCount++;
        return true;
    }

    /// <summary>Unions a chunk's removed-cell mask into this log.</summary>
    public void SetChunk(Int3 chunk, ulong[] mask)
    {
        for (int w = 0; w < ChunkStore.MaskWords; w++)
        {
            ulong bits = mask[w];
            while (bits != 0)
            {
                int b = BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                Add(ChunkStore.CellAt(chunk, w * 64 + b));
            }
        }
    }

    public void Merge(EditLog other)
    {
        foreach ((Int3 chunk, ulong[] mask) in other._chunks) SetChunk(chunk, mask);
    }

    public bool Contains(Int3 cell) =>
        _chunks.TryGetValue(ChunkStore.ChunkOf(cell), out ulong[] mask) &&
        (mask[ChunkStore.LocalIndex(cell) >> 6] & (1ul << (ChunkStore.LocalIndex(cell) & 63))) != 0;

    /// <summary>All cells, in canonical order (chunks sorted, then cell index).</summary>
    public IEnumerable<Int3> Cells()
    {
        foreach ((Int3 chunk, ulong[] mask) in _chunks)
            for (int w = 0; w < ChunkStore.MaskWords; w++)
            {
                ulong bits = mask[w];
                while (bits != 0)
                {
                    int b = BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    yield return ChunkStore.CellAt(chunk, w * 64 + b);
                }
            }
    }

    public bool ContentEquals(EditLog other)
    {
        if (other.CellCount != CellCount || other.ChunkCount != ChunkCount) return false;
        foreach ((Int3 chunk, ulong[] mask) in _chunks)
        {
            if (!other._chunks.TryGetValue(chunk, out ulong[] theirs)) return false;
            for (int w = 0; w < ChunkStore.MaskWords; w++)
                if (mask[w] != theirs[w]) return false;
        }
        return true;
    }

    // ---- Serialisation ----------------------------------------------------------------

    public byte[] Serialize()
    {
        var w = new ByteWriter();
        Write(w);
        return w.ToArray();
    }

    public void Write(ByteWriter w)
    {
        int start = w.Length;
        w.U32(Magic);
        w.U16(FormatVersion);
        w.U16(0);
        w.U32((uint)_chunks.Count);
        w.U32((uint)CellCount);

        var list = new ByteWriter();
        foreach ((Int3 chunk, ulong[] mask) in _chunks)
        {
            int recordStart = w.Length;
            w.I32(chunk.X);
            w.I32(chunk.Y);
            w.I32(chunk.Z);
            int count = PopCount(mask);
            byte[] deltas = EncodeDeltas(mask);
            if (deltas.Length < MaskBytes)
            {
                w.U8(0);
                w.U16((ushort)count);
                w.Bytes(deltas);
            }
            else
            {
                w.U8(1);
                w.U16((ushort)count);
                foreach (ulong word in mask) w.U64(word);
            }
            w.U32(Crc32.Compute(w.Written[recordStart..]));
        }
        w.U32(Crc32.Compute(w.Written[start..]));
    }

    public static EditLog Deserialize(ReadOnlySpan<byte> data)
    {
        var r = new ByteReader(data);
        EditLog log = Read(ref r);
        if (r.Remaining != 0) throw new CorruptDataException($"{r.Remaining} unexpected trailing bytes");
        return log;
    }

    public static EditLog Read(ref ByteReader r)
    {
        int start = r.Position;
        if (r.U32() != Magic) throw new CorruptDataException("not an edit log (bad magic)");
        ushort version = r.U16();
        if (version != FormatVersion) throw new CorruptDataException($"unsupported edit log version {version}");
        if (r.U16() != 0) throw new CorruptDataException("reserved header field is not zero");
        uint chunkCount = r.U32();
        uint cellCount = r.U32();
        if (chunkCount > r.Remaining / 17) throw new CorruptDataException($"chunk count {chunkCount} is larger than the data");

        var log = new EditLog();
        Int3? previous = null;
        for (uint n = 0; n < chunkCount; n++)
        {
            int recordStart = r.Position;
            var chunk = new Int3(r.I32(), r.I32(), r.I32());
            if (previous is Int3 p && chunk.CompareTo(p) <= 0) throw new CorruptDataException($"chunk {chunk} out of order");
            previous = chunk;

            byte encoding = r.U8();
            int count = r.U16();
            if (count < 1 || count > ChunkStore.Volume) throw new CorruptDataException($"chunk {chunk} has {count} cells");

            var mask = new ulong[ChunkStore.MaskWords];
            if (encoding == 0)
            {
                int index = -1;
                for (int k = 0; k < count; k++)
                {
                    uint step = r.VarUInt();
                    if (k > 0 && step == 0) throw new CorruptDataException($"chunk {chunk} repeats a cell");
                    long next = (long)index + step + (k == 0 ? 1 : 0);
                    if (next >= ChunkStore.Volume) throw new CorruptDataException($"chunk {chunk} cell index {next} out of range");
                    index = (int)next;
                    mask[index >> 6] |= 1ul << (index & 63);
                }
                if (EncodeDeltas(mask).Length >= MaskBytes) throw new CorruptDataException($"chunk {chunk} uses a non-canonical encoding");
            }
            else if (encoding == 1)
            {
                for (int w = 0; w < ChunkStore.MaskWords; w++) mask[w] = r.U64();
                if (PopCount(mask) != count) throw new CorruptDataException($"chunk {chunk} mask holds {PopCount(mask)} cells, header says {count}");
                if (EncodeDeltas(mask).Length < MaskBytes) throw new CorruptDataException($"chunk {chunk} uses a non-canonical encoding");
            }
            else
            {
                throw new CorruptDataException($"chunk {chunk} has unknown encoding {encoding}");
            }

            uint crc = Crc32.Compute(r.Slice(recordStart, r.Position - recordStart));
            if (r.U32() != crc) throw new CorruptDataException($"checksum mismatch in chunk {chunk}");
            log._chunks[chunk] = mask;
            log.CellCount += count;
        }

        if (log.CellCount != cellCount) throw new CorruptDataException($"header says {cellCount} cells, records hold {log.CellCount}");
        uint total = Crc32.Compute(r.Slice(start, r.Position - start));
        if (r.U32() != total) throw new CorruptDataException("edit log checksum mismatch");
        return log;
    }

    static int PopCount(ulong[] mask)
    {
        int n = 0;
        foreach (ulong w in mask) n += BitOperations.PopCount(w);
        return n;
    }

    /// <summary>
    /// Ascending indices as varints: the first index itself, then the gap to the previous one.
    /// Dug holes are compact, so gaps are small and most cells cost a single byte.
    /// </summary>
    static byte[] EncodeDeltas(ulong[] mask)
    {
        var w = new ByteWriter();
        int previous = -1;
        for (int word = 0; word < ChunkStore.MaskWords; word++)
        {
            ulong bits = mask[word];
            while (bits != 0)
            {
                int index = word * 64 + BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                w.VarUInt((uint)(previous < 0 ? index : index - previous));
                previous = index;
            }
        }
        return w.ToArray();
    }
}
