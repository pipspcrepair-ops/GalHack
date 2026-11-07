using System;
using System.Collections.Generic;

namespace GalHack.Music;

/// <summary>Where the listener is. Place and time follow the world; nothing here knows what the player is doing.</summary>
public enum MusicPlace { Open, Cave, Ruin, Town, Underwater, Ship, Base, Bunker, HighOrbit, DeepSpace, Nebula, GalacticCore }

/// <summary>The ground underfoot, by family: what a world's many biomes sound like.</summary>
public enum MusicGround { Open, Forest, Wet, Dry, Stone, Snow, Fire, Shore, Strange }

public enum DayPart { Day, Dusk, Night, Dawn }

/// <summary>What became of a world, where the data says.</summary>
public enum MusicTheme { None, Exodus, Nuclear, Overgrown, Mutagenic }

/// <summary>
/// One world's fixed facts, from the layers the game already has: its type, climate, star, how
/// advanced it was and what happened to it. Everything is 0 to 1 unless it says otherwise.
/// </summary>
public sealed record MusicWorld(DroneMood Type, ulong Seed)
{
    /// <summary>0 frozen to 1 scorching, as the world map's temperature reads.</summary>
    public float Heat { get; init; } = 0.5f;

    /// <summary>0 bone dry to 1 saturated.</summary>
    public float Damp { get; init; } = 0.5f;

    /// <summary>The star's colour: 0 a dim red dwarf, 0.5 a yellow sun, 1 a blue giant.</summary>
    public float Star { get; init; } = 0.5f;

    /// <summary>The old world's tech: 0 low, 0.5 mid, 1 high.</summary>
    public float Tech { get; init; } = 0.5f;

    /// <summary>Never settled: no bells or horns, nothing anyone built.</summary>
    public bool Inhabited { get; init; } = true;

    public MusicTheme Theme { get; init; }

    /// <summary>How far the Hollow Bloom has this whole world.</summary>
    public float Hollow { get; init; }
}

/// <summary>Where on that world, and when. A change of scene is a slow crossfade, in the same key.</summary>
public readonly record struct MusicScene(MusicPlace Place = MusicPlace.Open, MusicGround Ground = MusicGround.Open, DayPart Day = DayPart.Day, float Hollow = 0f)
{
    public bool InSpace => Place is MusicPlace.HighOrbit or MusicPlace.DeepSpace or MusicPlace.Nebula or MusicPlace.GalacticCore;
}

/// <summary>
/// What a world keeps wherever you stand on it, rolled once from its seed: root, scale, pace of
/// movement, instruments and a little theme of its own. Places change the colour and the room around
/// these, never the key, so a cave on a world is still that world.
/// </summary>
public sealed class WorldVoice
{
    // Scales are built, not listed: a lower and an upper tetrachord, in just ratios, so a mood offers
    // several and no two worlds need share one.
    static readonly float[][] Lowers =
    {
        new[] { 1f, 9f / 8f, 6f / 5f, 4f / 3f }, // 0 minor
        new[] { 1f, 16f / 15f, 6f / 5f, 4f / 3f }, // 1 phrygian
        new[] { 1f, 9f / 8f, 5f / 4f, 4f / 3f }, // 2 major
        new[] { 1f, 7f / 6f, 4f / 3f }, // 3 septimal
        new[] { 1f, 16f / 15f, 5f / 4f, 4f / 3f }, // 4 double second
        new[] { 1f, 4f / 3f }, // 5 open
        new[] { 1f, 9f / 8f, 4f / 3f }, // 6 suspended
    };

    static readonly float[][] Uppers =
    {
        new[] { 3f / 2f, 8f / 5f, 9f / 5f }, // 0 minor
        new[] { 3f / 2f, 5f / 3f, 16f / 9f }, // 1 dorian
        new[] { 3f / 2f, 8f / 5f, 15f / 8f }, // 2 leading
        new[] { 7f / 5f, 3f / 2f, 8f / 5f }, // 3 tritone
        new[] { 3f / 2f, 7f / 4f }, // 4 harmonic seventh
        new[] { 3f / 2f }, // 5 open
        new[] { 3f / 2f, 27f / 16f, 16f / 9f }, // 6 bright
        new[] { 3f / 2f, 14f / 9f, 7f / 4f }, // 7 septimal
    };

