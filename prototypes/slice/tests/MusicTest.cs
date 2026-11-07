using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalHack.Music;
using Godot;
using Planets.Gen;
using Slice.Game;
using Slice.Ui;
using Biome = Planets.Gen.Biome;

namespace Slice.Tests;

/// <summary>
/// The music and its settings: a drone is the same for a seed and different between seeds, moods
/// and scenes, it never clips or sits off centre, it fades in from nothing, a world keeps its key
/// wherever you stand on it, the layers each change the result, scenes and the climb crossfade, a
/// piece has a long shape with quiet in it, every credit is complete and matches CREDITS.md, the
/// volumes are remembered, and the real worker feeds the real engine. It rolls fresh seeds.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/MusicTest.tscn
/// </summary>
public partial class MusicTest : Node
{
    int _failures, _passes;
    const int Rate = SliceMusic.Rate;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    SliceMusic _music;
    double _waited;

    public override void _Ready()
    {
        ulong seed = WorldChoice.Tested.Seed;
        GD.Print($"world: {WorldChoice.Tested}");
        try
        {
            Drones(seed);
            Climb(seed);
            Layers(seed);
            Places(seed);
            Shape(seed);
            CreditsData();
            Volumes();
            Slider();
            Moods();
            SliceAudio.MakeBuses();
            _music = new SliceMusic { Name = "Music", World = new MusicWorld(DroneMood.Temperate, seed) };
            AddChild(_music);
        }
        catch (Exception e)
        {
            Check(false, $"threw: {e}");
            Finish();
        }
    }

    public override void _Process(double delta)
    {
        if (_music == null) return;
        _waited += delta;
        if (_waited < 1.5) return;
        Check(AudioServer.GetBusIndex("Music") > 0 && _music.GetNode<AudioStreamPlayer>("Drones").Bus == "Music", "the drones play on the Music bus");
        Check(_music.Pushed >= Rate, $"the worker filled the engine's buffer without the main thread ({_music.Pushed} frames in {_waited:F1} s)");
        SetProcess(false);
        Finish();
    }

