using System;
using System.Runtime.CompilerServices;

namespace GalHack.Music;

/// <summary>The seeded random numbers behind every choice a drone makes: its own, so one seed always gives one piece.</summary>
public sealed class DroneRng
{
    ulong _state;

    public DroneRng(ulong seed) => _state = seed;

    public ulong Next()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public float Float() => (float)((Next() >> 40) * (1.0 / (1UL << 24)));
    public float Range(float low, float high) => low + (high - low) * Float();
    public int Int(int count) => (int)(Next() % (ulong)count);
    public bool Chance(float odds) => Float() < odds;
    public T Pick<T>(T[] from) => from[Int(from.Length)];
}

/// <summary>
/// One cycle of each timbre, band-limited by construction (a few dozen harmonics, each at a fixed
/// random phase, so a low note reads as a soft reed rather than a buzzing saw). Looked up with a
/// 32-bit phase, so a voice can run for days without drifting.
/// </summary>
internal static class Wavetable
{
    public const int Bits = 11;
    public const int Size = 1 << Bits;
    const int FracBits = 32 - Bits;
    const uint FracMask = (1u << FracBits) - 1u;
    const float FracScale = 1f / (1u << FracBits);

    static float Bump(float n, float at, float width) => MathF.Exp(-(n - at) * (n - at) / (width * width));

    public static readonly float[] Sine = Build(1, _ => 1f);
    public static readonly float[] Warm = Build(32, n => MathF.Pow(n, -1.5f) * (1f + 0.3f * MathF.Cos(n * 0.9f)));
    public static readonly float[] Reed = Build(28, n => (n % 2 == 1 ? MathF.Pow(n, -0.9f) : 0.25f * MathF.Pow(n, -1.2f)) + 0.08f * Bump(n, 7f, 3f));
    public static readonly float[] Dark = Build(48, n => 1f / n);
    public static readonly float[] Glass = Build(8, n => n switch { 1 => 1f, 2 => 0.5f, 3 => 0.18f, 4 => 0.25f, 6 => 0.08f, 8 => 0.05f, _ => 0f });
    public static readonly float[] Choir = Build(24, n => MathF.Pow(n, -1.1f) * (0.3f + Bump(n, 3f, 1.5f) + 0.6f * Bump(n, 8f, 2.5f)));
    public static readonly float[] Bare = Build(17, n => n is 1 or 2 or 3 or 5 or 7 or 11 or 13 or 17 ? 1f / n : 0f);

    public static float[] Of(Wave wave) => wave switch
    {
        Wave.Warm => Warm,
        Wave.Reed => Reed,
        Wave.Dark => Dark,
        Wave.Glass => Glass,
        Wave.Choir => Choir,
        Wave.Bare => Bare,
        _ => Sine,
    };

    static float[] Build(int harmonics, Func<int, float> amp)
    {
        var table = new float[Size + 1];
        var rng = new DroneRng(0x5EED0 + (ulong)harmonics);
        for (int n = 1; n <= harmonics; n++)
        {
            float a = amp(n), phase = n == 1 ? 0f : rng.Float() * MathF.Tau;
            if (a == 0f) continue;
            for (int i = 0; i < Size; i++) table[i] += a * MathF.Sin(MathF.Tau * n * i / Size + phase);
        }
        float peak = 0f;
        for (int i = 0; i < Size; i++) peak = MathF.Max(peak, MathF.Abs(table[i]));
        for (int i = 0; i < Size; i++) table[i] /= peak;
        table[Size] = table[0];
        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Read(float[] table, uint phase)
    {
        uint i = phase >> FracBits;
        float a = table[i];
        return a + (table[i + 1] - a) * ((phase & FracMask) * FracScale);
    }

    /// <summary>A frequency as a 32-bit phase step.</summary>
    public static uint Step(float hz, float rate) => (uint)(Math.Clamp(hz / rate, 0f, 0.49f) * 4294967296.0);
}

/// <summary>A state-variable filter that stays stable while its cut-off moves (the trapezoidal form).</summary>
internal struct Svf
{
    float _a1, _a2, _a3, _k, _ic1, _ic2;

