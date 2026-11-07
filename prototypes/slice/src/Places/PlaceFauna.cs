using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PlanetSurface.Terrain;
using PoiGenerator.Gen;
using Slice.Surface;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;
using TD = TerrainDigging.Terrain;
using WNav = Wildlife.NavGrid;
using WTerrain = Wildlife.Terrain;

namespace Slice.Places;

/// <summary>
/// What lives in a place: wildlife's population function (<see cref="PoiFauna"/>) given the
/// place's rooms, its state, theme and depth: lurkers in dark rooms, prowlers on the corridors,
/// ambushers in the ceilings and behind doors (each with its tell: grit, a shadow, scratching, a
/// rattle), subsisters at moss and scraps with the place's hunters eating them, nests on the
/// deepest levels; and some places empty and silent. They live in the place's own blocks, in a
/// sim anchored at the place (<see cref="DeepSite"/>), so nothing about them is lost when the
/// surface patch moves on, or when you leave and come back.
/// </summary>
public sealed class PlaceFauna
{
    /// <summary>The planet's deep fauna (its POI and cave species), made once from its surface species.</summary>
    public sealed class Deep
    {
        public readonly DeepFauna Fauna;
        public readonly WildlifeData Data;
        public readonly object Lock = new();
        public readonly PlanetSurface.World.PlanetWorld Planet;
        /// <summary>How livable the world is, 0..1.</summary>
        public readonly float Livable;

        public Deep(WorldLife life, PlanetSurface.World.PlanetWorld planet, float livable = 1f)
        {
            Data = life.Data;
            Planet = planet;
            Livable = livable;
            Fauna = life.Deep();
        }
    }

    public PoiPopulation Population;
    public WildSim Sim;
    public bool Silent => Population == null || Population.Silent || Population.Spawns.Count == 0;
    DeepSite _site;

    /// <summary>The place's rooms as the population function sees them, and who lives there (worker-safe).</summary>
    public static PlaceFauna Plan(PlaceMemory memory, Deep deep)
    {
        PoiInstance inst = memory.Inst;
        Layout layout = inst.Layout;
        bool ship = inst.Family.Tags.Contains("ship") || inst.Family.Tags.Contains("wreck") || inst.Family.Tier == PoiTier.Wreck;
        var plan = new PoiPlan
        {
            Seed = inst.SpawnSeed,
            State = (PlaceState)(int)inst.State,
            Theme = ship ? PoiTheme.Derelict : ThemeOf(inst.Kit?.Faction ?? Faction.None, inst.Family),
            Derelict = ship,
            Livable = deep.Livable,
        };
        // People keep a place clear of what would hunt them, as a garrison does: at most vermin, down in the dark.
        if (inst.Occupancy is { Main: Occupant.Scavengers or Occupant.Squatters or Occupant.Gang or Occupant.Machines or Occupant.Faction or Occupant.Cyborgs or Occupant.Robots or Occupant.Cult or Occupant.Keepers or Occupant.Guardians, Second: not (Occupant.Nesters or Occupant.Prowlers) })
            plan.State = PlaceState.Active;
        var byIndex = new Dictionary<int, PoiRoom>();
        foreach (PlacedModule m in layout.Modules)
        {
            if (m.Kind == ModuleKind.Outdoor) continue;
            if (Decor.Interior(inst, m) is not { } inside || inside.Height < 2) continue;
            var room = new PoiRoom
            {
                Index = m.Index,
                Kind = KindOf(m, ship),
                Min = new Vector3(inside.X0, inside.Y, inside.Z0),
                Max = new Vector3(inside.X1 + 1, inside.Y + inside.Height, inside.Z1 + 1),
                Depth = Math.Max(0, -m.Storey),
                Dark = inst.State != PoiState.Active || m.Underground,
            };
            byIndex[m.Index] = room;
            plan.Rooms.Add(room);
        }
        foreach (Link l in layout.Links)
            if (byIndex.TryGetValue(l.A, out PoiRoom a) && byIndex.TryGetValue(l.B, out PoiRoom b))
            {
                if (!a.Links.Contains(b.Index)) a.Links.Add(b.Index);
                if (!b.Links.Contains(a.Index)) b.Links.Add(a.Index);
            }
        foreach (Doorway d in layout.Doorways)
        {
            if (d.Cells.Count == 0) continue;
            PInt3 min = d.Cells.Aggregate(PInt3.Min), max = d.Cells.Aggregate(PInt3.Max);
            var mid = new Vector3((min.X + max.X + 1) / 2f, min.Y, (min.Z + max.Z + 1) / 2f);
            PInt3 f = d.Facing.Vec();
            var outOfChild = new Vector3(f.X, 0, f.Z);
            if (byIndex.TryGetValue(d.ModuleA, out PoiRoom pa)) pa.Doors.Add((mid, outOfChild));
            if (byIndex.TryGetValue(d.ModuleB, out PoiRoom pb)) pb.Doors.Add((mid, -outOfChild));
        }
        PoiPopulation pop;
        lock (deep.Lock) pop = PoiFauna.Populate(plan, deep.Fauna, deep.Data.Deep);
        return new PlaceFauna { Population = pop };
    }

