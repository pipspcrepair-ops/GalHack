using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using ShipHandling.Handling;
using Slice.Boot;
using Slice.Construction;
using Slice.Items;
using Slice.Player;
using Slice.Surface;
using Slice.Ui;
using Slice.Vessels;
using SurvivalVitals.Model;
using Progress = Slice.Items.Progress;
using Weather.Model;
using Wildlife;
using Survivor = Slice.Player.Survivor;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Game;

/// <summary>Where the run is: the planet generating, the pod coming down, or play.</summary>
public enum Phase { Generating, Landing, Playing }

/// <summary>
/// The playable slice: crash-land in an escape pod, survive, gather, craft, build a base and
/// machines, build a vessel and fly it. It owns the surface zone and the castaway (body, suit,
/// inventory, tools and progress), turns one tick's <see cref="Controls"/> into what they do, and
/// places the camera. Tests and screenshots run this same node and drive it through
/// <see cref="Controls"/>.
/// </summary>
public partial class Game : Node3D
{
    /// <summary>False for tests and screenshots: no mouse capture, no keyboard; a script fills <see cref="Controls"/>.</summary>
    public bool Interactive = true;

    /// <summary>Start standing beside a landed pod instead of coming down in it (the systems test only; the real-player test plays the descent).</summary>
    public bool SkipIntro;

    /// <summary>
    /// TEST TIME-SCALE: crafting runs this many times faster. 1 in play; the real-player test sets
    /// it (and prints that it did) so an hour of crafting fits in a test run. Nothing is skipped.
    /// </summary>
    public double CraftTimeScale = 1;

    public readonly Controls Controls = new();

    public SurfaceZone Zone { get; private set; }
    public Camera3D Camera { get; private set; }
    public Phase Phase { get; private set; } = Phase.Generating;
    public Survivor Survivor { get; private set; }
    public Suit Suit { get; private set; }
    public PlayerInventory Pack { get; private set; }
    public Progress Progress { get; private set; }
    public SurvivalTool Tool { get; private set; }
    public BuildTool Builder { get; private set; }
    public ShotFx Shots { get; private set; }
    public Vessel Pod { get; private set; }
    public PlayerTarget Target { get; private set; }
    public Visor Visor { get; private set; }
    public TabMenu Menu { get; private set; }
    public CockpitConsole Console { get; private set; }
    /// <summary>The in-world screens on constructors and base cores (looked at to use).</summary>
    public MachineScreens MachineScreens { get; private set; }
    public DebugHud Debug { get; private set; }
    public Hands Hands { get; private set; }
    public SpotLight3D Torch { get; private set; }
    public GameStats Stats { get; } = new();
    public SliceAudio Audio { get; private set; }

    /// <summary>The suit fabricator (You tab): the bare minimum, from what is in your bag.</summary>
    public GalHack.Items.Production.Constructor Fabricator { get; private set; }

    /// <summary>Building pays materials (Survival) or costs nothing (Creative): the world's one setting.</summary>
    public GameMode Mode
    {
        get => Zone?.WorldRules.Mode ?? _mode;
        set
        {
            _mode = value;
            if (Zone != null) Zone.WorldRules.Mode = value;
            if (Progress != null) Progress.Creative = value == GameMode.Creative;
        }
    }
    GameMode _mode = GameMode.Survival;

    /// <summary>The starter vessel's blueprint, as the data has it.</summary>
    public Structure StarterPlan => _starter ??= Zone.Data.Blueprint(Zone.Data.Rules.Build.StarterBlueprint);
    Structure _starter;

    /// <summary>Chase camera instead of first person.</summary>
    public bool ThirdPerson;

    /// <summary>Screenshots: the camera stays here.</summary>
    public Transform3D? FixedCamera;

    /// <summary>A line for the visor, and its colour.</summary>
    public event Action<string, Color> Said;

    /// <summary>The pod has come down and the survivor can move (fires once).</summary>
    public event Action Landed;

    Vector2 _stick;
    readonly Dictionary<int, Node3D> _replicators = new();
    float _landingTime, _landingLength, _dropHeight;
    N.Vector3 _podRest;
    float _podHeading;
    bool _waitingForGround;
    readonly List<WarningEntry> _warnings = new();

