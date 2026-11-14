using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Skies;
using PlanetSurface.World;
using Slice.Surface;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>
/// The bodies with ground in the orbit zone: the start world and its thruster moon, each where the
/// system has it this moment, from the one model on the one clock (nothing is placed by a crossing).
/// One of them is the anchor everything flies in; the other moves round it, and what is near it is
/// carried along with it, so a ship waiting by the moon stays by the moon.
/// </summary>
public partial class OrbitZone
{
    /// <summary>A body's sizes in the zone, from its map: true scale, as the start world's.</summary>
    void Size(Celestial c, double width, double height, float ceiling)
    {
        c.GlobeRadius = (float)(width / (2 * Math.PI) * Rules.Globe.RadiusScale);
        c.AtmosphereRadius = c.GlobeRadius;
        c.Ceiling = ceiling;
        c.ShellRadius = c.GlobeRadius + ceiling - Rules.Crossing.Gap;
        c.Map = new GlobeMap(width, height, c.GlobeRadius);
        c.Reach = c.IsHome ? float.PositiveInfinity : c.GlobeRadius + ceiling + Rules.Crossing.Band + Rules.Space.TakesOverWithin;
    }

    /// <summary>
    /// Adds a moon of the start world, its surface being made (<paramref name="surface"/>, parked): its
    /// globe is painted from that surface's own map once it is there, and set up a piece a frame.
    /// </summary>
    public Celestial AddMoon(Orbiter body, SurfaceZone surface, float ceiling)
    {
        if (Moon != null) return Moon;
        var c = new Celestial(body, home: false) { Surface = surface, Seen = Home.Seen.SeenFrom(body) };
        Size(c, body.Spec.WidthMeters, body.Spec.HeightMeters, ceiling);
        if (!body.Spec.Airless) c.AtmosphereRadius = c.GlobeRadius * (1f + (float)Rules.Globe.Atmosphere);
        Moon = c;
        Bodies.Add(c);
        if (IsInsideTree()) Hang(c);
        Move();
        return c;
    }

    /// <summary>Its nodes in the zone: the globe under the plain node, so a parked zone doesn't hide it.</summary>
    void Hang(Celestial c)
    {
        if (c.Sphere != null) return;
        c.Sphere = new Node3D { Name = c.Name.Replace(" ", "") + "Sphere" };
        AddChild(c.Sphere);
        Shell(c);
        c.Globe = new Globe { Name = c.Name.Replace(" ", "") + "Globe" };
        _apart.AddChild(c.Globe);
        Placed(c);
    }

    /// <summary>
    /// The body is solid from the first moment, wherever you meet it: a sphere at its true radius, its
    /// sea level, moving with it. Nothing flies through a body whose ground hasn't streamed in (and the
    /// crossing onto it, high over this, is the way down).
    /// </summary>
    static void Shell(Celestial c) => c.Sphere.AddChild(Shell(c.GlobeRadius));

