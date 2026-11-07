using System.Collections.Generic;
using System.IO;
using GalHack.Physics.Engine;
using Godot;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// --view=crouch: a 1.2 m passage through a bulkhead (<see cref="CrouchRig"/>) on level ground near the bunker, seen
/// standing (it won't go), crouched (it will), from outside, and from inside it with Ctrl let go.
/// --out=DIR is the folder (one PNG a moment).
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> CrouchView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.38);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string dir = _args.GetValueOrDefault("out", "crouch");
        Directory.CreateDirectory(dir);
        Slice.Player.Survivor s = _game.Survivor;
        Vector3 spot = Play.FlatSpot(s.WorldFeet, 20f, span: 10f);
        foreach (int x in _drive.Wait(20)) yield return x;
        CrouchRig rig = CrouchRig.Build(_game, spot);
        // Three metres short of the passage's mouth, facing it.
        void StandAtTheMouth()
        {
            s.PlaceOutside(new System.Numerics.Vector3(rig.Start.X, rig.Start.Y, rig.Near + 3f));
            s.Yaw = 0f;
            s.Pitch = -0.04f;
        }
        StandAtTheMouth();
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;

        foreach (int x in Shot(Path.Combine(dir, "1-standing.png"))) yield return x;
        // Standing, W walks into the lintel and stops at the mouth.
        _game.Controls.Hold(Key.W);
        foreach (int x in _drive.Wait(120)) yield return x;
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "2-standing-blocked.png"))) yield return x;

        // Back, crouched: the eye comes down and the same passage is a way through.
        StandAtTheMouth();
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Controls.Hold(Key.Ctrl);
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "3-crouched.png"))) yield return x;
        _game.ThirdPerson = true;
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "4-crouched-third-person.png"))) yield return x;
        _game.ThirdPerson = false;

        // Into the passage.
        _game.Controls.Hold(Key.W);
        for (int i = 0; i < 400 && s.WorldFeet.Z > rig.Near - 2.2f; i++) yield return 0;
        _game.Controls.Hold(Key.W, false);
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in Shot(Path.Combine(dir, "5-inside.png"))) yield return x;
        // Ctrl let go under the lintel: there is no room to stand, so it stays crouched.
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(60)) yield return x;
        GD.Print($"(crouch view: let go of Ctrl inside the passage: crouched {s.Crouched}, eye {s.EyeAbove:F2} m)");
        foreach (int x in Shot(Path.Combine(dir, "6-inside-ctrl-released.png"))) yield return x;
        // Out the far end, and it stands.
        _game.Controls.Hold(Key.W);
        for (int i = 0; i < 400 && s.WorldFeet.Z > rig.Far - 2f; i++) yield return 0;
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(60)) yield return x;
        s.Yaw = Mathf.Pi;
        foreach (int x in _drive.Wait(10)) yield return x;
        GD.Print($"(crouch view: out the far side: crouched {s.Crouched}, eye {s.EyeAbove:F2} m)");
    }
}
