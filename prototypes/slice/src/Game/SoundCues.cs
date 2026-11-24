using System;
using Godot;

namespace Slice.Game;

/// <summary>
/// Significant sounds as the visor's sound direction marker sees them (an option, off by
/// default): gunfire, a creature's warning or the growl before it strikes, a blast. Where each
/// came from (world) and how much it matters.
/// </summary>
public static class SoundCues
{
    public static event Action<Vector3, float> Heard;

    public static void Cue(Vector3 world, float weight = 1f) => Heard?.Invoke(world, weight);
}
