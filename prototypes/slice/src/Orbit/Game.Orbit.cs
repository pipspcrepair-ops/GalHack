using Godot;
using Slice.Orbit;
using Slice.Surface;
using Slice.Vessels;

namespace Slice.Game;

/// <summary>The game across zones: the planet's surface and its orbit, one of them the zone you are in.</summary>
public partial class Game
{
    /// <summary>The planet's surface zone. It stays in memory, as you left it, while you are in orbit.</summary>
    public SurfaceZone Surface { get; private set; }

    /// <summary>The orbit round the planet, once a climb out has started it; it stays in memory while you are below.</summary>
    public OrbitZone Orbit => Spaceflight?.Orbit;

    /// <summary>In orbit (not on the planet's surface).</summary>
    public bool InOrbit => Zone is OrbitZone;

    /// <summary>Climbing out, coming down, and everything that differs in space.</summary>
    public Spaceflight Spaceflight { get; private set; }

    /// <summary>Starts the game's zones: the surface it begins in, and the spaceflight that crosses to orbit and back.</summary>
    void StartZones()
    {
        Surface = Zone;
        Spaceflight = new Spaceflight { Name = "Spaceflight", Game = this };
        AddChild(Spaceflight);
        Zone.Built += Spaceflight.PlanetBuilt;
    }

    /// <summary>Sets the chair within reach as the one to sit in.</summary>
    static bool TakeNearestChair(Vessel vessel, Vector3 eye)
    {
        vessel.NearestChair(eye, out GalHack.Structures.Int3 chair);
        vessel.TakeChair(chair);
        return true;
    }

    /// <summary>
    /// The last step of a zone handover: the zone you are in is now <paramref name="zone"/>. The
    /// camera, the cockpit console and the shots' effects go with you; everything the menu, the
    /// visor and the tools ask about (structures, containers, devices) is that zone's.
    /// </summary>
    internal void EnterZone(SurfaceZone zone)
    {
        // Camera and shots go with you; the console follows only if it isn't riding a hull with you.
        foreach (Node n in new Node[] { Camera, Console, Shots })
            if (n != null && n.GetParent() is SurfaceZone && n.GetParent() != zone) n.Reparent(zone, false);
        zone.Camera = Camera;
        zone.Focus = Focus;
        zone.WorldRules.Mode = Mode;
        Zone = zone;
        CloseContainer();
    }
}
