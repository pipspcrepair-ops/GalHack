using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Persistence;

/// <summary>Thrown when a commit would save a state that loses (or invents) items or blocks.</summary>
public sealed class SaveRefusedException : Exception
{
    public SaveRefusedException(string message) : base(message) { }
}

/// <summary>
/// Everything one commit needs, captured on the game thread so the disk work can run on any
/// thread. Records are immutable, so holding references is a consistent snapshot.
/// </summary>
public sealed class CommitPlan
{
    /// <summary>Records that are new or changed since the last commit.</summary>
    public IReadOnlyList<SaveRecord> Changed { get; init; }

    /// <summary>Ids of records removed since the last commit.</summary>
    public IReadOnlyList<string> Removed { get; init; }

    /// <summary>Every record id in the table, so the store can check it agrees before writing anything.</summary>
    public IReadOnlyCollection<string> AllIds { get; init; }

    /// <summary>Totals for the whole table, written into the manifest for the load audit.</summary>
    public AuditCounts Counts { get; init; }

    /// <summary>True for a restore: the plan holds every record and replaces the whole save.</summary>
    public bool FullRewrite { get; init; }

    internal Dictionary<string, long> Stamps { get; init; } = new();
    internal int DeclaredItems { get; init; }
    internal int DeclaredBlocks { get; init; }

    /// <summary>A plan that writes every record in <paramref name="records"/>, replacing whatever the save held.</summary>
    public static CommitPlan Everything(IEnumerable<SaveRecord> records)
    {
        List<SaveRecord> all = records.ToList();
        return new CommitPlan
        {
            Changed = all,
            Removed = Array.Empty<string>(),
            AllIds = all.Select(r => r.Id).ToList(),
            Counts = AuditCounts.Of(all),
            FullRewrite = true,
        };
    }
}

/// <summary>
/// The host's records in memory, with dirty tracking so saves are incremental: a commit writes
/// only the records changed since the last one.
///
/// It also keeps a ledger. Items and blocks can't appear or vanish by accident: the game must
/// <see cref="Declare"/> anything it creates or destroys on purpose (building a block from a hull
/// plate, say). If the totals don't match the last commit plus what was declared, the next commit
/// is refused, so a bug that loses things can never be written over the last good save.
///
/// Not thread-safe: use it from the game thread only, and hand <see cref="CommitPlan"/>s to the store.
/// </summary>
public sealed class RecordTable
{
    readonly Dictionary<string, SaveRecord> _records = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _changed = new(StringComparer.Ordinal); // id -> stamp of its latest change
    long _stamp;
    int _committedItems, _committedBlocks;
    int _declaredItems, _declaredBlocks;

    public RecordTable() { }

    /// <summary>A table loaded from disk: everything it holds is already saved.</summary>
    internal RecordTable(IEnumerable<SaveRecord> records)
    {
        foreach (SaveRecord r in records) _records.Add(r.Id, r);
        AuditCounts counts = AuditCounts.Of(_records.Values);
        _committedItems = counts.Items;
        _committedBlocks = counts.Blocks;
    }

    public int Count => _records.Count;

    /// <summary>Goes up on every change. Cheap to poll, e.g. to know when to refresh a HUD.</summary>
    public long ChangeStamp => _stamp;

    /// <summary>How many records have changed (or been removed) since the last commit.</summary>
    public int PendingChanges => _changed.Count;

    public IEnumerable<SaveRecord> All => _records.Values;

    public bool Contains(string id) => _records.ContainsKey(id);

    public bool TryGet(string id, out SaveRecord record) => _records.TryGetValue(id, out record);

    public SaveRecord Get(string id) =>
        _records.TryGetValue(id, out SaveRecord r) ? r : throw new KeyNotFoundException($"there is no record {id}");

    /// <summary>Records in one zone. A plain scan: fine for thousands, wants an index for millions.</summary>
    public IEnumerable<SaveRecord> InZone(string zoneId) => _records.Values.Where(r => r.ZoneId == zoneId);

