using System;
using System.Threading;

namespace GalHack.Music;

/// <summary>
/// The music of a game in play: one deck for the ground and one for the sky, crossfaded by how far
/// the air has gone. Each deck crossfades slowly from scene to scene, always in the world's key.
/// Only what can be heard runs, so most of the time this costs one engine. Fed by one thread
/// (<see cref="Render"/>); the scenes and <see cref="Space"/> may be set from any other.
/// </summary>
public sealed class DroneMix
{
    /// <summary>How long the mix takes to follow <see cref="Space"/>. The climb is already gradual; this means even a jump in it can't be heard.</summary>
    const float FollowSeconds = 3f;

    /// <summary>How long one scene takes to become the next.</summary>
    public const float SceneSeconds = 18f;

    /// <summary>One scene, and the one it is leaving.</summary>
    sealed class Deck
    {
        readonly MusicWorld _world;
        readonly int _rate;
        readonly float[] _l, _r, _oldL, _oldR;
        DroneEngine _now, _old;
        double _fade = 1.0;

        public Deck(MusicWorld world, int rate, int maxFrames)
        {
            _world = world;
            _rate = rate;
            _l = new float[maxFrames];
            _r = new float[maxFrames];
            _oldL = new float[maxFrames];
            _oldR = new float[maxFrames];
        }

        public int Running => (_now != null ? 1 : 0) + (_old != null ? 1 : 0);

        public void Release()
        {
            _now = null;
            _old = null;
            _fade = 1.0;
        }

        /// <summary>Adds this deck's frames at this level. A change waits for one under way, so no more than two ever run.</summary>
        public void Render(MusicScene wanted, float[] left, float[] right, int frames, float[] gain)
        {
            if (_now == null) _now = new DroneEngine(_world, wanted, _rate);
            else if (_old == null && wanted != _now.Scene)
            {
                _old = _now;
                _now = new DroneEngine(_world, wanted, _rate);
                _fade = 0.0;
            }
            _now.Render(_l, _r, 0, frames);
            _old?.Render(_oldL, _oldR, 0, frames);
            double step = 1.0 / (SceneSeconds * _rate);
            for (int i = 0; i < frames; i++)
            {
                float g = gain[i];
                if (_old == null)
                {
                    left[i] += _l[i] * g;
                    right[i] += _r[i] * g;
                    continue;
                }
                _fade = Math.Min(1.0, _fade + step);
                float angle = (float)_fade * MathF.PI / 2f, up = MathF.Sin(angle) * g, down = MathF.Cos(angle) * g;
                left[i] += _l[i] * up + _oldL[i] * down;
                right[i] += _r[i] * up + _oldR[i] * down;
            }
            if (_fade >= 1.0) _old = null;
        }
    }

    public int Rate { get; }
    readonly Deck _ground, _sky;
    readonly float[] _groundGain, _skyGain;
    readonly object _lock = new();
    MusicScene _groundScene, _skyScene = new(MusicPlace.HighOrbit);
    float _target;
    double _at; // a double: an ease this slow moves by less than a float can hold near 1, and would stall short

    public DroneMix(MusicWorld world, int sampleRate, int maxFrames)
    {
        Rate = sampleRate;
        _ground = new Deck(world, sampleRate, maxFrames);
        _sky = new Deck(world, sampleRate, maxFrames);
        _groundGain = new float[maxFrames];
        _skyGain = new float[maxFrames];
    }

    /// <summary>How far into space the listener is: 0 on the ground, 1 once the air has gone.</summary>
    public float Space
    {
        get => Volatile.Read(ref _target);
        set => Volatile.Write(ref _target, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>The scene on the ground.</summary>
    public MusicScene Ground
    {
        get { lock (_lock) return _groundScene; }
        set { lock (_lock) _groundScene = value; }
    }

    /// <summary>The scene in the sky.</summary>
    public MusicScene Sky
    {
        get { lock (_lock) return _skyScene; }
        set { lock (_lock) _skyScene = value; }
    }

    /// <summary>The drones running right now, for tests and for the cost report.</summary>
    public int Running => _ground.Running + _sky.Running;

    /// <summary>Writes the next frames. No more than the <c>maxFrames</c> it was made with.</summary>
    public void Render(float[] left, float[] right, int frames)
    {
        float target = Space;
        double step = 1.0 - Math.Exp(-1.0 / (FollowSeconds * Rate));
        for (int i = 0; i < frames; i++)
        {
            _at += (target - _at) * step;
            // Equal power: the middle of the climb is as loud as either end.
            float angle = (float)_at * MathF.PI / 2f;
            _groundGain[i] = MathF.Cos(angle);
            _skyGain[i] = MathF.Sin(angle);
        }
        Array.Clear(left, 0, frames);
        Array.Clear(right, 0, frames);
        if (target < 1f || _at < 0.999) _ground.Render(Ground, left, right, frames, _groundGain);
        else _ground.Release();
        if (target > 0f || _at > 0.001) _sky.Render(Sky, left, right, frames, _skyGain);
        else _sky.Release();
    }
}

/// <summary>
/// Keeps the music from flickering between scenes at a border: a new scene has to hold for a few
/// seconds before it counts, longer if only the ground has changed than if you have gone indoors.
/// </summary>
public sealed class SceneDwell
{
    const float PlaceSeconds = 5f, GroundSeconds = 30f;
    MusicScene _candidate;
    float _held;

    public MusicScene Scene { get; private set; }

    public SceneDwell(MusicScene start)
    {
        Scene = start;
        _candidate = start;
    }

    public MusicScene Feed(MusicScene seen, float dt)
    {
        if (seen != _candidate)
        {
            _candidate = seen;
            _held = 0f;
        }
        _held += dt;
        if (seen != Scene && _held >= (seen.Place != Scene.Place ? PlaceSeconds : GroundSeconds)) Scene = seen;
        return Scene;
    }
}
