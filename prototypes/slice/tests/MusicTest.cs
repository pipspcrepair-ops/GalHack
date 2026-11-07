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

namespace Slice.Tests;

/// <summary>
/// The music and its settings: a drone is the same for a seed and different between seeds and
/// moods, it never clips or sits off centre, it fades in from nothing, the climb crossfades, the
/// tracks keep to their moods and their long gaps, every credit is complete and matches CREDITS.md,
/// the volumes are remembered, and the real worker feeds the real engine. It rolls fresh seeds.
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
            Tracks(seed);
            CreditsData();
            Volumes();
            Slider();
            Moods();
            SliceAudio.MakeBuses();
            _music = new SliceMusic { Name = "Music" };
            _music.Configure(DroneMood.Temperate, seed, Array.Empty<MusicTrack>(), Path.GetTempPath());
            AddChild(_music);
            Check(AudioServer.GetBusIndex("Music") > 0 && _music.GetNode<AudioStreamPlayer>("Drones").Bus == "Music", "the drones play on the Music bus");
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
        Check(_music.Pushed >= Rate, $"the worker filled the engine's buffer without the main thread ({_music.Pushed} frames in {_waited:F1} s)");
        SetProcess(false);
        Finish();
    }

    void Finish()
    {
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    static (float[] L, float[] R) Render(DroneMood mood, ulong seed, int seconds)
    {
        var engine = new DroneEngine(mood, seed, Rate);
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
        var mix = new DroneMix(DroneMood.Lava, seed, Rate, Chunk);
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
    }

    void Tracks(ulong seed)
    {
        MusicTrack Track(string mood, string name) => new(mood, $"{mood}/{name}.ogg", name, "someone", "https://example.invalid", "CC0", "");
        var all = new[] { Track("temperate", "a"), Track("temperate", "b"), Track("hollow", "c"), Track("deep-space", "d") };

        var quiet = new TrackDirector(Array.Empty<MusicTrack>(), seed);
        bool anything = false;
        for (int i = 0; i < 36000; i++) anything |= quiet.Tick(1f, "temperate") != TrackCue.None;
        Check(!anything, "with no tracks at all the drones just go on");

        var director = new TrackDirector(all, seed);
        int played = 0;
        double lastEnd = -1.0, shortest = double.MaxValue, clock = 0.0;
        bool keptMood = true;
        for (int i = 0; i < 36000 * 2; i++)
        {
            clock += 1.0;
            TrackCue cue = director.Tick(1f, "temperate");
            if (cue == TrackCue.Start)
            {
                played++;
                keptMood &= director.Current.Mood == "temperate";
                if (lastEnd >= 0.0) shortest = Math.Min(shortest, clock - lastEnd);
                director.Began(200f);
            }
            else if (cue == TrackCue.Stop) lastEnd = clock;
        }
        Check(played > 10 && keptMood, $"only the mood's own tracks play: the Hollow's and space's never reach a temperate world ({played} in 20 hours)");
        Check(shortest >= 240.0, $"long quiet between tracks (shortest {shortest:F0} s)");

        var climbing = new TrackDirector(all, seed);
        for (int i = 0; i < 400 && climbing.Current == null; i++) climbing.Tick(1f, "temperate");
        climbing.Began(300f);
        for (int i = 0; i < 20; i++) climbing.Tick(1f, "temperate");
        float full = climbing.Level;
        bool stopped = false;
        float lowest = full;
        for (int i = 0; i < 20 && !stopped; i++)
        {
            stopped = climbing.Tick(1f, "deep-space") == TrackCue.Stop;
            lowest = Math.Min(lowest, climbing.Level);
        }
        Check(full > 0.9f && stopped && lowest == 0f, "leaving the mood's place fades its track out, slowly");

        var broken = new TrackDirector(new[] { Track("ice", "gone") }, seed);
        for (int i = 0; i < 400 && broken.Current == null; i++) broken.Tick(1f, "ice");
        broken.Failed();
        bool retried = false;
        for (int i = 0; i < 3000; i++) retried |= broken.Tick(1f, "ice") == TrackCue.Start;
        Check(!retried, "a track that won't load is dropped and the quiet goes on");
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
        Check(shared.Tracks.All(t => (t.Licence.Contains("CC0") || !string.IsNullOrWhiteSpace(t.Attribution))), "a licence that needs attribution has it word for word");

        var moods = ((DroneMood[])Enum.GetValues(typeof(DroneMood))).Select(DroneMoods.Key).ToHashSet();
        Check(shared.Tracks.All(t => moods.Contains(t.Mood)), "every track's folder is a mood");
        var listed = shared.Tracks.Select(t => Path.GetFullPath(Path.Combine(shared.MusicFolder, t.File))).ToHashSet();
        var files = Directory.GetFiles(shared.MusicFolder, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != ".gdignore").Select(Path.GetFullPath).ToList();
        Check(files.All(listed.Contains), $"no track file without a row: {string.Join(", ", files.Where(f => !listed.Contains(f)))}");

        string folder = Path.Combine(Path.GetTempPath(), $"galhack-music-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(folder, "ice"));
        string music = Path.Combine(folder, "music.json");
        string row(string name) => $"{{\"file\":\"ice/{name}\",\"title\":\"{name}\",\"author\":\"x\",\"source\":\"y\",\"licence\":\"CC0\"}}";
        File.WriteAllText(music, $"{{\"tracks\":[{row("here.ogg")},{row("gone.ogg")},{row("flac.flac")}]}}");
        File.WriteAllBytes(Path.Combine(folder, "ice", "here.ogg"), new byte[4]);
        File.WriteAllBytes(Path.Combine(folder, "ice", "flac.flac"), new byte[4]);
        Credits local = Credits.Load(music, GameData.Own("data/credits.json"), folder);
        Check(local.Tracks.Count == 1 && local.Tracks[0].Mood == "ice" && local.Rows.Count(r => r.Section.StartsWith("MUSIC (")) == 1, "a deleted file, or a format the engine can't play, leaves the player and the credits alike");
        Directory.Delete(folder, true);
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
        Check(SliceMusic.MoodOf(new PlanetSpec(seed, Archetype.Temperate, 0, Modifier.None)) == DroneMood.Temperate, "a temperate world plays temperate");
        Check(SliceMusic.MoodOf(new PlanetSpec(seed, Archetype.Temperate, 0, Modifier.Archipelago)) == DroneMood.Ocean, "an island world plays ocean");
        Check(SliceMusic.MoodOf(new PlanetSpec(seed, Archetype.Alien, 1, Modifier.None)) == DroneMood.Swamp, "the alien swamp plays swamp");
        Check(SliceMusic.MoodOf(new PlanetSpec(seed, Archetype.Lava, 0, Modifier.None)) == DroneMood.Lava, "lava plays lava");
        Check(SliceMusic.MoodOf(new PlanetSpec(seed, Archetype.Temperate, 0, Modifier.None), bloom: true) == DroneMood.Hollow, "a Bloom-taken world plays the Hollow");
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
