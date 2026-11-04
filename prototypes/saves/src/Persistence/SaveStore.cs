using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace GalHack.Persistence;

/// <summary>
/// One save on disk, written by shadow paging with plain files: no database.
///
///   manifest.json               the commit point: every record's file and checksum, plus audit totals
///   records/crate-a.g41.rec     one file per record per generation; never changed once written
///   backups/auto-...-g40.zip    rolling snapshots (see BackupScheduler)
///   quarantine/...              raw copies of a damaged save, kept before a restore replaces it
///
/// A commit (generation N):
///   1. writes each changed record to a new file named for generation N, and flushes it;
///   2. writes the new manifest to manifest.json.tmp, and flushes it;
///   3. renames manifest.json.tmp over manifest.json: the one atomic step, the commit point;
///   4. only then deletes the files no manifest references any more.
/// Steps 1 and 2 never touch a file the current manifest lists, so a crash at any moment leaves
/// the previous manifest and all its files intact. A load sweeps away whatever a crash left over.
///
/// Only the host has a SaveStore; clients never write. Thread-safe: commits, loads and backups
/// are serialised by one lock, so a commit can run on a worker thread.
/// </summary>
public sealed class SaveStore
{
    public const string ManifestFileName = "manifest.json";

    readonly object _lock = new();
    Dictionary<string, ManifestEntry> _entries = new(StringComparer.Ordinal); // the committed manifest
    long _generation = -1;                                                    // -1: not open
    readonly HashSet<string> _garbage = new(StringComparer.Ordinal);          // unreferenced files from a failed commit

    public SaveStore(string folder, RecordSchemas schemas = null, IClock clock = null)
    {
        Folder = Path.GetFullPath(folder);
        Schemas = schemas ?? new RecordSchemas();
        Clock = clock ?? SystemClock.Instance;
    }

    public string Folder { get; }
    public RecordSchemas Schemas { get; }
    public IClock Clock { get; }

    /// <summary>Crash points for tests. A commit can also be given its own injector.</summary>
    public FaultInjector Faults { get; set; } = new();

    public string ManifestPath => Path.Combine(Folder, ManifestFileName);
    public string RecordsFolder => Path.Combine(Folder, "records");
    public string BackupsFolder => Path.Combine(Folder, "backups");
    public string QuarantineFolder => Path.Combine(Folder, "quarantine");
    string TempManifestPath => ManifestPath + ".tmp";
    string RecordPath(string file) => Path.Combine(RecordsFolder, file);

    public bool Exists => File.Exists(ManifestPath);

    /// <summary>True once a load (or create) has succeeded. A save that failed its audit stays closed: only a restore can write to it.</summary>
    public bool IsOpen => Generation >= 0;

    /// <summary>The last committed generation. Lock-free, so a HUD can read it while a commit runs.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    // ---- Create and load ------------------------------------------------------------

    /// <summary>Starts a new, empty save at generation 0. Refuses if anything is here already.</summary>
    public void Create()
    {
        lock (_lock)
        {
            if (File.Exists(ManifestPath)) throw new InvalidOperationException($"there is already a save in {Folder}");
            if (OurRecordFiles().Count > 0) throw new InvalidOperationException($"{RecordsFolder} has record files but no manifest; refusing to start a new save over them");
            Directory.CreateDirectory(RecordsFolder);
            var manifest = new Manifest { Generation = 0, SavedAtUtc = Clock.UtcNow };
            DurableFile.Write(TempManifestPath, manifest.Encode(), Faults, "create manifest.json.tmp");
            DurableFile.Replace(TempManifestPath, ManifestPath);
            _entries = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
            Interlocked.Exchange(ref _generation, 0);
        }
    }

