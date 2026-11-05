using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
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
        if (ok) _passes++;
        else { _failures++; GD.PrintErr($"  FAIL {what}"); }
        return ok;
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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

        // A screen takes keys: order from the constructor's menu.
        var fabPage = (ConstructorPage)_main.ConstructorScreen.Page;
        int orders = w.Fab.Orders.Count;
        string toast = fabPage.Handle(ScreenKey.Act);
        Check(w.Fab.Orders.Count == orders + 1, $"clicking the constructor's screen orders the selected item ({toast})");
        fabPage.Handle(ScreenKey.Tab);
        int threshold = w.Fab.Restock.Threshold(w.Fab.Restock.InUse[0], w.Items);
        fabPage.Handle(ScreenKey.More);
        Check(w.Fab.Restock.Threshold(w.Fab.Restock.InUse[0], w.Items) == threshold + 1, "] raises the selected ammo's threshold");
        fabPage.Handle(ScreenKey.Toggle);
        Check(!w.Fab.Restock.Enabled, "R switches restock off");
        fabPage.Handle(ScreenKey.Toggle);

        // The factory refuses in survival with what it lacks, and spawns at once in creative.
        var factoryPage = (FactoryPage)_main.FactoryScreen.Page;
        string refused = factoryPage.Handle(ScreenKey.Act);
        Check(refused.StartsWith("REFUSED") && w.Factory.Current == null, $"the factory refuses the Gadfly without its materials ({refused})");
        w.ToggleMode();
        int spawned = _main.Spawned.Count;
        factoryPage.Handle(ScreenKey.Act);
        await Frames(3);
        Check(_main.Spawned.Count == spawned + 1 && _main.Spawned.Last().Structure.Count == 249, "in creative the Gadfly appears on the pad at once");
        w.ToggleMode();

        // Salvage the wreck on the pad.
        int wreck = w.Wreck.Count;
        Check(w.StartSalvage(), "the deconstructor starts salvaging the wreck");
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

        // Every page draws without trouble in both modes, and the frame stays cheap.
        foreach (MachineScreen s in _main.Screens) s.Focused = true;
        await Frames(10);
        double best = double.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 30; i++)
            {
                w.Tick(1.0 / 60);
                foreach (MachineScreen s in _main.Screens) { s.Page.Refresh(); s.Page.QueueRedraw(); }
                await Frames(1);
            }
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / 30);
        }
        GD.Print($"  frame with every screen redrawing: {best:0.00} ms (best of three windows)");
        Check(best < 50, $"a frame with all four screens redrawing stays under 50 ms headless ({best:0.00})");
    }
}
