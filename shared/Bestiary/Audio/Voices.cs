using System;

namespace Wildlife;

/// <summary>How someone sounds right now: at ease, attacking, frightened quiet, frightened loud.</summary>
public enum VoiceMood { Murmur, Shout, Whisper, Cry }

/// <summary>
/// How a world's people speak, without a word of it being a word: which vowels they lean on,
/// how quick and clipped they are, and where their sentences go. A hint, heard across everyone
/// on a world; no two worlds quite alike.
/// </summary>
public sealed class Dialect
{
    /// <summary>First and second formants of this world's favourite vowels, Hz for a grown man.</summary>
    public (float F1, float F2)[] Vowels;
    /// <summary>Syllables a second; how far a phrase rises (+) or falls (-) by its end, as a share of pitch; how often a consonant hisses rather than stops.</summary>
    public float Rate, Lilt, Hiss;

    static readonly (float, float)[] All =
    {
        (730f, 1090f), (530f, 1840f), (270f, 2290f), (570f, 840f), (300f, 870f),
        (660f, 1720f), (520f, 1190f), (490f, 1350f), (440f, 1020f), (390f, 1990f),
    };

    public static Dialect For(ulong worldSeed)
    {
        var rng = Rng.For(worldSeed, "dialect");
        int n = rng.Int(4, 6);
        var vowels = new (float, float)[n];
        var pool = new System.Collections.Generic.List<(float, float)>(All);
        rng.Shuffle(pool);
        float tilt = rng.Range(0.9f, 1.12f);
        for (int i = 0; i < n; i++) vowels[i] = (pool[i].Item1 * tilt, pool[i].Item2 * rng.Range(0.92f, 1.08f));
        return new Dialect { Vowels = vowels, Rate = rng.Range(3.6f, 6.4f), Lilt = rng.Range(-0.3f, 0.3f), Hiss = rng.Range(0.1f, 0.6f) };
    }
}

/// <summary>
/// One person's voice, worked out from their body: a long throat and a heavy frame speak low, a
/// child's short one high, age thins and roughens it. Rendered by a small formant synthesiser
/// into syllables that aren't words. All sound is generated here; nothing is recorded.
/// </summary>
public sealed class Voice
{
    public const int Rate = 22050;

    public ulong Seed;
    public Dialect Dialect;
    /// <summary>Speaking pitch, Hz.</summary>
    public float Pitch;
    /// <summary>Formants scaled for their throat: 1 is a man of 1.75 m.</summary>
    public float Throat;
    /// <summary>How much breath and how much gravel is in it, 0 to 1.</summary>
    public float Breath, Rasp;
    /// <summary>How quick they are, as a share of their world's rate.</summary>
    public float Pace;

    /// <summary>The voice a body has. The same person always sounds the same.</summary>
    public static Voice For(Figure f, Dialect dialect, ulong seed)
    {
        var rng = Rng.For(seed, "voice");
        float baseHz = f.Stage == LifeStage.Child ? 270f : f.Sex == Sex.Male ? 112f : 205f;
        float pitch = baseHz * MathF.Pow(1.72f / f.Height, 0.7f) * rng.Range(0.86f, 1.16f);
        pitch *= MathF.Pow(70f / MathF.Max(25f, f.Mass), 0.12f);
        if (f.Stage == LifeStage.Elder) pitch *= f.Sex == Sex.Male ? 1.1f : 0.9f;
        return new Voice
        {
            Seed = seed,
            Dialect = dialect,
            Pitch = pitch,
            Throat = (1.75f / f.Height) * (f.Sex == Sex.Female ? 1.12f : 1f) * (f.Stage == LifeStage.Child ? 1.1f : 1f) * rng.Range(0.95f, 1.05f),
            Breath = Math.Clamp(rng.Range(0.05f, 0.3f) + (f.Stage == LifeStage.Elder ? 0.25f : 0f), 0f, 1f),
            Rasp = Math.Clamp(rng.Range(0f, 0.3f) + (f.Stage == LifeStage.Elder ? 0.3f : 0f) + (f.Role == PersonRole.Drunk ? 0.3f : 0f) + (f.Role == PersonRole.Raider ? 0.25f : 0f), 0f, 1f),
            Pace = rng.Range(0.82f, 1.2f) * (f.Stage == LifeStage.Elder ? 0.8f : 1f) * (f.Role == PersonRole.Drunk ? 0.72f : 1f),
        };
    }

    /// <summary>A two-pole resonator: one formant.</summary>
    struct Formant
    {
        float _y1, _y2;

        public float Step(float x, float freq, float width)
        {
            float r = MathF.Exp(-MathF.PI * width / Rate);
            float a1 = 2f * r * MathF.Cos(MathF.Tau * freq / Rate), a2 = -r * r;
            float y = (1f - r) * x + a1 * _y1 + a2 * _y2;
            _y2 = _y1;
            _y1 = y;
            return y;
        }
    }

