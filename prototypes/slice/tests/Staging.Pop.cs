using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using PlanetSurface.Poi;

namespace Slice.Tests;

/// <summary>
/// Pop-in proof: a run toward a town (--target=town) or into a wood (--target=forest), by hover bike
/// (--route=bike, 38 m/s a metre and a half up) or low flight (--route=fly, 90 m/s at 70 m), on the
/// real clock, in daylight, a frame kept every tenth of a second (--out's name, numbered), so the
/// frames make a strip or a GIF of whatever comes into view on the way.
/// Run windowed: godot --path prototypes/slice res://tests/PopShots.tscn -- --view=popin --route=bike --target=town --out=DIR/bike-town.png
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> PopInView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Visor.Visible = false;
        bool fly = _args.GetValueOrDefault("route", "bike") == "fly";
        bool forest = _args.GetValueOrDefault("target", "town") == "forest";
        float speed = fly ? 90f : 38f, height = fly ? 70f : 3f, from = fly ? 1800f : 700f, to = fly ? -300f : 20f;
        var world = _game.Surface.Planet;
        var streamer = _game.Zone.Streamer;
        Vector3 at = _game.Survivor.WorldFeet;
        float Ground(Vector3 p) => MathF.Max(world.GroundHeight(Mathx.WrapX(p.X, world.Width), Mathx.Wrap(p.Z, world.Height)), 0f);
        var rng = new Random();
        Vector3 goal = at;
        if (forest)
        {
            // The thickest stand of trees the far rings hold round here.
            int best = -1;
            for (int i = 0; i < 96; i++)
            {
                float a = (float)(rng.NextDouble() * Math.Tau), r = 150f + (float)rng.NextDouble() * 300f;
                Vector3 p = at + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * r;
                int n = streamer.FloraNear(p, 40f).Count(s => Planets.Surface.FloraMeshes.IsLarge(s.Kind));
                if (n > best) (best, goal) = (n, p);
            }
            GD.Print($"(a wood of {best} trees within 40 m)");
        }
        else
        {
            var places = world.Pois.Where(s => s.Instance != null).ToList();
            PoiSite site = places.Where(s => places.Count(o => o != s && (o.Origin.X - s.Origin.X) * (o.Origin.X - s.Origin.X) + (o.Origin.Z - s.Origin.Z) * (o.Origin.Z - s.Origin.Z) < 400 * 400) >= 2)
                .OrderBy(s => _game.Places.DistanceTo(s, at)).FirstOrDefault() ?? places.OrderBy(s => _game.Places.DistanceTo(s, at)).FirstOrDefault();
            if (site != null)
            {
                var lo = site.Footprint.Min;
                var hi = site.Footprint.Max;
                goal = world.NearestCopy(new Vector3((lo.X + hi.X + 1) * 0.5f, 0f, (lo.Z + hi.Z + 1) * 0.5f), at);
                GD.Print($"(toward {site.Name}, {site.Siting})");
            }
        }
        float angle = (float)(rng.NextDouble() * Math.Tau);
        Vector3 way = new(MathF.Cos(angle), 0f, MathF.Sin(angle));
        Vector3 start = goal - way * from;
        void Put(Vector3 p)
        {
            float g = Ground(p);
            _game.Survivor.PlaceOutside(new Vector3(p.X, g + (fly ? height : 0.1f), p.Z).ToNumerics());
            Vector3 eye = new(p.X, g + height, p.Z);
            Vector3 look = new Vector3(goal.X, Ground(goal) + (forest ? 6f : 8f), goal.Z) - eye;
            if (new Vector2(look.X, look.Z).Length() < 80f) look = way * 80f + Vector3.Down * (fly ? 30f : 2f);
            _game.FixedCamera = new Transform3D(Basis.LookingAt(look, Vector3.Up), eye);
        }
        var settle = System.Diagnostics.Stopwatch.StartNew();
        while (settle.Elapsed.TotalSeconds < 40 && (settle.Elapsed.TotalSeconds < 4 || !streamer.Settled || _game.Places.Building != null || _game.Zone.Pois.MidPending))
        {
            Put(start);
            yield return 0;
        }
        string o = _args.GetValueOrDefault("out", "popin.png");
        string stem = System.IO.Path.ChangeExtension(o, null);
        float gone = 0f, length = from - to;
        double last = Time.GetTicksMsec() / 1000.0, shot = 0;
        int frame = 0;
        while (gone < length)
        {
            double now = Time.GetTicksMsec() / 1000.0;
            gone = MathF.Min(length, gone + speed * (float)Math.Min(now - last, 0.1));
            last = now;
            Put(start + way * gone);
            yield return 0;
            if (now - shot < 0.1) continue;
            shot = now;
            Image image = _game.GetViewport().GetTexture().GetImage();
            image.Resize(640, 360, Image.Interpolation.Bilinear);
            image.SavePng($"{stem}-{frame++:000}.png");
        }
        GD.Print($"(popin: {frame} frames, {length:F0} m at {speed:F0} m/s)");
        _game.FixedCamera = null;
    }
}
