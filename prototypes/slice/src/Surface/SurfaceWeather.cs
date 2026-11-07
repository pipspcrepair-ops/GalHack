using System;
using System.Collections.Generic;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using PhysicsSandbox.Bridges;
using PlanetSurface.Skies;
using PlanetSurface.WeatherView;
using PlanetSurface.World;
using PlanetSurface.Terrain;
using Slice.Player;
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

    /// <summary>The eye is shut in something that moves faster than the top-down picture follows: a ship in flight.</summary>
    public Func<bool> Enclosed = () => false;

    /// <summary>How much of what falls and blows gets near the eye, 0 to 1: nothing under water, in a ship in flight or where no sky reaches (a sealed room, a deep cave); by a door or a window the drops stop on what they hit.</summary>
    public float SkyAtEye
    {
        get
        {
            if (Zone.Underwater.CameraUnder || Enclosed()) return 0f;
            return Mathf.Clamp((Zone.Light.EyeOpen - 0.01f) * 14f, 0f, 1f); // the eye's openness only ever eases towards nothing
        }
    }

    /// <summary>Drops, and their splashes, are being drawn around the eye (tests).</summary>
    public bool RainShown => _rain.Node.Visible;
    public bool SplashShown => _splash.Visible;

    /// <summary>A strike's real light right now, and one set off by hand (tests).</summary>
    public float StrikeLight => _lightning.StrikeEnergy;
    public void StrikeAt(Vector3 at) => _lightning.Strike(at);

    /// <summary>The rain falling where the camera is, 0 to 1.</summary>
    public float RainHere => Sim == null || Camera == null ? 0f : Sim.RainAt(Camera.GlobalPosition);

    /// <summary>The storm towers this frame, and their rain shafts.</summary>
    readonly Vector4[] _towers = new Vector4[MostTowers];
    readonly float[] _funnels = new float[MostTowers];
    /// <summary>For tests: draw the towers without their rain shafts.</summary>
    public bool ShaftsHidden;
    readonly List<(MeshInstance3D node, ShaderMaterial draw)> _shafts = new();
    const int MostTowers = 4;
    float _flicker = 4f;
    readonly RandomNumberGenerator _rng = new() { Seed = 90125 };

    /// <summary>Thunder on its way: seconds yet, where the strike was, how far off.</summary>
    readonly List<(float left, Vector3 at, float distance)> _thunder = new();

    /// <summary>Thunders still on their way to the ear (tests).</summary>
    public int ThunderComing => _thunder.Count;

    /// <summary>How long a strike's thunder takes to arrive, seconds: sound crosses a kilometre in about three.</summary>
    public static float ThunderDelay(float metres) => metres / 343f;

    /// <summary>A strike was seen: its thunder arrives when the sound has come that far.</summary>
    public void Thunder(Vector3 at)
    {
        float distance = (Camera?.GlobalPosition ?? Observer()).DistanceTo(at);
        _thunder.Add((ThunderDelay(distance), at, distance));
    }

    void Rumble(float dt, Vector3 camera)
    {
        for (int i = _thunder.Count - 1; i >= 0; i--)
        {
            var (left, at, distance) = _thunder[i];
            left -= dt;
            if (left > 0f)
            {
                _thunder[i] = (left, at, distance);
                continue;
            }
            _thunder.RemoveAt(i);
            Zone.Sfx.Play(PlanetSurface.Fx.Sfx.Sound.Thunder, camera + (at - camera).Normalized() * 20f,
                1f - Mathf.Clamp(distance / 5000f, 0f, 0.3f), Mathf.Clamp(6f - distance / 60f, -14f, 6f));
        }
    }

    /// <summary>Tells the sky where the towers stand, hangs a rain shaft under each, and flickers them from within.</summary>
    void SyncCells(float dt, Vector3 camera)
    {
        int count = 0;
        Conditions c = Sim.Current;
        foreach (StormCell cell in Sim.Cells)
        {
            if (count >= MostTowers) break;
            float strength = cell.Strength * c.Cellular;
            if (strength < 0.04f) continue;
            Vector3 at = _planet.NearestCopy(_planet.Canonical(cell.Position), camera);
            _towers[count] = new Vector4(at.X, at.Z, cell.Radius, strength);
            _funnels[count] = cell.Funnel != null ? 1f : 0f;
            if (count >= _shafts.Count) _shafts.Add(AddShaft());
            var (node, draw) = _shafts[count];
            float baseY = at.Y - 10f, height = CloudBaseY - baseY, reach = cell.Radius * 0.85f;
            Vector2 wind2 = Sim.Current.Wind;
            Vector2 lean = wind2 * 6f; if (lean.Length() > height * 0.3f) lean = lean.Normalized() * height * 0.3f;
            float bound = reach * 1.9f + lean.Length();
            node.Visible = !ShaftsHidden;
            node.GlobalTransform = new Transform3D(Basis.Identity.Scaled(new Vector3(bound * 1.2f, height * 1.3f, bound * 1.2f)), new Vector3(at.X + lean.X * 0.5f, baseY + height * 0.6f, at.Z + lean.Y * 0.5f));
            // The mesh stands taller than the volume, so a view ray can never skirt its rim and still cross the veil.
            draw.SetShaderParameter("axis_base", new Vector3(at.X, baseY, at.Z));
            draw.SetShaderParameter("shaft_height", height);
            draw.SetShaderParameter("reach", reach);
            draw.SetShaderParameter("bound", bound);
            draw.SetShaderParameter("lean", lean);
            draw.SetShaderParameter("drift", wind2 * 0.04f);
            draw.SetShaderParameter("seed", (cell.Id * 0.6180339f) % 1f);
            // Standing in it there's nothing to see of it: the falling rain has taken over.
            float away = new Vector2(camera.X - at.X, camera.Z - at.Z).Length();
            draw.SetShaderParameter("amount", 0.9f * strength * Mathf.Clamp((c.Rain + 0.25f) * 1.2f, 0f, 1f) * Mathf.Clamp((away - reach * 0.9f) / (reach * 0.4f), 0f, 1f));
            draw.SetShaderParameter("tint", new Color(0.24f, 0.26f, 0.3f) * (0.2f + 0.8f * Zone.Sky.Daylight));
            count++;
        }
        Zone.Sky.SetCells(_towers, count, _funnels);
        for (int i = count; i < _shafts.Count; i++) _shafts[i].node.Visible = false;

        if (count == 0 || c.LightningPerMinute < 0.1f || (_flicker -= dt) > 0f) return;
        _flicker = _rng.RandfRange(2.5f, 9f);
        Vector4 tower = _towers[_rng.RandiRange(0, count - 1)];
        _lightning.CloudFlash(new Vector3(tower.X, CloudBaseY + 120f, tower.Y), _rng.RandfRange(0.25f, 0.6f));
    }

    (MeshInstance3D, ShaderMaterial) AddShaft()
    {
        var draw = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/rainshaft.gdshader") };
        var node = new MeshInstance3D
        {
            Name = "RainShaft", MaterialOverride = draw, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, CapTop = false, CapBottom = false, RadialSegments = 24, Rings = 1 },
        };
        AddChild(node);
        return (node, draw);
    }

    /// <summary>What falls stops on whatever is there (ground, roofs, ships, water): there is no other cover check.</summary>
    public bool FallStopsOnContact => _rain?.Process.CollisionMode == ParticleProcessMaterial.CollisionModeEnum.HideOnContact && _stops != null;

    /// <summary>How far over the eye the drops start, metres: above any ordinary roof, and half their life up.</summary>
    const float RainSheet = 16f;

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
        _lightning = new LightningFx { Name = "Lightning", Fx = this, LightShare = () => SkyAtEye };
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
        // Each starts on one sheet well overhead: a drop born under a roof would die, and splash, in mid-air.
        _rain = AddEmitter("Rain", 3400, 2.2f, new Vector3(18, 0.5f, 18), new Vector3(0, RainSheet, 0),
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
        Atmosphere.Compute(_planet?.Sky ?? Zone.Sky.System, Sim == null ? Atmosphere.Clear() : AirHere(), sunHeight);

    /// <summary>
    /// The weather where the camera is: out from under a cell the air is clear and the sun comes
    /// through, with the storm standing in plain sight over there; under it, the weather in full.
    /// </summary>
    Conditions AirHere()
    {
        Conditions c = Sim.Current;
        if (c.Cellular <= 0.01f || Camera == null) return c;
        float open = c.Cellular * (1f - Sim.CellCover(Camera.GlobalPosition));
        c[Param.VisibilityLog] = Mathf.Lerp(c[Param.VisibilityLog], MathF.Max(c[Param.VisibilityLog], 3.95f), open);
        c[Param.GroundFog] *= 1f - open;
        c[Param.Solar] = Mathf.Lerp(c.Solar, MathF.Max(c.Solar, 0.75f), open);
        c[Param.Cellular] = open; // right under a tower the whole deck is the storm's
        return c;
    }

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
        if ((_ravageTimer -= (float)delta) <= 0f)
        {
            _ravageTimer = 0.2f;
            foreach (Tornado t in Sim.Tornadoes)
                if (t.Touchdown && t.Strength > 0.25f) Ravage(t);
        }
    }

    float _ravageTimer;
    TreeFx _trees;
    readonly List<FloraSolid> _torn = new();

    /// <summary>Trees a tornado has torn up (tests).</summary>
    public int TreesTorn { get; private set; }

    /// <summary>The most trees one funnel tears up per pass: the ground's rebuild stays cheap.</summary>
    const int TornPerPass = 3;

    /// <summary>
    /// What a funnel does as it passes: the trees it reaches go down for good (the felled list, the
    /// stumps left behind) and creatures in it are flung, as players and vehicles are. Nothing
    /// sheltered is touched: a place's creatures live in their own sims, out of its reach.
    /// </summary>
    void Ravage(Tornado t)
    {
        Vector3 at = _planet.NearestCopy(_planet.Canonical(t.Position), Observer());
        float reach = t.Rules.CaptureRadius * 1.6f;
        _torn.Clear();
        foreach (var (solid, offset) in Zone.Streamer.SolidsNear(at, reach))
        {
            Vector3 c = solid.Center + offset;
            if (!solid.Fellable || new Vector2(c.X - at.X, c.Z - at.Z).Length() > reach) continue;
            _torn.Add(solid);
            if (_torn.Count >= TornPerPass) break;
        }
        foreach (FloraSolid s in _torn) TearUp(s, at);
        ThrowCreatures(t, at, reach);
        Zone.Dust?.Puff(at + Vector3.Up * 0.6f, new Color(_planet.Look.Dust, 1f), 3f);
    }

    void TearUp(FloraSolid tree, Vector3 funnel)
    {
        if (!_planet.Felled.Add(tree.Id)) return;
        Zone.Streamer.Fell(tree.Id);
        TreesTorn++;
        if (_trees == null)
        {
            _trees = new TreeFx();
            Zone.AddChild(_trees);
        }
        Vector3 c = _planet.NearestCopy(_planet.Canonical(tree.Center), funnel);
        Vector3 root = new(c.X, c.Y - tree.Extents.Y, c.Z);
        Vector3 away = root - funnel;
        away = new Vector3(-away.Z, 0f, away.X) + away * 0.4f; // round with the wind, and out
        Color leaf = TreeFx.LeafColour(tree.Kind);
        float height = MathF.Max(2f, tree.Extents.Y * 2f);
        _trees.Fell(root, away.LengthSquared() > 0.01f ? away : Vector3.Right, height, tree.Extents.X, leaf, crown: true);
        _trees.Leaves(root + Vector3.Up * height, leaf, 20, 2.2f);
    }

    void ThrowCreatures(Tornado t, Vector3 at, float reach)
    {
        WildlifePatch patch = Zone.Wildlife;
        if (patch?.Sim == null) return;
        Vector3 centre = patch.PatchLocal(at);
        foreach (Wildlife.Creature c in patch.Sim.Creatures)
        {
            Vector3 d = c.Position - centre;
            var flat = new Vector2(d.X, d.Z);
            float r = flat.Length();
            if (c.Thrown || r > reach) continue;
            Vector2 radial = r > 0.01f ? flat / r : Vector2.Right;
            // A body's share of the pull, as for vehicles: giants barely lift.
            float share = MathF.Min(1f, 2.2f / MathF.Max(0.4f, c.Species.Body.Size)) * t.Strength;
            var around = new Vector3(-radial.Y, 0f, radial.X);
            var outward = new Vector3(radial.X, 0f, radial.Y);
            patch.Sim.Throw(c, (around * t.Rules.OrbitSpeed + outward * t.Rules.FlingSpeed * 0.5f + Vector3.Up * t.Rules.LiftSpeed * 1.4f) * share + t.Velocity);
        }
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
            fx.ReachTo(CloudBaseY - 150f - fx.GlobalPosition.Y); // up into the lumpy bulge of the storm's dark base, at any distance and as it lowers
            Uproot(t, fx.GlobalPosition);
        }
        var gone = new List<int>();
        foreach (int id in _tornadoes.Keys) if (!alive.Contains(id)) gone.Add(id);
        foreach (int id in gone)
        {
            _tornadoes[id].QueueFree();
            _tornadoes.Remove(id);
        }
    }

    /// <summary>
    /// A tornado leaves a trail: every tree in its core goes, whatever the species, by the same felling
    /// as the tool's, so it stays gone. Only what's loaded near the player is touched, and a few a pass.
    /// </summary>
    void Uproot(Tornado t, Vector3 at)
    {
        if (t.Age < t.Rules.FormSeconds) return;
        // Listed first: felling changes the very lists being read.
        var inCore = new List<long>();
        foreach (var (solid, _) in Zone.Streamer.SolidsNear(at, t.Rules.CoreRadius))
            if (solid.Fellable && inCore.Count < 3) inCore.Add(solid.Id);
        foreach (long id in inCore)
            if (_planet.Felled.Add(id)) Zone.Streamer.Fell(id);
    }

    // ---- Each frame: what you see -----------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Sim == null || Camera == null) return;
        float dt = (float)delta;
        Conditions c = Sim.Current;
        Vector3 camera = Camera.GlobalPosition;
        // Nothing of the sky reaches under the water or a sealed room. Everywhere else it falls until it hits something.
        float open = SkyAtEye;
        Vector3 wind = Sim.WindAt(camera);
        Drive(_rain, RainHere * open, wind + Vector3.Down * 14f, camera);
        Drive(_snow, MathF.Max(c.Snow, c.Embers * 0.5f) * open, wind * 0.9f + Vector3.Down * 2.2f, camera);
        Drive(_dust, c.Dust * open, wind * 0.8f, camera);
        Drive(_motes, c.Dust * open, wind + Vector3.Up * 0.25f, camera);
        _splash.Visible = _rain.Node.Visible;
        _splash.GlobalPosition = camera; // its drops live in the world: only its culling box goes with the eye
        Color dust = _planet.Look.Dust;
        _dust.Draw.AlbedoColor = new Color(dust.R * 0.8f, dust.G * 0.75f, dust.B * 0.7f, 0.2f);
        _motes.Draw.AlbedoColor = new Color(dust.R * 1.1f, dust.G * 1.05f, dust.B, 0.55f);
        KeepStops(camera, dt);
        SyncCells(dt, camera);
        Rumble(dt, camera);

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

