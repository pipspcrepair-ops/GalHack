using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SurvivalVitals.Model;

namespace SurvivalVitals;

/// <summary>
/// Builds the prototype in code: a planet patch with a frozen valley, a volcanic vent, a
/// radiation crater, an airless sinkhole, faction land, a shelter, a crashed escape pod and
/// fruit bushes, under a day and night sky. Each physics tick it hands the player's position
/// and activity to the survival model, then makes the world match the model.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>World seed; tests and screenshots may set it before adding Main to the tree.</summary>
    public int Seed = 1977;

    public SurvivalSim Sim { get; private set; }
    public Terrain Terrain { get; private set; }
    public Player Player { get; private set; }
    public VisorHud Hud { get; private set; }
    public InventoryPanel InventoryPanel { get; private set; }
    public DeathScreen DeathScreen { get; private set; }

    /// <summary>What the keys would do here, shown under the crosshair.</summary>
    public string Prompt { get; private set; }
    public bool Mining { get; private set; }

    readonly List<(string text, double at)> _messages = new();
    double _clock;
    float _mineTimer;

    readonly Dictionary<Replicator, Node3D> _replicators = new();
    readonly Dictionary<int, Node3D> _backpacks = new();
    readonly Dictionary<EscapePod, Node3D> _pods = new();
    readonly Dictionary<Bush, Node3D> _fruit = new();
    MeshInstance3D _homeLamp;
    bool _homeShown;
    Node3D _spark;

    ProceduralSkyMaterial _sky;
    Godot.Environment _environment;
    DirectionalLight3D _sun, _moonLight;
    StandardMaterial3D _starMaterial;

    public override void _Ready()
    {
        Sim = new SurvivalSim(DataFiles.Load(), Seed);
        Terrain = new Terrain(Sim.Data.Planet);
        BuildSky();
        AddChild(Terrain.Build());
        BuildHorizon();
        BuildPatch();

        Player = new Player { Name = "Player", Main = this };
        AddChild(Player);
        StartData start = Sim.Data.Planet.Start;
        Player.Teleport(GroundAt(start.Position), Mathf.DegToRad((float)start.YawDegrees));
        Input.MouseMode = Input.MouseModeEnum.Captured;

        var layer = new CanvasLayer();
        AddChild(layer);
        Hud = new VisorHud { Main = this };
        layer.AddChild(Hud);
        InventoryPanel = new InventoryPanel { Main = this };
        layer.AddChild(InventoryPanel);
        DeathScreen = new DeathScreen { Main = this };
        layer.AddChild(DeathScreen);

        SyncWorld();
        Toast("Crash-landed. Loot the pod (E), pick fruit, and keep an eye on the visor.");
    }

    // ---- The tick ------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Survivor me = Sim.Player;
        Player.Controllable = !me.Dead && !InventoryPanel.Visible;

        if (!me.Dead)
        {
            me.Position = ToPos(Player.GlobalPosition);
            // A landing is judged before this tick's vitals, while the fall warning still stands.
            float landing = Player.TakeLanding();
            if (landing > 0) Sim.Land(landing);
            Vector3 hit = default;
            Mining = (Player.Controllable && Input.IsMouseButtonPressed(MouseButton.Left)
                      && Input.MouseMode == Input.MouseModeEnum.Captured || DebugMining) && AimedAtRock(out hit);
            Sim.Tick(dt, new Activity(Player.Pace, Mining, Player.FallSpeed));
            UpdateMining(dt, hit);
        }
        else
        {
            Mining = false;
            Sim.Tick(dt, default);
        }

        foreach (string e in Sim.Events) Toast(e);
        Sim.Events.Clear();
        if (me.Dead && !DeathScreen.Visible) OpenDeathScreen();
        SyncWorld();
        UpdatePrompt();
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        UpdateSky();
    }

    void UpdateMining(float dt, Vector3 hit)
    {
        _spark.Visible = Mining;
        if (!Mining)
        {
            _mineTimer = 0;
            return;
        }
        _spark.GlobalPosition = hit;
        _spark.Scale = Vector3.One * (0.7f + 0.3f * Mathf.Sin((float)_clock * 40f));
        _mineTimer += dt;
        float perOre = (float)Sim.Data.Vitals.Movement.MiningSecondsPerOre;
        if (_mineTimer >= perOre)
        {
            _mineTimer -= perOre;
            Sim.AddOre(1);
        }
    }

    bool AimedAtRock(out Vector3 hit)
    {
        hit = Vector3.Zero;
        Camera3D camera = Player.Camera;
        Vector3 from = camera.GlobalPosition;
        Vector3 to = from - camera.GlobalBasis.Z * (float)Sim.Data.Vitals.Movement.MiningReach;
        var query = PhysicsRayQueryParameters3D.Create(from, to, Layers.World);
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0 || result["collider"].As<Node>() is not Node node || !node.IsInGroup("rock")) return false;
        hit = result["position"].AsVector3();
        return true;
    }

    // ---- Keys ----------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured
            && !Sim.Player.Dead && !InventoryPanel.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        Key k = key.PhysicalKeycode;

        if (Sim.Player.Dead)
        {
            int index = k switch { Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2, Key.Key4 => 3, _ => -1 };
            if (index >= 0) RespawnAt(Sim.RespawnOptions()[index].Choice);
            return;
        }
        if (InventoryPanel.Visible)
        {
            if (k is Key.I or Key.Escape) CloseInventory();
            return;
        }

        switch (k)
        {
            case Key.Escape: Input.MouseMode = Input.MouseModeEnum.Visible; break;
            case Key.I: OpenInventory(); break;
            case Key.B: Act(Sim.DeployOrPack(ToPos(Player.GlobalPosition + Player.Forward * 2f))); break;
            case Key.F: AtReplicator(r => Sim.Feed(r)); break;
            case Key.Key1: AtReplicator(r => Sim.Craft(r, 1)); break;
            case Key.Key2: AtReplicator(r => Sim.Craft(r, 2)); break;
            case Key.Key3: AtReplicator(r => Sim.Craft(r, 3)); break;
            case Key.H: Act(Sim.ToggleHelmet()); break;
            case Key.K: Sim.Kill(); break;
            case Key.E: Interact(); break;
            case Key.Q: Act(Sim.UseMedkit()); break;
            case Key.R:
                Player.RunToggled = !Player.RunToggled;
                Toast(Player.RunToggled ? "Running pace on: 1.5x food, light stamina." : "Walking pace.");
                break;
            case Key.T:
                Sim.AdvanceWorld(Sim.Data.Planet.DayLengthSeconds / 12);
                Toast($"Skipped two hours: {Sim.Planet.HourOfDay(Sim.Time):00}:00.");
                break;
        }
    }

    void AtReplicator(Func<Replicator, Outcome> use)
    {
        Replicator r = Sim.NearestReplicator(Sim.Player.Position, ReplicatorReach);
        if (r == null) Toast("No replicator in reach. Set one down with B, or use the shelter's galley.");
        else Act(use(r));
    }

    const double ReplicatorReach = 3.2;

    /// <summary>E: the nearest thing in reach that E does something to.</summary>
    void Interact()
    {
        var target = Targets().OrderBy(t => t.distance).FirstOrDefault();
        if (target.use == null) Toast("Nothing to use here.");
        else Act(target.use());
    }

    List<(double distance, string prompt, Func<Outcome> use)> Targets()
    {
        var list = new List<(double, string, Func<Outcome>)>();
        Pos me = Sim.Player.Position;
        void Add(Pos at, double reach, string prompt, Func<Outcome> use)
        {
            double d = me.FlatDistanceTo(at);
            if (d <= reach) list.Add((d, prompt, use));
        }
        foreach (RespawnPoint p in Sim.RespawnPoints)
        {
            if (p.Kind == RespawnKind.HealingBooth) Add(p.Stand, 1.6, "E  healing booth: full health, radiation cleared", Sim.UseBooth);
            if (p.Kind == RespawnKind.CloneChamber)
                Add(p.Stand, 1.6, p.IsHome ? "Clone chamber: this is home" : "E  flag this clone chamber as home", () => Sim.SetHome(p));
        }
        PlanetData planet = Sim.Data.Planet;
        Add(planet.Storage.Position, 2.3, $"E  take 10 kg organics for the field  (storage {Sim.BaseStorage.Organics:F0} kg)", () => Sim.TakeFromStorage(10));
        foreach (Bush b in Sim.Bushes)
            if (b.Ripe(Sim.Time)) Add(b.Position, 2.4, "E  pick fruit", () => Sim.PickBush(b));
        foreach (EscapePod pod in Sim.Pods)
            if (pod.Rations > 0) Add(pod.Position, 4.0, $"E  take {pod.Rations} ration packs", () => Sim.LootPod(pod));
        foreach (Backpack pack in Sim.World.Backpacks)
            Add(pack.Position, 2.6, $"E  recover your backpack ({pack.Contents.ItemCount} items)", () => Sim.Recover(pack));
        return list;
    }

    void UpdatePrompt()
    {
        if (Sim.Player.Dead || InventoryPanel.Visible)
        {
            Prompt = null;
            return;
        }
        var parts = new List<string>();
        var target = Targets().OrderBy(t => t.distance).FirstOrDefault();
        if (target.prompt != null) parts.Add(target.prompt);
        if (Sim.NearestReplicator(Sim.Player.Position, ReplicatorReach) is Replicator r)
        {
            double hunger = Sim.Data.Vitals.Food.Max - Sim.Player.Vitals.Food;
            FeedQuote q = r.Quote(hunger);
            string feed = hunger < 0.5 ? "F  feed (full)" : $"F  feed to full: {q.Organics:F1} kg organics, {q.Energy:F0} energy";
            string make = string.Join("  ", Sim.Data.Food.Foods.Where(r.CanMake).Select(f => $"{f.Tier} {f.Name.ToLowerInvariant()}"));
            parts.Add($"{r.Tier.Name.ToUpperInvariant()}  ·  {feed}  ·  make: {make}" + (r.Tier.Portable ? $"  ·  B pack up ({r.Energy.Available:F0} fuel)" : ""));
        }
        Prompt = parts.Count == 0 ? null : string.Join("\n", parts);
    }

    // ---- Menus, death and respawn --------------------------------------------------------

    public void Act(Outcome outcome)
    {
        Toast(outcome.Message);
        if (InventoryPanel.Visible) InventoryPanel.Refresh();
    }

    void OpenInventory()
    {
        InventoryPanel.Refresh();
        InventoryPanel.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    void CloseInventory()
    {
        InventoryPanel.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    void OpenDeathScreen()
    {
        InventoryPanel.Visible = false;
        DeathScreen.Open();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void RespawnAt(RespawnChoice choice)
    {
        Outcome back = Sim.Respawn(choice);
        Toast(back.Message);
        if (!back.Ok) return;
        DeathScreen.Visible = false;
        Vector3 at = GroundAt(Sim.Player.Position);
        Pos pack = Sim.LastBackpack?.Position ?? Sim.Player.DeathPosition;
        float yaw = Mathf.Atan2(-(float)(pack.X - at.X), -(float)(pack.Z - at.Z)); // face the backpack
        Player.Teleport(at, yaw);
        SyncWorld();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // ---- Messages --------------------------------------------------------------------------

    public void Toast(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _messages.Add((text, _clock));
        if (_messages.Count > 12) _messages.RemoveAt(0);
    }

    public void ClearMessages() => _messages.Clear();

    /// <summary>The last few messages and how many seconds old each one is.</summary>
    public IEnumerable<(string text, float age)> RecentMessages() =>
        _messages.Where(m => _clock - m.at < 6).TakeLast(4).Select(m => (m.text, (float)(_clock - m.at)));

    // ---- Making the world match the model ------------------------------------------------

    void SyncWorld()
    {
        foreach (Replicator r in Sim.Replicators.Where(r => !_replicators.ContainsKey(r)).ToList())
        {
            Vector3 at = GroundAt(r.Position);
            float yaw = 0;
            if (r.Tier.Portable && Player != null)
            {
                Vector3 toPlayer = Player.GlobalPosition - at;
                yaw = Mathf.RadToDeg(Mathf.Atan2(toPlayer.X, toPlayer.Z)); // hatch towards whoever set it down
            }
            Node3D node = Props.Replicator(r.Tier, at, yaw);
            AddChild(node);
            _replicators[r] = node;
        }
        foreach (Replicator gone in _replicators.Keys.Where(r => !Sim.Replicators.Contains(r)).ToList())
        {
            _replicators[gone].QueueFree();
            _replicators.Remove(gone);
        }

        foreach (Backpack pack in Sim.World.Backpacks.Where(b => !_backpacks.ContainsKey(b.Id)))
        {
            Node3D node = Props.BackpackMarker(GroundAt(pack.Position));
            AddChild(node);
            _backpacks[pack.Id] = node;
        }
        foreach (int gone in _backpacks.Keys.Where(id => Sim.World.Backpacks.All(b => b.Id != id)).ToList())
        {
            _backpacks[gone].QueueFree();
            _backpacks.Remove(gone);
        }

        foreach (EscapePod pod in Sim.Pods.Where(p => !_pods.ContainsKey(p)))
        {
            Node3D node = Props.EscapePod(GroundAt(pod.Position), wrecked: !pod.FreshStart);
            AddChild(node);
            _pods[pod] = node;
        }

        foreach (var (bush, fruit) in _fruit) fruit.Visible = bush.Ripe(Sim.Time);

        bool home = Sim.RespawnPoints.Any(p => p.Kind == RespawnKind.CloneChamber && p.IsHome);
        if (_homeLamp != null && home != _homeShown)
        {
            Props.SetHomeLamp(_homeLamp, home);
            _homeShown = home;
        }
    }

    public static Pos ToPos(Vector3 v) => new(v.X, v.Y, v.Z);

    public float GroundHeight(Pos p) => Sim.Planet.VolumeAt(new Pos(p.X, 0.1, p.Z)) != null ? 0.02f : Terrain.Height((float)p.X, (float)p.Z);

    public Vector3 GroundAt(Pos p) => new((float)p.X, GroundHeight(p) + 0.02f, (float)p.Z);

    // ---- Building the patch --------------------------------------------------------------

    void BuildPatch()
    {
        PlanetData p = Sim.Data.Planet;
        AddChild(Props.Shelter(p.Volumes[0]));
        foreach (RespawnPoint point in Sim.RespawnPoints)
        {
            Vector3 at = GroundAt(point.Position);
            switch (point.Kind)
            {
                case RespawnKind.HealingBooth: AddChild(Props.HealingBooth(at)); break;
                case RespawnKind.CloneChamber: AddChild(Props.CloneChamber(at, out _homeLamp)); break;
                case RespawnKind.SurvivalTent: AddChild(Props.Tent(at)); break;
            }
        }
        AddChild(Props.StorageCrate(GroundAt(p.Storage.Position)));

        Vector3 start = GroundAt(p.Start.Position);
        foreach (Zone zone in p.Zones)
        {
            AddChild(Props.ZoneDressing(zone, Terrain));
            // Three signs round the edge, the first facing the crash site: no hazard is a surprise.
            var center = new Vector2((float)zone.Center.X, (float)zone.Center.Z);
            float toStart = Mathf.Atan2(start.Z - center.Y, start.X - center.X);
            float ring = (float)(zone.Radius + zone.Falloff) + 2f;
            for (int i = 0; i < 3; i++)
            {
                float a = toStart + i * Mathf.Tau / 3f;
                float x = center.X + Mathf.Cos(a) * ring, z = center.Y + Mathf.Sin(a) * ring;
                AddChild(Props.HazardSign(zone.Sign, new Vector3(x, Terrain.Height(x, z), z), Mathf.RadToDeg(Mathf.Atan2(Mathf.Cos(a), Mathf.Sin(a)))));
            }
        }
        foreach (Territory land in p.Territories) AddChild(Props.TerritoryMarker(land, Terrain.Height));

        for (int i = 0; i < p.Rocks.Count; i++)
        {
            double[] rock = p.Rocks[i];
            float x = (float)rock[0], z = (float)rock[1];
            AddChild(Props.Rock(new Vector3(x, Terrain.Height(x, z) - 0.2f, z), (float)rock[2], i));
        }
        for (int i = 0; i < Sim.Bushes.Count; i++)
        {
            AddChild(Props.Bush(GroundAt(Sim.Bushes[i].Position), i, out Node3D fruit));
            _fruit[Sim.Bushes[i]] = fruit;
        }

        _spark = new Node3D { Name = "MiningSpark", Visible = false };
        _spark.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f }, MaterialOverride = Palette.Glow(Palette.Yellow, 6f) });
        _spark.AddChild(new OmniLight3D { LightColor = Palette.Yellow, LightEnergy = 2f, OmniRange = 3f });
        AddChild(_spark);
    }

    /// <summary>Flat ground and distant mesas past the patch's edge, so the horizon never ends in a void.</summary>
    void BuildHorizon()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(6000, 6000) },
            MaterialOverride = Palette.Solid(Palette.Sand.Darkened(0.08f), 1f),
            Position = new Vector3(0, -0.8f, 0),
        });
        var rng = new RandomNumberGenerator { Seed = 1977 };
        Color[] rock = { Palette.Rust, new("b5651d"), new("8b3a1e"), new("d2915a") };
        for (int i = 0; i < 60; i++)
        {
            float a = rng.RandfRange(0, Mathf.Tau), d = rng.RandfRange(220, 1400);
            float width = rng.RandfRange(15, 70), height = rng.RandfRange(15, 110);
            bool spire = rng.Randf() < 0.3f;
            Vector3 size = spire ? new Vector3(width * 0.3f, height * 1.5f, width * 0.3f) : new Vector3(width, height * 0.5f, width * rng.RandfRange(0.6f, 1.4f));
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = Palette.Solid(rock[i % rock.Length], 1f),
                Position = new Vector3(Mathf.Cos(a) * d, size.Y / 2 - 1f, Mathf.Sin(a) * d),
                RotationDegrees = new Vector3(0, rng.RandfRange(0, 90), 0),
            });
        }
    }

    // ---- Sky, day and night --------------------------------------------------------------

    void BuildSky()
    {
        _sky = new ProceduralSkyMaterial { SunAngleMax = 30f, GroundBottomColor = new Color("6d2e1f") };
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogDensity = 0.0016f,
        };
        AddChild(new WorldEnvironment { Environment = _environment });

        _sun = new DirectionalLight3D { LightColor = new Color("ffd6a0"), ShadowEnabled = true, DirectionalShadowMaxDistance = 140f };
        AddChild(_sun);
        _moonLight = new DirectionalLight3D { LightColor = new Color("8fa8ff"), RotationDegrees = new Vector3(-50, -150, 0) };
        AddChild(_moonLight);

        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 420f, Height = 840f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("7fb7be"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true },
            Position = new Vector3(2900f, 1500f, -3900f),
        });

        // Stars: a scatter of tiny unshaded quads far away, faded in at night.
        _starMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            DisableFog = true,
            AlbedoColor = new Color(1, 1, 1, 0),
        };
        var stars = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new QuadMesh { Size = new Vector2(7, 7), Material = _starMaterial }, InstanceCount = 500 };
        var rng = new RandomNumberGenerator { Seed = 42 };
        for (int i = 0; i < stars.InstanceCount; i++)
        {
            Vector3 dir = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(0.08f, 1), rng.RandfRange(-1, 1)).Normalized();
            stars.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * rng.RandfRange(0.4f, 1.3f)), dir * 3500f));
        }
        AddChild(new MultiMeshInstance3D { Multimesh = stars, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        UpdateSky();
    }

    void UpdateSky()
    {
        if (Sim == null || _sky == null) return;
        float hour = (float)Sim.Planet.HourOfDay(Sim.Time);
        float height = Mathf.Sin(Mathf.Pi * (hour - 6f) / 12f);          // 1 at noon, 0 at dawn and dusk
        float day = Mathf.Clamp(height * 2.5f + 0.25f, 0, 1);
        float dusk = Mathf.Clamp(1 - Mathf.Abs(height) * 3.5f, 0, 1);

        _sun.RotationDegrees = new Vector3(-Mathf.Max(height, 0.03f) * 62f, 40f + (hour - 12f) * 12f, 0);
        _sun.LightEnergy = Mathf.Clamp(height * 3f, 0, 1) * 1.3f;
        _sun.Visible = height > -0.02f;
        _moonLight.LightEnergy = (1 - day) * 0.28f;

        var top = new Color("070a1c").Lerp(new Color("1d3557"), day);
        var horizon = new Color("1e1838").Lerp(new Color("f4a261"), day).Lerp(new Color("e76f51"), dusk * 0.6f);
        _sky.SkyTopColor = top;
        _sky.SkyHorizonColor = horizon;
        _sky.GroundHorizonColor = horizon.Darkened(0.15f);
        _environment.AmbientLightEnergy = 0.2f + 0.5f * day;
        _environment.FogLightColor = horizon;
        _starMaterial.AlbedoColor = new Color(1, 1, 1, Mathf.Clamp(1 - day * 1.6f, 0, 1));
    }

    // ---- Test hooks ----------------------------------------------------------------------

    /// <summary>Mines as if the left mouse button were held. For tests and screenshots.</summary>
    public bool DebugMining;

    /// <summary>Puts the player somewhere on the patch, facing a direction. For tests and screenshots.</summary>
    public void DebugPlace(double x, double z, float yawDegrees, float pitchDegrees = 0)
    {
        Vector3 at = GroundAt(Pos.Flat(x, z));
        Player.Teleport(at, Mathf.DegToRad(yawDegrees), Mathf.DegToRad(pitchDegrees));
        Sim.Player.Position = ToPos(at);
    }
}
