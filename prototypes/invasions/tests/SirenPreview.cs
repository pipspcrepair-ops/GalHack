using System;
using System.IO;
using Godot;
using Invasions.Audio;

namespace Invasions.Tests;

/// <summary>
/// Renders 20 s of the synthesised siren to audio-preview/siren-preview.wav and quits:
/// the alert wail from a standing start, then the steady all-clear and the rotor running down.
///   godot --headless --path prototypes/invasions res://tests/SirenPreview.tscn
/// The samples come from the same <see cref="SirenSynth"/> the game streams.
/// </summary>
public partial class SirenPreview : Node
{
    const int Rate = 44100;
    const double Length = 20, AlertUntil = 10.4, AllClearHold = 5.6;

    public override void _Ready()
    {
        string path = ProjectSettings.GlobalizePath("res://audio-preview/siren-preview.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var synth = new SirenSynth(Rate);
        int total = (int)(Length * Rate), block = 512;
        var left = new float[total];
        var right = new float[total];
        synth.Alert();
        bool cleared = false;
        for (int i = 0; i < total; i += block)
        {
            if (!cleared && i >= AlertUntil * Rate)
            {
                synth.AllClear(AllClearHold);
                cleared = true;
            }
            int n = Math.Min(block, total - i);
            synth.Render(left.AsSpan(i, n), right.AsSpan(i, n));
        }

        // Normalise to -1 dBFS so the preview is loud enough to judge, and fade the last half second.
        float peak = 1e-6f;
        for (int i = 0; i < total; i++) peak = Math.Max(peak, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
        float gain = 0.89f / peak;
        int fade = Rate / 2;
        using (var w = new BinaryWriter(File.Create(path)))
        {
            WriteHeader(w, total);
            for (int i = 0; i < total; i++)
            {
                float f = i > total - fade ? (total - i) / (float)fade : 1f;
                w.Write((short)Math.Clamp(left[i] * gain * f * 32767f, -32768f, 32767f));
                w.Write((short)Math.Clamp(right[i] * gain * f * 32767f, -32768f, 32767f));
            }
        }
        GD.Print($"wrote {path} ({Length} s, peak {peak:F3}, gain {gain:F2})");
        GetTree().Quit(0);
    }

    static void WriteHeader(BinaryWriter w, int frames)
    {
        const short channels = 2, bits = 16;
        int dataBytes = frames * channels * bits / 8;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write(channels);
        w.Write(Rate);
        w.Write(Rate * channels * bits / 8);
        w.Write((short)(channels * bits / 8));
        w.Write(bits);
        w.Write("data"u8);
        w.Write(dataBytes);
    }
}