    static PoiTheme ThemeOf(Faction f, FamilyDef family) =>
        family.Tags.Contains("mine") || family.Tags.Contains("cave") ? PoiTheme.Natural
        : f is Faction.Meridian or Faction.Exchange or Faction.Reavers or Faction.Freeholders ? PoiTheme.Industrial
        : f is Faction.Vharn or Faction.Kessari or Faction.Hollow ? PoiTheme.Alien
        : (family.Style ?? "").StartsWith("alien_", StringComparison.Ordinal) ? PoiTheme.Alien : PoiTheme.Industrial;

    static RoomKind KindOf(PlacedModule m, bool ship)
    {
        if (ship)
        {
            string p = m.Purpose ?? "";
            if (p.Contains("hold") || p.Contains("cargo")) return RoomKind.Hold;
            if (p.Contains("engine") || p.Contains("reactor")) return RoomKind.EngineRoom;
            if (p.Contains("duct") || p.Contains("crawl")) return RoomKind.Duct;
        }
        return m.Kind switch
        {
            ModuleKind.Entrance => RoomKind.Entrance,
            ModuleKind.Corridor => RoomKind.Corridor,
            ModuleKind.Hall => RoomKind.Hall,
            ModuleKind.Vault => RoomKind.Vault,
            ModuleKind.Descent or ModuleKind.Stair or ModuleKind.Lift => RoomKind.Descent,
            ModuleKind.Cave => RoomKind.Cave,
            _ => RoomKind.Room,
        };
    }

    /// <summary>
    /// <see cref="Solid"/> remembered cell by cell. A creature's body reads 27 cells for every density
    /// sample and takes many samples a tick, so a full place re-asked the block grid millions of times
    /// a second. A block that goes tells the place's <see cref="Forget"/>.
    /// </summary>
    sealed class SolidCells
    {
        readonly PoiInstance _inst;
        readonly PInt3 _min, _size;
        readonly byte[] _known; // 0 not read yet, 1 open, 2 solid

        public SolidCells(PoiInstance inst)
        {
            _inst = inst;
            _min = inst.Blocks.Min;
            _size = inst.Blocks.Size;
            _known = new byte[_size.X * _size.Y * _size.Z];
        }

        int Index(int x, int y, int z) => ((z - _min.Z) * _size.Y + y - _min.Y) * _size.X + x - _min.X;

        /// <summary>Only for cells in the place's grid (the block space's bounds see to that).</summary>
        public bool Get(int x, int y, int z)
        {
            int i = Index(x, y, z);
            byte k = _known[i];
            if (k == 0) _known[i] = k = Solid(_inst, x, y, z) ? (byte)2 : (byte)1;
            return k == 2;
        }

        public void Forget(PInt3 c)
        {
            if (_inst.Blocks.InBounds(c)) _known[Index(c.X, c.Y, c.Z)] = 0;
        }
    }

    SolidCells _cells;

    /// <summary>Blocks of the place changed: its creatures read them afresh.</summary>
    public void Forget(IEnumerable<PInt3> cells)
    {
        if (_cells == null) return;
        foreach (PInt3 c in cells) _cells.Forget(c);
    }

    /// <summary>Solid to a creature: the place's walls, floors and roofs and its heavy fittings; not air, doors, glass or what it can walk under. Below ground, rock outside the rooms.</summary>
    public static bool Solid(PoiInstance inst, int x, int y, int z)
    {
        var c = new PInt3(x, y, z);
        Block b = inst.Blocks.Get(c);
        if (b.IsAir) return y <= Assembler.GroundY && !inst.Layout.Occupied[c];
        return Built(b);
    }

    /// <summary>Solid to a creature out on the surface: only what was built (walls, floors, roofs, heavy fittings), not the ground round it.</summary>
    public static bool BuiltSolid(PoiInstance inst, PInt3 c)
    {
        Block b = inst.Blocks.Get(c);
        return !b.IsAir && Built(b);
    }

    static bool Built(Block b) => BlockInfo.Collides(b.Type) && b.Type is not (BlockType.Door or BlockType.Pane or BlockType.Table or BlockType.Rail or BlockType.Lattice);

