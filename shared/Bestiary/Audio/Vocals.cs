using System;

namespace Wildlife;

/// <summary>How one person feels right now. A settlement has a mood towards you; each person has their own.</summary>
public enum PersonMood { Calm, Uneasy, Scared, Angry, Grieving, Drunk, Content }

/// <summary>The sounds a person makes that are not speech: a grunt, a breath, a cry of pain, effort, a call, a laugh, a cry.</summary>
public enum VocalKind { Grunt, Breath, Pain, Effort, Call, Laugh, Cry }

/// <summary>
/// The non-speech sounds of a person's voice: the same synthesiser as the wordless speech (a glottal
/// pulse through three formants, breath and rasp), so each person is still themselves. Their
/// age and sex are already in the voice (pitch, throat length, breath, rasp, tremor); the mood bends
/// it on top: fear lifts and quickens it and shakes it, anger drops it and roughens it and makes it
/// loud, grief slows it and fills it with breath, drink slurs and wobbles it. No words, no samples.
/// </summary>
public sealed partial class Voice
{
    /// <summary>How a mood bends a voice: pitch and tempo scale, breath and rasp added, tremble and wobble depth, loudness 0 to 1, formant lift.</summary>
    public readonly record struct Colour(float Pitch, float Tempo, float Breath, float Rasp, float Tremble, float Wobble, float Gain, float Tense);

    public static Colour ColourOf(PersonMood m) => m switch
    {
        PersonMood.Content => new(1.06f, 1.05f, 0.05f, 0f, 0f, 0f, 0.55f, 1f),
        PersonMood.Uneasy => new(1.08f, 1.1f, 0.1f, 0f, 0.05f, 0f, 0.45f, 1.02f),
        PersonMood.Scared => new(1.3f, 1.35f, 0.15f, 0f, 0.18f, 0f, 0.7f, 1.08f),
        PersonMood.Angry => new(0.9f, 0.95f, 0f, 0.25f, 0f, 0f, 0.95f, 1.15f),
        PersonMood.Grieving => new(0.92f, 0.8f, 0.2f, 0f, 0.12f, 0f, 0.5f, 0.97f),
        PersonMood.Drunk => new(0.94f, 0.7f, 0.1f, 0.1f, 0f, 0.09f, 0.55f, 0.98f),
        _ => new(1f, 1f, 0f, 0f, 0f, 0f, 0.5f, 1f),
    };

    /// <summary>
    /// One non-speech sound in this voice and this mood; <paramref name="take"/> picks which of its variants.
    /// Pure and quick, but still best rendered off the main thread.
    /// </summary>
    public short[] Vocalize(VocalKind kind, PersonMood mood, int take)
    {
        var rng = Rng.For(Seed, "vocal", ((int)kind * 8 + (int)mood) * 16 + take);
        Colour c = ColourOf(mood);
        float f0 = Pitch * c.Pitch;
        float breath = Math.Clamp(Breath + c.Breath, 0f, 1f), rasp = Math.Clamp(Rasp + c.Rasp, 0f, 1f);
        float tremble = c.Tremble + Tremor, wobble = c.Wobble;
        float tempo = c.Tempo * Pace;
        var parts = new System.Collections.Generic.List<float[]>();

        float[] V(float seconds, Func<float, float> contour, (float F1, float F2) vowel, (float F1, float F2)? to, Func<float, float> env, float extraBreath = 0f, float extraRasp = 0f, float whisper = 0f)
        {
            int n = Math.Max(8, (int)(seconds * Rate));
            var o = new float[n];
            Formant a = default, b = default, d = default;
            float phase = 0f, low = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                var (v1, v2) = to is { } w ? (vowel.F1 + (w.F1 - vowel.F1) * t, vowel.F2 + (w.F2 - vowel.F2) * t) : vowel;
                float hz = f0 * contour(t) * (1f + tremble * MathF.Sin(MathF.Tau * 6.5f * i / Rate) * 0.5f + wobble * MathF.Sin(MathF.Tau * 1.6f * i / Rate)) * (1f + 0.01f * rng.Range(-1f, 1f));
                phase += hz / Rate;
                if (phase >= 1f) phase -= 1f;
                float white = rng.Range(-1f, 1f);
                low += (white - low) * 0.3f;
                float glottal = phase < 0.45f ? MathF.Sin(phase / 0.45f * MathF.PI) : 0f;
                float r = Math.Clamp(rasp + extraRasp, 0f, 1f);
                if (r > 0f && rng.Chance(0.5f)) glottal *= 1f - r * 0.5f;
                float src = whisper > 0f ? white * whisper + glottal * (1f - whisper) : glottal + white * Math.Clamp(breath + extraBreath, 0f, 1f) * 0.35f;
                float tense = c.Tense * Throat;
                float y = a.Step(src, v1 * tense, 80f) + 0.7f * b.Step(src, v2 * tense, 110f) + 0.3f * d.Step(src, 2500f * tense, 170f);
                o[i] = y * env(t);
            }
            return o;
        }
        float Attack(float t, float rise, float fall) => MathF.Min(1f, t / rise) * MathF.Min(1f, (1f - t) / fall);
        float[] Silence(float seconds) => new float[(int)(seconds * Rate)];

