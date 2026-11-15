using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using PoiRoom = Wildlife.PoiRoom;
using Int3 = GalHack.Structures.Int3;
using Block = GalHack.Structures.Block;

namespace Slice.Places;

/// <summary>
/// The crashed capital ships on the planet, as real vessels. A hulk's place (poi-generator's CapitalHull) is laid
/// out round a vessel plan: the ground cut away where the hull lies in it, its ramps and its yard. When the sleeper
/// comes within reach, each piece of the plan is built of the player's own blocks (HullVessel: its rooms dressed and
/// lit, damaged as the place's state and the crash say) on a worker, then set down in the hollow as a coreless wreck,
/// pitched as it came down. Its containers hold the salvage. A core set into one claims it; fixed, it lifts off and
/// flies. They stay once set down: nothing of a wreck is taken back while the game runs.
/// </summary>
public partial class Hulks : Node
{
    public SurfaceZone Zone;
    public Func<Vector3> Focus = () => Vector3.Zero;
    public PoiPlaces Places;

    /// <summary>How near a hulk's place the sleeper comes before its vessel is built and set down, m.</summary>
    public float Reach = 1500f;

    /// <summary>Each crash site's vessels, set down.</summary>
    public readonly Dictionary<PoiSite, List<Vessel>> Down = new();

    /// <summary>How long the last set-down took on the main thread (its pieces built and their collision worked out on a worker before), ms.</summary>
    public double LastSetDownMs { get; private set; }

    /// <summary>A hulk's vessel was set down.</summary>
    public event Action<PoiSite, Vessel> Set;

    /// <summary>Hulks being built off the main thread, by site.</summary>
    readonly Dictionary<PoiSite, System.Threading.Tasks.Task<List<HullVessel.Result>>> _building = new();

    int _tick;

    /// <summary>Wrecks set down still being dressed, a few pieces a tick.</summary>
    readonly Queue<IEnumerator<int>> _dressing = new();

