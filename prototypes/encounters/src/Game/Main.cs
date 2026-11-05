using System.Collections.Generic;
using System.Threading.Tasks;
using Encounters.Director;
using Godot;

namespace Encounters;

/// <summary>
/// Builds the prototype in code: two zones (Veyra orbit with its station and ringed giant, and
/// the Ashen Drift's asteroids under a red dwarf), the player's skiff, and the encounter
/// director dealing events around it. The director decides; this scene only feeds it the
/// player's view and draws what it reports. J jumps between the zones with a fade.
/// </summary>
public partial class Main : Node3D
{
    public const int LocalPlayer = 0;
    public const ulong Seed = 1977;
    public static readonly string[] PlayableZones = { "veyra-orbit", "drift-deep" };

    public EncounterDirector Director { get; private set; }
    public PlayerCraft Craft { get; private set; }
    public Camera3D Camera => Craft.Camera;
    public string ZoneId { get; private set; }
    public ZoneDef Zone => Director.Zones.Get(ZoneId);
    public IReadOnlyDictionary<long, EntityView> Views => _views;
    public Hud Hud { get; private set; }
    public DirectorOverlay Overlay { get; private set; }
    public DirectorMap Map { get; private set; }
    public Fx Fx { get; private set; }

    readonly Dictionary<long, EntityView> _views = new();
    readonly List<long> _gone = new();
    WorldEnvironment _environment;
    ShaderMaterial _sky;
    DirectionalLight3D _sun;
    Node3D _scenery;
    ColorRect _fade;
    int _frame, _physicsFrames;
    bool _switching;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 10; // after the skiff has moved
        Director = new EncounterDirector(GameData.Config(), GameData.Zones(), GameData.Deck(), Seed);
        Director.Logged += OnLogged;

        BuildEnvironment();
        Fx = new Fx { Name = "Fx" };
        AddChild(Fx);
        Craft = new PlayerCraft { Name = "Skiff" };
        AddChild(Craft);
        BuildUi();
        EnterZone(PlayableZones[0]);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _PhysicsProcess(double delta)
    {
        // The first ticks pay for JIT compilation; keep them out of the overlay's tick stats.
        if (++_physicsFrames == 90) Director.Stats.Reset();
        if (!_switching) FeedDirector();
        Director.Advance(delta);
        SyncViews((float)delta);
        Fx.Step((float)delta, Director.Instance(ZoneId), _views);
    }

    /// <summary>The host tells the director where each player is and where they look.</summary>
    void FeedDirector() =>
        Director.SetPlayer(LocalPlayer, ZoneId, Camera.GlobalPosition.ToNum(), (-Camera.GlobalBasis.Z).ToNum());

    /// <summary>One view per entity the director lists in this zone: transient ships and lasting records alike.</summary>
    void SyncViews(float dt)
    {
        _frame++;
        double time = Director.RenderTime;
        ZoneInstance zi = Director.Instance(ZoneId);
        if (zi != null)
            foreach (WorldEntity e in zi.Ships) Show(e, time, dt);
        foreach (WorldEntity e in Director.Records.InZone(ZoneId)) Show(e, time, dt);

        _gone.Clear();
        foreach (var (id, view) in _views)
            if (view.SeenFrame != _frame) _gone.Add(id);
        foreach (long id in _gone)
        {
            _views[id].QueueFree();
            _views.Remove(id);
        }
    }

