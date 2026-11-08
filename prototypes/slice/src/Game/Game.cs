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
using Slice.Vehicles;
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

    /// <summary>Hazards and attacks as symbols, over every menu.</summary>
    public Alerts Alerts { get; private set; }
    public TabMenu Menu { get; private set; }
    public CockpitConsole Console { get; private set; }
    /// <summary>The in-world screens on constructors and base-mode Big Cores (looked at to use).</summary>
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
        PlanetSurface.Skies.StarSystem.TemperateYearDays = Slice.Orbit.OrbitRules.Shared.Globe.TemperateYearDays;
        ResourceMount.Mount();
        ProcessPhysicsPriority = -20; // input before the physics host (-10) steps
        // The world stops with the escape menu open, whatever runs it (a test's or the screenshots' node runs on through a pause).
        ProcessMode = ProcessModeEnum.Pausable;
        LoadSettings();
        // The command line pins the world and the mode: -- --seed 123 --type meadow --depth 0.7 --fall famine --creative (WorldChoice.FromArgs)
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
        Zone = new SurfaceZone { Name = "Surface", Spec = World.Spec(), Force = World.Force, PickStart = PickStart };
        AddChild(Zone);
        AddChild(new GcPacer { Name = "GC pacer" });
        Zone.WorldRules.Mode = _mode;
        // Placed every frame from interpolated transforms, so it is not interpolated itself.
        Camera = new Camera3D { Name = "Camera", Fov = 75f, Near = 0.05f, Far = 12000f, Current = true, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        Zone.AddChild(Camera);
        Zone.Camera = Camera;
        Zone.Focus = Focus;
        Zone.Built += OnPlanetBuilt;
        StartZones();
        AddChild(new AfterStep { Name = "AfterStep", Game = this });
        AddChild(new DiegeticUi.Screens.ScreenManager { Name = "Screens", Camera = Camera });
        Visor = new Visor { Name = "Visor", Game = this };
        AddChild(Visor);
        Menu = new TabMenu { Name = "Menu", Game = this };
        AddChild(Menu);
        Escape = new EscapeMenu { Name = "Escape", Game = this };
        AddChild(Escape);
        Alerts = new Alerts { Name = "Alerts", Game = this };
        AddChild(Alerts);
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
        if (Survivor.OnBike is HoverBike bike) return bike.Position + new Vector3(bike.Velocity.X, 0f, bike.Velocity.Z).LimitLength(48f) * 0.5f;
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
        Icons = new Slice.Ui.ItemIcons { Name = "Icons" };
        AddChild(Icons);
        Icons.Setup(data.Items, data.Catalogue);
        Slice.Ui.ItemIcons.Shared = Icons;
        Progress = new Progress(data.Progress, data.Items, data.Catalogue) { Creative = Mode == GameMode.Creative };
        Fabricator = new GalHack.Items.Production.Constructor(data.Items, data.Items.Machines.Get("suit"), Pack.Storage, Zone.WorldRules);

        Shots = new ShotFx { Name = "Shots" };
        Zone.AddChild(Shots);
        Tool = new SurvivalTool(Zone, Survivor, Suit, Pack) { Sfx = Zone.Sfx, Fx = Shots };
        Arms = new Slice.Arms.ArmsHost { Name = "Arms", Game = this };
        Zone.AddChild(Arms);
        var debris = new PlanetSurface.Fx.Debris
        {
            Name = "Debris", Collector = () => Survivor.WorldEye() + Vector3.Down * 0.5f,
            ColorOf = (material, at) => Zone.Planet?.ChipColor(material, at) ?? new Color(0.5f, 0.45f, 0.4f),
        };
        Zone.AddChild(debris);
        Zone.Dust = debris;
        Tool.Debris = debris;
        Bikes = new HoverBikes(Zone) { Puff = (at, colour, size) => debris.Puff(at, colour, size) };
        Bikes.Bumped += at => Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, at, 0.8f, -6f);
        Zone.AddChild(Bikes);
        Survivor.Bikes = Bikes;
        Tool.Said += Say;
        Tool.Overflowed += left => SetDown(left, Survivor.WorldFeet);
        Tool.CreatureHit += c => Visor.Hit();
        Tool.CreatureStruck += OnCreatureStruck;
        Builder = new BuildTool(Zone, Survivor, Pack, Progress);
        Builder.Said += Say;
        Builder.Spilled += SetDown;
        WireProgress();

        Target = new PlayerTarget(this);
        Zone.Wildlife.SetPlayer(Target);
        Zone.Wildlife.Attacked += OnBitten;
        SetUpHusks();
        Zone.Wildlife.Telegraphed += e =>
        {
            Vector3 from = Zone.Wildlife.WorldOf(e.Creature);
            Visor.Threat(from);
            // A Husk's wind-up is never flagged: it looks like anyone until it has you.
            if (!e.Creature.Species.Husk) Alerts.Attack(Alerts.SignFor(e.Creature.Species), from);
        };
        Zone.Wildlife.Died += OnCreatureDied;
        Zone.Weather.Observer = () => Survivor.WorldFeet;
        Zone.Weather.Bridge.Shelter = body => Sheltered(body.WorldPosition.ToGodot());
        Suit.UnderCover = UnderCover;
        Zone.Weather.Enclosed = () => Survivor.Aboard is { } ship && !Survivor.OnOuterHull && !Slice.Surface.SkyLight.Stands(ship);
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
            Name = "HeadTorch", Visible = false, SpotRange = torch.Range, SpotAngle = torch.Angle, LightEnergy = torch.Energy, SpotAttenuation = torch.Attenuation,
            SpotAngleAttenuation = 0.8f, LightColor = new Color(1f, 0.95f, 0.85f), ShadowEnabled = true, Position = new Vector3(0.12f, 0.05f, 0f),
        };
        Camera.AddChild(Torch);
        Paperdoll.Attach(this);
        Audio = new SliceAudio { Name = "Audio", Game = this };
        AddChild(Audio);
        Howl = new WindHowl { Name = "Howl", Zone = Zone, Eye = () => Survivor.WorldEye(), Quiet = () => Spaceflight.AirGone > 0.5f };
        Zone.AddChild(Howl);
        // The castaway seen from outside (third person and screenshots); the first-person camera leaves it out.
        Survivor.Node.AddChild(CharacterModel.Build());
        Bodies = new CreatureBodies { Name = "CreatureBodies", Game = this };
        Zone.AddChild(Bodies);
        SetUpPlaces();

    }

    /// <summary>Wind moaning in the openings around you.</summary>
    public WindHowl Howl { get; private set; }

    /// <summary>Small 3D icons of items, each rendered once.</summary>
    public Slice.Ui.ItemIcons Icons { get; private set; }

    /// <summary>Hand weapons: the guns in your hands and every shot in flight, stuck or set down.</summary>
    public Slice.Arms.ArmsHost Arms { get; private set; }

    /// <summary>The hover bikes standing in the world.</summary>
    public HoverBikes Bikes { get; private set; }

    /// <summary>Creatures' collision, from their own bodies.</summary>
    public CreatureBodies Bodies { get; private set; }


    // ---- Each tick: input in, before the physics steps ------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (Zone == null) return; // the start screen is up
        if (Interactive) PollKeyboard();
        // Esc with no menu open: the escape menu, and the world stops (with the Tab menu open, Esc is the Tab menu's, below).
        if (Controls.KeyWasPressed(Key.Escape) && !Menu.IsOpen)
        {
            Escape.Open();
            return;
        }
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
        if (c.WasPressed(Key.M) && Slice.Items.ItemViews.Focused == null)
        {
            if (Menu.IsOpen && Menu.Tab == Slice.Ui.MenuTab.Map) Menu.Close();
            else if (!Menu.IsOpen) Menu.OpenMap(Slice.Ui.MapLevel.Local);
            else Menu.Tab = Slice.Ui.MenuTab.Map;
        }
        if (c.KeyWasPressed(Key.Escape) && Menu.IsOpen && !Slice.Items.ItemViews.Escape()) Menu.Close();
        if (c.KeyWasPressed(Key.Slash) && Menu.IsOpen && Slice.Items.ItemViews.Focused == null) Menu.FocusFilter();
        if (Menu.IsOpen && c.KeyWasPressed(Key.A) && c.ModifierHeld(Key.Ctrl) && Slice.Items.ItemViews.Focused == null) Slice.Ui.Slots.SelectAll();
        if (Menu.IsOpen) Menu.Input(c);
        if (c.WasPressed(Key.V) && !Menu.IsOpen) ThirdPerson = !ThirdPerson;
        if (c.WasPressed(Key.L)) ToggleTorch();
        Survivor.WantCrouch = false; // only the walk below asks for it
        StepWrongness((float)delta);
        if (Suit.Dead)
        {
            Survivor.Dismount();
            if (c.WasPressed(Key.R)) Respawn();
            Survivor.Legs.Move = default;
            Survivor.Legs.Jump = Survivor.Legs.Jetpack = false;
            return;
        }
        // Seized by a Husk: the hold has the body until it ends.
        if (Held(c, (float)delta)) return;
        if (Survivor.OnBike is HoverBike bike) Ride(bike, c);
        else if (Survivor.Seated is Vessel vessel) Fly(vessel, c);
        else Walk(c);
    }

    /// <summary>On the hover bike: W/S throttle and brake, A/D steer, Shift boosts, Space hops, F gets off. The mouse looks.</summary>
    void Ride(HoverBike bike, Controls c)
    {
        Survivor s = Survivor;
        s.Legs.Move = default;
        s.Legs.Jump = s.Legs.Jetpack = false;
        s.Legs.Vertical = 0f;
        bool typing = Menu.IsOpen && Slice.Items.ItemViews.Focused != null;
        if (!Menu.IsOpen) Turn(s, c);
        if (c.WasPressed(Key.F) && !Menu.IsOpen)
        {
            s.Dismount();
            return;
        }
        bike.Throttle = typing ? 0f : c.Axis(Key.W, Key.S);
        bike.Steer = typing ? 0f : c.Axis(Key.D, Key.A);
        bike.Boost = !typing && c.IsHeld(Key.Shift);
        bike.Hop = !typing && c.IsHeld(Key.Space);
    }

    /// <summary>Sets the hover bike in hand down on the ground in front of you.</summary>
    void PlaceBike()
    {
        if (!WorldRay.Cast(Zone, Survivor, 9f, Layers.World | Layers.Hull, out LookHit look) || look.Normal.Y < 0.5f)
        {
            Say("Needs open ground", Tones.Caution);
            return;
        }
        if (Pack.Remove("hover-bike", 1) < 1) return;
        Pack.Grid.Reconcile();
        Bikes.Place(look.Point, Survivor.Yaw);
    }

    /// <summary>E at a standing hover bike: it goes back in the pack.</summary>
    bool PickUpBike(Vector3 eye, Vector3 dir)
    {
        if (Bikes.Aimed(eye, dir, UseReach) is not HoverBike bike) return false;
        Bill left = Pack.Add("hover-bike", 1);
        if (!left.IsEmpty) SetDown(left, bike.Position);
        Bikes.Remove(bike);
        return true;
    }

    void Walk(Controls c)
    {
        Survivor s = Survivor;
        // The menu takes the mouse (the game doesn't pause: you can still walk with it open).
        if (!Menu.IsOpen) Turn(s, c);
        // Typing into a filter box doesn't move you.
        bool typing = Menu.IsOpen && Slice.Items.ItemViews.Focused != null;
        s.Legs.Move = typing ? N.Vector2.Zero : new N.Vector2(c.Axis(Key.D, Key.A), c.Axis(Key.S, Key.W));
        s.Legs.Sprint = c.IsHeld(Key.Shift) && !Menu.IsOpen;
        s.WantCrouch = c.IsHeld(Key.Ctrl) && !Menu.IsOpen; // hold Ctrl to crouch (where Ctrl is "down" instead, in water, on a ladder or in a shaft, the survivor ignores it)
        s.Legs.Jump = !typing && c.IsHeld(Key.Space);
        s.Legs.Jetpack = !typing && c.IsHeld(Key.Space);
        s.Legs.Vertical = typing ? 0f : c.Axis(Key.Space, Key.Ctrl);
        Tangled(s);
        Climb(c); // a ladder or a lift shaft at the feet
        if (Menu.IsOpen) return;

        // A number picks a slot; the same number again, or Y, switches between your own toolbar
        // and the linked structure's (Empyrion's virtual toolbar).
        for (int i = 0; i < PlayerInventory.ToolbarSlots; i++)
            if (c.WasPressed(Key.Key1 + i))
            {
                bool twice = i == Pack.Selected && Stats.Seconds - _numberAt < DoubleTap;
                _numberAt = Stats.Seconds;
                if (twice) SwitchBar();
                else Select(i);
            }
        if (c.WasPressed(Key.Y)) SwitchBar();
        if (c.WasPressed(Key.F) && !OpenCryopod()) TrySit();
        if (c.WasPressed(Key.E)) Use();
        if (c.WasPressed(Key.B)) Report(Suit.Sim.DeployOrPack(FeetPos()));
        if (c.WasPressed(Key.H)) Report(Suit.Sim.ToggleHelmet());
        HandKind hand = Pack.Hand;
        if (hand == HandKind.Weapon)
        {
            if (c.WasPressed(Key.Q)) Arms.NextMode();   // fire mode or shell type
            if (c.WasPressed(Key.R)) Arms.Reload();
        }
        if (hand == HandKind.Tool && c.WasPressed(Key.Q))
        {
            Tool.Mode = Tool.Mode switch { ToolMode.Gather => ToolMode.Shoot, ToolMode.Shoot => ToolMode.Deconstruct, _ => ToolMode.Gather };
        }
        if (hand is HandKind.Block or HandKind.Core or HandKind.Blueprint)
        {
            bool decoInHand = hand == HandKind.Block && Pack.HandItem == GalHack.Items.DecoCatalogue.UniversalItem;
            bool smallGrid = Builder.TargetVessel != null && Builder.TargetVessel.Structure.Grid.Size == GalHack.Structures.GridSize.Small;
            if (c.Wheel != 0 && hand == HandKind.Block)
            {
                if (decoInHand) Builder.NextPiece(c.Wheel, smallGrid);
                else Builder.NextShape(c.Wheel);
            }
            if (decoInHand && c.WasPressed(Key.G)) Builder.NextTheme(smallGrid);
            if (decoInHand && c.WasPressed(Key.R)) Builder.TurnDeco();
            else if (c.WasPressed(Key.R)) Builder.Rotate(1);
            if (c.WasPressed(Key.T)) Builder.Rotate(0);
            if (c.WasPressed(Key.X))
            {
                Builder.MirrorX = !Builder.MirrorX;
            }
        }
        else if (c.Wheel != 0) Select((Pack.Selected + Math.Sign(c.Wheel) + PlayerInventory.ToolbarSlots) % PlayerInventory.ToolbarSlots);
    }

    /// <summary>How soon a number pressed again counts as a double tap, seconds.</summary>
    const double DoubleTap = 0.4;
    double _numberAt = -1;

    /// <summary>The survival tool has been in your hands. Until then the visor says how to make it.</summary>
    public bool ToolEquipped { get; private set; }

    /// <summary>The hint at waking, until the tool is equipped; null after.</summary>
    public string ToolHint => ToolEquipped ? null : $"[{KeyBindings.Name(Settings.Keys["terminal"])}] SUIT FABRICATOR";

    /// <summary>Puts a toolbar slot in your hands.</summary>
    public void Select(int slot)
    {
        if (slot == Pack.Selected) return;
        Pack.Selected = slot;
        Builder.Hide();
        Slot s = Pack.InHand;
        string id = Pack.HandItem;
        string what = s.Block == PlayerInventory.BlueprintBlock ? "Blueprint projector: the Skiff" : id != null && Zone.Data.Items.TryGet(id, out var item) ? item.Name : "empty hands";
        string origin = Pack.OnRemoteBar && Pack.HandRemote is RemoteSlot rs ? $" ({rs.Origin})" : "";
    }

    /// <summary>Switches between your own toolbar and the remote one (an empty remote bar can't be opened).</summary>
    public void SwitchBar()
    {
        string refused = Pack.ToggleBar();
        if (refused != null)
        {
            Say(refused, Tones.Caution);
            return;
        }
        Builder.Hide();
    }

    /// <summary>The structure you are linked to (its area holds you), or null.</summary>
    public Vessel LinkedStructure { get; private set; }

    /// <summary>The cores whose area holds you, nearest first (the remote toolbar's reach).</summary>
    public IReadOnlyList<Vessel> CoresInReach { get; private set; } = Array.Empty<Vessel>();

    /// <summary>Links to the structure whose area you are in (the one you build on first), and works out which cores are in reach.</summary>
    public void LinkStructure()
    {
        var rules = Zone.Data.Rules.Build.Area;
        Vessel v = Slice.Construction.StructureArea.Linked(Zone, Survivor.WorldFeet, rules, Builder.TargetVessel, Survivor.Seated, Survivor.Aboard);
        LinkedStructure = v;
        Pack.Link(v?.Storage, v?.Name);
        var reach = Slice.Construction.StructureArea.Around(Zone, Survivor.WorldFeet, rules);
        // A structure you are aboard or seated in counts even when its area test misses (a hull far from its blocks' box).
        foreach (Vessel p in new[] { Survivor.Seated as Vessel, Survivor.Aboard })
            if (p != null && Slice.Construction.StructureArea.Linkable(p) && !reach.Contains(p)) reach.Add(p);
        CoresInReach = reach;
        Pack.InRange = s => reach.Any(r => r.Storage == s);
    }

    void Fly(Vessel vessel, Controls c)
    {
        // The mouse moves a virtual stick that recentres by itself (as in ship-handling).
        if (!Menu.IsOpen) _stick = (_stick * 0.85f + StickPush(c)).Clamp(new Vector2(-1, -1), new Vector2(1, 1));
        if (_stick.LengthSquared() < 1e-8f) _stick = Vector2.Zero; // recentred means centred: the decay alone never reaches zero
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
        // Anyone in a seat can reach the ship's systems; only the pilot role's holder flies it.
        if (vessel.Roles.Pilot != Vessel.Player) return;
        vessel.Flight.Input = PilotInput.Hands(move, turn, c.IsHeld(Key.Shift));
        if (c.WasPressed(Key.G)) vessel.Controller?.ToggleGear();
        if (c.WasPressed(Key.C)) vessel.Flight.ToggleCruise();
    }

    void ToggleTorch()
    {
        Torch.Visible = !Torch.Visible;
        Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Ping, Camera.GlobalPosition, Torch.Visible ? 2.2f : 1.8f, -18f);
    }

    /// <summary>F on foot: the nearest command chair within reach.</summary>
    void TrySit()
    {
        Vector3 eye = Survivor.WorldEye();
        Vessel best = null;
        float bestDistance = 3.2f;
        foreach (Vessel v in Zone.Vessels)
        {
            if (v.IsWreck) continue;
            float d = v.NearestChair(eye, out _);
            if (d < bestDistance) { best = v; bestDistance = d; }
        }
        HoverBike bike = Bikes.Nearest(eye, bestDistance, out _);
        if (bike != null && Survivor.Mount(bike))
        {
            return;
        }
        if (best != null && TakeNearestChair(best, eye)) Survivor.Sit(best);
    }

    void Report(Outcome o) { if (!o.Ok) Say(o.Message, Tones.Caution); } // what worked shows for itself

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
        Bikes.Tick(dt);
        if (Survivor.OnBike is HoverBike ridden) Survivor.Yaw += ridden.LastTurn;
        Survivor.AfterPhysics(dt);
        Suit.Tick(dt, Survivor);
        Target.Sync();
        Zone.Wildlife.Follow(Survivor.WorldFeet, onFoot: Survivor.Body.Frame == null);
        Bodies.KeepOff();
        Pack.Grid.Reconcile();
        LinkStructure();
        HandKind hand = Pack.Hand;
        Pack.Tick(); // an emptied remote toolbar hands back to your own: the bar's name shows it
        // A remote slot out of its origin's range stays in the hand, greyed, and does nothing.
        bool hands = !Suit.Dead && Survivor.Seated == null && Survivor.OnBike == null && !Menu.IsOpen && Pack.HandUsable;
        // Aim on an in-world screen within reach: the buttons click it, and nothing else fires.
        if (hands && MachineScreens.Use(Controls)) hands = false;
        bool primary = hands && Controls.Primary, pressed = hands && Controls.PrimaryPressed;
        if (hand == HandKind.Tool) ToolEquipped = true;
        if (hands && Spaceflight.TakeHands(dt, hand, primary, pressed)) hands = false; // in space: salvage, claiming
        bool deconstruct = hand == HandKind.Tool && Tool.Mode == ToolMode.Deconstruct;
        bool building = Builder.Projecting || hand is HandKind.Block or HandKind.Core or HandKind.Device or HandKind.Blueprint;
        if (hands && Builder.Projecting) Builder.Tick(dt, HandKind.Blueprint, Pack.InHand, Pack.Selected, primary, pressed, Controls.SecondaryPressed);
        else if (hands && deconstruct)
        {
            Builder.TickDeconstruct(pressed);
            if (primary && Builder.HasTarget && Builder.TargetVessel is Vessel dv && dv.Structure.TryGet(Builder.TargetCell, out _))
                Tool.ShowBeam(dv.ToWorld(PhysicsSandbox.Bridges.StructureBridge.CellCenter(dv.Structure, Builder.TargetCell)));
        }
        else if (hands && hand == HandKind.Tool) { Tool.ItemId = Pack.HandItem; Tool.Tick(dt, primary); }
        else if (hands && hand == HandKind.Weapon) Arms.Hold(dt, Pack.HandItem, primary, pressed);
        else if (hands && building)
            Builder.Tick(dt, hand, Pack.InHand, Pack.Selected, primary, pressed, hands && Controls.SecondaryPressed);
        else if (hands && hand == HandKind.Consumable && pressed) Consume(Pack.Selected);
        else if (hands && hand == HandKind.Vehicle && pressed) PlaceBike();
        else if (hands && hand == HandKind.Scanner && pressed) Scan();
        TickPings();
        if (!hands || !(building || deconstruct)) Builder.Hide();
        if (!hands || hand != HandKind.Weapon) Arms.Idle(dt);
        TickMachines(dt);
        CheckDeath();
        Stats.Tick(this, dt);
        TrackProgress();
        Controls.EndTick();
    }

    void OnBitten(AttackEvent e)
    {
        // A lunge that meets a wall on the way lands on the wall.
        if (Slice.Vessels.VesselDamage.Struck(Zone, e.Creature, Survivor.WorldEye())) return;
        if (!e.Hit || e.Damage <= 0f) return;
        double lost = Suit.Hit(e.Damage / 100.0 * Suit.Sim.Data.Vitals.Health.Max, DamageKind.Hit);
        Vector3 from = Zone.Wildlife.WorldOf(e.Creature);
        Visor.Hurt(from);
        Hands?.Jolt(from, MathF.Min(1f, e.Damage / 20f));
        Zone.Wildlife.Audio.PlayUi("hurt", -6f); // the blow landing, not an alert
        Alerts.Attack(Alerts.SignFor(e.Creature.Species), from);
    }

    /// <summary>A weapon's hit on a creature: the same smack and blood as the tool's.</summary>
    public void CreatureStruck(Creature c, Vector3 point, Vector3 dir) => OnCreatureStruck(c, point, dir);

    /// <summary>A hit lands on a creature: a wet smack where it landed, its blood, and a critter of a swarm gone with a pop.</summary>
    void OnCreatureStruck(Creature c, Vector3 point, Vector3 dir)
    {
        Species s = c.Species;
        bool swarm = s.Rig == RigKind.Swarm;
        Zone.Wildlife.SimOf(c, out Vector3 origin, out _);
        float pitch = Math.Clamp(1.2f / MathF.Sqrt(MathF.Max(0.3f, s.Body.Size)), 0.6f, 1.6f);
        Zone.Wildlife.Sound(origin, a => a.Play(swarm ? "pop" : "hit", c.Position + Vector3.Up * 0.5f, pitch, -2f));
        if (swarm) Zone.Blood.Pop(point, s.Blood, 8);
        else Zone.Blood.Hit(point, dir, s.Blood, s.Body.Size);
    }

    void OnWeather(WeatherEvent e)
    {
        Vector3 feet = Survivor.WorldFeet;
        switch (e.Kind)
        {
            case WeatherEventKind.LightningStrike:
                float distance = feet.DistanceTo(e.Position);
                Zone.Weather.Thunder(e.Position);
                if (distance < e.DamageRadius && !UnderCover())
                {
                    Suit.Hit(e.Damage, DamageKind.Hit);
                    Visor.Hurt(e.Position);
                    Alerts.Attack("storm", e.Position);
                }
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
        Key.W, Key.A, Key.S, Key.D, Key.Space, Key.Ctrl, Key.Shift, Key.Q, Key.E, Key.M,
    };

    /// <summary>The view turned by a tick's mouse movement, at your sensitivity, the pitch held short of straight up and down.</summary>
    void Turn(Survivor s, Controls c)
    {
        Vector2 turn = LookTurn(c);
        s.Yaw -= turn.X;
        s.Pitch = Mathf.Clamp(s.Pitch - turn.Y, -1.5f, 1.5f);
    }

    void PollKeyboard()
    {
        // Each held action by the key bound to it; the modifiers themselves too, for the menus' Shift- and Ctrl-clicks.
        KeyBindings keys = Settings.Keys;
        foreach (Key k in Watched) Controls.Hold(k, Input.IsPhysicalKeyPressed(keys.BoundFor(k)));
        foreach (Key m in new[] { Key.Shift, Key.Ctrl, Key.Alt })
            if (Input.IsPhysicalKeyPressed(m)) Controls.Modifiers.Add(m);
            else Controls.Modifiers.Remove(m);
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        Controls.Primary = (captured || Menu.IsOpen) && Input.IsMouseButtonPressed(MouseButton.Left);
        Controls.Secondary = (captured || Menu.IsOpen) && Input.IsMouseButtonPressed(MouseButton.Right);
        Controls.Mouse = Menu.ToVirtual(GetViewport().GetMousePosition());
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Interactive || Zone == null) return; // the start screen takes its own
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
                Press(key.PhysicalKeycode);
                break;
        }
    }

    // ---- The camera -----------------------------------------------------------------------------

    bool _menuWasOpen;

    public override void _Process(double delta)
    {
        if (Zone == null) return; // the start screen is up
        KeepSettingsApplied();
        if (Suit != null) SyncReplicators();
        if (Interactive)
        {
            var want = Menu.IsOpen ? Input.MouseModeEnum.Visible : _menuWasOpen ? Input.MouseModeEnum.Captured : Input.MouseMode;
            _menuWasOpen = Menu.IsOpen; // the moment the menu closes, by any means, aim is back
            if (Input.MouseMode != want) Input.MouseMode = want;
        }
        // A vessel in flight gets collision a column further out than someone on foot.
        if (Zone.IsBuilt) Zone.Streamer.CollisionRadius = (Survivor?.Seated?.Flight != null && Survivor.Seated.Controller?.Frozen == false) || Survivor?.OnBike != null ? 3 : 2;
        if (Survivor == null) return;
        LoadAhead(delta);
        Camera.SetCullMaskValue(2, (ThirdPerson || FixedCamera != null) && Survivor.Seated == null);
        Camera.GlobalTransform = FixedCamera ?? (Survivor.Seated is Vessel v && ThirdPerson
            ? Chase(v, 1.6f, 0.5f)
            : ThirdPerson ? Survivor.Eye().TranslatedLocal(new Vector3(0f, 0.6f, 4.5f)) : CloseUpView(Survivor.Eye()));
    }

    Vector3 _focusWas;

    /// <summary>What streams round you is told how you're moving, however you are: on foot, by bike, in a vessel, or carried. It loads ahead of you.</summary>
    void LoadAhead(double delta)
    {
        if (!Surface.IsBuilt) return;
        Vector3 focus = Focus();
        Vector3 moved = (focus - _focusWas) / (float)Math.Max(delta, 0.001);
        _focusWas = focus;
        if (moved.Length() > 400f || InOrbit) moved = Vector3.Zero; // a seam or a crossing moved the frame, not you
        Vector3 heading = Surface.Streamer.Heading.Lerp(moved, 0.1f);
        Surface.Streamer.Heading = heading;
        Surface.Infra.Heading = heading;
        if (Zone.Pois != null) Zone.Pois.Heading = heading;
        if (Places != null) Places.Heading = heading;
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
    public bool Crouched => _game.Survivor.Crouched && OnFoot;
    public float Lamp => _game.Torch is { Visible: true } ? 1f : 0f;
    public Health Health { get; } = new(100f);

    /// <summary>On foot and alive; aboard a vessel nothing can bite you, and nothing on the surface can reach you underground or under a place's roof.</summary>
    public bool Attackable => OnFoot && !_game.Sheltered();

    /// <summary>On foot and alive: fair game for whatever lives where you are.</summary>
    public bool OnFoot => !_game.Suit.Dead && _game.Survivor.Body.Frame == null;
    public float ThreatScale => _game.Survivor.Seated != null ? 2f : 1f;

    /// <summary>Wildlife keeps its own health scale (100); the suit's is the real one.</summary>
    public void Sync()
    {
        Health.Current = (float)(_game.Suit.HealthShare * Health.Max);
    }
}
