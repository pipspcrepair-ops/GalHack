using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PlanetSurface.Skies;
using PlanetSurface.World;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>
/// Every world of the system as zones (the design's planet, moon and gas giant orbits): the start world's orbit, and one
/// more for each other world gone to, made from its seed and the system's rules the first time a warp spools for it.
/// <list type="bullet">
/// <item>A world with ground circling the star: its orbit round its own globe, its surface, and a surface for each moon of
/// it (and their moons), all made the way the start world's are and landed on through the same crossing.</item>
/// <item>A gas giant, the star or a belt has no ground: its orbit is space in its own frame, its giant or star drawn by
/// space's sky and solid at its true radius, and a giant's moons in it with surfaces of their own.</item>
/// </list>
/// Each zone keeps its world's own rest frame, so nothing is matched on arrival. A zone left waits parked as it was, with
/// everything in it, its records the ledger's like every other zone's; nothing is kept in any other store.
/// </summary>
public partial class Spaceflight
{
    /// <summary>What making an orbit is doing this tick (the jump's slowest piece is named by it).</summary>
    internal string Doing;

    /// <summary>Every orbit made, the start world's first (once there are two).</summary>
    readonly List<OrbitZone> _orbits = new();

    /// <summary>The orbit round the start world.</summary>
    public OrbitZone FirstOrbit => _orbits.Count > 0 ? _orbits[0] : Orbit;

    /// <summary>Every orbit zone made so far, the start world's first.</summary>
    public IEnumerable<OrbitZone> Orbits => _orbits.Count > 0 ? _orbits : Orbit != null ? new[] { Orbit } : Array.Empty<OrbitZone>();

    /// <summary>
    /// Every zone in play: the start system's orbits and every surface in them (the start world alone, before its orbit is
    /// made), and out among the stars the zones of the system you are in.
    /// </summary>
    public IEnumerable<SurfaceZone> AllZones()
    {
        if (Orbit == null) yield return Game.Surface;
        foreach (OrbitZone o in Orbits)
        {
            yield return o;
            foreach (Celestial c in o.Bodies)
                if (c.Surface != null) yield return c.Surface;
        }
        if (Away != null)
            foreach (SurfaceZone z in Away.Zones) yield return z;
    }

    /// <summary>Every surface of the system.</summary>
    IEnumerable<SurfaceZone> AllSurfaces() => AllZones().Where(z => z is not OrbitZone);

    /// <summary>The orbit a body is in (as its globe or a moon of it), or null if it has none yet: the start system's, or the one you are in.</summary>
    public OrbitZone OrbitOf(Orbiter body) => Orbits.Concat(Away?.Orbits ?? Enumerable.Empty<OrbitZone>()).FirstOrDefault(o => o.Bodies.Exists(c => c.Body == body));

    /// <summary>The body in an orbit zone, or null.</summary>
    public static Celestial CelestialOf(OrbitZone o, Orbiter body) => o?.Bodies.Find(c => c.Body == body);

    /// <summary>A body's short name in the records: its place in the system's list.</summary>
    /// <summary>Out among the stars, the star's too (a body of the system you are in, or of the one a fold is making).</summary>
    string KeyOf(Orbiter body)
    {
        if (IndexOf(HomeSystem, body) is >= 0 and int i) return "b" + i;
        Abroad of = AbroadOf(body);
        return of == null ? "b-1" : $"s{of.Star}b{IndexOf(of.System, body)}";
    }

    /// <summary>The system out among the stars a body is of: the one you are in, or the one a fold is making.</summary>
    Abroad AbroadOf(Orbiter body) => new[] { Away, _coming }.FirstOrDefault(a => a != null && IndexOf(a.System, body) >= 0);

    static int IndexOf(StarSystem model, Orbiter body)
    {
        for (int i = 0; i < (model?.Bodies.Count ?? 0); i++)
            if (model.Bodies[i] == body) return i;
        return -1;
    }

    /// <summary>
    /// Each zone's name in the records: the start world's orbit, surface and moons as they always were, any other
    /// world's orbit and surface by the body's key.
    /// </summary>
    string NameOf(SurfaceZone z)
    {
        // Out among the stars: by the star, then the body's key in its system.
        if (new[] { Away, _coming }.FirstOrDefault(a => a != null && a.Zones.Contains(z)) is { } of)
            return AbroadPrefix(of.Star) + (z is OrbitZone a ? ZoneLedger.Orbit + "/" + KeyOf(a.Home.Body) : ZoneLedger.Surface + "/" + KeyOf(z.Body));
        OrbitZone start = FirstOrbit;
        if (z is OrbitZone o) return o == start ? ZoneLedger.Orbit : ZoneLedger.Orbit + "/" + KeyOf(o.Home.Body);
        if (z == Game.Surface) return ZoneLedger.Surface;
        int moon = start?.Bodies.FindIndex(c => c.Surface == z) ?? -1;
        return moon > 0 ? ZoneLedger.Moon + moon : ZoneLedger.Surface + "/" + KeyOf(z.Body);
    }

