using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// A zone's gravity: one uniform pull for everything in the zone that isn't inside a cabin.
/// Above the atmosphere ceiling there is no weight at all, which is the handling model's one
/// altitude switch between air and space handling, so flight and gravity always agree.
/// </summary>
public sealed class ZoneGravity
{
    /// <summary>m/s². Zero for a zone in deep space.</summary>
    public float Strength = Units.StandardGravity;

    /// <summary>World height above which nothing weighs anything.</summary>
    public float Ceiling = float.PositiveInfinity;

    public Vector3 Down = Units.Down;

    public bool InAtmosphere(Vector3 position) => position.Y < Ceiling;

    /// <summary>The acceleration gravity gives a body here, m/s².</summary>
    public Vector3 At(Vector3 position) => InAtmosphere(position) ? Down * Strength : Vector3.Zero;
}

/// <summary>
/// Ship gravity, built into every structure's core: on by default, toggled from the console,
/// and it costs no power. It pulls toward the structure's own floor (structure-local -Y), and
/// only on bodies inside its cabin world. Riders on the outer hull feel the zone's gravity.
/// </summary>
public sealed class CoreGravity
{
    public bool On = true;
    public float Strength = Units.StandardGravity;

    /// <summary>The pull in the structure's own frame, m/s².</summary>
    public Vector3 Local => On ? Units.Down * Strength : Vector3.Zero;
}
