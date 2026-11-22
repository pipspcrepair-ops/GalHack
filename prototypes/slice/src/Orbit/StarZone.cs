using System;
using System.Threading.Tasks;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Stars;
using Godot;
using PlanetSurface.Skies;
using Slice.Game;
using Slice.Items;
using Slice.Surface;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>
/// The space round a system's star: where a fold arrives (docs/solar-systems.md section 8), at the star's high orbit, as
/// close as you come, the star filling the view. Its own physics world with no weight and no air; the star solid at its
/// radius; every world of the system where the one clock has it, drawn by the same space sky as a world's orbit
/// (<see cref="SpaceSky"/>) from the same model (<see cref="StarSystem"/>), with the worlds' own far looks
/// (<see cref="BodySurfaces"/>); and the galaxy as it stands from this star (<see cref="GalaxySky"/>), so the star you
/// came from is a point at its true bearing.
///
/// <para><b>Frames.</b> Its world's axes are the system's own, fixed to the stars, and its origin is where it was
/// arrived at (<see cref="Origin"/>, in the system's frame), so every number near where you fly stays small. The star
/// is the model's home here (<see cref="StarSystem.SeenFrom"/>): the sky turns its globe's frame back into the
/// system's each frame, so nothing turns with the star's own spin.</para>
///
/// <para>It is made in two halves, as an orbit is: the sky's bake and the far looks' paint on worker threads from the
/// moment a fold spools, then set up a piece a frame while it waits parked out of the scene; and it is freed whole
/// (<see cref="Release"/>) when you fold on.</para>
/// </summary>
public partial class StarZone : SurfaceZone
{
    public readonly OrbitRules Rules;

    /// <summary>The star in the galaxy's catalogue.</summary>
    public readonly int StarId;

    /// <summary>The system, as its anchor world has it (the start world's own at the start's star).</summary>
    public readonly StarSystem System;

    /// <summary>The same system with the star as its home: the frames this zone draws in.</summary>
    public readonly StarSystem Seen;

    /// <summary>The galaxy as it stands from this star: its own sky, baked for it.</summary>
    public readonly GalaxySky Stars;

    /// <summary>Where it was arrived at, in the system's frame, m: the zone's origin.</summary>
    public readonly D3 Origin;

    /// <summary>The one clock, days: carried on from the system left.</summary>
    public double Days;

    public SpaceSky Space { get; private set; }

    /// <summary>The worlds' far looks, painted from their own specs on a thread of their own.</summary>
    public BodySurfaces Surfaces { get; private set; }

    /// <summary>Everything is set up and drawn: it can be crossed into.</summary>
    public bool Ready { get; private set; }

    /// <summary>The sky the star's light is drawn from (its colour, its strength).</summary>
    readonly SkySystem _light;

    readonly Galaxy _galaxy;
    System.Collections.Generic.IEnumerator<int> _building;
    StaticBody3D _shell;

    /// <param name="from">The star a fold comes from (its side of this one is where it arrives), or -1 for the system's plane.</param>
    public StarZone(GameData data, OrbitRules rules, Galaxy galaxy, int star, StarSystem system, SkySystem light, ulong frameSeed, int from, double days)
    {
        Name = $"Star{star}";
        Data = data;
        Rules = rules;
        StarId = star;
        System = system;
        Seen = system.SeenFrom(system.Star);
        _light = light;
        _galaxy = galaxy;
        Days = days;
        Stars = new GalaxySky(galaxy, star, frameSeed); // bakes on a worker from now
        Origin = Seen.Position(system.Star, days) + ArrivalSide(galaxy, star, from, frameSeed) * HighOrbit;
        Spec = system.Home.Spec;
    }

    /// <summary>The star's high orbit, from its centre, m.</summary>
    public double HighOrbit => System.Star.Radius * Rules.Fold.HighOrbit;

    /// <summary>
    /// The side a fold arrives on: toward the star it came from, in the system's frame (the catalogue's directions turned by
    /// the system's tilt against the galaxy, <see cref="SkyFrame.Tilt"/>); from nowhere, out along the system's plane.
    /// </summary>
    public static D3 ArrivalSide(Galaxy galaxy, int star, int from, ulong frameSeed)
    {
        if (from < 0 || from == star) return new D3(1, 0, 0);
        N.Vector3 along = N.Vector3.Normalize(galaxy.Stars[from].Position - galaxy.Stars[star].Position);
        N.Vector3 s = N.Vector3.Transform(along, N.Quaternion.Conjugate(SkyFrame.Tilt(frameSeed)));
        return new D3(s.X, s.Y, s.Z).Normalized;
    }

    /// <summary>A point of the system's frame in this zone's world.</summary>
    public Vector3 ToWorld(D3 system) => V(system - Origin);

    /// <summary>A world point in the system's frame.</summary>
    public D3 ToSystem(Vector3 world) => Origin + new D3(world.X, world.Y, world.Z);

    /// <summary>The star's centre in the world.</summary>
    public Vector3 StarCentre => ToWorld(Seen.Position(System.Star, Days));

