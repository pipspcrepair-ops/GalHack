using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Items;
using GalHack.Physics;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Items;
using Slice.Surface;
using Layers = GalHack.Physics.Layers;

namespace Slice.Places;

/// <summary>
/// The planet's places as part of the surface world you walk in. planet-surface's POI layer
/// draws every place on the planet (solid, on the world layer, so a vessel lands on a roof and
/// nothing flies through a wall); a place within <see cref="BuildRadius"/> of the focus is stood
/// up here as a real structure (<see cref="Place"/>): doors, keycards, caches, hidden rooms,
/// ladders, lifts, its rooms dressed, its creatures and people. The build runs on a worker and
/// then a few milliseconds a frame, so it never hitches, and finishes long before you are close
/// enough to see in; only then does the far drawing's block mesh give way to it, in the same
/// frame, and the two are the same blocks. There is no scene of its own and no swap at a door:
/// inside a place you are on the planet, under its sky, among its creatures, and digging in
/// through the ground or a wall reaches the same rooms.
/// </summary>
public partial class PoiPlaces : Node3D
{
    public SurfaceZone Zone;
    public Func<Vector3> Focus = () => Vector3.Zero;

    /// <summary>Whether the survivor carries a keycard of at least this clearance (the game asks the bag).</summary>
    public Func<string, bool> Holds = _ => false;

    /// <summary>Places within this of the focus (to the nearest edge of their footprint) stand as structures.</summary>
    public float BuildRadius = 320f;

    /// <summary>Beyond this they go back to the far drawing (or, changed, stay as drawn blocks only).</summary>
    public float DropRadius = 460f;

    /// <summary>Main-thread time a frame may spend standing a place up.</summary>
    public double BudgetMs = 2.5;

    /// <summary>The focus's own speed and way, m/s (the game sets it). A place stands this many seconds of travel before <see cref="BuildRadius"/>, and the way you are going comes first.</summary>
    public Vector3 Heading;
    public float AheadSeconds = 6f;

    /// <summary>The most that reach comes to, m: past it a place is only its far drawing, and flying over it needs no more.</summary>
    const float MostReach = 760f;

    /// <summary>The longest single build step and the most build work in one frame so far, ms (tests).</summary>
    public double WorstStepMs { get; private set; }
    public double WorstFrameMs { get; private set; }

    /// <summary>The longest step of each build stage (blocks, fixtures, caches, dressing, creatures, people), ms.</summary>
    public readonly Dictionary<string, double> StageWorst = new();

    /// <summary>Steps over 3 ms (tests: where a hitch would come from).</summary>
    public readonly List<(string Place, string Stage, double Ms, bool Collected)> SlowSteps = new();

    /// <summary>Places built so far and the seconds each took from start to standing (tests).</summary>
    public readonly List<(string Name, double Seconds, int Chunks)> BuildLog = new();

    public readonly List<Place> Standing = new();
    readonly Dictionary<PoiSite, PlaceMemory> _memory = new();
    readonly Dictionary<PoiSite, Place> _placeOf = new();
    readonly Dictionary<ulong, Place> _byBody = new();
    double _scan;

    public PlaceMemory MemoryOf(PoiSite site)
    {
        if (!_memory.TryGetValue(site, out PlaceMemory m)) _memory[site] = m = new PlaceMemory(site);
        return m;
    }

    public Place PlaceOf(PoiSite site) => _placeOf.TryGetValue(site, out Place p) ? p : null;

    /// <summary>The standing place a collider belongs to (its blocks, a door leaf, a lift car, a deco piece), or null.</summary>
    public Place PlaceOf(GodotObject collider)
    {
        if (collider is not Node n) return null;
        for (Node at = n; at != null && at != this; at = at.GetParent())
            if (at is Place p) return p.Ready ? p : null;
        return null;
    }

    public bool HasClearance(string clearance) => clearance == null || Holds(clearance);

    // ---- Standing places up, and taking them down -----------------------------------------------

    sealed class Job
    {
        public Place Place;
        public Task<List<ChunkMesh>> Meshing;
        public IEnumerator<int> Steps;
        public string Stage = "meshing";
        public Stopwatch Clock;
    }

