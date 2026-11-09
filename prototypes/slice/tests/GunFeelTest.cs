using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Slice.Game;
using Slice.Player;

namespace Slice.Tests;

/// <summary>
/// Gun feel: firing shows a muzzle flash that cleans itself up (a few frames of flash, a light,
/// smoke that clears), every kind of fire flashes, the held tool and the camera kick and come home
/// (the aim exactly what it was), and ten seconds of sustained fire do not grow the node count.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/GunFeelTest.tscn
/// </summary>
public partial class GunFeelTest : Node
{
    const int WatchdogMinutes = 10;
    Game.Game _game;
    Playthrough _play;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    static int Nodes() => (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);

    /// <summary>How many nodes stand under each branch a few levels down (where a count grew).</summary>
    Dictionary<string, int> Branches()
    {
        var map = new Dictionary<string, int>();
        int Walk(Node n, string path, int depth)
        {
            int count = 1;
            foreach (Node c in n.GetChildren()) count += Walk(c, path + "/" + c.Name, depth + 1);
            if (depth <= 4) map[path] = count;
            return count;
        }
        Walk(GetTree().Root, "", 0);
        return map;
    }

    static string Grown(Dictionary<string, int> a, Dictionary<string, int> b) =>
        string.Join(", ", b.Where(kv => kv.Value != a.GetValueOrDefault(kv.Key)).OrderByDescending(kv => kv.Key.Count(ch => ch == '/')).ThenByDescending(kv => Math.Abs(kv.Value - a.GetValueOrDefault(kv.Key))).Take(12).Select(kv => $"{kv.Key} {kv.Value - a.GetValueOrDefault(kv.Key):+0;-0}"));

