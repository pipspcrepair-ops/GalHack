using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Game;
using Slice.Ui;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The markers through a slow turn, in a window (it measures drawn frames; see MarkerSweep.manual). On a fresh random
/// world the castaway climbs out, map marks go down round them at several distances, and the view turns through
/// 360 degrees level, then through 360 more while it pans up and down. After every drawn frame, each marker the visor
/// drew is held against the real camera's projection of its target (<c>Camera3D.UnprojectPosition</c>):
/// an unpinned, unnudged marker sits within <see cref="Tolerance"/> px of it; no marker moves more than its target's
/// projection moved since the last frame, plus <see cref="Tolerance"/> px; and a pinned marker never goes there and
/// back across frames. Prints the worst of each. Also checks the options toggle applies live.
/// </summary>
public partial class MarkerSweep : Node
{
    const float Tolerance = 2f;
    const float DegPerTick = 1f;

    Game.Game _game;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Dictionary<string, (Vector2 at, Vector2 proj, bool pinned)> _last = new();
    readonly Dictionary<string, Vector2> _lastAt2 = new();
    float _worstOff, _worstStep, _worstFlip;
    string _worstOffWho = "", _worstStepWho = "";
    int _frames, _sampled, _flips, _pinnedSamples, _steps;
    bool _measuring;
    string _phase = "";

