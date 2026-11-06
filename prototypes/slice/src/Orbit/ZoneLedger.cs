using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalHack.Items;
using GalHack.Persistence;

namespace Slice.Orbit;

/// <summary>Something the ledger keeps a record of: the castaway, a structure, a container on the ground or a device.</summary>
public readonly record struct Holder(string Id, string Type, string Zone, string Ship, string Name, Bill Items, int Blocks);

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
    public const string Surface = "surface", Orbit = "orbit";
    public const string Castaway = "castaway";

    /// <summary>The zone of anything riding in a vessel's cabin (saves' convention).</summary>
    public static string Cabin(string shipId) => "cabin/" + shipId;

    public static readonly string[] Types = { "castaway", "structure", "container", "device" };

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
        return s;
    }

    sealed class Payload
    {
        public string Name { get; set; }
        public Dictionary<string, int> Items { get; set; } = new();
        public int Blocks { get; set; }
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static (int Items, int Blocks) Count(string payload)
    {
        Payload p = JsonSerializer.Deserialize<Payload>(payload, Json);
        return (p.Items.Values.Sum(), p.Blocks);
    }

    static SaveRecord Make(Holder h)
    {
        var p = new Payload { Name = h.Name, Blocks = h.Blocks };
        foreach (var (id, n) in h.Items) if (n > 0) p.Items[id] = n;
        return new SaveRecord
        {
            Id = h.Id, Type = h.Type, ZoneId = h.Zone, ShipId = h.Ship, Owner = h.Type == "castaway" ? "player" : null,
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
        // Holders that are gone (a body emptied, a wreck taken apart) are removed empty.
        foreach (string gone in Table.All.Select(r => r.Id).Where(id => !seen.Contains(id)).ToList())
        {
            SaveRecord r = Table.Get(gone);
            Table.Put(r with { Items = 0, Blocks = 0, Payload = JsonSerializer.Serialize(new Payload { Name = "gone" }, Json) });
            Table.Remove(gone);
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

    /// <summary>
    /// The game's own counts and zones right after a move, against the counts just before it and
    /// the zones on disk: every difference is something the move lost, doubled or put in the wrong
    /// zone. Empty when all is well.
    /// </summary>
    public List<string> Audit(IEnumerable<Holder> holders, (int Items, int Blocks) before)
    {
        var problems = new List<string>();
        var list = holders.ToList();
        foreach (Holder h in list)
        {
            if (!Table.TryGet(h.Id, out SaveRecord r)) problems.Add($"{h.Id} has no record");
            else if (r.ZoneId != h.Zone && !(h.Zone.StartsWith("cabin/") && r.ZoneId.StartsWith("cabin/")))
                problems.Add($"{h.Id} is in {h.Zone} but its record says {r.ZoneId}");
        }
        var (items, blocks) = Totals(list);
        if (items != before.Items) problems.Add($"{items} items where there were {before.Items}");
        if (blocks != before.Blocks) problems.Add($"{blocks} blocks where there were {before.Blocks}");
        return problems;
    }
}
