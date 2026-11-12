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

        /// <summary>The world's life: whoever holds a place is one of its own kinds.</summary>
        public readonly WorldLife Life;

        /// <summary>
        /// Where the survivor's eye is and which way they look (world frame), as last seen on the main
        /// thread: a place filled on a worker puts nothing where they'd see it appear, or beside them.
        /// Null until known (tests, the start).
        /// </summary>
        public volatile Viewer Eye;

        public sealed record Viewer(Vector3 At, Vector3 Look);

        public Deep(WorldLife life, PlanetSurface.World.PlanetWorld planet, float livable = 1f)
        {
            Life = life;
            Data = life.Data;
            Planet = planet;
            Livable = livable;
            Fauna = life.Deep();
        }
    }

    public PoiPopulation Population;
    /// <summary>The rooms the population was planned over (tests read depths and hops off it).</summary>
    public PoiPlan Rooms;
    public WildSim Sim;
    public bool Silent => Population == null || Population.Silent || Population.Spawns.Count == 0 && Population.Masses.Count == 0;
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
            Danger = (int)inst.Rarity / 4f * 0.6f + inst.State switch { PoiState.Infested => 0.4f, PoiState.Ruined => 0.15f, _ => 0f },
        };
        // The Turned (code HSK) hold it, alone or sharing it with others: a Husk den. Where the Husks'
        // warping (WRP) holds it, a light one: the wrongness, a few of them far in.
        if (inst.Occupancy is { } occ && (Has(occ, Occupant.Turned) || Has(occ, Occupant.Warped)))
        {
            bool turned = Has(occ, Occupant.Turned);
            Occupant who = turned ? Occupant.Turned : Occupant.Warped;
            plan.Husks = true;
            plan.Shared = occ.Main != who || occ.Second != null || occ.How != Sharing.Alone;
            // How far gone it is goes by how they hold it: alone (severe), sharing, second, or only a trace (light).
            plan.Severity = deep.Data.Husks.SeverityOf(occ.Main == who, !plan.Shared, occ.Main != who && occ.Second != who,
                Wildlife.Rng.For(inst.SpawnSeed, "den-severity").Float()) * (turned ? 1f : 0.4f);
        }
        HoldersOf(inst, deep.Life, plan);
        // What the world's dark holds, for the dark rooms; and now and then an armed group.
        plan.Dark.AddRange(deep.Life.Dark);
        plan.Armed = deep.Life.Armed;
        var byIndex = new Dictionary<int, PoiRoom>();
        foreach (PlacedModule m in layout.Modules)
        {
            if (m.Kind == ModuleKind.Outdoor)
            {
                // Open ground: a yard or a camp's pitch, for those that hold the place (the growth's on a camp it took).
                var floor = m.Cells.Where(c => c.Y == m.Min.Y).ToList();
                if (floor.Count < 9) continue;
                plan.Yards.Add(new PoiRoom
                {
                    Index = m.Index, Kind = RoomKind.Room, Yard = true, Purpose = m.Purpose ?? "",
                    Min = new Vector3(floor.Min(c => c.X), m.Min.Y, floor.Min(c => c.Z)),
                    Max = new Vector3(floor.Max(c => c.X) + 1, m.Min.Y + 6, floor.Max(c => c.Z) + 1),
                });
                continue;
            }
            if (Decor.Interior(inst, m) is not { } inside || inside.Height < 2) continue;
            // A working light keeps the room lit (steady and strong enough), whatever the place's state.
            bool lit = inst.Lights.Any(l => l.Module == m.Index && l.Mode is LightMode.Steady or LightMode.Beacon && l.Energy >= 0.9f);
            var caches = inst.Caches.Where(c => c.Module == m.Index && !c.PickedOver).ToList();
            var room = new PoiRoom
            {
                Index = m.Index,
                Kind = KindOf(m, ship),
                Min = new Vector3(inside.X0, inside.Y, inside.Z0),
                Max = new Vector3(inside.X1 + 1, inside.Y + inside.Height, inside.Z1 + 1),
                Depth = Math.Max(0, -m.Storey),
                Dark = (inst.State != PoiState.Active || m.Underground) && !lit,
                Lit = lit,
                Purpose = m.Purpose ?? "",
                Loot = caches.Count,
                LootTier = caches.Count == 0 ? 0 : caches.Max(c => c.Tier) + (caches.Any(c => c.InVault) ? 1 : 0),
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
        return new PlaceFauna { Population = pop, Rooms = plan };
    }

    static bool Has(OccupancyRoll o, Occupant who) => o.Main == who || o.Second == who || o.Trace == who;

    /// <summary>
    /// Who holds a place, from its rolled occupant (who the world's theme and fall put there), and
    /// which of the world's own kinds they are: nesters' nests; twisted beasts (and the fall's own
    /// changed, where it has them) in their den; machines, the old security or cyborgs on their
    /// rounds; people (the town) keeping it clear; the empty and the sleeping holding vermin at
    /// most. A place the growth took (infested) is the Hollows', its beasts through its rooms. Where
    /// two groups share it, the one that isn't people says what lives in it.
    /// </summary>
    public static void HoldersOf(PoiInstance inst, WorldLife life, PoiPlan plan)
    {
        if (inst.Occupancy is not { } occ)
        {
            if (inst.State != PoiState.Infested) return;
            plan.Holder = PlaceHolder.Hollows;
            plan.Holders.AddRange(life.Hollows);
            plan.Hollows.AddRange(life.Hollows);
            return;
        }
        // The growth took it, whoever else holds it now: its Hollow beasts are always in it and round it.
        if (inst.State == PoiState.Infested) plan.Hollows.AddRange(life.Hollows);
        Occupant main = Creatures(occ.Main) || occ.Second is not { } second || !Creatures(second) ? occ.Main : second;
        plan.Holder = HolderOf(main);
        switch (main)
        {
            case Occupant.Prowlers:
                // The world's twisted beasts, and its own changed where its fall left them (the Changed, ghouls, thralls).
                plan.Holders.AddRange(life.Twisted);
                plan.Holders.AddRange(life.Signature.Denning);
                break;
            case Occupant.Machines or Occupant.Robots or Occupant.Guardians:
                // The world's own machines of its era where its story left some walking, else the old security's.
                List<Species> own = life.Signature.Machines;
                plan.Holders.AddRange(own.Count > 0 ? own : life.Machines);
                break;
            case Occupant.Cyborgs:
                plan.Holders.AddRange(life.Cyborgs);
                break;
        }
        // Holders with nobody to be: the place is only what lives in any place.
        if (plan.Holder is PlaceHolder.Beasts or PlaceHolder.Machines && plan.Holders.Count == 0) plan.Holder = PlaceHolder.Empty;
    }

    /// <summary>Occupants that are creatures (beasts, machines, the Husks), not the town's people.</summary>
    static bool Creatures(Occupant o) => o is Occupant.Nesters or Occupant.Prowlers or Occupant.Turned or Occupant.Warped or Occupant.Machines or Occupant.Robots or Occupant.Guardians or Occupant.Cyborgs;

    public static PlaceHolder HolderOf(Occupant o) => o switch
    {
        Occupant.Nesters => PlaceHolder.Nesters,
        Occupant.Prowlers => PlaceHolder.Beasts,
        Occupant.Turned or Occupant.Warped => PlaceHolder.Husks,
        Occupant.Machines or Occupant.Robots or Occupant.Guardians or Occupant.Cyborgs => PlaceHolder.Machines,
        Occupant.Scavengers or Occupant.Squatters or Occupant.Gang or Occupant.Faction or Occupant.Cult or Occupant.Keepers or Occupant.Mimics => PlaceHolder.People,
        _ => PlaceHolder.Empty, // nobody, or the sleepers (still asleep, harming nobody)
    };

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
            _slope = new byte[_size.X * _size.Y * _size.Z];
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
            if (!_inst.Blocks.InBounds(c)) return;
            _known[Index(c.X, c.Y, c.Z)] = 0;
            _slope[Index(c.X, c.Y, c.Z)] = 0;
        }

        readonly byte[] _slope; // 0 not read yet, 1 a whole block or air, 2.. a ramp rising (see Slopes) + 2

        /// <summary>A stair or a ramp: the way it rises as the creatures' space numbers it, or -1 (only in the grid's bounds).</summary>
        public int Slope(int x, int y, int z)
        {
            if (x < _min.X || y < _min.Y || z < _min.Z || x >= _min.X + _size.X || y >= _min.Y + _size.Y || z >= _min.Z + _size.Z) return -1;
            int i = Index(x, y, z);
            byte k = _slope[i];
            if (k == 0)
            {
                Block b = _inst.Blocks.Get(new PInt3(x, y, z));
                PInt3 v = b.Facing.Vec();
                int slope = b.Type is BlockType.Stair or BlockType.Ramp && Get(x, y, z) ? Wildlife.Slopes.Of(v.X, v.Z) : -1;
                _slope[i] = k = (byte)(slope + 2);
            }
            return k - 2;
        }
    }

    SolidCells _cells;

    /// <summary>Blocks of the place changed: its creatures read them afresh.</summary>
    public void Forget(IEnumerable<PInt3> cells)
    {
        if (_cells == null) return;
        foreach (PInt3 c in cells) _cells.Forget(c);
    }

    /// <summary>
    /// Solid to a creature: the place's walls, floors and roofs, its glass and its whole-cell fittings; not air, doors,
    /// rails or lattices, nor the furniture and clutter that fill only part of a cell (those are its
    /// <see cref="PlaceParts"/>, gone round or under as they really stand). Below ground, rock outside the rooms.
    /// </summary>
    public static bool Solid(PoiInstance inst, int x, int y, int z)
    {
        var c = new PInt3(x, y, z);
        Block b = inst.Blocks.Get(c);
        if (b.IsAir) return y <= Assembler.GroundY && !inst.Layout.Occupied[c];
        return PlaceParts.Legacy ? Built(b) : Built(b) && !BlockInfo.IsPart(b.Type);
    }

    /// <summary>A place's space as its creatures live in it: its cells, its stairs as slopes and its parts (tests make their own this way).</summary>
    public static BlockSpace SpaceOf(PlaceMemory m)
    {
        PoiInstance inst = m.Inst;
        BlockGrid g = inst.Blocks;
        return new BlockSpace((x, y, z) => Solid(inst, x, y, z),
            new Vector3I(g.Min.X, g.Min.Y, g.Min.Z), new Vector3I(g.Min.X + g.Size.X, g.Min.Y + g.Size.Y, g.Min.Z + g.Size.Z))
        {
            Slope = (x, y, z) =>
            {
                Block b = g.Get(new PInt3(x, y, z));
                PInt3 v = b.Facing.Vec();
                return b.Type is BlockType.Stair or BlockType.Ramp ? Wildlife.Slopes.Of(v.X, v.Z) : -1;
            },
            Parts = PlaceParts.Of(m),
        };
    }

    /// <summary>Solid to a creature out on the surface: only what was built (walls, floors, roofs, heavy fittings), not the ground round it.</summary>
    public static bool BuiltSolid(PoiInstance inst, PInt3 c)
    {
        Block b = inst.Blocks.Get(c);
        return !b.IsAir && Built(b);
    }

    // Glass is as solid as the pane the player meets (a window's whole cell): a creature never walks through it or is
    // set down in it, half in the wall (places agent). Doors are their leaves (DoorWays); the rest are parts.
    static bool Built(Block b) => BlockInfo.Collides(b.Type) && b.Type is not (BlockType.Door or BlockType.Table or BlockType.Rail or BlockType.Lattice);

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
            new Vector3I(g.Min.X, g.Min.Y, g.Min.Z), new Vector3I(g.Min.X + g.Size.X, g.Min.Y + g.Size.Y, g.Min.Z + g.Size.Z)) { Slope = _cells.Slope, Parts = PlaceParts.Of(memory) };
        lock (deep.Lock) PoiFauna.Spawn(sim, Population, space, Fair(deep, planet, o, space));
        Sim = sim;
        // Those that would have appeared in your sight wait, and are put down once they can be unseen.
        _space = space;
        _origin = o;
        if (Population.Deferred.Count > 0) lock (Pending) Pending.Add(this);
        // Its creatures are bodies no door shuts on.
        memory.Bodies.Add(() => CreatureBodies(sim));
    }

    /// <summary>
    /// Fair spawning, in a place's frame: never within the clearance of the survivor, never behind or
    /// beside them close by, and never where they can see it from where they stand (in their view cone,
    /// with a clear line). Null (anything goes) while nobody is about.
    /// </summary>
    public static Func<Vector3, bool> Fair(Deep deep, PlanetSurface.World.PlanetWorld planet, Vector3 origin, ISolidSpace space)
    {
        if (deep.Eye is not { } v) return null;
        Vector3 eye = (planet != null ? planet.NearestCopy(v.At, origin) : v.At) - origin;
        Vector3 look = new Vector3(v.Look.X, 0f, v.Look.Z);
        look = look.LengthSquared() > 1e-4f ? look.Normalized() : Vector3.Forward;
        SpawnRules rules = deep.Data.Spawning;
        float cone = MathF.Cos(Mathf.DegToRad(rules.ViewConeDegrees));
        return p => Unseen(eye, look, cone, rules.ViewDistance, rules.CaveClearance, space, p);
    }

    /// <summary>Whether a spot is fair to put something down at, seen from an eye looking one way (tests too).</summary>
    public static bool Unseen(Vector3 eye, Vector3 look, float cone, float viewDistance, float clearance, ISolidSpace space, Vector3 p)
    {
        Vector3 body = p + Vector3.Up * 0.6f, to = body - eye;
        float d = to.Length();
        if (d < clearance) return false;
        // Behind or beside you, close: never (you'd turn round into it).
        if (d < 30f && look.Dot(new Vector3(to.X, 0f, to.Z).Normalized()) < 0.3f) return false;
        if (d > viewDistance) return true;
        return !(look.Dot(to / d) > cone && SpaceMath.Clear(space, eye, body, 0f));
    }

    ISolidSpace _space;
    Vector3 _origin;
    /// <summary>Places with spawns still waiting to be put down out of sight.</summary>
    static readonly List<PlaceFauna> Pending = new();

    /// <summary>On the main thread now and then: the waiting spawns of every place put down where you can't see them appear.</summary>
    public static void PutWaiting(Deep deep)
    {
        PlaceFauna[] all;
        lock (Pending) all = Pending.ToArray();
        foreach (PlaceFauna f in all)
        {
            if (f.Sim == null) continue;
            lock (deep.Lock) PoiFauna.PutDeferred(f.Sim, f.Population, f._space, Fair(deep, deep.Planet, f._origin, f._space), 2);
            if (f.Population.Deferred.Count == 0) lock (Pending) Pending.Remove(f);
        }
    }

    /// <summary>A sim's creatures as the doors see them (local feet, radius, height; a climber as the ball it rides in).</summary>
    public static IEnumerable<(Vector3 Feet, float Radius, float Height)> CreatureBodies(WildSim sim)
    {
        foreach (Creature c in sim.Creatures.ToArray())
        {
            if (c.Dead || c.Removed || c.InWalls || c.Space == null) continue;
            if (c.Species.Climbs) { float r = c.Clearance; yield return (Crawl.Center(c) - Vector3.Up * r, r, 2f * r); }
            else yield return (c.Position, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species));
        }
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
    /// <summary>Whether a world point is ground kept clear of caves (round the start bunker): its rock is read as solid.</summary>
    public Func<Vector3, bool> Keep;
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
    /// <summary>At most this many filled caves stand within this far of you: walking a long tunnel doesn't fill it again and again.</summary>
    const int MostNear = 3;
    const float NearCaves = 60f;

    /// <summary>Caves found and filled so far (tests).</summary>
    public int Found => _caves.Count;

    public override void _PhysicsProcess(double delta)
    {
        if (Zone?.Planet == null || Deep == null) return;
        // Where you are and look, for the places filled on workers (fair spawning).
        Deep.Eye = new PlaceFauna.Deep.Viewer(Feet() + Vector3.Up * 1.6f, Look());
        _timer -= delta;
        if (_timer > 0) return;
        _timer = 2.0;
        PlaceFauna.PutWaiting(Deep);
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
        // Repeat spawns capped: while enough caves stand round you, no more are filled.
        int near = 0;
        foreach (Cave c in _caves.Values) if (c.Site != null && planet.NearestCopy(c.Anchor, feet).DistanceTo(feet) < NearCaves) near++;
        if (near >= MostNear) return;
        _searchedFrom = feet;
        float surface = Zone.GroundAt(feet.X, feet.Z).Y;
        var anchor = new Vector3(MathF.Round(feet.X), MathF.Round(feet.Y), MathF.Round(feet.Z));
        var space = new LoadedVoxels(planet.Terrain.Store, Zone.Streamer, new Vector3I((int)anchor.X, (int)anchor.Y, (int)anchor.Z), Keep);
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
        // Never beside or behind you, never in your sight (PlaceFauna.Unseen).
        bool Unseen(Vector3 p) => PlaceFauna.Unseen(eye, look, cone, rules.ViewDistance, rules.CaveClearance, space, p);
        List<Creature> made;
        lock (Deep.Lock) made = CaveFauna.Populate(cave.Sim, space, Deep.Fauna, floor + Vector3.Up, 16f, seed, depth, crawlers: 1, subsisters: (int)MathF.Round(3f * Deep.Livable), allow: Unseen, dark: Deep.Life.Dark);
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
        readonly Func<Vector3, bool> _keep;

        public LoadedVoxels(TD.ChunkStore store, TerrainStreamer streamer, Vector3I offset, Func<Vector3, bool> keep = null)
        {
            _store = store;
            _streamer = streamer;
            _offset = offset;
            _keep = keep;
        }

        bool Kept(float x, float y, float z) => _keep != null && _keep(new Vector3(x + _offset.X, y + _offset.Y, z + _offset.Z));

        bool Loaded(TD.Int3 c) => _streamer.IsReady(TD.ChunkStore.ChunkOf(c));

        public float Density(Vector3 p)
        {
            var w = new System.Numerics.Vector3(p.X + _offset.X, p.Y + _offset.Y, p.Z + _offset.Z);
            return Loaded(TD.Int3.Floor(w)) && !Kept(p.X, p.Y, p.Z) ? _store.SampleDensity(w) : 1f;
        }

        public bool SolidCell(int x, int y, int z)
        {
            var c = new TD.Int3(x + _offset.X, y + _offset.Y, z + _offset.Z);
            return !Loaded(c) || Kept(x + 0.5f, y + 0.5f, z + 0.5f) || _store.IsSolid(c);
        }
    }
}