    Job _job;

    /// <summary>The place being stood up just now, if any (measurements).</summary>
    public string Building => _job?.Place.Site.Name;

    /// <summary>Starts the worst-frame figures over (measurements).</summary>
    public void ResetWorst() => WorstStepMs = WorstFrameMs = 0;

    /// <summary>The distance from a point to a site's footprint, on the planet (nearest copy).</summary>
    public float DistanceTo(PoiSite site, Vector3 p)
    {
        var lo = site.Footprint.Min;
        var hi = site.Footprint.Max;
        var c = Zone.Planet.NearestCopy(new Vector3((lo.X + hi.X + 1) * 0.5f, 0f, (lo.Z + hi.Z + 1) * 0.5f), p);
        float hx = (hi.X - lo.X + 1) * 0.5f, hz = (hi.Z - lo.Z + 1) * 0.5f;
        float dx = MathF.Max(0f, MathF.Abs(p.X - c.X) - hx), dz = MathF.Max(0f, MathF.Abs(p.Z - c.Z) - hz);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public override void _Ready() => ProcessPhysicsPriority = 60; // after the zone's frame shift (50)

    public override void _PhysicsProcess(double delta)
    {
        if (Zone?.Planet == null) return;
        Vector3 focus = Focus();
        foreach (Place p in Standing) Keep(p, focus);
        if (_job != null) Keep(_job.Place, focus);
    }

    /// <summary>
    /// A place on the copy of the planet nearest the focus. Written only when that changes: setting a
    /// position tells every collider and mesh under the node it moved, even to the same spot.
    /// </summary>
    void Keep(Place p, Vector3 focus)
    {
        Vector3 at = Zone.Planet.NearestCopy(p.Origin, focus);
        if (p.Position != at) p.Position = at;
    }

    public override void _Process(double delta)
    {
        if (Zone?.Planet == null) return;
        Vector3 focus = Focus();
        _scan -= delta;
        if (_scan <= 0)
        {
            _scan = 0.5;
            Scan(focus);
        }
        if (_job != null) Work();
    }

    /// <summary>How far off a place is started at the speed you are going.</summary>
    float Reach => MathF.Min(BuildRadius + Heading.Length() * AheadSeconds, MostReach);

    /// <summary>How much a site is worth starting now: the lower the sooner. Null: not now (out of reach, or behind you and receding).</summary>
    float? Urgency(PoiSite site, Vector3 focus)
    {
        float d = DistanceTo(site, focus);
        if (d >= Reach) return null;
        float speed = Heading.Length();
        if (speed < 3f) return d;
        var lo = site.Footprint.Min;
        var hi = site.Footprint.Max;
        Vector3 to = Zone.Planet.NearestCopy(new Vector3((lo.X + hi.X + 1) * 0.5f, 0f, (lo.Z + hi.Z + 1) * 0.5f), focus) - focus;
        float along = (to.X * Heading.X + to.Z * Heading.Z) / speed;
        if (along < 0f && d > BuildRadius * 0.5f) return null;
        return d - along * 0.5f;
    }

    /// <summary>The site to start next, leaving out one (the place the worker is ahead on).</summary>
    PoiSite Next(Vector3 focus, PoiSite besides = null)
    {
        PoiSite next = null;
        float best = float.MaxValue;
        foreach (PoiSite site in Zone.Planet.Pois)
        {
            if (site.Instance == null || site == besides || _placeOf.ContainsKey(site)) continue;
            if (Urgency(site, focus) is float u && u < best) (best, next) = (u, site);
        }
        return next;
    }

    /// <summary>Starts the nearest place within reach that isn't standing; drops the ones out of reach.</summary>
    void Scan(Vector3 focus)
    {
        foreach (Place p in Standing.ToList())
        {
            // (A place stood up while the planet was still loading: its far drawing arrived after it.)
            if (!p.FarHidden) p.FarHidden = ShowFar(p.Site, false);
            float d = DistanceTo(p.Site, focus);
            if (d <= MathF.Max(DropRadius, Reach + 140f)) { p.SetDormant(false); continue; }
            if (p.Memory.Modified) { p.SetDormant(true); continue; }
            Drop(p);
        }
        if (_job != null)
        {
            // The worker gets on with the place after this one, so it starts with its slow half done.
            if (_ahead == null && _job.Meshing.IsCompleted && Next(focus, _job.Place.Site) is PoiSite after)
            {
                _ahead = after;
                _aheadMeshing = Meshing(MemoryOf(after));
            }
            return;
        }
        // One worker's job at a time: what they read of the catalogues is made once, and not for two at once.
        if (_ahead != null && !_aheadMeshing.IsCompleted) return;
        PoiSite next = Next(focus);
        if (next != null) Begin(next);
        else _ahead = null;
    }

    PoiSite _ahead;
    Task<List<ChunkMesh>> _aheadMeshing;

    Task<List<ChunkMesh>> Meshing(PlaceMemory memory)
    {
        Decor decor = Decor.Shared; // loaded on the main thread the first time
        PlaceFauna.Deep deep = Fauna;
        return Task.Run(() =>
        {
            Prepare(memory, decor, deep);
            BlockGrid g = memory.Inst.Blocks;
            return Mesher.Build(g, memory.Inst.Layout.Occupied, Place.Options(g.Min));
        });
    }

    void Begin(PoiSite site)
    {
        PlaceMemory memory = MemoryOf(site);
        Place place = Place.Create(this, memory);
        place.Position = Zone.Planet.NearestCopy(place.Origin, Focus());
        AddChild(place);
        _placeOf[site] = place;
        _job = new Job { Place = place, Clock = Stopwatch.StartNew(), Meshing = _ahead == site ? _aheadMeshing : Meshing(memory) };
        _ahead = null;
        _aheadMeshing = null;
    }

    /// <summary>What a place needs worked out once, off the main thread: its rooms' dressing (clearing the generic props there) and its creatures.</summary>
    static void Prepare(PlaceMemory memory, Decor decor, PlaceFauna.Deep deep)
    {
        if (memory.Prepared) return;
        memory.Rooms = decor?.Plan(memory) ?? new Dictionary<int, PlaceRoom>();
        if (deep != null) memory.Fauna = PlaceFauna.Plan(memory, deep);
        memory.Fauna?.Populate(memory, deep);
        memory.Town = Town.For(memory);
        memory.Prepared = true;
    }

    /// <summary>One frame's share of the build, within the budget.</summary>
    void Work()
    {
        var frame = Stopwatch.StartNew();
        Job j = _job;
        while (frame.Elapsed.TotalMilliseconds < BudgetMs)
        {
            var step = Stopwatch.StartNew();
            int collections = GC.CollectionCount(0);
            string stage = j.Stage;
            bool more = Step(j);
            double ms = step.Elapsed.TotalMilliseconds;
            if (ms > 3.0) SlowSteps.Add((j.Place.Site.Name, stage, ms, GC.CollectionCount(0) > collections));
            // A garbage collection landing in a step is the runtime's, not the build's: counted apart.
            if (GC.CollectionCount(0) > collections) { frame.Restart(); continue; }
            WorstStepMs = Math.Max(WorstStepMs, ms);
            StageWorst[stage] = Math.Max(StageWorst.GetValueOrDefault(stage), ms);
            if (!more || _job != j) break;
        }
        WorstFrameMs = Math.Max(WorstFrameMs, frame.Elapsed.TotalMilliseconds);
    }

    /// <summary>One piece of main-thread work; false once nothing more can be done this frame.</summary>
    bool Step(Job j)
    {
        if (j.Steps == null)
        {
            if (!j.Meshing.IsCompleted) return false;
            if (j.Meshing.IsFaulted)
            {
                GD.PushError($"place {j.Place.Site.Name} failed to build: {j.Meshing.Exception?.GetBaseException()}");
                _job = null;
                return false;
            }
            j.Steps = Build(j, j.Meshing.Result).GetEnumerator();
        }
        if (j.Steps.MoveNext()) return true;
        Finish(j);
        return false;
    }

    /// <summary>The main thread's work to stand a place up, in small steps (each a millisecond or two).</summary>
    IEnumerable<int> Build(Job j, List<ChunkMesh> chunks)
    {
        Place p = j.Place;
        j.Stage = "blocks";
        foreach (ChunkMesh c in chunks)
        {
            p.SetChunk(c);
            yield return 0;
        }
        j.Stage = "fixtures";
        foreach (int x in p.BuildFixtures()) yield return x;
        j.Stage = "caches";
        foreach (int x in MakeCaches(p)) yield return x;
        j.Stage = "dressing";
        foreach (int x in p.Dress()) yield return x;
        j.Stage = "creatures";
        p.Memory.Fauna?.Stand(p, this);
        yield return 0;
        j.Stage = "people";
        p.Memory.Town?.Stand(p, this);
        yield return 0;
    }
    void Finish(Job j)
    {
        Place p = j.Place;
        p.Ready = true;
        Standing.Add(p);
        p.FarHidden = ShowFar(p.Site, false);
        BuildLog.Add((p.Site.Name, j.Clock.Elapsed.TotalSeconds, p.ChunkCount));
        _job = null;
        _scan = 0; // the next one starts now, not at the next look round
        Stood?.Invoke(p);
    }

    /// <summary>A place finished standing up (tests, the game's messages).</summary>
    public event Action<Place> Stood;

    void Drop(Place p)
    {
        p.Memory.Fauna?.Sit(p);
        p.Memory.Town?.Sit(p);
        Standing.Remove(p);
        _placeOf.Remove(p.Site);
        ShowFar(p.Site, true);
        p.QueueFree();
    }

    /// <summary>The far drawing's blocks (and its own dressing) shown or hidden; its lamps, glows and smoke always stay.</summary>
    bool ShowFar(PoiSite site, bool show)
    {
        Node3D far = Zone.Pois.NodeOf(site);
        if (far == null) return false;
        foreach (Node n in far.GetChildren())
        {
            string name = n.Name.ToString();
            if (n is CollisionObject3D body) body.ProcessMode = show ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
            else if (n is Node3D n3 && (name.StartsWith("Chunk") || name == "Deco")) n3.Visible = show;
        }
        return true;
    }

    /// <summary>
    /// Stands the nearest place once, all at once, while the game is still loading (the castaway
    /// asleep): the first time anything is built costs the most (the runtime compiles its code then),
    /// so that is paid here and never in play. A place out of reach is taken down again at once.
    /// </summary>
    public void WarmUp()
    {
        Vector3 from = Focus();
        PoiSite nearest = Zone.Planet.Pois.Where(s => s.Instance != null).OrderBy(s => DistanceTo(s, from)).FirstOrDefault();
        if (nearest == null) return;
        var clock = Stopwatch.StartNew();
        Begin(nearest);
        _job.Meshing.Wait();
        while (_job != null && Step(_job)) { }
        if (PlaceOf(nearest) is Place p && DistanceTo(nearest, from) > DropRadius) Drop(p);
        WarmUpMs = clock.Elapsed.TotalMilliseconds;
        WorstStepMs = WorstFrameMs = 0;
        StageWorst.Clear();
        SlowSteps.Clear();
        BuildLog.Clear();
    }

    /// <summary>What the warm-up took, ms (tests).</summary>
    public double WarmUpMs { get; private set; }

    /// <summary>Every place within reach stands right now (tests and screenshots that place the camera by hand).</summary>
    public void StandAll(double timeoutSeconds = 60)
    {
        var watch = Stopwatch.StartNew();
        Vector3 focus = Focus();
        while (watch.Elapsed.TotalSeconds < timeoutSeconds)
        {
            if (_job == null) Scan(focus);
            if (_job == null) return;
            if (_job.Steps == null) _job.Meshing.Wait();
            while (_job != null && Step(_job)) { }
        }
    }

    // ---- Caches ---------------------------------------------------------------------------------

    /// <summary>Each cache as a container in the zone, holding what the generator rolled for it (made once; kept for the game).</summary>
    IEnumerable<int> MakeCaches(Place p)
    {
        PlaceMemory m = p.Memory;
        ItemCatalogue items = Zone.Data.Items;
        int[] size = Zone.Data.Rules.Containers.Corpse;
        foreach (LootCache c in m.Inst.Caches)
        {
            if (m.Caches.ContainsKey(c)) continue;
            var bill = new Bill();
            int k = 0;
            foreach (LootItem li in c.Items)
            {
                if (!items.TryGet(li.Id, out ItemDef def)) continue;
                bill.Add(def.Id, CountOf(def, c.Tier, Hash.Cell(c.Cell, (uint)(++k * 31))));
            }
            string name = c.Secret ? "Hidden stash" : c.InVault ? "Vault cache" : "Cache";
            Lootable loot = Zone.Loot.Create(LootKind.Cache, name, p.WorldOf(c.Cell, 0.05f), Math.Max(4, size[0]), Math.Max(3, size[1]), bill);
            loot.Radius = 0.55f;
            m.Caches[c] = loot;
            yield return 0;
        }
    }

    /// <summary>How many of an item one roll gives: a handful of materials, a few parts, one tool or weapon.</summary>
    internal static int CountOf(ItemDef def, int tier, uint hash)
    {
        int roll = (int)(hash % 1000);
        return def.Category switch
        {
            ItemCategory.Raw or ItemCategory.Refined => Math.Min(def.StackSize, 4 + roll % 9 + tier * 2),
            ItemCategory.Part => Math.Min(def.StackSize, 2 + roll % 4 + tier),
            ItemCategory.Component => 1 + roll % 3,
            ItemCategory.Consumable or ItemCategory.Ammo => 1 + roll % 3,
            _ => 1,
        };
    }

    // ---- What a ray met ---------------------------------------------------------------------------

    /// <summary>What a look ray's hit is, in a place: the place, the cell just inside the face, and a door or deco piece if it was one.</summary>
    public readonly record struct Hit(Place Place, Int3 Cell, Place.Door Door, DecoHit Piece);

    public bool Resolve(GodotObject collider, Vector3 point, Vector3 normal, out Hit hit)
    {
        hit = default;
        Place p = PlaceOf(collider);
        if (p == null) return false;
        Place.Door door = p.DoorOf(collider);
        DecoHit piece = p.PieceOf(collider);
        Int3 cell = p.CellAt(point - normal * 0.05f);
        hit = new Hit(p, cell, door, piece);
        return true;
    }

    // ---- Climbing ---------------------------------------------------------------------------------

    /// <summary>
    /// What holds the survivor up at their feet: a ladder in the cell at their feet or just below
    /// (so the top rung carries them over), or a lift shaft; and the place and lift.
    /// </summary>
    public ClimbMode ClimbAt(Vector3 feet, out Place place, out int lift)
    {
        place = null;
        lift = -1;
        foreach (Place p in Standing)
        {
            if (p.Dormant) continue;
            Vector3 local = p.FromWorld(feet);
            Int3 up = new(Mathf.FloorToInt(local.X), Mathf.FloorToInt(local.Y + 0.1f), Mathf.FloorToInt(local.Z));
            Int3 below = up with { Y = Mathf.FloorToInt(local.Y - 0.45f) };
            if (!p.Inst.Blocks.InBounds(up) && !p.Inst.Blocks.InBounds(below)) continue;
            int shaft = p.ShaftAt(local);
            if (shaft >= 0)
            {
                place = p;
                lift = shaft;
                return ClimbMode.Shaft;
            }
            if (p.Inst.Blocks.Get(up).Type == BlockType.Ladder || p.Inst.Blocks.Get(below).Type == BlockType.Ladder)
            {
                place = p;
                return ClimbMode.Ladder;
            }
        }
        return ClimbMode.None;
    }

    /// <summary>The standing place whose grid holds a world point, if any.</summary>
    public Place PlaceAt(Vector3 world)
    {
        foreach (Place p in Standing)
            if (p.Inst.Blocks.InBounds(p.CellAt(world))) return p;
        return null;
    }

    // ---- The creatures' side ------------------------------------------------------------------------

    /// <summary>The deep fauna of this planet (POI and cave creatures), made once with the planet's own species.</summary>
    public PlaceFauna.Deep Fauna;
}
