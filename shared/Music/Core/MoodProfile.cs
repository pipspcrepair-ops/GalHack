namespace GalHack.Music;

/// <summary>What a drone plays, by the place it belongs to.</summary>
public enum DroneMood { Temperate, Arid, Ice, Lava, Swamp, Ocean, Airless, DeepSpace, Hollow }

/// <summary>The moods' names, as data and logs show them.</summary>
public static class DroneMoods
{
    public static string Key(DroneMood mood) => mood switch
    {
        DroneMood.DeepSpace => "deep-space",
        _ => mood.ToString().ToLowerInvariant(),
    };
}

/// <summary>The single-cycle timbres a drone is built from.</summary>
public enum Wave { Sine, Warm, Reed, Dark, Glass, Choir, Bare }

/// <summary>How a world's noise bed moves.</summary>
public enum BedKind { Still, Wind, Surf, Rumble, Murk }

/// <summary>The kinds of distant event a world's silence is broken by.</summary>
public enum ToneKind { Bell, Horn, Flute, Glass, Groan, Call, Beacon, Bowed, Choir, Pluck, Wire, Drip }

/// <summary>
/// One mood as data: its tuning, its layers and its room. A seed picks within these bounds (root,
/// timings, brightness, room, which notes), which is why two temperate worlds differ yet both read
/// as temperate. Scales are just ratios, so stacked notes beat slowly and cleanly rather than sour.
/// </summary>
public sealed class MoodProfile
{
    public float RootLow, RootHigh;
    /// <summary>The tetrachords a world of this mood may build its scale from (see <see cref="WorldVoice"/>).</summary>
    public int[] Lower, Upper;
    public float[] Registers;
    public float[] Gains;
    public Wave[] Waves;
    public float Bright = 400f, Sweep = 1f, SweepMin = 30f, SweepMax = 80f, Detune = 0.2f;
    public float ChangeMin = 45f, ChangeMax = 110f;
    public float Sub;
    public float StrataSend = 0.7f;

    /// <summary>Set by a place that sits below the world's own register.</summary>
    public float RootScale = 1f;

    public float PartialGain;
    public int PartialLow = 3, PartialHigh = 16;

    public float PadGain;
    public float PadMin = 20f, PadMax = 40f, PadRegister = 4f;
    public Wave PadWave = Wave.Warm;
    public float PadBright = 700f;

    public ToneKind[] Tones;
    public float ToneGain = 0.2f, ToneMin = 25f, ToneMax = 50f, ToneRegister = 4f;

    public BedKind Bed;
    public float BedGain, BedLow = 200f, BedHigh = 1500f;
    public float WhistleGain;

    public float Rt60 = 9f, RoomSize = 1f, Damp = 3500f, Wet = 0.7f, PredelayMs = 40f;

    /// <summary>How far wrongness goes: cents of sag, ring depth, breath. Mostly the Hollow's.</summary>
    public float Sag, Ring, Breath;

    /// <summary>The world's theme: who plays it, how loud, how often, how long a beat is, and how slow everything runs.</summary>
    public ToneKind Lead = ToneKind.Bowed, Second = ToneKind.Bell;
    public float MelodyGain = 0.12f, MelodyMin = 80f, MelodyMax = 190f, MelodyRegister = 4f, BeatSeconds = 3f, Beat = 1f;

    /// <summary>How often a long section ends in quiet, 0 to 1.</summary>
    public float Rest = 0.5f;

    /// <summary>A lid on the whole sound in hertz, for being inside something; 0 for none.</summary>
    public float Muffle;

    /// <summary>The long-term level this mood is held at, in dB under full scale. All of them are quiet: music sits under the game.</summary>
    public float LevelDb = -25f;
    public float Drive = 1f;

    /// <summary>How much of everything under about 160 Hz the master takes off: enough to leave room for the game, and for the notes to be heard on small speakers.</summary>
    public float Tilt = 0.65f;

