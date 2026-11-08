using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Physics.Engine;
using Planets.Gen;
using PoiGenerator.Gen;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// A war's crater beside what it was aimed at (--view=strike; start the world with --fall nuclear_war): the
/// nearest ground burst on a place, seen across its bowl toward the ruined place, from --up metres (18 by
/// default) and then from the air. What it was aimed at and every strike on the world are printed.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> StrikeView()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        var world = _game.Surface.Planet;
        foreach (Strike s in world.Blasts) GD.Print($"(blast: {s})");
        Vector3 from = _game.Survivor.WorldFeet;
        double W = world.Width, H = world.Height;
        double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, W), 2) + Math.Pow(Mathx.WrapDelta(az - bz, H), 2));
        // A crater with a place beside it: the place it was aimed at, standing above ground (not a bunker's
        // hatch), in daylight latitudes (the low sun of the poles shows nothing), nearest the start.
        var pick = world.Blasts.Where(s => s.Bowl && !s.Site && !s.Miss && s.Target is not ("bridge" or "junction" or "crossing" or ""))
            .Select(s => (Strike: s, Place: world.Pois.Where(p => p.GroundZero == null).OrderBy(p => Dist(p.Center.X, p.Center.Z, s.AimX, s.AimZ)).FirstOrDefault()))
            .Where(t => t.Place != null && Dist(t.Place.Center.X, t.Place.Center.Z, t.Strike.AimX, t.Strike.AimZ) < 60)
            .OrderBy(t => (t.Place.Family.Tags.Contains("underground") ? 2 : 0) + (Math.Abs(t.Strike.Z - H / 2) > H * 0.3 ? 1 : 0))
            .ThenBy(t => Dist(t.Strike.X, t.Strike.Z, Mathx.WrapX(from.X, W), Mathx.Wrap(from.Z, H))).FirstOrDefault();
        if (pick.Strike == null)
        {
            GD.Print("(strike: no ground burst beside a place on this world)");
            yield break;
        }
        Strike strike = pick.Strike;
        var site = pick.Place;
        GD.Print($"(strike: {strike} beside {site.Name}, {Dist(site.Center.X, site.Center.Z, strike.X, strike.Z):F0} m from its middle, blasted {site.Instance.Blasted:F2})");
        Vector3 gz = world.NearestCopy(new Vector3((float)strike.X, 0f, (float)strike.Z), from);
        gz.Y = world.GroundHeight(Mathx.WrapX(gz.X, W), Mathx.Wrap(gz.Z, H));
        Vector3 place = world.NearestCopy(new Vector3((float)site.Center.X, 0f, (float)site.Center.Z), gz);
        place.Y = world.GroundHeight(Mathx.WrapX(place.X, W), Mathx.Wrap(place.Z, H));
        Vector3 toPlace = new Vector3(place.X - gz.X, 0f, place.Z - gz.Z).Normalized();
        foreach (var (tag, back, up, side) in new[] { ("ground", strike.Crater * 1.6f + 30f, (float)Num("up", 18), 0.35f), ("air", strike.Crater * 2.5f + 160f, 150f, 0.15f) })
        {
            // Across the bowl toward the place, a little off its line.
            Vector3 dir = (toPlace + toPlace.Cross(Vector3.Up) * side).Normalized();
            Vector3 feet = gz - dir * back;
            feet.Y = world.GroundHeight(Mathx.WrapX(feet.X, W), Mathx.Wrap(feet.Z, H)) + 0.5f;
            _game.Survivor.PlaceOutside(feet.ToNumerics());
            Vector3 eye = feet + Vector3.Up * up;
            Vector3 look = gz.Lerp(place, tag == "ground" ? 0.45f : 0.4f) + Vector3.Up * (tag == "ground" ? 2f : 0f);
            double day = _game.Zone.Sky.TimeOfDay;
            for (int i = 0; i < 1500; i++)
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
                if (i % 120 == 2) day = LocalDay();
                _game.Zone.Sky.TimeOfDay = day;
                yield return 0;
                if (i > 150 && _game.Zone.Streamer.Settled && !_game.Zone.Pois.MidPending) break;
            }
            foreach (int x in _drive.Wait(30)) yield return x;
            Snap(tag);
            GD.Print($"(strike {tag}: eye {eye}, settled {_game.Zone.Streamer.Settled})");
        }
        _game.FixedCamera = null;
        // The same crater on the World map, close in.
        Slice.Ui.MapLevels levels = _game.Menu.Root.Map.Levels;
        _game.Menu.OpenMap(Slice.Ui.MapLevel.World);
        for (int i = 0; i < 150; i++) yield return 0;
        if (levels.View != null)
        {
            levels.View.CentreX = (strike.X + site.Center.X) / 2;
            levels.View.CentreZ = (strike.Z + site.Center.Z) / 2;
            levels.View.MetresPerPixel = Num("mpp", 2.5);
            levels.View.Settle();
            for (int i = 0; i < 40; i++) yield return 0;
            Snap("map");
        }
    }
}
