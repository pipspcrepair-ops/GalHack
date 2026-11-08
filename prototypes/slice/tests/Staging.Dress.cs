using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Physics.Engine;
using Planets.Gen;
using PlanetSurface.Infra;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// A world dressed by its layers, seen (--view=dress, with --theme and the eras on the command line as a
/// new game takes them): the start town's most dressed stretch of street at eye height, looking along it,
/// then the same stretch from about 30 m up and back, by day. Prints the world's roll and what its
/// dressing came to. Frames go to --dir as --tag-street.png and --tag-30m.png.
/// </summary>
public sealed partial class Staging
{
    /// <summary>The street furniture every settled world has; the rest is the world's own story.</summary>
    static readonly HashSet<string> BaseDress = new() { "lamp", "awning", "shop_sign", "sign_post" };

    IEnumerable<int> DressView()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        PrintLayers();
        var world = _game.Zone.Planet;
        WorldDress d = world.Dress;
        GD.Print($"DRESS {d.Items.Count} pieces, era {d.Before} (old) / {d.After} (now), emblem {d.Emblem}, planned in {d.PlanMs:F0} ms");
        GD.Print($"DRESS parts: {string.Join(" ", d.Items.GroupBy(it => it.Part.Id).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}:{g.Count()}"))}");
        string dir = _args.GetValueOrDefault("dir", "."), tag = _args.GetValueOrDefault("tag", "dress");
        Vector3 from = _game.Survivor.WorldFeet;
        Vector3 Near(double x, double z) => world.NearestCopy(new Vector3((float)x, 0f, (float)z), from);
        var pieces = d.Items.Select(it => (It: it, At: Near(it.X, it.Z))).Where(p => ((p.At - from) with { Y = 0f }).Length() < 4200f).ToList();
        var housed = new HashSet<InfraRoute>(world.Infra.Ruins.Where(f => f.Route != null && f.Family.Tags.Contains("house")).Select(f => f.Route));
        var streets = world.Infra.Routes.Where(r => r.Kind != RouteKind.Rail && (housed.Contains(r) || world.StartTown?.Streets.Contains(r) == true)).ToList();
        // A world with no town's street in reach: its roads, whatever lies along them.
        bool InReach(InfraRoute r) => Enumerable.Range(0, r.Count).Any(k => ((Near(r.X[k], r.Z[k]) - from) with { Y = 0f }).Length() < 4000f);
        if (!streets.Any(InReach)) streets = world.Infra.Routes.Where(r => r.Kind != RouteKind.Rail).ToList();
        // The stretch of street with the most of the world's own story in view ahead: solid pieces count double.
        float bestScore = -1f;
        Vector3 stand = from, way = Vector3.Forward;
        foreach (InfraRoute r in streets)
            for (int k = 2; k + 2 < r.Count; k += 2)
            {
                Vector3 p = Near(r.X[k], r.Z[k]);
                // A town's street near the start for choice; a world without one there, the nearest town's.
                if (((p - from) with { Y = 0f }).Length() > 4000f) continue;
                Vector3 along = (Near(r.X[k + 2], r.Z[k + 2]) - Near(r.X[k - 2], r.Z[k - 2])) with { Y = 0f };
                if (along.LengthSquared() < 1e-3f) continue;
                along = along.Normalized();
                foreach (float s in new[] { 1f, -1f })
                {
                    Vector3 fwd = along * s;
                    float score = 0f;
                    var kinds = new HashSet<string>();
                    foreach (var (it, at) in pieces)
                    {
                        Vector3 off = (at - p) with { Y = 0f };
                        float ahead = off.Dot(fwd), side = MathF.Abs(off.Dot(new Vector3(-fwd.Z, 0f, fwd.X)));
                        if (ahead < 4f || ahead > 50f || side > 4f + ahead * 0.45f) continue;
                        bool own = !BaseDress.Contains(it.Part.Id);
                        // The world's strongest leanings count most: its own story, not what else happens to lie about.
                        float lean = MathF.Min(2.5f, d.Old.GetValueOrDefault(it.Part.Id) + d.Now.GetValueOrDefault(it.Part.Id));
                        score += (own ? 0.3f + lean : 0.2f) * (it.Part.Solid ? 2f : 1f) / (1f + ahead / 25f);
                        if (own) kinds.Add(it.Part.Id);
                    }
                    score *= (1f + 0.15f * kinds.Count) / (1f + ((p - from) with { Y = 0f }).Length() / 3000f);
                    if (score > bestScore) { bestScore = score; stand = p; way = fwd; }
                }
            }
        stand.Y = _game.Zone.GroundAt(stand.X, stand.Z).Y;
        var seen = pieces.Where(p => ((p.At - stand) with { Y = 0f }).Dot(way) is > 2f and < 55f && ((p.At - stand) with { Y = 0f }).Length() < 60f).GroupBy(p => p.It.Part.Id).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}:{g.Count()}");
        GD.Print($"DRESS street at {stand}, looking {way}, score {bestScore:F1}: {string.Join(" ", seen)}");

        // At eye height in the street, then from 30 m up and back along it.
        var shots = new (string Name, Vector3 Eye, Vector3 Look)[]
        {
            ("street", stand + Vector3.Up * 1.7f - way * 2f, stand + way * 26f + Vector3.Up * 1.2f),
            ("30m", stand - way * 18f + Vector3.Up * 30f, stand + way * 16f + Vector3.Up * 0.5f),
        };
        foreach (var (name, eye0, look) in shots)
        {
            Vector3 eye = eye0;
            if (name == "street") eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f;
            // The castaway out of the shot, behind the eye: the ground and tiles round there come in.
            Vector3 feet = stand - way * (name == "street" ? 7f : 26f);
            feet.Y = _game.Zone.GroundAt(feet.X, feet.Z).Y + 0.5f;
            _game.Survivor.PlaceOutside(feet.ToNumerics());
            double day = LocalDay();
            for (int i = 0; i < 1500; i++)
            {
                _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
                if (i % 120 == 2) day = LocalDay();
                _game.Zone.Sky.TimeOfDay = day;
                yield return 0;
                if (i > 240 && _game.Zone.Streamer.Settled && !_game.Zone.Pois.MidPending && _game.Zone.Infra.NearestBehind > 400) break;
            }
            foreach (int x in _drive.Wait(30)) yield return x;
            string file = $"{dir}/{tag}-{name}.png";
            GD.Print(_game.GetViewport().GetTexture().GetImage().SavePng(file) == Error.Ok ? $"saved {file}" : $"could not save {file}");
        }
    }
}
