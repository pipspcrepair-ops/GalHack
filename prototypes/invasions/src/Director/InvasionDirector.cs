using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Invasions.Director;

/// <summary>
/// Decides when, where from and with what player bases are invaded, and how each invasion ends. Pure C#
/// with a simulated clock and a seed: no scene nodes, no wall-clock time.
///
/// Rules it enforces:
/// - A base is only invaded while at least one player is in its zone. The countdown to the next
///   invasion only runs while someone is home, and an invasion is called off (or withdraws) if
///   everyone leaves, so nobody logs in to a wrecked home.
/// - Invaders come from the nearest active base, within range, of any faction that invades and is
///   hostile to the players (<see cref="FactionStandings"/>). With none in range they come by ship
///   from one of those factions, if the server allows ship raids. Destroying or hijacking a base stops
///   the invasions it sends; a faction that stops being hostile calls off its invasions.
/// - Warnings fire at exact offsets before arrival on the simulated clock, however coarsely the
///   clock is advanced. Sirens start with the warning that asks for them and stop when it ends.
/// - Core destroyed: the base goes offline and the invaders withdraw. All invaders down or gone
///   with the core standing: victory. A new core brings the base back online.
/// - Invasions can be switched off (a world setting, the host's in co-op). Off: nothing new is
///   scheduled, an incoming invasion is called off and an assault withdraws, as when a faction
///   stands down. Back on: a fresh countdown starts from then, with the full warnings.
///
/// The scene feeds it presence, base stats, invader counts, core and enemy base changes and standing
/// changes, and drains <see cref="DrainEvents"/> each frame to show warnings, spawn forces and sound sirens.
/// </summary>
public sealed class InvasionDirector
{
    public InvasionData Data { get; }
    public ServerSettings Server { get; }
    public ulong Seed { get; }
    public FactionStandings Standings { get; }

    /// <summary>Simulated seconds since the director started.</summary>
    public double Now { get; private set; }

    /// <summary>Debug: <see cref="Tick"/> runs the clock <see cref="ServerSettings.FastTimeScale"/> times faster.</summary>
    public bool FastTime { get; set; }

    /// <summary>Whether invasions happen at all in this world (<see cref="SetInvasionsEnabled"/>).</summary>
    public bool InvasionsEnabled { get; private set; }

    public IReadOnlyList<PlayerBase> Bases => _bases;
    public IReadOnlyList<EnemyBase> EnemyBases => _enemies;

    /// <summary>Every event since the start, in order. Two runs with the same seed and inputs log the same lines.</summary>
    public IReadOnlyList<DirectorEvent> Log => _log;

    readonly List<PlayerBase> _bases = new();
    readonly List<EnemyBase> _enemies = new();
    readonly List<DirectorEvent> _log = new();
    readonly List<DirectorEvent> _undrained = new();
    int _nextInvasionId = 1;

    public InvasionDirector(InvasionData data, ServerSettings server, ulong seed)
    {
        Data = data;
        Server = server;
        Seed = seed;
        Standings = new FactionStandings(data.Factions);
        InvasionsEnabled = server.InvasionsEnabled;
    }

    // ---- World set-up -----------------------------------------------------------------

    public PlayerBase AddPlayerBase(string id, string name, GroundPos position)
    {
        var b = new PlayerBase
        {
            Id = id,
            Name = name,
            Position = position,
            Rng = new DeterministicRng(DeterministicRng.Derive(Seed, DeterministicRng.Hash(id))),
        };
        _bases.Add(b);
        b.HadSource = HasSource(b);
        b.CountdownRemaining = DrawInterval(b);
        Refresh(b);
        return b;
    }

    public EnemyBase AddEnemyBase(string id, string name, string faction, EnemyBaseKind kind, GroundPos position)
    {
        Data.Faction(faction); // refuse an unknown faction now rather than at the first invasion
        var e = new EnemyBase { Id = id, Name = name, Faction = faction, Kind = kind, Position = position };
        _enemies.Add(e);
        foreach (PlayerBase b in _bases) Refresh(b);
        return e;
    }

