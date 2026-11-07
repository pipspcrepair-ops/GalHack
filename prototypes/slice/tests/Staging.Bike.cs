using System.Collections.Generic;
using Godot;
using Planets.Gen;
using Weather.Model;

namespace Slice.Tests;

public sealed partial class Staging
{
    /// <summary>Puts a hover bike on toolbar slot 2.</summary>
    void GiveBike()
    {
        _game.Pack.Storage.Add("hover-bike", 1);
        _game.Pack.Grid[1].Item = "hover-bike";
        _game.Pack.Grid[1].Count = 1;
        _game.Pack.Grid.Reconcile();
    }

    /// <summary>The hover bike set down from the toolbar, mounted with F, and ridden at speed, seen from behind.</summary>
    IEnumerable<int> HoverBikeView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        GiveBike();
        foreach (int x in Play.Select(1)) yield return x;
        foreach (int x in _drive.LookAt(_game.Survivor.WorldFeet + new Vector3(0f, -1f, 0f) + Flat(_game.Survivor.WorldLook()) * 2.5f, 6)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(5)) yield return x;
        _game.ThirdPerson = true;
        _game.Controls.Hold(Key.W);
        _game.Controls.Hold(Key.Shift);
        foreach (int x in _drive.Wait(150)) yield return x;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.X, 0f, v.Z).Normalized();
}