    public static MoodProfile Of(DroneMood mood) => mood switch
    {
        // Lonely and warm, faded: a minor mode, a far horn over the ruins, wind in what is left of them.
        DroneMood.Temperate => new MoodProfile
        {
            RootLow = 49f, RootHigh = 73f,
            Lower = new[] { 0, 0, 6 }, Upper = new[] { 0, 1, 2 },
            Registers = new[] { 1f, 1f, 2f, 2f, 4f }, Gains = new[] { 0.36f, 0.3f, 0.36f, 0.28f, 0.2f },
            Waves = new[] { Wave.Warm, Wave.Warm, Wave.Choir, Wave.Choir, Wave.Warm },
            Bright = 820f, Sweep = 1.1f, Detune = 0.25f, Sub = 0.12f,
            PartialGain = 0.15f, PartialLow = 3, PartialHigh = 14,
            PadGain = 0.26f, PadMin = 16f, PadMax = 36f, PadRegister = 4f,
            Tones = new[] { ToneKind.Horn, ToneKind.Horn, ToneKind.Bell }, ToneGain = 0.28f, ToneMin = 24f, ToneMax = 46f, ToneRegister = 2f,
            Bed = BedKind.Wind, BedGain = 0.4f, BedLow = 180f, BedHigh = 1400f, WhistleGain = 0.05f,
            Rt60 = 9f, RoomSize = 1.1f, Damp = 3500f, Wet = 0.75f, LevelDb = -25.5f,
        },
        // Dry and bleached: thin reeds a semitone from the root, wind singing in rock, a long way to the far wall.
        DroneMood.Arid => new MoodProfile
        {
            RootLow = 55f, RootHigh = 82f,
            Lower = new[] { 1, 4 }, Upper = new[] { 0, 2 },
            Registers = new[] { 1f, 2f, 2f, 4f }, Gains = new[] { 0.3f, 0.32f, 0.26f, 0.2f },
            Waves = new[] { Wave.Reed, Wave.Reed, Wave.Bare, Wave.Reed },
            Bright = 1250f, Sweep = 1.3f, SweepMin = 24f, SweepMax = 60f, Detune = 0.4f, Sub = 0.06f, StrataSend = 0.55f,
            PartialGain = 0.09f, PartialLow = 5, PartialHigh = 20,
            PadGain = 0.12f, PadMin = 26f, PadMax = 50f, PadWave = Wave.Reed, PadBright = 1000f,
            Tones = new[] { ToneKind.Flute, ToneKind.Flute, ToneKind.Bell }, ToneGain = 0.2f, ToneMin = 22f, ToneMax = 44f, ToneRegister = 4f,
            Bed = BedKind.Wind, BedGain = 0.7f, BedLow = 320f, BedHigh = 3200f, WhistleGain = 0.16f,
            Rt60 = 5.5f, RoomSize = 1.6f, Damp = 5000f, Wet = 0.5f, PredelayMs = 110f, LevelDb = -26f,
        },
        // High, still and glassy: open fourths and fifths, almost no bass, the longest bright tail on a planet.
        DroneMood.Ice => new MoodProfile
        {
            RootLow = 82f, RootHigh = 123f,
            Lower = new[] { 5, 6 }, Upper = new[] { 6, 5, 1 },
            Registers = new[] { 1f, 2f, 4f, 4f, 8f }, Gains = new[] { 0.2f, 0.3f, 0.3f, 0.22f, 0.1f },
            Waves = new[] { Wave.Glass, Wave.Sine, Wave.Glass, Wave.Glass, Wave.Sine },
            Bright = 1700f, Sweep = 0.8f, SweepMin = 40f, SweepMax = 90f, Detune = 0.12f, StrataSend = 1f,
            PartialGain = 0.2f, PartialLow = 4, PartialHigh = 32,
            PadGain = 0.16f, PadMin = 18f, PadMax = 34f, PadRegister = 8f, PadWave = Wave.Glass, PadBright = 3200f,
            Tones = new[] { ToneKind.Glass }, ToneGain = 0.17f, ToneMin = 12f, ToneMax = 28f, ToneRegister = 8f,
            Bed = BedKind.Still, BedGain = 0.18f, BedLow = 2000f, BedHigh = 8500f,
            Rt60 = 16f, RoomSize = 1.4f, Damp = 9000f, Wet = 0.9f, PredelayMs = 70f, LevelDb = -28f,
        },
        // Tectonic: a tritone in the floor, heavy and saturated, with metal groaning a long way down.
        DroneMood.Lava => new MoodProfile
        {
            RootLow = 29f, RootHigh = 41f,
            Lower = new[] { 1, 0 }, Upper = new[] { 3 },
            Registers = new[] { 1f, 1f, 2f, 2f, 4f }, Gains = new[] { 0.5f, 0.4f, 0.42f, 0.34f, 0.26f },
            Waves = new[] { Wave.Dark, Wave.Dark, Wave.Dark, Wave.Reed, Wave.Dark },
            Bright = 380f, Sweep = 1.4f, SweepMin = 20f, SweepMax = 55f, Detune = 0.5f, Sub = 0.35f, StrataSend = 0.45f,
            PartialGain = 0.04f, PartialLow = 6, PartialHigh = 18,
            PadGain = 0.12f, PadMin = 24f, PadMax = 46f, PadRegister = 4f, PadWave = Wave.Dark, PadBright = 320f,
            Tones = new[] { ToneKind.Groan }, ToneGain = 0.34f, ToneMin = 20f, ToneMax = 42f, ToneRegister = 4f,
            Bed = BedKind.Rumble, BedGain = 0.34f, BedLow = 42f, BedHigh = 240f,
            Rt60 = 7f, RoomSize = 0.9f, Damp = 1800f, Wet = 0.6f, Drive = 1.6f, Tilt = 0.3f, LevelDb = -23.5f,
        },
        // Close and wet: septimal, sour intervals, a voice-like pad, something moving under the water.
        DroneMood.Swamp => new MoodProfile
        {
            RootLow = 44f, RootHigh = 62f,
            Lower = new[] { 3 }, Upper = new[] { 7, 4 },
            Registers = new[] { 1f, 2f, 2f, 3f }, Gains = new[] { 0.36f, 0.36f, 0.32f, 0.26f },
            Waves = new[] { Wave.Dark, Wave.Choir, Wave.Choir, Wave.Choir },
            Bright = 620f, Sweep = 1.2f, Detune = 0.7f, Sub = 0.14f, StrataSend = 0.6f,
            PartialGain = 0.06f, PartialLow = 4, PartialHigh = 12,
            PadGain = 0.2f, PadMin = 18f, PadMax = 38f, PadRegister = 3f, PadWave = Wave.Choir, PadBright = 480f,
            Tones = new[] { ToneKind.Call }, ToneGain = 0.2f, ToneMin = 18f, ToneMax = 38f, ToneRegister = 3f,
            Bed = BedKind.Murk, BedGain = 0.5f, BedLow = 110f, BedHigh = 720f,
            Rt60 = 5f, RoomSize = 0.7f, Damp = 1600f, Wet = 0.6f, PredelayMs = 18f, LevelDb = -27.5f,
        },
        // Vast and tidal: slow surf with no shore, a wide minor pad, long calls from far below.
        DroneMood.Ocean => new MoodProfile
        {
            RootLow = 41f, RootHigh = 58f,
            Lower = new[] { 0, 6 }, Upper = new[] { 0, 5 },
            Registers = new[] { 1f, 2f, 2f, 4f }, Gains = new[] { 0.34f, 0.32f, 0.32f, 0.22f },
            Waves = new[] { Wave.Warm, Wave.Warm, Wave.Choir, Wave.Warm },
            Bright = 820f, Sweep = 1f, SweepMin = 30f, SweepMax = 70f, Detune = 0.2f, Sub = 0.16f, StrataSend = 0.8f,
            PartialGain = 0.1f, PartialLow = 3, PartialHigh = 12,
            PadGain = 0.22f, PadMin = 20f, PadMax = 40f,
            Tones = new[] { ToneKind.Call, ToneKind.Call, ToneKind.Horn }, ToneGain = 0.2f, ToneMin = 22f, ToneMax = 46f, ToneRegister = 4f,
            Bed = BedKind.Surf, BedGain = 0.9f, BedLow = 130f, BedHigh = 2000f,
            Rt60 = 13f, RoomSize = 1.6f, Damp = 3000f, Wet = 0.75f, PredelayMs = 60f, LevelDb = -27.5f,
        },
        // Nothing moves here: a bare hum felt through the suit, the odd harmonic, a settling crack of metal.
        DroneMood.Airless => new MoodProfile
        {
            RootLow = 36f, RootHigh = 50f,
            Lower = new[] { 5 }, Upper = new[] { 5 },
            Registers = new[] { 1f, 2f, 4f }, Gains = new[] { 0.3f, 0.28f, 0.16f },
            Waves = new[] { Wave.Bare, Wave.Bare, Wave.Sine },
            Bright = 540f, Sweep = 0.7f, SweepMin = 50f, SweepMax = 120f, Detune = 0.06f, Sub = 0.1f, StrataSend = 0.8f,
            ChangeMin = 80f, ChangeMax = 170f,
            PartialGain = 0.12f, PartialLow = 6, PartialHigh = 28,
            PadGain = 0.06f, PadMin = 34f, PadMax = 70f, PadWave = Wave.Bare, PadBright = 500f,
            Tones = new[] { ToneKind.Groan, ToneKind.Beacon }, ToneGain = 0.12f, ToneMin = 30f, ToneMax = 60f, ToneRegister = 4f,
            Bed = BedKind.Still, BedGain = 0.09f, BedLow = 60f, BedHigh = 900f,
            Rt60 = 11f, RoomSize = 1.2f, Damp = 2200f, Wet = 0.85f, LevelDb = -28f,
        },
        // The widest of them: stacked fifths and ninths in the longest room, and a signal from nowhere.
        DroneMood.DeepSpace => new MoodProfile
        {
            RootLow = 32f, RootHigh = 44f,
            Lower = new[] { 6, 2, 0 }, Upper = new[] { 1, 6 },
            Registers = new[] { 1f, 2f, 2f, 4f, 4f, 8f }, Gains = new[] { 0.32f, 0.32f, 0.32f, 0.28f, 0.2f, 0.12f },
            Waves = new[] { Wave.Warm, Wave.Warm, Wave.Choir, Wave.Choir, Wave.Glass, Wave.Sine },
            Bright = 960f, Sweep = 1.5f, SweepMin = 40f, SweepMax = 110f, Detune = 0.15f, Sub = 0.2f, StrataSend = 1f,
            ChangeMin = 60f, ChangeMax = 130f,
            PartialGain = 0.18f, PartialLow = 4, PartialHigh = 26,
            PadGain = 0.3f, PadMin = 22f, PadMax = 46f, PadRegister = 8f, PadBright = 1000f,
            Tones = new[] { ToneKind.Beacon, ToneKind.Beacon, ToneKind.Bell }, ToneGain = 0.15f, ToneMin = 26f, ToneMax = 56f, ToneRegister = 16f,
            Bed = BedKind.Still, BedGain = 0.12f, BedLow = 90f, BedHigh = 5000f,
            Rt60 = 22f, RoomSize = 2f, Damp = 4500f, Wet = 0.95f, PredelayMs = 90f, LevelDb = -26f,
        },
        // Starts as nearly a normal world and goes wrong by degrees: voices sag out of tune, the air
        // breathes, every call is answered by itself a little flat. Slow, continuous and meant.
        _ => new MoodProfile
        {
            RootLow = 46f, RootHigh = 61f,
            Lower = new[] { 1 }, Upper = new[] { 3 },
            Registers = new[] { 1f, 2f, 2f, 4f, 4f }, Gains = new[] { 0.34f, 0.3f, 0.32f, 0.24f, 0.17f },
            Waves = new[] { Wave.Choir, Wave.Bare, Wave.Choir, Wave.Bare, Wave.Glass },
            Bright = 760f, Sweep = 1.2f, Detune = 0.3f, Sub = 0.14f, StrataSend = 0.8f,
            PartialGain = 0.09f, PartialLow = 5, PartialHigh = 17,
            PadGain = 0.18f, PadMin = 18f, PadMax = 38f, PadWave = Wave.Choir, PadBright = 600f,
            Tones = new[] { ToneKind.Horn }, ToneGain = 0.24f, ToneMin = 20f, ToneMax = 36f, ToneRegister = 2f,
            Bed = BedKind.Still, BedGain = 0.16f, BedLow = 90f, BedHigh = 1200f,
            Rt60 = 10f, RoomSize = 1.1f, Damp = 2800f, Wet = 0.8f, LevelDb = -26.5f,
        },
    };
}
