using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface;
using PlanetSurface.Skies;
using Slice.Orbit;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// WarpTest's watching: the streaks from the pilot's seat (windowed), the frame split by engine stage and by system for
/// every stage of every warp (--split=1), and the Skiff's attitude on a descent.
/// </summary>
public partial class WarpTest
{
    static bool Windowed => DisplayServer.GetName() != "headless";

    /// <summary>The pilot's forward in the world: the way the ship flies.</summary>
    static Vector3 Forward(Vessel v) => N.Vector3.Transform((v.PilotBasis * Vector3.Forward).ToNumerics(), v.Body.Rotation).ToGodot();

    /// <summary>The Skiff's top kept as near as it goes while its nose turns to <paramref name="forward"/>.</summary>
    Vector3 TopFor(Vector3 forward)
    {
        Vector3 top = N.Vector3.Transform(N.Vector3.UnitY, _ship.Body.Rotation).ToGodot(), up = top - forward * top.Dot(forward);
        return up.LengthSquared() > 1e-4f ? up.Normalized() : forward.Cross(Vector3.Right).Normalized();
    }

    /// <summary>The way to a body of the system from the Skiff, in the world of the orbit it is in.</summary>
    Vector3 Toward(Orbiter target)
    {
        D3 d = Model.Position(target, _game.Surface.Sky.Days) - Spaceflight.ToSystem(_game.Orbit, _ship.Body.Position);
        var v = new N.Vector3((float)d.X, (float)d.Y, (float)d.Z);
        return N.Vector3.Transform(v, N.Quaternion.Conjugate(Spaceflight.WorldToSystem(_game.Orbit))).ToGodot().Normalized();
    }

    // ---- The streaks from the pilot's seat -------------------------------------------------------------------------

    bool _watched;
    List<Image> _keep, _keepBig;
    List<double> _keepDeltas;

    /// <summary>A second of the tunnel from the seat: the frames, the time each took, the point ahead of the nose on them, three kept large.</summary>
    sealed record SeatView(float LookDeg, Vector2 Ahead, List<Image> Frames, List<double> Deltas, List<Image> Big);

    /// <summary>Every drawn frame kept while the seat's proof watches (in the frame's own pass: one a frame, none twice).</summary>
    public override void _Process(double delta)
    {
        if (_keep == null) return;
        Image shot = GetViewport().GetTexture().GetImage();
        if (_keep.Count is >= 12 and < 15)
        {
            var big = (Image)shot.Duplicate();
            big.Resize(960, 540, Image.Interpolation.Bilinear);
            _keepBig.Add(big);
        }
        shot.Resize(StreakFlow.W, StreakFlow.H, Image.Interpolation.Bilinear);
        _keep.Add(shot);
        _keepDeltas.Add(delta);
    }

    /// <summary>The hull turned 120° off the way to where it warps: what the pilot sees mustn't hang on where that lies.</summary>
    IEnumerable<int> TurnAway(Orbiter target)
    {
        Vector3 to = Toward(target), side = to.Cross(TopFor(to));
        Vector3 away = to.Rotated(side.Normalized(), Mathf.DegToRad(120f));
        foreach (int x in _pilot.TurnTo(_ship, away, TopFor(away))) yield return x;
    }

    /// <summary>
    /// From the pilot's seat in the tunnel, the cockpit in frame, at the window's 60 fps: a second looking along the nose,
    /// then a second looking 35° aside in the seat, every drawn frame kept.
    /// </summary>
    IEnumerable<int> SeatStreaks(Jump j, List<SeatView> views)
    {
        var s = _game.Survivor;
        try
        {
            foreach (float look in new[] { 0f, 35f })
            {
                s.Yaw = Mathf.DegToRad(look);
                s.Pitch = 0f;
                for (int i = 0; i < 10; i++) yield return 0; // the view settles
                Camera3D cam = _game.Camera;
                Vector2 size = GetViewport().GetVisibleRect().Size, at = cam.UnprojectPosition(cam.GlobalPosition + Forward(_ship) * 50f);
                var view = new SeatView(look, new Vector2(at.X * StreakFlow.W / size.X, at.Y * StreakFlow.H / size.Y), new(), new(), new());
                (_keep, _keepDeltas, _keepBig) = (view.Frames, view.Deltas, view.Big);
                views.Add(view);
                for (int i = 0; i < 70 && j.Now == Jump.Stage.Tunnel; i++) yield return 0;
                _keep = null;
            }
        }
        finally
        {
            // Looking ahead again, however the tunnel ended.
            _keep = null;
            (s.Yaw, s.Pitch) = (0f, 0f);
        }
    }

