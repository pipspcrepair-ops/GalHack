using System;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Skies;
using PlanetSurface.World;
using Slice.Surface;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>
/// One body with ground in the orbit zone: the start world or one of its moons. Its globe is drawn
/// from its own map (<see cref="Globe"/>), it stands where the system has it (<see cref="StarSystem"/>,
/// on the one clock), and under its crossing it has a surface of its own to land on (<see cref="Surface"/>).
///
/// <para><b>Frames.</b> Each body's globe frame is its own map's (<see cref="GlobeMap"/>), turning
/// with its spin. In the zone's world it stands at <c>world = Turn × globe + Shift</c>. One body is
/// the zone's anchor, the frame everything flies in: it holds still and the others move round it as
/// the system says. Near another body you fly in that body's frame: it takes over as the anchor as
/// you come near it, and whatever is near a body that is not the anchor is carried along with it.</para>
/// </summary>
public sealed class Celestial
{
    /// <summary>The body in the system.</summary>
    public readonly Orbiter Body;

    /// <summary>The start world (the first body, which owns the system).</summary>
    public readonly bool IsHome;

    /// <summary>The world its surface is made from, once made.</summary>
    public PlanetWorld World { get; internal set; }

    /// <summary>The zone you land in: the start world's surface, or the moon's own.</summary>
    public SurfaceZone Surface { get; internal set; }

    public GlobeMap Map { get; internal set; }
    public Globe Globe { get; internal set; }
    public float GlobeRadius { get; internal set; }
    public float AtmosphereRadius { get; internal set; }

    /// <summary>Crossing under this radius is the descent onto it.</summary>
    public float ShellRadius { get; internal set; }

    /// <summary>Its surface's crossing height, m over its sea level.</summary>
    public float Ceiling { get; internal set; }

    /// <summary>Within this distance of its centre it takes over as the frame you fly in, m.</summary>
    public float Reach { get; internal set; }

    /// <summary>Its globe is set up and drawn.</summary>
    public bool Ready { get; internal set; }

    /// <summary>The globe's place in the world: world = Turn × globe + Shift.</summary>
    public N.Quaternion Turn { get; internal set; } = N.Quaternion.Identity;
    public N.Vector3 Shift { get; internal set; }

    /// <summary>Where it stood last tick (what it carries moves by the change).</summary>
    internal N.Quaternion TurnWas = N.Quaternion.Identity;
    internal N.Vector3 ShiftWas;
    internal bool Moved;

    /// <summary>The system as seen from it (its own spin): the start world's own model for the start world.</summary>
    internal StarSystem Seen;

    /// <summary>The node its ring and other globe-framed things hang on.</summary>
    internal Node3D Sphere;

    public Celestial(Orbiter body, bool home)
    {
        Body = body;
        IsHome = home;
    }

    public string Name => IsHome ? "world" : Charts.Tidy(Body.Name);

    public N.Vector3 ToWorld(N.Vector3 globe) => Shift + N.Vector3.Transform(globe, Turn);
    public N.Vector3 ToGlobe(N.Vector3 world) => N.Vector3.Transform(world - Shift, N.Quaternion.Conjugate(Turn));

    /// <summary>Its centre in the world.</summary>
    public Vector3 Centre => Shift.ToGodot();

    /// <summary>Height over its sea level (its globe's ground), m.</summary>
    public float Height(Vector3 world) => ToGlobe(world.ToNumerics()).Length() - GlobeRadius;

    /// <summary>Height above its shell, m.</summary>
    public float AboveShell(Vector3 world) => ToGlobe(world.ToNumerics()).Length() - ShellRadius;

    public float SurfaceHeight(float radius) => radius - GlobeRadius;
    public float RadiusFor(float height) => GlobeRadius + height;

    /// <summary>The map place under a world point (map metres, east and south).</summary>
    public (double x, double z) Under(Vector3 world) => Map.MapPoint(ToGlobe(world.ToNumerics()));

    /// <summary>Its globe frame at a moment, in the system's frame: its own map's axes as they have turned.</summary>
    internal StarSystem.Orientation Orient(double days) => Seen.Orient(days);
}
