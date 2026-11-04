using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace TerrainDigging.Terrain;

/// <summary>Everything a save holds: player edits, structures, and what loading them must reproduce.</summary>
public sealed class SaveData
{
    public ulong Sequence;
    public int Seed;
    public string GeneratorId;
    public EditLog Edits;
    public List<Structure> Structures = new();

    /// <summary>How many removed cells the world had when saved. The load audit checks it.</summary>
    public int RemovedCells;

    /// <summary>The world's EditStateHash when saved. The load audit checks it.</summary>
    public ulong StateHash;

    public static SaveData From(ChunkStore store, ulong sequence) => new()
    {
        Sequence = sequence,
        Seed = store.Source.Seed,
        GeneratorId = store.Source.GeneratorId,
        Edits = store.ExportEdits(),
        Structures = store.Structures.OrderBy(s => s.Id).ToList(),
        RemovedCells = store.RemovedCount,
        StateHash = store.EditStateHash(),
    };
}

/// <summary>Where a save can be interrupted on purpose, to prove a crash can't hurt it.</summary>
public enum SaveCrashPoint
{
    None,

    /// <summary>The temp file is written and verified, but the process dies before swapping it in.</summary>
    AfterTempWritten,
}

public sealed class SimulatedCrashException : Exception
{
    public SimulatedCrashException() : base("simulated crash during save") { }
}

public sealed class SaveReport
{
    public bool Ok;
    public string Error;
    public int Bytes;
    public double Milliseconds;
    public int Cells;
}

public sealed class LoadResult
{
    public bool Ok;
    public string Error;
    public string LoadedFrom;
    public SaveData Data;

    /// <summary>A new store holding the audited state. The caller swaps it in only if Ok.</summary>
    public ChunkStore Store;

    public double Milliseconds;
}

/// <summary>
/// Saves edits atomically (rule 11). The new file is written beside the old one as .tmp,
/// flushed to disk, read back and checked, and only then swapped in, with the previous save
/// kept as .bak. If the game dies at any point, the old save is untouched.
///
/// Loading never trusts a file: it checks the format and checksums, rebuilds the edits on a
/// separate store, and audits that every saved cell and structure came back and that the
/// result hashes to what was saved. Anything else is refused, never half-loaded.
///
/// Layout (little-endian):
///   "GHSV" u16 version u16 0 u64 sequence i32 seed u16 len + generator id (UTF-8)
///   edit log (see EditLog)
///   u32 structureCount, per structure: i32 id, i32×3 min, i32×3 max
///   u32 removedCells u64 stateHash u32 crc32(everything before)
/// </summary>
public static class TerrainSave
{
    public const uint Magic = 0x56534847; // "GHSV"
    public const ushort FormatVersion = 1;

    public static string TempPath(string path) => path + ".tmp";
    public static string BackupPath(string path) => path + ".bak";

    // ---- Encoding ---------------------------------------------------------------------

    public static byte[] Encode(SaveData data)
    {
        var w = new ByteWriter();
        w.U32(Magic);
        w.U16(FormatVersion);
        w.U16(0);
        w.U64(data.Sequence);
        w.I32(data.Seed);
        byte[] id = Encoding.UTF8.GetBytes(data.GeneratorId ?? "");
        w.U16((ushort)id.Length);
        w.Bytes(id);
        data.Edits.Write(w);
        w.U32((uint)data.Structures.Count);
        foreach (Structure s in data.Structures)
        {
            w.I32(s.Id);
            w.I32(s.Box.Min.X);
            w.I32(s.Box.Min.Y);
            w.I32(s.Box.Min.Z);
            w.I32(s.Box.Max.X);
            w.I32(s.Box.Max.Y);
            w.I32(s.Box.Max.Z);
        }
        w.U32((uint)data.RemovedCells);
        w.U64(data.StateHash);
        w.U32(Crc32.Compute(w.Written));
        return w.ToArray();
    }