    public override void _Ready()
    {
        ResourceMount.Mount();
        // Creative is chosen for the session at launch: godot --path prototypes/slice -- --creative
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--creative") >= 0) _mode = GameMode.Creative;
        ProcessPhysicsPriority = -20; // input before the physics host (-10) steps
        Zone = new SurfaceZone { Name = "Surface" };
        AddChild(Zone);
        Zone.WorldRules.Mode = _mode;
        // Placed every frame from interpolated transforms, so it is not interpolated itself.
        Camera = new Camera3D { Name = "Camera", Fov = 75f, Near = 0.05f, Far = 12000f, Current = true, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        Zone.AddChild(Camera);
        Zone.Camera = Camera;
        Zone.Focus = Focus;
        Zone.Built += OnPlanetBuilt;
        AddChild(new AfterStep { Name = "AfterStep", Game = this });
        AddChild(new DiegeticUi.Screens.ScreenManager { Name = "Screens", Camera = Camera });
        Visor = new Visor { Name = "Visor", Game = this };
        AddChild(Visor);
        Menu = new TabMenu { Name = "Menu", Game = this };
        AddChild(Menu);
        Debug = new DebugHud { Name = "Debug", Game = this, Visible = false };
        AddChild(Debug);
        Said += (text, colour) => Visor.Say(text, colour);
        if (Interactive)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            // Closing the window quits through wildlife's shutdown: sound drained, caches released.
            GetTree().AutoAcceptQuit = false;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && Interactive) Wildlife.Shutdown.Quit(this);
    }

    Vector3 Focus()
    {
        if (Survivor == null) return Pod?.Body.Position.ToGodot() ?? (Zone.IsBuilt ? Zone.LandingSite : Vector3.Zero);
        if (Survivor.Seated is Vessel v)
        {
            // Streaming leads a flying vessel a little, so the ground ahead has collision when it gets there.
            Vector3 lead = v.Body.Velocity.ToGodot() * 0.5f;
            lead.Y = 0f;
            return v.Body.Position.ToGodot() + lead.LimitLength(32f);
        }
        return Survivor.WorldFeet;
    }

    public void Say(string text, Color colour) => Said?.Invoke(text, colour);

    // ---- Arriving ---------------------------------------------------------------------------

