using System;

namespace GalHack.Music;

/// <summary>
/// A world's music, synthesised sample by sample with no audio assets: stacked detuned strata under
/// slow filter sweeps, a noise bed that moves like the world's own weather, overtones, sparse pads
/// and distant tones, all sent through a long diffuse room. It never repeats (every slow movement
/// is a seeded random walk), never reacts to the player, and one world plus one scene always gives
/// the same piece. <see cref="Composer"/> decides what it plays; <see cref="Arc"/> and the world's own
/// theme give it a shape over tens of minutes. It has no engine dependencies, so it runs on a worker
/// or from a console tool.
/// </summary>
public sealed class DroneEngine
{
    /// <summary>Frames between control updates: slow movements are worked out this often and eased between.</summary>
    public const int Block = 64;

    /// <summary>The seconds rendered and thrown away first, so the room is full and the level settled before the fade-in.</summary>
    const float WarmSeconds = 14f;
    const float FadeInSeconds = 9f;
    const float WrongRiseSeconds = 150f;
    const int ToneVoices = 8;

    public MusicWorld World { get; }
    public MusicScene Scene { get; }
    public int Rate { get; }

    readonly MoodProfile _p;
    readonly DroneRng _rng;
    readonly Stratum[] _strata;
    readonly PadVoice[] _pads = new PadVoice[4];
    readonly ToneVoice[] _tones = new ToneVoice[ToneVoices];
    readonly Overtones _overtones;
    readonly NoiseBed _bed;
    readonly HollowBreath _breath;
    readonly DiffuseRoom _room;
    readonly float[] _scale;
    readonly WorldVoice _voice;
    readonly Arc _arc;
    readonly float _dt, _target;

    readonly float[] _strataL = new float[Block], _strataR = new float[Block];
    readonly float[] _dryL = new float[Block], _dryR = new float[Block];
    readonly float[] _sendL = new float[Block], _sendR = new float[Block];
    readonly float[] _outL = new float[Block], _outR = new float[Block];
    int _outAt = Block;

    float _root, _time, _nextChange, _nextPad, _nextTone, _nextRoot;
    float _sideLow, _tiltL, _tiltR, _power, _agc = 1f, _agcNext = 1f, _fade, _dcL, _dcR, _lastL, _lastR;
    float _wrong;
    float _answerAt, _answerHz, _answerLevel, _answerPan;
    int _toneCount;
    uint _ringPhase, _ringInc;
    float _phraseAt, _noteAt, _muffleL, _muffleR;
    int _note = -1, _shift, _echoes;
    bool _backwards;
    ToneKind _player;
    float _phraseLevel, _phraseOctave = 1f;
    Drift _macro, _wrongDrift;
    float _macroGain = 1f, _macroNext = 1f;
    bool _warm;

    /// <summary>A world type on its own, out in the open: the plain mood, for this seed.</summary>
    public DroneEngine(DroneMood mood, ulong seed, int sampleRate = 44100) : this(new MusicWorld(mood, seed), default, sampleRate) { }