    /// <summary>
    /// A few syllables of nothing in this voice. <paramref name="take"/> picks which: the same take
    /// is the same sound. Pure and slow enough that a game should do it off the main thread.
    /// </summary>
    public short[] Render(VoiceMood mood, int take)
    {
        var rng = Rng.For(Seed, "phrase", (int)mood * 16 + take);
        (int lo, int hi, float speed, float lift, float width) = mood switch
        {
            VoiceMood.Shout => (2, 4, 0.75f, 1.65f, 1.3f),
            VoiceMood.Whisper => (3, 6, 1.0f, 1f, 1f),
            VoiceMood.Cry => (2, 4, 0.5f, 1.45f, 1.1f),
            _ => (4, 8, 1f, 0.92f, 0.85f),
        };
        int syllables = rng.Int(lo, hi);
        float rate = Dialect.Rate * Pace * speed;
        int total = (int)((syllables + 0.5f) / rate * Rate) + Rate / 8;
        var out_ = new short[total];
        Formant a = default, b = default, c = default;
        float phase = 0f, noiseLow = 0f, peak = 0.001f;
        var buffer = new float[total];
        int at = Rate / 40;
        (float F1, float F2) from = Dialect.Vowels[rng.Int(0, Dialect.Vowels.Length - 1)];
        float f0 = Pitch * lift;
        for (int s = 0; s < syllables; s++)
        {
            (float F1, float F2) to = Dialect.Vowels[rng.Int(0, Dialect.Vowels.Length - 1)];
            int length = (int)(Rate / rate * rng.Range(0.8f, 1.2f));
            // Consonant: a short stop, a hiss, or a run straight into the vowel.
            float kind = rng.Float();
            int burst = kind < 0.45f ? (kind < 0.45f * Dialect.Hiss ? Rate / 16 : Rate / 60) : 0;
            float hissy = kind < 0.45f * Dialect.Hiss ? 1f : 0.45f;
            float syllableLift = 1f + rng.Range(-0.08f, 0.08f) + (s == 0 && mood == VoiceMood.Shout ? 0.1f : 0f);
            for (int i = 0; i < length && at < total; i++, at++)
            {
                float t = i / (float)length;
                float whole = (s + t) / syllables;
                float glide = Math.Clamp(t * 3.2f, 0f, 1f);
                float f1 = (from.F1 + (to.F1 - from.F1) * glide) * Throat, f2 = (from.F2 + (to.F2 - from.F2) * glide) * Throat, f3 = 2500f * Throat;
                if (mood == VoiceMood.Shout) f1 *= 1.25f;
                float pitch = f0 * syllableLift * (1f + Dialect.Lilt * (whole - 0.4f));
                if (mood == VoiceMood.Cry) pitch *= 1f + 0.07f * MathF.Sin(whole * 44f) - 0.25f * whole;
                else if (mood == VoiceMood.Shout) pitch *= 1f - 0.18f * whole;
                pitch *= 1f + 0.006f * rng.Range(-1f, 1f);
                phase += pitch / Rate;
                if (phase >= 1f) phase -= 1f;
                float white = rng.Range(-1f, 1f);
                noiseLow += (white - noiseLow) * 0.3f;
                float glottal = phase < 0.45f ? MathF.Sin(phase / 0.45f * MathF.PI) : 0f;
                if (Rasp > 0f) glottal *= 1f - Rasp * 0.5f * (MathF.Sin(phase * MathF.PI) > 0.5f ? 1f : 0f) * (rng.Chance(0.5f) ? 1f : 0f);
                float source = mood == VoiceMood.Whisper ? white * 0.5f : glottal + white * Breath * 0.3f;
                float y = a.Step(source, f1, 80f * width) + 0.7f * b.Step(source, f2, 110f * width) + 0.3f * c.Step(source, f3, 170f * width);
                // Each syllable swells and dies; a shout holds, a murmur barely opens.
                float env = MathF.Sin(MathF.Min(1f, t * (mood == VoiceMood.Shout ? 5f : 2.2f)) * MathF.PI * 0.5f) * MathF.Min(1f, (1f - t) * 5f);
                float sample = y * env * (mood == VoiceMood.Shout ? 1.4f : 1f);
                if (mood == VoiceMood.Whisper) sample += (white - noiseLow) * 0.14f * env; // the air on the teeth
                if (i < burst) sample = sample * 0.2f + (hissy > 0.9f ? white - noiseLow : noiseLow * 2f) * 0.35f * hissy * (1f - i / (float)burst);
                buffer[at] = sample;
                peak = MathF.Max(peak, MathF.Abs(sample));
            }
            from = to;
            if (mood == VoiceMood.Cry && s < syllables - 1) at += Rate / 14; // a catch of breath
        }
        float gain = (mood is VoiceMood.Murmur or VoiceMood.Whisper ? 0.55f : 0.85f) / peak; // a murmur is held back, a shout is not
        for (int i = 0; i < total; i++)
        {
            float x = buffer[i] * gain;
            if (mood == VoiceMood.Shout) x = MathF.Tanh(x * 2.4f) * 0.9f;
            if (i > total - 300) x *= (total - i) / 300f;
            out_[i] = (short)(x * 30000f);
        }
        return out_;
    }
}
