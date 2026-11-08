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
/// Layers everywhere, seen: what a world's roll made of it, in daylight. layers-land stands off the
/// nearest place to the landing with its land round it; layers-mark stands in the room a rare
/// occupant marked (--force-family and --force-occupant on the command line put one near the landing).
/// Both print the world's roll and what the world read off it.
/// </summary>
public sealed partial class Staging
{
    void PrintLayers()
    {
        var planet = _game.Zone.Planet;
        GD.Print($"LAYERS world {_game.World}: {planet.Layers}");
        GD.Print($"LAYERS reading {planet.Reading}");
        if (planet.Reading.Paint is { } p) GD.Print($"LAYERS paint main {p.Main} accent {p.Accent} roof {p.Roof} cloth {p.Cloth} light {p.Light} fade {p.Fade:F2} dirt {p.Dirt:F2}");
        GD.Print($"LAYERS flora alien {planet.Flora.Book.Climate.Alien:F2}, {planet.Flora.Book.Plants.Count} species: {string.Join(", ", planet.Flora.Book.Plants.GroupBy(x => x.Family).Select(g => $"{g.Key} {g.Count()}"))}");
        GD.Print($"LAYERS wildlife: {string.Join(", ", _game.Zone.Wildlife.Life.Moving.Select(s => s.CommonName))}");
    }

    /// <summary>The nearest lived-in or ruined place to the landing, from 40 m off its door and a little above, at noon.</summary>
    IEnumerable<int> LayersLand()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.45);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        PrintLayers();
        Vector3 from = _game.Survivor.WorldFeet;
        PoiSite site = _args.ContainsKey("family") ? Named()
            : _game.Zone.Planet.Pois.Where(s => s != _game.Bunker.Poi && s.Instance.Blocks.Size.X > 16).OrderBy(s => _game.Places.DistanceTo(s, from)).FirstOrDefault() ?? _game.Bunker.Poi;
        GD.Print($"LAYERS looking at {site.Family.Id} ({site.Instance.State})");
        Vector3 gate = Gate(site);
        Int3 f = site.Instance.Layout.EntranceFacing.Vec();
        Vector3 stand = gate + new Vector3(f.X, 0f, f.Z) * (float)Num("back", 40);
        stand.Y = _game.Zone.GroundAt(stand.X, stand.Z).Y + 0.2f;
        foreach (int x in GoTo(site, stand)) yield return x;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.45);
        Vector3 eye = stand + Vector3.Up * (float)Num("up", 16);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(gate + Vector3.Up * 4f - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(90)) yield return x;
    }

    /// <summary>Inside the place a rare occupant marked: a few metres from the mark, looking at it, by day with the torch on.</summary>
    IEnumerable<int> LayersMark()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.45);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        PrintLayers();
        string family = _args.GetValueOrDefault("force-family", _args.GetValueOrDefault("family", ""));
        Vector3 from = _game.Survivor.WorldFeet;
        PoiSite site = _game.Zone.Planet.Pois.Where(s => s.Family.Id == family && s.Instance.Markers.Any(IsMark)).OrderBy(s => _game.Places.DistanceTo(s, from)).FirstOrDefault();
        if (site == null)
        {
            GD.Print($"LAYERS no {family} with a mark on this world");
            yield break;
        }
        foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
        Place p = _game.Places.PlaceOf(site);
        Marker mark = p.Inst.Markers.First(IsMark);
        GD.Print($"LAYERS mark {mark.Kind} {mark.Note} at {mark.Cell} in {site.Family.Id}, occupancy {p.Inst.Occupancy}");
        var at = new Vector3(mark.Cell.X + 0.5f, mark.Cell.Y + 0.5f, mark.Cell.Z + 0.5f);
        // Where to stand: a floor cell on the mark's own level, 4 to 9 m off, with nothing between the eye and the mark.
        Vector3 target = p.ToWorld(at + Vector3.Up * (mark.Kind == "wrong_object" ? 0.2f : 0.4f));
        float lift = mark.Kind == "wrong_object" ? 1.2f : 2.2f;
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        NavGrid nav = p.Nav();
        bool Clear(Vector3 eye) => space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, target + (eye - target).Normalized() * 0.8f, GalHack.Physics.Layers.World)).Count == 0;
        Vector3? best = null;
        float bestScore = float.MaxValue;
        for (int dz = -9; dz <= 9; dz++)
            for (int dx = -9; dx <= 9; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var c = new Int3(mark.Cell.X + dx, mark.Cell.Y + dy, mark.Cell.Z + dz);
                    float d = new Vector2(dx, dz).Length();
                    if (d < 4f || d > 9f || !nav.Standable(c)) continue;
                    Vector3 feetAt = PlaceWalk.Feet(p, c), eyeAt = feetAt + Vector3.Up * lift;
                    float score = MathF.Abs(d - 6f) + (Clear(eyeAt) ? 0f : 100f);
                    if (score < bestScore) { bestScore = score; best = feetAt; }
                }
        Vector3 stand = best ?? p.ToWorld(at + new Vector3(5f, 0f, 0f));
        GD.Print($"LAYERS standing {(best == null ? "nowhere good" : bestScore < 100f ? "in clear view" : "with something in the way")}");
        Vector3 eye = stand + Vector3.Up * lift;
        // The castaway stands a little behind the camera, so the view isn't through their own head.
        Vector3 back = (eye - target) with { Y = 0f };
        _game.Survivor.PlaceOutside((stand + back.Normalized() * 1.2f).ToNumerics());
        foreach (int x in _drive.Wait(40)) yield return x;
        _game.Torch.Visible = false;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.45);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(target - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(90)) yield return x;
    }

    static readonly string[] RareMarks = { "keeper_post", "sleepers", "guardian_ring", "wrong_object", "copies", "warped" };

    bool IsMark(Marker m) => _args.TryGetValue("mark", out string want) ? m.Kind == want : RareMarks.Contains(m.Kind);
}