    /// <summary>
    /// Reads and checks the whole save. If anything is missing or damaged it refuses, lists the
    /// backups and changes nothing on disk. Otherwise it sweeps away files left by an interrupted
    /// save and returns every record.
    /// </summary>
    public LoadResult Load(FaultInjector faults = null)
    {
        lock (_lock)
        {
            var watch = Stopwatch.StartNew();
            var result = new LoadResult();
            _entries = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
            Interlocked.Exchange(ref _generation, -1);

            if (!File.Exists(ManifestPath))
            {
                int strays = OurRecordFiles().Count;
                if (strays == 0)
                {
                    result.NoSave = true;
                    result.Problems.Add($"there is no save in {Folder} yet");
                }
                else result.Problems.Add($"manifest.json is missing, but {strays} record files are here: the save is damaged");
                result.Backups = ListBackups();
                return result;
            }

            Manifest manifest;
            try
            {
                manifest = Manifest.Decode(File.ReadAllBytes(ManifestPath));
            }
            catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
            {
                result.Problems.Add("manifest.json can't be used: " + e.Message);
                result.Backups = ListBackups();
                return result;
            }

            RecordLoader.Verify(manifest, ReadRecordFile, Schemas, result);
            if (!result.Ok)
            {
                result.Backups = ListBackups();
                return result;
            }

            _entries = manifest.Records.ToDictionary(e => e.Id, StringComparer.Ordinal);
            Interlocked.Exchange(ref _generation, manifest.Generation);
            _garbage.Clear();
            Sweep(result, faults ?? Faults);
            result.Elapsed = watch.Elapsed;
            return result;
        }
    }

