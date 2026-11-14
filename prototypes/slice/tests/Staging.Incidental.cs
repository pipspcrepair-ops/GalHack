using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Physics.Engine;
using PoiGenerator.Gen;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

/// <summary>
/// A place nobody marked, in the game itself (--view=incidental --what=sewer|ruin): the eye standing height (1.7 m) a few metres
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
            var ruin = world.Infra.Ruins.FirstOrDefault(r => r.Stamp != null && world.Infra.Town?.Houses.Contains(r) != true);
            if (ruin != null)
            {
                var (rx, rz) = ruin.Center;
                var c = new Vector3((float)rx, 0f, (float)rz);
                c.Y = world.GroundHeight(Planets.Gen.Mathx.WrapX(c.X, world.Width), Planets.Gen.Mathx.Wrap(c.Z, world.Height)) + 3f;
                spot = (c, new Vector3(1f, 0f, 0.4f).Normalized());
            }
        }
        if (spot is not { } s) { GD.Print($"(incidental: no {what} on this world)"); yield break; }
        foreach (var (name, back) in new[] { ("near", what == "sewer" ? 6f : 14f), ("far", 30f) })
        {
            Vector3 centre = world.NearestCopy(s.target, _game.Survivor.WorldFeet);
            Vector3 feet = centre + s.dir * back - (what == "sewer" ? Vector3.Zero : Vector3.Zero);
            if (what == "ruin") feet.Y = world.GroundHeight(Planets.Gen.Mathx.WrapX(feet.X, world.Width), Planets.Gen.Mathx.Wrap(feet.Z, world.Height));
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
            Snap($"{what}-{name}");
            lamp?.QueueFree();
        }
        _game.FixedCamera = null;
    }
}
