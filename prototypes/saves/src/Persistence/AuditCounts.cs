using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Persistence;

/// <summary>
/// Totals the manifest promises and the load audit checks: how many records, items and blocks
/// there are, and how many records of each type players own. If a record file goes missing, or
/// the manifest loses an entry, these stop adding up and the load refuses.
/// </summary>
public sealed class AuditCounts
{
    public int Records { get; set; }
    public int Items { get; set; }
    public int Blocks { get; set; }

    /// <summary>Player-owned records by type, e.g. structure 2, container 5.</summary>
    public SortedDictionary<string, int> Owned { get; set; } = new(StringComparer.Ordinal);

    public static AuditCounts Of(IEnumerable<SaveRecord> records)
    {
        var counts = new AuditCounts();
        foreach (SaveRecord r in records)
        {
            counts.Records++;
            counts.Items += r.Items;
            counts.Blocks += r.Blocks;
            if (r.Owner != null) counts.Owned[r.Type] = counts.Owned.GetValueOrDefault(r.Type) + 1;
        }
        return counts;
    }

    public int OwnedOf(string type) => Owned.GetValueOrDefault(type);

    /// <summary>Every way <paramref name="found"/> differs from these counts, in words. Empty if they match.</summary>
    public List<string> Differences(AuditCounts found)
    {
        var list = new List<string>();
        void Compare(string what, int expected, int actual)
        {
            if (expected != actual) list.Add($"{what}: the manifest says {expected}, the records hold {actual}");
        }
        Compare("records", Records, found.Records);
        Compare("items", Items, found.Items);
        Compare("blocks", Blocks, found.Blocks);
        foreach (string type in Owned.Keys.Union(found.Owned.Keys).OrderBy(t => t, StringComparer.Ordinal))
            Compare($"player-owned {type} records", OwnedOf(type), found.OwnedOf(type));
        return list;
    }

    public bool SameAs(AuditCounts other) => Differences(other).Count == 0;

    public override string ToString()
    {
        string owned = Owned.Count == 0 ? "" : ", owned " + string.Join(", ", Owned.Select(kv => $"{kv.Key} {kv.Value}"));
        return $"{Records} records, {Items} items, {Blocks} blocks{owned}";
    }
}
