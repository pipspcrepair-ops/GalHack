using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Construction;

/// <summary>A whole blueprint being set down in Creative: under way over a few ticks, then placed (or not, and why).</summary>
public sealed class WholePlacement
{
    public Structure Plan;
    public Vessel Vessel;
    public string Why;
    public bool Done;

    /// <summary>What it cost: finding the spot, building the body, clearing the plants and ground (ms), and the most any one tick spent on it.</summary>
    public double SiteMs, SpawnMs, GroundMs, WorstTickMs;

    /// <summary>Plants felled from under it.</summary>
    public int Felled;

    /// <summary>Placed or given up.</summary>
    public event Action<WholePlacement> Finished;

    internal IEnumerator<bool> Steps;
    internal void Finish() => Finished?.Invoke(this);
}

/// <summary>
/// Creative's PLACE on the BLUEPRINTS tab: a whole structure at once, in front of you, on clear
/// ground. Its heading snaps to the quarter turn nearest the way you face and its spot to its own
/// grid; the spot is the nearest ahead whose ground is level enough under its whole footprint, dry,
/// and clear of terrain, places and other structures; the plants it stands on are felled. A vessel stands on its lowest point (its
/// legs' feet) at the highest ground under it, so nothing of it is in the ground; a base meets the
/// ground (it may sink a little into the high side, and the ground there is cleared flush, as a
/// base's blocks always clear it).
///
/// It keeps to the frame's budget: the search runs a few milliseconds a tick, the plants are felled in
/// the next, the body is built in one, its look is drawn over the next frames, and a base's ground is
/// cleared a column a tick after.
/// </summary>
public sealed partial class BuildTool
{
    /// <summary>How far ahead the search for a spot goes, beyond the structure's own size, m.</summary>
    public const float SiteReach = 60f;

    /// <summary>What the search may spend in one tick, ms.</summary>
    public const double SiteBudgetMs = 4.0;

    /// <summary>The whole placement under way, or the last one.</summary>
    public WholePlacement Placing { get; private set; }

    WholePlacementRunner _runner;

    /// <summary>Starts setting a whole blueprint down (Creative): it runs over the next few ticks; <see cref="WholePlacement.Finished"/> says how it went.</summary>
    public WholePlacement PlaceWhole(Structure plan)
    {
        var job = new WholePlacement { Plan = plan };
        job.Steps = Whole(job).GetEnumerator();
        Placing = job;
        if (_runner == null)
        {
            _runner = new WholePlacementRunner { Name = "WholePlacement", Tool = this };
            _zone.AddChild(_runner);
        }
        return job;
    }

    /// <summary>Each tick: the placement under way takes its step.</summary>
    internal void Advance()
    {
        WholePlacement job = Placing;
        if (job == null || job.Done) return;
        var clock = Stopwatch.StartNew();
        bool more = job.Steps.MoveNext();
        job.WorstTickMs = Math.Max(job.WorstTickMs, clock.Elapsed.TotalMilliseconds);
        if (more) return;
        job.Done = true;
        job.Finish();
    }