    /// <summary>A wreck's record id carries its world's key past the start world's orbit (every orbit's sites are numbered alike).</summary>
    string WreckPrefix(Vessel v) => v.Zone is OrbitZone o && o != FirstOrbit ? KeyOf(o.Home.Body) + "-" : "";

    // ---- Making a world's zones ------------------------------------------------------------------------------------

    /// <summary>The world a body's orbit is round: the body itself if it circles the star, else the world (or giant) over it.</summary>
    static Orbiter RootOf(Orbiter b)
    {
        while (b.Parent != null && b.Parent.Kind != OrbiterKind.Star) b = b.Parent;
        return b;
    }

    /// <summary>
    /// A body's own surface zone (a moon's, or any other world's), made from its spec by the world's generator on worker
    /// threads (at once, or once <see cref="SurfaceZone.Make"/> is called), waiting parked: its sky the system seen from
    /// it, its gravity its own, its crossing the same hard cap as every world's.
    /// </summary>
    SurfaceZone BodyZone(Orbiter body, bool now)
    {
        SurfaceZone home = Game.Surface;
        StarSystem model = SystemHere;
        float ceiling = Rules.OrbitAltitude("default");
        var zone = new SurfaceZone
        {
            Name = Charts.Tidy(body.Name).Replace(" ", ""), Data = home.Data, Spec = body.Spec, ShareFrom = ShareHere, SkySurfaces = SurfacesHere, Body = body,
            MakeLater = !now, ProcessMode = ProcessModeEnum.Disabled, Visible = false,
        };
        Game.AddChild(zone);
        Mute(zone);
        zone.Physics.Zone.Strength = model.GravityOf(body, home.Physics.Zone.Strength);
        zone.Physics.Zone.Ceiling = ceiling;
        zone.Built += () =>
        {
            zone.Ground.Ceiling = ceiling;
            LightBodies(zone);
            Mute(zone);
            GD.Print($"{Charts.Tidy(body.Name)} is made: {body.Spec}, gravity {zone.Physics.Zone.Strength:F1} m/sÃ‚Â²");
        };
        _surfaceShownOf[zone] = false;
        _groundShownOf[zone] = true;
        return zone;
    }

    /// <summary>
    /// Makes the orbit a body is in, a piece a call (false until it is ready to enter): for a world with ground its surface
    /// first (worker threads), then its orbit painted from it (worker threads), entering the scene parked and set up a piece
    /// a tick, then its surface arrived a piece a tick; for a giant, the star or a belt the space round it. Its moons get
    /// surfaces of their own, made as you come near (<paramref name="target"/>'s at once).
    /// </summary>
    internal IEnumerable<bool> MakeOrbit(Orbiter root, Orbiter target, Action<OrbitZone, SurfaceZone> made)
    {
        SurfaceZone start = Game.Surface;
        StarSystem model = SystemHere;
        float ceiling = Rules.OrbitAltitude("default");
        OrbitZone orbit;
        if (root.Spec != null)
        {
            Doing = "its surface zone";
            SurfaceZone home = BodyZone(root, now: true);
            made(null, home);
            yield return false;
            Doing = "waiting on its world";
            while (home.Made == null) yield return false;
            Doing = "its orbit zone";
            PlanetWorld w = home.Made;
            home.Wildlife.RollAhead(w); // its life rolled on a worker while its orbit is made
            bool clear = w.Sky.Density < SurfaceWeather.NoAir;
            orbit = new OrbitZone(w, start.Data, Rules, PoiGenerator.Gen.Hash.Combine(model.StarKey, root.Seed), ceiling, clear ? (_, _) => 0f : null)
            {
                Name = "Orbit" + home.Name, ProcessMode = ProcessModeEnum.Disabled, Visible = false, Surfaces = SurfacesHere, DrawLater = true,
            };
            orbit.Home.Surface = home;
            orbit.Days = () => Below.Sky.Days;
            if (_orbits.Count == 0) _orbits.Add(Orbit);
            if (Away != null) Away.Orbits.Add(orbit);
            else _orbits.Add(orbit);
        }
        else orbit = StarSpace(root, Away);
        made(orbit, orbit.Home.Surface);
        // A world's or a giant's moons (and theirs); the star's planets and a belt's rocks are orbits of their own.
        foreach (Orbiter m in root.Kind is OrbiterKind.Star or OrbiterKind.Belt ? Enumerable.Empty<Orbiter>() : Under(model, root).ToList())
            if (m.Spec != null && OrbitOf(m) == null)
            {
                Doing = "a moon's surface zone";
                orbit.AddMoon(m, BodyZone(m, now: m == target), ceiling);
                yield return false; // a surface zone a tick: each enters the scene with its own nodes
            }
        Doing = "waiting on its orbit's paint";
        while (!orbit.Prepared) yield return false;
        Doing = "its orbit entering the scene";
        Game.AddChild(orbit);
        yield return false;
        while (!orbit.Ready)
        {
            Doing = $"its orbit set up a piece ({orbit.Wrecks.Count} wrecks in, its globe {(orbit.Home.Ready ? "set up" : "setting up")})";
            orbit.BuildStep();
            yield return false;
        }
        // Its wrecks made solid before you arrive (nothing there can touch them yet), within the ticks' shared budget.
        while (orbit.Wrecks.Exists(w => w.PendingShapes > 0))
        {
            Doing = $"its wrecks made solid ({orbit.Wrecks.Sum(w => w.PendingShapes)} chunks to go)";
            foreach (Vessel w in orbit.Wrecks) w.BuildPendingShapes();
            yield return false;
        }
        Doing = "waiting on its life";
        while (orbit.Home.Surface is { } rolling && !rolling.Wildlife.LifeReady) yield return false;
        if (orbit.Home.Surface is { } surface)
            for (Doing = "its surface arriving"; !surface.TryArrive(); Doing = "its surface arriving: " + surface.ArriveSteps.Split(", ").Where(s => s != "").LastOrDefault()) yield return false;
        GD.Print($"[{orbit.Name} made: {orbit.Bodies.Count} bodies with ground or frame, {orbit.Wrecks.Count} wrecks, setting up {orbit.LongestBuildMs:F1} ms at most; its surface arrived {orbit.Home.Surface?.ArriveSteps}]");
        yield return true;
    }