    public DroneEngine(MusicWorld world, MusicScene scene, int sampleRate = 44100)
    {
        World = world;
        Scene = scene;
        Rate = sampleRate;
        _voice = WorldVoice.Of(world);
        _p = Composer.Compose(world, scene, _voice);
        // Its own stream per scene, but its root, scale and theme are the world's, so scenes crossfade in key.
        ulong key = (ulong)scene.Place * 31UL + (ulong)scene.Ground * 7UL + (ulong)scene.Day;
        _rng = new DroneRng(world.Seed * 0x9E3779B97F4A7C15UL ^ (key + 1) * 0xD1B54A32D192ED03UL);
        _dt = (float)Block / Rate;
        _target = MathF.Pow(10f, _p.LevelDb / 20f);
        DroneRng rng = _rng;

        _root = _voice.Root * _p.RootScale;
        _scale = _voice.Scale;
        _arc = new Arc(_voice.Pace, _p.Rest, rng);

        _strata = new Stratum[_p.Registers.Length];
        for (int i = 0; i < _strata.Length; i++)
        {
            float register = _p.Registers[i], hz = i == 0 ? _root : PickPitch(register, i);
            float cut = _p.Bright * MathF.Pow(register, 0.35f);
            float sag = _p.Sag * rng.Range(0.4f, 1f) * (rng.Chance(0.75f) ? -1f : 1f);
            float depth = i < 2 ? 0.25f : i == 2 ? 0.7f : 1f; // the top of the stack thins to nothing and comes back; the floor stays
            float width = i == 0 ? 0.25f : i == 1 ? 0.5f : 0.9f; // the bass stays in the middle, and folds to mono safely
            _strata[i] = new Stratum(hz, _p.Waves[i], _p.Gains[i], cut, _p.Sweep, _p.SweepMin, _p.SweepMax, _p.Detune, depth, sag, i == 0 ? _p.Sub : 0f, width, Rate, rng);
        }
        for (int i = 0; i < _pads.Length; i++) _pads[i] = new PadVoice();
        for (int i = 0; i < _tones.Length; i++) _tones[i] = new ToneVoice();
        _overtones = new Overtones(_p.PartialGain, _p.PartialLow, _p.PartialHigh, Rate, rng);
        _bed = new NoiseBed(_p, _root, Rate, rng);
        if (_p.Breath > 0f) _breath = new HollowBreath(_p.Breath, Rate, rng);
        _room = new DiffuseRoom(_p.Rt60, _p.RoomSize, _p.Damp, _p.PredelayMs, Rate, rng);
        _macro = Drift.Over(45f, 140f, rng);
        _wrongDrift = Drift.Over(40f, 90f, rng);
        _ringInc = Wavetable.Step(_root * rng.Range(1.31f, 1.44f), Rate);

        _nextChange = rng.Range(_p.ChangeMin * 0.5f, _p.ChangeMax * 0.5f);
        _nextPad = rng.Range(3f, 14f);
        _nextTone = rng.Range(_p.ToneMin * 0.4f, _p.ToneMax * 0.5f);
        _nextRoot = rng.Range(180f, 380f) * _p.Beat;
        _phraseAt = WarmSeconds + rng.Range(14f, 42f);

        // Fill the room in silence: the music comes up out of nothing, already a place.
        _power = _target * _target;
        int warmBlocks = (int)(WarmSeconds / _dt);
        for (int i = 0; i < warmBlocks; i++) RenderBlock();
        _warm = true;
    }

    float PickPitch(float register, int stratum)
    {
        // Low voices lean to the root and fifth, so the floor stays open; colour notes live higher.
        float ratio = stratum <= 1 && _rng.Chance(0.7f) ? Nearest(_rng.Chance(0.6f) ? 1.5f : 1f) : _scale[_rng.Int(_scale.Length)];
        return _root * ratio * register;
    }

    float Nearest(float ratio)
    {
        float best = _scale[0];
        foreach (float s in _scale) if (MathF.Abs(s - ratio) < MathF.Abs(best - ratio)) best = s;
        return best;
    }

    /// <summary>Writes the next frames. Any length, as often as you like, on one thread.</summary>
    public void Render(float[] left, float[] right, int offset, int frames)
    {
        while (frames > 0)
        {
            if (_outAt == Block)
            {
                RenderBlock();
                _outAt = 0;
            }
            int n = Math.Min(frames, Block - _outAt);
            Array.Copy(_outL, _outAt, left, offset, n);
            Array.Copy(_outR, _outAt, right, offset, n);
            _outAt += n;
            offset += n;
            frames -= n;
        }
    }