    /// <summary>Which way the streaks ran from the seat (<see cref="StreakFlow"/>); three consecutive frames saved, the point ahead marked.</summary>
    void Judge(SeatView v, WarpRoute route, float awayDeg)
    {
        var (median, outward) = StreakFlow.Run(v.Frames.Select(StreakFlow.Luma).ToList(), v.Ahead);
        double fps = v.Deltas.Count / Math.Max(1e-6, v.Deltas.Sum());
        string look = v.LookDeg == 0f ? "ahead" : $"{v.LookDeg:F0}-aside";
        for (int k = 0; k < v.Big.Count && _shots != null; k++)
        {
            Image f = v.Big[k];
            f.Convert(Image.Format.Rgba8);
            for (int d = -6; d <= 6; d++)
                foreach (var (x, y) in new[] { ((int)(v.Ahead.X * 2) + d, (int)(v.Ahead.Y * 2)), ((int)(v.Ahead.X * 2), (int)(v.Ahead.Y * 2) + d) })
                    if (x >= 0 && y >= 0 && x < f.GetWidth() && y < f.GetHeight()) f.SetPixel(x, y, Colors.Red);
            string file = System.IO.Path.Combine(_shots, $"seat-{look}-{k + 1}.png");
            f.SavePng(file);
            GD.Print($"  (shot: {file})");
        }
        Check(median > 0 && outward > 0.8f,
            $"from the pilot's seat, the cockpit in frame, looking {(v.LookDeg == 0f ? "ahead" : $"{v.LookDeg:F0}° aside")}, the hull {awayDeg:F0}° off the way to {route.Name}: " +
            $"{v.Frames.Count} frames at {fps:F0} fps, the streaks move {median:+0.0;-0.0} px a frame (median, at {StreakFlow.W} px wide) out from the point ahead of the nose at ({v.Ahead.X:F0}, {v.Ahead.Y:F0}), outward in {outward:P0} of frame pairs");
    }