    public PlayerBase Base(string id) => _bases.First(b => b.Id == id);
    public EnemyBase Enemy(string id) => _enemies.First(e => e.Id == id);

    // ---- Inputs from the game ---------------------------------------------------------

    /// <summary>How many players are inside the base's zone right now.</summary>
    public void SetPlayersPresent(string baseId, int count)
    {
        PlayerBase b = Base(baseId);
        count = Math.Max(0, count);
        if (b.PlayersPresent == count) return;
        b.PlayersPresent = count;
        if (count > 0) b.AbsentSince = null;
        else if (b.Active is { Phase: InvasionPhase.Incoming or InvasionPhase.Assault }) b.AbsentSince ??= Now;
        Refresh(b);
    }

    public void SetBaseStats(string baseId, BaseStats stats) => Base(baseId).Stats = stats;

    /// <summary>Invaders still in the fight: alive and not yet gone, including vehicle crews and troops aboard.</summary>
    public void ReportInvadersRemaining(string baseId, int count)
    {
        PlayerBase b = Base(baseId);
        Invasion inv = b.Active;
        if (inv is not { Phase: InvasionPhase.Assault or InvasionPhase.Withdrawing }) return;
        inv.InvadersRemaining = Math.Max(0, count);
        if (inv.InvadersRemaining > 0) return;
        if (inv.Phase == InvasionPhase.Assault) End(b, inv, InvasionOutcome.Victory, Format(Message("victory", inv), b, inv));
        else End(b, inv, inv.Outcome, Format(Message("withdrawn", inv), b, inv));
    }

    public void ReportCoreDestroyed(string baseId)
    {
        PlayerBase b = Base(baseId);
        if (!b.CoreAlive) return;
        b.CoreAlive = false;
        Emit(DirectorEventKind.BaseOffline, b, b.Active, 0, Format(Message("offline", b.Active), b, b.Active));

        Invasion inv = b.Active;
        if (inv?.Phase == InvasionPhase.Assault)
        {
            Withdraw(b, inv, InvasionOutcome.CoreDestroyed, "coreDestroyed");
        }
        else if (inv?.Phase == InvasionPhase.Incoming)
        {
            End(b, inv, InvasionOutcome.CalledOff, Format(Message("calledOff", inv), b, inv));
        }
        Refresh(b);
    }

    public void ReportCorePlaced(string baseId)
    {
        PlayerBase b = Base(baseId);
        if (b.CoreAlive) return;
        b.CoreAlive = true;
        Emit(DirectorEventKind.BaseOnline, b, b.Active, 0, Format(Message("online", b.Active), b, b.Active));
        ResetCountdown(b);
        Refresh(b);
    }

    /// <summary>An enemy base was knocked out or hijacked (or rebuilt). Only active bases send invasions.</summary>
    public void SetEnemyBaseState(string enemyId, EnemyBaseState state)
    {
        EnemyBase e = Enemy(enemyId);
        if (e.State == state) return;
        if (state != EnemyBaseState.Active)
        {
            string key = state == EnemyBaseState.Hijacked ? "sourceHijacked" : "sourceDestroyed";
            Emit(DirectorEventKind.SourceLost, null, null, 0, Format(Message(key, null), null, null, e.Name));
        }
        // Its incoming invasions are called off; bases it was going to invade are retimed for whoever replaces it.
        ChangeSources(() => e.State = state,
            b => state != EnemyBaseState.Active && b.Active is { Phase: InvasionPhase.Incoming } inv && inv.Source.EnemyBaseId == e.Id
                ? Format(Message("calledOff", inv), b, inv)
                : null,
            null);
    }

    // ---- Standing ---------------------------------------------------------------------

    /// <summary>
    /// A wrong done to a faction (an attack on its people or ships, a broken deal). Deliberate wrongs
    /// count in full; accidental ones count by the faction's accident weight, so some factions can't be
    /// turned hostile by accident at all.
    /// </summary>
    public void Wrong(string faction, double amount, bool deliberate) =>
        SetStanding(faction, Standings.AfterWrong(faction, amount, deliberate));