    /// <summary>Everything under a body, moons of moons too, nearest first.</summary>
    static IEnumerable<Orbiter> Under(StarSystem model, Orbiter parent)
    {
        foreach (Orbiter b in model.Under(parent).OrderBy(b => b.Orbit))
        {
            yield return b;
            foreach (Orbiter c in Under(model, b)) yield return c;
        }
    }

    /// <summary>Lets an orbit go that was made for a jump and never entered: it, its surfaces and their worlds.</summary>
    internal void Unmake(OrbitZone orbit, SurfaceZone home)
    {
        if (orbit != null)
        {
            _orbits.Remove(orbit);
            foreach (Celestial c in orbit.Bodies)
                if (c.Surface != null) Free(c.Surface);
            Free(orbit);
        }
        else if (home != null) Free(home);
        if (_orbits.Count == 1) _orbits.Clear();
    }

    void Free(SurfaceZone z)
    {
        Forget(z);
        if (!IsInstanceValid(z)) return;
        if (z.IsInsideTree()) z.QueueFree();
        else z.Free();
    }

    /// <summary>What the flight keeps of a zone let go of, dropped.</summary>
    void Forget(SurfaceZone z)
    {
        _surfaceShownOf.Remove(z);
        _groundShownOf.Remove(z);
        _hiddenKidsOf.Remove(z);
        _sunOf.Remove(z);
        _highest.Remove(z);
    }

    /// <summary>The game goes: an orbit made that never entered the scene goes with it.</summary>
    void FreeLoose()
    {
        foreach (OrbitZone o in Orbits.ToList())
            if (IsInstanceValid(o) && !o.IsInsideTree()) o.Free();
    }

    // ---- The system's frame and a zone's -------------------------------------------------------------------------

    /// <summary>A point of a zone's world in the system's frame (m), on the one clock: from the frame body it flies in.</summary>
    public static D3 ToSystem(OrbitZone o, N.Vector3 world)
    {
        Celestial a = o.Anchor;
        double days = o.Days();
        N.Vector3 g = a.ToGlobe(world);
        return o.Home.Seen.Position(a.Body, days) + a.Orient(days).ToSystem(new D3(g.X, g.Y, g.Z));
    }

    /// <summary>A point of the system's frame in a zone's world.</summary>
    public static N.Vector3 FromSystem(OrbitZone o, D3 p)
    {
        Celestial a = o.Anchor;
        double days = o.Days();
        D3 g = a.Orient(days).ToGlobe(p - o.Home.Seen.Position(a.Body, days));
        return a.ToWorld(new N.Vector3((float)g.X, (float)g.Y, (float)g.Z));
    }

    /// <summary>The turn taking a zone's world directions to the system's.</summary>
    public static N.Quaternion WorldToSystem(OrbitZone o)
    {
        Celestial a = o.Anchor;
        StarSystem.Orientation oa = a.Orient(o.Days());
        N.Vector3 F(D3 d) => new((float)d.X, (float)d.Y, (float)d.Z);
        return N.Quaternion.Normalize(OrbitZone.FromAxes(F(oa.X), F(oa.Y), F(oa.Z)) * N.Quaternion.Conjugate(a.Turn));
    }
}
