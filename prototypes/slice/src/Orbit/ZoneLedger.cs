using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalHack.Items;
using GalHack.Persistence;

namespace Slice.Orbit;

/// <summary>
/// Something the ledger keeps a record of: the sleeper, a structure, a container on the ground or a device. In a zone that
/// is freed when it is left (a star's), where it is too (<see cref="ZoneLedger.Placing"/>), so it can be put back.
/// </summary>
public readonly record struct Holder(string Id, string Type, string Zone, string Ship, string Name, Bill Items, int Blocks, ZoneLedger.Placing Place = null);

/// <summary>
/// Where everything is, on disk, kept with saves' persistence core (prototypes/saves,
/// src/Persistence: records, a ledger of item and block totals, shadow-paged commits that land
/// with one atomic rename). The slice has no saves yet; this is only what its zone handovers need,
/// done the way the saves prototype crosses a zone:
/// <list type="number">
/// <item>Before a crossing, every holder's record is brought up to date and committed (what play changed is declared).</item>
/// <item>The crossing writes only the crosser's new zone (riders ride along in its cabin, unwritten) and commits. Only then does anything move.</item>
/// <item>After it moved, the game's own counts must equal the committed totals: nothing lost or doubled in the move.</item>
/// </list>
/// A crash at any point leaves the record in one zone or the other on disk, never both, never
/// neither (the zone test crashes a crossing at every one of its points and reloads).
/// </summary>
public sealed class ZoneLedger
{
    public const string Surface = "surface", Orbit = "orbit", Moon = "moon";
    public const string Sleeper = "sleeper";

    /// <summary>The zone round a catalogue star (<c>StarZone</c>): one per star, freed when it is left.</summary>
    public const string Star = "star";

    public static string StarZoneOf(int star) => Star + star;

    /// <summary>Where a holder in a freed zone was, to be put back: a structure's blueprint and its turn (x, y, z, w), and its place in its zone's world.</summary>
    public sealed record Placing(string Blueprint, float[] At, float[] Turn);

    /// <summary>
    /// Records in zones out of play now (a star's zone folded away from), by zone: kept as they are, never taken for
    /// gone, and counted in the totals, until their zone is in play again.
    /// </summary>
    public Func<string, bool> Kept = _ => false;

    /// <summary>The zone of anything riding in a vessel's cabin (saves' convention).</summary>
    public static string Cabin(string shipId) => "cabin/" + shipId;

    public static readonly string[] Types = { "sleeper", "structure", "container", "device" };

    public readonly SaveStore Store;
    public readonly RecordTable Table = new();

    /// <summary>The last commit's numbers (records written, milliseconds).</summary>
    public CommitResult LastCommit { get; private set; }

    /// <summary>Why the last crossing or snapshot was refused, if it was.</summary>
    public string Trouble { get; private set; }

    public int Commits { get; private set; }

    /// <summary>Opens a fresh ledger in a folder (anything there from an earlier game is cleared).</summary>
    public ZoneLedger(string folder, bool fresh = true)
    {
        if (fresh && Directory.Exists(folder)) Directory.Delete(folder, true);
        Store = new SaveStore(folder, Schemas());
        if (!Store.Exists) Store.Create();
        else Table = Store.Load().Table;
    }

    public static RecordSchemas Schemas()
    {
        var s = new RecordSchemas();
        foreach (string type in Types) s.Register(type, 1, Count);
        s.Register("castaway", 1, Count); // the old name of "sleeper", still read from saves written before the rename
        return s;
    }

    sealed class Payload
    {
        public string Name { get; set; }
        public Dictionary<string, int> Items { get; set; } = new();
        public int Blocks { get; set; }
        public Placing Place { get; set; }
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    static (int Items, int Blocks) Count(string payload)
    {
        Payload p = JsonSerializer.Deserialize<Payload>(payload, Json);
        return (p.Items.Values.Sum(), p.Blocks);
    }

    static SaveRecord Make(Holder h)
    {
        var p = new Payload { Name = h.Name, Blocks = h.Blocks, Place = h.Place };
        foreach (var (id, n) in h.Items) if (n > 0) p.Items[id] = n;
        return new SaveRecord
        {
            Id = h.Id, Type = h.Type, ZoneId = h.Zone, ShipId = h.Ship, Owner = h.Type == "sleeper" ? "player" : null,
            Items = p.Items.Values.Sum(), Blocks = h.Blocks, Payload = JsonSerializer.Serialize(p, Json),
        };
    }

    /// <summary>
    /// Brings every holder's record up to date: what play has changed since the last commit (things
    /// dug, made, eaten, salvaged) is declared, so the ledger's totals are the game's.
    /// </summary>
    void Refresh(IEnumerable<Holder> holders)
    {
        var seen = new HashSet<string>();
        int items = 0, blocks = 0;
        foreach (Holder h in holders)
        {
            SaveRecord r = Make(h);
            seen.Add(r.Id);
            items += r.Items;
            blocks += r.Blocks;
            if (Table.TryGet(r.Id, out SaveRecord old) && old == r) continue;
            Table.Put(r);
        }
        // Holders that are gone (a body emptied, a wreck taken apart) are removed empty; those in a zone out of play wait.
        foreach (SaveRecord r in Table.All.Where(r => !seen.Contains(r.Id)).ToList())
        {
            if (Kept(r.ZoneId))
            {
                items += r.Items;
                blocks += r.Blocks;
                continue;
            }
            Table.Put(r with { Items = 0, Blocks = 0, Payload = JsonSerializer.Serialize(new Payload { Name = "gone" }, Json) });
            Table.Remove(r.Id);
        }
        var (expectItems, expectBlocks) = Table.ExpectedTotals;
        Table.Declare(items - expectItems, blocks - expectBlocks);
    }

