using System;
using System.Collections.Generic;
using System.IO;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using TerrainDigging.Terrain;
using Weather.Model;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace PhysicsSandbox.World;

/// <summary>
/// Everything the sandbox simulates, wired together through the shared physics module and
/// nothing else: terrain-digging's chunk store (with the sandbox's landscape), building's
/// structures flown by ship-handling's handling model, weather's wind and tornadoes, a lake,
/// crates and a player. The playable scene and the tests both build on this; it has no camera, HUD or input.
///
/// Data is read from where each prototype keeps it (handling.json, building's data folder,
/// weather.json) and the shared module's own physics.json, never copied.
/// </summary>
public partial class SandboxWorld : Node3D
{
    public const int Seed = 1977;

    /// <summary>False in headless tests: collision and physics only.</summary>
    public bool Visuals = true;

    /// <summary>False to start empty (tests that place their own things).</summary>
    public bool Populate = true;

    public PhysicsRules Rules { get; private set; }
    public PhysicsWorld Physics { get; private set; }
    public PhysicsHost Host { get; private set; }
    public Tunables Handling { get; private set; }
    public Catalogue Catalogue { get; private set; }
    public WeatherBridge Weather { get; private set; }
    public ChunkStore Store { get; private set; }
    public TerrainBridge Terrain { get; private set; }
    public TerrainView TerrainView { get; private set; }

    /// <summary>The crates there are, by name (data/crates.json).</summary>
    public IReadOnlyDictionary<string, CrateKind> CrateKinds { get; private set; }

    public readonly List<Ship> Ships = new();
    public readonly List<Crate> Crates = new();
    public readonly List<Creature> Creatures = new();
    public Player Player { get; private set; }

    /// <summary>The named starting vessels (null until populated).</summary>
    public Ship Corsair, Rover;

    readonly Dictionary<Body, Ship> _shipOf = new();

