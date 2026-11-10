using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Planets.Gen;
using Archetype = Planets.Gen.Archetype;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PlanetSurface.WeatherView;
using PlanetSurface.World;
using Weather.Model;

namespace AssetViewer.Providers.Nature;

/// <summary>What a weather asset shows: one of the model's weathers, a strike, a kind of whirl, a front or the ground after it.</summary>
public sealed record WeatherScene(string Kind, WeatherKind Weather, string Archetype, TornadoShape? Shape = null, bool Ground = false)
{
    public const string State = "state", Strike = "strike", Funnel = "funnel", Spouts = "spouts", Devil = "devil", Front = "front", Cover = "cover";
}

/// <summary>
/// The weather prototype's model running on a planet, as the game shows it: storm towers and their rain
/// shafts in the sky rig, the game's own lightning and tornado effects, rain, snow, dust and embers round the
/// camera, and the ground that goes wet, snowy or dusty. World-space, so it does not turn on the turntable.
/// </summary>
public partial class WeatherView : SurfaceView, IWeatherScene
{
    public static WeatherData Data => _data ??= WeatherData.Parse(File.ReadAllText(NatureWorlds.Prototypes + "weather/data/weather.json"));
    static WeatherData _data;

    public WeatherSim Sim { get; private set; }
    public WeatherScene Scene { get; }
    public NoiseTexture2D CloudNoise { get; }
    public Texture2D SoftDot { get; }
    Camera3D _fallback;
    /// <summary>The camera that draws this: the viewer's (a stand-in at the spot when nothing is looking, as in a headless run).</summary>
    public Camera3D Camera => (IsInsideTree() ? GetViewport()?.GetCamera3D() : null) ?? (_fallback ??= MakeFallback());

    Camera3D MakeFallback()
    {
        var c = new Camera3D { Name = "Stand-in" };
        AddChild(c);
        c.Position = Spot + Vector3.Up * 20f;
        return c;
    }
    public Vector3 ToSun => Sky.ToSun;
    public float CloudBaseY => Sky.Air.CloudBase;
    public IReadOnlyList<Hazard> Hazards => Sim?.Hazards ?? (IReadOnlyList<Hazard>)Array.Empty<Hazard>();
    protected override Vector2 Wind => Sim?.Current.Wind ?? Vector2.Zero;

    LightningFx _lightning;
    readonly Dictionary<int, TornadoFx> _tornadoes = new();
    readonly List<Emitter> _emitters = new();
    Emitter _rain, _snow, _dust, _embers;
    readonly Vector4[] _towers = new Vector4[MostTowers];
    readonly float[] _funnels = new float[MostTowers];
    readonly List<(MeshInstance3D node, ShaderMaterial draw)> _shafts = new();
    const int MostTowers = 4;
    float _windTravel, _flicker = 2f, _speed = 1f, _strikeEvery;
    string _animation = "";
    readonly RandomNumberGenerator _rng = new();
    readonly ulong _seed;
    float _holdUntil;
    int _strikeAfter;
    bool _repeat;

    sealed class Emitter
    {
        public GpuParticles3D Node;
        public ParticleProcessMaterial Process;
        public StandardMaterial3D Draw;
        public Vector3 Offset;
    }