    /// <summary>Brings every record up to date and commits now. False (the last good commit kept) if the disk refused.</summary>
    public bool Snapshot(IEnumerable<Holder> holders)
    {
        Refresh(holders);
        return Commit("snapshot");
    }

    /// <summary>
    /// A crossing in play: every record brought up to date and the crosser's new zone, committed on
    /// a worker thread so no frame waits on the disk. Nothing moves until <see cref="Finish"/> says
    /// it landed. False if the ledger refused outright.
    /// </summary>
    public bool Begin(IEnumerable<Holder> holders, string id, string zone)
    {
        Refresh(holders);
        _before = Table.Get(id);
        Table.Put(_before with { ZoneId = zone, ShipId = null });
        try { _plan = Table.PrepareCommit(); }
        catch (SaveRefusedException e)
        {
            Trouble = $"crossing: {e.Message}";
            Table.Put(_before);
            return false;
        }
        CommitPlan plan = _plan;
        _commit = System.Threading.Tasks.Task.Run(() => Store.Commit(plan));
        return true;
    }

    /// <summary>A crossing's commit is still on its way to the disk.</summary>
    public bool Committing => _commit != null && !_commit.IsCompleted;

    /// <summary>The crossing's commit is done: true if it landed (the crosser may move now); false, and the record is put back, if not.</summary>
    public bool Finish()
    {
        try
        {
            LastCommit = _commit.Result;
            Table.CompleteCommit(_plan);
            Commits++;
            Trouble = null;
            return true;
        }
        catch (AggregateException e)
        {
            Trouble = $"crossing: {e.InnerException?.Message}";
            Table.Put(_before);
            return false;
        }
        finally { _commit = null; }
    }

    System.Threading.Tasks.Task<CommitResult> _commit;
    CommitPlan _plan;
    SaveRecord _before;

    /// <summary>
    /// The crossing's record: the crosser is now in another zone (and anything riding it stays in
    /// its cabin, unwritten). Committed before anything moves; if the commit fails the record is
    /// put back and the crossing doesn't happen.
    /// </summary>
    public bool Cross(string id, string zone, FaultInjector faults = null)
    {
        SaveRecord before = Table.Get(id);
        Table.Put(before with { ZoneId = zone, ShipId = null });
        if (Commit("crossing", faults)) return true;
        Table.Put(before);
        return false;
    }

    bool Commit(string why, FaultInjector faults = null)
    {
        try
        {
            CommitPlan plan = Table.PrepareCommit();
            LastCommit = Store.Commit(plan, faults);
            Table.CompleteCommit(plan);
            Commits++;
            Trouble = null;
            return true;
        }
        catch (SimulatedCrash) { throw; } // a crash is a crash: nothing catches it
        catch (Exception e) when (e is SaveRefusedException or IOException or UnauthorizedAccessException)
        {
            Trouble = $"{why}: {e.Message}";
            return false;
        }
    }

    /// <summary>The committed totals (items, blocks).</summary>
    public (int Items, int Blocks) Committed => Table.ExpectedTotals;

    /// <summary>Where the ledger says a record is.</summary>
    public string ZoneOf(string id) => Table.TryGet(id, out SaveRecord r) ? r.ZoneId : null;

    /// <summary>The totals of what is held (items, blocks).</summary>
    public static (int Items, int Blocks) Totals(IEnumerable<Holder> holders)
    {
        int items = 0, blocks = 0;
        foreach (Holder h in holders)
        {
            items += h.Items.Total;
            blocks += h.Blocks;
        }
        return (items, blocks);
    }

    /// <summary>The totals of what waits in zones out of play (<see cref="Kept"/>), by their records.</summary>
    public (int Items, int Blocks) KeptTotals()
    {
        int items = 0, blocks = 0;
        foreach (SaveRecord r in Table.All)
            if (Kept(r.ZoneId)) (items, blocks) = (items + r.Items, blocks + r.Blocks);
        return (items, blocks);
    }

    /// <summary>What the ledger holds in a zone with its place: each record's id, type, place and items (to be put back there).</summary>
    public IEnumerable<(string Id, string Type, Placing Place, Bill Items)> KeptIn(string zone)
    {
        foreach (SaveRecord r in Table.InZone(zone))
        {
            Payload p = JsonSerializer.Deserialize<Payload>(r.Payload, Json);
            if (p.Place == null) continue;
            var bill = new Bill();
            foreach (var (id, n) in p.Items) bill.Add(id, n);
            yield return (r.Id, r.Type, p.Place, bill);
        }
    }

    /// <summary>
    /// The game's own counts and zones right after a move, against the counts just before it and
    /// the zones on disk: every difference is something the move lost, doubled or put in the wrong
    /// zone. Empty when all is well. <paramref name="now"/>: the totals now, when they count more than
    /// the holders in play (what waits in the ledger for a zone out of play).
    /// </summary>
    public List<string> Audit(IEnumerable<Holder> holders, (int Items, int Blocks) before, (int Items, int Blocks)? now = null)
    {
        var problems = new List<string>();
        var list = holders.ToList();
        foreach (Holder h in list)
        {
            if (!Table.TryGet(h.Id, out SaveRecord r)) problems.Add($"{h.Id} has no record");
            else if (r.ZoneId != h.Zone && !(h.Zone.StartsWith("cabin/") && r.ZoneId.StartsWith("cabin/")))
                problems.Add($"{h.Id} is in {h.Zone} but its record says {r.ZoneId}");
        }
        var (items, blocks) = now ?? Totals(list);
        if (items != before.Items) problems.Add($"{items} items where there were {before.Items}");
        if (blocks != before.Blocks) problems.Add($"{blocks} blocks where there were {before.Blocks}");
        return problems;
    }
}