    byte[] ReadRecordFile(string file)
    {
        try
        {
            return File.ReadAllBytes(RecordPath(file));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes what an interrupted save left behind: a temp manifest, record files no manifest
    /// lists, half-written backups. Only ever runs after the manifest and every file it lists
    /// passed the audit, and only touches files it recognises as its own.
    /// </summary>
    void Sweep(LoadResult result, FaultInjector faults)
    {
        if (File.Exists(TempManifestPath))
        {
            File.Delete(TempManifestPath);
            result.TempManifestFound = true;
            faults.Point("sweep: removed manifest.json.tmp");
        }

        var referenced = new HashSet<string>(_entries.Values.Select(e => e.File), StringComparer.Ordinal);
        foreach (string path in OurRecordFiles())
        {
            string name = Path.GetFileName(path);
            if (referenced.Contains(name)) continue;
            RecordFile.TryParseFileName(name, out _, out long generation);
            File.Delete(path);
            // Newer than the manifest: a commit that never reached its commit point, rolled back.
            // Older: a superseded file whose deletion was cut off after the commit landed.
            if (generation > _generation) result.UnfinishedFilesRemoved++;
            else result.LeftoverFilesRemoved++;
            faults.Point($"sweep: removed {name}");
        }

        if (Directory.Exists(BackupsFolder))
        {
            foreach (string path in Directory.GetFiles(BackupsFolder, "*.zip.tmp"))
            {
                File.Delete(path);
                faults.Point($"sweep: removed {Path.GetFileName(path)}");
            }
        }
    }

    List<string> OurRecordFiles() =>
        Directory.Exists(RecordsFolder)
            ? Directory.GetFiles(RecordsFolder).Where(p => RecordFile.TryParseFileName(Path.GetFileName(p), out _, out _)).ToList()
            : new List<string>();

    // ---- Commit -----------------------------------------------------------------------

    /// <summary>
    /// Writes a plan as the next generation. If this throws, the previous generation is still the
    /// save, untouched; leave the records dirty and try again later.
    /// </summary>
    public CommitResult Commit(CommitPlan plan, FaultInjector faults = null)
    {
        lock (_lock)
        {
            if (_generation < 0) throw new InvalidOperationException("the save isn't open: load or create it before committing");
            return CommitLocked(plan, _generation + 1, faults ?? Faults);
        }
    }

    CommitResult CommitLocked(CommitPlan plan, long generation, FaultInjector faults)
    {
        var watch = Stopwatch.StartNew();

        // Work out the new manifest's record list and check it matches the table exactly, before
        // anything touches the disk: the save and the game must agree on what exists.
        var entries = plan.FullRewrite
            ? new Dictionary<string, ManifestEntry>(StringComparer.Ordinal)
            : new Dictionary<string, ManifestEntry>(_entries, StringComparer.Ordinal);
        foreach (string id in plan.Removed) entries.Remove(id);
        var ids = new HashSet<string>(entries.Keys, StringComparer.Ordinal);
        foreach (SaveRecord r in plan.Changed) ids.Add(r.Id);
        if (ids.Count != plan.AllIds.Count || !ids.SetEquals(plan.AllIds))
            throw new InvalidOperationException("the record table and the save on disk disagree about which records exist; nothing was written");

        Directory.CreateDirectory(RecordsFolder);
        var written = new List<string>();
        long bytesWritten = 0;
        try
        {
            // 1. Each changed record goes to a brand-new file for this generation. Nothing the
            //    current manifest lists is touched, so a crash here leaves the last commit whole.
            //    Write them all, then flush them all together: every record is on the disk
            //    before the manifest that points at it is even written.
            foreach (SaveRecord record in plan.Changed)
            {
                byte[] bytes = RecordFile.Encode(record);
                string file = RecordFile.FileName(record.Id, generation);
                written.Add(file);
                DurableFile.Write(RecordPath(file), bytes, faults, $"record {record.Id}", flush: false);
                entries[record.Id] = new ManifestEntry(record.Id, file, bytes.Length, Framing.Hash(bytes));
                bytesWritten += bytes.Length;
            }
            DurableFile.FlushAll(written.Select(RecordPath).ToList());
            faults.Point("records flushed");

            // 2. The new manifest, to a temp file.
            var manifest = new Manifest
            {
                Generation = generation,
                SavedAtUtc = Clock.UtcNow,
                Audit = plan.Counts,
                Records = entries.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList(),
            };
            byte[] manifestBytes = manifest.Encode();
            DurableFile.Write(TempManifestPath, manifestBytes, faults, "manifest.json.tmp");
            bytesWritten += manifestBytes.Length;

            // 3. The commit point: one atomic rename.
            DurableFile.Replace(TempManifestPath, ManifestPath);
        }
        catch (Exception e) when (e is not SimulatedCrash)
        {
            // A real error (disk full, say). The old manifest is still the save; what this attempt
            // wrote is unreferenced, so tidy it away after the next good commit.
            foreach (string file in written) _garbage.Add(file);
            throw;
        }

        Dictionary<string, ManifestEntry> previous = _entries;
        _entries = entries;
        Interlocked.Exchange(ref _generation, generation);
        faults.Point("manifest.json replaced: committed");

        // 4. Only now delete the files that no manifest references any more.
        var referenced = new HashSet<string>(entries.Values.Select(e => e.File), StringComparer.Ordinal);
        IEnumerable<string> candidates = plan.FullRewrite
            ? OurRecordFiles().Select(Path.GetFileName)
            : previous.Values.Select(e => e.File);
        List<string> doomed = candidates.Concat(_garbage).Distinct()
            .Where(f => !referenced.Contains(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        _garbage.Clear();
        int deleted = 0;
        foreach (string file in doomed)
        {
            try
            {
                DurableFile.Delete(RecordPath(file));
                deleted++;
            }
            catch (IOException)
            {
                _garbage.Add(file); // locked for a moment; the commit has landed, so just try again next time
            }
            faults.Point($"deleted {file}");
        }

        return new CommitResult
        {
            Generation = generation,
            Written = plan.Changed.Count,
            Removed = plan.Removed.Count,
            FilesDeleted = deleted,
            BytesWritten = bytesWritten,
            Elapsed = watch.Elapsed,
        };
    }

    // ---- Backups ----------------------------------------------------------------------

    /// <summary>Backups on disk, newest first.</summary>
    public List<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(BackupsFolder)) return new List<BackupInfo>();
        return Directory.GetFiles(BackupsFolder, "*.zip")
            .Select(BackupInfo.TryParse)
            .Where(b => b != null)
            .OrderByDescending(b => b.TakenAtUtc)
            .ThenByDescending(b => b.Generation)
            .ThenByDescending(b => b.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Snapshots the committed save into one zip: the manifest and exactly the files it lists,
    /// each checked against its checksum on the way. Written to a temp name and renamed, so a
    /// backup is either whole or absent.
    /// </summary>
    public BackupInfo CreateBackup(string kind, FaultInjector faults = null)
    {
        lock (_lock)
        {
            if (_generation < 0) throw new InvalidOperationException("there is no open save to back up");
            faults ??= Faults;
            Directory.CreateDirectory(BackupsFolder);

            DateTime now = Clock.UtcNow;
            string name = BackupInfo.MakeName(kind, now, _generation);
            for (int copy = 2; File.Exists(Path.Combine(BackupsFolder, name)); copy++)
                name = BackupInfo.MakeName(kind, now, _generation, copy);
            string path = Path.Combine(BackupsFolder, name);
            string temp = path + ".tmp";

            byte[] manifestBytes = File.ReadAllBytes(ManifestPath);
            if (Manifest.Decode(manifestBytes).Generation != _generation)
                throw new IOException("manifest.json was changed by something else; not backing it up");

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    AddEntry(zip, ManifestFileName, manifestBytes);
                    foreach (ManifestEntry e in _entries.Values.OrderBy(e => e.File, StringComparer.Ordinal))
                    {
                        byte[] bytes = File.ReadAllBytes(RecordPath(e.File));
                        if (Framing.Hash(bytes) != e.Sha256)
                            throw new IOException($"record {e.Id} is damaged on disk ({e.File}); refusing to back up a damaged save");
                        AddEntry(zip, "records/" + e.File, bytes);
                    }
                }
                stream.Flush(flushToDisk: true);
            }
            faults.Point($"backup {name}: written");
            DurableFile.Replace(temp, path);
            faults.Point($"backup {name}: published");
            return BackupInfo.TryParse(path);
        }
    }

    static void AddEntry(ZipArchive zip, string name, byte[] bytes)
    {
        using Stream s = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        s.Write(bytes, 0, bytes.Length);
    }

    public void DeleteBackup(string name)
    {
        lock (_lock) DurableFile.Delete(Path.Combine(BackupsFolder, Path.GetFileName(name)));
    }

    /// <summary>Reads and fully checks a backup, without touching the live save.</summary>
    public LoadResult ReadBackup(string name)
    {
        var watch = Stopwatch.StartNew();
        var result = new LoadResult();
        string path = Path.Combine(BackupsFolder, Path.GetFileName(name));
        try
        {
            byte[] manifestBytes = null;
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    using var stream = entry.Open();
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    if (entry.FullName == ManifestFileName) manifestBytes = memory.ToArray();
                    else if (entry.FullName.StartsWith("records/", StringComparison.Ordinal)) files[entry.FullName["records/".Length..]] = memory.ToArray();
                }
            }
            if (manifestBytes == null) result.Problems.Add($"backup {name} has no manifest");
            else RecordLoader.Verify(Manifest.Decode(manifestBytes), f => files.GetValueOrDefault(f), Schemas, result);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or UnauthorizedAccessException)
        {
            result.Problems.Add($"backup {name} can't be read: {e.Message}");
        }
        result.Elapsed = watch.Elapsed;
        return result;
    }

