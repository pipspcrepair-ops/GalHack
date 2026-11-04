using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PhysicsSandbox.Bridges;
using PhysicsSandbox.World;
using ShipHandling.Handling;
using Weather.Model;
using N = System.Numerics;

namespace PhysicsSandbox;

/// <summary>
/// Renders one scripted moment of the sandbox to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/physics-sandbox res://tests/Screenshot.tscn -- --view=flight --out=shot.png
/// Views: flight (the Corsair low over the dug lane, the rover crossing its trench), crash (a
/// wreck in the crater it cut), sever (an engine pod shot off and tumbling away), hull (a player
/// riding the roof of the flying Corsair), cabin (inside the cabin mid-roll), tornado (a crate and
/// the player lifted by a funnel).
/// </summary>
public partial class Screenshot : Node
{
    Main _main;
    SandboxWorld W => _main.World;
    int _tick;
    string _view = "flight";
    string _out = "shot.png";
    int _captureTick = 120;
    bool _captured;
    N.Vector3? _crater;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        ProcessPhysicsPriority = -30; // inputs before Main and the physics host
        _main = new Main { Interactive = false };
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_tick == 3) Setup();
        if (_tick > 3) Drive();
        if (_tick == _captureTick && !_captured)
        {
            _captured = true;
            Capture();
        }
    }

    public override void _Process(double delta)
    {
        if (_tick > 3) Frame();
    }

    Ship Corsair => W.Corsair;
    Ship Rover => W.Rover;
    Player Player => W.Player;

    void Setup()
    {
        _main.Hud.ShowHelp = false;
        _main.Hud.Visible = false;
        W.Weather.Calm = true;
        switch (_view)
        {
            case "flight":
            {
                W.Weather.Calm = false;
                W.Weather.Sim.Snap(WeatherKind.DustStorm);
                float lane = SandboxTerrain.LaneZ;
                Corsair.Vessel.Place(Corsair.Body, W.Ground(-62f, lane + 8f) + N.Vector3.UnitY * 8f, -Mathf.Pi / 2f);
                Corsair.Body.Velocity = new N.Vector3(34f, 0f, 0f);
                Corsair.Binding.Teleport();
                Rover.Vessel.Place(Rover.Body, W.Ground(-30f, lane) + N.Vector3.UnitY * 2.3f, -Mathf.Pi / 2f);
                Rover.Body.Velocity = new N.Vector3(16f, 0f, 0f);
                Rover.Binding.Teleport();
                Player.PlaceOutside(W.Ground(6f, lane + 12f) + N.Vector3.UnitY * 0.05f);
                _captureTick = 100;
                break;
            }
            case "crash":
                Corsair.Vessel.Place(Corsair.Body, W.Ground(-6f, -8f) + N.Vector3.UnitY * 60f, 0.5f); // the level landing field: it bounces straight back into its hole
                Corsair.Body.Rotation = MathX.ThenLocal(Corsair.Body.Rotation, MathX.AxisAngle(N.Vector3.UnitX, -0.7f));
                Corsair.Body.Velocity = new N.Vector3(0f, -88f, -2f); // past what its hull can take: a wreck
                Corsair.Binding.Teleport();
                W.Physics.Cratered += c => _crater ??= c.Center;
                _captureTick = 330;
                break;
            case "sever":
                Corsair.Vessel.Place(Corsair.Body, new N.Vector3(-55f, 58f, 20f), -Mathf.Pi / 2f + 0.4f);
                Corsair.Body.Velocity = MathX.Forward(Corsair.Body.Rotation) * 26f;
                Corsair.Binding.Teleport();
                _captureTick = 96;
                break;
            case "hull":
                Corsair.Vessel.Place(Corsair.Body, W.Ground(-20f, 25f) + N.Vector3.UnitY * 16f, -Mathf.Pi / 2f);
                Corsair.Binding.Teleport();
                Player.PlaceOutside(Corsair.Body.ToParent(new N.Vector3(-0.9f, 2.82f, -1.2f)));
                Player.Yaw = -Mathf.Pi / 2f + 0.5f;
                _captureTick = 170;
                break;
            case "cabin":
                Corsair.Vessel.Place(Corsair.Body, W.Ground(-40f, 30f) + N.Vector3.UnitY * 30f, -Mathf.Pi / 2f);
                Corsair.Binding.Teleport();
                Player.PlaceAboard(Corsair, new N.Vector3(-0.9f, 0.3f, 2.4f));
                Player.Yaw = -0.35f;
                Player.Pitch = -0.12f;
                _captureTick = 150;
                break;
            case "tornado":
            {
                W.Weather.Calm = false;
                W.Weather.Sim.Snap(WeatherKind.Tornado);
                N.Vector3 spot = W.Ground(-35f, 30f);
                Player.PlaceOutside(spot + N.Vector3.UnitY * 0.05f);
                for (int i = 0; i < 3; i++) Crate.Spawn(W, spot + new N.Vector3(2.5f + i * 1.1f, 0.5f, -1.5f + i), i + 1);
                Tornado t = W.Weather.Sim.SpawnTornado((spot + new N.Vector3(-4f, 0f, 1f)).ToGodot(), (spot + new N.Vector3(40f, 0f, 0f)).ToGodot());
                t.SkipFormation();
                _captureTick = 150;
                break;
            }
        }
    }

    void Drive()
    {
        switch (_view)
        {
            case "flight":
                Corsair.Flight.Input = PilotInput.Hands(new Vector3(0f, 0.05f, -0.5f), new Vector3(0f, 0.12f, 0f));
                Rover.Flight.Input = PilotInput.Hands(new Vector3(0f, 0f, -0.35f));
                break;
            case "sever":
                Corsair.Flight.Input = PilotInput.Hands(new Vector3(0f, 0f, -0.4f), new Vector3(0f, 0.15f, 0f));
                if (_tick == 34) _main.ShootPod();
                break;
            case "hull":
                Corsair.Flight.Input = PilotInput.Hands(new Vector3(0f, _tick < 90 ? 0.08f : 0f, -0.35f), new Vector3(0f, 0.18f, 0f));
                break;
            case "cabin":
                Corsair.Flight.Input = PilotInput.Hands(new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0.12f, _tick > 110 && _tick < 140 ? 0.5f : 0f));
                break;
        }
    }

    /// <summary>Each view's camera, from interpolated transforms.</summary>
    void Frame()
    {
        switch (_view)
        {
            case "flight":
            {
                N.Vector3 g = W.Ground(-6f, SandboxTerrain.LaneZ);
                var at = new Vector3(-3f, g.Y + 3f, SandboxTerrain.LaneZ + 1f);
                var eye = new Vector3(-20f, g.Y + 8f, SandboxTerrain.LaneZ + 19f);
                _main.FixedCamera = new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), eye);
                break;
            }
            case "crash":
            {
                // Frame the hole and the wreck together, from above and to one side.
                Vector3 wreck = Corsair.Node.GetGlobalTransformInterpolated().Origin;
                Vector3 hole = _crater?.ToGodot() ?? wreck;
                Vector3 middle = (wreck + hole) * 0.5f + Vector3.Down * 2f;
                float span = Mathf.Max(8f, (wreck - hole).Length());
                Vector3 eye = middle + new Vector3(0.9f, 0.75f, 1.1f) * (span + 10f);
                _main.FixedCamera = new Transform3D(Basis.LookingAt(middle - eye, Vector3.Up), eye);
                break;
            }
            case "sever":
            {
                Transform3D t = Corsair.Node.GetGlobalTransformInterpolated();
                Vector3 eye = t.Origin + t.Basis * new Vector3(-13f, 4f, 14f);
                _main.FixedCamera = new Transform3D(Basis.LookingAt(t.Origin + t.Basis * new Vector3(0f, 0f, 3f) - eye, Vector3.Up), eye);
                break;
            }
            case "hull":
            {
                Transform3D eye = _main.Eye(Player);
                Vector3 behind = eye.Origin + eye.Basis * new Vector3(1.6f, 1.2f, 4.6f);
                _main.FixedCamera = new Transform3D(Basis.LookingAt(eye.Origin + eye.Basis * new Vector3(0f, -1.1f, -2f) - behind, Vector3.Up), behind);
                break;
            }
            case "cabin":
                _main.FixedCamera = _main.Eye(Player);
                _main.FixedIsEye = true;
                break;
            case "tornado":
            {
                Vector3 p = Player.Node.GetGlobalTransformInterpolated().Origin;
                N.Vector3 spot = W.Ground(-35f, 30f);
                var eye = new Vector3(spot.X + 16f, spot.Y + 3f, spot.Z + 24f);
                var at = new Vector3(p.X, (p.Y + spot.Y) * 0.5f + 4f, p.Z);
                _main.FixedCamera = new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), eye);
                break;
            }
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
