using System;
using System.Diagnostics;
using System.IO;
using Godot;
using Slice.Game;

namespace Slice.Tests;

/// <summary>
/// The engines' sound, by measurement (nobody listens here). The synth is rendered exactly as the
/// game plays it and checked: no sample jumps anywhere, however the audio is cut into buffers;
/// no repeating pattern (with the old looped noise as the control that the detector can see one);
/// a smooth rise and fall with thrust; brightness climbing with thrust; a bigger vessel deeper
/// than a smaller one; vacuum keeping the rumble; clean starts, stops and vessel changes; no
/// clipping; and a cost far under real time. It also renders the sweep and the steady full-thrust
/// samples as WAVs (to $THRUSTER_OUT, or the temp folder) and checks the real player feeds the
/// engine on the effects bus. Voices start their noise from fresh random states each run, except
/// where two renders must be compared with each other.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/ThrusterSoundTest.tscn
/// </summary>
public partial class ThrusterSoundTest : Node
{
    const int Rate = ThrusterVoice.Rate;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    ThrusterPlayer _player;
    double _waited;

    static readonly VoiceProfile Skiff = VoiceProfile.Vessel(100f), Large = VoiceProfile.Vessel(1200f), Capital = VoiceProfile.Vessel(4800f);

    public override void _Ready()
    {
        try
        {
            Profiles();
            Jumps(Large, "large vessel");
            Jumps(Skiff, "skiff");
            Jumps(Capital, "capital ship");
            Jumps(VoiceProfile.Jetpack, "jetpack");
            Repeats(Large, "large vessel");
            Repeats(VoiceProfile.Jetpack, "jetpack");
            Sweep();
            Brightness();
            Size();
            Vacuum();
            Starts(Large, "large vessel");
            Starts(VoiceProfile.Jetpack, "jetpack");
            Switch();
            Cost();
            Samples();
            SliceAudio.MakeBuses();
            _player = new ThrusterPlayer { Name = "Engines", Voice = new ThrusterVoice(Large) { On = true, Thrust = 0.6f } };
            AddChild(_player);
            foreach (Node n in _player.GetChildren()) SliceAudio.Route(n);
            Check(_player.GetChild<AudioStreamPlayer>(0).Bus == "Effects", "the engines play on the Effects bus, so the effects volume sets them");
        }
        catch (Exception e)
        {
            Check(false, $"threw: {e}");
            Finish();
        }
    }

    public override void _Process(double delta)
    {
        if (_player == null) return;
        _waited += delta;
        if (_waited < 1.5) return;
        Check(_player.Pushed >= Rate, $"the real player fed the engine's buffer ({_player.Pushed} frames in {_waited:F1} s)");
        SetProcess(false);
        Finish();
    }

    void Finish()
    {
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else
        {
            _failures++;
            GD.Print($"FAIL: {what}");
        }
    }

    // ---- rendering and measuring -----------------------------------------------------------

    /// <summary>Runs a voice in game-sized chunks, setting the thrust and the running state before each from functions of time.</summary>
    static float[] Run(ThrusterVoice v, float seconds, Func<float, float> thrust, Func<float, bool> on = null, int chunk = 512)
    {
        int total = (int)(seconds * Rate);
        var all = new float[total];
        var buf = new float[chunk];
        for (int at = 0; at < total; at += chunk)
        {
            int n = Math.Min(chunk, total - at);
            float t = (float)at / Rate;
            v.Thrust = thrust(t);
            v.On = on?.Invoke(t) ?? true;
            v.Render(buf, n);
            Array.Copy(buf, 0, all, at, n);
        }
        return all;
    }

    static float[] Steady(VoiceProfile p, float thrust, float seconds, float air = 1f)
    {
        var v = new ThrusterVoice(p) { Air = air };
        return Run(v, seconds, _ => thrust);
    }

    static double Rms(float[] x, int from, int to)
    {
        double sum = 0.0;
        for (int i = from; i < to; i++) sum += x[i] * x[i];
        return Math.Sqrt(sum / Math.Max(1, to - from));
    }

    static float MaxStep(float[] x, int from, int to)
    {
        float m = 0f;
        for (int i = Math.Max(1, from); i < to; i++) m = Math.Max(m, Math.Abs(x[i] - x[i - 1]));
        return m;
    }

    static float Peak(float[] x)
    {
        float m = 0f;
        foreach (float s in x) m = Math.Max(m, Math.Abs(s));
        return m;
    }

