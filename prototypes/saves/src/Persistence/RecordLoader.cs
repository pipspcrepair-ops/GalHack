using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GalHack.Persistence;

/// <summary>
/// Checks a manifest's records, wherever they are read from (the live folder or a backup zip):
/// every file present, the right size, the right checksum, the right record inside; old versions
/// migrated; every ship that something is aboard present; and the totals equal to the manifest's
/// audit. Only if all of that holds does the result get a table. One bad record fails the load.
/// </summary>
internal static class RecordLoader
{
    /// <param name="readFile">Returns a record file's bytes, or null if it doesn't exist. Must be thread-safe.</param>
    public static void Verify(Manifest manifest, Func<string, byte[]> readFile, RecordSchemas schemas, LoadResult result)
    {
        result.Generation = manifest.Generation;
        result.SavedAtUtc = manifest.SavedAtUtc;
        result.Expected = manifest.Audit;

        List<ManifestEntry> entries = manifest.Records;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (ManifestEntry e in entries)
        {
            if (!ids.Add(e.Id)) result.Problems.Add($"the manifest lists record {e.Id} twice");
            if (!files.Add(e.File)) result.Problems.Add($"the manifest lists file {e.File} twice");
        }
        if (result.Problems.Count > 0) return;

        // Each record is independent, so check them in parallel: a full load of tens of thousands
        // of small files is dominated by opening them.
        var records = new SaveRecord[entries.Count];
        var problems = new string[entries.Count];
        var migrated = new bool[entries.Count];
        Parallel.For(0, entries.Count, i =>
        {
            ManifestEntry e = entries[i];
            try
            {
                if (!RecordFile.TryParseFileName(e.File, out string fileId, out _) || fileId != e.Id)
                {
                    problems[i] = $"record {e.Id}: the manifest points it at {e.File}, which is not one of its files";
                    return;
                }
                byte[] bytes = readFile(e.File);
                if (bytes == null) { problems[i] = $"record {e.Id}: its file {e.File} is missing"; return; }
                if (bytes.Length != e.Bytes) { problems[i] = $"record {e.Id}: {e.File} is {bytes.Length} bytes but should be {e.Bytes} (cut short or overwritten)"; return; }
                if (Framing.Hash(bytes) != e.Sha256) { problems[i] = $"record {e.Id}: {e.File} fails its checksum (the file is damaged)"; return; }

                SaveRecord record = RecordFile.Decode(bytes);
                if (record.Id != e.Id) { problems[i] = $"record {e.Id}: {e.File} holds record {record.Id} instead"; return; }
                string problem = record.Problem();
                if (problem != null) { problems[i] = $"record {e.Id}: {problem}"; return; }

                SaveRecord current = schemas.Upgrade(record, out problem);
                if (current == null) { problems[i] = problem; return; }
                migrated[i] = !ReferenceEquals(current, record);
                records[i] = current;
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                problems[i] = $"record {e.Id}: {ex.Message}";
            }
        });
        result.Problems.AddRange(problems.Where(p => p != null));
        result.Checked = records.Count(r => r != null);
        if (result.Problems.Count > 0) return;

        // Anything aboard a ship needs the ship: otherwise it has nowhere to be.
        var byId = records.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (SaveRecord r in records)
            if (r.ShipId != null && !byId.ContainsKey(r.ShipId))
                result.Problems.Add($"{r.Id} is aboard ship {r.ShipId}, which is missing");

        // The audit: the records must add up to exactly what the manifest promised.
        result.Found = AuditCounts.Of(records);
        result.Problems.AddRange(manifest.Audit.Differences(result.Found));
        if (result.Problems.Count > 0) return;

        foreach (string type in records.Select(r => r.Type).Distinct().Where(t => !schemas.Knows(t)))
            result.Notes.Add($"records of unknown type '{type}' were kept exactly as they are");

        var table = new RecordTable(records);
        for (int i = 0; i < records.Length; i++)
            if (migrated[i]) table.MarkDirty(records[i].Id); // the next commit writes them in the new format
        result.Migrated = migrated.Count(m => m);
        if (result.Migrated > 0) result.Notes.Add($"{result.Migrated} records migrated to the current format (the old files stay until the next save)");
        result.Table = table;
    }
}
