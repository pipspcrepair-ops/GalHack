using System;

namespace GalHack.Music;

/// <summary>
/// One sustained note of the drone: four detuned oscillators through its own slow filter. The
/// detune is in hertz, not cents, so low notes shimmer about as fast as high ones. A stratum
/// changes note only by fading out, retuning in silence and fading back in, so harmony moves one
/// voice at a time and nothing ever slides or jumps.
/// </summary>
internal sealed class Stratum
{
    const int Oscs = 4;
    // Unequal voices beat without ever cancelling, and each on its own drift, so a low note breathes
    // irregularly instead of pulsing like a tremolo.
    static readonly float[] Spread = { -1f, -0.25f, 0.4f, 0.85f };
    static readonly float[] Level = { 0.5f, 1f, 0.8f, 0.45f };
    static readonly float[] Pans = { -0.9f, 0.3f, -0.3f, 0.9f };

    readonly uint[] _phase = new uint[Oscs];
    readonly uint[] _inc = new uint[Oscs];
    readonly float[] _panL = new float[Oscs];
    readonly float[] _panR = new float[Oscs];
    readonly Drift[] _detuneDrift = new Drift[Oscs];
    readonly float[] _table;
    readonly float _base, _bright, _sweep, _detune, _depth, _sag, _rate, _sub;
    Svf _filterL, _filterR;
    Drift _sweepDrift, _ampDrift, _sagDrift, _wow;
    uint _subPhase, _subInc;
    float _hz, _pending, _fade, _fadeStep, _gain, _gainNext;

    public Stratum(float hz, Wave wave, float gain, float bright, float sweep, float sweepMin, float sweepMax, float detune, float depth, float sag, float sub, float width, float rate, DroneRng rng)
    {
        _hz = hz;
        _table = Wavetable.Of(wave);
        _base = gain;
        _bright = bright;
        _sweep = sweep;
        _detune = detune;
        _depth = depth;
        _sag = sag;
        _sub = sub;
        _rate = rate;
        _fade = 1f;
        _sweepDrift = Drift.Over(sweepMin, sweepMax, rng);
        _ampDrift = Drift.Over(14f, 44f, rng);
        _wow = Drift.Over(5f, 17f, rng);
        _sagDrift = Drift.Over(16f, 40f, rng);
        for (int i = 0; i < Oscs; i++)
        {
            _phase[i] = (uint)rng.Next();
            _detuneDrift[i] = Drift.Over(11f, 37f, rng);
            float angle = (Pans[i] * width + 1f) * MathF.PI / 4f;
            _panL[i] = MathF.Cos(angle) * Level[i];
            _panR[i] = MathF.Sin(angle) * Level[i];
        }
    }

    public bool Settled => _fadeStep == 0f && _pending == 0f;
    public float Hz => _pending != 0f ? _pending : _hz;

    /// <summary>Moves to a new note: out over one time, back in over another, silently in between.</summary>
    public void Retune(float hz, float outSeconds, float inSeconds)
    {
        _pending = hz;
        _fadeStep = -1f / outSeconds;
        _returnSeconds = inSeconds;
    }

    float _returnSeconds;

    public void Control(float dt, float wrong, DroneRng rng)
    {
        if (_fadeStep != 0f)
        {
            _fade += _fadeStep * dt;
            if (_fade <= 0f)
            {
                _fade = 0f;
                _hz = _pending;
                _pending = 0f;
                _fadeStep = 1f / _returnSeconds;
            }
            else if (_fade >= 1f)
            {
                _fade = 1f;
                _fadeStep = 0f;
            }
        }
        float sweep = _sweepDrift.Tick(dt, rng), amp = _ampDrift.Tick(dt, rng), sag = _sagDrift.Tick(dt, rng), wow = _wow.Tick(dt, rng);
        // A few cents of slow wander, as anything played by something other than a clock has.
        float hz = _hz * MathF.Pow(2f, (wrong * _sag * (0.8f + 0.2f * sag) + 2.5f * wow) / 1200f);
        for (int i = 0; i < Oscs; i++) _inc[i] = Wavetable.Step(hz + Spread[i] * _detune * (1f + 0.65f * _detuneDrift[i].Tick(dt, rng)), _rate);
        _subInc = Wavetable.Step(hz, _rate);
        float cut = _bright * MathF.Pow(2f, _sweep * sweep);
        _filterL.Set(cut, 0.8f, _rate);
        _filterR.Set(cut, 0.8f, _rate);
        float eased = _fade * _fade * (3f - 2f * _fade);
        _gainNext = _base * (1f - _depth * (0.5f - 0.5f * amp)) * eased;
    }

