using System;
using System.Collections.Generic;
using Godot;
using Weather.Model;

namespace Weather;

/// <summary>
/// Everything you see of the weather: sky, fog, light, precipitation particles, lightning,
/// tornado funnels and burning debris. It only reads the model; nothing here affects play.
///
/// The Mobile renderer has no volumetric fog, so depth comes from exponential and height fog,
/// a sky shader with cloud layers, and a few camera-following particle boxes kept within a
/// low-end budget.
/// </summary>
public partial class WeatherFx : Node3D
{
    /// <summary>The most particles any weather may have alive at once.</summary>
    public const int ParticleBudget = 5000;

    /// <summary>The direction toward the sun (west-north-west, low): long shadows and side light.</summary>
    public static readonly Vector3 ToSun = new Vector3(-0.85f, 0.42f, -0.32f).Normalized();

    public WeatherDirector Director;
    public Camera3D Camera;

    public Godot.Environment Environment { get; private set; }
    public NoiseTexture2D CloudNoise { get; private set; }
    public Texture2D SoftDot { get; private set; }

    ShaderMaterial _sky;
    DirectionalLight3D _sun, _flashLight;
    Look _look;
    bool _lookReady;
    Vector2 _cloudDrift;
    float _windTravel;

    readonly List<Emitter> _emitters = new();
    Emitter _rain, _snow, _grit, _puffs, _embers;
    LightningFx _lightning;
    DebrisFx _debris;
    readonly Dictionary<int, TornadoFx> _tornadoes = new();

    /// <summary>A camera-following particle box whose amount follows one weather parameter.</summary>
    sealed class Emitter
    {
        public GpuParticles3D Node;
        public ParticleProcessMaterial Process;
        public StandardMaterial3D Draw;
        public Vector3 Offset;
    }

