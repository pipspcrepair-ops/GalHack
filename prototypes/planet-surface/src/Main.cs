using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Fx;
using PlanetSurface.Player;
using PlanetSurface.Poi;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PlanetSurface.WeatherView;
using PlanetSurface.World;
using TerrainDigging.Terrain;

namespace PlanetSurface;

/// <summary>
/// The playable surface. Builds a planet off the main thread, streams its terrain around you,
/// runs its sky and weather, seats its POIs, and lets you walk, dig, place blocks and fly.
/// Planets switch between all five archetypes; each planet's edits are saved when you leave and
/// restored when you come back.
///
/// The planet is a torus. You always stand on the planet itself (0 to its width, 0 to its
/// height); when you walk past an edge you're moved by a whole planet and the loaded terrain is
/// re-keyed, so nothing streams again and nothing hitches.
/// </summary>
public partial class Main : Node3D
{
    public PlanetWorld World { get; private set; }
    public TerrainStreamer Streamer { get; private set; }
    public SkyRig Sky { get; private set; }
    public WeatherRig Weather { get; private set; }
    public PoiLayer Pois { get; private set; }
    public Walker Player { get; private set; }
    public Camera3D Camera { get; private set; }
    public FreeFly Fly { get; private set; }
    public SurvivalTool Tool { get; private set; }
    public StuckGuard Guard { get; private set; }
    public StructureView Structures { get; private set; }
    public Debris Debris { get; private set; }
    public Sfx Sfx { get; private set; }
    public Hud Hud { get; private set; }
    public MeshInstance3D Sea { get; private set; }

    /// <summary>The planet is built, the ground under you is in, and you can move.</summary>
    public bool IsReady { get; private set; }
    public bool Flying => Fly.Active;

    /// <summary>False in tests and screenshots: no mouse capture, no keyboard control.</summary>
    public bool Interactive = true;

    /// <summary>Where the next planet starts you, if not at its landing site (tests, screenshots).</summary>
    public Vector3? SpawnOverride;

    public PlanetSpec Spec { get; private set; }
    public double ArriveSeconds { get; private set; }
    public int FrameShifts { get; private set; }

    /// <summary>The slowest frame of this node's own work, and where it went (tests).</summary>
    public double WorstMainMs;
    public string WorstMain = "";

    static PoiCatalog _catalog;
    Task<PlanetWorld> _building;
    Stopwatch _arriving;
    ulong _saveSequence;
    ShaderMaterial _water, _seaMaterial;
    Godot.Environment _underwater;
    ColorRect _underwaterTint;

    /// <summary>The camera is under water (a sea or a river): the view is tinted and fogged.</summary>
    public bool CameraUnderwater { get; private set; }
    double _valleyTimer;
    bool _settledHeap;
    float _valleyFloor;

    public static PoiCatalog Catalog => _catalog ??= PoiCatalog.Load(ProjectSettings.GlobalizePath("res://") + "../poi-generator/data");

