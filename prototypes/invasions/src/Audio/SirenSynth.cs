using System;

namespace Invasions.Audio;

/// <summary>
/// A synthesised WW2-style mechanical air-raid siren. Plain C# (no engine types), so the game streams it
/// live and tests/SirenPreview renders the very same samples to a WAV.
///
/// It models the machine rather than drawing a pitch curve: a motor spins a rotor whose ports chop
/// the air, and the pitch and loudness follow the rotor speed.
/// - Alert: the motor is switched on and off on a cycle, so the rotor winds up and coasts back down
///   in a slow rising and falling wail (about 4.6 s per cycle). It never falls fully back between
///   wails, which is what gives the long moan.
/// - All-clear: the motor stays on, so the rotor settles on one steady note, then it is switched off
///   and the rotor runs down to silence.
/// - The sound: two rotors a minor third apart (the classic dual-tone chord), each a stack of
///   harmonics with a soft saturating edge, a little rotor wobble, air noise chopped by the ports, and
///   loudness that rises with pitch. A short and a long echo carry it across the base, offset left and
///   right for width.
/// Because the motor drives everything, it fades in as it spins up and fades out as it runs down, and
/// a live stream of it loops with no seam.
/// </summary>
public sealed class SirenSynth
{
    public enum Mode { Off, Alert, AllClear }

    public int SampleRate { get; }
    public Mode Current { get; private set; } = Mode.Off;
    /// <summary>Rotor speed, 0 at rest to about 1 flat out.</summary>
    public double RotorSpeed { get; private set; }
    /// <summary>The low rotor's fundamental right now, in Hz.</summary>
    public double Frequency => TopFrequency * RotorSpeed * Wobble(_t);
    /// <summary>Anything still sounding, echoes included.</summary>
    public bool Audible => RotorSpeed > 0.01 || _motorOn || _tail > 0;
    public bool MotorOn => _motorOn;

    // The machine.
    const double TopFrequency = 485;     // low rotor at full speed; the alert wails between ~210 and ~465 Hz
    const double SecondRotor = 1.2;      // a minor third above
    const double SpinUp = 1.0;           // seconds: time constant with the motor on
    const double RunDown = 2.6;          // seconds: time constant coasting
    const double AlertOn = 2.6, AlertOff = 2.0;
    const double AllClearGap = 1.6;      // the alert stops and the rotor drops before the steady note

    readonly double[] _partials = new double[9];
    readonly float[] _echoL, _echoR, _farEcho;
    int _echoPos, _farPos;
    double _t, _cycle, _allClearLeft, _gapLeft, _phaseA, _phaseB, _master, _noiseLow, _noiseHigh, _lpL, _lpR;
    bool _motorOn;
    int _tail;
    ulong _noise;

    public SirenSynth(int sampleRate, ulong seed = 0x51E7)
    {
        SampleRate = sampleRate;
        _noise = seed | 1;
        _echoL = new float[(int)(0.29 * sampleRate)];
        _echoR = new float[(int)(0.37 * sampleRate)];
        _farEcho = new float[(int)(0.95 * sampleRate)];
        // Odd partials a touch stronger: the ports chop the air into something between a sine and a pulse.
        for (int k = 1; k < _partials.Length; k++) _partials[k] = Math.Pow(k, -1.6) * (k % 2 == 1 ? 1.25 : 0.8);
    }

    /// <summary>Starts (or keeps) the rising and falling alert.</summary>
    public void Alert()
    {
        if (Current == Mode.Alert) return;
        Current = Mode.Alert;
        _cycle = 0;
        _gapLeft = 0;
    }

    /// <summary>Holds a steady note for <paramref name="seconds"/>, then lets the rotor run down to silence.</summary>
    public void AllClear(double seconds)
    {
        _gapLeft = Current == Mode.Alert ? AllClearGap : 0;
        Current = Mode.AllClear;
        _allClearLeft = seconds;
    }

    /// <summary>Switches the motor off: the rotor runs down and the siren dies away.</summary>
    public void Stop() => Current = Mode.Off;

