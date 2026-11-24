using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Orbit;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The globe's mountains never stand up to a ship in orbit (owner, 24 Nov: "I managed to crash into the worlds mountains in
/// low orbit, somehow"). On a fresh world the Skiff climbs through the crossing and flies low through the band, round the
/// globe at 1,250 m, to the world's highest ground, by the game's controls, and stops over it at 1,100 m and at 1,250 m.
/// Every drawn frame in the band: the sheet stands from the height the readout gives and from the up under you, to the
/// centimetre and a twentieth of a degree (the band's flat terms drift off the globe as you fly: it is laid out again under
/// you), so all the relief it stands up lies below you. Every tick: whatever is solid under the Skiff (the globe's sea-level
/// shell, the land's own ground waiting under the band in the surface's terms) stands where the map's ground is at that
/// place on the globe (the land's own height there, within 10 m) and well under the Skiff: drifted off the globe, the land's ground stood up as an unseen mountain in
/// orbit, and the Skiff touches nothing.
///
/// Seated in the Skiff and flown from the external chase camera throughout. Windowed with --shots=folder, a picture over the
/// highest ground at each height.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/OrbitReliefTest.tscn
/// </summary>
public partial class OrbitReliefTest : Node
{
    const int WatchdogMinutes = 20;
    static readonly float[] Heights = { 1100f, 1250f };
    const float Cruise = 1250f;

