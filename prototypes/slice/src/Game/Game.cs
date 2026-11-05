using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using ShipHandling.Handling;
using Slice.Boot;
using Slice.Construction;
using Slice.Player;
using Slice.Surface;
using Slice.Ui;
using Slice.Vessels;
using SurvivalVitals.Model;
using Weather.Model;
using Wildlife;
using Survivor = Slice.Player.Survivor;
using Inventory = GalHack.Structures.Inventory;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Game;

/// <summary>What the survivor has in hand.</summary>
public enum Hand { Gather, Shoot, Build, Blueprint }

/// <summary>Where the run is: the planet generating, the pod coming down, or play.</summary>
public enum Phase { Generating, Landing, Playing }

/// <summary>
/// The playable slice: crash-land in an escape pod, survive, gather, build a vessel and fly it.
/// It owns the surface zone and the castaway (body, suit, pack and tools), turns one tick's
/// <see cref="Controls"/> into what they do, and places the camera. Tests and screenshots run
/// this same node and drive it through <see cref="Controls"/>.
/// </summary>
public partial class Game : Node3D
{
    /// <summary>False for tests and screenshots: no mouse capture, no keyboard; a script fills <see cref="Controls"/>.</summary>
    public bool Interactive = true;

    /// <summary>Start standing beside a landed pod instead of coming down in it.</summary>
    public bool SkipIntro;

    /// <summary>Building pays materials (Survival) or costs nothing (Creative): a per-world setting.</summary>
    public BuildMode BuildMode = BuildMode.Survival;

    public readonly Controls Controls = new();

    public SurfaceZone Zone { get; private set; }
    public Camera3D Camera { get; private set; }
    public Phase Phase { get; private set; } = Phase.Generating;
    public Survivor Survivor { get; private set; }
    public Suit Suit { get; private set; }
    public Inventory Pack { get; } = new() { Creative = false };
    public SurvivalTool Tool { get; private set; }
    public BuildTool Builder { get; private set; }
    public IBuildCosts Costs { get; private set; }
    public Vessel Pod { get; private set; }
    public PlayerTarget Target { get; private set; }
    public Hand Hand { get; private set; } = Hand.Gather;
    public Visor Visor { get; private set; }
    public Wrist Wrist { get; private set; }
    public CockpitConsole Console { get; private set; }
    public DebugHud Debug { get; private set; }

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
        ProcessPhysicsPriority = -20; // input before the physics host (-10) steps
        Zone = new SurfaceZone { Name = "Surface" };
        AddChild(Zone);
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
        Costs = new CatalogueCosts { Mode = BuildMode };

        // The pod comes down at the generator's landing site, standing level on its highest ground.
        Vector3 landing = Zone.LandingSite;
        _podHeading = -(float)Zone.Planet.Landing.Heading;
        Structure pod = Zone.Data.Blueprint("pod.gbp");
        float top = landing.Y;
        for (int i = 0; i < 9; i++)
        {
            Vector3 p = landing + new Vector3((i % 3 - 1) * 1.9f, 0f, (i / 3 - 1) * 1.9f);
            top = MathF.Max(top, Zone.GroundAt(p.X, p.Z).Y);
        }
        _podRest = new N.Vector3(landing.X, top + PhysicsSandbox.Bridges.StructureBridge.ShapeOf(pod).FeetBelowOrigin + 0.02f, landing.Z);
        _dropHeight = SkipIntro ? 0f : rules.PodDropHeight;
        _landingLength = SkipIntro ? 0f : rules.PodDropSeconds;
        Pod = Vessel.Spawn(Zone, pod, _podRest + N.Vector3.UnitY * _dropHeight, _podHeading, landed: true);
        Pod.Cargo.Add(new Dictionary<string, int>(Zone.Data.Rules.Materials.PodLocker));