    void Finish()
    {
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    static (float[] L, float[] R) Render(DroneMood mood, ulong seed, int seconds) => Render(new MusicWorld(mood, seed), default, seconds);

    static (float[] L, float[] R) Render(MusicWorld world, MusicScene scene, int seconds)
    {
        var engine = new DroneEngine(world, scene, Rate);
        int frames = seconds * Rate;
        var l = new float[frames];
        var r = new float[frames];
        for (int at = 0; at < frames; at += 1500) engine.Render(l, r, at, Math.Min(1500, frames - at));
        return (l, r);
    }

    static double Rms(float[] x, int from, int to)
    {
        double sum = 0.0;
        for (int i = from; i < to; i++) sum += x[i] * x[i];
        return Math.Sqrt(sum / Math.Max(1, to - from));
    }

    /// <summary>How alike two signals are, 1 for the same and about 0 for unrelated.</summary>
    static double Likeness(float[] a, float[] b, int from)
    {
        double ab = 0.0, aa = 0.0, bb = 0.0;
        for (int i = from; i < a.Length; i++)
        {
            ab += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }
        return ab / Math.Sqrt(aa * bb + 1e-12);
    }

    void Drones(ulong seed)
    {
        var first = Render(DroneMood.Temperate, seed, 15);
        var again = Render(DroneMood.Temperate, seed, 15);
        Check(first.L.SequenceEqual(again.L) && first.R.SequenceEqual(again.R), "one seed gives one piece, sample for sample");
        var other = Render(DroneMood.Temperate, seed + 1, 15);
        double likeness = Math.Abs(Likeness(first.L, other.L, 5 * Rate));
        Check(likeness < 0.5, $"another seed gives another piece (likeness {likeness:F2})");

        var moods = (DroneMood[])Enum.GetValues(typeof(DroneMood));
        foreach (DroneMood mood in moods)
        {
            var (l, r) = Render(mood, seed, 30);
            float peak = 0f, step = 0f;
            double dc = 0.0;
            bool finite = true;
            for (int i = 1; i < l.Length; i++)
            {
                finite &= float.IsFinite(l[i]) && float.IsFinite(r[i]);
                peak = Math.Max(peak, Math.Max(Math.Abs(l[i]), Math.Abs(r[i])));
                step = Math.Max(step, Math.Abs(l[i] - l[i - 1]));
                dc += l[i];
            }
            double level = 20.0 * Math.Log10(Rms(l, 12 * Rate, l.Length) + 1e-12);
            Check(finite, $"{mood}: nothing but finite samples");
            Check(peak < 0.72f, $"{mood}: no clipping, peak {peak:F2}");
            Check(level is > -42.0 and < -16.0, $"{mood}: sits under the game at {level:F1} dBFS");
            Check(Math.Abs(dc / l.Length) < 0.003, $"{mood}: centred (DC {dc / l.Length:F5})");
            Check(step < 0.1f, $"{mood}: no clicks (largest step {step:F3})");
            Check(Math.Abs(l[0]) < 0.001f && Rms(l, 0, Rate / 10) < Rms(l, 12 * Rate, 13 * Rate), $"{mood}: rises out of silence");
            if (mood != DroneMood.Temperate)
            {
                var (t, _) = Render(DroneMood.Temperate, seed, 30);
                double near = Math.Abs(Likeness(t, l, 5 * Rate));
                Check(near < 0.5, $"{mood}: its own piece, not temperate's (likeness {near:F2})");
            }
        }
    }

    void Climb(ulong seed)
    {
        const int Chunk = 2048;
        var mix = new DroneMix(new MusicWorld(DroneMood.Lava, seed), Rate, Chunk);
        var l = new float[Chunk];
        var r = new float[Chunk];
        double Seconds(int s, float space)
        {
            mix.Space = space;
            double sum = 0.0;
            int chunks = s * Rate / Chunk;
            for (int i = 0; i < chunks; i++)
            {
                mix.Render(l, r, Chunk);
                sum += Rms(l, 0, Chunk) * Rms(l, 0, Chunk);
            }
            return Math.Sqrt(sum / chunks);
        }
        double ground = Seconds(16, 0f);
        Check(mix.Running == 1, "on the ground one drone runs");
        double climbing = Seconds(12, 0.5f);
        Check(mix.Running == 2, "halfway up both run, crossfading");
        Check(climbing > ground * 0.4, $"the climb leaves no hole ({ground:F3} to {climbing:F3})");
        double space = Seconds(30, 1f);
        Check(mix.Running == 1 && space > 0.003, "in space the world's drone has gone and deep space plays on");
        Seconds(1, 0f);
        Check(mix.Running == 2, "coming back down starts the world again");
        Seconds(30, 0f);
        Check(mix.Running == 1, "back on the ground space has gone");
        mix.Ground = new MusicScene(MusicPlace.Cave);
        Seconds(4, 0f);
        Check(mix.Running == 2, "a new scene crossfades from the last");
        mix.Ground = new MusicScene(MusicPlace.Bunker);
        Seconds(2, 0f);
        Check(mix.Running == 2, "another change waits its turn: never more than two");
        Seconds(40, 0f);
        Check(mix.Running == 1, "and when it has settled, one drone runs again");

        var dwell = new SceneDwell(default);
        var cave = new MusicScene(MusicPlace.Cave);
        var forest = new MusicScene(MusicPlace.Open, MusicGround.Forest);
        bool flicker = false;
        for (int i = 0; i < 40; i++) flicker |= dwell.Feed(i % 3 == 0 ? cave : default, 1f) != default;
        Check(!flicker, "walking along a border doesn't flicker the music");
        for (int i = 0; i < 6; i++) dwell.Feed(cave, 1f);
        Check(dwell.Scene == cave, "going underground counts within seconds");
        dwell = new SceneDwell(default);
        for (int i = 0; i < 10; i++) dwell.Feed(forest, 1f);
        bool early = dwell.Scene == forest;
        for (int i = 0; i < 25; i++) dwell.Feed(forest, 1f);
        Check(!early && dwell.Scene == forest, "a change of ground takes longer, a region and not a patch");
    }

    void Layers(ulong seed)
    {
        var plain = new MusicWorld(DroneMood.Temperate, seed);
        WorldVoice voice = WorldVoice.Of(plain), again = WorldVoice.Of(plain), other = WorldVoice.Of(plain with { Seed = seed + 1 });
        Check(voice.Scale.SequenceEqual(again.Scale) && voice.Motif.SequenceEqual(again.Motif) && voice.Root == again.Root, "a world's voice is fixed by its seed");
        var voices = Enumerable.Range(1, 60).Select(i => WorldVoice.Of(plain with { Seed = seed + (ulong)i })).ToList();
        int scales = voices.Select(v => string.Join(",", v.Scale.Select(x => x.ToString("F3")))).Distinct().Count();
        int leads = voices.Select(v => v.Lead).Distinct().Count();
        Check(scales >= 6 && leads >= 3, $"worlds of one type roll their own scales ({scales}) and instruments ({leads})");
        Check(voices.Select(v => v.Pace).Max() > voices.Select(v => v.Pace).Min() * 1.4f, "and their own pace");
        Check(WorldVoice.Of(plain with { Star = 0f }).Root < WorldVoice.Of(plain with { Star = 1f }).Root, "a redder star sits lower");

        float[] Sound(MusicWorld w, MusicScene s) => Render(w, s, 12).L;
        float[] home = Sound(plain, default);
        foreach (var (name, world, scene) in new (string, MusicWorld, MusicScene)[]
        {
            ("the ground", plain, new MusicScene(Ground: MusicGround.Stone)),
            ("the time of day", plain, new MusicScene(Day: DayPart.Night)),
            ("the climate", plain with { Heat = 1f, Damp = 0f }, default),
            ("the star", plain with { Star = 0f }, default),
            ("the tech level", plain with { Tech = 1f }, default),
            ("what became of it", plain with { Theme = MusicTheme.Nuclear }, default),
            ("the Hollow", plain, new MusicScene(Hollow: 1f)),
        })
            Check(!home.SequenceEqual(Sound(world, scene)), $"{name} changes the result");

        MoodProfile wild = Composer.Compose(plain with { Inhabited = false }, default, voice);
        Check(!wild.Tones.Any(t => t is ToneKind.Horn or ToneKind.Bell or ToneKind.Beacon), "a world nobody settled has no horns or bells");
    }

    void Places(ulong seed)
    {
        var world = new MusicWorld(DroneMood.Arid, seed) { Heat = 0.8f };
        foreach (MusicPlace place in (MusicPlace[])Enum.GetValues(typeof(MusicPlace)))
        {
            var (l, _) = Render(world, new MusicScene(place), 20);
            float peak = l.Max(Math.Abs);
            double level = 20.0 * Math.Log10(Rms(l, 12 * Rate, l.Length) + 1e-12);
            Check(l.All(float.IsFinite) && peak < 0.72f, $"{place}: clean, peak {peak:F2}");
            Check(level is > -46.0 and < -16.0, $"{place}: sits under the game at {level:F1} dBFS");
        }
    }

    void Shape(ulong seed)
    {
        // Half an hour at a low rate: density and quiet are the Arc's, and it is the same at any rate.
        var engine = new DroneEngine(new MusicWorld(DroneMood.Temperate, seed), new MusicScene(), 8000);
        var l = new float[8000 * 10];
        var r = new float[8000 * 10];
        var levels = new List<double>();
        for (int i = 0; i < 240; i++)
        {
            engine.Render(l, r, 0, l.Length);
            levels.Add(Rms(l, 0, l.Length));
        }
        double loud = levels.Max(), quiet = levels.Min(), largest = 0.0;
        // Steps only count outside the rests: down there, one faint tone passing is a big ratio of nothing.
        for (int i = 1; i < levels.Count; i++)
            if (Math.Min(levels[i], levels[i - 1]) > loud * 0.2) largest = Math.Max(largest, Math.Abs(20.0 * Math.Log10((levels[i] + 1e-9) / (levels[i - 1] + 1e-9))));
        Check(loud / Math.Max(quiet, 1e-6) > 2.0, $"over 40 minutes it thins and fills ({20.0 * Math.Log10(loud / Math.Max(quiet, 1e-6)):F1} dB)");
        Check(largest < 9.0, $"and never moves suddenly (largest step {largest:F1} dB in 10 s)");
    }

    void CreditsData()
    {
        Credits shared = Credits.Shared;
        Check(shared.Rows.Count >= 7, $"the credits have rows ({shared.Rows.Count})");
        Check(shared.Rows.All(r => !string.IsNullOrWhiteSpace(r.What) && !string.IsNullOrWhiteSpace(r.Author) && !string.IsNullOrWhiteSpace(r.Source) && !string.IsNullOrWhiteSpace(r.Licence)), "every credit has its work, author, source and licence");
        Check(shared.Rows.Any(r => r.What.Contains("Generated ambient drones") && r.Author.Contains("GalHack")), "the generated drones are credited to this project");
        string text = File.ReadAllText(Path.Combine(GameData.PrototypesRoot, "..", "CREDITS.md"));
        var missing = shared.Rows.Where(r => !text.Contains(r.Author) || !text.Contains(r.Source)).Select(r => r.What).ToList();
        Check(missing.Count == 0, $"CREDITS.md holds every row {string.Join(", ", missing)}");
    }

    void Volumes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"galhack-volumes-{Guid.NewGuid():N}.json");
        AudioSettings fresh = AudioSettings.Load(path);
        Check(Mathf.IsEqualApprox(fresh.Get(AudioSettings.Master), 0.5f) && fresh.Get(AudioSettings.Music) == 1f, "the master starts at 50%");
        fresh.Set(AudioSettings.Music, 0.3f);
        fresh.Set(AudioSettings.Ambience, 2f);
        fresh.Save(path);
        AudioSettings loaded = AudioSettings.Load(path);
        Check(Mathf.IsEqualApprox(loaded.Get(AudioSettings.Music), 0.3f) && loaded.Get(AudioSettings.Ambience) == 1f && Mathf.IsEqualApprox(loaded.Get(AudioSettings.Master), 0.5f), "the volumes come back, held to 0 to 1");
        File.WriteAllText(path, "not json");
        Check(Mathf.IsEqualApprox(AudioSettings.Load(path).Get(AudioSettings.Master), 0.5f), "a broken file means the defaults");
        File.Delete(path);