    static StaticBody3D Shell(float radius)
    {
        var shell = new StaticBody3D { Name = "Shell", CollisionLayer = Layers.World, CollisionMask = 0 };
        shell.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = radius } });
        return shell;
    }

    /// <summary>
    /// The bodies whose orbits come within sight of the start world (<see cref="OrbitRules.SpaceRules.CameraFar"/>) with no
    /// ground here: its other moons, the giant it circles and that giant's other moons. Solid at their true radius all the same.
    /// </summary>
    readonly List<(Orbiter Body, Node3D Shell)> _solids = new();
    public IReadOnlyList<(Orbiter Body, Node3D Shell)> OtherSolids => _solids;

    void Solids()
    {
        StarSystem model = Home.Seen;
        Orbiter home = model.Home, host = model.Neighbour;
        foreach (Orbiter b in model.Bodies)
        {
            double closest = b.Parent == home ? b.Orbit : host == null ? double.MaxValue : b == host ? home.Orbit : b.Parent == host ? Math.Abs(b.Orbit - home.Orbit) : double.MaxValue;
            if (closest - b.Radius > Rules.Space.CameraFar || b == home || b == model.FuelMoon || b.Radius <= 0.0) continue;
            StaticBody3D shell = Shell((float)b.Radius);
            AddChild(shell);
            _solids.Add((b, shell));
        }
    }

    Task<GlobePaint> _moonPaint;
    IEnumerator<int> _moonSetup;

    /// <summary>One piece of the moon's globe a frame: painted off the main thread from its own map, then uploaded.</summary>
    void MoonStep()
    {
        Celestial c = Moon;
        if (c == null || c.Ready) return;
        Hang(c);
        if (_moonPaint == null)
        {
            if (c.Surface?.Made is not PlanetWorld w) return;
            c.World = w;
            var rules = Rules.Globe;
            bool clear = w.Sky.Density < SurfaceWeather.NoAir;
            _moonPaint = Task.Run(() => GlobePaint.Make(w.Spec, w.Map, rules, clear ? (_, _) => 0f : null, (x, z) => w.Palette.FarGround(x, z, 64f)));
            return;
        }
        if (!_moonPaint.IsCompleted) return;
        if (_moonPaint.IsFaulted) throw new InvalidOperationException("the moon's globe failed to paint", _moonPaint.Exception);
        _moonSetup ??= c.Globe.Setup(_moonPaint.Result, c.GlobeRadius, c.AtmosphereRadius, Rules.Globe, new Vector2((float)c.Map.Width, (float)c.Map.Height)).GetEnumerator();
        if (!_moonSetup.MoveNext())
        {
            c.Ready = true;
            _moonSetup = null;
        }
    }

    /// <summary>The body whose reach a point is in: a moon near it, otherwise the start world.</summary>
    public Celestial OwnerOf(Vector3 world)
    {
        foreach (Celestial c in Bodies)
            if (!c.IsHome && world.DistanceTo(c.Centre) < c.Reach) return c;
        return Home;
    }

    /// <summary>
    /// Another body takes over as the frame you fly in. Nothing moves: from now on it holds still
    /// and the one before moves round it instead.
    /// </summary>
    public void TakeOver(Celestial c)
    {
        if (c == null || c == Anchor) return;
        Anchor = c;
    }

    /// <summary>
    /// Every body where the system has it this moment, round the anchor; what is near a body that
    /// isn't the anchor goes with it.
    /// </summary>
    public void Move()
    {
        if (Bodies.Count < 2) return;
        double days = Days();
        StarSystem model = Home.Seen;
        Celestial a = Anchor;
        StarSystem.Orientation oa = a.Orient(days);
        D3 pa = model.Position(a.Body, days);
        foreach (Celestial c in Bodies)
        {
            if (c == a) continue;
            StarSystem.Orientation oc = c.Orient(days);
            N.Vector3 pos = G(oa.ToGlobe(model.Position(c.Body, days) - pa));
            N.Quaternion q = FromAxes(G(oa.ToGlobe(oc.X)), G(oa.ToGlobe(oc.Y)), G(oa.ToGlobe(oc.Z)));
            c.TurnWas = c.Turn;
            c.ShiftWas = c.Shift;
            c.Turn = N.Quaternion.Normalize(a.Turn * q);
            c.Shift = a.Shift + N.Vector3.Transform(pos, a.Turn);
            if (!c.Moved)
            {
                c.TurnWas = c.Turn;
                c.ShiftWas = c.Shift;
                c.Moved = true;
            }
            Placed(c);
        }
        foreach (var (b, shell) in _solids) shell.Position = (a.Shift + N.Vector3.Transform(G(oa.ToGlobe(model.Position(b, days) - pa)), a.Turn)).ToGodot();
        if (Physics == null) return;
        foreach (Body b in Physics.Bodies)
        {
            if (b.Frame != null) continue; // riders go with what they ride
            Celestial owner = OwnerOf(b.Position.ToGodot());
            if (owner == a || (owner.Turn == owner.TurnWas && owner.Shift == owner.ShiftWas)) continue;
            N.Quaternion d = N.Quaternion.Normalize(owner.Turn * N.Quaternion.Conjugate(owner.TurnWas));
            b.Position = owner.Shift + N.Vector3.Transform(b.Position - owner.ShiftWas, d);
            b.Rotation = N.Quaternion.Normalize(d * b.Rotation);
            b.Velocity = N.Vector3.Transform(b.Velocity, d);
            b.Up = N.Vector3.Transform(b.Up, d);
            Host.BindingOf(b)?.Teleport();
        }
    }

    static N.Vector3 G(D3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    /// <summary>The rotation taking the zone's axes to these (the images of X, Y and Z).</summary>
    static N.Quaternion FromAxes(N.Vector3 x, N.Vector3 y, N.Vector3 z)
    {
        var m = new N.Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        return N.Quaternion.Normalize(N.Quaternion.CreateFromRotationMatrix(m));
    }
}
