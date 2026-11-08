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

    /// <summary>
    /// A war's craters (--view=craters): --pick=gz a nuclear ground zero from the air with the ring of tower
    /// stumps round it (start with --fall nuclear_war), --pick=field a bombardment's crater field from the air
    /// (--fall orbital_bombardment), --pick=grown an old shell-hole field from the ground among its holes
    /// (--fall invasion --layer age.long). The world's own terrain and weather; what was picked is printed.
    /// </summary>
    IEnumerable<int> CraterViews()
    {
        _game.Visor.Visible = false;
        var world = _game.Surface.Planet;
        string pick = _args.GetValueOrDefault("pick", "gz");
        double W = world.Width, H = world.Height;
        double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, W), 2) + Math.Pow(Mathx.WrapDelta(az - bz, H), 2));
        Vector3 start = _game.Survivor.WorldFeet;
        Strike centre;
        float up, back;
        if (pick == "gz")
        {
            // A ground zero in the city's grid first, then the one with the most of a town standing round it, nearest the start.
            var city = world.Infra.City;
            bool InCity(Strike b) => city != null && b.Within && Dist(b.X, b.Z, city.CenterX, city.CenterZ) < city.Radius + b.Crater;
            centre = world.Blasts.Where(b => b.Bowl && b.Flatten)
                .OrderByDescending(b => (InCity(b) ? 100000 : 0) + (b.Within ? 1000 : 0) + world.Pois.Count(p => Dist(p.Center.X, p.Center.Z, b.X, b.Z) < b.Crater + 600))
                .ThenBy(b => Dist(b.X, b.Z, Mathx.WrapX(start.X, W), Mathx.Wrap(start.Z, H))).FirstOrDefault();
            // At the rim, 120 m up, looking across ground zero to the city beyond.
            up = (float)Num("up", 120);
            back = (float)Num("back", centre == null ? 0 : centre.Crater * 1.05);
            if (centre != null) GD.Print($"(craters gz: in the city's grid {InCity(centre)}, the city {(city == null ? "none" : $"{Dist(city.CenterX, city.CenterZ, centre.X, centre.Z):F0} m off, radius {city.Radius:F0}")})");
        }
        else
        {
            // The thickest stretch of the field: the hole with the most others round it.
            var field = world.Blasts.Where(b => b.Field && b.Bowl).ToList();
            // (from the ground, the most hole: their bowls' area round it)
            centre = pick == "field" ? field.OrderByDescending(b => field.Count(o => Dist(o.X, o.Z, b.X, b.Z) < 90)).FirstOrDefault()
                : field.OrderByDescending(b => field.Where(o => Dist(o.X, o.Z, b.X, b.Z) < 50).Sum(o => o.Crater * o.Crater)).FirstOrDefault();
            up = (float)Num("up", pick == "field" ? 120 : 2.4);
            back = (float)Num("back", pick == "field" ? 150 : 20);
        }
        if (centre == null)
        {
            GD.Print($"(craters: nothing to show for {pick} on this world)");
            yield break;
        }
        foreach (Strike b in world.Blasts.Where(b => b.Within || b.Target is "city" or "capital" or "town" || b.Bowl && Dist(b.X, b.Z, centre.X, centre.Z) < 1500 && !b.Field)) GD.Print($"(craters: {b}{(b.Within ? ", in the town it hit" : "")})");
        int round = world.Blasts.Count(b => b.Bowl && Dist(b.X, b.Z, centre.X, centre.Z) < 160);
        GD.Print($"(craters {pick}: {centre}, {round} craters within 160 m, {world.Blasts.Count(b => b.Bowl)} on the world, age {world.Reading.Age:F2}, {world.Pois.Count(p => Dist(p.Center.X, p.Center.Z, centre.X, centre.Z) < centre.Crater + 700)} places within {centre.Crater + 700:F0} m)");
        Vector3 gz = world.NearestCopy(new Vector3((float)centre.X, 0f, (float)centre.Z), start);
        gz.Y = world.GroundHeight(Mathx.WrapX(gz.X, W), Mathx.Wrap(gz.Z, H));
        // From the side the sun lights, so the bowls' far walls catch it; a ground zero from over its ring of ruins, toward the crater.
        Vector3 toSun = new Vector3(_game.Zone.Sky.ToSun.X, 0f, _game.Zone.Sky.ToSun.Z);
        Vector3 dir = toSun.LengthSquared() > 1e-4f ? -toSun.Normalized() : Vector3.Forward;
        var ring = world.Pois.Where(p => Dist(p.Center.X, p.Center.Z, centre.X, centre.Z) < centre.Crater + 900).ToList();
        if (pick == "gz" && ring.Count > 0)
        {
            double sx = ring.Average(p => Mathx.WrapDelta(p.Center.X - centre.X, W)), sz = ring.Average(p => Mathx.WrapDelta(p.Center.Z - centre.Z, H));
            if (sx * sx + sz * sz > 1) dir = new Vector3((float)sx, 0f, (float)sz).Normalized();
            if (world.Infra.City is { } cp && Dist(cp.CenterX, cp.CenterZ, centre.X, centre.Z) > 30)
                dir = new Vector3((float)Mathx.WrapDelta(cp.CenterX - centre.X, W), 0f, (float)Mathx.WrapDelta(cp.CenterZ - centre.Z, H)).Normalized();
            foreach (var t in world.Source.Stamps.Where(t => t.Shape != GalHack.Terrain.StampShape.Bowl && Dist(t.CenterX, t.CenterZ, centre.X, centre.Z) < centre.Crater * 1.5)) GD.Print($"(craters: a stamp left in the bowl: {t.Name} at {t.CenterX:F0},{t.CenterZ:F0})");
        }
        Vector3 feet = gz - dir * back;
        feet.Y = world.GroundHeight(Mathx.WrapX(feet.X, W), Mathx.Wrap(feet.Z, H)) + 0.5f;
        _game.Survivor.PlaceOutside(feet.ToNumerics());
        Vector3 eye = feet + Vector3.Up * up;
        // From the ground, over the nearest hole to the ones beyond it.
        Vector3 look = pick == "grown" ? gz + dir * 14f : pick == "gz" ? gz + dir * (centre.Crater + 380f) : gz;
        double day = _game.Zone.Sky.TimeOfDay;
        for (int i = 0; i < 2400; i++)
        {
            _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
            if (i % 120 == 2) day = LocalDay() + (pick == "gz" ? Num("shift", -0.07) : 0); // a ground zero under a lower sun, so the bowl's walls show
            _game.Zone.Sky.TimeOfDay = day;
            yield return 0;
            if (i > 200 && _game.Zone.Streamer.Settled && !_game.Zone.Pois.MidPending) break;
        }
        foreach (int x in _drive.Wait(60)) yield return x;
        Snap(pick);
        if (pick == "gz")
        {
            float At(double d) => world.GroundHeight(Mathx.WrapX(gz.X + dir.X * d, W), Mathx.Wrap(gz.Z + dir.Z * d, H));
            GD.Print($"(craters gz: ground at zero {At(0):F1}, half way {At(centre.Crater * 0.5):F1}, rim {At(centre.Crater):F1}, 1.3 radii {At(centre.Crater * 1.3):F1}, 2 radii {At(centre.Crater * 2):F1}; depth {centre.Depth:F1}, rim {centre.Rim:F1})");
        }
        GD.Print($"(craters {pick}: eye {eye}, settled {_game.Zone.Streamer.Settled}, weather {_game.Zone.Weather.Sim.Current})");
        _game.FixedCamera = null;
    }
}