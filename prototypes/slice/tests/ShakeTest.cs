using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using GalHack.Physics.Engine;
using Slice.Game;
using Slice.Player;
using Slice.Vessels;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The view holds still against what goes with it, at a frame rate above the physics tick (the
/// owner's "guns in hand shivering when moving" and "ship interiors shaking"). On a fresh random
/// world, sampled every drawn frame with the frames unlocked from the 60 Hz physics: walking and
/// sprinting over the open ground, the held tool rides the view by its own easy bob and the eye
/// rises and falls smoothly; seated in a vessel lifting off, flying and turning, the interior is
/// drawn on the same interpolated transform the eye stands on, and its offset from the eye wavers
/// only by single-precision rounding (it prints how much: a millimetre or so, kilometres out).
///
/// Run (no --fixed-fps: drawn frames must fall between physics ticks):
///   Godot --headless --path prototypes/slice res://tests/ShakeTest.tscn
/// </summary>
public partial class ShakeTest : Node
{
    const int WatchdogMinutes = 8;
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    Survivor S => _game.Survivor;

    // ---- Sampled every drawn frame, after the game's camera and the hands have placed themselves ----
    enum Watch { None, Walk, Vessel, Bike }
    Watch _watch;
    Vessel _vessel;
    Vector3? _handWas;
    float _eyeYWas, _eyeVyWas;
    bool _eyeHad, _eyeVyHad;
    int _frames, _ticksAt;
    readonly List<float> _hand = new(), _eyeJerk = new(), _cabin = new(), _cabinMoved = new(), _cabinTurned = new();
    Transform3D? _cabinWas;
    float _viewVsDraw, _far;
    Vector3? _rigWas, _rigVelWas, _handVelWas;
    Transform3D? _bodyWas;
    readonly List<float> _body = new();
    float _figGap;
    readonly List<float> _rig = new();
    int _floorFlips;
    bool _floorWas;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        Engine.MaxFps = 150; // drawn frames between the ticks, as on the owner's screen
        ProcessPhysicsPriority = 100;
        ProcessPriority = 1000; // after the game's camera and the hands
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        SetProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    public override void _Process(double delta)
    {
        if (_watch == Watch.None || _game?.Camera == null) return;
        float dt = (float)delta;
        if (dt <= 1e-5f) return;
        _frames++;
        Transform3D cam = _game.Camera.GlobalTransform;
        if (_watch == Watch.Walk)
        {
            // The tool's tip as the eye sees it.
            if (_game.Hands.BarrelTip is Vector3 tip)
            {
                // How fast it moves in the view, and how much that changes from one frame to the next:
                // its bob is smooth (a few cm/s, changing little a frame), a shiver is not.
                Vector3 inView = cam.AffineInverse() * tip;
                if (_handWas is Vector3 was)
                {
                    Vector3 vel = (inView - was) / dt;
                    if (_handVelWas is Vector3 vw) _hand.Add((vel - vw).Length());
                    _handVelWas = vel;
                }
                _handWas = inView;
            }
            if (_game.Hands.GetNodeOrNull<Node3D>("Rig") is Node3D rig)
            {
                if (_rigWas is Vector3 rw)
                {
                    Vector3 vel = (rig.Position - rw) / dt;
                    if (_rigVelWas is Vector3 vw) _rig.Add((vel - vw).Length());
                    _rigVelWas = vel;
                }
                _rigWas = rig.Position;
            }
            // The eye's climb rate, and how much it changes from one drawn frame to the next.
            float y = cam.Origin.Y;
            if (_eyeHad)
            {
                float vy = (y - _eyeYWas) / dt;
                if (_eyeVyHad) _eyeJerk.Add(MathF.Abs(vy - _eyeVyWas));
                _eyeVyWas = vy;
                _eyeVyHad = true;
            }
            _eyeYWas = y;
            _eyeHad = true;
        }
        else if (_watch == Watch.Vessel && _vessel != null)
        {
            // The vessel's interior as the eye sees it.
            Transform3D inView = cam.AffineInverse() * _vessel.View.GetGlobalTransformInterpolated();
            _viewVsDraw = MathF.Max(_viewVsDraw, (_vessel.View.GetGlobalTransformInterpolated().Origin - (_vessel.DrawTransform * _vessel.View.Transform).Origin).Length());
            _far = MathF.Max(_far, cam.Origin.Length());
            if (_cabinWas is Transform3D was)
            {
                float moved = (inView.Origin - was.Origin).Length();
                // A turn of the interior in view, as metres at 2 m (the far wall of a cockpit).
                float turned = (inView.Basis * Vector3.Forward * 2f - was.Basis * Vector3.Forward * 2f).Length();
                _cabin.Add(MathF.Max(moved, turned));
                _cabinMoved.Add(moved);
                _cabinTurned.Add(turned);
            }
            _cabinWas = inView;
        }
        // The sleeper's figure against what carries it: the vessel's seat, or the bike.
        Transform3D? carrier = _watch == Watch.Vessel && _vessel != null ? _vessel.DrawTransform
            : _watch == Watch.Bike && S.OnBike != null ? S.OnBike.Node.GetGlobalTransformInterpolated() : null;
        if (carrier is Transform3D c && _game.Figure != null)
        {
            Transform3D body = c.AffineInverse() * _game.Figure.GetGlobalTransformInterpolated();
            _figGap = MathF.Max(_figGap, (_game.Figure.GlobalTransform.Origin - _game.Figure.GetGlobalTransformInterpolated().Origin).Length());
            if (_bodyWas is Transform3D bw)
                _body.Add(MathF.Max((body.Origin - bw.Origin).Length(), (body.Basis * Vector3.Up - bw.Basis * Vector3.Up).Length()));
            _bodyWas = body;
        }
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    static float Pct(List<float> xs, float p) => xs.Count == 0 ? float.MaxValue : xs.OrderBy(x => x).ElementAt(Math.Min(xs.Count - 1, (int)(xs.Count * p)));

    static string Stats(List<float> xs, float scale, string unit)
    {
        if (xs.Count == 0) return "no samples";
        var s = xs.OrderBy(x => x).ToList();
        return $"median {s[s.Count / 2] * scale:F2}, 99th {s[(int)(s.Count * 0.99f)] * scale:F2}, worst {s[^1] * scale:F2} {unit} over {s.Count} frames";
    }

    void Begin(Watch w)
    {
        _watch = w;
        _handWas = null;
        _cabinWas = null;
        _eyeHad = _eyeVyHad = false;
        _hand.Clear();
        _eyeJerk.Clear();
        _cabin.Clear();
        _cabinMoved.Clear();
        _rig.Clear();
        _rigWas = _rigVelWas = _handVelWas = null;
        _bodyWas = null;
        _body.Clear();
        _floorFlips = 0;
        _cabinTurned.Clear();
        _frames = 0;
        _ticksAt = (int)Engine.GetPhysicsFrames();
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = false, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        var play = new Playthrough(_game);
        foreach (int x in play.MakeTool()) yield return x;
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (int x in _drive.WalkTo(_game.Bunker.CryoFront, 0.3f, 10f)) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;

        // ---- On foot over the open ground, walking then sprinting, the tool in hand ----------------
        Vector3 site = play.FlatSpot(S.WorldFeet, 40f, span: 14f);
        int steps0 = S.Binding.StepsTaken;
        GD.Print($"   in hand: {_game.Pack.Hand}, tip {_game.Hands.BarrelTip}");
        foreach (bool sprint in new[] { false, true })
        {
            Begin(Watch.Walk);
            _game.Controls.Hold(Key.W);
            _game.Controls.Hold(Key.Shift, sprint);
            for (int i = 0; i < 60 * 7; i++)
            {
                S.Yaw += 0.004f; // a long curve, over whatever ground is there
                yield return 0;
                if (S.Body.OnFloor != _floorWas) _floorFlips++;
                _floorWas = S.Body.OnFloor;
            }
            _game.Controls.ReleaseAll();
            int ticks = (int)Engine.GetPhysicsFrames() - _ticksAt;
            GD.Print($"-- {(sprint ? "sprinting" : "walking")}: {_frames} frames over {ticks} ticks, {S.Binding.StepsTaken - steps0} steps up so far --");
            GD.Print($"   the tool's tip in view changes speed {Stats(_hand, 1f, "m/s")}");
            GD.Print($"   the rig in the camera changes speed {Stats(_rig, 1f, "m/s")}; on-floor flips {_floorFlips}");
            GD.Print($"   the eye's climb rate changes {Stats(_eyeJerk, 1f, "m/s")}");
            float rig99 = Pct(_rig, 0.99f), jerk99 = Pct(_eyeJerk, 0.99f);
            Check(_rig.Count > 100 && _frames > ticks * 1.1f && rig99 < 1f,
                $"{(sprint ? "sprinting" : "walking")}, the held tool rides the view by its own easy bob, never snapping: its speed in the view changes under 1 m/s a frame (99th: {Stats(_rig, 1f, "m/s")})");
            Check(jerk99 < 2.5f, $"{(sprint ? "sprinting" : "walking")}, the eye rises and falls smoothly: its climb rate changes under 2.5 m/s a frame (99th: {Stats(_eyeJerk, 1f, "m/s")})");
        }
        _watch = Watch.None;

        // ---- Seated in a vessel lifting off, flying and turning --------------------------------------
        S.PlaceOutside((site + new Vector3(0f, 1f, 8f)).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        var s = _game.Zone.Data.Blueprint("starter.gbp");
        Vessel v = Vessel.Spawn(_game.Zone, s, (site + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        v.Settle();
        foreach (int x in _drive.Wait(10)) yield return x;
        v.TakeChair(v.Chairs.First());
        S.Sit(v);
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(S.Seated == v, "in the vessel's chair");
        if (S.Seated != v) yield break;
        _vessel = v;
        Begin(Watch.Vessel);
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 150; i++) yield return 0;
        _game.Controls.Hold(Key.Space, false);
        _game.Controls.Hold(Key.W);
        for (int i = 0; i < 60 * 5; i++)
        {
            _game.Controls.Look = new Vector2(i % 120 < 60 ? 25f : -25f, i % 90 < 45 ? 8f : -8f);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        _game.Controls.Look = Vector2.Zero;
        GD.Print($"-- the vessel flew {v.Body.Velocity.Length():F1} m/s at the end, {v.AboveGround:F0} m up --");
        Check(v.AboveGround > 3f && v.Body.Velocity.Length() > 3f, $"it flew: {v.AboveGround:F0} m up, {v.Body.Velocity.Length():F1} m/s");
        GD.Print($"   moved {Stats(_cabinMoved, 1000f, "mm")}; turned {Stats(_cabinTurned, 1000f, "mm")}; view vs draw {_viewVsDraw * 1000f:F2} mm; camera {_far:F0} m from the origin");
        // What is left is float rounding: Godot draws in single precision, and here the world's
        // coordinates run to kilometres (a float's step at 8-16 km is about a millimetre).
        float ulp = MathF.Pow(2f, MathF.Floor(MathF.Log2(MathF.Max(1f, _far))) - 23f);
        Check(_cabin.Count > 100 && _viewVsDraw < 1e-5f && _cabinTurned.Max() < 5e-5f,
            $"lifting off, flying and turning, the interior is drawn on the very transform the eye stands on: no turn against the eye ({Stats(_cabinTurned, 1000f, "mm")}), the same interpolated frame ({_viewVsDraw * 1000f:F3} mm apart)");
        Check(Pct(_cabinMoved, 0.99f) < 4f * ulp,
            $"and its offset from the eye wavers only by float rounding {_far / 1000f:F1} km from the origin: {Stats(_cabinMoved, 1000f, "mm")} against a float step of {ulp * 1000f:F2} mm");
        GD.Print($"   figure set vs interpolated: {_figGap:F3} m");
        Check(_body.Count > 100 && _figGap < 1e-4f && Pct(_body, 0.99f) < 4f * ulp,
            $"and the sleeper's body sits still in its seat, drawn where it is set ({_figGap * 1000f:F2} mm apart): against the vessel only float rounding ({Stats(_body, 1000f, "mm")})");
        _watch = Watch.None;

        // ---- On a hover bike, riding and turning -------------------------------------------------------
        S.StandUp();
        foreach (int x in _drive.Wait(20)) yield return x;
        Vector3 pad = play.FlatSpot(S.WorldFeet, 30f, span: 10f);
        S.PlaceOutside((pad + Vector3.Up * 0.5f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        var bike = _game.Bikes.Place(pad + new Vector3(2f, 0.6f, 0f), S.Yaw);
        foreach (int x in _drive.Wait(10)) yield return x;
        S.Mount(bike);
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(S.OnBike == bike, "on the hover bike");
        if (S.OnBike != bike) yield break;
        Begin(Watch.Bike);
        _figGap = 0f;
        _game.Controls.Hold(Key.W);
        for (int i = 0; i < 60 * 5; i++)
        {
            _game.Controls.Look = new Vector2(i % 120 < 60 ? 20f : -20f, 0f);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        _game.Controls.Look = Vector2.Zero;
        float ulpBike = MathF.Pow(2f, MathF.Floor(MathF.Log2(MathF.Max(1f, S.WorldFeet.Length()))) - 23f);
        Check(_body.Count > 100 && _figGap < 1e-4f && Pct(_body, 0.99f) < 4f * ulpBike,
            $"riding and turning, the sleeper's body sits still on the bike, drawn where it is set ({_figGap * 1000f:F2} mm apart): against it only float rounding ({Stats(_body, 1000f, "mm")})");
        _watch = Watch.None;
    }
}
