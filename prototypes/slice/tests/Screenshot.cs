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

    Game.Game _game;
    string _out = "screenshot.png", _view = "pod";
    readonly Dictionary<string, string> _args = new();
    IEnumerator<int> _script;
    double _elapsed;
    bool _capturing;
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
        if (_view == "city") PlanetSurface.Infra.CityPlan.Force = true; // a city on any lived-in world
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--ringed=1") >= 0 ? RingedWorld() : WorldChoice.Tested) };
        AddChild(_game);
        _staging = new Staging(_game, _args) { TakeShot = file => _pending = file };
        _script = _staging.Run(_view).GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_capturing) return;
        if (_pending != null)
        {
            Middle(_pending);
            return;
        }
        if ((_elapsed += delta) > TimeoutSeconds)
        {
            GD.PrintErr($"screenshot '{_view}' timed out");
            Wildlife.Shutdown.Quit(this, 1);
            return;
        }
        bool more;
        try { more = _script.MoveNext(); }
        catch (Exception e)
        {
            GD.PrintErr($"screenshot '{_view}' failed: {e}");
            Wildlife.Shutdown.Quit(this, 1);
            return;
        }
        if (!more) Capture();
    }

    /// <summary>A shot part way through a view: saved, and the view goes on.</summary>
    async void Middle(string file)
    {
        _capturing = true;
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
        _capturing = true;
        // Two drawn frames, so the last placement and every screen redraw are in the image.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error err = image.SavePng(_out);
        GD.Print(err == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {err}");
        Wildlife.Shutdown.Quit(this, err == Error.Ok ? 0 : 1);
    }
}