    Game.Game _game;
    Driver _drive;
    Pilot _pilot;
    Vessel _skiff;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Dictionary<string, string> _args = new();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) _args[arg[2..eq]] = arg[(eq + 1)..];
        }
        ProcessPhysicsPriority = 100; // after the game has stepped
        ProcessPriority = 101; // after the world below is drawn (Spaceflight is 100)
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try
        {
            more = _run.MoveNext();
            if (more) WatchTicks();
        }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        Engine.TimeScale = 1;
        Report();
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        SetProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    bool InBand => _game?.InOrbit == true && _game.Spaceflight.Aligned && _game.Spaceflight.Band < 1f && _game.Orbit?.Anchor != null;

    // ---- Watched all the way ---------------------------------------------------------------------

    int _frames, _ticks, _touched, _solidUnder;
    float _worstHeight, _worstUp, _leastClear = float.MaxValue;
    string _firstSolid, _firstTouch;
    float _worstOver = float.MinValue;
    int _relaid;
    N.Vector3 _shiftWas;

    /// <summary>Every drawn frame in the band: the sheet stands from your true height and up, and the relief it stands up under you lies below you.</summary>
    public override void _Process(double delta)
    {
        if (!InBand || _skiff == null) return;
        Celestial o = _game.Orbit.Anchor;
        if (o.Globe is not { Visible: true } globe) return;
        WorldView d = globe.Drawn;
        if (d.Height <= 2f) return;
        _frames++;
        _worstHeight = MathF.Max(_worstHeight, MathF.Abs(d.Height - o.Height(d.Eye)));
        _worstUp = MathF.Max(_worstUp, Mathf.RadToDeg(d.Up.AngleTo(d.Eye - o.Centre)));
        // The ground the sheet stands up under the Skiff, from the map's relief there at the share it is drawn: under it.
        Vector3 at = _skiff.Body.Position.ToGodot();
        var (x, z) = o.Map.MapPoint(o.ToGlobe(at.ToNumerics()));
        var map = _game.Surface.Planet.Map;
        float ground = MathF.Max(map.Sample(map.Elevation, x, z), 0f) * d.Relief;
        _leastClear = MathF.Min(_leastClear, o.Height(at) - ground);
    }

    /// <summary>Every tick in orbit: whatever is solid under the Skiff is the ground the map has at that place on the globe, and the Skiff touches nothing.</summary>
    void WatchTicks()
    {
        if (_game?.InOrbit != true || _skiff == null || _game.Orbit.Anchor is not { } o) return;
        _ticks++;
        if (o.Shift != _shiftWas) (_relaid, _shiftWas) = (_relaid + 1, o.Shift);
        N.Vector3 p = _skiff.Body.Position;
        float h = o.Height(p.ToGodot());
        N.Vector3 down = N.Vector3.Normalize(o.Centre.ToNumerics() - p);
        if (_game.Orbit.Physics.Query.Ray(p + down * 4f, p + down * (h + 50f), Layers.ProbeMask, _skiff.Body, out RayHit hit))
        {
            float under = o.Height(hit.Point.ToGodot());
            var (x, z) = o.Map.MapPoint(o.ToGlobe(hit.Point));
            float ground = MathF.Max(_game.Surface.Planet.GroundHeight(x, z), 0f); // the land's own height there, as its mesh is made
            _worstOver = MathF.Max(_worstOver, under - ground);
            if (under > ground + 10f || h - under < 50f)
            {
                _solidUnder++;
                _firstSolid ??= $"at {h:F0} m something solid ({hit.Surface}) stood {under:F0} m over sea level under the Skiff, the map's ground there {ground:F0} m";
            }
        }
        if (_skiff.Body.OnFloor || _skiff.Body.ParkedOn != null)
        {
            _touched++;
            _firstTouch ??= $"at {h:F0} m the Skiff stood on {_skiff.Body.ParkedOn?.ToString() ?? "a floor"}";
        }
    }

    void Report()
    {
        Check(_frames > 300 && _worstHeight < 0.05f && _worstUp < 0.05f,
            $"in the band the sheet stands from the readout's height and the up under you: {_frames} frames, at worst {_worstHeight:F3} m and {_worstUp:F3}° off (laid out again under you {_relaid} times)");
        Check(_frames > 300 && _leastClear > 50f,
            $"all the relief the sheet stands up lies under the Skiff: its least clearance over the drawn ground under it {_leastClear:F0} m (the world's highest ground {_game?.Spaceflight.Highest:F0} m)");
        Check(_ticks > 600 && _solidUnder == 0, $"whatever is solid under the Skiff in orbit is the map's ground at that place on the globe, well under it: {_solidUnder} wrong of {_ticks} ticks, at most {_worstOver:F1} m over the land's own height there{(_firstSolid != null ? ": " + _firstSolid : "")}");
        Check(_ticks > 600 && _touched == 0, $"the Skiff touched nothing in orbit: {_touched} of {_ticks} ticks{(_firstTouch != null ? ": " + _firstTouch : "")}");
    }

    // ---- The flight ------------------------------------------------------------------------------

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        _pilot = new Pilot(_game, _drive);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        _game.Zone.Sky.TimeOfDay = 0.45;
        GD.Print($"world: {_game.World}");
        foreach (int x in _pilot.BuildSkiff(s => _skiff = s)) yield return x;
        if (!Check(_skiff != null, "the Skiff is built")) yield break;
        foreach (int x in _pilot.Board(_skiff)) yield return x;
        // Piloted from the external chase camera, as the owner flies (V), never first person or a free camera.
        if (!_game.ThirdPerson) foreach (int x in _drive.Tap(Key.V)) yield return x;
        if (!Check(_game.ThirdPerson && _game.FixedCamera == null, "the Skiff is flown from the external chase camera")) yield break;

        Hurry(4);
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 150 && !_game.InOrbit; i++) yield return 0;
        _game.Controls.ReleaseAll();
        if (!Check(_game.InOrbit, "the Skiff climbed through the crossing into orbit")) yield break;

        // The world's highest ground, and the way round the globe to it, low through the band.
        Celestial o = _game.Orbit.Anchor;
        var map = _game.Surface.Planet.Map;
        int top = Enumerable.Range(0, map.Count).MaxBy(k => map.Elevation[k]);
        N.Vector3 peak = o.Map.Direction(map.CellX(top % map.Width), map.CellY(top / map.Width));
        N.Vector3 from = N.Vector3.Normalize(o.ToGlobe(_skiff.Body.Position));
        float arc = MathF.Acos(Math.Clamp(N.Vector3.Dot(from, peak), -1f, 1f));
        GD.Print($"  the highest ground, {map.Elevation[top]:F0} m, lies {Mathf.RadToDeg(arc):F0}° round the globe (radius {o.GlobeRadius:F0} m)");
        Vector3 Over(N.Vector3 dir, float height) => _game.Orbit.Anchor.ToWorld(dir * (_game.Orbit.Anchor.GlobeRadius + height)).ToGodot();
        int legs = Math.Max(1, (int)MathF.Ceiling(Mathf.RadToDeg(arc) / 6f));
        for (int i = 1; i < legs; i++)
        {
            N.Vector3 dir = Slerp(from, peak, i / (float)legs);
            foreach (int x in _pilot.FlyTo(_skiff, () => Over(dir, Cruise), 80f, 90f, through: true)) yield return x;
        }
        foreach (float height in Heights)
        {
            foreach (int x in _pilot.FlyTo(_skiff, () => Over(peak, height), 12f, 120f)) yield return x;
            Hurry(1);
            // Level over the peak, the nose a little down the way it came, so the mountains stand in the picture.
            Vector3 up = (_skiff.Body.Position.ToGodot() - _game.Orbit.Anchor.Centre).Normalized();
            Vector3 ahead = (Over(peak, height) - Over(from, height)).Slide(up).Normalized();
            foreach (int x in _pilot.TurnTo(_skiff, (ahead - up * 0.35f).Normalized(), up, 2f, 20f)) yield return x;
            for (int i = 0; i < 60; i++) yield return 0;
            float h = _game.Orbit.Anchor.Height(_skiff.Body.Position.ToGodot());
            Check(MathF.Abs(h - height) < 30f, $"the Skiff stopped over the highest ground at {h:F0} m ({height:F0} m wanted)");
            Shot($"{height:F0}");
            Hurry(4);
        }
        Hurry(1);
    }

    static N.Vector3 Slerp(N.Vector3 a, N.Vector3 b, float t)
    {
        float angle = MathF.Acos(Math.Clamp(N.Vector3.Dot(a, b), -1f, 1f));
        if (angle < 1e-4f) return b;
        return N.Vector3.Normalize((a * MathF.Sin((1f - t) * angle) + b * MathF.Sin(t * angle)) / MathF.Sin(angle));
    }

    /// <summary>Faster than life, printed: for the long flight round the globe only.</summary>
    void Hurry(double scale)
    {
        if (Engine.TimeScale == scale) return;
        Engine.TimeScale = scale;
        GD.Print($"  (game time runs {scale}x for this stretch)");
    }

    /// <summary>Windowed with --shots=folder: what is on the screen now.</summary>
    void Shot(string tag)
    {
        if (DisplayServer.GetName() == "headless" || !_args.TryGetValue("shots", out string folder)) return;
        System.IO.Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, $"orbitrelief-{tag}.png");
        Error e = _game.GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"  (screenshot {path}: {e})");
    }

    bool Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
        return ok;
    }

    void Fail(string what) => _ = Check(false, what);
}
