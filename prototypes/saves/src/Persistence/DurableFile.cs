using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GalHack.Persistence;

/// <summary>
/// The only disk operations a commit uses: write a whole file, flush files all the way to the
/// disk, atomically rename one file over another, and delete. Each write has two crash points,
/// halfway through (a torn file) and once it is written, so tests can kill a save at any moment.
/// </summary>
public static class DurableFile
{
    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/>, replacing any stale file of
    /// that name. With <paramref name="flush"/> it is on the disk itself before this returns;
    /// without, it is with the OS (safe from a process crash, not yet from a power cut) until
    /// <see cref="FlushAll"/>.
    /// </summary>
    public static void Write(string path, byte[] bytes, FaultInjector faults, string step, bool flush = true)
    {
        bool tear = faults.Due(step + ": half written");
        // bufferSize 0: no FileStream buffer, so every byte goes straight to the OS.
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 0))
        {
            stream.Write(bytes, 0, tear ? bytes.Length / 2 : bytes.Length);
            if (flush) stream.Flush(flushToDisk: true);
        }
        if (tear) faults.Crash(step + ": half written");
        faults.Point(step + (flush ? ": flushed" : ": written"));
    }

    /// <summary>
    /// Flushes already-written files all the way to the disk, several at once: a flush mostly
    /// waits on the drive, and a drive takes many in parallel. Returns once every one is done.
    /// </summary>
    public static void FlushAll(IReadOnlyList<string> paths)
    {
        System.Threading.Tasks.Parallel.ForEach(paths, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 16 }, path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 0);
            stream.Flush(flushToDisk: true);
        });
    }

    /// <summary>
    /// Renames <paramref name="source"/> over <paramref name="destination"/> in one atomic step:
    /// a reader sees the old file or the new one, never neither and never a mix. On Windows it
    /// asks for write-through, so the rename is on the disk before we go on to delete anything
    /// the old file referenced.
    /// </summary>
    public static void Replace(string source, string destination)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!MoveFileExW(source, destination, MoveFileReplaceExisting | MoveFileWriteThrough))
                        throw new IOException($"could not replace {destination}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                }
                else
                {
                    File.Move(source, destination, overwrite: true);
                }
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                // A virus scanner or indexer can hold the file for a moment. Nothing has changed yet, so wait and retry.
                Thread.Sleep(25 * attempt);
            }
        }
    }

    public static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    const uint MoveFileReplaceExisting = 0x1, MoveFileWriteThrough = 0x8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileExW(string existingFileName, string newFileName, uint flags);
}
