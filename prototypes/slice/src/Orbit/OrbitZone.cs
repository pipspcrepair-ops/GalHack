using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using PlanetSurface.World;
using Slice.Game;
using Slice.Items;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>
/// The space round the start world as its own zone (the design's planet orbit): its own physics
/// world with no weight and no air anywhere (the handling model's space rules: space top speeds),
/// the world below drawn as a globe from its own map, the sun, the stars and the world's moons, and
/// the start system's wrecks near the world, built from the POI generator's data, to board, loot,
/// take apart and claim.
///
/// It is a zone like the surface (the same game layer plays in both: the castaway, vessels,
/// containers, the menu and the visor), with none of the surface's ground: no terrain, sea, weather
/// or wildlife. Its data is made on worker threads as soon as a climb looks likely; it then enters
/// the scene parked (disabled and hidden) and is set up a piece a frame, so it is complete before
/// anyone crosses. It stays in the scene, parked, as it was left while you are on the surface:
/// wrecks stay salvaged, claimed and emptied.
///
/// Directions from the globe's centre are places on the map (<see cref="GlobeMap"/>). One mapping
/// joins the two zones' heights: the globe's ground is the surface's sea level, so a height on the
/// surface is that many metres over the globe's ground.
///
/// <para><b>The frame.</b> The surface can't be moved or turned (its terrain and water are drawn
/// from world coordinates), so the orbit is laid out to fit it: its bodies live in the same world
/// coordinates as the surface, with the globe set under the crossing point, tangent to sea level
/// there, its local east, up and south along the world's axes (<see cref="Align"/>). So a crossing
/// changes no number at all: the vessel keeps its position, rotation and velocity, the surface keeps
/// its sky and sun, and the globe lies hidden under the ground. Aligning somewhere else moves
/// everything in the zone rigidly, the sky too, which changes nothing you can see.</para>
/// </summary>
public partial class OrbitZone : SurfaceZone
{
    public OrbitRules Rules;

    /// <summary>The world this orbit is round.</summary>
    public PlanetWorld Below { get; private set; }

    /// <summary>What every other body looks like from afar: the surface's sky paints them, and both skies show them.</summary>
    public PlanetSurface.Skies.BodySurfaces Surfaces { get; init; }

    /// <summary>The start world as a body of this zone: its globe, its frame, its surface.</summary>
    public Celestial Home { get; }

    /// <summary>The start world's moon within reach of thrusters, once it is being made (null till then).</summary>
    public Celestial Moon { get; private set; }

    /// <summary>The body whose frame everything flies in: it holds still, the others move round it.</summary>
    public Celestial Anchor { get; private set; }

    /// <summary>Every body with ground in the zone, the start world first.</summary>
    public readonly List<Celestial> Bodies = new();

    /// <summary>The start world's globe's mapping between its directions and the map's points.</summary>
    public GlobeMap Map => Home.Map;

    public float GlobeRadius => Home.GlobeRadius;

    /// <summary>The rim of air drawn round the globe.</summary>
    public float AtmosphereRadius => Home.AtmosphereRadius;

    /// <summary>Crossing under this radius is the descent.</summary>
    public float ShellRadius => Home.ShellRadius;

    /// <summary>The surface's atmosphere ceiling: a climb crosses here. The shell is this, less the gap, over the globe's ground.</summary>
    public float Ceiling { get; private set; }
    public float Gap { get; private set; }

    public Globe Globe => Home.Globe;
    public SpaceSky Space { get; private set; }

    /// <summary>The start world's globe's place in the world: world = Turn × globe + Shift.</summary>
    public N.Quaternion Turn => Home.Turn;
    public N.Vector3 Shift => Home.Shift;

    public N.Vector3 ToWorld(N.Vector3 globe) => Home.ToWorld(globe);
    public N.Vector3 ToGlobe(N.Vector3 world) => Home.ToGlobe(world);

    /// <summary>The clock (days on the system's one clock, the surface's sky's): set by whoever runs the zone.</summary>
    public Func<double> Days = () => 0.0;

    /// <summary>The wrecks set adrift so far.</summary>
    public readonly List<Vessel> Wrecks = new();

    /// <summary>Its paint and its wrecks are made (worker threads); it can enter the scene.</summary>
    public bool Prepared => _preparing.IsCompletedSuccessfully;

    /// <summary>Complete, in the scene, ready for anyone to cross into.</summary>
    public bool Ready { get; private set; }

    /// <summary>How long making it took off the main thread, ms.</summary>
    public double PrepareMs { get; private set; }

