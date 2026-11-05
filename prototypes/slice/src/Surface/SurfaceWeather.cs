using System;
using System.Collections.Generic;
using GalHack.Physics.Engine;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PhysicsSandbox.Bridges;
using PlanetSurface.Skies;
using PlanetSurface.WeatherView;
using PlanetSurface.World;
using TerrainDigging.Terrain;
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
    Emitter _rain, _snow, _dust;
    float _windTravel, _shelter, _shelterTimer;
    double _lastStep = -1;

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
        _rain = AddEmitter("Rain", 2800, 1.1f, new Vector3(22, 10, 22), new Vector3(0, 11, 0),
            new QuadMesh { Size = new Vector2(0.018f, 0.9f) }, new Color(0.78f, 0.85f, 0.9f, 0.32f), billboard: false);
        _snow = AddEmitter("Snow", 3200, 2.5f, new Vector3(16, 7, 16), new Vector3(0, 6, 0),
            new QuadMesh { Size = new Vector2(0.06f, 0.14f) }, new Color(1.3f, 1.35f, 1.45f, 1f), billboard: false);
        _dust = AddEmitter("Dust", 240, 4f, new Vector3(24, 6, 24), new Vector3(0, 4, 0),
            new QuadMesh { Size = new Vector2(9f, 9f) }, new Color(0.42f, 0.2f, 0.08f, 0.42f), billboard: true);
        _dust.Process.AngleMax = 360f;
        _dust.Process.ScaleMin = 0.6f;
        _dust.Process.ScaleMax = 1.7f;
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
    public const double CalmOpening = 20 * 60;

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
        for (ulong k = 0; k < 64; k++)
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
        if ((_shelterTimer -= dt) <= 0f)
        {
            _shelterTimer = 0.25f;
            _shelter = Sheltered(camera) ? 1f : 0f;
        }
        float open = 1f - _shelter;
        Vector3 wind = Sim.WindAt(camera);
        Drive(_rain, c.Rain * open, wind + Vector3.Down * 14f, camera);
        Drive(_snow, MathF.Max(c.Snow, c.Embers * 0.5f) * open, wind * 0.9f + Vector3.Down * 2.2f, camera);
        Drive(_dust, c.Dust * open, wind * 0.8f, camera);
        Color dust = _planet.Look.Dust;
        _dust.Draw.AlbedoColor = new Color(dust.R * 0.75f, dust.G * 0.7f, dust.B * 0.65f, 0.42f);

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

    /// <summary>Under rock nothing falls on you. Only chunks already streamed are read, so this never generates terrain.</summary>
    bool Sheltered(Vector3 at)
    {
        if (at.Y > _planet.GroundHeight(Mathx.WrapX(at.X, _planet.Width), Mathx.Wrap(at.Z, _planet.Height)) + 2.5f) return false;
        ChunkStore store = _planet.Terrain.Store;
        Int3 cell = Int3.Floor(new System.Numerics.Vector3(at.X, at.Y, at.Z));
        for (int up = 1; up <= 24; up++)
        {
            var above = new Int3(cell.X, cell.Y + up, cell.Z);
            if (!Zone.Streamer.IsReady(ChunkStore.ChunkOf(above))) return false;
            if (store.IsSolid(above)) return true;
        }
        return false;
    }

    Emitter AddEmitter(string name, int amount, float lifetime, Vector3 extents, Vector3 offset, QuadMesh mesh, Color color, bool billboard)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = extents,
            Gravity = Vector3.Zero, Direction = Vector3.Down, Spread = 3f, InitialVelocityMin = 10f, InitialVelocityMax = 12f,
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
            VertexColorUseAsAlbedo = true, AlbedoColor = color, AlbedoTexture = billboard ? SoftDot : null,
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
