using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Planets.Gen;
using Slice.Surface;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;

namespace Slice.Tests;

/// <summary>
/// Dust storms are places, on real rolled worlds (fresh every run, never a pinned seed), the weather
/// model alone. On dry worlds that can make them: they form over dry loose ground upwind of you and
/// roll in on their wind; outside one the world's own sky, wind and visibility hold, inside it the
/// visibility falls to near nothing; its hazards (gale, solar cut, heat, the depth the fallout reads
/// by, the warning) hold only inside; and none outlives its life, more stand than the rules allow, or
/// costs an allocation to read. Worlds whose weather has no dust storm never get one.
///
/// Run: --headless --path prototypes/slice res://tests/DustStormTest.tscn
/// </summary>
public partial class DustStormTest : Node
{
    int _failures, _passes;
    readonly Random _rng = new();
    WeatherData _data;

    public override void _Ready()
    {
        var clock = Stopwatch.StartNew();
        try { Run(); }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    sealed record World(PlanetSpec Spec, WorldMap Map, Func<float, float, Ground> Land);

    World Roll(Archetype archetype)
    {
        var spec = PlanetSpec.Roll((ulong)_rng.NextInt64(), archetype, _rng.Next(PlanetSpec.VariantCount), 8);
        GD.Print($"world: seed {spec.Seed}, {spec.Archetype} variant {spec.Variant}");
        WorldMap map = PlanetGenerator.Generate(spec);
        return new World(spec, map, SurfaceWeather.LandOf(map, _data.Regions));
    }

    WeatherSim Sim(World w) => new(_data, SurfaceWeather.ArchetypeFor(w.Spec), (ulong)_rng.NextInt64())
    {
        Land = w.Land, WorldSize = new Vector2((float)w.Map.WidthMeters, (float)w.Map.HeightMeters), MeanWarmth = w.Map.Temperature.Average(),
        GroundHeight = (x, z) => MathF.Max(0f, w.Map.Sample(w.Map.Elevation, Mathx.WrapX(x, w.Map.WidthMeters), Mathx.Wrap(z, w.Map.HeightMeters))),
    };

    float Loose(World w, Vector3 p) => Regions.DustGround(_data.Regions, w.Land(p.X, p.Z));

    /// <summary>A point of dry loose ground, or null if the world has hardly any.</summary>
    Vector3? DryPoint(World w)
    {
        for (int k = 0; k < 4000; k++)
        {
            var p = new Vector3((float)(_rng.NextDouble() * w.Map.WidthMeters), 0f, (float)(_rng.NextDouble() * w.Map.HeightMeters));
            if (Loose(w, p) >= _data.DustStorms.FormGround) return p;
        }
        return null;
    }

    void Run()
    {
        _data = WeatherData.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://") + "../weather/data/weather.json"));
        DustStormRules r = _data.DustStorms;
        WeatherState storm = _data.State(WeatherKind.DustStorm);

        // ---- Worlds that make dust storms ----
        int dry = 0, born = 0, bornOff = 0, bornDownwind = 0, probed = 0, maxCount = 0, offPace = 0;
        int badInside = 0, badOutside = 0, warnedInside = 0, warnedBeside = 0, readsAllocated = 0;
        float oldest = 0f;
        var notes = new List<string>();
        for (int tries = 0; dry < 3 && tries < 12; tries++)
        {
            World w = Roll(Archetype.Arid);
            WeatherSim sim = Sim(w);
            if (sim.Share(WeatherKind.DustStorm) <= 0f || DryPoint(w) is not Vector3 observer)
            {
                GD.Print("  (no dust storms or no dry loose ground on this one: another)");
                continue;
            }
            dry++;
            var birth = new Dictionary<int, (Vector3 at, double time)>();
            var done = new HashSet<int>();
            for (int i = 0; i < 2 * 3600; i++)
            {
                sim.Step(1f, observer);
                maxCount = Math.Max(maxCount, sim.DustStorms.Count);
                Segment s = sim.SegmentAt(sim.Time);
                bool calm = Calm(s.Kind) && Calm(s.FromKind) && sim.Front == null; // the world's own weather gives no gale, murk or cut of its own
                foreach (DustStorm d in sim.DustStorms)
                {
                    oldest = MathF.Max(oldest, d.Age);
                    if (!birth.TryGetValue(d.Id, out var b))
                    {
                        birth[d.Id] = (d.Position, sim.Time);
                        born++;
                        if (Loose(w, d.Position) < r.FormGround) bornOff++;
                        if (d.Local(observer, sim.WorldSize).X < 0f) bornDownwind++; // the observer must stand ahead of it: it comes to them
                        continue;
                    }
                    float went = (d.Position - b.at).Length(), should = d.Speed * (float)(sim.Time - b.time);
                    if (MathF.Abs(went - should) > 0.01f * should + 0.5f) offPace++;
                    if (!calm || d.Strength < 0.99f || !done.Add(d.Id)) continue;

                    probed++;
                    Vector3 inside = d.Position;
                    Vector3 outside = d.Position + new Vector3(d.Heading.X, 0f, d.Heading.Y) * (d.HalfLength + 600f);
                    Vector3 beside = d.Position + new Vector3(-d.Heading.Y, 0f, d.Heading.X) * (d.HalfWidth + 400f);
                    if (sim.DustDepth(outside) > 0f || sim.DustDepth(beside) > 0f) { probed--; done.Remove(d.Id); continue; } // another storm there
                    Conditions a = sim.LocalAt(inside), o = sim.LocalAt(outside);
                    Conditions pa = SurfaceWeather.AirAt(sim, inside), po = SurfaceWeather.AirAt(sim, outside); // what solar panels read
                    float windIn = sim.MeanWindAt(inside).Length(), windOut = sim.MeanWindAt(outside).Length();
                    bool inOk = sim.DustDepth(inside) > 0.99f && a.Visibility < 60f && a.Dust > 0.8f && windIn > 0.6f * storm.WindSpeed
                        && pa.Solar <= storm.Solar + 0.02f && a.Exposure > 0.8f * storm.Exposure;
                    bool outOk = o.Visibility > 5000f && o.Dust < 0.05f && windOut < 0.5f * storm.WindSpeed && po.Solar >= 0.6f && o.Exposure < 0.05f;
                    if (!inOk) { badInside++; notes.Add($"inside: vis {a.Visibility:F0} m, dust {a.Dust:F2}, wind {windIn:F1}, solar {pa.Solar:F2}, exposure {a.Exposure:F2}"); }
                    if (!outOk) { badOutside++; notes.Add($"outside: vis {o.Visibility:F0} m, dust {o.Dust:F2}, wind {windOut:F1}, solar {po.Solar:F2}, exposure {o.Exposure:F2}"); }
                    var warnings = new List<WarningEntry>();
                    sim.GetWarnings(warnings, inside);
                    if (warnings.Any(e => e.Sign == "dust_storm")) warnedInside++;
                    sim.GetWarnings(warnings, beside);
                    if (warnings.Any(e => e.Sign == "dust_storm" && (e.From - d.Position)?.Length() < 1f)) warnedBeside++;
                    // Reading the weather where a storm stands costs no allocation: the bridge and the panels read it every tick.
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int k = 0; k < 200; k++) { sim.LocalAt(inside); sim.WindAt(outside); sim.DustDepth(beside); }
                    if (GC.GetAllocatedBytesForCurrentThread() != before) readsAllocated++;
                }
            }
        }
        Check(dry >= 3, $"three dry worlds that make dust storms rolled ({dry})");
        Check(born >= dry, $"dust storms formed on them over two hours each ({born})");
        Check(bornOff == 0, $"every one formed over dry loose ground ({bornOff} off it)");
        Check(bornDownwind == 0, $"every one formed upwind of the observer, so its wall comes to them ({bornDownwind} downwind)");
        Check(offPace == 0, $"each rolls on at its own speed the way its wind blows, no drift ({offPace} samples off pace)");
        Check(probed >= dry, $"grown storms probed in and out while the world's own weather was fair ({probed})");
        Check(badInside == 0, $"inside one: depth 1, visibility under 60 m, dust, its gale, solar cut to its own, heat ({badInside} not){Note(notes, "inside")}");
        Check(badOutside == 0, $"600 m outside it the world's own: visibility over 5 km, no dust, no gale, solar panels at 60% or more, no heat ({badOutside} not){Note(notes, "outside")}");
        Check(warnedInside == probed, $"the visor's dust storm sign inside every one ({warnedInside} of {probed})");
        Check(warnedBeside == 0, $"and none for a storm 400 m to one side, out of its path ({warnedBeside})");
        Check(readsAllocated == 0, $"reading the weather by a storm allocates nothing ({readsAllocated} probes did)");
        Check(maxCount <= r.MaxStorms, $"never more than {r.MaxStorms} at once ({maxCount})");
        Check(oldest <= r.LifeMax + r.FadeSeconds + 1f, $"none outlives its life and its fading ({oldest:F0} s oldest, at most {r.LifeMax + r.FadeSeconds:F0})");

        // A jump straight into a dust storm over dry ground stands one over the observer; a jump out of it leaves none.
        {
            World w = Roll(Archetype.Arid);
            WeatherSim sim = Sim(w);
            if (DryPoint(w) is Vector3 p)
            {
                sim.Step(1f, p);
                sim.Snap(WeatherKind.DustStorm);
                float depth = sim.DustDepth(p), visIn = sim.LocalAt(p).Visibility;
                sim.Snap(WeatherKind.Clear);
                float visOut = sim.LocalAt(p).Visibility;
                Check(depth > 0.99f && visIn < 60f && sim.DustStorms.Count == 0 && visOut > 5000f,
                    $"a jump into a dust storm stands one over you (depth {depth:F2}, visibility {visIn:F0} m); a jump out of it leaves none ({sim.DustStorms.Count}, visibility {visOut:F0} m)");
            }
        }

        // ---- Worlds whose weather never has a dust storm ----
        int forbidden = 0, wrongly = 0;
        foreach (Archetype kind in new[] { Archetype.Temperate, Archetype.Ice, Archetype.Lava, Archetype.Alien })
        {
            World w = Roll(kind);
            WeatherSim sim = Sim(w);
            if (sim.Share(WeatherKind.DustStorm) > 0f) continue;
            forbidden++;
            var at = new Vector3((float)(_rng.NextDouble() * w.Map.WidthMeters), 0f, (float)(_rng.NextDouble() * w.Map.HeightMeters));
            for (int i = 0; i < 3600; i++)
            {
                sim.Step(2f, at);
                if (sim.DustStorms.Count > 0 || sim.LocalAt(at).Dust > 0.3f) { wrongly++; break; }
            }
        }
        Check(forbidden >= 2 && wrongly == 0, $"worlds whose weather has no dust storm never get one over two hours ({forbidden} such worlds, {wrongly} did)");
    }

    static bool Calm(WeatherKind k) => k is WeatherKind.Clear or WeatherKind.Clouds or WeatherKind.DustStorm;

    static string Note(List<string> notes, string which)
    {
        string first = notes.FirstOrDefault(n => n.StartsWith(which));
        return first == null ? "" : $"; e.g. {first}";
    }
}
