using System;
using System.Collections.Generic;
using Godot;

namespace Weather.Model;

/// <summary>
/// One stretch of the timeline: a weather that blends in from <see cref="From"/> over
/// <see cref="Transition"/> seconds, then holds until the next segment starts.
/// </summary>
public sealed class Segment
{
    public WeatherKind Kind { get; init; }
    public WeatherKind FromKind { get; init; }
    public double Start { get; init; }
    public double Duration { get; internal set; }
    public float Transition { get; init; }
    public Conditions From { get; init; }
    public Conditions To { get; init; }
    public float WindHeading { get; init; }

    /// <summary>Forced by a debug key: arrives with no forecast lead.</summary>
    public bool Forced { get; init; }

    /// <summary>When the visor first shows the forecast, and when a severe warning goes up.</summary>
    public double ForecastAt { get; init; }
    public double WarningAt { get; init; }

    public double End => Start + Duration;
}

public readonly record struct ForecastEntry(WeatherKind Kind, string Name, double Eta, bool Severe);

public enum WarningLevel { Advisory, Warning, Danger }

/// <summary>
/// A visor warning. <see cref="ArrivesAt"/> is when the danger lands; <see cref="Kind"/> is set for
/// weather warnings and <see cref="HazardId"/> for strikes (lightning, debris). For a symbol-only
/// visor: <see cref="Sign"/> names the hazard's sign, <see cref="Left"/> is how long it lasts once
/// here (seconds; 0 when unknown) and <see cref="From"/> is where it is or comes from.
/// </summary>
public readonly record struct WarningEntry(WarningLevel Level, string Text, double Eta, WeatherKind? Kind = null, double ArrivesAt = 0, int HazardId = 0,
    string Sign = null, double Left = 0, Vector3? From = null);

/// <summary>Weather that lingers on the ground after it has passed.</summary>
public struct GroundCover
{
    public float Wetness, Snow, Dust;
}

/// <summary>
/// The weather model: plain C#, no scene nodes, deterministic from a seed.
///
/// The timeline is a list of segments generated ahead of time from the planet archetype's
/// weights, so a forecast is simply a look at segments that haven't started yet. Conditions
/// at any moment are a pure function of the timeline: a smoothstep blend from the previous
/// weather to the next, stretched so no parameter changes faster than its data-defined max
/// rate. Hazards (lightning, falling debris, tornadoes) draw from a second random stream, so
/// where the player walks never changes what weather comes next.
/// </summary>
public sealed class WeatherSim
{
    public WeatherData Data { get; }
    public Archetype Archetype { get; private set; }
    public ulong Seed { get; }
    public double Time { get; private set; }
    public Conditions Current { get; private set; }
    public GroundCover Ground { get; private set; }

    /// <summary>Things lightning can hit. The world adds them and keeps their positions current.</summary>
    public List<StrikePoint> StrikePoints { get; } = new();

    /// <summary>Terrain height, for strikes on bare ground and for tornado bases.</summary>
    public Func<float, float, float> GroundHeight { get; set; } = static (_, _) => 0f;

    public IReadOnlyList<Segment> Timeline => _segments;
    public IReadOnlyList<Hazard> Hazards => _hazards;
    public IReadOnlyList<Tornado> Tornadoes => _tornadoes;

    /// <summary>The storms in the sky: where the rain, strikes and funnels come from.</summary>
    public IReadOnlyList<StormCell> Cells => _cells;

    /// <summary>What happened during the last <see cref="Step"/>.</summary>
    public IReadOnlyList<WeatherEvent> Events => _events;

    readonly List<Segment> _segments = new();
    readonly List<Hazard> _hazards = new();
    readonly List<Tornado> _tornadoes = new();
    readonly List<StormCell> _cells = new();
    float _cellTimer;
    readonly List<WeatherEvent> _events = new();
    readonly Rng _timelineRng, _hazardRng;
    readonly uint _gustSeed;
    float _lightningCharge, _lightningNext, _debrisCharge, _debrisNext, _tornadoCharge, _tornadoNext;
    int _nextId = 1;

