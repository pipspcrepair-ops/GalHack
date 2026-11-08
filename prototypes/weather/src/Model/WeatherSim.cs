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
///
/// The timeline is the world's air: what is coming, and when. What it does at a place is the
/// region's (<see cref="LocalAt"/>): the front over it, and its land and climate, which decide what
/// can form there (<see cref="Land"/>, <see cref="RegionRules"/>). Each world leans its type's weights
/// by its own dials (<see cref="WorldWeather"/>).
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

    /// <summary>The front the weather is arriving on (or has arrived on), or null: a storm's line, or a dust wall.</summary>
    public Front Front => _front;

    /// <summary>The stretch of the timeline the front carries (its weather behind the edge, the one before it ahead), or null.</summary>
    public Segment FrontWeather => _frontSegment;

    /// <summary>No fronts: storms come as loose cells (single-cell views, tests).</summary>
    public bool FrontsOff { get; set; }

    /// <summary>The world's size, metres, for what is measured across it (a front's line): zero for no wrap.</summary>
    public Vector2 WorldSize { get; set; }

    /// <summary>The ground can be lifted as dust here (dry loose ground): a dust storm comes as a wall. Null: everywhere.</summary>
    public Func<bool> LooseGround { get; set; }

    /// <summary>Open water at a point (a sea or a lake, not a stream): waterspouts form only over it. Null: none anywhere.</summary>
    public Func<float, float, bool> OpenWater { get; set; }

    /// <summary>
    /// The land and climate at a point, from the world's map. Null: no map, the same open plain
    /// everywhere, and nothing is held back by region (the weather prototype's own field).
    /// </summary>
    public Func<float, float, Ground> Land { get; set; }

    /// <summary>The world's mean warmth on the same 0..1 scale as <see cref="Ground.Warmth"/>: its type's base temperature is the weather there.</summary>
    public float MeanWarmth { get; set; } = 0.5f;

    /// <summary>This world's own dials on its type's weights.</summary>
    public WorldWeather World { get; }

    /// <summary>The weather where the observer was at the last <see cref="Step"/>: their region's, under their part of the front.</summary>
    public Conditions Here { get; private set; }

    /// <summary>What happened during the last <see cref="Step"/>.</summary>
    public IReadOnlyList<WeatherEvent> Events => _events;

    readonly List<Segment> _segments = new();
    readonly List<Hazard> _hazards = new();
    readonly List<Tornado> _tornadoes = new();
    readonly List<StormCell> _cells = new();
    float _cellTimer;
    Front _front;
    readonly List<WeatherEvent> _events = new();
    readonly Rng _timelineRng, _hazardRng;
    readonly uint _gustSeed;
    float _lightningCharge, _lightningNext, _debrisCharge, _debrisNext, _tornadoCharge, _tornadoNext, _spoutCharge, _spoutNext = 1f, _devilCharge, _devilNext = 1f;
    float _groundCheck;
    Vector3 _observer;
    Segment _frontSegment;
    int _nextId = 1;

    public WeatherSim(WeatherData data, string archetypeId, ulong seed, WeatherKind? start = null, WorldWeather world = null)
    {
        Data = data;
        Archetype = data.Archetype(archetypeId);
        Seed = seed;
        World = world ?? WorldWeather.Neutral;
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
        Here = Current;
    }

    /// <summary>The timeline is always known this far ahead, so every forecast can go up on time.</summary>
    double Horizon => Data.MaxLead + 60.0;

    // ---- Ticking ----------------------------------------------------------------------

    /// <summary>Advances the weather. <paramref name="observer"/> is where the player is: hazards happen around them.</summary>
    public void Step(float dt, Vector3 observer)
    {
        _events.Clear();
        Time += dt;
        _observer = observer;
        EnsureTimeline(Time + Horizon);
        Current = ConditionsAt(Time);
        UpdateGround(dt);
        StepFront(dt, observer);
        StepCells(dt, observer);
        StepStrikes(dt, observer);
        StepTornadoes(dt, observer);
        Here = LocalAt(observer);
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
            if (kind != previous) total += Weight(kind, weight);
        float pick = _timelineRng.NextFloat() * total;
        WeatherKind chosen = previous ?? WeatherKind.Clear;
        foreach (var (kind, weight) in Archetype.Weights)
        {
            if (kind == previous) continue;
            chosen = kind;
            pick -= Weight(kind, weight);
            if (pick < 0f) break;
        }
        return chosen;
    }

    /// <summary>How often a weather visits this world: its type's weight, leaned by the world's own dials.</summary>
    public float Weight(WeatherKind kind, float weight) => World.Weigh(Data.Worlds, kind, weight);

    /// <summary>A weather's share of this world's timeline, 0..1, by weight (tests, surveys).</summary>
    public float Share(WeatherKind kind)
    {
        float total = 0f, mine = 0f;
        foreach (var (k, w) in Archetype.Weights)
        {
            float x = Weight(k, w);
            total += x;
            if (k == kind) mine = x;
        }
        return total > 0f ? mine / total : 0f;
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
        if (_frontSegment != null && !_segments.Contains(_frontSegment)) _frontSegment = null; // its weather is gone from the timeline
        EnsureTimeline(Time + Horizon);
    }

    // ---- Regions ----------------------------------------------------------------------

    /// <summary>The land at a point (an open plain if the world has no map).</summary>
    public Ground GroundAt(Vector3 p) => Land?.Invoke(p.X, p.Z) ?? Model.Ground.Plain;

    /// <summary>The mean height of the land round a point, for what pools in low ground.</summary>
    float AroundHeight(Vector3 p)
    {
        float sum = 0f;
        for (int k = 0; k < 4; k++)
        {
            float a = k * MathF.Tau / 4f + 0.4f;
            sum += Land(p.X + MathF.Cos(a) * 400f, p.Z + MathF.Sin(a) * 400f).Height;
        }
        return sum / 4f;
    }

    /// <summary>What a weather is in a region: itself where its land allows it, else what it falls back to there.</summary>
    public WeatherKind LocalKind(WeatherKind kind, Vector3 p)
    {
        if (Land == null || !Data.Regions.Fallback.TryGetValue(kind, out WeatherKind fallback)) return kind;
        Ground g = GroundAt(p);
        if (Regions.Suits(Data.Regions, kind, g, kind == WeatherKind.Fog ? AroundHeight(p) : g.Height) >= 0.5f) return kind;
        // A tornado storm is the area's danger, not one spot's: funnels that form on open land in reach
        // wander, so it is a tornado storm wherever such land lies within a few kilometres.
        if (kind == WeatherKind.Tornado)
            for (int ring = 1; ring <= 2; ring++)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * MathF.Tau / 8f + ring * 0.4f;
                    Vector3 q = p + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * (1500f * ring);
                    if (Regions.TornadoGround(Data.Regions, Land, q) > 0.25f) return kind;
                }
        return fallback;
    }

    /// <summary>
    /// The weather at a point. Under a front's storm it is the storm's, ahead of its edge and past its
    /// deck still the weather before it, so a front is weather moving across the map from one region
    /// into the next. Then the region has its say: a weather its land doesn't allow falls back to
    /// another (a dust storm over wet woods is a windy overcast), the air is colder up high, rain
    /// falls as snow where it is cold enough and snow as rain where it isn't, dust lifts only off dry
    /// loose ground, fog pools in damp low ground, and the wind blows harder on the heights and softer
    /// in the woods.
    /// </summary>
    public Conditions LocalAt(Vector3 p)
    {
        Conditions c = Current;
        Segment s = SegmentAt(Time);
        WeatherKind from = s.FromKind, to = s.Kind;
        float blend = Blend(s, Time);
        if (_front != null && _frontSegment != null && (_frontSegment == s || _frontSegment.Start > Time))
        {
            // The front's own weather behind its edge, the one before it everywhere else.
            float under = _front.Strength * _front.Under(p);
            c = Conditions.Lerp(_frontSegment.Start > Time ? Current : _frontSegment.From, _frontSegment.To, under);
            (from, to, blend) = (_frontSegment.FromKind, _frontSegment.Kind, under);
        }
        if (Land == null) return c;

        RegionRules r = Data.Regions;
        Ground g = Land(p.X, p.Z);
        float around = AroundHeight(p);
        if (from != to) c = Conditions.Lerp(Localize(c, from, g, around), Localize(c, to, g, around), blend);
        else c = Localize(c, to, g, around);

        // Warmer and colder ground than the world's middle, and the cold of height.
        float temperature = c.Temperature + r.WarmthDegrees * (g.Warmth - MeanWarmth) * 2f - r.LapseRate * MathF.Max(0f, g.Height) / 1000f;
        c[Param.Temperature] = temperature;
        float snowy = 1f - Front.Smooth(r.Blizzard.SnowBelow, r.Blizzard.SnowBelow + 2f, temperature);
        float fall = c.Rain + c.Snow;
        c[Param.Snow] = fall * snowy;
        c[Param.Rain] = fall * (1f - snowy);
        c[Param.Dust] *= Regions.DustGround(r, g);
        c[Param.GroundFog] *= 0.25f + 0.75f * Regions.FogGround(r, g, around);
        c[Param.Tornadoes] *= Regions.TornadoPoint(r, g);
        float wind = (1f + r.Wind.HighBoost * Regions.High(r, g)) * (1f - r.Wind.ForestCut * g.Forest);
        c[Param.WindX] *= wind;
        c[Param.WindZ] *= wind;
        return c;
    }

    /// <summary>A weather's conditions where its land allows it only so far: the rest is its fallback's, the wind kept.</summary>
    Conditions Localize(in Conditions c, WeatherKind kind, in Ground g, float around)
    {
        RegionRules r = Data.Regions;
        if (!r.Fallback.TryGetValue(kind, out WeatherKind fallback)) return c;
        float suits = Regions.Suits(r, kind, g, around);
        if (suits >= 1f) return c;
        float heading = MathF.Atan2(c[Param.WindZ], c[Param.WindX]);
        Conditions instead = Conditions.FromState(Data.State(fallback), Archetype.BaseTemperature, heading);
        instead[Param.WindX] = c[Param.WindX];
        instead[Param.WindZ] = c[Param.WindZ];
        instead[Param.Gustiness] = c[Param.Gustiness];
        return Conditions.Lerp(instead, c, suits);
    }

    // ---- Forecast and warnings --------------------------------------------------------

    /// <summary>Upcoming weather whose forecast has gone up, soonest first.</summary>
    public void GetForecast(List<ForecastEntry> into) => GetForecast(into, _observer);

    /// <summary>Upcoming weather whose forecast has gone up, soonest first, as it will be in the observer's region.</summary>
    public void GetForecast(List<ForecastEntry> into, Vector3 observer)
    {
        into.Clear();
        foreach (Segment s in _segments)
        {
            if (s.Start <= Time || Time < s.ForecastAt) continue;
            WeatherKind kind = LocalKind(s.Kind, observer);
            WeatherState state = Data.State(kind);
            into.Add(new ForecastEntry(kind, state.Name, s.Start - Time, state.Severe));
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
            if (t.Kind != TornadoKind.Tornado)
            {
                // A waterspout coming in is worth a word; a dust devil is not.
                if (t.Kind == TornadoKind.Waterspout && t.Touchdown && distance < 400f)
                    into.Add(new WarningEntry(WarningLevel.Advisory, $"Waterspout {distance:F0} m {Bearing(observer, t.Position)}", 0));
                continue;
            }
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
            // As it will be here: a tornado storm over the mountains is a lightning storm, a blizzard on warm low ground no blizzard.
            WeatherKind kind = LocalKind(s.Kind, observer);
            // Only a real hazard warns, and by its own sign: clear skies, clouds and fog never do.
            if (SignOf(kind) == null) continue;
            WeatherState state = Data.State(kind);
            double eta = s.Start - Time;
            string text = eta > 0 ? $"{state.Name} in {Clock(eta)}. {state.Warning}" : state.Warning;
            // Weather comes in on the wind: from upwind of the observer.
            var upwind = new Vector3(-MathF.Cos(s.WindHeading), 0f, -MathF.Sin(s.WindHeading));
            into.Add(new WarningEntry(WarningLevel.Warning, text, Math.Max(0, eta), kind, s.Start,
                Sign: SignOf(kind), Left: Math.Max(0, s.End - Math.Max(Time, s.Start)), From: observer + upwind * 2000f));
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
        if (_front is { Kind: FrontKind.Storm } f && f.Strength > 0.02f)
        {
            StepFrontCells(dt, observer, f);
            return;
        }
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

    /// <summary>A storm cell at a place, this strong (a jump, tests, screenshots: the weather makes its own). <paramref name="first"/>: drawn before any other (a funnel's).</summary>
    public StormCell AddCell(Vector3 at, float strength, bool first = false)
    {
        var cell = new StormCell { Id = _nextId++, Position = Flat(at), Strength = strength, Radius = _hazardRng.Range(Data.Cells.RadiusMin, Data.Cells.RadiusMax) };
        if (first) _cells.Insert(0, cell);
        else _cells.Add(cell);
        return cell;
    }

    /// <summary>Empties the sky of cells at once (tests, screenshots).</summary>
    public void ClearCells() => _cells.Clear();

    /// <summary>How much storm hangs over a point, 0 to 1: its cells', and the deck trailing behind a front's edge.</summary>
    public float CellCover(Vector3 p)
    {
        float cover = _front is { Kind: FrontKind.Storm } f ? f.Cover(p) : 0f;
        foreach (StormCell c in _cells) cover = MathF.Max(cover, c.Cover(p));
        return cover;
    }

    // ---- Fronts -----------------------------------------------------------------------

    /// <summary>How long a front takes to grow as it comes into sight, and to fade once its weather has gone, seconds.</summary>
    const float FrontGrowSeconds = 40f, FrontFadeSeconds = 90f;

    /// <summary>
    /// Storms arrive on a front. It forms once its weather is near enough to be in sight, upwind by
    /// as far as it will travel before that weather is half in, so its shelf is seen for minutes
    /// before it arrives; it advances on the storm's wind and fades once its weather has gone. A
    /// jump straight into the weather puts the observer under it, its edge already past.
    /// </summary>
    void StepFront(float dt, Vector3 observer)
    {
        CellRules r = Data.Cells;
        EnsureTimeline(Time + r.FrontSight / 7f);
        Segment coming = FrontSegment(r.FrontSight / 7f, out FrontKind kind);
        if (_front != null)
        {
            _front.Centre += _front.Velocity * dt;
            if (coming != null && kind == _front.Kind && (_frontSegment == null || _frontSegment.End <= Time)) _frontSegment = coming;
            bool wanted = coming != null && kind == _front.Kind;
            float target = wanted ? 1f : 0f;
            _front.Strength += Math.Clamp(target - _front.Strength, -dt / FrontFadeSeconds, dt / FrontGrowSeconds);
            if (!wanted && _front.Strength <= 0f) (_front, _frontSegment) = (null, null);
            return;
        }
        if (coming == null || FrontsOff) return;
        Conditions to = coming.To;
        float speed = FrontSpeed(kind, to.WindSpeed);
        float distance = (float)(coming.Start + coming.Transition * 0.5 - Time) * speed;
        bool jumped = coming.Forced && Blend(coming, Time) >= 0.5f;
        if (jumped) distance = -1500f;
        else if (distance > r.FrontSight) return; // not in sight yet
        var line = Front.Lattice(to.Wind, WorldSize, MinFrontSpacing);
        // On a small world it comes into sight no farther off than its copies allow.
        if (!jumped && distance > line.spacing * 0.5f - 1500f) return;
        _front = MakeFront(kind, observer, distance, to.Wind, speed, jumped ? 1f : 0f);
        _frontSegment = coming;
    }

    /// <summary>Copies of a front stand at least this far apart across the world, metres: room for its storm and the open air ahead of it.</summary>
    const float MinFrontSpacing = 5500f;

    /// <summary>A front advances a little slower than its storm's wind (a dust wall slower still), m/s.</summary>
    static float FrontSpeed(FrontKind kind, float wind) => MathF.Max(7f, wind * (kind == FrontKind.Dust ? 0.8f : 0.9f));

    /// <summary>What kind of front a weather comes on, if any: thunderstorms and tornado storms on a storm's line, dust storms over dry loose ground as a wall.</summary>
    public bool FrontOf(in Conditions to, out FrontKind kind)
    {
        kind = FrontKind.Storm;
        if (to.Cellular > 0.3f && (to.LightningPerMinute >= 1f || to.TornadoesPerMinute > 0.05f)) return true;
        kind = FrontKind.Dust;
        bool loose = LooseGround?.Invoke() ?? (Land == null || Regions.DustGround(Data.Regions, GroundAt(_observer)) > 0.3f);
        return to.Dust >= 0.5f && loose && Ground.Wetness < 0.4f && Ground.Snow < 0.3f;
    }

    /// <summary>The weather now in, or the next one due within <paramref name="ahead"/> seconds, that comes on a front; null for none.</summary>
    Segment FrontSegment(float ahead, out FrontKind kind)
    {
        kind = FrontKind.Storm;
        int lo = 0, hi = _segments.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_segments[mid].Start <= Time) lo = mid;
            else hi = mid - 1;
        }
        for (int i = lo; i < _segments.Count && _segments[i].Start <= Time + ahead; i++)
            if (_segments[i].End > Time && FrontOf(_segments[i].To, out kind)) return _segments[i];
        return null;
    }

    Front MakeFront(FrontKind kind, Vector3 observer, float distance, Vector2 wind, float speed, float strength, bool? roll = null)
    {
        var (heading, period, spacing, shear) = Front.Lattice(wind, WorldSize, MinFrontSpacing);
        float bow = MathF.Min(_hazardRng.Range(500f, 1400f), spacing * 0.12f);
        float reach = spacing * 0.5f - bow;
        float seed = _hazardRng.NextFloat(), along = _hazardRng.Range(0f, period);
        var f = new Front
        {
            Id = _nextId++, Kind = kind, Heading = heading, Speed = speed, Period = period, Spacing = spacing, Shear = shear, Bow = bow,
            Bows = Math.Max(1, (int)MathF.Round(period / 22000f)), Bays = Math.Max(2, (int)MathF.Round(period / 5000f)),
            Depth = MathF.Min(kind == FrontKind.Dust ? _hazardRng.Range(5000f, 10000f) : _hazardRng.Range(7000f, 14000f), reach * 0.85f),
            Severity = _hazardRng.Range(0.5f, 1f), Roll = roll ?? (kind == FrontKind.Storm && _hazardRng.NextFloat() < 0.3f),
            Seed = seed, Strength = strength,
        };
        // Its edge passes the observer's stretch of it `distance` upwind of them.
        distance = Math.Clamp(distance, -reach + 300f, reach - 300f);
        var perp = new Vector2(-heading.Y, heading.X);
        Vector2 c = new Vector2(observer.X, observer.Z) - heading * (distance - f.Lag(along)) - perp * along;
        f.Centre = new Vector3(c.X, 0f, c.Y);
        return f;
    }

    /// <summary>
    /// A front this far upwind of a point, fully grown, its cells already standing (tests, screenshots).
    /// <paramref name="wind"/> sets the way it advances (the weather's wind if not given); <paramref name="roll"/> gives it a roll cloud.
    /// </summary>
    public Front StartFront(FrontKind kind, Vector3 observer, float distance, Vector2? wind = null, bool? roll = null)
    {
        _front = MakeFront(kind, observer, distance, wind ?? Current.Wind, FrontSpeed(kind, Current.WindSpeed), 1f, roll);
        // It carries the timeline's own front weather, if one is in or due: the weather before it ahead of its edge, its own behind.
        Segment due = FrontSegment(Data.Cells.FrontSight / 7f, out FrontKind dueKind);
        _frontSegment = due != null && dueKind == kind ? due : null;
        _cells.RemoveAll(c => c.Funnel == null);
        if (kind == FrontKind.Storm) StepFrontCells(0f, observer, _front);
        return _front;
    }

    /// <summary>Takes the front away at once, and its cells (tests, screenshots).</summary>
    public void ClearFront()
    {
        _front = null;
        _frontSegment = null;
        _cells.RemoveAll(c => c.OnFront && c.Funnel == null);
    }

    /// <summary>
    /// A front's cells: lined up along the edge nearest the observer just behind it, one to each
    /// stretch of the line, riding the front as it advances. Loose cells, and a copy's cells once
    /// the observer is nearer the next copy, die away.
    /// </summary>
    void StepFrontCells(float dt, Vector3 observer, Front f)
    {
        CellRules r = Data.Cells;
        int slots = Math.Max(1, (int)MathF.Round(f.Period / r.FrontSpacing));
        float spacing = f.Period / slots, here = f.Along(observer);
        int copy = f.Copy(observer);
        float Apart(float u) => MathF.Abs(u - here - f.Period * MathF.Round((u - here) / f.Period));
        Vector3 rides = f.Velocity, drift = new Vector3(Current.Wind.X, 0f, Current.Wind.Y) * r.DriftShare;
        int onFront = 0;
        for (int i = _cells.Count - 1; i >= 0; i--)
        {
            StormCell cell = _cells[i];
            if (cell.Funnel is { Done: true })
            {
                cell.Funnel = null;
                cell.OnFront = false; // its funnel took it off the line: it dies away, and the line grows another
            }
            if (cell.Funnel != null)
            {
                cell.Position = Flat(cell.Funnel.Position);
                cell.Strength = MathF.Min(1f, cell.Strength + dt / r.GrowSeconds);
                continue;
            }
            cell.Position += (cell.OnFront ? rides : drift) * dt;
            bool kept = cell.OnFront && f.Copy(cell.Position) == copy && Apart(f.Along(cell.Position)) <= spacing * 2.6f;
            float target = kept ? f.Strength : 0f;
            cell.Strength += Math.Clamp(target - cell.Strength, -dt / r.GrowSeconds, dt / r.GrowSeconds);
            if (target == 0f && cell.Strength <= 0f) _cells.RemoveAt(i);
            else if (kept) onFront++;
        }
        int want = (int)MathF.Round(f.Strength * r.MaxCells) - (_cells.Count - onFront);
        bool grown = f.Strength >= 0.99f;
        _cellTimer -= dt;
        while (onFront < want && (grown || _cellTimer <= 0f))
        {
            _cellTimer = r.SpawnEvery * 0.5f;
            // The free stretch of the line nearest the observer's.
            float best = float.MaxValue, slot = 0f;
            int n0 = (int)MathF.Floor(here / spacing);
            for (int k = n0 - 2; k <= n0 + 2; k++)
            {
                int key = ((k % slots) + slots) % slots;
                float u = (k + 0.5f) * spacing + (Fract(MathF.Sin(key * 12.9898f + f.Id * 78.233f) * 43758.547f) - 0.5f) * spacing * 0.35f;
                float d = Apart(u);
                if (d > spacing * 2.2f || d >= best) continue;
                bool taken = false;
                foreach (StormCell c in _cells)
                {
                    float gap = f.Along(c.Position) - u;
                    if (c.OnFront && f.Copy(c.Position) == copy && MathF.Abs(gap - f.Period * MathF.Round(gap / f.Period)) < spacing * 0.5f) { taken = true; break; }
                }
                if (!taken) (best, slot) = (d, u);
            }
            if (best == float.MaxValue) break;
            float radius = _hazardRng.Range(r.RadiusMin, r.RadiusMax);
            Vector3 at = f.EdgeAt(slot, observer) - new Vector3(f.Heading.X, 0f, f.Heading.Y) * radius * _hazardRng.Range(0.55f, 0.85f);
            _cells.Add(new StormCell { Id = _nextId++, Position = Flat(at), Strength = grown ? 1f : 0f, Radius = radius, OnFront = true });
            onFront++;
        }
    }

    static float Fract(float x) => x - MathF.Floor(x);

    /// <summary>The rain falling at a point: its region's, as much of it as its cells' share lets through there.</summary>
    public float RainAt(Vector3 p)
    {
        Conditions c = Land == null && _frontSegment == null ? Current : LocalAt(p);
        return c.Rain * (1f - c.Cellular * (1f - CellCover(p)));
    }

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
        // Under a storm (a cell's tower or the deck behind a front's edge), strikes come down round you.
        if (cell == null || CellCover(observer) > 0.2f) return observer + RandomOffset(Data.Lightning.StrikeOffset);
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
        foreach (Tornado t in _tornadoes) if (!t.Dissipating && t.Kind == TornadoKind.Tornado) active++;
        if (OpenWater != null) StepSpouts(dt, observer);
        if (Land != null) StepDevils(dt, observer);
        if (Accumulate(ref _tornadoCharge, ref _tornadoNext, Current.TornadoesPerMinute, dt) && active < rules.MaxActive)
            TryTornado(observer);

        bool stormGone = Current.TornadoesPerMinute < 0.05f;
        _groundCheck -= dt;
        bool look = _groundCheck <= 0f;
        if (look) _groundCheck = 0.5f;
        for (int i = _tornadoes.Count - 1; i >= 0; i--)
        {
            Tornado t = _tornadoes[i];
            bool wasDown = t.Touchdown;
            if (stormGone && !t.Homing && !t.Kept && t.Kind == TornadoKind.Tornado) t.Dissipate();
            if (t.Kind == TornadoKind.Waterspout)
            {
                SpoutFate(t);
                if (t.Storm && Current.LightningPerMinute < 0.1f && Current.TornadoesPerMinute < 0.05f) t.Dissipate();
            }
            t.Step(dt, observer, GroundHeight);
            if (look) GroundFate(t, 0.5f);
            if (t.Touchdown && !wasDown)
                _events.Add(new WeatherEvent(WeatherEventKind.TornadoTouchdown, t.Id, t.Position, null, 0f, 0f));
            if (!t.Done) continue;
            _events.Add(new WeatherEvent(WeatherEventKind.TornadoGone, t.Id, t.Position, null, 0f, 0f));
            _tornadoes.RemoveAt(i);
        }
    }

    /// <summary>
    /// A storm lets a funnel down: only from one of its strong cells, never within the rule's distance
    /// of the player, and only where the region allows it (broad open flat or rolling land in warm
    /// humid air; rarely over dense forest; never over high mountains). Over open water the same cell
    /// lets down waterspouts instead. Where no cell has such ground under it, none forms.
    /// </summary>
    void TryTornado(Vector3 observer)
    {
        TornadoGround rules = Data.Regions.Tornado;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            StormCell cell = null;
            int strong = 0;
            foreach (StormCell c in _cells)
                if (c.Strength >= rules.MinCell && c.Funnel == null && Flat(c.Position - observer).Length() < Data.Cells.Range * 0.8f && _hazardRng.NextFloat() * ++strong < 1f) cell = c;
            if (cell == null) return;
            Vector3 at = cell.Position + RandomOffset(cell.Radius * 0.55f);
            if (Flat(at - observer).Length() < rules.MinDistance) continue;
            if (OpenWater != null && OpenWater(at.X, at.Z))
            {
                if (SpoutGroups() < 2) SpawnSpouts(at, storm: true);
                return;
            }
            if (_hazardRng.NextFloat() >= Regions.TornadoGround(Data.Regions, Land, at)) continue;
            // Natural tornadoes do not chase the player: a random heading, no homing, a slow wander.
            Tornado born = SpawnTornado(at, observer, Roll(Current.WindSpeed));
            born.Homing = false;
            born.SetHeading(_hazardRng.Range(0f, MathF.Tau));
            born.WanderRate = 0.12f;
            return;
        }
    }

    /// <summary>
    /// A tornado's own speed, life and shape, from its storm's wind: ground speed rolled up to the
    /// 60 km/h cap; its life falls as its speed rises (a fast one brief, a slow meandering one lingering
    /// longest); and its shape leans by its speed (fast ones ropes and cones, slow ones wide wedges).
    /// </summary>
    public (TornadoRules rules, TornadoShapeRules shape) Roll(float stormWind)
    {
        TornadoRules b = Data.Tornado;
        float speed = Math.Clamp(MathF.Max(stormWind, 4f) * _hazardRng.Range(b.SteerMin, b.SteerMax), b.SpeedMin, b.SpeedMax);
        float fast = (speed - b.SpeedMin) / (b.SpeedMax - b.SpeedMin);
        float life = (b.LifeSlow + (b.LifeFast - b.LifeSlow) * fast) * (1f + _hazardRng.Range(-b.LifeJitter, b.LifeJitter));
        float total = 0f;
        foreach (TornadoShapeRules s in b.Shapes) total += ShapeOdds(s, fast);
        float pick = _hazardRng.NextFloat() * total;
        TornadoShapeRules shape = b.Shapes[^1];
        foreach (TornadoShapeRules s in b.Shapes)
        {
            pick -= ShapeOdds(s, fast);
            if (pick < 0f) { shape = s; break; }
        }
        return (b.Shaped(shape, speed, life), shape);
    }

    static float ShapeOdds(TornadoShapeRules s, float fast)
    {
        float d = (fast - s.Speed) / MathF.Max(s.Spread, 0.01f);
        return s.Weight * MathF.Exp(-d * d);
    }

    /// <summary>Starts a tornado of a rolled shape and speed forming at <paramref name="at"/> (see <see cref="Roll"/>).</summary>
    public Tornado SpawnTornado(Vector3 at, Vector3 homeOn, (TornadoRules rules, TornadoShapeRules shape) rolled)
    {
        Tornado t = SpawnTornado(at, homeOn, rolled.rules);
        Shape(t, rolled.shape);
        return t;
    }

    void Shape(Tornado t, TornadoShapeRules shape)
    {
        t.Shape = shape.Shape;
        t.Look = _hazardRng.Range(shape.LookMin, shape.LookMax);
        t.Smooth = _hazardRng.NextFloat() < shape.Smooth;
    }

    /// <summary>
    /// What the ground does to a whirl as it wanders (every half second): a tornado that wanders onto
    /// ground it can't stand on (high mountains, steep ground) ropes out; over dense forest it weakens
    /// and soon ropes out too; onto open water a strong one goes on as a waterspout, a weak one falls
    /// apart. A dust devil dies off dry loose ground or under cloud.
    /// </summary>
    void GroundFate(Tornado t, float dt)
    {
        if (!t.Touchdown || t.Dissipating || t.Homing || t.Kept) return;
        if (t.Kind == TornadoKind.DustDevil)
        {
            if (Land == null) return;
            Ground d = GroundAt(t.Position);
            if (Regions.DustGround(Data.Regions, d) < 0.3f || CellCover(t.Position) > 0.05f || Current.CloudCover > Data.Regions.Dust.DevilCloud * 1.3f) t.Dissipate();
            return;
        }
        if (t.Kind != TornadoKind.Tornado) return;
        if (OpenWater != null && OpenWater(t.Position.X, t.Position.Z))
        {
            if (t.Strength > 0.7f && _hazardRng.NextFloat() < 0.5f)
            {
                t.Kind = TornadoKind.Waterspout;
                t.Ashore = false;
            }
            else t.Dissipate();
            return;
        }
        if (Land == null) return;
        RegionRules r = Data.Regions;
        Ground g = GroundAt(t.Position);
        if (Regions.Barred(r, g)) t.Dissipate();
        else if (g.Forest >= r.Tornado.DenseForest && _hazardRng.NextFloat() < 0.25f * dt) t.Dissipate();
    }

    // ---- Dust devils --------------------------------------------------------------------

    /// <summary>
    /// Dust devils: fair-weather whirls off hot dry loose ground on clear calm days, never under
    /// cloud and never on top of the player. Short, small and mostly harmless.
    /// </summary>
    void StepDevils(float dt, Vector3 observer)
    {
        DustGround rules = Data.Regions.Dust;
        Conditions c = Current;
        float fair = c.Rain < 0.02f && c.Snow < 0.02f && c.CloudCover < rules.DevilCloud ? Front.Smooth(rules.DevilWind, rules.DevilWind * 0.4f, c.WindSpeed) : 0f;
        if (!Accumulate(ref _devilCharge, ref _devilNext, fair * rules.DevilsPerMinute, dt)) return;
        int devils = 0;
        foreach (Tornado t in _tornadoes) if (t.Kind == TornadoKind.DustDevil) devils++;
        if (devils >= 3) return;
        for (int k = 0; k < 8; k++)
        {
            Vector3 p = observer + RandomOffset(900f);
            if (Flat(p - observer).Length() < 120f || CellCover(p) > 0.02f || (OpenWater?.Invoke(p.X, p.Z) ?? false)) continue;
            Ground g = GroundAt(p);
            if (Regions.DustGround(Data.Regions, g) < 0.6f || g.Warmth < rules.DevilWarmth) continue;
            SpawnDevil(p);
            return;
        }
    }

    /// <summary>A dust devil at a point (natural ones from <see cref="StepDevils"/>; tests and screenshots).</summary>
    public Tornado SpawnDevil(Vector3 at)
    {
        TornadoRules rules = Data.Tornado.Scaled(0.3f, 0.12f, 0.35f, 3f, _hazardRng.Range(20f, 70f), 6f);
        rules.Speed = _hazardRng.Range(1f, 4f);
        var devil = new Tornado(_nextId++, rules, new Vector3(at.X, GroundHeight(at.X, at.Z), at.Z), _hazardRng.Range(0f, MathF.Tau), homing: false, _hazardRng.Range(0f, MathF.Tau))
        {
            Kind = TornadoKind.DustDevil, Storm = false, Height = _hazardRng.Range(15f, 120f), WanderRate = 0.3f,
        };
        _tornadoes.Add(devil);
        _events.Add(new WeatherEvent(WeatherEventKind.TornadoForming, devil.Id, devil.Position, null, 0f, 0f));
        return devil;
    }

    // ---- Waterspouts --------------------------------------------------------------------

    /// <summary>
    /// Waterspouts form only over open water: under growing cumulus on warm fair days with little wind
    /// (short, mostly harmless), or out of a storm's cells. Two to four hang from one base at different
    /// stages, some never reaching the water.
    /// </summary>
    void StepSpouts(float dt, Vector3 observer)
    {
        Conditions c = Current;
        float fair = c.Rain < 0.05f && c.LightningPerMinute < 0.1f && c.CloudCover > 0.25f && c.CloudCover < 0.85f
            ? Front.Smooth(12f, 26f, c.Temperature) * Front.Smooth(11f, 4f, c.WindSpeed) : 0f;
        float stormy = MathF.Min(1f, c.LightningPerMinute / 4f) * c.Cellular;
        if (!Accumulate(ref _spoutCharge, ref _spoutNext, fair * 0.3f + stormy * 0.35f, dt)) return;
        if (SpoutGroups() >= 2) return;
        if (stormy > fair)
            foreach (StormCell cell in _cells)
            {
                if (cell.Strength < 0.6f || cell.Funnel != null) continue;
                Vector3 p = cell.Position + RandomOffset(cell.Radius * 0.5f);
                if (OpenWater(p.X, p.Z)) { SpawnSpouts(p, storm: true); return; }
            }
        if (fair <= 0f) return;
        for (int k = 0; k < 12; k++)
        {
            Vector3 p = observer + RandomOffset(1800f);
            if (Flat(p - observer).Length() > 250f && OpenWater(p.X, p.Z)) { SpawnSpouts(p, storm: false); return; }
        }
    }

    /// <summary>How many bases have spouts hanging from them now.</summary>
    int SpoutGroups()
    {
        int n = 0;
        for (int i = 0; i < _tornadoes.Count; i++)
        {
            Tornado t = _tornadoes[i];
            if (t.Kind != TornadoKind.Waterspout) continue;
            bool first = true;
            for (int j = 0; j < i && first; j++) first = !(_tornadoes[j].Kind == TornadoKind.Waterspout && _tornadoes[j].Group == t.Group);
            if (first) n++;
        }
        return n;
    }

    /// <summary>
    /// Two to four spouts from one base, coming down at different times, a few stopping short of the
    /// water. A storm's base is the cell over them; a fair day's is the cumulus they grow under.
    /// </summary>
    public IReadOnlyList<Tornado> SpawnSpouts(Vector3 at, bool storm)
    {
        var made = new List<Tornado>();
        int n = 2 + Math.Min(2, (int)(_hazardRng.NextFloat() * 3f));
        int group = _nextId++;
        Vector2 wind = Current.Wind;
        float heading = wind.LengthSquared() > 0.01f ? MathF.Atan2(wind.Y, wind.X) : _hazardRng.Range(0f, MathF.Tau);
        for (int k = 0; k < n; k++)
        {
            Vector3 p = k == 0 ? at : at + RandomOffset(storm ? 260f : 170f);
            if (OpenWater != null && !OpenWater(p.X, p.Z)) p = at;
            float life = storm ? _hazardRng.Range(70f, 160f) : _hazardRng.Range(45f, 110f);
            TornadoRules rules = storm ? Data.Tornado.Scaled(0.7f, 0.5f, 0.6f, 18f, life, 14f) : Data.Tornado.Scaled(0.35f, 0.15f, 0.45f, 22f, life, 16f);
            rules.Speed = MathF.Max(1f, Current.WindSpeed * 0.25f);
            var spout = new Tornado(_nextId++, rules, new Vector3(p.X, GroundHeight(p.X, p.Z), p.Z), heading, homing: false, _hazardRng.Range(0f, MathF.Tau))
            {
                Kind = TornadoKind.Waterspout, Group = group, Storm = storm,
                Reach = k > 0 && _hazardRng.NextFloat() < 0.45f ? _hazardRng.Range(0.3f, 0.75f) : 1f,
                WanderRate = 0.1f,
            };
            spout.Delay(k == 0 ? 0f : _hazardRng.Range(4f, 30f));
            _tornadoes.Add(spout);
            made.Add(spout);
            _events.Add(new WeatherEvent(WeatherEventKind.TornadoForming, spout.Id, spout.Position, null, 0f, 0f));
        }
        if (storm)
        {
            StormCell parent = null;
            foreach (StormCell c in _cells)
                if (c.Funnel == null && Flat(c.Position - at).Length() < c.Radius * 1.2f && (parent == null || c.Strength > parent.Strength)) parent = c;
            (parent ?? AddCell(at, 1f, first: true)).Funnel = made[0];
        }
        return made;
    }

    /// <summary>A spout that drifts ashore decays, unless a storm's strong one goes on over the land as a tornado.</summary>
    void SpoutFate(Tornado t)
    {
        if (t.Ashore || !t.Touchdown || OpenWater == null || OpenWater(t.Position.X, t.Position.Z)) return;
        t.Ashore = true;
        // Only onto land a tornado could stand on.
        bool land = Land == null || Regions.TornadoPoint(Data.Regions, GroundAt(t.Position)) > 0.2f;
        if (land && t.Storm && t.Strength > 0.6f && t.Reach >= 1f && _hazardRng.NextFloat() < 0.35f)
        {
            t.Kind = TornadoKind.Tornado;
            var (rules, shape) = Roll(Current.WindSpeed);
            rules.Lifetime = MathF.Min(rules.Lifetime, t.Lifetime);
            t.Rules = rules;
            Shape(t, shape);
        }
        else t.Dissipate();
    }

    /// <summary>Starts a tornado forming at <paramref name="at"/>, heading for <paramref name="homeOn"/>.</summary>
    public Tornado SpawnTornado(Vector3 at, Vector3 homeOn) => SpawnTornado(at, homeOn, Data.Tornado);

    /// <summary>Starts a tornado with these rules forming at <paramref name="at"/>, heading for <paramref name="homeOn"/>.</summary>
    public Tornado SpawnTornado(Vector3 at, Vector3 homeOn, TornadoRules rules)
    {
        float heading = MathF.Atan2(homeOn.Z - at.Z, homeOn.X - at.X);
        at = new Vector3(at.X, GroundHeight(at.X, at.Z), at.Z);
        var tornado = new Tornado(_nextId++, rules, at, heading, homing: true, _hazardRng.Range(0f, MathF.Tau));
        _tornadoes.Add(tornado);
        _events.Add(new WeatherEvent(WeatherEventKind.TornadoForming, tornado.Id, at, null, 0f, 0f));
        // Every funnel hangs from a cell: the nearest that has none yet, or a new one over it.
        StormCell parent = null;
        foreach (StormCell c in _cells)
            if (c.Funnel == null && Flat(c.Position - at).Length() < c.Radius && (parent == null || c.Strength > parent.Strength)) parent = c;
        parent ??= AddCell(at, 1f, first: true); // drawn whatever else is in the sky
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
        Vector2 mean = MeanWindAt(position);
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

    /// <summary>The mean wind at a place, m/s: its part of the front's (the storm's behind the edge), harder on the heights, softer in the woods.</summary>
    public Vector2 MeanWindAt(Vector3 p)
    {
        Vector2 mean = Current.Wind;
        if (_front != null && _frontSegment != null && (_frontSegment == SegmentAt(Time) || _frontSegment.Start > Time))
            mean = (_frontSegment.Start > Time ? Current : _frontSegment.From).Wind.Lerp(_frontSegment.To.Wind, _front.Strength * _front.Under(p));
        if (Land == null) return mean;
        RegionRules r = Data.Regions;
        Ground g = Land(p.X, p.Z);
        return mean * (1f + r.Wind.HighBoost * Regions.High(r, g)) * (1f - r.Wind.ForestCut * g.Forest);
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
