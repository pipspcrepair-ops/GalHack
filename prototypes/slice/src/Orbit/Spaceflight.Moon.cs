using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Skies;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>
/// The start world's moon, a place you fly to and land on. It is a body of the system like any
/// other (<see cref="StarSystem"/>, the one source of truth), a short flight out from high orbit at
/// Empyrion's distances, and it has a surface of its own: a zone made from its own spec off the
/// main thread from the start, waiting parked, with no loading screen when you get there.
///
/// Gravity frames hand over: in the orbit zone you fly in the frame of the body you are near (the
/// zone's anchor). As you come within a moon's reach it takes over: it holds still and the world
/// you left moves round it, so its ground waits where you see it. The descent onto it and the climb
/// off it are the start world's, the same seam over its own map.
/// </summary>
public partial class Spaceflight
{
    /// <summary>The body you are near, on or over: the orbit zone's anchor (the start world before there is an orbit).</summary>
    public Celestial Here => Orbit?.Anchor;

    /// <summary>The surface of the body you are near, on or over: the start world's, or the moon's.</summary>
    public SurfaceZone Below => Here?.Surface ?? Game.Surface;

    /// <summary>The thruster moon's surface zone, made from the start (parked till you land there), or null.</summary>
    public SurfaceZone MoonSurface => Orbit?.Moon?.Surface;

    /// <summary>On a moon's surface (or anywhere on a world that isn't the start world).</summary>
    public bool OnOtherBody => Game.Zone != null && Game.Zone != Game.Surface && Game.Zone is not OrbitZone;

    /// <summary>Where you last left each body's surface, in its own frame: you come back down near it.</summary>
    readonly Dictionary<Celestial, Vector3> _leftAt = new();

    /// <summary>The highest ground of each body, m.</summary>
    readonly Dictionary<SurfaceZone, float> _highest = new();

    /// <summary>
    /// Every moon you can fly to (the start world's own, and on a giant's moon her sisters round the giant), each
    /// with a surface of its own made from its own spec by the world's generator on worker threads: the thruster
    /// moon's from the first moment, every other one's once you come within <see cref="OrbitRules.SpaceRules.MakeMoonWithin"/>
    /// of it (<see cref="MoonComing"/>). Each waits parked (out of the physics and hidden) until you land on it; its
    /// sky is the system's seen from it, its gravity its own, its crossing the same hard cap as every world's.
    /// </summary>
    void MakeMoons()
    {
        SurfaceZone home = Game.Surface;
        StarSystem model = home.Planet.StarSystem;
        if (Orbit.Moon != null) return;
        float ceiling = Rules.OrbitAltitude("default");
        foreach (Orbiter moon in model.Bodies.Where(b => b.Kind == OrbiterKind.Moon && b != model.Home && b.Spec != null && (b.Parent == model.Home || b.Parent == model.Neighbour))
                     .OrderBy(b => b != model.FuelMoon))
        {
            var zone = new SurfaceZone
            {
                Name = Charts.Tidy(moon.Name).Replace(" ", ""), Data = home.Data, Spec = moon.Spec, ShareFrom = home.Planet, SkySurfaces = home.Sky.Surfaces, Body = moon,
                MakeLater = moon != model.FuelMoon, ProcessMode = ProcessModeEnum.Disabled, Visible = false,
            };
            Game.AddChild(zone);
            Mute(zone);
            zone.Physics.Zone.Strength = model.GravityOf(moon, home.Physics.Zone.Strength);
            zone.Physics.Zone.Ceiling = ceiling;
            zone.Built += () =>
            {
                zone.Ground.Ceiling = ceiling;
                LightBodies(zone);
                Mute(zone);
                GD.Print($"{Charts.Tidy(moon.Name)} is made: {moon.Spec}, gravity {zone.Physics.Zone.Strength:F1} m/s²");
            };
            Orbit.AddMoon(moon, zone, ceiling);
            _surfaceShownOf[zone] = false;
            _groundShownOf[zone] = true;
        }
    }

    /// <summary>A parked surface draws and lights nothing: its sky's environment and its light field are off.</summary>
    static void Mute(SurfaceZone zone)
    {
        foreach (WorldEnvironment w in zone.Sky.GetChildren().OfType<WorldEnvironment>()) w.Environment = null;
        if (zone.Light != null) zone.Light.Active = false;
    }

    /// <summary>
    /// In orbit: a moon's surface starts being made as you come within reach of it (and at once, if it is the frame
    /// already), and it finishes arriving while you are out in its reach, ahead of the descent.
    /// </summary>
    void MoonComing()
    {
        Vector3 eye = Game.Camera.GlobalPosition;
        foreach (Celestial c in Orbit.Moons)
            if (!c.Surface.Making && (c == Here || eye.DistanceTo(c.Centre) - c.GlobeRadius < Rules.Space.MakeMoonWithin)) c.Surface.Make();
        SurfaceZone m = Below;
        if (Here.IsHome || m.Arrived) return;
        if (!m.TryArrive()) return;
        GD.Print($"[{Here.Name}'s surface arrived, a piece a frame: {m.ArriveSteps}]");
        Prepare(m);
    }

    /// <summary>
    /// In orbit: the body whose reach you are in takes over as the frame you fly in (nothing moves).
    /// Only well clear of a crossing band, where nothing of a surface is in view.
    /// </summary>
    void HandOverFrames(Vector3 at)
    {
        OrbitZone o = Orbit;
        if (o.Bodies.Count < 2 || _aligned || _band < 1f) return;
        Celestial owner = o.OwnerOf(at);
        if (owner == o.Anchor) return;
        // Leaving a moon's reach takes a little more than coming into it, so the frame never flickers at the edge.
        if (owner.IsHome && at.DistanceTo(o.Anchor.Centre) < o.Anchor.Reach + 300f) return;
        SurfaceZone before = Below;
        StreamAhead(false, default);
        o.TakeOver(owner);
        Took(before);
        GD.Print($"[frame: {owner.Name} takes over]");
    }

