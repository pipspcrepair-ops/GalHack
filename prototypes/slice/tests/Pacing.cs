using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Game;
using Slice.Player;
using GalHack.Physics.Engine;
using Slice.Vessels;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Frame pacing in the game as it starts (the pod, the bunker's stairs, the start town), driven through the
/// controls with real physics: standing still, standing and turning, walking toward the start's town, on a
/// hover bike and flying the starter vessel, each measured by <see cref="FramePacing"/>.
/// Run windowed, full screen on a screen (borderless, its own resolution) with the owner's settings:
///   godot --path prototypes/slice res://tests/Pacing.tscn -- --settings=owner-settings.cfg --screen=N [--seed=N] [--dump=dir] [--trees=0.5] [--quick=1]
/// </summary>
public partial class Pacing : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    FramePacing _pacing;
    readonly Dictionary<string, string> _args = new();
    Survivor S => _game.Survivor;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(10));
            Console.Error.WriteLine("Pacing took over 10 minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) _args[arg[2..eq]] = arg[(eq + 1)..];
        }
        if (_args.TryGetValue("screen", out string sc)) DisplayServer.WindowSetCurrentScreen(int.Parse(sc));
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen); // the owner's borderless, at the screen's own size
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
        if (_args.TryGetValue("trees", out string tr)) PlanetSurface.Terrain.PlanetFlora.TreeDensity = float.Parse(tr, System.Globalization.CultureInfo.InvariantCulture);
        ProcessPhysicsPriority = 100;
        _game = new Game.Game { Interactive = false, SettingsPath = _args.GetValueOrDefault("settings"), World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _pacing = new FramePacing { Name = "Pacing", Camera = () => _game.Camera };
        AddChild(_pacing);
        _drive = new Driver(_game);
        _run = Run().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        int code = 0;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            GD.PrintErr($"FAIL pacing: {e}");
            more = false;
            code = 1;
        }
        if (more) return;
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, code);
    }

    /// <param name="drive">what moves you meanwhile, if not <paramref name="each"/> tick by tick</param>
    /// <summary>The mouse, moved every drawn frame as a hand moves it (pixels a second; a tick takes what came since the last).</summary>
    Vector2 _mouse;

    public override void _Process(double delta)
    {
        if (_mouse != Vector2.Zero) _game.Controls.Look += _mouse * (float)delta;
    }

    IEnumerable<int> Measure(string name, int ticks, Action each = null, IEnumerable<int> drive = null)
    {
        foreach (int x in _drive.UntilSettled(30)) yield return x;
        _pacing.Start();
        Vector3 from = S.WorldFeet;
        using (IEnumerator<int> d = drive?.GetEnumerator())
            for (int i = 0; i < ticks && (d == null || d.MoveNext()); i++)
            {
                each?.Invoke();
                yield return 0;
            }
        if (_args.TryGetValue("dump", out string dir))
        {
            System.IO.Directory.CreateDirectory(dir);
            _pacing.Dump($"{dir}/{name}.csv");
        }
        GD.Print($"PACING {name} ({S.WorldFeet.DistanceTo(from):F0} m in {ticks / 60} s; paced at {_game.Pacer.Refreshes} refreshes a frame, work {_game.Pacer.Work:F1} ms, room {_game.Pacer.Room:F1} ms): {_pacing.Stop()}");
        _mouse = Vector2.Zero;
    }

    IEnumerable<int> Run()
    {
        foreach (int x in _drive.UntilPlaying(300)) yield return x;
        GD.Print($"PACING {FramePacing.Screens()}; window {DisplayServer.WindowGetSize()} {DisplayServer.WindowGetMode()}, vsync {DisplayServer.WindowGetVsyncMode()}, fps cap {Engine.MaxFps}, " +
                 $"physics {Engine.PhysicsTicksPerSecond}/s, interpolation {GetTree().PhysicsInterpolation}, jitter fix {Engine.PhysicsJitterFix}, max steps {Engine.MaxPhysicsStepsPerFrame}, " +
                 $"delta smoothing {ProjectSettings.GetSetting("application/run/delta_smoothing")}, .NET processors {System.Environment.ProcessorCount}, worker pool {ProjectSettings.GetSetting("threading/worker_pool/max_threads")}, trees {PlanetSurface.Terrain.PlanetFlora.TreeDensity}; world {_game.World}");
        var zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (int x in _drive.WalkTo(_game.Bunker.CryoFront, 0.3f, 10f)) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
        Vector3 town = _game.Bunker.Landmark ?? (S.WorldFeet + new Vector3(0f, 0f, -300f));
        // --city=1: all of it in the world's largest town or city instead, from a street 150 m short of its middle.
        if (_args.ContainsKey("city") && PopRoute.Biggest(_game) is var (mid, radius, what))
        {
            Vector3 street = mid - Vector3.Right * 150f;
            S.PlaceOutside((street with { Y = zone.GroundAt(street.X, street.Z).Y + 0.5f }).ToNumerics());
            town = mid + Vector3.Right * 150f;
            GD.Print($"PACING city: {what}, radius {radius:F0} m");
            foreach (int x in _drive.Wait(60)) yield return x;
            foreach (int x in _drive.UntilSettled(60)) yield return x;
            foreach (int x in _drive.Wait(300)) yield return x;
        }
        foreach (int x in _drive.LookAt(town with { Y = S.WorldEye().Y }, 10)) yield return x;
        _drive.Controls.Look = Vector2.Zero;

        foreach (int x in Measure("standing", 600)) yield return x;
        _mouse = new Vector2(360f, 0f);
        foreach (int x in Measure("turning", 600)) yield return x;
        _mouse = Vector2.Zero;
        foreach (int x in _drive.UntilSettled(30)) yield return x;
        foreach (int x in Measure("walking", 900, null, _drive.WalkTo(town, 3f, 15f, sprint: false))) yield return x;
        _drive.ReleaseAll();
        if (_args.ContainsKey("quick")) yield break; // --quick=1: standing, turning and walking only

        var play = new Playthrough(_game);
        Vector3 pad = play.FlatSpot(S.WorldFeet, 20f, span: 10f);
        S.PlaceOutside((pad + Vector3.Up * 0.5f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        // Back over the open ground the walk came across, away from the town's walls.
        Vector3 back = _game.Start - pad;
        var bike = _game.Bikes.Place(pad + new Vector3(2f, 0.6f, 0f), MathF.Atan2(-back.X, -back.Z));
        foreach (int x in _drive.Wait(10)) yield return x;
        S.Mount(bike);
        S.Yaw = bike.Yaw;
        foreach (int x in _drive.Wait(10)) yield return x;
        if (S.OnBike != bike) throw new Exception("not on the bike");
        _drive.Controls.Hold(Key.W);
        foreach (int x in _drive.Wait(60)) yield return x;
        int k = 0;
        foreach (int x in Measure("biking", 900, () => _mouse = new Vector2(++k % 240 < 120 ? 60f : -60f, 0f))) yield return x;
        _drive.ReleaseAll();
        _mouse = Vector2.Zero;
        S.Dismount();
        foreach (int x in _drive.Wait(30)) yield return x;

        Vector3 site = play.FlatSpot(S.WorldFeet, 25f, span: 14f);
        S.PlaceOutside((site + new Vector3(0f, 1f, 8f)).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        Vessel v = Vessel.Spawn(zone, zone.Data.Blueprint("starter.gbp"), (site + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        v.Settle();
        foreach (int x in _drive.Wait(10)) yield return x;
        v.TakeChair(v.Chairs.First());
        S.Sit(v);
        foreach (int x in _drive.Wait(10)) yield return x;
        if (S.Seated != v) throw new Exception("not in the vessel's chair");
        _drive.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(180)) yield return x;
        _drive.Controls.Hold(Key.Space, false);
        _drive.Controls.Hold(Key.W);
        foreach (int x in _drive.Wait(120)) yield return x;
        k = 0;
        foreach (int x in Measure("flying", 900, () => _mouse = new Vector2(++k % 240 < 120 ? 360f : -360f, 0f))) yield return x;
        _drive.ReleaseAll();
        _mouse = Vector2.Zero;
    }
}
