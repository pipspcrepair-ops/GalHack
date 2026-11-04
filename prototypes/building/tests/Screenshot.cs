using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;

namespace BlockBuilding;

/// <summary>
/// Renders one view of the build scene to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/building res://tests/Screenshot.tscn -- --view=ship --out=shot.png
/// Views: ship (the example ship in full livery), build (ghost preview with mirror planes),
/// air (the visor overlay showing a leak), wreck (a nacelle blasted off and lying on the pad),
/// livery (a close look at the paint).
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 3;

    Main _main;
    int _tick, _captureTick = 120;
    string _view = "ship", _out = "shot.png";

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
        if (_tick == SetupTick) Setup();
        if (_tick == _captureTick) Capture();
    }

    void Setup()
    {
        Player player = _main.Player;
        BuildController build = _main.Build;
        StructureView ship = _main.Ship;
        Vector3 Cell(int x, int y, int z) => ship.CellToGlobal(new Int3(x, y, z));
        switch (_view)
        {
            case "ship":
                _main.Hud.Visible = false;
                build.PreviewEnabled = false;
                player.DebugPlace(new Vector3(-22f, 0.9f, -25f), 0, 0, fly: true);
                player.DebugLookAt(Cell(0, 1, 1) + Vector3.Up * 1.2f);
                break;
            case "livery":
                _main.Hud.Visible = false;
                build.PreviewEnabled = false;
                player.DebugPlace(new Vector3(-13f, 3.5f, -24f), 0, 0, fly: true);
                player.DebugLookAt(Cell(-4, 2, -5));
                break;
            case "build":
                // A steel slope with the X mirror on: the ghost and its mirror image on the roof,
                // the mirror plane through the core.
                build.SetActive(ship);
                ToggleKey(Key.X);
                Press(Key.Key6);
                Press(Key.Key6);
                Press(Key.Key6); // slot 6, third variant: the turret
                player.DebugPlace(new Vector3(15f, 24f, 36f), 0, 0, fly: true);
                player.DebugLookAt(Cell(6, 2, 7) + Vector3.Up * (ship.CellSize * 0.5f - 0.05f));
                break;
            case "air":
                build.SetActive(ship);
                build.PreviewEnabled = false;
                Building.Remove(ship.Structure, new Int3(-4, 2, -2), build.Inventory);
                Building.Toggle(ship.Structure, new Int3(4, 1, -3)); // and leave the starboard door open
                ship.Sync();
                ToggleKey(Key.O);
                player.DebugPlace(new Vector3(-23f, 10f, -15f), 0, 0, fly: true);
                player.DebugLookAt(Cell(-1, 1, -3));
                break;
            case "wreck":
                build.SetActive(ship);
                build.PreviewEnabled = false;
                build.BlastAt(ship, Cell(5, 1, 6), Vector3.Right);
                _main.GetNode<Effects>("Effects").Explosion(Cell(5, 1, 6), 2.6f);
                player.DebugPlace(new Vector3(36f, 8f, 30f), 0, 0, fly: true);
                player.DebugLookAt(Cell(4, 0, 4));
                _captureTick = 260;
                break;
        }
    }

    void Press(Key key) => _main.Build.HandleInput(new InputEventKey { PhysicalKeycode = key, Pressed = true });
    void ToggleKey(Key key) => Press(key);

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
