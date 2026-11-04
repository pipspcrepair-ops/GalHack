using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Every sound in the prototype is synthesised here at start-up: no audio files, no licences.
/// The important ones are the attack telegraphs: a growl that builds through the wind-up
/// (hiss for crawlers, buzz for swarms, a long roar for giants), so you hear a lunge coming
/// before you see it land.
/// </summary>
public partial class ProceduralAudio : Node3D
{
    const int Rate = 22050;
    readonly Dictionary<string, AudioStreamWav> _clips = new();
    readonly List<AudioStreamPlayer3D> _pool = new();
    AudioStreamPlayer _ui;
    int _next;

    public AudioStreamWav HoverLoop { get; private set; }
    public AudioStreamWav GrindLoop { get; private set; }

    public override void _Ready()
    {
        var rng = new Rng(77);
        _clips["growl"] = Make(1.3f, (t, n) => Envelope(t, 1.3f, 0.9f) * (Saw(t * (70f + 25f * t)) * 0.6f + n * 0.45f) * (0.6f + 0.4f * MathF.Sin(t * 31f)), rng);
        _clips["roar"] = Make(2.0f, (t, n) => Envelope(t, 2.0f, 1.4f) * (Saw(t * (42f + 12f * MathF.Sin(t * 3f))) * 0.6f + n * 0.6f), rng);
        _clips["hiss"] = Make(1.2f, (t, n) => Envelope(t, 1.2f, 0.9f) * n * (0.7f + 0.3f * MathF.Sin(t * 60f)), rng, highpass: true);
        _clips["buzz"] = Make(1.2f, (t, n) => Envelope(t, 1.2f, 0.95f) * (Square(t * (190f + 60f * t)) * 0.35f + n * 0.15f) * (0.5f + 0.5f * MathF.Sin(t * 45f)), rng);
        _clips["chirp"] = Make(1.0f, (t, n) => Envelope(t, 1.0f, 0.8f) * MathF.Sin(MathF.Tau * (380f + 300f * MathF.Sin(t * 14f)) * t) * 0.5f, rng);
        _clips["snap"] = Make(0.18f, (t, n) => MathF.Exp(-t * 30f) * (n * 0.8f + MathF.Sin(MathF.Tau * 110f * t) * 0.5f), rng);
        _clips["shot"] = Make(0.35f, (t, n) => MathF.Exp(-t * 18f) * (n * 0.7f + MathF.Sin(MathF.Tau * (90f - 120f * t) * t) * 0.6f), rng);
        _clips["thud"] = Make(0.3f, (t, n) => MathF.Exp(-t * 14f) * (MathF.Sin(MathF.Tau * 55f * t) * 0.8f + n * 0.2f), rng);
        _clips["chop"] = Make(0.22f, (t, n) => MathF.Exp(-t * 20f) * (n * 0.9f + Square(t * 70f) * 0.3f), rng);
        _clips["discover"] = Make(0.9f, (t, n) => MathF.Exp(-t * 3f) * (MathF.Sin(MathF.Tau * 523f * t) + MathF.Sin(MathF.Tau * (t < 0.25f ? 659f : 784f) * t)) * 0.25f, rng);
        HoverLoop = Make(1.0f, (t, n) => (MathF.Sin(MathF.Tau * 55f * t) * 0.5f + MathF.Sin(MathF.Tau * 110f * t) * 0.25f + n * 0.08f) * 0.6f, rng, loop: true);
        GrindLoop = Make(0.5f, (t, n) => (Square(t * 36f) * 0.3f + n * 0.6f) * (0.7f + 0.3f * MathF.Sin(t * MathF.Tau * 12f)), rng, loop: true);

        for (int i = 0; i < 12; i++)
        {
            var p = new AudioStreamPlayer3D { UnitSize = 8f, MaxDistance = 90f, Bus = "Master" };
            AddChild(p);
            _pool.Add(p);
        }
        _ui = new AudioStreamPlayer();
        AddChild(_ui);
    }

    /// <summary>The wind-up sound for a creature, pitched by size.</summary>
    public void Telegraph(Creature c)
    {
        string clip = c.Species.Rig switch
        {
            RigKind.Chain => "hiss",
            RigKind.Swarm => "buzz",
            _ when c.Species.Plan == BodyPlan.Giant => "roar",
            _ when c.Species.Body.Size < 0.9f => "chirp",
            _ => "growl",
        };
        float pitch = Math.Clamp(1.4f / MathF.Sqrt(MathF.Max(0.3f, c.Species.Body.Size)), 0.55f, 1.8f);
        Play(clip, c.Position + Vector3.Up, pitch, c.Species.Plan == BodyPlan.Giant ? 6f : 2f);
    }

    public void Play(string clip, Vector3 at, float pitch = 1f, float volumeDb = 0f)
    {
        if (!_clips.TryGetValue(clip, out var stream) || _pool.Count == 0) return;
        AudioStreamPlayer3D p = _pool[_next++ % _pool.Count];
        p.Stream = stream;
        p.PitchScale = pitch;
        p.VolumeDb = volumeDb;
        p.GlobalPosition = at;
        p.Play();
    }

    public void PlayUi(string clip, float volumeDb = -6f)
    {
        if (!_clips.TryGetValue(clip, out var stream)) return;
        _ui.Stream = stream;
        _ui.VolumeDb = volumeDb;
        _ui.Play();
    }

    static AudioStreamWav Make(float seconds, Func<float, float, float> wave, Rng rng, bool highpass = false, bool loop = false)
    {
        int n = (int)(seconds * Rate);
        var data = new byte[n * 2];
        float prevNoise = 0f, lowNoise = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float white = rng.Range(-1f, 1f);
            lowNoise += (white - lowNoise) * 0.18f; // a softer, rumbling noise unless asked for hiss
            float noise = highpass ? white - prevNoise * 0.6f : lowNoise * 2.2f;
            prevNoise = white;
            float s = Math.Clamp(wave(t, noise), -1f, 1f);
            if (loop)
            {
                float edge = MathF.Min(t, seconds - t);
                s *= Math.Clamp(edge * 200f, 0f, 1f); // no click at the loop seam
            }
            short v = (short)(s * 30000f);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = data,
        };
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = n;
        }
        return stream;
    }

    /// <summary>Swells over the wind-up, then cuts off sharply: the lunge lands at the end.</summary>
    static float Envelope(float t, float length, float peakAt)
    {
        if (t < peakAt) return 0.15f + 0.85f * (t / peakAt) * (t / peakAt);
        return MathF.Max(0f, 1f - (t - peakAt) / MathF.Max(0.01f, length - peakAt));
    }

    static float Saw(float phase) => 2f * (phase - MathF.Floor(phase + 0.5f));
    static float Square(float phase) => phase - MathF.Floor(phase) < 0.5f ? 1f : -1f;
}