    /// <summary>Something that pleases a faction: a job done, a payment, a truce.</summary>
    public void Favour(string faction, double amount) => SetStanding(faction, Standings[faction] + Math.Max(0, amount));

    /// <summary>
    /// Sets a faction's standing (held to its floor and ceiling). A faction that turns hostile starts
    /// counting as a source; one that stops being hostile calls off its incoming invasions and pulls out
    /// of any assault under way.
    /// </summary>
    public void SetStanding(string faction, double value)
    {
        bool invadedBefore = Standings.Invades(faction);
        if (Standings[faction] == value) return;
        bool invadesNow = invadedBefore;
        ChangeSources(() =>
            {
                Standings.Set(faction, value);
                invadesNow = Standings.Invades(faction);
            },
            b => b.Active is { Phase: InvasionPhase.Incoming } inv && inv.Faction == faction && !invadesNow
                ? Format(Message("standDown", inv), b, inv)
                : null,
            b =>
            {
                if (b.Active is { Phase: InvasionPhase.Assault } inv && inv.Faction == faction && !invadesNow)
                    Withdraw(b, inv, InvasionOutcome.CalledOff, "standDownWithdraw");
            });
        if (invadedBefore == invadesNow) return;
        FactionData f = Data.Faction(faction);
        foreach (PlayerBase b in _bases)
            Emit(DirectorEventKind.StandingChanged, b, null, 0, Format(Data.Message(invadesNow ? "nowHostile" : "noLongerHostile", f), b, null, attacker: f));
    }

    /// <summary>
    /// Applies a change to who can send invasions, then: calls off the incoming invasions it ends
    /// (<paramref name="callOff"/> returns the message, or null to leave one be), lets
    /// <paramref name="alsoForBase"/> deal with assaults under way, and gives every base whose source
    /// changed a fresh countdown, timed for whoever replaces it.
    /// </summary>
    void ChangeSources(Action change, Func<PlayerBase, string> callOff, Action<PlayerBase> alsoForBase)
    {
        var before = _bases.ToDictionary(b => b, NearestSourceBase);
        bool[] hadShips = _bases.Select(HasShipRaid).ToArray();
        change();
        for (int i = 0; i < _bases.Count; i++)
        {
            PlayerBase b = _bases[i];
            if (callOff(b) is string text)
            {
                End(b, b.Active, InvasionOutcome.CalledOff, text); // End() draws a fresh countdown
                continue;
            }
            alsoForBase?.Invoke(b);
            if (NearestSourceBase(b) != before[b] || HasShipRaid(b) != hadShips[i]) ResetCountdown(b);
        }
        foreach (PlayerBase b in _bases) Refresh(b);
    }

    // ---- Options ----------------------------------------------------------------------

    /// <summary>
    /// Switches invasions off or on for this world. Off: no new invasion is scheduled, an incoming
    /// one is called off (its warnings stop, and its sirens) and an assault under way withdraws, the
    /// same way as when its faction stands down. Nothing already damaged or spilled is undone. On:
    /// scheduling starts again from now, with a fresh countdown, so the next invasion comes on the
    /// usual timing with all its warnings, never at once.
    /// </summary>
    public void SetInvasionsEnabled(bool on)
    {
        if (InvasionsEnabled == on) return;
        InvasionsEnabled = on;
        Emit(DirectorEventKind.InvasionsToggled, null, null, 0, Data.Message(on ? "enabled" : "disabled"));
        foreach (PlayerBase b in _bases)
        {
            if (!on && b.Active is { Phase: InvasionPhase.Incoming } incoming)
                End(b, incoming, InvasionOutcome.CalledOff, Format(Message("disabledCalledOff", incoming), b, incoming));
            else if (!on && b.Active is { Phase: InvasionPhase.Assault } assault)
                Withdraw(b, assault, InvasionOutcome.CalledOff, "disabledWithdraw");
            if (on) ResetCountdown(b);
            Refresh(b);
        }
    }

