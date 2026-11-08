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
/// Regional weather on real rolled worlds, the weather model alone (no game): fresh worlds every
/// run, never a pinned seed. On each, storms are run over several stretches of its land, and every
/// hazard is checked against the ground it forms and stands on: tornadoes only over broad open warm
/// humid land and never over high mountains, waterspouts over open water, dust devils over dry loose
/// ground in clear air, blizzards on cold or high ground, fog in damp low ground. Then a front is
/// followed across the map from one region into the next.
///
/// Run: --headless --fixed-fps 60 --quit-after 60000 --path prototypes/slice res://tests/RegionTest.tscn
/// </summary>
public partial class RegionTest : Node
{
    int _failures, _passes;
    readonly Random _rng = new();
    WeatherData _data;
    RegionRules _rules;

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

    /// <summary>One rolled world, ready for the weather: its map, and the sim's view of its land.</summary>
    sealed class World
    {
        public PlanetSpec Spec;
        public WorldMap Map;
        public Func<float, float, Ground> Land;
        public Func<float, float, bool> Sea;
    }

    World Roll(Archetype archetype)
    {
        var spec = PlanetSpec.Roll((ulong)_rng.NextInt64(), archetype, _rng.Next(PlanetSpec.VariantCount), 8);
        WorldMap map = PlanetGenerator.Generate(spec);
        bool Water(float x, float z) => map.Has(map.CellAt(Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(z, map.HeightMeters)), CellFlags.Water);
        return new World
        {
            Spec = spec, Map = map, Land = SurfaceWeather.LandOf(map, _rules),
            Sea = (x, z) => Water(x, z) && Water(x + 60f, z) && Water(x - 60f, z) && Water(x, z + 60f) && Water(x, z - 60f),
        };
    }

    WeatherSim Sim(World w, WeatherKind start)
    {
        var sim = new WeatherSim(_data, SurfaceWeather.ArchetypeFor(w.Spec), (ulong)_rng.NextInt64(), start)
        {
            Land = w.Land, OpenWater = w.Sea, WorldSize = new Vector2((float)w.Map.WidthMeters, (float)w.Map.HeightMeters),
            MeanWarmth = w.Map.Temperature.Average(),
            GroundHeight = (x, z) => MathF.Max(0f, w.Map.Sample(w.Map.Elevation, Mathx.WrapX(x, w.Map.WidthMeters), Mathx.Wrap(z, w.Map.HeightMeters))),
        };
        return sim;
    }

    Vector3 Anywhere(World w) => new((float)(_rng.NextDouble() * w.Map.WidthMeters), 0f, (float)(_rng.NextDouble() * w.Map.HeightMeters));

