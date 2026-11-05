using System.Threading.Tasks;
using DiegeticUi.World;
using Godot;

namespace DiegeticUi.Tests;

/// <summary>
/// Renders one view of the bridge to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/diegetic-ui res://tests/Screenshot.tscn -- --view=console --out=shot.png
/// Views: console (a role mid-drag), holo (the star map in use), wrist (the inventory),
/// cold (the temperature vital flashing in the cold store), invasion (the countdown on the
/// visor and the console, sirens turning; invasion-late between banners), fire (a warning
/// shot's visor flash), constructor, trader, look (the look cursor on the constructor, from where
/// you'd walk past it), overview (the room).
/// </summary>
public partial class Screenshot : Node
{
    Main _main;
    Drive _drive;
    string _view = "console";
    string _out = "shot.png";
    bool _late;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        _main = new Main();
        AddChild(_main);
        _drive = new Drive(this, _main);
        Run();
    }

    async void Run()
    {
        await _drive.Frames(10);
        switch (_view)
        {
            case "console": await Console(); break;
            case "holo": await Holo(); break;
            case "wrist": await WristView(); break;
            case "cold": await Cold(); break;
            case "invasion": await Invasion(); break;
            case "invasion-late": _late = true; await Invasion(); break;
            case "fire": await Fire(); break;
            case "constructor": await Constructor(); break;
            case "trader": await Trader(); break;
            case "look": await Look(); break;
            default: await Overview(); break;
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }

    async Task Overview()
    {
        _drive.StandAndLook(new Vector3(1.2f, 0.05f, 4.6f), new Vector3(-0.6f, 1.5f, -4f));
        await _drive.Seconds(1.5);
    }

    async Task Console()
    {
        var page = _main.ConsolePage;
        await _drive.Use(_main.ConsoleScreen, new Vector3(0, 0.05f, -4.15f), _main.ConsoleScreen.GlobalPosition);
        // Hover the shield switch first so its cost shows on the bars, then pick up GUN and
        // carry it over KESTREL's card.
        _drive.MoveMouseTo(_drive.WindowPoint(_main.ConsoleScreen, Drive.Centre(page.ShieldSwitch)));
        await _drive.Frames(3);
        _drive.MoveMouseTo(_drive.WindowPoint(_main.ConsoleScreen, Drive.Centre(page.Chips[2])));
        await _drive.Frames(2);
        Drive.Mouse(true);
        await _drive.Frames(2);
        Vector2 card = Drive.Centre(page.Cards[2]) + new Vector2(120, 6);
        for (int i = 1; i <= 6; i++)
        {
            Vector2 from = Drive.Centre(page.Chips[2]);
            _drive.MoveMouseTo(_drive.WindowPoint(_main.ConsoleScreen, from.Lerp(card, i / 6f)), leftHeld: true);
            await _drive.Frames(2);
        }
        await _drive.Seconds(0.3);
    }

    async Task Holo()
    {
        var holo = _main.HoloTable;
        _main.State.Map.Clock = 20;
        await _drive.Use(holo, new Vector3(0, 0.05f, 0.9f), holo.GlobalPosition + new Vector3(0, 1.5f, 0));
        var tallow = _main.State.Map.Bodies[4];
        await _drive.Click(_main.Camera.UnprojectPosition(holo.BodyPosition(tallow)));
        var meridian = _main.State.Map.Bodies[2];
        _drive.MoveMouseTo(_main.Camera.UnprojectPosition(holo.BodyPosition(meridian)));
        await _drive.Seconds(0.4);
    }

    async Task WristView()
    {
        _drive.StandAndLook(new Vector3(-2.6f, 0.05f, 2.2f), new Vector3(-5f, 1.2f, -1.4f));
        await _drive.Frames(5);
        Drive.Tap(Key.Tab);
        await _drive.Seconds(0.8);
        var screen = _main.Wrist.Screen;
        _drive.MoveMouseTo(_drive.WindowPoint(screen, new Vector2(12 + 104 + 48, 70 + 42)));
        await _drive.Seconds(0.3);
    }

    async Task Cold()
    {
        _main.ShoveIntoColdStore();
        await _drive.Seconds(1.0);
        _drive.Aim(new Vector3(-3f, 1.7f, 3.0f)); // back out through the doorway at the warm bridge
        var temp = _main.State.Vitals.Temperature;
        for (int i = 0; i < 1200 && temp.State != Logic.VitalState.Warning; i++) await _drive.Frames();
        await _drive.Seconds(1.6);
        // Catch the flash while it is lit.
        for (int i = 0; i < 120 && !_main.Visor.FlashLit; i++) await _drive.Frames();
    }

    async Task Constructor()
    {
        var screen = _main.ConstructorScreen;
        await _drive.Use(screen, new Vector3(-3.6f, 0.05f, -1.4f), screen.GlobalPosition);
        await _drive.Click(_drive.WindowPoint(screen, Drive.Centre(_main.ConstructorPage.OrderButtons[0])));
        await _drive.Click(_drive.WindowPoint(screen, Drive.Centre(_main.ConstructorPage.OrderButtons[4])));
        await _drive.Seconds(2.2);
    }

    /// <summary>Walking past the constructor and just looking at it: the look cursor on an order button, lit.</summary>
    async Task Look()
    {
        var screen = _main.ConstructorScreen;
        Input.MouseMode = Input.MouseModeEnum.Visible; // stray mouse moves can't turn the view
        _drive.StandAndLook(new Vector3(-3.2f, 0.05f, -2.3f), screen.GlobalPosition);
        await _drive.Seconds(0.3);
        _main.Player.SetPhysicsProcess(false); // settled on the deck; stray keys can't walk it off the aim
        _drive.Aim(screen.PixelToWorld(Drive.Centre(_main.ConstructorPage.OrderButtons[1]) + new Vector2(40, 8)));
        await _drive.Seconds(0.4);
    }

    async Task Trader()
    {
        var screen = _main.TraderScreen;
        await _drive.Use(screen, new Vector3(3.6f, 0.05f, -1.4f), screen.GlobalPosition);
        _drive.MoveMouseTo(_drive.WindowPoint(screen, Drive.Centre(_main.TraderPage.SellHold)));
        await _drive.Seconds(0.4);
    }

    async Task Invasion()
    {
        _drive.StandAndLook(new Vector3(-0.75f, 0.05f, -3.55f), new Vector3(0.15f, 1.95f, -5.7f));
        await _drive.Frames(5);
        Drive.Tap(Key.I);
        await _drive.Seconds(_late ? 8.6 : 5.5);
    }

    async Task Fire()
    {
        _drive.StandAndLook(new Vector3(0.6f, 0.05f, 1.6f), new Vector3(-0.4f, 1.7f, -6f));
        await _drive.Frames(5);
        _main.WarningShot(25);
        await _drive.Seconds(0.12);
    }
}
