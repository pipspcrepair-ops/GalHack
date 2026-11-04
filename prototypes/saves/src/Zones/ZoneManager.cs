using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalHack.Persistence;
using GalHack.Records;
using Godot;

namespace Saves.Zones;

/// <summary>What a zone is made of. The game supplies it; the manager decides when it runs.</summary>
public interface IZoneContent
{
    /// <summary>
    /// Runs on a worker thread: everything that doesn't need the scene tree (layouts, textures,
    /// meshes as arrays). Must not touch nodes or the record table.
    /// </summary>
    object Generate(string zoneId);

    /// <summary>Main-thread steps that turn the generated data into nodes, run a few milliseconds' worth per frame.</summary>
    IEnumerable<Action> Build(Zone zone, object generated);

    /// <summary>
    /// Makes the node for one record in this zone and registers it with
    /// <see cref="Zone.Register"/>. A ship also spawns and registers its cabin's records.
    /// </summary>
    void Spawn(Zone zone, SaveRecord record);

    /// <summary>Writes live node state (where ships and players have moved to) back into their records.</summary>
    void Capture(Zone zone);
}

/// <summary>
/// The host's live zones. Each one with a player in it runs as its own isolated world; only the
/// active one is drawn. At most <see cref="MaxLive"/> are live at once, and an empty one lingers
/// for <see cref="LingerSeconds"/> so hopping between surface and orbit never reloads it.
///
/// Zones load in the background: generation on a worker thread, then node building in small
/// slices per frame, so a transition can start loading early and never freezes a frame.
///
/// A zone always saves before it unloads, and checks on load that every record it should hold
/// came back. Nothing is ever dropped: a record that can't be spawned is reported, not removed.
/// </summary>
public partial class ZoneManager : Node
{
    public int MaxLive { get; set; } = 4;
    public double LingerSeconds { get; set; } = 120;

    /// <summary>How long node building may take per frame.</summary>
    public double BuildBudgetMs { get; set; } = 4;

    /// <summary>Extra seconds every background load takes, to see how a slow PC behaves.</summary>
    public double SimulatedSlowness { get; set; }

    public IZoneContent Content { get; set; }
    public RecordTable Table { get; set; }

    /// <summary>Commits right now, waiting for any save in progress. False if the commit failed.</summary>
    public Func<string, FaultInjector, bool> CommitNow { get; set; }

    readonly Dictionary<string, Zone> _live = new(StringComparer.Ordinal);
    readonly HashSet<string> _occupied = new(StringComparer.Ordinal);
    double _clock;

    public IReadOnlyCollection<Zone> Live => _live.Values;
    public Zone Active { get; private set; }
    public double Clock => _clock;

    /// <summary>Set when a zone could not load or unload; shown on the HUD.</summary>
    public string Trouble { get; private set; }

    public event Action<Zone> ZoneReady;
    public event Action<string> ZoneUnloaded;

    public Zone Get(string zoneId) => _live.GetValueOrDefault(zoneId);

    public bool IsReady(string zoneId) => _live.TryGetValue(zoneId, out Zone z) && z.IsReady;

    /// <summary>The zones that have a player in them this frame. Everything else lingers.</summary>
    public void SetOccupied(IEnumerable<string> zoneIds)
    {
        _occupied.Clear();
        foreach (string id in zoneIds) _occupied.Add(id);
    }

    public bool IsOccupied(string zoneId) => _occupied.Contains(zoneId);

    /// <summary>Starts loading a zone in the background, if it isn't live already. Null if the cap leaves no room.</summary>
    public Zone Preload(string zoneId)
    {
        if (_live.TryGetValue(zoneId, out Zone existing)) return existing;
        if (_live.Count >= MaxLive && !EvictOne())
        {
            Trouble = $"can't load {zoneId}: all {MaxLive} live zones have players in them";
            return null;
        }

        var zone = new Zone { ZoneId = zoneId, Name = zoneId.Replace('/', '_'), LingerLeft = LingerSeconds, RequestedAt = _clock };
        _live[zoneId] = zone;
        AddChild(zone);
        double slowness = SimulatedSlowness;
        IZoneContent content = Content;
        var watch = Stopwatch.StartNew();
        zone.Generating = Task.Run(() =>
        {
            if (slowness > 0) Thread.Sleep(TimeSpan.FromSeconds(slowness));
            object data = content.Generate(zoneId);
            zone.GenerateSeconds = watch.Elapsed.TotalSeconds;
            return data;
        });
        return zone;
    }

