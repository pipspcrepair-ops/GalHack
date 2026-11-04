using System;
using Godot;
using Weather.Model;

namespace Weather;

/// <summary>
/// The weather's sound, synthesised live from filtered noise: no audio files at all. Wind
/// rises and whistles with speed and gusts, rain hisses, a tornado roars long before it
/// arrives (its loudness comes from the model's <see cref="Tornado.Roar"/>), thunder rolls in
/// after each flash at the speed of sound, and the visor chirps when a new danger appears.
/// One generator stream at a low mix rate keeps it cheap.
/// </summary>
public partial class WeatherAudio : Node
{
    public WeatherDirector Director;
    public Camera3D Listener;

    const int MixRate = 22050;
    const float SpeedOfSound = 343f;

    AudioStreamGeneratorPlayback _playback;
    const int Chunk = 256;
    readonly Vector2[] _buffer = new Vector2[Chunk]; // pushed in fixed chunks, so filling never allocates
    uint _noise = 0x1234567;

    // Filter states (one-pole low-passes) and envelopes.
    float _windLow, _windHigh, _windGain, _whistle;
    float _rainLow, _rainGain;
    float _roarLow, _roarMid, _roarGain, _roarTarget;
    float _thunderLow, _thunderEnv, _thunderCutoff = 0.02f, _crack;
    float _beepPhase, _beepTime = 99f;
    float _windTarget, _rainTarget, _gustTarget;
    readonly System.Collections.Generic.List<(float at, float strength, float distance)> _thunder = new();
    float _clock;
    int _lastDangerCount;

    public override void _Ready()
    {
        var stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.15f };
        var player = new AudioStreamPlayer { Stream = stream, VolumeDb = -4f, Autoplay = true, Name = "WeatherSound" };
        AddChild(player);
        player.Play();
        _playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    /// <summary>Let go of the playback handle, or the engine reports it leaked at exit.</summary>
    public override void _ExitTree()
    {
        _playback?.Dispose();
        _playback = null;
    }

    /// <summary>A strike was seen: its thunder arrives after distance / speed of sound.</summary>
    public void Thunder(Vector3 at)
    {
        float distance = Listener.GlobalPosition.DistanceTo(at);
        _thunder.Add((_clock + distance / SpeedOfSound, 1f / (1f + distance / 300f), distance));
    }

    /// <summary>The visor's warning chirp.</summary>
    public void Alert() => _beepTime = 0f;

    public override void _Process(double delta)
    {
        if (Director?.Sim == null || Listener == null) return;
        float dt = (float)delta;
        _clock += dt;
        WeatherSim sim = Director.Sim;
        Vector3 ear = Listener.GlobalPosition;
        Vector3 air = Director.Forces.AirAt(ear);
        float speed = air.Length();

        _windTarget = Mathf.Clamp(speed / 30f, 0f, 1.3f);
        _gustTarget = sim.Current.Gustiness;
        _rainTarget = sim.Current.Rain;
        _roarTarget = 0f;
        foreach (Tornado t in sim.Tornadoes) _roarTarget = Mathf.Max(_roarTarget, t.Roar(ear));

        for (int i = _thunder.Count - 1; i >= 0; i--)
        {
            if (_thunder[i].at > _clock) continue;
            _thunderEnv = Mathf.Max(_thunderEnv, _thunder[i].strength);
            _thunderCutoff = Mathf.Lerp(0.06f, 0.008f, Mathf.Clamp(_thunder[i].distance / 1200f, 0f, 1f)); // far thunder only rumbles
            if (_thunder[i].distance < 120f) _crack = 1f;
            _thunder.RemoveAt(i);
        }

        int dangers = 0;
        foreach (Hazard h in sim.Hazards) if (h.Kind == HazardKind.Lightning && h.Target.DistanceTo(ear) < 350f) dangers++;
        foreach (Tornado t in sim.Tornadoes) if (t.Roar(ear) > 0.05f) dangers++;
        if (dangers > _lastDangerCount) Alert();
        _lastDangerCount = dangers;

        Fill();
    }

    void Fill()
    {
        if (_playback == null) return;
        int chunks = Math.Min(_playback.GetFramesAvailable() / Chunk, 8);
        for (int c = 0; c < chunks; c++)
        {
            Synthesize();
            _playback.PushBuffer(_buffer);
        }
    }

    void Synthesize()
    {
        const float dt = 1f / MixRate;
        for (int i = 0; i < Chunk; i++)
        {
            // Envelopes glide so nothing clicks when the weather changes.
            _windGain += (_windTarget - _windGain) * 0.0004f;
            _rainGain += (_rainTarget - _rainGain) * 0.0004f;
            _roarGain += (_roarTarget - _roarGain) * 0.0002f;

            float white = Noise();
            float gust = 0.7f + 0.3f * MathF.Sin(_clock * 0.9f + i * dt * 0.9f) * _gustTarget;

            // Wind: a low body plus a whistle that climbs with speed.
            float windCut = 0.01f + 0.05f * _windGain * gust;
            _windLow += (white - _windLow) * windCut;
            _windHigh += (white - _windHigh) * (windCut * 3f);
            _whistle += (_windHigh - _windLow - _whistle) * 0.08f;
            float wind = (_windLow * 2.2f + _whistle * 0.6f * _windGain) * _windGain * gust;

            // Rain: bright hiss, high-passed noise.
            _rainLow += (white - _rainLow) * 0.3f;
            float rain = (white - _rainLow) * 0.35f * _rainGain;

            // Tornado: a deep, pulsing freight-train roar.
            _roarLow += (white - _roarLow) * 0.006f;
            _roarMid += (white - _roarMid) * 0.03f;
            float throb = 0.75f + 0.25f * MathF.Sin(_clock * 5.3f);
            float roar = (_roarLow * 9f + _roarMid * 1.5f * _roarGain) * _roarGain * throb;

            // Thunder: a crack for close strikes, then a long low roll.
            _thunderLow += (white - _thunderLow) * _thunderCutoff;
            float thunder = _thunderLow * 9f * _thunderEnv + white * _crack * 0.8f;
            _thunderEnv *= 0.99992f;
            _crack *= 0.9992f;

            // Visor alert: three short chirps.
            float beep = 0f;
            if (_beepTime < 0.5f)
            {
                _beepPhase += 1320f * dt * MathF.Tau;
                bool on = (_beepTime % 0.16f) < 0.08f;
                beep = on ? MathF.Sin(_beepPhase) * 0.18f : 0f;
            }
            _beepTime += dt;

            float mono = MathF.Tanh(wind + rain + roar + thunder);
            _buffer[i] = new Vector2(mono + beep, mono + beep);
        }
    }

    float Noise()
    {
        _noise ^= _noise << 13;
        _noise ^= _noise >> 17;
        _noise ^= _noise << 5;
        return (_noise & 0xFFFF) / 32767.5f - 1f;
    }
}
