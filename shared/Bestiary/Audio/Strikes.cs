using System;
using Godot;

namespace Wildlife;

/// <summary>
/// The sounds of a heavy body's strikes, made here like every other sound: a stomp's deep thud,
/// felt as much as heard and carried well beyond the fight, and a tail's whoosh as it sweeps.
/// </summary>
public partial class ProceduralAudio
{
    AudioStreamPlayer3D _quake;
    AudioStreamWav _stomp, _whoosh;

    /// <summary>Stomps played (tests).</summary>
    public int Stomps;

    void MakeStrikes(Rng rng)
    {
        // A deep thud: a falling sub-bass thump under a short crunch of grit, ringing out low.
        _stomp = Make(1.4f, (t, n) => MathF.Exp(-t * 3.2f) * MathF.Sin(MathF.Tau * (44f - 14f * t) * t) * 0.95f
            + MathF.Exp(-t * 22f) * n * 0.5f + MathF.Exp(-t * 6f) * MathF.Sin(MathF.Tau * 23f * t) * 0.5f, rng);
        // A whoosh: air rushing past, swelling and falling away.
        _whoosh = Make(0.5f, (t, n) => MathF.Sin(MathF.PI * Math.Clamp(t / 0.5f, 0f, 1f)) * n * 0.6f, rng, highpass: true);
        _quake = new AudioStreamPlayer3D { UnitSize = 40f, MaxDistance = 600f, Bus = "Master" };
        AddChild(_quake);
    }

    /// <summary>A foot comes down (or a heavy charge pounds): deeper and louder the heavier the body.</summary>
    public void Quake(QuakeEvent e)
    {
        if (_quake == null) return;
        Species s = e.Creature.Species;
        if (!e.Stomp)
        {
            Play("thud", e.Position, Heft.Pitch(s) * 0.7f, Heft.Loudness(s) - 2f + 6f * e.Strength);
            return;
        }
        Stomps++;
        _quake.Stream = _stomp;
        _quake.PitchScale = Math.Clamp(Heft.Pitch(s) * 0.8f, 0.45f, 1.1f);
        _quake.VolumeDb = 4f + 8f * e.Strength;
        _quake.GlobalPosition = e.Position + Origin;
        _quake.Play();
    }

    /// <summary>A tail sweeping through the air.</summary>
    public void Whoosh(Creature c, Vector3 at)
    {
        if (_whoosh == null) return;
        AudioStreamPlayer3D p = _pool[_next++ % _pool.Count];
        p.Stream = _whoosh;
        p.PitchScale = Math.Clamp(Heft.Pitch(c.Species), 0.5f, 1.4f);
        p.VolumeDb = Heft.Loudness(c.Species);
        p.GlobalPosition = at + Origin;
        p.Play();
    }
}
