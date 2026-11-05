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
    const double TimeoutSeconds = 600;

    Game.Game _game;
    string _out = "screenshot.png", _view = "pod";
    readonly Dictionary<string, string> _args = new();
    IEnumerator<int> _script;
    double _elapsed;
    bool _capturing;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100; // after the game's tick, so what it sets is read on the next one
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) _args[arg[2..eq]] = arg[(eq + 1)..];
        }
        if (_args.TryGetValue("out", out string o)) _out = o;
        if (_args.TryGetValue("view", out string v)) _view = v;
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        var staging = new Staging(_game, _args);
        _script = staging.Run(_view).GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_capturing) return;
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
