using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Places;

namespace Slice.Tests;

/// <summary>
/// Weapons on the world (--view=shotdamage): the nearest place's outside wall holed by rifle rounds and rockets (an NPC's), and a
/// rocket's crater in open ground beside it, each seen from a few metres off (the wall before the shots too). --flavour=1: the
/// nearest roadside ruin's wall (a house, a farm, a shed, a fuel stop) instead.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> ShotDamage()
    {
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Visor.Visible = false;
        var rng = new Random();
        Vector3 from = _game.Survivor.WorldFeet;
        var site = (_args.ContainsKey("flavour") ? _game.Zone.Planet.Sites.Where(PlanetSurface.Infra.FlavourRuin.IsFlavour) : _game.Zone.Planet.Pois.AsEnumerable()).Where(s => s.Instance != null && !s.Family.Tags.Contains("underground") && (!_args.TryGetValue("tag", out string tag) || s.Family.Tags.Contains(tag)))
            .OrderBy(s => _game.Zone.Planet.NearestCopy(new Vector3(s.Entrance.X, s.Entrance.Y, s.Entrance.Z), from).DistanceTo(from)).FirstOrDefault();
        if (site == null) { GD.Print("(shotdamage: no place on this world)"); yield break; }
        Vector3 gate = _game.Zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), from);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(gate.X + 20f, gate.Z) + Vector3.Up).ToNumerics());
        for (int i = 0; i < 3600 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        Place place = _game.Places.PlaceOf(site);
        if (place == null || !WorldFire.Wall(place, rng, out Int3 cell, out Int3 o)) { GD.Print($"(shotdamage: {site.Name} has no outside wall to fire at)"); yield break; }
        Vector3 outward = new(o.X, o.Y, o.Z), face = place.WorldOf(cell) + outward * 0.5f;
        if (_args.ContainsKey("tag"))
        {
            // A kind of place asked for by name: its front, where you come at it (a fuel stop's forecourt and shop).
            var mid = _game.Zone.Planet.NearestCopy(new Vector3((site.Footprint.Min.X + site.Footprint.Max.X) * 0.5f, gate.Y, (site.Footprint.Min.Z + site.Footprint.Max.Z) * 0.5f), from);
            outward = new Vector3(gate.X - mid.X, 0f, gate.Z - mid.Z).Normalized();
            face = mid + Vector3.Up * 2f;
        }
        Vector3 across = outward.Cross(Vector3.Up);
        float off = _args.ContainsKey("tag") ? 3f : 1f; // (a whole place's front is seen and shot from further off than one wall)
        _game.Survivor.PlaceOutside((face + outward * 14f * off + across * 8f).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        Vector3 wallEye = face + (outward * 7f + across * 3f) * off + Vector3.Up * 0.8f * off;
        foreach (int x in View("wall-before", wallEye, face)) yield return x;
        GalHack.Arms.WeaponDef rifle = _game.Zone.Data.Arms.OfItem(Wildlife.Weapons.ItemOf("rifle")), rocket = WorldFire.Rocket(_game);
        for (int i = 0; i < 12; i++)
        {
            WorldFire.Fire(_game, rifle, face + outward * 6f * off + across * 0.3f, face + across * (i % 3 - 1) * 0.6f * off);
            foreach (int x in _drive.Wait(5)) yield return x;
        }
        for (int i = 0; i < 2; i++)
        {
            WorldFire.Fire(_game, rocket, face + outward * 7f * off, face + across * (i - 0.5f) * 2f * off + Vector3.Up * 0.5f * i);
            foreach (int x in _drive.Wait(50)) yield return x;
        }
        Vector3 spot = _game.Zone.GroundAt(face.X + outward.X * 24f + across.X * 10f, face.Z + outward.Z * 24f + across.Z * 10f);
        WorldFire.Fire(_game, rocket, spot + new Vector3(2f, 6f, 1f), spot);
        foreach (int x in _drive.Wait(60)) yield return x;
        GD.Print($"(shotdamage: {site.Name}, wall {place.Inst.Blocks.Get(cell).Material}; {_game.Places.BlocksBroken} blocks broken, {_game.Places.BlocksFallen} fallen; craters {_game.Arms.Craters})");
        foreach (var (tag, eye, look) in new[] { ("wall", wallEye, face), ("crater", spot + (outward + across * 0.5f).Normalized() * 6f + Vector3.Up * 3.5f, spot) })
            foreach (int x in View(tag, eye, look)) yield return x;
        _game.FixedCamera = null;

        IEnumerable<int> View(string tag, Vector3 eye, Vector3 look)
        {
            _game.Zone.Sky.TimeOfDay = HighSun(_game); // (by day: the setting-up can run into the night)
            for (int i = 0; i < 600; i++)
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
                yield return 0;
                if (i > 90 && _game.Zone.Streamer.Settled) break;
            }
            foreach (int x in _drive.Wait(20)) yield return x;
            Snap(tag);
        }
    }
}
