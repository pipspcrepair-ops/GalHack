using System;
using System.Collections.Generic;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using PhysicsSandbox.Bridges;
using PlanetSurface.Skies;
using PlanetSurface.WeatherView;
using PlanetSurface.World;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;

namespace Slice.Surface;

/// <summary>
/// The weather prototype's model on the planet, pushing bodies through the shared physics
/// module (the physics sandbox's <see cref="WeatherBridge"/>: wind by mass, tornadoes that
/// catch and fling), and what it looks like here: clouds and fog through planet-surface's sky
/// (<see cref="Atmosphere"/>), its lightning and tornado effects, rain, snow and dust around
/// the camera, and the ground getting wet, snowy or dusty through the shared shader globals.
/// </summary>
public partial class SurfaceWeather : Node3D, IWeatherScene
{
    public SurfaceZone Zone;

    /// <summary>The weather's forces on the one force path; also steps the model.</summary>
    public WeatherBridge Bridge { get; private set; }
    public WeatherSim Sim => Bridge?.Sim;

    /// <summary>Where hazards happen: around the player.</summary>
    public Func<Vector3> Observer = () => Vector3.Zero;

    /// <summary>Something happened (a strike, a funnel forming): the game warns and applies it.</summary>
    public event Action<WeatherEvent> Happened;

    public NoiseTexture2D CloudNoise { get; }
    public Texture2D SoftDot { get; }
    public Camera3D Camera => Zone.Camera;
    public Vector3 ToSun => Zone.Sky.ToSun;
    public float CloudBaseY => Zone.Sky.Air.CloudBase;
    public IReadOnlyList<Hazard> Hazards => Sim?.Hazards ?? (IReadOnlyList<Hazard>)Array.Empty<Hazard>();
    public Vector2 Wind2D => Sim?.Current.Wind ?? Vector2.Zero;

    PlanetWorld _planet;
    LightningFx _lightning;
    readonly Dictionary<int, TornadoFx> _tornadoes = new();
    readonly List<Emitter> _emitters = new();
    Emitter _rain, _snow, _dust, _motes;
    GpuParticles3D _splash;
    GpuParticlesCollisionHeightField3D _stops;
    float _windTravel, _stopsTimer;
    double _lastStep = -1;

    /// <summary>The rain falling where the camera is, 0 to 1.</summary>
    public float RainHere => Sim?.Current.Rain ?? 0f;

    /// <summary>What falls stops on whatever is there (ground, roofs, ships, water): there is no other cover check.</summary>
    public bool FallStopsOnContact => _rain?.Process.CollisionMode == ParticleProcessMaterial.CollisionModeEnum.HideOnContact && _stops != null;

    /// <summary>The top-down picture of what stands around the camera, this big, moved in steps of <see cref="StopsStep"/>.</summary>
    const float StopsSize = 112f, StopsHeight = 140f, StopsStep = 8f;

    sealed class Emitter
    {
        public GpuParticles3D Node;
        public ParticleProcessMaterial Process;
        public StandardMaterial3D Draw;
        public Vector3 Offset;
    }

    public SurfaceWeather()
    {
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
    }

