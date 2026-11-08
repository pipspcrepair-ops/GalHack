using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Weather.Forces;
using Weather.Model;

namespace Weather;

/// <summary>
/// Headless test of the weather model, the force module and the scene. Run with:
///   godot --headless --path prototypes/weather res://tests/WeatherTest.tscn
/// Exits 0 if every check passes, 1 if any fails.
///
/// The pure checks run first and need no scene. Then the real scene is built and driven for
/// a few seconds of physics, so the same forces are proven on Jolt bodies too.
/// </summary>
public partial class WeatherTest : Node
{
    const float Dt = 1f / 60f;

    WeatherData _data;
    int _failures;

    // Scene stage
    Main _main;
    int _stage, _tick;
    Vector3 _hoverStart, _roverStart;
    float _hoverPeak, _hoverGround;
    bool _hoverReleased;
    readonly List<(string weather, int particles)> _particleCounts = new();

    public override void _Ready()
    {
        _data = WeatherData.Parse(FileAccess.GetFileAsString("res://data/weather.json"));
        GD.Print($"Loaded {_data.States.Count} weather states and {_data.Archetypes.Count} archetypes\n");

        Section("Determinism");
        SameSeedSameWeather();
        Section("Forecasts and warnings");
        ForecastsLeadTheWeather();
        Section("Wind and mass");
        WindScalesWithMass();
        Section("Tornado");
        TornadoLiftsOrbitsReleases();
        TornadoIsNeverSilent();
        Section("Tornado speed, life and shape");
        SpeedSetsLife();
        Section("Each world's own weather");
        WorldDials();
        Section("Front edges");
        FrontEdgesBillow();
        Section("Tornadoes by mass");
        PullOrderedByMass();
        Section("Damage: never reduced, a tornado fling's landing included");
        FlingIsUniform();
        StrikesAreWarnedFirst();
        Section("Lightning");
        LightningBuildsThenHitsHighest();
        Section("Modifiers per weather");
        ModifiersApply();
        Section("Smooth transitions");
        TransitionsAreSmooth();
        Section("Performance");
        TickCost();

        Section("Scene");
        ProcessPhysicsPriority = 100; // check after the world has moved
        _main = new Main { Seed = 7 };
        AddChild(_main);
    }

    // ---- Pure checks ------------------------------------------------------------------

    void SameSeedSameWeather()
    {
        var a = new WeatherSim(_data, "temperate", 1234);
        var b = new WeatherSim(_data, "temperate", 1234);
        var c = new WeatherSim(_data, "temperate", 1235);
        AddStrikePoints(a);
        AddStrikePoints(b);
        var eventsA = new List<string>();
        var eventsB = new List<string>();
        float maxDiff = 0f;
        for (int i = 0; i < 2 * 3600 * 10; i++) // two hours at 10 Hz
        {
            var observer = new Vector3(MathF.Sin(i * 0.001f) * 80f, 0f, i * 0.002f);
            a.Step(0.1f, observer);
            b.Step(0.1f, observer);
            c.Step(0.1f, observer);
            foreach (var e in a.Events) eventsA.Add($"{a.Time:F1} {e.Kind} {e.Position}");
            foreach (var e in b.Events) eventsB.Add($"{b.Time:F1} {e.Kind} {e.Position}");
            for (int p = 0; p < Conditions.Count; p++) maxDiff = MathF.Max(maxDiff, MathF.Abs(a.Current[p] - b.Current[p]));
        }

        string Describe(WeatherSim s) => string.Join(" ", s.Timeline.Select(x => $"{x.Kind}@{x.Start:F0}+{x.Duration:F0}"));
        Check(Describe(a) == Describe(b) && a.Timeline.Count > 20,
            $"same seed, same timeline ({a.Timeline.Count} segments over 2 h)");
        Check(maxDiff == 0f, "same seed, identical conditions every tick");
        Check(eventsA.SequenceEqual(eventsB) && eventsA.Count > 0, $"same seed, same hazards ({eventsA.Count} strikes and tornado events)");
        Check(Describe(a) != Describe(c), "a different seed gives a different timeline");
        GD.Print("      first hour: " + string.Join(", ", a.Timeline.TakeWhile(s => s.Start < 3600).Select(s => $"{s.Kind} {WeatherSim.Clock(s.Start)}")));
    }

    void ForecastsLeadTheWeather()
    {
        int segments = 0, severe = 0, lateForecasts = 0, lateWarnings = 0;
        float shortestWarning = float.MaxValue;
        var forecast = new List<ForecastEntry>();
        var warnings = new List<WarningEntry>();
        foreach (Archetype archetype in _data.Archetypes)
        {
            var sim = new WeatherSim(_data, archetype.Id, 99);
            var firstForecast = new Dictionary<double, double>(); // segment start -> first time forecast
            var firstWarning = new Dictionary<double, double>();
            const float step = 0.5f;
            while (sim.Time < 3 * 3600)
            {
                sim.Step(step, Vector3.Zero);
                sim.GetForecast(forecast);
                foreach (var f in forecast)
                    firstForecast.TryAdd(Math.Round(sim.Time + f.Eta, 2), sim.Time);
                sim.GetWarnings(warnings, Vector3.Zero);
                foreach (var w in warnings)
                    if (w.Kind != null) firstWarning.TryAdd(Math.Round(w.ArrivesAt, 2), sim.Time);
            }

            foreach (Segment s in sim.Timeline)
            {
                WeatherState state = _data.State(s.Kind);
                if (s.Start < state.ForecastLead + step || s.Start > 3 * 3600 - 1) continue; // outside what we watched
                segments++;
                double key = Math.Round(s.Start, 2);
                if (!firstForecast.TryGetValue(key, out double seen) || seen > s.Start - state.ForecastLead + step)
                {
                    lateForecasts++;
                    GD.PrintErr($"      {archetype.Id}: {s.Kind} at {s.Start:F0} forecast late or never ({(firstForecast.TryGetValue(key, out double x) ? x : -1):F0})");
                }
                if (!state.Severe) continue;
                severe++;
                if (!firstWarning.TryGetValue(key, out double warned) || warned >= s.Start || warned > s.Start - state.WarningLead + step)
                {
                    lateWarnings++;
                    GD.PrintErr($"      {archetype.Id}: severe {s.Kind} at {s.Start:F0} warned late or never");
                }
                else shortestWarning = MathF.Min(shortestWarning, (float)(s.Start - warned));
            }
        }
        Check(lateForecasts == 0 && segments > 100,
            $"every forecast appears at least its lead time ahead ({segments} arrivals over 7 planets × 3 h)");
        Check(lateWarnings == 0 && severe > 20,
            $"every severe weather is warned first ({severe} severe arrivals, shortest warning {shortestWarning:F0} s ahead)");

        // Tornadoes are warned while the funnel forms, before it touches down.
        var sim2 = new WeatherSim(_data, "temperate", 5, WeatherKind.Clear);
        sim2.SpawnTornado(new Vector3(200, 0, 0), Vector3.Zero);
        sim2.GetWarnings(warnings, Vector3.Zero);
        bool warnedEarly = warnings.Any(w => w.Text.Contains("Funnel forming"));
        Check(warnedEarly && _data.Tornado.FormSeconds >= 8f,
            $"a tornado is warned the moment it starts forming, {_data.Tornado.FormSeconds:F0} s before touchdown");
    }