    /// <summary>Adds a record or replaces the one with the same id, and marks it dirty.</summary>
    public void Put(SaveRecord record)
    {
        string problem = record.Problem();
        if (problem != null) throw new ArgumentException(problem);
        if (_records.TryGetValue(record.Id, out SaveRecord old) && old.Type != record.Type)
            throw new ArgumentException($"{record.Id} is a {old.Type}; it can't become a {record.Type}");
        _records[record.Id] = record;
        _changed[record.Id] = ++_stamp;
    }

    /// <summary>Marks a record dirty without changing it, so the next commit writes it again.</summary>
    public void MarkDirty(string id)
    {
        Get(id);
        _changed[id] = ++_stamp;
    }

    /// <summary>
    /// Removes a record as part of a player action whose contents have already gone somewhere
    /// else in the same commit, like a dropped stack picked up into an inventory. Refuses one
    /// that still holds items or blocks: move them out first, then remove the empty shell.
    /// </summary>
    public void Remove(string id)
    {
        SaveRecord r = Get(id);
        if (r.Items > 0 || r.Blocks > 0)
            throw new InvalidOperationException($"{id} still holds {r.Items} items and {r.Blocks} blocks; move them somewhere first");
        _records.Remove(id);
        _changed[id] = ++_stamp;
    }

    /// <summary>
    /// The only way the game may tidy anything up on its own. Anything a player owns or has
    /// touched is refused and left exactly as it was. Returns the refusal, or null if removed.
    /// </summary>
    public string Cleanup(string id)
    {
        SaveRecord r = Get(id);
        string refusal = CleanupGuard.Refusal(r);
        if (refusal != null) return refusal;
        Declare(-r.Items, -r.Blocks); // world junk going away on purpose
        _records.Remove(id);
        _changed[id] = ++_stamp;
        return null;
    }

    /// <summary>
    /// Says that items or blocks were created (positive) or destroyed (negative) on purpose:
    /// mining, crafting, building a block out of a hull plate. Anything undeclared must add up.
    /// </summary>
    public void Declare(int items, int blocks)
    {
        _declaredItems += items;
        _declaredBlocks += blocks;
    }

    /// <summary>The item and block totals the next commit must have, given what was saved and declared.</summary>
    public (int Items, int Blocks) ExpectedTotals => (_committedItems + _declaredItems, _committedBlocks + _declaredBlocks);

    /// <summary>
    /// Captures what the next commit must write. Throws <see cref="SaveRefusedException"/> if the
    /// totals don't add up, before anything touches the disk.
    /// </summary>
    public CommitPlan PrepareCommit()
    {
        AuditCounts counts = AuditCounts.Of(_records.Values);
        var (items, blocks) = ExpectedTotals;
        if (counts.Items != items || counts.Blocks != blocks)
        {
            throw new SaveRefusedException(
                $"save refused: there are {counts.Items} items and {counts.Blocks} blocks, but there should be {items} and {blocks}. " +
                "Something lost or duplicated things without declaring it; the last good save is kept.");
        }

        var changed = new List<SaveRecord>();
        var removed = new List<string>();
        foreach (string id in _changed.Keys)
        {
            if (_records.TryGetValue(id, out SaveRecord r)) changed.Add(r);
            else removed.Add(id);
        }
        return new CommitPlan
        {
            Changed = changed,
            Removed = removed,
            AllIds = _records.Keys.ToList(),
            Counts = counts,
            Stamps = new Dictionary<string, long>(_changed, StringComparer.Ordinal),
            DeclaredItems = _declaredItems,
            DeclaredBlocks = _declaredBlocks,
        };
    }

    /// <summary>
    /// Call once the store has committed <paramref name="plan"/>. Records changed again since the
    /// plan was made stay dirty. If the commit failed, don't call this: everything stays dirty
    /// and the next commit tries again.
    /// </summary>
    public void CompleteCommit(CommitPlan plan)
    {
        foreach (var (id, stamp) in plan.Stamps)
            if (_changed.TryGetValue(id, out long now) && now == stamp) _changed.Remove(id);
        _committedItems = plan.Counts.Items;
        _committedBlocks = plan.Counts.Blocks;
        _declaredItems -= plan.DeclaredItems;
        _declaredBlocks -= plan.DeclaredBlocks;
    }

    /// <summary>A copy of every record, for comparing whole states in tests.</summary>
    public Dictionary<string, SaveRecord> Snapshot() => new(_records, StringComparer.Ordinal);
}