    /// <summary>
    /// Its sim, made once (worker-safe): the place's ground round it for anything that wanders
    /// out, and its creatures put in its blocks, each in its den, ambush, nest or beat.
    /// </summary>
    public void Populate(PlaceMemory memory, Deep deep)
    {
        if (Silent || Sim != null) return;
        PoiInstance inst = memory.Inst;
        BlockGrid g = inst.Blocks;
        float half = MathF.Max(MathF.Max(-g.Min.X, g.Max.X + 1), MathF.Max(-g.Min.Z, g.Max.Z + 1)) + 8f;
        var o = new Vector3(memory.Site.Origin.X, memory.Site.Origin.Y, memory.Site.Origin.Z);
        PlanetSurface.World.PlanetWorld planet = deep.Planet;
        var terrain = new WTerrain((x, z) => planet.GroundHeight(Planets.Gen.Mathx.WrapX(o.X + x, planet.Width), Planets.Gen.Mathx.Wrap(o.Z + z, planet.Height)) - o.Y, half);
        var sim = new WildSim(deep.Data, terrain, new WNav(terrain.HalfSize, 3f));
        _cells = new SolidCells(inst);
        var space = new BlockSpace(_cells.Get,
            new Vector3I(g.Min.X, g.Min.Y, g.Min.Z), new Vector3I(g.Min.X + g.Size.X, g.Min.Y + g.Size.Y, g.Min.Z + g.Size.Z));
        lock (deep.Lock) PoiFauna.Spawn(sim, Population, space);
        Sim = sim;
    }

    /// <summary>The place stands: its creatures are drawn, heard and step again, as they were left.</summary>
    public void Stand(Place place, PoiPlaces places)
    {
        if (Silent || Sim == null) return;
        _site = DeepSite.Create(places.Zone.Wildlife, Sim, "Creatures");
        place.AddChild(_site);
    }

    /// <summary>The place is taken down: its creatures wait, as they were, for you to come back.</summary>
    public void Sit(Place place)
    {
        _site = null; // freed with the place; the sim is kept
    }

    public DeepSite Site => _site != null && GodotObject.IsInstanceValid(_site) ? _site : null;
}

/// <summary>
/// The planet's caves: as the survivor walks, on the surface or underground, the roomiest chamber
/// ahead of them is filled once with crawlers on its walls and ceiling and grazers at its moss
/// (wildlife's <see cref="CaveFauna"/>), living in the planet's own rock, so a cave is lived in
/// before you get there and nothing turns up behind you. Each cave keeps its creatures for the
/// game; its sim stands only while you are near it.
/// </summary>
public partial class Caves : Node3D
{
    public SurfaceZone Zone;
    public Func<Vector3> Feet;
    public Func<bool> OnFoot;
    /// <summary>Which way the survivor is looking.</summary>
    public Func<Vector3> Look = () => Vector3.Forward;
    /// <summary>In somewhere that isn't a cave: a place's rooms, the bunker.</summary>
    public Func<bool> Indoors = () => false;
    public PlaceFauna.Deep Deep;

    /// <summary>The longest search for a chamber so far, on its worker, ms (tests).</summary>
    public double WorstMs { get; private set; }

    sealed class Cave
    {
        public Vector3 Anchor;      // canonical, whole metres: the sim's origin
        public WildSim Sim;
        public DeepSite Site;
    }

    readonly Dictionary<(int, int, int), Cave> _caves = new();
    double _timer;
    Vector3? _searchedFrom;

    /// <summary>How far ahead of you the search looks, and how far you walk between searches, m.</summary>
    const float Ahead = 14f, SearchEvery = 12f;

    /// <summary>Caves found and filled so far (tests).</summary>
    public int Found => _caves.Count;

    public override void _PhysicsProcess(double delta)
    {
        if (Zone?.Planet == null || Deep == null) return;
        _timer -= delta;
        if (_timer > 0) return;
        _timer = 2.0;
        Vector3 feet = Feet();
        var planet = Zone.Planet;
        foreach (Cave c in _caves.Values)
        {
            Vector3 at = planet.NearestCopy(c.Anchor, feet);
            float d = at.DistanceTo(feet);
            if (c.Site != null && d > 160f) { c.Site.QueueFree(); c.Site = null; }
            else if (c.Site == null && d < 100f) Stand(c, at);
            else if (c.Site != null) c.Site.Position = at;
        }
        if (_search != null)
        {
            if (!_search.IsCompleted) return;
            Task<(Cave Cave, (int, int, int) Key)> done = _search;
            _search = null;
            if (done.IsFaulted) return; // the streamer changed under the search: the next one will do
            var (found, key) = done.Result;
            if (found != null && !_caves.ContainsKey(key))
            {
                _caves[key] = found;
                Stand(found, planet.NearestCopy(found.Anchor, feet));
            }
            return;
        }
        if (!OnFoot() || Indoors()) return;
        if (_searchedFrom is { } from && from.DistanceTo(feet) < SearchEvery) return;
        _searchedFrom = feet;
        float surface = Zone.GroundAt(feet.X, feet.Z).Y;
        var anchor = new Vector3(MathF.Round(feet.X), MathF.Round(feet.Y), MathF.Round(feet.Z));
        var space = new LoadedVoxels(planet.Terrain.Store, Zone.Streamer, new Vector3I((int)anchor.X, (int)anchor.Y, (int)anchor.Z));
        float time = Zone.Wildlife.Sim?.Time ?? 0f;
        Vector3 look = Look();
        look.Y = 0f;
        look = look.LengthSquared() > 1e-4f ? look.Normalized() : Vector3.Forward;
        _search = Task.Run(() => Search(planet, space, anchor, feet - anchor, look, surface, time));
    }

