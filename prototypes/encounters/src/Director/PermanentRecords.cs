using System;
using System.Collections.Generic;

namespace Encounters.Director;

/// <summary>
/// The world's lasting facts: everything players own, everything they touched or claimed, and
/// every wreck they saw. It is all that survives a zone closing, and the only part
/// of the director a save file would need, along with the zone data.
///
/// There is deliberately no way to remove anything. Event cleanup only ever sees a zone
/// instance's transient ships, and anything lasting has already moved here, out of its reach.
/// </summary>
public sealed class PermanentRecords
{
    readonly Dictionary<long, WorldEntity> _byId = new();
    readonly Dictionary<string, List<WorldEntity>> _byZone = new();
    readonly List<JournalEntry> _journal = new();
    static readonly List<WorldEntity> None = new();
    long _nextId = 1;

    /// <summary>How often cleanup was handed something lasting and kept it instead. Should stay 0.</summary>
    public int GuardTrips { get; internal set; }

    public int Count => _byId.Count;

    public IEnumerable<WorldEntity> All => _byId.Values;

    public IReadOnlyList<JournalEntry> Journal => _journal;

    /// <summary>Ids for entities and encounters. A save would store this with the records.</summary>
    public long NextId() => _nextId++;

    public bool Contains(long id) => _byId.ContainsKey(id);

    public bool TryGet(long id, out WorldEntity entity) => _byId.TryGetValue(id, out entity);

    public IReadOnlyList<WorldEntity> InZone(string zoneId) => _byZone.TryGetValue(zoneId, out var list) ? list : None;

    internal void Keep(WorldEntity entity, double time, string why)
    {
        if (!entity.IsLasting) throw new InvalidOperationException($"#{entity.Id} is not lasting: only owned, touched or wrecked things become records");
        if (!_byId.TryAdd(entity.Id, entity)) return;
        if (!_byZone.TryGetValue(entity.ZoneId, out var list)) _byZone[entity.ZoneId] = list = new List<WorldEntity>();
        list.Add(entity);
        _journal.Add(new JournalEntry(time, entity.ZoneId, entity.Id, why));
    }

    internal void Note(WorldEntity entity, double time, string what) =>
        _journal.Add(new JournalEntry(time, entity.ZoneId, entity.Id, what));
}
