using System.Linq;
using System.Threading.Tasks;
using GalHack.Items;
using Godot;
using Manufacturing.Game;
using Manufacturing.Screens;

namespace Manufacturing.Tests;

/// <summary>
/// Renders one view of the workshop to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/manufacturing res://tests/Screenshot.tscn -- --view=constructor --out=shot.png
/// The machine views are what a player standing at the screen sees, with the cursor on
/// something: constructor (the cursor on a recipe; orders, sub-parts, a paused ammo restock),
/// deconstructor (on a stack, showing what it breaks into, while the wreck is salvaged), factory
/// (on BUILD, under the Gadfly's bill with what's missing), storage (on a stack, read out in the
/// footer). walkup is the constructor from a couple of steps back, the screen at eye height on the
/// machine's face. overview is the workshop, the Gadfly on its pad and a spill crate.
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
            case "walkup":
                // Iron ore runs out: the orders wait on it, and the restock pauses saying how much it needs.
                w.Storage.Remove("iron-ore", w.Storage.Count("iron-ore"));
                w.Storage.TryAdd(Bill.Of(("iron-bar", 8)));
                w.Fab.Order("power-unit", 2);
                w.Fab.Order("rocket", 2);
                for (int i = 0; i < 18; i++) w.Tick(0.5);
                ((ConstructorPage)_main.ConstructorScreen.Page).ShowRecipe("circuit-board");
                await Frames(3);
                await Point(_main.ConstructorScreen, ConstructorPage.RecipeKey("circuit-board"), _view == "walkup" ? 2.6f : 1.3f);
                break;
            case "deconstructor":
                w.Decon.BreakItems("servo", 1);
                w.StartSalvage();
                for (int i = 0; i < 9; i++) w.Tick(0.5);
                await Frames(3);
                await Point(_main.DeconstructorScreen, Nth(_main.DeconstructorScreen, "entry:", 3), 1.3f);
                break;
            case "factory":
                await Point(_main.FactoryScreen, FactoryPage.BuildKey, 1.7f); // the button is low on the glass: a step further back keeps it all in view
                break;
            case "storage":
                w.Gather();
                await Frames(3);
                await Point(_main.StorageScreen, Nth(_main.StorageScreen, "stack:", 7), 1.3f);
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

    /// <summary>
    /// Stands the player on the floor a distance straight out from one of a screen's hot spots,
    /// looking at it: the cursor lands there as it would in play.
    /// </summary>
    async Task Point(MachineScreen screen, string spot, float distance)
    {
        Rect2? rect = screen.Page.Spots.RectOf(spot);
        if (rect == null) GD.PrintErr($"no hot spot {spot} on {screen.Name}; aiming at the middle");
        Vector3 target = screen.PixelToWorld(rect?.GetCenter() ?? new Vector2(MachinePage.W, MachinePage.H) / 2);
        Vector3 feet = target + screen.GlobalBasis.Z * distance;
        feet.Y = screen.GlobalPosition.Y - Player.EyeHeight; // on the floor: the screen hangs at eye height
        _main.Player.StandAndLook(feet, target);
        await Frames(3);
    }

    /// <summary>The key of the nth hot spot (from 0) whose key starts with a prefix, as last painted.</summary>
    static string Nth(MachineScreen screen, string prefix, int n) =>
        screen.Page.Spots.Keys.Where(k => k.StartsWith(prefix)).Skip(n).FirstOrDefault() ?? prefix;

    void Look(Vector3 from, Vector3 at)
    {
        Camera3D cam = _main.Player.Camera;
        cam.GlobalPosition = from;
        cam.LookAt(at, Vector3.Up);
    }
}
