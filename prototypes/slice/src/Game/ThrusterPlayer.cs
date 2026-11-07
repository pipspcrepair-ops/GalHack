using System.Threading;
using Godot;

namespace Slice.Game;

/// <summary>
/// Plays a <see cref="ThrusterVoice"/> through the engine's audio generator. A worker thread tops
/// the buffer up a few hundred samples at a time (the same pattern as the music), so a slow frame
/// cannot starve it and the main thread only sets three numbers. The player is routed to the
/// effects bus like every other player that joins the tree.
/// </summary>
public partial class ThrusterPlayer : Node
{
    const int Chunk = 512;

    public ThrusterVoice Voice;
    public float Db = -8f;

    /// <summary>The frames handed to the engine so far.</summary>
    public long Pushed => Interlocked.Read(ref _pushed);

    long _pushed;
    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _playback;
    Thread _worker;
    volatile bool _running;

    public override void _Ready()
    {
        _player = new AudioStreamPlayer
        {
            Name = Name,
            VolumeDb = Db,
            Stream = new AudioStreamGenerator { MixRate = ThrusterVoice.Rate, BufferLength = 0.3f },
        };
        AddChild(_player);
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        _running = true;
        _worker = new Thread(Work) { Name = "Thruster " + Name, IsBackground = true };
        _worker.Start();
    }

    void Work()
    {
        var mono = new float[Chunk];
        var frames = new Vector2[Chunk];
        while (_running)
        {
            while (_running && _playback.GetFramesAvailable() >= Chunk)
            {
                Voice.Render(mono, Chunk);
                for (int i = 0; i < Chunk; i++) frames[i] = new Vector2(mono[i], mono[i]);
                _playback.PushBuffer(frames);
                Interlocked.Add(ref _pushed, Chunk);
            }
            Thread.Sleep(12);
        }
    }

    /// <summary>Quiet at once (a menu, quitting): the voice still fades, then the player stops with the tree.</summary>
    public void Silence() => Voice.On = false;

    public void StopPlayer() => _player?.Stop();

    public override void _ExitTree()
    {
        _running = false;
        _worker?.Join();
        _worker = null;
        _playback?.Dispose();
        _playback = null;
    }
}