    /// <summary>
    /// Replaces the live save with a backup, as one more atomic commit. What was live is kept
    /// first: as a "prerestore" backup if it is intact, or copied raw into quarantine/ if it is
    /// damaged, so a restore can never be the thing that loses data. Then the restored save is
    /// loaded back through the same audit as any other, and that result is returned.
    /// </summary>
    public LoadResult Restore(string backupName, FaultInjector faults = null)
    {
        lock (_lock)
        {
            faults ??= Faults;
            LoadResult backup = ReadBackup(backupName);
            if (!backup.Ok)
            {
                backup.Backups = ListBackups();
                return backup;
            }

            LoadResult live = Load(faults);
            if (live.Ok) CreateBackup("prerestore", faults);
            else if (!live.NoSave) Quarantine(faults);

            long generation = Math.Max(Math.Max(_generation, backup.Generation), HighestGenerationOnDisk()) + 1;
            CommitLocked(CommitPlan.Everything(backup.Table.All), generation, faults);
            return Load(faults);
        }
    }

    /// <summary>Raw, flushed copies of whatever is in the save folder, before a restore replaces it.</summary>
    void Quarantine(FaultInjector faults)
    {
        string stamp = Clock.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string folder = Path.Combine(QuarantineFolder, stamp);
        for (int copy = 2; Directory.Exists(folder); copy++) folder = Path.Combine(QuarantineFolder, $"{stamp}-{copy}");
        Directory.CreateDirectory(Path.Combine(folder, "records"));

        var quiet = new FaultInjector();
        if (File.Exists(ManifestPath))
            DurableFile.Write(Path.Combine(folder, ManifestFileName), File.ReadAllBytes(ManifestPath), quiet, "quarantine");
        if (Directory.Exists(RecordsFolder))
            foreach (string path in Directory.GetFiles(RecordsFolder))
                DurableFile.Write(Path.Combine(folder, "records", Path.GetFileName(path)), File.ReadAllBytes(path), quiet, "quarantine");
        faults.Point("quarantine copied");
    }

    long HighestGenerationOnDisk()
    {
        long highest = 0;
        foreach (string path in OurRecordFiles())
            if (RecordFile.TryParseFileName(Path.GetFileName(path), out _, out long generation))
                highest = Math.Max(highest, generation);
        return highest;
    }
}