    public float Root { get; private init; }
    public float[] Scale { get; private init; }

    /// <summary>How fast everything moves, this world's own tempo: above 1 is slower.</summary>
    public float Pace { get; private init; }

    public float Bright { get; private init; }
    public float Sweep { get; private init; }
    public float Detune { get; private init; }
    public float Rt60 { get; private init; }
    public float RoomSize { get; private init; }
    public float Damp { get; private init; }
    public float ToneGap { get; private init; }

    /// <summary>The two instruments that carry this world's theme, and how long its beat is, in seconds.</summary>
    public ToneKind Lead { get; private init; }
    public ToneKind Second { get; private init; }
    public float Beat { get; private init; }

    /// <summary>The theme: steps through the scale, and each note's length in beats.</summary>
    public int[] Motif { get; private init; }
    public float[] Lengths { get; private init; }

    /// <summary>One random draw per stratum, kept for the composer's choice of its timbre.</summary>
    public float[] WaveDraws { get; private init; }
    public int[] WaveReplacements { get; private init; }

    public static WorldVoice Of(MusicWorld w)
    {
        MoodProfile home = MoodProfile.Of(w.Type);
        var rng = new DroneRng(w.Seed * 0x9E3779B97F4A7C15UL ^ ((ulong)w.Type + 1) * 0xD1B54A32D192ED03UL);

        // A red sun sits a little lower than a blue one, on any world.
        float root = MathF.Exp(rng.Range(MathF.Log(home.RootLow), MathF.Log(home.RootHigh)));
        root *= MathF.Pow(2f, (w.Star - 0.5f) * 5f / 12f);

        var scale = new List<float>(Lowers[rng.Pick(home.Lower)]);
        scale.AddRange(Uppers[rng.Pick(home.Upper)]);
        if (scale.Count >= 6 && rng.Chance(0.5f))
        {
            int drop = 1 + rng.Int(scale.Count - 1);
            if (MathF.Abs(scale[drop] - 1.5f) > 0.02f && MathF.Abs(scale[drop] - 4f / 3f) > 0.02f) scale.RemoveAt(drop);
        }

        // Who plays the theme depends on who lived here: strings and breath on an old world, glass and signal on an advanced one.
        ToneKind[] leads = !w.Inhabited ? new[] { ToneKind.Flute, ToneKind.Call, ToneKind.Glass }
            : w.Tech < 0.34f ? new[] { ToneKind.Flute, ToneKind.Pluck, ToneKind.Bowed, ToneKind.Bell }
            : w.Tech < 0.67f ? new[] { ToneKind.Bowed, ToneKind.Horn, ToneKind.Bell, ToneKind.Choir }
            : new[] { ToneKind.Glass, ToneKind.Choir, ToneKind.Wire, ToneKind.Bowed };

        int length = 3 + rng.Int(4), at = 0;
        var motif = new int[length];
        var lengths = new float[length];
        float[] beats = { 1f, 1f, 2f, 2f, 3f, 4f };
        for (int i = 0; i < length; i++)
        {
            motif[i] = at;
            lengths[i] = rng.Pick(beats);
            int step = rng.Pick(new[] { -2, -1, -1, 1, 1, 2, 3 }); // mostly by step, the odd leap
            at = Math.Clamp(at + step, -4, 8);
        }
        lengths[length - 1] = 4f; // a theme ends on a long note

        var draws = new float[8];
        var replacements = new int[8];
        for (int i = 0; i < draws.Length; i++)
        {
            draws[i] = rng.Float();
            replacements[i] = rng.Int(1000);
        }

        return new WorldVoice
        {
            Root = root,
            Scale = scale.ToArray(),
            Pace = rng.Range(0.7f, 1.5f),
            Bright = rng.Range(0.75f, 1.3f),
            Sweep = rng.Range(0.85f, 1.15f),
            Detune = rng.Range(0.75f, 1.35f),
            Rt60 = rng.Range(0.85f, 1.2f),
            RoomSize = rng.Range(0.9f, 1.12f),
            Damp = rng.Range(0.8f, 1.25f),
            ToneGap = rng.Range(0.8f, 1.25f),
            Lead = rng.Pick(leads),
            Second = rng.Pick(leads),
            Beat = rng.Range(1.8f, 4.2f),
            Motif = motif,
            Lengths = lengths,
            WaveDraws = draws,
            WaveReplacements = replacements,
        };
    }
}

