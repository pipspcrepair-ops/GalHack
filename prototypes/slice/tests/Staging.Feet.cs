using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Planets.Gen;
using Slice.Game;
using Slice.Places;
using Wildlife;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Feet on the ground (--view=feet): on a fresh world, the castaway set down by a few of its places (city lots
/// first, then towns and other lived-in places), and every person there watched for a while: the townsfolk of the
/// places standing and the world's own people out in the open (roamers, settlers by their homes). Each one's feet
/// are measured against what the physics world has under them (terrain, floor, stair): more than 0.3 m above it
/// (or below it) counts. Prints "FEET ok" or "FEET FAIL" with examples.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> FeetView()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        var world = _game.Surface.Planet;
        Vector3 start = _game.Survivor.WorldFeet;
        // Where to stand: city lots, then any place with people, nearest the start first.
        var spots = new List<(string what, Vector3 at)>();
        if (world.Infra?.City is { Sites.Count: > 0 } city)
            foreach (var s in city.Sites.OrderBy(s => _rngFeet.Next()).Take(3))
                spots.Add(("city " + s.Name, world.NearestCopy(new Vector3(s.Entrance.X, s.Entrance.Y, s.Entrance.Z), start)));
        foreach (var s in world.Pois.Where(s => s.Instance?.Layout != null && s.Siting != "city")
                     .OrderBy(s => (world.NearestCopy(new Vector3(s.Entrance.X, 0, s.Entrance.Z), start) - start).Length()).Take(6 - spots.Count))
            spots.Add((s.Name, world.NearestCopy(new Vector3(s.Entrance.X, s.Entrance.Y, s.Entrance.Z), start)));
        int samples = 0, off = 0, townsfolk = 0, roamers = 0;
        float worst = 0f;
        var examples = new List<string>();
        var seen = new HashSet<object>();
        foreach (var (what, at) in spots)
        {
            Vector3 feet = at + new Vector3(3f, 0f, 3f);
            feet.Y = world.GroundHeight(Mathx.WrapX(feet.X, world.Width), Mathx.Wrap(feet.Z, world.Height)) + 0.5f;
            _game.Survivor.PlaceOutside(feet.ToNumerics());
            for (int i = 0; i < 2400; i++)
            {
                yield return 0;
                if (i > 300 && _game.Zone.Streamer.Settled && !_game.Zone.Pois.MidPending) break;
            }
            for (int i = 0; i < 900; i++)
            {
                yield return 0;
                if (i % 15 != 0) continue;
                var space = _game.Zone.GetWorld3D().DirectSpaceState;
                void Measure(string kind, string who, Vector3 w, string more = "")
                {
                    if ((w - feet).Length() > 35f) return; // colliders stream in near the survivor only
                    var q = PhysicsRayQueryParameters3D.Create(w + Vector3.Up * 1.0f, w + Vector3.Down * 3f, Layers.World);
                    q.Exclude = WorldRay.Ignored;
                    var r = space.IntersectRay(q);
                    // (No collider under them yet, at the edge of the streamed ground: the land itself.)
                    float gap = r.Count > 0 ? w.Y - ((Vector3)r["position"]).Y : w.Y - world.GroundHeight(Mathx.WrapX(w.X, world.Width), Mathx.Wrap(w.Z, world.Height));
                    samples++;
                    if (MathF.Abs(gap) > MathF.Abs(worst)) worst = gap;
                    if (MathF.Abs(gap) <= 0.3f) return;
                    off++;
                    if (examples.Count < 8 && seen.Add(who)) examples.Add($"{kind} {who} at {w.X:F1},{w.Y:F2},{w.Z:F1}: {gap:+0.00;-0.00} m off, {(w - feet).Length():F0} m from you, land {world.GroundHeight(Mathx.WrapX(w.X, world.Width), Mathx.Wrap(w.Z, world.Height)):F2}{more} ({what})");
                }
                foreach (Place place in _game.Places.Standing)
                {
                    if (place.Memory.Town is not { } town || !place.Ready) continue;
                    foreach (Town.Person p in town.People)
                    {
                        if (p.Gone || p.Dead || p.Indoors || p.Away || p.Sitting) continue;
                        if (seen.Add(("t", p))) townsfolk++;
                        Measure("townsperson", $"{p.Job} of {place.Memory.Site.Name}", town.World(p.Pos), $", local {p.Pos.X:F1},{p.Pos.Y:F2},{p.Pos.Z:F1}, grid ground {(town.Ground(p.Pos) is float g ? g.ToString("F2") : "none")}, walking {p.Now > 0.1f}");
                    }
                }
                var wild = _game.Zone.Wildlife;
                foreach (Creature c in wild.Sim.Creatures)
                {
                    if (c.Removed || c.Dead || c.Species.Figure == null || c.Falling) continue;
                    if (seen.Add(("w", c))) roamers++;
                    Measure("roamer", c.Species.CommonName + "#" + c.Id, wild.WorldOf(c));
                }
            }
        }
        string line = $"{samples} samples of {townsfolk} townsfolk and {roamers} people out in the open by {spots.Count} places; {off} more than 0.3 m off what is under them (worst {worst:+0.00;-0.00} m)";
        GD.Print(off == 0 && samples > 50 ? $"FEET ok  {line}" : $"FEET FAIL  {line}{(examples.Count > 0 ? "; e.g. " + string.Join("; ", examples) : "")}");
    }

    readonly System.Random _rngFeet = new();
}