    // ---- Debug controls ---------------------------------------------------------------

    /// <summary>
    /// Starts the full warning timeline now (key I). The presence rule still applies: no player
    /// home, no core, no source, or an invasion already under way, and nothing happens.
    /// </summary>
    public bool StartInvasionNow(string baseId)
    {
        PlayerBase b = Base(baseId);
        if (!InvasionsEnabled || b.PlayersPresent == 0 || !b.CoreAlive || b.Active != null || !HasSource(b)) return false;
        b.NextAt = Now;
        Advance(0);
        return b.Active != null;
    }

    /// <summary>Debug: brings the next warning (or the arrival) forward to now.</summary>
    public bool SkipToNextWarning(string baseId)
    {
        PlayerBase b = Base(baseId);
        if (b.Active is not { Phase: InvasionPhase.Incoming } inv) return false;
        var steps = Data.OrderedWarnings;
        inv.ArrivalAt = Now + (inv.NextWarning < steps.Count ? steps[inv.NextWarning].SecondsBeforeArrival : 0);
        Advance(0);
        return true;
    }

    // ---- Clock ------------------------------------------------------------------------

    /// <summary>Advances by real seconds, compressed by fast time.</summary>
    public void Tick(double realSeconds) => Advance(realSeconds * (FastTime ? Server.FastTimeScale : 1.0));

    /// <summary>
    /// Advances the simulated clock. Everything due inside the step happens at its own exact time,
    /// in order, so a warning due at T-300 s is stamped T-300 s even if the clock jumps a whole minute.
    /// </summary>
    public void Advance(double seconds)
    {
        double end = Now + Math.Max(0, seconds);
        for (int guard = 0; guard < 10000; guard++)
        {
            var due = NextDue(end);
            if (due == null) break;
            Now = Math.Max(Now, due.Value.Time);
            // Stamp with the exact due time: arrival - 600 can land one rounding step before the
            // moment it was scheduled, and the record should still say exactly arrival - 600.
            _stamp = due.Value.Time;
            due.Value.Run();
            _stamp = null;
        }
        Now = end;
    }

    double? _stamp;

    /// <summary>Simulated seconds of player presence left before the base's next invasion, if it is counting down.</summary>
    public double? NextInvasionIn(string baseId)
    {
        PlayerBase b = Base(baseId);
        if (b.Active != null || !InvasionsEnabled) return null;
        return b.NextAt is double at ? at - Now : (HasSource(b) && b.CoreAlive ? b.CountdownRemaining : null);
    }

    public List<DirectorEvent> DrainEvents()
    {
        var events = new List<DirectorEvent>(_undrained);
        _undrained.Clear();
        return events;
    }

    (double Time, Action Run)? NextDue(double end)
    {
        (double Time, Action Run)? best = null;
        void Consider(double? time, Action run)
        {
            if (time is double t && t <= end && (best == null || t < best.Value.Time)) best = (t, run);
        }

        var steps = Data.OrderedWarnings;
        foreach (PlayerBase b in _bases)
        {
            PlayerBase target = b;
            Consider(b.NextAt, () => BeginInvasion(target));
            if (b.Active is not { } inv) continue;
            if (inv.Phase == InvasionPhase.Incoming)
            {
                if (inv.NextWarning < steps.Count)
                    Consider(inv.ArrivalAt - steps[inv.NextWarning].SecondsBeforeArrival, () => IssueWarning(target, inv));
                else
                    Consider(inv.ArrivalAt, () => Arrive(target, inv));
            }
            if (b.AbsentSince is double since && inv.Phase is InvasionPhase.Incoming or InvasionPhase.Assault)
                Consider(since + Data.Presence.AbandonGraceSeconds, () => Abandon(target, inv));
        }
        return best;
    }

    // ---- Sources ----------------------------------------------------------------------