        Survivor = Survivor.Spawn(Zone, _podRest + N.Vector3.UnitY * (_dropHeight + 0.3f));
        Survivor.Body.DamageSink = Suit = new Suit(Zone, Zone.Data.Survival(), (int)Zone.Spec.Seed);
        Suit.Said += line => Say(line, Tones.Caution);
        Suit.Sim.RespawnPoints.Add(new RespawnPoint
        {
            Id = "pod", Name = "Escape pod", Kind = RespawnKind.SurvivalTent,
            Position = new Pos(_podRest.X, _podRest.Y, _podRest.Z), Stand = new Pos(_podRest.X, _podRest.Y, _podRest.Z),
        });
        Suit.Sim.Pods[0].Position = new Pos(_podRest.X, _podRest.Y, _podRest.Z);
        Survivor.Sit(Pod);

        Tool = new SurvivalTool(Zone, Survivor, Suit, Pack) { Sfx = Zone.Sfx };
        var debris = new PlanetSurface.Fx.Debris { Name = "Debris", Collector = () => Survivor.WorldEye() + Vector3.Down * 0.5f };
        Zone.AddChild(debris);
        Tool.Debris = debris;
        Tool.Said += Say;
        Tool.Fired += (from, to, hit) => Visor.Tracer(from, to, hit);
        Builder = new BuildTool(Zone, Survivor, Pack, Costs);
        Builder.Said += Say;
        Builder.Finished += v => Say($"Fly it: F at the {v.Name}'s seat", Tones.Good);

        Target = new PlayerTarget(this);
        Zone.Wildlife.SetPlayer(Target);
        Zone.Wildlife.Attacked += OnBitten;
        Zone.Wildlife.Telegraphed += e => Visor.Threat(Zone.Wildlife.FromPatch(e.Creature.Position));
        Zone.Weather.Observer = () => Survivor.WorldFeet;
        Zone.Weather.Happened += OnWeather;

        Wrist = new Wrist { Name = "Wrist", Game = this };
        Camera.AddChild(Wrist);
        Console = new CockpitConsole { Name = "Console", Game = this };
        Zone.AddChild(Console);

