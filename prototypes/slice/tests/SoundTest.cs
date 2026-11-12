using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Structures;
using Godot;
using Slice.Game;
using Slice.Surface;
using Weather.Model;
using WeatherKind = Weather.Model.WeatherKind;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The weather's sound where you stand: rain and wind follow the local weather and your exposure
/// (open ground, ridge, hollow, lee, under a roof, in a shut room), and an opening howls only when
/// it's windy. Levels are measured, never listened to.
///
/// Run: --headless --fixed-fps 60 --path prototypes/slice res://tests/SoundTest.tscn
/// </summary>
public partial class SoundTest : Node
{
    const int WatchdogMinutes = 12;
    Game.Game _game;
    Playthrough _play;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
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
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    SurfaceZone Zone => _game.Zone;
    Slice.Player.Survivor Me => _game.Survivor;
    SliceAudio Audio => _game.Audio;

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = false, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _play = new Playthrough(_game);
        _drive = _play.Drive;
        while (_game.Phase != Phase.Playing) yield return 0;
        Zone.Weather.Bridge.Calm = true;
        Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Zone.Wildlife.Frozen = true;
        foreach (int x in _drive.UntilPlaying()) yield return x;
        foreach (int x in _play.WakeAndLeave((_, _) => { })) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;

        Formulas();
        foreach (int x in Outdoors()) yield return x;
        foreach (int x in Indoors()) yield return x;
    }

    /// <summary>The mixing rules alone: every ordering the owner asked for.</summary>
    void Formulas()
    {
        float open = AmbientExposure.RainLevel(0.8f, 0f, 0f, false, false);
        float roof = AmbientExposure.RainLevel(0.8f, 1f, 0.5f, false, false);
        Check(open > 0.7f && roof < open * 0.4f && roof > 0f, $"rain: full in the open ({open:F2}), muffled under a roof ({roof:F2})");
        Check(AmbientExposure.RainLevel(0.8f, 1f, 1f, true, false) == 0f && AmbientExposure.RainLevel(0f, 0f, 0f, false, false) == 0f, "rain: none in a shut room, none when none falls");
        Check(AmbientExposure.RainLevel(0.8f, 0f, 0f, false, true) < open, "rain: quieter in a cockpit");
        float calm = AmbientExposure.WindLevel(2f, 0f, 0f, 0f, 0f, false, false), gale = AmbientExposure.WindLevel(25f, 0f, 0f, 0f, 0f, false, false);
        Check(calm < 0.1f && gale > 0.7f, $"wind: calm {calm:F2}, a gale {gale:F2}");
        float ridge = AmbientExposure.WindLevel(15f, 0f, 0f, 25f, 0f, false, false), flat = AmbientExposure.WindLevel(15f, 0f, 0f, 0f, 0f, false, false);
        float hollow = AmbientExposure.WindLevel(15f, 0f, 0f, -25f, 0f, false, false), lee = AmbientExposure.WindLevel(15f, 0f, 0f, 0f, 1f, false, false);
        Check(ridge > flat * 1.2f && hollow < flat * 0.7f && lee < flat * 0.6f, $"wind: ridge {ridge:F2} > open {flat:F2} > hollow {hollow:F2}, lee {lee:F2}");
        float indoors = AmbientExposure.WindLevel(25f, 1f, 1f, 0f, 0f, false, false);
        Check(indoors < gale * 0.15f && AmbientExposure.WindLevel(25f, 1f, 1f, 0f, 0f, true, false) == 0f, $"wind: muffled indoors ({indoors:F2} of {gale:F2}), gone in a shut room");
        Check(WindHowl.Loudness(3f) == 0f && WindHowl.Loudness(30f) > WindHowl.Loudness(12f) && WindHowl.Loudness(12f) > 0f, "howl: silent in a calm, louder as the wind rises");
    }

    IEnumerable<int> Settle(int frames)
    {
        foreach (int x in _drive.Wait(frames)) yield return x;
    }

    /// <summary>Out on the ground: rain and wind follow the weather and the lie of the land.</summary>
    IEnumerable<int> Outdoors()
    {
        Vector3 spot = Open(Me.WorldFeet);
        Me.PlaceOutside((spot + Vector3.Up * 0.3f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        Weather.Model.WeatherSim sky = Zone.Weather.Sim;
        AmbientExposure ex = _game.Exposure;
        ex.Held = false;

        sky.Snap(WeatherKind.Clear);
        foreach (int x in Settle(240)) yield return x;
        float calmWind = Audio.WindLevel;
        Check(calmWind < 0.25f && Audio.RainLevel < 0.01f && Zone.Sfx.RainDb <= -79f, $"a clear calm day: wind {calmWind:F2} ({Zone.Sfx.WindDb:F0} dB), rain silent ({Zone.Sfx.RainDb:F0} dB)");

        sky.Snap(WeatherKind.Monsoon);
        foreach (int x in Settle(300)) yield return x;
        float rain = Audio.RainLevel;
        Check(ex.Cover < 0.3f && ex.Walled < 0.4f, $"open ground reads open: cover {ex.Cover:F2}, walls {ex.Walled:F2}, relief {ex.Relief:F1} m, lee {ex.Lee:F2}");
        Check(rain > 0.15f && Zone.Sfx.RainDb > -45f, $"in the open a monsoon is loud: rain {rain:F2} ({Zone.Sfx.RainDb:F0} dB), here {Zone.Weather.RainHere:F2}");
        ex.Set(1f, 0.3f, 0f, 0f);
        foreach (int x in Settle(180)) yield return x;
        float roofed = Audio.RainLevel;
        Check(roofed < rain * 0.45f && roofed > 0f, $"under a roof it is muffled: {roofed:F2} against {rain:F2} ({Zone.Sfx.RainDb:F0} dB)");
        ex.Held = false;

        sky.Snap(WeatherKind.Hurricane);
        foreach (int x in Settle(240)) yield return x;
        ex.Set(0f, 0f, 0f, 0f);
        foreach (int x in Settle(180)) yield return x;
        float flat = Audio.WindLevel;
        Check(flat > 0.5f && flat > calmWind * 3f, $"in a gale on open ground wind is loud: {flat:F2} ({Zone.Sfx.WindDb:F0} dB) against {calmWind:F2} in the calm");
        ex.Set(0f, 0f, 25f, 0f);
        foreach (int x in Settle(180)) yield return x;
        float ridge = Audio.WindLevel;
        ex.Set(0f, 0f, -25f, 0f);
        foreach (int x in Settle(180)) yield return x;
        float hollow = Audio.WindLevel;
        ex.Set(0f, 0f, 0f, 1f);
        foreach (int x in Settle(180)) yield return x;
        float lee = Audio.WindLevel;
        ex.Set(1f, 0.8f, 0f, 0f);
        foreach (int x in Settle(180)) yield return x;
        float indoors = Audio.WindLevel;
        Check(ridge > flat && hollow < flat * 0.7f && lee < flat * 0.6f && indoors < flat * 0.25f, $"ridge {ridge:F2} > open {flat:F2} > hollow {hollow:F2}, lee {lee:F2}, roofed and walled {indoors:F2}");
        ex.Held = false;
        sky.Snap(WeatherKind.Clear);
        foreach (int x in Settle(120)) yield return x;
    }

    Vector3 Open(Vector3 near)
    {
        for (float r = 0f; r < 400f; r += 12f)
        for (int i = 0; i < 16; i++)
        {
            Vector3 g = Zone.GroundAt(near.X + MathF.Cos(i * 0.39f) * r, near.Z + MathF.Sin(i * 0.39f) * r);
            if (g.Y > 1.5f && !Zone.Water.LiquidAt(g.X, g.Z, out _) && _game.Places?.PlaceAt(g) == null) return g;
        }
        return Zone.GroundAt(near.X, near.Z);
    }

    /// <summary>A shut steel room in a monsoon and a gale: nothing is heard. Then a gap in its wall: the wind howls there, only when it blows.</summary>
    IEnumerable<int> Indoors()
    {
        Vector3 feet = Me.WorldFeet;
        Weather.Model.WeatherSim sky = Zone.Weather.Sim;
        AmbientExposure ex = _game.Exposure;
        ex.Held = false;
        foreach (bool gap in new[] { false, true })
        {
            var text = new System.Text.StringBuilder("schema galhack.blueprint
version 1
name \"Shut\"
grid large
mode base
gravity off
");
            for (int y = 0; y <= 4; y++)
            for (int z = -5; z <= 5; z++)
            for (int x = -5; x <= 5; x++)
                if ((y == 0 || y == 4 || Math.Abs(x) == 5 || Math.Abs(z) == 5) && !(gap && x == 5 && Math.Abs(z) <= 1 && y >= 1 && y <= 3)) text.Append($"block {x} {y} {z} steel
");
            text.Append("block 4 1 4 core
block -4 1 -4 generator
");
            var bp = Blueprint.Load(text.ToString(), Zone.Data.Catalogue, out List<string> errors);
            Check(bp != null, $"the room's blueprint loads {string.Join("; ", errors)}");
            Vector3 site = Zone.GroundAt(feet.X + (gap ? 80f : 30f), feet.Z);
            Slice.Vessels.Vessel room = Slice.Vessels.Vessel.SpawnBase(Zone, bp, (site + Vector3.Up * 4f).ToNumerics(), 0f);
            Me.PlaceOutside(room.ToWorld(new N.Vector3(0f, 3f, 0f)).ToNumerics());
            Zone.Light.Touch();
            foreach (int x in _drive.UntilSettled()) yield return x;
            sky.Snap(WeatherKind.Monsoon);
            foreach (int x in Settle(300)) yield return x;
            ex.Survey();
            if (!gap)
            {
                Check(ex.Cover > 0.8f && ex.Walled > 0.8f, $"in the room the survey finds a roof and walls: cover {ex.Cover:F2}, walls {ex.Walled:F2}");
                Check(Audio.RainLevel < 0.01f && Zone.Sfx.RainDb <= -79f, $"shut in, a monsoon is silent: rain {Audio.RainLevel:F2} ({Zone.Sfx.RainDb:F0} dB)");
                sky.Snap(WeatherKind.Hurricane);
                foreach (int x in Settle(240)) yield return x;
                Check(Audio.WindLevel < 0.01f && Zone.Sfx.WindDb <= -79f && _game.Howl.Level < 0.05f, $"and a gale outside is silent: wind {Audio.WindLevel:F2} ({Zone.Sfx.WindDb:F0} dB), howl {_game.Howl.Level:F2}");
            }
            else
            {
                sky.Snap(WeatherKind.Hurricane);
                foreach (int x in Settle(300)) yield return x;
                float gale = _game.Howl.Level;
                Check(_game.Howl.Opening != null && gale > 0.3f, $"a gap in the wall moans in a gale: level {gale:F2}, opening {(_game.Howl.Opening != null)}");
                sky.Snap(WeatherKind.Clear);
                foreach (int x in Settle(300)) yield return x;
                Check(_game.Howl.Level < 0.05f, $"and is quiet once it calms: {_game.Howl.Level:F2}");
            }
            sky.Snap(WeatherKind.Clear);
            Me.PlaceOutside((feet + Vector3.Up * 0.5f).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
        }
    }
}
