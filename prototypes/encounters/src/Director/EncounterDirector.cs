using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using static Encounters.Director.MathX;

namespace Encounters.Director;

/// <summary>
/// The encounter director: makes the galaxy feel alive around the players without simulating
/// it. It deals small events from a weighted deck into the zones players are in, keeps them out
/// of sight until they are underway, scales them by the players present, and cleans them up
/// when players leave. Only lasting facts outlive a zone: what players own, what they touched
/// and the wrecks they saw, all kept in the <see cref="Ledger"/>.
///
/// Pure C#, no engine: the host feeds it player positions and view directions and a clock,
/// and reads back ships, records and leads to draw. The same seed and the same inputs give
/// the same events.
/// </summary>
public sealed class EncounterDirector
{
    public DirectorConfig Config { get; }
    public ZoneCatalog Zones { get; }
    public EventDeck Deck { get; }
    public Ledger Ledger { get; }
    public ulong Seed { get; }

    /// <summary>Simulated time, advanced only by ticks.</summary>
    public double Time { get; private set; }

    /// <summary>Simulated time including the part of a tick not yet run: where to sample ships for drawing.</summary>
    public double RenderTime => Time + _accumulator;

    public TickStats Stats { get; } = new();
    public IReadOnlyList<ZoneInstance> Instances => _instances;
    public IReadOnlyList<SignalLead> Leads => _leads;
    public IReadOnlyList<LogEntry> RecentLog => _recent;
    public IEnumerable<PlayerState> Players => _players.Values;

    public event Action<LogEntry> Logged;
    public event Action<ZoneInstance, Encounter> Spawned;
    public event Action<SignalLead> LeadEmitted;

    readonly SortedDictionary<int, PlayerState> _players = new();
    readonly List<ZoneInstance> _instances = new();
    readonly List<SignalLead> _leads = new();
    readonly Dictionary<string, int> _visits = new();
    readonly List<LogEntry> _recent = new();
    double _accumulator;
    long _nextLeadId = 1;
    const int RecentLogSize = 120;

    public EncounterDirector(DirectorConfig config, ZoneCatalog zones, EventDeck deck, ulong seed, Ledger ledger = null)
    {
        Config = config;
        Zones = zones;
        Deck = deck;
        Seed = seed;
        Ledger = ledger ?? new Ledger();
    }

    public PlayerState Player(int id) => _players.TryGetValue(id, out PlayerState p) ? p : null;

    public ZoneInstance Instance(string zoneId)
    {
        foreach (ZoneInstance zi in _instances)
            if (zi.Zone.Id == zoneId) return zi;
        return null;
    }

    // ---- Players ------------------------------------------------------------------------

    /// <summary>
    /// Where a player is and where they look. Call it every frame (the host runs every zone that
    /// has a player in it). Changing zone opens the new zone's instance and, if they were the
    /// last one there, closes the old one.
    /// </summary>
    public void SetPlayer(int id, string zoneId, Vector3 position, Vector3 forward)
    {
        if (!_players.TryGetValue(id, out PlayerState player)) _players[id] = player = new PlayerState { Id = id };
        player.Position = position;
        player.Forward = SafeNormalize(forward, -Vector3.UnitZ);
        if (player.ZoneId == zoneId) return;
        Zones.Get(zoneId); // fail loudly on an unknown zone, before leaving the old one
        if (player.ZoneId != null) Leave(player);
        player.ZoneId = zoneId;
        Join(player);
    }

    public void RemovePlayer(int id)
    {
        if (!_players.Remove(id, out PlayerState player)) return;
        if (player.ZoneId != null) Leave(player);
    }

    void Join(PlayerState player)
    {
        ZoneInstance zi = Instance(player.ZoneId) ?? Open(player.ZoneId);
        Log(zi.Zone.Id, "join", player.Id, Inv($"P{player.Id} arrives"));
        zi.AddPlayer(player);
    }

    void Leave(PlayerState player)
    {
        ZoneInstance zi = Instance(player.ZoneId);
        _leads.RemoveAll(lead => lead.PlayerId == player.Id);
        zi.RemovePlayer(player);
        Log(zi.Zone.Id, "leave", player.Id, Inv($"P{player.Id} leaves"));
        if (zi.Players.Count > 0) return;
        zi.Close(Time);
        _instances.Remove(zi);
    }

    ZoneInstance Open(string zoneId)
    {
        int visit = _visits.TryGetValue(zoneId, out int v) ? v + 1 : 1;
        _visits[zoneId] = visit;
        // Each zone visit draws from its own stream: events in one zone never shift another's.
        var rng = new Rng(Rng.Mix(Seed, StableHash.Of(zoneId) + (ulong)visit));
        var zi = new ZoneInstance(this, Zones.Get(zoneId), visit, rng, Time);
        int i = 0;
        while (i < _instances.Count && string.CompareOrdinal(_instances[i].Zone.Id, zoneId) < 0) i++;
        _instances.Insert(i, zi);
        Log(zoneId, "open", visit, Inv($"visit {visit}"));
        return zi;
    }

    // ---- Time ---------------------------------------------------------------------------

    /// <summary>Runs as many fixed ticks as the elapsed time covers.</summary>
    public void Advance(double seconds)
    {
        _accumulator += seconds;
        double step = Config.TickSeconds;
        for (int ticks = 0; _accumulator >= step && ticks < 10; ticks++)
        {
            _accumulator -= step;
            Tick();
        }
        _accumulator = Math.Min(_accumulator, step); // after a long hitch, catch up a little, never spiral
    }

