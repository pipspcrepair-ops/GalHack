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

namespace Slice.Places;

/// <summary>
/// The crashed capital ships on the planet, as real vessels. A hulk's place (poi-generator's CapitalHull) is laid
/// out round a vessel plan: the ground cut away where the hull lies buried, its ramps and its yard. When the
/// survivor comes within reach, the plan's pieces are built of the player's own large-grid blocks, damaged as
/// the place's state and the crash say, and set down in the hollow as coreless wrecks, pitched as they came down.
/// Their containers hold the salvage. A core set into one claims it; fixed, it lifts off and flies.
/// They stay once set down: nothing of a wreck is taken back while the game runs.
/// </summary>
public partial class Hulks : Node
{
    public SurfaceZone Zone;
    public Func<Vector3> Focus = () => Vector3.Zero;
    public PoiPlaces Places;

    /// <summary>How near a hulk's place the survivor comes before its vessel is set down, m.</summary>
    public float Reach = 1500f;

    /// <summary>Each crash site's vessels, set down.</summary>
    public readonly Dictionary<PoiSite, List<Vessel>> Down = new();

    /// <summary>A hulk's vessel was set down.</summary>
    public event Action<PoiSite, Vessel> Set;

    int _tick;

    public override void _PhysicsProcess(double delta)
    {
        if (Zone?.Planet == null) return;
        LiftClear();
        if (++_tick % 30 != 0) return;
        Vector3 at = Focus();
        foreach (PoiSite site in Zone.Planet.Pois)
        {
            if (site.Layout == null || Down.ContainsKey(site) || CapitalHull.Of(site.Layout) == null) continue;
            Vector3 o = Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), at);
            if (new Vector2(o.X - at.X, o.Z - at.Z).Length() > Reach) continue;
            SetDown(site);
            return; // one a tick at most: each is a few thousand blocks
        }
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
    public List<Vessel> SetDown(PoiSite site)
    {
        if (Down.TryGetValue(site, out var done)) return done;
        var list = new List<Vessel>();
        Down[site] = list;
        CapitalHull.HullPlan plan = CapitalHull.Of(site.Layout);
        if (plan == null) return list;
        Catalogue catalogue = Zone.Data.Catalogue;
        Vector3 origin = Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), Focus());
        ulong seed = site.Layout.Seed;
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
        for (int i = 0; i < plan.Pieces.Count; i++)
        {
            CapitalHull.HullPiece piece = plan.Pieces[i];
            HashSet<PoiGenerator.Gen.Int3> gone = plan.Gone(i, site.State, seed);
            Structure s = Build(piece.Text(site.Family.Name, gone), catalogue, site.Family.Name + piece.Name);
            if (s == null) continue;
            var at = (origin + new Vector3(piece.Origin.X, piece.Origin.Y, piece.Origin.Z)).ToNumerics();
            Vessel v = Vessel.SpawnGroundWreck(Zone, s, at, piece.Rotation);
            v.Derelict = new Slice.Orbit.DerelictPlan { Id = $"hulk-{site.Origin.X}-{site.Origin.Z}-{i}", Name = s.Name, Structure = s, Position = at, Rotation = piece.Rotation };
            Stock(v, rng, site.State);
            list.Add(v);
            Set?.Invoke(site, v);
        }
        GD.Print($"hulk: {site.Family.Name} '{site.Layout.Variant?.Name}' ({site.State}) set down as {list.Count} wreck(s), {list.Sum(v => v.Structure.Count)} blocks");
        return list;
    }

    /// <summary>A blueprint's text built as a structure of the catalogue's blocks; a wreck has no core, every other rule holds.</summary>
    public static Structure Build(string text, Catalogue catalogue, string name)
    {
        BlueprintData data = Blueprint.Parse(text);
        var errors = Blueprint.Validate(data, catalogue).Where(e => !e.StartsWith("no core") && !e.Contains("not attached") && !e.Contains("attached to the core")).ToList();
        if (errors.Count > 0)
        {
            GD.PushError($"hulk '{name}': {string.Join("; ", errors.Take(5))}");
            return null;
        }
        Structure s = Blueprint.Build(data, catalogue);
        s.DirtyChunks.Clear();
        return s;
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
/// yet): each piece's blocks as they stand (the plan less what the crash and the years took, as <see cref="Hulks"/>
/// sets it down), turned and pitched as it lies, read a metre cell at a time; and its rooms as the population
/// function sees them. Its doors, chairs and stair slopes are open.
/// </summary>
public sealed class HullSpace
{
    sealed record Lying(HashSet<PoiGenerator.Gen.Int3> Solid, N.Vector3 Origin, N.Quaternion Back, PoiGenerator.Gen.Int3 Min, PoiGenerator.Gen.Int3 Max);

    readonly List<Lying> _pieces = new();
    /// <summary>The cells any piece covers, in the place's frame (max exclusive).</summary>
    public Vector3I Min { get; } = new(int.MaxValue, int.MaxValue, int.MaxValue);
    public Vector3I Max { get; } = new(int.MinValue, int.MinValue, int.MinValue);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PoiInstance, HullSpace> Known = new();

    /// <summary>The hulk's vessel of a place, or null where the place is no hulk.</summary>
    public static HullSpace Of(PoiInstance inst)
    {
        if (CapitalHull.Of(inst.Layout) is not { } plan) return null;
        lock (Known) return Known.GetValue(inst, _ => new HullSpace(plan, inst.State, inst.Layout.Seed));
    }

    HullSpace(CapitalHull.HullPlan plan, PoiState state, ulong seed)
    {
        for (int i = 0; i < plan.Pieces.Count; i++)
        {
            CapitalHull.HullPiece p = plan.Pieces[i];
            HashSet<PoiGenerator.Gen.Int3> gone = plan.Gone(i, state, seed);
            var solid = p.Intact.Where(kv => !gone.Contains(kv.Key) && Solid(kv.Value)).Select(kv => kv.Key).ToHashSet();
            if (p.Intact.Count == 0) continue;
            PoiGenerator.Gen.Int3 lo = p.Intact.Keys.Aggregate(PoiGenerator.Gen.Int3.Min), hi = p.Intact.Keys.Aggregate(PoiGenerator.Gen.Int3.Max);
            _pieces.Add(new Lying(solid, p.Origin, N.Quaternion.Inverse(p.Rotation), lo, hi));
            // Its corners as it lies, for the cells it covers (a block b's middle is 2 b from the origin, a block 2 m).
            for (int c = 0; c < 8; c++)
            {
                var corner = new N.Vector3((c & 1) == 0 ? 2 * lo.X - 1 : 2 * hi.X + 1, (c & 2) == 0 ? 2 * lo.Y - 1 : 2 * hi.Y + 1, (c & 4) == 0 ? 2 * lo.Z - 1 : 2 * hi.Z + 1);
                N.Vector3 at = p.Origin + N.Vector3.Transform(corner, p.Rotation);
                Min = new Vector3I(Math.Min(Min.X, (int)MathF.Floor(at.X)), Math.Min(Min.Y, (int)MathF.Floor(at.Y)), Math.Min(Min.Z, (int)MathF.Floor(at.Z)));
                Max = new Vector3I(Math.Max(Max.X, (int)MathF.Ceiling(at.X)), Math.Max(Max.Y, (int)MathF.Ceiling(at.Y)), Math.Max(Max.Z, (int)MathF.Ceiling(at.Z)));
            }
        }
    }

    /// <summary>A block that stops a body: not a door, a chair or a stair's slope (gone up or through).</summary>
    static bool Solid(string block) => !(block.StartsWith("door", StringComparison.Ordinal) || block.StartsWith("chair", StringComparison.Ordinal) || block.Contains(" slope"));

    /// <summary>A metre cell of the place's frame: solid (a standing block), open (inside a piece's blocks, none standing there), or null (no piece there).</summary>
    public bool? Cell(int x, int y, int z)
    {
        if (x < Min.X || y < Min.Y || z < Min.Z || x >= Max.X || y >= Max.Y || z >= Max.Z) return null;
        var mid = new N.Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
        foreach (Lying p in _pieces)
        {
            N.Vector3 l = N.Vector3.Transform(mid - p.Origin, p.Back) * 0.5f;
            var b = new PoiGenerator.Gen.Int3((int)MathF.Round(l.X), (int)MathF.Round(l.Y), (int)MathF.Round(l.Z));
            if (b.X < p.Min.X || b.Y < p.Min.Y || b.Z < p.Min.Z || b.X > p.Max.X || b.Y > p.Max.Y || b.Z > p.Max.Z) continue;
            return p.Solid.Contains(b);
        }
        return null;
    }

    /// <summary>
    /// The pieces' rooms as the population function sees them, in the place's frame (the box each lies in, as it lies),
    /// indexed from <paramref name="first"/> and linked through their doors, their storeys counted from the deck the
    /// boarding door opens on. Dark: a wreck has no power.
    /// </summary>
    public static List<PoiRoom> Rooms(CapitalHull.HullPlan plan, int first)
    {
        // The deck the boarding door opens onto: that of the room nearest the door.
        int entry = 0;
        foreach (CapitalHull.HullPiece p in plan.Pieces)
            if (p.Door is { } d && p.Rooms.Count > 0)
            {
                var at = new N.Vector3(2 * d.X, 2 * d.Y, 2 * d.Z);
                entry = p.Rooms.MinBy(r => N.Vector3.DistanceSquared(N.Vector3.Clamp(at, r.Min, r.Max), at)).Deck;
            }
        var list = new List<PoiRoom>();
        foreach (CapitalHull.HullPiece p in plan.Pieces)
        {
            int base_ = first + list.Count;
            foreach (CapitalHull.HullRoom r in p.Rooms)
            {
                var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var hi = -lo;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new N.Vector3((c & 1) == 0 ? r.Min.X : r.Max.X, (c & 2) == 0 ? r.Min.Y : r.Max.Y, (c & 4) == 0 ? r.Min.Z : r.Max.Z);
                    N.Vector3 w = p.Origin + N.Vector3.Transform(corner, p.Rotation);
                    lo = lo.Min(new Vector3(w.X, w.Y, w.Z));
                    hi = hi.Max(new Vector3(w.X, w.Y, w.Z));
                }
                // Pitched, its floor stands at its middle's height (the box's lowest edge is under the deck).
                float floor = p.Origin.Y + N.Vector3.Transform(((r.Min + r.Max) * 0.5f) with { Y = r.Min.Y }, p.Rotation).Y;
                int storey = r.Deck - entry;
                var room = new PoiRoom
                {
                    Index = first + list.Count, Kind = PlaceFauna.KindOf(r.Purpose, r.Kind, true), Purpose = r.Purpose,
                    Min = lo with { Y = MathF.Min(floor, hi.Y - 2f) }, Max = hi, Storey = storey, Depth = Math.Max(0, -storey), Dark = true,
                };
                room.Links.AddRange(r.Links.Select(j => base_ + j));
                list.Add(room);
            }
        }
        return list;
    }
}