    void OnPlanetBuilt()
    {
        SliceRules.PlanetRules rules = Zone.Data.Rules.Planet;
        GameData data = Zone.Data;

        // The pod comes down at the generator's landing site, standing level on its highest ground.
        Vector3 landing = Zone.LandingSite;
        _podHeading = -(float)Zone.Planet.Landing.Heading;
        Structure pod = data.Blueprint("pod.gbp");
        // It comes to rest with its hatch sill a short step above the ground in front of it, so
        // you can walk out and back in; where the ground under it is higher, its landing carves
        // its bed (see Descend).
        N.Quaternion turn = MathX.Yaw(_podHeading);
        Vector3 hatch = landing + MathX.Rotate(turn, new N.Vector3(0f, 0f, -2.4f)).ToGodot();
        float sill = MathF.Max(Zone.GroundAt(hatch.X, hatch.Z).Y, Zone.GroundAt(landing.X, landing.Z).Y - 0.6f);
        _podRest = new N.Vector3(landing.X, sill + PhysicsSandbox.Bridges.StructureBridge.ShapeOf(pod).FeetBelowOrigin + 0.15f, landing.Z);
        _dropHeight = SkipIntro ? 0f : rules.PodDropHeight;
        _landingLength = SkipIntro ? 0f : rules.PodDropSeconds;
        Pod = Vessel.Spawn(Zone, pod, _podRest + N.Vector3.UnitY * _dropHeight, _podHeading, landed: true);
        // Before any ground streams in, so its chunks and colliders are built with the bed already cut.
        CarveBed(pod, _podRest, MathX.Yaw(_podHeading));
        foreach (var (id, n) in data.Rules.Materials.PodLocker) Pod.Storage.Add(id, n);

        Survivor = Survivor.Spawn(Zone, _podRest + N.Vector3.UnitY * (_dropHeight + 0.3f));
        Survivor.Body.DamageSink = Suit = new Suit(Zone, data.Survival(), (int)Zone.Spec.Seed);
        Suit.Said += line => Say(line, Tones.Caution);
        Suit.Sim.RespawnPoints.Add(new RespawnPoint
        {
            Id = "pod", Name = "Escape pod", Kind = RespawnKind.SurvivalTent,
            Position = new Pos(_podRest.X, _podRest.Y, _podRest.Z), Stand = new Pos(_podRest.X, _podRest.Y, _podRest.Z),
        });
        Suit.Sim.Pods[0].Position = new Pos(_podRest.X, _podRest.Y, _podRest.Z);
        Survivor.Sit(Pod);

        Pack = new PlayerInventory(data.Items);
        for (int i = 0; i < data.Rules.Materials.Toolbar.Count && i < PlayerInventory.ToolbarSlots; i++)
        {
            Pack.Storage.Add(data.Rules.Materials.Toolbar[i], 1);
            Pack.Grid[i].Item = data.Rules.Materials.Toolbar[i];
            Pack.Grid[i].Count = 1;
        }
        Pack.Grid.Reconcile();
        Progress = new Progress(data.Progress, data.Items, data.Catalogue) { Creative = Mode == GameMode.Creative };
        Fabricator = new GalHack.Items.Production.Constructor(data.Items, data.Items.Machines.Get("suit"), Pack.Storage, Zone.WorldRules);

        Shots = new ShotFx { Name = "Shots" };
        Zone.AddChild(Shots);
        Tool = new SurvivalTool(Zone, Survivor, Suit, Pack) { Sfx = Zone.Sfx, Fx = Shots };
        var debris = new PlanetSurface.Fx.Debris { Name = "Debris", Collector = () => Survivor.WorldEye() + Vector3.Down * 0.5f };
        Zone.AddChild(debris);
        Tool.Debris = debris;
        Tool.Said += Say;
        Tool.Overflowed += left => SetDown(left, Survivor.WorldFeet);
        Tool.CreatureHit += c => Visor.Hit();
        Builder = new BuildTool(Zone, Survivor, Pack, Progress);
        Builder.Said += Say;
        Builder.Finished += v => Say($"Fly it: F at the {v.Name}'s seat", Tones.Good);
        Builder.Spilled += SetDown;
        WireProgress();

        Target = new PlayerTarget(this);
        Zone.Wildlife.SetPlayer(Target);
        Zone.Wildlife.Attacked += OnBitten;
        Zone.Wildlife.Telegraphed += e => Visor.Threat(Zone.Wildlife.FromPatch(e.Creature.Position));
        Zone.Wildlife.Died += OnCreatureDied;
        Zone.Weather.Observer = () => Survivor.WorldFeet;
        Zone.Weather.Happened += OnWeather;

        Console = new CockpitConsole { Name = "Console", Game = this };
        Zone.AddChild(Console);
        MachineScreens = new MachineScreens { Name = "MachineScreens", Game = this };
        AddChild(MachineScreens);
        Hands = new Hands { Name = "Hands", Game = this };
        Camera.AddChild(Hands);
        SliceRules.TorchRules torch = data.Rules.Torch;
        Torch = new SpotLight3D
        {
            Name = "HeadTorch", Visible = false, SpotRange = torch.Range, SpotAngle = torch.Angle, LightEnergy = torch.Energy,
            SpotAngleAttenuation = 0.8f, LightColor = new Color(1f, 0.95f, 0.85f), ShadowEnabled = true, Position = new Vector3(0.12f, 0.05f, 0f),
        };
        Camera.AddChild(Torch);
        Paperdoll.Attach(this);
        Audio = new SliceAudio { Name = "Audio", Game = this };
        AddChild(Audio);
        // The castaway seen from outside (third person and screenshots); the first-person camera leaves it out.
        Survivor.Node.AddChild(CharacterModel.Build());
        _creatureBlockers = new CreatureBlockers { Name = "CreatureBlockers", Game = this };
        Zone.AddChild(_creatureBlockers);

        Phase = Phase.Landing;
        _waitingForGround = true;
        Say(SkipIntro ? "Escape pod down." : "Escape pod: re-entry. Brace.", Tones.Caution);
    }

