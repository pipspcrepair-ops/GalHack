using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Open-world screenshot scenes: a person's eye on the land. --what=wreck (--site=pile-up|evacuation-jam|roadblock|car-park),
/// power (a line of poles), pylon (the tall pylons), vista (the highest point of road near you), road.
/// --back, --h, --yaw, --time, --weather=Kind, --index tune the shot.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> SceneView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.38);
        WeatherKind kind = Enum.Parse<WeatherKind>(_args.GetValueOrDefault("weather", "Clear"), true);
        _game.Zone.Weather.Sim.Snap(kind);
        var planet = _game.Zone.Planet;
        var net = planet.Infra;
        string what = _args.GetValueOrDefault("what", "wreck");
        Vector3 from = _game.Survivor.WorldFeet;
        Vector3 W(double x, double y, double z) => planet.NearestCopy(new Vector3((float)x, (float)y, (float)z), from);
        Vector3 stand, look;
        float h = (float)Num("h", 1.7);
        int index = (int)Num("index", 0);
        if (what == "wreck")
        {
            string kindName = _args.GetValueOrDefault("site", "pile-up");
            var sites = net.Wrecks.Where(s => s.Site == kindName && s.Route != null).OrderBy(s => W(s.X, s.Y, s.Z).DistanceTo(from)).ToList();
            var s0 = sites[Math.Min(index, sites.Count - 1)];
            GD.Print($"scene wreck: {s0.Site} ({sites.Count} of them)");
            var (sx, sz) = s0.World(PlanetSurface.Infra.WreckSite.AlignX(s0.Site), 0f);
            var dir = new Vector3(s0.DirX, 0f, s0.DirZ).Normalized();
            float back = (float)Num("back", 16);
            Vector3 start = W(sx, s0.Y, sz);
            stand = start - dir * back + new Vector3(-dir.Z, 0f, dir.X) * (float)Num("side", 0);
            look = start + dir * (s0.Length * (float)Num("far", 0.4)) + Vector3.Up * 1.4f;
        }
        else if (what is "power" or "pylon")
        {
            bool pylon = what == "pylon";
            var poles = net.Power.Poles;
            var cand = Enumerable.Range(0, poles.Count - 1).Where(i => poles[i].Pylon == pylon && poles[i + 1].Pylon == pylon
                && new Vector2((float)(poles[i + 1].X - poles[i].X), (float)(poles[i + 1].Z - poles[i].Z)).Length() < 200f)
                .OrderBy(i => W(poles[i].X, poles[i].Ground, poles[i].Z).DistanceTo(from)).ToList();
            int i0 = cand[Math.Min(index, cand.Count - 1)];
            var a = poles[i0]; var b = poles[i0 + 1];
            GD.Print($"scene {what}: pole {i0}, {cand.Count} candidates");
            Vector3 pa = W(a.X, a.Ground, a.Z), pb = W(b.X, b.Ground, b.Z);
            Vector3 dir = (pb - pa) with { Y = 0f };
            dir = dir.Normalized();
            float back = (float)Num("back", pylon ? 60 : 30), side = (float)Num("side", 0);
            stand = pa - dir * back + new Vector3(-dir.Z, 0f, dir.X) * side;
            look = pa + dir * 30f + Vector3.Up * (pylon ? 22f : 6f);
        }
        else
        {
            // The highest stretch of road within reach, looked at along its way.
            var best = net.Routes.Where(r => r.Count > 8).SelectMany(r => Enumerable.Range(0, r.Count - 4).Select(i => (r, i)))
                .Where(t => W(t.r.X[t.i], t.r.Y[t.i], t.r.Z[t.i]).DistanceTo(from) < (float)Num("reach", 2500))
                .OrderByDescending(t => t.r.Y[t.i] + (what == "road" ? 0f : 0f)).ToList();
            var (r1, i1) = best[Math.Min(index, best.Count - 1)];
            GD.Print($"scene {what}: road point {i1} at height {r1.Y[i1]:F0}");
            stand = W(r1.X[i1], r1.Y[i1], r1.Z[i1]);
            Vector3 ahead = W(r1.X[i1 + 3], r1.Y[i1 + 3], r1.Z[i1 + 3]);
            Vector3 dir = (ahead - stand) with { Y = 0f };
            dir = dir.Normalized();
            float yaw = Mathf.DegToRad((float)Num("yaw", 0));
            dir = dir.Rotated(Vector3.Up, yaw);
            look = stand + dir * 200f;
        }
        stand.Y = _game.Zone.GroundAt(stand.X, stand.Z).Y + 0.2f;
        _game.Survivor.PlaceOutside(stand.ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait((int)Num("wait", 600))) yield return x;
        stand = _game.Survivor.WorldFeet;
        // The look point is on the ground only roughly: lift it to the land where a look is level with the land.
        if (what is "vista" or "road") look.Y = stand.Y + (float)Num("pitch", 6);
        else if (what == "wreck") look.Y = Math.Max(look.Y, stand.Y + 1.4f);
        Vector3 flat = (look - stand) with { Y = 0f };
        // The camera stands a few metres ahead of the castaway, so their own body never fills a corner of the shot.
        Vector3 eye = stand + flat.Normalized() * (float)Num("lead", 3);
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + h;
        _game.Visor.Visible = false;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(30)) yield return x;
    }
}
