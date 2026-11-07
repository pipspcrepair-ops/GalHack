using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Every sound in the prototype is synthesised here at start-up: no audio files, no licences.
/// The important ones are the attack telegraphs: each behaviour's signature attack has its
/// own wind-up sound (a predator's growl, a pack's snarl, a herd's bellow, a giant's roar, an
/// ambusher's hiss, a scavenger's chitter, a swarm's buzz) that builds through the wind-up,
/// so you hear a lunge coming, and what kind, before you see it land.
/// </summary>
public partial class ProceduralAudio : Node3D
{
    const int Rate = 22050;
    readonly Dictionary<string, AudioStreamWav> _clips = new();
    readonly List<AudioStreamPlayer3D> _pool = new();
    int _next;

    /// <summary>Where the sim's origin is in the world, for a sim that lives on a patch of a bigger one (zero here).</summary>
    public Vector3 Origin;

    public AudioStreamWav HoverLoop { get; private set; }
    public AudioStreamWav GrindLoop { get; private set; }

    static AudioStreamWav _hum;

    /// <summary>A hoverer's wings: a soft, breathy hum at the beat and its octave, on one shared loop.</summary>
    public static AudioStreamWav Hum => _hum ??= Make(1f, (t, n) =>
        (Saw(t * 54f) * 0.2f + MathF.Sin(MathF.Tau * 108f * t) * 0.32f + MathF.Sin(MathF.Tau * 216f * t) * 0.1f + n * 0.14f) * (0.85f + 0.15f * MathF.Sin(MathF.Tau * 9f * t)), new Rng(53), loop: true);

    /// <summary>Lets the hum go at shutdown, on the main thread.</summary>
    public static void ReleaseHum()
    {
        _hum?.Dispose();
        _hum = null;
    }

