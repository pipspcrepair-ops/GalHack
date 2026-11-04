using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace GalHack.Persistence;

/// <summary>
/// What this build knows about each record type: its current payload version, the migration
/// from every older version, and how to count the items and blocks in a payload. Every format
/// change ships with a migration here, so old saves always load.
///
/// Types it has never heard of (a newer build's, a mod's) are kept exactly as they are: being
/// unable to read something is never a reason to drop it.
/// </summary>
public sealed class RecordSchemas
{
    sealed class TypeInfo
    {
        public int Current;
        public Func<string, (int Items, int Blocks)> Count;
        public readonly Dictionary<int, Func<JsonObject, JsonObject>> Upgrades = new();
    }

    readonly Dictionary<string, TypeInfo> _types = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a type at its current version. <paramref name="count"/> reads a current-version
    /// payload and counts its items and blocks; the loader uses it to prove a migration lost nothing.
    /// </summary>
    public RecordSchemas Register(string type, int currentVersion, Func<string, (int Items, int Blocks)> count = null)
    {
        _types[type] = new TypeInfo { Current = currentVersion, Count = count };
        return this;
    }

    /// <summary>Adds the migration that turns a <paramref name="fromVersion"/> payload into the next version's.</summary>
    public RecordSchemas Migration(string type, int fromVersion, Func<JsonObject, JsonObject> upgrade)
    {
        if (!_types.TryGetValue(type, out TypeInfo info)) throw new InvalidOperationException($"register {type} first");
        info.Upgrades[fromVersion] = upgrade;
        return this;
    }

    public bool Knows(string type) => _types.ContainsKey(type);

    public int CurrentVersion(string type) =>
        _types.TryGetValue(type, out TypeInfo info) ? info.Current : throw new KeyNotFoundException($"unknown record type {type}");

    /// <summary>Counts a payload's items and blocks, if this type has a counter.</summary>
    public (int Items, int Blocks)? Count(SaveRecord record) =>
        _types.TryGetValue(record.Type, out TypeInfo info) && info.Count != null ? info.Count(record.Payload) : null;

    /// <summary>
    /// Brings a record up to its type's current version, one migration at a time, then recounts
    /// it: a migration that changes the item or block count is a bug, and the load refuses.
    /// Returns null and a problem if it can't be done.
    /// </summary>
    public SaveRecord Upgrade(SaveRecord record, out string problem)
    {
        problem = null;
        if (!_types.TryGetValue(record.Type, out TypeInfo info)) return record;
        if (record.Version > info.Current)
        {
            problem = $"{record.Id}: {record.Type} v{record.Version} was saved by a newer build (this one reads up to v{info.Current})";
            return null;
        }

        SaveRecord r = record;
        try
        {
            while (r.Version < info.Current)
            {
                if (!info.Upgrades.TryGetValue(r.Version, out var upgrade))
                {
                    problem = $"{r.Id}: there is no migration from {r.Type} v{r.Version}";
                    return null;
                }
                JsonObject payload = JsonNode.Parse(r.Payload).AsObject();
                r = r with { Version = r.Version + 1, Payload = upgrade(payload).ToJsonString() };
            }
        }
        catch (Exception e)
        {
            problem = $"{r.Id}: migrating {r.Type} v{r.Version} failed: {e.Message}";
            return null;
        }

        if (r != record && info.Count != null)
        {
            var (items, blocks) = info.Count(r.Payload);
            if (items != record.Items || blocks != record.Blocks)
            {
                problem = $"{r.Id}: migrating {r.Type} to v{r.Version} changed it from {record.Items} items and {record.Blocks} blocks to {items} and {blocks}";
                return null;
            }
        }
        return r;
    }
}
