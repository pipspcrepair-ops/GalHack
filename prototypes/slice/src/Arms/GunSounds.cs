using System;
using System.Collections.Generic;
using GalHack.Arms;
using Godot;

namespace Slice.Arms;

/// <summary>
/// Plays the guns' generated sounds (shared/Arms GunVoice): every weapon's report, action,
/// reload, impact, charge stage and held loop is synthesised from its parts once at load and kept
/// as a clip, and so are the fight's own sounds (<see cref="CombatSound"/>). Gunshots have their
/// own pool of 3D players that carries them far and loud; everything else a nearer one. The
/// player's own hits are confirmed on a flat player, so they're heard at any distance. One looping
/// player follows the muzzle while a flame is held. No files.
/// </summary>
public partial class GunSounds : Node3D
{
    readonly Dictionary<(string, VoiceKind), AudioStreamWav> _clips = new();
    readonly AudioStreamWav[] _shared = new AudioStreamWav[Enum.GetValues<CombatSound>().Length];
    readonly List<AudioStreamPlayer3D> _pool = new(), _loud = new();
    AudioStreamPlayer3D _loop;
    AudioStreamPlayer _confirm;
    int _next, _nextLoud;
    float _loopLeft;

    /// <summary>How a gunshot carries: full strength out to this many metres, heard out to the last, and how much over the rest.</summary>
    public const float LoudUnit = 30f, LoudReach = 900f, LoudDb = 4f;
    const float Unit = 9f, Reach = 160f, Ceiling = 6f;

    /// <summary>Sounds played so far, by kind (tests).</summary>
    public readonly int[] Played = new int[Enum.GetValues<VoiceKind>().Length];
    public readonly int[] PlayedShared = new int[Enum.GetValues<CombatSound>().Length];
    public int Clips => _clips.Count;
    public bool Looping => _loop != null && _loop.Playing;
    /// <summary>The player that carried the last gunshot (tests: is it playing, from where).</summary>
    public AudioStreamPlayer3D LastLoud { get; private set; }
    public AudioStreamPlayer ConfirmPlayer => _confirm;

    public void Make(IEnumerable<WeaponDef> weapons)
    {
        foreach (WeaponDef d in weapons)
            foreach (VoiceKind k in Enum.GetValues<VoiceKind>())
            {
                if (k == VoiceKind.Hold && d.Trigger != Trigger.Hold && d.Swarm == null) continue;
                if (k == VoiceKind.Stage && d.Charge == null) continue;
                _clips[(d.Id, k)] = Wav(GunVoice.Make(d, k), k == VoiceKind.Hold);
            }
        foreach (CombatSound k in Enum.GetValues<CombatSound>()) _shared[(int)k] = Wav(GunVoice.Make(k), false);
    }

    public override void _Ready()
    {
        for (int i = 0; i < 12; i++) _pool.Add(Player(Unit, Reach));
        for (int i = 0; i < 8; i++) _loud.Add(Player(LoudUnit, LoudReach));
        _loop = Player(6f, 60f);
        _confirm = new AudioStreamPlayer { Name = "HitConfirm", Bus = "Master", VolumeDb = -2f };
        AddChild(_confirm);
    }

    AudioStreamPlayer3D Player(float unit, float reach)
    {
        var p = new AudioStreamPlayer3D { UnitSize = unit, MaxDistance = reach, MaxDb = Ceiling, Bus = "Master", PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        AddChild(p);
        return p;
    }

    public bool Has(WeaponDef d, VoiceKind k) => _clips.ContainsKey((d.Base.Id, k));

    /// <summary>How loud a weapon's report is heard so far away, dB: the engine's inverse-distance falloff from the unit size, capped, and silent past the reach.</summary>
    public static float ReportDbAt(WeaponDef d, float metres)
    {
        bool loud = GunVoice.Gunshot(d);
        float unit = loud ? LoudUnit : Unit, reach = loud ? LoudReach : Reach;
        if (metres > reach) return float.NegativeInfinity;
        return MathF.Min(Ceiling, (loud ? LoudDb : 0f) + 20f * MathF.Log10(unit / MathF.Max(0.1f, metres)));
    }

    /// <summary>One of a weapon's sounds at a point; pitch nudges it (charge stages climb). A gunshot is loud and carries.</summary>
    public void Play(WeaponDef d, VoiceKind kind, Vector3 at, float pitch = 1f, float db = 0f)
    {
        if (!_clips.TryGetValue((d.Base.Id, kind), out AudioStreamWav clip) || _pool.Count == 0) return;
        Played[(int)kind]++;
        bool loud = kind == VoiceKind.Report && GunVoice.Gunshot(d);
        AudioStreamPlayer3D p = loud ? _loud[_nextLoud++ % _loud.Count] : _pool[_next++ % _pool.Count];
        if (loud) LastLoud = p;
        Start(p, clip, at, pitch, db + (loud ? LoudDb : kind == VoiceKind.Report ? 0f : -4f));
    }

    /// <summary>One of the fight's own sounds at a point.</summary>
    public void Play(CombatSound kind, Vector3 at, float pitch = 1f, float db = 0f)
    {
        if (_pool.Count == 0) return;
        PlayedShared[(int)kind]++;
        Start(_pool[_next++ % _pool.Count], _shared[(int)kind], at, pitch, db);
    }

    /// <summary>The player's hit landed (or killed): a thud heard the same at any distance.</summary>
    public void Confirm(bool kill)
    {
        if (_confirm == null) return;
        CombatSound k = kill ? CombatSound.Kill : CombatSound.Confirm;
        PlayedShared[(int)k]++;
        _confirm.Stream = _shared[(int)k];
        _confirm.Play();
    }

    static void Start(AudioStreamPlayer3D p, AudioStreamWav clip, Vector3 at, float pitch, float db)
    {
        p.Stream = clip;
        p.PitchScale = pitch;
        p.VolumeDb = db;
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
