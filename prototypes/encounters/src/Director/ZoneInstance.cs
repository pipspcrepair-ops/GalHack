using System;
using System.Collections.Generic;
using System.Numerics;
using static Encounters.Director.MathX;

namespace Encounters.Director;

public enum TouchStatus { Touched, TooFar, NotFound, AlreadyLasting }

public readonly record struct TouchOutcome(TouchStatus Status, TouchKind Kind, WorldEntity Entity);

/// <summary>
/// The director's working copy of one zone that has players in it. Each one gets its own
/// events, budget, cooldowns and random stream, scaled by the players present. It lives only
/// while a player is in the zone: when the last one leaves it cleans up its untouched ships and
/// is dropped, and only the ledger's lasting records remain.
/// </summary>
public sealed class ZoneInstance
{
    public ZoneDef Zone { get; }
    public int Visit { get; }
    public IReadOnlyList<PlayerState> Players => _players;
    public IReadOnlyList<Encounter> Encounters => _encounters;

    /// <summary>Untouched event ships: the only things cleanup can remove.</summary>
    public IReadOnlyList<WorldEntity> Ships => _ships;

    public double NextSpawnAt { get; private set; }
    public IReadOnlyDictionary<string, double> CategoryReadyAt => _categoryReadyAt;
    public IReadOnlyDictionary<string, double> LastSpawnOf => _lastSpawnOf;

    public int Spawns { get; private set; }
    public int SpawnAttempts { get; private set; }
    public int PlacementFailures { get; private set; }

    /// <summary>Candidate spawn points turned down, by <see cref="SpawnVerdict"/>.</summary>
    public readonly int[] Rejections = new int[5];

    public int MaxEvents => Config.MaxEvents(_players.Count);
    public int MaxShips => Config.MaxShips(_players.Count);

    internal readonly Rng Rng;
    readonly EncounterDirector _director;
    readonly List<PlayerState> _players = new();
    readonly List<Encounter> _encounters = new();
    readonly List<WorldEntity> _ships = new();
    readonly Dictionary<string, double> _categoryReadyAt = new();
    readonly Dictionary<string, double> _lastSpawnOf = new();
    readonly List<WeightedEvent> _weights = new();
    readonly List<int> _open = new();
    readonly List<PlannedShip> _plan = new();
    readonly List<ShipScript> _scripts = new();
    int _quickSpawns, _hostileSpawns;

    DirectorConfig Config => _director.Config;
    Ledger Ledger => _director.Ledger;

    internal ZoneInstance(EncounterDirector director, ZoneDef zone, int visit, Rng rng, double time)
    {
        _director = director;
        Zone = zone;
        Visit = visit;
        Rng = rng;
        _quickSpawns = Config.WarmStart.Events;
        NextSpawnAt = time + Config.WarmStart.Delay;
    }

    // ---- Queries ------------------------------------------------------------------------

    public int CategoryCount(string category)
    {
        int n = 0;
        foreach (Encounter enc in _encounters)
            if (enc.Def.Category == category) n++;
        return n;
    }

    public WorldEntity FindShip(long id)
    {
        foreach (WorldEntity ship in _ships)
            if (ship.Id == id) return ship;
        return null;
    }

    public Encounter FindEncounter(long id)
    {
        foreach (Encounter enc in _encounters)
            if (enc.Id == id) return enc;
        return null;
    }

    /// <summary>Why a card can't be drawn right now (cap, cooldown, no-repeat, budget), or null if it can.</summary>
    public string BlockReason(EventDef def, double time)
    {
        if (CategoryCount(def.Category) >= Config.CategoryCap(def.Category)) return "category cap";
        if (_categoryReadyAt.TryGetValue(def.Category, out double ready) && time < ready) return "category cooldown";
        if (_lastSpawnOf.TryGetValue(def.Id, out double last) && time < last + Config.NoRepeatWindow) return "no-repeat";
        if (def.MinShips > MaxShips - _ships.Count) return "ship budget";
        return null;
    }

    /// <summary>The deck's draw chances at a spot in this zone.</summary>
    public List<WeightedEvent> WeightsAt(Vector3 at, List<WeightedEvent> into = null) =>
        DeckWeights.Evaluate(_director.Deck, Zone, at, Config, into);

    // ---- Players ------------------------------------------------------------------------

    internal void AddPlayer(PlayerState player)
    {
        int i = 0;
        while (i < _players.Count && _players[i].Id < player.Id) i++;
        _players.Insert(i, player);

        // A friend arriving mid-event hears the same calls everyone else did.
        foreach (Encounter enc in _encounters)
            if (enc.State == EncounterState.Active && enc.Def.Signal != null)
                _director.EmitLead(this, enc, player);
    }