    /// <summary>The body below changed: the one you left is hidden as it was, the new one starts hidden, its clock the one clock.</summary>
    void Took(SurfaceZone before)
    {
        SurfaceZone now = Below;
        if (now == before) return;
        now.Sky.Days = before.Sky.Days;
        _sky = null;
        ShowGround(false); // its land waits hidden (its zone is still), ready for the band
    }

    /// <summary>The one clock: every body's sky tells the same days as the one you are under.</summary>
    void KeepOneClock()
    {
        if (Orbit == null) return;
        SurfaceZone s = Below;
        foreach (Celestial c in Orbit.Bodies)
            if (c.Surface is { Sky: not null } other && other != s) other.Sky.Days = s.Sky.Days;
    }

    /// <summary>
    /// The other bodies with ground, drawn by their own globes at their true places where that shows
    /// (the start world always; the moon fading in over the sky's far look of it as you come near),
    /// and nowhere while a surface's sky is the one drawing them (under the top of the band).
    /// </summary>
    void DrawOthers()
    {
        OrbitZone o = Orbit;
        if (o == null || o.Bodies.Count < 2) return;
        Vector3 eye = Game.Camera.GlobalPosition;
        bool space = Game.InOrbit && _band >= 1f;
        foreach (Celestial c in o.Bodies)
        {
            if (c.IsHome && c == Here || c.Globe == null) continue;
            float show = 0f;
            if (space && c.Ready)
            {
                if (c.IsHome) show = 1f;
                else
                {
                    float[] from = Rules.Space.MoonGlobeFrom;
                    float ground = eye.DistanceTo(c.Centre) - c.GlobeRadius;
                    show = Smooth((from[0] - ground) / MathF.Max(from[0] - from[1], 1f));
                }
            }
            if (!c.IsHome) o.Space.Fade(c.Body, space ? 1f - show : 1f);
            if (c == Here) continue; // its own globe is drawn by the seam (DrawWorld), once it is set up
            if (show <= 0f)
            {
                c.Globe.Visible = false;
                continue;
            }
            DrawFar(c, eye, show);
        }
    }

    /// <summary>The moon is drawn this frame, by a surface's sky, the far look of it or its own globe (tests: it never vanishes). Why not, if not.</summary>
    public bool MoonInView(out string why)
    {
        why = null;
        Celestial m = Orbit?.Moon;
        if (m == null)
        {
            why = "there is no moon";
            return false;
        }
        if (!Game.InOrbit || _band < 1f) return true; // a surface's sky draws it, at the same place
        bool globe = m.Globe != null && m.Globe.Visible && !m.Globe.Hidden;
        float fade = Orbit.Space.FadeOf(m.Body);
        if (globe || (Orbit.Space.BodiesShown && fade > 0.002f)) return true;
        why = $"its globe {(globe ? "shown" : "hidden")} ({(m == Here ? "the frame" : "another body")}), its far look at {fade:P0}, space's bodies {(Orbit.Space.BodiesShown ? "shown" : "hidden")}";
        return false;
    }

    /// <summary>A body you are not near, drawn as a whole globe at its true place; its air as much as it shows (<paramref name="shown"/>: the far look's air the rest).</summary>
    void DrawFar(Celestial c, Vector3 eye, float shown)
    {
        N.Vector3 q = c.ToGlobe(eye.ToNumerics());
        float distance = q.Length();
        var (gx, gz) = c.Map.MapPoint(q);
        N.Quaternion frame = N.Quaternion.Normalize(c.Turn * c.Map.Frame(gx, gz));
        Vector3 up = MathX.Rotate(frame, N.Vector3.UnitY).ToGodot();
        Vector3 east = MathX.Rotate(frame, N.Vector3.UnitX).ToGodot();
        Vector3 south = MathX.Rotate(frame, N.Vector3.UnitZ).ToGodot();
        N.Quaternion map = c.Map.Frame(gx, gz);
        float R = c.GlobeRadius;
        float air = c.World != null ? Globe.AirShare(c.World.Sky.Density) : 0f;
        int umbrae = Orbit.Space.Umbrae(Game.Surface.Planet.StarSystem, c.Body, Below.Sky.Days, Orbit.Home.Turn.ToBasis(), _umbra, out float umbraSun);
        c.Globe.Draw(new WorldView
        {
            Umbra = _umbra, UmbraCount = umbrae, UmbraSun = umbraSun,
            Up = up, East = east, South = south,
            MapUp = MathX.Rotate(map, N.Vector3.UnitY).ToGodot(),
            MapEast = MathX.Rotate(map, N.Vector3.UnitX).ToGodot(),
            MapSouth = MathX.Rotate(map, N.Vector3.UnitZ).ToGodot(),
            Nadir = new Vector2((float)gx, (float)gz),
            Height = MathF.Max(distance - R, 2f),
            Bend = 1f / R,
            FlatFrom = 0f,
            Eye = eye,
            RingRadius = R,
            RingShown = 1f,
            Flat = 0f,
            Taper = 1f,
            Local = 3000f,
            Relief = (float)Rules.Globe.ReliefFar,
            CloudBend = 1f / R,
            CloudLift = (float)Rules.Globe.CloudHeight * R,
            OverLand = 1f,

            ToSun = (Orbit.Home.Turn.ToBasis() * Orbit.Space.ToSun).Normalized(),
            FogDensity = 0f,
            FogColor = Colors.Black,
            Rim = air * shown,
            TrueDepth = true,
            TrueCentre = c.Centre,
        });
    }
}