    void WindScalesWithMass()
    {
        // Same wind, same drag profile: acceleration is exactly inverse to mass.
        var light = new TestBody(Specs.HoverMass, 2f);
        var heavy = new TestBody(Specs.RoverMass, 2f);
        var wind = new Vector3(20f, 0f, 0f);
        float aLight = ForceModule.Drag(light, wind).Length() / light.Mass;
        float aHeavy = ForceModule.Drag(heavy, wind).Length() / heavy.Mass;
        float ratio = aLight / aHeavy;
        Check(MathF.Abs(ratio - Specs.RoverMass / Specs.HoverMass) < 0.01f,
            $"wind acceleration ∝ 1/mass ({aLight:F3} vs {aHeavy:F4} m/s², ratio {ratio:F2} = mass ratio {Specs.RoverMass / Specs.HoverMass:F2})");

        // The real vehicles, free to drift in a steady 20 m/s wind for 3 s.
        var forces = new ForceModule { Wind = _ => wind };
        var hover = new TestBody(Specs.HoverMass, Specs.HoverDragArea);
        var rover = new TestBody(Specs.RoverMass, Specs.RoverDragArea);
        forces.Register(hover);
        forces.Register(rover);
        for (int i = 0; i < 180; i++)
        {
            forces.Step(Dt);
            hover.Integrate(Dt, gravity: false);
            rover.Integrate(Dt, gravity: false);
        }
        float drift = hover.Position.Length() / rover.Position.Length();
        Check(drift >= 5f, $"light hover vessel drifts {drift:F1}× further than the heavy rover ({hover.Position.Length():F1} m vs {rover.Position.Length():F2} m in 3 s)");
    }

    /// <summary>
    /// Thousands of tornadoes rolled from storms of every strength, fresh seeds every run: none faster
    /// than 60 km/h, the fast ones briefest and the slow ones longest-lived, fast ones mostly ropes and
    /// cones and slow ones mostly wedges.
    /// </summary>
    void SpeedSetsLife()
    {
        var sim = new WeatherSim(_data, "temperate", (ulong)Random.Shared.NextInt64(), WeatherKind.Tornado);
        var rolled = new List<(float speed, float life, TornadoShape shape)>();
        for (int i = 0; i < 4000; i++)
        {
            var (rules, shape) = sim.Roll(Random.Shared.NextSingle() * 30f);
            rolled.Add((rules.Speed, rules.Lifetime, shape.Shape));
        }
        float fastest = rolled.Max(x => x.speed);
        Check(fastest <= 60f / 3.6f + 1e-3f, $"no tornado moves faster than 60 km/h (fastest {fastest * 3.6f:F1} km/h of {rolled.Count})");
        var bySpeed = rolled.OrderBy(x => x.speed).ToList();
        int q = bySpeed.Count / 4;
        float[] life = Enumerable.Range(0, 4).Select(k => bySpeed.Skip(k * q).Take(q).Average(x => x.life)).ToArray();
        float[] speed = Enumerable.Range(0, 4).Select(k => bySpeed.Skip(k * q).Take(q).Average(x => x.speed)).ToArray();
        Check(life[0] > life[1] && life[1] > life[2] && life[2] > life[3] && life[0] > 2.5f * life[3],
            $"its life falls as its speed rises: by speed quarter {string.Join(", ", Enumerable.Range(0, 4).Select(k => $"{speed[k] * 3.6f:F0} km/h lives {life[k]:F0} s"))}");
        float Share(int k, params TornadoShape[] shapes) => bySpeed.Skip(k * q).Take(q).Count(x => shapes.Contains(x.shape)) / (float)q;
        float fastRopes = Share(3, TornadoShape.Rope, TornadoShape.Cone), slowWedges = Share(0, TornadoShape.Wedge);
        Check(fastRopes > 0.7f && slowWedges > 0.5f && Share(0, TornadoShape.Rope) < 0.1f && Share(3, TornadoShape.Wedge) < 0.1f,
            $"fast ones lean to ropes and cones ({fastRopes:P0} of the fastest quarter), slow ones to wedges ({slowWedges:P0} of the slowest)");
    }

