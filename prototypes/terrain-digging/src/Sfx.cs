using System;
using System.Collections.Generic;
using Godot;

namespace TerrainDigging;

/// <summary>
/// Sound effects synthesised in code at startup: no audio files, so nothing to license or
/// credit. Each sound is a few lines of maths (filtered noise, decaying sines) rendered once
/// to a 16-bit clip, then played from a small pool of 3D voices with a little random pitch so
/// repeated digs never sound mechanical.
/// </summary>
public partial class Sfx : Node3D
{
    public enum Sound { Dig, Ore, Clank, Place, Boom, Ping }

    const int Rate = 22050;
    const int Voices = 10;

    readonly Dictionary<Sound, AudioStreamWav> _clips = new();
    readonly AudioStreamPlayer3D[] _voices = new AudioStreamPlayer3D[Voices];
    readonly RandomNumberGenerator _rng = new() { Seed = 42 };
    int _next;

    public override void _Ready()
    {
        _clips[Sound.Dig] = Render(0.18f, Dig);
        _clips[Sound.Ore] = Render(0.45f, Ore);
        _clips[Sound.Clank] = Render(0.3f, Clank);
        _clips[Sound.Place] = Render(0.22f, Place);
        _clips[Sound.Boom] = Render(1.1f, Boom);
        _clips[Sound.Ping] = Render(0.8f, Ping);
        for (int i = 0; i < Voices; i++)
        {
            _voices[i] = new AudioStreamPlayer3D { UnitSize = 6f, MaxDistance = 120f };
            AddChild(_voices[i]);
        }
    }

    public void Play(Sound sound, Vector3 at, float pitch = 1f, float volumeDb = 0f)
    {
        AudioStreamPlayer3D voice = _voices[_next];
        _next = (_next + 1) % Voices;
        voice.Stream = _clips[sound];
        voice.GlobalPosition = at;
        voice.PitchScale = pitch * _rng.RandfRange(0.92f, 1.08f);
        voice.VolumeDb = volumeDb;
        voice.Play();
    }

    /// <summary>Peak and RMS level of each clip (0..1), so tests can catch a silent or clipping sound.</summary>
    public IEnumerable<(Sound sound, float peak, float rms, int clipped)> Levels()
    {
        foreach ((Sound sound, AudioStreamWav clip) in _clips)
        {
            byte[] data = clip.Data;
            float peak = 0f;
            double sum = 0;
            int clipped = 0;
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                float s = Math.Abs((short)(data[i] | (data[i + 1] << 8)) / 32000f);
                peak = Math.Max(peak, s);
                sum += s * s;
                if (s >= 0.999f) clipped++;
            }
            yield return (sound, peak, (float)Math.Sqrt(sum / (data.Length / 2)), clipped);
        }
    }

    // ---- Synthesis --------------------------------------------------------------------

    /// <summary>
    /// Renders a clip and scales it to a fixed peak, so every sound sits at a known level
    /// (loudness is set per play) and none of them can clip.
    /// </summary>
    static AudioStreamWav Render(float seconds, Func<float, Noise, float> sample)
    {
        const float Peak = 0.85f;
        int count = (int)(seconds * Rate);
        var samples = new float[count];
        var noise = new Noise(1977);
        float loudest = 1e-6f;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / Rate;
            // A 5 ms fade at each end so no clip starts or stops with a click.
            float edge = Mathf.Min(1f, Mathf.Min(t, seconds - t) / 0.005f);
            samples[i] = sample(t, noise) * edge;
            loudest = Mathf.Max(loudest, Mathf.Abs(samples[i]));
        }

        var data = new byte[count * 2];
        float gain = Peak / loudest * 32000f;
        for (int i = 0; i < count; i++)
        {
            short s = (short)(samples[i] * gain);
            data[i * 2] = (byte)(s & 0xFF);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }

    /// <summary>A crunchy thud: low-passed noise over a short low sine.</summary>
    static float Dig(float t, Noise n) =>
        n.LowPass(0.18f) * 2.6f * Mathf.Exp(-t * 28f) + Mathf.Sin(Mathf.Tau * 85f * t) * 0.6f * Mathf.Exp(-t * 22f);

    /// <summary>A bright two-note crystal chime.</summary>
    static float Ore(float t, Noise n) =>
        (Mathf.Sin(Mathf.Tau * 1318.5f * t) * 0.35f + Mathf.Sin(Mathf.Tau * 1975.5f * t) * 0.25f * Mathf.Clamp((t - 0.06f) * 60f, 0f, 1f))
        * Mathf.Exp(-t * 7f);

    /// <summary>Metal on stone: inharmonic partials and a click. Says "this won't cut".</summary>
    static float Clank(float t, Noise n) =>
        (Mathf.Sin(Mathf.Tau * 523f * t) * 0.3f + Mathf.Sin(Mathf.Tau * 1307f * t) * 0.25f
         + Mathf.Sin(Mathf.Tau * 2213f * t) * 0.18f + Mathf.Sin(Mathf.Tau * 3407f * t) * 0.12f) * Mathf.Exp(-t * 16f)
        + n.White() * 0.5f * Mathf.Exp(-t * 120f);

    /// <summary>A solid, wooden-ish knock for a block settling into place.</summary>
    static float Place(float t, Noise n) =>
        Mathf.Sin(Mathf.Tau * (150f - 60f * t) * t) * 0.8f * Mathf.Exp(-t * 18f) + n.LowPass(0.3f) * 0.3f * Mathf.Exp(-t * 40f);

    /// <summary>A crash or liftoff: a long rumble with a deep sine underneath.</summary>
    static float Boom(float t, Noise n) =>
        n.LowPass(0.04f + 0.1f * Mathf.Exp(-t * 6f)) * 5f * Mathf.Exp(-t * 3.2f)
        + Mathf.Sin(Mathf.Tau * 42f * t) * 0.7f * Mathf.Exp(-t * 4f);

    /// <summary>The detector: a rising sweep with a shimmer.</summary>
    static float Ping(float t, Noise n) =>
        Mathf.Sin(Mathf.Tau * (880f * t + 500f * t * t)) * (0.4f + 0.1f * Mathf.Sin(Mathf.Tau * 14f * t)) * Mathf.Exp(-t * 4.5f);

    /// <summary>Deterministic white noise with a one-pole low-pass, for the noisy parts.</summary>
    sealed class Noise
    {
        uint _state;
        float _low;

        public Noise(uint seed) => _state = seed;

        public float White()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state / (float)uint.MaxValue * 2f - 1f;
        }

        public float LowPass(float amount)
        {
            _low += (White() - _low) * amount;
            return _low;
        }
    }
}
