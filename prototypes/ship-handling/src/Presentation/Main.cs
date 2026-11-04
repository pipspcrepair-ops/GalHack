using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipHandling.Handling;
using ShipHandling.Physics;

namespace ShipHandling;

/// <summary>
/// Builds the playable prototype in code: the planet surface, the physics module with three
/// vessels (a hover rover, a small fighter and a capital ship), their art, the sky, a chase
/// and cockpit camera, and the HUD. Reads the keyboard and mouse into the active vessel's
/// pilot input; every other vessel is left to its autopilot.
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Where each vessel starts: the rover hovering by the pad, the others landed on it.</summary>
    public static readonly (Vector3 position, float yaw)[] Spawns =
    {
        (new Vector3(10f, 1.5f, 50f), 90f),
        (new Vector3(35f, 2.4f, 0f), 0f),
        (new Vector3(-35f, 11f, 15f), 0f),
    };

    public Tunables Tunables { get; private set; }
    public FlightPhysics Physics { get; private set; }
    public PlanetSurface Surface { get; private set; }
    public List<Vessel> Vessels { get; } = new();
    public List<VesselLoadout> Loadouts { get; } = new();
    public FlightCamera Camera { get; private set; }
    public Hud Hud { get; private set; }
    public SkyDome Sky { get; private set; }

    public int ActiveIndex { get; private set; }
    public Vessel Active => Vessels[ActiveIndex];

    /// <summary>Screenshots and demos set vessel inputs themselves; the keyboard is then ignored.</summary>
    public bool ScriptedInput;

    /// <summary>Grab the mouse for steering. Screenshots leave it alone.</summary>
    public bool CaptureMouse = true;

    /// <summary>False after F: the pilot has stood up and the autopilot holds course.</summary>
    public bool Seated { get; private set; } = true;

    readonly List<VesselLoadout> _originals = new();
    Vector2 _stick;
    bool _spaceHeld;

    const float MouseSensitivity = 0.0035f, StickRecenter = 1.2f, StickDeadZone = 0.04f;

    public static Tunables LoadTunables() => Tunables.FromJson(FileAccess.GetFileAsString("res://data/handling.json"));

    public override void _Ready()
    {
        ProcessPhysicsPriority = -20; // input first, then the physics module, then the camera
        Tunables = LoadTunables();

        Sky = new SkyDome { Name = "Sky", Ceiling = Tunables.Planet.AtmosphereCeiling };
        AddChild(Sky);
        Surface = new PlanetSurface { Name = "Surface" };
        AddChild(Surface);

        Physics = new FlightPhysics(Tunables);
        AddChild(Physics);
        AddChild(new CraterArt(Physics));

        for (int i = 0; i < 3; i++)
        {
            VesselLoadout loadout = VesselDesigns.Create(i, Tunables);
            _originals.Add(loadout.Clone());
            Loadouts.Add(loadout);
            Vessel vessel = Physics.Spawn(loadout, Spawns[i].position, Spawns[i].yaw);
            vessel.AddChild(new VesselArt(vessel));
            Vessels.Add(vessel);
        }
        ResetVessel(0);
        ResetVessel(1);
        ResetVessel(2);

        Camera = new FlightCamera { Name = "Camera" };
        AddChild(Camera);
        Camera.MakeCurrent();

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Physics.Impact += hit => Hud.Log(Describe(hit));
        Physics.Landed += v => Hud.Log($"{v.Structure.Name} landed and stopped simulating");
        Physics.LiftedOff += v => Hud.Log($"{v.Structure.Name} lifted off");

        Select(1);
        if (CaptureMouse && DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    static string Describe(ImpactInfo hit)
    {
        string what = hit.Other == null ? "the ground" : hit.Other.Structure.Name;
        string text = $"{hit.Vessel.Structure.Name} hit {what} at {hit.Speed:F0} m/s";
        if (hit.Damage <= 0f) return text + ": slid off, no damage";
        text += $": {hit.Damage:F1} damage" + (hit.Other != null ? " each" : "");
        return hit.CraterRadius > 0f ? text + $", {hit.CraterRadius:F1} m crater" : text;
    }

    public void Select(int index)
    {
        ActiveIndex = index;
        Camera.Target = Active;
        Camera.Snap();
        Seated = true;
    }

    /// <summary>Back to the original build at its spawn, undamaged.</summary>
    public void ResetVessel(int index)
    {
        Loadouts[index].CopyFrom(_originals[index]);
        Vessel v = Vessels[index];
        v.ResetDamage();
        var (position, yaw) = Spawns[index];
        if (v.Gear != null) v.PlaceLanded(position, yaw);
        else v.Place(position, yaw);
        if (index == ActiveIndex) Camera?.Snap();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _stick = (_stick + motion.Relative * MouseSensitivity).LimitLength(1f);
            return;
        }
        if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;

        VesselLoadout loadout = Loadouts[ActiveIndex];
        switch (key.PhysicalKeycode)
        {
            case Key.Key1: Select(0); break;
            case Key.Key2: Select(1); break;
            case Key.Key3: Select(2); break;
            case Key.C: Active.ToggleCruise(); break;
            case Key.G: Active.ToggleGear(); break;
            case Key.V:
                Camera.Cockpit = !Camera.Cockpit;
                Camera.Snap();
                break;
            case Key.F:
                Seated = !Seated;
                Hud.Log(Seated ? "Pilot back in the seat" : "Pilot stood up: autopilot holds course");
                break;
            case Key.Bracketright:
                AddMainEngine(loadout);
                Hud.Log($"Added a {VesselDesigns.MainEngineSize(loadout.Class)} main engine");
                break;
            case Key.Bracketleft:
                Hud.Log(loadout.RemoveThruster(Vector3.Forward) ? "Removed a main engine" : "No main engines left");
                break;
            case Key.K:
                loadout.AddCargo(50f);
                Hud.Log($"Loaded 50 t of cargo ({loadout.CargoMass:F0} t aboard)");
                break;
            case Key.L:
                loadout.MakeLopsided();
                Hud.Log("Every thruster bolted onto the left flank: the numbers don't change, and it still flies true");
                break;
            case Key.Backspace:
                ResetVessel(ActiveIndex);
                Hud.Log($"{Active.Structure.Name} reset");
                break;
            case Key.Escape:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                break;
        }
    }

    /// <summary>The ] key: one more main engine of the class's main size, bolted on beside the others.</summary>
    void AddMainEngine(VesselLoadout loadout)
    {
        string size = VesselDesigns.MainEngineSize(loadout.Class);
        Tunables.ThrusterPart part = Tunables.Parts.Thrusters[size];
        var mains = loadout.ThrusterList.Where(t => t.Facing.Dot(Vector3.Forward) > 0.9f).ToList();
        Vector3 anchor = mains.Count > 0 ? mains[0].Position : new Vector3(0f, 0f, loadout.Blocks.Max(b => b.Center.Z + b.Size.Z / 2f));
        float spacing = size == "capital" ? 5f : size == "large" ? 1.4f : 0.6f;
        int n = mains.Count;
        float side = n % 2 == 0 ? -1f : 1f;
        var position = new Vector3(side * (Mathf.Abs(anchor.X) + spacing * (n / 2)), anchor.Y, anchor.Z);
        loadout.AddThruster(new ThrusterSpec(size, position, Vector3.Forward, part.Thrust, part.Mass));
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _stick = _stick.MoveToward(Vector2.Zero, StickRecenter * dt);
        if (ScriptedInput) return;

        foreach (Vessel v in Vessels)
            if (v != Active) v.Input = PilotInput.Autopilot;

        bool space = Held(Key.Space);
        bool jump = space && !_spaceHeld;
        _spaceHeld = space;
        if (!Seated)
        {
            Active.Input = PilotInput.Autopilot;
            return;
        }

        bool hover = Active.Stats.IsHover;
        Vector2 stick = _stick.Length() < StickDeadZone ? Vector2.Zero : _stick;
        Active.Input = new PilotInput
        {
            Move = new Vector3(Axis(Key.D, Key.A), hover ? 0f : Axis(Key.Space, Key.Ctrl), Axis(Key.S, Key.W)),
            Turn = new Vector3(-stick.Y, -stick.X, Axis(Key.Q, Key.E)),
            Boost = Held(Key.Shift),
            Jump = hover && jump,
            Seated = true,
        };
    }

    public override void _Process(double delta)
    {
        Sky.Follow(Camera.GlobalPosition);
        Hud.Show(Active, Loadouts[ActiveIndex], Seated, _stick);
    }

    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
}
