using System;
using System.Collections.Generic;
using Godot;

namespace BlockBuilding;

/// <summary>
/// Every sound in the prototype is synthesised here at start-up: no audio files, nothing to
/// licence or credit. Short, punchy and a little analogue, like 1970s sci-fi hardware.
/// </summary>
public partial class Sfx : Node
{
    const int Rate = 22050;
    readonly Dictionary<string, AudioStreamWav> _sounds = new();
    readonly List<AudioStreamPlayer3D> _voices = new();
    AudioStreamPlayer _ui;
    int _next;
    readonly Random _rng = new(5);

    public override void _Ready()
    {
        _sounds["place"] = Make(0.22f, Place);
        _sounds["remove"] = Make(0.3f, Remove);
        _sounds["paint"] = Make(0.16f, Spray);
        _sounds["upgrade"] = Make(0.42f, Upgrade);
        _sounds["error"] = Make(0.2f, Error);
        _sounds["zap"] = Make(0.2f, Zap);
        _sounds["boom"] = Make(1.1f, Boom);
        _sounds["creak"] = Make(0.9f, Creak);
        _sounds["click"] = Make(0.05f, t => Mathf.Sin(t * 2200f * Mathf.Tau) * Mathf.Exp(-t * 90f) * 0.5f);
        _sounds["toggle"] = Make(0.12f, t => Square(t * (t < 0.05f ? 520f : 780f)) * Mathf.Exp(-t * 22f) * 0.25f);

        for (int i = 0; i < 12; i++)
        {
            var voice = new AudioStreamPlayer3D { UnitSize = 8f, MaxDistance = 250f };
            AddChild(voice);
            _voices.Add(voice);
        }
        _ui = new AudioStreamPlayer { VolumeDb = -6f };
        AddChild(_ui);
    }

    /// <summary>Plays a sound at a point in the world, with a little pitch variation so repeats don't grate.</summary>
    public void Play(string name, Vector3 at, float pitch = 1f, float volumeDb = 0f)
    {
        if (!_sounds.TryGetValue(name, out AudioStreamWav stream)) return;
        AudioStreamPlayer3D voice = _voices[_next];
        _next = (_next + 1) % _voices.Count;
        voice.Stream = stream;
        voice.GlobalPosition = at;
        voice.PitchScale = pitch * (0.94f + 0.12f * (float)_rng.NextDouble());
        voice.VolumeDb = volumeDb;
        voice.Play();
    }

    /// <summary>A sound with no position (interface clicks).</summary>
    public void PlayUi(string name, float pitch = 1f)
    {
        if (!_sounds.TryGetValue(name, out AudioStreamWav stream)) return;
        _ui.Stream = stream;
        _ui.PitchScale = pitch;
        _ui.Play();
    }

    // ---- Recipes -------------------------------------------------------------------------

    float Noise() => (float)_rng.NextDouble() * 2f - 1f;
    static float Square(float phase) => Mathf.PosMod(phase, 1f) < 0.5f ? 1f : -1f;

    /// <summary>A solid clunk: a falling low tone, a metal ping and a click of contact.</summary>
    float Place(float t)
    {
        float body = Mathf.Sin(Mathf.Tau * (70f * t + 90f * (1f - Mathf.Exp(-t * 30f)) / 30f)) * Mathf.Exp(-t * 26f);
        float ping = Mathf.Sin(Mathf.Tau * 1180f * t) * Mathf.Exp(-t * 38f) * 0.22f + Mathf.Sin(Mathf.Tau * 1730f * t) * Mathf.Exp(-t * 50f) * 0.12f;
        float click = t < 0.006f ? Noise() * (1f - t / 0.006f) * 0.6f : 0f;
        return (body * 0.9f + ping + click) * 0.8f;
    }

    float _lowpass;

    /// <summary>A crunchy unbolting: filtered noise and a falling buzz.</summary>
    float Remove(float t)
    {
        _lowpass += (Noise() - _lowpass) * 0.25f;
        float buzz = Square(t * (190f - 260f * t)) * 0.18f;
        return (_lowpass * 1.4f + buzz) * Mathf.Exp(-t * 11f) * (t < 0.01f ? t / 0.01f : 1f);
    }

    float _prev;

    /// <summary>A short hiss of spray paint: high-passed noise with a soft attack.</summary>
    float Spray(float t)
    {
        float n = Noise();
        float hp = n - _prev;
        _prev = n;
        return hp * 0.32f * Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t * 14f);
    }

    /// <summary>Two rising tones over a thunk: something got better.</summary>
    float Upgrade(float t)
    {
        float tone = t < 0.12f
            ? Mathf.Sin(Mathf.Tau * 660f * t)
            : Mathf.Sin(Mathf.Tau * 990f * t) + 0.4f * Mathf.Sin(Mathf.Tau * 1980f * t);
        return Place(t) * 0.6f + tone * 0.22f * Mathf.Exp(-(t < 0.12f ? t : t - 0.12f) * 9f);
    }

    /// <summary>A low double buzz: not allowed.</summary>
    float Error(float t)
    {
        bool on = t < 0.07f || (t > 0.1f && t < 0.17f);
        return on ? Square(t * 110f) * 0.18f + Mathf.Sin(t * 55f * Mathf.Tau) * 0.15f : 0f;
    }

    /// <summary>A pulp-sci-fi blaster: a fast falling sweep.</summary>
    float Zap(float t)
    {
        float f = 1900f * Mathf.Exp(-t * 14f) + 180f;
        _zapPhase += f / Rate;
        return (Mathf.Sin(_zapPhase * Mathf.Tau) * 0.6f + Square(_zapPhase * 0.5f) * 0.15f) * Mathf.Exp(-t * 12f);
    }

    float _zapPhase, _boomLow;

    /// <summary>A rolling explosion: low-passed noise over a sub thump.</summary>
    float Boom(float t)
    {
        _boomLow += (Noise() - _boomLow) * (0.08f + 0.3f * Mathf.Exp(-t * 8f));
        float sub = Mathf.Sin(Mathf.Tau * (45f * t + 40f * (1f - Mathf.Exp(-t * 6f)) / 6f)) * Mathf.Exp(-t * 4f);
        return (_boomLow * 2.4f * Mathf.Exp(-t * 3.2f) + sub * 0.8f) * Mathf.Min(1f, t / 0.004f);
    }

    /// <summary>Metal tearing as a section breaks away.</summary>
    float Creak(float t)
    {
        float wobble = 70f + 25f * Mathf.Sin(t * 23f) + 40f * t;
        _creakPhase += wobble / Rate;
        float saw = (Mathf.PosMod(_creakPhase, 1f) * 2f - 1f) * 0.3f;
        return (saw + Noise() * 0.08f) * Mathf.Sin(Mathf.Pi * Mathf.Min(1f, t / 0.9f)) * 0.9f;
    }

    float _creakPhase;

    static AudioStreamWav Make(float seconds, Func<float, float> sample)
    {
        int n = (int)(seconds * Rate);
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float fade = Mathf.Min(1f, (n - i) / (Rate * 0.01f)); // no click at the end
            short s = (short)(Mathf.Clamp(sample(t) * fade, -1f, 1f) * 32000f);
            data[i * 2] = (byte)(s & 0xff);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }
}