    internal void RemovePlayer(PlayerState player) => _players.Remove(player);

    // ---- The tick -----------------------------------------------------------------------

    internal void Tick(double time)
    {
        foreach (WorldEntity ship in _ships)
            ship.Sample(time, out ship.Position, out ship.Forward);
        RunTimelines(time);
        Despawn(time);
        DropFinished();
        if (time >= NextSpawnAt) TrySpawn(time);
    }

    void RunTimelines(double time)
    {
        foreach (Encounter enc in _encounters)
        {
            for (int k = enc.WreckSchedule.Count - 1; k >= 0; k--)
            {
                (long shipId, double at) = enc.WreckSchedule[k];
                if (time < at) continue;
                enc.WreckSchedule.RemoveAt(k);
                if (FindShip(shipId) is { } ship) Wreck(ship, time, "lost in the fight");
            }
            if (enc.State == EncounterState.Active && time >= enc.End) WindDown(enc, time);
        }
    }

    /// <summary>The event is over: its ships head off, or, if nobody answered a distress call, it is lost.</summary>
    void WindDown(Encounter enc, double time)
    {
        enc.State = EncounterState.Leaving;
        _director.Log(Zone.Id, "winddown", enc.Id, enc.Def.Id);
        foreach (WorldEntity ship in enc.Ships)
        {
            if (!ship.Live) continue;
            if (enc.Def.Cleanup == CleanupRule.WreckOnExpire) Wreck(ship, time, "nobody answered");
            else ship.Script = DepartScript(enc, ship, time);
        }
    }

    ShipScript DepartScript(Encounter enc, WorldEntity ship, double time)
    {
        if (enc.Def.Behaviour == Behaviour.Drift) return ship.Script; // hulks drift on until out of sight
        ship.Sample(time, out Vector3 position, out Vector3 forward);
        ship.Sample(time - 0.5, out Vector3 before, out _);
        float speed = Vector3.Distance(position, before) / 0.5f;
        Vector3 dir = enc.Def.Behaviour switch
        {
            Behaviour.Pass or Behaviour.Convoy => forward,
            Behaviour.Dock => SafeNormalize(SafeNormalize(Flat(position - Zone.FindPoi(enc.Def.Anchor).Center), forward) + Vector3.UnitY * 0.2f, forward),
            Behaviour.Mine => SafeNormalize(SafeNormalize(Flat(position - ship.Script.Center), forward) + Vector3.UnitY * 0.2f, forward),
            _ => SafeNormalize(forward + SafeNormalize(position - enc.Anchor, forward) * 0.8f, forward), // break off the fight
        };
        return ShipScript.Depart(time, position, forward, dir, MathF.Max(speed, 8f), 14f);
    }

    /// <summary>
    /// Ships of an event that is winding down go once no player could see them go. Any untouched
    /// ship goes once every player is far away. Nothing lasting is ever in this list.
    /// </summary>
    void Despawn(double time)
    {
        float far = Config.Despawn.FarDistance, near = Config.Despawn.MinDistance, coneCos = Config.ViewConeCos;
        for (int i = _ships.Count - 1; i >= 0; i--)
        {
            WorldEntity ship = _ships[i];
            bool farFromAll = true, hiddenFromAll = true;
            foreach (PlayerState player in _players)
            {
                float d = Vector3.Distance(ship.Position, player.Position);
                if (d <= far) farFromAll = false;
                if (d <= near || SpawnRules.InView(ship.Position, player, coneCos)) hiddenFromAll = false;
            }
            if (farFromAll) RemoveTransient(ship, time, "beyond every player's range");
            else if (hiddenFromAll && ship.Encounter.State != EncounterState.Active) RemoveTransient(ship, time, "left, out of sight");
        }
    }

    void DropFinished()
    {
        for (int e = _encounters.Count - 1; e >= 0; e--)
        {
            Encounter enc = _encounters[e];
            if (enc.LiveShips > 0) continue;
            enc.State = EncounterState.Done;
            _encounters.RemoveAt(e);
            _director.Log(Zone.Id, "ended", enc.Id, enc.Def.Id);
        }
    }

    // ---- Spawning -----------------------------------------------------------------------

