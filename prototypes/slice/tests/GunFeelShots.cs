using System;
using System.Collections.Generic;
using Godot;
using Slice.Game;
using Slice.Player;

namespace Slice.Tests;

/// <summary>
/// Screenshots of the muzzle flashes, mid-flash. Run windowed:
///   godot --path prototypes/slice res://tests/GunFeelShots.tscn -- --feel=bullet|tool|gather --where=bunker|outside --out=FILE.png
/// (Not a *Test scene, so the gate doesn't run it.)
/// </summary>
public partial class GunFeelShots : Node
{
    Game.Game _game;
    Playthrough _play;
    IEnumerator<int> _run;
    string _feel = "bullet", _where = "outside", _out = "gunfeel.png";
    bool _capturing;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (eq < 3 || !arg.StartsWith("--")) continue;
            string v = arg[(eq + 1)..];
            switch (arg[2..eq])
            {
                case "feel": _feel = v; break;
                case "where": _where = v; break;
                case "out": _out = v; break;
                case "size": GunFeel.FlashScale = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
            }
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
            GD.PrintErr($"gun feel shot failed: {e}");
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
        if (_where == "outside")
        {
            foreach (int x in drive.ClimbOut(_game.Bunker)) yield return x;
            foreach (int x in drive.UntilSettled()) yield return x;
        }
        foreach (int x in _play.Tool(_feel == "gather" ? ToolMode.Gather : ToolMode.Shoot)) yield return x;
        Vector3 feet = _game.Survivor.WorldFeet;
        Vector3 target = _where == "outside"
            ? _game.Zone.GroundAt(feet.X + 7f, feet.Z + 4f)
            : _game.Bunker.At(new Vector3(3.25f, 1.4f, -1.0f));
        if (_feel == "gather")
        {
            Vector3 away = feet - _game.Bunker.At(Vector3.Zero); // the ground on the far side from the bunker, so no block is in the way
            away.Y = 0f;
            away = away.LengthSquared() < 1e-4f ? Vector3.Right : away.Normalized();
            target = _game.Zone.GroundAt(feet.X + away.X * 3f, feet.Z + away.Z * 3f);
        }
        foreach (int x in drive.LookAt(target, 8)) yield return x;
        foreach (int x in drive.Wait(20)) yield return x;

        // The flash and the kick, then freeze them where they stand and let the smoke come out.
        if (_feel == "gather")
        {
            _game.Controls.Primary = true;
            foreach (int x in drive.Wait(6)) yield return x;
            _game.Shots.SetProcess(false);
            _game.Hands.SetProcess(false);
            foreach (int x in drive.Wait(2)) yield return x;
            GD.Print($"gather: beam showing {_game.Shots.BeamShowing}");
            yield break;
        }
        GunFeel feel = GunFeel.Get(_feel);
        _game.Hands.Recoil(feel);
        yield return 0; // the hand has kicked: the flash goes on its barrel tip as it is now
        Vector3 muzzle = _game.Tool.Muzzle();
        if (feel == GunFeel.Tool)
        {
            _game.Shots.Fire(muzzle, target, default, null, feel.Id, _game.Tool.HeldMuzzle);
            yield return 0; // a tick on, the beam is out of the barrel and the flash still up
        }
        else _game.Shots.Muzzle(feel.Id, muzzle, _game.Survivor.WorldLook());
        _game.Shots.SetProcess(false); // in this very tick, before any frame has counted the flash down
        _game.Hands.SetProcess(false);
        foreach (int x in drive.Wait(_where == "outside" ? 10 : 8)) yield return x;
        GD.Print($"{feel.Id}: showing {_game.Shots.FlashesShowing}, active {_game.Shots.FlashesActive}");
    }

    async void Capture()
    {
        _capturing = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(_out);
        GD.Print(err == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {err}");
        Wildlife.Shutdown.Quit(this, err == Error.Ok ? 0 : 1);
    }
}
