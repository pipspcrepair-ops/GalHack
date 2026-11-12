using System;
using Godot;
using Slice.Items;
using Slice.Surface;
using Slice.Vessels;

namespace Slice.Game;

/// <summary>
/// The slice's sound, all synthesised in code (no audio files): the modules' own synths compiled
/// in from where they live (planet-surface's digging, ore, clank, place, boom, thunder and its
/// wind and rain; building's place, remove, upgrade and UI clicks; diegetic-ui's chirp; wildlife's
/// calls), plus the slice's own small voices (the jetpack's hiss and a vessel's engines, both synthesised live by <see cref="ThrusterVoice"/> with no loop to hear, and
/// footsteps), and the music (<see cref="SliceMusic"/>). Four volumes in the escape menu (and the Stats tab's settings),
/// remembered between runs: master, and music, effects and ambience as buses under it. Every player
/// that joins the tree is routed to its bus here, so nothing needs to know about them, wherever
/// the code that made it lives. Players are stopped before quitting (wildlife's shutdown drains them).
/// </summary>
public partial class SliceAudio : Node
{
    public Game Game;
    BlockBuilding.Sfx _ui;
    ThrusterPlayer _jet, _engine;
    Vessel _engineOf;
    AudioStreamPlayer _under;
    bool _muffled;
    AudioStreamPlayer3D _step;
    AudioStreamWav _chirp;
    float _stepTimer;
    int _shots, _placed, _taken;
    float _saveIn;

    /// <summary>The four volumes, as they are saved.</summary>
    public AudioSettings Settings { get; private set; }

    /// <summary>The music.</summary>
    public SliceMusic Music { get; private set; }

    static string SettingsPath => ProjectSettings.GlobalizePath("user://audio-settings.json");

    /// <summary>A volume, 0 to 1, by its key in <see cref="AudioSettings.Volumes"/>: applied now, saved a moment later.</summary>
    /// <summary>Turns the sound direction marker on or off (remembered).</summary>
    public void SetDirection(bool on)
    {
        Settings.SoundDirection = on;
        _saveIn = 1f;
    }

    public void SetVolume(string key, float value)
    {
        Settings.Set(key, value);
        Apply(key);
        _saveIn = 1f;
    }

    /// <summary>The bus a volume sets (master is the engine's own bus 0).</summary>
    static int Bus(string key) => key == AudioSettings.Master ? 0 : AudioServer.GetBusIndex(AudioSettings.BusName(key));