    public WeatherSim(WeatherData data, string archetypeId, ulong seed, WeatherKind? start = null)
    {
        Data = data;
        Archetype = data.Archetype(archetypeId);
        Seed = seed;
        _timelineRng = new Rng(seed);
        _hazardRng = new Rng(seed ^ 0xA5A5_5A5A_C3C3_3C3CUL);
        _gustSeed = (uint)(seed * 2654435761UL);
        _lightningNext = _hazardRng.Exponential();
        _debrisNext = _hazardRng.Exponential();
        _tornadoNext = _hazardRng.Exponential();

        WeatherKind kind = start ?? PickNext(null);
        float heading = _timelineRng.Range(0f, MathF.Tau);
        var settled = Conditions.FromState(data.State(kind), Archetype.BaseTemperature, heading);
        _segments.Add(new Segment
        {
            Kind = kind, FromKind = kind, Start = 0, Duration = PickDuration(data.State(kind), 0f),
            From = settled, To = settled, WindHeading = heading,
        });
        EnsureTimeline(Horizon);
        Current = ConditionsAt(0);
    }

    /// <summary>The timeline is always known this far ahead, so every forecast can go up on time.</summary>
    double Horizon => Data.MaxLead + 60.0;

    // ---- Ticking ----------------------------------------------------------------------

    /// <summary>Advances the weather. <paramref name="observer"/> is where the player is: hazards happen around them.</summary>
    public void Step(float dt, Vector3 observer)
    {
        _events.Clear();
        Time += dt;
        EnsureTimeline(Time + Horizon);
        Current = ConditionsAt(Time);
        UpdateGround(dt);
        StepCells(dt, observer);
        StepStrikes(dt, observer);
        StepTornadoes(dt, observer);
    }

    void UpdateGround(float dt)
    {
        GroundRules r = Data.Ground;
        Conditions c = Current;
        GroundCover g = Ground;
        float drying = (1f - MathF.Min(1f, c.Rain * 2f)) * (0.4f + c.Solar);
        g.Wetness = Math.Clamp(g.Wetness + (c.Rain * r.WetRate - drying * r.DryRate) * dt, 0f, 1f);
        g.Snow = Math.Clamp(g.Snow + (c.Snow * r.SnowRate - MathF.Max(0f, c.Temperature) * r.MeltRate * (1f - c.Snow)) * dt, 0f, 1f);
        g.Dust = Math.Clamp(g.Dust + (c.Dust * r.DustRate - (1f - c.Dust) * (r.DustClearRate + c.Rain * r.DustClearRate * 10f)) * dt, 0f, 1f);
        Ground = g;
    }

    // ---- Timeline ---------------------------------------------------------------------

    /// <summary>The conditions at any time on the timeline: a pure function, so tests can sample it freely.</summary>
    public Conditions ConditionsAt(double time)
    {
        Segment s = SegmentAt(time);
        return Conditions.Lerp(s.From, s.To, Blend(s, time));
    }

