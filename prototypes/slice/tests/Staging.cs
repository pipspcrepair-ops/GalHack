using System;
using System.Collections.Generic;
using System.Globalization;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The screenshot views: each stages one moment of the loop through the game itself, with its
/// own controls (tests/Driver) wherever a player's hands would do it. Views: pod, dig, build,
/// liftoff, flying, cockpit.
/// </summary>
public sealed class Staging
{
    readonly Game.Game _game;
    readonly Driver _drive;
    readonly Dictionary<string, string> _args;

    public Staging(Game.Game game, Dictionary<string, string> args)
    {
        _game = game;
        _drive = new Driver(game);
        _args = args;
    }

    public IEnumerable<int> Run(string view)
    {
        if (view == "descent")
        {
            foreach (int x in Descent()) yield return x;
            yield break;
        }
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = view != "dig";
        IEnumerable<int> script = view switch
        {
            "pod" => Pod(),
            "dig" => Dig(),
            "build" => Build(half: true),
            "liftoff" => Liftoff(),
            "flying" => Flying(),
            "cockpit" => Cockpit(),
            "wrist" => WristUp(),
            _ => throw new ArgumentException($"no view '{view}'"),
        };
        foreach (int x in script) yield return x;
        if (_args.TryGetValue("hud", out string hud)) _game.Visor.Visible = hud != "0";
    }