    IEnumerable<bool> Whole(WholePlacement job)
    {
        Structure plan = job.Plan;
        Transform3D? frame = null;
        string why = null;
        var clock = Stopwatch.StartNew();
        foreach (bool _ in Site(plan, (f, w) => { frame = f; why = w; }))
        {
            job.SiteMs += clock.Elapsed.TotalMilliseconds;
            yield return true;
            clock.Restart();
        }
        job.SiteMs += clock.Elapsed.TotalMilliseconds;
        if (frame is not Transform3D at)
        {
            job.Why = why;
            Said?.Invoke($"{plan.Name} · {why}", Tones.Caution);
            yield break;
        }
        // The plants it will stand on go first, as a cut plant goes (felled for good, collider and all): set down into a
        // tree, a hull is pushed up onto it, and comes down when woken.
        clock.Restart();
        job.Felled = FellUnder(plan, at);
        job.GroundMs = clock.Elapsed.TotalMilliseconds;
        yield return true;
        // The body and its colliders now; its look over the next frames.
        clock.Restart();
        Structure s = Copy(plan);
        N.Vector3 origin = at.Origin.ToNumerics();
        float heading = MathX.Heading(at.Basis.GetRotationQuaternion().ToNumerics());
        Vessel v = s.Mode == CoreMode.Base
            ? Vessel.SpawnBase(_zone, s, origin, heading, drawLater: true)
            : Vessel.Spawn(_zone, s, origin, heading, landed: true, drawLater: true);
        job.SpawnMs = clock.Elapsed.TotalMilliseconds;
        job.Vessel = v;
        Placed += s.Count;
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, at.Origin, 0.6f, 0f);
        Said?.Invoke($"{s.Name} placed", Tones.Good);
        Started?.Invoke(v);
        if (!v.IsBase) yield break;
        // A base meets the ground flush: the blocks at and under it clear it, a 16 m column of ground a tick.
        foreach (var column in v.GroundParts())
        {
            yield return true;
            clock.Restart();
            _zone.Edits?.ClearForStructure(column);
            job.GroundMs += clock.Elapsed.TotalMilliseconds;
        }
    }

    /// <summary>Head room kept clear over a structure set down whole, m: a crown hanging lower would stop a vessel lifting off.</summary>
    const float HeadRoom = 4f;

    /// <summary>
    /// Fells every plant rooted under a structure's footprint where it will stand (a metre round it too), and every tree
    /// whose trunk or crown reaches into that box or the head room over it (vessels collide with flora). Returns how many.
    /// </summary>
    int FellUnder(Structure plan, Transform3D frame)
    {
        if (_zone.Streamer == null || _zone.Planet == null) return 0;
        var (min, max) = plan.Bounds();
        float cell = plan.Grid.CellSize, margin = 1f;
        Vector3 lo = new Vector3(min.X, min.Y, min.Z) * cell - Vector3.One * (cell * 0.5f + margin);
        Vector3 hi = new Vector3(max.X, max.Y, max.Z) * cell + Vector3.One * (cell * 0.5f + margin);
        Vector3 mid = frame * ((lo + hi) * 0.5f);
        float reach = new Vector2(hi.X - lo.X, hi.Z - lo.Z).Length() * 0.5f;
        Transform3D toLocal = frame.AffineInverse();
        int n = 0;
        foreach (var plant in new List<PlanetSurface.Terrain.TerrainStreamer.PlantSighting>(_zone.Streamer.FloraNear(mid, reach)))
        {
            Vector3 at = toLocal * plant.Root;
            if (at.X >= lo.X && at.X <= hi.X && at.Z >= lo.Z && at.Z <= hi.Z && _zone.Fell(plant.Id, plant.Root)) n++;
        }
        Vector3 top = hi + Vector3.Up * HeadRoom;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = top - lo },
            Transform = frame * new Transform3D(Basis.Identity, (lo + top) * 0.5f),
            CollisionMask = GalHack.Physics.Layers.Flora,
        };
        foreach (Godot.Collections.Dictionary hit in _zone.GetWorld3D().DirectSpaceState.IntersectShape(query, 64))
            if (_zone.Streamer.SolidHit((Rid)hit["rid"], (int)hit["shape"], out PlanetSurface.Terrain.FloraSolid tree) && _zone.Fell(tree.Id, mid)) n++;
        return n;
    }

    /// <summary>
    /// The nearest spot ahead where a whole blueprint can stand, or false and why not ("NO LEVEL
    /// GROUND AHEAD", "WATER AHEAD", "SOMETHING IN THE WAY"). All at once: the tab's PLACE spreads it over ticks.
    /// </summary>
    public bool FindSite(Structure plan, out Transform3D frame, out string why)
    {
        Transform3D? found = null;
        string reason = null;
        foreach (bool _ in Site(plan, (f, w) => { found = f; reason = w; }, budgetMs: double.MaxValue)) { }
        frame = found ?? default;
        why = reason;
        return found != null;
    }

    /// <summary>The search, yielding whenever it has spent the tick's budget; it ends by reporting the spot, or why there is none.</summary>
    IEnumerable<bool> Site(Structure plan, Action<Transform3D?, string> result, double budgetMs = SiteBudgetMs)
    {
        if (plan == null || plan.Count == 0)
        {
            result(null, "NOTHING TO PLACE");
            yield break;
        }
        var (min, max) = plan.Bounds();
        float cell = plan.Grid.CellSize;
        Vector3 lo = new Vector3(min.X, min.Y, min.Z) * cell - Vector3.One * cell * 0.5f;
        Vector3 hi = new Vector3(max.X, max.Y, max.Z) * cell + Vector3.One * cell * 0.5f;
        Vector3 centre = (lo + hi) * 0.5f;
        bool isBase = plan.Mode == CoreMode.Base;
        float feet = Feet(plan);

        float look = _survivor.Yaw + YawOfFrame();
        float quarter = Mathf.Pi * 0.5f;
        var basis = new Basis(Vector3.Up, MathF.Round(look / quarter) * quarter);
        var lookBasis = new Basis(Vector3.Up, look);
        Vector3 ahead = lookBasis * Vector3.Forward, side = lookBasis * Vector3.Right;
        float sx = hi.X - lo.X, sz = hi.Z - lo.Z;
        float radius = 0.5f * MathF.Sqrt(sx * sx + sz * sz);
        // Level enough: a share of its size, within limits. A vessel stands on its legs, one end higher off
        // the ground than the other on a slope; a base sinks into its high side, as far as half a block.
        float tolerance = isBase ? Math.Clamp(0.1f * radius, 0.5f, 2.5f) : Math.Clamp(0.2f * radius, 0.75f, 6f);
        float step = MathF.Max(cell, MathF.Max(sx, sz) / 8f);
        Vector3 from = _survivor.WorldFeet;
        int water = 0, steep = 0, blocked = 0;
        var clock = Stopwatch.StartNew();
        for (float d = radius + 3f; d <= radius + SiteReach; d += MathF.Max(2f, cell))
            foreach (float s in new[] { 0f, -0.7f, 0.7f, -1.4f, 1.4f })
            {
                if (clock.Elapsed.TotalMilliseconds > budgetMs)
                {
                    yield return false;
                    clock.Restart();
                }
                Vector3 at = from + ahead * d + side * (s * radius);
                // The footprint's middle goes there: the core (the origin) sits wherever that puts it, snapped to the grid.
                Vector3 origin = at - basis * new Vector3(centre.X, 0f, centre.Z);
                origin.X = MathF.Round(origin.X / cell) * cell;
                origin.Z = MathF.Round(origin.Z / cell) * cell;
                if (!Ground(origin, basis, lo, hi, step, tolerance, !isBase, out float top, out float low, out bool wet))
                {
                    if (wet) water++;
                    else steep++;
                    continue;
                }
                float bottom = isBase ? MathF.Max(low, top - cell * 0.5f) : top + 0.05f;
                origin.Y = bottom + feet;
                var candidate = new Transform3D(basis, origin);
                if (Blocked(candidate, lo, hi, top + 0.15f - origin.Y))
                {
                    blocked++;
                    continue;
                }
                result(candidate, null);
                yield break;
            }
        result(null, blocked >= Math.Max(water, steep) ? "SOMETHING IN THE WAY" : water > steep ? "WATER AHEAD" : "NO LEVEL GROUND AHEAD");
    }

    /// <summary>How far the structure's lowest point (its hull, or its legs' feet when it lands on them) is below its origin, m.</summary>
    float Feet(Structure plan)
    {
        float feet = StructureBridge.ShapeOf(plan).FeetBelowOrigin;
        if (plan.Mode is CoreMode.Base or CoreMode.Hover) return feet;
        var spec = new StructureVessel(plan, _zone.Data.Handling);
        foreach (ShipHandling.Handling.GearSpec g in spec.LandingGear) feet = MathF.Max(feet, -g.Position.Y + g.Length);
        return feet;
    }

    /// <summary>
    /// The ground under a footprint: its highest and lowest, and false if it is too uneven or under water. A vessel
    /// (<paramref name="feet"/>) stands on the highest of the ground's collision, which its feet meet, not the natural
    /// height between its faces; a base meets the natural ground, which its blocks clear.
    /// </summary>
    bool Ground(Vector3 origin, Basis basis, Vector3 lo, Vector3 hi, float step, float tolerance, bool feet, out float top, out float low, out bool wet)
    {
        top = float.MinValue;
        low = float.MaxValue;
        wet = false;
        float stand = float.MinValue;
        int nx = Math.Max(1, (int)MathF.Ceiling((hi.X - lo.X) / step)), nz = Math.Max(1, (int)MathF.Ceiling((hi.Z - lo.Z) / step));
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
            {
                Vector3 p = origin + basis * new Vector3(Mathf.Lerp(lo.X, hi.X, i / (float)nx), 0f, Mathf.Lerp(lo.Z, hi.Z, j / (float)nz));
                if (_zone.Water != null && _zone.Water.LiquidAt(p.X, p.Z, out _))
                {
                    wet = true;
                    return false;
                }
                float h = _zone.GroundAt(p.X, p.Z).Y;
                top = MathF.Max(top, h);
                low = MathF.Min(low, h);
                if (top - low > tolerance) return false;
                if (feet) stand = MathF.Max(stand, _zone.GroundTop(p.X, p.Z));
            }
        if (feet) top = stand;
        return true;
    }

    /// <summary>Is anything solid (the ground's own collision, a place, a structure, a device) inside the box the structure will fill, above its footing?</summary>
    bool Blocked(Transform3D frame, Vector3 lo, Vector3 hi, float floor)
    {
        float bottom = MathF.Max(lo.Y, floor);
        if (hi.Y - bottom < 0.1f) return false;
        var size = new Vector3(hi.X - lo.X, hi.Y - bottom, hi.Z - lo.Z);
        var mid = new Vector3((lo.X + hi.X) * 0.5f, (bottom + hi.Y) * 0.5f, (lo.Z + hi.Z) * 0.5f);
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = size },
            Transform = new Transform3D(frame.Basis, frame * mid),
            CollisionMask = Layers.World | Layers.Hull | Layers.Props,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };
        // Plants don't count (their colliders are the physics server's own, with no node): they are felled once it stands.
        foreach (Godot.Collections.Dictionary hit in _zone.GetWorld3D().DirectSpaceState.IntersectShape(query, 32))
            if (hit["collider"].AsGodotObject() != null) return true;
        // You are not built into it.
        Vector3 you = frame.AffineInverse() * _survivor.WorldFeet;
        return you.X > lo.X - 1f && you.X < hi.X + 1f && you.Z > lo.Z - 1f && you.Z < hi.Z + 1f && you.Y < hi.Y + 1f;
    }

    /// <summary>A plan's blocks, paint and lights on a new structure of its own, owned by the player.</summary>
    Structure Copy(Structure plan)
    {
        Structure s = Like(plan);
        s.Owner = Building.DefaultOwner;
        foreach (var (p, b) in plan.Blocks()) s.Set(p, b);
        foreach (var (p, light) in plan.LightStyles) s.SetLight(p, light);
        s.DirtyChunks.Clear();
        return s;
    }
}

/// <summary>Ticks the whole placement under way (a child of the zone).</summary>
public partial class WholePlacementRunner : Node
{
    public BuildTool Tool;
    public override void _PhysicsProcess(double delta) => Tool?.Advance();
}