    /// <summary>
    /// A storm front's edge, on fresh fronts every run: it never runs straight. Walking along it every 25 m,
    /// no stretch longer than a few hundred metres keeps one direction, and its direction keeps turning.
    /// </summary>
    void FrontEdgesBillow()
    {
        int fronts = 20, ruled = 0;
        float worstRun = 0f, leastTurn = float.MaxValue;
        for (int k = 0; k < fronts; k++)
        {
            var sim = new WeatherSim(_data, "storm", (ulong)Random.Shared.NextInt64(), WeatherKind.Thunderstorm)
            { WorldSize = new Vector2(Random.Shared.Next(8, 33) * 1000f, Random.Shared.Next(4, 17) * 1000f) };
            float a = Random.Shared.NextSingle() * MathF.Tau;
            Front f = sim.StartFront(FrontKind.Storm, Vector3.Zero, 2000f, new Vector2(MathF.Cos(a), MathF.Sin(a)) * 15f);
            const float step = 25f;
            int samples = (int)MathF.Min(f.Period / step, 1600);
            float run = 0f, longest = 0f, turnSum = 0f;
            float prevSlope = (f.Lag(step) - f.Lag(0f)) / step;
            for (int i = 1; i < samples; i++)
            {
                float u = i * step;
                float slope = (f.Lag(u + step) - f.Lag(u)) / step;
                float turn = MathF.Abs(MathF.Atan(slope) - MathF.Atan(prevSlope));
                turnSum += turn;
                run = turn < 0.002f ? run + step : 0f; // under a tenth of a degree a step: running straight
                longest = MathF.Max(longest, run);
                prevSlope = slope;
            }
            float turnPerKm = turnSum / (samples * step / 1000f);
            worstRun = MathF.Max(worstRun, longest);
            leastTurn = MathF.Min(leastTurn, turnPerKm);
            if (longest > 300f || turnPerKm < 0.5f) ruled++;
        }
        Check(ruled == 0, $"no front's edge runs straight: over {fronts} fresh fronts, the longest straight stretch {worstRun:F0} m, the least turning {Mathf.RadToDeg(leastTurn):F0}° a kilometre");
    }

    /// <summary>Each world leans its type's weights by its own climate and layers, and two worlds of a type still differ.</summary>
    void WorldDials()
    {
        WorldRules rules = _data.Worlds;
        var dry = new Dictionary<string, string> { ["temp"] = "hot", ["water"] = "none" };
        var wet = new Dictionary<string, string> { ["temp"] = "mild", ["water"] = "wet" };
        int n = 60, drier = 0, stormier = 0, differ = 0;
        for (int i = 0; i < n; i++)
        {
            ulong seed = (ulong)Random.Shared.NextInt64();
            WorldWeather d = WorldWeather.Roll(rules, dry, new[] { "dust_storms" }, seed);
            WorldWeather w = WorldWeather.Roll(rules, wet, new[] { "thunderheads" }, seed);
            WorldWeather other = WorldWeather.Roll(rules, wet, new[] { "thunderheads" }, seed + 1);
            if (d.Dryness > w.Dryness && d.Wetness < w.Wetness) drier++;
            if (w.Tornadoes > d.Tornadoes && w.Storminess > d.Storminess) stormier++;
            if (MathF.Abs(w.Storminess - other.Storminess) > 0.01f) differ++;
        }
        Check(drier == n, $"a hot waterless world of dust storms is drier and less wet than a mild wet one ({drier} of {n} rolls)");
        Check(stormier == n, $"a wet world of big storm towers is stormier and more prone to tornadoes ({stormier} of {n})");
        Check(differ > n * 0.9f, $"two worlds with the same layers still lean their own way ({differ} of {n})");
        ulong s = (ulong)Random.Shared.NextInt64();
        var plain = new WeatherSim(_data, "temperate", s);
        var towers = new WeatherSim(_data, "temperate", s, world: WorldWeather.Roll(rules, wet, new[] { "thunderheads", "perpetual_storm" }, s));
        Check(towers.Share(WeatherKind.Tornado) > plain.Share(WeatherKind.Tornado) * 1.5f,
            $"the dials reach the timeline: tornado storms are {plain.Share(WeatherKind.Tornado):P0} of a plain temperate world's weather, {towers.Share(WeatherKind.Tornado):P0} of a stormy one's ({towers.World})");
    }

    /// <summary>
    /// Bodies from a pebble-light critter to a capital ship at the base of each shape of tornado: the
    /// pull, the lift and the throw fall with mass for every shape, and only the lightest are lifted far.
    /// </summary>
    void PullOrderedByMass()
    {
        (string name, float mass, float area)[] bodies =
        {
            ("critter", 8f, 0.08f), ("player", Specs.PlayerMass, Specs.PlayerDragArea), ("light skiff", Specs.HoverMass, Specs.HoverDragArea),
            ("rover", Specs.RoverMass, Specs.RoverDragArea), ("hover tank", 354000f, 90f), ("frigate", 1002500f, 400f),
        };
        bool ordered = true, bigStay = true;
        foreach (TornadoShapeRules shape in _data.Tornado.Shapes)
        {
            var row = new List<string>();
            float lastPeak = float.MaxValue, lastThrow = float.MaxValue;
            foreach (var (name, mass, area) in bodies)
            {
                var (peak, thrown) = Pull(shape, mass, area);
                row.Add($"{name} {peak:F0} m up, {thrown:F0} m off");
                ordered &= peak <= lastPeak * 1.05f + 0.5f && thrown <= lastThrow * 1.1f + 1f; // below the mass limit all take the full pull alike
                lastPeak = peak;
                lastThrow = thrown;
                if (mass > 100000f) bigStay &= peak < 0.5f && thrown < 3f;
            }
            GD.Print($"      {shape.Shape,-6} {string.Join(", ", row)}");
        }
        Check(ordered, "for every shape, the heavier the body the less it is lifted and the less far it is thrown");
        Check(bigStay, "a hover tank or a frigate stays put: no tornado lifts it, none drags it more than a few metres");
    }