        Phase = Phase.Landing;
        _waitingForGround = true;
        Say(SkipIntro ? "Escape pod down." : "Escape pod: re-entry. Brace.", Tones.Caution);
    }

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
        Say("Down. F to unstrap. The locker (E) has what the pod could carry.", Tones.Good);
        Landed?.Invoke();
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
        if (c.WasPressed(Key.V)) ThirdPerson = !ThirdPerson;
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
        s.Yaw -= c.Look.X * 0.0025f;
        s.Pitch = Mathf.Clamp(s.Pitch - c.Look.Y * 0.0025f, -1.5f, 1.5f);
        s.Legs.Move = new N.Vector2(c.Axis(Key.D, Key.A), c.Axis(Key.S, Key.W));
        s.Legs.Sprint = c.IsHeld(Key.Shift);
        s.Legs.Jump = c.IsHeld(Key.Space);
        s.Legs.Jetpack = c.IsHeld(Key.Space);
        s.Legs.Vertical = c.Axis(Key.Space, Key.Ctrl);

        if (c.WasPressed(Key.Key1)) Choose(Hand.Gather);
        if (c.WasPressed(Key.Key2)) Choose(Hand.Shoot);
        if (c.WasPressed(Key.Key3)) Choose(Hand.Build);
        if (c.WasPressed(Key.Key4)) Choose(Hand.Blueprint);
        if (c.WasPressed(Key.Tab)) Wrist.Toggle();
        if (c.WasPressed(Key.F)) TrySit();
        if (c.WasPressed(Key.E)) Use(c.IsHeld(Key.Shift));
        if (c.WasPressed(Key.B)) Report(Suit.Sim.DeployOrPack(FeetPos()));
        if (c.WasPressed(Key.H)) Report(Suit.Sim.ToggleHelmet());
        if (Hand == Hand.Build)
        {
            if (c.Wheel != 0) Builder.NextSlot(c.Wheel);
            if (c.WasPressed(Key.Bracketright)) Builder.NextSlot(1);
            if (c.WasPressed(Key.Bracketleft)) Builder.NextSlot(-1);
            if (c.WasPressed(Key.R)) Builder.Rotate(1);
            if (c.WasPressed(Key.T)) Builder.Rotate(0);
            if (c.WasPressed(Key.X))
            {
                Builder.MirrorX = !Builder.MirrorX;
                Say(Builder.MirrorX ? "Mirror left/right on" : "Mirror off", Tones.Text);
            }
        }
    }

    void Fly(Vessel vessel, Controls c)
    {
        // The mouse moves a virtual stick that recentres by itself (as in ship-handling).
        _stick = (_stick * 0.85f + c.Look * 0.012f).Clamp(new Vector2(-1, -1), new Vector2(1, 1));
        if (c.WasPressed(Key.F))
        {
            Survivor.StandUp();
            _stick = Vector2.Zero;
            return;
        }
        if (vessel.Flight == null) return;
        var move = new Vector3(c.Axis(Key.D, Key.A), c.Axis(Key.Space, Key.Ctrl), c.Axis(Key.S, Key.W));
        var turn = new Vector3(-_stick.Y, -_stick.X, c.Axis(Key.Q, Key.E));
        vessel.Flight.Input = PilotInput.Hands(move, turn, c.IsHeld(Key.Shift));
        if (c.WasPressed(Key.G)) vessel.Controller?.ToggleGear();
        if (c.WasPressed(Key.C)) vessel.Flight.ToggleCruise();
    }

    void Choose(Hand hand)
    {
        Hand = hand;
        Builder.Hide();
        switch (hand)
        {
            case Hand.Gather: Tool.Mode = ToolMode.Gather; Say("Survival tool: gather (dig ore, cut plants)", Tones.Text); break;
            case Hand.Shoot: Tool.Mode = ToolMode.Shoot; Say("Survival tool: shoot", Tones.Text); break;
            case Hand.Build:
                Builder.Style = BuildStyle.Free;
                Say($"Build: {Builder.Choice.Label} (wheel to change, R rotate, X mirror, right click takes down)", Tones.Text);
                break;
            case Hand.Blueprint:
                if (Builder.Underway == null && Builder.Plan == null) Builder.Follow(Zone.Data.Blueprint(Zone.Data.Rules.Build.StarterBlueprint));
                else Builder.Style = BuildStyle.Blueprint;
                Say("Blueprint: set it down on open ground, then hold the button to build it", Tones.Text);
                break;
        }
    }

    // ---- Using things --------------------------------------------------------------------------

    /// <summary>F on foot: the nearest cockpit seat within reach.</summary>
    void TrySit()
    {
        Vector3 eye = Survivor.WorldEye();
        Vessel best = null;
        float bestDistance = 3.2f;
        foreach (Vessel v in Zone.Vessels)
        {
            if (v.Seat == null || v.IsWreck) continue;
            float d = v.Body.ToParent(v.Seat.Value).ToGodot().DistanceTo(eye);
            if (d < bestDistance) { best = v; bestDistance = d; }
        }
        if (best == null) Say("No seat within reach", Tones.Caution);
        else if (Survivor.Sit(best)) Say(best == Pod ? "Strapped into the pod's seat" : $"At the {best.Name}'s controls: WASD, Space/Ctrl, mouse, Q/E roll, G gear", Tones.Text);
    }

    /// <summary>
    /// E: feed at a replicator within reach; otherwise a container under the crosshair: take
    /// everything in it (Shift: store the pack's materials in it instead).
    /// </summary>
    void Use(bool store)
    {
        Replicator rep = Suit.Sim.NearestReplicator(FeetPos(), 2.5);
        if (rep != null)
        {
            Report(Suit.Sim.Feed(rep));
            return;
        }
        Vector3 eye = Survivor.WorldEye(), dir = Survivor.WorldLook();
        var query = PhysicsRayQueryParameters3D.Create(eye, eye + dir * 3.5f, Layers.Hull);
        Godot.Collections.Dictionary hit = Zone.GetWorld3D().DirectSpaceState.IntersectRay(query);
        Vessel v = hit.Count > 0 ? Zone.VesselOf(Zone.Host.BodyOf((GodotObject)hit["collider"])) : null;
        if (v == null)
        {
            Say("Nothing to use here", Tones.Caution);
            return;
        }
        if (store)
        {
            int moved = 0;
            foreach (var (item, n) in Pack.Items.ToList())
            {
                int put = v.Store(item, n);
                Pack.Items[item] = n - put;
                moved += put;
            }
            Say(moved > 0 ? $"Stored {moved} units in the {v.Name}" : $"The {v.Name} has no room (build a container)", moved > 0 ? Tones.Good : Tones.Caution);
            return;
        }
        if (v == Pod && Suit.Sim.Pods[0].Rations > 0) Report(Suit.Sim.LootPod(Suit.Sim.Pods[0]));
        if (v.Cargo.Total > 0)
        {
            string list = string.Join(", ", v.Cargo.Items.Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {kv.Key}"));
            foreach (var (item, n) in v.Cargo.Items.ToList()) Pack.Add(new Dictionary<string, int> { [item] = n });
            v.Cargo.Items.Clear();
            Say($"Took {list}", Tones.Good);
        }
        else if (v != Pod || Suit.Sim.Pods[0].Rations == 0) Say($"The {v.Name}'s storage is empty", Tones.Text);
    }

    void Report(Outcome o) => Say(o.Message, o.Ok ? Tones.Good : Tones.Caution);

    Pos FeetPos()
    {
        N.Vector3 p = Survivor.Body.WorldPosition;
        return new Pos(p.X, p.Y, p.Z);
    }

    // ---- Each tick: after the physics ----------------------------------------------------------

    /// <summary>After the physics host and the world have stepped: the body's frame, the suit and the tools.</summary>
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
        bool hands = !Suit.Dead && Survivor.Seated == null;
        if (hands && Hand is Hand.Gather or Hand.Shoot) Tool.Tick(dt, Controls.Primary);
        else if (hands) Builder.Tick(dt, Controls.Primary, Controls.PrimaryPressed, Controls.SecondaryPressed);
        if (!hands || Hand is Hand.Gather or Hand.Shoot) Builder.Hide();
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

    void Respawn()
    {
        Outcome o = Suit.Sim.Respawn(RespawnChoice.NearestTent);
        if (!o.Ok) return;
        Survivor.PlaceAboard(Pod, new N.Vector3(0f, 0.3f, -0.5f));
        Survivor.Body.Velocity = N.Vector3.Zero;
        Report(o);
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
        Controls.Primary = captured && Input.IsMouseButtonPressed(MouseButton.Left);
        Controls.Secondary = captured && Input.IsMouseButtonPressed(MouseButton.Right);
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
                if (Input.MouseMode != Input.MouseModeEnum.Captured)
                {
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                    break;
                }
                if (button.ButtonIndex == MouseButton.Left) Controls.PrimaryPressed = true;
                else if (button.ButtonIndex == MouseButton.Right) Controls.SecondaryPressed = true;
                else if (button.ButtonIndex == MouseButton.WheelUp) Controls.Wheel--;
                else if (button.ButtonIndex == MouseButton.WheelDown) Controls.Wheel++;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.PhysicalKeycode == Key.Escape) Input.MouseMode = Input.MouseModeEnum.Visible;
                else Controls.Tap(key.PhysicalKeycode);
                break;
        }
    }

    // ---- The camera -----------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Suit != null) SyncReplicators();
        // A vessel in flight gets collision a column further out than someone on foot.
        if (Zone.IsBuilt) Zone.Streamer.CollisionRadius = Survivor?.Seated?.Flight != null && Survivor.Seated.Controller?.Frozen == false ? 3 : 2;
        if (Survivor == null)
        {
            if (Zone.IsBuilt && Pod != null) Camera.GlobalTransform = Chase(Pod, 2.4f, 0.6f);
            return;
        }
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
