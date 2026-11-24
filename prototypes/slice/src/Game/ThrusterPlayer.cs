using System.Threading;
using Godot;

namespace Slice.Game;

/// <summary>Anything synthesised sample by sample: an engine's voice, the warp drive's.</summary>
public interface ISynth
{
    /// <summary>Fills <paramref name="mono"/> with the next <paramref name="count"/> samples.</summary>
    void Render(float[] mono, int count, int offset = 0);
}

/// <summary>
/// Plays a <see cref="ThrusterVoice"/> (or any <see cref="ISynth"/>) through the engine's audio generator. A worker thread tops
/// the buffer up a few hundred samples at a time (the same pattern as the music), so a slow frame
/// cannot starve it and the main thread only sets three numbers. The player is routed to the
/// effects bus like every other player that joins the tree.
/// </summary>
public partial class ThrusterPlayer : Node
{
    const int Chunk = 512;

    public ThrusterVoice Voice;

    /// <summary>What it plays: <see cref="Voice"/> unless set.</summary>
    public ISynth Synth;

    /// <summary>Its sample rate.</summary>
    public int Rate = ThrusterVoice.Rate;
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
            Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = 0.3f },
        };
        AddChild(_player);
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        Synth ??= Voice;
        _running = true;
        _worker = new Thread(Work) { Name = "Thruster " + Name, IsBackground = true };
        _worker.Start();
    }

    /// <summary>Thruster workers running now, in every game (a game torn down leaves none).</summary>
    public static int Workers => Volatile.Read(ref _workers);
    static int _workers;

    void Work()
    {
        Interlocked.Increment(ref _workers);
        try
        {
            var mono = new float[Chunk];
            var frames = new Vector2[Chunk];
            while (_running)
            {
                while (_running && _playback.GetFramesAvailable() >= Chunk)
                {
                    Synth.Render(mono, Chunk);
                    for (int i = 0; i < Chunk; i++) frames[i] = new Vector2(mono[i], mono[i]);
                    _playback.PushBuffer(frames);
                    Interlocked.Add(ref _pushed, Chunk);
                }
                Thread.Sleep(12);
            }
        }
        finally { Interlocked.Decrement(ref _workers); }
    }

    /// <summary>Quiet at once (a menu, quitting): the voice still fades, then the player stops with the tree.</summary>
    public void Silence() { if (Voice != null) Voice.On = false; }

    public void StopPlayer() => _player?.Stop();

    public override void _ExitTree()
    {
        _running = false;
        _worker?.Join(2000); // a worker that is stuck must not hold the way out
        _worker = null;
        _playback?.Dispose();
        _playback = null;
    }
}