    /// <summary>A body at the base of a tornado of one shape, at full strength: how high it goes, and how far from where it stood it ends up.</summary>
    (float peak, float thrown) Pull(TornadoShapeRules shape, float mass, float area)
    {
        var sim = new WeatherSim(_data, "temperate", (ulong)Random.Shared.NextInt64(), WeatherKind.Clear);
        var forces = new ForceModule();
        var body = new TestBody(mass, area);
        forces.Register(body);
        Tornado t = sim.SpawnTornado(new Vector3(0, 0, 0), new Vector3(100, 0, 0), _data.Tornado.Shaped(shape, 0.01f, 300f));
        t.SkipFormation();
        body.Position = new Vector3(3f, 0f, 0f);
        float peak = 0f;
        for (int i = 0; i < 60 * 25; i++)
        {
            sim.Step(Dt, body.Position);
            Glue.Sync(sim, forces);
            forces.Step(Dt);
            body.Integrate(Dt, gravity: true);
            peak = MathF.Max(peak, body.Position.Y);
        }
        return (peak, new Vector2(body.Position.X - 3f, body.Position.Z).Length());
    }

    /// <summary>What happened to a body caught at a tornado's base, from catch to landing.</summary>
    record struct Fling(List<VortexPhase> Phases, float Peak, float Orbit, float Landing, float HealthAfter, float Dealt);

    /// <summary>Puts a body at the base of a fresh tornado and follows it until it lands after the fling.</summary>
    Fling RunFling(float mass, float dragArea, float startHealth, int seconds = 30)
    {
        var sim = new WeatherSim(_data, "temperate", 3, WeatherKind.Clear);
        var forces = new ForceModule { Wind = sim.WindAt };
        var body = new TestBody(mass, dragArea);
        forces.Register(body);
        var phases = new List<VortexPhase>();
        forces.PhaseChanged += (_, phase) => phases.Add(phase);
        var hp = new Health(100f);
        hp.Set(startHealth);

        Tornado tornado = sim.SpawnTornado(new Vector3(-40, 0, 0), Vector3.Zero);
        while (!tornado.Touchdown) sim.Step(Dt, body.Position);
        body.Position = tornado.Position + new Vector3(4f, 0f, 0f);

        float peak = 0f, orbit = 0f, landing = 0f, dealt = 0f;
        bool released = false;
        for (int i = 0; i < 60 * seconds && landing == 0f; i++)
        {
            if (!released && phases.Contains(VortexPhase.Released))
            {
                released = true;
                body.Landed = null; // only the landing after the fling counts
            }
            sim.Step(Dt, body.Position);
            Glue.Sync(sim, forces);
            forces.Step(Dt);
            body.Integrate(Dt, gravity: true);
            peak = MathF.Max(peak, body.Position.Y);
            if (forces.PhaseOf(body) == VortexPhase.Caught) orbit = forces.OrbitOf(body);
            if (released && body.Landed is float speed)
            {
                // Exactly what the player does on touching down: fall damage, a fling or not.
                landing = speed;
                dealt = hp.Hit(DamageRules.FallDamage(speed));
            }
        }
        return new Fling(phases, peak, orbit, landing, hp.Current, dealt);
    }

    void TornadoLiftsOrbitsReleases()
    {
        Fling f = RunFling(Specs.PlayerMass, Specs.PlayerDragArea, 100f);
        Check(f.Phases.Count >= 2 && f.Phases[0] == VortexPhase.Caught, "a body at the tornado's base is caught");
        Check(f.Peak > 20f, $"it rises ({f.Peak:F1} m at the top)");
        Check(f.Orbit > MathF.PI, $"it orbits the funnel ({f.Orbit / MathF.Tau:F2} turns)");
        Check(f.Phases.Contains(VortexPhase.Released) && f.Landing > 0f,
            $"it is let go and falls back to the ground (landing at {f.Landing:F1} m/s)");

        Fling heavy = RunFling(Specs.RoverMass, Specs.RoverDragArea, 100f, seconds: 10);
        Check(heavy.Peak < f.Peak * 0.7f, $"the 6.5 t rover is lifted less ({heavy.Peak:F1} m vs {f.Peak:F1} m)");
    }

    void FlingIsUniform()
    {
        Fling full = RunFling(Specs.PlayerMass, Specs.PlayerDragArea, 100f);
        float raw = DamageRules.FallDamage(full.Landing);
        Check(full.Dealt > 0f && MathF.Abs(full.Dealt - MathF.Min(raw, 100f)) < 1e-3f,
            $"from full health a fling's landing deals its fall damage in full (landing at {full.Landing:F1} m/s: {raw:F0}, dealt {full.Dealt:F0}, {full.HealthAfter:F0} left)");
        Fling low = RunFling(Specs.PlayerMass, Specs.PlayerDragArea, 30f);
        float rawLow = DamageRules.FallDamage(low.Landing);
        Check(MathF.Abs(low.HealthAfter - MathF.Max(0f, 30f - rawLow)) < 1e-3f,
            $"from 30 health nothing holds it back: {rawLow:F0} raw leaves {low.HealthAfter:F1}{(low.HealthAfter <= 0f ? " (dead)" : "")}");

        // A fling's landing and a cliff fall at the same speed hurt exactly the same.
        var plain = new Health(100f);
        plain.Set(30f);
        plain.Hit(DamageRules.FallDamage(low.Landing));
        Check(MathF.Abs(plain.Current - low.HealthAfter) < 1e-3f, $"the same {low.Landing:F0} m/s fall from a cliff leaves the same {plain.Current:F1}");
    }