    /// <summary>
    /// Loads a zone completely before returning: for starting the game, and tests. Null if it
    /// could not load; <see cref="Trouble"/> says why.
    /// </summary>
    public Zone LoadNow(string zoneId)
    {
        Zone zone = Preload(zoneId);
        if (zone == null) return null;
        while (!zone.IsReady)
        {
            Advance(zone, double.PositiveInfinity);
            if (Get(zoneId) != zone) return null; // its load failed and it was dropped
        }
        return zone;
    }

    /// <summary>Makes a zone the one that is drawn. Every other zone keeps simulating, unseen.</summary>
    public void SetActive(Zone zone, Vector2I size)
    {
        foreach (Zone z in _live.Values)
        {
            bool drawn = z == zone;
            z.RenderTargetUpdateMode = drawn ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
            z.Size = drawn ? size : new Vector2I(2, 2);
        }
        Active = zone;
    }

    public override void _Process(double delta) => Tick(delta);

    /// <summary>Advances loading, building and linger timers. Tests call it directly with simulated time.</summary>
    public void Tick(double delta)
    {
        _clock += delta;
        foreach (Zone zone in _live.Values.ToList())
        {
            if (!zone.IsReady)
            {
                Advance(zone, BuildBudgetMs);
                continue;
            }
            if (_occupied.Contains(zone.ZoneId) || zone == Active)
            {
                zone.LingerLeft = LingerSeconds;
                continue;
            }
            zone.LingerLeft -= delta;
            if (zone.LingerLeft <= 0 && !Unload(zone))
                zone.LingerLeft = 10; // the save failed: stay live, try again shortly
        }
    }

    /// <summary>Runs a zone's loading for up to <paramref name="budgetMs"/> of this frame.</summary>
    void Advance(Zone zone, double budgetMs)
    {
        if (zone.State == ZoneState.Loading)
        {
            if (!zone.Generating.IsCompleted)
            {
                if (!double.IsPositiveInfinity(budgetMs)) return;
                try { zone.Generating.Wait(); }
                catch (AggregateException) { } // looked at just below
            }
            if (!zone.Generating.IsCompletedSuccessfully)
            {
                // A load that failed must say so and let go, never sit at "loading" for ever.
                // It has no nodes or records of its own yet, so dropping it loses nothing.
                Trouble = $"{zone.ZoneId} failed to load: {zone.Generating.Exception?.GetBaseException().Message ?? "cancelled"}";
                GD.PrintErr(Trouble);
                _live.Remove(zone.ZoneId);
                RemoveChild(zone);
                zone.QueueFree();
                return;
            }
            zone.GeneratedAt = _clock;
            zone.BuildSteps = new Queue<Action>(Content.Build(zone, zone.Generating.Result));
            foreach (SaveRecord record in RecordsFor(zone.ZoneId).ToList())
            {
                SaveRecord r = record;
                zone.BuildSteps.Enqueue(() => Content.Spawn(zone, r));
            }
            zone.BuildStepsTotal = zone.BuildSteps.Count;
            zone.State = ZoneState.Building;
            return; // building starts next frame, so this frame only paid for the queue
        }

        var watch = Stopwatch.StartNew();
        zone.BuildFrames++;
        while (zone.BuildSteps.Count > 0 && watch.Elapsed.TotalMilliseconds < budgetMs)
        {
            double before = watch.Elapsed.TotalMilliseconds;
            zone.BuildSteps.Dequeue()();
            zone.BuildStepsDone++;
            zone.LongestBuildStepMs = Math.Max(zone.LongestBuildStepMs, watch.Elapsed.TotalMilliseconds - before);
        }
        if (zone.BuildSteps.Count > 0) return;

        AuditSpawned(zone);
        zone.State = ZoneState.Live;
        zone.ReadyAt = _clock;
        zone.LingerLeft = LingerSeconds;
        ZoneReady?.Invoke(zone);
    }

    /// <summary>The records a zone spawns itself: its own. Ships spawn their cabins' records.</summary>
    IEnumerable<SaveRecord> RecordsFor(string zoneId) =>
        Table.InZone(zoneId).OrderBy(r => r.Type == RecordTypes.Ship ? 0 : 1).ThenBy(r => r.Id, StringComparer.Ordinal);

