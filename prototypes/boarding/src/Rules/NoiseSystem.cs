using System;
using Godot;

namespace Boarding;

public enum NoiseKind { Footstep, Gunfire, Explosion }

/// <summary>A sound guards might hear: anyone within <see cref="Radius"/> metres can.</summary>
public readonly record struct Noise(Vector3 Position, float Radius, NoiseKind Kind, bool FromPlayer);

/// <summary>
/// Noise as gameplay, not audio: every footstep, shot and charge is announced with a
/// radius, and guards decide for themselves whether it reached them. Hacking and keycards
/// make no noise at all, which is the whole point of them.
/// </summary>
public sealed class NoiseSystem
{
    public event Action<Noise> Emitted;

    public int Count { get; private set; }
    public Noise? Last { get; private set; }

    public void Emit(Vector3 globalPosition, float radius, NoiseKind kind, bool fromPlayer)
    {
        if (radius <= 0f) return;
        var noise = new Noise(globalPosition, radius, kind, fromPlayer);
        Count++;
        Last = noise;
        Emitted?.Invoke(noise);
    }
}