    void TornadoIsNeverSilent()
    {
        var sim = new WeatherSim(_data, "temperate", 8, WeatherKind.Clear);
        var forces = new ForceModule { Wind = sim.WindAt };
        var observer = new TestBody(Specs.PlayerMass, Specs.PlayerDragArea); // stands still at the origin
        forces.Register(observer);
        var warnings = new List<WarningEntry>();
        Tornado t = sim.SpawnTornado(new Vector3(_data.Tornado.SpawnDistance, 0, 0), Vector3.Zero);
        float warned = -1f, heard = -1f, felt = -1f, caught = -1f, seen = -1f;
        for (float time = 0f; time < 120f && caught < 0f; time += Dt)
        {
            sim.Step(Dt, observer.Position);
            Glue.Sync(sim, forces);
            forces.Step(Dt);
            sim.GetWarnings(warnings, observer.Position);
            if (warned < 0f && warnings.Any(w => w.Level == WarningLevel.Danger)) warned = time;
            if (seen < 0f && t.Formation > 0.05f) seen = time;
            if (heard < 0f && t.Roar(observer.Position) > 0.1f) heard = time;
            if (felt < 0f && (forces.AirAt(observer.Position) - sim.WindAt(observer.Position)).Length() > 3f) felt = time;
            if (forces.PhaseOf(observer) == VortexPhase.Caught) caught = time;
        }
        Check(caught > 0f, $"a tornado spawned {_data.Tornado.SpawnDistance:F0} m away reaches a player who stands still ({caught:F0} s)");
        Check(warned >= 0f && caught - warned >= _data.Tornado.FormSeconds,
            $"the visor warns {caught - warned:F0} s before it arrives");
        Check(seen >= 0f && caught - seen >= _data.Tornado.FormSeconds * 0.9f,
            $"the sky shows it: the funnel starts lowering {caught - seen:F0} s before it arrives");
        Check(heard >= 0f && caught - heard >= 10f, $"its roar is audible {caught - heard:F0} s before it arrives");
        Check(felt >= 0f && caught - felt >= 4f, $"the wind swings round into it {caught - felt:F0} s before it arrives");
    }

    /// <summary>
    /// Lightning and burning debris may kill, so neither may ever be a surprise: every strike the
    /// weather throws is visible (glow, beam) for its whole configured lead, and every one that
    /// lands near the player is on the visor for that long too.
    /// </summary>
    void StrikesAreWarnedFirst()
    {
        var warnings = new List<WarningEntry>();
        foreach (var (weather, kind, rules, near) in new[]
        {
            (WeatherKind.Thunderstorm, HazardKind.Lightning, _data.Lightning, 350f),
            (WeatherKind.FireStorm, HazardKind.Debris, _data.Debris, 60f),
        })
        {
            var sim = new WeatherSim(_data, "temperate", 23, weather);
            sim.Snap(weather);
            AddStrikePoints(sim);
            var player = new StrikePoint { Id = 99, Name = "YOU", Position = new Vector3(0, 1.8f, 0) };
            sim.StrikePoints.Add(player);

            var seen = new Dictionary<int, double>();
            var warned = new Dictionary<int, double>();
            int hits = 0, nearHits = 0, late = 0;
            double shortestSeen = double.MaxValue, shortestWarned = double.MaxValue;
            for (int i = 0; i < 60 * 600; i++) // ten minutes of storm around a player at the origin
            {
                sim.Step(Dt, Vector3.Zero);
                foreach (Hazard h in sim.Hazards) seen.TryAdd(h.Id, sim.Time);
                sim.GetWarnings(warnings, Vector3.Zero);
                foreach (WarningEntry w in warnings) if (w.HazardId > 0) warned.TryAdd(w.HazardId, sim.Time);

                foreach (WeatherEvent e in sim.Events)
                {
                    if (e.Kind != WeatherEventKind.LightningStrike && e.Kind != WeatherEventKind.DebrisImpact) continue;
                    hits++;
                    double shown = sim.Time - seen[e.Id];
                    shortestSeen = Math.Min(shortestSeen, shown);
                    if (shown < rules.WarningSeconds - 1e-3) late++;
                    if (new Vector2(e.Position.X, e.Position.Z).Length() >= near) continue;
                    nearHits++;
                    double onVisor = warned.TryGetValue(e.Id, out double at) ? sim.Time - at : -1;
                    shortestWarned = Math.Min(shortestWarned, onVisor);
                    if (onVisor < rules.WarningSeconds - 1e-3) late++;
                }
            }
            string what = kind == HazardKind.Lightning ? "lightning" : "burning debris";
            Check(late == 0 && hits > 20 && nearHits > 10,
                $"every {what} hit is shown and on the visor for its full {rules.WarningSeconds:F1} s lead first " +
                $"({hits} hits, {nearHits} near you; shortest {shortestSeen:F2} s shown, {shortestWarned:F2} s on the visor)");
        }

        // Damage is never reduced: a hit lands in full, and enough of them kill.
        var hp = new Health(100f);
        float dealt = hp.Hit(_data.Lightning.Damage);
        Check(MathF.Abs(dealt - _data.Lightning.Damage) < 1e-3f, $"a lightning hit lands in full ({dealt:F0} of {_data.Lightning.Damage:F0})");
        hp.Set(10f);
        dealt = hp.Hit(_data.Debris.Damage);
        Check(hp.Dead && MathF.Abs(dealt - 10f) < 1e-3f, "and lightning or debris can kill: no last stand, no cap");
        Check(new Health(100f).Hit(DamageRules.FallDamage(10f)) == 0f, "a 10 m/s landing is free");
    }