    /// <summary>Parses and checksums a save. Throws CorruptDataException with the reason.</summary>
    public static SaveData Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8) throw new CorruptDataException("file is too short to be a save");
        uint crc = Crc32.Compute(bytes[..^4]);
        if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes[^4..]) != crc)
            throw new CorruptDataException("file checksum mismatch (the file is damaged)");

        var r = new ByteReader(bytes[..^4]);
        if (r.U32() != Magic) throw new CorruptDataException("not a GalHack terrain save");
        ushort version = r.U16();
        if (version != FormatVersion) throw new CorruptDataException($"unsupported save version {version}");
        if (r.U16() != 0) throw new CorruptDataException("reserved header field is not zero");

        var data = new SaveData { Sequence = r.U64(), Seed = r.I32() };
        int idLength = r.U16();
        data.GeneratorId = Encoding.UTF8.GetString(r.Bytes(idLength));
        data.Edits = EditLog.Read(ref r);
        uint structureCount = r.U32();
        if (structureCount > r.Remaining / 28) throw new CorruptDataException($"structure count {structureCount} is larger than the data");
        for (uint i = 0; i < structureCount; i++)
        {
            int structureId = r.I32();
            var min = new Int3(r.I32(), r.I32(), r.I32());
            var max = new Int3(r.I32(), r.I32(), r.I32());
            data.Structures.Add(new Structure(structureId, new CellBox(min, max)));
        }
        data.RemovedCells = (int)r.U32();
        data.StateHash = r.U64();
        if (r.Remaining != 0) throw new CorruptDataException($"{r.Remaining} unexpected trailing bytes");
        return data;
    }

    // ---- Saving -----------------------------------------------------------------------

    public static SaveReport Save(ChunkStore store, string path, ulong sequence, SaveCrashPoint crash = SaveCrashPoint.None)
    {
        var timer = Stopwatch.StartNew();
        SaveData data = SaveData.From(store, sequence);
        byte[] bytes = Encode(data);
        string tmp = TempPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
        {
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }

        // Verify what actually reached the disk before it replaces anything.
        byte[] back = File.ReadAllBytes(tmp);
        if (!back.AsSpan().SequenceEqual(bytes))
            return new SaveReport { Error = "temp file read back differently; old save kept" };
        try
        {
            Decode(back);
        }
        catch (CorruptDataException e)
        {
            return new SaveReport { Error = $"temp file failed verification ({e.Message}); old save kept" };
        }

        if (crash == SaveCrashPoint.AfterTempWritten) throw new SimulatedCrashException();

        // An NTFS rename is atomic: the save path holds either the whole old file or the whole new one.
        if (File.Exists(path)) File.Replace(tmp, path, BackupPath(path), ignoreMetadataErrors: true);
        else File.Move(tmp, path);

        return new SaveReport { Ok = true, Bytes = bytes.Length, Cells = data.RemovedCells, Milliseconds = timer.Elapsed.TotalMilliseconds };
    }

    // ---- Loading ----------------------------------------------------------------------

    /// <summary>Loads the save, or its backup if the save itself fails the audit.</summary>
    public static LoadResult LoadLatest(string path, BaseCache baseCache)
    {
        LoadResult main = Load(path, baseCache);
        if (main.Ok) return main;
        LoadResult backup = Load(BackupPath(path), baseCache);
        if (backup.Ok)
        {
            backup.Error = $"save refused ({main.Error}); loaded the backup";
            return backup;
        }
        return main;
    }

    /// <summary>Loads and audits one file. Never changes any live state.</summary>
    public static LoadResult Load(string path, BaseCache baseCache)
    {
        var timer = Stopwatch.StartNew();
        LoadResult Fail(string why) => new() { Error = why, LoadedFrom = path, Milliseconds = timer.Elapsed.TotalMilliseconds };

        if (!File.Exists(path)) return Fail("no save file");
        SaveData data;
        try
        {
            data = Decode(File.ReadAllBytes(path));
        }
        catch (CorruptDataException e)
        {
            return Fail(e.Message);
        }
        catch (IOException e)
        {
            return Fail($"could not read the file: {e.Message}");
        }

        ITerrainSource source = baseCache.Source;
        if (data.Seed != source.Seed) return Fail($"save is for seed {data.Seed}, this world is {source.Seed}");
        if (data.GeneratorId != source.GeneratorId) return Fail($"save was dug on terrain '{data.GeneratorId}', this is '{source.GeneratorId}'");

        // Rebuild on a separate store: the live world is untouched until the audit passes.
        var store = new ChunkStore(baseCache);
        ApplyReport report = store.ApplyDiff(data.Edits);
        if (report.Invalid.Count > 0)
            return Fail($"{report.Invalid.Count} saved edits can't exist on this terrain (first at {report.Invalid[0]})");

        int missing = data.Edits.Cells().Count(cell => !store.IsRemoved(cell));
        if (missing > 0) return Fail($"{missing} saved edits did not come back");
        if (store.RemovedCount != data.RemovedCells)
            return Fail($"save should hold {data.RemovedCells} removed cells but holds {store.RemovedCount}: edits were lost");

        foreach (Structure s in data.Structures)
        {
            Int3 size = s.Box.Size;
            if (size.X < 1 || size.Y < 1 || size.Z < 1 || size.X > ChunkStore.MaxStructureSize ||
                size.Y > ChunkStore.MaxStructureSize || size.Z > ChunkStore.MaxStructureSize)
                return Fail($"structure #{s.Id} has an impossible size {size}");
            if (store.Structures.Any(o => o.Id == s.Id || o.Box.Intersects(s.Box)))
                return Fail($"structure #{s.Id} duplicates or overlaps another");
            if (ChunkStore.BoxCells(s.Box).Any(store.IsSolid))
                return Fail($"structure #{s.Id} would be buried in terrain");
            store.RestoreStructure(s);
        }

        if (store.EditStateHash() != data.StateHash) return Fail("loaded state doesn't match the saved fingerprint");

        return new LoadResult
        {
            Ok = true,
            LoadedFrom = path,
            Data = data,
            Store = store,
            Milliseconds = timer.Elapsed.TotalMilliseconds,
        };
    }
}