    void Compose()
    {
        DroneRng rng = _rng;

        // Harmony moves one voice at a time; the root itself moves rarely, and only by a mode step.
        if (_time >= _nextChange)
        {
            _nextChange = _time + rng.Range(_p.ChangeMin, _p.ChangeMax);
            int i = _strata.Length > 1 ? 1 + rng.Int(_strata.Length - 1) : 0;
            if (_strata[i].Settled) _strata[i].Retune(PickPitch(_p.Registers[i], i), rng.Range(9f, 16f), rng.Range(12f, 22f));
        }
        if (_time >= _nextRoot && _strata[0].Settled)
        {
            _nextRoot = _time + rng.Range(180f, 380f) * _p.Beat;
            float step = Nearest(rng.Chance(0.5f) ? 1.5f : 4f / 3f);
            float next = _root * step;
            while (next > _p.RootHigh) next *= 0.5f;
            if (next < _p.RootLow * 0.9f) next *= 2f;
            _root = next;
            _strata[0].Retune(_root, rng.Range(14f, 22f), rng.Range(16f, 26f));
        }

        if (_p.PadGain > 0f && _time >= _nextPad)
        {
            _nextPad = _time + rng.Range(_p.PadMin, _p.PadMax) * (1.8f - _arc.Density);
            if (_arc.Presence > 0.5f)
            foreach (PadVoice v in _pads)
            {
                if (v.Active) continue;
                float hz = _root * _scale[rng.Int(_scale.Length)] * _p.PadRegister;
                v.Start(_p.PadWave, hz, _p.PadBright * rng.Range(0.8f, 1.2f), _p.PadGain * rng.Range(0.7f, 1f), rng.Range(7f, 15f), rng.Range(10f, 22f), Rate, rng);
                break;
            }
        }

        if (_time >= _nextTone)
        {
            _nextTone = _time + rng.Range(_p.ToneMin, _p.ToneMax) * (1.6f - 0.8f * _arc.Density);
            ToneKind kind = rng.Pick(_p.Tones);
            float octave = kind == ToneKind.Bell ? 2f : 1f;
            float hz = _root * Nearest(rng.Chance(0.55f) ? 1f : rng.Pick(_scale)) * _p.ToneRegister * octave;
            float pan = rng.Range(-0.8f, 0.8f), level = _p.ToneGain * rng.Range(0.65f, 1f);
            ToneVoice voice = StartTone(kind, hz, level, pan);
            _toneCount++;
            // The Hollow's calls come back wrong, and by the end some arrive backwards.
            if (_p.Sag > 0f && voice != null)
            {
                if (_wrong > 0.55f && _toneCount % 3 == 0) voice.Reverse(rng.Range(6f, 9f));
                if (_wrong > 0.2f)
                {
                    _answerAt = _time + rng.Range(7f, 11f);
                    _answerHz = hz * MathF.Pow(2f, -rng.Range(0.03f, 0.06f) * _wrong);
                    _answerLevel = level * 0.7f;
                    _answerPan = -pan;
                }
            }
        }
        if (_answerAt > 0f && _time >= _answerAt)
        {
            _answerAt = 0f;
            StartTone(ToneKind.Horn, _answerHz, _answerLevel, _answerPan, -0.05f * _wrong);
        }
        Melody();
    }

