using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Slice.Game;

namespace Slice.Tests;

/// <summary>
/// Renders one moment of the slice to a PNG. Run windowed, not headless:
///   godot --path prototypes/slice res://tests/Screenshot.tscn -- --view=NAME --out=FILE.png
/// Views are staged by <see cref="Staging"/>, through the game's own controls wherever they can be.
/// </summary>
public partial class Screenshot : Node
{
    const double TimeoutSeconds = 1200;
    /// <summary>How long, wall-clock seconds, a shot may wait for the window to draw its frames.</summary>
    const double DrawSeconds = 60;

    Game.Game _game;
    string _out = "screenshot.png", _view = "pod";
    readonly Dictionary<string, string> _args = new();
    IEnumerator<int> _script;
    double _elapsed;
    bool _capturing, _quitting;
    ulong _captureSince;
    Staging _staging;
    string _pending;

    /// <summary>A fresh world with rings: the next one rolled that has them.</summary>
    static WorldChoice RingedWorld()
    {
        for (int i = 0; i < 4096; i++)
        {
            WorldChoice w = WorldChoice.Fresh("random");
            if (w.Spec().Has(Planets.Gen.Modifier.RingedSky)) return w;
        }
        throw new InvalidOperationException("no ringed world in 4,096 rolls");
    }

    public override void _Ready()
    {
        // A backstop after that one: should Exit wait on a busy main thread, the process ends itself regardless.
        new System.Threading.Thread(() => { System.Threading.Thread.Sleep(TimeSpan.FromSeconds(TimeoutSeconds + 360)); Console.Error.WriteLine("screenshot: wall-clock backstop"); System.Diagnostics.Process.GetCurrentProcess().Kill(); }) { IsBackground = true }.Start();
        // A wall-clock limit as well: a hung run quits itself, even with the game's clock stopped.
        var watchdog = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(TimeoutSeconds + 300));
            Console.Error.WriteLine($"screenshot '{_view}' took over {(TimeoutSeconds + 300) / 60:0} minutes of wall-clock time: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        watchdog.Start();
        ProcessPhysicsPriority = 100; // after the game's tick, so what it sets is read on the next one
        ProcessMode = ProcessModeEnum.Always; // a view runs on while the escape menu stops the world
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) _args[arg[2..eq]] = arg[(eq + 1)..];
        }
        if (_args.TryGetValue("out", out string o)) _out = o;
        if (_args.TryGetValue("view", out string v)) _view = v;
        // A shot waits for drawn frames, and a headless run draws none: it would sit there until the watchdog.
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr($"screenshot '{_view}': a headless run draws no frames to save; run it in a window (no --headless)");
            Quit(1);
            return;
        }
        if (_view == "city") PlanetSurface.Infra.CityPlan.Force = true; // a city on any lived-in world
        if (_view == "hulk") HulkTest.Force(); // a long hauler down near the start
        if (_view == "placesurf")
        {
            // The place surfaces proof: a city to frame, and an alien place set down near the start.
            PlanetSurface.Infra.CityPlan.Force = true;
            PlanetSurface.Poi.PoiSiting.ForceFamily = _args.GetValueOrDefault("force", "alien_cathedral");
        }
        // A place of a family near the landing, and who is in it (--force-family or --force, --force-occupant=Keepers).
        if (_args.TryGetValue("force-family", out string ff) || _args.TryGetValue("force", out ff)) PlanetSurface.Poi.PoiSiting.ForceFamily = ff;
        if (_args.TryGetValue("force-occupant", out string fo) && Enum.TryParse(fo, true, out PoiGenerator.Gen.Occupant who))
        {
            PlanetSurface.Poi.PoiSiting.ForceOccupancy = new PoiGenerator.Gen.OccupancyRoll(who);
            PlanetSurface.Poi.PoiSiting.ForceState = PoiGenerator.Gen.PoiState.Abandoned;
        }
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--ringed=1") >= 0 ? RingedWorld() : WorldChoice.Tested) };
        AddChild(_game);
        _staging = new Staging(_game, _args) { TakeShot = file => _pending = file };
        _script = _staging.Run(_view).GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_quitting || _script == null) return;
        if (_capturing)
        {
            // The game clock's limit is not counted while a shot waits on the window: this one is.
            if (Time.GetTicksMsec() - _captureSince > DrawSeconds * 1000)
            {
                GD.PrintErr($"screenshot '{_view}': the window drew no frame in {DrawSeconds:0} s, so the shot could not be taken");
                Quit(1);
            }
            return;
        }
        if (_pending != null)
        {
            Middle(_pending);
            return;
        }
        if ((_elapsed += delta) > TimeoutSeconds)
        {
            GD.PrintErr($"screenshot '{_view}' timed out");
            Quit(1);
            return;
        }
        bool more;
        try { more = _script.MoveNext(); }
        catch (Exception e)
        {
            GD.PrintErr($"screenshot '{_view}' failed: {e}");
            Quit(1);
            return;
        }
        if (!more) Capture();
    }

    void Quit(int code)
    {
        _quitting = true;
        Wildlife.Shutdown.Quit(this, code);
    }

    /// <summary>A shot starts: a minimised window draws nothing, so it is brought back first.</summary>
    void BeginShot()
    {
        _capturing = true;
        _captureSince = Time.GetTicksMsec();
        if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Minimized) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
    }

    /// <summary>A shot part way through a view: saved, and the view goes on.</summary>
    async void Middle(string file)
    {
        BeginShot();
        _pending = null;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        GD.Print(image.SavePng(file) == Error.Ok ? $"saved {file}" : $"could not save {file}");
        _capturing = false;
        _staging.Snapping = false;
    }

    async void Capture()
    {
        BeginShot();
        // Two drawn frames, so the last placement and every screen redraw are in the image.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error err = image.SavePng(_out);
        GD.Print(err == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {err}");
        Quit(err == Error.Ok ? 0 : 1);
    }
}
