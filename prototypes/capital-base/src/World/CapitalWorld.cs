using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CapitalBase.Core;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using GalHack.Terrain;
using Godot;
using PhysicsSandbox.Bridges;
using Planets.Gen;
using ShipHandling.Handling;
using TerrainDigging.Terrain;
using Weather.Model;
using N = System.Numerics;
using PlanetWorld = PlanetSurface.World.PlanetWorld;
using SInt3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace CapitalBase.World;

/// <summary>
/// Everything this prototype simulates, wired through the shared modules and nothing else: a
/// planet from the planet generator as diggable voxel terrain (shared/Terrain, drawn by
/// planet-surface's meshing), building's structures as bodies in shared/Physics flown by
/// ship-handling's handling model, weather's wind, and the rules that are new here: the core
/// console (base and capital mode, lift-off), building into the ground, parking on a host,
/// auto-transmit to Home, and a ledger of every item and block.
///
/// The playable scene, the tests and the screenshots all build on this; it has no camera, HUD or input.
/// </summary>
public partial class CapitalWorld : Node3D
{
    /// <summary>False in headless tests: colliders and physics only.</summary>
    public bool Visuals = true;

    public CapitalRules Rules { get; private set; }
    public PhysicsRules PhysicsRules { get; private set; }
    public Tunables Handling { get; private set; }
    public Catalogue Catalogue { get; private set; }
    public PlanetWorld Planet { get; private set; }
    public TerrainPatch Patch { get; private set; }
    public Ground Ground { get; private set; }
    public PhysicsWorld Physics { get; private set; }
    public PhysicsHost Host { get; private set; }
    public WeatherBridge Weather { get; private set; }
    public Debris Debris { get; private set; }

    public readonly List<Craft> Crafts = new();
    public readonly HomeFlag Home = new();

    /// <summary>The player's materials (building's inventory): creative places for free, survival pays.</summary>
    public readonly Inventory Inventory = new() { Creative = true };

    /// <summary>Landing spots found on the terrain near the landing site, best first.</summary>
    public List<Spot> Spots { get; private set; } = new();
    public N.Vector2 SiteCentre { get; private set; }
    public double PlanetBuildMs { get; private set; }

    /// <summary>The console's messages, oldest first.</summary>
    public readonly List<string> Log = new();
    public event Action<string> Said;

    /// <summary>Items and blocks created (positive) or destroyed (negative) on purpose since the start.</summary>
    public int DeclaredItems { get; private set; }
    public int DeclaredBlocks { get; private set; }
    public event Action<int, int> Declared;

    readonly Dictionary<Craft, LiftOff> _liftoffs = new();
    readonly List<Craft> _landed = new();
    readonly List<(Craft a, Craft b, long until)> _apart = new();
    float _transmitTimer;

