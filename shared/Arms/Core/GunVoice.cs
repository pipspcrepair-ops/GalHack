using System;

namespace GalHack.Arms;

/// <summary>Which of a gun's sounds.</summary>
public enum VoiceKind { Report, Mechanism, Reload, Impact, Stage, Hold }

/// <summary>
/// Every gun's sounds, synthesised from its parts at load: no files. What a gun is sets how it
/// sounds: its calibre (a small round cracks high and short, a big one booms low and long), its
/// energy (a zap and a hum instead of powder), whether it is alive (wet, breathy, no metal), and
/// its parts (pellets add body, a blast a deep thump, fire a roar, a charge rising clicks). Each
/// gun's noise comes from its own seed, so no two share a report. Mono samples at <see cref="Rate"/>.
/// </summary>
public static class GunVoice
{
    public const int Rate = 22050;

    /// <summary>The traits a gun's sounds are drawn from.</summary>
    public readonly record struct Traits(float Calibre, bool Energy, bool Organic, bool Explosive, bool Fire, bool Shock, int Pellets, bool Placed, uint Seed);

    public static Traits Of(WeaponDef d) => new(
        Math.Clamp(d.Look.Calibre, 0f, 1f), d.Class == DamageClass.Energy && (d.Tags & HarmTags.Fire) == 0, d.Look.Band == Band.Living,
        d.Blast != null || d.Class == DamageClass.Explosive, (d.Tags & HarmTags.Fire) != 0, (d.Tags & HarmTags.Shock) != 0,
        d.Pellets?.Count ?? 1, d.Placed != null, Hash(d.Id));

    public static float[] Make(WeaponDef d, VoiceKind kind)
    {
        Traits t = Of(d);
        var rng = new Noise(t.Seed ^ (uint)(kind + 1) * 2654435761u);
        return kind switch
        {
            VoiceKind.Report => Report(t, rng),
            VoiceKind.Mechanism => Mechanism(t, rng, d),
            VoiceKind.Reload => Reload(t, rng),
            VoiceKind.Impact => Impact(t, rng),
            VoiceKind.Stage => Stage(t, rng),
            _ => Hold(t, rng),
        };
    }

    // ---- The shot itself ---------------------------------------------------------------------

    static float[] Report(Traits t, Noise n)
    {
        if (t.Fire) return Roar(n, 0.35f, 1f);
        if (t.Organic)
        {
            // A wet cough: low noise through a falling throat formant, a slap at the start.
            float len = 0.22f + 0.2f * t.Calibre;
            return Render(len, (time, i) =>
            {
                float env = MathF.Exp(-time * (14f - 6f * t.Calibre)) * MathF.Min(1f, time * 400f);
                float f = 520f - 300f * time / len;
                return env * (n.Band(i, f, 0.18f) * 1.4f + n.Low(i, 0.08f) * 0.6f) + MathF.Exp(-time * 80f) * n.Next() * 0.3f;
            });
        }
        if (t.Energy)
        {
            // A zap: a falling tone, a buzz of harmonics, a fizz; lightning adds a crackle.
            float len = 0.18f + 0.25f * t.Calibre;
            float f0 = 1400f - 900f * t.Calibre;
            float phase = 0f;
            return Render(len, (time, i) =>
            {
                float env = MathF.Exp(-time * (11f - 4f * t.Calibre)) * MathF.Min(1f, time * 600f);
                float f = f0 * MathF.Exp(-time * 9f) + 120f;
                phase += f / Rate;
                float tone = MathF.Sin(MathF.Tau * phase) * 0.5f + MathF.Sin(MathF.Tau * phase * 2.01f) * 0.2f + (Frac(phase) - 0.5f) * 0.3f;
                float crack = t.Shock ? (n.Next() > 0.92f - 0.2f * env ? n.Next() * 2f - 1f : 0f) * 0.9f : 0f;
                return env * (tone + n.High(i, 0.6f) * 0.25f + crack);
            });
        }
        {
            // Powder: a sharp crack, a body thump whose pitch falls with calibre, a rolling tail.
            float len = 0.3f + 0.9f * t.Calibre + (t.Explosive ? 0.4f : 0f);
            float body = (t.Explosive ? 70f : 150f) - 80f * t.Calibre;
            float cut = 0.55f - 0.42f * t.Calibre;
            float phase = 0f;
            float pellets = MathF.Min(1f, (t.Pellets - 1) / 8f);
            return Render(len, (time, i) =>
            {
                float crack = MathF.Exp(-time * 260f) * n.High(i, 0.9f) * (1.2f - t.Calibre * 0.5f);
                float thumpEnv = MathF.Exp(-time * (30f - 16f * t.Calibre)) * MathF.Min(1f, time * 1500f);
                phase += body * (1f + 0.8f * MathF.Exp(-time * 40f)) / Rate;
                float thump = MathF.Sin(MathF.Tau * phase) * thumpEnv * (0.7f + 0.4f * t.Calibre + 0.3f * pellets);
                float tail = n.Low(i, cut) * MathF.Exp(-time * (7f - 4f * t.Calibre)) * (0.55f + 0.3f * pellets);
                return crack + thump + tail;
            });
        }
    }