    public void Set(float hz, float q, float rate)
    {
        float g = MathF.Tan(MathF.PI * Math.Clamp(hz, 8f, rate * 0.45f) / rate);
        _k = 1f / q;
        _a1 = 1f / (1f + g * (g + _k));
        _a2 = g * _a1;
        _a3 = g * _a2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick(float x, out float low, out float band)
    {
        float v3 = x - _ic2;
        band = _a1 * _ic1 + _a2 * v3;
        low = _ic2 + _a2 * _ic1 + _a3 * v3;
        _ic1 = 2f * band - _ic1 + 1e-18f;
        _ic2 = 2f * low - _ic2 + 1e-18f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Low(float x)
    {
        Tick(x, out float low, out _);
        return low;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Band(float x)
    {
        Tick(x, out _, out float band);
        return band;
    }
}

/// <summary>
/// A value that wanders between -1 and 1, easing from one random target to the next: every slow
/// movement in a drone is one of these, so nothing cycles and nothing ever repeats exactly.
/// </summary>
internal struct Drift
{
    float _from, _to, _t, _step, _minSeconds, _maxSeconds;

    public static Drift Over(float minSeconds, float maxSeconds, DroneRng rng)
    {
        var d = new Drift { _from = rng.Range(-1f, 1f), _to = rng.Range(-1f, 1f), _t = rng.Float(), _minSeconds = minSeconds, _maxSeconds = maxSeconds };
        d._step = 1f / rng.Range(minSeconds, maxSeconds);
        return d;
    }

    public float Tick(float dt, DroneRng rng)
    {
        _t += dt * _step;
        if (_t >= 1f)
        {
            _t -= 1f;
            _from = _to;
            _to = rng.Range(-1f, 1f);
            _step = 1f / rng.Range(_minSeconds, _maxSeconds);
        }
        float s = _t * _t * (3f - 2f * _t);
        return _from + (_to - _from) * s;
    }
}

/// <summary>Fast noise, pinked (about -3 dB an octave): closer to wind and water than a white hiss.</summary>
internal struct PinkNoise
{
    uint _state;
    float _b0, _b1, _b2;

    public PinkNoise(uint seed)
    {
        _state = seed | 1u;
        _b0 = _b1 = _b2 = 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Tick()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        float white = (int)_state * 4.656613e-10f;
        _b0 = 0.99765f * _b0 + white * 0.0990460f;
        _b1 = 0.96300f * _b1 + white * 0.2965164f;
        _b2 = 0.57000f * _b2 + white * 1.0526913f;
        return (_b0 + _b1 + _b2 + white * 0.1848f) * 0.11f;
    }
}

/// <summary>
/// The room: an eight-line feedback delay network behind a chain of all-pass diffusers, with its
/// lines slowly modulated so the tail never rings metallic. Everything distant in a drone lives in
/// here, which is most of what makes it sound like a place rather than a tone.
/// </summary>
internal sealed class DiffuseRoom
{
    const int Lines = 8, Diffusers = 4;
    static readonly float[] LineMs = { 67.3f, 79.1f, 89.3f, 101.7f, 113.9f, 127.1f, 139.7f, 151.3f };
    static readonly float[] DiffuserMs = { 4.7f, 7.1f, 11.3f, 17.9f, 5.3f, 8.3f, 12.7f, 19.1f };
    const float DiffuserGain = 0.62f;

    readonly float[][] _lines = new float[Lines][];
    readonly int[] _length = new int[Lines];
    readonly int[] _mask = new int[Lines];
    readonly float[] _feedback = new float[Lines];
    readonly float[] _damped = new float[Lines];
    readonly float[] _bass = new float[Lines];
    readonly float[] _read = new float[Lines];
    readonly float[] _modPhase = new float[Lines];
    readonly float[] _modRate = new float[Lines];
    readonly float[] _modAt = new float[Lines];
    readonly float[][] _diffuser = new float[Diffusers * 2][];
    readonly int[] _diffuserAt = new int[Diffusers * 2];
    readonly float[] _pre;
    readonly int _preLength;
    readonly float _damp, _bassCut, _modDepth;
    int _write, _preWrite;

    public DiffuseRoom(float rt60, float size, float dampHz, float predelayMs, float rate, DroneRng rng)
    {
        for (int i = 0; i < Lines; i++)
        {
            _length[i] = (int)(LineMs[i] * size * rate / 1000f);
            int cap = 1;
            while (cap < _length[i] + 64) cap <<= 1;
            _lines[i] = new float[cap];
            _mask[i] = cap - 1;
            _feedback[i] = MathF.Pow(10f, -3f * _length[i] / (rate * rt60));
            _modPhase[i] = rng.Float() * MathF.Tau;
            _modRate[i] = rng.Range(0.09f, 0.31f);
            _modAt[i] = _length[i];
        }
        for (int i = 0; i < _diffuser.Length; i++) _diffuser[i] = new float[(int)(DiffuserMs[i] * rate / 1000f)];
        _preLength = Math.Max(1, (int)(predelayMs * rate / 1000f));
        _pre = new float[_preLength * 2];
        _damp = 1f - MathF.Exp(-MathF.Tau * dampHz / rate);
        _bassCut = 1f - MathF.Exp(-MathF.Tau * 45f / rate); // the tail keeps its bass from piling up
        _modDepth = 7f * rate / 44100f;
    }

    /// <summary>Moves the lines' read points a little: once per control block, far slower than hearing.</summary>
    public void Control(float dt)
    {
        for (int i = 0; i < Lines; i++)
        {
            _modPhase[i] += _modRate[i] * dt * MathF.Tau;
            if (_modPhase[i] > MathF.Tau) _modPhase[i] -= MathF.Tau;
            _modAt[i] = _length[i] + (MathF.Sin(_modPhase[i]) + 1f) * _modDepth;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    float Diffuse(int line, float x)
    {
        float[] d = _diffuser[line];
        int at = _diffuserAt[line];
        float delayed = d[at];
        float v = x + DiffuserGain * delayed;
        d[at] = v;
        _diffuserAt[line] = at + 1 == d.Length ? 0 : at + 1;
        return delayed - DiffuserGain * v;
    }

    public void Tick(float inL, float inR, out float outL, out float outR)
    {
        float l = _pre[_preWrite], r = _pre[_preWrite + _preLength];
        _pre[_preWrite] = inL;
        _pre[_preWrite + _preLength] = inR;
        if (++_preWrite == _preLength) _preWrite = 0;

        for (int i = 0; i < Diffusers; i++)
        {
            l = Diffuse(i, l);
            r = Diffuse(i + Diffusers, r);
        }

        float sum = 0f;
        for (int i = 0; i < Lines; i++)
        {
            float[] line = _lines[i];
            int mask = _mask[i];
            float at = _write - _modAt[i];
            int a = (int)MathF.Floor(at);
            float frac = at - a, s0 = line[a & mask], s1 = line[(a + 1) & mask];
            float s = s0 + (s1 - s0) * frac;
            _damped[i] += _damp * (s - _damped[i]);
            _bass[i] += _bassCut * (_damped[i] - _bass[i]);
            float v = (_damped[i] - _bass[i] * 0.7f) * _feedback[i];
            _read[i] = v;
            sum += v;
        }
        sum *= 2f / Lines; // Householder: every line feeds all the others, at no cost in gain
        outL = (_read[0] - _read[2] + _read[4] - _read[6]) * 0.5f;
        outR = (_read[1] - _read[3] + _read[5] - _read[7]) * 0.5f;
        for (int i = 0; i < Lines; i++)
            _lines[i][_write & _mask[i]] = _read[i] - sum + ((i & 1) == 0 ? l : r) + 1e-18f;
        _write = (_write + 1) & 0x3FFFFFFF;
    }
}
