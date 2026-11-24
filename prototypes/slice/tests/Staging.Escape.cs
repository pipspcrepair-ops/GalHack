using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Ui;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The escape menu over the world, a frame of each page, all through the game's own keys and mouse:
/// <c>--view=escmenu --dir=folder</c> writes escmenu-world, -graphics, -view (the menu aside), -sound, -controls (a key
/// moved, a clash and a prompt showing), -mouse and -quit. <c>--view=escnewgame --dir=folder</c> writes
/// escnew-menu, escnew-ask (NEW GAME?) and escnew-start (the start screen it leads to).
/// </summary>
public sealed partial class Staging
{
    /// <summary>NEW GAME on the escape menu, its question, and YES: the world torn down and the start screen up, as at launch.</summary>
    IEnumerable<int> EscapeNewGameShots()
    {
        string dir = _args.GetValueOrDefault("dir", "user://");
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.LookAt(_game.Survivor.WorldEye() + _game.Survivor.WorldLook() * 20f + Vector3.Down * 2f, 10)) yield return x;
        EscapeMenu esc = _game.Escape;
        IEnumerable<int> Click(string id)
        {
            for (int i = 0; i < 4 && esc.RectOf(id) == null; i++) yield return 0;
            if (esc.RectOf(id) is not Rect2 r) throw new InvalidOperationException($"no widget '{id}' on the escape menu ({string.Join(", ", esc.Root.WidgetIds.Take(60))})");
            _game.Controls.Mouse = r.GetCenter();
            yield return 0;
            _game.Controls.PrimaryPressed = _game.Controls.Primary = true;
            yield return 0;
            _game.Controls.Primary = false;
            _game.Controls.PrimaryReleased = true;
            yield return 0;
        }
        IEnumerable<int> Frame(string name)
        {
            _game.Controls.Mouse = new Vector2(-100, -100);
            foreach (int x in _drive.Wait(4)) yield return x;
            foreach (int x in Shot($"{dir}/escnew-{name}.png")) yield return x;
        }
        _game.Controls.Tap(Key.Escape);
        foreach (int x in _drive.Wait(2)) yield return x;
        if (!esc.IsOpen) throw new InvalidOperationException("Esc did not open the escape menu");
        foreach (int x in Frame("menu")) yield return x;
        foreach (int x in Click("esc:new")) yield return x;
        foreach (int x in Frame("ask")) yield return x;
        foreach (int x in Click("esc:new-yes")) yield return x;
        for (int i = 0; i < 600 && _game.Next?.StartScreen?.Root == null; i++) yield return 0;
        if (_game.Next?.StartScreen == null) throw new InvalidOperationException("YES did not bring up the start screen");
        for (int i = 0; i < 30; i++) yield return 0;
        foreach (int x in Shot($"{dir}/escnew-start.png")) yield return x;
    }

    IEnumerable<int> EscapeMenuShots()
    {
        string dir = _args.GetValueOrDefault("dir", "user://");
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.LookAt(_game.Survivor.WorldEye() + _game.Survivor.WorldLook() * 20f + Vector3.Down * 2f, 10)) yield return x;
        EscapeMenu esc = _game.Escape;

        IEnumerable<int> Click(string id)
        {
            for (int i = 0; i < 4 && esc.RectOf(id) == null; i++) yield return 0;
            if (esc.RectOf(id) is not Rect2 r) throw new InvalidOperationException($"no widget '{id}' on the escape menu ({string.Join(", ", esc.Root.WidgetIds.Take(60))})");
            _game.Controls.Mouse = r.GetCenter();
            yield return 0;
            _game.Controls.PrimaryPressed = _game.Controls.Primary = true;
            yield return 0;
            _game.Controls.Primary = false;
            _game.Controls.PrimaryReleased = true;
            yield return 0;
        }
        IEnumerable<int> Press(Key key)
        {
            _game.Controls.Tap(key);
            yield return 0;
            yield return 0;
        }
        IEnumerable<int> Frame(string name)
        {
            _game.Controls.Mouse = new Vector2(-100, -100); // nothing lit under the pointer
            foreach (int x in _drive.Wait(4)) yield return x;
            foreach (int x in Shot($"{dir}/escmenu-{name}.png")) yield return x;
        }

        foreach (int x in Press(Key.Escape)) yield return x;
        if (!esc.IsOpen) throw new InvalidOperationException("Esc did not open the escape menu");
        foreach (int x in Frame("world")) yield return x;
        foreach (int x in Click("esc:options")) yield return x;
        foreach (int x in Click("opt:graphics")) yield return x;
        foreach (int x in Frame("graphics")) yield return x;
        foreach (int x in Click("opt:view")) yield return x;
        foreach (int x in Frame("view")) yield return x;
        foreach (int x in Press(Key.Escape)) yield return x;
        foreach (int x in Click("opt:sound")) yield return x;
        foreach (int x in Frame("sound")) yield return x;
        foreach (int x in Click("opt:controls")) yield return x;
        // The torch moved to K, the map put on K as well (a clash, red on both), and the use key asking for a new one.
        foreach (int x in Click("key:torch")) yield return x;
        foreach (int x in Press(Key.K)) yield return x;
        foreach (int x in Click("key:map")) yield return x;
        foreach (int x in Press(Key.K)) yield return x;
        foreach (int x in Click("key:use")) yield return x;
        foreach (int x in Frame("controls")) yield return x;
        foreach (int x in Press(Key.Escape)) yield return x;
        foreach (int x in Click("opt:mouse")) yield return x;
        foreach (int x in Frame("mouse")) yield return x;
        foreach (int x in Click("esc:quit")) yield return x;
        foreach (int x in Frame("quit")) yield return x;
        foreach (int x in Click("esc:quit-no")) yield return x;
        foreach (int x in Click("esc:resume")) yield return x;
    }
}
