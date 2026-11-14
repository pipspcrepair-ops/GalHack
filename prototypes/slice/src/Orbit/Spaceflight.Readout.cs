using System;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Skies;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>
/// What the flight readout and the compass say in space. Near a body's surface (under the top of
/// its crossing band) it is that body's altitude and the place under you; in open space no body's
/// altitude, up or horizon is used: the readout is your speed and the nearest body, with how far
/// off its surface is.
/// </summary>
public partial class Spaceflight
{
    /// <summary>The nearest body to a point in orbit: its name, the distance to its surface (m), and the body (null: the world below).</summary>
    public (string Name, float ToSurface, Orbiter Body) Nearest(Vector3 world)
    {
        OrbitZone o = Orbit;
        N.Vector3 q = o.ToGlobe(world.ToNumerics());
        (string, float, Orbiter) best = ("YOUR WORLD", q.Length() - o.GlobeRadius, null); // as the system map names it
        StarSystem model = Game.Surface.Planet.StarSystem;
        double days = Game.Surface.Sky.Days;
        var eye = new D3(q.X, q.Y, q.Z);
        StarSystem.Orientation turned = model.Orient(days);
        foreach (Orbiter b in model.Bodies)
        {
            if (b == model.Home || b.Kind is OrbiterKind.Star or OrbiterKind.Belt) continue;
            float d = (float)(model.See(b, days, eye, turned).Distance - b.Radius);
            if (d < best.Item2) best = (Charts.Tidy(b.Name).ToUpperInvariant(), d, b);
        }
        return best;
    }

    /// <summary>Whether a point in orbit is near a body's surface: under the top of the world's crossing band.</summary>
    public bool NearSurface(Vector3 world) => Orbit != null && Height(world) < BandTop;

    static string Far(float m) => m >= 10000f ? $"{m / 1000f:F0} km" : m >= 1000f ? $"{m / 1000f:F1} km" : $"{MathF.Max(m, 0f):F0} m";

    /// <summary>
    /// The visor's flight line in orbit. Near the world: speed, height, the place under you. In open
    /// space: speed, and the nearest body with the distance to its surface.
    /// </summary>
    public string FlightLine(Vessel v)
    {
        Vector3 p = v.Body.Position.ToGodot();
        string cruise = v.Flight.State.Cruise ? "   CRUISE" : "";
        float speed = v.Body.Velocity.Length();
        if (NearSurface(p))
        {
            var (x, z) = Here.Under(p);
            return $"{speed:F0} m/s   ALT {Height(p):F0} m   OVER {Where(x, z)}{cruise}";
        }
        var (name, toSurface, _) = Nearest(p);
        return $"{speed:F0} m/s   {name} {Far(toSurface)}{cruise}";
    }

    /// <summary>A direction in the local frame of the place under you (east, up, south), for the compass: north stays north in orbit.</summary>
    public Vector3 Local(Vector3 direction)
    {
        if (!Game.InOrbit) return direction;
        if (_aligned) return direction; // the world's axes are the local ones here
        var (x, z) = Here.Under(Game.Survivor.WorldFeet);
        N.Quaternion toGlobe = N.Quaternion.Conjugate(Here.Turn);
        return MathX.Unrotate(Here.Map.Frame(x, z), N.Vector3.Transform(direction.ToNumerics(), toGlobe)).ToGodot();
    }
}