    /// <summary>The pod coming down at dawn on its retros, seen from the ground.</summary>
    IEnumerable<int> Descent()
    {
        while (_game.Phase == Phase.Generating) yield return 0;
        _game.Visor.Visible = false;
        _game.Zone.Weather.Sim?.Snap(WeatherKind.Clouds);
        Vector3 landing = _game.Zone.LandingSite;
        float heading = (float)Num("heading", 160) * Mathf.Pi / 180f;
        Vector3 eye = landing + new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading)) * 34f;
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 2f;
        double until = Num("at", 5.6);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int ticks = 0;
        while (ticks++ < until * 60)
        {
            Vector3 pod = _game.Pod.Body.Position.ToGodot();
            _game.FixedCamera = new Transform3D(Basis.LookingAt(pod + Vector3.Up * 1.5f - eye, Vector3.Up), eye);
            yield return 0;
        }
    }

    /// <summary>The escape pod just down at dawn, seen from a little way off.</summary>
    IEnumerable<int> Pod()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.285);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clouds);
        _game.Visor.Visible = false;
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 pod = _game.Pod.Body.Position.ToGodot();
        float heading = (float)Num("heading", 160) * Mathf.Pi / 180f;
        var dir = new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading));
        Vector3 eye = pod + dir * (float)Num("distance", 10) + Vector3.Up * 1.6f;
        eye.Y = MathF.Max(eye.Y, _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f);
        // The castaway stands at the hatch, looking out.
        _game.Survivor.PlaceOutside(_game.Pod.Body.ToParent(new N.Vector3(0.6f, -0.2f, -3.0f)));
        _game.FixedCamera = new Transform3D(Basis.LookingAt(pod + Vector3.Up * 1.4f - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(40)) yield return x;
    }

    /// <summary>First person, digging into an iron deposit: the visor counting the ore in.</summary>
    IEnumerable<int> Dig()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.33);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Vector3 ore = DepositFinder.Nearest(_game.Zone, DepositKind.Iron, _game.Survivor.WorldFeet, 600f) ?? _game.Survivor.WorldFeet;
        Vector3 toOre = ore - _game.Survivor.WorldFeet;
        toOre.Y = 0f;
        Vector3 stand = ore - toOre.Normalized() * 4.5f;
        foreach (int x in _drive.WalkTo(stand, 1.2f, 60f)) yield return x;
        foreach (int x in _drive.HoldPrimary(ore + Vector3.Up * 0.5f, 70)) yield return x;
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.HoldPrimary(ore, 14)) yield return x;
        _game.Controls.Primary = true;
        foreach (int x in _drive.LookAt(ore, 4)) yield return x;
    }

    /// <summary>A blueprint set down and built part way, the rest of it a ghost.</summary>
    IEnumerable<int> Build(bool half)
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.34);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (var (item, n) in _game.Builder.TotalCost(_game.StarterPlan)) _game.Pack.Add(new Dictionary<string, int> { [item] = n });
        Vector3 pod = _game.Pod.Body.Position.ToGodot();
        Vector3 pad = FlatSpot(pod, 16f);
        Vector3 stand = pad + (pad - pod).Normalized() * 8f;
        foreach (int x in _drive.WalkTo(stand, 1.2f, 30f)) yield return x;
        foreach (int x in _drive.Tap(Key.Key4)) yield return x;
        foreach (int x in _drive.LookAt(pad, 6)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        Vessel v = _game.Builder.Underway;
        int stop = half ? _game.StarterPlan.Count * 11 / 20 : int.MaxValue;
        foreach (int x in _drive.HoldPrimary(v.Body.Position.ToGodot(), 3000, () => _game.Builder.Underway == null || v.Structure.Count >= stop)) yield return x;
        foreach (int x in _drive.UntilSettled()) yield return x;
        // Step round to a three-quarter view of it.
        Vector3 side = v.Body.ToParent(new N.Vector3(4.5f, 0f, -4.5f)).ToGodot();
        foreach (int x in _drive.WalkTo(side, 0.8f, 20f)) yield return x;
        foreach (int x in _drive.LookAt(v.Body.Position.ToGodot() + Vector3.Up * 0.4f, 10)) yield return x;
    }

    Vessel BuiltSkiff()
    {
        foreach (Vessel v in _game.Zone.Vessels)
            if (v != _game.Pod && v.Structure.Count == _game.StarterPlan.Count) return v;
        return null;
    }

    /// <summary>The finished Skiff lifting off over the land, seen from the ground.</summary>
    IEnumerable<int> Liftoff()
    {
        foreach (int x in Build(half: false)) yield return x;
        Vessel v = BuiltSkiff();
        Vector3 ground = v.Body.Position.ToGodot();
        foreach (int x in _drive.Board(v)) yield return x;
        _game.Visor.Visible = false;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(55)) yield return x;
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(5)) yield return x;
        Vector3 at = v.Body.Position.ToGodot();
        float heading = (float)Num("heading", 215) * Mathf.Pi / 180f;
        Vector3 eye = ground + new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading)) * 17f;
        eye.Y = MathF.Max(_game.Zone.GroundAt(eye.X, eye.Z).Y + 2.5f, at.Y - 3f);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(at + Vector3.Down * 1.5f - eye, Vector3.Up), eye);
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(8)) yield return x;
    }

    /// <summary>In flight over the planet, a chase view.</summary>
    IEnumerable<int> Flying()
    {
        foreach (int x in Build(half: false)) yield return x;
        Vessel v = BuiltSkiff();
        foreach (int x in _drive.Board(v)) yield return x;
        _game.Visor.Visible = false;
        _game.ThirdPerson = true;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(150)) yield return x;
        _game.Controls.Hold(Key.Space, false);
        float turn = (float)Num("turn", 1.2f);
        for (int i = 0; i < 360; i++)
        {
            _game.Controls.Hold(Key.W);
            _game.Controls.Hold(Key.Space, v.AboveGround < 70f);
            _game.Controls.Look = new Vector2(i < 60 ? turn : 0f, 0f);
            yield return 0;
        }
        foreach (int x in _drive.UntilSettled(20)) yield return x;
    }

    /// <summary>The wrist computer raised beside the pod, after emptying its locker.</summary>
    IEnumerable<int> WristUp()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.31);
        Vessel pod = _game.Pod;
        foreach (var (p, b) in pod.Structure.Blocks())
        {
            if (!pod.Structure.TypeOf(b).IsContainer) continue;
            Vector3 at = pod.Body.ToParent(PhysicsSandbox.Bridges.StructureBridge.CellCenter(pod.Structure, p)).ToGodot();
            foreach (int x in _drive.WalkTo(at, 1.4f, 20f)) yield return x;
            foreach (int x in _drive.LookAt(at, 6)) yield return x;
            foreach (int x in _drive.Tap(Key.E)) yield return x;
            break;
        }
        // Out into the open, then up with the wrist.
        Vector3 outside = pod.Body.ToParent(new N.Vector3(0f, 0f, -7f)).ToGodot();
        foreach (int x in _drive.WalkTo(outside, 1f, 20f)) yield return x;
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        Vector3 ahead = outside + (outside - pod.Body.Position.ToGodot()).Normalized() * 20f;
        foreach (int x in _drive.LookAt(ahead + Vector3.Down * 2f, 50)) yield return x;
    }

    /// <summary>In the Skiff's seat, the console showing its four numbers.</summary>
    IEnumerable<int> Cockpit()
    {
        foreach (int x in Build(half: false)) yield return x;
        Vessel v = BuiltSkiff();
        foreach (int x in _drive.Board(v)) yield return x;
        _game.Survivor.Pitch = -0.32f;
        foreach (int x in _drive.Wait(40)) yield return x;
    }

    Vector3 FlatSpot(Vector3 near, float distance)
    {
        SurfaceZone zone = _game.Zone;
        for (int i = 0; i < 64; i++)
        {
            float a = i * 0.61f, r = distance + (i / 16) * 6f;
            var p = new Vector3(near.X + MathF.Cos(a) * r, 0f, near.Z + MathF.Sin(a) * r);
            float h0 = zone.GroundAt(p.X, p.Z).Y, spread = 0f;
            for (int k = 0; k < 8; k++)
            {
                float b = k * MathF.Tau / 8f;
                spread = MathF.Max(spread, MathF.Abs(zone.GroundAt(p.X + MathF.Cos(b) * 4f, p.Z + MathF.Sin(b) * 4f).Y - h0));
            }
            if (h0 > 1f && spread < 0.35f) return zone.GroundAt(p.X, p.Z);
        }
        return zone.GroundAt(near.X + distance, near.Z);
    }

    double Num(string key, double fallback) => _args.TryGetValue(key, out string s) ? double.Parse(s, CultureInfo.InvariantCulture) : fallback;
}
