using System;
using System.Collections.Generic;
using GalHack.Arms;
using Godot;

namespace Slice.Arms;

/// <summary>
/// Plays the guns' generated sounds (shared/Arms GunVoice): every weapon's report, action,
/// reload, impact, charge stage and held loop is synthesised from its parts once at load and kept
/// as a clip. A small pool of 3D players carries them, and one looping player follows the muzzle
/// while a flame is held. No files.
/// </summary>
public partial class GunSounds : Node3D
{
    readonly Dictionary<(string, VoiceKind), AudioStreamWav> _clips = new();
    readonly List<AudioStreamPlayer3D> _pool = new();
    AudioStreamPlayer3D _loop;
    int _next;
    float _loopLeft;

    /// <summary>Sounds played so far, by kind (tests).</summary>
    public readonly int[] Played = new int[Enum.GetValues<VoiceKind>().Length];
    public int Clips => _clips.Count;
    public bool Looping => _loop != null && _loop.Playing;

    public void Make(IEnumerable<WeaponDef> weapons)
    {
        foreach (WeaponDef d in weapons)
            foreach (VoiceKind k in Enum.GetValues<VoiceKind>())
            {
                if (k == VoiceKind.Hold && d.Trigger != Trigger.Hold && d.Swarm == null) continue;
                if (k == VoiceKind.Stage && d.Charge == null) continue;
                _clips[(d.Id, k)] = Wav(GunVoice.Make(d, k), k == VoiceKind.Hold);
            }
    }

    public override void _Ready()
    {
        for (int i = 0; i < 12; i++)
        {
            var p = new AudioStreamPlayer3D { UnitSize = 9f, MaxDistance = 160f, Bus = "Master", PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
            AddChild(p);
            _pool.Add(p);
        }
        _loop = new AudioStreamPlayer3D { UnitSize = 6f, MaxDistance = 60f, Bus = "Master", PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        AddChild(_loop);
    }

    public bool Has(WeaponDef d, VoiceKind k) => _clips.ContainsKey((d.Base.Id, k));

    /// <summary>One of a weapon's sounds at a point; pitch nudges it (charge stages climb).</summary>
    public void Play(WeaponDef d, VoiceKind kind, Vector3 at, float pitch = 1f, float db = 0f)
    {
        if (!_clips.TryGetValue((d.Base.Id, kind), out AudioStreamWav clip) || _pool.Count == 0) return;
        Played[(int)kind]++;
        AudioStreamPlayer3D p = _pool[_next];
        _next = (_next + 1) % _pool.Count;
        p.Stream = clip;
        p.PitchScale = pitch;
        p.VolumeDb = db + (kind == VoiceKind.Report ? 0f : -4f);
        p.GlobalPosition = at;
        p.Play();
    }

    /// <summary>Keeps a weapon's held loop going at a point for a little longer (call each tick it fires).</summary>
    public void Hold(WeaponDef d, Vector3 at)
    {
        if (!_clips.TryGetValue((d.Base.Id, VoiceKind.Hold), out AudioStreamWav clip)) return;
        _loopLeft = 0.15f;
        _loop.GlobalPosition = at;
        if (_loop.Stream != clip) _loop.Stream = clip;
        if (!_loop.Playing)
        {
            Played[(int)VoiceKind.Hold]++;
            _loop.Play();
        }
    }

    public override void _Process(double delta)
    {
        if (_loopLeft > 0f && (_loopLeft -= (float)delta) <= 0f) _loop.Stop();
    }

    static AudioStreamWav Wav(float[] samples, bool loop)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Math.Clamp(samples[i] * 32767f, -32768f, 32767f);
            data[i * 2] = (byte)(v & 0xff);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }
        var w = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = GunVoice.Rate, Stereo = false, Data = data };
        if (loop)
        {
            w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            w.LoopBegin = 0;
            w.LoopEnd = samples.Length;
        }
        return w;
    }
}
