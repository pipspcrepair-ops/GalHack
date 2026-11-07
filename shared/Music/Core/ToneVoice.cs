using System;

namespace GalHack.Music;

/// <summary>
/// One distant event: a handful of sine partials with a slow shaped envelope, drifting pitch and
/// no percussive edge, sent almost entirely to the room. High partials bloom only near the peak,
/// as a real horn or struck plate does, so a tone opens and closes rather than switching on.
/// </summary>
internal sealed class ToneVoice
{
    const int Partials = 6;
    static readonly float[] Sine = Wavetable.Sine;

    readonly uint[] _phase = new uint[Partials];
    readonly float[] _ratio = new float[Partials];
    readonly float[] _amp = new float[Partials];
    readonly float[] _gainNow = new float[Partials];
    readonly float[] _gainNext = new float[Partials];
    readonly uint[] _inc = new uint[Partials];
    float _attack, _hold, _release, _t, _bloom, _hz, _glide, _glideSeconds, _vibrato, _vibratoRate, _pulseRate, _level, _panL, _panR, _dry;
    bool _sounding;

    public bool Active { get; private set; }

    /// <summary>Starts a tone. <paramref name="glide"/> is octaves moved across its whole length.</summary>
    public void Start(ToneKind kind, float hz, float level, float pan, DroneRng rng, float glide = 0f)
    {
        Active = true;
        _sounding = true;
        _t = 0f;
        _hz = hz;
        _level = level;
        _glide = glide;
        _pulseRate = 0f;
        _vibrato = 0f;
        _vibratoRate = 0.2f;
        _dry = 0.06f;
        float angle = (pan + 1f) * MathF.PI / 4f;
        _panL = MathF.Cos(angle);
        _panR = MathF.Sin(angle);
        Array.Fill(_gainNow, 0f);
        for (int i = 0; i < Partials; i++) _phase[i] = (uint)rng.Next();

        switch (kind)
        {
            case ToneKind.Bell: // a soft mallet on something large, a long way off
                Set(new[] { 1f, 2.01f, 2.76f, 4.07f, 5.42f, 6.8f }, new[] { 1f, 0.5f, 0.36f, 0.2f, 0.1f, 0.05f });
                _attack = 0.09f; _hold = 0f; _release = rng.Range(6f, 10f); _bloom = 0.9f;
                break;
            case ToneKind.Horn: // a siren or ship's horn in a dead city, heard from miles away
                Set(new[] { 1f, 2f, 3f, 4f, 5f, 6f }, new[] { 1f, 0.6f, 0.45f, 0.25f, 0.14f, 0.08f });
                _attack = rng.Range(2.4f, 4f); _hold = rng.Range(2f, 5f); _release = rng.Range(4f, 7f); _bloom = 0.8f;
                _glide = glide != 0f ? glide : -0.018f;
                _vibrato = 0.0015f;
                break;
            case ToneKind.Flute: // breath across something hollow
                Set(new[] { 1f, 2f, 3f, 4f, 5f, 7f }, new[] { 1f, 0.2f, 0.12f, 0.05f, 0.03f, 0.02f });
                _attack = rng.Range(1.2f, 2.4f); _hold = rng.Range(1f, 3f); _release = rng.Range(3f, 5f); _bloom = 0.5f;
                _vibrato = 0.004f;
                _vibratoRate = rng.Range(3.2f, 4.6f);
                _dry = 0.1f;
                break;
            case ToneKind.Glass: // a held glass note, cold, clean and slow
                Set(new[] { 1f, 2.003f, 3.01f, 4.2f, 5.43f, 6.27f }, new[] { 1f, 0.32f, 0.16f, 0.12f, 0.05f, 0.03f });
                _attack = rng.Range(0.5f, 1.3f); _hold = 0f; _release = rng.Range(7f, 12f); _bloom = 0.6f;
                _dry = 0.1f;
                break;
            case ToneKind.Groan: // metal or rock settling under load, bending as it goes
                Set(new[] { 1f, 1.41f, 1.83f, 2.33f, 2.97f, 3.61f }, new[] { 1f, 0.7f, 0.5f, 0.35f, 0.2f, 0.12f });
                _attack = rng.Range(1.5f, 2.8f); _hold = rng.Range(1f, 3f); _release = rng.Range(5f, 8f); _bloom = 0.8f;
                _glide = glide != 0f ? glide : rng.Range(-0.28f, -0.1f);
                break;
            case ToneKind.Call: // something large, a long way down, sounding its own depth
                Set(new[] { 1f, 2f, 3f, 4f, 5f, 6f }, new[] { 1f, 0.42f, 0.16f, 0.09f, 0.05f, 0.03f });
                _attack = rng.Range(2f, 3.4f); _hold = rng.Range(0.8f, 2f); _release = rng.Range(4f, 7f); _bloom = 0.7f;
                _glide = glide != 0f ? glide : rng.Range(-0.4f, 0.32f);
                _vibrato = 0.002f;
                _vibratoRate = rng.Range(1f, 2f);
                break;
            case ToneKind.Bowed: // a long note drawn on something with strings, somewhere else in the building
                Set(new[] { 1f, 2f, 3f, 4f, 5f, 6f }, new[] { 1f, 0.55f, 0.4f, 0.26f, 0.16f, 0.1f });
                _attack = rng.Range(1.4f, 2.8f); _hold = rng.Range(0.8f, 2f); _release = rng.Range(3f, 5f); _bloom = 0.35f;
                _vibrato = 0.0022f;
                _vibratoRate = rng.Range(3.8f, 5.2f);
                _dry = 0.14f;
                break;
            case ToneKind.Choir: // not voices, but the shape of them: a vowel held by nobody
                Set(new[] { 1f, 2f, 3f, 4f, 7f, 8f }, new[] { 0.55f, 1f, 0.5f, 0.2f, 0.34f, 0.26f });
                _attack = rng.Range(2f, 3.6f); _hold = rng.Range(1f, 2.4f); _release = rng.Range(3.5f, 6f); _bloom = 0.2f;
                _vibrato = 0.0016f;
                _vibratoRate = rng.Range(2.6f, 3.8f);
                break;
            case ToneKind.Pluck: // a string or tine touched gently, and left to die away
                Set(new[] { 1f, 2f, 3.01f, 4.2f, 5.4f, 6.8f }, new[] { 1f, 0.42f, 0.22f, 0.1f, 0.05f, 0.02f });
                _attack = 0.04f; _hold = 0f; _release = rng.Range(3f, 5.5f); _bloom = 1.2f;
                _dry = 0.16f;
                break;
            case ToneKind.Wire: // current in a line: one thin steady note with its octaves, gone as it came
                Set(new[] { 1f, 2f, 3f, 4f, 6f, 8f }, new[] { 1f, 0.3f, 0.2f, 0.12f, 0.06f, 0.03f });
                _attack = rng.Range(2.4f, 4.2f); _hold = rng.Range(2f, 5f); _release = rng.Range(3f, 5f); _bloom = 0.15f;
                _vibrato = 0.0006f;
                _vibratoRate = 0.13f;
                break;
            case ToneKind.Drip: // one drop in a long way of dark
                Set(new[] { 1f, 2.3f, 3.1f, 4.4f, 5.9f, 7.3f }, new[] { 1f, 0.18f, 0.08f, 0.04f, 0.02f, 0.01f });
                _attack = 0.012f; _hold = 0f; _release = rng.Range(0.3f, 0.7f); _bloom = 1.4f;
                _glide = rng.Range(0.3f, 0.6f);
                _dry = 0.03f;
                break;
            default: // Beacon: a slow, patient signal, repeating to nobody
                Set(new[] { 1f, 1.5f, 2f, 3f, 4f, 6f }, new[] { 1f, 0.24f, 0.2f, 0.07f, 0.03f, 0.01f });
                _attack = 0.5f; _hold = rng.Range(7f, 11f); _release = 3f; _bloom = 0.3f;
                _pulseRate = rng.Range(0.24f, 0.36f);
                break;
        }
        _glideSeconds = _attack + _hold + _release;
    }