    public override void _Ready()
    {
        // Streaming allocates steadily on the workers; a blocking full collection would stall the
        // main thread for several milliseconds. Low-latency mode keeps full collections in the background.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        PlanetTerrain.ConfigureWorldBounds();
        FarBuilder.Solids ??= FloraSolidRules.Load(ProjectSettings.GlobalizePath("res://data/flora-solids.json"));
        Sky = new SkyRig { Name = "Sky" };
        AddChild(Sky);
        Streamer = new TerrainStreamer { Name = "Terrain" };
        AddChild(Streamer);
        Pois = new PoiLayer { Name = "Pois" };
        AddChild(Pois);
        Structures = new StructureView { Name = "Structures" };
        AddChild(Structures);

        Camera = new Camera3D { Name = "Camera", Fov = 75f, Near = 0.08f, Far = 12000f };
        AddChild(Camera);
        Camera.MakeCurrent();
        Fly = new FreeFly { Name = "FreeFly", Camera = Camera };
        AddChild(Fly);

        Player = new Walker { Name = "Player", Camera = Camera, ControlsEnabled = false, ProcessMode = ProcessModeEnum.Disabled };
        // Water is a volume: the walker swims in seas and rivers by the shared physics module's water
        // rules, and steps up onto anything a step tall by its walking rules.
        var rules = GalHack.Physics.PhysicsRules.FromJson(System.IO.File.ReadAllText(
            ProjectSettings.GlobalizePath("res://") + "../../shared/Physics/physics.json"));
        Player.Water = rules.Water;
        Player.StepHeight = rules.Walking.StepHeight;
        Player.WaterAt = p => World?.WaterSurfaceAt(p);
        AddChild(Player);
        Guard = new StuckGuard { Name = "StuckGuard", Store = () => World?.Terrain.Store, Player = Player, OutOfSolids = (feet, height, radius) => Streamer.OutOfSolids(feet, height, radius) };
        AddChild(Guard);

        Sfx = new Sfx { Name = "Sfx" };
        AddChild(Sfx);
        Debris = new Debris { Name = "Debris", Collector = () => Player.GlobalPosition + Vector3.Up * 1.1f };
        AddChild(Debris);
        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Tool = new SurvivalTool { Name = "Tool", Main = this };
        AddChild(Tool);
        Weather = new WeatherRig { Name = "Weather", Main = this };
        AddChild(Weather);

        Sea = new MeshInstance3D { Name = "Sea", Mesh = new PlaneMesh { Size = new Vector2(24000f, 24000f), SubdivideWidth = 0, SubdivideDepth = 0 }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(Sea);

        // Under water the view closes in: the liquid's own murk instead of the sky, and a tint.
        var tintLayer = new CanvasLayer { Name = "UnderwaterTint", Layer = 5 };
        _underwaterTint = new ColorRect { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _underwaterTint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        tintLayer.AddChild(_underwaterTint);
        AddChild(tintLayer);

        Guard.Rescued += (from, to) => Hud.Toast($"You were inside terrain: moved {from.DistanceTo(to):F1} m to open space", Hud.Caution, 3f);
        ProcessPhysicsPriority = 100; // after the player moves: frame shifts see where it ended up
        if (Spec == null) Arrive(PlanetSpec.Roll(1, Archetype.Temperate, 0));
    }

    // ---- Planets --------------------------------------------------------------------------

    /// <summary>Leaves the current planet (saving its edits) and starts building the next one.</summary>
    public void Arrive(PlanetSpec spec)
    {
        if (World != null) SaveQuietly();
        IsReady = false;
        Spec = spec;
        Player.ProcessMode = ProcessModeEnum.Disabled;
        Player.ControlsEnabled = false;
        Guard.Enabled = false;
        Streamer.Clear();
        Pois.Clear();
        Structures.Clear();
        World = null;
        Hud.Loading($"Generating {spec.Archetype} / {Archetypes.Get(spec.Archetype, spec.Variant).VariantName}...");
        _arriving = Stopwatch.StartNew();
        _settledHeap = false;
        PoiCatalog catalog = Catalog;
        _building = Task.Run(() => PlanetWorld.Build(spec, catalog));
    }

    void Arrived(PlanetWorld world)
    {
        World = world;
        RenderingServer.GlobalShaderParameterSet("planet_size", new Vector2((float)world.Width, (float)world.Height));

        // Load this planet's edits, if it has been dug before.
        LoadResult loaded = World.Terrain.Load(World.SavePath, out _);
        if (loaded.Ok) Hud.Toast($"Restored {loaded.Data.RemovedCells} dug cells and {World.Terrain.Store.Structures.Count} blocks on this planet", Hud.Good, 3f);
        if (loaded.Ok && !World.LoadFelled()) GD.PushError($"{World.FelledPath} is unreadable: its felled trees stand again");
        Structures.Sync(World.Terrain.Store.Structures);

        WorldLook look = world.Look;
        _water = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
        _water.SetShaderParameter("shallow", look.Shallow);
        _water.SetShaderParameter("deep", look.Deep);
        _water.SetShaderParameter("lava", look.Lava);
        _water.SetShaderParameter("glow", look.LiquidGlow);
        _seaMaterial = (ShaderMaterial)_water.Duplicate();
        _seaMaterial.SetShaderParameter("sea", true);
        _seaMaterial.SetShaderParameter("sea_map", world.SeaTexture());
        Sea.MaterialOverride = _seaMaterial;
        _underwater = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = look.Deep,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = look.Shallow,
            AmbientLightEnergy = 0.9f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogLightColor = look.Deep.Lerp(look.Shallow, 0.35f),
            FogDensity = 0.06f,
            FogSkyAffect = 1f,
        };
        _underwaterTint.Color = new Color(look.Shallow, 0.25f);

        Vector3 spawn = SpawnOverride ?? new Vector3((float)world.Landing.X, 0, (float)world.Landing.Y);
        spawn.Y = world.GroundHeight(spawn.X, spawn.Z) + 0.3f;
        Streamer.SetWorld(world, spawn, _water);
        Sky.SetWorld(world);
        Weather.SetWorld(world);
        Pois.Build(world);
        Player.Teleport(spawn);
        Player.SetLook(-world.Landing.Heading, -0.1f);
        Camera.GlobalPosition = spawn + Vector3.Up * 1.6f;
        Fly.Active = false;
        Hud.Loading("Landing...");
    }

    void BecomeReady()
    {
        // Behind the landing screen: settle the new planet's long-lived data into the old
        // generation now, rather than in a long collection mid-play.
        if (!_settledHeap)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            _settledHeap = true;
        }
        IsReady = true;
        ArriveSeconds = _arriving.Elapsed.TotalSeconds;
        Hud.Loading(null);
        Guard.Enabled = true;
        if (!Flying)
        {
            Player.ProcessMode = ProcessModeEnum.Inherit;
            Player.ControlsEnabled = Interactive;
        }
        if (Interactive) Input.MouseMode = Input.MouseModeEnum.Captured;
        Hud.Toast($"{World.Spec} · {World.Sky.Summary}", Hud.Text, 5f);
        Hud.Toast($"{World.Pois.Count} places nearby", Hud.Text, 5f);
    }

