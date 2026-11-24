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
/// rocket's crater in open ground beside it, each seen from a few metres off.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> ShotDamage()
    {
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Visor.Visible = false;
        var rng = new Random();
        Vector3 from = _game.Survivor.WorldFeet;
        var site = _game.Zone.Planet.Pois.Where(s => s.Instance != null && !s.Family.Tags.Contains("underground"))
            .OrderBy(s => _game.Zone.Planet.NearestCopy(new Vector3(s.Entrance.X, s.Entrance.Y, s.Entrance.Z), from).DistanceTo(from)).FirstOrDefault();
        if (site == null) { GD.Print("(shotdamage: no place on this world)"); yield break; }
        Vector3 gate = _game.Zone.Planet.NearestCopy(new Vector3(site.Entrance.X + 0.5f, site.Entrance.Y, site.Entrance.Z + 0.5f), from);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(gate.X + 20f, gate.Z) + Vector3.Up).ToNumerics());
        for (int i = 0; i < 3600 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        Place place = _game.Places.PlaceOf(site);
        if (place == null || !WorldFire.Wall(place, rng, out Int3 cell, out Int3 o)) { GD.Print($"(shotdamage: {site.Name} has no outside wall to fire at)"); yield break; }
        Vector3 outward = new(o.X, o.Y, o.Z), face = place.WorldOf(cell) + outward * 0.5f, across = outward.Cross(Vector3.Up);
        _game.Survivor.PlaceOutside((face + outward * 14f + across * 8f).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        GalHack.Arms.WeaponDef rifle = _game.Zone.Data.Arms.OfItem(Wildlife.Weapons.ItemOf("rifle")), rocket = WorldFire.Rocket(_game);
        for (int i = 0; i < 12; i++)
        {
            WorldFire.Fire(_game, rifle, face + outward * 6f + across * 0.3f, face + across * (i % 3 - 1) * 0.6f);
            foreach (int x in _drive.Wait(5)) yield return x;
        }
        for (int i = 0; i < 2; i++)
        {
            WorldFire.Fire(_game, rocket, face + outward * 7f, face + Vector3.Up * 0.5f * i);
            foreach (int x in _drive.Wait(50)) yield return x;
        }
        Vector3 spot = _game.Zone.GroundAt(face.X + outward.X * 24f + across.X * 10f, face.Z + outward.Z * 24f + across.Z * 10f);
        WorldFire.Fire(_game, rocket, spot + new Vector3(2f, 6f, 1f), spot);
        foreach (int x in _drive.Wait(60)) yield return x;
        GD.Print($"(shotdamage: {site.Name}, wall {place.Inst.Blocks.Get(cell).Material}; {_game.Places.BlocksBroken} blocks broken, {_game.Places.BlocksFallen} fallen; craters {_game.Arms.Craters})");
        _game.Zone.Sky.TimeOfDay = HighSun(_game); // (by day: the setting-up can run into the night)
        foreach (var (tag, eye, look) in new[] { ("wall", face + outward * 7f + across * 3f + Vector3.Up * 0.8f, face), ("crater", spot + (outward + across * 0.5f).Normalized() * 6f + Vector3.Up * 3.5f, spot) })
        {
            for (int i = 0; i < 600; i++)
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
                yield return 0;
                if (i > 90 && _game.Zone.Streamer.Settled) break;
            }
            foreach (int x in _drive.Wait(20)) yield return x;
            Snap(tag);
        }
        _game.FixedCamera = null;
    }
}
