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
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The camera against what it rides with (the owner: "the camera STILL isn't tied to the position of
/// the cabin"). On a fresh random world, sampled every drawn frame at 60 fps and at an uneven rate:
/// seated in a vessel flying, turning, rolling, speeding up, braking and bumped, the first-person eye
/// stands still in the vessel's drawn frame (a millimetre, a tenth of a degree), and that drawn frame
/// is this frame's interpolation of the physics ticks, never the last frame's. Third person, the chase
/// view stands still in the vessel's own frame as it rolls; the right button held swings it round the
/// vessel without steering and it eases back behind on letting go; the wheel zooms it between just
/// outside the hull and several hull sizes; it is never inside the hull's bounds or the ground. The
/// same free look and zoom on foot (the sleeper keeps facing its way) and on the hover bike (the bike
/// keeps its heading).
///
/// Run (no --fixed-fps: drawn frames must fall between physics ticks):
///   Godot --headless --path prototypes/slice res://tests/PilotCamTest.tscn
/// </summary>
public partial class PilotCamTest : Node
{
    const int WatchdogMinutes = 9;
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();
    Survivor S => _game.Survivor;

    // ---- Sampled every drawn frame, after the game has placed the camera ----
    Vessel _vessel;
    bool _watching, _uneven;
    Transform3D _tickWas, _tickNow;
    int _ticksSeen;
    readonly List<float> _moved = new(), _turned = new(), _stale = new(), _inside = new(), _underground = new();
    (Vector3 pos, Basis basis)? _first;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100; // after the physics host, the game and its after-step: the tick's last word
        ProcessPriority = 1000;       // after the game's camera
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--shots=") && DisplayServer.GetName() != "headless") System.IO.Directory.CreateDirectory(_shots = arg["--shots=".Length..]);
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        // The vessel's transform as the tick left it: drawing interpolates between the last two.
        if (_vessel != null)
        {
            _tickWas = _ticksSeen > 0 ? _tickNow : _vessel.Node.GlobalTransform;
            _tickNow = _vessel.Node.GlobalTransform;
            _ticksSeen++;
        }
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
        if (_uneven) OS.DelayUsec(_rng.Next(0, 14000)); // frames of every length, out of step with the ticks
        if (_strip > 0)
        {
            _strip--;
            Shot($"{_stripName}-{_stripAt++}");
        }
        if (!_watching || _vessel == null || _ticksSeen < 2) return;
        Transform3D cam = _game.Camera.GlobalTransform, drawn = _vessel.DrawTransform;
        // What this frame draws the vessel with: the last two ticks at this frame's fraction.
        Transform3D due = _tickWas.InterpolateWith(_tickNow, (float)Engine.GetPhysicsInterpolationFraction());
        _stale.Add((float)Gap(drawn.Origin, due.Origin));
        // The camera in the vessel's drawn frame, worked out in double precision (the world runs to kilometres).
        var rel = (pos: Local(drawn, cam.Origin), basis: drawn.Basis.Orthonormalized().Inverse() * cam.Basis.Orthonormalized());
        if (_game.ThirdPerson)
        {
            var (middle, radius) = Middle(_vessel, drawn);
            float out_ = cam.Origin.DistanceTo(middle);
            _inside.Add(radius - out_);
            _underground.Add(_game.Zone.GroundAt(cam.Origin.X, cam.Origin.Z).Y - cam.Origin.Y);
            // Brought in short of the ground or something in the way: rightly not where it would stand in the clear.
            if (MathF.Abs(out_ - _game.Zoom(Game.Game.OrbitMode.Vessel) * radius * 2f) > 0.05f)
            {
                _pulledIn++;
                return;
            }
        }
        if (_first is { } f)
        {
            _moved.Add((rel.pos - f.pos).Length());
            _turned.Add(Mathf.RadToDeg((f.basis.Inverse() * rel.basis).GetRotationQuaternion().GetAngle()));
        }
        else _first = rel;
    }

    int _pulledIn;

    /// <summary>Windowed runs (--shots=DIR): frames saved for the eye, a strip of them while turning.</summary>
    string _shots;
    int _strip, _stripAt;
    string _stripName;

    void Shot(string name)
    {
        if (_shots == null) return;
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_shots, name + ".png"));
    }

    void Strip(string name, int frames)
    {
        _stripName = name;
        _strip = frames;
        _stripAt = 0;
    }

    static double Gap(Vector3 a, Vector3 b)
    {
        double x = (double)a.X - b.X, y = (double)a.Y - b.Y, z = (double)a.Z - b.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    /// <summary>A world point in a frame, in double precision: a millimetre means a millimetre 9 km out.</summary>
    static Vector3 Local(Transform3D t, Vector3 p)
    {
        double x = (double)p.X - t.Origin.X, y = (double)p.Y - t.Origin.Y, z = (double)p.Z - t.Origin.Z;
        Basis b = t.Basis.Orthonormalized();
        double Dot(Vector3 a) => a.X * x + a.Y * y + a.Z * z;
        return new Vector3((float)Dot(b.Column0), (float)Dot(b.Column1), (float)Dot(b.Column2));
    }

    static (Vector3 middle, float radius) Middle(Vessel v, Transform3D t)
    {
        var (min, max) = v.Structure.Bounds();
        float cell = v.Structure.Grid.CellSize;
        Vector3 lo = (min.ToVector3().ToGodot() - Vector3.One * 0.5f) * cell, hi = (max.ToVector3().ToGodot() + Vector3.One * 0.5f) * cell;
        return (t * ((lo + hi) * 0.5f), (hi - lo).Length() * 0.5f);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    static string Stats(List<float> xs, float scale, string unit)
    {
        if (xs.Count == 0) return "no samples";
        var s = xs.OrderBy(x => x).ToList();
        return $"median {s[s.Count / 2] * scale:F2}, 99th {s[(int)(s.Count * 0.99f)] * scale:F2}, worst {s[^1] * scale:F2} {unit} over {s.Count} frames";
    }

    void Watch(bool on)
    {
        _watching = on;
        if (!on) return;
        _first = null;
        _pulledIn = 0;
        foreach (var l in new[] { _moved, _turned, _stale, _inside, _underground }) l.Clear();
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
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (int x in _drive.WalkTo(_game.Bunker.CryoFront, 0.3f, 10f)) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Controls c = _game.Controls;

        // ---- On foot, third person: free look round the sleeper, and the wheel ------------------------
        Vector3 site = play.FlatSpot(S.WorldFeet, 40f, span: 14f);
        S.PlaceOutside((site + Vector3.Up * 0.5f).ToNumerics());
        foreach (int x in _drive.Wait(40)) yield return x;
        _game.ThirdPerson = true;
        foreach (int x in FreeLookAndZoom("on foot", () => S.Eye().Origin, () => S.Yaw, "the sleeper's heading")) yield return x;

        // ---- On the hover bike, riding ------------------------------------------------------------------
        var bike = _game.Bikes.Place(site + new Vector3(2f, 0.6f, 0f), S.Yaw);
        foreach (int x in _drive.Wait(10)) yield return x;
        S.Mount(bike);
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(S.OnBike == bike, "on the hover bike");
        if (S.OnBike == bike)
        {
            c.Hold(Key.W);
            foreach (int x in FreeLookAndZoom("on the bike", () => S.Eye().Origin, () => bike.Yaw, "the bike's heading")) yield return x;
            c.ReleaseAll();
            S.Dismount();
            foreach (int x in _drive.Wait(20)) yield return x;
        }

        // ---- Seated in a vessel ----------------------------------------------------------------------------
        S.PlaceOutside((site + new Vector3(0f, 1f, 8f)).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        Vessel v = Vessel.Spawn(_game.Zone, _game.Zone.Data.Blueprint("starter.gbp"), (site + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        v.Settle();
        foreach (int x in _drive.Wait(10)) yield return x;
        v.TakeChair(v.Chairs.First());
        S.Sit(v);
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(S.Seated == v, "in the vessel's chair");
        if (S.Seated != v) yield break;
        _vessel = v;
        _game.ThirdPerson = false;
        c.Hold(Key.Space);
        for (int i = 0; i < 300; i++) yield return 0;
        c.ReleaseAll();
        var (_, radius) = Middle(v, v.DrawTransform);
        GD.Print($"   the vessel: {radius * 2f:F1} m across");

        foreach (bool uneven in new[] { false, true })
        {
            Engine.MaxFps = uneven ? 0 : 60;
            _uneven = uneven;
            string rate = uneven ? "uneven frames" : "60 fps";
            foreach (bool third in new[] { false, true })
            {
                _game.ThirdPerson = third;
                foreach (int x in _drive.Wait(30)) yield return x; // the zoom settles
                Watch(true);
                int ticks0 = (int)Engine.GetPhysicsFrames();
                foreach (int x in Manoeuvre(v, uneven ? null : third ? "tp-roll" : "fp-turn")) yield return x;
                Watch(false);
                int frames = _stale.Count, ticks = (int)Engine.GetPhysicsFrames() - ticks0;
                string view = third ? "third person" : "first person";
                GD.Print($"-- {view}, {rate}: {frames} frames over {ticks} ticks; {v.Body.Velocity.Length():F0} m/s, {v.AboveGround:F0} m up, {_game.Camera.GlobalPosition.Length() / 1000f:F1} km from the origin --");
                GD.Print($"   drawn vs this frame's interpolation {Stats(_stale, 1000f, "mm")}");
                GD.Print($"   camera in the vessel's frame: moved {Stats(_moved, 1000f, "mm")}; turned {Stats(_turned, 1f, "deg")}; {_pulledIn} frames brought in short of the ground");
                Check(_stale.Count > 200 && _stale.Max() < 1e-3f, $"{view}, {rate}: the vessel is drawn on this frame's interpolation of its ticks, never the last frame's ({Stats(_stale, 1000f, "mm")})");
                float within = third ? 5e-3f : 1e-3f; // third person: metres out, a few millimetres of float rounding are nothing
                Check(_moved.Count > 200 && _moved.Max() < within && _turned.Max() < 0.1f,
                    $"{view}, {rate}: turning, rolling, speeding up, braking and bumped, the camera stands still in the vessel's drawn frame (moved {_moved.DefaultIfEmpty(float.MaxValue).Max() * 1000f:F2} mm, turned {_turned.DefaultIfEmpty(float.MaxValue).Max():F3} deg at worst)");
                if (third)
                    Check(_inside.Max() < 0f && _underground.Max() < 0f,
                        $"{view}, {rate}: never inside the hull's bounds ({-_inside.Max():F2} m clear at least) or the ground ({-_underground.Max():F2} m above at least)");
            }
        }
        Engine.MaxFps = 60;
        _uneven = false;

        // ---- Third person in the vessel: free look round it, without steering; the wheel --------------
        _game.ThirdPerson = true;
        c.Hold(Key.W);
        foreach (int x in FreeLookAndZoom("piloting", () => Middle(v, v.DrawTransform).middle, () => v.Flight.Input.Turn.Length(), "the stick", vessel: v, radius: radius)) yield return x;
        c.ReleaseAll();
    }

    /// <summary>Flying: turns, rolls both ways, a boost, a brake, and a knock from the side.</summary>
    IEnumerable<int> Manoeuvre(Vessel v, string strip)
    {
        Controls c = _game.Controls;
        c.Hold(Key.W);
        for (int i = 0; i < 360; i++)
        {
            c.Look = new Vector2(i % 120 < 60 ? 25f : -25f, i % 90 < 45 ? 8f : -8f);
            c.Hold(Key.Q, i is >= 60 and < 120);
            c.Hold(Key.E, i is >= 120 and < 180);
            c.Hold(Key.Shift, i is >= 180 and < 240);
            c.Hold(Key.S, i is >= 270 and < 330);
            c.Hold(Key.W, i is < 270 or >= 330);
            c.Hold(Key.Space, v.AboveGround < 40f); // well clear of the ground
            if (i == 80 && strip != null) Strip(strip, 6); // turning and rolling
            if (i == 200)
            {
                v.Body.Velocity += N.Vector3.Transform(new N.Vector3(9f, 3f, 0f), v.Body.Rotation); // a knock
                v.Body.AngularVelocity += new N.Vector3(0.6f, 0.3f, 0.9f);
            }
            yield return 0;
        }
        c.ReleaseAll();
        c.Look = Vector2.Zero;
    }

    /// <summary>
    /// The right button held swings the camera round <paramref name="pivot"/> while <paramref name="held"/>
    /// (what you face, or the stick) stays put; let go and it eases back; the wheel zooms between the mode's limits.
    /// </summary>
    IEnumerable<int> FreeLookAndZoom(string what, Func<Vector3> pivot, Func<float> held, string heldName, Vessel vessel = null, float radius = 0f)
    {
        Controls c = _game.Controls;
        Game.Game.OrbitMode mode = _game.Following;
        var (near, far) = Game.Game.ZoomLimits(mode);
        float unit = mode == Game.Game.OrbitMode.Vessel ? radius * 2f : 1f;
        foreach (int x in _drive.Wait(30)) yield return x;
        if (vessel != null) foreach (int x in _drive.Wait(60)) yield return x; // the stick recentred
        Vector3 Away() => _game.Camera.GlobalPosition - pivot();
        // Directions in the frame it orbits in: the vessel's, or the eye's heading.
        Vector3 Behind()
        {
            Basis frame = vessel != null ? vessel.DrawTransform.Basis.Orthonormalized() : S.Eye().Basis * new Basis(Vector3.Right, -S.Pitch);
            return (frame.Inverse() * Away()).Normalized();
        }
        Vector3 start = Behind();
        Shot($"{what}-behind".Replace(' ', '-'));
        int slot = _game.Pack.Selected;
        float heldWas = held();
        float most = 0f, drift = 0f;
        c.Secondary = true;
        for (int i = 0; i < 50; i++)
        {
            c.Look = new Vector2(60f, i < 25 ? -6f : 6f);
            if (i == 0) c.SecondaryPressed = true;
            if (i == 12) Shot($"{what}-side".Replace(' ', '-'));
            yield return 0;
            most = MathF.Max(most, Mathf.RadToDeg(start.AngleTo(Behind())));
            drift = MathF.Max(drift, vessel != null ? held() : MathF.Abs(Mathf.Wrap(held() - heldWas, -MathF.PI, MathF.PI)));
        }
        c.Secondary = false;
        c.Look = Vector2.Zero;
        Check(most > 60f && drift < (vessel != null ? 1e-4f : 0.01f),
            $"{what}: the right button held swings the camera {most:F0} degrees round, {heldName} unmoved ({drift:F4})");
        for (int i = 0; i < 120; i++) yield return 0;
        float back = Mathf.RadToDeg(start.AngleTo(Behind()));
        Check(back < 3f, $"{what}: let go, it eases back behind ({back:F1} degrees off after 2 s)");

        float dist0 = Away().Length();
        c.Wheel = -40; // as far in as it goes
        yield return 0;
        foreach (int x in _drive.Wait(60)) yield return x;
        float zin = Away().Length(), zinWant = _game.Zoom(mode) * unit;
        Shot($"{what}-in".Replace(' ', '-'));
        c.Wheel = 60; // as far out
        yield return 0;
        foreach (int x in _drive.Wait(60)) yield return x;
        float zout = Away().Length(), zoutWant = _game.Zoom(mode) * unit;
        Shot($"{what}-out".Replace(' ', '-'));
        GD.Print($"   {what}: {dist0:F1} m out, zoomed in {zin:F1} m (limit {near * unit:F1}), out {zout:F1} m (limit {far * unit:F1})");
        Check(MathF.Abs(zinWant - near * unit) < 1e-3f && MathF.Abs(zoutWant - far * unit) < 1e-3f && zin < dist0 * 0.8f && zout > dist0 * 1.5f && zin >= near * unit * 0.95f,
            $"{what}: the wheel zooms between {near * unit:F1} and {far * unit:F1} m ({zin:F1} in, {zout:F1} out)");
        if (vessel != null) Check(zin > radius, $"{what}: zoomed right in it stays outside the hull's bounds ({zin:F1} m from the middle, {radius:F1} m to its corners)");
        else Check(_game.Pack.Selected == slot, $"{what}: the wheel zooms instead of changing the toolbar slot");
        // Back to the default for the next mode.
        c.Wheel = 0;
        foreach (int x in _drive.Wait(2)) yield return x;
    }
}