/// <summary>
/// Builds the sound of one scene from parts: the world type's mood, then each layer in turn (ground,
/// climate, star, tech, theme, place, time of day, the Hollow), each nudging what the last left.
/// Nothing is bespoke to any world, so every combination has its own result and a new layer is one
/// more small method.
/// </summary>
public static class Composer
{
    public static MoodProfile Compose(MusicWorld w, MusicScene s, WorldVoice v)
    {
        // Space has its own base; its worlds' character carries in through the voice.
        MoodProfile p = MoodProfile.Of(s.InSpace ? DroneMood.DeepSpace : w.Type);
        p.Bright *= v.Bright;
        p.Sweep *= v.Sweep;
        p.Detune *= v.Detune;
        p.Rt60 *= v.Rt60;
        p.RoomSize *= v.RoomSize;
        p.Damp *= v.Damp;
        p.ToneMin *= v.ToneGap;
        p.ToneMax *= v.ToneGap;
        Pace(p, v.Pace);
        Instruments(p, w, v);
        if (!s.InSpace)
        {
            Ground(p, s.Ground);
            Climate(p, w);
            Theme(p, w);
        }
        Star(p, w);
        Tech(p, w);
        Place(p, s.Place);
        if (!s.InSpace) Day(p, s.Day);
        float hollow = MathF.Max(w.Type == DroneMood.Hollow ? 1f : w.Hollow, s.Hollow);
        if (hollow > 0f) Hollow(p, hollow);
        return p;
    }

    static void Pace(MoodProfile p, float pace)
    {
        p.ChangeMin *= pace;
        p.ChangeMax *= pace;
        p.PadMin *= pace;
        p.PadMax *= pace;
        p.SweepMin *= pace;
        p.SweepMax *= pace;
        p.Beat = pace;
    }

    /// <summary>The stack keeps the mood's timbres, with an upper voice or two traded for what the world's people would have made.</summary>
    static void Instruments(MoodProfile p, MusicWorld w, WorldVoice v)
    {
        Wave[] swaps = w.Tech < 0.34f ? new[] { Wave.Reed, Wave.Warm, Wave.Choir }
            : w.Tech < 0.67f ? new[] { Wave.Warm, Wave.Choir, Wave.Dark }
            : new[] { Wave.Glass, Wave.Sine, Wave.Bare };
        var waves = (Wave[])p.Waves.Clone();
        for (int i = 2; i < waves.Length; i++)
            if (v.WaveDraws[i] < 0.4f) waves[i] = swaps[v.WaveReplacements[i] % swaps.Length];
        p.Waves = waves;
        p.Lead = v.Lead;
        p.Second = v.Second;
        p.BeatSeconds = v.Beat;
        if (!w.Inhabited) p.Tones = Natural(p.Tones);
    }

    /// <summary>Nobody ever lived here: what they would have built is taken out of the air.</summary>
    static ToneKind[] Natural(ToneKind[] tones)
    {
        var kept = new List<ToneKind>();
        foreach (ToneKind t in tones) if (t is not (ToneKind.Horn or ToneKind.Bell or ToneKind.Beacon)) kept.Add(t);
        return kept.Count > 0 ? kept.ToArray() : new[] { ToneKind.Call };
    }