    public override void _Ready()
    {
        new System.Threading.Thread(() => { System.Threading.Thread.Sleep(TimeSpan.FromMinutes(12)); Console.Error.WriteLine("FAIL  watchdog"); System.Environment.Exit(1); }) { IsBackground = true }.Start();
        ProcessPhysicsPriority = 100;
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("MarkerSweep needs a window: run it without --headless");
            GetTree().Quit(1);
            return;
        }
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        RenderingServer.Singleton.FramePostDraw += Sample;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_run == null) return;
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e) { Fail($"scenario threw: {e}"); more = false; }
        if (more) return;
        _run = null;
        RenderingServer.Singleton.FramePostDraw -= Sample;
        Report();
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) { _passes++; GD.Print($"PASS  {what}"); }
        else Fail(what);
    }

    void Fail(string what) { _failures++; GD.PrintErr($"FAIL  {what}"); }

    IEnumerable<int> Scenario()
    {
        var drive = new Driver(_game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        foreach (int x in drive.ClimbOut(_game.Bunker)) yield return x;
        _game.Zone.Sky.TimeOfDay = 0.36;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        Vector3 eye = _game.Survivor.WorldEye();
        var rng = new Random();
        float a0 = (float)(rng.NextDouble() * Math.Tau);
        float[] ranges = { 25f, 60f, 110f, 180f, 300f, 600f, 900f };
        for (int i = 0; i < ranges.Length; i++)
        {
            float a = a0 + i * 0.9f + (float)rng.NextDouble() * 0.3f;
            _game.Spaceflight.Charts.Mark(eye.X + MathF.Cos(a) * ranges[i], eye.Z + MathF.Sin(a) * ranges[i]);
        }
        _game.Scan();
        foreach (int x in drive.Wait(30)) yield return x;
        GD.Print($"{_game.Visor.Markers().Count} markers");
        // Level: a slow full turn.
        _phase = "level";
        _measuring = true;
        for (int t = 0; t < (int)(360 / DegPerTick); t++)
        {
            Aim(0f, DegPerTick);
            yield return 0;
        }
        // Panning: another turn with the pitch swinging up and down.
        _phase = "pan";
        for (int t = 0; t < (int)(360 / DegPerTick); t++)
        {
            Aim(Mathf.DegToRad(40f * MathF.Sin(t / 360f * MathF.Tau * 3f)), DegPerTick);
            yield return 0;
        }
        _measuring = false;
        foreach (int x in Toggle()) yield return x;
    }

    void Aim(float pitch, float yawDeg)
    {
        const float sensitivity = 0.0025f;
        _game.Controls.Look = new Vector2(-Mathf.DegToRad(yawDeg) / sensitivity, -(pitch - _game.Survivor.Pitch) / sensitivity);
    }

    /// <summary>After every drawn frame: what the visor drew against the camera.</summary>
    void Sample()
    {
        if (!_measuring) return;
        Camera3D cam = _game.Camera;
        var drawn = _game.Visor.Drawn;
        _frames++;
        var seen = new HashSet<string>();
        foreach (Visor.PlacedMarker p in drawn)
        {
            string key = p.Mark.Kind + p.Mark.Tag + p.Mark.At.X.ToString("F1");
            seen.Add(key);
            bool behind = cam.IsPositionBehind(p.Mark.At);
            Vector2 proj = cam.UnprojectPosition(p.Mark.At);
            Vector2 at = p.Icon.GetCenter();
            _sampled++;
            bool free = !p.Pinned && !p.Nudged && !behind;
            if (free)
            {
                float off = at.DistanceTo(proj);
                if (off > _worstOff) { _worstOff = off; _worstOffWho = $"{p.Mark.Tag} {_phase} frame {_frames}"; }
            }
            if (free && _last.TryGetValue(key, out var was) && !was.pinned)
            {
                _steps++;
                float step = at.DistanceTo(was.at) - proj.DistanceTo(was.proj);
                if (step > _worstStep) { _worstStep = step; _worstStepWho = $"{p.Mark.Tag} {_phase} frame {_frames}"; }
            }
            if (p.Pinned)
            {
                _pinnedSamples++;
                // Back and forth: two frames ago and now are the same place, and the frame between was far from both.
                if (_lastAt2.TryGetValue(key, out Vector2 two) && _last.TryGetValue(key, out var one) && one.pinned)
                {
                    float there = one.at.DistanceTo(at);
                    if (at.DistanceTo(two) < Tolerance && there > 8f) { _flips++; _worstFlip = MathF.Max(_worstFlip, there); }
                }
            }
            if (_last.TryGetValue(key, out var prev)) _lastAt2[key] = prev.at; else _lastAt2.Remove(key);
            _last[key] = (at, proj, p.Pinned);
        }
        foreach (string gone in _last.Keys.Where(k => !seen.Contains(k)).ToList()) { _last.Remove(gone); _lastAt2.Remove(gone); }
    }

    IEnumerable<int> Toggle()
    {
        _game.Settings.MarkersInWorld = false;
        _game.ApplySettings();
        yield return 0;
        yield return 0;
        Check(!_game.Visor.MarkersInWorld && _game.Visor.Drawn.Count == 0, "compass mode: nothing drawn in the world, applied live");
        _game.Settings.MarkersInWorld = true;
        _game.ApplySettings();
        yield return 0;
        yield return 0;
        Check(_game.Visor.MarkersInWorld && _game.Visor.Drawn.Count > 0, "world mode: drawn again, applied live");
    }

    void Report()
    {
        GD.Print($"frames {_frames}, markers sampled {_sampled} ({_steps} frame pairs), pinned {_pinnedSamples}");
        GD.Print($"worst off the camera's projection: {_worstOff:F2} px ({_worstOffWho})");
        GD.Print($"worst step beyond the target's own: {_worstStep:F2} px ({_worstStepWho})");
        GD.Print($"pinned back-and-forth: {_flips} (worst {_worstFlip:F1} px)");
        Check(_frames > 100, "frames were drawn");
        Check(_sampled > 100, "markers were on screen");
        Check(_worstOff <= Tolerance, $"each marker within {Tolerance} px of the projection of its target");
        Check(_worstStep <= Tolerance, $"no marker moves more than its target plus {Tolerance} px");
        Check(_flips == 0, "no pinned marker flips back and forth");
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks)" : $"{_failures} CHECK(S) FAILED");
    }
}
