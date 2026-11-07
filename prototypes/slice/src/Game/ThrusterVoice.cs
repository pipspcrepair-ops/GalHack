using System;

namespace Slice.Game;

/// <summary>
/// What one engine voice is like, drawn from the thrust behind it. Bigger thrust sounds lower and
/// deeper, slower to spool up, a little louder; the jetpack is the small bright extreme.
/// </summary>
public readonly record struct VoiceProfile
{
    /// <summary>Roar cut-off at idle and at full thrust, Hz.</summary>
    public float RoarLow { get; init; }
    public float RoarHigh { get; init; }

    /// <summary>The turbine whine's frequency at idle and at full thrust, Hz.</summary>
    public float WhineLow { get; init; }
    public float WhineHigh { get; init; }

    /// <summary>The chest-felt rumble's tone at idle and at full thrust, Hz.</summary>
    public float RumbleLow { get; init; }
    public float RumbleHigh { get; init; }

    /// <summary>How much of each layer is mixed in (the whine and the rumble; the roar is 1).</summary>
    public float Whine { get; init; }
    public float Rumble { get; init; }

    /// <summary>Overall level at full thrust, linear.</summary>
    public float Level { get; init; }

    /// <summary>The idle: how much thrust a running engine always has, 0..1.</summary>
    public float Idle { get; init; }

    /// <summary>Seconds the sound takes to follow a rise and a fall in thrust, and (Release) to fade to silence once the engine stops.</summary>
    public float Rise { get; init; }
    public float Fall { get; init; }
    public float Release { get; init; }

    /// <summary>0 for the smallest engine to 1 for the biggest; it only sets how the profile is drawn.</summary>
    public float Depth { get; init; }

    /// <summary>The thrust, in kN, that reads as the smallest vessel and as the biggest.</summary>
    public const float SmallKn = 40f, HugeKn = 8000f;

    /// <summary>A vessel's engines from its strongest direction's thrust in kN (data: small 50, large 300, capital 2400 per thruster).</summary>
    public static VoiceProfile Vessel(float kilonewtons)
    {
        float depth = Math.Clamp((MathF.Log10(Math.Max(kilonewtons, 1f)) - MathF.Log10(SmallKn)) / (MathF.Log10(HugeKn) - MathF.Log10(SmallKn)), 0f, 1f);
        float pitch = MathF.Pow(2f, -1.3f * depth); // a capital ship sounds nearly an octave and a half down
        return new VoiceProfile
        {
            Depth = depth,
            RoarLow = 150f * pitch, RoarHigh = 2300f * MathF.Pow(2f, -0.9f * depth),
            WhineLow = 170f * pitch, WhineHigh = 520f * pitch,
            RumbleLow = 34f * MathF.Pow(2f, -0.5f * depth), RumbleHigh = 78f * MathF.Pow(2f, -0.5f * depth),
            Whine = 0.10f * (1f - 0.5f * depth), Rumble = 0.55f + 0.35f * depth,
            Level = 0.25f + 0.08f * depth, Idle = 0.14f,
            Rise = 0.6f + 1.8f * depth, Fall = 0.9f + 2.2f * depth, Release = 0.8f + 1.4f * depth,
        };
    }

    /// <summary>The jetpack: a short bright hiss, quick to follow the trigger, no rumble to speak of.</summary>
    public static VoiceProfile Jetpack => new()
    {
        Depth = 0f,
        RoarLow = 500f, RoarHigh = 3300f,
        WhineLow = 0f, WhineHigh = 0f, RumbleLow = 60f, RumbleHigh = 90f,
        Whine = 0f, Rumble = 0.12f, Level = 0.2f, Idle = 0f,
        Rise = 0.18f, Fall = 0.3f, Release = 0.2f,
    };
}

/// <summary>
/// One engine's sound, synthesised sample by sample so there is no loop to hear. Three layers
/// share one smoothed "spool" value that chases the asked-for thrust (slowly up, slower down):
/// a roar of filtered noise whose cut-off climbs, a low rumble with its own tone, and a whine
/// that rises in pitch. Pitch, loudness and brightness all follow the spool together, so they
/// glide and never step. Nothing repeats: the noise is a running generator, and every phase and
/// filter state carries on from one sample to the next. It fades in and out through its own
/// gate, so starting and stopping make no click. In vacuum the airy roar and the whine thin away
/// and the rumble stays, as if felt through the hull.
///
/// Pure arithmetic with no engine types, so a test renders it exactly as the game plays it. Set
/// <see cref="Thrust"/>, <see cref="On"/> and <see cref="Air"/> from anywhere; call
/// <see cref="Render"/> from one thread.
/// </summary>
public sealed class ThrusterVoice
{
    public const int Rate = 22050;

    /// <summary>Asked-for thrust, 0..1.</summary>
    public volatile float Thrust;

    /// <summary>Whether the engine is running.</summary>
    public volatile bool On;

    /// <summary>1 in air, 0 in vacuum.</summary>
    public volatile float Air = 1f;

    VoiceProfile _p;
    VoiceProfile? _pending;
    uint _rng;
    float _spool, _gate, _air = 1f;
    float _low, _band, _rumbleLp, _wobble1, _wobble2;
    double _phaseRumble, _phaseWhine, _phaseWhine2;

    /// <summary>A voice. Its noise starts from <paramref name="seed"/>, or from a fresh one each time.</summary>
    public ThrusterVoice(VoiceProfile profile, uint seed = 0)
    {
        _p = profile;
        _rng = seed != 0 ? seed : (uint)Random.Shared.Next(1, int.MaxValue);
    }

    public VoiceProfile Profile => _p;

    /// <summary>The spool: 0 silent idle to 1 full thrust, as heard.</summary>
    public float Spool => _spool;

