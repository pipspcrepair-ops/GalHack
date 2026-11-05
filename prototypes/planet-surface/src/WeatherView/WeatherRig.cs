using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Planets.Gen;
using PlanetSurface.Player;
using PlanetSurface.Skies;
using Archetype = Planets.Gen.Archetype;
using PlanetSurface.World;
using Weather;
using Weather.Forces;
using Weather.Model;

namespace PlanetSurface.WeatherView;

/// <summary>
/// The weather prototype's model and forces, running on the planet. Its data file chooses which
/// weathers visit which kind of world; the model forecasts, blends and schedules hazards; the
/// force module pushes the player with wind and tornadoes. This node turns that into what you
/// see and hear here: clouds and fog through the sky rig (<see cref="Atmosphere"/>), rain, snow,
/// dust and embers around the camera, lightning, tornado funnels, ground that gets wet, snowy or
/// dusty, wind in the trees, and the wind and rain loops.
/// </summary>
public partial class WeatherRig : Node3D
{
    public const int ParticleBudget = 5000;

    public Main Main;
    public WeatherSim Sim { get; private set; }
    public ForceModule Forces { get; } = new();
    public static WeatherData Data => _data ??= WeatherData.Parse(File.ReadAllText(ProjectSettings.GlobalizePath("res://") + "../weather/data/weather.json"));

    public NoiseTexture2D CloudNoise { get; }
    public Texture2D SoftDot { get; }
    public Camera3D Camera => Main.Camera;
    public Vector3 ToSun => Main.Sky.ToSun;
    public float CloudBaseY => Main.Sky.Air.CloudBase;
    public IReadOnlyList<Hazard> Hazards => Sim?.Hazards ?? (IReadOnlyList<Hazard>)Array.Empty<Hazard>();
    public Vector2 Wind2D => Sim?.Current.Wind ?? Vector2.Zero;
    public string ArchetypeId { get; private set; } = "temperate";

    static WeatherData _data;
    PlanetWorld _world;
    LightningFx _lightning;
    readonly Dictionary<int, TornadoFx> _tornadoes = new();
    readonly List<Emitter> _emitters = new();
    Emitter _rain, _snow, _grit, _puffs, _embers;
    StrikePoint _playerPoint;
    float _windTravel, _shelter, _shelterTimer;
    int _keyIndex;
    bool _registered;

    sealed class Emitter
    {
        public GpuParticles3D Node;
        public ParticleProcessMaterial Process;
        public StandardMaterial3D Draw;
        public Vector3 Offset;
    }