    /// <summary>The voxel patch's per-frame rebuild budget in play, ms.</summary>
    public double RebuildBudgetMs = 4.0;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 0; // after the physics host (-10): react to where things ended up
        Build();
    }

    // ==== Setup ===================================================================================

    void Build()
    {
        Rules = CapitalRules.FromJson(File.ReadAllText(Paths.Own("data", "capital-base.json")));
        PhysicsRules = PhysicsRules.FromJson(File.ReadAllText(Paths.Repo("shared", "Physics", "physics.json")));
        Handling = Tunables.FromJson(File.ReadAllText(Paths.Repo("prototypes", "ship-handling", "data", "handling.json")));
        Catalogue = StructureBridge.LoadCatalogue(Paths.Repo("prototypes", "building", "data"),
            Paths.Repo("prototypes", "physics-sandbox", "data", "vessel-parts.json"));
        WeatherData weather = WeatherData.Parse(File.ReadAllText(Paths.Repo("prototypes", "weather", "data", "weather.json")));

        // The planet, built the way planet-surface builds it (no POIs here: nothing anchors the ground).
        PlanetTerrain.ConfigureWorldBounds();
        CapitalRules.PlanetData p = Rules.Planet;
        var spec = PlanetSpec.Roll(p.Seed, Enum.Parse<Planets.Gen.Archetype>(p.Archetype), p.Variant, p.WidthKm);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Planet = PlanetWorld.Build(spec, null);
        PlanetBuildMs = watch.Elapsed.TotalMilliseconds;
        RenderingServer.GlobalShaderParameterSet("planet_size", new Vector2((float)Planet.Width, (float)Planet.Height));

        // Near the landing site (always by water), the nearest dry, gentle stretch with room for
        // four landings; the square of voxel chunks is centred on it.
        int s = ChunkStore.Size;
        var landing = new N.Vector2((float)Planet.Landing.X, (float)Planet.Landing.Y);
        (N.Vector2 area, List<Spot> spots) = Sites.FindArea(Planet.Source, landing, (p.PatchColumns - 6) * s, CapitalFeet, 4, 44f, 9f * Mathf.Pi / 180f);
        Spots = spots;
        int cx = (int)Math.Floor(area.X / s), cz = (int)Math.Floor(area.Y / s);
        SiteCentre = new N.Vector2(cx * s + s * 0.5f, cz * s + s * 0.5f);
        Patch = new TerrainPatch { Name = "Ground", Visuals = Visuals };
        AddChild(Patch);
        Patch.Build(Planet, cx, cz, p.PatchColumns);
        Ground = new Ground(Planet.Terrain, Patch);

        // Physics: the planet's gravity, the atmosphere 600 m over the site, craters through the ground's edit API.
        Physics = new PhysicsWorld(PhysicsRules);
        Physics.Zone.Strength = Handling.Planet.Gravity;
        Physics.Zone.Ceiling = GroundHeight(SiteCentre.X, SiteCentre.Y) + Handling.Planet.AtmosphereCeiling;
        Physics.Terrain = Ground;
        Ground.World = Physics;
        Physics.StuckFields.Add(new GroundField(Ground.Store, Physics.Zone.Ceiling, b => CraftOf(b)?.Pin != null));
        Host = new PhysicsHost(Physics);
        AddChild(Host);

        Weather = new WeatherBridge(new WeatherSim(weather, spec.Archetype.ToString().ToLowerInvariant(), spec.Seed, WeatherKind.Clear));
        Weather.Sim.GroundHeight = (x, z) => Planet.Source.SurfaceHeight(x, z);
        Weather.Observer = () => Crafts.Count > 0 ? Crafts[0].Body.Position : new N.Vector3(SiteCentre.X, 0, SiteCentre.Y);
        Physics.ForceProviders.Add(Weather);

        Physics.Landed += body => { if (CraftOf(body) is Craft c) _landed.Add(c); };

        Debris = new Debris { Name = "Debris", Visible = Visuals };
        AddChild(Debris);
    }

    /// <summary>Bastion 7's six legs, relative to its core (x, z): where its feet stand.</summary>
    static readonly N.Vector2[] CapitalFeet =
    {
        new(-8f, -14f), new(8f, -14f), new(-8f, 0f), new(8f, 0f), new(-8f, 14f), new(8f, 14f),
    };

    public float GroundHeight(float x, float z) => Planet.Source.SurfaceHeight(x, z);

    /// <summary>The top of the actual (dug) ground in a column, by the terrain's own cells.</summary>
    public float GroundTop(float x, float z)
    {
        int fx = (int)MathF.Floor(x), fz = (int)MathF.Floor(z);
        int y = (int)MathF.Floor(GroundHeight(x, z)) + 6;
        while (y > -1000 && !Ground.Store.IsSolid(new TInt3(fx, y, fz))) y--;
        return y + 1f;
    }

    // ==== Crafts =================================================================================

    public Craft CraftOf(Body body)
    {
        foreach (Craft c in Crafts) if (c.Body == body) return c;
        return null;
    }

    public Craft CraftById(string id) => Crafts.FirstOrDefault(c => c.Id == id);

    public Structure LoadBlueprint(string file)
    {
        string path = File.Exists(Paths.Own("data", "blueprints", file)) ? Paths.Own("data", "blueprints", file) : Paths.Repo(file);
        return StructureBridge.LoadBlueprint(path, Catalogue);
    }

    /// <summary>A structure from blueprint text (a save), as a craft at a pose. Nothing is declared: it already existed.</summary>
    public Craft Adopt(string id, Structure s, N.Vector3 position, N.Quaternion rotation)
    {
        var craft = new Craft(this, id, s, position, rotation);
        Crafts.Add(craft);
        UpdateWind(craft);
        return craft;
    }

    /// <summary>A new craft from a blueprint file: its blocks come into the world, and the ledger hears about them.</summary>
    public Craft Spawn(string id, string file, N.Vector3 position, N.Quaternion rotation)
    {
        Structure s = LoadBlueprint(file);
        Declare(0, s.Count);
        return Adopt(id, s, position, rotation);
    }

    /// <summary>A craft flying at a point, at rest.</summary>
    public Craft SpawnFlying(string id, string file, N.Vector3 at, float heading)
    {
        Craft c = Spawn(id, file, at, MathX.Yaw(heading));
        c.PlaceFlying(at, MathX.Yaw(heading), N.Vector3.Zero);
        return c;
    }

    /// <summary>
    /// A base set into the ground at a spot: its floor level with the ground's mean height under it,
    /// anything below that built into the ground (the terrain inside those blocks is cleared).
    /// </summary>
    public Craft SpawnBase(string id, string file, N.Vector2 at, float heading)
    {
        Structure s = LoadBlueprint(file);
        float half = s.Grid.CellSize * 0.5f;
        float mean = 0f;
        int n = 0;
        var (min, max) = s.Bounds();
        for (int x = min.X; x <= max.X; x++)
        for (int z = min.Z; z <= max.Z; z++)
        {
            mean += GroundHeight(at.X + x * s.Grid.CellSize, at.Y + z * s.Grid.CellSize);
            n++;
        }
        mean /= n;
        Declare(0, s.Count);
        Craft c = Adopt(id, s, new N.Vector3(at.X, mean + half, at.Y), MathX.Yaw(heading));
        c.Anchor();
        ClearGroundInside(c, c.Structure.Blocks().Select(b => b.pos));
        UpdateWind(c);
        return c;
    }

    /// <summary>Parks a vessel on a host at a point in the host's frame (the pad), already settled.</summary>
    public void DockAt(Craft vessel, Craft host, N.Vector3 local, float heading)
    {
        N.Vector3 feet = new(0f, vessel.Spec.LandingGear.Count == 0 ? 0f : vessel.Spec.LandingGear.Min(g => g.Position.Y - g.Length), 0f);
        N.Vector3 at = host.Body.ToParent(local - feet);
        N.Quaternion rotation = N.Quaternion.Normalize(host.Body.Rotation * MathX.Yaw(heading));
        vessel.PlaceLanded(at, rotation);
        Dock(vessel, host);
    }

    // ==== The ledger =============================================================================

    /// <summary>Items or blocks created (positive) or destroyed (negative) on purpose. Anything else must add up.</summary>
    public void Declare(int items, int blocks)
    {
        DeclaredItems += items;
        DeclaredBlocks += blocks;
        Declared?.Invoke(items, blocks);
    }

    /// <summary>Every item and block in the world right now, counted where they are.</summary>
    public (int Items, int Blocks) Count()
    {
        int items = Inventory.Total, blocks = 0;
        foreach (Craft c in Crafts)
        {
            items += c.Cargo.Total;
            blocks += c.Structure.Count;
        }
        return (items, blocks);
    }

    public void Say(string message)
    {
        Log.Add(message);
        if (Log.Count > 200) Log.RemoveAt(0);
        Said?.Invoke(message);
    }

    // ==== The core console ========================================================================

    /// <summary>
    /// Switches a large core between base and capital mode. One field changes; no block, item or
    /// pose does. Base mode needs the ship down (landed on its gear or its own blocks): it is then
    /// anchored where it stands. Capital mode lets it fly again; if it is keyed into the ground it
    /// stays held until lift-off cuts it free.
    /// </summary>
    public string SwitchMode(Craft c, CoreMode mode)
    {
        if (!c.IsLarge) return Reply("A small-grid core is always a vessel.");
        if (c.Structure.Mode == mode) return Reply($"{c.Name} is already in {Label(mode)} mode.");
        if (_liftoffs.ContainsKey(c)) return Reply("Lift-off in progress.");

        if (mode == CoreMode.Base)
        {
            if (c.State == CraftState.Flying)
                return Reply($"BASE MODE REFUSED: {c.Name} is flying. Land first: a base can't hang in the air.");
            if (c.State == CraftState.Docked) return Reply($"BASE MODE REFUSED: {c.Name} is parked on another structure.");
            ToggleMode(c);
            if (c.Pin == null) c.Anchor();
            UpdateWind(c);
            return Reply($"BASE MODE: {c.Name} is anchored where it stands. It can't fly and no wind moves it; storage, power and life support carry on.");
        }

        ToggleMode(c);
        CutPlan plan = PlanCut(c);
        if (plan.Cells.Count > 0 || plan.Refused.Count > 0)
        {
            UpdateWind(c);
            return Reply($"CAPITAL MODE: {c.Name} is keyed into the ground ({plan.Cells.Count + plan.Refused.Count} m³ hold it). Lift-off cuts it free.");
        }
        c.Release();
        UpdateWind(c);
        return Reply($"CAPITAL MODE: {c.Name} can fly. Ready to lift off.");
    }

    /// <summary>Flips the core's mode. Building marks the core's chunk dirty for it; its look doesn't change, so nothing is remeshed.</summary>
    static void ToggleMode(Craft c)
    {
        var dirty = new HashSet<SInt3>(c.Structure.DirtyChunks);
        Building.ToggleCoreMode(c.Structure);
        c.Structure.DirtyChunks.Clear();
        c.Structure.DirtyChunks.UnionWith(dirty);
    }

    static string Label(CoreMode m) => m == CoreMode.Base ? "base" : "capital";

    string Reply(string message)
    {
        Say(message);
        return message;
    }

    /// <summary>The wind rules: bases and parked or keyed hulls are never pushed; anything flying or simply landed is.</summary>
    public void UpdateWind(Craft c)
    {
        if (c.Pin == null) Weather.Track(c.Body);
        else Weather.Untrack(c.Body);
    }

    /// <summary>Everything that would leave the ground with this hull: its blocks and every vessel parked on it.</summary>
    public List<BlockBox> LiftingBoxes(Craft c)
    {
        var boxes = c.Boxes().ToList();
        foreach (Craft d in DockedOn(c)) boxes.AddRange(d.Boxes());
        return boxes;
    }

    public IEnumerable<Craft> DockedOn(Craft host) => Crafts.Where(d => d.Pin is { Docked: true } p && p.Host == host.Body);

    public CutPlan PlanCut(Craft c) => CutFree.Plan(LiftingBoxes(c), c.Tilt, Rules.Liftoff, Ground.Check);

    /// <summary>
    /// The lift margin with everything it carries: upward thrust minus the weight of the hull, its
    /// cargo and every vessel parked on it. Negative: too heavy.
    /// </summary>
    public (float margin, float weight, float upward, float carried) LiftMargin(Craft c)
    {
        HandlingStats stats = HandlingModel.Compute(c.Spec, new FlightConditions(Physics.Zone.Strength, true), Handling);
        float carried = DockedOn(c).Sum(d => (float)d.Tonnes);
        float weight = (stats.Mass + carried) * Physics.Zone.Strength;
        return (stats.UpwardThrust - weight, weight, stats.UpwardThrust, carried);
    }

    /// <summary>
    /// Lift-off from the console. Refused (with the reason, and nothing touched) in base mode,
    /// without power, when too heavy, or when the ground holding the hull can't be cut. Otherwise
    /// the ground holding it is cut free first (logged, through the edit API), and once the ground
    /// under it has been rebuilt, it climbs straight up through the cut before the pilot has it.
    /// </summary>
    public string LiftOffFrom(Craft c)
    {
        if (_liftoffs.ContainsKey(c)) return Reply("Lift-off in progress.");
        if (c.IsBase) return Reply($"LIFT-OFF REFUSED: {c.Name} is a base. Switch the core to capital mode first.");
        if (c.State == CraftState.Flying) return Reply($"{c.Name} is already flying.");
        if (c.State == CraftState.Docked) return Reply($"{c.Name} is parked on another structure: just fly off.");
        if (!c.Stats.Powered) return Reply($"LIFT-OFF REFUSED: {c.Name} has no power.");
        var (margin, weight, upward, carried) = LiftMargin(c);
        if (margin < 0f)
        {
            float g = Physics.Zone.Strength;
            Catalogue.TryGetType("thruster", out BlockType jet);
            int jets = (int)MathF.Ceiling(-margin / jet.Thrust);
            return Reply($"LIFT-OFF REFUSED: TOO HEAVY. {weight / g:F0} t" + (carried > 0 ? $" (with {carried:F1} t parked aboard)" : "") +
                         $" needs {weight:F0} kN of lift and the lift jets give {upward:F0} kN. Drop {-margin / g:F1} t or add {jets} lift jet{(jets == 1 ? "" : "s")}.");
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        CutPlan plan = PlanCut(c);
        double planMs = watch.Elapsed.TotalMilliseconds;
        if (!plan.Ok) return Reply(plan.Refusal);

        PlanetEdit edit = Ground.Remove(plan.Cells, Cut.LiftOff, c.Id);
        Debris.Crumble(edit.Removed, Planet, Rules.Liftoff.DebrisSeconds);
        var run = new LiftOff(c, plan, edit, planMs);
        _liftoffs[c] = run;
        GroundEdit logged = Ground.History[^1];
        return Reply(edit.Removed.Count > 0
            ? $"LIFT-OFF: cut {edit.Removed.Count} cells of ground free ({Ground.Describe(logged.Materials)}); climbing {plan.Climb:F1} m clear."
            : $"LIFT-OFF: nothing holds {c.Name}; climbing {plan.Climb:F1} m clear.");
    }

    public LiftOff LiftOffOf(Craft c) => _liftoffs.TryGetValue(c, out LiftOff run) ? run : null;
    public bool IsLiftingOff(Craft c) => _liftoffs.ContainsKey(c);

    /// <summary>Flags exactly one base or capital ship as Home. A new flag clears the old one.</summary>
    public string FlagHome(Craft c)
    {
        if (!c.IsLarge) return Reply("HOME REFUSED: only a base or a capital ship can be Home.");
        string old = Home.Flag(c.Id);
        Craft was = old == null ? null : CraftById(old);
        return Reply(was != null ? $"HOME: {c.Name} (was {was.Name})." : $"HOME: {c.Name}.");
    }

    public Craft HomeCraft => Home.HomeId == null ? null : CraftById(Home.HomeId);

    public string SetAutoTransmit(Craft v, bool on)
    {
        v.AutoTransmit = on;
        Pump(v);
        return Reply($"{v.Name}: auto-transmit {(on ? "on" : "off")}.");
    }

    // ==== Building onto a structure ===============================================================

    /// <summary>
    /// Places a block on a structure, in either game mode: free in creative, paid from the player's
    /// inventory in survival (building's own cost rule, behind its creative switch). Ground inside
    /// the new block is cleared through the terrain's edit API, so a block can be built into the
    /// ground. Every material and block works in both core modes.
    /// </summary>
    public (PlaceError error, int cleared) PlaceBlock(Craft c, SInt3 cell, Block block)
    {
        // Ground a block is built into must give way, as terrain-digging's own placement rule says:
        // only bedrock refuses it (the cell counts as occupied). A POI's ground gives way like any other.
        List<TInt3> ground = GroundInside(c, cell);
        if (ground.Any(t => Ground.Check(t) is Verdict.Bedrock or Verdict.OutsideWorld))
        {
            Say("Can't build there: bedrock fills that space.");
            return (PlaceError.Occupied, 0);
        }
        Dictionary<string, int> cost = c.Structure.Catalogue.CostOf(block, c.Structure.Grid);
        bool paid = !Inventory.Creative;
        PlaceError error = Building.Place(c.Structure, cell, block, Inventory, c.Structure.Owner ?? Building.DefaultOwner);
        if (error != PlaceError.None) return (error, 0);
        Declare(paid ? -cost.Values.Sum() : 0, 1);
        int cleared = ground.Count == 0 ? 0 : Ground.Remove(ground, Cut.Build, c.Id).Removed.Count;
        c.ApplyEdits();
        if (c.State == CraftState.Landed && cleared > 0)
        {
            // A landed ship just built into the ground is held by it until lift-off.
            c.Anchor();
            UpdateWind(c);
        }
        return (PlaceError.None, cleared);
    }

    /// <summary>Takes a block off with the multi-tool: its materials go to the player (both game modes, building's rule).</summary>
    public string RemoveBlock(Craft c, SInt3 cell)
    {
        if (!c.Structure.TryGet(cell, out Block b)) return "Nothing there.";
        if (Building.WouldSever(c.Structure, cell, out int cut))
            return Reply($"That block holds {cut} others on. Take those off first.");
        int before = Inventory.Total;
        RemoveResult r = Building.Remove(c.Structure, cell, Inventory);
        if (r.Status != RemoveStatus.Removed) return Reply($"Can't remove that ({r.Status}).");
        Declare(Inventory.Total - before, -1);
        c.ApplyEdits();
        return Reply($"Removed a {c.Structure.TypeOf(b).Name.ToLowerInvariant()}.");
    }

    /// <summary>The solid ground inside a block's cell (grown by the build margin), sorted.</summary>
    List<TInt3> GroundInside(Craft c, SInt3 cell) =>
        CutFree.CellsInside(c.BoxOf(cell), Rules.Build.Margin).Where(t => Ground.Store.IsSolid(t)).ToList();

    /// <summary>Clears the ground inside some of a structure's blocks (a base set into the ground). Returns the cells removed.</summary>
    int ClearGroundInside(Craft c, IEnumerable<SInt3> cells)
    {
        var ground = new SortedSet<TInt3>();
        foreach (SInt3 p in cells) ground.UnionWith(GroundInside(c, p));
        if (ground.Count == 0) return 0;
        return Ground.Remove(ground, Cut.Build, c.Id).Removed.Count;
    }

    // ==== Digging and drilling ====================================================================

    /// <summary>The player's dig tool: a ball of ground, its yield into the player's inventory. Anywhere but bedrock, under a base too.</summary>
    public PlanetEdit Dig(N.Vector3 point, float radius)
    {
        PlanetEdit edit = Ground.Dig(TInt3.Floor(point), radius, "player");
        foreach (var (item, n) in Yield(edit)) Inventory.Add(new Dictionary<string, int> { [item] = n });
        Declare(edit.Removed.Count(r => Rules.ItemFor(r.Material.ToString()) != null), 0);
        return edit;
    }

    /// <summary>What a dug ball gives, item by item.</summary>
    SortedDictionary<string, int> Yield(PlanetEdit edit)
    {
        var haul = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (RemovedCell r in edit.Removed)
            if (Rules.ItemFor(r.Material.ToString()) is string item) haul[item] = haul.GetValueOrDefault(item) + 1;
        return haul;
    }

    /// <summary>The result of one drill: what it dug and where it went.</summary>
    public readonly record struct DrillResult(string Refusal, int Dug, int ToHome, int Aboard, LinkState Link);

    /// <summary>
    /// A harvesting vessel drills the ground under its keel. With auto-transmit on and Home in
    /// range with room, the haul goes straight into Home's storage; what doesn't fit (or everything,
    /// out of range) goes into the vessel's own cargo. It won't dig more than the two can hold.
    /// </summary>
    public DrillResult Drill(Craft v)
    {
        float radius = Rules.Drill.Radius;
        N.Vector3 from = v.Body.CenterOfMassPosition;
        float top = GroundTop(from.X, from.Z);
        float clearance = from.Y - top;
        if (clearance > Rules.Drill.Reach + 2f) return new DrillResult($"Nothing to drill within {Rules.Drill.Reach:F0} m.", 0, 0, 0, v.Link.State);
        TInt3 centre = TInt3.Floor(new N.Vector3(from.X, top - radius * 0.5f, from.Z));

        Craft home = HomeCraft;
        LinkReport link = Measure(v, home);
        double room = v.FreeSu + (link.State == LinkState.InRange ? link.HomeFreeSu : 0.0);
        int most = ChunkStore.SphereCells(centre, radius).Count(cell => Ground.Check(cell) == Verdict.Removable);
        if (most * Rules.Catalog.LargestUnit > room + 1e-9)
            return new DrillResult($"CARGO FULL: {room:F0} SU free, a drill takes up to {most * Rules.Catalog.LargestUnit:F0}. Unload or get Home in range.",
                0, 0, 0, link.State);

        PlanetEdit edit = Ground.Dig(centre, radius, v.Id, Cut.Drill);
        SortedDictionary<string, int> haul = Yield(edit);
        Declare(haul.Values.Sum(), 0);
        var (toHome, aboard) = Cargo.Split(haul, link.State == LinkState.InRange ? home.Cargo : null,
            home?.StorageCapacity ?? 0, v.Cargo, Rules.Catalog);
        if (toHome > 0) home.CargoChanged();
        if (aboard > 0) v.CargoChanged();
        v.Link = Measure(v, home);
        return new DrillResult(null, haul.Values.Sum(), toHome, aboard, v.Link.State);
    }

    /// <summary>Distance from a vessel to Home's core, the short way round the planet.</summary>
    public float DistanceToHome(Craft v, Craft home)
    {
        if (home == null) return float.PositiveInfinity;
        N.Vector3 a = v.Body.CenterOfMassPosition, b = home.Body.ToParent(StructureBridge.CellCenter(home.Structure, home.Structure.Core ?? SInt3.Zero));
        double dx = Mathx.WrapDelta(b.X - a.X, Planet.Width), dz = Mathx.WrapDelta(b.Z - a.Z, Planet.Height);
        return (float)Math.Sqrt(dx * dx + (b.Y - a.Y) * (double)(b.Y - a.Y) + dz * dz);
    }

    LinkReport Measure(Craft v, Craft home)
    {
        float d = DistanceToHome(v, home);
        double free = home?.FreeSu ?? 0.0;
        LinkState state = AutoTransmit.Evaluate(v.AutoTransmit, home != null, d, Rules.AutoTransmit.Radius, free, Rules.Catalog, v.Cargo);
        return new LinkReport(state, d, free, v.Cargo.Total, 0);
    }

    /// <summary>One pass of a vessel's transmitter: whatever it holds goes to Home if the link is up.</summary>
    public LinkReport Pump(Craft v)
    {
        Craft home = HomeCraft;
        float d = DistanceToHome(v, home);
        LinkReport r = AutoTransmit.Pump(v.AutoTransmit && home != v, home != null, d, Rules.AutoTransmit.Radius, v.Cargo,
            home?.Cargo, home?.StorageCapacity ?? 0, Rules.Catalog);
        if (r.Moved > 0)
        {
            home.CargoChanged();
            v.CargoChanged();
        }
        v.Link = r;
        return r;
    }

    public string LinkText(Craft v) => AutoTransmit.Describe(v.Link, HomeCraft?.Name ?? "none", Rules.AutoTransmit.Radius);

    // ==== Parking ================================================================================

    /// <summary>Parks a vessel on a host: it rides the host from now on, sheltered from the wind.</summary>
    public void Dock(Craft vessel, Craft host)
    {
        vessel.DockOn(host);
        vessel.Hull.AddCollisionExceptionWith(host.Hull);
        host.Hull.AddCollisionExceptionWith(vessel.Hull);
        _apart.RemoveAll(x => x.a == vessel && x.b == host);
        UpdateWind(vessel);
        Say($"{vessel.Name} parked on {host.Name}.");
    }

    /// <summary>The pilot wants to go: the vessel is handed back to its controller, moving with its host, and flies off.</summary>
    public void Undock(Craft vessel)
    {
        if (vessel.Pin is not { Docked: true } pin) return;
        Craft host = CraftOf(pin.Host);
        vessel.Release();
        UpdateWind(vessel);
        if (host != null) _apart.Add((vessel, host, Physics.Tick + 90));
        Say($"{vessel.Name} lifted off {host?.Name}.");
    }

    /// <summary>A vessel that has just settled: on a structure's hull, it parks there.</summary>
    void TryDock(Craft c)
    {
        if (c.Pin != null || !c.Vessel.Frozen) return;
        N.Vector3 from = c.Body.CenterOfMassPosition;
        if (!Physics.Query.Ray(from, from - N.Vector3.UnitY * 12f, Layers.Hull, c.Body, out RayHit hit)) return;
        Craft host = CraftOf(hit.Body);
        if (host == null || host == c || !host.IsLarge || host.Pin is { Docked: true }) return;
        Dock(c, host);
    }

    // ==== Every tick ===============================================================================

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        foreach (Craft c in _landed.ToList()) TryDock(c);
        _landed.Clear();

        foreach (Craft c in Crafts)
        {
            if (c.Pin is { Docked: true } && c.Flight.Seated && c.Flight.AnyThrust) Undock(c);
            if (c.CheckRestingOnHull(dt)) TryDock(c);
            c.Vessel.Gear?.Probe(Physics, c.Body);
        }

        for (int i = _apart.Count - 1; i >= 0; i--)
        {
            var (a, b, until) = _apart[i];
            if (Physics.Tick < until) continue;
            if (IsInstanceValid(a.Hull) && IsInstanceValid(b.Hull) && a.Pin?.Host != b.Body)
            {
                a.Hull.RemoveCollisionExceptionWith(b.Hull);
                b.Hull.RemoveCollisionExceptionWith(a.Hull);
            }
            _apart.RemoveAt(i);
        }

        foreach (LiftOff run in _liftoffs.Values.ToList())
        {
            run.Step(this, dt);
            if (run.Phase == LiftOffPhase.Done)
            {
                _liftoffs.Remove(run.Craft);
                Say($"{run.Craft.Name} is clear of the ground: {run.Climbed:F1} m up, {run.Edit.Removed.Count} cells cut, top climb speed {run.TopRise:F2} m/s.");
            }
        }

        _transmitTimer += dt;
        if (_transmitTimer >= Rules.AutoTransmit.Interval)
        {
            _transmitTimer = 0f;
            foreach (Craft c in Crafts)
                if (!c.IsLarge) Pump(c);
        }
    }

    public override void _Process(double delta)
    {
        if (Patch.Pending > 0) Patch.RebuildPending(RebuildBudgetMs);
        foreach (Craft c in Crafts) c.RefreshView();
    }

    // ==== Saves ===================================================================================

    /// <summary>A saved base, or a hull keyed into the ground, held at its saved pose.</summary>
    public void RestoreAnchor(Craft c, N.Vector3 position, N.Quaternion rotation)
    {
        c.AnchorAt(position, rotation);
        UpdateWind(c);
    }

    /// <summary>A saved parked vessel, back on its host at its saved place.</summary>
    public void RestoreDock(Craft vessel, Craft host, N.Vector3 local, N.Quaternion localRotation)
    {
        vessel.DockAt(host, local, localRotation);
        vessel.Hull.AddCollisionExceptionWith(host.Hull);
        host.Hull.AddCollisionExceptionWith(vessel.Hull);
        UpdateWind(vessel);
    }

    /// <summary>Takes every craft out (a save is being loaded in their place).</summary>
    public void Clear()
    {
        foreach (Craft c in Crafts)
        {
            Weather.Untrack(c.Body);
            c.Discard();
        }
        Crafts.Clear();
        _liftoffs.Clear();
        _landed.Clear();
        _apart.Clear();
        Home.Clear();
        Inventory.Items.Clear();
    }

    /// <summary>A fresh ledger after a load: what's in the world now is the starting point.</summary>
    public void ResetLedger()
    {
        DeclaredItems = 0;
        DeclaredBlocks = 0;
    }
}
