using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Persistence;

/// <summary>
/// What a load found. Either everything recorded came back, checksums and audit included, and
/// <see cref="Table"/> holds it; or the load refused, <see cref="Problems"/> says why, and
/// <see cref="Backups"/> lists what can be restored instead. There is no partial result.
/// </summary>
public sealed class LoadResult
{
    public bool Ok => Table != null && Problems.Count == 0;

    /// <summary>The records, or null if the load refused.</summary>
    public RecordTable Table { get; internal set; }

    public long Generation { get; internal set; } = -1;
    public DateTime SavedAtUtc { get; internal set; }

    /// <summary>What the manifest promised, and what the records actually added up to.</summary>
    public AuditCounts Expected { get; internal set; }
    public AuditCounts Found { get; internal set; }

    /// <summary>Why the load refused, one line per thing wrong. Empty when it succeeded.</summary>
    public List<string> Problems { get; } = new();

    /// <summary>Things worth knowing that are not problems: migrations, unknown types kept as they are.</summary>
    public List<string> Notes { get; } = new();

    /// <summary>True when there is simply no save in the folder yet.</summary>
    public bool NoSave { get; internal set; }

    public int Checked { get; internal set; }
    public int Migrated { get; internal set; }

    /// <summary>Files from a save that was cut off before its commit point: it was rolled back, as if it never started.</summary>
    public int UnfinishedFilesRemoved { get; internal set; }

    /// <summary>Superseded files whose cleanup was cut off after a commit: the commit had landed.</summary>
    public int LeftoverFilesRemoved { get; internal set; }

    public bool TempManifestFound { get; internal set; }

    /// <summary>True when the last session stopped in the middle of a save, and this load recovered from it.</summary>
    public bool RecoveredFromInterruptedSave => UnfinishedFilesRemoved > 0 || LeftoverFilesRemoved > 0 || TempManifestFound;

    /// <summary>Backups on disk, newest first. Filled in when a load refuses, so the host can pick one.</summary>
    public List<BackupInfo> Backups { get; internal set; } = new();

    public TimeSpan Elapsed { get; internal set; }

    public string Summary()
    {
        if (!Ok)
        {
            IEnumerable<string> shown = Problems.Take(12);
            string more = Problems.Count > 12 ? $"\n  ...and {Problems.Count - 12} more" : "";
            return $"LOAD REFUSED ({Problems.Count} problem{(Problems.Count == 1 ? "" : "s")}):\n  " + string.Join("\n  ", shown) + more;
        }
        string text = $"generation {Generation} loaded, audit OK: {Found} ({Checked} checksums verified, {Elapsed.TotalMilliseconds:F0} ms)";
        if (Migrated > 0) text += $"; {Migrated} records migrated";
        if (RecoveredFromInterruptedSave)
        {
            text += UnfinishedFilesRemoved > 0 || (TempManifestFound && LeftoverFilesRemoved == 0)
                ? $"; an interrupted save was rolled back ({UnfinishedFilesRemoved} unfinished files removed)"
                : $"; an interrupted cleanup was finished ({LeftoverFilesRemoved} old files removed)";
        }
        return text;
    }
}

/// <summary>What a commit did.</summary>
public sealed class CommitResult
{
    public long Generation { get; init; }
    public int Written { get; init; }
    public int Removed { get; init; }
    public int FilesDeleted { get; init; }
    public long BytesWritten { get; init; }
    public TimeSpan Elapsed { get; init; }

    public override string ToString() =>
        $"generation {Generation}: {Written} records written, {Removed} removed, {FilesDeleted} old files deleted, {BytesWritten / 1024.0:F1} KB, {Elapsed.TotalMilliseconds:F1} ms";
}
