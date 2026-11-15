using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Physics.Engine;
using PoiGenerator.Gen;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

/// <summary>
/// A place nobody marked, in the game itself (--view=incidental --what=sewer|ruin|place, --nth=N --count=K for K ruins or points of interest
/// from the Nth; every view audits what is drawn round it, --audit=300 m, and the run fails on anything unpainted or floating): the eye standing height (1.7 m) a few metres
/// off it and again 30 m off, for the sewer with a lamp over the junction it looks at (it is dark down there), for the nearest
/// roadside ruin in daylight. --rounded=0 builds the underground as it was before the sections were shaped. Frames go beside --out.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> IncidentalView()
    {
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Visor.Visible = false;
        var world = _game.Surface.Planet;
        string what = _args.GetValueOrDefault("what", "sewer");
        (Vector3 target, Vector3 dir)? spot = null;
        var spots = new List<(Vector3 target, Vector3 dir)>();
        if (what == "sewer" && world.StartTown?.Under?.Built is { } built)
        {
            // The first junction chamber on a long enough arm: look at it down the bore that leads there.
            foreach (UnderRoom room in built.Plan.Rooms.Where(r => r.Kind == UnderSpaceKind.Junction && r.Network == UnderKind.Sewer))
            foreach (UnderRun run in built.Plan.Runs.Where(r => r.Kind == UnderKind.Sewer && r.Length > 50))
            {
                int end = run.Length - 1;
                for (int i = 0; i < run.Length && spot == null; i++)
                {
                    var (x, z) = run.Column(i);
                    if (x < room.Min.X - 1 || x > room.Max.X + 1 || z < room.Min.Z - 1 || z > room.Max.Z + 1) continue;
                    int away = i > end / 2 ? -1 : 1;
                    var (ax, az) = run.Column(Math.Clamp(i + away * 36, 0, end));
                    spot = (new Vector3(x + 0.5f, run.Floors[i] + 1f, z + 0.5f), new Vector3(ax - x, 0f, az - z).Normalized());
                }
                if (spot != null) break;
            }
        }
        else if (what == "ruin")
        {
            foreach (var ruin in world.Infra.Ruins.Where(r => r.Stamp != null && world.Infra.Town?.Houses.Contains(r) != true).Skip(int.Parse(_args.GetValueOrDefault("nth", "0"))).Take(int.Parse(_args.GetValueOrDefault("count", "1"))))
            {
                var (rx, rz) = ruin.Center;
                var c = new Vector3((float)rx, 0f, (float)rz);
                c.Y = world.GroundHeight(Planets.Gen.Mathx.WrapX(c.X, world.Width), Planets.Gen.Mathx.Wrap(c.Z, world.Height)) + 3f;
                spots.Add((c, new Vector3(1f, 0f, 0.4f).Normalized()));
            }
        }
        else if (what == "place")
        {
            // Points of interest about the world, out to a lone works or wreck: each a few metres off, as for a ruin.
            foreach (var site in world.Pois.Where(s => s.Siting != "city").Skip(int.Parse(_args.GetValueOrDefault("nth", "0"))).Take(int.Parse(_args.GetValueOrDefault("count", "1"))))
            {
                var (px, pz) = site.Center;
                var c = new Vector3((float)px, 0f, (float)pz);
                c.Y = world.GroundHeight(Planets.Gen.Mathx.WrapX(c.X, world.Width), Planets.Gen.Mathx.Wrap(c.Z, world.Height)) + 3f;
                spots.Add((c, new Vector3(1f, 0f, 0.4f).Normalized()));
            }
        }
        if (spot != null) spots.Add(spot.Value);
        if (spots.Count == 0) { GD.Print($"(incidental: no {what} on this world)"); yield break; }
        int shot = 0;
        foreach (var s in spots)
        {
        shot++;
        foreach (var (name, back) in new[] { ("near", what == "sewer" ? 6f : 14f), ("far", what == "place" ? 90f : 30f) })
        {
            Vector3 centre = world.NearestCopy(s.target, _game.Survivor.WorldFeet);
            Vector3 feet = centre + s.dir * back - (what == "sewer" ? Vector3.Zero : Vector3.Zero);
            if (what != "sewer") feet.Y = world.GroundHeight(Planets.Gen.Mathx.WrapX(feet.X, world.Width), Planets.Gen.Mathx.Wrap(feet.Z, world.Height));
            else feet.Y = centre.Y - 1f;
            Vector3 eye = feet + Vector3.Up * 1.7f;
            Vector3 look = centre + (what == "sewer" ? Vector3.Up * 0.6f : Vector3.Up * 2.5f);
            _game.Survivor.PlaceOutside(feet.ToNumerics());
            OmniLight3D lamp = null;
            if (what == "sewer")
            {
                lamp = new OmniLight3D { Name = "ProofLamp", LightColor = new Color(1f, 0.85f, 0.65f), LightEnergy = 3f, OmniRange = 16f, ShadowEnabled = true };
                _game.Zone.AddChild(lamp);
                lamp.GlobalPosition = eye - s.dir * 0.5f + Vector3.Up * 0.8f;
            }
            double day = _game.Zone.Sky.TimeOfDay;
            for (int i = 0; i < 900; i++)
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
                if (i % 120 == 2) day = LocalDay();
                _game.Zone.Sky.TimeOfDay = day;
                yield return 0;
                if (i > 200 && _game.Zone.Streamer.Settled && !_game.Zone.Pois.MidPending) break;
            }
            // Anything round it drawn without paint, or standing off the ground with nothing under it (a stand-in block, a flat face, a stray piece).
            var faults = Unpainted.Near(_game, eye, (float)Num("audit", 300), (x, z) => _game.Zone.GroundAt(x, z).Y);
            Faults += faults.Count;
            Unpainted.Print($"world seed {_game.World.Seed}, {what} {shot} {name}, eye {eye.Round()}", faults);
            Snap($"{what}{(spots.Count > 1 ? shot.ToString() : "")}-{name}");
            lamp?.QueueFree();
        }
        }
        _game.FixedCamera = null;
        GD.Print($"  {(Faults == 0 ? "ok   " : "FAIL ")} nothing unpainted or floating round {spots.Count} {what}(s) on seed {_game.World.Seed}: {Faults} found");
    }

    /// <summary>Unpainted or floating pieces the views found: a run with any fails.</summary>
    public int Faults;
}