    /// <summary>Every wreck set down is dressed (tests: now, all of it).</summary>
    public void DressAll()
    {
        while (_dressing.TryDequeue(out var steps)) while (steps.MoveNext()) { }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Zone?.Planet == null) return;
        LiftClear();
        if (_dressing.TryPeek(out var dressing) && !dressing.MoveNext()) _dressing.Dequeue();
        foreach (var (site, task) in _building.Where(kv => kv.Value.IsCompleted).ToList())
        {
            _building.Remove(site);
            if (task.IsFaulted)
            {
                GD.PushError($"hulk at {site.Origin}: could not be built: {task.Exception?.InnerException}");
                Down[site] = new List<Vessel>();
                continue;
            }
            SetDown(site, task.Result);
            return; // one a tick at most: each is tens of thousands of blocks
        }
        if (++_tick % 30 != 0) return;
        Vector3 at = Focus();
        foreach (PoiSite site in Zone.Planet.Pois)
        {
            if (site.Layout == null || Down.ContainsKey(site) || _building.ContainsKey(site) || CapitalHull.Of(site.Layout) == null) continue;
            Vector3 o = Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), at);
            if (new Vector2(o.X - at.X, o.Z - at.Z).Length() > Reach) continue;
            CapitalHull.HullPlan plan = CapitalHull.Of(site.Layout);
            PoiState state = site.State;
            ulong seed = site.Layout.Seed;
            Deco(); // (its dresser made here, on the main thread)
            _building[site] = System.Threading.Tasks.Task.Run(() => HullSpace.Of(plan, state, seed).Pieces);
        }
    }

    /// <summary>Every piece of a hulk's plan as a wreck, its dressing's looks made ready (worker-safe).</summary>
    public static List<HullVessel.Result> Build(CapitalHull.HullPlan plan, Catalogue catalogue, PoiState state, ulong seed, GalHack.Deco.DecoPoi deco)
    {
        var pieces = plan.Pieces.Select((piece, i) => HullVessel.Build(plan, piece, catalogue, state, seed + (ulong)i * 7919UL, deco)).ToList();
        if (Decor.Shared?.Meshes is { } meshes)
            foreach (HullVessel.Dressing d in pieces.SelectMany(p => p.Decor)) meshes.Prepare(d.Look);
        foreach (HullVessel.Result r in pieces) r.Colliders = PhysicsSandbox.Bridges.StructureBridge.Colliders(r.Structure);
        return pieces;
    }

    readonly HashSet<(Vessel, ulong)> _excepted = new();

    /// <summary>
    /// A hulk lifting off rises through its own crash site: the ramps and cuttings dug to its ways in stand flush
    /// against its flanks, so while it climbs they are no obstacle to it (the ground itself is cut as the lift-off
    /// plans). Once it is clear they meet it again.
    /// </summary>
    void LiftClear()
    {
        foreach (var (site, vessels) in Down)
            foreach (Vessel v in vessels)
            {
                if (v.Lifting == null || v.Node is not PhysicsBody3D hull) continue;
                bool climbing = v.Lifting.Phase != CapitalBase.World.LiftOffPhase.Done;
                if (Places?.PlaceOf(site) is not { } place) continue;
                foreach (Node n in place.GetChildren())
                {
                    if (n is not CollisionObject3D body) continue;
                    var key = (v, body.GetInstanceId());
                    if (climbing && _excepted.Add(key)) hull.AddCollisionExceptionWith(body);
                    else if (!climbing && _excepted.Remove(key) && IsInstanceValid(body)) hull.RemoveCollisionExceptionWith(body);
                }
            }
    }

    /// <summary>Builds and sets down a crash site's vessels now (also for tests).</summary>
    public List<Vessel> SetDown(PoiSite site) =>
        Down.TryGetValue(site, out var done) ? done : SetDown(site, CapitalHull.Of(site.Layout) is { } plan ? HullSpace.Of(plan, site.State, site.Layout.Seed).Pieces : new());

    static GalHack.Deco.DecoPoi _deco;

    /// <summary>The hulks' own dresser (its planner is its own; the library's pieces and looks are shared), or null without the library.</summary>
    public static GalHack.Deco.DecoPoi Deco() =>
        Decor.Shared is not { } d ? null : _deco?.Forge == d.Forge ? _deco : _deco = new GalHack.Deco.DecoPoi(d.Forge, d.Poi.Arrangements);

    List<Vessel> SetDown(PoiSite site, List<HullVessel.Result> pieces)
    {
        if (Down.TryGetValue(site, out var done)) return done;
        var list = new List<Vessel>();
        Down[site] = list;
        Vector3 origin = Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), Focus());
        ulong seed = site.Layout.Seed;
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (HullVessel.Result r in pieces)
        {
            CapitalHull.HullPiece piece = r.Piece;
            var at = (origin + new Vector3(piece.Origin.X, piece.Origin.Y, piece.Origin.Z)).ToNumerics();
            Vessel v = Vessel.SpawnGroundWreck(Zone, r.Structure, at, piece.Rotation, r.Colliders);
            v.Derelict = new Slice.Orbit.DerelictPlan { Id = $"hulk-{site.Origin.X}-{site.Origin.Z}-{list.Count}", Name = r.Structure.Name, Structure = r.Structure, Position = at, Rotation = piece.Rotation, Door = r.Door, Hull = r };
            _dressing.Enqueue(v.DressSteps(r.Decor).GetEnumerator());
            Stock(v, rng, site.State);
            list.Add(v);
            Set?.Invoke(site, v);
        }
        GD.Print($"hulk: {site.Family.Name} '{site.Layout.Variant?.Name}' ({site.State}) set down as {list.Count} wreck(s), {list.Sum(v => v.Structure.Count)} blocks, {pieces.Sum(p => p.Decor.Count)} pieces of dressing, {clock.Elapsed.TotalMilliseconds:F0} ms on the main thread");
        LastSetDownMs = clock.Elapsed.TotalMilliseconds;
        return list;
    }

    /// <summary>The holds' salvage: a few rolls of the wreck tables for the containers that came through, fewer the worse its state.</summary>
    static void Stock(Vessel v, Random rng, PoiState state)
    {
        var items = v.Zone.Data.Items;
        int containers = v.Structure.Blocks().Count(b => v.Structure.TypeOf(b.block).IsContainer);
        int rolls = Math.Min(24, containers / 12 + 2) * (state == PoiState.Ruined ? 1 : 2) / 2;
        var bill = new GalHack.Items.Bill();
        for (int i = 0; i < rolls; i++)
            foreach (var (item, least, most) in Slice.Orbit.OrbitRules.Shared.LootFor(i % 4 == 0 ? "military" : "salvage"))
            {
                if (!items.Has(item)) continue;
                int n = rng.Next(least, most + 1);
                if (n > 0) bill.Add(item, n);
            }
        if (!bill.IsEmpty) v.Storage.AddAll(bill);
    }
}