    /// <summary>
    /// The nearest base within range of a player base that can send an invasion: still active, and its
    /// faction invades and is hostile. Null if there is none.
    /// </summary>
    public EnemyBase NearestSourceBase(PlayerBase b) =>
        _enemies
            .Where(e => e.CanSendInvasions && Standings.Invades(e.Faction) && e.Position.DistanceTo(b.Position) <= Data.Sources.NearbyRange)
            .OrderBy(e => e.Position.DistanceTo(b.Position))
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>The factions that could send a ship raid right now, in id order: hostile, invading, with ships.</summary>
    public IReadOnlyList<FactionData> ShipRaiders() =>
        Server.ShipRaids ? Data.Factions.All.Where(f => f.CanRaidByShip && Standings.Invades(f.Id)).ToList() : Array.Empty<FactionData>();

    bool HasShipRaid(PlayerBase b) => ShipRaiders().Count > 0;

    public bool HasSource(PlayerBase b) => NearestSourceBase(b) != null || HasShipRaid(b);

    /// <summary>
    /// Where the next invasion of this base comes from: the nearest hostile base in range, else a ship of
    /// a hostile faction, picked by ship-raid weight, from a random bearing (if the server allows ship
    /// raids), else nowhere.
    /// </summary>
    public InvasionSource ChooseSource(PlayerBase b, DeterministicRng rng)
    {
        EnemyBase e = NearestSourceBase(b);
        if (e != null)
        {
            return new InvasionSource
            {
                Kind = e.Kind switch
                {
                    EnemyBaseKind.Outpost => SourceKind.Outpost,
                    EnemyBaseKind.Fortress => SourceKind.Fortress,
                    _ => SourceKind.Garrison,
                },
                Faction = e.Faction,
                EnemyBaseId = e.Id,
                Name = e.Name,
                Position = e.Position,
                Bearing = b.Position.BearingTo(e.Position),
                Distance = b.Position.DistanceTo(e.Position),
            };
        }
        var raiders = ShipRaiders();
        if (raiders.Count == 0) return null;

        string factionId = rng.PickWeighted(raiders.Select(f => new KeyValuePair<string, double>(f.Id, f.ShipRaidWeight)).ToList());
        FactionData faction = Data.Faction(factionId);
        double bearing = Math.Floor(rng.Range(0, 360));
        string name = faction.ShipNames.Count > 0 ? rng.Pick(faction.ShipNames) : $"a {faction.Name} ship";
        double distance = Data.Sources.ShipScanDistance;
        return new InvasionSource
        {
            Kind = SourceKind.Ship,
            Faction = factionId,
            Name = name,
            Position = b.Position + GroundPos.FromBearing(bearing, distance),
            Bearing = bearing,
            Distance = distance,
        };
    }

    /// <summary>Where the long-range scan places the force: it closes from its source to the deploy line by arrival.</summary>
    public GroundPos? ScanPosition(Invasion inv)
    {
        if (!inv.OnLongRangeScan || inv.Phase != InvasionPhase.Incoming) return null;
        PlayerBase b = Base(inv.BaseId);
        WarningStep firstScan = Data.OrderedWarnings.FirstOrDefault(w => w.LongRangeScan);
        double window = Math.Max(1, firstScan?.SecondsBeforeArrival ?? 1);
        double t = Math.Clamp(1 - inv.SecondsToArrival(Now) / window, 0, 1);
        GroundPos deploy = b.Position + (inv.Source.Position - b.Position).Normalized() *
            Math.Min(Data.Approach.DeployDistance, inv.Source.Distance);
        return inv.Source.Position.Lerp(deploy, t);
    }

    // ---- What happens -----------------------------------------------------------------

