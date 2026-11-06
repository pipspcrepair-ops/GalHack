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

/// <summary>Where the run is: the planet generating (and the castaway asleep in the cryopod), or play.</summary>
public enum Phase { Generating, Playing }

/// <summary>
/// The playable slice: wake in a buried cryopod bunker, survive, gather, craft, build a base and
/// machines, build a vessel and fly it. It owns the surface zone and the castaway (body, suit,
/// inventory, tools and progress), turns one tick's <see cref="Controls"/> into what they do, and
/// places the camera. Tests and screenshots run this same node and drive it through
/// <see cref="Controls"/>.
/// </summary>
public partial class Game : Node3D
{
    /// <summary>False for tests and screenshots: no mouse capture, no keyboard; a script fills <see cref="Controls"/>.</summary>
    public bool Interactive = true;

    /// <summary>Start with the cryopod already open (the systems test only; the real-player test wakes in it shut).</summary>
    public bool SkipIntro;

    /// <summary>
    /// The world: its seed and planet type. Set before the game enters the tree to pin it (tests,
    /// screenshots, <c>--seed N --planet type</c>); left null, a played game asks on the start
    /// screen and anything else rolls a fresh one. A saved game keeps its own.
    /// </summary>
    public WorldChoice World;

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


    Vector2 _stick;
    readonly Dictionary<int, Node3D> _replicators = new();
    readonly List<WarningEntry> _warnings = new();

    public override void _Ready()
    {
        ResourceMount.Mount();
        ProcessPhysicsPriority = -20; // input before the physics host (-10) steps
        // The command line pins the world and the mode: -- --seed 123 --planet ice --creative
        string[] args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--creative") >= 0) _mode = GameMode.Creative;
        World ??= WorldChoice.FromArgs(args);
        if (World == null && Interactive)
        {
            // A new game: the start screen asks for the mode, the planet type and (if any) a seed.
            var start = new StartScreen { Name = "Start" };
            start.Chosen += (world, mode) =>
            {
                World = world;
                _mode = mode;
                Begin();
            };
            AddChild(start);
            return;
        }
        World ??= WorldChoice.Fresh();
        Begin();
    }

    /// <summary>A new game from the start screen (STATS: NEW GAME).</summary>
    public void NewGame() => GetTree().ReloadCurrentScene();

    /// <summary>The world chosen: generate it, and everything that lives in it.</summary>
    void Begin()
    {
        GD.Print($"world: {World}");
        Zone = new SurfaceZone { Name = "Surface", Spec = World.Spec(), PickStart = PickStart };
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
        if (Survivor == null) return Zone.IsBuilt ? Start : Vector3.Zero;
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
        WakeInBunker();

        Pack = new PlayerInventory(data.Items);
        for (int i = 0; i < data.Rules.Materials.Toolbar.Count && i < PlayerInventory.ToolbarSlots; i++)
        {
            Pack.Storage.Add(data.Rules.Materials.Toolbar[i], 1);
            Pack.Grid[i].Item = data.Rules.Materials.Toolbar[i];
            Pack.Grid[i].Count = 1;
        }
        Pack.Grid.Reconcile();
        Slice.Items.ItemViews.Reset();
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

    }

    CreatureBlockers _creatureBlockers;


    // ---- Each tick: input in, before the physics steps ------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (Zone == null) return; // the start screen is up
        if (Interactive) PollKeyboard();
        if (Phase == Phase.Generating)
        {
            if (Zone.IsBuilt) Waking();
            return;
        }
        Controls c = Controls;
        if (c.WasPressed(Key.F3)) Debug.Visible = !Debug.Visible;
        // A filter box with the keyboard takes the typing; Escape clears it, then lets go, then closes the menu.
        if (Menu.IsOpen && Slice.Items.ItemViews.Focused != null && (c.Typed.Length > 0 || c.Backspaces > 0))
            Slice.Items.ItemViews.Type(c.Typed, c.Backspaces);
        if (c.WasPressed(Key.Tab)) { Slice.Items.ItemViews.Focused = null; Menu.Toggle(); }
        if (c.WasPressed(Key.Escape) && Menu.IsOpen && !Slice.Items.ItemViews.Escape()) Menu.Close();
        if (c.WasPressed(Key.Slash) && Menu.IsOpen && Slice.Items.ItemViews.Focused == null) Menu.FocusFilter();
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
        // Typing into a filter box doesn't move you.
        bool typing = Menu.IsOpen && Slice.Items.ItemViews.Focused != null;
        s.Legs.Move = typing ? N.Vector2.Zero : new N.Vector2(c.Axis(Key.D, Key.A), c.Axis(Key.S, Key.W));
        s.Legs.Sprint = c.IsHeld(Key.Shift) && !Menu.IsOpen;
        s.Legs.Jump = !typing && c.IsHeld(Key.Space);
        s.Legs.Jetpack = !typing && c.IsHeld(Key.Space);
        s.Legs.Vertical = typing ? 0f : c.Axis(Key.Space, Key.Ctrl);
        if (Menu.IsOpen) return;

