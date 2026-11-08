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
            if (site.Instance == null || Down.ContainsKey(site) || CapitalHull.Of(site.Instance.Layout) == null) continue;
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
        CapitalHull.HullPlan plan = CapitalHull.Of(site.Instance.Layout);
        if (plan == null) return list;
        Catalogue catalogue = Zone.Data.Catalogue;
        Vector3 origin = Zone.Planet.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), Focus());
        ulong seed = site.Instance.Layout.Seed;
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
        for (int i = 0; i < plan.Pieces.Count; i++)
        {
            CapitalHull.HullPiece piece = plan.Pieces[i];
            HashSet<PoiGenerator.Gen.Int3> gone = piece.Damage(site.Instance.State, seed + (ulong)i * 7919UL);
            Structure s = Build(piece.Text(site.Family.Name, gone), catalogue, site.Family.Name + piece.Name);
            if (s == null) continue;
            var at = (origin + new Vector3(piece.Origin.X, piece.Origin.Y, piece.Origin.Z)).ToNumerics();
            Vessel v = Vessel.SpawnGroundWreck(Zone, s, at, piece.Rotation);
            v.Derelict = new Slice.Orbit.DerelictPlan { Id = $"hulk-{site.Origin.X}-{site.Origin.Z}-{i}", Name = s.Name, Structure = s, Position = at, Rotation = piece.Rotation };
            Stock(v, rng, site.Instance.State);
            list.Add(v);
            Set?.Invoke(site, v);
        }
        GD.Print($"hulk: {site.Family.Name} '{site.Instance.Layout.Variant?.Name}' ({site.Instance.State}) set down as {list.Count} wreck(s), {list.Sum(v => v.Structure.Count)} blocks");
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