    void TrySpawn(double time)
    {
        SpawnAttempts++;
        NextSpawnAt = time + Config.RetryDelay; // pushed further out if this attempt spawns
        if (_players.Count == 0 || _encounters.Count >= MaxEvents) return;

        // Weigh the deck where one of the players is: their territory, their danger.
        PlayerState focus = _players[Rng.Index(_players.Count)];
        DeckWeights.Evaluate(_director.Deck, Zone, focus.Position, Config, _weights);

        // The danger grade decides whether this spawn is hostile; the weights decide which card.
        // Drawing from one pool only matters once caps bite: if a capped peaceful card's share
        // spilled over to every open card, a quiet zone would fill up with fights.
        // Caps and cooldowns block the two pools unevenly, so lean towards whichever has fallen
        // behind its share: over a visit, the mix that actually spawns follows the danger grade.
        float hostileShare = 0f;
        foreach (WeightedEvent w in _weights)
            if (w.Def.Hostile) hostileShare += w.Share;
        float realized = Spawns == 0 ? hostileShare : (float)_hostileSpawns / Spawns;
        bool hostile = Rng.NextFloat() < Math.Clamp(hostileShare + 3f * (hostileShare - realized), 0f, 1f);

        float total = 0f;
        _open.Clear();
        for (int i = 0; i < _weights.Count; i++)
        {
            if (_weights[i].Def.Hostile != hostile || _weights[i].Share <= 0f || BlockReason(_weights[i].Def, time) != null) continue;
            _open.Add(i);
            total += _weights[i].Share;
        }
        if (_open.Count == 0) return; // that pool is capped or cooling down: try again shortly

        float pick = Rng.NextFloat() * total;
        WeightedEvent chosen = _weights[_open[^1]];
        foreach (int i in _open)
        {
            pick -= _weights[i].Share;
            if (pick >= 0f) continue;
            chosen = _weights[i];
            break;
        }

        if (!PlanShips(chosen.Def, chosen.SideA, chosen.SideB, MaxShips - _ships.Count)) return;
        if (!TryPlace(chosen.Def, focus, time, out Vector3 anchor))
        {
            PlacementFailures++;
            return;
        }
        Create(chosen.Def, chosen.SideA, chosen.SideB, anchor, focus.Id, time, debug: false);
    }

    /// <summary>Rolls ship counts and crews, adding ships per extra player and trimming to the budget.</summary>
    bool PlanShips(EventDef def, string sideA, string sideB, int shipBudget)
    {
        _plan.Clear();
        int extraPlayers = Math.Max(0, _players.Count - 1);
        Span<int> counts = stackalloc int[def.Ships.Count];
        int total = 0;
        for (int g = 0; g < def.Ships.Count; g++)
        {
            ShipGroupDef group = def.Ships[g];
            counts[g] = Rng.Range(group.Count[0], group.Count[1]) + (int)(group.PerExtraPlayer * extraPlayers);
            total += counts[g];
        }
        while (total > shipBudget)
        {
            int trim = -1;
            for (int g = 0; g < counts.Length; g++)
                if (counts[g] > def.Ships[g].Count[0] && (trim < 0 || counts[g] > counts[trim])) trim = g;
            if (trim < 0) return false;
            counts[trim]--;
            total--;
        }
        for (int g = 0; g < def.Ships.Count; g++)
        {
            ShipGroupDef group = def.Ships[g];
            string faction = group.Faction != "" ? group.Faction : def.Faction != "*" ? def.Faction : group.Side == "b" ? sideB : sideA;
            string hull = _director.Deck.ResolveHull(group.Hull, faction);
            for (int n = 0; n < counts[g]; n++)
                _plan.Add(new PlannedShip(g, group.Role, faction, hull, group.Side, Rng.Range(group.Crew[0], group.Crew[1])));
        }
        return _plan.Count > 0;
    }

    /// <summary>Tries spawn points around the focus player until every ship of the event starts hidden from every player.</summary>
    bool TryPlace(EventDef def, PlayerState focus, double time, out Vector3 anchor)
    {
        float min = Config.MinSpawnDistance, max = Config.MaxSpawnDistance;
        if (def.SpawnDistance != null)
        {
            min = MathF.Max(min, def.SpawnDistance[0]);
            max = MathF.Max(min, def.SpawnDistance[1]);
        }
        float coneCos = Config.ViewConeCos;
        for (int attempt = 0; attempt < Config.SpawnAttempts; attempt++)
        {
            Vector3 dir = Rng.AroundHorizon(Config.SpawnVerticalSpread);
            if (Vector3.Dot(dir, focus.Forward) > coneCos)
            {
                Rejections[(int)SpawnVerdict.InView]++;
                continue;
            }
            anchor = focus.Position + dir * Rng.Range(min, max);
            Vector3 passTarget = Formation.PassTargetNear(anchor, focus.Position, Rng);
            Formation.Build(def, _plan, anchor, passTarget, Zone, Rng, time, _scripts);
            if (AllHidden(time)) return true;
        }
        anchor = default;
        return false;
    }

