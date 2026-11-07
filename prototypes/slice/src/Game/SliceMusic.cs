using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GalHack.Music;
using Godot;
using Planets.Gen;

namespace Slice.Game;

/// <summary>
/// The music: shared/Music's drones for this world and its seed, handing over to deep space across
/// the climb, with the chosen tracks laid sparsely over them. Everything it follows is the place
/// you are in; nothing about what you are doing reaches it.
///
/// The synthesis runs on its own low-priority thread, which also pushes straight to the engine's
/// two-second generator buffer, so a stall on the main thread can't reach the sound. Everything
/// plays on the Music bus, under the music and master volumes.
/// </summary>
public partial class SliceMusic : Node
{
    public const int Rate = 44100;
    const int Chunk = 2048;
    const float DroneDb = -2f, TrackDb = -7f;

    /// <summary>How far the drones go down under a full-level track, so the two don't argue.</summary>
    const float Duck = 0.5f;

    public Game Game;

    /// <summary>Set while somewhere the Hollow Bloom has taken: nothing plays its set anywhere else.</summary>
    public bool InHollow;

    /// <summary>How far the air has gone, for a test with no game behind it.</summary>
    public float Space;

    DroneMood _world;
    ulong _seed;
    DroneMix _mix;
    TrackDirector _director;
    string _folder;
    AudioStreamPlayer _drone, _track;
    AudioStreamGeneratorPlayback _playback;
    Thread _worker;
    volatile bool _running;
    long _pushed;
    Task<AudioStream> _loading;

    /// <summary>The frames handed to the engine so far.</summary>
    public long Pushed => Interlocked.Read(ref _pushed);

    /// <summary>The drones running: one, or two across the climb.</summary>
    public int Running => _mix?.Running ?? 0;

    public TrackDirector Director => _director;

    /// <summary>A world's mood. Worlds in the data that are neither airless nor Bloom-taken yet read as their type; those two come from the caller.</summary>
    public static DroneMood MoodOf(PlanetSpec spec, bool airless = false, bool bloom = false)
    {
        if (bloom) return DroneMood.Hollow;
        if (airless) return DroneMood.Airless;
        return spec.Archetype switch
        {
            Archetype.Arid => DroneMood.Arid,
            Archetype.Ice => DroneMood.Ice,
            Archetype.Lava => DroneMood.Lava,
            Archetype.Alien => spec.Variant == 1 ? DroneMood.Swamp : DroneMood.Ice,
            _ => spec.Has(Modifier.Archipelago) ? DroneMood.Ocean : DroneMood.Temperate,
        };
    }

    /// <summary>Plays this mood from this seed, for when no game is setting it: call before adding the node.</summary>
    public void Configure(DroneMood world, ulong seed, IEnumerable<MusicTrack> tracks, string folder)
    {
        _world = world;
        _seed = seed;
        _director = new TrackDirector(tracks, seed);
        _folder = folder;
    }

    public override void _Ready()
    {
        if (_director == null) Configure(MoodOf(Game.World.Spec()), Game.World.Seed, Credits.Shared.Tracks, Credits.Shared.MusicFolder);
        _mix = new DroneMix(_world, _seed, Rate, Chunk);
        _drone = new AudioStreamPlayer
        {
            Name = "Drones", Bus = "Music", VolumeDb = DroneDb,
            Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = 2f },
        };
        _track = new AudioStreamPlayer { Name = "Track", Bus = "Music", VolumeDb = -80f };
        AddChild(_drone);
        AddChild(_track);
        _drone.Play();
        _playback = (AudioStreamGeneratorPlayback)_drone.GetStreamPlayback();
        _running = true;
        _worker = new Thread(Work) { Name = "Music", IsBackground = true, Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    /// <summary>The worker: tops the buffer up, then sleeps. Nothing here allocates once the drones exist.</summary>
    void Work()
    {
        var left = new float[Chunk];
        var right = new float[Chunk];
        var frames = new Vector2[Chunk];
        while (_running)
        {
            while (_running && _playback.GetFramesAvailable() >= Chunk)
            {
                _mix.Render(left, right, Chunk);
                for (int i = 0; i < Chunk; i++) frames[i] = new Vector2(left[i], right[i]);
                _playback.PushBuffer(frames);
                Interlocked.Add(ref _pushed, Chunk);
            }
            Thread.Sleep(40);
        }
    }

    public override void _Process(double delta)
    {
        float space = Game?.Spaceflight?.AirGone ?? Space;
        _mix.Space = space;
        DroneMood here = space >= 0.5f ? DroneMood.DeepSpace : InHollow ? DroneMood.Hollow : _world;

        switch (_director.Tick((float)delta, DroneMoods.Key(here)))
        {
            case TrackCue.Start:
                string path = Path.Combine(_folder, _director.Current.File);
                _loading = Task.Run(() => LoadTrack(path)); // reading and parsing a long file is no job for a frame
                break;
            case TrackCue.Stop:
                _track.Stop();
                _track.Stream = null;
                break;
        }
        if (_loading is { IsCompleted: true })
        {
            AudioStream stream = _loading.IsCompletedSuccessfully ? _loading.Result : null;
            _loading = null;
            if (stream == null) _director.Failed();
            else
            {
                _track.Stream = stream;
                _track.VolumeDb = -80f;
                _track.Play();
                _director.Began((float)stream.GetLength());
            }
        }
        float level = _director.Level;
        _track.VolumeDb = level > 0f ? Mathf.LinearToDb(level) + TrackDb : -80f;
        _mix.Duck = 1f - Duck * level;
    }

    static AudioStream LoadTrack(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
        ".mp3" => AudioStreamMP3.LoadFromFile(path),
        ".wav" => AudioStreamWav.LoadFromFile(path),
        _ => null,
    };

    public void StopAll()
    {
        _drone?.Stop();
        _track?.Stop();
    }

    /// <summary>The worker goes first, then the playback handle, or the engine reports it leaked at exit.</summary>
    public override void _ExitTree()
    {
        _running = false;
        _worker?.Join();
        _worker = null;
        _playback?.Dispose();
        _playback = null;
    }
}