    CreatureBlockers _creatureBlockers;

    // ---- The pod coming down -------------------------------------------------------------------

    void Descend(float dt)
    {
        _landingTime += dt;
        float t = _landingLength > 0f ? Mathf.Clamp(_landingTime / _landingLength, 0f, 1f) : 1f;
        // Falling fast, then braking hard on its retros for the last stretch.
        float left = 1f - t;
        float height = _dropHeight * left * left * (1f + 0.6f * t);
        Pod.Body.Position = _podRest + N.Vector3.UnitY * height;
        Pod.Body.Rotation = MathX.Yaw(_podHeading);
        Pod.Fx.Burn = t > 0.55f && t < 1f ? 1f : 0f;
        Survivor.AfterPhysics(dt);
        if (t < 1f) return;

        // Down, once the ground under it is in.
        if (_waitingForGround && !Zone.Streamer.ReadyAround(_podRest.ToGodot(), 1, needCollision: true)) return;
        _waitingForGround = false;
        Pod.Controller.PlaceLanded(Pod.Body, _podRest, _podHeading);
        Pod.Binding.Teleport();
        Pod.Fx.Burn = 0f;
        if (_landingLength > 0f)
        {
            Zone.Sfx.Play(PlanetSurface.Fx.Sfx.Sound.Boom, _podRest.ToGodot(), 0.7f, 2f);
            Tool.Debris.Puff(_podRest.ToGodot(), new Color(0.6f, 0.55f, 0.45f, 0.7f), 6f);
        }
        Phase = Phase.Playing;
        if (SkipIntro)
        {
            Survivor.StandUp();
            Survivor.PlaceOutside(Pod.Body.ToParent(new N.Vector3(0f, -0.2f, -3.2f)));
            Survivor.Yaw = Mathf.Pi;
        }
        Say("Down. F to unstrap. Tab: your inventory, crafting and the tech tree. The locker (E) has supplies.", Tones.Good);
        Landed?.Invoke();
    }

    /// <summary>The ground the landed pod's hull sits in is cut away through the terrain's edit API (its landing carved its bed), so it never stands in the ground.</summary>
    void CarveBed(Structure s, N.Vector3 at, N.Quaternion rotation)
    {
        var (min, max) = s.Bounds();
        float cell = s.Grid.CellSize, half = cell * 0.5f;
        N.Vector3 lo = min.ToVector3() * cell - new N.Vector3(half + 0.1f), hi = max.ToVector3() * cell + new N.Vector3(half + 0.1f);
        Vector3 c = at.ToGodot();
        float r = (hi - lo).Length() * 0.5f + 1f;
        var cells = new List<TerrainDigging.Terrain.Int3>();
        var store = Zone.Planet.Terrain.Store;
        for (int z = (int)MathF.Floor(c.Z - r); z <= c.Z + r; z++)
        for (int y = (int)MathF.Floor(c.Y - r); y <= c.Y + r; y++)
        for (int x = (int)MathF.Floor(c.X - r); x <= c.X + r; x++)
        {
            N.Vector3 local = MathX.Unrotate(rotation, new N.Vector3(x + 0.5f, y + 0.5f, z + 0.5f) - at);
            if (local.X < lo.X || local.Y < lo.Y || local.Z < lo.Z || local.X > hi.X || local.Y > hi.Y || local.Z > hi.Z) continue;
            var cellAt = new TerrainDigging.Terrain.Int3(x, y, z);
            if (store.MaterialAt(cellAt) != TerrainDigging.Terrain.CellMaterial.Air) cells.Add(cellAt);
        }
        if (cells.Count > 0) Zone.Edits.DigCells(cells);
    }

