using Godot;

namespace WalkableShip;

/// <summary>
/// Renders one view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/walkable-ship res://tests/Screenshot.tscn -- --view=cabin --out=shot.png
/// Views: cabin, stunt, cockpit, outside.
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 2;
    const int CaptureTick = 200;

    Main _main;
    int _tick;
    string _view = "cabin";
    string _out = "shot.png";

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
        if (_tick == SetupTick) Setup(_main.Ships[0], _main.Player);
        if (_tick == CaptureTick) Capture();
    }

    void Setup(Ship ship, Player player)
    {
        switch (_view)
        {
            case "cabin":
                player.DebugPlaceAboard(new Vector3(1.6f, 0.05f, 9.5f), Vector3.Zero);
                player.DebugSetLook(0.12f, -0.05f);
                break;
            case "stunt":
                ship.Position = new Vector3(0, 90, 0);
                ship.StuntIntensity = 1.4f;
                ship.StuntAutopilot = true;
                player.DebugPlaceAboard(new Vector3(-1.5f, 0.05f, 2f), Vector3.Zero);
                player.DebugSetLook(1.1f, 0.05f);
                break;
            case "cockpit":
                ship.Position = new Vector3(120, 45, 300);
                player.DebugSit();
                break;
            case "outside":
                player.DebugPlaceOutside(new Vector3(-16f, 0.05f, 34f));
                player.DebugSetLook(-0.42f, 0.04f);
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
