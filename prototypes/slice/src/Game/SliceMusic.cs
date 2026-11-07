using System;
using System.Threading;
using GalHack.Music;
using Godot;

namespace Slice.Game;

/// <summary>
/// The music, all of it generated: shared/Music's drones for this world, following where you are
/// on it (<see cref="MusicScenes"/>) and handing over to space across the climb. Everything it
/// follows is the world, the place and the time of day; what you are doing never reaches it.
///
/// The synthesis runs on its own low-priority thread, which also pushes straight to the engine's
/// two-second generator buffer, so a stall on the main thread can't reach the sound. It plays on the
/// Music bus, under the music and master volumes.
/// </summary>
public partial class SliceMusic : Node
{
    public const int Rate = 44100;
    const int Chunk = 2048;
    const float DroneDb = -2f, ReadEvery = 1f;

    public Game Game;

    /// <summary>A test's world, with no game behind it: set before adding the node.</summary>
    public MusicWorld World;

    /// <summary>A test's place and height, with no game behind it.</summary>
    public MusicScene Scene;
    public float Space;

    DroneMix _mix;
    SceneDwell _ground, _sky;
    AudioStreamPlayer _drone;
    AudioStreamGeneratorPlayback _playback;
    Thread _worker;
    volatile bool _running;
    long _pushed;
    float _read;

    /// <summary>The frames handed to the engine so far.</summary>
    public long Pushed => Interlocked.Read(ref _pushed);

    /// <summary>The drones running: one, or two across a change of scene, more across the climb.</summary>
    public int Running => _mix?.Running ?? 0;

    /// <summary>The scene playing on the ground.</summary>
    public MusicScene GroundScene => _mix?.Ground ?? default;

    /// <summary>The game's world is generated after this node exists, so a game's music starts when there is a world to read.</summary>
    bool WorldReady => Game == null || (Game.Surface?.Planet != null && Game.Survivor != null && Game.Places != null);

    void Start()
    {
        World ??= MusicScenes.WorldOf(Game);
        MusicScene start = Game != null ? MusicScenes.Ground(Game) : Scene;
        _ground = new SceneDwell(start);
        _sky = new SceneDwell(new MusicScene(MusicPlace.HighOrbit));
        _mix = new DroneMix(World, Rate, Chunk) { Ground = start };
        _drone = new AudioStreamPlayer
        {
            Name = "Drones", Bus = "Music", VolumeDb = DroneDb,
            Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = 2f },
        };
        AddChild(_drone);
        _drone.Play();
        _playback = (AudioStreamGeneratorPlayback)_drone.GetStreamPlayback();
        _running = true;
        _worker = new Thread(Work) { Name = "Music", IsBackground = true, Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    /// <summary>The worker: tops the buffer up, then sleeps. A new scene's engine is made here, off the main thread.</summary>
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
        if (_mix == null)
        {
            if (WorldReady) Start();
            return;
        }
        _mix.Space = Game?.Spaceflight?.AirGone ?? Space;
        _read -= (float)delta;
        if (_read > 0f) return;
        _read = ReadEvery;
        if (Game == null)
        {
            _mix.Ground = Scene;
            return;
        }
        if (!Game.InOrbit) _mix.Ground = _ground.Feed(MusicScenes.Ground(Game), ReadEvery);
        if (_mix.Space > 0f) _mix.Sky = _sky.Feed(MusicScenes.Sky(Game), ReadEvery);
    }

    public void StopAll() => _drone?.Stop();

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
