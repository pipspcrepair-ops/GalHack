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
/// or wildlife. It is made off the main thread as soon as a climb out looks likely, enters the scene
/// at the crossing, and stays in memory, as it was left, while you are on the surface: wrecks
/// stay salvaged, claimed and emptied.
///
/// The globe is fixed at the zone's origin: a direction from the centre is a place on the map
/// (<see cref="GlobeMap"/>), so crossing in or out maps a position and a heading exactly.
/// </summary>
public partial class OrbitZone : SurfaceZone
{
    public OrbitRules Rules;

    /// <summary>The world this orbit is round.</summary>
    public PlanetWorld Below { get; private set; }

    /// <summary>The globe's mapping between its directions and the map's points.</summary>
    public GlobeMap Map { get; private set; }

    public float GlobeRadius { get; private set; }

    /// <summary>The top of the atmosphere: crossing under it is the descent.</summary>
    public float AtmosphereRadius { get; private set; }

    public Globe Globe { get; private set; }
    public SpaceSky Space { get; private set; }

    /// <summary>The wrecks set adrift so far (they come in a few a frame once the zone is in the scene).</summary>
    public readonly List<Vessel> Wrecks = new();

    /// <summary>Its paint and its wrecks, made off the main thread; ready once this is done.</summary>
    public bool Prepared => _preparing.IsCompletedSuccessfully;

    /// <summary>Every wreck is in the scene.</summary>
    public bool Settled => Prepared && _spawned && _toSpawn.Count == 0;

    /// <summary>How long making it took off the main thread, ms (the seam hides it).</summary>
    public double PrepareMs { get; private set; }

    /// <summary>The longest any one frame's spawning took, ms (it must not hitch).</summary>
    public double LongestSpawnMs { get; private set; }

    readonly Task<(GlobePaint paint, List<DerelictPlan> wrecks)> _preparing;
    readonly Queue<DerelictPlan> _toSpawn = new();
    bool _spawned, _placed;

    /// <summary>Starts making the orbit round a world, off the main thread. It enters the scene at the first crossing.</summary>
    public OrbitZone(PlanetWorld below, GameData data, OrbitRules rules, ulong systemSeed)
    {
        Name = "Orbit";
        Data = data;
        Rules = rules;
        Below = below;
        Spec = below.Spec;
        GlobeRadius = (float)(below.Width / (2 * Math.PI) * rules.Globe.RadiusScale);
        AtmosphereRadius = GlobeRadius * (1f + (float)rules.Globe.Atmosphere);
        Map = new GlobeMap(below.Width, below.Height, GlobeRadius);
        GlobeMap map = Map;
        float atmosphere = AtmosphereRadius;
        var library = PoiCatalog.Library;
        _preparing = Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            GlobePaint paint = GlobePaint.Make(below.Spec, below.Map, rules.Globe);
            List<DerelictPlan> wrecks = Derelicts.Plan(library, systemSeed, map, atmosphere, rules, data.Catalogue, data.Items);
            PrepareMs = watch.Elapsed.TotalMilliseconds;
            return (paint, wrecks);
        });
    }

    /// <summary>Waits for it to be made (tests and screenshots, which have nothing else to do).</summary>
    public void WaitUntilPrepared() => _preparing.Wait();

    /// <summary>The wreck plans (once prepared): where every wreck floats.</summary>
    public IReadOnlyList<DerelictPlan> Plans => Prepared ? _preparing.Result.wrecks : Array.Empty<DerelictPlan>();

    public override void _Ready()
    {
        ProcessPhysicsPriority = 50;
        if (!Prepared) WaitUntilPrepared(); // only ever entered once ready: this never waits in play
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

        var (paint, wrecks) = _preparing.Result;
        Space = new SpaceSky { Name = "Sky" };
        AddChild(Space);
        Space.Setup(paint.Look, Below.Sky, Rules.Space);
        Globe = new Globe { Name = "Globe" };
        AddChild(Globe);
        Globe.Setup(paint, GlobeRadius, AtmosphereRadius, Rules.Globe);
        foreach (DerelictPlan p in wrecks) _toSpawn.Enqueue(p);
        _spawned = true;
        if (!_placed) Arrive(Vector3.Up, Basis.Identity);
    }

    /// <summary>
    /// The sun and the moons, placed once, at the first crossing: the sun where the surface's sky
    /// had it over the crossing point (so the side you climbed out over is lit as you left it), and
    /// each moon where that sky showed it. <paramref name="frame"/> is the crossing point's local
    /// frame (east, up, south) in the zone.
    /// </summary>
    public void Arrive(Vector3 toSun, Basis frame)
    {
        if (_placed || Space == null) return;
        _placed = true;
        Vector3 sun = (frame * toSun).Normalized();
        Space.Place(sun, frame);
        Globe.SetSun(sun);
    }

    public override void _Process(double delta)
    {
        if (_toSpawn.Count == 0) return;
        // A wreck a frame: building one (its mesh, colliders and cabin) takes a few milliseconds.
        var watch = Stopwatch.StartNew();
        DerelictPlan p = _toSpawn.Dequeue();
        Vessel wreck = Vessel.SpawnDerelict(this, p.Structure, p.Position, p.Rotation);
        wreck.Derelict = p;
        if (!p.Loot.IsEmpty) wreck.Storage.AddAll(p.Loot);
        Wrecks.Add(wreck);
        LongestSpawnMs = Math.Max(LongestSpawnMs, watch.Elapsed.TotalMilliseconds);
    }

    public override void _PhysicsProcess(double delta) { } // no seams to wrap in space

    /// <summary>Spawns every wreck still waiting now (tests and screenshots).</summary>
    public void SpawnAll()
    {
        while (_toSpawn.Count > 0) _Process(0);
    }

    /// <summary>No ground anywhere in space.</summary>
    public override Vector3 GroundAt(float x, float z) => new(x, float.NegativeInfinity, z);

    /// <summary>Height above the top of the atmosphere, m.</summary>
    public float AboveAtmosphere(Vector3 p) => p.Length() - AtmosphereRadius;

    /// <summary>Height above the globe's ground, m.</summary>
    public float AboveGlobe(Vector3 p) => p.Length() - GlobeRadius;

    /// <summary>The map place under a point (map metres, east and south).</summary>
    public (double x, double z) Under(Vector3 p) => Map.MapPoint(new N.Vector3(p.X, p.Y, p.Z));
}
