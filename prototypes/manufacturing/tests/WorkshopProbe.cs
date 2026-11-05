using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DiegeticUi.Screens;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Structures;
using Godot;
using Manufacturing.Game;
using Manufacturing.Screens;

namespace Manufacturing.Tests;

/// <summary>
/// Headless probe of the real scene: it loads the workshop, lets the screens draw, and drives the
/// same entry points the input does (the multi-tool, the screens' keys, the factory, salvage).
///   godot --headless --path prototypes/manufacturing res://tests/WorkshopProbe.tscn
/// </summary>
public partial class WorkshopProbe : Node
{
    int _passes, _failures;
    Main _main;

    public override void _Ready()
    {
        _main = new Main();
        AddChild(_main);
        Run();
    }

    bool Check(bool ok, string what)
    {
        if (ok) { _passes++; GD.Print($"  ok   {what}"); }
        else { _failures++; GD.PrintErr($"  FAIL {what}"); }
        return ok;
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async Task PhysicsFrames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>The top of the base's floor (its floor blocks are the y = 0 layer), where the player stands.</summary>
    float Floor => 0.5f * _main.Workshop.Base.Grid.CellSize;

    /// <summary>
    /// Stands the player on the floor in front of a screen and looks at one of its hot spots, as
    /// a player would aim; returns the page pixel aimed at.
    /// </summary>
    async Task<Vector2> AimAt(MachineScreen screen, string spot, float distance = 2f)
    {
        Vector2 pixel = screen.Page.Spots.RectOf(spot)?.GetCenter() ?? throw new InvalidOperationException($"no hot spot {spot} on {screen.Name}");
        await LookAt(screen.PixelToWorld(pixel), screen, distance);
        return pixel;
    }

    async Task LookAt(Vector3 target, MachineScreen screen, float distance)
    {
        Vector3 feet = screen.GlobalPosition + screen.GlobalBasis.Z * distance;
        feet.Y = Floor;
        _main.Player.StandAndLook(feet, target);
        await PhysicsFrames(4);
        _main.Player.StandAndLook(_main.Player.GlobalPosition, target); // re-aim from where the feet settled
        await PhysicsFrames(3);
        await Frames(2);
    }

    /// <summary>A mouse click through the real input path: the game, the pointer, the screen, the page.</summary>
    async Task Click(MouseButton button = MouseButton.Left, bool shift = false)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true, ShiftPressed = shift });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false, ShiftPressed = shift });
        await Frames(2);
    }

    async void Run()
    {
        try { await Probe(); }
        catch (Exception e) { _failures++; GD.PrintErr($"  FAIL probe threw: {e}"); }
        GD.Print($"\n{_passes} checks passed, {_failures} failed");
        GD.Print(_failures == 0 ? "ALL PASSED" : "SOME CHECKS FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async Task Probe()
    {
        await Frames(20);
        Workshop w = _main.Workshop;
        Check(_main.Screens.Count == 4, $"four machine screens are mounted ({_main.Screens.Count})");
        Check(_main.Screens.All(s => s.Redraws > 0), "every screen has drawn its page");
        ScreensAtEyeHeight();
        Check(w.Fab.Restock.InUse.SequenceEqual(new[] { "belt-12mm", "rocket", "shells-40mm" }), $"the constructor restocks the ammo of the base's guns ({string.Join(", ", w.Fab.Restock.InUse)})");

        // Restock runs from the start: three boxes of shells under a threshold of ten.
        for (int i = 0; i < 4000 && w.Fab.Restock.State == GalHack.Items.Production.RestockState.Restocking; i++) w.Tick(0.5);
        Check(w.Storage.Count("shells-40mm") == 10, $"the turrets' shells were restocked to ten ({w.Storage.Count("shells-40mm")})");
        Check(w.Fab.Restock.State is GalHack.Items.Production.RestockState.Stocked or GalHack.Items.Production.RestockState.Paused,
            $"restock has settled: {w.Fab.Restock.Status}");

        // The multi-tool pays from storage, and refunds.
        int plates = w.Storage.Count("hull-plate");
        _main.SelectSlot(0);
        _main.AimAt(new Int3(4, 0, 2), new Int3(5, 0, 2));
        PlaceResult? placed = _main.PlaceAimed();
        Check(placed?.Ok == true && w.Base.Has(new Int3(5, 0, 2)), "a steel block placed with the multi-tool joins the base");
        Check(w.Storage.Count("hull-plate") == plates - 8, "and its 8 hull plates came out of storage");
        _main.AimAt(new Int3(5, 0, 2), new Int3(6, 0, 2));
        TakeDownResult? removed = _main.RemoveAimed();
        Check(removed?.Ok == true && w.Storage.Count("hull-plate") == plates, "taking it down puts the plates back");

        await CursorOnTheConstructor();

        // Keys still work as extras: order the selected item from the constructor's menu.
        var fabPage = (ConstructorPage)_main.ConstructorScreen.Page;
        int orders = w.Fab.Orders.Count;
        string toast = fabPage.Handle(ScreenKey.Act);
        Check(w.Fab.Orders.Count == orders + 1, $"Enter on the constructor's screen orders the selected item ({toast})");
        fabPage.Handle(ScreenKey.Tab);
        int threshold = w.Fab.Restock.Threshold(w.Fab.Restock.InUse[0], w.Items);
        fabPage.Handle(ScreenKey.More);
        Check(w.Fab.Restock.Threshold(w.Fab.Restock.InUse[0], w.Items) == threshold + 1, "] raises the selected ammo's threshold");
        fabPage.Handle(ScreenKey.Toggle);
        Check(!w.Fab.Restock.Enabled, "R switches restock off");
        fabPage.Handle(ScreenKey.Toggle);

        // The factory's BUILD button: refused in survival with what it lacks, and spawns at once in creative.
        var factoryPage = (FactoryPage)_main.FactoryScreen.Page;
        string refused = null;
        factoryPage.Said += Heard;
        void Heard(string line) => refused = line;
        await AimAt(_main.FactoryScreen, FactoryPage.BuildKey);
        bool lit = factoryPage.Spots.Hovered == FactoryPage.BuildKey;
        await Click();
        factoryPage.Said -= Heard;
        Check(lit && refused != null && refused.StartsWith("REFUSED") && w.Factory.Current == null,
            $"aimed at the factory's lit BUILD button, a click is refused without the materials ({refused})");
        w.ToggleMode();
        int spawned = _main.Spawned.Count;
        factoryPage.Handle(ScreenKey.Act);
        await Frames(3);
        Check(_main.Spawned.Count == spawned + 1 && _main.Spawned.Last().Structure.Count == 249, "in creative the Gadfly appears on the pad at once");
        w.ToggleMode();

        // Salvage the wreck on the pad: click it on the deconstructor's list.
        int wreck = w.Wreck.Count;
        await AimAt(_main.DeconstructorScreen, "entry:wreck");
        lit = _main.DeconstructorScreen.Page.Spots.Hovered == "entry:wreck";
        await Click();
        Check(lit && w.SalvageLeft > 0, $"clicking the wreck on the deconstructor's lit list starts salvaging it ({w.SalvageLeft} blocks to go)");

        // Storage: point at a stack and its tile lights (the footer reads it out in full).
        MachineScreen storage = _main.StorageScreen;
        string stack = "stack:" + w.Storage.Stacks.First().Item.Id;
        Vector2 tile = await AimAt(storage, stack);
        Check(_main.Pointer.Target == storage && storage.CursorPixel.DistanceTo(tile) < 1 && storage.Page.Spots.Hovered == stack,
            $"on the storage screen the cursor sits on the aimed stack and lights it ({storage.Page.Spots.Hovered})");
        await LookAt(storage.GlobalPosition + new Vector3(8, 0, 0), storage, 2); // along the wall: off every screen

        for (int i = 0; i < 400 && w.Wreck.Count > 0; i++) w.Tick(0.5);
        for (int i = 0; i < 100; i++) w.Tick(0.5);
        Check(w.Wreck.Count == 0 && w.Decon.Returned.Total > 0, $"the wreck's {wreck} blocks became {w.Decon.Returned.Total} parts at {w.Decon.ReturnRate:P0}");

        // Spill: fill storage, take a block down, a crate appears and can be emptied back. (Storage is filled with whole stones,
        // so up to one plate may still squeeze in: the rest spills.)
        _main.AimAt(new Int3(4, 0, 2), new Int3(5, 0, 2));
        Check(_main.PlaceAimed()?.Ok == true, "another steel block goes on");
        w.Storage.TryAdd(Bill.Of(("stone", (int)w.Storage.FreeSu)));
        _main.AimAt(new Int3(5, 0, 2), new Int3(6, 0, 2));
        removed = _main.RemoveAimed();
        await Frames(3);
        Check(w.Crates.Crates.Count == 1 && removed?.Spilled.Total is > 0 and <= 8, $"with storage full, a removed block's parts spill into a crate ({removed?.Spilled})");
        w.Storage.Remove("stone", 100);
        if (w.Crates.Crates.Count > 0) w.Crates.Collect(w.Crates.Crates[0], w.Storage);
        await Frames(3);
        Check(w.Crates.Crates.Count == 0, "emptying the crate removes it");

        // Every page redraws every frame, with all four in view: the frame stays cheap.
        _main.Player.StandAndLook(new Vector3(0, Floor, 3), new Vector3(0, Floor + Player.EyeHeight, -5));
        await PhysicsFrames(3);
        await Frames(3);
        ScreenManager manager = _main.ScreenManager;
        (int budget, double hz) = (manager.Budget, manager.BackgroundHz);
        (manager.Budget, manager.BackgroundHz) = (8, 1e6);
        double best = double.MaxValue;
        long redraws = manager.TotalRedraws;
        for (int window = 0; window < 3; window++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 30; i++)
            {
                w.Tick(1.0 / 60);
                foreach (MachineScreen s in _main.Screens) { s.Page.QueueRedraw(); s.MarkDirty(); }
                await Frames(1);
            }
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / 30);
        }
        redraws = manager.TotalRedraws - redraws;
        (manager.Budget, manager.BackgroundHz) = (budget, hz);
        GD.Print($"  frame with every screen redrawing: {best:0.00} ms (best of three windows, {redraws} redraws in 90 frames)");
        Check(redraws >= 4 * 89 && best < 50, $"a frame with all four screens redrawing stays under 50 ms headless ({best:0.00} ms, {redraws} redraws)");
    }

    /// <summary>The machines' screens hang with their centres near the player's eyes and face straight out at them.</summary>
    void ScreensAtEyeHeight()
    {
        const float band = 0.1f, maxTilt = 15;
        float eye = _main.Player.Camera.GlobalPosition.Y; // the player is standing on the floor
        bool ok = Math.Abs(eye - (Floor + Player.EyeHeight)) < 0.05f;
        var report = new System.Collections.Generic.List<string>();
        foreach (MachineScreen s in _main.Screens)
        {
            Vector3 toEye = s.GlobalBasis.Z * 1.5f + new Vector3(0, eye - s.GlobalPosition.Y, 0);
            float off = Mathf.RadToDeg(s.GlobalBasis.Z.AngleTo(toEye));
            ok &= Math.Abs(s.GlobalPosition.Y - eye) <= band && off <= maxTilt;
            report.Add($"{s.Name} {s.GlobalPosition.Y - Floor:F2} m, {off:F0}°");
        }
        Check(ok, $"every screen's centre is within {band * 100:0} cm of the eyes ({eye - Floor:F2} m above the floor) and faces them within {maxTilt}° ({string.Join("; ", report)})");
    }

    /// <summary>
    /// The look-and-click cursor on the constructor: aim at a recipe and the cursor sits on that
    /// pixel with the row lit; a click orders it, Shift-click orders five, the X cancels, the lamp
    /// toggles restock; look away or step back out of reach and the cursor is gone.
    /// </summary>
    async Task CursorOnTheConstructor()
    {
        Workshop w = _main.Workshop;
        MachineScreen screen = _main.ConstructorScreen;
        var page = (ConstructorPage)screen.Page;
        string servo = ConstructorPage.RecipeKey("servo");
        Vector2 pixel = await AimAt(screen, servo);
        float error = screen.CursorPixel.DistanceTo(pixel);
        Check(_main.Pointer.Target == screen && screen.InUse && error < 1 && page.Spots.Hovered == servo && !_main.Hud.Crosshair
              && _main.Screens.Count(s => s.InUse) == 1,
            $"aiming at the servo recipe from 2 m puts the cursor on that pixel ({error:F3} px off) and lights the row; the crosshair steps aside");
        Check(page.Focused, "the hint line is on the glass while the cursor is");

        int orders = w.Fab.Orders.Count;
        await Click();
        Check(w.Fab.Orders.Count == orders + 1 && w.Fab.Orders[^1].Item.Id == "servo" && w.Fab.Orders[^1].Count == 1,
            $"clicking the servo recipe orders one servo ({w.Fab.Orders.Count - orders} new order)");
        await Click(shift: true);
        Check(w.Fab.Orders.Count == orders + 2 && w.Fab.Orders[^1].Count == 5, "Shift-click orders five");

        await Frames(2); // the page repaints with the new orders and their cancel keys
        await AimAt(screen, "cancel:0");
        await Click();
        Check(w.Fab.Orders.Count == orders + 1, "clicking an order's X cancels it");

        bool restock = w.Fab.Restock.Enabled;
        await AimAt(screen, "restock");
        await Click();
        bool flipped = w.Fab.Restock.Enabled != restock;
        await Click();
        Check(flipped && w.Fab.Restock.Enabled == restock, "clicking the restock lamp switches auto-restock off and on");

        string shells = "more:shells-40mm";
        int threshold = w.Fab.Restock.Threshold("shells-40mm", w.Items);
        await AimAt(screen, shells);
        await Click();
        Check(w.Fab.Restock.Threshold("shells-40mm", w.Items) == threshold + 1, "the + key on the shells raises their restock threshold");
        await AimAt(screen, "less:shells-40mm");
        await Click();

        await LookAt(screen.GlobalPosition + new Vector3(8, 0, 0), screen, 2); // along the wall
        Check(_main.Pointer.Target == null && !screen.InUse && page.Spots.Hovered == null && !page.Focused && _main.Hud.Crosshair,
            "looking away takes the cursor off the glass, unlights the row and brings the crosshair back");

        await LookAt(screen.GlobalPosition, screen, 4.5f);
        Check(_main.Pointer.Target == null && !screen.InUse, "4.5 m back, looking straight at it, it is out of reach: no cursor");
    }
}