    /// <summary>
    /// The sky's floor of brightness (its darkest fifth) in the middle of a view from just off the hull, facing away from the local sun and from the
    /// body you fly over (windowed), with a shot. The owner (23 Nov): the star's glare may wash the sky at its high orbit,
    /// "it's a problem if it follows us when we leave": measured at a fresh orbit, at the star, and right after leaving it.
    /// </summary>
    IEnumerable<int> SkyAway(string shot, Action<double> measured)
    {
        Vector3 fromStar = -Toward(Model.Star), fromBody = _game.Orbit.Anchor is { } c && c.Body != Model.Star ? (_ship.Body.Position.ToGodot() - c.Centre).Normalized() : fromStar;
        Vector3 away = fromStar + fromBody;
        away = away.LengthSquared() > 0.09f ? away.Normalized() : fromStar.Cross(MathF.Abs(fromStar.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        _game.FixedCamera = new Transform3D(Basis.LookingAt(away, MathF.Abs(away.Y) < 0.9f ? Vector3.Up : Vector3.Right), _game.Camera.GlobalPosition + away * 40f);
        for (int i = 0; i < 20; i++) yield return 0;
        float[] luma = StreakFlow.Luma(GetViewport().GetTexture().GetImage());
        // The darkest fifth of the middle of the view: glare lifts the whole sky, a nebula or a cluster only its part.
        var middle = new List<float>();
        for (int y = StreakFlow.H * 3 / 10; y < StreakFlow.H * 7 / 10; y++)
            for (int x = StreakFlow.W / 4; x < StreakFlow.W * 3 / 4; x++) middle.Add(luma[y * StreakFlow.W + x]);
        middle.Sort();
        Shot(shot);
        _game.FixedCamera = null;
        for (int i = 0; i < 5; i++) yield return 0;
        measured(middle[middle.Count / 5]);
    }

    // ---- Where the frames go (--split=1) ---------------------------------------------------------------------------

    FrameSplit _split;
    SystemSplit _systems;
    TimeSpan _cpuAt;
    ulong _mainAt;
    readonly Stopwatch _splitClock = new();

    void SplitStart()
    {
        if (!_args.ContainsKey("split")) return;
        _split ??= new FrameSplit(GetTree().Root, GetViewport().GetViewportRid()) { HitchMs = 50 };
        _systems ??= new SystemSplit(_game) { Deeper = n => n is SurfaceZone };
        _split.Systems = _systems;
        _split.Doing = () => $"{Flight.Jumping?.Now}: {Flight.Doing}";
        _systems.Reset();
        _split.Reset();
        _cpuAt = Process.GetCurrentProcess().TotalProcessorTime;
        _mainAt = FrameProbe.Cycles();
        _splitClock.Restart();
    }

    /// <summary>A stage's frames by engine stage and by system, and how many cores the process kept busy over it (the main thread's share apart).</summary>
    void SplitReport(string what)
    {
        if (_split == null) return;
        double wall = _splitClock.Elapsed.TotalMilliseconds, cpu = (Process.GetCurrentProcess().TotalProcessorTime - _cpuAt).TotalMilliseconds;
        double main = FrameProbe.CyclesPerMs > 0 ? (FrameProbe.Cycles() - _mainAt) / FrameProbe.CyclesPerMs : 0;
        GD.Print($"SPLIT {what}: {_split.Report()}");
        GD.Print($"SPLIT {what} cores: the process {cpu / Math.Max(1, wall):F2} cores busy on average over {wall / 1000:F1} s, the main thread {main / Math.Max(1, wall):F2} of them, the rest {(cpu - main) / Math.Max(1, wall):F2} on {System.Environment.ProcessorCount} logical cores");
        foreach (string h in _split.Hitches.Take(8)) GD.Print($"SPLIT {what} hitch {h}");
        GD.Print($"SPLIT {what} by system (cpu ms a frame): {_systems.Report(16)}");
        SplitStart();
    }

    // ---- A descent -------------------------------------------------------------------------------------------------

    /// <summary>The Skiff's attitude over a body: its top against the sky over it (and the map's up there), its spin and its controls' state.</summary>
    string Attitude(Celestial c)
    {
        Vector3 at = _ship.Body.Position.ToGodot(), radial = (at - c.Centre).Normalized();
        Vector3 top = N.Vector3.Transform(N.Vector3.UnitY, _ship.Body.Rotation).ToGodot();
        var (x, z) = c.Under(at);
        Vector3 mapUp = N.Vector3.Transform(N.Vector3.UnitY, N.Quaternion.Normalize(c.Turn * c.Map.Frame(x, z))).ToGodot();
        var st = _ship.Flight.State;
        Vector3 nose = Forward(_ship), north = N.Vector3.Transform(-N.Vector3.UnitZ, N.Quaternion.Normalize(c.Turn * c.Map.Frame(x, z))).ToGodot();
        return $"its top {Mathf.RadToDeg(top.AngleTo(radial)):F0}° off the sky over it (the map's up there {Mathf.RadToDeg(mapUp.AngleTo(radial)):F0}° off it), its nose {Mathf.RadToDeg(nose.AngleTo(north)):F0}° off the map's north and {Mathf.RadToDeg(nose.AngleTo(-radial)):F0}° off straight down, " +
            $"turning {_ship.Body.AngularVelocity.Length():F2} rad/s (asked {_ship.Flight.Input.Turn}, seated {(_ship.Flight.Input.Seated ? "yes" : "NO")} {(_game.Survivor.Seated == _ship ? "here" : "elsewhere")}, pilot {(_ship.Roles.Pilot == Vessel.Player ? "you" : "not you")}, frozen {_ship.Controller?.Frozen}, settling {_ship.Controller?.Settling}, tossed {_ship.Body.Tossed:F1}, look {_game.Controls.Look}, turn rate {_ship.Stats?.TurnRate:F0}°/s, menu {(_game.Menu.IsOpen ? "open" : "shut")}), pushing {st.ThrustLocal.Length():F1} m/s² along its own {st.ThrustLocal.Normalized()}, cruise {(st.Cruise ? "on" : "off")}, assist {(st.AssistOff ? "off" : "on")}";
    }
}