    /// <summary>Everything recorded in this zone, and aboard its ships, must have come back.</summary>
    void AuditSpawned(Zone zone)
    {
        zone.AuditProblems.Clear();
        foreach (string id in ExpectedIn(zone.ZoneId))
            if (!zone.Spawned.ContainsKey(id))
                zone.AuditProblems.Add($"{id} is recorded in {zone.ZoneId} but did not come back");
        if (zone.AuditProblems.Count > 0) Trouble = zone.AuditProblems[0];
    }

    /// <summary>Ids of every record in a zone, including the contents of ships there.</summary>
    public IEnumerable<string> ExpectedIn(string zoneId)
    {
        foreach (SaveRecord r in Table.InZone(zoneId))
        {
            yield return r.Id;
            if (r.Type == RecordTypes.Ship)
                foreach (SaveRecord aboard in Table.InZone(ZoneIds.Cabin(r.Id)))
                    yield return aboard.Id;
        }
    }

    /// <summary>Makes room under the cap: unloads the empty zone closest to unloading anyway.</summary>
    bool EvictOne()
    {
        Zone victim = _live.Values
            .Where(z => z.IsReady && z != Active && !_occupied.Contains(z.ZoneId))
            .OrderBy(z => z.LingerLeft)
            .FirstOrDefault();
        return victim != null && Unload(victim);
    }

    /// <summary>
    /// Saves a zone, then unloads it. Its records stay in the table and on disk; only the nodes
    /// go. If the save fails, the zone stays live and nothing is lost.
    /// </summary>
    public bool Unload(Zone zone)
    {
        if (zone == Active || _occupied.Contains(zone.ZoneId)) return false;
        Content.Capture(zone);
        if (CommitNow != null && !CommitNow($"saving {zone.ZoneId} before it unloads", null))
        {
            Trouble = $"{zone.ZoneId} could not save, so it stays live";
            return false;
        }
        _live.Remove(zone.ZoneId);
        RemoveChild(zone);
        zone.QueueFree();
        ZoneUnloaded?.Invoke(zone.ZoneId);
        return true;
    }

    /// <summary>
    /// Moves a thing (with everything aboard it) from one live zone to another. The record is
    /// written first: <paramref name="writeRecord"/> changes its zone field, and the commit makes
    /// that the truth on disk. Only then is the node removed from the old zone and added to the
    /// new one. A crash at any point leaves it in the old zone or the new one after a reload,
    /// never both and never neither, because the record has only one zone field.
    /// </summary>
    public bool Cross(string recordId, Node3D node, Zone from, Zone to, Action writeRecord, Action<Node3D> placeNode, FaultInjector faults = null)
    {
        if (from == to || !to.IsReady) return false;
        faults ??= new FaultInjector();
        SaveRecord before = Table.Get(recordId);
        // Whatever is aboard lives in the ship's cabin zone: it rides along without being written.
        var riders = Table.InZone(ZoneIds.Cabin(recordId))
            .Select(r => (r.Id, Node: from.Spawned.GetValueOrDefault(r.Id)))
            .ToList();

        // 1. The record, then the commit.
        writeRecord();
        if (CommitNow != null && !CommitNow($"crossing {recordId} to {to.ZoneId}", faults))
        {
            Table.Put(before); // undo in memory too: it never left
            Trouble = $"{recordId} could not cross to {to.ZoneId}: the save failed, so it stays put";
            return false;
        }

        // 2. Out of the old zone...
        node.GetParent().RemoveChild(node);
        from.Unregister(recordId);
        foreach (var (id, _) in riders) from.Unregister(id);
        faults.Point("crossing: removed from the old zone");

        // 3. ...and into the new one, passengers and cargo aboard untouched.
        to.Root.AddChild(node);
        placeNode(node);
        to.Register(recordId, node);
        foreach (var (id, rider) in riders)
            if (rider != null) to.Register(id, rider);
        faults.Point("crossing: spawned in the new zone");
        return true;
    }

    /// <summary>Frees every zone without saving: for a reload, which goes back to what is on disk.</summary>
    public void DropAll()
    {
        foreach (Zone zone in _live.Values)
        {
            RemoveChild(zone);
            zone.QueueFree();
        }
        _live.Clear();
        Active = null;
    }
}