    public WeatherRig()
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
        ProcessPhysicsPriority = -20; // before the player moves: forces apply this tick
        BuildParticles();
        _lightning = new LightningFx { Name = "Lightning", Fx = this };
        AddChild(_lightning);
    }

    /// <summary>Which of the weather data's planet kinds a world is.</summary>
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

    public void SetWorld(PlanetWorld world)
    {
        _world = world;
        ArchetypeId = ArchetypeFor(world.Spec);
        Sim = new WeatherSim(Data, ArchetypeId, world.Spec.Seed, WeatherKind.Clouds)
        {
            GroundHeight = (x, z) => world.GroundHeight(Mathx.WrapX(x, world.Width), Mathx.Wrap(z, world.Height)),
        };
        Forces.Wind = Sim.WindAt;
        if (!_registered)
        {
            Forces.Register(Main.Player);
            _registered = true;
        }
        foreach (TornadoFx t in _tornadoes.Values) t.QueueFree();
        _tornadoes.Clear();
        int id = 1;
        _playerPoint = new StrikePoint { Id = id++, Name = "YOU" };
        Sim.StrikePoints.Add(_playerPoint);
        foreach (Poi.PoiSite poi in world.Pois)
        {
            var top = new Vector3((poi.Stamp.MinX + poi.Stamp.MaxX) * 0.5f, poi.Footprint.Max.Y, (poi.Stamp.MinZ + poi.Stamp.MaxZ) * 0.5f);
            Sim.StrikePoints.Add(new StrikePoint { Id = id++, Name = poi.Family.Name, Position = top });
        }
    }

    /// <summary>The air right now, for the sky rig.</summary>
    public AtmosphereState Air(float sunHeight) => Atmosphere.Compute(_world?.Sky ?? Main.Sky.System, Sim?.Current ?? Atmosphere.Clear(), sunHeight);

    // ---- Physics tick: the model and its forces --------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (Sim == null || !Main.IsReady) return;
        float dt = (float)delta;
        Walker player = Main.Player;
        _playerPoint.Position = player.GlobalPosition + Vector3.Up * 1.9f;
        Sim.Step(dt, player.GlobalPosition);

        // The force module sees each tornado at its copy nearest the player, so a funnel that has
        // wandered across a seam still pulls from where it stands.
        Forces.Vortices.Clear();
        foreach (Tornado t in Sim.Tornadoes)
        {
            if (!t.Touchdown) continue;
            Vortex v = Glue.ToVortex(t);
            v.Base = _world.NearestCopy(_world.Canonical(t.Position), player.GlobalPosition);
            Forces.Vortices.Add(v);
        }
        if (!Main.Flying) Forces.Step(dt);
        player.MoveFactor = Sim.Current.MoveSpeed;

        foreach (WeatherEvent e in Sim.Events) Handle(e);
        SyncTornadoes();
    }

    void Handle(WeatherEvent e)
    {
        switch (e.Kind)
        {
            case WeatherEventKind.LightningStrike:
                _lightning.Strike(e.Position);
                float distance = Main.Player.GlobalPosition.DistanceTo(e.Position);
                Main.Sfx.Play(Fx.Sfx.Sound.Thunder, Main.Camera.GlobalPosition + (e.Position - Main.Camera.GlobalPosition).Normalized() * 20f,
                    1f, Mathf.Clamp(6f - distance / 60f, -12f, 6f));
                Main.Player.Kick(0.4f, Mathf.Clamp(60f / Mathf.Max(distance, 10f), 0f, 1.2f));
                if (e.Target == _playerPoint || distance < e.DamageRadius)
                    Main.Hud.Toast("STRUCK BY LIGHTNING · get low in a storm: it hits the highest thing around", Hud.Warning, 4f);
                break;
            case WeatherEventKind.DebrisImpact:
                Main.Debris.Puff(e.Position, new Color(1f, 0.5f, 0.2f, 0.6f), 3f);
                break;
            case WeatherEventKind.TornadoForming:
                Main.Hud.Toast($"Funnel cloud forming {WeatherSim.Bearing(Main.Player.GlobalPosition, e.Position)}", Hud.Warning, 4f);
                break;
            case WeatherEventKind.TornadoTouchdown:
                Main.Hud.Toast("Tornado on the ground", Hud.Warning, 3f);
                break;
        }
    }

    void SyncTornadoes()
    {
        var alive = new HashSet<int>();
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
            fx.GlobalPosition = _world.NearestCopy(_world.Canonical(t.Position), Main.Camera.GlobalPosition);
        }
        var gone = new List<int>();
        foreach (int id in _tornadoes.Keys) if (!alive.Contains(id)) gone.Add(id);
        foreach (int id in gone)
        {
            _tornadoes[id].QueueFree();
            _tornadoes.Remove(id);
        }
    }

    // ---- Frame: what you see and hear ------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Sim == null) return;
        float dt = (float)delta;
        Conditions c = Sim.Current;
        Vector3 camera = Main.Camera.GlobalPosition;

        // Under rock or a roof the rain and snow stop (checked a few times a second, on the cell grid).
        if ((_shelterTimer -= dt) <= 0f && Main.World != null)
        {
            _shelterTimer = 0.25f;
            _shelter = Sheltered(camera) ? 1f : 0f;
        }
        float open = 1f - _shelter;
        Vector3 wind = Forces.AirAt(camera);
        Drive(_rain, c.Rain * open, wind + Vector3.Down * 14f, camera);
        float ash = c.Embers * 0.55f;
        float flakes = MathF.Max(c.Snow, ash);
        Drive(_snow, flakes * open, wind * 0.9f + Vector3.Down * 2.2f, camera);
        float ashShare = flakes > 0.01f ? Mathf.Clamp(ash / (c.Snow + ash + 0.001f), 0f, 1f) : 0f;
        _snow.Draw.AlbedoColor = new Color(1.3f, 1.35f, 1.45f, 1f).Lerp(new Color(0.22f, 0.2f, 0.2f, 0.85f), ashShare);
        Drive(_grit, c.Dust * open, wind * 1.1f + Vector3.Down * 0.5f, camera);
        Drive(_puffs, c.Dust * open, wind * 0.8f, camera);
        Drive(_embers, c.Embers * open, wind * 0.6f + Vector3.Up * 1.8f, camera);
        Color dust = Main.World?.Look.Dust ?? new Color(0.6f, 0.45f, 0.3f);
        _grit.Draw.AlbedoColor = new Color(dust.R * 0.8f, dust.G * 0.75f, dust.B * 0.7f, 0.55f);
        _puffs.Draw.AlbedoColor = new Color(dust.R * 0.75f, dust.G * 0.7f, dust.B * 0.65f, 0.42f);

        Vector2 w = c.Wind;
        float speed = w.Length();
        Vector2 dir = speed > 0.01f ? w / speed : Vector2.Right;
        _windTravel = (_windTravel + speed * dt) % 8000f;
        GroundCover g = Sim.Ground;
        RenderingServer.GlobalShaderParameterSet("weather_wind", new Vector4(dir.X, dir.Y, speed, _windTravel));
        RenderingServer.GlobalShaderParameterSet("weather_ground", new Vector4(g.Wetness, g.Snow, g.Dust, c.Solar));
        Vector3 flash = _lightning.FlashDirection;
        RenderingServer.GlobalShaderParameterSet("weather_flash", new Vector4(flash.X, flash.Y, flash.Z, _lightning.Flash));
        Main.Sfx.SetWeather(speed, c.Rain, _shelter);
    }

    bool Sheltered(Vector3 at)
    {
        // Only someone at or below the ground's surface can be under rock: a pit, a cave, a
        // building. Above it nothing shelters you, and asking would generate terrain on this thread.
        if (at.Y > Main.World.GroundHeight(Mathx.WrapX(at.X, Main.World.Width), Mathx.Wrap(at.Z, Main.World.Height)) + 2.5f) return false;
        // Only chunks the streamer has already built are read: one that isn't loaded is open sky
        // (everything above the meshed range is), and generating it here would hitch the frame.
        var store = Main.World.Terrain.Store;
        var cell = TerrainDigging.Terrain.Int3.Floor(new System.Numerics.Vector3(at.X, at.Y, at.Z));
        for (int up = 1; up <= 24; up++)
        {
            var above = new TerrainDigging.Terrain.Int3(cell.X, cell.Y + up, cell.Z);
            if (!Main.Streamer.IsReady(TerrainDigging.Terrain.ChunkStore.ChunkOf(above))) return false;
            if (store.IsSolid(above)) return true;
        }
        return false;
    }

    /// <summary>Particles alive right now across every weather effect, for the budget check.</summary>
    public int ActiveParticles()
    {
        int count = 0;
        foreach (Emitter e in _emitters)
            if (e.Node.Visible && e.Node.Emitting) count += (int)MathF.Round(e.Node.Amount * e.Node.AmountRatio);
        foreach (TornadoFx t in _tornadoes.Values) count += t.ActiveParticles();
        return count + _lightning.ActiveParticles();
    }

    // ---- Keys -----------------------------------------------------------------------------

    static readonly WeatherKind[] Cycle =
    {
        WeatherKind.Clear, WeatherKind.Clouds, WeatherKind.Rain, WeatherKind.Fog, WeatherKind.Thunderstorm, WeatherKind.Snow,
        WeatherKind.Blizzard, WeatherKind.DustStorm, WeatherKind.Tornado, WeatherKind.Monsoon, WeatherKind.FireStorm, WeatherKind.Hurricane,
    };

    public void HandleKey(Key key)
    {
        if (Sim == null) return;
        if (key == Key.K)
        {
            WeatherKind kind = Cycle[_keyIndex++ % Cycle.Length];
            Sim.Force(kind);
            Main.Hud.Toast($"Forcing {Data.State(kind).Name.ToLowerInvariant()}", Hud.Text);
        }
        else if (key == Key.L)
        {
            Sim.StartStrike(HazardKind.Lightning, Main.Player.GlobalPosition + Main.Player.LookDirection * 60f);
            Main.Hud.Toast("A strike is charging ahead of you", Hud.Caution);
        }
        else if (key == Key.O)
        {
            Vector3 look = Main.Player.LookDirection;
            var flat = new Vector3(look.X, 0, look.Z);
            flat = flat.LengthSquared() < 0.01f ? Vector3.Forward : flat.Normalized();
            Sim.SpawnTornado(Main.Player.GlobalPosition + flat * 260f, Main.Player.GlobalPosition);
        }
    }

    /// <summary>Screenshots: one weather turning into another, caught part way (0 to 1) through the change.</summary>
    public void SnapPartway(WeatherKind from, WeatherKind to, float part, GroundCover? ground = null)
    {
        Sim.Snap(from);
        Sim.Force(to);
        float seconds = (float)Sim.SegmentAt(Sim.Time).Transition * part;
        for (float t = 0; t < seconds; t += 0.5f) Sim.Step(0.5f, Main.Player.GlobalPosition);
        if (ground is GroundCover g) Sim.DebugSetGround(g);
    }

    /// <summary>Screenshots: a bolt and its flash, now, with no build-up.</summary>
    public void DebugStrike(Vector3 at) => _lightning.Strike(at);

    /// <summary>Tests and screenshots: jump straight to a weather and its ground cover.</summary>
    public void Snap(WeatherKind kind, GroundCover? ground = null)
    {
        Sim.Snap(kind);
        if (ground is GroundCover g) Sim.DebugSetGround(g);
    }

    // ---- Particles (ported from the weather prototype) -------------------------------------

    void BuildParticles()
    {
        _rain = AddEmitter("Rain", 3200, 1.1f, new Vector3(22, 10, 22), new Vector3(0, 11, 0),
            new QuadMesh { Size = new Vector2(0.018f, 0.9f) }, new Color(0.78f, 0.85f, 0.9f, 0.32f), billboard: false, texture: null);
        _snow = AddEmitter("Snow", 4200, 2.5f, new Vector3(16, 7, 16), new Vector3(0, 6, 0),
            new QuadMesh { Size = new Vector2(0.06f, 0.14f) }, new Color(1, 1, 1, 0.95f), billboard: false, texture: SoftDot);
        // Flakes are stretched along their velocity only a little: in a blizzard that is sideways,
        // and a long soft dot reads as a flying saucer rather than a streak. The ones right at the
        // lens fade out, or a single flake would fill a corner of the screen.
        _snow.Draw.DistanceFadeMinDistance = 0.8f;
        _snow.Draw.DistanceFadeMaxDistance = 3f;
        _grit = AddEmitter("DustGrit", 1600, 1.2f, new Vector3(20, 5, 20), new Vector3(0, 3, 0),
            new QuadMesh { Size = new Vector2(0.035f, 0.8f) }, new Color(0.5f, 0.27f, 0.12f, 0.55f), billboard: false, texture: null);
        _puffs = AddEmitter("DustPuffs", 260, 4f, new Vector3(24, 6, 24), new Vector3(0, 4, 0),
            new QuadMesh { Size = new Vector2(9f, 9f) }, new Color(0.42f, 0.2f, 0.08f, 0.42f), billboard: true, texture: SoftDot);
        _embers = AddEmitter("Embers", 1400, 2.5f, new Vector3(24, 12, 24), new Vector3(0, 6, 0),
            new QuadMesh { Size = new Vector2(0.06f, 0.32f) }, new Color(1f, 0.55f, 0.15f, 1f), billboard: false, texture: SoftDot);
        _embers.Draw.AlbedoColor = new Color(4f, 1.7f, 0.5f, 1f);
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
            VertexColorUseAsAlbedo = true, AlbedoColor = color, AlbedoTexture = texture,
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

    /// <summary>Sets an emitter's share, velocity and box position: upwind of the camera by half a lifetime of drift.</summary>
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
}