    public Segment SegmentAt(double time)
    {
        int lo = 0, hi = _segments.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_segments[mid].Start <= time) lo = mid;
            else hi = mid - 1;
        }
        return _segments[lo];
    }

    static float Blend(Segment s, double time)
    {
        if (s.Transition <= 0f) return 1f;
        float t = (float)Math.Clamp((time - s.Start) / s.Transition, 0.0, 1.0);
        return t * t * (3f - 2f * t);
    }

    /// <summary>The weather that dominates right now (the incoming one once it is more than half in).</summary>
    public WeatherKind Dominant
    {
        get
        {
            Segment s = SegmentAt(Time);
            return Blend(s, Time) >= 0.5f ? s.Kind : s.FromKind;
        }
    }

    /// <summary>The weather currently blending in, and how far in it is (0..1).</summary>
    public (WeatherKind kind, float blend) Incoming
    {
        get
        {
            Segment s = SegmentAt(Time);
            return (s.Kind, Blend(s, Time));
        }
    }

    public void EnsureTimeline(double until)
    {
        while (_segments[^1].End < until)
        {
            Segment last = _segments[^1];
            WeatherKind kind = PickNext(last.Kind);
            WeatherState state = Data.State(kind);
            float heading = last.WindHeading + _timelineRng.Range(-0.9f, 0.9f);
            var to = Conditions.FromState(state, Archetype.BaseTemperature, heading);
            float transition = MathF.Max(Data.Transitions.NaturalSeconds, Conditions.RequiredSeconds(last.To, to, Data.MaxRates));
            double start = last.End;
            _segments.Add(new Segment
            {
                Kind = kind, FromKind = last.Kind, Start = start, Duration = PickDuration(state, transition),
                Transition = transition, From = last.To, To = to, WindHeading = heading,
                ForecastAt = start - state.ForecastLead,
                WarningAt = state.Severe ? start - state.WarningLead : double.MaxValue,
            });
        }
    }

    WeatherKind PickNext(WeatherKind? previous)
    {
        float total = 0f;
        foreach (var (kind, weight) in Archetype.Weights)
            if (kind != previous) total += weight;
        float pick = _timelineRng.NextFloat() * total;
        WeatherKind chosen = previous ?? WeatherKind.Clear;
        foreach (var (kind, weight) in Archetype.Weights)
        {
            if (kind == previous) continue;
            chosen = kind;
            pick -= weight;
            if (pick < 0f) break;
        }
        return chosen;
    }

    /// <summary>Every weather holds for a while after it has fully arrived.</summary>
    float PickDuration(WeatherState state, float transition) =>
        MathF.Max(_timelineRng.Range(state.DurationMin, state.DurationMax), transition + 30f);

    /// <summary>
    /// Debug: bring in a weather now. It still blends in smoothly from whatever the sky is
    /// doing at this moment (even mid-transition), but it gets no forecast lead.
    /// </summary>
    public void Force(WeatherKind kind) => Replace(kind, snap: false);

    /// <summary>Tests and screenshots: jump straight to a weather, no transition.</summary>
    public void Snap(WeatherKind kind)
    {
        Replace(kind, snap: true);
        Current = ConditionsAt(Time);
    }

    /// <summary>Moves to another kind of planet. The weather turns toward that planet's typical skies.</summary>
    public void SetArchetype(string id)
    {
        Archetype = Data.Archetype(id);
        Replace(PickNext(null), snap: false);
    }

    void Replace(WeatherKind kind, bool snap)
    {
        Conditions now = ConditionsAt(Time);
        WeatherKind dominant = Dominant;
        Segment current = SegmentAt(Time);
        int index = _segments.IndexOf(current);
        _segments.RemoveRange(index + 1, _segments.Count - index - 1);
        if (current.Start >= Time && _segments.Count > 1) _segments.RemoveAt(index); // replaced again this tick
        else current.Duration = Time - current.Start;

        WeatherState state = Data.State(kind);
        float heading = MathF.Atan2(now[Param.WindZ], now[Param.WindX]) + _timelineRng.Range(-0.5f, 0.5f);
        var to = Conditions.FromState(state, Archetype.BaseTemperature, heading);
        float transition = snap ? 0f : MathF.Max(Data.Transitions.MinSeconds, Conditions.RequiredSeconds(now, to, Data.MaxRates));
        var segment = new Segment
        {
            Kind = kind, FromKind = dominant, Start = Time, Duration = MathF.Max(state.DurationMax, transition + 30f),
            Transition = transition, From = snap ? to : now, To = to, WindHeading = heading, Forced = true,
            ForecastAt = Time, WarningAt = state.Severe ? Time : double.MaxValue,
        };
        if (_segments.Count == 1 && _segments[0].Duration <= 0) _segments[0] = segment;
        else _segments.Add(segment);
        EnsureTimeline(Time + Horizon);
    }

    // ---- Forecast and warnings --------------------------------------------------------

    /// <summary>Upcoming weather whose forecast has gone up, soonest first.</summary>
    public void GetForecast(List<ForecastEntry> into)
    {
        into.Clear();
        foreach (Segment s in _segments)
        {
            if (s.Start <= Time || Time < s.ForecastAt) continue;
            WeatherState state = Data.State(s.Kind);
            into.Add(new ForecastEntry(s.Kind, state.Name, s.Start - Time, state.Severe));
        }
    }

    /// <summary>Everything dangerous the visor should be shouting about, most urgent first.</summary>
    public void GetWarnings(List<WarningEntry> into, Vector3 observer)
    {
        into.Clear();
        foreach (Hazard h in _hazards)
        {
            float distance = Flat(h.Target - observer).Length();
            if (h.Kind == HazardKind.Lightning && distance < 350f)
            {
                string what = h.TargetPoint?.Name ?? "open ground";
                into.Add(new WarningEntry(WarningLevel.Danger,
                    $"Lightning charge building on {what}, {distance:F0} m {Bearing(observer, h.Target)}", h.Remaining,
                    ArrivesAt: Time + h.Remaining, HazardId: h.Id, Sign: "storm", From: h.Target));
            }
            else if (h.Kind == HazardKind.Debris && distance < 60f)
            {
                into.Add(new WarningEntry(WarningLevel.Danger,
                    $"Burning debris incoming, {distance:F0} m {Bearing(observer, h.Target)}", h.Remaining,
                    ArrivesAt: Time + h.Remaining, HazardId: h.Id, Sign: "falling_debris", From: h.Target));
            }
        }

        foreach (Tornado t in _tornadoes)
        {
            float distance = Flat(t.Position - observer).Length();
            if (!t.Touchdown)
                into.Add(new WarningEntry(WarningLevel.Danger,
                    $"Funnel forming {distance:F0} m {Bearing(observer, t.Position)}, touchdown in {t.Rules.FormSeconds - t.Age:F0} s",
                    t.Rules.FormSeconds - t.Age, Sign: "tornado", From: t.Position));
            else if (!t.Dissipating || distance < 200f)
                into.Add(new WarningEntry(WarningLevel.Danger,
                    $"Tornado {distance:F0} m {Bearing(observer, t.Position)}{(t.Homing ? ", heading your way" : "")}", 0,
                    Sign: "tornado", Left: Math.Max(0, t.Rules.Lifetime - t.Age), From: t.Position));
        }

        foreach (Segment s in _segments)
        {
            if (Time < s.WarningAt || Time >= s.End) continue;
            // Only a real hazard warns, and by its own sign: clear skies, clouds and fog never do.
            if (SignOf(s.Kind) == null) continue;
            WeatherState state = Data.State(s.Kind);
            double eta = s.Start - Time;
            string text = eta > 0 ? $"{state.Name} in {Clock(eta)}. {state.Warning}" : state.Warning;
            // Weather comes in on the wind: from upwind of the observer.
            var upwind = new Vector3(-MathF.Cos(s.WindHeading), 0f, -MathF.Sin(s.WindHeading));
            into.Add(new WarningEntry(WarningLevel.Warning, text, Math.Max(0, eta), s.Kind, s.Start,
                Sign: SignOf(s.Kind), Left: Math.Max(0, s.End - Math.Max(Time, s.Start)), From: observer + upwind * 2000f));
        }
    }

    /// <summary>
    /// The sign a weather is warned with (the names of the game's sign set), or null for weather that is
    /// no danger on foot and so never warns: clear skies, clouds and fog. There is no generic sign to fall
    /// back on: a hazard without a sign of its own gets one drawn.
    /// </summary>
    public static string SignOf(WeatherKind kind) => kind switch
    {
        WeatherKind.Tornado => "tornado",
        WeatherKind.Thunderstorm => "storm",
        WeatherKind.Hurricane => "high_wind",
        WeatherKind.DustStorm => "dust_storm",
        WeatherKind.Blizzard or WeatherKind.Snow => "extreme_cold",
        WeatherKind.FireStorm => "fire",
        WeatherKind.Monsoon or WeatherKind.Rain => "flood",
        _ => null, // Clear, Clouds, Fog
    };

    // ---- Storm cells ------------------------------------------------------------------

    /// <summary>
    /// Cells come and go with how stormy the weather is. They form upwind and drift over, unless the
    /// storm is already full-grown with none in the sky (a jump in the weather): then the first
    /// stands overhead.
    /// </summary>
    void StepCells(float dt, Vector3 observer)
    {
        CellRules r = Data.Cells;
        Conditions c = Current;
        float storm = MathF.Max(c.Rain, MathF.Min(1f, c.LightningPerMinute / 4f));
        if (c.TornadoesPerMinute > 0.05f) storm = 1f;
        int want = storm < r.From ? 0 : 1 + (int)MathF.Round(storm * (r.MaxCells - 1));
        Vector3 drift = new Vector3(c.Wind.X, 0f, c.Wind.Y) * r.DriftShare;
        for (int i = _cells.Count - 1; i >= 0; i--)
        {
            StormCell cell = _cells[i];
            if (cell.Funnel is { Done: true }) cell.Funnel = null;
            if (cell.Funnel != null)
            {
                cell.Position = Flat(cell.Funnel.Position);
                cell.Strength = MathF.Min(1f, cell.Strength + dt / r.GrowSeconds);
                continue;
            }
            cell.Position += drift * dt;
            float target = i < want ? 1f : 0f;
            cell.Strength += Math.Clamp(target - cell.Strength, -dt / r.GrowSeconds, dt / r.GrowSeconds);
            bool spent = target == 0f && cell.Strength <= 0f;
            if (spent || Flat(cell.Position - observer).Length() > r.Range) _cells.RemoveAt(i);
        }
        _cellTimer -= dt;
        if (_cells.Count >= want) return;
        if (_cells.Count == 0 && storm >= 0.7f)
        {
            AddCell(observer + RandomOffset(r.RadiusMin * 0.3f), 1f);
            return;
        }
        if (_cellTimer > 0f) return;
        _cellTimer = r.SpawnEvery;
        float speed = drift.Length();
        Vector3 down = speed > 0.01f ? drift / speed : new Vector3(1f, 0f, 0f);
        var across = new Vector3(-down.Z, 0f, down.X);
        AddCell(observer - down * _hazardRng.Range(0.45f, 0.9f) * r.Range + across * _hazardRng.Range(-0.4f, 0.4f) * r.Range, 0f);
    }

    /// <summary>A storm cell at a place, this strong (a jump, tests, screenshots: the weather makes its own).</summary>
    public StormCell AddCell(Vector3 at, float strength)
    {
        var cell = new StormCell { Id = _nextId++, Position = Flat(at), Strength = strength, Radius = _hazardRng.Range(Data.Cells.RadiusMin, Data.Cells.RadiusMax) };
        _cells.Add(cell);
        return cell;
    }

    /// <summary>Empties the sky of cells at once (tests, screenshots).</summary>
    public void ClearCells() => _cells.Clear();

    /// <summary>How much storm hangs over a point, 0 to 1.</summary>
    public float CellCover(Vector3 p)
    {
        float cover = 0f;
        foreach (StormCell c in _cells) cover = MathF.Max(cover, c.Cover(p));
        return cover;
    }

    /// <summary>The rain falling at a point: the weather's, as much of it as its cells' share lets through there.</summary>
    public float RainAt(Vector3 p) => Current.Rain * (1f - Current.Cellular * (1f - CellCover(p)));

    StormCell NearestCell(Vector3 p)
    {
        StormCell best = null;
        float nearest = float.MaxValue;
        foreach (StormCell c in _cells)
        {
            float d = Flat(c.Position - p).Length() - c.Radius;
            if (d < nearest) { nearest = d; best = c; }
        }
        return best;
    }

    /// <summary>Where the next bolt aims: near the observer if a cell hangs over them, else out of the nearest cell.</summary>
    Vector3 StrikeSpot(Vector3 observer)
    {
        StormCell cell = NearestCell(observer);
        if (cell == null || cell.Cover(observer) > 0.2f) return observer + RandomOffset(Data.Lightning.StrikeOffset);
        return cell.Position + RandomOffset(cell.Radius * 0.6f);
    }

    // ---- Hazards: lightning and falling debris ---------------------------------------

    void StepStrikes(float dt, Vector3 observer)
    {
        Conditions c = Current;
        if (Accumulate(ref _lightningCharge, ref _lightningNext, c.LightningPerMinute, dt)
            && Count(HazardKind.Lightning) < Data.Lightning.MaxConcurrent)
            StartStrike(HazardKind.Lightning, StrikeSpot(observer));

        if (Accumulate(ref _debrisCharge, ref _debrisNext, c.DebrisPerMinute, dt)
            && Count(HazardKind.Debris) < Data.Debris.MaxConcurrent)
            StartStrike(HazardKind.Debris, observer + RandomOffset(Data.Debris.StrikeOffset));

        for (int i = _hazards.Count - 1; i >= 0; i--)
        {
            Hazard h = _hazards[i];
            h.Age += dt;
            if (h.Kind == HazardKind.Lightning) Aim(h);
            if (h.Age < h.BuildUp) continue;
            Aim(h); // the strike goes to whatever is highest at the moment it lands
            StrikeRules rules = h.Kind == HazardKind.Lightning ? Data.Lightning : Data.Debris;
            var kind = h.Kind == HazardKind.Lightning ? WeatherEventKind.LightningStrike : WeatherEventKind.DebrisImpact;
            _events.Add(new WeatherEvent(kind, h.Id, h.Target, h.TargetPoint, rules.Damage, rules.DamageRadius));
            _hazards.RemoveAt(i);
        }
    }

    /// <summary>Random events at a varying rate: charge builds with the rate and fires at an exponential threshold.</summary>
    bool Accumulate(ref float charge, ref float next, float perMinute, float dt)
    {
        if (perMinute <= 0.001f) return false;
        charge += perMinute / 60f * dt;
        if (charge < next) return false;
        charge = 0f;
        next = _hazardRng.Exponential();
        return true;
    }

    int Count(HazardKind kind)
    {
        int n = 0;
        foreach (Hazard h in _hazards) if (h.Kind == kind) n++;
        return n;
    }

    Vector3 RandomOffset(float radius)
    {
        float angle = _hazardRng.Range(0f, MathF.Tau);
        float distance = MathF.Sqrt(_hazardRng.NextFloat()) * radius;
        return new Vector3(MathF.Cos(angle) * distance, 0f, MathF.Sin(angle) * distance);
    }

    /// <summary>Starts a strike aimed at <paramref name="center"/>. Lightning will hit the highest point within its radius.</summary>
    public Hazard StartStrike(HazardKind kind, Vector3 center)
    {
        StrikeRules rules = kind == HazardKind.Lightning ? Data.Lightning : Data.Debris;
        var hazard = new Hazard
        {
            Id = _nextId++, Kind = kind, Center = Flat(center), Radius = kind == HazardKind.Lightning ? rules.Radius : 0f,
            BuildUp = rules.WarningSeconds,
        };
        Aim(hazard);
        _hazards.Add(hazard);
        return hazard;
    }

    void Aim(Hazard h)
    {
        var (point, target) = HighestPoint(h.Center, h.Radius);
        h.Target = point;
        h.TargetPoint = target;
    }

    /// <summary>The highest strike point within <paramref name="radius"/> (horizontally) of a spot, or the ground there.</summary>
    public (Vector3 point, StrikePoint target) HighestPoint(Vector3 center, float radius)
    {
        var best = new Vector3(center.X, GroundHeight(center.X, center.Z), center.Z);
        StrikePoint bestPoint = null;
        foreach (StrikePoint p in StrikePoints)
        {
            float dx = p.Position.X - center.X, dz = p.Position.Z - center.Z;
            if (dx * dx + dz * dz > radius * radius || p.Position.Y <= best.Y) continue;
            best = p.Position;
            bestPoint = p;
        }
        return (best, bestPoint);
    }

    // ---- Tornadoes --------------------------------------------------------------------

    void StepTornadoes(float dt, Vector3 observer)
    {
        TornadoRules rules = Data.Tornado;
        int active = 0;
        foreach (Tornado t in _tornadoes) if (!t.Dissipating) active++;
        if (Accumulate(ref _tornadoCharge, ref _tornadoNext, Current.TornadoesPerMinute, dt) && active < rules.MaxActive)
        {
            float angle = _hazardRng.Range(0f, MathF.Tau);
            var away = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            // Out of the nearest cell, if one is near: a funnel comes down from a cloud.
            StormCell cell = NearestCell(observer);
            var at = cell != null && Flat(cell.Position - observer).Length() < Data.Cells.Range * 0.6f
                ? cell.Position + away * MathF.Min(rules.SpawnDistance, cell.Radius * 0.5f)
                : observer + away * rules.SpawnDistance;
            // Natural tornadoes do not chase the player: a random heading, no homing, a slow wander.
            Tornado born = SpawnTornado(at, observer);
            born.Homing = false;
            born.SetHeading(_hazardRng.Range(0f, MathF.Tau));
            born.WanderRate = 0.12f;
        }

        bool stormGone = Current.TornadoesPerMinute < 0.05f;
        for (int i = _tornadoes.Count - 1; i >= 0; i--)
        {
            Tornado t = _tornadoes[i];
            bool wasDown = t.Touchdown;
            if (stormGone && !t.Homing) t.Dissipate();
            t.Step(dt, observer, GroundHeight);
            if (t.Touchdown && !wasDown)
                _events.Add(new WeatherEvent(WeatherEventKind.TornadoTouchdown, t.Id, t.Position, null, 0f, 0f));
            if (!t.Done) continue;
            _events.Add(new WeatherEvent(WeatherEventKind.TornadoGone, t.Id, t.Position, null, 0f, 0f));
            _tornadoes.RemoveAt(i);
        }
    }

    /// <summary>Starts a tornado forming at <paramref name="at"/>, heading for <paramref name="homeOn"/>.</summary>
    public Tornado SpawnTornado(Vector3 at, Vector3 homeOn)
    {
        float heading = MathF.Atan2(homeOn.Z - at.Z, homeOn.X - at.X);
        at = new Vector3(at.X, GroundHeight(at.X, at.Z), at.Z);
        var tornado = new Tornado(_nextId++, Data.Tornado, at, heading, homing: true, _hazardRng.Range(0f, MathF.Tau));
        _tornadoes.Add(tornado);
        _events.Add(new WeatherEvent(WeatherEventKind.TornadoForming, tornado.Id, at, null, 0f, 0f));
        // Every funnel hangs from a cell: the nearest that has none yet, or a new one over it.
        StormCell parent = null;
        foreach (StormCell c in _cells)
            if (c.Funnel == null && Flat(c.Position - at).Length() < c.Radius && (parent == null || c.Strength > parent.Strength)) parent = c;
        parent ??= AddCell(at, 1f);
        parent.Funnel = tornado;
        return tornado;
    }

    /// <summary>Sets what's lying on the ground (screenshots).</summary>
    public void DebugSetGround(GroundCover cover) => Ground = cover;

    /// <summary>Removes every tornado at once (tests, screenshots).</summary>
    public void ClearTornadoes() => _tornadoes.Clear();

    /// <summary>Calls off every strike that is coming (tests, screenshots).</summary>
    public void ClearHazards() => _hazards.Clear();

    // ---- Wind -------------------------------------------------------------------------

    /// <summary>
    /// The wind at a place, m/s: the mean wind plus gusts. Gust fronts travel downwind at the
    /// wind's own speed, so a gust visibly sweeps across the grass before it hits you.
    /// </summary>
    public Vector3 WindAt(Vector3 position)
    {
        Vector2 mean = Current.Wind;
        float speed = mean.Length();
        if (speed < 0.01f) return Vector3.Zero;
        Vector2 dir = mean / speed;
        float along = position.X * dir.X + position.Z * dir.Y;
        float phase = ((float)Time * speed - along) / 80f;
        float gust = Current.Gustiness * (0.55f * Noise.Value1D(phase, _gustSeed) + 0.25f * Noise.Value1D(phase * 2.7f + 31f, _gustSeed));
        float veer = Current.Gustiness * 0.2f * Noise.Value1D(phase * 0.5f + 77f, _gustSeed);
        Vector2 wind = dir.Rotated(veer) * speed * MathF.Max(0.1f, 1f + gust);
        return new Vector3(wind.X, 0f, wind.Y);
    }

    // ---- Helpers ----------------------------------------------------------------------

    static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    /// <summary>Compass direction from one point to another; -Z is north.</summary>
    public static string Bearing(Vector3 from, Vector3 to)
    {
        float angle = MathF.Atan2(to.X - from.X, -(to.Z - from.Z)); // 0 = north, clockwise
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        int i = (int)MathF.Round(angle / (MathF.Tau / 8f));
        return names[((i % 8) + 8) % 8];
    }

    public static string Clock(double seconds)
    {
        int s = (int)Math.Ceiling(Math.Max(0, seconds));
        return $"{s / 60}:{s % 60:D2}";
    }
}