    bool AllHidden(double time)
    {
        foreach (ShipScript script in _scripts)
        {
            script.Evaluate(time, out Vector3 position, out _);
            SpawnVerdict verdict = SpawnRules.Check(position, Zone, _players, Config);
            if (verdict == SpawnVerdict.Ok) continue;
            Rejections[(int)verdict]++;
            return false;
        }
        return true;
    }

    Encounter Create(EventDef def, string sideA, string sideB, Vector3 anchor, int focusId, double time, bool debug)
    {
        var enc = new Encounter
        {
            Id = Ledger.NextId(), Def = def, ZoneId = Zone.Id, Start = time,
            End = time + Rng.Range(def.Duration[0], def.Duration[1]),
            Anchor = anchor, FocusPlayer = focusId, SideA = sideA, SideB = sideB, Debug = debug,
        };
        int crew = 0;
        for (int i = 0; i < _plan.Count; i++)
        {
            PlannedShip p = _plan[i];
            var ship = new WorldEntity
            {
                Id = Ledger.NextId(), ZoneId = Zone.Id, Kind = EntityKind.NpcShip,
                Hull = p.Hull, Faction = p.Faction, Role = p.Role, Side = p.Side, Crew = p.Crew,
                Hostile = def.Hostile, EncounterId = enc.Id, EventId = def.Id,
                Script = _scripts[i], CreatedAt = time, Encounter = enc, Live = true,
            };
            ship.Sample(time, out ship.Position, out ship.Forward);
            _ships.Add(ship);
            enc.Ships.Add(ship);
            crew += p.Crew;
        }
        ScheduleWrecks(enc);
        _encounters.Add(enc);

        _categoryReadyAt[def.Category] = time + Config.CategoryCooldown(def.Category);
        _lastSpawnOf[def.Id] = time;
        if (!debug)
        {
            float gap = _quickSpawns > 0
                ? Config.WarmStart.Gap
                : Rng.Range(Config.SpawnGap[0], Config.SpawnGap[1]) / (1f + Config.SpawnGapPlayerFactor * Math.Max(0, _players.Count - 1));
            if (_quickSpawns > 0) _quickSpawns--;
            NextSpawnAt = time + gap;
            Spawns++;
            if (def.Hostile) _hostileSpawns++;
        }

        _director.Log(Zone.Id, debug ? "debug" : "spawn", enc.Id,
            Inv($"{def.Id} x{enc.Ships.Count} crew {crew} at ({anchor.X:F0}, {anchor.Y:F0}, {anchor.Z:F0}) for P{focusId}"));
        _director.OnSpawned(this, enc);
        if (def.Signal != null)
            foreach (PlayerState player in _players) _director.EmitLead(this, enc, player);
        return enc;
    }