    /// <summary>Looks for the roomiest chamber round a point and fills it, off the main thread (a private sim, its rock only read).</summary>
    (Cave, (int, int, int)) Search(PlanetSurface.World.PlanetWorld planet, LoadedVoxels space, Vector3 anchor, Vector3 feet, Vector3 look, float surface, float time)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        float Height(float x, float z) => planet.GroundHeight(Planets.Gen.Mathx.WrapX(anchor.X + x, planet.Width), Planets.Gen.Mathx.Wrap(anchor.Z + z, planet.Height)) - anchor.Y;
        bool found = CaveFauna.FindChamber(space, look * Ahead, 16f, out Vector3 floor, Height);
        WorstMs = Math.Max(WorstMs, clock.Elapsed.TotalMilliseconds);
        if (!found) return (null, default);
        Vector3 chamber = planet.Canonical(anchor + floor);
        var key = ((int)MathF.Floor(chamber.X / 24f), (int)MathF.Floor(chamber.Y / 24f), (int)MathF.Floor(chamber.Z / 24f));
        if (_caves.ContainsKey(key)) return (null, default);
        var cave = new Cave { Anchor = planet.Canonical(anchor) };
        var terrain = new WTerrain(Height, 40f);
        cave.Sim = new WildSim(Deep.Data, terrain, new WNav(terrain.HalfSize, 3f)) { Time = time };
        ulong seed = (ulong)(key.Item1 * 73856093L ^ key.Item2 * 19349663L ^ key.Item3 * 83492791L) ^ planet.Spec.Seed;
        int depth = Math.Max(1, (int)((surface - (anchor.Y + floor.Y)) / 12f) + 1);
        SpawnRules rules = Deep.Data.Spawning;
        Vector3 eye = feet + Vector3.Up * 1.6f;
        float cone = MathF.Cos(Mathf.DegToRad(rules.ViewConeDegrees));
        bool Unseen(Vector3 p)
        {
            Vector3 to = p - eye;
            float d = to.Length();
            return d >= rules.CaveClearance && !(look.Dot(to / d) > cone && SpaceMath.Clear(space, eye, p, 0f));
        }
        List<Creature> made;
        lock (Deep.Lock) made = CaveFauna.Populate(cave.Sim, space, Deep.Fauna, floor + Vector3.Up, 16f, seed, depth, crawlers: 1, subsisters: (int)MathF.Round(3f * Deep.Livable), allow: Unseen);
        if (made.Count == 0) return (null, default); // nowhere out of your sight: the next search may do better
        return (cave, key);
    }

    Task<(Cave Cave, (int, int, int) Key)> _search;
    void Stand(Cave c, Vector3 at)
    {
        c.Site = DeepSite.Create(Zone.Wildlife, c.Sim, "Cave");
        AddChild(c.Site);
        c.Site.GlobalPosition = at;
    }

    /// <summary>The cave creatures' rock: the planet's voxels, read only where the streamer has built them (nothing is generated on the main thread); elsewhere solid.</summary>
    sealed class LoadedVoxels : ISolidSpace
    {
        readonly TD.ChunkStore _store;
        readonly TerrainStreamer _streamer;
        readonly Vector3I _offset;

        public LoadedVoxels(TD.ChunkStore store, TerrainStreamer streamer, Vector3I offset)
        {
            _store = store;
            _streamer = streamer;
            _offset = offset;
        }

        bool Loaded(TD.Int3 c) => _streamer.IsReady(TD.ChunkStore.ChunkOf(c));

        public float Density(Vector3 p)
        {
            var w = new System.Numerics.Vector3(p.X + _offset.X, p.Y + _offset.Y, p.Z + _offset.Z);
            return Loaded(TD.Int3.Floor(w)) ? _store.SampleDensity(w) : 1f;
        }

        public bool SolidCell(int x, int y, int z)
        {
            var c = new TD.Int3(x + _offset.X, y + _offset.Y, z + _offset.Z);
            return !Loaded(c) || _store.IsSolid(c);
        }
    }
}