    public override void _Ready()
    {
        var rng = new Rng(77);
        _clips["growl"] = Make(1.3f, (t, n) => Envelope(t, 1.3f, 0.9f) * (Saw(t * (70f + 25f * t)) * 0.6f + n * 0.45f) * (0.6f + 0.4f * MathF.Sin(t * 31f)), rng);
        _clips["roar"] = Make(2.0f, (t, n) => Envelope(t, 2.0f, 1.4f) * (Saw(t * (42f + 12f * MathF.Sin(t * 3f))) * 0.6f + n * 0.6f), rng);
        _clips["hiss"] = Make(1.2f, (t, n) => Envelope(t, 1.2f, 0.9f) * n * (0.7f + 0.3f * MathF.Sin(t * 60f)), rng, highpass: true);
        _clips["buzz"] = Make(1.2f, (t, n) => Envelope(t, 1.2f, 0.95f) * (Square(t * (190f + 60f * t)) * 0.35f + n * 0.15f) * (0.5f + 0.5f * MathF.Sin(t * 45f)), rng);
        _clips["snarl"] = Make(1.0f, (t, n) => Envelope(t, 1.0f, 0.75f) * (Saw(t * (120f + 50f * MathF.Sin(t * 23f))) * 0.45f + n * 0.55f) * (0.5f + 0.5f * MathF.Abs(MathF.Sin(t * 17f))), rng);
        _clips["bellow"] = Make(1.4f, (t, n) => Envelope(t, 1.4f, 1.0f) * (Saw(t * (58f - 8f * t)) * 0.7f + MathF.Sin(MathF.Tau * 116f * t) * 0.2f + n * 0.25f) * (0.8f + 0.2f * MathF.Sin(t * 9f)), rng);
        _clips["chitter"] = Make(1.0f, (t, n) => Envelope(t, 1.0f, 0.8f) * n * (MathF.Sin(t * 140f) > 0.3f ? 1f : 0.1f), rng, highpass: true);
        _clips["grunt"] = Make(0.6f, (t, n) => Envelope(t, 0.6f, 0.4f) * (Saw(t * 85f) * 0.5f + n * 0.3f), rng);
        _clips["chirp"] = Make(1.0f, (t, n) => Envelope(t, 1.0f, 0.8f) * MathF.Sin(MathF.Tau * (380f + 300f * MathF.Sin(t * 14f)) * t) * 0.5f, rng);
        _clips["snap"] = Make(0.18f, (t, n) => MathF.Exp(-t * 30f) * (n * 0.8f + MathF.Sin(MathF.Tau * 110f * t) * 0.5f), rng);
        _clips["shot"] = Make(0.35f, (t, n) => MathF.Exp(-t * 18f) * (n * 0.7f + MathF.Sin(MathF.Tau * (90f - 120f * t) * t) * 0.6f), rng);
        _clips["thud"] = Make(0.3f, (t, n) => MathF.Exp(-t * 14f) * (MathF.Sin(MathF.Tau * 55f * t) * 0.8f + n * 0.2f), rng);
        _clips["chop"] = Make(0.22f, (t, n) => MathF.Exp(-t * 20f) * (n * 0.9f + Square(t * 70f) * 0.3f), rng);
        // Wounds. A wet smack on a creature, a soft pop for a swarm critter, a blow taken in the suit.
        _clips["hit"] = Make(0.2f, (t, n) => MathF.Exp(-t * 26f) * (n * 0.65f + MathF.Sin(MathF.Tau * (150f - 300f * t) * t) * 0.6f), rng);
        _clips["pop"] = Make(0.12f, (t, n) => MathF.Exp(-t * 45f) * (MathF.Sin(MathF.Tau * (620f - 2600f * t) * t) * 0.7f + n * 0.25f), rng);
        _clips["hurt"] = Make(0.32f, (t, n) => MathF.Exp(-t * 13f) * (MathF.Sin(MathF.Tau * 62f * t) * 0.8f + n * 0.45f), rng);
        // The discovery chime: two soft glassy bell notes a fifth apart, rising, under a second
        // long. Nothing like any creature's wind-up, so it never reads as a threat.
        _clips["discover"] = Make(0.75f, (t, n) => 0.3f * (Bell(t, 659.3f) + 0.85f * Bell(t - 0.11f, 987.8f)) * Math.Clamp((0.75f - t) * 16f, 0f, 1f), rng);
        HoverLoop = Make(1.0f, (t, n) => (MathF.Sin(MathF.Tau * 55f * t) * 0.5f + MathF.Sin(MathF.Tau * 110f * t) * 0.25f + n * 0.08f) * 0.6f, rng, loop: true);
        GrindLoop = Make(0.5f, (t, n) => (Square(t * 36f) * 0.3f + n * 0.6f) * (0.7f + 0.3f * MathF.Sin(t * MathF.Tau * 12f)), rng, loop: true);
        // A throat bringing something up: a wet heave with a rough voice under it.
        _clips["retch"] = Make(0.8f, (t, n) => Envelope(t, 0.8f, 0.55f) * (Saw(t * (95f - 40f * t)) * 0.35f + n * 0.7f) * (0.55f + 0.45f * MathF.Sin(t * 38f)), rng);
        // A machine winding up: a servo whine climbing over a hard buzz.
        _clips["servo"] = Make(1.1f, (t, n) => Envelope(t, 1.1f, 0.9f) * (Square(t * (140f + 260f * t)) * 0.28f + MathF.Sin(MathF.Tau * (520f + 700f * t) * t) * 0.28f + n * 0.08f), rng);
        // Small sounds from out of sight: a couple of soft footfalls; something knocked over; a machine's tick.
        _clips["steps"] = Make(0.7f, (t, n) => (MathF.Exp(-MathF.Max(0f, t) * 40f) + MathF.Exp(-MathF.Max(0f, t - 0.36f) * 40f) * (t > 0.36f ? 1f : 0f)) * (MathF.Sin(MathF.Tau * 70f * t) * 0.6f + n * 0.5f), rng);
        _clips["clatter"] = Make(0.55f, (t, n) => MathF.Exp(-t * 9f) * (n * 0.6f + Square(t * 410f) * 0.15f * MathF.Exp(-t * 20f)) * (0.4f + 0.6f * MathF.Abs(MathF.Sin(t * 70f))), rng, highpass: true);
        _clips["tick"] = Make(0.4f, (t, n) => (MathF.Exp(-t * 60f) + 0.6f * MathF.Exp(-MathF.Max(0f, t - 0.2f) * 60f) * (t > 0.2f ? 1f : 0f)) * (Square(t * 900f) * 0.3f + n * 0.3f), rng, highpass: true);
        // Something battering at a wall: a deep blow, and the crack of what's giving.
        _clips["batter"] = Make(0.9f, (t, n) => MathF.Exp(-t * 7f) * (MathF.Sin(MathF.Tau * (48f - 20f * t) * t) * 0.9f + n * 0.5f) + MathF.Exp(-MathF.Max(0f, t - 0.08f) * 25f) * (t > 0.08f ? 1f : 0f) * n * 0.4f, rng);
        MakeTells(rng);

        for (int i = 0; i < 12; i++)
        {
            var p = new AudioStreamPlayer3D { UnitSize = 8f, MaxDistance = 90f, Bus = "Master" };
            AddChild(p);
            _pool.Add(p);
        }
        for (int i = 0; i < 4; i++)
        {
            var p = new AudioStreamPlayer3D { UnitSize = 5f, MaxDistance = 40f, Bus = "Master" };
            AddChild(p);
            _throats.Add(p);
        }
        _ui = new AudioStreamPlayer { Name = "UiSound", Bus = "Master" };
        AddChild(_ui);
    }