    /// <summary>The Hollow's reversed swell: it gathers slowly and then is simply gone, into the room.</summary>
    public void Reverse(float attack)
    {
        _attack = attack;
        _hold = 0f;
        _release = 0.9f;
        _glideSeconds = attack + 1f;
    }

    void Set(float[] ratio, float[] amp)
    {
        Array.Copy(ratio, _ratio, Partials);
        Array.Copy(amp, _amp, Partials);
    }

    public void Control(float dt, float rate)
    {
        if (!Active) return;
        _t += dt;
        if (_t >= _attack + _hold + _release)
        {
            Active = false;
            Array.Fill(_gainNext, 0f);
            return;
        }
        float env = _t < _attack ? Ease(_t / _attack)
            : _t < _attack + _hold ? 1f
            : Ease(1f - (_t - _attack - _hold) / _release);
        if (_pulseRate > 0f)
        {
            float pulse = 0.5f - 0.5f * MathF.Cos(MathF.Tau * _pulseRate * _t);
            env *= pulse * pulse;
        }
        float bend = MathF.Pow(2f, _glide * MathF.Min(1f, _t / _glideSeconds) + _vibrato * MathF.Sin(MathF.Tau * _vibratoRate * _t));
        for (int i = 0; i < Partials; i++)
        {
            _gainNext[i] = _level * _amp[i] * MathF.Pow(env, 1f + _bloom * i);
            _inc[i] = Wavetable.Step(_hz * bend * _ratio[i], rate);
        }
    }

    static float Ease(float x) => x * x * (3f - 2f * x);

    public void Render(float[] dryL, float[] dryR, float[] sendL, float[] sendR, int frames)
    {
        if (!_sounding) return;
        for (int p = 0; p < Partials; p++)
        {
            uint phase = _phase[p], inc = _inc[p];
            float g = _gainNow[p], step = (_gainNext[p] - g) / frames;
            for (int i = 0; i < frames; i++)
            {
                float s = Wavetable.Read(Sine, phase) * g;
                phase += inc;
                g += step;
                float l = s * _panL, r = s * _panR;
                sendL[i] += l;
                sendR[i] += r;
                dryL[i] += l * _dry;
                dryR[i] += r * _dry;
            }
            _phase[p] = phase;
            _gainNow[p] = _gainNext[p];
        }
        _sounding = Active;
    }
}