    void Run()
    {
        _data = WeatherData.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://") + "../weather/data/weather.json"));
        _rules = _data.Regions;
        Archetype[] kinds = { Archetype.Temperate, Archetype.Temperate, Archetype.Alien, Archetype.Arid, Archetype.Temperate, Archetype.Arid, Archetype.Ice, Archetype.Temperate, Archetype.Temperate, Archetype.Alien };
        int tornadoes = 0, onMountain = 0, badSpawn = 0, nearPlayer = 0, spouts = 0, spoutsOffWater = 0, devils = 0, badDevils = 0;
        int ropedOut = 0, wanderedOn = 0, worlds = 0, mountainWorlds = 0, mountainPoints = 0, mountainOpen = 0;
        float closest = float.MaxValue;
        var survey = new List<string>();
        foreach (Archetype kind in kinds)
        {
            World w = Roll(kind);
            worlds++;
            bool mountains = false;
            Vector3 peak = Vector3.Zero;
            for (int k = 0; k < 3000; k++)
            {
                Vector3 p = Anywhere(w);
                Ground m = w.Land(p.X, p.Z);
                if (m.Height <= _rules.MountainHeight && m.Mountain < 0.5f) continue;
                mountains = true;
                peak = p;
                mountainPoints++;
                if (Regions.TornadoGround(_rules, w.Land, p) > 0f) mountainOpen++;
            }
            if (mountains) mountainWorlds++;
            int before = tornadoes;
            // Storms over four stretches of its land, a quarter of an hour each.
            for (int stretch = 0; stretch < 4; stretch++)
            {
                WeatherSim sim = Sim(w, WeatherKind.Tornado);
                sim.FrontsOff = true;
                Vector3 observer = mountains && stretch < 2 ? peak : Anywhere(w); // storms over the mountains themselves too
                var seen = new HashSet<int>();
                for (int i = 0; i < 900 * 2; i++)
                {
                    if (i % 240 == 0) sim.Snap(i % 480 == 0 ? WeatherKind.Tornado : WeatherKind.Thunderstorm);
                    sim.Step(0.5f, observer);
                    foreach (Tornado t in sim.Tornadoes)
                    {
                        Ground g = w.Land(t.Position.X, t.Position.Z);
                        bool fresh = seen.Add(t.Id);
                        if (t.Kind == TornadoKind.Waterspout)
                        {
                            if (fresh) { spouts++; if (!w.Sea(t.Position.X, t.Position.Z)) spoutsOffWater++; }
                            continue;
                        }
                        if (t.Kind != TornadoKind.Tornado) continue;
                        if (fresh)
                        {
                            tornadoes++;
                            float d = new Vector2(t.Position.X - observer.X, t.Position.Z - observer.Z).Length();
                            closest = MathF.Min(closest, d);
                            if (d < _rules.Tornado.MinDistance) nearPlayer++;
                            if (Regions.TornadoGround(_rules, w.Land, t.Position) <= 0f) badSpawn++;
                        }
                        // Standing at full strength on a mountain or a steep slope: never. It ropes out as it wanders on.
                        if (t.Touchdown && !t.Dissipating && Regions.Barred(_rules, g)) onMountain++;
                        if (t.Touchdown && t.Dissipating && t.Age < t.Lifetime - 1f && (Regions.Barred(_rules, g) || g.Forest >= _rules.Tornado.DenseForest)) ropedOut++;
                    }
                }
                foreach (Tornado t in sim.Tornadoes) if (t.Kind == TornadoKind.Tornado && Regions.High(_rules, w.Land(t.Position.X, t.Position.Z)) > 0.3f) wanderedOn++;
            }
            // Clear hot days for dust devils.
            {
                WeatherSim sim = Sim(w, WeatherKind.Clear);
                Vector3 observer = Anywhere(w);
                var seen = new HashSet<int>();
                for (int i = 0; i < 600 * 2; i++)
                {
                    if (i % 200 == 0) { sim.Snap(WeatherKind.Clear); observer = Anywhere(w); }
                    sim.Step(0.5f, observer);
                    foreach (Tornado t in sim.Tornadoes)
                    {
                        if (t.Kind != TornadoKind.DustDevil || !seen.Add(t.Id)) continue;
                        devils++;
                        Ground g = w.Land(t.Position.X, t.Position.Z);
                        if (Regions.DustGround(_rules, g) < 0.6f || sim.CellCover(t.Position) > 0.02f || sim.Current.CloudCover > _rules.Dust.DevilCloud
                            || new Vector2(t.Position.X - observer.X, t.Position.Z - observer.Z).Length() < 100f) badDevils++;
                    }
                }
            }
            float top = w.Map.Elevation.Max(), steep = w.Map.Slope.Count(s => s > _rules.SteepSlope) / (float)w.Map.Count;
            survey.Add($"{w.Spec.Archetype} {w.Spec.Seed:X}: {tornadoes - before} tornadoes; highest {top:F0} m{(mountains ? ", has mountains" : "")}, {steep:P0} steep");
        }
        foreach (string s in survey) GD.Print("      " + s);
        Check(tornadoes > 10, $"tornadoes form on these worlds ({tornadoes} over {worlds} worlds, {mountainWorlds} with high mountains)");
        Check(badSpawn == 0, $"every one forms over broad open flat or rolling warm humid land ({badSpawn} did not)");
        Check(mountainOpen == 0, $"no point of high mountains is ground a tornado may form over ({mountainPoints} such points sampled on {mountainWorlds} worlds, {mountainOpen} allowed)");
        Check(onMountain == 0, $"none ever stands at strength over high mountains or steep ground ({onMountain} tornado-ticks did)");
        Check(nearPlayer == 0, $"none forms on top of the player (closest {closest:F0} m, the rule {_rules.Tornado.MinDistance:F0} m)");
        Check(spoutsOffWater == 0, $"waterspouts form only over open water ({spouts} formed, {spoutsOffWater} off it)");
        Check(badDevils == 0, $"dust devils form only over dry loose ground in clear air, never on top of the player ({devils} formed, {badDevils} wrong)");
        GD.Print($"      {ropedOut} tornado-ticks roping out on high or wooded ground, {wanderedOn} left standing near mountains at the end");

        Blizzards();
        Fog();
        FrontsCross();
        CloudContours();
    }