    bool AimIsExact() => _game.Camera.GlobalBasis.IsEqualApprox(_game.Survivor.Eye().Basis);

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = false, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _play = new Playthrough(_game);
        _drive = _play.Drive;
        while (_game.Phase != Phase.Playing) yield return 0;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        foreach (int x in _drive.UntilPlaying()) yield return x;
        // Out of the cryopod, the locker's kit in the bag, then the tool in hand in Shoot mode.
        foreach (int x in _play.WakeAndLeave((_, _) => { })) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _play.Tool(ToolMode.Shoot)) yield return x;
        foreach (int x in _drive.LookAt(_game.Survivor.WorldEye() + _game.Survivor.WorldLook() * 8f, 6)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;

        ShotFx fx = _game.Shots;
        Hands hands = _game.Hands;
        Check(fx.FlashPool >= 8 && fx.FlashesActive == 0, $"the flash pool is built up front ({fx.FlashPool} slots) and idle");
        Check(AimIsExact() && hands.Kick == 0f, "at rest, no kick and the camera is exactly on the aim");

        // ---- The survival tool: a flare, a kick, and everything home again ----
        int flashes = fx.Flashes;
        float peakKick, peakTilt;
        hands.ResetPeaks();
        _game.Controls.Primary = true;
        yield return 0;
        _game.Controls.Primary = false;
        for (int i = 0; i < 60; i++)
        {
            yield return 0;
        }
        peakKick = hands.PeakKick;
        peakTilt = hands.PeakTilt;
        Check(fx.Flashes == flashes + 1, "firing the tool shows one muzzle flare");
        Check(fx.FlashesActive == 0, "and the flare has cleaned itself up a second later");
        Check(peakKick > 0.5f && peakTilt > 0.0005f, $"the hand kicked ({peakKick:F2}) and the camera tilted ({Mathf.RadToDeg(peakTilt):F2} degrees)");
        Check(hands.Kick == 0f && AimIsExact(), "then the recoil returned to rest and the camera aim came back exactly");

        Check(GunFeel.Tool.Colour.R > 0.9f && GunFeel.Tool.Colour.B < 0.4f, $"the shoot bolt and its flash are red ({GunFeel.Tool.Colour})");

        // ---- Every kind of fire flashes, lasts a few frames, and clears ----
        Vector3 at = _game.Tool.Muzzle(), dir = _game.Survivor.WorldLook();
        foreach (GunFeel feel in GunFeel.All)
        {
            int before = fx.Flashes;
            fx.SetProcess(false); // stepped by hand, a frame at a time: ticks and drawn frames needn't match
            fx.Muzzle(feel.Id, at, dir);
            bool lit = fx.FlashesShowing > 0;
            int frames = 0;
            while (frames < 20 && fx.FlashesShowing > 0) { fx._Process(1.0 / 60.0); frames++; }
            fx.SetProcess(true);
            Check(fx.Flashes == before + 1 && lit && frames == feel.Frames && feel.Frames is >= 2 and <= 4, $"{feel.Id}: a {feel.Style} flash shown for {frames} frames, then gone");
            for (int i = 0; i < 150 && fx.FlashesActive > 0; i++) yield return 0;
            Check(fx.FlashesActive == 0, $"{feel.Id}: smoke and light cleaned up");
        }

        // ---- A vessel gun scales by calibre and shakes the pilot's camera, and the aim comes home ----
        fx.Muzzle("vessel_gun", at, dir, default, 2.5f);
        hands.Shake(GunFeel.Get("vessel_gun").Shake);
        hands.ResetPeaks();
        for (int i = 0; i < 20; i++)
        {
            yield return 0;
        }
        for (int i = 0; i < 90; i++) yield return 0;
        float shakeAway = hands.PeakTilt;
        Check(shakeAway > 0.0005f && AimIsExact(), $"a vessel gun's shake moved the camera ({Mathf.RadToDeg(shakeAway):F2} degrees) and the aim came back exactly");

        // ---- Sustained fire: ten seconds, node count flat (tool shots into the open sky, so no scorch decals pile up) ----
        foreach (int x in _drive.LookAt(_game.Survivor.WorldEye() + new Vector3(0.3f, 1f, 0.2f).Normalized() * 50f, 8)) yield return x;
        at = _game.Tool.Muzzle();
        dir = _game.Survivor.WorldLook();
        foreach (int x in _drive.Wait(240)) yield return x;
        int warm = Nodes();
        var warmBranches = Branches();
        int flashesBefore = fx.Flashes, maxNodes = warm;
        int shotsBefore = _game.Tool.Shots;
        string[] mix = { "bullet", "plasma", "laser", "rocket" };
        for (int tick = 0; tick < 600; tick++)
        {
            _game.Controls.Primary = true; // the tool, as fast as it will go
            if (tick % 4 == 0) fx.Muzzle(mix[tick / 4 % mix.Length], at, dir);
            yield return 0;
            maxNodes = Math.Max(maxNodes, Nodes());
        }
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait(240)) yield return x;
        int after = Nodes();
        Check(fx.Flashes - flashesBefore > 100 && _game.Tool.Shots - shotsBefore > 10,
            $"10 s of fire: {_game.Tool.Shots - shotsBefore} tool shots and {fx.Flashes - flashesBefore} flashes");
        Check(fx.FlashPool == 16 && maxNodes - warm <= 40 && after - warm <= 2,
            $"the pool never grew ({fx.FlashPool} slots) and nodes held steady: {warm} before, at most {maxNodes} during, {after} after (changed: {Grown(warmBranches, Branches())})");
        Check(AimIsExact(), "and the aim is still exactly where it was");

        // ---- Gather mode: a blue beam while the tool works, gone when it stops ----
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
        Vector3 feet = _game.Survivor.WorldFeet;
        foreach (int x in _drive.LookAt(_game.Zone.GroundAt(feet.X + 1.5f, feet.Z + 1.5f), 8)) yield return x;
        foreach (int x in _drive.Wait(20)) yield return x;
        Check(!fx.BeamShowing, "no beam while the tool is idle");
        int flashesG = fx.Flashes;
        _game.Controls.Primary = true;
        bool beamSeen = false;
        for (int i = 0; i < 40; i++) { yield return 0; beamSeen |= fx.BeamShowing; }
        Check(beamSeen && fx.Beams >= 1, "gathering shows the blue beam");
        Check(fx.Flashes == flashesG, "and the gather beam has no muzzle flash");
        _game.Controls.Primary = false;
        for (int i = 0; i < 10; i++) yield return 0;
        Check(!fx.BeamShowing, "the beam is gone once the tool stops");

        // ---- Death: starvation and a creature's hit both kill, movement stops, the banner has its cause, R wakes you at full health ----
        foreach (int x in _drive.Wait(30)) yield return x;
        Suit suit = _game.Suit;
        double max = suit.Sim.Data.Vitals.Health.Max;
        suit.Vitals.Health = 0.4; // the HUD prints "0" (F0) for this, but it is not death
        Check($"{suit.Vitals.Health:F0}" == "0" && !suit.Dead, "health 0.4 prints as 0 on the HUD and is still alive");
        suit.Vitals.Food = 0;
        for (int i = 0; i < 60 * 30 && !suit.Dead; i++) yield return 0;
        Check(suit.Dead && suit.Vitals.Health <= 0 && suit.Sim.Player.DeathCause == "Starved", $"starvation drains that remainder and kills ({suit.Sim.Player.DeathCause})");
        Check(!_game.Menu.IsOpen && suit.Sim.Player.DeathCause != null, "dead with the menu closed and a cause for the Visor's death banner");
        Vector3 spot = _game.Survivor.WorldFeet;
        _game.Controls.Hold(Key.W);
        foreach (int x in _drive.Wait(60)) yield return x;
        _game.Controls.ReleaseAll();
        Check(_game.Survivor.WorldFeet.DistanceTo(spot) < 0.6f, "a dead survivor does not walk");
        foreach (int x in _drive.Tap(Key.R)) yield return x;
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(!suit.Dead && suit.Vitals.Health >= max - 0.5, $"R wakes them at full health ({suit.Vitals.Health:F0} of {max:F0})");

        double lost = suit.Hit(max * 2, SurvivalVitals.Model.DamageKind.Hit); // a bite past armour
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(lost > 0 && suit.Dead && suit.Sim.Player.DeathCause != null, $"a killing hit kills at once ({suit.Sim.Player.DeathCause})");
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in _drive.Tap(Key.R)) yield return x;
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(!suit.Dead && suit.Vitals.Health >= max - 0.5, "and R wakes them again at full health");
    }
}
