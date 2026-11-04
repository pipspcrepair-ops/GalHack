using System;
using Godot;

namespace DiegeticUi.World;

/// <summary>
/// The prototype's sounds, synthesised in code: no audio files, nothing to license. A
/// rising-and-falling siren whoop for the invasion warning, and a sharp two-tone chirp for a
/// warning shot.
/// </summary>
public static class Sfx
{
    const int Rate = 22050;

    /// <summary>
    /// One siren cycle, looped while the sirens turn. The pitch sweeps 520-900-520 Hz over
    /// 1.6 s, which is a whole number of waves (1136), so the loop has no click at the seam.
    /// </summary>
    public static AudioStreamWav SirenLoop()
    {
        const double length = 1.6;
        var samples = new double[(int)(length * Rate)];
        double phase = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)Rate;
            double freq = 520 + 380 * (0.5 - 0.5 * Math.Cos(t / length * Math.Tau));
            phase += freq * Math.Tau / Rate;
            double tone = Math.Sin(phase);
            samples[i] = 0.2 * tone + 0.08 * Math.Sign(tone); // a little square edge: a klaxon, not a flute
        }
        return Wav(samples, loop: true);
    }

    /// <summary>A short high-low chirp: "that shot was meant for you".</summary>
    public static AudioStreamWav WarningChirp()
    {
        var samples = new double[(int)(0.34 * Rate)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)Rate;
            bool first = t < 0.15;
            double env = first ? Envelope(t, 0.15) : Envelope(t - 0.17, 0.17);
            samples[i] = Math.Sin(t * Math.Tau * (first ? 1320 : 880)) * env * 0.45;
        }
        return Wav(samples);
    }

    static double Envelope(double t, double length) =>
        t < 0 || t > length ? 0 : Math.Min(1, t / 0.008) * Math.Min(1, (length - t) / 0.03);

    static AudioStreamWav Wav(double[] samples, bool loop = false)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)(Math.Clamp(samples[i], -1, 1) * short.MaxValue);
            data[i * 2] = (byte)(v & 0xff);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }
        var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
        if (loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = samples.Length;
        }
        return wav;
    }
}
