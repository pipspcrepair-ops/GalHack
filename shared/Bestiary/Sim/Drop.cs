using System;

namespace Wildlife;

/// <summary>
/// Falling on nothing, the one step for everything that comes down on its feet: a town's people (in its rooms and out on the
/// land) and the wild sim's creatures on open ground. Exact for a steady pull, so a far body stepped seldom lands when and where
/// a near one stepped every tick would.
/// </summary>
public static class Drop
{
    /// <summary>
    /// One step of a fall from height <paramref name="y"/> at <paramref name="speed"/> (m/s, downward) onto the top of what is
    /// under it at <paramref name="ground"/>, under a pull of <paramref name="g"/>. True once down: on the ground, with the speed
    /// it hit at.
    /// </summary>
    public static bool Step(ref float y, ref float speed, float ground, float g, float dt)
    {
        float next = y - speed * dt - 0.5f * g * dt * dt;
        if (next > ground)
        {
            y = next;
            speed += g * dt;
            return false;
        }
        speed = MathF.Sqrt(speed * speed + 2f * g * MathF.Max(0f, y - ground));
        y = ground;
        return true;
    }
}
