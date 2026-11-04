using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GalHack.Persistence;

/// <summary>
/// One snapshot in the backups folder: a zip holding a committed manifest and exactly the record
/// files it lists, so it can be checked and restored on its own. Everything about it is in its
/// name, e.g. auto-20261104-183000-g412.zip.
/// </summary>
public sealed class BackupInfo
{
    public string Name { get; init; }
    public string Path { get; init; }

    /// <summary>"auto" (the schedule), "manual" (the console), or "prerestore" (taken just before a restore).</summary>
    public string Kind { get; init; }

    public DateTime TakenAtUtc { get; init; }
    public long Generation { get; init; }
    public long Bytes { get; init; }

    public static string MakeName(string kind, DateTime utc, long generation, int copy = 1)
    {
        if (!SaveRecord.IsValidId(kind) || kind.Contains('-')) throw new ArgumentException($"bad backup kind '{kind}'");
        string suffix = copy > 1 ? $"-{copy}" : "";
        return $"{kind}-{utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-g{generation}{suffix}.zip";
    }

    /// <summary>Reads a backup's name. Null for anything that isn't one of ours.</summary>
    public static BackupInfo TryParse(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        if (!name.EndsWith(".zip", StringComparison.Ordinal)) return null;
        string[] parts = name[..^4].Split('-');
        if (parts.Length is < 4 or > 5) return null;
        if (!DateTime.TryParseExact(parts[1] + parts[2], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime taken)) return null;
        if (parts[3].Length < 2 || parts[3][0] != 'g' || !long.TryParse(parts[3].AsSpan(1), out long generation)) return null;
        long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        return new BackupInfo { Name = name, Path = path, Kind = parts[0], TakenAtUtc = taken, Generation = generation, Bytes = bytes };
    }

    public override string ToString() => $"{Name} ({Kind}, generation {Generation}, {Bytes / 1024.0:F0} KB)";
}

/// <summary>
/// How long backups are kept: every snapshot for the last day, then the newest of each UTC day
/// for a month. The newest few are always kept however old they are, so a long break from the
/// game can never leave a save with no backups at all.
/// </summary>
public sealed class BackupPolicy
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan KeepAllFor { get; init; } = TimeSpan.FromDays(1);
    public TimeSpan KeepDailyFor { get; init; } = TimeSpan.FromDays(30);
    public int KeepNewest { get; init; } = 3;

    /// <summary>The backups this policy says to delete now.</summary>
    public List<BackupInfo> Expired(IReadOnlyList<BackupInfo> backups, DateTime nowUtc)
    {
        List<BackupInfo> newestFirst = backups.OrderByDescending(b => b.TakenAtUtc).ThenByDescending(b => b.Name, StringComparer.Ordinal).ToList();
        var newestOfItsDay = new HashSet<string>(
            newestFirst.GroupBy(b => b.TakenAtUtc.Date).Select(day => day.First().Name), StringComparer.Ordinal);

        var expired = new List<BackupInfo>();
        for (int i = 0; i < newestFirst.Count; i++)
        {
            BackupInfo b = newestFirst[i];
            TimeSpan age = nowUtc - b.TakenAtUtc;
            bool keep = i < KeepNewest
                || age < KeepAllFor
                || (age < KeepDailyFor && newestOfItsDay.Contains(b.Name));
            if (!keep) expired.Add(b);
        }
        return expired;
    }
}

/// <summary>
/// Takes a snapshot whenever one is due and applies the retention policy. Call
/// <see cref="Tick"/> every so often from the thread that commits (the store serialises them anyway).
/// </summary>
public sealed class BackupScheduler
{
    readonly SaveStore _store;

    public BackupScheduler(SaveStore store, BackupPolicy policy = null)
    {
        _store = store;
        Policy = policy ?? new BackupPolicy();
    }

    public BackupPolicy Policy { get; }

    /// <summary>When the next snapshot is due, or null if the save isn't open or has no backups yet.</summary>
    public DateTime? NextDueUtc(IReadOnlyList<BackupInfo> backups) =>
        backups.Count == 0 ? null : backups[0].TakenAtUtc + Policy.Interval;

    /// <summary>Takes a snapshot if one is due, then deletes expired ones. Returns the new snapshot, or null.</summary>
    public BackupInfo Tick()
    {
        if (!_store.IsOpen) return null;
        DateTime now = _store.Clock.UtcNow;
        List<BackupInfo> backups = _store.ListBackups();

        BackupInfo made = null;
        bool due = backups.Count == 0
            || now - backups[0].TakenAtUtc >= Policy.Interval
            || backups[0].TakenAtUtc > now + Policy.Interval; // the clock went backwards: don't wait for it
        if (due)
        {
            made = _store.CreateBackup("auto");
            backups.Insert(0, made);
        }
        foreach (BackupInfo old in Policy.Expired(backups, now))
            _store.DeleteBackup(old.Name);
        return made;
    }
}