    // ---- Every frame ----------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_building != null)
        {
            if (!_building.IsCompleted) return;
            if (_building.IsFaulted)
            {
                GD.PushError($"planet failed to build: {_building.Exception}");
                Hud.Loading($"Planet failed to build: {_building.Exception?.InnerException?.Message}");
                _building = null;
                return;
            }
            PlanetWorld built = _building.Result;
            _building = null;
            Arrived(built);
        }
        if (World == null) return;

        long t0 = Stopwatch.GetTimestamp();
        Vector3 focus = Flying ? Camera.GlobalPosition : Player.GlobalPosition;
        Streamer.SetFocus(focus);
        Streamer.CollisionEnabled = !Flying;
        if (!IsReady && Streamer.ReadyAround(Player.GlobalPosition, 1, needCollision: !Flying)) BecomeReady();

        float latitude = (float)(Mathx.Wrap(focus.Z, World.Height) / World.Height * 2.0 - 1.0);
        long t1 = Stopwatch.GetTimestamp();
        Sky.Advance(delta, latitude, Weather.Air(Sky.ToSun.Y), Weather.Wind2D);
        long t2 = Stopwatch.GetTimestamp();
        UpdateValleyFog(delta, focus);
        Sea.GlobalPosition = new Vector3(Camera.GlobalPosition.X, 0f, Camera.GlobalPosition.Z);
        UpdateUnderwater();
        long t3 = Stopwatch.GetTimestamp();
        UpdateHud();
        long t4 = Stopwatch.GetTimestamp();
        double total = Stopwatch.GetElapsedTime(t0, t4).TotalMilliseconds;
        if (total > WorstMainMs)
        {
            WorstMainMs = total;
            WorstMain = $"ready check {Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds:F1}, sky {Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds:F1}, " +
                        $"fog {Stopwatch.GetElapsedTime(t2, t3).TotalMilliseconds:F1}, hud {Stopwatch.GetElapsedTime(t3, t4).TotalMilliseconds:F1}";
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (World == null) return;
        WrapFrame();
    }

    /// <summary>
    /// Keeps whoever is moving on the planet itself: past an edge (with a few metres of slack, so
    /// walking along a seam doesn't flip back and forth) they move by a whole planet, and the
    /// loaded terrain is re-keyed with them.
    /// </summary>
    void WrapFrame()
    {
        Node3D mover = Flying ? Camera : Player;
        Vector3 p = mover.GlobalPosition;
        int W = World.Terrain.Width, H = World.Terrain.Height;
        int dx = p.X < -8 ? 1 : p.X >= W + 8 ? -1 : 0;
        int dz = p.Z < -8 ? 1 : p.Z >= H + 8 ? -1 : 0;
        if (dx == 0 && dz == 0) return;
        var shift = new Vector3(dx * W, 0, dz * H);
        Player.Teleport(Player.GlobalPosition + shift, keepVelocity: true);
        Camera.GlobalPosition += shift;
        Camera.ResetPhysicsInterpolation();
        Streamer.ShiftFrame(dx * World.Terrain.ChunksX, dz * World.Terrain.ChunksZ);
        Streamer.SetFocus(Flying ? Camera.GlobalPosition : Player.GlobalPosition);
        FrameShifts++;
    }