    public WeatherFx()
    {
        // Built in the constructor so other nodes can share the textures before _Ready.
        CloudNoise = new NoiseTexture2D
        {
            Width = 512, Height = 512, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { Seed = 3, Frequency = 0.008f, FractalOctaves = 5, FractalGain = 0.55f, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin },
        };
        SoftDot = new GradientTexture2D
        {
            Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
            Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.5f), new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 0.35f, 1f } },
        };
    }

    public override void _Ready()
    {
        BuildEnvironment();
        BuildParticles();
        _lightning = new LightningFx { Name = "Lightning", Fx = this };
        AddChild(_lightning);
        _debris = new DebrisFx { Name = "Debris", Fx = this };
        AddChild(_debris);
    }

    void BuildEnvironment()
    {
        _sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") };
        _sky.SetShaderParameter("cloud_noise", CloudNoise);
        Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size32, ProcessMode = Sky.ProcessModeEnum.Incremental },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapWhite = 6f,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            GlowEnabled = true,
            GlowIntensity = 0.7f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 1.1f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            AdjustmentEnabled = true,
            AdjustmentContrast = 1.08f,
        };
        AddChild(new WorldEnvironment { Environment = Environment });

        _sun = new DirectionalLight3D
        {
            Name = "Sun", ShadowEnabled = true, DirectionalShadowMaxDistance = 180f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
        };
        AddChild(_sun);
        _sun.LookAtFromPosition(Vector3.Zero, -ToSun, Vector3.Up);

        _flashLight = new DirectionalLight3D { Name = "FlashLight", LightColor = new Color("d6ccff"), LightEnergy = 0f, ShadowEnabled = false };
        AddChild(_flashLight);
        _flashLight.LookAtFromPosition(Vector3.Zero, new Vector3(0.2f, -1f, 0.1f), Vector3.Forward);
    }

    // ---- Particles --------------------------------------------------------------------

    void BuildParticles()
    {
        _rain = AddEmitter("Rain", 3200, 1.1f, new Vector3(22, 10, 22), new Vector3(0, 11, 0),
            new QuadMesh { Size = new Vector2(0.018f, 0.9f) }, new Color(0.78f, 0.85f, 0.9f, 0.32f), billboard: false, texture: null);
        _snow = AddEmitter("Snow", 2600, 2.5f, new Vector3(16, 7, 16), new Vector3(0, 6, 0),
            new QuadMesh { Size = new Vector2(0.08f, 0.42f) }, new Color(1, 1, 1, 0.95f), billboard: false, texture: SoftDot);
        _snow.Draw.DistanceFadeMinDistance = 0.4f;
        _snow.Draw.DistanceFadeMaxDistance = 2f;
        _grit = AddEmitter("DustGrit", 1600, 1.2f, new Vector3(20, 5, 20), new Vector3(0, 3, 0),
            new QuadMesh { Size = new Vector2(0.035f, 0.8f) }, new Color(0.5f, 0.27f, 0.12f, 0.55f), billboard: false, texture: null);
        _puffs = AddEmitter("DustPuffs", 260, 4f, new Vector3(24, 6, 24), new Vector3(0, 4, 0),
            new QuadMesh { Size = new Vector2(9f, 9f) }, new Color(0.42f, 0.2f, 0.08f, 0.42f), billboard: true, texture: SoftDot);
        _embers = AddEmitter("Embers", 1400, 2.5f, new Vector3(24, 12, 24), new Vector3(0, 6, 0),
            new QuadMesh { Size = new Vector2(0.06f, 0.32f) }, new Color(1f, 0.55f, 0.15f, 1f), billboard: false, texture: SoftDot);

        // Embers glow: unshaded and over-bright, so they feed the bloom pass.
        _embers.Draw.AlbedoColor = new Color(4f, 1.7f, 0.5f, 1f);
        _puffs.Process.AngleMin = 0f;
        _puffs.Process.AngleMax = 360f;
        _puffs.Process.ScaleMin = 0.6f;
        _puffs.Process.ScaleMax = 1.7f;
        _snow.Process.ScaleMin = 0.6f;
        _snow.Process.ScaleMax = 1.4f;
        _embers.Process.ScaleMin = 0.4f;
        _embers.Process.ScaleMax = 1.3f;
        _embers.Process.Spread = 50f;
        _snow.Process.Spread = 18f;
        _puffs.Process.Spread = 15f;

        var flicker = new Gradient
        {
            Colors = new[] { new Color(1, 1, 0.8f, 0), new Color(1, 0.9f, 0.6f, 1), new Color(1, 0.45f, 0.15f, 1), new Color(0.6f, 0.1f, 0.05f, 0) },
            Offsets = new[] { 0f, 0.1f, 0.6f, 1f },
        };
        _embers.Process.ColorRamp = new GradientTexture1D { Gradient = flicker };
    }

    Emitter AddEmitter(string name, int amount, float lifetime, Vector3 extents, Vector3 offset, QuadMesh mesh, Color color, bool billboard, Texture2D texture)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = extents,
            Gravity = Vector3.Zero,
            Direction = Vector3.Down,
            Spread = 3f,
            InitialVelocityMin = 10f,
            InitialVelocityMax = 12f,
            ColorRamp = new GradientTexture1D
            {
                Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 0.12f, 0.8f, 1f } },
            },
        };
        var draw = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = color,
            AlbedoTexture = texture,
            BillboardMode = billboard ? BaseMaterial3D.BillboardModeEnum.Particles : BaseMaterial3D.BillboardModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            // Streaks right in front of the lens would be huge: fade them out.
            DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
            DistanceFadeMinDistance = 0.6f,
            DistanceFadeMaxDistance = billboard ? 2.5f : 4f,
        };
        var node = new GpuParticles3D
        {
            Name = name,
            Amount = amount,
            Lifetime = lifetime,
            Preprocess = lifetime,
            ProcessMaterial = process,
            DrawPass1 = mesh,
            MaterialOverride = draw,
            LocalCoords = false,
            AmountRatio = 0f,
            VisibilityAabb = new Aabb(new Vector3(-60, -40, -60), new Vector3(120, 80, 120)),
            TransformAlign = billboard ? GpuParticles3D.TransformAlignEnum.Disabled : GpuParticles3D.TransformAlignEnum.ZBillboardYToVelocity,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        var emitter = new Emitter { Node = node, Process = process, Draw = draw, Offset = offset };
        _emitters.Add(emitter);
        return emitter;
    }

    /// <summary>Particles alive right now across every weather effect, for the budget check.</summary>
    public int ActiveParticles()
    {
        int count = 0;
        foreach (Emitter e in _emitters)
            if (e.Node.Visible && e.Node.Emitting) count += (int)MathF.Round(e.Node.Amount * e.Node.AmountRatio);
        foreach (TornadoFx t in _tornadoes.Values) count += t.ActiveParticles();
        count += _debris.ActiveParticles() + _lightning.ActiveParticles();
        return count;
    }

    // ---- Every frame ------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Director?.Sim == null || Camera == null) return;
        float dt = (float)delta;
        WeatherSim sim = Director.Sim;
        Conditions c = sim.Current;

        UpdateLook(sim, dt);
        UpdateEnvironment(c, dt);
        UpdateParticles(c);
        UpdateGlobals(sim, c, dt);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Director?.Sim == null) return;
        SyncTornadoes(Director.Sim);
    }

    /// <summary>Screenshots: jump straight to the current weather's look, no easing.</summary>
    public void SnapLook() => _lookReady = false;

    void UpdateLook(WeatherSim sim, float dt)
    {
        Segment s = sim.SegmentAt(sim.Time);
        var (_, blend) = sim.Incoming;
        Look target = Look.Lerp(Look.ByKind[s.FromKind], Look.ByKind[s.Kind], blend);
        // Easing on top of the model's own smooth blend hides the switch when weather is forced mid-transition.
        _look = _lookReady ? Look.Lerp(_look, target, 1f - MathF.Exp(-dt / 1.5f)) : target;
        _lookReady = true;
    }

    void UpdateEnvironment(Conditions c, float dt)
    {
        Look l = _look;
        float visibility = c.Visibility;
        float flash = _lightning.Flash;

        _sky.SetShaderParameter("zenith", l.Zenith);
        _sky.SetShaderParameter("horizon", l.Horizon);
        _sky.SetShaderParameter("ground", l.Ground);
        _sky.SetShaderParameter("cloud_lit", l.CloudLit);
        _sky.SetShaderParameter("cloud_shade", l.CloudShade);
        _sky.SetShaderParameter("sun_color", l.Sun);
        _sky.SetShaderParameter("sun_strength", l.SunDisc);

        Environment.FogLightColor = l.Fog.Lerp(new Color("8f86c0"), flash * 0.4f);
        Environment.FogDensity = Mathf.Clamp(2.4f / visibility, 0.0001f, 0.1f); // ~90% fogged at the visibility distance
        Environment.FogSkyAffect = Mathf.Clamp(1.3f - Mathf.Log(visibility) / Mathf.Log(10f) / 3.6f, 0.12f, 0.9f);
        Environment.FogSunScatter = l.FogScatter;
        Environment.FogHeight = Terrain.Height(0, 0) + 5f;
        Environment.FogHeightDensity = c.GroundFog * 0.06f;
        Environment.AmbientLightColor = l.Ambient;
        Environment.AmbientLightEnergy = l.AmbientEnergy + flash * 0.5f;
        Environment.AdjustmentSaturation = l.Saturation;

        _sun.LightColor = l.Sun;
        _sun.LightEnergy = l.SunEnergy * (0.35f + 0.65f * c.Solar);
        _sun.ShadowOpacity = Mathf.Clamp(0.25f + c.Solar, 0f, 1f);
        _flashLight.LightEnergy = flash * 1.2f;
    }

    void UpdateParticles(Conditions c)
    {
        Vector3 camera = Camera.GlobalPosition;
        Vector3 wind = Director.Forces.AirAt(camera); // gusts and tornado inflow included

        // Rain falls at ~9 m/s plus the wind; the box is shifted upwind so drops land around you.
        Drive(_rain, c.Rain, wind + Vector3.Down * 14f, camera);
        // Ash falls with the snow emitter in a fire storm: same motion, darker colour.
        float ash = c.Embers * 0.55f;
        float flakes = MathF.Max(c.Snow, ash);
        Drive(_snow, flakes, wind * 0.9f + Vector3.Down * 2.2f, camera);
        float ashShare = flakes > 0.01f ? Mathf.Clamp(ash / (c.Snow + ash + 0.001f), 0f, 1f) : 0f;
        _snow.Draw.AlbedoColor = new Color(1.3f, 1.35f, 1.45f, 1f).Lerp(new Color(0.22f, 0.2f, 0.2f, 0.85f), ashShare); // a touch over-bright, so flakes show against a pale sky
        Drive(_grit, c.Dust, wind * 1.1f + Vector3.Down * 0.5f, camera);
        Drive(_puffs, c.Dust, wind * 0.8f, camera);
        Drive(_embers, c.Embers, wind * 0.6f + Vector3.Up * 1.8f, camera);
    }

    /// <summary>Sets an emitter's share, velocity and box position: upwind of the camera by half a lifetime of drift, so particles cross it mid-life.</summary>
    static void Drive(Emitter e, float amount, Vector3 velocity, Vector3 camera)
    {
        float upwindSeconds = (float)e.Node.Lifetime * 0.5f;
        e.Node.AmountRatio = Mathf.Clamp(amount, 0f, 1f);
        e.Node.Visible = amount > 0.005f;
        if (!e.Node.Visible) return;
        float speed = velocity.Length();
        e.Process.Direction = speed > 0.01f ? velocity / speed : Vector3.Down;
        e.Process.InitialVelocityMin = speed * 0.9f;
        e.Process.InitialVelocityMax = speed * 1.1f;
        Vector3 horizontal = new(velocity.X, 0, velocity.Z);
        e.Node.GlobalPosition = camera + e.Offset - horizontal * upwindSeconds;
    }

    void UpdateGlobals(WeatherSim sim, Conditions c, float dt)
    {
        Vector2 wind = c.Wind;
        float speed = wind.Length();
        Vector2 dir = speed > 0.01f ? wind / speed : Vector2.Right;
        _windTravel = (_windTravel + speed * dt) % 8000f; // the shader's gust waves repeat every 80 m
        _cloudDrift += wind * dt * 1.6f;                  // clouds ride the faster wind aloft
        GroundCover g = sim.Ground;

        RenderingServer.GlobalShaderParameterSet("weather_wind", new Vector4(dir.X, dir.Y, speed, _windTravel));
        RenderingServer.GlobalShaderParameterSet("weather_cloud", new Vector4(_cloudDrift.X, _cloudDrift.Y, c.CloudCover, c.CloudDarkness));
        RenderingServer.GlobalShaderParameterSet("weather_ground", new Vector4(g.Wetness, g.Snow, g.Dust, Mathf.Clamp(c.Solar * 1.2f - c.CloudCover * 0.3f, 0f, 1f)));
        RenderingServer.GlobalShaderParameterSet("weather_air", new Vector4(c.Gustiness, c.Rain, c.Embers, 0f));
        Vector3 flashDir = _lightning.FlashDirection;
        RenderingServer.GlobalShaderParameterSet("weather_flash", new Vector4(flashDir.X, flashDir.Y, flashDir.Z, _lightning.Flash));

        Tornado nearest = null;
        float best = float.MaxValue;
        foreach (Tornado t in sim.Tornadoes)
        {
            float d = t.Position.DistanceSquaredTo(Camera.GlobalPosition);
            if (d < best) { best = d; nearest = t; }
        }
        RenderingServer.GlobalShaderParameterSet("weather_tornado", nearest == null
            ? Vector4.Zero
            : new Vector4(nearest.Position.X, nearest.Position.Z, Mathf.Max(nearest.Formation * 0.6f, nearest.Strength), nearest.Rules.CaptureRadius));
    }

    // ---- Tornadoes, strikes -----------------------------------------------------------

    void SyncTornadoes(WeatherSim sim)
    {
        foreach (Tornado t in sim.Tornadoes)
        {
            if (!_tornadoes.TryGetValue(t.Id, out TornadoFx fx))
            {
                fx = new TornadoFx { Name = $"Tornado{t.Id}", Fx = this, Rules = t.Rules };
                AddChild(fx);
                _tornadoes[t.Id] = fx;
                fx.GlobalPosition = t.Position;
                fx.ResetPhysicsInterpolation();
            }
            fx.Follow(t);
        }
        if (_tornadoes.Count == sim.Tornadoes.Count) return;
        var gone = new List<int>();
        foreach (int id in _tornadoes.Keys)
        {
            bool alive = false;
            foreach (Tornado t in sim.Tornadoes) alive |= t.Id == id;
            if (!alive) gone.Add(id);
        }
        foreach (int id in gone)
        {
            _tornadoes[id].QueueFree();
            _tornadoes.Remove(id);
        }
    }

    /// <summary>A lightning strike or a debris impact landed.</summary>
    public void OnImpact(WeatherEvent e)
    {
        if (e.Kind == WeatherEventKind.LightningStrike) _lightning.Strike(e.Position);
        else _debris.Impact(e.Id, e.Position);
    }

    public IReadOnlyList<Hazard> Hazards => Director.Sim.Hazards;
}