    /// <summary>The spectral centroid in Hz, and the share of energy under 150 Hz, averaged over 4096-sample windows from the given sample on.</summary>
    static (double centroid, double lowShare) Spectrum(float[] x, int from)
    {
        const int N = 4096;
        double c = 0.0, low = 0.0;
        int windows = 0;
        var re = new double[N];
        var im = new double[N];
        for (int at = from; at + N <= x.Length && windows < 12; at += N, windows++)
        {
            for (int i = 0; i < N; i++)
            {
                re[i] = x[at + i] * (0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / N));
                im[i] = 0.0;
            }
            Fft(re, im);
            double sum = 0.0, weighted = 0.0, lo = 0.0;
            for (int k = 1; k < N / 2; k++)
            {
                double power = re[k] * re[k] + im[k] * im[k], hz = (double)k * Rate / N;
                sum += power;
                weighted += power * hz;
                if (hz < 150.0) lo += power;
            }
            c += weighted / Math.Max(sum, 1e-18);
            low += lo / Math.Max(sum, 1e-18);
        }
        return (c / windows, low / windows);
    }

    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1.0, ci = 0.0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
    }

    // ---- the checks --------------------------------------------------------------------------

    void Profiles()
    {
        VoiceProfile a = VoiceProfile.Vessel(50f), b = VoiceProfile.Vessel(300f), c = VoiceProfile.Vessel(2400f);
        Check(a.Depth < b.Depth && b.Depth < c.Depth, $"more thrust reads as a bigger vessel (depth {a.Depth:F2} < {b.Depth:F2} < {c.Depth:F2})");
        Check(a.RoarHigh > b.RoarHigh && b.RoarHigh > c.RoarHigh && a.RumbleLow > c.RumbleLow, "a bigger vessel's roar is lower and darker");
        Check(a.Rise < c.Rise && a.Fall < c.Fall, "a bigger vessel spools up and down more slowly");
        Check(VoiceProfile.Vessel(0f).Depth == 0f && VoiceProfile.Vessel(1e9f).Depth == 1f, "the extremes clamp");
    }

    /// <summary>No sample jumps: the same output however it is cut into buffers, and no step beyond what the signal's own brightness allows.</summary>
    void Jumps(VoiceProfile p, string name)
    {
        Func<float, float> sweep = t => t < 1f ? 0f : Math.Clamp((t - 1f) / 6f, 0f, 1f) * (t < 10f ? 1f : Math.Clamp(1f - (t - 10f) / 6f, 0f, 1f));
        float[] a = Run(new ThrusterVoice(p), 18f, sweep, t => t < 16f, 512);
        // However the audio is cut into buffers the sound is the same, sample for sample (thrust held, so only the cutting differs).
        uint seed = (uint)Random.Shared.Next(1, int.MaxValue);
        float[] b512 = Run(new ThrusterVoice(p, seed), 8f, _ => 0.7f, null, 512);
        float[] b64 = Run(new ThrusterVoice(p, seed), 8f, _ => 0.7f, null, 64);
        float[] b777 = Run(new ThrusterVoice(p, seed), 8f, _ => 0.7f, null, 777);
        double diff = 0.0;
        for (int i = 0; i < b512.Length; i++) diff = Math.Max(diff, Math.Max(Math.Abs(b512[i] - b64[i]), Math.Abs(b512[i] - b777[i])));
        Check(diff < 1e-6, $"{name}: cutting the audio into 64, 512 or 777-sample buffers gives the same sound (largest difference {diff:F7})");

        float[] steadyFull = Steady(p, 1f, 10f);
        float fullStep = MaxStep(steadyFull, 5 * Rate, 10 * Rate);
        float step = MaxStep(a, 0, a.Length);
        bool finite = true;
        foreach (float s in a) finite &= float.IsFinite(s);
        Check(finite, $"{name}: only finite samples");
        Check(step <= fullStep * 1.3f + 0.01f, $"{name}: the sweep's largest sample step {step:F3} is no bigger than full thrust's own {fullStep:F3}");

        // Buffer-boundary steps in particular: none stands out from the steps around it.
        float boundary = 0f, around = 0f;
        for (int at = 512; at < a.Length - 1; at += 512)
        {
            boundary = Math.Max(boundary, Math.Abs(a[at] - a[at - 1]));
            around = Math.Max(around, MaxStep(a, at - 40, at - 1));
        }
        Check(boundary <= Math.Max(around, 0.01f) * 1.5f, $"{name}: buffer boundaries show no jump (largest {boundary:F3}, around them {around:F3})");
        Check(Peak(a) < 0.95f, $"{name}: no clipping, peak {Peak(a):F2}");
        Check(Peak(steadyFull) > 0.05f, $"{name}: full thrust is audible, peak {Peak(steadyFull):F2}");
    }

    /// <summary>Whether any stretch of the sound comes round again: the old looped noise is the control.</summary>
    void Repeats(VoiceProfile p, string name)
    {
        float[] x = Steady(p, 1f, 20f);
        double worst = Recurrence(x);
        double control = Recurrence(OldLoop(20f));
        GD.Print($"repeat check: {name} {worst:F2}, old loop {control:F2}");
        Check(control > 0.8, $"control: the old looping noise shows as repeating (likeness {control:F2})");
        Check(worst < 0.35, $"{name}: 20 s of steady thrust never repeats (strongest likeness to any other stretch {worst:F2})");
    }

    /// <summary>The strongest normalised match between a 0.2 s stretch and any other stretch of the same sound, on the differenced and thinned signal (so a steady tone doesn't count).</summary>
    static double Recurrence(float[] x)
    {
        const int Thin = 3;
        int n = x.Length / Thin;
        var d = new float[n];
        for (int i = 1; i < n; i++) d[i] = x[i * Thin] - x[(i - 1) * Thin];
        int window = (int)(0.2f * Rate / Thin), at = (int)(6f * Rate / Thin);
        double ww = 0.0;
        for (int i = 0; i < window; i++) ww += d[at + i] * d[at + i];
        double best = 0.0;
        for (int lag = 0; lag + window <= n; lag++)
        {
            if (Math.Abs(lag - at) < window) continue; // the stretch itself and what overlaps it
            double ab = 0.0, bb = 0.0;
            for (int i = 0; i < window; i++)
            {
                float v = d[lag + i];
                ab += d[at + i] * v;
                bb += v * v;
            }
            best = Math.Max(best, Math.Abs(ab) / Math.Sqrt(ww * bb + 1e-18));
        }
        return best;
    }

    /// <summary>What the game had: 0.8 s of low-passed noise with its last 5% faded, looped, with a whisper of fresh noise on top.</summary>
    static float[] OldLoop(float seconds)
    {
        int len = (int)(0.8f * Rate), total = (int)(seconds * Rate);
        var loop = new float[len];
        var rng = new Random(11);
        float a = 420f / Rate * Mathf.Tau, y = 0f;
        for (int i = 0; i < len; i++)
        {
            y += a * ((float)rng.NextDouble() * 2f - 1f - y);
            float fade = i > len * 0.95f ? (len - i) / (len * 0.05f) : 1f;
            loop[i] = y * 3f * fade;
        }
        var x = new float[total];
        var rng2 = new Random(3);
        for (int i = 0; i < total; i++) x[i] = loop[i % len] + (float)(rng2.NextDouble() * 2.0 - 1.0) * 0.01f;
        return x;
    }

    /// <summary>A slow sweep up and down: loudness follows thrust smoothly with no steps, and the sound dies away to nothing.</summary>
    void Sweep()
    {
        float[] x = RenderSweep(Large);
        const int Win = Rate / 2, Hop = Rate / 4;
        double Db(int at) => 20.0 * Math.Log10(Rms(x, at, at + Win) + 1e-9);
        // Thrust rises 3..9 s and falls 12..18 s, and the sound lags it: judge the rise from 3.5 to 11 s and the fall from 13 to 20 s.
        double maxJump = 0.0, worstRise = 0.0, worstFall = 0.0;
        for (int at = (int)(3.5f * Rate); at + Hop + Win < (int)(11f * Rate); at += Hop)
        {
            double step = Db(at + Hop) - Db(at);
            maxJump = Math.Max(maxJump, Math.Abs(step));
            worstRise = Math.Min(worstRise, step);
        }
        for (int at = (int)(13f * Rate); at + Hop + Win < (int)(20f * Rate); at += Hop)
        {
            double step = Db(at + Hop) - Db(at);
            maxJump = Math.Max(maxJump, Math.Abs(step));
            worstFall = Math.Max(worstFall, step);
        }
        double idle = Db((int)(1f * Rate)), full = Db((int)(11f * Rate));
        GD.Print($"sweep: idle {idle:F1} dB, full {full:F1} dB, worst dip {worstRise:F1}, worst bump {worstFall:F1}, largest half-second jump {maxJump:F1} dB");
        Check(worstRise > -1.5, $"the sweep rises steadily (largest dip between half-second windows {worstRise:F1} dB)");
        Check(worstFall < 1.5, $"the sweep falls steadily (largest bump {worstFall:F1} dB)");
        Check(maxJump < 4.5, $"no half-second jump in loudness over 4.5 dB (largest {maxJump:F1} dB)");
        Check(full - idle > 15.0, $"full thrust is much louder than idle ({idle:F1} to {full:F1} dB)");
        Check(idle > -50.0, $"idle is soft but present ({idle:F1} dBFS)");
        Check(Rms(x, x.Length - Rate, x.Length) < 1e-4, "the sweep ends in silence");
    }

    /// <summary>Idle three seconds, up over six, hold three, down over six, idle two, engine off at 20 s and fade out.</summary>
    static float[] RenderSweep(VoiceProfile p)
    {
        Func<float, float> thrust = t => t < 3f ? 0f : t < 9f ? (t - 3f) / 6f : t < 12f ? 1f : t < 18f ? 1f - (t - 12f) / 6f : 0f;
        return Run(new ThrusterVoice(p), 26f, thrust, t => t < 20f);
    }

    /// <summary>Brighter and louder with every step up in thrust, for each kind of vessel and the jetpack.</summary>
    void Brightness()
    {
        foreach (var (p, name) in new[] { (Skiff, "skiff"), (Large, "large vessel"), (Capital, "capital ship"), (VoiceProfile.Jetpack, "jetpack") })
        {
            double last = 0.0, lastLevel = -1.0;
            bool rising = true, louder = true;
            string line = "";
            foreach (float thrust in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                float[] x = Steady(p, thrust, 14f);
                var (centroid, _) = Spectrum(x, 10 * Rate);
                double level = Rms(x, 10 * Rate, 14 * Rate);
                rising &= centroid > last;
                louder &= level > lastLevel;
                last = centroid;
                lastLevel = level;
                line += $" {centroid:F0}Hz/{20 * Math.Log10(level + 1e-9):F0}dB";
            }
            GD.Print($"brightness {name}:{line}");
            Check(rising, $"{name}: brightness rises with every step of thrust ({line.Trim()})");
            Check(louder, $"{name}: loudness rises with every step of thrust");
        }
    }

    void Size()
    {
        foreach (float thrust in new[] { 0.5f, 1f })
        {
            double small = Spectrum(Steady(Skiff, thrust, 14f), 10 * Rate).centroid;
            double big = Spectrum(Steady(Capital, thrust, 14f), 10 * Rate).centroid;
            Check(big < small * 0.8, $"at {thrust:P0} thrust a capital ship ({big:F0} Hz) is deeper than a skiff ({small:F0} Hz)");
        }
        Check(Rms(Steady(Capital, 1f, 14f), 10 * Rate, 14 * Rate) > Rms(Steady(Skiff, 1f, 14f), 10 * Rate, 14 * Rate), "a capital ship is the louder at full thrust");
    }

    /// <summary>In vacuum the roar and whine thin out and the rumble remains: darker, more of it low, still plainly there.</summary>
    void Vacuum()
    {
        float[] air = Steady(Large, 1f, 16f, 1f);
        float[] vac = Steady(Large, 1f, 16f, 0f);
        var (cAir, lowAir) = Spectrum(air, 12 * Rate);
        var (cVac, lowVac) = Spectrum(vac, 12 * Rate);
        double dbAir = 20 * Math.Log10(Rms(air, 12 * Rate, 16 * Rate)), dbVac = 20 * Math.Log10(Rms(vac, 12 * Rate, 16 * Rate));
        GD.Print($"vacuum: {cVac:F0} Hz / {dbVac:F1} dB against air {cAir:F0} Hz / {dbAir:F1} dB; share under 150 Hz {lowVac:P0} against {lowAir:P0}");
        Check(cVac < cAir * 0.7, $"vacuum is darker ({cVac:F0} Hz against {cAir:F0} Hz in air)");
        Check(lowVac > lowAir, $"vacuum keeps more of its energy in the rumble ({lowVac:P0} against {lowAir:P0})");
        Check(dbVac < dbAir && dbVac > dbAir - 14.0 && dbVac > -45.0, $"vacuum is quieter but still felt ({dbVac:F1} dB against {dbAir:F1} dB)");
    }

    /// <summary>Starts and stops without a click, on the engine's own fade.</summary>
    void Starts(VoiceProfile p, string name)
    {
        float[] x = Run(new ThrusterVoice(p), 12f, _ => 1f, t => t >= 1f && t < 7f);
        float full = MaxStep(x, 4 * Rate, 6 * Rate);
        float edge = MaxStep(x, Rate - 100, 8 * Rate);
        Check(Math.Abs(x[0]) < 1e-4f && Rms(x, 0, Rate / 2) < 1e-6, $"{name}: silent before it starts");
        Check(edge <= full * 1.3f + 0.01f, $"{name}: starting and stopping make no step bigger than the sound's own ({edge:F3} against {full:F3})");
        Check(Rms(x, 11 * Rate, 12 * Rate) < 1e-9, $"{name}: dies away to true silence after stopping");
        Check(Rms(x, Rate, Rate + Rate / 20) < Rms(x, 5 * Rate, 6 * Rate) * 0.1, $"{name}: the first 50 ms of starting is under a tenth of the full level");
    }

    /// <summary>Climbing into another vessel while the engine is running fades out, changes and fades back in: no jump.</summary>
    void Switch()
    {
        var v = new ThrusterVoice(Skiff);
        var buf = new float[512];
        var all = new float[10 * Rate];
        int swap = 4 * Rate / 512 * 512;
        for (int at = 0; at + 512 <= all.Length; at += 512)
        {
            v.On = true;
            v.Thrust = 0.8f;
            if (at == swap) v.Retarget(Capital);
            v.Render(buf, 512);
            Array.Copy(buf, 0, all, at, 512);
        }
        float own = MaxStep(Steady(Skiff, 0.8f, 8f), 4 * Rate, 8 * Rate);
        int worstAt = 0;
        for (int i = 1; i < all.Length; i++) if (Math.Abs(all[i] - all[i - 1]) > Math.Abs(all[worstAt] - all[Math.Max(0, worstAt - 1)])) worstAt = i;
        GD.Print($"switch: largest step at {worstAt / (float)Rate:F2} s");
        Check(MaxStep(all, 0, all.Length) <= own * 2f + 0.01f, $"changing vessel mid-flight jumps nowhere ({MaxStep(all, 0, all.Length):F3} against {own:F3})");
        Check(v.Profile.Depth == Capital.Depth && v.Gate > 0.9f, "the new vessel's sound is in place and fully open");
    }

    /// <summary>Cost: twenty seconds of the voice in a small part of the time it plays for.</summary>
    void Cost()
    {
        var v = new ThrusterVoice(Large) { Thrust = 0.7f, On = true };
        var buf = new float[512];
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < 20 * Rate / 512; i++) v.Render(buf, 512);
        double ms = timer.Elapsed.TotalMilliseconds;
        double share = ms / 20000.0;
        GD.Print($"cost: 20 s of sound in {ms:F0} ms, {share:P2} of one core");
        Check(share < 0.03, $"cheap: the voice takes {share:P2} of one core to play");
    }

    /// <summary>The sweep and the steady full-thrust sample, as WAV files for anyone who does want to listen.</summary>
    void Samples()
    {
        string dir = System.Environment.GetEnvironmentVariable("THRUSTER_OUT") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        float[] sweep = RenderSweep(Large);
        float[] steady = Steady(Large, 1f, 24f);
        WriteWav(Path.Combine(dir, "thruster-sweep.wav"), sweep);
        WriteWav(Path.Combine(dir, "thruster-steady.wav"), steady[(4 * Rate)..]); // after the spool-up: 20 s steady at full thrust
        Check(File.Exists(Path.Combine(dir, "thruster-steady.wav")), $"wrote the sample WAVs to {dir}");
        GD.Print($"sweep peak {Peak(sweep):F2}, steady peak {Peak(steady):F2}");
    }

    static void WriteWav(string path, float[] x)
    {
        using var w = new BinaryWriter(File.Create(path));
        int n = x.Length;
        w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        w.Write(36 + n * 2);
        w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        w.Write(n * 2);
        foreach (float s in x) w.Write((short)(Math.Clamp(s, -1f, 1f) * 32767f));
    }
}