    /// <summary>The longest any one frame's setting-up took, ms (it must not show).</summary>
    public double LongestBuildMs { get; private set; }

    readonly Task<(GlobePaint paint, List<DerelictPlan> wrecks)> _preparing;
    IEnumerator<int> _building;

    /// <summary>Starts making the orbit round a world, on worker threads. It enters the scene when that's done.</summary>
    public OrbitZone(PlanetWorld below, GameData data, OrbitRules rules, ulong systemSeed, float ceiling, Func<float, float, float> cloudCoverage = null)
    {
        Name = "Orbit";
        Data = data;
        Rules = rules;
        Below = below;
        Spec = below.Spec;
        Ceiling = ceiling;
        Gap = rules.Crossing.Gap;
        PlanetSurface.Skies.StarSystem model = below.StarSystem;
        Home = new Celestial(model.Home, home: true) { World = below, Seen = model };
        Size(Home, below, ceiling);
        Home.AtmosphereRadius = Home.GlobeRadius * (1f + (float)rules.Globe.Atmosphere);
        Bodies.Add(Home);
        Anchor = Home;
        GlobeMap map = Map;
        float shell = ShellRadius;
        var library = PoiCatalog.Library;
        _preparing = Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            GlobePaint paint = GlobePaint.Make(below.Spec, below.Map, rules.Globe, cloudCoverage, (x, z) => below.Palette.FarGround(x, z, 64f));
            List<DerelictPlan> wrecks = Derelicts.Plan(library, systemSeed, map, shell, rules, data.Catalogue, data.Items);
            PrepareMs = watch.Elapsed.TotalMilliseconds;
            return (paint, wrecks);
        });
    }

    /// <summary>Waits for its data (tests and screenshots, which have nothing else to do).</summary>
    public void WaitUntilPrepared() => _preparing.Wait();

    /// <summary>The globe's paint (once prepared): its look is the sky's look too.</summary>
    public GlobePaint Paint => Prepared ? _preparing.Result.paint : null;


    /// <summary>The wreck plans (once prepared): where every wreck floats.</summary>
    public IReadOnlyList<DerelictPlan> Plans => Prepared ? _preparing.Result.wrecks : Array.Empty<DerelictPlan>();

    public override void _Ready()
    {
        ProcessPhysicsPriority = 50;
        if (_preparing.IsFaulted) throw new InvalidOperationException("the orbit failed to build", _preparing.Exception);
        Physics = new PhysicsWorld(Data.Physics);
        // No weight anywhere and never in the atmosphere: the handling model flies by its space rules.
        Physics.Zone.Strength = 0f;
        Physics.Zone.Ceiling = float.NegativeInfinity;
        Host = new PhysicsHost(Physics);
        AddChild(Host);
        Physics.MotionChanged += body => VesselOf(body)?.BecomeWreck();
        Sfx = new PlanetSurface.Fx.Sfx { Name = "Sfx" };
        AddChild(Sfx);
        // Space has no weather and (for now) no creatures: empty, so the game layer runs unchanged.
        Weather = new SurfaceWeather { Name = "Weather", Zone = this };
        AddChild(Weather);
        Wildlife = new WildlifePatch { Name = "Wildlife", Zone = this };
        AddChild(Wildlife);
        Loot = new Lootables { Name = "Loot", Items = Data.Items };
        AddChild(Loot);
        // The globe and the moons stand in the globe's frame; the sky turns with it.
        Home.Sphere = new Node3D { Name = "Sphere" };
        AddChild(Home.Sphere);
        Space = new SpaceSky { Name = "Sky" };
        AddChild(Space);
        // Under a plain node, so a parked orbit's hidden zone doesn't hide it: it is the far land from the surface too.
        _apart = new Node { Name = "Apart" };
        AddChild(_apart);
        Home.Globe = new Globe { Name = "Globe" };
        _apart.AddChild(Home.Globe);
        _building = Build().GetEnumerator();
    }

    /// <summary>One piece of setting up, called once a frame while it waits parked: a texture, a wreck.</summary>
    public void BuildStep()
    {
        if (Ready) MoonStep();
        if (Ready || _building == null) return;
        var watch = Stopwatch.StartNew();
        if (!_building.MoveNext())
        {
            Ready = true;
            _building = null;
        }
        LongestBuildMs = Math.Max(LongestBuildMs, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Finishes setting up now (tests and screenshots).</summary>
    public void BuildAll()
    {
        while (!Ready) BuildStep();
    }

    IEnumerable<int> Build()
    {
        var (paint, wrecks) = _preparing.Result;
        Space.Setup(paint.Look, Below.Sky, Below.StarSystem, Rules.Space, Surfaces);
        yield return 0;
        foreach (int x in Globe.Setup(paint, GlobeRadius, AtmosphereRadius, Rules.Globe, new Vector2((float)Below.Width, (float)Below.Height))) yield return x;
        Home.Ready = true;
        foreach (DerelictPlan p in wrecks)
        {
            yield return 0;
            Vessel wreck = Vessel.SpawnDerelict(this, p.Structure, ToWorld(p.Position), N.Quaternion.Normalize(Turn * p.Rotation));
            wreck.Derelict = p;
            if (!p.Loot.IsEmpty)
            {
                GalHack.Items.Bill left = wreck.Storage.AddAll(p.Loot);
                if (!left.IsEmpty) Loot.Spill(left, ToWorld(p.Position).ToGodot());
            }
            Wrecks.Add(wreck);
        }
    }

    Node _apart;

    /// <summary>
    /// Lays the zone out so the start world's map point (x, z) lies under the world point (sx, sz):
    /// the globe's ground tangent to sea level there, its local east, up and south along the world's
    /// axes. Everything in the zone moves with it rigidly, so nothing changes to the eye.
    /// </summary>
    public void Align(double x, double z, float sx, float sz) => Align(Home, x, z, sx, sz);

    /// <summary>The same over any body's map: it becomes the anchor, laid out under the world point.</summary>
    public void Align(Celestial c, double x, double z, float sx, float sz)
    {
        Anchor = c;
        N.Quaternion turn = N.Quaternion.Conjugate(c.Map.Frame(x, z));
        var shift = new N.Vector3(sx, -c.GlobeRadius, sz);
        N.Quaternion d = N.Quaternion.Normalize(turn * N.Quaternion.Conjugate(c.Turn));
        N.Vector3 Moved(N.Vector3 w) => shift + N.Vector3.Transform(c.ToGlobe(w), turn);
        foreach (Body b in Physics.Bodies)
        {
            if (b.Frame != null) continue; // riders stay where they are aboard
            b.Position = Moved(b.Position);
            b.Rotation = N.Quaternion.Normalize(d * b.Rotation);
            b.Velocity = N.Vector3.Transform(b.Velocity, d);
            b.AngularVelocity = N.Vector3.Transform(b.AngularVelocity, d);
            b.Up = N.Vector3.Transform(b.Up, d);
            BodyBinding binding = Host.BindingOf(b);
            binding?.Teleport();
            binding?.Node.ResetPhysicsInterpolation();
        }
        foreach (Lootable l in Loot.All)
        {
            l.Position = Moved(l.Position.ToNumerics()).ToGodot();
            if (l.Node != null) l.Node.GlobalPosition = l.Position;
        }
        foreach (Celestial o in Bodies)
        {
            if (o == c) continue;
            o.Shift = Moved(o.Shift);
            o.Turn = N.Quaternion.Normalize(d * o.Turn);
            o.ShiftWas = o.Shift;
            o.TurnWas = o.Turn;
            Placed(o);
        }
        c.Turn = turn;
        c.Shift = shift;
        c.TurnWas = turn;
        c.ShiftWas = shift;
        Placed(c);
    }

    /// <summary>A body's globe-framed nodes follow it.</summary>
    static void Placed(Celestial c)
    {
        if (c.Sphere == null) return;
        c.Sphere.Transform = new Transform3D(c.Turn.ToBasis(), c.Shift.ToGodot());
        c.Globe?.PlaceRing(c.Sphere.Transform);
    }

    public override void _Process(double delta) { }

    public override void _PhysicsProcess(double delta) => Move(); // no seams to wrap in space: the bodies move instead

    /// <summary>No ground anywhere in space.</summary>
    public override Vector3 GroundAt(float x, float z) => new(x, float.NegativeInfinity, z);

    /// <summary>Height above the shell, m, in the sphere's own terms.</summary>
    public float AboveShell(Vector3 world) => ToGlobe(world.ToNumerics()).Length() - ShellRadius;

    /// <summary>The height on the surface a radius here stands for (the globe's ground is sea level).</summary>
    public float SurfaceHeight(float radius) => radius - GlobeRadius;

    /// <summary>The radius here a height on the surface stands for.</summary>
    public float RadiusFor(float height) => GlobeRadius + height;

    /// <summary>The map place under a world point (map metres, east and south).</summary>
    public (double x, double z) Under(Vector3 world) => Map.MapPoint(ToGlobe(world.ToNumerics()));
}