/// <summary>
/// A crashed capital hulk's vessel as what lives in it meets it, in its place's frame (worker-safe, nothing set down
/// yet): the hull's pieces built once (<see cref="HullVessel"/>, the same wrecks <see cref="Hulks"/> sets down), each
/// lying turned and pitched as it came down, read a metre cell at a time; and their spaces as the population function
/// sees them: the holds, the engine room and the crawlways the nesters take, the rooms and the decks' corridors.
/// </summary>
public sealed class HullSpace
{
    /// <summary>The hull's pieces as built: what the creatures meet is what is set down.</summary>
    public readonly List<HullVessel.Result> Pieces;
    /// <summary>
    /// Each piece as built, read once here on the worker before anything is set down: the cells that stop a body and its
    /// bounds. Once set down a wreck's structure is the main thread's to change (salvage, building, repair), so nothing
    /// here reads it again: what lives in it meets the wreck as it came down.
    /// </summary>
    readonly List<(HashSet<Int3> Solid, Int3 Lo, Int3 Hi, CapitalHull.HullPiece Piece)> _lying = new();
    /// <summary>The cells any piece covers, in the place's frame (max exclusive).</summary>
    public Vector3I Min { get; } = new(int.MaxValue, int.MaxValue, int.MaxValue);
    public Vector3I Max { get; } = new(int.MinValue, int.MinValue, int.MinValue);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CapitalHull.HullPlan, HullSpace> Known = new();

    /// <summary>The hulk's vessel of a place (built the first time it is asked for), or null where the place is no hulk.</summary>
    public static HullSpace Of(PoiInstance inst) => CapitalHull.Of(inst.Layout) is { } plan ? Of(plan, inst.State, inst.Layout.Seed) : null;

    /// <summary>A hull plan's vessel, built once whoever asks first (the place's creatures or the hulk set down).</summary>
    public static HullSpace Of(CapitalHull.HullPlan plan, PoiState state, ulong seed)
    {
        lock (Known) return Known.GetValue(plan, _ => new HullSpace(plan, state, seed));
    }

    HullSpace(CapitalHull.HullPlan plan, PoiState state, ulong seed)
    {
        Pieces = Hulks.Build(plan, Slice.Game.GameData.Shared.Catalogue, state, seed, Hulks.Deco());
        foreach (HullVessel.Result r in Pieces)
        {
            Structure st = r.Structure;
            var (lo, hi) = st.Bounds();
            // Doors, chairs and stairs are gone up or through.
            _lying.Add((st.Blocks().Where(b => st.TypeOf(b.block) is var t && t.Collide != Collide.None && !t.IsDoor && t.Function is not ("chair" or "stairs"))
                .Select(b => b.pos).ToHashSet(), lo, hi, r.Piece));
            for (int c = 0; c < 8; c++)
            {
                N.Vector3 at = Place(r, new N.Vector3((c & 1) == 0 ? lo.X - 0.5f : hi.X + 0.5f, (c & 2) == 0 ? lo.Y - 0.5f : hi.Y + 0.5f, (c & 4) == 0 ? lo.Z - 0.5f : hi.Z + 0.5f));
                Min = new Vector3I(Math.Min(Min.X, (int)MathF.Floor(at.X)), Math.Min(Min.Y, (int)MathF.Floor(at.Y)), Math.Min(Min.Z, (int)MathF.Floor(at.Z)));
                Max = new Vector3I(Math.Max(Max.X, (int)MathF.Ceiling(at.X)), Math.Max(Max.Y, (int)MathF.Ceiling(at.Y)), Math.Max(Max.Z, (int)MathF.Ceiling(at.Z)));
            }
        }
    }

    /// <summary>A point of a piece's structure frame (a cell's middle is its coordinates, metres) in the place's frame.</summary>
    static N.Vector3 Place(HullVessel.Result r, N.Vector3 local) => r.Piece.Origin + N.Vector3.Transform(local, r.Piece.Rotation);