    public WeatherView(PlanetWorld world, WeatherScene scene, ulong seed, double time) : base(world, null, AheadOf(scene))
    {
        Scene = scene;
        _seed = seed;
        _rng.Seed = seed * 7919 + 3;
        CloudNoise = new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { Seed = 3, Frequency = 0.012f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin },
        };
        SoftDot = new GradientTexture2D
        {
            Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
            Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.5f), new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 0.35f, 1f } },
        };
        OpenTime = time;
        Stage.StartInside = true;
        Stage.MinDistanceShare = 0.01f;
        FocusFollowsCamera = true;
        // A storm tower is hundreds of metres; the camera drifts round it instead of turning it.
        SettleSeconds = 4.0;
    }

    public override IReadOnlyList<string> Animations => Scene.Kind switch
    {
        WeatherScene.State => StateAnimations(),
        WeatherScene.Strike => new[] { "Frozen strike", "Strike", "Charging", "Storm" },
        WeatherScene.Funnel => new[] { "Mature", "Forming", "Touchdown", "Dissipating" },
        WeatherScene.Spouts => new[] { "Mature", "Forming", "Dissipating" },
        WeatherScene.Devil => new[] { "Mature", "Forming", "Dissipating" },
        WeatherScene.Front => new[] { "Arriving", "Overhead" },
        _ => new[] { "Settled" },
    };

    string[] StateAnimations()
    {
        WeatherState s = Data.State(Scene.Weather);
        return s.Cellular > 0f || s.Lightning > 0f
            ? new[] { "Mature", "Forming", "Dissipating", "Gust front" }
            : new[] { "Mature", "Forming", "Dissipating" };
    }

    public override void PlayAnimation(string name)
    {
        base.PlayAnimation(name);
        _animation = name;
        if (Sim != null) Begin(name);
    }

    public override void SetSpeed(float scale) => _speed = scale;

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        WeatherState s = Data.State(Scene.Weather);
        yield return ("World", Scene.Archetype);
        yield return ("Weather", s.Name);
        if (Sim != null)
        {
            yield return ("Wind", $"{Sim.Current.WindSpeed:F0} m/s");
            yield return ("Visibility", $"{Mathf.Pow(10f, Sim.Current[Param.VisibilityLog]):F0} m");
            yield return ("Storm cells", Sim.Cells.Count.ToString());
            yield return ("Funnels", Sim.Tornadoes.Count.ToString());
        }
        yield return ("Cloud base", $"{CloudBaseY:F0} m");
        yield return ("Scale", Scene.Ground ? "ground level" : "about a kilometre across");
    }

    // ---- The model ---------------------------------------------------------------------------

    protected override void AfterWorld()
    {
        PlanetWorld planet = World;
        WeatherData data = Data;
        var worldWeather = WorldWeather.Roll(data.Worlds, null, Array.Empty<string>(), planet.Spec.Seed);
        Sim = new WeatherSim(data, Scene.Archetype, planet.Spec.Seed + _seed, WeatherKind.Clouds, worldWeather)
        {
            GroundHeight = (x, z) =>
            {
                float ground = planet.GroundHeight(Mathx.WrapX(x, planet.Width), Mathx.Wrap(z, planet.Height));
                return planet.WaterSurfaceAt(new Vector3(x, 0f, z)) is float water && water > ground ? water : ground;
            },
            WorldSize = new Vector2((float)planet.Width, (float)planet.Height),
            Land = LandOf(planet.Map, data.Regions),
            MeanWarmth = System.Linq.Enumerable.Average(planet.Map.Temperature),
        };
        Sim.OpenWater = (x, z) => OpenWater(planet, x, z);
        BuildEmitters();
        _lightning = new LightningFx { Name = "Lightning", Fx = this };
        AddChild(_lightning);
        Begin(string.IsNullOrEmpty(_animation) ? Animations[0] : _animation);
        // The sim's first steps put the air right before the first frame is drawn.
        for (int i = 0; i < 4; i++) Step(0.05f);
    }

    static bool OpenWater(PlanetWorld planet, float x, float z)
    {
        for (int k = 0; k < 5; k++)
        {
            float px = x + (k == 1 ? 60f : k == 2 ? -60f : 0f), pz = z + (k == 3 ? 60f : k == 4 ? -60f : 0f);
            if (planet.WaterSurfaceAt(new Vector3(px, 0f, pz)) is not float water) return false;
            if (water - planet.GroundHeight(Mathx.WrapX(px, planet.Width), Mathx.Wrap(pz, planet.Height)) < 1.5f) return false;
        }
        return true;
    }

    /// <summary>The land and climate at a point as the weather reads it, off the world map (as the game's weather does).</summary>
    public static Func<float, float, Ground> LandOf(WorldMap map, RegionRules rules)
    {
        var forest = new float[256];
        var loose = new float[256];
        foreach (Biome b in Enum.GetValues<Biome>())
        {
            forest[(int)b] = rules.Forest.TryGetValue(b.ToString(), out float f) ? f : 0f;
            loose[(int)b] = rules.Loose.TryGetValue(b.ToString(), out float l) ? l : 0f;
        }
        var mountain = new float[256];
        var rugged = new float[256];
        foreach (Landform l in Enum.GetValues<Landform>())
        {
            mountain[(int)l] = rules.Mountains.Contains(l.ToString()) ? 1f : 0f;
            rugged[(int)l] = LandformInfo.Openness(l) switch { Openness.Open => 0f, Openness.Rolling => 0.5f, _ => 1f };
        }
        return (x, z) =>
        {
            double px = Mathx.WrapX(x, map.WidthMeters), pz = Mathx.Wrap(z, map.HeightMeters);
            int cell = map.CellAt(px, pz);
            byte biome = map.Biome[cell], land = map.Landform[cell];
            return new Ground(map.Sample(map.Elevation, px, pz), map.Sample(map.Slope, px, pz), forest[biome],
                map.Sample(map.Humidity, px, pz), map.Sample(map.Temperature, px, pz), map.Has(cell, CellFlags.Water), loose[biome],
                mountain[land], rugged[land]);
        };
    }

    /// <summary>Sets the sim up for a scene and an animation: the weather snapped or forming, cells standing, funnels down.</summary>
    void Begin(string animation)
    {
        Sim.ClearTornadoes();
        Sim.ClearHazards();
        Sim.ClearCells();
        Sim.ClearFront();
        foreach (TornadoFx t in _tornadoes.Values) t.QueueFree();
        _tornadoes.Clear();
        _strikeEvery = 0f;
        _strikeAfter = 0;
        _repeat = false;
        Vector3 at = At;
        WeatherKind kind = Scene.Weather;
        WeatherState state = Data.State(kind);
        bool cells = state.Cellular > 0f || state.Lightning > 0f;
        switch (Scene.Kind)
        {
            case WeatherScene.State:
                switch (animation)
                {
                    case "Forming": Sim.Snap(WeatherKind.Clear); Sim.Force(kind); break;
                    case "Dissipating": Sim.Snap(kind); Sim.Force(WeatherKind.Clear); StandCells(at, 1f); break;
                    case "Gust front":
                        Sim.Snap(kind);
                        Sim.StartFront(Scene.Weather == WeatherKind.DustStorm ? FrontKind.Dust : FrontKind.Storm, Spot, 1500f);
                        break;
                    default:
                        Sim.Snap(kind);
                        if (cells) StandCells(at, 1f);
                        GroundFor(kind);
                        break;
                }
                if (animation == "Forming" && cells) StandCells(at, 0.1f);
                break;
            case WeatherScene.Strike:
                Sim.Snap(WeatherKind.Thunderstorm);
                StandCells(at, 1f);
                GroundFor(WeatherKind.Thunderstorm);
                _lightning.Held = animation == "Frozen strike";
                _holdUntil = 0f;
                if (animation is "Frozen strike" or "Strike") _strikeAfter = 8;
                else if (animation == "Charging") Sim.StartStrike(HazardKind.Lightning, at);
                else _strikeEvery = 2.2f;
                if (animation == "Strike") _repeat = true;
                break;
            case WeatherScene.Funnel:
                Sim.Snap(WeatherKind.Tornado);
                Sim.ClearTornadoes();
                Sim.ClearCells();
                SpawnFunnel(at, animation);
                break;
            case WeatherScene.Spouts:
                Sim.Snap(state.Rain > 0f ? kind : WeatherKind.Clouds);
                Sim.ClearCells();
                foreach (Tornado t in Sim.SpawnSpouts(at, storm: true)) Prepare(t, animation);
                break;
            case WeatherScene.Devil:
                Sim.Snap(WeatherKind.Clear);
                Prepare(Sim.SpawnDevil(at), animation);
                break;
            case WeatherScene.Front:
                Sim.Snap(kind);
                Sim.StartFront(Scene.Weather == WeatherKind.DustStorm ? FrontKind.Dust : FrontKind.Storm, Spot, animation == "Overhead" ? -200f : 2200f);
                break;
            case WeatherScene.Cover:
                Sim.Snap(WeatherKind.Clouds);
                GroundFor(kind);
                break;
        }
        PlaceSimWeather();
    }

    Vector3 StrikeTarget() => At;

    void PlaceSimWeather() { }

    void StandCells(Vector3 at, float strength)
    {
        StormCell main = Sim.AddCell(at, strength);
        if (!Scene.Ground) Sim.AddCell(at + new Vector3(900, 0, -700), strength * 0.8f);
        if (!Scene.Ground) Sim.AddCell(at + new Vector3(-1100, 0, -400), strength * 0.6f);
    }

    void GroundFor(WeatherKind kind)
    {
        GroundCover g = default;
        switch (kind)
        {
            case WeatherKind.Rain or WeatherKind.Monsoon or WeatherKind.Thunderstorm or WeatherKind.Hurricane or WeatherKind.Tornado: g.Wetness = 1f; break;
            case WeatherKind.Snow or WeatherKind.Blizzard: g.Snow = 1f; break;
            case WeatherKind.DustStorm: g.Dust = 1f; break;
            case WeatherKind.Fog: g.Wetness = 0.6f; break;
        }
        Sim.DebugSetGround(g);
    }

    void SpawnFunnel(Vector3 at, string animation)
    {
        TornadoRules b = Data.Tornado;
        TornadoShapeRules shape = b.Shapes.Find(s => s.Shape == (Scene.Shape ?? TornadoShape.Cone)) ?? b.Shapes[0];
        float speed = Mathf.Lerp(b.SpeedMin, b.SpeedMax, shape.Speed);
        float life = Mathf.Lerp(b.LifeSlow, b.LifeFast, shape.Speed);
        Tornado t = Sim.SpawnTornado(at, at + new Vector3(0, 0, -100), (b.Shaped(shape, speed, life), shape));
        t.Homing = false;
        t.SetHeading(-MathF.PI * 0.5f);
        t.WanderRate = 0.06f;
        Prepare(t, animation);
    }

    /// <summary>Puts a whirl at the stage of its life an animation names.</summary>
    void Prepare(Tornado t, string animation)
    {
        t.Kept = true;
        switch (animation)
        {
            case "Forming": break;
            case "Dissipating":
                t.SkipFormation();
                t.Dissipate();
                for (int i = 0; i < 40; i++) t.Step(0.25f, Spot, Sim.GroundHeight);
                break;
            case "Touchdown":
                t.Step(t.Rules.FormSeconds * 0.92f, Spot, Sim.GroundHeight);
                break;
            default:
                t.SkipFormation();
                t.Step(1f, Spot, Sim.GroundHeight);
                break;
        }
        if (t.Kind != TornadoKind.DustDevil) Sim.Snap(Scene.Kind == WeatherScene.Spouts ? Sim.Dominant : WeatherKind.Tornado);
    }

    // ---- Each frame ---------------------------------------------------------------------------

    float _accum;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (Sim == null) return;
        _accum += (float)delta * _speed;
        int guard = 0;
        while (_accum > 0.0001f && guard++ < 8)
        {
            float dt = MathF.Min(_accum, 0.05f);
            _accum -= dt;
            Step(dt);
        }
        Look((float)delta);
    }

    void Step(float dt)
    {
        Sim.Step(dt, Spot);
        if (_strikeAfter > 0 && --_strikeAfter == 0) { _lightning.Strike(StrikeTarget()); if (_repeat) _strikeAfter = 150; }
        foreach (WeatherEvent e in Sim.Events)
            if (e.Kind == WeatherEventKind.LightningStrike) _lightning.Strike(e.Position);
        if (_strikeEvery > 0f && (_strikeEvery -= dt) <= 0f)
        {
            _strikeEvery = _rng.RandfRange(1.6f, 3.2f);
            _lightning.Strike(At + new Vector3(_rng.RandfRange(-300, 300), 0, _rng.RandfRange(-500, 100)));
        }
        SyncTornadoes();
    }

    protected override AtmosphereState Air()
    {
        if (Sim == null) return base.Air();
        Vector3 eye = Camera?.GlobalPosition ?? Spot;
        Conditions c = AirAt(Sim, eye);
        return Atmosphere.Compute(World.Sky, c, Sky.ToSun.Y);
    }

    /// <summary>The weather the sky over a point shows: out from under a storm the air is clear and the storm stands in plain sight.</summary>
    public static Conditions AirAt(WeatherSim sim, Vector3 p)
    {
        Conditions c = sim.LocalAt(p);
        if (sim.Front is { Kind: FrontKind.Storm } f)
        {
            float clear = f.Clear(p);
            c[Param.CloudCover] = Mathf.Lerp(c.CloudCover, MathF.Min(c.CloudCover, 0.4f), clear);
            c[Param.CloudDarkness] = Mathf.Lerp(c.CloudDarkness, MathF.Min(c.CloudDarkness, 0.2f), clear);
        }
        if (c.Cellular <= 0.01f) return c;
        float open = c.Cellular * (1f - sim.CellCover(p));
        c[Param.VisibilityLog] = Mathf.Lerp(c[Param.VisibilityLog], MathF.Max(c[Param.VisibilityLog], 3.95f), open);
        c[Param.GroundFog] *= 1f - open;
        c[Param.Solar] = Mathf.Lerp(c.Solar, MathF.Max(c.Solar, 0.75f), open);
        c[Param.Cellular] = open;
        return c;
    }

    void SyncTornadoes()
    {
        var alive = new HashSet<int>();
        Vector3 eye = Camera?.GlobalPosition ?? Spot;
        foreach (Tornado t in Sim.Tornadoes)
        {
            alive.Add(t.Id);
            if (!_tornadoes.TryGetValue(t.Id, out TornadoFx fx))
            {
                fx = new TornadoFx { Name = $"Tornado{t.Id}", Fx = this, Rules = t.Rules, Soil = World.Look.Dust };
                AddChild(fx);
                _tornadoes[t.Id] = fx;
            }
            fx.Follow(t);
            fx.GlobalPosition = t.Position;
            fx.ReachTo(t.Kind == TornadoKind.DustDevil ? t.Height : CloudBaseY - (t.Storm ? 150f : 10f) - fx.GlobalPosition.Y);
        }
        var gone = new List<int>();
        foreach (int id in _tornadoes.Keys) if (!alive.Contains(id)) gone.Add(id);
        foreach (int id in gone) { _tornadoes[id].QueueFree(); _tornadoes.Remove(id); }
    }

    void Look(float dt)
    {
        Camera3D cam = Camera;
        if (cam == null) return;
        Vector3 camera = cam.GlobalPosition;
        Conditions c = Sim.LocalAt(camera);
        // Out of sight of the weather's cell, its drops do not fall where the camera is.
        float here = 1f;
        Vector3 wind = Sim.WindAt(camera);
        Drive(_rain, Sim.RainAt(camera) * here, wind + Vector3.Down * 14f, camera);
        float ash = c.Embers * 0.55f;
        float flakes = MathF.Max(c.Snow, ash) * here;
        Drive(_snow, flakes, wind * 0.9f + Vector3.Down * 2.2f, camera);
        float ashShare = flakes > 0.01f ? Mathf.Clamp(ash / (c.Snow + ash + 0.001f), 0f, 1f) : 0f;
        _snow.Draw.AlbedoColor = new Color(1.3f, 1.35f, 1.45f, 1f).Lerp(new Color(0.22f, 0.2f, 0.2f, 0.85f), ashShare);
        Drive(_dust, c.Dust * here, wind * 0.8f, camera);
        Drive(_embers, c.Embers * here, wind * 0.6f + Vector3.Up * 1.8f, camera);
        Color dust = World.Look.Dust;
        _dust.Draw.AlbedoColor = new Color(dust.R * 0.8f, dust.G * 0.75f, dust.B * 0.7f, 0.3f);

        SyncCells(dt, camera, c);
        SyncFront(camera);
        Vector2 w = c.Wind;
        float speed = w.Length();
        Vector2 dir = speed > 0.01f ? w / speed : Vector2.Right;
        _windTravel = (_windTravel + speed * dt) % 8000f;
        GroundCover g = Sim.Ground;
        RenderingServer.GlobalShaderParameterSet("weather_wind", new Vector4(dir.X, dir.Y, speed, _windTravel));
        RenderingServer.GlobalShaderParameterSet("weather_ground", new Vector4(g.Wetness, g.Snow, g.Dust, c.Solar));
        Vector3 flash = _lightning.FlashAt;
        RenderingServer.GlobalShaderParameterSet("weather_flash", new Vector4(flash.X, flash.Y, flash.Z, _lightning.Flash));
    }

    void SyncCells(float dt, Vector3 camera, Conditions c)
    {
        int count = 0;
        foreach (StormCell cell in Sim.Cells)
        {
            if (count >= MostTowers) break;
            float strength = cell.Strength * c.Cellular;
            if (strength < 0.04f) continue;
            Vector3 at = new(cell.Position.X, GroundAt(cell.Position.X, cell.Position.Z), cell.Position.Z);
            _towers[count] = new Vector4(at.X, at.Z, cell.Radius, strength);
            _funnels[count] = cell.Funnel != null ? 1f : 0f;
            if (count >= _shafts.Count) _shafts.Add(AddShaft());
            var (node, draw) = _shafts[count];
            float baseY = at.Y - 10f, height = CloudBaseY + 100f - baseY, reach = cell.Radius * 0.85f;
            Vector2 wind2 = Sim.Current.Wind;
            Vector2 lean = wind2 * 6f; if (lean.Length() > height * 0.3f) lean = lean.Normalized() * height * 0.3f;
            float bound = reach * 1.9f + lean.Length();
            node.Visible = true;
            node.GlobalTransform = new Transform3D(Basis.Identity.Scaled(new Vector3(bound * 1.2f, height * 1.3f, bound * 1.2f)), new Vector3(at.X + lean.X * 0.5f, baseY + height * 0.6f, at.Z + lean.Y * 0.5f));
            draw.SetShaderParameter("axis_base", new Vector3(at.X, baseY, at.Z));
            draw.SetShaderParameter("shaft_height", height);
            draw.SetShaderParameter("reach", reach);
            draw.SetShaderParameter("bound", bound);
            draw.SetShaderParameter("lean", lean);
            draw.SetShaderParameter("drift", wind2 * 0.04f);
            Sky.ShareCloudBase(draw);
            draw.SetShaderParameter("towers", _towers);
            draw.SetShaderParameter("tower_n", Mathf.Min(MostTowers, 4));
            draw.SetShaderParameter("tower_wall", _funnels);
            draw.SetShaderParameter("skirt_amount", Mathf.Clamp((c.Rain - 0.5f) * 2f, 0f, 1f) * strength);
            draw.SetShaderParameter("seed", (cell.Id * 0.6180339f) % 1f);
            float away = new Vector2(camera.X - at.X, camera.Z - at.Z).Length();
            draw.SetShaderParameter("amount", 0.9f * strength * Mathf.Clamp((c.Rain + 0.25f) * 1.2f, 0f, 1f) * Mathf.Clamp((away - reach * 0.9f) / (reach * 0.4f), 0f, 1f));
            draw.SetShaderParameter("to_sun", ToSun);
            draw.SetShaderParameter("sun_glow", new Color(1f, 0.82f, 0.55f).Lerp(new Color(1f, 1f, 1f), Mathf.Clamp(ToSun.Y * 3f, 0f, 1f)));
            draw.SetShaderParameter("sun_amount", Mathf.Clamp(ToSun.Y * 6f + 0.4f, 0f, 1f) * (0.3f + 0.7f * Sky.Daylight));
            draw.SetShaderParameter("tint", new Color(0.3f, 0.33f, 0.38f) * (0.12f + 0.88f * Sky.Daylight));
            count++;
        }
        Sky.SetCells(_towers, count, _funnels);
        for (int i = count; i < _shafts.Count; i++) _shafts[i].node.Visible = false;
        if (count == 0 || c.LightningPerMinute < 0.1f || (_flicker -= dt) > 0f) return;
        _flicker = _rng.RandfRange(2.5f, 9f);
        Vector4 tower = _towers[_rng.RandiRange(0, count - 1)];
        _lightning.CloudFlash(new Vector3(tower.X, CloudBaseY + 120f, tower.Y), _rng.RandfRange(0.25f, 0.6f));
    }

    void SyncFront(Vector3 camera)
    {
        Front f = Sim.Front;
        if (f == null || f.Strength < 0.005f)
        {
            Sky.SetFront(Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero);
            return;
        }
        Vector3 c = f.Centre;
        Sky.SetFront(new Vector4(c.X, c.Z, f.Heading.X, f.Heading.Y), new Vector4(f.Strength, f.Period, f.Spacing, f.Depth),
            new Vector4(f.Shear, f.Bow, f.Bows, f.Bays), new Vector4(f.Kind == FrontKind.Storm ? 1f : 0f, f.Roll ? 1f : 0f, f.Seed, f.Severity));
    }

    (MeshInstance3D, ShaderMaterial) AddShaft()
    {
        var draw = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/rainshaft.gdshader") };
        var node = new MeshInstance3D
        {
            Name = "RainShaft", MaterialOverride = draw, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, CapTop = false, CapBottom = false, RadialSegments = 24, Rings = 1 },
        };
        AddChild(node);
        return (node, draw);
    }

    // ---- Particles (the game's emitters, without the collision picture) -----------------------

    void BuildEmitters()
    {
        _rain = AddEmitter("Rain", 3400, 2.2f, new Vector3(18, 0.5f, 18), new Vector3(0, 16, 0),
            new QuadMesh { Size = new Vector2(0.03f, 0.8f) }, new Color(0.82f, 0.88f, 0.94f, 0.5f), false, Streak());
        _rain.Process.ScaleMin = 0.5f;
        _rain.Process.ScaleMax = 1.35f;
        _rain.Draw.DistanceFadeMinDistance = 1.2f;
        _rain.Draw.DistanceFadeMaxDistance = 5f;
        _snow = AddEmitter("Snow", 3200, 2.5f, new Vector3(16, 7, 16), new Vector3(0, 6, 0),
            new QuadMesh { Size = new Vector2(0.06f, 0.14f) }, new Color(1.3f, 1.35f, 1.45f, 1f), false, null);
        _dust = AddEmitter("Dust", 96, 5f, new Vector3(26, 4, 26), new Vector3(0, 3, 0),
            new QuadMesh { Size = new Vector2(13f, 9f) }, new Color(0.42f, 0.2f, 0.08f, 0.3f), true, PlanetSurface.Fx.Debris.Haze);
        _dust.Process.AngleMax = 360f;
        _dust.Process.ScaleMin = 0.7f;
        _dust.Process.ScaleMax = 1.8f;
        _dust.Draw.DistanceFadeMinDistance = 1.5f;
        _dust.Draw.DistanceFadeMaxDistance = 7f;
        _embers = AddEmitter("Embers", 1400, 2.5f, new Vector3(24, 12, 24), new Vector3(0, 6, 0),
            new QuadMesh { Size = new Vector2(0.06f, 0.32f) }, new Color(4f, 1.7f, 0.5f, 1f), false, SoftDot);
        _embers.Process.Spread = 50f;
    }

    static Texture2D Streak()
    {
        const int W = 8, H = 48;
        Image image = Image.CreateEmpty(W, H, true, Image.Format.Rgba8);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float across = 1f - MathF.Abs((x + 0.5f) / W * 2f - 1f), along = (y + 0.5f) / H;
            float a = across * across * Mathf.Pow(MathF.Sin(along * MathF.PI), 1.4f) * (0.55f + 0.45f * along);
            image.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    Emitter AddEmitter(string name, int amount, float lifetime, Vector3 extents, Vector3 offset, QuadMesh mesh, Color color, bool billboard, Texture2D texture)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = extents,
            Gravity = Vector3.Zero, Direction = Vector3.Down, Spread = 3f, InitialVelocityMin = 10f, InitialVelocityMax = 12f,
            ColorRamp = new GradientTexture1D
            {
                Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 0.12f, 0.8f, 1f } },
            },
        };
        var draw = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true, AlbedoColor = color, AlbedoTexture = texture ?? (billboard ? SoftDot : null),
            BillboardMode = billboard ? BaseMaterial3D.BillboardModeEnum.Particles : BaseMaterial3D.BillboardModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha, DistanceFadeMinDistance = 0.6f, DistanceFadeMaxDistance = billboard ? 2.5f : 4f,
        };
        var node = new GpuParticles3D
        {
            Name = name, Amount = amount, Lifetime = lifetime, Preprocess = lifetime, ProcessMaterial = process, DrawPass1 = mesh,
            MaterialOverride = draw, LocalCoords = false, AmountRatio = 0f, Visible = false,
            VisibilityAabb = new Aabb(new Vector3(-60, -40, -60), new Vector3(120, 80, 120)),
            TransformAlign = billboard ? GpuParticles3D.TransformAlignEnum.Disabled : GpuParticles3D.TransformAlignEnum.ZBillboardYToVelocity,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        var emitter = new Emitter { Node = node, Process = process, Draw = draw, Offset = offset };
        _emitters.Add(emitter);
        return emitter;
    }

    static void Drive(Emitter e, float amount, Vector3 velocity, Vector3 camera)
    {
        e.Node.AmountRatio = Mathf.Clamp(amount, 0f, 1f);
        e.Node.Visible = amount > 0.005f;
        if (!e.Node.Visible) return;
        float speed = velocity.Length();
        e.Process.Direction = speed > 0.01f ? velocity / speed : Vector3.Down;
        e.Process.InitialVelocityMin = speed * 0.9f;
        e.Process.InitialVelocityMax = speed * 1.1f;
        var horizontal = new Vector3(velocity.X, 0, velocity.Z);
        e.Node.GlobalPosition = camera + e.Offset - horizontal * ((float)e.Node.Lifetime * 0.5f);
    }

    /// <summary>How far ahead of the camera the weather stands, m: a storm tower far enough that its rain is not on the camera.</summary>
    static float AheadOf(WeatherScene scene) => scene.Ground ? 0f : scene.Kind switch
    {
        WeatherScene.Funnel or WeatherScene.Spouts => 600f,
        WeatherScene.Devil => 170f,
        WeatherScene.Strike => 600f,
        WeatherScene.Front => 0f,
        _ => 1050f,
    };

    float Ahead => AheadOf(Scene);
    /// <summary>Where the weather happens: ahead of the standing camera (or on it, in ground view).</summary>
    Vector3 At
    {
        get
        {
            float z = Spot.Z - Ahead;
            return new Vector3(Spot.X, GroundAt(Spot.X, z), z);
        }
    }

    /// <summary>The box the viewer frames: the ground the camera stands on and the weather ahead of it. The camera starts inside it, at its back.</summary>
    public override Aabb? FrameBounds
    {
        get
        {
            float ground = World.GroundHeight(Spot.X, Spot.Z);
            if (Scene.Ground) return new Aabb(new Vector3(Spot.X - 18f, ground, Spot.Z - 18f), new Vector3(36f, 30f, 36f));
            float half = MathF.Max(120f, Ahead * 0.75f), top = Scene.Kind == WeatherScene.Devil ? 160f : 520f;
            float radius = new Vector3(half * 2f, top, half * 2f).Length() * 0.5f;
            // The viewer puts the free camera a quarter of the radius behind the middle of the box.
            float centreZ = Spot.Z - radius * 0.25f - Ahead * 0.5f + radius * 0.25f;
            return new Aabb(new Vector3(Spot.X - half, ground, Spot.Z - radius * 0.25f - half), new Vector3(half * 2f, top, half * 2f));
        }
    }
}