    /// <summary>How open the gate is, 0..1.</summary>
    public float Gate => _gate;

    /// <summary>Another vessel's engines: the sound fades out, changes, and fades back in, so nothing jumps.</summary>
    public void Retarget(VoiceProfile profile)
    {
        if (_gate <= 0f && !On) _p = profile;
        else _pending = profile;
    }

    float Noise()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return (_rng >> 8) * (2f / 16777216f) - 1f;
    }

    static float Coeff(float seconds) => 1f - MathF.Exp(-1f / (Math.Max(seconds, 0.005f) * Rate));

    /// <summary>Fills <paramref name="mono"/> with the next <paramref name="count"/> samples.</summary>
    public void Render(float[] mono, int count, int offset = 0)
    {
        VoiceProfile p = _p;
        bool on = On && _pending == null;
        float thrust = Math.Clamp(Thrust, 0f, 1f), airAsk = Math.Clamp(Air, 0f, 1f);
        float target = on ? p.Idle + (1f - p.Idle) * thrust : 0f;
        float gateTarget = on ? 1f : -0.05f; // aims just under zero, so the fade ends at silence in about the release time
        float riseC = Coeff(p.Rise), fallC = Coeff(p.Fall), gateUp = Coeff(0.12f), gateDown = Coeff(p.Release / 3f), airC = Coeff(0.6f);
        float wobbleC = Coeff(0.12f), wobble2C = Coeff(0.9f);
        float wobbleNorm = MathF.Sqrt((2f - wobbleC) / wobbleC * 3f), wobble2Norm = MathF.Sqrt((2f - wobble2C) / wobble2C * 3f);
        const float Q = 1.15f;

        for (int i = 0; i < count; i++)
        {
            _air += (airAsk - _air) * airC;
            _spool += (target - _spool) * (target > _spool ? riseC : fallC);
            _gate += (gateTarget - _gate) * (on ? gateUp : gateDown);
            if (_gate <= 0f && !on)
            {
                _gate = 0f;
                if (_pending != null)
                {
                    _p = p = _pending.Value;
                    _pending = null;
                    riseC = Coeff(p.Rise); fallC = Coeff(p.Fall); gateDown = Coeff(p.Release / 3f);
                    on = On;
                    gateTarget = on ? 1f : -0.05f;
                    target = on ? p.Idle + (1f - p.Idle) * thrust : 0f;
                }
                else
                {
                    // Silent and stopped: the filters settle so the next start begins from nothing.
                    _spool = 0f; _low = _band = _rumbleLp = 0f;
                    mono[offset + i] = 0f;
                    continue;
                }
            }

            float s = _spool, air = _air;
            float shape = s * MathF.Sqrt(s);          // loudness grows faster than thrust
            float level = p.Level * _gate * (0.12f + 0.88f * shape);

            // Slow wanderings so the roar breathes and never repeats.
            _wobble1 += (Noise() - _wobble1) * wobbleC;
            _wobble2 += (Noise() - _wobble2) * wobble2C;
            float breathe = Math.Clamp(1f + 0.16f * _wobble1 * wobbleNorm * (0.4f + 0.6f * s) + 0.08f * _wobble2 * wobble2Norm, 0.4f, 1.6f);

            // Roar: noise through a state-variable low-pass whose cut-off climbs with the spool and closes in vacuum.
            float fc = p.RoarLow + (p.RoarHigh - p.RoarLow) * s * s * (1.3f - 0.3f * s);
            fc *= 0.55f + 0.45f * air;
            float f = 2f * MathF.Sin(MathF.PI * Math.Min(fc, Rate / 5f) / Rate);
            float n = Noise();
            _low += f * _band;
            float high = n - _low - Q * _band;
            _band += f * high;
            float roar = _low * MathF.Sqrt(Rate * 0.5f / fc) * 0.42f;
            float roarMix = 0.16f + 0.84f * air;

            // Rumble: very low noise plus a tone and its octave, the tone rising with the spool.
            float a = Math.Min(1f, 6.2831853f * (p.RumbleLow + 30f * s) / Rate);
            _rumbleLp += a * (Noise() - _rumbleLp);
            float rumbleNoise = _rumbleLp * MathF.Sqrt((2f - a) / a * 3f) * 0.35f;
            double rf = p.RumbleLow + (p.RumbleHigh - p.RumbleLow) * s;
            _phaseRumble += rf / Rate;
            _phaseRumble -= Math.Floor(_phaseRumble);
            float tone = (float)(Math.Sin(_phaseRumble * Math.PI * 2.0) + 0.45 * Math.Sin(_phaseRumble * Math.PI * 4.0 + 0.7));
            float rumble = (rumbleNoise + 0.55f * tone) * p.Rumble * (1f + 0.35f * air) * (0.55f + 0.45f * s);

            // Whine: two close tones that beat, rising in pitch and fading up late in the spool.
            float whine = 0f;
            if (p.Whine > 0f)
            {
                double wf = p.WhineLow + (p.WhineHigh - p.WhineLow) * s;
                _phaseWhine += wf / Rate;
                _phaseWhine -= Math.Floor(_phaseWhine);
                _phaseWhine2 += wf * 1.012 / Rate;
                _phaseWhine2 -= Math.Floor(_phaseWhine2);
                whine = (float)(Math.Sin(_phaseWhine * Math.PI * 2.0) + Math.Sin(_phaseWhine2 * Math.PI * 2.0)) * 0.5f * p.Whine * s * s * air;
            }

            float x = level * (roar * roarMix * breathe * (0.7f + 0.6f * s) + rumble + whine);
            mono[offset + i] = MathF.Tanh(x);
        }
    }
}