    /// <summary>Under water, the camera sees the liquid's murk (thicker the deeper it is) through a tint; above it, the sky again.</summary>
    void UpdateUnderwater()
    {
        Vector3 eye = Camera.GlobalPosition;
        float? surface = _underwater == null ? null : World.WaterSurfaceAt(eye);
        bool under = surface is float s && eye.Y < s;
        if (under) _underwater.FogDensity = 0.06f + 0.003f * (surface.Value - eye.Y);
        if (under == CameraUnderwater) return;
        CameraUnderwater = under;
        Camera.Environment = under ? _underwater : null;
        _underwaterTint.Visible = under;
    }

    void UpdateValleyFog(double delta, Vector3 focus)
    {
        // The low ground within a kilometre or so: where morning fog pools.
        _valleyTimer -= delta;
        if (_valleyTimer <= 0)
        {
            _valleyTimer = 1.0;
            var heights = new List<float>(48);
            for (int r = 1; r <= 3; r++)
            for (int a = 0; a < 16; a++)
            {
                double angle = a * Math.Tau / 16;
                heights.Add(Math.Max(0f, World.Map.Sample(World.Map.Elevation,
                    Mathx.WrapX(focus.X + Math.Cos(angle) * r * 350, World.Width), Mathx.Wrap(focus.Z + Math.Sin(angle) * r * 350, World.Height))));
            }
            heights.Add(Math.Max(0f, World.GroundHeight(focus.X, focus.Z)));
            heights.Sort();
            _valleyFloor = heights[heights.Count / 6];
        }
        Sky.SetValleyFog(_valleyFloor, Sky.Air);
    }

    // ---- Edits ----------------------------------------------------------------------------

    public PlanetEdit Dig(Int3 center, float radius)
    {
        PlanetEdit edit = World.Terrain.Dig(center, radius);
        Streamer.MarkDirty(edit.DirtyChunks);
        Streamer.MarkDug(edit.Removed);
        return edit;
    }

    public PlanetEdit Crater(System.Numerics.Vector3 center, float radius)
    {
        PlanetEdit edit = World.Terrain.Crater(center, radius);
        Streamer.MarkDirty(edit.DirtyChunks);
        Streamer.MarkDug(edit.Removed);
        return edit;
    }

    public PlaceResult PlaceStructure(CellBox box)
    {
        PlaceResult result = World.Terrain.PlaceStructure(box);
        if (result.Verdict != PlaceVerdict.Placed) return result;
        Streamer.MarkDirty(result.Cleared.DirtyChunks);
        Streamer.MarkDug(result.Cleared.Removed);
        Structures.Sync(World.Terrain.Store.Structures);
        return result;
    }

    public SaveReport Save()
    {
        SaveReport report = World.Terrain.Save(World.SavePath, ++_saveSequence);
        if (report.Ok) World.SaveFelled();
        Hud.Toast(report.Ok
            ? $"Saved {report.Cells} dug cells, {World.Terrain.Store.Structures.Count} blocks and {World.Felled.Count} felled trees · {report.Bytes / 1024f:F1} KB"
            : $"Save failed: {report.Error}", report.Ok ? Hud.Good : Hud.Warning, 3f);
        return report;
    }

    void SaveQuietly()
    {
        if (World.Terrain.Store.RemovedCount == 0 && World.Terrain.Store.Structures.Count == 0 && World.Felled.Count == 0) return;
        SaveReport report = World.Terrain.Save(World.SavePath, ++_saveSequence);
        if (report.Ok) World.SaveFelled();
        else GD.PushError($"saving {World.Spec} failed: {report.Error}");
    }

    public LoadResult Load()
    {
        LoadResult result = World.Terrain.Load(World.SavePath, out HashSet<Int3> dirty);
        if (!result.Ok)
        {
            Hud.Toast($"Load refused: {result.Error}. Nothing changed.", Hud.Warning, 4f);
            return result;
        }
        Streamer.MarkDirty(dirty);
        Structures.Sync(World.Terrain.Store.Structures);
        if (World.LoadFelled()) Streamer.RefreshFelled();
        Hud.Toast($"Loaded and audited {result.Data.RemovedCells} cells, {World.Terrain.Store.Structures.Count} blocks and {World.Felled.Count} felled trees", Hud.Good, 3f);
        return result;
    }

    /// <summary>
    /// Fells a tree (the survival tool's cut): its collider goes this frame, it is gone from the
    /// scatter at every level of detail, it stays felled through streaming and saves, and it drops wood.
    /// </summary>
    public bool FellTree(FloraSolid tree)
    {
        if (!tree.Fellable || !World.Felled.Add(tree.Id)) return false;
        Streamer.Fell(tree.Id);
        Tool.Gathered["wood"] = Tool.Gathered.TryGetValue("wood", out int n) ? n + tree.Wood : tree.Wood;
        Hud.Toast($"+{tree.Wood} wood", Hud.Good, 0.9f);
        Sfx.Play(Sfx.Sound.Dig, Player.EyePosition);
        return true;
    }