        var wind = new AudioStreamPlayer { Name = "Wind" };
        var step = new AudioStreamPlayer3D { Name = "Steps" };
        var music = new AudioStreamPlayer { Name = "Track", Bus = "Music" };
        SliceAudio.MakeBuses();
        SliceAudio.Route(wind);
        SliceAudio.Route(step);
        SliceAudio.Route(music);
        Check(wind.Bus == "Ambience" && step.Bus == "Effects" && music.Bus == "Music", "wind goes to ambience, everything else to effects, music stays where it is");
        wind.Free();
        step.Free();
        music.Free();
    }

    void Slider()
    {
        var g = new Gui();
        var r = new Rect2(100, 10, 200, 30);
        float value = 0.5f;
        void Pass(Vector2 at, bool pressed, bool held)
        {
            g.BeginInput(at, pressed, !pressed && !held, held, false, false);
            g.Slider("s", r, ref value, Colors.White);
            g.EndInput();
        }
        Pass(new Vector2(150, 20), true, true);
        Check(Mathf.IsEqualApprox(value, 0.25f), "pressing a slider sets it there");
        Pass(new Vector2(900, 700), false, true);
        Check(value == 1f, "dragged past its end it stops at the end");
        Pass(new Vector2(150, 20), false, false);
        Pass(new Vector2(120, 20), false, false);
        Check(value == 1f, "let go, it no longer follows the pointer");
    }

    void Moods()
    {
        ulong seed = WorldChoice.Tested.Seed;
        Check(MusicScenes.MoodOf(new PlanetSpec(seed, Archetype.Temperate, 0, Modifier.None)) == DroneMood.Temperate, "a temperate world plays temperate");
        Check(MusicScenes.MoodOf(new PlanetSpec(seed, Archetype.Temperate, 0, Modifier.Archipelago)) == DroneMood.Ocean, "an island world plays ocean");
        Check(MusicScenes.MoodOf(new PlanetSpec(seed, Archetype.Alien, 1, Modifier.None)) == DroneMood.Swamp, "the alien swamp plays swamp");
        Check(MusicScenes.MoodOf(new PlanetSpec(seed, Archetype.Lava, 0, Modifier.None)) == DroneMood.Lava, "lava plays lava");
        Check(MusicScenes.MoodOf(new PlanetSpec(seed, Archetype.Temperate, 0, Modifier.None), bloom: true) == DroneMood.Hollow, "a Bloom-taken world plays the Hollow");
        Check(MusicScenes.GroundOf(Biome.Forest) == MusicGround.Forest && MusicScenes.GroundOf(Biome.Dunes) == MusicGround.Dry && MusicScenes.GroundOf(Biome.Glacier) == MusicGround.Snow, "biomes fall into their ground families");
        Check(MusicScenes.DayOf(0.5) == DayPart.Day && MusicScenes.DayOf(0.0) == DayPart.Night && MusicScenes.DayOf(0.25) == DayPart.Dawn && MusicScenes.DayOf(0.77) == DayPart.Dusk, "the clock falls into parts of the day");
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else
        {
            _failures++;
            GD.Print($"FAIL: {what}");
        }
    }
}
