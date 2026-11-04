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

/// <summary>A visor warning. <see cref="Kind"/> and <see cref="ArrivesAt"/> are set for weather warnings.</summary>
public readonly record struct WarningEntry(WarningLevel Level, string Text, double Eta, WeatherKind? Kind = null, double ArrivesAt = 0);

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

    /// <summary>What happened during the last <see cref="Step"/>.</summary>
    public IReadOnlyList<WeatherEvent> Events => _events;

    readonly List<Segment> _segments = new();
    readonly List<Hazard> _hazards = new();
    readonly List<Tornado> _tornadoes = new();
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
                    $"Lightning charge building on {what}, {distance:F0} m {Bearing(observer, h.Target)}", h.Remaining));
            }
            else if (h.Kind == HazardKind.Debris && distance < 60f)
            {
                into.Add(new WarningEntry(WarningLevel.Danger,
                    $"Burning debris incoming, {distance:F0} m {Bearing(observer, h.Target)}", h.Remaining));
            }
        }

        foreach (Tornado t in _tornadoes)
        {
            float distance = Flat(t.Position - observer).Length();
            if (!t.Touchdown)
                into.Add(new WarningEntry(WarningLevel.Danger,
                    $"Funnel forming {distance:F0} m {Bearing(observer, t.Position)}, touchdown in {t.Rules.FormSeconds - t.Age:F0} s",
                    t.Rules.FormSeconds - t.Age));
            else if (!t.Dissipating || distance < 200f)
                into.Add(new WarningEntry(WarningLevel.Danger,
                    $"Tornado {distance:F0} m {Bearing(observer, t.Position)}{(t.Homing ? ", heading your way" : "")}", 0));
        }

        foreach (Segment s in _segments)
        {
            if (Time < s.WarningAt || Time >= s.End) continue;
            WeatherState state = Data.State(s.Kind);
            double eta = s.Start - Time;
            string text = eta > 0 ? $"{state.Name} in {Clock(eta)}. {state.Warning}" : state.Warning;
            into.Add(new WarningEntry(WarningLevel.Warning, text, Math.Max(0, eta), s.Kind, s.Start));
        }
    }

    // ---- Hazards: lightning and falling debris ---------------------------------------

    void StepStrikes(float dt, Vector3 observer)
    {
        Conditions c = Current;
        if (Accumulate(ref _lightningCharge, ref _lightningNext, c.LightningPerMinute, dt)
            && Count(HazardKind.Lightning) < Data.Lightning.MaxConcurrent)
            StartStrike(HazardKind.Lightning, observer + RandomOffset(Data.Lightning.StrikeOffset));

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
            var at = observer + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * rules.SpawnDistance;
            SpawnTornado(at, observer);
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
        return tornado;
    }

    /// <summary>Sets what's lying on the ground (screenshots).</summary>
    public void DebugSetGround(GroundCover cover) => Ground = cover;

    /// <summary>Removes every tornado at once (tests, screenshots).</summary>
    public void ClearTornadoes() => _tornadoes.Clear();

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