    public override void _Ready()
    {
        _lightning = new LightningFx { Name = "Lightning", Fx = this };
        AddChild(_lightning);
        // A top-down depth picture of everything around the camera: what falls dies on it.
        _stops = new GpuParticlesCollisionHeightField3D
        {
            Name = "Stops", Size = new Vector3(StopsSize, StopsHeight, StopsSize), Resolution = GpuParticlesCollisionHeightField3D.ResolutionEnum.Resolution256,
            UpdateMode = GpuParticlesCollisionHeightField3D.UpdateModeEnum.WhenMoved, Visible = false, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            HeightfieldMask = 0xFFFFFFFFu & ~(1u << 1), // not the castaway's own body
        };
        AddChild(_stops);
        _splash = AddSplash();
        // Fine soft streaks, of many lengths and brightnesses, that only come in a few metres from the eye.
        _rain = AddEmitter("Rain", 2200, 1.1f, new Vector3(18, 10, 18), new Vector3(0, 11, 0),
            new QuadMesh { Size = new Vector2(0.03f, 0.8f) }, new Color(0.82f, 0.88f, 0.94f, 0.5f), billboard: false, Streak());
        _rain.Process.ScaleMin = 0.5f;
        _rain.Process.ScaleMax = 1.35f;
        _rain.Process.ColorInitialRamp = new GradientTexture1D { Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 0.22f), new Color(1, 1, 1, 0.6f), new Color(1, 1, 1, 1f) }, Offsets = new[] { 0f, 0.6f, 1f } } };
        _rain.Draw.DistanceFadeMinDistance = 1.2f;
        _rain.Draw.DistanceFadeMaxDistance = 5f;
        _rain.Node.SubEmitter = _rain.Node.GetPathTo(_splash);
        _rain.Process.SubEmitterMode = ParticleProcessMaterial.SubEmitterModeEnum.AtCollision;
        _rain.Process.SubEmitterAmountAtCollision = 1;
        _snow = AddEmitter("Snow", 3200, 2.5f, new Vector3(16, 7, 16), new Vector3(0, 6, 0),
            new QuadMesh { Size = new Vector2(0.06f, 0.14f) }, new Color(1.3f, 1.35f, 1.45f, 1f), billboard: false);
        // Blowing dust: a low haze of big soft banks, drifting and slowly turning, and a few motes in it.
        _dust = AddEmitter("Dust", 96, 5f, new Vector3(26, 4, 26), new Vector3(0, 3, 0),
            new QuadMesh { Size = new Vector2(13f, 9f) }, new Color(0.42f, 0.2f, 0.08f, 0.2f), billboard: true, PlanetSurface.Fx.Debris.Haze);
        _dust.Process.AngleMax = 360f;
        _dust.Process.AngularVelocityMin = -7f;
        _dust.Process.AngularVelocityMax = 7f;
        _dust.Process.ScaleMin = 0.7f;
        _dust.Process.ScaleMax = 1.8f;
        _dust.Process.ColorRamp = new GradientTexture1D { Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 0.45f, 1f } } };
        _dust.Draw.DistanceFadeMinDistance = 1.5f;
        _dust.Draw.DistanceFadeMaxDistance = 7f;
        _motes = AddEmitter("Motes", 110, 2.6f, new Vector3(13, 3, 13), new Vector3(0, 1.8f, 0),
            new QuadMesh { Size = new Vector2(0.035f, 0.035f) }, new Color(0.8f, 0.65f, 0.48f, 0.55f), billboard: true);
        _motes.Process.ScaleMin = 0.5f;
        _motes.Process.ScaleMax = 1.5f;
        _motes.Draw.DistanceFadeMinDistance = 0.3f;
        _motes.Draw.DistanceFadeMaxDistance = 1.2f;
    }

    /// <summary>A rain streak: soft across, tapering at both ends.</summary>
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

    /// <summary>What a drop does where it lands: a tiny crown of droplets, seen only close to.</summary>
    GpuParticles3D AddSplash()
    {
        var node = new GpuParticles3D
        {
            Name = "Splash", Amount = 340, Lifetime = 0.2f, LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-80, -60, -80), new Vector3(160, 120, 160)), Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Gravity = new Vector3(0f, -9f, 0f), Direction = Vector3.Up, Spread = 38f, InitialVelocityMin = 0.7f, InitialVelocityMax = 1.9f,
                EmissionShapeOffset = new Vector3(0f, 0.22f, 0f), ScaleMin = 0.5f, ScaleMax = 1.2f,
                ColorRamp = new GradientTexture1D { Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 0.9f), new Color(1, 1, 1, 0) } } },
            },
            DrawPass1 = new QuadMesh { Size = new Vector2(0.06f, 0.06f) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = true, AlbedoColor = new Color(0.84f, 0.9f, 0.95f, 0.5f), AlbedoTexture = SoftDot,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                // Past the minimum it's the far end that fades: gone beyond a dozen metres or so.
                DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha, DistanceFadeMinDistance = 14f, DistanceFadeMaxDistance = 7f,
            },
        };
        AddChild(node);
        return node;
    }

    /// <summary>
    /// The weather a kind of world gets, by the weather data's planet kinds (the same mapping
    /// planet-surface's weather rig uses).
    /// </summary>
    public static string ArchetypeFor(PlanetSpec spec)
    {
        if (spec.Has(Modifier.PerpetualStorm)) return "storm";
        if (spec.Has(Modifier.Archipelago) && spec.Archetype == Archetype.Temperate) return "ocean";
        return (spec.Archetype, spec.Variant) switch
        {
            (Archetype.Temperate, 0) => "temperate",
            (Archetype.Temperate, _) => "swamp",
            (Archetype.Arid, _) => "arid",
            (Archetype.Ice, _) => "ice",
            (Archetype.Lava, _) => "lava",
            (Archetype.Alien, 0) => "storm",
            _ => "swamp",
        };
    }

    /// <summary>Seconds of the opening with no severe weather: a castaway's first job is the pod, not a tornado.</summary>
    public const double CalmOpening = 12 * 60;

    /// <summary>
    /// Starts the planet's weather: a quiet sky, and of the weather seeds that follow from the
    /// planet's, the first whose forecast keeps the opening free of severe weather. After that it
    /// is the planet's own weather, every storm warned before it arrives.
    /// </summary>
    public void SetWorld(PlanetWorld planet)
    {
        _planet = planet;
        WeatherData data = Zone.Data.Weather;
        string archetype = ArchetypeFor(planet.Spec);
        WeatherSim sim = null;
        for (ulong k = 0; k < 1024; k++)
        {
            sim = new WeatherSim(data, archetype, planet.Spec.Seed + k * 0x9E3779B9UL, WeatherKind.Clouds);
            sim.EnsureTimeline(CalmOpening);
            bool calm = true;
            foreach (Segment s in sim.Timeline)
                if (s.Start < CalmOpening && data.State(s.Kind).Severe) calm = false;
            if (calm) break;
        }
        sim.GroundHeight = (x, z) => planet.GroundHeight(Mathx.WrapX(x, planet.Width), Mathx.Wrap(z, planet.Height));
        Bridge = new WeatherBridge(sim) { Observer = () => Observer().ToNumerics() };
        Zone.Physics.ForceProviders.Clear();
        Zone.Physics.ForceProviders.Add(Bridge);
    }

    /// <summary>The air right now, for the sky.</summary>
    public AtmosphereState Air(float sunHeight) =>
        Atmosphere.Compute(_planet?.Sky ?? Zone.Sky.System, Sim?.Current ?? Atmosphere.Clear(), sunHeight);

    // ---- Each tick: the model has stepped inside the physics host; pass on what happened ------

    public override void _PhysicsProcess(double delta)
    {
        if (Sim == null) return;
        if (Sim.Time != _lastStep)
        {
            _lastStep = Sim.Time;
            foreach (WeatherEvent e in Sim.Events)
            {
                if (e.Kind == WeatherEventKind.LightningStrike) _lightning.Strike(e.Position);
                Happened?.Invoke(e);
            }
        }
        SyncTornadoes();
    }

    void SyncTornadoes()
    {
        var alive = new HashSet<int>();
        Vector3 eye = Camera?.GlobalPosition ?? Observer();
        foreach (Tornado t in Sim.Tornadoes)
        {
            alive.Add(t.Id);
            if (!_tornadoes.TryGetValue(t.Id, out TornadoFx fx))
            {
                fx = new TornadoFx { Name = $"Tornado{t.Id}", Fx = this, Rules = t.Rules };
                AddChild(fx);
                _tornadoes[t.Id] = fx;
            }
            fx.Follow(t);
            fx.GlobalPosition = _planet.NearestCopy(_planet.Canonical(t.Position), eye);
        }
        var gone = new List<int>();
        foreach (int id in _tornadoes.Keys) if (!alive.Contains(id)) gone.Add(id);
        foreach (int id in gone)
        {
            _tornadoes[id].QueueFree();
            _tornadoes.Remove(id);
        }
    }

    // ---- Each frame: what you see -----------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Sim == null || Camera == null) return;
        float dt = (float)delta;
        Conditions c = Sim.Current;
        Vector3 camera = Camera.GlobalPosition;
        // Nothing of the sky reaches under the water. Everywhere else it falls until it hits something.
        float open = Zone.Underwater.CameraUnder ? 0f : 1f;
        Vector3 wind = Sim.WindAt(camera);
        Drive(_rain, RainHere * open, wind + Vector3.Down * 14f, camera);
        Drive(_snow, MathF.Max(c.Snow, c.Embers * 0.5f) * open, wind * 0.9f + Vector3.Down * 2.2f, camera);
        Drive(_dust, c.Dust * open, wind * 0.8f, camera);
        Drive(_motes, c.Dust * open, wind + Vector3.Up * 0.25f, camera);
        _splash.Visible = _rain.Node.Visible;
        Color dust = _planet.Look.Dust;
        _dust.Draw.AlbedoColor = new Color(dust.R * 0.8f, dust.G * 0.75f, dust.B * 0.7f, 0.2f);
        _motes.Draw.AlbedoColor = new Color(dust.R * 1.1f, dust.G * 1.05f, dust.B, 0.55f);
        KeepStops(camera, dt);

        Vector2 w = c.Wind;
        float speed = w.Length();
        Vector2 dir = speed > 0.01f ? w / speed : Vector2.Right;
        _windTravel = (_windTravel + speed * dt) % 8000f;
        GroundCover g = Sim.Ground;
        RenderingServer.GlobalShaderParameterSet("weather_wind", new Vector4(dir.X, dir.Y, speed, _windTravel));
        RenderingServer.GlobalShaderParameterSet("weather_ground", new Vector4(g.Wetness, g.Snow, g.Dust, c.Solar));
        Vector3 flash = _lightning.FlashDirection;
        RenderingServer.GlobalShaderParameterSet("weather_flash", new Vector4(flash.X, flash.Y, flash.Z, _lightning.Flash));
    }

    /// <summary>
    /// Keeps the top-down picture over the camera while anything falls. It is taken again only when
    /// it moves, a step at a time, and now and then for whatever has been built or flown in since.
    /// </summary>
    void KeepStops(Vector3 camera, float dt)
    {
        bool falling = _rain.Node.Visible || _snow.Node.Visible || _dust.Node.Visible;
        bool shown = _stops.Visible;
        _stops.Visible = falling;
        if (!falling) return;
        Vector3 at = (camera / StopsStep).Floor() * StopsStep;
        _stopsTimer -= dt;
        if (!shown || _stopsTimer <= 0f)
        {
            _stopsTimer = 4f;
            at.X += 0.01f; // any move takes the picture again
        }
        if (at != _stops.GlobalPosition) _stops.GlobalPosition = at;
    }

    Emitter AddEmitter(string name, int amount, float lifetime, Vector3 extents, Vector3 offset, QuadMesh mesh, Color color, bool billboard, Texture2D texture = null)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = extents,
            Gravity = Vector3.Zero, Direction = Vector3.Down, Spread = 3f, InitialVelocityMin = 10f, InitialVelocityMax = 12f,
            CollisionMode = ParticleProcessMaterial.CollisionModeEnum.HideOnContact,
            ColorRamp = new GradientTexture1D
            {
                Gradient = new Gradient
                {
                    Colors = new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) },
                    Offsets = new[] { 0f, 0.12f, 0.8f, 1f },
                },
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
            MaterialOverride = draw, LocalCoords = false, AmountRatio = 0f, Visible = false, CollisionBaseSize = 0.05f,
            VisibilityAabb = new Aabb(new Vector3(-60, -40, -60), new Vector3(120, 80, 120)),
            TransformAlign = billboard ? GpuParticles3D.TransformAlignEnum.Disabled : GpuParticles3D.TransformAlignEnum.ZBillboardYToVelocity,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        var emitter = new Emitter { Node = node, Process = process, Draw = draw, Offset = offset };
        _emitters.Add(emitter);
        return emitter;
    }

    /// <summary>An emitter's share, velocity and box: upwind of the camera by half a lifetime of drift.</summary>
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
}