    void Apply(string key)
    {
        int bus = Bus(key);
        float value = Settings.Get(key);
        AudioServer.SetBusMute(bus, value <= 0f);
        float hush = key == AudioSettings.Effects || key == AudioSettings.Ambience ? _hushShown : 0f;
        AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Mathf.Clamp(value, 0.0001f, 1f)) - hush);
    }

    float _hushShown;

    /// <summary>
    /// How far the world's sound has died away, dB (the Husks' wrongness sets it): the effects and the
    /// ambience fall, the music doesn't. Applied in half-dB steps.
    /// </summary>
    public float Hush
    {
        get => _hushShown;
        set
        {
            float v = MathF.Round(Math.Max(0f, value) * 2f) / 2f;
            if (v == _hushShown || Settings == null) return;
            _hushShown = v;
            Apply(AudioSettings.Effects);
            Apply(AudioSettings.Ambience);
        }
    }

    public static void MakeBuses()
    {
        foreach (string key in AudioSettings.Volumes)
        {
            if (key == AudioSettings.Master || AudioServer.GetBusIndex(AudioSettings.BusName(key)) >= 0) continue;
            AudioServer.AddBus();
            int bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, AudioSettings.BusName(key));
            AudioServer.SetBusSend(bus, "Master");
        }
    }

    /// <summary>A player still on the default bus goes to ambience if it is the wind, the rain, the water around you or the howl at an opening, and to effects otherwise.</summary>
    public static void Route(Node n)
    {
        if (n is AudioStreamPlayer p)
        {
            if (p.Bus == "Master") p.Bus = AudioSettings.BusName(n.Name == "Wind" || n.Name == "Rain" || n.Name == "Underwater" ? AudioSettings.Ambience : AudioSettings.Effects);
        }
        else if (n is AudioStreamPlayer3D p3)
        {
            if (p3.Bus == "Master") p3.Bus = AudioSettings.BusName(n.Name == "Howl" ? AudioSettings.Ambience : AudioSettings.Effects);
        }
        else if (n is AudioStreamPlayer2D p2)
        {
            if (p2.Bus == "Master") p2.Bus = AudioSettings.BusName(AudioSettings.Effects);
        }
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= Route;
        Muffle(false); // the buses outlive the game: a NEW GAME taken under water must not start muffled
        if (_saveIn > 0f) Settings.Save(SettingsPath);
    }

    public override void _Ready()
    {
        MakeBuses();
        Settings = AudioSettings.Load(SettingsPath);
        foreach (string key in AudioSettings.Volumes) Apply(key);
        foreach (Node n in Game.FindChildren("*", "", true, false)) Route(n);
        GetTree().NodeAdded += Route;
        // Its one-shots are short, and the escape menu's clicks must sound while the world is stopped.
        _ui = new BlockBuilding.Sfx { Name = "BuildingSfx", ProcessMode = ProcessModeEnum.Always };
        AddChild(_ui);
        _chirp = DiegeticUi.World.Sfx.WarningChirp();
        _jet = Engine("Jetpack", VoiceProfile.Jetpack, -9f);
        _engine = Engine("Engines", VoiceProfile.Vessel(100f), -6f);
        _under = new AudioStreamPlayer { Name = "Underwater", Stream = Noise(1.4f, 160f, 0.35f), VolumeDb = -40f };
        AddChild(_under);
        _step = new AudioStreamPlayer3D { Name = "Steps", Stream = Tap(), VolumeDb = -20f, MaxDistance = 20f };
        AddChild(_step);
        // The music plays on through the escape menu's pause, so its volume can be set by ear.
        Music = new SliceMusic { Name = "Music", Game = Game, ProcessMode = ProcessModeEnum.Always };
        AddChild(Music);
        Game.Menu.Sounded += OnMenu;
        Game.Escape.Sounded += OnMenu;
    }

    /// <summary>An engine voice: synthesised as it plays, so it has no loop point to hear.</summary>
    ThrusterPlayer Engine(string name, VoiceProfile profile, float db)
    {
        var p = new ThrusterPlayer { Name = name, Voice = new ThrusterVoice(profile), Db = db };
        AddChild(p);
        return p;
    }

    /// <summary>A vessel's strongest direction of thrust in kN: what sets how big its engines sound.</summary>
    public static float BiggestThrust(Vessel v)
    {
        var stats = v.Stats;
        if (stats == null) return 0f;
        float best = stats.UpwardThrust;
        foreach (float t in stats.Thrust) best = Mathf.Max(best, t);
        return best;
    }

    /// <summary>
    /// How hard a vessel's engines are working, 0..1: the force they are applying (acceleration times mass)
    /// over the most they can give. Boost can ask for more than the rated thrust, so it caps at 1.
    /// </summary>
    public static float Effort(Vessel v)
    {
        float cap = BiggestThrust(v);
        if (cap <= 0f || v.Flight == null) return 0f;
        return Mathf.Clamp(v.Flight.State.ThrustLocal.Length() * v.Stats.Mass / cap, 0f, 1f);
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
        if (_saveIn > 0f)
        {
            _saveIn -= (float)delta;
            if (_saveIn <= 0f) Settings.Save(SettingsPath);
        }
        Game g = Game;
        if (g?.Survivor == null || g.Phase != Phase.Playing) return;
        float dt = (float)delta;
        // Weather on the surface: planet-surface's wind and rain, muffled inside a structure.
        // Under water everything comes through it, dull and far off; the weather not at all.
        bool under = g.Zone.Underwater?.CameraUnder == true;
        Muffle(under);
        Fade(_under, under, -20f, dt);
        UpdateWeather(under, dt);

        // The jetpack hisses while it pushes; a vessel's engines rumble with its thrust.
        bool jetting = g.Survivor.Seated == null && !g.Survivor.Body.OnFloor && g.Survivor.Legs.Jetpack && !g.Suit.Dead;
        _jet.Voice.On = jetting;
        _jet.Voice.Thrust = 1f;
        _jet.Voice.Air = 1f - g.Spaceflight.AirGone;
        bool engines = g.Survivor.Seated is Vessel v && v.Flight != null && v.Controller?.Frozen == false;
        _engine.Voice.On = engines;
        if (engines)
        {
            var seat = (Vessel)g.Survivor.Seated;
            if (seat != _engineOf)
            {
                _engineOf = seat;
                _engine.Voice.Retarget(VoiceProfile.Vessel(BiggestThrust(seat)));
            }
            _engine.Voice.Thrust = Effort(seat);
            _engine.Voice.Air = 1f - g.Spaceflight.AirGone; // in vacuum the roar thins to what the hull carries
        }

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
                _step.VolumeDb = g.Survivor.Crouched ? -28f : -20f; // creeping is softer
                _step.Play();
            }
        }

        // Shots, placements and take-downs, from the tools' counters.
        if (g.Tool.Shots != _shots) { _shots = g.Tool.Shots; _ui.Play("zap", g.Tool.Muzzle(), 1.3f, -10f); }
        if (g.Builder.Placed != _placed) { _placed = g.Builder.Placed; _ui.Play("place", g.Survivor.WorldEye(), 1f, -8f); }
        if (g.Builder.TakenDown != _taken) { _taken = g.Builder.TakenDown; _ui.Play("remove", g.Survivor.WorldEye(), 1f, -8f); }
    }

    float _windNow, _rainNow;

    /// <summary>The wind's and rain's loudness now (linear), after exposure (tests).</summary>
    public float WindLevel => _windNow;
    public float RainLevel => _rainNow;

    /// <summary>
    /// Wind by the LOCAL wind speed (ridges, hollows, woods, gusts) and rain by what falls HERE, both
    /// shaped by <see cref="AmbientExposure"/>: roof, walls, lee and relief; sealed rooms hear neither.
    /// </summary>
    void UpdateWeather(bool under, float dt)
    {
        Game g = Game;
        var w = g.Zone.Weather;
        float wantWind = 0f, wantRain = 0f;
        if (!under && w.Sim != null && w.Camera != null)
        {
            AmbientExposure e = g.Exposure;
            bool cockpit = g.Survivor.Seated != null;
            bool aboard = g.Survivor.Aboard != null && !g.Survivor.OnOuterHull;
            bool shut = aboard || w.SkyAtEye < 0.2f;
            float speed = w.Sim.WindAt(w.Camera.GlobalPosition).Length();
            float air = 1f - g.Spaceflight.AirGone; // the wind thins away with the air
            wantWind = AmbientExposure.WindLevel(speed, e.Cover, e.Walled, e.Relief, e.Lee, shut, cockpit) * air;
            wantRain = AmbientExposure.RainLevel(w.RainHere, e.Cover, e.Walled, shut, cockpit) * air;
        }
        _windNow = Mathf.MoveToward(_windNow, wantWind, dt * 1.2f);
        _rainNow = Mathf.MoveToward(_rainNow, wantRain, dt * 1.2f);
        g.Zone.Sfx.SetLevels(_windNow, _rainNow);
    }

    /// <summary>A low-pass on the effects and ambience buses (the music stays clear), there once (a new game reloads the scene, the buses stay) and on only under water.</summary>
    void Muffle(bool on)
    {
        if (on == _muffled) return;
        _muffled = on;
        foreach (string key in new[] { AudioSettings.Effects, AudioSettings.Ambience })
        {
            int bus = Bus(key), found = -1;
            for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
                if (AudioServer.GetBusEffect(bus, i) is AudioEffectLowPassFilter) found = i;
            if (found < 0)
            {
                AudioServer.AddBusEffect(bus, new AudioEffectLowPassFilter { CutoffHz = 520f, Resonance = 0.6f });
                found = AudioServer.GetBusEffectCount(bus) - 1;
            }
            AudioServer.SetBusEffectEnabled(bus, found, on);
        }
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
        _jet.Silence();
        _engine.Silence();
        _jet.StopPlayer();
        _engine.StopPlayer();
        _under.Stop();
        _step.Stop();
        Music.StopAll();
        Muffle(false);
    }

    // ---- The slice's own small synths ----------------------------------------------------------

    /// <summary>A loop of filtered noise: the dull press of water round the helmet.</summary>
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

    const int Rate = 22050;

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
