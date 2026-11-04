using System.Diagnostics;
using System.Linq;
using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>
/// Builds the prototype in code: the seeded terrain and its view, sky, water, the beacon POI,
/// a player with the survival tool, a supply crate, the stuck guard, and the HUD. Also handles
/// the save, load, rebuild, detector and debug keys.
/// </summary>
public partial class Main : Node3D
{
    public const int WorldSeed = 1977;

    public ChunkStore Store { get; private set; }
    public TerrainView View { get; private set; }
    public Player Player { get; private set; }
    public Camera3D Camera { get; private set; }
    public GatherTool Tool { get; private set; }
    public Debris Debris { get; private set; }
    public OreDetector Detector { get; private set; }
    public StuckGuard Guard { get; private set; }
    public StructureView Structures { get; private set; }
    public ChunkBorders Borders { get; private set; }
    public Hud Hud { get; private set; }
    public RigidBody3D Crate { get; private set; }

    /// <summary>The first terrain build is finished and the player is live.</summary>
    public bool IsReady { get; private set; }

    /// <summary>False in tests and screenshots: no mouse capture, no keyboard control.</summary>
    public bool Interactive = true;

    public string SavePath { get; set; }
    public double StartupSeconds { get; private set; }

    ulong _saveSequence;
    readonly Stopwatch _startup = Stopwatch.StartNew();

    public override void _Ready()
    {
        SavePath ??= ProjectSettings.GlobalizePath("res://saves/world.ghs");
        Store = new ChunkStore(new SimpleTerrainSource(WorldSeed));

        Scenery.BuildSky(this);
        View = new TerrainView { Name = "Terrain" };
        AddChild(View);
        View.Initialize(Store);
        View.ApplyBudgetMs = 12.0; // generous while the loading screen is up
        View.QueueWorld();
        Scenery.BuildWater(this, Store);
        foreach (PoiSite poi in Store.Source.PointsOfInterest) Scenery.BuildBeacon(this, poi);

        Structures = new StructureView { Name = "Structures" };
        AddChild(Structures);

        Camera = new Camera3D { Name = "Camera", Fov = 78f, Near = 0.05f, Far = 3000f };
        AddChild(Camera);
        Camera.MakeCurrent();

        var spawn = SimpleTerrainSource.Spawn;
        Player = new Player { Name = "Player", Camera = Camera, ControlsEnabled = false, ProcessMode = ProcessModeEnum.Disabled };
        AddChild(Player);
        Player.GlobalPosition = new Vector3(spawn.X, Store.Source.SurfaceHeight(spawn.X, spawn.Y) + 0.3f, spawn.Y);
        Player.SetLook(-2.3f, -0.12f);

        Crate = BuildCrate(new Vector3(spawn.X + 3f, 0f, spawn.Y - 3f));

        Debris = new Debris { Name = "Debris", Store = Store, Collector = () => Player.GlobalPosition + Vector3.Up * 1.1f };
        AddChild(Debris);
        Detector = new OreDetector { Name = "Detector", Store = Store };
        AddChild(Detector);

        Guard = new StuckGuard { Name = "StuckGuard", Store = Store };
        AddChild(Guard);
        Guard.Watch(new PlayerBody(Player));
        Guard.Watch(new PropBody(Crate, "Supply crate", 1f));
        Guard.Rescued += (body, from, to) =>
            Hud.Toast($"{body.Label} was inside terrain: moved {from.DistanceTo(to):F1} m to open space", Palette.Caution, 3f);

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Tool = new GatherTool { Name = "GatherTool", Main = this };
        AddChild(Tool);
        Borders = new ChunkBorders { Name = "ChunkBorders", Center = () => Player.GlobalPosition };
        AddChild(Borders);

        View.ChunkApplied += chunk =>
        {
            Borders.Flash(chunk);
            Guard.WakeBodiesIn(View.ChunkBounds(chunk));
        };
        View.RebuildVerified += (chunks, mismatched, seconds) => Hud.Toast(mismatched == 0
            ? $"Rebuilt {chunks} chunks from seed + edits in {seconds:F1} s: every mesh identical"
            : $"Rebuild MISMATCH: {mismatched} of {chunks} chunks differ", mismatched == 0 ? Palette.Good : Palette.Warning, 4f);
    }