    void LightningBuildsThenHitsHighest()
    {
        var sim = new WeatherSim(_data, "temperate", 11, WeatherKind.Clear);
        sim.Snap(WeatherKind.Clear);
        var spire = new StrikePoint { Id = 1, Name = "spire", Position = new Vector3(0, 60, 0) };
        var tree = new StrikePoint { Id = 2, Name = "tree", Position = new Vector3(30, 12, 5) };
        var player = new StrikePoint { Id = 3, Name = "you", Position = new Vector3(15, 1.8f, -10) };
        var mast = new StrikePoint { Id = 4, Name = "far mast", Position = new Vector3(160, 95, 0) };
        sim.StrikePoints.AddRange(new[] { spire, tree, player, mast });

        Hazard h = sim.StartStrike(HazardKind.Lightning, new Vector3(25, 0, 0));
        bool glowOnSpire = h.TargetPoint == spire;
        float struckAt = -1f;
        StrikePoint hit = null;
        for (int i = 0; i < 60 * 10 && struckAt < 0f; i++)
        {
            sim.Step(Dt, Vector3.Zero);
            foreach (var e in sim.Events)
                if (e.Kind == WeatherEventKind.LightningStrike) { struckAt = h.Age; hit = e.Target; }
        }
        float buildUp = _data.Lightning.BuildUpSeconds;
        Check(glowOnSpire, "the build-up glow shows on the spire from the start");
        Check(struckAt >= buildUp - 1e-3f && struckAt < buildUp + 2 * Dt, $"it strikes only after the {buildUp:F1} s build-up (at {struckAt:F2} s)");
        Check(hit == spire, $"it hits the highest point in its radius ({hit?.Name ?? "ground"}), not the taller mast outside it");

        player.Position = new Vector3(-150, 22f, 0); // jetpacking alone over open ground
        Hazard h2 = sim.StartStrike(HazardKind.Lightning, new Vector3(-160, 0, 10));
        Check(h2.TargetPoint == player, "a player high in the air over open ground becomes the target");
        player.Position = new Vector3(-150, 1.0f, 0); // drops and crouches
        tree.Position = new Vector3(-170, 9f, 20);
        for (int i = 0; i < 60 * 5; i++) sim.Step(Dt, Vector3.Zero);
        Check(h2.TargetPoint == tree, "getting low hands the strike to a taller tree nearby");
    }

    void ModifiersApply()
    {
        var sim = new WeatherSim(_data, "temperate", 1);
        float baseTemp = sim.Archetype.BaseTemperature;
        bool allMatch = true;
        GD.Print($"      {"weather",-14}{"temp °C",9}{"visibility",12}{"solar",8}{"wind",8}  creatures");
        foreach (WeatherKind kind in Enum.GetValues<WeatherKind>())
        {
            sim.Snap(kind);
            WeatherState s = _data.State(kind);
            Conditions c = sim.Current;
            allMatch &= MathF.Abs(c.Solar - s.Solar) < 1e-4f && MathF.Abs(c.Temperature - (baseTemp + s.Temperature)) < 1e-4f
                && MathF.Abs(c.Visibility - s.Visibility) / s.Visibility < 1e-3f && MathF.Abs(c.WindSpeed - s.WindSpeed) < 1e-3f;
            GD.Print($"      {s.Name,-14}{c.Temperature,9:F0}{c.Visibility,11:F0}m{c.Solar * 100,7:F0}%{c.WindSpeed,6:F0}m/s  {c.CreatureActivity:F2} {s.CreatureHint}");
        }
        Check(allMatch, "each weather sets its own solar output, temperature, visibility and wind");

        Conditions Of(WeatherKind k) { sim.Snap(k); return sim.Current; }
        Conditions clear = Of(WeatherKind.Clear);
        Check(Of(WeatherKind.DustStorm).Solar < 0.15f * clear.Solar && Of(WeatherKind.DustStorm).Visibility < 50f,
            "dust storms cut solar output and visibility to near zero");
        Check(Of(WeatherKind.Blizzard).Temperature < clear.Temperature - 20f && Of(WeatherKind.Blizzard).MoveSpeed < 0.8f,
            "blizzards are bitterly cold and slow you down");
        Check(Of(WeatherKind.FireStorm).Temperature > clear.Temperature + 30f, "fire storms are scorching");
        Check(Of(WeatherKind.Rain).Solar < clear.Solar && Of(WeatherKind.Monsoon).Solar < Of(WeatherKind.Rain).Solar,
            "rain cuts solar output, monsoons more");
        Check(Of(WeatherKind.Fog).CreatureActivity > clear.CreatureActivity && Of(WeatherKind.Hurricane).CreatureActivity < 0.3f,
            "creatures hunt in fog and hide from hurricanes");

        var ice = new WeatherSim(_data, "ice", 1);
        ice.Snap(WeatherKind.Snow);
        sim.Snap(WeatherKind.Snow);
        Check(ice.Current.Temperature < sim.Current.Temperature - 20f, "the same weather is colder on an ice world");
    }

    void TransitionsAreSmooth()
    {
        float worst = 0f;
        string worstWhat = "";
        foreach (Archetype archetype in _data.Archetypes)
        {
            var sim = new WeatherSim(_data, archetype.Id, 77);
            sim.EnsureTimeline(2 * 3600);
            Measure(t => sim.ConditionsAt(t), 2 * 3600, archetype.Id);
        }

        // Forcing new weather every 9 s, even mid-transition, never makes anything jump.
        var forced = new WeatherSim(_data, "temperate", 5);
        var kinds = Enum.GetValues<WeatherKind>();
        Conditions previous = forced.Current;
        for (int i = 1; i < 60 * 600; i++)
        {
            if (i % (60 * 9) == 0) forced.Force(kinds[(i / (60 * 9) * 5) % kinds.Length]);
            forced.Step(Dt, Vector3.Zero);
            Compare(previous, forced.Current, Dt, "forced");
            previous = forced.Current;
        }

        Check(worst <= 1.001f, $"no parameter changes faster than its max rate ({worst * 100:F0}% of the limit at worst: {worstWhat})");
        GD.Print("      limits per second: " + string.Join(", ",
            Enumerable.Range(0, Conditions.Count).Select(i => $"{(Param)i} {_data.MaxRates[i]}")));

        void Measure(Func<double, Conditions> at, double until, string what)
        {
            const double step = 0.05;
            Conditions prev = at(0);
            for (double t = step; t < until; t += step)
            {
                Conditions next = at(t);
                Compare(prev, next, (float)step, what);
                prev = next;
            }
        }

        void Compare(in Conditions a, in Conditions b, float dt, string what)
        {
            for (int p = 0; p < Conditions.Count; p++)
            {
                float rate = MathF.Abs(b[p] - a[p]) / dt / _data.MaxRates[p];
                if (rate > worst)
                {
                    worst = rate;
                    worstWhat = $"{(Param)p} in {what}";
                }
            }
        }
    }