    static void Ground(MoodProfile p, MusicGround ground)
    {
        switch (ground)
        {
            case MusicGround.Forest:
                p.Bright *= 0.9f;
                p.BedGain *= 0.8f;
                p.PartialGain *= 1.25f;
                p.Rt60 *= 0.85f;
                p.Damp *= 0.85f;
                break;
            case MusicGround.Wet:
                if (p.Bed != BedKind.Surf) p.Bed = BedKind.Murk;
                p.Detune *= 1.2f;
                p.Damp *= 0.8f;
                break;
            case MusicGround.Dry:
                if (p.Bed == BedKind.Still) p.Bed = BedKind.Wind;
                p.WhistleGain += 0.05f;
                p.Wet *= 0.88f;
                p.Bright *= 1.08f;
                break;
            case MusicGround.Stone:
                p.RoomSize *= 1.25f;
                p.PredelayMs += 50f;
                p.WhistleGain += 0.05f;
                break;
            case MusicGround.Snow:
                p.Bright *= 1.15f;
                p.Damp *= 1.3f;
                p.BedHigh *= 1.4f;
                p.Detune *= 0.8f;
                break;
            case MusicGround.Fire:
                p.Bright *= 0.8f;
                p.Sub += 0.1f;
                p.BedLow *= 0.6f;
                break;
            case MusicGround.Shore:
                p.Bed = BedKind.Surf;
                p.BedGain = MathF.Max(p.BedGain, 0.6f);
                p.BedLow = 130f;
                p.BedHigh = 1800f;
                break;
            case MusicGround.Strange:
                p.Ring = 0.06f;
                p.PartialHigh += 8;
                p.PartialGain *= 1.2f;
                break;
        }
    }

    static void Climate(MoodProfile p, MusicWorld w)
    {
        p.Detune *= 0.8f + 0.5f * w.Heat; // heat makes everything waver; cold keeps it true
        p.Sweep *= 0.9f + 0.25f * w.Heat;
        p.Rt60 *= 1.1f - 0.25f * w.Damp; // wet air takes the edge and length off a sound
        p.Damp *= 1.2f - 0.4f * w.Damp;
    }

    static void Star(MoodProfile p, MusicWorld w)
    {
        p.Bright *= 0.8f + 0.45f * w.Star;
        p.Damp *= 0.85f + 0.35f * w.Star;
        p.PartialHigh += (int)(6f * (w.Star - 0.5f));
    }

    static void Tech(MoodProfile p, MusicWorld w)
    {
        p.Detune *= 1.15f - 0.3f * w.Tech; // the more exact the people, the truer what they left
        p.PartialGain *= 0.9f + 0.3f * w.Tech;
    }

    static void Theme(MoodProfile p, MusicWorld w)
    {
        switch (w.Theme)
        {
            case MusicTheme.Exodus: // everyone left: the signals are still going, and more calls go unanswered
                p.Tones = Add(p.Tones, ToneKind.Beacon);
                p.PadGain *= 0.8f;
                p.MelodyMin *= 1.4f;
                p.MelodyMax *= 1.4f;
                break;
            case MusicTheme.Nuclear: // ash in the air: duller, thinner, and very slightly off
                p.Bright *= 0.82f;
                p.PartialGain *= 0.6f;
                p.BedGain *= 1.2f;
                p.Detune *= 1.2f;
                p.Sag = 7f;
                p.MelodyMin *= 1.6f;
                p.MelodyMax *= 1.6f;
                break;
            case MusicTheme.Overgrown:
                p.PartialGain *= 1.3f;
                p.PadGain *= 1.2f;
                p.Rt60 *= 0.9f;
                break;
            case MusicTheme.Mutagenic: // life gone wrong, but alive: it wanders, and sours a little
                p.Detune *= 1.4f;
                p.Ring = MathF.Max(p.Ring, 0.08f);
                break;
        }
    }

