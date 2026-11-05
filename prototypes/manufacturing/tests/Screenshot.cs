using System.Threading.Tasks;
using GalHack.Items;
using Godot;
using Manufacturing.Game;
using Manufacturing.Screens;

namespace Manufacturing.Tests;

/// <summary>
/// Renders one view of the workshop to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/manufacturing res://tests/Screenshot.tscn -- --view=constructor --out=shot.png
/// Views: constructor (orders, sub-parts, a paused ammo restock), deconstructor (salvaging the
/// wreck), factory (the Gadfly's bill with what's missing), storage, overview (the workshop, the
/// Gadfly on its pad, a spill crate).
/// </summary>
public partial class Screenshot : Node
{
    Main _main;
    string _view = "overview", _out = "shot.png";

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        _main = new Main();
        AddChild(_main);
        Run();
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async void Run()
    {
        await Frames(10);
        _main.Player.SetPhysicsProcess(false);
        _main.Hud.ShowHelp = false;
        Workshop w = _main.Workshop;
        switch (_view)
        {
            case "constructor":
                // Iron ore runs out: the orders wait on it, and the restock pauses saying how much it needs.
                w.Storage.Remove("iron-ore", w.Storage.Count("iron-ore"));
                w.Storage.TryAdd(Bill.Of(("iron-bar", 8)));
                w.Fab.Order("power-unit", 2);
                w.Fab.Order("rocket", 2);
                for (int i = 0; i < 18; i++) w.Tick(0.5);
                Face(_main.ConstructorScreen);
                break;
            case "deconstructor":
                w.Decon.BreakItems("servo", 1);
                w.StartSalvage();
                for (int i = 0; i < 9; i++) w.Tick(0.5);
                Face(_main.DeconstructorScreen);
                break;
            case "factory":
                Face(_main.FactoryScreen);
                break;
            case "storage":
                w.Gather();
                Face(_main.StorageScreen);
                break;
            default:
                w.Rules.Mode = GameMode.Creative;
                w.Factory.Start("Gadfly", w.Blueprints[0].Blocks);
                w.Rules.Mode = GameMode.Survival;
                w.StartSalvage();
                for (int i = 0; i < 12; i++) w.Tick(0.5);
                w.Storage.TryAdd(Bill.Of(("stone", (int)w.Storage.FreeSu)));
                w.Gather(); // storage is full: the load spills into a crate on the floor
                Look(new Vector3(0, 10, 24), new Vector3(0, 0.5f, -1));
                break;
        }
        await Frames(30);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }

    /// <summary>Squares the camera up to a screen, close enough that it fills most of the view.</summary>
    void Face(MachineScreen screen)
    {
        Camera3D cam = _main.Player.Camera;
        cam.GlobalTransform = screen.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, 0.82f));
    }

    void Look(Vector3 from, Vector3 at)
    {
        Camera3D cam = _main.Player.Camera;
        cam.GlobalPosition = from;
        cam.LookAt(at, Vector3.Up);
    }
}
