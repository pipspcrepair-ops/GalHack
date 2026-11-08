using System.Collections.Generic;
using System.IO;
using GalHack.Physics.Engine;
using Godot;
using Weather.Model;

namespace Slice.Tests;

/// <summary>
/// --view=castaway: the castaway's own figure in third person, on the world's own ground and weather:
/// walking, mid-jump, on the jetpack and astride the hover bike. --out=DIR is the folder.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> CastawayView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        string dir = _args.GetValueOrDefault("out", "castaway");
        Directory.CreateDirectory(dir);
        Slice.Player.Survivor s = _game.Survivor;
        Vector3 spot = Play.FlatSpot(s.WorldFeet, 25f, span: 12f);
        s.PlaceOutside((spot + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        _game.ThirdPerson = true;
        s.Yaw += 0.8f;
        s.Pitch = -0.15f;
        _game.Controls.Hold(Key.W);
        foreach (int x in _drive.Wait(45)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "1-walking.png"))) yield return x;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(2)) yield return x;
        _game.Controls.Hold(Key.Space, false);
        foreach (int x in _drive.Wait(14)) yield return x;
        GD.Print($"(castaway: jump: floor {s.Body.OnFloor}, up {s.Body.Velocity.Y:F2}, feet {s.WorldFeet.Y:F2}, ground {_game.Zone.GroundAt(s.WorldFeet.X, s.WorldFeet.Z).Y:F2})");
        foreach (int x in Shot(Path.Combine(dir, "2-mid-jump.png"))) yield return x;
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(60)) yield return x;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(50)) yield return x;
        GD.Print($"(castaway: jet: floor {s.Body.OnFloor}, firing {s.Legs.JetpackFiring}, fuel {s.Legs.JetpackFuel:F2}, feet {s.WorldFeet.Y:F2}, ground {_game.Zone.GroundAt(s.WorldFeet.X, s.WorldFeet.Z).Y:F2})");
        foreach (int x in Shot(Path.Combine(dir, "3-jetpack.png"))) yield return x;
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(150)) yield return x;
        _game.ThirdPerson = false;
        GiveBike();
        foreach (int x in Play.Select(1)) yield return x;
        foreach (int x in _drive.LookAt(s.WorldFeet + new Vector3(0f, -1f, 0f) + Flat(s.WorldLook()) * 2.5f, 6)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(5)) yield return x;
        _game.ThirdPerson = true;
        _game.Controls.Hold(Key.W);
        foreach (int x in _drive.Wait(60)) yield return x;
        s.Yaw += 0.6f;
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "4-hover-bike.png"))) yield return x;
        _game.Controls.ReleaseAll();
    }
}