    public void Render(float[] busL, float[] busR, int frames)
    {
        float g = _gain, step = (_gainNext - g) / frames;
        float[] table = _table;
        for (int i = 0; i < frames; i++)
        {
            float l = 0f, r = 0f;
            for (int o = 0; o < Oscs; o++)
            {
                float s = Wavetable.Read(table, _phase[o]);
                _phase[o] += _inc[o];
                l += s * _panL[o];
                r += s * _panR[o];
            }
            float low = 0f;
            if (_sub > 0f)
            {
                low = Wavetable.Read(Wavetable.Sine, _subPhase) * _sub;
                _subPhase += _subInc;
            }
            g += step;
            busL[i] += (_filterL.Low(l * 0.4f) + low) * g;
            busR[i] += (_filterR.Low(r * 0.4f) + low) * g;
        }
        _gain = _gainNext;
    }
}

/// <summary>
/// A sparse pad note: three detuned oscillators whose filter opens as the note swells, far slower
/// than anyone would play it. It exists to change the air, not to be noticed starting.
/// </summary>
internal sealed class PadVoice
{
    const int Oscs = 3;
    readonly uint[] _phase = new uint[Oscs];
    readonly uint[] _inc = new uint[Oscs];
    float[] _table;
    Svf _filterL, _filterR;
    float _attack, _release, _t, _bright, _level, _gain, _gainNext;

    public bool Active { get; private set; }

    public void Start(Wave wave, float hz, float bright, float level, float attack, float release, float rate, DroneRng rng)
    {
        Active = true;
        _table = Wavetable.Of(wave);
        _t = 0f;
        _attack = attack;
        _release = release;
        _bright = bright;
        _level = level;
        _inc[0] = Wavetable.Step(hz, rate);
        _inc[1] = Wavetable.Step(hz + 0.22f, rate);
        _inc[2] = Wavetable.Step(hz - 0.31f, rate);
        for (int i = 0; i < Oscs; i++) _phase[i] = (uint)rng.Next();
    }

    public void Control(float dt, float rate)
    {
        if (!Active) return;
        _t += dt;
        float env;
        if (_t >= _attack + _release)
        {
            env = 0f;
            Active = false;
        }
        else
        {
            float x = _t < _attack ? _t / _attack : 1f - (_t - _attack) / _release;
            float s = MathF.Sin(x * MathF.PI / 2f);
            env = s * s;
        }
        _gainNext = _level * env;
        float cut = _bright * (0.35f + env);
        _filterL.Set(cut, 0.7f, rate);
        _filterR.Set(cut * 1.07f, 0.7f, rate);
    }

    public void Render(float[] dryL, float[] dryR, float[] sendL, float[] sendR, int frames)
    {
        if (!Active && _gain == 0f) return;
        float g = _gain, step = (_gainNext - g) / frames;
        float[] table = _table;
        for (int i = 0; i < frames; i++)
        {
            float a = Wavetable.Read(table, _phase[0]), b = Wavetable.Read(table, _phase[1]), c = Wavetable.Read(table, _phase[2]);
            _phase[0] += _inc[0];
            _phase[1] += _inc[1];
            _phase[2] += _inc[2];
            g += step;
            float l = _filterL.Low(a + b * 0.8f + c * 0.3f) * g * 0.45f;
            float r = _filterR.Low(a + b * 0.3f + c * 0.8f) * g * 0.45f;
            sendL[i] += l;
            sendR[i] += r;
            dryL[i] += l * 0.3f;
            dryR[i] += r * 0.3f;
        }
        _gain = _gainNext;
    }
}

/// <summary>
/// The harmonics that come and go high over the drone, as they do from an aeolian harp or a throat
/// singer: a few pure overtones of the root, each rising, fading and resting on its own clock, so
/// the top of the sound is never the same twice and rarely more than two or three at once.
/// </summary>
internal sealed class Overtones
{
    const int Count = 10;
    readonly uint[] _phase = new uint[Count];
    readonly uint[] _inc = new uint[Count];
    readonly float[] _level = new float[Count];
    readonly float[] _t = new float[Count];
    readonly float[] _rise = new float[Count];
    readonly float[] _fall = new float[Count];
    readonly float[] _gain = new float[Count];
    readonly float[] _next = new float[Count];
    readonly float[] _panL = new float[Count];
    readonly float[] _panR = new float[Count];
    readonly float _gainAll, _rate;
    readonly int _low, _high;