    public void Tick()
    {
        long start = Stopwatch.GetTimestamp();
        Time += Config.TickSeconds;
        foreach (ZoneInstance zi in _instances) zi.Tick(Time);
        UpdateLeads();
        Stats.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
    }

    // ---- What players do ----------------------------------------------------------------

    /// <summary>A player loots, claims or boards something. It becomes a lasting record on the spot.</summary>
    public TouchOutcome Touch(int playerId, long entityId)
    {
        PlayerState player = Player(playerId);
        ZoneInstance zi = player?.ZoneId == null ? null : Instance(player.ZoneId);
        if (zi == null) return new TouchOutcome(TouchStatus.NotFound, TouchKind.None, null);
        return zi.Touch(player, entityId, RenderTime);
    }

    /// <summary>Something a player built, dropped or stored. It goes straight into the ledger.</summary>
    public WorldEntity RegisterPlayerOwned(int owner, string zoneId, EntityKind kind, string hull, string label, Vector3 position, Vector3 forward)
    {
        if (owner < 0) throw new ArgumentOutOfRangeException(nameof(owner), "player-owned needs an owner");
        Zones.Get(zoneId);
        var entity = new WorldEntity
        {
            Id = Ledger.NextId(), ZoneId = zoneId, Kind = kind, Hull = hull, Label = label,
            Owner = owner, Position = position, Forward = SafeNormalize(forward, -Vector3.UnitZ), CreatedAt = Time,
        };
        Ledger.Keep(entity, Time, Inv($"{label}, owned by P{owner}"));
        Log(zoneId, "owned", entity.Id, label);
        return entity;
    }

    /// <summary>For screenshots and tests: spawns a card at a spot, ignoring the spawn rules and the budget.</summary>
    public Encounter DebugSpawn(string zoneId, string eventId, Vector3 anchor, Vector3 passTarget)
    {
        ZoneInstance zi = Instance(zoneId) ?? throw new InvalidOperationException($"no players in '{zoneId}'");
        return zi.ForceSpawn(Deck.Get(eventId), anchor, passTarget, Time);
    }

    // ---- Leads --------------------------------------------------------------------------

    public IEnumerable<SignalLead> LeadsFor(int playerId)
    {
        foreach (SignalLead lead in _leads)
            if (lead.PlayerId == playerId) yield return lead;
    }

    /// <summary>
    /// Points a player towards an event: the true direction turned by up to the fuzz angle and
    /// the true distance scaled by up to the fuzz fraction. A lead, not a waypoint.
    /// </summary>
    internal void EmitLead(ZoneInstance zi, Encounter enc, PlayerState player)
    {
        WorldEntity target = null;
        foreach (WorldEntity ship in enc.Ships)
            if (ship.Live) { target = ship; break; }
        if (target == null) return;

        Vector3 truth = target.Position - player.Position;
        float distance = truth.Length();
        if (distance < 1f) return;
        Vector3 trueDir = truth / distance;

        LeadConfig fuzz = Config.Lead;
        float angle = fuzz.AngleFuzzDeg * Deg * MathF.Sqrt(zi.Rng.NextFloat()); // spread evenly over the cone, not bunched at its centre
        Vector3 axis = Rotate(AnyPerpendicular(trueDir), trueDir, zi.Rng.Range(0f, MathF.Tau));
        var lead = new SignalLead
        {
            Id = _nextLeadId++, PlayerId = player.Id, ZoneId = zi.Zone.Id, EncounterId = enc.Id, TargetId = target.Id,
            Kind = enc.Def.Signal.Kind, Label = enc.Def.Signal.Label,
            Origin = player.Position,
            Direction = Vector3.Normalize(Rotate(trueDir, axis, angle)),
            Distance = distance * (1f + zi.Rng.Range(-fuzz.DistanceFuzz, fuzz.DistanceFuzz)),
            EmittedAt = Time, TrueOffset = truth,
        };
        _leads.Add(lead);
        Log(zi.Zone.Id, "lead", lead.Id, Inv($"{lead.Kind} for P{player.Id}: ~{lead.Distance:F0} m"));
        LeadEmitted?.Invoke(lead);
    }

    /// <summary>A lead resolves when a player gets close, and expires when its event winds down or its target is gone.</summary>
    void UpdateLeads()
    {
        for (int i = _leads.Count - 1; i >= 0; i--)
        {
            SignalLead lead = _leads[i];
            ZoneInstance zi = Instance(lead.ZoneId);
            Encounter enc = zi?.FindEncounter(lead.EncounterId);
            WorldEntity target = zi?.FindShip(lead.TargetId);
            PlayerState player = Player(lead.PlayerId);
            string end = null;
            if (enc is not { State: EncounterState.Active } || target == null || player?.ZoneId != lead.ZoneId) end = "expired";
            else if (Vector3.Distance(player.Position, target.Position) <= Config.Lead.ResolveDistance) end = "resolved";
            if (end == null) continue;
            _leads.RemoveAt(i);
            Log(lead.ZoneId, "lead-" + end, lead.Id, Inv($"{lead.Kind} for P{lead.PlayerId}"));
        }
    }

    // ---- Log ----------------------------------------------------------------------------

    internal void Log(string zone, string kind, long id, string text)
    {
        var entry = new LogEntry(Time, zone, kind, id, text);
        if (_recent.Count == RecentLogSize) _recent.RemoveAt(0);
        _recent.Add(entry);
        Logged?.Invoke(entry);
    }

    internal void OnSpawned(ZoneInstance zi, Encounter enc) => Spawned?.Invoke(zi, enc);
}