    /// <summary>Picks which ships of a fight will be wrecked, and when. Someone always flies home.</summary>
    void ScheduleWrecks(Encounter enc)
    {
        WreckDef wrecks = enc.Def.Wrecks;
        if (wrecks == null || enc.Ships.Count < 2) return;
        int count = Math.Min(Rng.Range(wrecks.Count[0], wrecks.Count[1]), enc.Ships.Count - 1);
        var order = new int[enc.Ships.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        for (int k = 0; k < count; k++)
        {
            int j = k + Rng.Index(order.Length - k);
            (order[k], order[j]) = (order[j], order[k]);
            double at = enc.Start + (enc.End - enc.Start) * Rng.Range(wrecks.When[0], wrecks.When[1]);
            enc.WreckSchedule.Add((enc.Ships[order[k]].Id, at));
        }
    }

    /// <summary>For screenshots and tests: spawns a card at a given spot, ignoring the spawn rules and budget.</summary>
    internal Encounter ForceSpawn(EventDef def, Vector3 anchor, Vector3 passTarget, double time)
    {
        string sideA = null, sideB = null;
        foreach (WeightedEvent w in WeightsAt(anchor))
            if (w.Def == def) (sideA, sideB) = (w.SideA, w.SideB);
        if (def.Territory == TerritoryRule.Border && (sideA == null || sideB == null))
        {
            using var factions = _director.Deck.Factions.Keys.GetEnumerator();
            factions.MoveNext(); sideA = factions.Current;
            factions.MoveNext(); sideB = factions.Current;
        }
        if (!PlanShips(def, sideA, sideB, int.MaxValue)) return null;
        Formation.Build(def, _plan, anchor, passTarget, Zone, Rng, time, _scripts);
        return Create(def, sideA, sideB, anchor, _players.Count > 0 ? _players[0].Id : -1, time, debug: true);
    }

    // ---- Touch, wrecks and cleanup ------------------------------------------------------

    internal TouchOutcome Touch(PlayerState player, long entityId, double time)
    {
        WorldEntity entity = FindShip(entityId);
        bool transient = entity != null;
        if (entity == null && Ledger.TryGet(entityId, out WorldEntity lasting) && lasting.ZoneId == Zone.Id) entity = lasting;
        if (entity == null) return new TouchOutcome(TouchStatus.NotFound, TouchKind.None, null);

        entity.Sample(time, out Vector3 at, out _);
        if (Vector3.Distance(at, player.Position) > Config.TouchDistance) return new TouchOutcome(TouchStatus.TooFar, TouchKind.None, entity);
        if (entity.Touch != TouchKind.None || entity.PlayerOwned) return new TouchOutcome(TouchStatus.AlreadyLasting, entity.Touch, entity);

        TouchKind kind = entity.Wrecked ? TouchKind.Looted
            : entity.EventId == "distress-call" || entity.Role == "hulk" ? TouchKind.Boarded
            : TouchKind.Claimed;
        entity.Touch = kind;
        entity.TouchedBy = player.Id;
        if (kind == TouchKind.Claimed) entity.Owner = player.Id;

        string what = Inv($"{kind} by P{player.Id}");
        if (transient)
        {
            entity.Encounter.Touched = true;
            Promote(entity, time, what);
        }
        else Ledger.Note(entity, time, what);
        _director.Log(Zone.Id, "touch", entity.Id, Inv($"{entity.Hull} {what}"));
        return new TouchOutcome(TouchStatus.Touched, kind, entity);
    }

    /// <summary>A ship is lost. If a player saw it happen, the wreck stays as salvage; otherwise it just goes.</summary>
    void Wreck(WorldEntity ship, double time, string why)
    {
        bool witnessed = false;
        foreach (PlayerState player in _players)
            if (Vector3.Distance(ship.Position, player.Position) <= Config.WreckWitnessDistance) witnessed = true;
        if (!witnessed || ship.Encounter.Def.Cleanup == CleanupRule.Despawn)
        {
            RemoveTransient(ship, time, "wrecked unseen");
            return;
        }
        ship.Wrecked = true;
        ship.WreckedAt = time;
        ship.Crew = Rng.Range(0, ship.Crew / 2); // survivors aboard, for a later rescue
        Promote(ship, time, Inv($"wreck of a {ship.Faction} {ship.Hull}, {why}"));
        _director.Log(Zone.Id, "wreck", ship.Id, Inv($"{ship.Hull} ({ship.Faction}) {why}"));
    }

    /// <summary>Moves a ship out of the instance's reach into the ledger, frozen where it is now.</summary>
    void Promote(WorldEntity ship, double time, string why)
    {
        ship.Sample(time, out ship.Position, out ship.Forward);
        ship.Script = null;
        ship.Live = false;
        ship.Encounter = null;
        _ships.Remove(ship);
        Ledger.Keep(ship, time, why);
    }

    /// <summary>
    /// The only way anything leaves the world. It refuses anything lasting and keeps it instead:
    /// unreachable through the director's own paths, but a future system that marks a ship
    /// owned without promoting it must not be able to lose it.
    /// </summary>
    void RemoveTransient(WorldEntity ship, double time, string why)
    {
        if (ship.IsLasting)
        {
            Ledger.GuardTrips++;
            Promote(ship, time, "kept by the cleanup guard instead of: " + why);
            _director.Log(Zone.Id, "guard", ship.Id, why);
            return;
        }
        _ships.Remove(ship);
        ship.Live = false;
        _director.Log(Zone.Id, "despawn", ship.Id, why);
    }

    /// <summary>The last player left: every untouched ship goes, everything lasting stays in the ledger.</summary>
    internal void Close(double time)
    {
        int transient = _ships.Count;
        for (int i = _ships.Count - 1; i >= 0; i--) RemoveTransient(_ships[i], time, "zone closed");
        foreach (Encounter enc in _encounters) enc.State = EncounterState.Done;
        _encounters.Clear();
        _director.Log(Zone.Id, "close", 0,
            Inv($"{transient} untouched ships cleaned up, {Ledger.InZone(Zone.Id).Count} lasting records kept"));
    }
}