    public Overtones(float gain, int low, int high, float rate, DroneRng rng)
    {
        _gainAll = gain;
        _low = low;
        _high = high;
        _rate = rate;
        for (int i = 0; i < Count; i++) _t[i] = -rng.Range(0f, 34f); // negative is resting
    }

    public void Control(float dt, float root, DroneRng rng)
    {
        for (int i = 0; i < Count; i++)
        {
            bool wasResting = _t[i] < 0f;
            _t[i] += dt;
            if (_t[i] < 0f) continue;
            if (wasResting)
            {
                int h = _low + rng.Int(_high - _low + 1);
                float hz = root * h;
                while (hz > _rate * 0.2f) hz *= 0.5f;
                _inc[i] = Wavetable.Step(hz, _rate);
                _level[i] = _gainAll * MathF.Sqrt((float)_low / h) * rng.Range(0.5f, 1f);
                _rise[i] = rng.Range(5f, 14f);
                _fall[i] = rng.Range(8f, 20f);
                float angle = rng.Float() * MathF.PI / 2f;
                _panL[i] = MathF.Cos(angle);
                _panR[i] = MathF.Sin(angle);
            }
            float life = _rise[i] + _fall[i], env;
            if (_t[i] >= life)
            {
                env = 0f;
                _t[i] = -rng.Range(4f, 30f);
            }
            else
            {
                float x = _t[i] < _rise[i] ? _t[i] / _rise[i] : 1f - (_t[i] - _rise[i]) / _fall[i];
                float s = MathF.Sin(x * MathF.PI / 2f);
                env = s * s;
            }
            _next[i] = _level[i] * env;
        }
    }

    public void Render(float[] dryL, float[] dryR, float[] sendL, float[] sendR, int frames)
    {
        float[] table = Wavetable.Sine;
        for (int p = 0; p < Count; p++)
        {
            if (_next[p] == 0f && _gain[p] == 0f) continue;
            uint phase = _phase[p], inc = _inc[p];
            float g = _gain[p], step = (_next[p] - g) / frames, panL = _panL[p], panR = _panR[p];
            for (int i = 0; i < frames; i++)
            {
                float s = Wavetable.Read(table, phase) * g;
                phase += inc;
                g += step;
                sendL[i] += s * panL;
                sendR[i] += s * panR;
                dryL[i] += s * panL * 0.2f;
                dryR[i] += s * panR * 0.2f;
            }
            _phase[p] = phase;
            _gain[p] = _next[p];
        }
    }
}

/// <summary>
/// The noise bed, which is the world itself more than it is music: wind that gusts and sings in
/// narrow resonances tuned to the drone, surf that swells and withdraws, a rumble in the ground, or
/// something shifting about in the murk. Its two channels are unrelated noise, so it is wide.
/// </summary>
internal sealed class NoiseBed
{
    const int Whistles = 2;
    readonly BedKind _kind;
    readonly float _gain, _low, _high, _whistle, _rate;
    PinkNoise _noiseL, _noiseR;
    Svf _lowL, _lowR, _highL, _highR, _whistleA, _whistleB;
    Drift _gust, _level, _wander, _whistleWander;
    float _lowGain, _highGain, _lowNext, _highNext, _whistleGain, _whistleNext;
    float _wave, _waveStep, _waveHeight = 0.8f, _panL = 0.7f, _panR = 0.7f, _panFromL = 0.7f, _panFromR = 0.7f, _panToL = 0.7f, _panToR = 0.7f;
    float _hzA, _hzB;

    public NoiseBed(MoodProfile p, float root, float rate, DroneRng rng)
    {
        _kind = p.Bed;
        _gain = p.BedGain;
        _low = p.BedLow;
        _high = p.BedHigh;
        _whistle = p.WhistleGain;
        _rate = rate;
        _noiseL = new PinkNoise((uint)rng.Next());
        _noiseR = new PinkNoise((uint)rng.Next());
        _gust = Drift.Over(3f, 11f, rng);
        _level = Drift.Over(6f, 18f, rng);
        _wander = p.Bed == BedKind.Murk ? Drift.Over(0.7f, 2.6f, rng) : Drift.Over(12f, 30f, rng);
        _whistleWander = Drift.Over(5f, 16f, rng);
        _hzA = Harmonic(root, rng);
        _hzB = Harmonic(root, rng);
        _wave = rng.Float();
        _waveStep = 1f / rng.Range(9f, 17f);
    }

