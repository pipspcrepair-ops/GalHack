using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Orbit;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>A damaged ship's smoke: in orbit in front of a planet (with a pixel probe), and on the ground.</summary>
public sealed partial class Staging
{
    /// <summary>Wears the first few devices of a vessel to smoking and returns where the first one is, in the world.</summary>
    Vector3 SmokingDevice(Vessel v)
    {
        Structure s = v.Structure;
        var devices = s.Blocks().Where(b => !s.TypeOf(b.block).IsArmor).Take(3).ToList();
        foreach (var (p, b) in devices) s.SetHp(p, s.MaxHp(b) * 0.1f);
        v.View.Queue(s.DirtyChunks);
        s.DirtyChunks.Clear();
        return v.ToWorld(PhysicsSandbox.Bridges.StructureBridge.CellCenter(s, devices[0].pos));
    }

    /// <summary>Every smoke's puff material (--old=1 puts them back in the old draw order).</summary>
    IEnumerable<BaseMaterial3D> SmokeLooks(Vessel v) =>
        v.View.Fx.GetChildren().OfType<CpuParticles3D>().Where(p => p.Name.ToString().StartsWith("Smoke")).Select(p => (BaseMaterial3D)p.MaterialOverride).Distinct();

    /// <summary>
    /// A damaged Skiff in orbit with a neighbouring planet behind its smoke: a frame with the smoke and one without it. The smoke's
    /// pixels must differ from the bare frame (a planet drawn over it would make them equal). Prints CHECK lines; exits 1 on a fail.
    /// </summary>
    IEnumerable<int> SmokeOrbit()
    {
        foreach (int x in ToOrbit()) yield return x;
        foreach (int x in UntilSurfaces()) yield return x;
        Vector3 world = _game.Orbit.ToWorld(N.Vector3.Zero).ToGodot(), out0 = (Skiff.Body.Position.ToGodot() - world).Normalized();
        Hurry(3);
        foreach (int x in _pilot.FlyTo(Skiff, () => world + out0 * (_game.Orbit.ShellRadius + 2500f), 60f, 120f)) yield return x;
        Hurry(1);
        _game.Controls.ReleaseAll();
        _game.Visor.Visible = false;
        var sky = _game.Surface.Sky;
        sky.Paused = true;
        OrbitZone o = _game.Orbit;
        o.Globe.Hidden = true;
        var model = _game.Surface.Planet.StarSystem;
        double days = sky.Days;
        var turned = model.Orient(days);
        Basis turn = o.Turn.ToBasis();
        var planet = model.Bodies.Where(b => b.Kind == Weather.Model.OrbiterKind.Planet && b.Parent == model.Star && b != model.Home)
            .OrderBy(b => (model.Position(b, days) - model.Position(model.Home, days)).Length).First();
        var toStar = (model.Position(model.Star, days) - model.Position(planet, days)).Normalized;
        var side = toStar.Cross(new D3(0, 1, 0)).Normalized;
        D3 from = model.Position(planet, days) - model.Position(model.Home, days) + (toStar * 0.8 + side * 0.6 + new D3(0, 0.2, 0)).Normalized * (planet.Radius * 3.0);
        _game.Spaceflight.ViewFrom = turned.ToGlobe(from);
        var s = model.See(planet, days, turned.ToGlobe(from), turned);
        Vector3 look = turn * new Vector3((float)s.Direction.X, (float)s.Direction.Y, (float)s.Direction.Z);

        Vessel v = Skiff;
        Vector3 cell = SmokingDevice(v);
        Vector3 up = Mathf.Abs(look.Y) > 0.95f ? Vector3.Forward : Vector3.Up;
        // Eye behind the smoke, planet beyond it.
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look, up), cell - look * 3.5f + up * 0.8f);
        for (int i = 0; i < 120; i++) yield return 0;
        GD.Print($"(smoke: {SmokeLooks(v).Count()} puff materials, priority {SmokeLooks(v).FirstOrDefault()?.RenderPriority}, planet {planet.Name} {Mathf.RadToDeg(s.Angular):F1} degrees radius)");
        if (_args.GetValueOrDefault("old") == "1") foreach (BaseMaterial3D m in SmokeLooks(v)) m.RenderPriority = 0;
        for (int i = 0; i < 6; i++) yield return 0;
        Snap("smoke");
        Image with = _game.GetViewport().GetTexture().GetImage();
        foreach (CpuParticles3D p in v.View.Fx.GetChildren().OfType<CpuParticles3D>()) if (p.Name.ToString().StartsWith("Smoke")) p.Visible = false;
        for (int i = 0; i < 4; i++) yield return 0;
        Image bare = _game.GetViewport().GetTexture().GetImage();
        bare.SavePng(System.IO.Path.ChangeExtension(_args.GetValueOrDefault("out", "smoke.png"), null) + "-bare.png");

        // The middle fifth of the frame, where the smoke is.
        int w = with.GetWidth(), h = with.GetHeight(), changed = 0, total = 0;
        for (int y = h * 2 / 5; y < h * 3 / 5; y += 2)
            for (int x = w * 2 / 5; x < w * 3 / 5; x += 2, total++)
            {
                Color a = with.GetPixel(x, y), b = bare.GetPixel(x, y);
                if (Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) > 0.06f) changed++;
            }
        bool ok = changed > total / 50;
        GD.Print($"CHECK {(ok ? "ok" : "FAIL")}: smoke shows in front of the planet ({changed} of {total} centre pixels differ from the frame without it)");
        Wildlife.Shutdown.Quit(_game, ok ? 0 : 1);
    }

    /// <summary>A landed, worn Skiff-sized vessel on the ground, close on its smoke.</summary>
    IEnumerable<int> SmokeGround()
    {
        foreach (int x in DamageSetting()) yield return x;
        Structure s = _game.Zone.Data.Blueprint("starter.gbp");
        s.Name = "Smoking";
        Vessel v = Vessel.Spawn(_game.Zone, s, DamageSite(), 0.5f, landed: true);
        foreach (int x in _drive.Wait(4)) yield return x;
        Vector3 at = SmokingDevice(v);
        Aim(at + new Vector3(1.8f, 1.2f, -2.6f), at + Vector3.Up * 0.8f);
        foreach (int x in _drive.Wait(150)) yield return x;
        Snap("ground");
        Wildlife.Shutdown.Quit(_game, 0);
    }
}
