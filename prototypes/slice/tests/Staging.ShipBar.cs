using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using GalHack.Physics.Engine;
using Godot;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// --view=shipbar: the sleeper in the command chair of <see cref="BarTest.Rig"/>'s vessel by day on real ground, the
/// structure's toolbar along the bottom (1-bar.png), a burst from its gatlings at the wall ahead, from beside it (2-burst.png), the
/// Core tab's BAR strip and SETTINGS page (3-core.png) and its NUMBERS page (4-core-numbers.png), and the turret close up switched
/// on and off by its slot (5-turret-on.png, 6-turret-off.png). --dir=DIR is the folder.
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
        // The burst from beside the vessel, its rounds in flight between the guns and the wall: taken once one is well out.
        foreach (int x in _drive.Tap(Key.Key2)) yield return x;
        Vector3 muzzle = v.Bar.Muzzles().First(), ahead = (v.ToWorld(N.Vector3.Zero) - v.ToWorld(N.Vector3.UnitZ)).Normalized();
        Vector3 side = ahead.Cross(Vector3.Up).Normalized(), eye = muzzle - side * 9f + Vector3.Up * 2.5f - ahead * 3f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(muzzle + ahead * 10f - eye, Vector3.Up), eye);
        _game.Controls.PrimaryPressed = true;
        for (int i = 0; i < 240; i++)
        {
            _game.Controls.Primary = true;
            yield return 0;
            if (i < (int)Num("burst", 20)) continue;
            bool out_ = false;
            foreach (ref readonly GalHack.Arms.Shot shot in _game.Arms.Pool.Shots.AsSpan())
                if (shot.Live && shot.Def.Item == "gatling" && (shot.Pos.ToGodot() - muzzle).Dot(ahead) is > 4f and < 12f) out_ = true;
            if (out_) break;
        }
        foreach (int x in Shot(Path.Combine(dir, "2-burst.png"))) yield return x;
        _game.Controls.Primary = false;
        _game.FixedCamera = null;
        foreach (int x in Play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "3-core.png"))) yield return x;
        _game.Menu.Root.Core.Structure.ShowNumbers = true;
        foreach (int x in _drive.Wait(5)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "4-core-numbers.png"))) yield return x;
        _game.Menu.Root.Core.Structure.ShowNumbers = false;
        foreach (int x in Play.CloseMenu()) yield return x;
        // The turret close up, switched on (barrels run out, its light lit) and off (parked low, dark).
        Vector3 turret = v.ToWorld(v.Structure.PointOf(BarTest.Turret, v.Structure.Get(BarTest.Turret), N.Vector3.Zero) * v.Structure.Grid.CellSize);
        Vector3 look = turret + side * 2.2f + Vector3.Up * 1.0f + ahead * 1.6f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(turret - look, Vector3.Up), look);
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "5-turret-on.png"))) yield return x;
        foreach (int x in _drive.Tap(Key.Key3)) yield return x;
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "6-turret-off.png"))) yield return x;
        _game.FixedCamera = null;
    }
}