    public static string RepoRoot => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));
    public static string DataPath(params string[] parts) => Path.Combine(RepoRoot, Path.Combine(parts));

    public override void _Ready()
    {
        ProcessPhysicsPriority = 0; // after the physics host (-10): game logic reads where things ended up
        Build();
    }

    void Build()
    {
        Rules = PhysicsRules.FromJson(File.ReadAllText(DataPath("shared", "Physics", "physics.json")));
        Handling = Tunables.FromJson(File.ReadAllText(DataPath("prototypes", "ship-handling", "data", "handling.json")));
        Catalogue = StructureBridge.LoadCatalogue(DataPath("prototypes", "building", "data"),
            ProjectSettings.GlobalizePath("res://data/vessel-parts.json"));
        WeatherData weather = WeatherData.Parse(File.ReadAllText(DataPath("prototypes", "weather", "data", "weather.json")));
        CrateKinds = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CrateKind>>(
            File.ReadAllText(ProjectSettings.GlobalizePath("res://data/crates.json")),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });

        Store = new ChunkStore(new SandboxTerrain(Seed));
        Terrain = new TerrainBridge(Store);
        TerrainView = new TerrainView { Name = "Terrain", Visuals = Visuals };
        AddChild(TerrainView);
        TerrainView.Initialize(Store);
        TerrainView.BuildAll();
        Terrain.Edited += r => TerrainView.Rebuild(r.DirtyChunks);

        Physics = new PhysicsWorld(Rules);
        Physics.Zone.Strength = Handling.Planet.Gravity;
        Physics.Zone.Ceiling = Handling.Planet.AtmosphereCeiling;
        Physics.Terrain = Terrain;
        Physics.Water = new LakeWater((SandboxTerrain)Store.Source);
        Lake.Build(this, Visuals);
        Terrain.World = Physics;
        Physics.StuckFields.Add(new TerrainField(Store) { Ceiling = Physics.Zone.Ceiling });
        Host = new PhysicsHost(Physics);
        AddChild(Host);

        Weather = new WeatherBridge(new WeatherSim(weather, "arid", Seed, WeatherKind.Clouds));
        Weather.Sim.GroundHeight = (x, z) => Store.Source.SurfaceHeight(x, z);
        Physics.ForceProviders.Add(Weather);

        Physics.MotionChanged += body => ShipOf(body)?.BecomeWreck();
        Physics.Unregistered += body => _shipOf.Remove(body);

        if (Populate) SpawnScenario();
    }

    /// <summary>
    /// The sandbox's scene: the Corsair landed on the field with its door open behind it, the
    /// hover rover on its lane short of a dug trench, crates by the field, the player at the
    /// Corsair's tail.
    /// </summary>
    void SpawnScenario()
    {
        Corsair = SpawnLanded("corsair.gbp", new N.Vector2(0f, 0f), 0f);
        Rover = SpawnVessel("sandflea.gbp", Ground(-38f, SandboxTerrain.LaneZ) + N.Vector3.UnitY * 2.4f, -MathF.PI / 2f, landed: false);
        DigTrench(0, SandboxTerrain.LaneZ, halfLength: 7, width: 2, depth: 2);
        for (int i = 0; i < 6; i++)
            Crate.Spawn(this, Ground(13f + (i % 3) * 1.2f, 7f + (i / 3) * 1.3f) + N.Vector3.UnitY * 0.45f, i);
        Player = Player.Spawn(this, Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        Weather.Observer = () => Player.Body.WorldPosition;
    }

    public Player SpawnPlayer(N.Vector3 feet)
    {
        Player = Player.Spawn(this, feet);
        Weather.Observer = () => Player.Body.WorldPosition;
        return Player;
    }

    // ---- Spawning and finding -----------------------------------------------------------------

    public Structure LoadBlueprint(string file) =>
        StructureBridge.LoadBlueprint(ProjectSettings.GlobalizePath($"res://data/blueprints/{file}"), Catalogue);

    public Ship SpawnVessel(string file, N.Vector3 at, float heading, bool landed) => Track(Ship.SpawnVessel(this, LoadBlueprint(file), at, heading, landed));

    /// <summary>A vessel standing on its gear on the ground at a spot, settled and not simulating.</summary>
    public Ship SpawnLanded(string file, N.Vector2 spot, float heading) => SpawnLandedAt(file, Ground(spot.X, spot.Y), heading);

    /// <summary>
    /// A vessel standing on its gear with its feet at a point (the ground, or a deck: on a base or
    /// a capital ship it parks there), settled and not simulating.
    /// </summary>
    public Ship SpawnLandedAt(string file, N.Vector3 footing, float heading)
    {
        Structure s = LoadBlueprint(file);
        float feet = StructureBridge.ShapeOf(s).FeetBelowOrigin;
        float legs = 0f;
        var spec = new StructureVessel(s, Handling);
        foreach (GearSpec g in spec.LandingGear) legs = System.MathF.Max(legs, -g.Position.Y + g.Length);
        N.Vector3 at = footing + N.Vector3.UnitY * (System.MathF.Max(feet, legs) + 0.02f);
        return Track(Ship.SpawnVessel(this, s, at, heading, landed: true));
    }

    /// <summary>A structure in base mode, fixed where it is put, its keel at <paramref name="at"/>'s height.</summary>
    public Ship SpawnBase(string file, N.Vector3 at, float heading)
    {
        Structure s = LoadBlueprint(file);
        float keel = StructureBridge.ShapeOf(s).FeetBelowOrigin;
        return Track(Ship.SpawnBase(this, s, at + N.Vector3.UnitY * keel, heading));
    }

    Ship Track(Ship ship)
    {
        _shipOf[ship.Body] = ship;
        return ship;
    }

    public Ship ShipOf(Body body)
    {
        if (body == null) return null;
        if (_shipOf.TryGetValue(body, out Ship s)) return s;
        foreach (Ship ship in Ships)
            if (ship.Body == body)
            {
                _shipOf[body] = ship;
                return ship;
            }
        return null;
    }

    /// <summary>A point on the natural ground surface.</summary>
    public N.Vector3 Ground(float x, float z) => new(x, Store.Source.SurfaceHeight(x, z), z);

    /// <summary>The top of the actual (dug) ground in a column.</summary>
    public float GroundTop(float x, float z) => Store.TopSolid((int)System.MathF.Floor(x), (int)System.MathF.Floor(z)) + 1f;

    /// <summary>Digs a trench across the rover lane (x across, z along): a run of tool digs through the terrain's API.</summary>
    public EditResult DigTrench(int x, float laneZ, int halfLength, int width, int depth)
    {
        var cells = new List<TInt3>();
        for (int dz = -halfLength; dz <= halfLength; dz++)
            for (int dx = 0; dx < width; dx++)
            {
                int cx = x + dx, cz = (int)System.MathF.Floor(laneZ) + dz;
                int top = Store.TopSolid(cx, cz);
                for (int d = 0; d < depth; d++) cells.Add(new TInt3(cx, top - d, cz));
            }
        return Terrain.DigCells(cells);
    }

    /// <summary>
    /// Takes a node out of the tree (and its body out of the physics space) at once, then frees
    /// it. A plain QueueFree waits for the end of the frame, which can be more than one physics
    /// tick away when frames run slow, and runs must not depend on frame timing.
    /// </summary>
    static void Discard(Node node)
    {
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

    /// <summary>Takes a ship out of the world entirely. Only tests do this: in play nothing disappears.</summary>
    public void Remove(Ship ship)
    {
        if (Player?.Seated == ship) Player.StandUp();
        if (Player?.Body.Frame == ship.Body) Player.Leave();
        Physics.Unregister(ship.Body);
        Physics.StuckFields.Remove(ship.Field);
        Weather.Untrack(ship.Body);
        Ships.Remove(ship);
        Discard(ship.Node);
    }

    /// <summary>Takes a crate out of the world. Only tests do this.</summary>
    public void Remove(Crate crate)
    {
        Physics.Unregister(crate.Body);
        Weather.Untrack(crate.Body);
        Crates.Remove(crate);
        Discard(crate.Node);
    }

    /// <summary>Takes a creature out of the world. Only tests do this.</summary>
    public void Remove(Creature creature)
    {
        Physics.Unregister(creature.Body);
        Weather.Untrack(creature.Body);
        Creatures.Remove(creature);
        Discard(creature.Binding.Node);
    }

    readonly List<(PhysicsBody3D a, PhysicsBody3D b, long until)> _apart = new();

    /// <summary>Two bodies ignore each other for a moment (a section just severed from its hull).</summary>
    public void Separate(PhysicsBody3D a, PhysicsBody3D b, float seconds)
    {
        a.AddCollisionExceptionWith(b);
        b.AddCollisionExceptionWith(a);
        _apart.Add((a, b, Physics.Tick + (long)(seconds * Engine.PhysicsTicksPerSecond)));
    }

    // ---- Every tick ---------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        Player?.AfterPhysics((float)delta);
        foreach (Ship ship in Ships) ship.Gear?.Refresh();
        for (int i = _apart.Count - 1; i >= 0; i--)
        {
            var (a, b, until) = _apart[i];
            if (Physics.Tick < until) continue;
            if (IsInstanceValid(a) && IsInstanceValid(b))
            {
                a.RemoveCollisionExceptionWith(b);
                b.RemoveCollisionExceptionWith(a);
            }
            _apart.RemoveAt(i);
        }
    }
}
