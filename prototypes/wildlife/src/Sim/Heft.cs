using System;
using Godot;

namespace Wildlife;

/// <summary>
/// How a body's size weighs on the way it moves: a small thing darts and pivots; something the
/// size of a strider takes a second or two to get going, more to stop, and swings wide at speed.
/// </summary>
public static class Heft
{
    /// <summary>Seconds from standing to top speed.</summary>
    public static float TopSpeedIn(Species s) => Math.Clamp(0.15f + 0.22f * s.Body.Size, 0.2f, 3.5f);

    public static float Accel(Creature c) => MathF.Max(0.5f, c.Stats.RunSpeed) / TopSpeedIn(c.Species);

    /// <summary>Braking: a big body gives up its speed slower than it gained it.</summary>
    public static float Brake(Creature c) => Accel(c) * Mathf.Lerp(1f, 0.6f, Bulk(c.Species));

    /// <summary>Radians a second: small things turn on the spot; a big one turns slowly, and more slowly the faster it is going.</summary>
    public static float TurnRate(Creature c, float speed)
    {
        float standing = Math.Clamp(5f / MathF.Pow(MathF.Max(0.6f, c.Species.Body.Size), 0.8f), 0.6f, 7f);
        float running = Math.Clamp(speed / MathF.Max(0.5f, c.Stats.RunSpeed), 0f, 1f);
        return standing * Mathf.Lerp(1f, 0.45f, running * Bulk(c.Species));
    }

    /// <summary>How far a body going this fast needs to stop, m.</summary>
    public static float StoppingDistance(Creature c, float speed) => speed * speed / (2f * Brake(c));

    /// <summary>0 for anything up to about a metre, 1 from five metres.</summary>
    public static float Bulk(Species s) => Math.Clamp((s.Body.Size - 1f) / 4f, 0f, 1f);
}