    /// <summary>How far over the star's high orbit a point stands, m (under 0: closer in than you come).</summary>
    public float AboveHighOrbit(Vector3 world) => (float)((ToSystem(world) - Seen.Position(System.Star, Days)).Length - HighOrbit);

    public override void _Ready()
    {
        ProcessPhysicsPriority = 50;
        Physics = new PhysicsWorld(Data.Physics);
        // No weight anywhere and no air: the handling model flies by its space rules, as in a world's orbit.
        Physics.Zone.Strength = 0f;
        Physics.Zone.Ceiling = float.NegativeInfinity;
        Physics.Zone.Above = p => AboveHighOrbit(p.ToGodot());
        Host = new PhysicsHost(Physics);
        AddChild(Host);
        Physics.MotionChanged += body => VesselOf(body)?.BecomeWreck();
        Sfx = new PlanetSurface.Fx.Sfx { Name = "Sfx" };
        AddChild(Sfx);
        // No weather and no creatures out here: empty, so the game layer runs unchanged.
        Weather = new SurfaceWeather { Name = "Weather", Zone = this };
        AddChild(Weather);
        Wildlife = new WildlifePatch { Name = "Wildlife", Zone = this };
        AddChild(Wildlife);
        Loot = new Lootables { Name = "Loot", Items = Data.Items };
        AddChild(Loot);
        Space = new SpaceSky { Name = "Sky" };
        AddChild(Space);
        // The star can't be entered: solid at its radius (the push back out from its high orbit is section 8 item 15's later job).
        _shell = new StaticBody3D { Name = "Star", CollisionLayer = Layers.World, CollisionMask = 0 };
        _shell.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = (float)System.Star.Radius } });
        AddChild(_shell);
        _building = Build().GetEnumerator();
    }

    System.Collections.Generic.IEnumerable<int> Build()
    {
        // The far looks from where it is arrived at, biggest first (a headless run paints none).
        StarSystem.Orientation o = Seen.Orient(Days);
        Surfaces = new BodySurfaces(Seen, o.ToGlobe(Origin - Seen.Position(System.Star, Days)));
        yield return 0;
        Space.Setup(null, _light, Seen, Rules.Space, Surfaces, (float)Rules.Globe.Atmosphere);
        yield return 0;
        _shell.Position = StarCentre;
        Space.ShowBodies(true);
    }

    /// <summary>One piece of setting up, once a frame while it waits parked (and its sky's upload a texture a frame).</summary>
    public void BuildStep()
    {
        Stars.Poll();
        if (Ready || _building == null) return;
        if (_building.MoveNext()) return;
        _building = null;
        Ready = Stars.Ready;
        if (!Ready) _building = Waiting().GetEnumerator();
    }

    System.Collections.Generic.IEnumerable<int> Waiting()
    {
        while (!Stars.Ready) yield return 0;
    }

    /// <summary>Finishes setting up now, sky and all (tests, and what has nothing else to do).</summary>
    public void BuildAll()
    {
        while (!Ready)
        {
            Stars.Finish();
            BuildStep();
        }
    }

    /// <summary>The space sky on and lit, or off and dark: only one environment is ever active.</summary>
    public void Show(bool shown)
    {
        Space.Show(shown, shown ? 1f : 0f);
        Space.Light(shown ? 1f : 0f);
        Space.ShowBodies(shown);
    }

    /// <summary>The clock runs on, and the sky is drawn from the eye where everything stands this moment.</summary>
    public void Track(Vector3 eye, double delta)
    {
        Days += delta / (_light.DayMinutes * 60.0);
        Stars.Poll();
        Space.Galaxy(Stars);
        Surfaces?.Pump();
        StarSystem.Orientation o = Seen.Orient(Days);
        // The star's globe axes in the system's frame are the world's: the sky turns the spin back out.
        var turn = new Basis(V(o.X), V(o.Y), V(o.Z));
        D3 eyeGlobe = o.ToGlobe(ToSystem(eye) - Seen.Position(System.Star, Days));
        Space.Track(Seen, Days, turn, eyeGlobe, eye);
    }

    /// <summary>The direction to look, in this zone's world, to see a direction of the catalogue.</summary>
    public Vector3 LookAlong(N.Vector3 catalogue) => Stars.Field.Inverse() * new Vector3(catalogue.X, catalogue.Y, catalogue.Z);

    /// <summary>
    /// Lets go of everything that isn't a node now, so a fold away leaves nothing behind: the sky's textures and its
    /// worker's result, the far looks' array and their thread. Its nodes go with <see cref="Node.QueueFree"/>.
    /// </summary>
    public void Release()
    {
        Stars.Release();
        Surfaces?.Release();
        Surfaces = null;
    }

    public override void _Process(double delta) { }

    public override void _PhysicsProcess(double delta) { }

    /// <summary>No ground anywhere in space.</summary>
    public override Vector3 GroundAt(float x, float z) => new(x, float.NegativeInfinity, z);

    static Vector3 V(D3 d) => new((float)d.X, (float)d.Y, (float)d.Z);
}