    static ToneKind[] Add(ToneKind[] tones, ToneKind extra)
    {
        var all = new ToneKind[tones.Length + 1];
        tones.CopyTo(all, 0);
        all[tones.Length] = extra;
        return all;
    }

    static void Thin(MoodProfile p, int strata)
    {
        if (p.Registers.Length <= strata) return;
        Array.Resize(ref p.Registers, strata);
        Array.Resize(ref p.Gains, strata);
        Array.Resize(ref p.Waves, strata);
    }

    static void Place(MoodProfile p, MusicPlace place)
    {
        switch (place)
        {
            case MusicPlace.Cave: // stone on every side: the top goes, the bass stays, water drips in the dark
                Thin(p, 4);
                p.Bright *= 0.55f;
                p.Bed = BedKind.Still;
                p.BedGain = 0.1f;
                p.BedLow = 70f;
                p.BedHigh = 500f;
                p.WhistleGain = 0f;
                p.Tones = new[] { ToneKind.Drip, ToneKind.Drip, ToneKind.Groan };
                p.ToneMin *= 0.35f;
                p.ToneMax *= 0.45f;
                p.ToneRegister = 16f;
                p.ToneGain = 0.09f;
                p.PartialGain *= 0.35f;
                p.PadGain *= 0.5f;
                p.Rt60 = 7f;
                p.RoomSize = 0.85f;
                p.Damp = 1500f;
                p.Wet = 0.95f;
                p.Muffle = 2600f;
                p.MelodyGain *= 0.6f;
                p.LevelDb -= 2f;
                break;
            case MusicPlace.Ruin: // the air is stiller indoors, the world outside muffled, and the rooms answer back
                p.Bright *= 0.7f;
                p.Bed = BedKind.Still;
                p.BedGain = 0.1f;
                p.BedLow = 90f;
                p.BedHigh = 900f;
                p.WhistleGain = 0f;
                p.Rt60 = 4.5f;
                p.RoomSize = 0.7f;
                p.Damp = 2000f;
                p.PredelayMs = 14f;
                p.Muffle = 3200f;
                p.PadGain *= 0.7f;
                p.Tones = Add(p.Tones, ToneKind.Groan);
                p.LevelDb -= 1.5f;
                break;
            case MusicPlace.Town: // people live here: warmer, closer, more of a tune
                p.Bright *= 1.1f;
                p.PadGain *= 1.2f;
                p.PartialGain *= 0.8f;
                p.MelodyGain *= 1.4f;
                p.MelodyMin *= 0.55f;
                p.MelodyMax *= 0.55f;
                p.Rt60 *= 0.6f;
                p.RoomSize *= 0.8f;
                p.BedGain *= 0.7f;
                p.Rest *= 0.5f;
                break;
            case MusicPlace.Underwater: // everything high is gone, and something very big is in the same water
                p.Bright *= 0.5f;
                p.Bed = BedKind.Surf;
                p.BedGain = 0.5f;
                p.BedLow = 70f;
                p.BedHigh = 420f;
                p.WhistleGain = 0f;
                p.Tones = new[] { ToneKind.Call, ToneKind.Call, ToneKind.Groan };
                p.ToneRegister = 3f;
                p.Rt60 = 12f;
                p.RoomSize = 1.5f;
                p.Damp = 900f;
                p.Muffle = 1400f;
                p.PartialGain *= 0.3f;
                p.Lead = ToneKind.Call;
                break;
            case MusicPlace.Ship: // a hull: bare machine tones, air moving, a very small tight room
            case MusicPlace.Base:
                bool ship = place == MusicPlace.Ship;
                Thin(p, ship ? 3 : 4);
                p.Waves = (Wave[])p.Waves.Clone();
                p.Waves[0] = Wave.Bare;
                p.Bright *= ship ? 0.6f : 0.75f;
                p.Detune *= 0.5f;
                p.Bed = BedKind.Still;
                p.BedGain = ship ? 0.13f : 0.09f;
                p.BedLow = 150f;
                p.BedHigh = 2400f;
                p.WhistleGain = 0f;
                p.Tones = ship ? new[] { ToneKind.Beacon, ToneKind.Wire } : new[] { ToneKind.Wire, ToneKind.Bowed };
                p.ToneGain = 0.1f;
                p.ToneRegister = 8f;
                p.Rt60 = ship ? 1.8f : 2.6f;
                p.RoomSize = 0.5f;
                p.Damp = 3000f;
                p.PredelayMs = 6f;
                p.Wet = 0.55f;
                p.PadGain *= 0.5f;
                p.LevelDb -= 2f;
                break;
            case MusicPlace.Bunker: // the first thing you hear: cold air, a hum, one dead pod's worth of silence
                Thin(p, 3);
                p.Bright *= 0.6f;
                p.Detune *= 0.6f;
                p.Bed = BedKind.Still;
                p.BedGain = 0.1f;
                p.BedLow = 120f;
                p.BedHigh = 1600f;
                p.WhistleGain = 0f;
                p.Tones = new[] { ToneKind.Wire, ToneKind.Drip };
                p.ToneGain = 0.08f;
                p.ToneRegister = 8f;
                p.Rt60 = 3.2f;
                p.RoomSize = 0.55f;
                p.Damp = 2200f;
                p.PredelayMs = 8f;
                p.Muffle = 3600f;
                p.PadGain *= 0.4f;
                p.MelodyGain *= 0.6f;
                p.LevelDb -= 3f;
                break;
            case MusicPlace.HighOrbit: // the world still in view: its warmth stays under the emptiness
                p.Bright *= 1.1f;
                p.Rt60 *= 0.75f;
                p.PadGain *= 1.15f;
                p.PartialGain *= 1.2f;
                break;
            case MusicPlace.Nebula: // lit gas in every direction: harmonics everywhere, nothing with an edge
                p.PartialGain *= 2f;
                p.PartialHigh += 10;
                p.PadGain *= 1.3f;
                p.Sweep *= 1.3f;
                p.Detune *= 1.5f;
                p.BedGain *= 2.2f;
                p.BedHigh = 7000f;
                p.Tones = new[] { ToneKind.Glass, ToneKind.Choir };
                p.ToneRegister = 8f;
                break;
            case MusicPlace.GalacticCore: // the stars crowd in and the floor drops out: the deepest, densest of all
                p.RootScale = 0.75f;
                p.Sub += 0.25f;
                p.Gains = (float[])p.Gains.Clone();
                p.Gains[0] *= 1.3f;
                p.Rt60 = 30f;
                p.PartialGain *= 1.6f;
                p.PartialHigh += 14;
                p.Bright *= 0.85f;
                p.Sweep *= 1.2f;
                p.Tones = new[] { ToneKind.Groan, ToneKind.Choir };
                p.ToneRegister = 4f;
                break;
        }
    }

    static void Day(MoodProfile p, DayPart day)
    {
        switch (day)
        {
            case DayPart.Night:
                p.Bright *= 0.75f;
                p.BedGain *= 0.75f;
                p.PadGain *= 1.1f;
                p.Rest *= 1.5f;
                p.LevelDb -= 1.5f;
                break;
            case DayPart.Dawn:
                p.PartialGain *= 1.3f;
                p.Bright *= 0.95f;
                p.MelodyGain *= 1.15f;
                break;
            case DayPart.Dusk:
                p.PadGain *= 1.2f;
                p.Bright *= 0.88f;
                break;
        }
    }

    /// <summary>
    /// The Hollow lies over any of the above, scaled by how far it has reached. It takes nothing away
    /// and adds nothing sudden: it only leans on what is already there, a little more over time.
    /// </summary>
    static void Hollow(MoodProfile p, float hollow)
    {
        p.Sag = MathF.Max(p.Sag, 46f * hollow);
        p.Ring = MathF.Max(p.Ring, 0.5f * hollow);
        p.Breath = 0.75f * hollow;
    }
}