    /// <summary>Renders the next samples into both channels.</summary>
    public void Render(Span<float> left, Span<float> right)
    {
        double dt = 1.0 / SampleRate;
        for (int i = 0; i < left.Length; i++)
        {
            StepMotor(dt);
            double s = RotorSpeed;
            double f = TopFrequency * s * Wobble(_t);
            _phaseA = (_phaseA + f * dt) % 1.0;
            _phaseB = (_phaseB + f * SecondRotor * dt) % 1.0;

            double tone = Voice(_phaseA, f) + 0.8 * Voice(_phaseB, f * SecondRotor);
            // Air rushing through the ports: band-limited noise, chopped at the rotor rate.
            double white = NextNoise();
            _noiseLow += (white - _noiseLow) * 0.35;
            _noiseHigh += (_noiseLow - _noiseHigh) * 0.04;
            double air = (_noiseLow - _noiseHigh) * (0.65 + 0.35 * Math.Sin(_phaseA * 2 * Math.PI)) * 0.55;

            // Louder as it winds up, and the master envelope smooths the very start and end.
            _master += ((_motorOn || s > 0.03 ? 1.0 : 0.0) - _master) * (dt / 0.6);
            double loud = Math.Pow(s, 1.6) * _master;
            double dry = Math.Tanh((tone * 0.55 + air * s) * 1.35) * 0.66 * loud;

            // Echoes off the far walls and the ground, darker than the direct sound.
            float el = _echoL[_echoPos % _echoL.Length], er = _echoR[_echoPos % _echoR.Length], far = _farEcho[_farPos];
            _lpL += (el - _lpL) * 0.25;
            _lpR += (er - _lpR) * 0.25;
            _echoL[_echoPos % _echoL.Length] = (float)(dry + _lpL * 0.38);
            _echoR[_echoPos % _echoR.Length] = (float)(dry + _lpR * 0.38);
            _farEcho[_farPos] = (float)(dry * 0.6 + far * 0.25);
            _echoPos = (_echoPos + 1) % (_echoL.Length * _echoR.Length);
            _farPos = (_farPos + 1) % _farEcho.Length;

            left[i] = (float)(dry + _lpL * 0.30 + far * 0.16);
            right[i] = (float)(dry + _lpR * 0.30 + far * 0.18);

            _tail = Math.Abs(left[i]) + Math.Abs(right[i]) > 1e-4 ? SampleRate : Math.Max(0, _tail - 1);
            _t += dt;
        }
    }

    void StepMotor(double dt)
    {
        switch (Current)
        {
            case Mode.Alert:
                _cycle = (_cycle + dt) % (AlertOn + AlertOff);
                _motorOn = _cycle < AlertOn;
                break;
            case Mode.AllClear:
                if (_gapLeft > 0)
                {
                    _gapLeft -= dt;
                    _motorOn = false;
                }
                else
                {
                    _allClearLeft -= dt;
                    _motorOn = _allClearLeft > 0;
                    if (!_motorOn) Current = Mode.Off;
                }
                break;
            default:
                _motorOn = false;
                break;
        }
        double target = _motorOn ? 1.0 : 0.0;
        double tau = _motorOn ? SpinUp : RunDown;
        RotorSpeed += (target - RotorSpeed) * (dt / tau);
        if (!_motorOn && RotorSpeed < 1e-4) RotorSpeed = 0;
    }

    /// <summary>One rotor: harmonics up to well below Nyquist, so nothing aliases at full speed.</summary>
    double Voice(double phase, double f)
    {
        double sum = 0;
        double angle = phase * 2 * Math.PI;
        for (int k = 1; k < _partials.Length && f * k < SampleRate * 0.4; k++) sum += _partials[k] * Math.Sin(angle * k);
        return sum;
    }

    /// <summary>A rotor never spins perfectly true.</summary>
    static double Wobble(double t) => 1 + 0.0035 * Math.Sin(2 * Math.PI * 5.3 * t) + 0.002 * Math.Sin(2 * Math.PI * 0.7 * t);

    double NextNoise()
    {
        _noise ^= _noise << 13;
        _noise ^= _noise >> 7;
        _noise ^= _noise << 17;
        return (_noise >> 11) * (2.0 / (1UL << 53)) - 1.0;
    }
}