    void Show(WorldEntity e, double time, float dt)
    {
        if (!_views.TryGetValue(e.Id, out EntityView view))
        {
            view = EntityView.Create(e, Fx);
            AddChild(view);
            _views[e.Id] = view;
        }
        view.SeenFrame = _frame;
        view.Sync(time, dt);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        Craft.HandleInput(e);
        if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.PhysicalKeycode)
        {
            case Key.Tab: Overlay.Visible = !Overlay.Visible; break;
            case Key.M: Map.Visible = !Map.Visible; break;
            case Key.E: TouchNearest(); break;
            case Key.C: DropCargo(); break;
            case Key.J: _ = JumpZone(); break;
            case Key.V: Craft.Cockpit = !Craft.Cockpit; break;
            case Key.Escape: Input.MouseMode = Input.MouseModeEnum.Visible; break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    void TouchNearest()
    {
        if (Hud.TouchTarget == null) return;
        TouchOutcome outcome = Director.Touch(LocalPlayer, Hud.TouchTarget.Id);
        if (outcome.Status == TouchStatus.TooFar) Hud.Toast("Too far away to touch it", Palette.TextDim, 2);
    }

    /// <summary>Something the player drops is theirs: straight into the permanent records, never cleaned up.</summary>
    void DropCargo() =>
        Director.RegisterPlayerOwned(LocalPlayer, ZoneId, EntityKind.PlayerCargo, "cargo-pod", "Cargo pod",
            (Craft.GlobalPosition + Craft.GlobalBasis.Z * 14f).ToNum(), (-Craft.GlobalBasis.Z).ToNum());

    async Task JumpZone()
    {
        if (_switching) return;
        _switching = true;
        await FadeTo(1f, 0.45f);
        int next = (System.Array.IndexOf(PlayableZones, ZoneId) + 1) % PlayableZones.Length;
        EnterZone(PlayableZones[next]);
        await FadeTo(0f, 0.7f);
        _switching = false;
    }

    async Task FadeTo(float alpha, float seconds)
    {
        Tween tween = CreateTween();
        tween.TweenProperty(_fade, "color:a", alpha, seconds);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    /// <summary>
    /// Moves the player into a zone: new scenery and sky, the skiff at the zone's entry point.
    /// Telling the director closes the old zone's instance, which cleans up its untouched events.
    /// </summary>
    public void EnterZone(string zoneId)
    {
        ZoneId = zoneId;
        foreach (EntityView view in _views.Values) view.QueueFree();
        _views.Clear();
        Fx.Clear();

        ZoneLook look = ZoneLook.For(zoneId);
        _scenery?.QueueFree();
        _scenery = ZoneScenery.Build(Zone, look);
        AddChild(_scenery);
        look.Apply(_sky, _sun, _environment.Environment);

        Craft.CameraOverride = null;
        Craft.Place(new Vector3(0, 40, 0), new Vector3(0, 0, -1000));
        FeedDirector();
    }

    void OnLogged(LogEntry e)
    {
        if (Hud == null) return;
        if (e.Zone != ZoneId && e.Kind != "close") return;
        switch (e.Kind)
        {
            case "lead":
                if (Director.Leads.Count > 0 && Director.Leads[^1].PlayerId == LocalPlayer)
                {
                    SignalLead lead = Director.Leads[^1];
                    Hud.Toast(lead.Kind switch
                    {
                        "distress" => $"Distress call picked up, ~{Hud.Km(lead.Distance)} away",
                        "lifesign" => $"Life signs: {lead.Label}, ~{Hud.Km(lead.Distance)} away",
                        _ => $"Transmission: \"{lead.Label}\", ~{Hud.Km(lead.Distance)} away",
                    }, Palette.Lead(lead.Kind));
                }
                break;
            case "wreck":
                if (Director.Records.TryGet(e.Id, out WorldEntity wreck))
                    Hud.Toast($"{Hud.Describe(wreck, Director)} lost. Its wreck stays as salvage", Palette.Distress);
                break;
            case "touch":
                if (Director.Records.TryGet(e.Id, out WorldEntity touched))
                    Hud.Toast(touched.Touch == TouchKind.Claimed
                        ? $"Wreck of a {Hud.Describe(touched, Director)} claimed as salvage: it's yours"
                        : $"{Hud.Describe(touched, Director)} touched: now a permanent record, still its crew's", Palette.Lasting);
                break;
            case "owned":
                Hud.Toast("Cargo pod dropped: yours, it never goes away", Palette.Lasting, 3);
                break;
            case "close":
                Hud.Toast($"Left {Director.Zones.Get(e.Zone).Name}: {e.Text}", Palette.TextDim, 6);
                break;
        }
    }

    void BuildEnvironment()
    {
        _sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/space_sky.gdshader") };
        var environment = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size128 },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.6f,
            ReflectedLightSource = Environment.ReflectionSource.Sky,
            TonemapMode = Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            GlowEnabled = true,
            GlowIntensity = 0.7f,
            GlowBloom = 0.08f,
            GlowHdrThreshold = 1.1f,
            GlowBlendMode = Environment.GlowBlendModeEnum.Additive,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.12f,
            AdjustmentContrast = 1.05f,
        };
        _environment = new WorldEnvironment { Environment = environment };
        AddChild(_environment);

        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 1800f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
        };
        AddChild(_sun);
    }

    void BuildUi()
    {
        var layer = new CanvasLayer { Name = "Ui" };
        AddChild(layer);
        Hud = new Hud { Name = "Hud", Main = this };
        layer.AddChild(Hud);
        Map = new DirectorMap { Name = "Map", Main = this, Visible = false };
        layer.AddChild(Map);
        Overlay = new DirectorOverlay { Name = "Overlay", Main = this, Visible = false };
        layer.AddChild(Overlay);
        _fade = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_fade);
    }
}