        switch (kind)
        {
            case VocalKind.Grunt:
                parts.Add(V(rng.Range(0.16f, 0.26f) / tempo, t => 1f - 0.3f * t, (550f, 1000f), null, t => Attack(t, 0.08f, 0.5f), 0.1f, 0.2f));
                break;
            case VocalKind.Effort:
                parts.Add(V(rng.Range(0.3f, 0.42f) / tempo, t => 0.85f + 0.2f * MathF.Sin(t * MathF.PI), (600f, 1100f), (500f, 900f), t => Attack(t, 0.15f, 0.35f), 0.35f, 0.3f));
                break;
            case VocalKind.Pain:
                parts.Add(V(rng.Range(0.35f, 0.55f) / tempo, t => t < 0.15f ? 1.1f + 2f * t : 1.4f - 0.55f * (t - 0.15f), (730f, 1090f), (660f, 1000f), t => Attack(t, 0.04f, 0.4f), 0.1f, 0.2f));
                break;
            case VocalKind.Call:
            {
                parts.Add(V(0.22f / tempo, t => 1f + 0.35f * t, (660f, 1720f), null, t => Attack(t, 0.1f, 0.2f)));
                parts.Add(V(rng.Range(0.4f, 0.65f) / tempo, t => 1.3f - 0.2f * t, (730f, 1090f), (570f, 840f), t => Attack(t, 0.05f, 0.3f)));
                break;
            }
            case VocalKind.Laugh:
            {
                int ha = rng.Int(3, 6);
                for (int i = 0; i < ha; i++)
                {
                    float k = i / (float)ha;
                    parts.Add(V(0.1f / tempo, t => 1.15f - 0.3f * k - 0.1f * t, (730f, 1090f), null, t => Attack(t, 0.15f, 0.5f) * (1f - 0.5f * k), 0.3f));
                    parts.Add(Silence(0.075f / tempo));
                }
                break;
            }
            case VocalKind.Cry:
            {
                int wails = rng.Int(2, 4);
                for (int i = 0; i < wails; i++)
                {
                    parts.Add(V(rng.Range(0.45f, 0.65f) / tempo, t => 1.15f + 0.35f * MathF.Sin(t * MathF.PI) - 0.2f * t + 0.05f * MathF.Sin(t * 40f), (700f, 1100f), (450f, 1500f), t => Attack(t, 0.12f, 0.3f), 0.2f));
                    parts.Add(V(0.14f / tempo, t => 1f, (500f, 1000f), null, t => 0.5f * Attack(t, 0.3f, 0.4f), 0f, 0f, 0.9f)); // the catch of breath
                }
                break;
            }
            default: // Breath: air through the throat, in then out; quick and shallow when frightened, wheezing when old
            {
                int pairs = mood is PersonMood.Scared or PersonMood.Uneasy ? 2 : 1;
                for (int i = 0; i < pairs; i++)
                {
                    parts.Add(V(0.3f / tempo, t => 1f, (600f, 1200f), null, t => Attack(t, 0.6f, 0.3f) * 0.8f, 0f, 0f, 0.95f));
                    parts.Add(V(0.4f / tempo, t => 1f, (500f, 1000f), null, t => Attack(t, 0.25f, 0.7f), 0f, 0f, 0.95f));
                }
                break;
            }
        }

        int total = 0;
        foreach (var p in parts) total += p.Length;
        total += Rate / 20;
        var buf = new float[total];
        int at = Rate / 40;
        float peak = 0.001f;
        foreach (var p in parts)
            for (int i = 0; i < p.Length && at < total; i++, at++) { buf[at] = p[i]; peak = MathF.Max(peak, MathF.Abs(p[i])); }
        var pcm = new short[total];
        float gain = c.Gain / peak;
        for (int i = 0; i < total; i++)
        {
            float x = buf[i] * gain;
            if (mood == PersonMood.Angry) x = MathF.Tanh(x * 1.6f) * 0.95f;
            if (i > total - 300) x *= (total - i) / 300f;
            pcm[i] = (short)(Math.Clamp(x, -0.99f, 0.99f) * 30000f);
        }
        return pcm;
    }
}
