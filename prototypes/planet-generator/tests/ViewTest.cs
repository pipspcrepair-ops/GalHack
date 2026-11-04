using System;
using System.Diagnostics;
using Godot;
using Planets.Gen;

namespace Planets;

/// <summary>
/// Headless smoke test of the views. Run with:
///   godot --headless --path prototypes/planet-generator res://tests/ViewTest.tscn
/// Lands, flies fast north-east across the corner where both seams meet while terrain streams,
/// returns to orbit, switches planet and lands again. Exits 0 if everything settles in time and the main thread never
/// stalls, 1 otherwise. (Headless means no GPU: this checks streaming and stability, not frame rate.)
/// </summary>
public partial class ViewTest : Node
{
    const double StepTimeout = 60;
    const double FlySeconds = 6, FlySpeed = 250; // m/s: boost speed, faster than anyone flies for long

    Main _main;
    int _stage, _failures;
    double _stageTime, _flown, _worstFrameMs;
    long _lastTicks;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        _main = new Main();
        AddChild(_main);
        _main.SetSpec(PlanetSpec.Roll(4242, Archetype.Temperate, 1, 8));
    }

    public override void _Process(double delta)
    {
        // Main-thread time between frames: chunk uploads and everything else must stay small.
        long now = _clock.ElapsedTicks;
        if (_lastTicks != 0 && _stage is 2 or 3) _worstFrameMs = Math.Max(_worstFrameMs, (now - _lastTicks) * 1000.0 / Stopwatch.Frequency);
        _lastTicks = now;
        _stageTime += delta;
        if (_stageTime > StepTimeout)
        {
            Fail($"stage {_stage} did not finish within {StepTimeout} s");
            Finish();
            return;
        }

        switch (_stage)
        {
            case 0: // planet generated
                if (_main.Busy) return;
                _main.Land();
                Next(_main.Surface != null, "landed");
                break;
            case 1: // initial terrain streamed in
                if (!_main.Surface.Streamer.Settled) return;
                Pass($"terrain around the landing site settled in {_stageTime:F1} s ({_main.Surface.Streamer.ChunkCount} chunks)");
                // Start just south-west of the corner and fly north-east across both seams at once.
                _main.Surface.PlaceCamera(_main.Map.WidthMeters - 300, 300, 40f, MathF.PI / 4, 0f);
                Next(true, null);
                break;
            case 2: // fly north-east at boost speed, crossing x = 0 and z = 0
                _flown += delta;
                Vector3 p = _main.Surface.Camera.Position;
                _main.Surface.Camera.Position = p + new Vector3(1f, 0f, -1f).Normalized() * (float)(FlySpeed * delta);
                if (_flown < FlySeconds) return;
                float x = _main.Surface.Camera.Position.X, z = _main.Surface.Camera.Position.Z;
                Check(x >= 0 && x < _main.Map.WidthMeters * 0.5f && z > _main.Map.HeightMeters * 0.5f && z < _main.Map.HeightMeters,
                    $"crossed both seams and wrapped (camera at x = {x:F0} m, z = {z:F0} m)");
                Next(true, null);
                break;
            case 3: // streaming catches up after the flight
                if (!_main.Surface.Streamer.Settled) return;
                Pass($"streaming caught up {_stageTime:F1} s after a {FlySeconds * FlySpeed / 1000:F1} km flight");
                Check(_worstFrameMs < 50, $"main thread never stalled while streaming (worst frame {_worstFrameMs:F1} ms)");
                _main.ReturnToOrbit();
                _main.SetSpec(PlanetSpec.Roll(99, Archetype.Lava, 0, 16));
                Next(_main.Surface == null, "returned to orbit");
                break;
            case 4: // a different planet, landed on again
                if (_main.Busy) return;
                _main.Land();
                Next(_main.Surface != null, $"generated a {_main.Map.Spec.Archetype} planet and landed again");
                break;
            case 5:
                if (!_main.Surface.Streamer.Settled) return;
                Pass($"second landing settled in {_stageTime:F1} s");
                _main.ReturnToOrbit();
                Finish();
                break;
        }
    }

    void Next(bool ok, string what)
    {
        if (what != null) Check(ok, what);
        _stage++;
        _stageTime = 0;
    }

    void Finish()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
        SetProcess(false);
    }

    void Check(bool ok, string what)
    {
        if (ok) Pass(what);
        else Fail(what);
    }

    static void Pass(string what) => GD.Print($"PASS  {what}");

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