    AudioStreamPlayer _ui;

    /// <summary>A sound that belongs to the interface, not to a place: the discovery chime.</summary>
    public void PlayUi(string clip, float volumeDb = -11f)
    {
        if (_ui == null || !_clips.TryGetValue(clip, out var stream)) return;
        _ui.Stream = stream;
        _ui.VolumeDb = volumeDb;
        _ui.Play();
    }

    /// <summary>The UI player, for the tests: is the chime playing?</summary>
    public AudioStreamPlayer UiPlayer => _ui;

    /// <summary>A small bell: a soft strike, a fast-fading inharmonic overtone, a ringing fundamental.</summary>
    static float Bell(float t, float freq)
    {
        if (t < 0f) return 0f;
        float strike = MathF.Min(1f, t * 250f);
        return strike * MathF.Exp(-t * 6f) * (MathF.Sin(MathF.Tau * freq * t) + 0.35f * MathF.Exp(-t * 10f) * MathF.Sin(MathF.Tau * freq * 2.76f * t));
    }

    /// <summary>The wind-up sound of a creature's signature attack, pitched by size.</summary>
    public void Telegraph(Creature c)
    {
        string clip = c.Species.Rig switch
        {
            RigKind.Chain => "hiss",
            RigKind.Swarm => "buzz",
            _ when c.Species.Plan == BodyPlan.Giant => "roar",
            _ when c.Species.Body.Size < 0.8f => "chirp",
            _ => c.Species.Attack.Sound,
        };
        if (!_clips.ContainsKey(clip)) clip = "growl";
        // A person shouts; a machine and an animal sound what they weigh.
        if (c.Species.Figure is { Voice: not null } && c.Species.Spit == SpitKind.None)
        {
            Say(c.Species, VoiceMood.Shout, c.Position + Vector3.Up * 1.6f);
            return;
        }
        Play(clip, c.Position + Vector3.Up, Heft.Pitch(c.Species), Heft.Loudness(c.Species));
    }

    /// <summary>The dry click of something that hunts by its voice.</summary>
    public void Click(Creature c) => Play("click", c.Position + Vector3.Up * c.Species.BodyHeight * 0.8f, Heft.Pitch(c.Species), -6f);

    /// <summary>A breaker at a block: the warning blows, then the one that lands, heavier.</summary>
    public void Batter(Creature c, Vector3 at, bool landed = false) => Play("batter", at, Heft.Pitch(c.Species) * (landed ? 0.85f : 1f), Heft.Loudness(c.Species) + (landed ? 4f : 0f));

    /// <summary>A footfall, as heavy as what made it.</summary>
    public void Footfall(Creature c) => Play("thud", c.Position, Heft.Pitch(c.Species) * 0.8f, Heft.Loudness(c.Species) - 8f);