    void BeginInvasion(PlayerBase b)
    {
        b.NextAt = null;
        var rng = new DeterministicRng(DeterministicRng.Derive(Seed, DeterministicRng.Hash(b.Id), (ulong)++b.InvasionsBegun));
        InvasionSource source = ChooseSource(b, rng);
        if (source == null || !InvasionsEnabled || b.PlayersPresent == 0 || !b.CoreAlive)
        {
            Refresh(b);
            return;
        }

        var inv = new Invasion
        {
            Id = _nextInvasionId++,
            BaseId = b.Id,
            Source = source,
            ScheduledAt = Now,
            ArrivalAt = Now + Data.Timeline.LeadSeconds,
            Rng = rng,
        };
        b.Active = inv;
        b.History.Add(inv);
        Emit(DirectorEventKind.InvasionScheduled, b, inv, 0,
            string.Create(CultureInfo.InvariantCulture, $"arrival at {inv.ArrivalAt:R} from {source}"));
        // The first warning has the full lead time, so it falls due right now and fires next.
    }

    void IssueWarning(PlayerBase b, Invasion inv)
    {
        WarningStep step = Data.OrderedWarnings[inv.NextWarning++];
        inv.WarningLevel = Math.Max(inv.WarningLevel, step.Level);

        bool newlyOnScan = step.LongRangeScan && !inv.OnLongRangeScan;
        if (newlyOnScan)
        {
            // The force musters when it shows up on scans, sized by the base and the players home right now.
            inv.Force ??= Compose(b, inv);
            inv.OnLongRangeScan = true;
        }

        string text = Format(Data.WarningMessage(step, Data.Faction(inv.Faction)), b, inv, minutes: step.SecondsBeforeArrival / 60);
        Emit(DirectorEventKind.Warning, b, inv, step.Level, text);
        if (newlyOnScan) Emit(DirectorEventKind.ForceOnScan, b, inv, step.Level, inv.Force.ToString());
        if (step.Siren && !inv.SirenOn)
        {
            inv.SirenOn = true;
            Emit(DirectorEventKind.SirenOn, b, inv, step.Level, "sirens on");
        }
    }

    void Arrive(PlayerBase b, Invasion inv)
    {
        inv.Force ??= Compose(b, inv);
        inv.Phase = InvasionPhase.Assault;
        inv.ArrivedAt = Now;
        inv.InvadersRemaining = inv.Force.TotalInvaders;
        if (!inv.SirenOn)
        {
            inv.SirenOn = true; // an assault under way always has the sirens going
            Emit(DirectorEventKind.SirenOn, b, inv, 0, "sirens on");
        }
        Emit(DirectorEventKind.ForceArrived, b, inv, 0, inv.Force.ToString());
    }

    void Abandon(PlayerBase b, Invasion inv)
    {
        b.AbsentSince = null;
        if (inv.Phase == InvasionPhase.Incoming)
        {
            End(b, inv, InvasionOutcome.Abandoned, Format(Message("abandoned", inv), b, inv));
            return;
        }
        Withdraw(b, inv, InvasionOutcome.Abandoned, "abandonedWithdraw");
    }

    /// <summary>An assault under way pulls out. Sirens keep sounding until the last invader is gone.</summary>
    void Withdraw(PlayerBase b, Invasion inv, InvasionOutcome outcome, string messageKey)
    {
        inv.Outcome = outcome;
        inv.Phase = InvasionPhase.Withdrawing;
        b.AbsentSince = null;
        Emit(DirectorEventKind.Withdraw, b, inv, 0, Format(Message(messageKey, inv), b, inv));
        if (inv.InvadersRemaining == 0) End(b, inv, inv.Outcome, Format(Message("withdrawn", inv), b, inv));
    }

    void End(PlayerBase b, Invasion inv, InvasionOutcome outcome, string text)
    {
        inv.Outcome = outcome == InvasionOutcome.None ? InvasionOutcome.Victory : outcome;
        inv.Phase = InvasionPhase.Ended;
        inv.EndedAt = Now;
        if (inv.SirenOn)
        {
            inv.SirenOn = false;
            Emit(DirectorEventKind.SirenOff, b, inv, 0, "sirens off");
        }
        Emit(DirectorEventKind.InvasionEnded, b, inv, 0, $"{inv.Outcome}: {text}");
        b.Active = null;
        b.AbsentSince = null;
        ResetCountdown(b);
        Refresh(b);
    }

