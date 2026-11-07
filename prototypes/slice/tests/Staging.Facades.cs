using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Buildings as they read from outside: each family asked for that this world has, its most built-up
/// part, from about 60 m at a person's eye height and again from close by. Saves its own pictures.
///   --view=facades --families=survivor_town,freeholder_farm (or all) --max=8 --dir=FOLDER --tag=before
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> FacadeView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.5);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        string dir = _args.GetValueOrDefault("dir", "."), tag = _args.GetValueOrDefault("tag", "shot");
        float far = (float)Num("far", 60), near = (float)Num("near", 16);
        var planet = _game.Zone.Planet;
        Vector3 from = _game.Survivor.WorldFeet;
        GD.Print($"families here: {string.Join(" ", planet.Pois.Select(s => s.Family.Id).Distinct().OrderBy(s => s))}");
        string wanted = _args.GetValueOrDefault("families", "all");
        IEnumerable<PoiSite> pick = wanted == "all"
            ? planet.Pois.GroupBy(s => s.Family.Id).Select(g => g.OrderBy(s => _game.Places.DistanceTo(s, from)).First())
            : wanted.Split(',').Select(f => planet.Pois.Where(s => s.Family.Id == f).OrderBy(s => _game.Places.DistanceTo(s, from)).FirstOrDefault()).Where(s => s != null);
        foreach (PoiSite site in pick.Take((int)Num("max", 8)).ToList())
        {
            foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
            Place p = _game.Places.PlaceOf(site);
            var cells = p?.Inst.Blocks.Blocks().Where(b => b.Block.Role is Role.Wall or Role.Window && b.Cell.Y >= 0).Select(b => b.Cell).ToList();
            if (cells == null || cells.Count == 0) { GD.Print($"{site.Family.Id}: nothing above ground"); continue; }
            var heart = cells.GroupBy(c => (c.X >> 4, c.Z >> 4)).OrderByDescending(g => g.Count()).First().ToList();
            Int3 mid = new((int)heart.Average(c => c.X), (int)heart.Average(c => c.Y), (int)heart.Average(c => c.Z));
            Vector3 target = p.WorldOf(mid);
            GD.Print($"{site.Family.Id}: {site.Instance.Kit.Id} kit, {site.Instance.State}, {heart.Count} wall cells round {mid}");
            Int3 f = site.Instance.Layout.EntranceFacing.Vec();
            Vector3 way = Vector3.Forward, wall = target;
            int most = -1;
            // Walk round it, the sunny side first and the entrance if it can be, until a place to stand sees the wall: trees only collide near you.
            Vector3 sun = _game.Zone.Sky.ToSun;
            float Bearing(int a) => MathF.Atan2(f.Z, f.X) + a * MathF.Tau / 8f;
            foreach (int a in Enumerable.Range(0, 8).OrderByDescending(a => new Vector3(MathF.Cos(Bearing(a)), 0f, MathF.Sin(Bearing(a))).Dot(sun) + (a == 0 ? 0.3f : 0f)))
            {
                if (most >= 9) break;
                var (w, at) = WallFrom(target, Bearing(a));
                Vector3 eye = StandOff(at, w, far);
                _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.5f).ToNumerics());
                foreach (int x in _drive.Wait(45)) yield return x;
                int clear = Sight(StandOff(at, w, far), at, w);
                if (clear > most) { most = clear; way = w; wall = at; }
            }
            foreach (float dist in new[] { far, near })
            {
                Vector3 eye = StandOff(wall, way, dist);
                _game.Survivor.PlaceOutside((eye + way * 4f + Vector3.Down * 1.5f).ToNumerics()); // out of the shot
                foreach (int x in _drive.Wait(45)) yield return x;
                eye = StandOff(wall, way, dist); // the ground there has come in by now
                Vector3 aim = wall with { Y = eye.Y + 1.5f + dist * 0.04f };
                _game.FixedCamera = new Transform3D(Basis.LookingAt(aim - eye, Vector3.Up), eye);
                foreach (int x in _drive.Wait(90)) yield return x;
                string file = $"{dir}/{tag}-{site.Family.Id}-{(dist == far ? "far" : "near")}.png";
                GD.Print(_game.GetViewport().GetTexture().GetImage().SavePng(file) == Error.Ok ? $"saved {file}" : $"could not save {file}");
            }
            _game.FixedCamera = null;
        }
    }

    Vector3 StandOff(Vector3 wall, Vector3 way, float dist)
    {
        Vector3 eye = wall + way * dist;
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f;
        return eye;
    }

    /// <summary>The way out from a target on a bearing, and the wall it meets: distances run from the wall, whatever size the place is.</summary>
    (Vector3 Way, Vector3 Wall) WallFrom(Vector3 target, float bearing)
    {
        Vector3 way = new(MathF.Cos(bearing), 0f, MathF.Sin(bearing));
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        Vector3 wall = target; // the outermost of a few heights, so a doorway or a hole doesn't count as the wall
        foreach (float up in new[] { 1f, 3f, 5f, 7f })
        {
            var face = space.IntersectRay(PhysicsRayQueryParameters3D.Create(target + way * 200f + Vector3.Up * up, target + Vector3.Up * up, GalHack.Physics.Layers.World));
            if (face.Count > 0 && ((Vector3)face["position"]).DistanceTo(target) < 60f && ((Vector3)face["position"] - wall).Dot(way) > 0f) wall = (Vector3)face["position"];
        }
        return (way, wall);
    }

    /// <summary>How many of ten points along a wall an eye can see.</summary>
    int Sight(Vector3 eye, Vector3 wall, Vector3 way)
    {
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        int clear = 0;
        for (int side = -2; side <= 2; side++)
            foreach (float up in new[] { 0.5f, 3f })
            {
                Vector3 to = wall + new Vector3(-way.Z, 0f, way.X) * (side * 4f) + Vector3.Up * up;
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, to, 0xffffffff));
                if (hit.Count == 0 || ((Vector3)hit["position"]).DistanceTo(to) < 6f) clear++;
            }
        return clear;
    }
}