    /// <summary>A blizzard falls as snow on cold or high ground; on warm low ground the same weather is rain and wind.</summary>
    void Blizzards()
    {
        int coldSnow = 0, cold = 0, warmRain = 0, warm = 0, warmSnow = 0;
        for (int n = 0; n < 4; n++)
        {
            World w = Roll(n % 2 == 0 ? Archetype.Temperate : Archetype.Alien);
            WeatherSim sim = Sim(w, WeatherKind.Blizzard);
            sim.Snap(WeatherKind.Blizzard);
            sim.Step(0.1f, Anywhere(w));
            for (int i = 0; i < 300; i++)
            {
                Vector3 p = Anywhere(w);
                Ground g = w.Land(p.X, p.Z);
                if (g.Water) continue;
                Conditions c = sim.LocalAt(p);
                float coldness = Regions.ColdGround(_rules, g);
                if (coldness > 0.9f) { cold++; if (c.Snow > 0.3f) coldSnow++; }
                else if (coldness < 0.05f && g.Warmth > 0.5f && g.Height < 200f)
                {
                    warm++;
                    if (c.Rain > 0.2f && c.Snow < 0.05f) warmRain++;
                    if (c.Snow > 0.3f) warmSnow++;
                }
            }
        }
        Check(cold == 0 || coldSnow > cold * 0.9f, $"a blizzard blows snow on cold or high ground ({coldSnow} of {cold} such points)");
        Check(warm > 10 && warmSnow == 0 && warmRain > warm * 0.9f, $"and is rain and wind on warm low ground ({warmRain} of {warm} rain, {warmSnow} snow)");
    }

    /// <summary>Fog pools in damp low ground and valleys; high dry ground under the same weather stays clear of it.</summary>
    void Fog()
    {
        int damp = 0, dampFog = 0, dry = 0, dryFog = 0;
        for (int n = 0; n < 4; n++)
        {
            World w = Roll(n % 2 == 0 ? Archetype.Temperate : Archetype.Arid);
            WeatherSim sim = Sim(w, WeatherKind.Fog);
            sim.Snap(WeatherKind.Fog);
            sim.Step(0.1f, Anywhere(w));
            for (int i = 0; i < 400; i++)
            {
                Vector3 p = Anywhere(w);
                Ground g = w.Land(p.X, p.Z);
                if (g.Water) continue;
                Conditions c = sim.LocalAt(p);
                if (g.Humidity > 0.7f && g.Height < 40f) { damp++; if (c.GroundFog > 0.5f) dampFog++; }
                if (g.Humidity < 0.3f) { dry++; if (c.GroundFog > 0.5f) dryFog++; }
            }
        }
        Check(damp == 0 || dampFog > damp * 0.8f, $"fog pools in damp low ground ({dampFog} of {damp} such points)");
        Check(dry > 10 && dryFog < dry * 0.1f, $"and not on dry ground ({dryFog} of {dry} such points)");
    }