    /// <summary>
    /// The world's theme: a few notes through its scale, never twice quite the same. Each time it may
    /// be moved up or down the scale, run backwards or lose a note, and sometimes a second instrument
    /// answers it, lower and further off. Long silence between. Under the Hollow it plays a little
    /// flat, more so as things go on: the same tune, not quite right.
    /// </summary>
    void Melody()
    {
        DroneRng rng = _rng;
        if (_note < 0)
        {
            if (_time < _phraseAt) return;
            if (_arc.Density < 0.3f || _arc.Presence < 0.8f)
            {
                _phraseAt = _time + 20f;
                return;
            }
            _note = 0;
            _noteAt = _time;
            _shift = rng.Pick(new[] { 0, 0, 0, 2, -2, 3, 4 });
            _backwards = rng.Chance(0.25f);
            _player = _echoes > 0 ? _p.Second : _p.Lead;
            _phraseLevel = _p.MelodyGain * (_echoes > 0 ? 0.55f : 1f);
            _phraseOctave = _echoes > 0 ? 0.5f : 1f;
            return;
        }
        if (_time < _noteAt) return;
        int[] motif = _voice.Motif;
        if (_note >= motif.Length)
        {
            _note = -1;
            if (_echoes == 0 && rng.Chance(0.35f))
            {
                _echoes = 1;
                _phraseAt = _time + rng.Range(6f, 16f);
            }
            else
            {
                _echoes = 0;
                _phraseAt = _time + rng.Range(_p.MelodyMin, _p.MelodyMax) * _p.Beat;
            }
            return;
        }
        int at = _backwards ? motif.Length - 1 - _note : _note;
        float length = _voice.Lengths[at] * _p.BeatSeconds * _p.Beat;
        if (rng.Chance(0.88f) || _note == 0)
        {
            int count = _scale.Length, step = motif[at] + _shift;
            int degree = ((step % count) + count) % count, octave = (step - degree) / count;
            float hz = _root * _scale[degree] * MathF.Pow(2f, octave) * _p.MelodyRegister * _phraseOctave;
            if (_p.Sag > 0f) hz *= MathF.Pow(2f, -_wrong * 0.75f * _p.Sag / 1200f);
            StartTone(_player, hz, _phraseLevel * rng.Range(0.8f, 1f), rng.Range(-0.5f, 0.5f));
        }
        _note++;
        _noteAt = _time + length;
    }

    ToneVoice StartTone(ToneKind kind, float hz, float level, float pan, float glide = 0f)
    {
        foreach (ToneVoice v in _tones)
        {
            if (v.Active) continue;
            v.Start(kind, hz, level, pan, _rng, glide);
            return v;
        }
        return null;
    }

    void Control()
    {
        _time += _dt;
        _arc.Control(_dt, _rng);
        Compose();
        if (_p.Sag > 0f)
        {
            float rise = Math.Clamp(_time / WrongRiseSeconds, 0f, 1f);
            _wrong = (0.1f + 0.9f * rise * rise * (3f - 2f * rise)) * (0.82f + 0.18f * _wrongDrift.Tick(_dt, _rng));
        }
        foreach (Stratum s in _strata) s.Control(_dt, _wrong, _arc.Density, _rng);
        foreach (PadVoice v in _pads) v.Control(_dt, Rate);
        foreach (ToneVoice v in _tones) v.Control(_dt, Rate);
        _overtones.Control(_dt, _root, _rng);
        _bed.Control(_dt, _root, _rng);
        _breath?.Control(_dt, _wrong, _rng);
        _room.Control(_dt);
        _macroGain = _macroNext;
        _macroNext = MathF.Pow(10f, 2.5f * _macro.Tick(_dt, _rng) / 20f) * (_warm ? _arc.Presence : 1f);
    }

