using System;
using Godot;
using Slice.Items;
using Slice.Vessels;

namespace Slice.Game;

/// <summary>
/// The slice's sound, all synthesised in code (no audio files): the modules' own synths compiled
/// in from where they live (planet-surface's digging, ore, clank, place, boom, thunder and its
/// wind and rain; building's place, remove, upgrade and UI clicks; diegetic-ui's chirp; wildlife's
/// calls), plus three small loops of the slice's own (the jetpack's hiss, a vessel's engines,
/// footsteps). One master volume, set in the Stats tab's options; everything is mixed well under
/// it, so nothing is harsh. Players are stopped before quitting (wildlife's shutdown drains them).
/// </summary>
public partial class SliceAudio : Node
{
    public Game Game;
    BlockBuilding.Sfx _ui;
    AudioStreamPlayer _jet, _engine, _under;
    bool _muffled;
    AudioStreamPlayer3D _step;
    AudioStreamWav _chirp;
    float _stepTimer;
    int _shots, _placed, _taken;

    /// <summary>The master volume, 0 to 1 (the options).</summary>
    public float Master
    {
        get => Mathf.DbToLinear(AudioServer.GetBusVolumeDb(0));
        set => AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Clamp(value, 0.0001f, 1f)));
    }

    public override void _Ready()
    {
        _ui = new BlockBuilding.Sfx { Name = "BuildingSfx" };
        AddChild(_ui);
        _chirp = DiegeticUi.World.Sfx.WarningChirp();
        _jet = Loop("Jetpack", Noise(0.6f, 1600f, 0.18f), -16f);
        _engine = Loop("Engines", Noise(0.8f, 420f, 0.3f), -14f);
        _under = Loop("Underwater", Noise(1.4f, 160f, 0.35f), -40f);
        _step = new AudioStreamPlayer3D { Name = "Steps", Stream = Tap(), VolumeDb = -20f, MaxDistance = 20f };
        AddChild(_step);
        Master = 0.8f;
        Game.Menu.Sounded += OnMenu;
    }

    AudioStreamPlayer Loop(string name, AudioStreamWav stream, float db)
    {
        var p = new AudioStreamPlayer { Name = name, Stream = stream, VolumeDb = db };
        AddChild(p);
        return p;
    }

    /// <summary>The Tab menu's own chirps: open, close, tab, drop, queued, unlock, refused.</summary>
    void OnMenu(string what)
    {
        switch (what)
        {
            case "open": _ui.PlayUi("toggle", 1.3f); break;
            case "close": _ui.PlayUi("toggle", 0.9f); break;
            case "tab": _ui.PlayUi("click", 1.2f); break;
            case "drop": _ui.PlayUi("click", 0.8f); break;
            case "queued": _ui.PlayUi("toggle", 1.6f); break;
            case "unlock": _ui.PlayUi("upgrade", 1.1f); break;
            case "refused": _ui.PlayUi("error"); break;
            default: _ui.PlayUi("click"); break;
        }
    }

    /// <summary>A crafting run finished: diegetic-ui's chirp, softly.</summary>
    public void CraftDone() => Play2D(_chirp, -14f, 1.4f);

    void Play2D(AudioStream stream, float db, float pitch)
    {
        var p = new AudioStreamPlayer { Stream = stream, VolumeDb = db, PitchScale = pitch };
        AddChild(p);
        p.Play();
        p.Finished += p.QueueFree;
    }

    public override void _Process(double delta)
    {
        Game g = Game;
        if (g?.Survivor == null || g.Phase != Phase.Playing) return;
        float dt = (float)delta;
        // Weather on the surface: planet-surface's wind and rain, muffled inside a structure.
        var w = g.Zone.Weather;
        float shelter = g.Survivor.Aboard != null && !g.Survivor.OnOuterHull ? 0.8f : g.Survivor.Seated != null ? 0.6f : 0f;
        shelter = Mathf.Max(shelter, g.Spaceflight.AirGone); // the wind thins away with the air
        // Under water everything comes through it, dull and far off; the weather not at all.
        bool under = g.Zone.Underwater?.CameraUnder == true;
        Muffle(under);
        Fade(_under, under, -20f, dt);
        g.Zone.Sfx.SetWeather(under ? 0f : w.Wind2D.Length(), under ? 0f : w.Sim?.Current.Rain ?? 0f, shelter);

        // The jetpack hisses while it pushes; a vessel's engines rumble with its thrust.
        bool jetting = g.Survivor.Seated == null && !g.Survivor.Body.OnFloor && g.Survivor.Legs.Jetpack && !g.Suit.Dead;
        Fade(_jet, jetting, -18f, dt);
        bool engines = g.Survivor.Seated is Vessel v && v.Flight != null && v.Controller?.Frozen == false;
        Fade(_engine, engines, -15f, dt);
        if (engines) _engine.PitchScale = 0.8f + Mathf.Min(0.6f, ((Vessel)g.Survivor.Seated).Body.Velocity.Length() / 120f);

        // Footsteps on the ground: a soft tap per stride.
        float speed = g.Survivor.Body.OnFloor ? new Vector2(g.Survivor.Body.Velocity.X, g.Survivor.Body.Velocity.Z).Length() : 0f;
        if (speed > 0.8f && g.Survivor.Seated == null)
        {
            _stepTimer -= dt * speed;
            if (_stepTimer <= 0f)
            {
                _stepTimer = 1.6f;
                _step.GlobalPosition = g.Survivor.WorldFeet;
                _step.PitchScale = 0.85f + (float)GD.RandRange(0, 0.3);
                _step.Play();
            }
        }

        // Shots, placements and take-downs, from the tools' counters.
        if (g.Tool.Shots != _shots) { _shots = g.Tool.Shots; _ui.Play("zap", g.Tool.Muzzle(), 1.3f, -10f); }
        if (g.Builder.Placed != _placed) { _placed = g.Builder.Placed; _ui.Play("place", g.Survivor.WorldEye(), 1f, -8f); }
        if (g.Builder.TakenDown != _taken) { _taken = g.Builder.TakenDown; _ui.Play("remove", g.Survivor.WorldEye(), 1f, -8f); }
    }

    /// <summary>A low-pass on the master bus, there once (a new game reloads the scene, the bus stays) and on only under water.</summary>
    void Muffle(bool on)
    {
        if (on == _muffled) return;
        _muffled = on;
        int found = -1;
        for (int i = 0; i < AudioServer.GetBusEffectCount(0); i++)
            if (AudioServer.GetBusEffect(0, i) is AudioEffectLowPassFilter) found = i;
        if (found < 0)
        {
            AudioServer.AddBusEffect(0, new AudioEffectLowPassFilter { CutoffHz = 520f, Resonance = 0.6f });
            found = AudioServer.GetBusEffectCount(0) - 1;
        }
        AudioServer.SetBusEffectEnabled(0, found, on);
    }

    static void Fade(AudioStreamPlayer p, bool on, float db, float dt)
    {
        if (on && !p.Playing) { p.VolumeDb = -40f; p.Play(); }
        if (!p.Playing) return;
        p.VolumeDb = Mathf.MoveToward(p.VolumeDb, on ? db : -40f, dt * 60f);
        if (!on && p.VolumeDb <= -39f) p.Stop();
    }

    public void StopAll()
    {
        _jet.Stop();
        _engine.Stop();
        _under.Stop();
        _step.Stop();
        Muffle(false);
    }

    // ---- The slice's own small synths ----------------------------------------------------------

    const int Rate = 22050;

    /// <summary>A looping band of filtered noise: a hiss (high cut-off) or a rumble (low).</summary>
    static AudioStreamWav Noise(float seconds, float cutoff, float gain)
    {
        int n = (int)(seconds * Rate);
        var data = new byte[n * 2];
        var rng = new Random(11);
        float a = Mathf.Clamp(cutoff / Rate * Mathf.Tau, 0.001f, 1f), y = 0f;
        for (int i = 0; i < n; i++)
        {
            y += a * ((float)rng.NextDouble() * 2f - 1f - y);
            // Ends meet: fade the last 5% into the first.
            float fade = i > n * 0.95f ? (n - i) / (n * 0.05f) : 1f;
            short s = (short)(Math.Clamp(y * gain * 3f * fade, -1f, 1f) * 32767);
            data[2 * i] = (byte)s;
            data[2 * i + 1] = (byte)(s >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Data = data, LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopEnd = n };
    }

    /// <summary>A footstep: a short soft thud of low noise.</summary>
    static AudioStreamWav Tap()
    {
        int n = (int)(0.09f * Rate);
        var data = new byte[n * 2];
        var rng = new Random(5);
        float y = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            y += 0.12f * ((float)rng.NextDouble() * 2f - 1f - y);
            short s = (short)(Math.Clamp(y * MathF.Exp(-t * 45f) * 2.2f, -1f, 1f) * 32767);
            data[2 * i] = (byte)s;
            data[2 * i + 1] = (byte)(s >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Data = data };
    }
}