    /// <summary>A metre cell of the place's frame: solid (a block that stops a body), open (inside a piece's bounds, nothing standing there), or null (no piece there).</summary>
    public bool? Cell(int x, int y, int z)
    {
        if (x < Min.X || y < Min.Y || z < Min.Z || x >= Max.X || y >= Max.Y || z >= Max.Z) return null;
        var mid = new N.Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
        foreach (var (solid, lo, hi, piece) in _lying)
        {
            var p = Int3.Round(N.Vector3.Transform(mid - piece.Origin, N.Quaternion.Inverse(piece.Rotation)));
            if (p.X < lo.X || p.Y < lo.Y || p.Z < lo.Z || p.X > hi.X || p.Y > hi.Y || p.Z > hi.Z) continue;
            return solid.Contains(p);
        }
        return null;
    }

    /// <summary>
    /// The pieces' spaces as the population function sees them, in the place's frame (the box each lies in, as it lies),
    /// indexed from <paramref name="first"/> and linked through their doors, stair cores and halls' walkways, their
    /// storeys their decks (the boarding door opens on deck 0). Dark: a wreck has no power.
    /// </summary>
    public List<PoiRoom> Rooms(int first)
    {
        var list = new List<PoiRoom>();
        foreach (HullVessel.Result r in Pieces)
        {
            int base_ = list.Count;
            var spaces = r.Spaces.Where(s => s.Floor.Count >= 4).ToList();
            foreach (HullVessel.Space sp in spaces)
            {
                // Its box in the structure frame: its floor cells' span, from its walking level up its headroom.
                var bmin = new N.Vector3(sp.U0 - 0.5f, sp.Y - 0.5f, sp.V0 - r.Piece.V0 - 0.5f);
                var bmax = new N.Vector3(sp.U1 + 0.5f, sp.Y - 0.5f + sp.Height, sp.V1 - r.Piece.V0 + 0.5f);
                var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var hi = -lo;
                for (int c = 0; c < 8; c++)
                {
                    N.Vector3 w = Place(r, new N.Vector3((c & 1) == 0 ? bmin.X : bmax.X, (c & 2) == 0 ? bmin.Y : bmax.Y, (c & 4) == 0 ? bmin.Z : bmax.Z));
                    lo = lo.Min(new Vector3(w.X, w.Y, w.Z));
                    hi = hi.Max(new Vector3(w.X, w.Y, w.Z));
                }
                // Pitched, its floor stands at its middle's height (the box's lowest edge is under the deck).
                float floor = Place(r, ((bmin + bmax) * 0.5f) with { Y = bmin.Y }).Y;
                ModuleKind kind = sp.Hall ? ModuleKind.Hall : sp.Purpose == "stair" ? ModuleKind.Stair : sp.Corridor ? ModuleKind.Corridor : ModuleKind.Room;
                list.Add(new PoiRoom
                {
                    Index = first + list.Count, Kind = PlaceFauna.KindOf(sp.Purpose, kind, true), Purpose = sp.Purpose,
                    Min = lo with { Y = MathF.Min(floor, hi.Y - 2f) }, Max = hi, Storey = sp.Deck, Depth = 0, Dark = true,
                });
            }
            void Link(int a, int b)
            {
                if (a == b || a < 0 || b < 0 || list[base_ + a].Links.Contains(first + base_ + b)) return;
                list[base_ + a].Links.Add(first + base_ + b);
                list[base_ + b].Links.Add(first + base_ + a);
            }
            int At(int deck, (int U, int V) c) => spaces.FindIndex(s => s.Deck == deck && s.Floor.Contains(c));
            for (int i = 0; i < spaces.Count; i++)
            {
                HullVessel.Space sp = spaces[i];
                // Through each doorway, to the space beyond its wall.
                foreach (var d in sp.Doors) Link(i, At(sp.Deck, (2 * d.Wall.U - d.Inside.U, 2 * d.Wall.V - d.Inside.V)));
                // Up a stair core, landing to landing; up from a hall's floor to its walkways.
                if (sp.Purpose == "stair") Link(i, spaces.FindIndex(s => s.Purpose == "stair" && s.Deck == sp.Deck + 1 && s.Floor.Overlaps(sp.Floor)));
                if (sp.Hall) Link(i, spaces.FindIndex(s => s.Corridor && s.Deck == sp.Deck + 1 && s.Floor.Overlaps(sp.Floor)));
            }
        }
        return list;
    }
}
