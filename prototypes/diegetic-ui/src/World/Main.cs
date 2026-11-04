using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Logic;
using DiegeticUi.Pages;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
using Godot;

namespace DiegeticUi.World;

/// <summary>
/// Builds the prototype in code: a ship's bridge in orbit of a ringed giant, with a systems
/// console, a holo-table, a constructor, a trade terminal and a cold store; a first-person
/// player with a wrist computer and a helmet visor. Then runs the simulation behind them.
///
/// Debug keys: C shoves you into the cold store, I starts the invasion warning in fast time
/// (a minute a second), F fires a warning shot past the ship.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>The invasion countdown runs this many times faster than real time.</summary>
    public const double FastTime = 60;

    public GameState State { get; } = new();
    public Player Player { get; private set; }
    public Camera3D Camera { get; private set; }
    public Interactor Interactor { get; private set; }
    public ScreenManager Screens { get; private set; }
    public Visor Visor { get; private set; }
    public Wrist Wrist { get; private set; }
    public HoloTable HoloTable { get; private set; }
    public WorldScreen ConsoleScreen { get; private set; }
    public WorldScreen ConstructorScreen { get; private set; }
    public WorldScreen TraderScreen { get; private set; }
    public ConsolePage ConsolePage { get; private set; }
    public ConstructorPage ConstructorPage { get; private set; }
    public TraderPage TraderPage { get; private set; }
    public List<Siren> Sirens { get; } = new();

    /// <summary>The warning clock's speed: 1 normally, <see cref="FastTime"/> during the debug invasion.</summary>
    public double WarningClockScale { get; set; } = 1;

    public bool InColdStore { get; private set; }

    readonly List<RigidBody3D> _crates = new();
    readonly Dictionary<VitalKind, VitalState> _shownStates = new();
    Node3D _lever;
    StandardMaterial3D _shieldLamp;
    MeshInstance3D _shieldBubble;
    AudioStreamPlayer _sirenSound, _chirp;
    bool _gravityShown = true, _shieldsShown;
    double _sirensOffAt = -1, _time;
    int _shots;

    public override void _Ready()
    {
        BuildSpace();
        AddChild(new Bridge { Name = "Bridge" });
        BuildProps();
        BuildCrates();

        Camera = new Camera3D { Name = "Camera", Fov = 75, Near = 0.03f, Far = 6000, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        AddChild(Camera);
        Camera.MakeCurrent();

        Player = new Player { Name = "Player" };
        AddChild(Player);
        Player.Place(Bridge.Spawn, 0);

        Interactor = new Interactor { Name = "Interactor", Camera = Camera, HeadView = Player.HeadView, BlockMask = Layers.World };
        AddChild(Interactor);
        Screens = new ScreenManager { Name = "ScreenManager", Camera = Camera };
        AddChild(Screens);

        BuildScreens();

        Wrist = new Wrist { Name = "Wrist", Interactor = Interactor, State = State, HazardsHere = () => InColdStore ? Hazard.Cold : Hazard.None };
        Camera.AddChild(Wrist);

        Visor = new Visor { Name = "Visor", State = State, Player = Player, Interactor = Interactor, Camera = Camera };
        AddChild(Visor);

        _sirenSound = new AudioStreamPlayer { Stream = Sfx.SirenLoop(), VolumeDb = -16 };
        _chirp = new AudioStreamPlayer { Stream = Sfx.WarningChirp(), VolumeDb = -9 };
        AddChild(_sirenSound);
        AddChild(_chirp);

        foreach (Vital v in State.Vitals.All) _shownStates[v.Kind] = v.State;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Visor.Say("VESTA: ONLINE. TAB FOR YOUR WRIST.");
    }

    // ---- Input ------------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (Interactor.HandleInput(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Tab: ToggleWrist(); return;
                case Key.C: ShoveIntoColdStore(); return;
                case Key.I: StartInvasion(); return;
                case Key.F: WarningShot(); return;
            }
        }
        Player.HandleInput(e);
    }

    public void ToggleWrist()
    {
        if (Interactor.Focus.State == FocusState.Free) Interactor.Enter(Wrist.Screen);
        else if (Interactor.Focus.Target == Wrist.Screen) Interactor.Back();
    }

    /// <summary>Debug: a shove through the cold store's door, so the temperature vital has something to do.</summary>
    public void ShoveIntoColdStore()
    {
        Interactor.Back();
        Player.Place(Bridge.ColdDoor, -Mathf.Pi / 2, 0, new Vector3(3.5f, 2f, 0)); // a stumble in through the doorway
        Visor.Say("VESTA: HULL SECTION AT -60°C");
    }

    /// <summary>Debug: an invasion fleet ten minutes out, in fast time (a minute a second).</summary>
    public void StartInvasion()
    {
        if (State.Warnings.Next != null) return;
        State.Warnings.Schedule("invasion", "INVASION FLEET INBOUND", 600.2);
        WarningClockScale = FastTime;
        Announce(State.Warnings.Advance(0));
    }

    /// <summary>
    /// Debug: a hostile's warning shot, meant to miss. A tracer streaks past outside, the visor
    /// flashes on the side it came from and the cockpit chirps.
    /// </summary>
    public void WarningShot(float? bearingDegrees = null)
    {
        float bearing = Mathf.DegToRad(bearingDegrees ?? (_shots++ * 137.5f + 20) % 360);
        var from = new Vector3(Mathf.Sin(bearing), 0.15f, -Mathf.Cos(bearing)) * 90 + new Vector3(0, 2, -1);
        var miss = new Vector3(-from.Z, 0, from.X).Normalized() * 9 + new Vector3(0, 6, -1); // wide of the hull
        AddChild(new Tracer { From = from, To = miss + (miss - from).Normalized() * 60 });
        Visor.UnderFire(from);
        _chirp.Play();
    }

    // ---- Simulation ---------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        _time += delta;

        InColdStore = Bridge.ColdStore.HasPoint(Player.GlobalPosition + Vector3.Up * 0.5f);
        Surroundings around = InColdStore ? new Surroundings { AmbientC = -60, Breathable = true } : Surroundings.ShipInterior;
        State.Vitals.Tick(delta, around, State.Loadout.Protection);
        Visor.FrostTarget = InColdStore ? 0.7f : 0f;
        ReportVitals();

        Announce(State.Warnings.Advance(delta * WarningClockScale));
        UpdateSirens();

        State.Constructor.Tick(delta);
        State.Map.Clock += delta;

        UpdateProps(delta);

        Player.LookLocked = Interactor.LookLocked;
        Player.MoveLocked = Interactor.Focus.Busy && Interactor.Focus.Target.MovesCamera;
    }

    void ReportVitals()
    {
        foreach (Vital v in State.Vitals.All)
        {
            VitalState was = _shownStates[v.Kind], now = v.State;
            if (was == now) continue;
            _shownStates[v.Kind] = now;
            if (now == VitalState.Warning && was == VitalState.Ok)
                Visor.Say(v.Kind == VitalKind.Temperature ? "VESTA: CORE TEMP FALLING. GET WARM." : $"VESTA: {v.Label} LOW", UiTheme.Colors.Warn);
            else if (now == VitalState.Critical)
                Visor.Say($"VESTA: {v.Label} CRITICAL. YOU ARE HURT.", UiTheme.Colors.Danger);
            else if (now == VitalState.Ok)
                Visor.Say($"VESTA: {v.Label} NOMINAL", UiTheme.Colors.Good);
        }
    }

    void Announce(List<Announcement> fired)
    {
        foreach (Announcement a in fired)
        {
            Visor.Announce(a);
            float speed = a.MinutesLeft switch { 10 => 0.5f, 5 => 0.8f, 1 => 1.4f, _ => 1.8f };
            foreach (Siren siren in Sirens)
            {
                siren.SetActive(true);
                siren.Speed = speed;
            }
            if (!_sirenSound.Playing) _sirenSound.Play();
            _sirenSound.PitchScale = a.MinutesLeft <= 1 ? 1.25f : 1f;
            if (a.MinutesLeft == 0)
            {
                _sirensOffAt = _time + 8;
                WarningClockScale = 1;
            }
        }
    }

    void UpdateSirens()
    {
        bool alert = State.Warnings.Next != null;
        foreach (WorldScreen screen in new[] { ConsoleScreen, ConstructorScreen, TraderScreen })
            screen.SetAlert(alert ? 0.7f : 0f);
        if (_sirensOffAt > 0 && _time > _sirensOffAt)
        {
            _sirensOffAt = -1;
            foreach (Siren siren in Sirens) siren.SetActive(false);
            _sirenSound.Stop();
        }
    }

    void UpdateProps(double delta)
    {
        // The real lever on the desk follows the one on the screen.
        float target = Mathf.DegToRad(State.Ship.CoreGravity ? 32 : -32);
        _lever.Rotation = new Vector3(Mathf.MoveToward(_lever.Rotation.X, target, (float)delta * 5), 0, 0);

        if (State.Ship.Shields != _shieldsShown)
        {
            _shieldsShown = State.Ship.Shields;
            _shieldLamp.EmissionEnergyMultiplier = _shieldsShown ? 4 : 0;
            _shieldBubble.Visible = _shieldsShown;
            Visor.Say(_shieldsShown ? "VESTA: SHIELDS UP" : "VESTA: SHIELDS DOWN", UiTheme.Colors.Good);
        }

        if (State.Ship.CoreGravity != _gravityShown)
        {
            _gravityShown = State.Ship.CoreGravity;
            var rng = new RandomNumberGenerator { Seed = 11 };
            foreach (RigidBody3D crate in _crates)
            {
                crate.GravityScale = _gravityShown ? 1 : 0;
                crate.Sleeping = false;
                if (!_gravityShown)
                    crate.ApplyCentralImpulse(new Vector3(rng.RandfRange(-0.4f, 0.4f), rng.RandfRange(0.8f, 1.6f), rng.RandfRange(-0.4f, 0.4f)) * crate.Mass);
            }
            Visor.Say(_gravityShown ? "VESTA: CORE GRAVITY RESTORED" : "VESTA: CORE GRAVITY OFF. MAG BOOTS ON.");
        }
    }

    // ---- Building ----------------------------------------------------------------------------

    void BuildScreens()
    {
        ConsolePage = new ConsolePage(State.Ship, State.Roster, State.Warnings);
        ConsoleScreen = Screen("ConsoleScreen", "SHIP SYSTEMS", Bridge.ConsoleScreen, new Vector2(1.28f, 0.72f), ConsolePage);

        ConstructorPage = new ConstructorPage(State.Constructor, State.Warnings);
        ConstructorScreen = Screen("ConstructorScreen", "CONSTRUCTOR", Bridge.ConstructorScreen, new Vector2(1.28f, 0.72f), ConstructorPage);

        TraderPage = new TraderPage(State.Trader, State.Suit, State.Hold, State.Wallet, State.Warnings);
        TraderScreen = Screen("TraderScreen", "TRADE LINK", Bridge.TraderScreen, new Vector2(1.28f, 0.72f), TraderPage);

        HoloTable = new HoloTable { Name = "HoloTable", Map = State.Map, Position = Bridge.HoloTable };
        AddChild(HoloTable);
    }

    WorldScreen Screen(string name, string prompt, Transform3D at, Vector2 size, Control page)
    {
        var screen = new WorldScreen { Name = name, Prompt = prompt, SizeMeters = size, Transform = at };
        AddChild(screen);
        screen.SetContent(page);
        return screen;
    }

    void BuildProps()
    {
        // The core-gravity lever: a real handle beside the screen, thrown by the on-screen one.
        _lever = new Node3D { Name = "GravityLever", Position = Bridge.LeverProp + new Vector3(0, 0.08f, 0) };
        AddChild(_lever);
        _lever.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.34f, 0.05f) }, Position = new Vector3(0, 0.17f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("8d99ae"), Metallic = 0.7f, Roughness = 0.3f } });
        _lever.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.065f, Height = 0.13f }, Position = new Vector3(0, 0.36f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Bridge.Orange, Roughness = 0.4f } });

        // The shield switch: a chunky toggle with a lamp that lights while the shields are up.
        _shieldLamp = new StandardMaterial3D { AlbedoColor = new Color("0a3d3f"), EmissionEnabled = true, Emission = new Color("1fd6c4"), EmissionEnergyMultiplier = 0 };
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f }, MaterialOverride = _shieldLamp, Position = Bridge.ShieldProp + new Vector3(0, 0.12f, 0) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.16f, 0.06f) }, Position = Bridge.ShieldProp + new Vector3(0.13f, 0.14f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Bridge.Red, Roughness = 0.5f } });

        foreach (Vector3 p in Bridge.Sirens)
        {
            var siren = new Siren { Position = p };
            AddChild(siren);
            Sirens.Add(siren);
        }
    }

    void BuildCrates()
    {
        Color[] colours = { Bridge.Orange, Bridge.Yellow, Bridge.Teal, Bridge.Red, Bridge.Cream };
        Vector3[] spots = { new(-4.7f, 0.4f, 3.2f), new(-4.0f, 0.38f, 4.3f), new(-4.9f, 0.45f, 4.9f), new(-4.6f, 1.15f, 3.4f), new(-3.3f, 0.35f, 5.0f) };
        for (int i = 0; i < spots.Length; i++)
        {
            Vector3 size = Vector3.One * (0.6f + 0.1f * (i % 3));
            var crate = new RigidBody3D { Mass = 30, LinearDamp = 0.4f, AngularDamp = 0.4f, CollisionLayer = Layers.Crates, CollisionMask = Layers.World | Layers.Crates | Layers.Player, Position = spots[i] };
            crate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            crate.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = colours[i], Roughness = 0.8f } });
            AddChild(crate);
            _crates.Add(crate);
        }
    }

    void BuildSpace()
    {
        var sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/space_sky.gdshader") };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size64 },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("b9a9c9"),
            AmbientLightEnergy = 0.5f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapWhite = 6,
            GlowEnabled = true,
            GlowIntensity = 0.55f,
            GlowStrength = 1.0f,
            GlowBloom = 0.02f,
            GlowHdrThreshold = 1.05f, // screens stay just under it; lamps, holograms and the sun bloom
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        var sun = new DirectionalLight3D { Name = "Sun", LightColor = new Color("ffe0b8"), LightEnergy = 1.5f, ShadowEnabled = true, DirectionalShadowMaxDistance = 30 };
        AddChild(sun);
        sun.LookAtFromPosition(Vector3.Zero, new Vector3(-0.5f, -0.38f, 0.78f), Vector3.Up);

        // The ringed giant hanging off the bow, lit from the right.
        var giant = new Node3D { Name = "Giant", Position = new Vector3(-0.5f, 0.3f, -1f).Normalized() * 2600 };
        AddChild(giant);
        giant.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 700, Height = 1400, RadialSegments = 96, Rings = 48 },
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/gas_giant.gdshader") },
            RotationDegrees = new Vector3(0, 0, 14),
        });
        var ring = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/planet_ring.gdshader") };
        ring.SetShaderParameter("inner", 0.62f);
        giant.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(2, 2) },
            MaterialOverride = ring,
            Scale = Vector3.One * 1500,
            RotationDegrees = new Vector3(14, 25, 14),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // The shield bubble: a faint skin of light outside the hull while the shields are up.
        var bubble = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hologram.gdshader") };
        bubble.SetShaderParameter("tint", new Color("1fd6c4"));
        bubble.SetShaderParameter("intensity", 0.35f);
        bubble.SetShaderParameter("body", 0.08f);
        bubble.SetShaderParameter("rim", 0.6f);
        bubble.SetShaderParameter("band_density", 1.5f);
        _shieldBubble = new MeshInstance3D { Mesh = new SphereMesh { Radius = 22, Height = 44 }, MaterialOverride = bubble, Position = new Vector3(0, 2, -1), Visible = false };
        AddChild(_shieldBubble);
    }
}

/// <summary>A warning shot: a bright bolt that streaks past the ship and is gone.</summary>
public partial class Tracer : MeshInstance3D
{
    public Vector3 From { get; init; }
    public Vector3 To { get; init; }
    const float Seconds = 0.45f;
    float _t;

    public override void _Ready()
    {
        Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 14 };
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color("ff7a2a"),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color("ff7a2a"),
            EmissionEnergyMultiplier = 8,
        };
        CastShadow = ShadowCastingSetting.Off;
        Place();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta / Seconds;
        if (_t >= 1)
        {
            QueueFree();
            return;
        }
        Place();
    }

    void Place()
    {
        Vector3 dir = (To - From).Normalized();
        Vector3 at = From.Lerp(To, _t);
        // A cylinder runs along Y: aim its Y axis down the flight path.
        Vector3 side = dir.Cross(Vector3.Up).LengthSquared() > 1e-4f ? dir.Cross(Vector3.Up).Normalized() : Vector3.Right;
        GlobalTransform = new Transform3D(new Basis(side, dir, side.Cross(dir)), at);
    }
}