    static float Harmonic(float root, DroneRng rng)
    {
        float hz = root * (8 + rng.Int(16));
        while (hz > 1800f) hz *= 0.5f;
        return hz;
    }

    public void Control(float dt, float root, DroneRng rng)
    {
        float gust = 0.5f + 0.5f * _gust.Tick(dt, rng), level = 0.5f + 0.5f * _level.Tick(dt, rng), wander = _wander.Tick(dt, rng);
        float lowCut, highCut, q = 0.8f;
        switch (_kind)
        {
            case BedKind.Wind:
                float g2 = gust * gust;
                _lowNext = _gain * (0.3f + 0.7f * g2);
                _highNext = _gain * 0.6f * (0.2f + 0.8f * g2);
                lowCut = _low * (1f + 1.2f * gust);
                highCut = _high * (0.55f + 0.9f * gust + 0.15f * wander);
                _whistleNext = _whistle * g2 * gust;
                break;
            case BedKind.Surf:
                _wave += dt * _waveStep;
                if (_wave >= 1f)
                {
                    _wave -= 1f;
                    // Sea has sets: a few big ones, a lull, never a metronome.
                    _waveStep = 1f / (rng.Chance(0.25f) ? rng.Range(17f, 28f) : rng.Range(8f, 16f));
                    _waveHeight = rng.Chance(0.3f) ? rng.Range(0.3f, 0.5f) : rng.Range(0.55f, 1f);
                    _panFromL = _panToL;
                    _panFromR = _panToR;
                    float angle = rng.Range(0.2f, 1.37f);
                    _panToL = MathF.Cos(angle);
                    _panToR = MathF.Sin(angle);
                }
                float rise = MathF.Sin(MathF.PI * MathF.Pow(_wave, 0.65f)), env = rise * rise * _waveHeight;
                _lowNext = _gain * (0.3f + 0.7f * env);
                _highNext = _gain * 0.5f * (0.08f + 0.92f * env * env);
                lowCut = _low * (1f + 2.2f * env);
                highCut = _high * (0.3f + 0.8f * env);
                _panL = _panFromL + (_panToL - _panFromL) * _wave;
                _panR = _panFromR + (_panToR - _panFromR) * _wave;
                break;
            case BedKind.Rumble:
                _lowNext = _gain * (0.45f + 0.55f * level);
                _highNext = _gain * 0.3f * (0.2f + 0.8f * gust * gust);
                lowCut = _low * (1f + 0.9f * gust);
                highCut = _high * (1f + 0.5f * wander);
                break;
            case BedKind.Murk:
                _lowNext = _gain * 0.6f * (0.5f + 0.5f * level);
                _highNext = _gain * (0.25f + 0.75f * level * level);
                lowCut = _low * 1.4f;
                highCut = _high * MathF.Pow(2f, 0.9f * wander) * 0.6f;
                q = 3f;
                break;
            default:
                _lowNext = _gain * (0.7f + 0.3f * level);
                _highNext = _gain * 0.6f * (0.5f + 0.5f * gust);
                lowCut = _low * (1f + 0.3f * wander);
                highCut = _high * (1f + 0.3f * wander);
                break;
        }
        _lowL.Set(lowCut, _kind == BedKind.Rumble ? 0.7f : 0.5f, _rate);
        _lowR.Set(lowCut * 1.05f, _kind == BedKind.Rumble ? 0.7f : 0.5f, _rate);
        _highL.Set(highCut, q, _rate);
        _highR.Set(highCut * 0.96f, q, _rate);
        if (_whistle > 0f)
        {
            float w = 1f + 0.018f * _whistleWander.Tick(dt, rng);
            _whistleA.Set(_hzA * w, 34f, _rate);
            _whistleB.Set(_hzB * (2f - w), 34f, _rate);
        }
    }