    ForcePlan Compose(PlayerBase b, Invasion inv) =>
        ForceComposer.Compose(b.Stats, Math.Max(1, b.PlayersPresent), inv.Source.Kind, Data.Faction(inv.Faction), Data, inv.Rng);

    // ---- Scheduling -------------------------------------------------------------------

    /// <summary>Starts or parks the countdown to the next invasion, and notices sources coming and going.</summary>
    void Refresh(PlayerBase b)
    {
        bool hasSource = HasSource(b);
        if (!hasSource && b.HadSource)
        {
            b.HadSource = false;
            Emit(DirectorEventKind.InvasionsStopped, b, null, 0, Format(Message("stopped", null), b, null));
        }
        else if (hasSource && !b.HadSource)
        {
            b.HadSource = true;
            ResetCountdown(b);
            Emit(DirectorEventKind.InvasionsResumed, b, null, 0, Format(Message("resumed", null), b, null));
        }

        bool counting = InvasionsEnabled && b.PlayersPresent > 0 && b.CoreAlive && b.Active == null && hasSource;
        if (counting && b.NextAt == null) b.NextAt = Now + b.CountdownRemaining;
        else if (!counting && b.NextAt is double at)
        {
            b.CountdownRemaining = Math.Max(0, at - Now);
            b.NextAt = null;
        }
    }

    void ResetCountdown(PlayerBase b)
    {
        double interval = DrawInterval(b);
        if (b.NextAt != null) b.NextAt = Now + interval;
        else b.CountdownRemaining = interval;
    }

    /// <summary>Presence-seconds until the next invasion: the server's interval, jittered, longer for ship raids.</summary>
    double DrawInterval(PlayerBase b)
    {
        double jitter = 1 + Server.IntervalJitter * (2 * b.Rng.NextDouble() - 1);
        double seconds = Server.InvasionIntervalMinutes * 60 * Math.Max(0.05, jitter);
        if (NearestSourceBase(b) == null) seconds *= Data.Sources.ShipRaidIntervalMultiplier;
        return seconds;
    }

    // ---- Events and text --------------------------------------------------------------

    void Emit(DirectorEventKind kind, PlayerBase b, Invasion inv, int level, string text)
    {
        var e = new DirectorEvent(_stamp ?? Now, kind, b?.Id ?? "", inv?.Id ?? 0, level, text);
        _log.Add(e);
        _undrained.Add(e);
    }

    /// <summary>A message in the invading faction's words if it has its own, else the shared wording.</summary>
    string Message(string key, Invasion inv) => Data.Message(key, inv != null ? Data.Faction(inv.Faction) : null);

    string Format(string template, PlayerBase b, Invasion inv, string sourceName = null, double? minutes = null, FactionData attacker = null)
    {
        var ci = CultureInfo.InvariantCulture;
        attacker ??= inv != null ? Data.Faction(inv.Faction) : null;
        string text = template
            .Replace("{attacker}", attacker?.Name ?? "hostile")
            .Replace("{base}", b?.Name ?? "the base")
            .Replace("{source}", sourceName ?? inv?.Source.Name ?? "?");
        if (inv != null)
        {
            text = text
                .Replace("{bearing}", inv.Source.BearingDegrees.ToString("000", ci) + "°")
                .Replace("{compass}", inv.Source.Compass)
                .Replace("{minutes}", (minutes ?? inv.SecondsToArrival(Now) / 60).ToString("0.#", ci));
            if (inv.Force != null)
            {
                text = text
                    .Replace("{tier}", inv.Force.Tier)
                    .Replace("{troops}", inv.Force.Troops.ToString(ci))
                    .Replace("{vehicles}", inv.Force.VehicleSummary());
            }
        }
        return text;
    }

    /// <summary>Formats a message about an invasion for the scene (arrivals, broken assaults, raids ending).</summary>
    public string Describe(string key, Invasion inv) => Format(Message(key, inv), Base(inv.BaseId), inv);
}