    RigidBody3D BuildCrate(Vector3 at)
    {
        var crate = new RigidBody3D
        {
            Name = "SupplyCrate",
            Mass = 25f,
            CollisionLayer = Layers.Props,
            CollisionMask = Layers.Solid | Layers.Player | Layers.Props,
            Freeze = true,
        };
        var material = Shaders.Make(Shaders.Block);
        material.SetShaderParameter("body_color", new Color("3fb8af"));
        material.SetShaderParameter("trim_color", Palette.BlockTrim);
        material.SetShaderParameter("stripe_color", Palette.Beacon);
        material.SetShaderParameter("half_size", Vector3.One * 0.5f);
        crate.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = material });
        crate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } });
        AddChild(crate);
        crate.GlobalPosition = new Vector3(at.X, Store.Source.SurfaceHeight(at.X, at.Z) + 0.8f, at.Z);
        return crate;
    }

    public override void _Process(double delta)
    {
        if (!IsReady)
        {
            int total = ChunkStore.WorldChunks.Volume;
            Hud.Loading($"Generating terrain  {View.MeshHashes.Count * 100 / total}%");
            if (View.IsIdle && View.MeshHashes.Count >= total) BecomeReady();
            return;
        }
        UpdateHud();
    }

    void BecomeReady()
    {
        IsReady = true;
        StartupSeconds = _startup.Elapsed.TotalSeconds;
        GD.Print($"Terrain ready: {View.MeshedChunks} chunk meshes, {View.Triangles} triangles in {StartupSeconds:F2} s");
        View.ApplyBudgetMs = 2.0;
        View.ResetFrameStats();
        Hud.Loading(null);
        Player.ProcessMode = ProcessModeEnum.Inherit;
        Player.ControlsEnabled = Interactive;
        Crate.Freeze = false;
        if (Interactive) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsReady || !Interactive || e is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.PhysicalKeycode)
        {
            case Key.C: Tool.Crater(); break;
            case Key.Q: PingOre(); break;
            case Key.F5: Save(); break;
            case Key.F9: Load(); break;
            case Key.F8: Rebuild(); break;
            case Key.B: Borders.Visible = !Borders.Visible; break;
            case Key.H: Hud.ToggleHelp(); break;
        }
    }

    // ---- Actions ----------------------------------------------------------------------

    public System.Collections.Generic.List<OreDetector.Deposit> PingOre()
    {
        var deposits = Detector.Ping(Player.GlobalPosition + Vector3.Up);
        Hud.Toast(deposits.Count == 0
            ? $"Detector: no ore within {OreDetector.Range:F0} m"
            : $"Detector: {deposits.Count} deposits · nearest {deposits[0].Distance:F0} m ({deposits[0].Cells} cells)", Palette.Ore, 3f);
        return deposits;
    }

    public SaveReport Save()
    {
        SaveReport report = TerrainSave.Save(Store, SavePath, ++_saveSequence);
        Hud.Toast(report.Ok
            ? $"Saved {report.Cells} dug cells and {Store.Structures.Count} blocks · {report.Bytes / 1024f:F1} KB · {report.Milliseconds:F0} ms"
            : $"Save failed: {report.Error}", report.Ok ? Palette.Good : Palette.Warning, 3f);
        return report;
    }

    public LoadResult Load()
    {
        LoadResult result = TerrainSave.LoadLatest(SavePath, Store.Base);
        if (!result.Ok)
        {
            Hud.Toast($"Load refused: {result.Error}. Nothing changed.", Palette.Warning, 4f);
            return result;
        }
        var dirty = Store.ReplaceStateWith(result.Store);
        View.MarkDirty(dirty);
        Structures.Rebuild(Store.Structures);
        _saveSequence = result.Data.Sequence;
        Hud.Toast($"Loaded and audited {result.Data.RemovedCells} cells and {Store.Structures.Count} blocks · {dirty.Count} chunks rebuilt" +
                  (result.Error != null ? $"\n{result.Error}" : ""), result.Error == null ? Palette.Good : Palette.Caution, 3.5f);
        return result;
    }

    public void Rebuild()
    {
        if (View.IsVerifying) return;
        Hud.Toast("Wiping every chunk mesh and rebuilding from seed + edits…", Palette.Text, 2f);
        View.WipeAndRebuild();
    }

    /// <summary>Places a block without the tool (tests and screenshots).</summary>
    public PlaceResult PlaceStructureAt(CellBox box)
    {
        PlaceResult result = Store.PlaceStructure(box);
        if (result.Verdict == PlaceVerdict.Placed)
        {
            View.MarkDirty(result.Cleared.DirtyChunks);
            Structures.Add(result.Structure);
        }
        return result;
    }

    /// <summary>A short burst of light (craters).</summary>
    public void Flash(Vector3 at, Color color)
    {
        var light = new OmniLight3D { LightColor = color, LightEnergy = 8f, OmniRange = 16f, Position = at + Vector3.Up * 2f };
        AddChild(light);
        Tween tween = light.CreateTween();
        tween.TweenProperty(light, "light_energy", 0f, 0.5f);
        tween.TweenCallback(Callable.From(light.QueueFree));
    }

    // ---- HUD --------------------------------------------------------------------------

    void UpdateHud()
    {
        int[] g = Tool.Gathered;
        string target = "";
        if (Tool.AimingAtStructure) target = "Target: building block";
        else if (Tool.HasTarget)
        {
            Int3 t = Tool.Target;
            float depth = Store.Source.SurfaceHeight(t.X + 0.5f, t.Z + 0.5f) - (t.Y + 0.5f);
            target = Tool.TargetVerdict switch
            {
                Verdict.Bedrock => "Target: BEDROCK (undiggable)",
                Verdict.Anchored => $"Target: {Store.MaterialAt(t).Label()} · ANCHORED",
                _ => $"Target: {Store.MaterialAt(t).Label()} · {Mathf.Max(0f, depth):F0} m below the original surface",
            };
        }
        string stance = Player.IsCrouching ? (Player.CanStand() ? "Crouching" : "Crouching · no room to stand") : "";
        Hud.SetStatus(
            $"◆ Ore {g[(int)CellMaterial.Ore]}",
            $"Rock {g[(int)CellMaterial.Rock] + g[(int)CellMaterial.DeepRock]}   Soil {g[(int)CellMaterial.Soil]}\n" +
            $"{target}\n" +
            $"Edits: {Store.RemovedCount} cells in {Store.EditedChunks.Count()} chunks · {Store.Structures.Count} blocks\n" +
            $"Jetpack {Player.JetpackFuel * 100f:F0}%   {stance}");
        Hud.SetPerf(
            $"{Engine.GetFramesPerSecond()} fps · {View.MeshedChunks} chunk meshes · {View.Triangles / 1000}k tris\n" +
            $"remesh avg {View.RemeshMs.Average:F2} ms · max {View.RemeshMs.Max:F2} ms ({View.RemeshMs.Count})\n" +
            $"terrain main thread {View.LastFrameMs:F2} ms · worst {View.WorstFrameMs:F2} ms\n" +
            $"queue {View.Pending}");
    }
}