    // ---- Input ----------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Interactive) return;
        if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured && IsReady)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        Key k = key.PhysicalKeycode;
        if (k == Key.Escape) Input.MouseMode = Input.MouseModeEnum.Visible;
        if (k >= Key.Key1 && k <= Key.Key5) { Arrive(PlanetSpec.Roll(Spec.Seed, (Archetype)(k - Key.Key1), 0)); return; }
        if (!IsReady) return;
        switch (k)
        {
            case Key.V: Arrive(PlanetSpec.Roll(Spec.Seed, Spec.Archetype, (Spec.Variant + 1) % PlanetSpec.VariantCount)); break;
            case Key.N: Arrive(PlanetSpec.Roll(Spec.Seed + 1, Spec.Archetype, Spec.Variant)); break;
            case Key.F: ToggleFly(); break;
            case Key.F5: Save(); break;
            case Key.F9: Load(); break;
            case Key.H: Hud.ToggleHelp(); break;
            case Key.T: Sky.TimeScale = Sky.TimeScale > 1f ? 1f : 20f; Hud.Toast(Sky.TimeScale > 1f ? "Time ×20" : "Time ×1", Hud.Text); break;
            case Key.Y: Sky.TimeOfDay = Sky.TimeOfDay < 0.2 || Sky.TimeOfDay >= 0.85 ? 0.26 : Sky.TimeOfDay < 0.4 ? 0.5 : Sky.TimeOfDay < 0.6 ? 0.74 : 0.95; break;
            default: Weather.HandleKey(k); break;
        }
    }

    public void ToggleFly()
    {
        if (!Flying)
        {
            Fly.Begin(Camera.GlobalPosition, Player.Yaw, Player.Pitch);
            Player.ProcessMode = ProcessModeEnum.Disabled;
            Guard.Enabled = false;
            Hud.Toast("Free-fly · F to land where you are", Hud.Text);
        }
        else
        {
            Fly.Active = false;
            Vector3 p = Camera.GlobalPosition;
            Player.Teleport(new Vector3(p.X, World.GroundHeight(p.X, p.Z) + 0.5f, p.Z));
            Player.SetLook(Fly.Yaw, 0f);
            // Wait for collision under the new spot before walking (see _Process).
            IsReady = false;
            Hud.Loading("Landing...");
        }
    }

    void UpdateHud()
    {
        Vector3 p = Flying ? Camera.GlobalPosition : Player.GlobalPosition;
        float ground = World.GroundHeight(p.X, p.Z);
        var gathered = new System.Text.StringBuilder();
        foreach (var (what, n) in Tool.Gathered) gathered.Append($"{what} {n}  ");
        string target = !Tool.HasTarget ? "" : Tool.TargetVerdict switch
        {
            Verdict.Bedrock => "Target: BEDROCK",
            Verdict.Anchored => $"Target: {World.Terrain.Store.MaterialAt(Tool.Target).Label()} · ANCHORED",
            _ => $"Target: {World.Terrain.Store.MaterialAt(Tool.Target).Label()}",
        };
        Hud.SetStatus(
            $"{World.Spec}\n{World.Sky.Summary}\n" +
            $"{p.X / 1000f:F2} km E, {p.Z / 1000f:F2} km S · altitude {p.Y:F0} m · over {World.Palette.BiomeAt(Mathx.WrapX(p.X, World.Width), Mathx.Wrap(p.Z, World.Height))}\n" +
            $"{(Flying ? "FREE-FLY" : Player.Underwater ? "UNDERWATER · suit on its own oxygen" : Player.Swimming ? "Swimming · Space up, Ctrl down" : Player.IsCrouching ? "Crouching" : "")}  Jetpack {Player.JetpackFuel * 100f:F0}%\n" +
            $"{gathered}\n{target}");
        Hud.SetPerf(
            $"{Engine.GetFramesPerSecond()} fps · {Streamer.MeshedChunks} voxel chunks ({Streamer.Triangles / 1000}k tris, {Streamer.CollisionBodies} with collision) · {Streamer.FarChunks} far · queue {Streamer.Pending}\n" +
            $"terrain main thread {Streamer.LastFrameMs:F2} ms, worst {Streamer.WorstFrameMs:F2} ms · ground {ground:F0} m · frame shifts {FrameShifts}");
    }
}