        // A number picks a slot; the same number again, or Y, switches between your own toolbar
        // and the linked structure's (Empyrion's virtual toolbar).
        for (int i = 0; i < PlayerInventory.ToolbarSlots; i++)
            if (c.WasPressed(Key.Key1 + i))
            {
                if (i == Pack.Selected) SwitchBar();
                else Select(i);
            }
        if (c.WasPressed(Key.Y)) SwitchBar();
        if (c.WasPressed(Key.F) && !OpenCryopod()) TrySit();
        if (c.WasPressed(Key.E)) Use();
        if (c.WasPressed(Key.B)) Report(Suit.Sim.DeployOrPack(FeetPos()));
        if (c.WasPressed(Key.H)) Report(Suit.Sim.ToggleHelmet());
        HandKind hand = Pack.Hand;
        if (hand == HandKind.Tool && c.WasPressed(Key.Q))
        {
            Tool.Mode = Tool.Mode switch { ToolMode.Gather => ToolMode.Shoot, ToolMode.Shoot => ToolMode.Deconstruct, _ => ToolMode.Gather };
            Say(Tool.Mode switch
            {
                ToolMode.Shoot => "Survival tool: SHOOT (Q: deconstruct)",
                ToolMode.Deconstruct => "Survival tool: DECONSTRUCT: take blocks down, their items back (Q: gather)",
                _ => "Survival tool: GATHER (Q: shoot)",
            }, Tones.Text);
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
        string id = Pack.HandItem;
        string what = s.Block == PlayerInventory.BlueprintBlock ? "Blueprint projector: the Skiff" : id != null && Zone.Data.Items.TryGet(id, out var item) ? item.Name : "empty hands";
        Say($"{slot + 1}{(Pack.OnStructureBar ? $" ({Pack.LinkedName})" : "")}: {what}", Tones.Text);
    }

    /// <summary>Switches between your own toolbar and the linked structure's (only with a structure linked).</summary>
    public void SwitchBar()
    {
        if (Pack.Bar == BarSource.Personal && Pack.Linked == null)
        {
            Say("No structure in range: its toolbar works inside its area", Tones.Caution);
            return;
        }
        Pack.ToggleBar();
        Builder.Hide();
        Say(Pack.Bar == BarSource.Structure ? $"{Pack.LinkedName.ToUpperInvariant()} TOOLBAR: blocks come from its storage" : "YOUR TOOLBAR: blocks come from your own stacks", Tones.Text);
    }

    /// <summary>The structure you are linked to (its area holds you), or null.</summary>
    public Vessel LinkedStructure { get; private set; }

    /// <summary>Links the structure toolbar to the structure whose area you are in (the one you build on first).</summary>
    public void LinkStructure()
    {
        Vessel v = Slice.Construction.StructureArea.Linked(Zone, Survivor.WorldFeet, Zone.Data.Rules.Build.Area, Builder.TargetVessel, Survivor.Seated, Survivor.Aboard);
        LinkedStructure = v;
        Pack.Link(v?.Storage, v?.Name);
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
        else if (Survivor.Sit(best)) Say($"At the {best.Name}'s controls: WASD, Space/Ctrl, mouse, Q/E roll, G gear", Tones.Text);
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
        LinkStructure();
        HandKind hand = Pack.Hand;
        bool hands = !Suit.Dead && Survivor.Seated == null && !Menu.IsOpen;
        // Aim on an in-world screen within reach: the buttons click it, and nothing else fires.
        if (hands && MachineScreens.Use(Controls)) hands = false;
        bool primary = hands && Controls.Primary, pressed = hands && Controls.PrimaryPressed;
        bool deconstruct = hand == HandKind.Tool && Tool.Mode == ToolMode.Deconstruct;
        bool building = Builder.Projecting || hand is HandKind.Block or HandKind.Core or HandKind.Device or HandKind.Blueprint;
        if (hands && Builder.Projecting) Builder.Tick(dt, HandKind.Blueprint, Pack.InHand, Pack.Selected, primary, pressed, Controls.SecondaryPressed);
        else if (hands && deconstruct) Builder.TickDeconstruct(pressed);
        else if (hands && hand == HandKind.Tool) Tool.Tick(dt, primary);
        else if (hands && building)
            Builder.Tick(dt, hand, Pack.InHand, Pack.Selected, primary, pressed, hands && Controls.SecondaryPressed);
        else if (hands && hand == HandKind.Consumable && pressed) Consume(Pack.Selected);
        if (!hands || !(building || deconstruct)) Builder.Hide();
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
            case InputEventKey { Pressed: true } key:
                // A focused filter box on the menu takes printable keys and backspace (repeats too).
                if (Menu.IsOpen && Slice.Items.ItemViews.Focused != null && key.PhysicalKeycode is not (Key.Escape or Key.Tab))
                {
                    if (key.PhysicalKeycode == Key.Backspace) Controls.Backspaces++;
                    else if (key.Unicode > 31) Controls.Type(char.ConvertFromUtf32((int)key.Unicode));
                    break;
                }
                if (key.Echo) break;
                if (key.PhysicalKeycode == Key.Escape && !Menu.IsOpen) Input.MouseMode = Input.MouseModeEnum.Visible;
                else Controls.Tap(key.PhysicalKeycode);
                break;
        }
    }

    // ---- The camera -----------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Zone == null) return; // the start screen is up
        if (Suit != null) SyncReplicators();
        if (Interactive)
        {
            var want = Menu.IsOpen ? Input.MouseModeEnum.Visible : Input.MouseMode;
            if (Input.MouseMode != want) Input.MouseMode = want;
        }
        // A vessel in flight gets collision a column further out than someone on foot.
        if (Zone.IsBuilt) Zone.Streamer.CollisionRadius = Survivor?.Seated?.Flight != null && Survivor.Seated.Controller?.Frozen == false ? 3 : 2;
        if (Survivor == null) return;
        Camera.SetCullMaskValue(2, (ThirdPerson || FixedCamera != null) && Survivor.Seated == null);
        Camera.GlobalTransform = FixedCamera ?? (Survivor.Seated is Vessel v && ThirdPerson
            ? Chase(v, 1.6f, 0.5f)
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
