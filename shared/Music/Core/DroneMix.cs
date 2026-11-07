using System;
using System.Threading;

namespace GalHack.Music;

/// <summary>
/// The music of a game in play: the world's drone and deep space's, crossfaded by how far the air
/// has gone. Only what can be heard runs: the far drone starts when the climb begins, and is let go
/// once it has been silent a while, so most of the time this costs one engine. Fed by one thread
/// (<see cref="Render"/>); <see cref="Space"/> may be set from any other.
/// </summary>
public sealed class DroneMix
{
    /// <summary>How long the mix takes to follow <see cref="Space"/>. The climb is already gradual; this means even a jump in it can't be heard.</summary>
    const float FollowSeconds = 3f;

    public int Rate { get; }
    readonly DroneMood _world, _space;
    readonly ulong _seed;
    readonly float[] _a, _b;
    DroneEngine _worldDrone, _spaceDrone;
    float _at, _target;

    public DroneMix(DroneMood world, ulong seed, int sampleRate, int maxFrames)
    {
        _world = world;
        _space = DroneMood.DeepSpace;
        _seed = seed;
        Rate = sampleRate;
        _a = new float[maxFrames * 2];
        _b = new float[maxFrames * 2];
    }

    /// <summary>How far into space the listener is: 0 on the ground, 1 once the air has gone.</summary>
    public float Space
    {
        get => Volatile.Read(ref _target);
        set => Volatile.Write(ref _target, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>The drones running right now, for tests and for the cost report.</summary>
    public int Running => (_worldDrone != null ? 1 : 0) + (_spaceDrone != null ? 1 : 0);

    /// <summary>Writes the next frames. No more than the <c>maxFrames</c> it was made with.</summary>
    public void Render(float[] left, float[] right, int frames)
    {
        float target = Space;
        if (target > 0f) _spaceDrone ??= new DroneEngine(_space, _seed ^ 0x51ACE51ACE5UL, Rate);
        if (target < 1f) _worldDrone ??= new DroneEngine(_world, _seed, Rate);

        float step = 1f - MathF.Exp(-1f / (FollowSeconds * Rate));
        Array.Clear(left, 0, frames);
        Array.Clear(right, 0, frames);
        _worldDrone?.Render(_a, _b, 0, frames);
        _spaceDrone?.Render(_a, _b, frames, frames);
        for (int i = 0; i < frames; i++)
        {
            _at += (target - _at) * step;
            // Equal power: the middle of the climb is as loud as either end.
            float angle = _at * MathF.PI / 2f, ground = MathF.Cos(angle), sky = MathF.Sin(angle);
            if (_worldDrone != null)
            {
                left[i] += _a[i] * ground;
                right[i] += _b[i] * ground;
            }
            if (_spaceDrone != null)
            {
                left[i] += _a[i + frames] * sky;
                right[i] += _b[i + frames] * sky;
            }
        }
        if (target == 0f && _at < 0.001f) _spaceDrone = null;
        if (target == 1f && _at > 0.999f) _worldDrone = null;
    }
}