    // ---- The action between shots: a bolt, a pump, a hum, a squelch --------------------------------

    static float[] Mechanism(Traits t, Noise n, WeaponDef d)
    {
        if (t.Organic) return Render(0.25f, (time, i) => MathF.Exp(-time * 12f) * n.Band(i, 300f + 200f * MathF.Sin(time * 40f), 0.2f) * 0.8f);
        if (t.Energy || t.Fire)
            return Render(0.3f, (time, i) => MathF.Sin(MathF.Tau * (220f + 500f * time) * time) * 0.2f * MathF.Exp(-time * 6f) + n.High(i, 0.3f) * 0.05f * MathF.Exp(-time * 10f));
        if (d.Pump > 0f)
            // A pump: a slide back, a clack, a slide forward, a clack.
            return Render(0.42f, (time, i) => Click(time, 0f, 900f, n) * 0.6f + Slide(time, 0.02f, 0.14f, n, i) + Click(time, 0.16f, 1300f, n) + Slide(time, 0.2f, 0.12f, n, i) * 0.8f + Click(time, 0.33f, 1100f, n));
        // A bolt or a slide: two short metal clicks.
        return Render(0.2f, (time, i) => Click(time, 0.0f, 1800f - 600f * t.Calibre, n) + Click(time, 0.07f, 2400f - 800f * t.Calibre, n) * 0.7f);
    }

    static float[] Reload(Traits t, Noise n)
    {
        if (t.Organic)
            // A wet gulp as the meat goes in, a slurp, a settling throb.
            return Render(0.9f, (time, i) => MathF.Exp(-MathF.Abs(time - 0.15f) * 14f) * n.Band(i, 260f, 0.2f) * 0.9f
                + MathF.Exp(-MathF.Abs(time - 0.5f) * 8f) * n.Band(i, 180f + 120f * time, 0.12f) * 0.7f
                + MathF.Sin(MathF.Tau * 70f * time) * 0.25f * MathF.Exp(-MathF.Abs(time - 0.75f) * 12f));
        if (t.Energy || t.Fire)
            // A cell out with a clunk, in with a clack, then the charge-up whine.
            return Render(0.95f, (time, i) => Click(time, 0.05f, 700f, n) + Click(time, 0.4f, 1000f, n)
                + (time > 0.45f ? MathF.Sin(MathF.Tau * (300f + 900f * (time - 0.45f)) * (time - 0.45f)) * 0.22f * MathF.Exp(-(time - 0.45f) * 3f) : 0f));
        // A magazine out, a slide of metal, a magazine in, a bolt home.
        return Render(0.85f, (time, i) => Click(time, 0.04f, 900f, n) + Slide(time, 0.1f, 0.2f, n, i) * 0.7f + Click(time, 0.42f, 1100f, n) + Click(time, 0.66f, 1700f, n) * 0.9f);
    }

    static float[] Impact(Traits t, Noise n)
    {
        if (t.Explosive)
        {
            // A blast: a deep falling boom and a long rumble of debris.
            float phase = 0f;
            return Render(1.6f, (time, i) =>
            {
                phase += (55f * MathF.Exp(-time * 2f) + 25f) / Rate;
                return MathF.Sin(MathF.Tau * phase) * MathF.Exp(-time * 3f) * 0.9f + n.Low(i, 0.12f) * MathF.Exp(-time * 2.2f) * 1.1f + MathF.Exp(-time * 120f) * n.Next() * 0.6f;
            });
        }
        if (t.Organic) return Render(0.2f, (time, i) => MathF.Exp(-time * 25f) * n.Band(i, 380f, 0.25f));
        if (t.Energy) return Render(0.18f, (time, i) => MathF.Exp(-time * 30f) * (n.High(i, 0.7f) * 0.6f + MathF.Sin(MathF.Tau * 900f * time) * 0.3f));
        return Render(0.16f, (time, i) => MathF.Exp(-time * 40f) * (n.Next() * 2f - 1f) * 0.6f + MathF.Exp(-time * 30f) * MathF.Sin(MathF.Tau * (2600f - 1200f * t.Calibre) * time) * 0.25f);
    }