    void TickCost()
    {
        var sim = new WeatherSim(_data, "storm", 21, WeatherKind.Tornado);
        AddStrikePoints(sim);
        for (int i = 0; i < 30; i++) sim.StrikePoints.Add(new StrikePoint { Id = 100 + i, Name = "tree", Position = new Vector3(i * 9 - 130, 10, i * 7 - 100) });
        var forces = new ForceModule { Wind = sim.WindAt };
        var bodies = new List<TestBody>();
        for (int i = 0; i < 32; i++)
        {
            var b = new TestBody(100f + i * 200f, 1f + i * 0.2f) { Position = new Vector3(i * 3 - 48, 0, 0) };
            bodies.Add(b);
            forces.Register(b);
        }
        for (int i = 0; i < 600; i++) sim.Step(Dt, Vector3.Zero); // warm up
        sim.ClearTornadoes();
        sim.SpawnTornado(new Vector3(150, 0, 0), Vector3.Zero).SkipFormation();
        sim.SpawnTornado(new Vector3(-150, 0, 40), Vector3.Zero).SkipFormation();

        const int ticks = 2400; // 40 s: both tornadoes stay on the ground throughout
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++) sim.Step(Dt, Vector3.Zero);
        double weatherUs = clock.Elapsed.TotalMilliseconds * 1000.0 / ticks;
        clock.Restart();
        for (int i = 0; i < ticks; i++)
        {
            Glue.Sync(sim, forces);
            forces.Step(Dt);
        }
        double forceUs = clock.Elapsed.TotalMilliseconds * 1000.0 / ticks;
        GD.Print($"      weather model tick: {weatherUs:F2} µs ({sim.StrikePoints.Count} strike points, {sim.Tornadoes.Count} tornadoes)");
        GD.Print($"      force module tick: {forceUs:F2} µs ({bodies.Count} bodies, {forces.Vortices.Count} vortices)");
        Check(weatherUs < 100 && forceUs < 100, "weather and force ticks each cost well under 0.1 ms");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { sim.Step(Dt, Vector3.Zero); forces.Step(Dt); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 64 * 1024, $"ticking allocates next to nothing ({allocated / 1000.0:F0} bytes per tick)");
    }

    static void AddStrikePoints(WeatherSim sim)
    {
        sim.StrikePoints.Add(new StrikePoint { Id = 1, Name = "spire", Position = new Vector3(-50, 70, -150) });
        sim.StrikePoints.Add(new StrikePoint { Id = 2, Name = "hover vessel", Position = new Vector3(-5, 3, -14) });
        sim.StrikePoints.Add(new StrikePoint { Id = 3, Name = "rover", Position = new Vector3(6, 4, -14) });
    }

    // ---- Scene checks -----------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (_main == null || !_main.IsNodeReady()) return;
        _tick++;
        WeatherDirector weather = _main.Weather;

        switch (_stage)
        {
            case 0: // let everything settle on the ground in calm air
                if (_tick == 1) weather.Sim.Snap(WeatherKind.Clear);
                if (_tick == 90)
                {
                    _hoverStart = _main.Hover.GlobalPosition;
                    _roverStart = _main.Rover.GlobalPosition;
                    weather.Sim.Snap(WeatherKind.Hurricane);
                    Next();
                }
                break;

            case 1: // a hurricane: the moored skiff strains downwind, the rover doesn't budge
                if (_tick == 60 * 6)
                {
                    float hover = Flat(_main.Hover.GlobalPosition - _hoverStart);
                    float rover = Flat(_main.Rover.GlobalPosition - _roverStart);
                    Check(hover > 5f * MathF.Max(rover, 0.2f),
                        $"in the scene, a hurricane pushes the hover vessel {hover:F1} m and the rover {rover:F2} m");
                    _main.ResetVehicles();
                    weather.Sim.Snap(WeatherKind.Clear);
                    Next();
                }
                break;

            case 2: // a tornado right on the hover vessel lifts it on real Jolt physics
                if (_tick == 60)
                {
                    Tornado t = weather.Sim.SpawnTornado(_main.Hover.GlobalPosition + new Vector3(-30, 0, 0), _main.Hover.GlobalPosition);
                    _hoverGround = _main.Hover.GlobalPosition.Y;
                    weather.DebugTouchdown(t);
                }
                if (_tick > 60)
                {
                    _hoverPeak = MathF.Max(_hoverPeak, _main.Hover.GlobalPosition.Y - _hoverGround);
                    _hoverReleased |= weather.Forces.PhaseOf(_main.Hover) == VortexPhase.Released;
                }
                if (_tick == 60 * 25)
                {
                    Check(_hoverPeak > 12f, $"in the scene, a tornado lifts the hover vessel {_hoverPeak:F1} m");
                    Check(_hoverReleased, "and lets it go again");
                    weather.ClearTornadoes();
                    Next();
                }
                break;

            case 3: // every weather: particles stay within the low-end budget
                if (_tick == 1) _particleIndex = 0;
                if (_tick % 3 == 1)
                {
                    var kinds = Enum.GetValues<WeatherKind>();
                    if (_particleIndex > 0)
                        _particleCounts.Add((_data.State(kinds[_particleIndex - 1]).Name, _main.Fx.ActiveParticles()));
                    if (_particleIndex == kinds.Length)
                    {
                        // A tornado storm with two funnels on screen is the worst case.
                        weather.Sim.Snap(WeatherKind.Tornado);
                        weather.Sim.SpawnTornado(new Vector3(200, 0, 0), Vector3.Zero).SkipFormation();
                        weather.Sim.SpawnTornado(new Vector3(-200, 0, 0), Vector3.Zero).SkipFormation();
                        Next();
                        break;
                    }
                    weather.Sim.Snap(kinds[_particleIndex++]);
                }
                break;

            case 4:
                if (_tick == 10)
                {
                    _particleCounts.Add(("Tornado storm, 2 funnels", _main.Fx.ActiveParticles()));
                    GD.Print($"      {"weather",-26}{"particles",10}");
                    foreach (var (name, count) in _particleCounts) GD.Print($"      {name,-26}{count,10}");
                    int worst = _particleCounts.Max(p => p.particles);
                    Check(worst <= WeatherFx.ParticleBudget,
                        $"particles stay within the low-end budget ({worst} at worst, budget {WeatherFx.ParticleBudget})");
                    weather.ClearTornadoes();
                    Next();
                }
                break;

            case 5: // the real player, on Jolt: caught, thrown about, and the landing hurts like any fall
            {
                Player player = _main.Player;
                if (_tick == 1)
                {
                    weather.Sim.Snap(WeatherKind.Clear);
                    player.DebugPlace(Terrain.OnGround(60f, 60f, 0.05f), 0f, 0f); // open ground, away from the pad
                    player.Health.RegenPerSecond = 0f; // no healing mid-flight: what it takes is what you lose
                    player.Health.Set(100f);
                }
                if (_tick == 30)
                {
                    Tornado t = weather.Sim.SpawnTornado(player.GlobalPosition + new Vector3(-3f, 0f, 0f), player.GlobalPosition);
                    t.SkipFormation();
                    _playerGround = player.GlobalPosition.Y;
                }
                if (_tick <= 30) break;
                VortexPhase phase = weather.Forces.PhaseOf(player);
                _playerCaught |= phase == VortexPhase.Caught;
                _playerReleased |= phase == VortexPhase.Released;
                _playerPeak = MathF.Max(_playerPeak, player.GlobalPosition.Y - _playerGround);
                bool landed = _playerReleased && player.IsOnFloor() && _playerPeak > 10f;
                if (landed || _tick > 60 * 30)
                {
                    Check(_playerCaught && _playerReleased && _playerPeak > 15f,
                        $"in the scene, the player is caught, carried up {_playerPeak:F0} m and thrown clear");
                    Check(landed && player.Health.Current < player.Health.Max,
                        $"and lands hurt by the fall like any other: 100 health before, {player.Health.Current:F1} after");
                    player.Health.RegenPerSecond = 2.5f;
                    weather.ClearTornadoes();
                    Next();
                }
                break;
            }

            case 6: // the debug keys, through the real input path
            {
                WeatherSim sim = weather.Sim;
                WeatherKind Now() => sim.SegmentAt(sim.Time).Kind;
                if (_tick == 1) Press(Key.Key5);
                if (_tick == 3)
                {
                    Check(Now() == WeatherKind.DustStorm, "key 5 brings in a dust storm");
                    Press(Key.Key4);
                }
                if (_tick == 5)
                {
                    _keyKind = Now();
                    Press(Key.Key4);
                }
                if (_tick == 7)
                {
                    Check(_keyKind == WeatherKind.Snow && Now() == WeatherKind.Blizzard, "key 4 brings snow, again a blizzard");
                    _tornadoCount = sim.Tornadoes.Count;
                    Press(Key.T);
                }
                if (_tick == 9)
                {
                    Check(sim.Tornadoes.Count == _tornadoCount + 1 && sim.Tornadoes[^1].Homing,
                        $"T starts a tornado {Flat(sim.Tornadoes[^1].Position - _main.Player.GlobalPosition):F0} m away, heading for you");
                    _archetype = sim.Archetype.Id;
                    Press(Key.B);
                }
                if (_tick == 11)
                {
                    Check(sim.Archetype.Id != _archetype, $"B moves on to the next planet ({_archetype} -> {sim.Archetype.Id})");
                    Next();
                }
                break;
            }

            default:
                GD.Print(_failures == 0 ? "\nALL PASSED" : $"\n{_failures} CHECK(S) FAILED");
                GetTree().Quit(_failures == 0 ? 0 : 1);
                break;
        }
    }

    int _particleIndex;
    float _playerGround, _playerPeak;
    bool _playerCaught, _playerReleased;
    WeatherKind _keyKind;
    int _tornadoCount;
    string _archetype;

    /// <summary>Presses and releases a key, as if from the keyboard.</summary>
    static void Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = false });
    }

    static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();

    void Next()
    {
        _stage++;
        _tick = 0;
    }

    // ---- Helpers ----------------------------------------------------------------------

    static void Section(string name) => GD.Print($"\n== {name}");

    void Check(bool ok, string what)
    {
        if (ok) GD.Print($"PASS  {what}");
        else
        {
            _failures++;
            GD.PrintErr($"FAIL  {what}");
        }
    }

    /// <summary>A point mass with a flat ground at y = 0, standing in for a real body.</summary>
    sealed class TestBody : IForceBody
    {
        public TestBody(float mass, float dragArea)
        {
            Mass = mass;
            DragArea = dragArea;
        }

        public float Mass { get; }
        public float DragArea { get; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }

        /// <summary>Vertical speed of the last landing, once it has landed.</summary>
        public float? Landed { get; set; }

        Vector3 _force;

        public void ApplyForce(Vector3 force) => _force += force;
        public void ApplyImpulse(Vector3 impulse) => Velocity += impulse / Mass;

        public void Integrate(float dt, bool gravity)
        {
            Vector3 accel = _force / Mass + (gravity ? Vector3.Down * ForceModule.Gravity : Vector3.Zero);
            _force = Vector3.Zero;
            Velocity += accel * dt;
            Position += Velocity * dt;
            if (gravity && Position.Y < 0f)
            {
                if (Velocity.Y < -1f) Landed = -Velocity.Y;
                Position = new Vector3(Position.X, 0f, Position.Z);
                Velocity = new Vector3(Velocity.X * 0.5f, 0f, Velocity.Z * 0.5f); // ground friction
            }
        }
    }
}