    public void Render(float[] dryL, float[] dryR, float[] sendL, float[] sendR, int frames)
    {
        float lowG = _lowGain, highG = _highGain, whistleG = _whistleGain;
        float lowStep = (_lowNext - lowG) / frames, highStep = (_highNext - highG) / frames, whistleStep = (_whistleNext - whistleG) / frames;
        bool whistle = _whistle > 0f, rumble = _kind == BedKind.Rumble;
        for (int i = 0; i < frames; i++)
        {
            float a = _noiseL.Tick(), b = _noiseR.Tick();
            lowG += lowStep;
            highG += highStep;
            whistleG += whistleStep;
            // Only a rumble keeps its floor; wind and water sit in a band, or they turn to mud.
            float bodyL = rumble ? _lowL.Low(a) : _lowL.Band(a), bodyR = rumble ? _lowR.Low(b) : _lowR.Band(b);
            float l = bodyL * lowG + _highL.Band(a) * highG;
            float r = bodyR * lowG + _highR.Band(b) * highG;
            if (whistle)
            {
                float wa = _whistleA.Band(a + b) * whistleG, wb = _whistleB.Band(a - b) * whistleG;
                l += wa * 1.6f + wb * 0.5f;
                r += wa * 0.5f + wb * 1.6f;
            }
            l *= _panL * 1.41f;
            r *= _panR * 1.41f;
            dryL[i] += l;
            dryR[i] += r;
            sendL[i] += l * 0.5f;
            sendR[i] += r * 0.5f;
        }
        _lowGain = _lowNext;
        _highGain = _highNext;
        _whistleGain = _whistleNext;
    }
}

/// <summary>
/// The Hollow's breathing: noise through two vowel resonances, drawn slowly in and out on an
/// irregular clock. It never forms a word and never quite stops being air, and it can only be heard
/// once the world has gone far enough wrong.
/// </summary>
internal sealed class HollowBreath
{
    static readonly float[,] Vowels = { { 300f, 870f }, { 530f, 1840f }, { 730f, 1090f }, { 270f, 2290f }, { 570f, 840f } };

    readonly float _gain, _rate;
    PinkNoise _noise;
    Svf _f1L, _f2L, _f1R, _f2R;
    Drift _vowelDrift;
    int _from, _to;
    float _t, _inhale, _exhale, _pause, _level, _levelNext;

    public HollowBreath(float gain, float rate, DroneRng rng)
    {
        _gain = gain;
        _rate = rate;
        _noise = new PinkNoise((uint)rng.Next());
        _vowelDrift = Drift.Over(7f, 18f, rng);
        _to = rng.Int(Vowels.GetLength(0));
        Next(rng);
    }

    void Next(DroneRng rng)
    {
        _t = 0f;
        _inhale = rng.Range(3.2f, 5.4f);
        _exhale = rng.Range(4.2f, 7.5f);
        _pause = rng.Range(1.4f, 6f);
        _from = _to;
        _to = rng.Int(Vowels.GetLength(0));
    }

    public void Control(float dt, float wrong, DroneRng rng)
    {
        _t += dt;
        if (_t >= _inhale + _exhale + _pause) Next(rng);
        float env = 0f;
        if (_t < _inhale) env = MathF.Sin(_t / _inhale * MathF.PI / 2f);
        else if (_t < _inhale + _exhale) env = MathF.Cos((_t - _inhale) / _exhale * MathF.PI / 2f);
        _levelNext = _gain * wrong * env * env;
        float mix = Math.Clamp(_t / (_inhale + _exhale), 0f, 1f) + 0.05f * _vowelDrift.Tick(dt, rng);
        float f1 = Vowels[_from, 0] + (Vowels[_to, 0] - Vowels[_from, 0]) * mix;
        float f2 = Vowels[_from, 1] + (Vowels[_to, 1] - Vowels[_from, 1]) * mix;
        _f1L.Set(f1, 6f, _rate);
        _f2L.Set(f2, 7f, _rate);
        _f1R.Set(f1 * 1.03f, 6f, _rate);
        _f2R.Set(f2 * 0.97f, 7f, _rate);
    }

    public void Render(float[] sendL, float[] sendR, float[] dryL, float[] dryR, int frames)
    {
        float g = _level, step = (_levelNext - g) / frames;
        for (int i = 0; i < frames; i++)
        {
            float n = _noise.Tick();
            g += step;
            float l = (_f1L.Band(n) + _f2L.Band(n) * 0.6f) * g, r = (_f1R.Band(n) + _f2R.Band(n) * 0.6f) * g;
            sendL[i] += l;
            sendR[i] += r;
            dryL[i] += l * 0.3f;
            dryR[i] += r * 0.3f;
        }
        _level = _levelNext;
    }
}