    void RenderBlock()
    {
        Control();
        Array.Clear(_strataL);
        Array.Clear(_strataR);
        Array.Clear(_dryL);
        Array.Clear(_dryR);
        Array.Clear(_sendL);
        Array.Clear(_sendR);

        foreach (Stratum s in _strata) s.Render(_strataL, _strataR, Block);
        foreach (PadVoice v in _pads) v.Render(_dryL, _dryR, _sendL, _sendR, Block);
        foreach (ToneVoice v in _tones) v.Render(_dryL, _dryR, _sendL, _sendR, Block);
        _overtones.Render(_dryL, _dryR, _sendL, _sendR, Block);
        _bed.Render(_dryL, _dryR, _sendL, _sendR, Block);
        _breath?.Render(_sendL, _sendR, _dryL, _dryR, Block);

        if (_p.Ring > 0f)
        {
            // A little of the stack ring-modulated by a note that belongs to nothing: sidebands that
            // are not in any scale, faded up only as far as the world has gone wrong.
            float depth = _p.Ring * _wrong;
            for (int i = 0; i < Block; i++)
            {
                float carrier = Wavetable.Read(Wavetable.Sine, _ringPhase) * depth;
                _ringPhase += _ringInc;
                _strataL[i] += _strataL[i] * carrier;
                _strataR[i] += _strataR[i] * carrier;
            }
        }

        float send = _p.StrataSend, wet = _p.Wet, drive = _p.Drive, tilt = _p.Tilt, tiltL = _tiltL, tiltR = _tiltR;
        float tiltCut = 1f - MathF.Exp(-MathF.Tau * 160f / Rate), sideCut = 1f - MathF.Exp(-MathF.Tau * 220f / Rate), sideLow = _sideLow;
        float muffle = _p.Muffle > 0f ? 1f - MathF.Exp(-MathF.Tau * _p.Muffle / Rate) : 0f;
        float agc = _agc, agcStep = (_agcNext - agc) / Block;
        float macro = _macroGain, macroStep = (_macroNext - macro) / Block;
        float fade = _fade, fadeStep = _warm ? 1f / (FadeInSeconds * Rate) : 0f;
        float dcL = _dcL, dcR = _dcR, lastL = _lastL, lastR = _lastR, energy = 0f;
        for (int i = 0; i < Block; i++)
        {
            _room.Tick(_sendL[i] + _strataL[i] * send, _sendR[i] + _strataR[i] * send, out float wetL, out float wetR);
            float l = _dryL[i] + _strataL[i] + wetL * wet;
            float r = _dryR[i] + _strataR[i] + wetR * wet;
            if (drive > 1f)
            {
                l = Soft(l * drive) / drive;
                r = Soft(r * drive) / drive;
            }
            tiltL += tiltCut * (l - tiltL);
            tiltR += tiltCut * (r - tiltR);
            l -= tilt * tiltL;
            r -= tilt * tiltR;
            // The bass goes to the middle: a long room's low modes can otherwise pull the sides out of phase.
            float mid = (l + r) * 0.5f, side = (l - r) * 0.5f;
            sideLow += sideCut * (side - sideLow);
            side -= sideLow;
            l = mid + side;
            r = mid - side;
            energy += (l * l + r * r) * 0.5f;

            agc += agcStep;
            macro += macroStep;
            fade = MathF.Min(1f, fade + fadeStep);
            float eased = fade * fade * (3f - 2f * fade), g = agc * macro * eased;
            l *= g;
            r *= g;
            if (muffle > 0f)
            {
                _muffleL += muffle * (l - _muffleL);
                _muffleR += muffle * (r - _muffleR);
                l = _muffleL;
                r = _muffleR;
            }

            // Off with any drift of the centre line, and a limit it can't pass however the layers line up.
            dcL = l - lastL + 0.9985f * dcL;
            dcR = r - lastR + 0.9985f * dcR;
            lastL = l;
            lastR = r;
            _outL[i] = Limit(dcL);
            _outR[i] = Limit(dcR);
        }
        _sideLow = sideLow;
        _tiltL = tiltL;
        _tiltR = tiltR;
        _dcL = dcL;
        _dcR = dcR;
        _lastL = lastL;
        _lastR = lastR;
        _fade = fade;
        _agc = _agcNext;

        // The level is ridden over a few seconds, so a seed's loudness matches its mood's and a thick
        // moment doesn't get louder than a thin one by more than the swell intends. Ridden over ten, a
        // lone horn in a sparse stretch lifted its ten seconds by 9-12 dB and the gain sagged after it
        // (about one seed in four); over three, about one in a few hundred.
        const float RideSeconds = 3f;
        float c = 1f - MathF.Exp(-_dt / (RideSeconds * 0.5f));
        _power += c * (energy / Block - _power);
        // Held to the mood's level, less in a thin section, so a sparse stretch stays sparse.
        float want = _target * (0.6f + 0.4f * _arc.Density) / MathF.Sqrt(MathF.Max(_power, 1e-9f));
        _agcNext += (Math.Clamp(want, 0.05f, 12f) - _agcNext) * c;
    }

    static float Soft(float x) => x / MathF.Sqrt(1f + x * x);

    /// <summary>Transparent at the level music plays at, and it can never pass 0.7 of full scale.</summary>
    static float Limit(float x)
    {
        const float Ceiling = 0.7f;
        float y = x / Ceiling;
        return x / MathF.Sqrt(1f + y * y);
    }
}