    /// <summary>One charge stage: a click and a tone a step higher each time (the player pitches it).</summary>
    static float[] Stage(Traits t, Noise n) => Render(0.16f, (time, i) => Click(time, 0f, 2000f, n) * 0.7f + MathF.Sin(MathF.Tau * 660f * time) * 0.3f * MathF.Exp(-time * 18f));

    /// <summary>The sound held while firing continues: a flame's roaring tear, a hive's buzz, a beam's hum. One second, made to loop.</summary>
    static float[] Hold(Traits t, Noise n)
    {
        if (t.Fire) return Roar(n, 1f, 0.8f, loop: true);
        if (t.Organic)
        {
            float phase = 0f;
            return Render(1f, (time, i) =>
            {
                phase += (190f + 15f * MathF.Sin(MathF.Tau * 3f * time)) / Rate;
                return ((Frac(phase) > 0.5f ? 1f : -1f) * 0.18f + n.Band(i, 380f, 0.3f) * 0.2f) * (0.7f + 0.3f * MathF.Sin(MathF.Tau * 9f * time));
            });
        }
        return Render(1f, (time, i) => MathF.Sin(MathF.Tau * 110f * time) * 0.3f + n.High(i, 0.2f) * 0.08f);
    }

    static float[] Roar(Noise n, float seconds, float level, bool loop = false) => Render(seconds, (time, i) =>
    {
        float env = loop ? 1f : MathF.Min(1f, time * 30f) * MathF.Exp(-time * 3f);
        float flutter = 0.75f + 0.25f * MathF.Sin(MathF.Tau * 13f * time) * MathF.Sin(MathF.Tau * 3.1f * time);
        return env * level * (n.Low(i, 0.18f) * 1.3f + n.Band(i, 900f, 0.08f) * 0.5f) * flutter;
    });

    // ---- Building blocks ---------------------------------------------------------------------

    static float Click(float time, float at, float freq, Noise n)
    {
        float t = time - at;
        if (t < 0f) return 0f;
        return MathF.Exp(-t * 180f) * (MathF.Sin(MathF.Tau * freq * t) * 0.6f + (n.Next() * 2f - 1f) * 0.5f);
    }

    static float Slide(float time, float at, float length, Noise n, int i)
    {
        float t = time - at;
        if (t < 0f || t > length) return 0f;
        return MathF.Sin(MathF.PI * t / length) * n.High(i, 0.4f) * 0.18f;
    }

    static float[] Render(float seconds, Func<float, int, float> f)
    {
        int count = Math.Max(1, (int)(seconds * Rate));
        var s = new float[count];
        float peak = 1e-4f;
        for (int i = 0; i < count; i++)
        {
            s[i] = f(i / (float)Rate, i);
            peak = MathF.Max(peak, MathF.Abs(s[i]));
        }
        // Normalised to a common peak, with a few milliseconds' fade at the end so nothing clicks.
        float gain = 0.9f / peak;
        int fade = Math.Min(count, Rate / 200);
        for (int i = 0; i < count; i++)
        {
            float g = i >= count - fade ? (count - i) / (float)fade : 1f;
            s[i] = Math.Clamp(s[i] * gain * g, -1f, 1f);
        }
        return s;
    }

    static float Frac(float x) => x - MathF.Floor(x);

    static uint Hash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }

    /// <summary>A small running noise source with one-pole filters (low, high and a resonant band), so each sound is its own.</summary>
    sealed class Noise
    {
        uint _s;
        float _low, _high, _prev, _b1, _b2;
        int _lowAt = -1, _highAt = -1, _bandAt = -1;

        public Noise(uint seed) => _s = seed == 0 ? 1u : seed;

        public float Next()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return (_s & 0xffffff) / (float)0x1000000;
        }

        float White() => Next() * 2f - 1f;

        public float Low(int i, float k)
        {
            if (i != _lowAt) { _low += (White() - _low) * k; _lowAt = i; }
            return _low * 1.6f;
        }

        public float High(int i, float k)
        {
            if (i != _highAt)
            {
                float x = White();
                _high = k * (_high + x - _prev);
                _prev = x;
                _highAt = i;
            }
            return _high;
        }

        /// <summary>White noise through a two-pole resonance at a frequency (Hz), q as a damping share.</summary>
        public float Band(int i, float freq, float damping)
        {
            if (i != _bandAt)
            {
                float w = MathF.Tau * Math.Clamp(freq, 20f, Rate * 0.45f) / Rate;
                float r = 1f - damping * w;
                float y = White() * damping + 2f * r * MathF.Cos(w) * _b1 - r * r * _b2;
                _b2 = _b1;
                _b1 = y;
                _bandAt = i;
            }
            return _b1 * 3f;
        }
    }
}
