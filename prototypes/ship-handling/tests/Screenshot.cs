using Godot;
using ShipHandling.Handling;
using ShipHandling.Physics;

namespace ShipHandling;

/// <summary>
/// Renders one scripted moment of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/ship-handling res://tests/Screenshot.tscn -- --view=lake --out=shot.png
/// Views: lake, hills, capital, crater, space, overview.
/// </summary>
public partial class Screenshot : Node
{
    Main _main;
    int _tick;
    string _view = "overview";
    string _out = "shot.png";
    int _captureTick = 150;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        ProcessPhysicsPriority = -30; // set inputs before Main and the physics module run
        _main = new Main { ScriptedInput = true, CaptureMouse = false };
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_tick == 3) Setup();
        if (_tick > 3) Drive();
        if (_tick == _captureTick) Capture();
    }

    Vessel Rover => _main.Vessels[0];
    Vessel Fighter => _main.Vessels[1];
    Vessel Capital => _main.Vessels[2];

    void Setup()
    {
        FlightCamera camera = _main.Camera;
        switch (_view)
        {
            case "lake":
                _main.Select(0);
                Rover.Place(new Vector3(PlanetSurface.LakeCenter.X + 70f, PlanetSurface.WaterLevel + 1.5f, PlanetSurface.LakeCenter.Y + 8f), 90f);
                Rover.State.Velocity = new Vector3(-38f, 0f, 0f);
                camera.Orbit = new Vector2(0.75f, -0.12f);
                camera.Zoom = 1.25f;
                _captureTick = 110;
                break;
            case "hills":
                _main.Select(1);
                Fighter.Place(new Vector3(-210f, 52f, 230f), 35f);
                Fighter.State.Velocity = Fighter.GlobalBasis * new Vector3(0, 0, -55f);
                camera.Orbit = new Vector2(-0.35f, -0.08f);
                camera.Zoom = 1.1f;
                _captureTick = 75;
                break;
            case "capital":
                _main.Select(2);
                Capital.Place(new Vector3(60f, 70f, 120f), 20f);
                Capital.State.Velocity = Capital.GlobalBasis * new Vector3(0, 0, -12f);
                camera.Orbit = new Vector2(0.55f, -0.04f);
                camera.Zoom = 0.95f;
                _captureTick = 240;
                break;
            case "space":
                _main.Select(1);
                Fighter.Place(new Vector3(0f, _main.Tunables.Planet.AtmosphereCeiling + 120f, 300f), 200f);
                Fighter.State.Orientation = new Basis(Vector3.Up, Mathf.DegToRad(200f)) * new Basis(Vector3.Right, -0.35f);
                Fighter.State.Velocity = Fighter.State.Orientation * new Vector3(0, 0, -110f);
                camera.Orbit = new Vector2(-0.5f, -0.1f);
                _captureTick = 90;
                break;
            case "crater":
                _main.Select(1);
                Fighter.Place(new Vector3(-20f, 90f, -120f), 0f);
                Fighter.State.Orientation = new Basis(Vector3.Right, -0.9f);
                Fighter.State.Velocity = new Vector3(0f, -58f, -9f);
                _captureTick = 480;
                break;
        }
    }

    void Drive()
    {
        switch (_view)
        {
            case "lake":
                Rover.Input = PilotInput.Hands(Vector3.Forward, new Vector3(0, 0.08f, 0), boost: _tick > 60);
                break;
            case "hills":
                Fighter.Input = PilotInput.Hands(Vector3.Forward * 0.8f, new Vector3(0.05f, 0.55f, _tick < 40 ? 0.9f : 0.15f));
                break;
            case "space":
                Fighter.Input = PilotInput.Hands(Vector3.Forward, new Vector3(0f, 0.3f, -0.3f));
                break;
            case "capital":
                Capital.Input = PilotInput.Hands(Vector3.Forward, new Vector3(0f, 1f, 0f)); // full ahead, hard over
                break;
            case "crater":
                Fighter.Input = PilotInput.Autopilot;
                if (_tick == 450)
                {
                    // Step back from the wreck and look at the hole it made.
                    var camera = _main.Camera;
                    camera.Target = null;
                    Vector3 crater = _main.Physics.Craters.Count > 0 ? _main.Physics.Craters[0].Position : Fighter.GlobalPosition;
                    Vector3 eye = crater + new Vector3(16f, 13f, 20f);
                    camera.GlobalTransform = new Transform3D(Basis.LookingAt(crater + new Vector3(-2f, 0f, -4f) - eye, Vector3.Up), eye);
                    camera.ResetPhysicsInterpolation();
                }
                break;
        }
    }

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
