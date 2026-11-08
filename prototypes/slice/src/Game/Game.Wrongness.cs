using System;
using Godot;
using Slice.Surface;
using Wildlife;

namespace Slice.Game;

/// <summary>
/// The Husks' wrongness as you feel it (wildlife's <see cref="Wildlife.Wrongness"/>): faint near a
/// few of them, strong in a deep den. It builds slowly as you come near and fades as you go; past
/// a point several things go wrong together, with a sound (a low pressure swelling in the ears),
/// and then grow with it: the compass drifts off true, the light that casts the shadows turns a
/// little off the sun's way (the sun stays where it is in the sky), and sound dies away. Animals
/// keep off them in the sim. Nothing names it; the most the visor does is garble a digit.
/// </summary>
public partial class Game
{
    /// <summary>How strongly it is felt here now (eased), 0..1; and how much of it shows, 0 below the onset.</summary>
    public float Wrongness { get; private set; }
    public float WrongShown { get; private set; }
    /// <summary>How far the compass has drifted off true, degrees (the visor reads it).</summary>
    public float CompassDrift { get; private set; }
    /// <summary>Times it came on, the cue with it (tests).</summary>
    public int WrongOnsets { get; private set; }

    /// <summary>It comes on past this, and goes off below that (so its edge never flickers).</summary>
    const float WrongOnset = 0.16f, WrongOff = 0.08f;
    /// <summary>At full: how far the compass wanders, and the shadows turn, degrees; how far sound falls, dB.</summary>
    const float MostDrift = 40f, MostSkew = 9f, MostHush = 15f;

    bool _wrongOn;
    float _wrongIn, _wrongRaw, _wrongClock;
    AudioStreamPlayer _wrongCue;

    void StepWrongness(float dt)
    {
        WildlifePatch patch = Zone?.Wildlife;
        if (patch?.Sim == null || Survivor == null) return;
        _wrongClock += dt;
        if ((_wrongIn -= dt) <= 0f)
        {
            _wrongIn = 0.2f;
            float raw = patch.Player != null ? Wildlife.Wrongness.At(patch.Sim, patch.Player.Position) : 0f;
            patch.Sim.WrongPlaces.Clear();
            foreach (DeepSite s in patch.Sites)
            {
                if (s.Player != null) raw = MathF.Max(raw, Wildlife.Wrongness.At(s.Sim, s.Player.Position));
                // A den's Husks, for the open country's animals to keep off too.
                foreach (Creature c in s.Sim.Creatures)
                    if (c.Species.Husk && !c.Dead && !c.Removed) patch.Sim.WrongPlaces.Add(patch.PatchLocal(s.World(c.Position)));
            }
            if (InOrbit) raw = 0f;
            _wrongRaw = raw;
        }
        // It builds as you come near (it takes a few seconds), and fades a little faster as you go.
        Wrongness = Mathf.MoveToward(Wrongness, _wrongRaw, dt * (_wrongRaw > Wrongness ? 0.22f : 0.35f));
        if (!_wrongOn && Wrongness >= WrongOnset)
        {
            _wrongOn = true;
            WrongOnsets++;
            Cue();
        }
        else if (_wrongOn && Wrongness < WrongOff) _wrongOn = false;
        WrongShown = _wrongOn ? Mathf.SmoothStep(WrongOff, 0.9f, Wrongness) : 0f;
        float t = _wrongClock;
        CompassDrift = WrongShown * MostDrift * (MathF.Sin(t * 0.31f) + 0.6f * MathF.Sin(t * 0.83f + 1.7f)) / 1.6f;
        Vector3 axis = new Vector3(MathF.Sin(t * 0.05f), 0.25f, MathF.Cos(t * 0.05f)).Normalized();
        Zone.Sky.ShadowSkew = WrongShown > 0.002f ? new Basis(axis, Mathf.DegToRad(MostSkew) * WrongShown) : Basis.Identity;
        Audio.Hush = WrongShown * MostHush;
    }

    /// <summary>The cue: a low pressure swelling and ebbing in the ears, two tones beating against each other, made here.</summary>
    void Cue()
    {
        if (_wrongCue == null)
        {
            _wrongCue = new AudioStreamPlayer { Name = "Wrong", Stream = Pressure(), VolumeDb = -9f };
            AddChild(_wrongCue);
        }
        _wrongCue.Play();
    }

    static AudioStreamWav Pressure()
    {
        const int rate = 22050;
        const float seconds = 3.2f;
        int n = (int)(seconds * rate);
        var data = new byte[n * 2];
        var rng = new Random(29);
        float hiss = 0f;
        for (int i = 0; i < n; i++)
        {
            float time = (float)i / rate, k = time / seconds;
            // In slowly, out slowly: a swell, not a sting.
            float env = MathF.Sin(MathF.PI * MathF.Pow(k, 0.6f));
            env *= env;
            hiss += 0.02f * ((float)rng.NextDouble() * 2f - 1f - hiss);
            float s = 0.5f * MathF.Sin(MathF.Tau * 49f * time) + 0.42f * MathF.Sin(MathF.Tau * 52.5f * time + 0.6f)
                      + 0.06f * MathF.Sin(MathF.Tau * 1870f * time) * (0.5f + 0.5f * MathF.Sin(MathF.Tau * 0.7f * time)) + hiss * 1.4f;
            short v = (short)(Math.Clamp(s * env * 0.8f, -1f, 1f) * 32767);
            data[2 * i] = (byte)v;
            data[2 * i + 1] = (byte)(v >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = data };
    }
}
