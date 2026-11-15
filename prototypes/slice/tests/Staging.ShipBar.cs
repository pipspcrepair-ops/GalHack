using System.Collections.Generic;
using System.IO;
using GalHack.Physics.Engine;
using Godot;
using Slice.Ui;
using Slice.Vessels;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// --view=shipbar: the sleeper in the command chair of <see cref="BarTest.Rig"/>'s vessel by day on real ground, the
/// structure's toolbar along the bottom (1-bar.png), a burst from its gatlings at the wall ahead (2-burst.png), and the
/// Core tab's BAR strip (3-core.png). --dir=DIR is the folder.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> ShipBarView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Sky.Paused = true;
        string dir = _args.GetValueOrDefault("dir", "shipbar");
        Directory.CreateDirectory(dir);
        Vector3 site = Play.FlatSpot(_game.Survivor.WorldFeet, 40f, span: 34f);
        Fell(site, 26f);
        _game.Survivor.PlaceOutside((site + new Vector3(8f, 2f, 6f)).ToNumerics());
        foreach (int x in _drive.Wait(20)) yield return x;
        Vessel v = BarTest.Rig(_game, site, 20f, out _);
        foreach (int x in _drive.Wait(30)) yield return x;
        v.TakeChair(BarTest.Chair);
        _game.ThirdPerson = false;
        _game.Survivor.Sit(v);
        v.Storage.Add("belt-12mm", 4);
        foreach (int x in _drive.UntilSettled()) yield return x;
        _game.Survivor.Pitch = -0.12f;
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "1-bar.png"))) yield return x;
        foreach (int x in _drive.Tap(Key.Key2)) yield return x;
        _game.Controls.PrimaryPressed = true;
        for (int i = 0; i < (int)Num("burst", 14); i++)
        {
            _game.Controls.Primary = true;
            yield return 0;
        }
        foreach (int x in Shot(Path.Combine(dir, "2-burst.png"))) yield return x;
        _game.Controls.Primary = false;
        foreach (int x in Play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "3-core.png"))) yield return x;
        foreach (int x in Play.CloseMenu()) yield return x;
    }
}
