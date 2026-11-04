using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DiegeticUi.Logic;
using DiegeticUi.Screens;
using DiegeticUi.World;
using Godot;

namespace DiegeticUi.Tests;

/// <summary>
/// Tests for the diegetic UI. Run headless:
///   godot --headless --path prototypes/diegetic-ui res://tests/UiTest.tscn
/// Exits 0 if every check passes, 1 if any fails. Run it windowed as well to get real
/// SubViewport render timings (headless uses a dummy renderer that draws nothing).
///
/// The first half tests the pure view-models; the second builds the bridge and drives it
/// through the real input path.
/// </summary>
public partial class UiTest : Node
{
    int _failures;
    Main _main;
    Drive _drive;
    bool Headless => DisplayServer.GetName() == "headless";

    public override void _Ready() => Run();

    async void Run()
    {
        try
        {
            GD.Print("-- view-models");
            VitalFlashesBeforeHarm();
            WarningsAtTenFiveOne();
            InventoryTotalsAndCap();
            ConstructorAutoQueues();
            RolesOnePilotSharedGunnery();
            SellAllWanted();
            FocusMachineStates();

            GD.Print("-- bridge scene");
            _main = new Main();
            AddChild(_main);
            _drive = new Drive(this, _main);
            await _drive.Frames(5);
            await RayToPixelAccuracy();
            await FocusInTheScene();
            await WristFocus();
            await ColdStoreFlash();
            await ScreenRedrawCost();
        }
        catch (Exception e)
        {
            Fail($"exception: {e}");
        }
        Engine.TimeScale = 1;
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- View-models ------------------------------------------------------------------------

    void VitalFlashesBeforeHarm()
    {
        var vitals = new Vitals();
        var cold = new Surroundings { AmbientC = -60, Breathable = true };
        double t = 0, warnAt = -1, harmAt = -1, healthAtWarn = -1;
        const double dt = 0.05;
        while (t < 120 && harmAt < 0)
        {
            vitals.Tick(dt, cold, Hazard.None);
            t += dt;
            if (warnAt < 0 && vitals.Temperature.State == VitalState.Warning)
            {
                warnAt = t;
                healthAtWarn = vitals.Health.Value;
            }
            if (vitals.CostingHealth) harmAt = t;
        }
        Check(warnAt > 0 && harmAt > 0 && warnAt < harmAt && harmAt - warnAt >= 3 && healthAtWarn >= 100,
            $"cold: TEMP flashes at {warnAt:F1} s, health is untouched then ({healthAtWarn:F0}), damage starts at {harmAt:F1} s ({harmAt - warnAt:F1} s of warning)");

        // Thermal gear covers it: no flash at all.
        var covered = new Vitals();
        for (int i = 0; i < 1200; i++) covered.Tick(dt, cold, Hazard.Cold);
        Check(covered.Temperature.State == VitalState.Ok && !covered.CostingHealth, "with cold protection the same 60 s never warns");

        // Every vital keeps a gap between its warning line and its harm line.
        bool gaps = vitals.All.All(v => v.WarnLow > v.HarmLow || double.IsNegativeInfinity(v.WarnLow))
                    && vitals.All.All(v => v.WarnHigh < v.HarmHigh || double.IsPositiveInfinity(v.WarnHigh));
        Check(gaps, "every vital warns before its harm line");
    }

    void WarningsAtTenFiveOne()
    {
        var timeline = new WarningTimeline();
        timeline.Schedule("invasion", "INVASION", 630);
        var fired = new List<Announcement>();
        for (int s = 0; s < 700; s++) fired.AddRange(timeline.Advance(1));
        string got = string.Join(", ", fired.Select(a => $"{a.MinutesLeft}@{a.At:0}s"));
        Check(got == "10@30s, 5@330s, 1@570s, 0@630s", $"warnings fire once each at 10, 5 and 1 minutes, then on arrival ({got})");

        var jumpy = new WarningTimeline();
        jumpy.Schedule("storm", "STORM", 630);
        string first = string.Join(",", jumpy.AdvanceTo(400).Select(a => a.MinutesLeft));
        string again = string.Join(",", jumpy.AdvanceTo(400).Select(a => a.MinutesLeft));
        string rest = string.Join(",", jumpy.AdvanceTo(700).Select(a => a.MinutesLeft));
        Check(first == "10,5" && again == "" && rest == "1,0", $"one big step announces every mark it crosses, in order, once ({first} | {again} | {rest})");

        var late = new WarningTimeline();
        late.Schedule("raid", "RAID", 200);
        string lateMarks = string.Join(",", late.AdvanceTo(300).Select(a => a.MinutesLeft));
        Check(lateMarks == "1,0", $"a threat scheduled 3:20 out skips the stale 10 and 5 minute marks ({lateMarks})");
    }

    void InventoryTotalsAndCap()
    {
        var suit = new Inventory("SUIT", 50);
        suit.Add(Catalog.IronOre, 8);       // 16 kg, 8 L
        suit.Add(Catalog.BioGel, 3);        // 1.5 kg, 1.5 L
        suit.Add(Catalog.O2Canister, 2);    // 4 kg, 3 L
        Check(Near(suit.TotalMassKg, 21.5) && Near(suit.TotalVolumeL, 12.5),
            $"mass and volume totals are right ({suit.TotalMassKg} kg, {suit.TotalVolumeL} L)");

        bool all = suit.TryAdd(Catalog.RoverWheel, 7);       // 42 L into 37.5 L free: refused whole
        int some = suit.Add(Catalog.RoverWheel, 7);          // as many as fit: 6
        bool kit = suit.TryAdd(Catalog.MedKit, 2);           // 2 L into 1.5 L: refused
        int oneKit = suit.Add(Catalog.MedKit, 2);            // 1 fits
        Check(!all && some == 6 && !kit && oneKit == 1 && suit.TotalVolumeL <= suit.VolumeCapL && Near(suit.TotalVolumeL, 49.5),
            $"the volume cap is enforced: all-or-nothing refused, partial add stops at the cap ({suit.TotalVolumeL} / {suit.VolumeCapL} L)");
    }

    void ConstructorAutoQueues()
    {
        var store = new Inventory("STORE", 1000);
        store.Add(Catalog.IronPlate, 3);
        store.Add(Catalog.CopperOre, 10);
        store.Add(Catalog.Polymer, 2);
        store.Add(Catalog.IronOre, 10);
        var fab = new ConstructorQueue(store);
        fab.Order(Catalog.RoverWheel, 2);

        string subParts = string.Join(", ", fab.SubParts.Select(j => $"{j.Item.Id} x{j.Count}"));
        Check(subParts == "iron_plate x3, copper_wire x6, motor x2" && fab.Orders.Count == 1 && fab.Missing.Count == 0
              && fab.SubParts.All(j => j.Auto && j.Feeds == fab.Orders[0]),
            $"ordering 2 rover wheels auto-queues the missing sub-parts, in build order ({subParts})");

        for (int i = 0; i < 4000 && fab.Busy; i++) fab.Tick(0.1);
        Check(!fab.Busy && store.Count("rover_wheel") == 2 && store.Count("iron_plate") == 0 && store.Count("motor") == 0
              && store.Count("copper_wire") == 0 && store.Count("iron_ore") == 4 && store.Count("copper_ore") == 7 && store.Count("polymer") == 0,
            $"building them uses exactly the planned parts (wheels {store.Count("rover_wheel")}, ore left {store.Count("iron_ore")} iron, {store.Count("copper_ore")} copper)");

        var empty = new ConstructorQueue(new Inventory("EMPTY", 100));
        empty.Order(Catalog.MedKit);
        Check(empty.Missing.GetValueOrDefault("bio_gel") == 1 && empty.Missing.GetValueOrDefault("polymer") == 1 && empty.SubParts.Count == 0,
            "raw materials nobody can make are reported missing, not queued");
    }

    void RolesOnePilotSharedGunnery()
    {
        var roster = new StationRoster(new[] { "ORLA", "BRIX", "KESTREL" });
        roster.Assign(Station.Pilot, "BRIX");
        bool dropped = roster.Unassign(Station.Pilot, "BRIX");
        Check(roster.Holders(Station.Pilot).SequenceEqual(new[] { "BRIX" }) && !dropped && roster.LastRefusal != null,
            "the pilot role moves on assignment and can't be dropped: exactly one pilot");

        roster.Assign(Station.Gunnery, "ORLA");
        roster.Assign(Station.Gunnery, "KESTREL");
        bool split = roster.TurretsFor("ORLA") == 2 && roster.TurretsFor("KESTREL") == 2;
        roster.Assign(Station.Gunnery, "BRIX");
        Check(split && roster.Holders(Station.Gunnery).Count == 3 && roster.TurretsFor("ORLA") == 2 && roster.TurretsFor("KESTREL") == 1
              && roster.TurretsFor("BRIX") == 1 && roster.Holders(Station.Pilot).Count == 1,
            "gunnery is shared: 4 turrets split 2/2, then 2/1/1, while the pilot stays single");

        roster.Assign(Station.Navigation, "ORLA");
        roster.Assign(Station.Navigation, "KESTREL");
        Check(roster.Holders(Station.Navigation).SequenceEqual(new[] { "KESTREL" }) && !roster.Assign(Station.Pilot, "NOBODY"),
            "single roles move to the new holder; strangers can't take a station");
    }

    void SellAllWanted()
    {
        var state = new GameState();
        var bag = new Inventory("BAG", 100);
        bag.Add(Catalog.IronOre, 5);      // wanted at 17
        bag.Add(Catalog.BioGel, 2);       // wanted at 56
        bag.Add(Catalog.CopperOre, 3);    // bought, but not wanted
        bag.Add(Catalog.MedKit, 1);       // not bought at all
        var wallet = new Wallet(100);
        int preview = state.Trader.WantedValue(bag);
        int earned = state.Trader.SellAllWanted(bag, wallet);
        Check(preview == 5 * 17 + 2 * 56 && earned == preview && wallet.Credits == 100 + preview
              && bag.Count("iron_ore") == 0 && bag.Count("bio_gel") == 0 && bag.Count("copper_ore") == 3 && bag.Count("medkit") == 1,
            $"sell-all removes exactly the wanted items and credits {earned} (shown as +{preview} before the press)");
    }

    void FocusMachineStates()
    {
        var m = new FocusMachine<string> { EaseSeconds = 0.3 };
        bool entered = m.Enter("console");
        bool cursorWhileEntering = m.CursorVisible;
        m.Tick(0.15);
        bool midway = m.State == FocusState.Entering;
        m.Tick(0.2);
        bool using_ = m.State == FocusState.Using && m.CursorVisible && m.Target == "console";
        bool second = m.Enter("trader");
        bool back = m.Back();
        bool cursorLeaving = m.CursorVisible;
        for (int i = 0; i < 10; i++) m.Tick(0.05);
        Check(entered && !cursorWhileEntering && midway && using_ && !second && back && !cursorLeaving && m.State == FocusState.Free && m.Target == null,
            "focus machine: Free -> Entering -> Using (cursor only here) -> Leaving -> Free; no second screen while busy");

        m.Enter("console");
        m.Tick(0.1);
        m.Back();
        double blend = m.Blend;
        m.Tick(0.05);
        Check(m.State == FocusState.Leaving && m.Blend < blend && !m.CursorVisible, "Esc while easing in eases straight back out");
    }

    // ---- The bridge ---------------------------------------------------------------------------

    /// <summary>
    /// Casts rays at known pixels of the console screen from several angles, through the pick
    /// collider and the plane maths, and checks the pixel that comes back.
    /// </summary>
    async Task RayToPixelAccuracy()
    {
        WorldScreen screen = _main.ConsoleScreen;
        var probe = new Camera3D { Fov = 75 };
        AddChild(probe);
        Vector2[] pixels = { new(37.5f, 410.25f), new(640, 360), new(1250.7f, 15.2f), new(5, 700), new(901.3f, 222.9f) };
        (float yaw, float pitch)[] angles = { (0, 0), (35, 10), (-55, -20), (70, 25), (-80, 5) };
        float worst = 0;
        int hits = 0, casts = 0;
        await _drive.PhysicsFrames(2);
        var space = _main.GetWorld3D().DirectSpaceState;
        foreach (var (yaw, pitch) in angles)
        {
            Vector3 dir = screen.GlobalBasis * (Basis.FromEuler(new Vector3(Mathf.DegToRad(pitch), Mathf.DegToRad(yaw), 0)) * Vector3.Back);
            foreach (Vector2 pixel in pixels)
            {
                Vector3 target = screen.PixelToWorld(pixel);
                probe.GlobalTransform = new Transform3D(Basis.Identity, target + dir * 1.6f).LookingAt(screen.GlobalPosition, screen.GlobalBasis.Y);
                Vector2 window = probe.UnprojectPosition(target);
                Vector3 origin = probe.ProjectRayOrigin(window), normal = probe.ProjectRayNormal(window);
                casts++;
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + normal * 4, WorldScreen.PickLayer));
                if (hit.Count > 0 && hit["collider"].AsGodotObject() is Node c && c.GetMeta("focus_target").AsGodotObject() == screen) hits++;
                if (screen.RayToPixel(origin, normal, out Vector2 got)) worst = Mathf.Max(worst, got.DistanceTo(pixel));
                else worst = float.MaxValue;
            }
        }
        probe.QueueFree();
        Check(worst < 1f && hits == casts,
            $"ray to screen pixel is within a pixel at 5 angles up to 80° off-axis (worst {worst:F3} px over {casts} rays, {hits} picked the screen)");
    }

    async Task FocusInTheScene()
    {
        WorldScreen console = _main.ConsoleScreen;
        Interactor interactor = _main.Interactor;
        Vector3 standAt = new(0, 0.05f, -4.15f);
        _drive.StandAndLook(standAt, console.GlobalPosition);
        await _drive.PhysicsFrames(3);
        await _drive.Frames(2);
        Check(interactor.LookTarget == console && interactor.Focus.State == FocusState.Free && !interactor.CursorVisible && !console.InUse,
            "looking at the console offers it (E prompt), with no cursor anywhere");

        Drive.Tap(Key.E);
        await _drive.Frames(2);
        bool entering = interactor.Focus.State == FocusState.Entering && !interactor.CursorVisible && !console.InUse;
        for (int i = 0; i < 120 && interactor.Focus.State != FocusState.Using; i++) await _drive.Frames();
        Transform3D framed = console.FocusView(_main.Camera);
        bool cameraFramed = _main.Camera.GlobalPosition.DistanceTo(framed.Origin) < 0.01f;
        Check(entering && interactor.Focus.State == FocusState.Using && interactor.CursorVisible && console.InUse && cameraFramed
              && WorldScreen.All.Count(s => s.InUse) == 1,
            "E eases the view in (no cursor yet), then the cursor appears on that screen only, framed square on");

        Vector3 before = _main.Player.GlobalPosition;
        Drive.Key(Key.W, true);
        await _drive.PhysicsFrames(20);
        Drive.Key(Key.W, false);
        Check(_main.Player.GlobalPosition.DistanceTo(before) < 0.05f, "walking is locked while a framed screen is in use");

        // Click the shield switch through ray -> pixel -> pushed event.
        var page = _main.ConsolePage;
        await _drive.Click(_drive.WindowPoint(console, Drive.Centre(page.ShieldSwitch)));
        Check(_main.State.Ship.Shields && page.ShieldSwitch.On, "clicking the on-screen shield switch raises the shields");

        // Drag GUN from the palette onto KESTREL: gunnery is now shared.
        _drive.MoveMouseTo(_drive.WindowPoint(console, Drive.Centre(page.Chips[(int)Station.Gunnery])));
        await _drive.Frames(2);
        Drive.Mouse(true);
        await _drive.Frames(2);
        bool dragging = page.Dragging == Station.Gunnery;
        Vector2 kestrel = Drive.Centre(page.Cards[2]);
        for (int i = 1; i <= 5; i++)
        {
            _drive.MoveMouseTo(_drive.WindowPoint(console, Drive.Centre(page.Chips[2]).Lerp(kestrel, i / 5f)), leftHeld: true);
            await _drive.Frames(2);
        }
        bool overCard = page.DropTarget == page.Cards[2];
        Drive.Mouse(false);
        await _drive.Frames(2);
        var roster = _main.State.Roster;
        Check(dragging && overCard && roster.Holders(Station.Gunnery).Contains("KESTREL") && roster.Holders(Station.Gunnery).Contains("BRIX")
              && roster.TurretsFor("KESTREL") == 2,
            "dragging GUN onto KESTREL's card on the console shares gunnery (2 turrets each)");

        // Drag PILOT off ORLA's card onto nothing: refused, and the console says why.
        var (_, pilotTag) = page.Cards[0].Tags().First(t => t.Role == Station.Pilot);
        Vector2 tag = page.Cards[0].GetGlobalRect().Position + pilotTag.GetCenter();
        _drive.MoveMouseTo(_drive.WindowPoint(console, tag));
        await _drive.Frames(2);
        Drive.Mouse(true);
        await _drive.Frames(2);
        _drive.MoveMouseTo(_drive.WindowPoint(console, new Vector2(300, 640)), leftHeld: true);
        await _drive.Frames(2);
        Drive.Mouse(false);
        await _drive.Frames(2);
        Check(roster.Holders(Station.Pilot).SequenceEqual(new[] { "ORLA" }) && roster.LastRefusal != null,
            "dragging the pilot role off into nothing is refused: still exactly one pilot");

        Drive.Tap(Key.Escape);
        await _drive.Frames(2);
        bool leaving = interactor.Focus.State == FocusState.Leaving && !interactor.CursorVisible && !console.InUse;
        for (int i = 0; i < 120 && interactor.Focus.State != FocusState.Free; i++) await _drive.Frames();
        bool backAtHead = _main.Camera.GlobalPosition.DistanceTo(_main.Player.HeadView().Origin) < 0.01f;
        Check(leaving && interactor.Focus.State == FocusState.Free && backAtHead && WorldScreen.All.All(s => !s.InUse),
            "Esc steps back: the cursor goes at once, the view eases back to the head");
    }

    async Task WristFocus()
    {
        WorldScreen wrist = _main.Wrist.Screen;
        Drive.Tap(Key.Tab);
        await _drive.Frames(2);
        bool up = _main.Interactor.Focus.Target == wrist && !_main.Interactor.CursorVisible;
        for (int i = 0; i < 120 && _main.Interactor.Focus.State != FocusState.Using; i++) await _drive.Frames();
        bool inUse = wrist.InUse && _main.Interactor.CursorVisible && _main.Wrist.Visible && _main.Wrist.Raise > 0.99f;

        Vector3 before = _main.Player.GlobalPosition;
        Drive.Key(Key.W, true);
        await _drive.PhysicsFrames(20);
        Drive.Key(Key.W, false);
        bool walked = _main.Player.GlobalPosition.DistanceTo(before) > 0.3f;

        Drive.Tap(Key.Tab);
        for (int i = 0; i < 120 && _main.Interactor.Focus.State != FocusState.Free; i++) await _drive.Frames();
        await _drive.Frames(2);
        Check(up && inUse && walked && !wrist.InUse && !_main.Wrist.Visible,
            "Tab raises the wrist (cursor only once it's up), you can keep walking, Tab lowers and hides it");
    }

    async Task ColdStoreFlash()
    {
        // Real game time, sped up so the test doesn't wait 15 s.
        Engine.TimeScale = 6;
        _main.ShoveIntoColdStore();
        Vitals v = _main.State.Vitals;
        double flashHealth = -1;
        bool hurtBeforeFlash = false;
        for (int i = 0; i < 2000 && !v.CostingHealth; i++)
        {
            await _drive.Frames();
            if (v.Health.Value < 100 && flashHealth < 0) hurtBeforeFlash = true;
            if (flashHealth < 0 && _main.Visor.Flashing(VitalKind.Temperature)) flashHealth = v.Health.Value;
        }
        await _drive.Seconds(0.5);
        Engine.TimeScale = 1;
        GD.Print($"      (cold store: player at {_main.Player.GlobalPosition}, inside {_main.InColdStore}, core {v.Temperature.Value:F1} °C)");
        Check(_main.InColdStore && flashHealth >= 100 && !hurtBeforeFlash && v.CostingHealth && v.Health.Value < 100,
            $"in the cold store the visor flashes TEMP at full health, and only later starts costing health (now {v.Health.Value:F1} HP)");
        _main.Player.Place(Bridge.Spawn, 0);
    }

    /// <summary>
    /// With every screen in view: how much a full redraw costs per frame, and that idle
    /// screens never redraw.
    /// </summary>
    async Task ScreenRedrawCost()
    {
        _drive.StandAndLook(new Vector3(0, 0.05f, 4.3f), new Vector3(0, 1.5f, -3.5f));
        await _drive.Frames(3);
        Drive.Tap(Key.Tab); // the wrist too
        for (int i = 0; i < 120 && _main.Interactor.Focus.State != FocusState.Using; i++) await _drive.Frames();
        await _drive.Seconds(0.5); // let the wrist's first redraws and any widget animation settle

        List<WorldScreen> screens = WorldScreen.All.ToList();
        Camera3D camera = _main.Camera;
        var seen = screens.Where(s => s.IsSeenBy(camera)).Select(s => s.Name.ToString()).ToList();
        Check(seen.Count == screens.Count, $"all {screens.Count} screens are in view ({string.Join(", ", seen)})");

        foreach (WorldScreen s in screens) RenderingServer.ViewportSetMeasureRenderTime(s.Viewport.GetViewportRid(), true);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

        // 1. Everything changes at once (an alert hits every console): spread over a few frames.
        foreach (WorldScreen s in screens) s.MarkDirty();
        var cpu = new List<double>();
        var perFrame = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            await _drive.Frames();
            cpu.Add(_main.Screens.CpuMsLastFrame);
            perFrame.Add(_main.Screens.RedrawsLastFrame);
        }
        Check(perFrame.Sum() == screens.Count && perFrame.Max() <= _main.Screens.Budget + 1,
            $"a full refresh of {screens.Count} screens is spread over frames: {string.Join(" ", perFrame)} redraws, manager CPU {cpu.Max():F3} ms/frame at most");
        await _drive.Frames(3);
        if (!Headless) await MeasureRenderCost(screens);
        else GD.Print("      (headless: the dummy renderer draws nothing, so per-viewport GPU/CPU render times are only printed in a windowed run)");

        // 2. Idle: nothing changes for 2 seconds. Nobody may redraw.
        var before = screens.ToDictionary(s => s, s => s.Redraws);
        var idleCpu = new List<double>();
        var watch = Stopwatch.StartNew();
        int frames = 0;
        while (watch.Elapsed.TotalSeconds < 2)
        {
            await _drive.Frames();
            idleCpu.Add(_main.Screens.CpuMsLastFrame);
            frames++;
        }
        var redrawn = screens.Where(s => s.Redraws != before[s]).Select(s => $"{s.Name} x{s.Redraws - before[s]}").ToList();
        Check(redrawn.Count == 0,
            $"idle: 0 redraws over {frames} frames with all screens in view (manager CPU {idleCpu.Average() * 1000:F1} us/frame avg){(redrawn.Count > 0 ? " REDRAWN: " + string.Join(", ", redrawn) : "")}");

        // 3. One change, one redraw, on the one screen that shows it.
        before = screens.ToDictionary(s => s, s => s.Redraws);
        _main.State.Ship.Toggle(ShipSystem.CoreGravity);
        await _drive.Seconds(0.6); // the lever on the screen animates for ~0.3 s
        var changed = screens.Where(s => s.Redraws != before[s]).ToDictionary(s => s.Name.ToString(), s => s.Redraws - before[s]);
        Check(changed.Count == 1 && changed.ContainsKey("ConsoleScreen"),
            $"flipping core gravity redraws only the console ({string.Join(", ", changed.Select(c => $"{c.Key} x{c.Value}"))}: the lever eases over a few frames, then stops)");

        // 4. A busy constructor redraws at its refresh rate, not every frame.
        before = screens.ToDictionary(s => s, s => s.Redraws);
        _main.State.Constructor.Order(Catalog.HullPlate);
        watch.Restart();
        frames = 0;
        while (watch.Elapsed.TotalSeconds < 1)
        {
            await _drive.Frames();
            frames++;
        }
        int fab = _main.ConstructorScreen.Redraws - before[_main.ConstructorScreen];
        Check(fab > 0 && fab <= _main.ConstructorScreen.RefreshHz + 2,
            $"a busy constructor redraws {fab} times in 1 s ({frames} frames): capped by its {_main.ConstructorScreen.RefreshHz} Hz refresh");
        Drive.Tap(Key.Tab);
        await _drive.Frames(30);
    }

    /// <summary>
    /// What one redraw of each screen costs on this machine: each SubViewport is forced to
    /// draw every frame for a moment, and the renderer's own timings are averaged.
    /// </summary>
    async Task MeasureRenderCost(List<WorldScreen> screens)
    {
        var cpu = screens.ToDictionary(s => s, _ => 0.0);
        var gpu = screens.ToDictionary(s => s, _ => 0.0);
        Rid mainRid = GetViewport().GetViewportRid();
        double mainGpuIdle = 0;
        const int samples = 30;

        for (int i = 0; i < samples; i++)
        {
            await _drive.Frames();
            mainGpuIdle += RenderingServer.ViewportGetMeasuredRenderTimeGpu(mainRid) / samples;
        }
        foreach (WorldScreen s in screens) s.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        await _drive.Frames(5);
        for (int i = 0; i < samples; i++)
        {
            await _drive.Frames();
            foreach (WorldScreen s in screens)
            {
                Rid rid = s.Viewport.GetViewportRid();
                cpu[s] += RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid) / samples;
                gpu[s] += RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid) / samples;
            }
        }
        foreach (WorldScreen s in screens) s.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        await _drive.Frames(3);

        foreach (WorldScreen s in screens)
            GD.Print($"      {s.Name,-18} {s.Pixels.X}x{s.Pixels.Y}  one redraw: CPU {cpu[s]:F3} ms  GPU {gpu[s]:F3} ms");
        GD.Print($"      all {screens.Count} redrawing every frame would cost CPU {cpu.Values.Sum():F3} ms + GPU {gpu.Values.Sum():F3} ms per frame;" +
                 $" idle they cost nothing (for scale, the 3D view itself: GPU {mainGpuIdle:F2} ms)");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;

    void Check(bool ok, string what)
    {
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
