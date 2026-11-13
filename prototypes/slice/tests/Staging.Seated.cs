using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Vessels;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// --view=seated: the sleeper in every chair and cockpit (<see cref="SeatedTest.Floor"/>), from its
/// own eyes looking ahead and looking down at its lap, and from outside, in front of the chair.
/// --out=DIR is the folder (three PNGs a chair).
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> SeatedView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.36);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string dir = _args.GetValueOrDefault("out", "seated");
        Directory.CreateDirectory(dir);
        Vector3 site = Play.FlatSpot(_game.Survivor.WorldFeet, 30f, span: 14f);
        int turns = 0;
        foreach (var type in _game.Zone.Data.Catalogue.Types.Where(t => t != null && Vessel.IsChair(t)).ToList())
        {
            site += new Vector3(18f, 0f, 0f);
            _game.Survivor.PlaceOutside((site + new Vector3(0f, 3f, -10f)).ToNumerics());
            foreach (int x in _drive.Wait(20)) yield return x;
            Vessel v = SeatedTest.Floor(_game, type, site, turns++);
            foreach (int x in _drive.Wait(20)) yield return x;
            v.TakeChair(SeatedTest.Anchor);
            _game.Survivor.Sit(v);
            foreach (int x in _drive.UntilSettled()) yield return x;
            _game.Survivor.Pitch = -0.25f;
            foreach (int x in _drive.Wait(40)) yield return x;
            foreach (int x in Shot(Path.Combine(dir, $"{type.Id}-1-ahead.png"))) yield return x;
            _game.Survivor.Pitch = -0.95f;
            foreach (int x in _drive.Wait(5)) yield return x;
            foreach (int x in Shot(Path.Combine(dir, $"{type.Id}-2-down.png"))) yield return x;
            // From outside: in front of the chair and off to its right, looking back at its sitter.
            Basis chair = v.DrawTransform.Basis * v.ChairBasis;
            Vector3 seat = v.DrawTransform * v.Seat.Value.ToGodot();
            Vector3 eye = seat - chair.Z * 3.2f + chair.X * 1.6f + chair.Y * 0.9f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(seat + chair.Y * 0.3f - eye, chair.Y), eye);
            foreach (int x in _drive.Wait(5)) yield return x;
            foreach (int x in Shot(Path.Combine(dir, $"{type.Id}-3-outside.png"))) yield return x;
            _game.FixedCamera = null;
            _game.Survivor.StandUp();
        }
    }
}
