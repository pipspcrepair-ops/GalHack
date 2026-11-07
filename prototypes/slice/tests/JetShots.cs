using System;
using System.Collections.Generic;
using Godot;
using Slice.Game;

namespace Slice.Tests;

/// <summary>
/// Screenshots of the jetpack gauge on foot. Run windowed:
///   godot --path prototypes/slice res://tests/JetShots.tscn -- --mode=use|low|idle --out=FILE.png
/// use: the pack firing with fuel part spent; low: nearly empty; idle: full and at rest, so no gauge.
/// (Not a *Test scene, so the gate doesn't run it.)
/// </summary>
public partial class JetShots : Node
{
    Game.Game _game;
    Playthrough _play;
    IEnumerator<int> _run;
    string _mode = "use", _out = "jet.png";
    bool _capturing;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (eq < 3 || !arg.StartsWith("--")) continue;
            string v = arg[(eq + 1)..];
            if (arg[2..eq] == "mode") _mode = v;
            else if (arg[2..eq] == "out") _out = v;
        }
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_capturing) return;
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            GD.PrintErr($"jet shot failed: {e}");
            Wildlife.Shutdown.Quit(this, 1);
            return;
        }
        if (!more) Capture();
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _play = new Playthrough(_game);
        Driver drive = _play.Drive;
        foreach (int x in drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.33;
        _game.Zone.Wildlife.Frozen = true;
        foreach (int x in _play.WakeAndLeave((_, _) => { })) yield return x;
        foreach (int x in drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in drive.UntilSettled()) yield return x;
        var legs = _game.Survivor.Legs;
        if (_mode == "idle")
        {
            foreach (int x in drive.Wait(240)) yield return x; // full and at rest: the gauge has faded away
            GD.Print($"idle: fuel {legs.JetpackFuel:0.00} of {legs.JetpackFuelMax:0.00}");
            yield break;
        }
        legs.JetpackFuel = _mode == "low" ? 0.3f : 2.2f;
        _game.Controls.Hold(Key.Space);
        foreach (int x in drive.Wait(_mode == "low" ? 6 : 14)) yield return x;
        GD.Print($"{_mode}: fuel {legs.JetpackFuel:0.00}, firing {legs.JetpackFiring}, on floor {_game.Survivor.Body.OnFloor}");
    }

    async void Capture()
    {
        _capturing = true;
        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(_out);
        GD.Print(err == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {err}");
        Wildlife.Shutdown.Quit(this, err == Error.Ok ? 0 : 1);
    }
}