    /// <summary>
    /// A storm front forms on its own and is followed for twenty minutes: it advances across the map
    /// over ground of different kinds, and at a point ahead of it the weather turns from the fair one to
    /// the storm's as its edge comes over.
    /// </summary>
    void FrontsCross()
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            World w = Roll(Archetype.Temperate);
            var sim = new WeatherSim(_data, "storm", (ulong)_rng.NextInt64(), WeatherKind.Clear)
            {
                Land = w.Land, OpenWater = w.Sea, WorldSize = new Vector2((float)w.Map.WidthMeters, (float)w.Map.HeightMeters),
            };
            Vector3 observer = Anywhere(w);
            for (int i = 0; i < 3 * 3600 && (sim.Front == null || sim.Front.Kind != FrontKind.Storm || sim.Front.Ahead(observer) < 3000f); i++) sim.Step(1f, observer);
            Front f = sim.Front;
            if (f == null || f.Ahead(observer) < 3000f || sim.FrontWeather == null) continue;
            WeatherKind before = sim.FrontWeather.FromKind, behind = sim.FrontWeather.Kind;
            if (_data.State(before).Lightning > 0.2f) continue; // a storm already here: nothing to tell apart
            float along = f.Along(observer);
            Vector3 start = f.EdgeAt(along, observer);
            var crossed = new HashSet<string>();
            float boltsAhead = sim.LocalAt(observer).LightningPerMinute, boltsUnder = 0f;
            for (int i = 0; i < 1200 && sim.Front != null; i++)
            {
                sim.Step(1f, observer);
                if (i % 30 != 0) continue;
                Vector3 edge = sim.Front.EdgeAt(along, observer);
                crossed.Add(Kind(w.Land(edge.X, edge.Z)));
                if (sim.Front.Ahead(observer) < -1500f)
                {
                    boltsUnder = MathF.Max(boltsUnder, sim.LocalAt(observer).LightningPerMinute);
                }
            }
            if (sim.Front == null) continue;
            float moved = new Vector2(sim.Front.EdgeAt(along, observer).X - start.X, sim.Front.EdgeAt(along, observer).Z - start.Z).Length();
            Check(moved > 5000f && crossed.Count >= 2,
                $"a front advances {moved / 1000f:F1} km across the map in 20 minutes, over {crossed.Count} kinds of ground ({string.Join(", ", crossed)})");
            Check(boltsAhead < 0.3f && boltsUnder > 1f,
                $"the weather at a point turns as its edge comes over: {before} ahead of it ({boltsAhead:F1} strikes a minute), {behind} under it ({boltsUnder:F1} a minute)");
            return;
        }
        Check(false, "a storm front formed on its own on a storm world within three hours (12 worlds tried, the weather before it fair)");
    }

    /// <summary>
    /// The noise the sky cuts its clouds from, at every cover from thin to full: where its edges run (the
    /// contour at that cover), they run every way alike, never mostly along the texture's axes or in straight
    /// lines. Perlin's lattice failed this: square clouds as thin cover first broke through.
    /// </summary>
    void CloudContours()
    {
        float was = AxisShare(new FastNoiseLite { Seed = 11, Frequency = 0.008f, FractalOctaves = 5, FractalGain = 0.5f, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin }, out _);
        float worst = AxisShare(PlanetSurface.Skies.SkyRig.CloudNoiseSource(), out float worstCover);
        // Edges running every way alike put a ninth of themselves within 5° of an axis.
        Check(worst < 0.16f, $"the clouds' edges run every way at every cover, never along the axes: at worst {worst:P0} of edge within 5° of an axis (at cover {worstCover:F2}; a ninth, 11%, is no lean at all; the Perlin noise they were cut from before: {was:P0})");
    }

    /// <summary>The largest share of a noise's contour, over covers from thin to full, that runs within 5° of an axis.</summary>
    static float AxisShare(FastNoiseLite noise, out float worstCover)
    {
        // The texture as the sky gets it: tiled seamless, its edges blended (a blend that runs along the axes).
        const int size = 512;
        Image image = noise.GetSeamlessImage(size, size, false, false, 0.1f, true);
        var v = new float[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) v[x, y] = image.GetPixel(x, y).R;
        float worst = 0f;
        worstCover = 0f;
        foreach (float cover in new[] { 0.15f, 0.3f, 0.45f, 0.6f, 0.75f })
        {
            // The contour where this much of the sky is cloud: the value with that share above it.
            var all = new List<float>(size * size);
            foreach (float f in v) all.Add(f);
            all.Sort();
            float threshold = all[(int)((1f - cover) * (all.Count - 1))];
            int edges = 0, axis = 0;
            for (int y = 1; y < size - 1; y++)
                for (int x = 1; x < size - 1; x++)
                {
                    if ((v[x, y] - threshold) * (v[x + 1, y] - threshold) > 0f && (v[x, y] - threshold) * (v[x, y + 1] - threshold) > 0f) continue;
                    float gx = v[x + 1, y] - v[x - 1, y], gy = v[x, y + 1] - v[x, y - 1];
                    float angle = MathF.Abs(MathF.Atan2(gy, gx)) % (MathF.PI / 2f);
                    edges++;
                    if (angle < 0.0873f || angle > MathF.PI / 2f - 0.0873f) axis++; // within 5° of an axis
                }
            float share = axis / (float)Math.Max(1, edges);
            if (share > worst) (worst, worstCover) = (share, cover);
        }
        return worst;
    }

    /// <summary>A rough name for a stretch of land, to tell regions apart.</summary>
    string Kind(in Ground g) =>
        g.Water ? "water" : Regions.High(_rules, g) > 0.5f ? "mountains" : g.Forest > 0.5f ? "forest" : g.Slope > 0.15f ? "hills" : g.Humidity < 0.35f ? "dry plain" : "plain";
}
