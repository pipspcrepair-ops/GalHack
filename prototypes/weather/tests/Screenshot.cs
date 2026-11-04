using Godot;
using Weather.Model;

namespace Weather;

/// <summary>
/// Renders one weather view to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/weather res://tests/Screenshot.tscn -- --view=thunder --out=shot.png
/// Views: clear, thunder, dust, blizzard, tornado, fire, hurricane, fog.
/// Each sets up its weather instantly, then waits for its moment (a strike on the spire, the
/// skiff at the top of its lift) before capturing.
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 2;
    const int SettleTicks = 240;   // particles and noise textures fill in
    const int GiveUpTick = 60 * 40;

    Main _main;
    int _tick;
    string _view = "clear";
    string _out = "shot.png";
    bool _capturing;
    float _hoverGround;
    Vector3 _feet;
    float _yaw, _pitch;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        ProcessPhysicsPriority = 100;
        _main = new Main();
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_capturing) return;
        if (_tick == SetupTick) Setup();
        if (_tick > SetupTick) Hold();
        if (_tick > SettleTicks && Ready()) Capture();
        else if (_tick > GiveUpTick)
        {
            GD.PrintErr($"{_view}: its moment never came, capturing anyway");
            Capture();
        }
    }

    WeatherSim Sim => _main.Weather.Sim;
    Player Player => _main.Player;

    void Setup()
    {
        _main.Hud.ShowHelp = false;
        switch (_view)
        {
            case "clear":
                Weather(WeatherKind.Clouds);
                Look(new Vector3(9f, 0f, 16f), _main.Spire.RodTip - new Vector3(0, 30, 0), -0.02f);
                break;
            case "thunder":
                Weather(WeatherKind.Thunderstorm);
                Ground(0.85f, 0f, 0f);
                Look(new Vector3(14f, 0f, -40f), _main.Spire.RodTip - new Vector3(0, 25, 0), 0.04f);
                break;
            case "dust":
                // The skiff straining at its line, the sun a pale disc through the dust.
                Weather(WeatherKind.DustStorm);
                Ground(0f, 0f, 0.7f);
                // Down-sun of the rover: its silhouette under a pale sun.
                LookFrom(Main.RoverSpot + new Vector2(0.85f, 0.32f) * 10f, Terrain.OnGround(Main.RoverSpot.X, Main.RoverSpot.Y, 3.5f));
                break;
            case "blizzard":
                Weather(WeatherKind.Blizzard);
                Ground(0f, 0.9f, 0f);
                // The rover turned toward us, its lights burning through the whiteout. Up-wind of the
                // skiff, so its mooring line drifts it away from the lens, not into it.
                Vector2 eye = Main.RoverSpot + new Vector2(5f, 8.5f);
                _main.Rover.GlobalTransform = new Transform3D(new Basis(Vector3.Up, -2.25f), Terrain.OnGround(Main.RoverSpot.X, Main.RoverSpot.Y, 0.05f));
                LookFrom(eye, Terrain.OnGround(Main.RoverSpot.X, Main.RoverSpot.Y, 2.4f));
                break;
            case "fog":
                Weather(WeatherKind.Fog);
                Look(new Vector3(0f, 0f, 9f), new Vector3(0f, 2f, -10f), 0f);
                break;
            case "hurricane":
                Weather(WeatherKind.Hurricane);
                Ground(0.9f, 0f, 0f);
                Look(new Vector3(14f, 0f, 10f), new Vector3(-6f, 2f, -12f), 0f);
                break;
            case "fire":
                Weather(WeatherKind.FireStorm);
                Ground(0f, 0f, 0.4f);
                Look(new Vector3(-20f, 0f, -60f), _main.Spire.RodTip - new Vector3(0, 35, 0), 0.12f);
                break;
            case "tornado":
                Weather(WeatherKind.Tornado);
                Ground(0.4f, 0f, 0f);
                Vector3 hover = _main.Hover.GlobalPosition;
                _hoverGround = hover.Y;
                Look(new Vector3(58f, 0f, 30f), hover + new Vector3(0, 30, 0), 0.3f);
                break;
        }
    }

    /// <summary>Things that have to happen at a particular moment after the weather has settled.</summary>
    void Hold()
    {
        Player.DebugPlace(_feet, _yaw, _pitch); // the wind would otherwise walk the camera off its mark
        if (_view == "thunder" && _tick == SettleTicks - 200)
            Sim.StartStrike(HazardKind.Lightning, _main.Spire.RodTip); // charges for 4 s, then strikes the spire
        if (_view == "fire" && _tick == SettleTicks - 150)
            Sim.StartStrike(HazardKind.Debris, Player.GlobalPosition + Player.LookDirection.Slide(Vector3.Up).Normalized() * 55f);
        if (_view == "tornado" && _tick == SettleTicks - 150)
        {
            // Right beside the skiff, heading away from the camera, so it carries the skiff up in view.
            Sim.ClearTornadoes();
            Vector3 at = _main.Hover.GlobalPosition + new Vector3(-4f, 0f, 3f);
            Tornado t = Sim.SpawnTornado(at, at + new Vector3(-100f, 0, -60f));
            t.Homing = false;
            t.SkipFormation();
        }
    }

    new bool Ready()
    {
        switch (_view)
        {
            case "thunder":
                foreach (var e in Sim.Events)
                    if (e.Kind == WeatherEventKind.LightningStrike && e.Target?.Name == "the spire") return true;
                return false;
            case "fire":
                foreach (Hazard h in Sim.Hazards) if (h.Kind == HazardKind.Debris && h.Progress > 0.72f) return true;
                return false;
            case "tornado":
                return _main.Hover.GlobalPosition.Y - _hoverGround > 24f;
            default:
                return true;
        }
    }

    void Weather(WeatherKind kind)
    {
        Sim.Snap(kind);
        _main.Fx.SnapLook();
    }

    void Ground(float wet, float snow, float dust) => Sim.DebugSetGround(new GroundCover { Wetness = wet, Snow = snow, Dust = dust });

    void LookFrom(Vector2 spot, Vector3 at) => Look(new Vector3(spot.X - Main.SpawnSpot.X, 0f, spot.Y - Main.SpawnSpot.Y), at, 0f);

    /// <summary>Stands the player at a spot near the pad, looking at a point.</summary>
    void Look(Vector3 offset, Vector3 at, float extraPitch)
    {
        var feet = Terrain.OnGround(Main.SpawnSpot.X + offset.X, Main.SpawnSpot.Y + offset.Z, 0.05f);
        Vector3 eye = feet + Vector3.Up * 1.6f;
        Vector3 d = (at - eye).Normalized();
        (_feet, _yaw, _pitch) = (feet, Mathf.Atan2(-d.X, -d.Z), Mathf.Asin(d.Y) + extraPitch);
        Player.DebugPlace(_feet, _yaw, _pitch);
    }

    async void Capture()
    {
        _capturing = true;
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        await ToSignal(RenderingServer.Singleton, "frame_post_draw"); // two frames: the flash has lit the scene
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