    // ---- Each tick: input in, before the physics steps ------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (Interactive) PollKeyboard();
        if (Phase == Phase.Generating) return;
        if (Phase == Phase.Landing)
        {
            Descend((float)delta);
            return;
        }
        Controls c = Controls;
        if (c.WasPressed(Key.F3)) Debug.Visible = !Debug.Visible;
        if (c.WasPressed(Key.Tab)) Menu.Toggle();
        if (c.WasPressed(Key.Escape) && Menu.IsOpen) Menu.Close();
        if (Menu.IsOpen) Menu.Input(c);
        if (c.WasPressed(Key.V) && !Menu.IsOpen) ThirdPerson = !ThirdPerson;
        if (c.WasPressed(Key.L)) ToggleTorch();
        if (Suit.Dead)
        {
            if (c.WasPressed(Key.R)) Respawn();
            Survivor.Legs.Move = default;
            Survivor.Legs.Jump = Survivor.Legs.Jetpack = false;
            return;
        }
        if (Survivor.Seated is Vessel vessel) Fly(vessel, c);
        else Walk(c);
    }

    void Walk(Controls c)
    {
        Survivor s = Survivor;
        // The menu takes the mouse (the game doesn't pause: you can still walk with it open).
        if (!Menu.IsOpen)
        {
            s.Yaw -= c.Look.X * 0.0025f;
            s.Pitch = Mathf.Clamp(s.Pitch - c.Look.Y * 0.0025f, -1.5f, 1.5f);
        }
        s.Legs.Move = new N.Vector2(c.Axis(Key.D, Key.A), c.Axis(Key.S, Key.W));
        s.Legs.Sprint = c.IsHeld(Key.Shift) && !Menu.IsOpen;
        s.Legs.Jump = c.IsHeld(Key.Space);
        s.Legs.Jetpack = c.IsHeld(Key.Space);
        s.Legs.Vertical = c.Axis(Key.Space, Key.Ctrl);
        if (Menu.IsOpen) return;

        for (int i = 0; i < PlayerInventory.ToolbarSlots; i++)
            if (c.WasPressed(Key.Key1 + i)) Select(i);
        if (c.WasPressed(Key.F)) TrySit();
        if (c.WasPressed(Key.E)) Use();
        if (c.WasPressed(Key.B)) Report(Suit.Sim.DeployOrPack(FeetPos()));
        if (c.WasPressed(Key.H)) Report(Suit.Sim.ToggleHelmet());
        HandKind hand = Pack.Hand;
        if (hand == HandKind.Tool && c.WasPressed(Key.Q))
        {
            Tool.Mode = Tool.Mode == ToolMode.Gather ? ToolMode.Shoot : ToolMode.Gather;
            Say(Tool.Mode == ToolMode.Shoot ? "Survival tool: SHOOT (Q to gather)" : "Survival tool: GATHER (Q to shoot)", Tones.Text);
        }
        if (hand is HandKind.Block or HandKind.Core or HandKind.Blueprint)
        {
            if (c.Wheel != 0 && hand == HandKind.Block) Builder.NextShape(c.Wheel);
            if (c.WasPressed(Key.R)) Builder.Rotate(1);
            if (c.WasPressed(Key.T)) Builder.Rotate(0);
            if (c.WasPressed(Key.X))
            {
                Builder.MirrorX = !Builder.MirrorX;
                Say(Builder.MirrorX ? "Mirror left/right on" : "Mirror off", Tones.Text);
            }
        }
        else if (c.Wheel != 0) Select((Pack.Selected + Math.Sign(c.Wheel) + PlayerInventory.ToolbarSlots) % PlayerInventory.ToolbarSlots);
    }

    /// <summary>Puts a toolbar slot in your hands.</summary>
    public void Select(int slot)
    {
        if (slot == Pack.Selected) return;
        Pack.Selected = slot;
        Builder.Hide();
        Slot s = Pack.InHand;
        string what = s.Block == PlayerInventory.BlueprintBlock ? "Blueprint projector: the Skiff" : s.Block != null
            ? Zone.Data.Catalogue.Type(s.Block).Name : s.Item != null ? Zone.Data.Items.Item(s.Item).Name : "empty hands";
        Say($"{slot + 1}: {what}", Tones.Text);
    }

    void Fly(Vessel vessel, Controls c)
    {
        // The mouse moves a virtual stick that recentres by itself (as in ship-handling).
        if (!Menu.IsOpen) _stick = (_stick * 0.85f + c.Look * 0.012f).Clamp(new Vector2(-1, -1), new Vector2(1, 1));
        if (c.WasPressed(Key.F) && !Menu.IsOpen)
        {
            Survivor.StandUp();
            _stick = Vector2.Zero;
            return;
        }
        if (vessel.Flight == null) return;
        var move = new Vector3(c.Axis(Key.D, Key.A), c.Axis(Key.Space, Key.Ctrl), c.Axis(Key.S, Key.W));
        // Small vessels turn no faster than the data's cap, however light (the stick asks for a share of it).
        float cap = vessel.Structure.Grid.Size == GridSize.Small && vessel.Stats is HandlingStats st && st.TurnRate > 0f
            ? Mathf.Min(1f, Zone.Data.Rules.Build.SmallVesselMaxTurn / st.TurnRate) : 1f;
        var turn = new Vector3(-_stick.Y, -_stick.X, c.Axis(Key.Q, Key.E)) * cap;
        vessel.Flight.Input = PilotInput.Hands(move, turn, c.IsHeld(Key.Shift));
        if (c.WasPressed(Key.G)) vessel.Controller?.ToggleGear();
        if (c.WasPressed(Key.C)) vessel.Flight.ToggleCruise();
    }

    void ToggleTorch()
    {
        Torch.Visible = !Torch.Visible;
        Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Ping, Camera.GlobalPosition, Torch.Visible ? 2.2f : 1.8f, -18f);
        Say(Torch.Visible ? "Head torch on (L)" : "Head torch off", Tones.Text);
    }

    /// <summary>F on foot: the nearest cockpit seat within reach.</summary>
    void TrySit()
    {
        Vector3 eye = Survivor.WorldEye();
        Vessel best = null;
        float bestDistance = 3.2f;
        foreach (Vessel v in Zone.Vessels)
        {
            if (v.Seat == null || v.IsWreck) continue;
            float d = v.ToWorld(v.Seat.Value).DistanceTo(eye);
            if (d < bestDistance) { best = v; bestDistance = d; }
        }
        if (best == null) Say("No seat within reach", Tones.Caution);
        else if (Survivor.Sit(best)) Say(best == Pod ? "Strapped into the pod's seat" : $"At the {best.Name}'s controls: WASD, Space/Ctrl, mouse, Q/E roll, G gear", Tones.Text);
    }

    void Report(Outcome o) => Say(o.Message, o.Ok ? Tones.Good : Tones.Caution);

    Pos FeetPos()
    {
        N.Vector3 p = Survivor.Body.WorldPosition;
        return new Pos(p.X, p.Y, p.Z);
    }

    // ---- Each tick: after the physics ----------------------------------------------------------

    /// <summary>After the physics host and the world have stepped: the body's frame, the suit, the tools, the machines.</summary>
    internal void AfterPhysics(float dt)
    {
        if (Phase != Phase.Playing)
        {
            Controls.EndTick();
            return;
        }
        Survivor.AfterPhysics(dt);
        Suit.Tick(dt, Survivor);
        Target.Sync();
        Zone.Wildlife.Follow(Survivor.WorldFeet, onFoot: Survivor.Body.Frame == null);
        _creatureBlockers.KeepOff();
        Pack.Grid.Reconcile();
        HandKind hand = Pack.Hand;
        bool hands = !Suit.Dead && Survivor.Seated == null && !Menu.IsOpen;
        // Aim on an in-world screen within reach: the buttons click it, and nothing else fires.
        if (hands && MachineScreens.Use(Controls)) hands = false;
        bool primary = hands && Controls.Primary, pressed = hands && Controls.PrimaryPressed;
        if (hands && hand == HandKind.Tool) Tool.Tick(dt, primary);
        else if (hands && hand is HandKind.Block or HandKind.Core or HandKind.Device or HandKind.Blueprint)
            Builder.Tick(dt, hand, Pack.InHand, Pack.Selected, primary, pressed, hands && Controls.SecondaryPressed);
        else if (hands && hand == HandKind.Consumable && pressed) Consume(Pack.Selected);
        if (!hands || hand is not (HandKind.Block or HandKind.Core or HandKind.Device or HandKind.Blueprint)) Builder.Hide();
        TickMachines(dt);
        CheckDeath();
        Stats.Tick(this, dt);
        TrackProgress();
        Controls.EndTick();
    }

    void OnBitten(AttackEvent e)
    {
        if (!e.Hit || e.Damage <= 0f) return;
        double lost = Suit.Hit(e.Damage / 100.0 * Suit.Sim.Data.Vitals.Health.Max, DamageKind.Hit);
        Visor.Hurt(Zone.Wildlife.FromPatch(e.Creature.Position));
        Say($"{e.Creature.Species.CommonName}: -{lost:F0} health", Tones.Warning);
    }

    void OnWeather(WeatherEvent e)
    {
        Vector3 feet = Survivor.WorldFeet;
        switch (e.Kind)
        {
            case WeatherEventKind.LightningStrike:
                float distance = feet.DistanceTo(e.Position);
                Zone.Sfx.Play(PlanetSurface.Fx.Sfx.Sound.Thunder, Camera.GlobalPosition + (e.Position - Camera.GlobalPosition).Normalized() * 20f,
                    1f, Mathf.Clamp(6f - distance / 60f, -12f, 6f));
                if (Survivor.Seated == null && distance < e.DamageRadius)
                {
                    Suit.Hit(e.Damage, DamageKind.Hit);
                    Say("STRUCK BY LIGHTNING: get low in a storm", Tones.Warning);
                }
                break;
            case WeatherEventKind.TornadoForming:
                Say($"Funnel cloud forming {WeatherSim.Bearing(feet, e.Position)}", Tones.Warning);
                break;
        }
    }

    /// <summary>The weather's warnings for the visor, nearest first.</summary>
    public IReadOnlyList<WarningEntry> WeatherWarnings()
    {
        _warnings.Clear();
        Zone.Weather.Sim?.GetWarnings(_warnings, Survivor?.WorldFeet ?? Vector3.Zero);
        return _warnings;
    }

    // ---- The keyboard and mouse ----------------------------------------------------------------

    static readonly Key[] Watched =
    {
        Key.W, Key.A, Key.S, Key.D, Key.Space, Key.Ctrl, Key.Shift, Key.Q, Key.E,
    };

    void PollKeyboard()
    {
        foreach (Key k in Watched) Controls.Hold(k, Input.IsPhysicalKeyPressed(k));
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        Controls.Primary = (captured || Menu.IsOpen) && Input.IsMouseButtonPressed(MouseButton.Left);
        Controls.Secondary = (captured || Menu.IsOpen) && Input.IsMouseButtonPressed(MouseButton.Right);
        Controls.Mouse = Menu.ToVirtual(GetViewport().GetMousePosition());
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Interactive) return;
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                Controls.Look += motion.Relative;
                break;
            case InputEventMouseButton { Pressed: true } button:
                if (Input.MouseMode != Input.MouseModeEnum.Captured && !Menu.IsOpen)
                {
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                    break;
                }
                if (button.ButtonIndex == MouseButton.Left) Controls.PrimaryPressed = true;
                else if (button.ButtonIndex == MouseButton.Right) Controls.SecondaryPressed = true;
                else if (button.ButtonIndex == MouseButton.WheelUp) Controls.Wheel--;
                else if (button.ButtonIndex == MouseButton.WheelDown) Controls.Wheel++;
                break;
            case InputEventMouseButton { Pressed: false } release:
                if (release.ButtonIndex == MouseButton.Left) Controls.PrimaryReleased = true;
                else if (release.ButtonIndex == MouseButton.Right) Controls.SecondaryReleased = true;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.PhysicalKeycode == Key.Escape && !Menu.IsOpen) Input.MouseMode = Input.MouseModeEnum.Visible;
                else Controls.Tap(key.PhysicalKeycode);
                break;
        }
    }

    // ---- The camera -----------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Suit != null) SyncReplicators();
        if (Interactive)
        {
            var want = Menu.IsOpen ? Input.MouseModeEnum.Visible : Input.MouseMode;
            if (Input.MouseMode != want) Input.MouseMode = want;
        }
        // A vessel in flight gets collision a column further out than someone on foot.
        if (Zone.IsBuilt) Zone.Streamer.CollisionRadius = Survivor?.Seated?.Flight != null && Survivor.Seated.Controller?.Frozen == false ? 3 : 2;
        if (Survivor == null)
        {
            if (Zone.IsBuilt && Pod != null) Camera.GlobalTransform = Chase(Pod, 2.4f, 0.6f);
            return;
        }
        Camera.SetCullMaskValue(2, (ThirdPerson || FixedCamera != null) && Survivor.Seated == null);
        Camera.GlobalTransform = FixedCamera ?? (Survivor.Seated is Vessel v && (ThirdPerson || Phase == Phase.Landing)
            ? Chase(v, v == Pod ? 3.2f : 1.6f, 0.5f)
            : ThirdPerson ? Survivor.Eye().TranslatedLocal(new Vector3(0f, 0.6f, 4.5f)) : Survivor.Eye());
    }

    /// <summary>The survival replicators set down in the world, drawn as the kit's little cabinet.</summary>
    void SyncReplicators()
    {
        var seen = new HashSet<int>();
        foreach (Replicator r in Suit.Sim.Replicators)
        {
            seen.Add(r.Id);
            if (_replicators.ContainsKey(r.Id)) continue;
            var node = new Node3D { Name = $"Replicator{r.Id}", Position = new Vector3((float)r.Position.X, (float)r.Position.Y, (float)r.Position.Z) };
            node.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.9f, 0.5f) }, Position = Vector3.Up * 0.45f,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("0a9396"), Roughness = 0.5f, Metallic = 0.3f },
            });
            node.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.62f, 0.12f, 0.52f) }, Position = Vector3.Up * 0.7f,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("f4a100"), EmissionEnabled = true, Emission = new Color("f4a100"), EmissionEnergyMultiplier = 0.6f },
            });
            Zone.AddChild(node);
            _replicators[r.Id] = node;
        }
        foreach (int id in _replicators.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _replicators[id].QueueFree();
            _replicators.Remove(id);
        }
    }

    /// <summary>A chase camera behind and above a vessel, sized to it.</summary>
    public static Transform3D Chase(Vessel vessel, float distance, float height)
    {
        Transform3D t = vessel.DrawTransform;
        var (min, max) = vessel.Structure.Bounds();
        float size = MathF.Max(3f, (max - min).ToVector3().Length() * vessel.Structure.Grid.CellSize);
        Vector3 target = t.Origin + Vector3.Up * size * 0.15f;
        Vector3 back = t.Basis.Z;
        back.Y = 0f;
        back = back.LengthSquared() < 1e-4f ? Vector3.Back : back.Normalized();
        Vector3 eye = target + back * size * distance + Vector3.Up * size * height;
        return new Transform3D(Basis.LookingAt(target - eye, Vector3.Up), eye);
    }

    /// <summary>Runs <see cref="Game.AfterPhysics"/> once the physics host (-10) and the world's logic (0) have stepped.</summary>
    sealed partial class AfterStep : Node
    {
        public Game Game;
        public override void _Ready() => ProcessPhysicsPriority = 5;
        public override void _PhysicsProcess(double delta) => Game.AfterPhysics((float)delta);
    }
}

/// <summary>The survivor as wildlife sees them: where they stand, where they look, how hurt they are.</summary>
public sealed class PlayerTarget : ITarget
{
    readonly Game _game;
    public PlayerTarget(Game game) => _game = game;

    public string Name => "Castaway";
    public Vector3 Position => _game.Zone.Wildlife.PatchLocal(_game.Survivor.WorldFeet);
    public Vector3 Forward => _game.Survivor.WorldLook();
    public Health Health { get; } = new(100f);

    /// <summary>On foot and alive; aboard a vessel nothing can bite you.</summary>
    public bool Attackable => !_game.Suit.Dead && _game.Survivor.Body.Frame == null;
    public float ThreatScale => _game.Survivor.Seated != null ? 2f : 1f;

    /// <summary>Wildlife keeps its own health scale (100); the suit's is the real one.</summary>
    public void Sync()
    {
        Health.Current = (float)(_game.Suit.HealthShare * Health.Max);
    }
}