    /// <summary>A small sound from something out of sight in a place: what it is, doing what, as loud as it weighs.</summary>
    public void Quiet(QuietSound q)
    {
        Species s = q.Creature.Species;
        float pitch = Heft.Pitch(s) * (q.ThroughWall ? 0.8f : 1f);
        switch (q.Kind)
        {
            case QuietKind.Murmur: Say(s, q.ThroughWall ? VoiceMood.Whisper : VoiceMood.Murmur, q.At, q.Volume); break;
            case QuietKind.Skitter: Play("scratch", q.At, pitch * 1.2f, q.Volume); break;
            case QuietKind.Clatter: Play("clatter", q.At, 1f, q.Volume); break;
            case QuietKind.Servo: Play("tick", q.At, pitch, q.Volume); break;
            default: Play("steps", q.At, pitch, q.Volume); break;
        }
    }

    // ---- Voices ----------------------------------------------------------------------------

    const int Takes = 3;
    readonly Dictionary<(Voice, VoiceMood, int), AudioStreamWav> _spoken = new();
    readonly Dictionary<(Voice, VoiceMood, int), System.Threading.Tasks.Task<byte[]>> _rendering = new();
    readonly List<AudioStreamPlayer3D> _throats = new();
    int _nextThroat, _take;

    /// <summary>Someone says something, wordlessly. A phrase not yet heard is rendered off the main thread and spoken next time.</summary>
    public void Say(Species person, VoiceMood mood, Vector3 at, float? volumeDb = null)
    {
        Voice v = person.Figure?.Voice;
        if (v == null || _throats.Count == 0) return;
        var key = (v, mood, _take++ % Takes);
        if (!_spoken.TryGetValue(key, out AudioStreamWav clip))
        {
            if (_rendering.TryGetValue(key, out var task))
            {
                if (!task.IsCompleted) return;
                clip = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Voice.Rate, Stereo = false, Data = task.Result };
                _rendering.Remove(key);
                if (_spoken.Count > 256) _spoken.Clear();
                _spoken[key] = clip;
            }
            else
            {
                int take = key.Item3;
                _rendering[key] = System.Threading.Tasks.Task.Run(() =>
                {
                    short[] pcm = v.Render(mood, take);
                    var bytes = new byte[pcm.Length * 2];
                    Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                    return bytes;
                });
                return;
            }
        }
        AudioStreamPlayer3D p = _throats[_nextThroat++ % _throats.Count];
        p.Stream = clip;
        p.VolumeDb = volumeDb ?? mood switch { VoiceMood.Shout => 2f, VoiceMood.Cry => -3f, VoiceMood.Whisper => -14f, _ => -11f };
        p.MaxDistance = mood == VoiceMood.Shout ? 70f : mood == VoiceMood.Whisper ? 9f : 24f;
        p.GlobalPosition = at + Origin;
        p.Play();
    }

    public void Play(string clip, Vector3 at, float pitch = 1f, float volumeDb = 0f)
    {
        if (!_clips.TryGetValue(clip, out var stream) || _pool.Count == 0) return;
        AudioStreamPlayer3D p = _pool[_next++ % _pool.Count];
        p.Stream = stream;
        p.PitchScale = pitch;
        p.VolumeDb = volumeDb;
        p.GlobalPosition = at + Origin;
        p.Play();
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

    // ---- Tells: what gives an ambush away, before it strikes ------------------------------

    /// <summary>
    /// The sounds of caves and places, all synthesised: claws scrabbling inside a wall, a vent
    /// grille rattling, grit trickling from a ceiling, slow breathing in the dark, the dry
    /// clicks of something blind, knocking behind a door.
    /// </summary>
    void MakeTells(Rng rng)
    {
        // Scrabbling claws: short bursts of hard clicks, uneven, muffled by the rock.
        _clips["scratch"] = Make(0.9f, (t, n) =>
        {
            float burst = MathF.Max(0f, MathF.Sin(t * 23f + MathF.Sin(t * 7f) * 2f));
            float click = MathF.Pow(MathF.Max(0f, MathF.Sin(t * 410f + MathF.Sin(t * 37f) * 6f)), 24f);
            return (n * 0.35f + click * 0.9f) * burst * Math.Clamp(t * 12f, 0f, 1f) * Math.Clamp((0.9f - t) * 6f, 0f, 1f) * 0.8f;
        }, rng, highpass: true);
        // A grille rattling in its frame: metal on metal, a few uneven shakes.
        _clips["rattle"] = Make(1.2f, (t, n) =>
        {
            float shake = MathF.Pow(MathF.Max(0f, MathF.Sin(t * 31f + MathF.Sin(t * 5f) * 3f)), 6f);
            float metal = Square(t * 612f) * 0.25f + MathF.Sin(MathF.Tau * 1830f * t) * 0.2f + n * 0.25f;
            return metal * shake * Envelope(t, 1.2f, 0.8f);
        }, rng, highpass: true);
        // Grit trickling down: sparse little impacts in a soft hiss.
        _clips["dust"] = Make(1.4f, (t, n) =>
        {
            float grain = MathF.Pow(MathF.Max(0f, MathF.Sin(t * 290f + MathF.Sin(t * 53f) * 9f)), 40f);
            return (n * 0.12f + grain * 0.6f) * Math.Clamp(t * 4f, 0f, 1f) * Math.Clamp((1.4f - t) * 3f, 0f, 1f);
        }, rng, highpass: true);
        // Breathing in the dark: a slow, wet draw in, a long rasping breath out.
        _clips["breath"] = Make(1.6f, (t, n) =>
        {
            float draw = t < 0.6f ? MathF.Sin(t / 0.6f * MathF.PI) * 0.5f : MathF.Sin((t - 0.6f) / 1.0f * MathF.PI) * 0.9f;
            float rasp = 0.6f + 0.4f * MathF.Sin(t * 70f) * MathF.Sin(t * 13f);
            return n * MathF.Max(0f, draw) * rasp * 0.8f;
        }, rng);
        // Dry clicks, like a fingernail on bone: something finding you by sound.
        _clips["click"] = Make(0.8f, (t, n) =>
        {
            float k = t * 7.5f;
            float local = (k - MathF.Floor(k)) / 7.5f;
            return MathF.Exp(-local * 260f) * (MathF.Sin(MathF.Tau * 2900f * local) * 0.7f + n * 0.4f) * (MathF.Floor(k) % 3 == 2 ? 0.4f : 1f);
        }, rng, highpass: true);
        // Knocking behind a door: three heavy, uneven blows and a scrape.
        _clips["knock"] = Make(1.3f, (t, n) =>
        {
            float s = 0f;
            foreach (float at in new[] { 0.05f, 0.38f, 0.55f })
            {
                float u = t - at;
                if (u > 0f) s += MathF.Exp(-u * 18f) * (MathF.Sin(MathF.Tau * 62f * u) * 0.9f + n * 0.3f);
            }
            if (t > 0.8f) s += n * 0.25f * MathF.Sin((t - 0.8f) / 0.5f * MathF.PI);
            return s;
        }, rng);
    }

    /// <summary>A tell, heard where it happens: scratching moves with the thing in the wall.</summary>
    public void Tell(TellEvent e)
    {
        string clip = e.Kind switch
        {
            TellKind.Scratch => "scratch",
            TellKind.Rattle => "rattle",
            TellKind.Dust => "dust",
            TellKind.Shadow => "",
            TellKind.Thump => "knock",
            TellKind.Breath => "breath",
            TellKind.Click => "click",
            TellKind.Hiss => "hiss",
            _ => e.Creature?.Species.Attack.Sound ?? "growl",
        };
        if (clip == "") return;
        float pitch = e.Creature != null ? Heft.Pitch(e.Creature.Species) : 1f;
        float volume = e.Kind == TellKind.Scratch && e.Creature is { InWalls: true } ? -5f : e.Kind == TellKind.Dust ? -2f : 1f;
        Play(clip, e.Position, e.Kind is TellKind.Rattle or TellKind.Thump or TellKind.Dust ? 1f : pitch, volume);
    }
}
