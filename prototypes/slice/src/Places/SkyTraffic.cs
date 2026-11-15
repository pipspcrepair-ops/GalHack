using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using GalHack.Terrain;
using Godot;
using PlanetSurface.Poi;
using Planets.Gen;
using PoiGenerator.Gen;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Places;

/// <summary>
/// The start world's sky (design, "The journey to the core": a start world has a lone trader or scout in its sky). One
/// generated Freeholder vessel (<see cref="SmallCraft"/>, never a stock blueprint) takes off from a settlement or port
/// near where the sleeper woke, flies between the world's places and lands at the inhabited ones for a while, a scout
/// circling the places it looks at on the way. Its lights are on at night; far off it is drawn as a cheap stand-in.
/// Trading at its stops waits for the trade system: <see cref="Stopped"/> is where that hooks in.
/// </summary>
public partial class SkyTraffic : Node
{
    public SurfaceZone Zone;

    /// <summary>The sleeper: where the ship keeps its routes near, and what it runs from.</summary>
    public Func<Vector3> Focus = () => Vector3.Zero;

    /// <summary>Where the world is seen from (the camera), for the stand-in.</summary>
    public Func<Vector3> Eye;

    /// <summary>Who holds the pilot role aboard.</summary>
    public const string PilotName = "skyship-pilot";

    /// <summary>The highest it flies, m: under the 1,060 m altitude cap.</summary>
    public const float Ceiling = 1030f;

    /// <summary>How far from the start its stops lie, m: the sleeper sees it come and go.</summary>
    public float RouteReach = 5000f;

    /// <summary>Beyond this from the eye it is drawn as its stand-in, m.</summary>
    public float StandInFrom = 900f;

    /// <summary>Tests: the role to roll (null: the world's own roll); no ship at all (a baseline run).</summary>
    public static CraftRole? ForceRole;
    public static bool Off;

    public Skyship Ship { get; private set; }

    /// <summary>It came down at a place and stands there a while: where a trade at its stop would open.</summary>
    public event Action<Skyship, PoiSite> Stopped;

    /// <summary>Why there is no ship, when there is none (tests).</summary>
    public string Missing { get; private set; }

    List<PoiSite> _settled, _looks, _stops;
    StandIn _standIn;
    Random _rng;
    bool _settledOnCollision, _night;
    int _tick;
    Vector3 _start;

    /// <summary>What its last tick cost, ms: the pilot, and every tenth tick its upkeep (tests).</summary>
    public double LastTickMs { get; private set; }

    /// <summary>Its worst tick since it spawned, ms, and what it was doing then (tests).</summary>
    public double WorstMs { get; private set; }
    public string WorstWhat { get; private set; } = "";

    List<GalHack.Structures.Int3> _lamps = new();
    int _firstLamp = -1;

    /// <summary>Each frame, its lamps' far points where its lamps are drawn now, lit while they are (none once it is a wreck).</summary>
    public override void _Process(double delta)
    {
        if (Ship == null || _firstLamp < 0 || Zone.Pois?.Far is not { } far) return;
        Vessel v = Ship.Vessel;
        if (!GodotObject.IsInstanceValid(v.Node)) return;
        Transform3D at = v.DrawTransform;
        Structure s = v.Structure;
        float cell = s.Grid.CellSize;
        for (int i = 0; i < _lamps.Count; i++)
        {
            GalHack.Structures.Int3 p = _lamps[i];
            bool lit = s.TryGet(p, out GalHack.Structures.Block b) && s.TypeOf(b).IsLight && !b.Off && Ship.Phase != SkyPhase.Wrecked;
            N.Vector3 out_ = Orientations.Matrix(b.Rot).Apply(new N.Vector3(0, 0, -0.6f));
            Vector3 local = (new Vector3(p.X, p.Y, p.Z) + new Vector3(out_.X, out_.Y, out_.Z)) * cell;
            LightSettings l = s.LightAt(p);
            far.SetLamp(_firstLamp + i, at * local, GalHack.Structures.Presentation.ChunkMesher.Rgb(l.Color), lit ? 1.4f : 0f, l.Anim == LightAnim.Strobe);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Off || Zone?.Planet == null || !Zone.Arrived || Zone.IsOtherBody || Missing != null) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        string what;
        if (Ship == null) { Spawn(); what = "spawn"; }
        else
        {
            what = Ship.Phase.ToString();
            Ship.Tick((float)delta);
            if (++_tick % 10 == 0) { Upkeep(); what += " + upkeep"; }
        }
        LastTickMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (LastTickMs > WorstMs && Ship != null && what != "spawn") (WorstMs, WorstWhat) = (LastTickMs, what);
    }

    // ---- The ship ---------------------------------------------------------------------------------------------

    void Spawn()
    {
        ulong seed = Zone.Spec.Seed;
        _rng = new Random(unchecked((int)(seed ^ (seed >> 32)) ^ 0x5c1f7));
        _start = Focus();
        Stops();
        CraftRole role = ForceRole ?? (_rng.Next(2) == 0 ? CraftRole.Trader : CraftRole.Scout);
        float gravity = Zone.Physics.Zone.Strength;
        Structure s = null;
        for (int i = 0; i < 8 && s == null; i++) s = SmallCraft.Roll(Zone.Data.Catalogue, role, CraftStyle.Freeholder, _rng.Next(), gravity);
        if (s == null) { Missing = "no roll met its needs"; return; }
        // Its home: the nearest inhabited place to the start that isn't on top of it.
        PoiSite home = _stops.OrderBy(p => (_settled.Contains(p) ? 0f : 1500f) + (Distance(p, _start) < 400f ? 1e6f : Distance(p, _start))).FirstOrDefault();
        N.Vector3? at = null;
        if (home != null)
        {
            SpotSearch q = Search(home, s, null);
            while (!Advance(q, 1e6)) { }
            at = q.Spot;
        }
        if (at == null) { Missing = home == null ? "no places on the world" : "no clear ground by its home"; return; }
        Vessel v = Vessel.Spawn(Zone, s, at.Value + N.Vector3.UnitY * 3f, (float)(_rng.NextDouble() * Math.Tau), landed: true);
        v.Settle();
        v.Roles.Sat(PilotName, v.Chairs.First());
        Stock(v, role);
        Ship = new Skyship(this, v, role, home, _rng.Next());
        Zone.FrameShifted += Ship.Shift;
        _standIn = new StandIn { Name = "StandIn", Visible = false };
        v.Node.AddChild(_standIn);
        _standIn.Build(s);
        // Its lamps carry at night as a lived-in place's do: far points, its real lights handing off to them.
        v.View.HandOff = true;
        _lamps = s.Blocks().Where(b => s.TypeOf(b.block).IsLight).Select(b => b.pos).ToList();
        _firstLamp = Zone.Pois?.Far?.AddLamps(_lamps.Count) ?? -1;
        GD.Print($"skyship: a {role} ({s.BlockCount} blocks, {StructureStats.Compute(s).Mass:F0} t) landed by {home.Family.Name}, {Distance(home, _start):F0} m from the start");
    }

    /// <summary>Its holds: usable goods for a trader, a little for a scout; its generator fuel (CraftNeeds' hours at full load) in core storage, its capacitor charged.</summary>
    void Stock(Vessel v, CraftRole role)
    {
        var items = Zone.Data.Items;
        var bill = new GalHack.Items.Bill();
        for (int i = 0; i < (role == CraftRole.Trader ? 4 : 1); i++)
            foreach (var (item, least, most) in Slice.Orbit.OrbitRules.Shared.LootFor(i % 2 == 0 ? "scrap" : "salvage"))
                if (items.Has(item) && _rng.Next(least, most + 1) is int n && n > 0) bill.Add(item, n);
        if (items.TryGet("fuel-pack", out var pack) && pack.FuelUnits > 0)
            bill.Add(pack.Id, (int)Math.Ceiling(CraftNeeds.Default.FuelMJ(StructureStats.Compute(v.Structure)) / (pack.FuelUnits * v.Power.Rules.FuelUnitMJ)));
        v.Storage.AddAll(bill);
        v.Power.ChargeMJ = v.Power.CapacityMJ;
    }

    /// <summary>A few times a second: its lights by the daylight, its stand-in by the distance, and a landed hull set on the ground's collision once it streams in.</summary>
    void Upkeep()
    {
        Vessel v = Ship.Vessel;
        if (!GodotObject.IsInstanceValid(v.Node)) return;
        float day = Zone.Sky.Daylight;
        if (_night ? day > 0.4f : day < 0.3f) _night = !_night;
        if (Ship.Phase != SkyPhase.Wrecked && v.LightsOn != _night) v.LightsOn = _night;
        Vector3 eye = Eye?.Invoke() ?? Focus();
        bool far = v.Body.Position.ToGodot().DistanceTo(eye) > StandInFrom;
        if (far) _standIn.Build(v.Structure);
        _standIn.Visible = far;
        v.View.Visible = !far;
        if (Ship.Phase != SkyPhase.Landed) _settledOnCollision = false;
        else if (!_settledOnCollision && v.Controller is { Frozen: true } && Collides(v.Body.Position))
        {
            _settledOnCollision = true;
            v.Settle();
        }
    }

    public void Landed(Skyship ship) => Stopped?.Invoke(ship, ship.At);

    /// <summary>What it runs from.</summary>
    public N.Vector3 Threat() => Focus().ToNumerics();

    // ---- Places ---------------------------------------------------------------------------------------------

    /// <summary>The inhabited places (or towns, ports and cities), the places a scout looks at (anything bigger than a shed), and the stops it puts down at.</summary>
    void Stops()
    {
        var wholes = new HashSet<object>();
        var sites = Zone.Planet.Pois.Where(p => p.Whole == null || wholes.Add(p.Whole)).Where(p => p.Stamp != null).ToList();
        _settled = sites.Where(p => p.Occupancy != null || p.Tier >= PoiTier.Town).ToList();
        _looks = sites.Where(p => p.Tier >= PoiTier.Site && !p.Family.Id.StartsWith("flavour_")).ToList();
        // Where it puts down: the inhabited places within its reach of the start; where they are few, the nearest other places too.
        _stops = _settled.Where(p => Distance(p, _start) < RouteReach).ToList();
        foreach (PoiSite p in _looks.Where(p => !_stops.Contains(p)).OrderBy(p => Distance(p, _start)))
            if (_stops.Count < 4) _stops.Add(p);
        if (_stops.Count < 2) _stops = _settled.Concat(_looks).Distinct().ToList();
        _roofs = sites.Select(s => (s.Center.X, s.Center.Z, MathF.Max(s.Stamp.MaxX - s.Stamp.MinX, s.Stamp.MaxZ - s.Stamp.MinZ) * 0.5f + 60f, s.Origin.Y + Buildings(s))).ToArray();
    }

    /// <summary>A place's middle in the frame, nearest a point.</summary>
    public N.Vector3 CentreOf(PoiSite p, N.Vector3 near)
    {
        var (x, z) = p.Center;
        return Zone.Planet.NearestCopy(new Vector3((float)x, p.Origin.Y, (float)z), near.ToGodot()).ToNumerics();
    }

    float Distance(PoiSite p, Vector3 from) => MathX.Flat(CentreOf(p, from.ToNumerics()) - from.ToNumerics()).Length();

    /// <summary>
    /// Where to go next: a place it isn't at, near it, and within its reach of the start (running, away from the
    /// sleeper too). One of the few best, at random.
    /// </summary>
    public PoiSite NextStop(Skyship ship, bool lands, bool awayFromThreat = false)
    {
        N.Vector3 p = ship.Vessel.Body.Position, threat = Threat();
        List<PoiSite> pool = lands || _looks.Count < 2 ? _stops : _looks;
        var ranked = pool.Where(s => s != ship.At && s != ship.Bound).Select(s =>
        {
            N.Vector3 c = CentreOf(s, p);
            float score = MathX.Flat(c - p).Length() + MathF.Max(0f, Distance(s, _start) - RouteReach) * 3f;
            if (awayFromThreat) score -= MathX.Flat(c - threat).Length() * 0.5f;
            return (s, score);
        }).OrderBy(x => x.score).Take(4).ToList();
        return ranked.Count == 0 ? null : ranked[_rng.Next(ranked.Count)].s;
    }

    /// <summary>A search for a clear spot to put down by a place, worked a little each tick (<see cref="Advance"/>) so no frame pays for all of it.</summary>
    public sealed class SpotSearch
    {
        public PoiSite Site;
        public bool Done;
        public N.Vector3? Spot;
        internal IEnumerator<bool> Steps;
    }

    /// <summary>Starts looking for a spot by a place: flat, dry, off every place's and road's ground, away from other hulls and trees.</summary>
    public SpotSearch FindSpot(PoiSite site, Vessel ship) => Search(site, ship.Structure, ship);

    SpotSearch Search(PoiSite site, Structure s, Vessel self)
    {
        var q = new SpotSearch { Site = site };
        q.Steps = Candidates(q, site, s, self).GetEnumerator();
        return q;
    }

    /// <summary>Works a search on for up to this long, ms; true once it is done (found, or nowhere fits).</summary>
    public bool Advance(SpotSearch q, double budgetMs = 1.5)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!q.Done && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < budgetMs)
            if (!q.Steps.MoveNext()) q.Done = true;
        return q.Done;
    }

    IEnumerable<bool> Candidates(SpotSearch q, PoiSite site, Structure s, Vessel self)
    {
        N.Vector3 c = CentreOf(site, self?.Body.Position ?? _start.ToNumerics());
        float r = Radius(s), half = MathF.Max(site.Stamp.MaxX - site.Stamp.MinX, site.Stamp.MaxZ - site.Stamp.MinZ) * 0.5f;
        float turn = (float)(_rng.NextDouble() * Math.Tau);
        for (float ring = half + r + 10f; ring < half + 260f; ring += 14f)
            for (int k = 0; k < 16; k++)
            {
                float a = turn + k * MathF.Tau / 16f;
                var p = new N.Vector3(c.X + MathF.Cos(a) * ring, 0f, c.Z + MathF.Sin(a) * ring);
                if (Fits(p, r, self) is not float y)
                {
                    yield return false;
                    continue;
                }
                // What the scatter grows there, worked out for its chunks as the terrain does, never streamed in.
                bool? clear;
                while ((clear = Unwooded(p, r)) == null) yield return false;
                if (clear == true)
                {
                    q.Spot = p with { Y = y };
                    yield break;
                }
                yield return false;
            }
    }

    /// <summary>Whether a spot it means to put down on is still clear of other hulls (its trees and rocks were known when it was picked).</summary>
    public bool Clear(N.Vector3 spot, Vessel self) => Fits(spot, Radius(self.Structure), self) != null && Unwooded(spot, Radius(self.Structure)) != false;

    /// <summary>The ground's height at a spot if a hull this wide can put down there: dry, off every place, road and roadside piece, flat, no other hull.</summary>
    float? Fits(N.Vector3 p, float r, Vessel self)
    {
        var planet = Zone.Planet;
        double cx = Mathx.WrapX(p.X, planet.Width), cz = Mathx.Wrap(p.Z, planet.Height);
        if (planet.Map.IsSea(cx, cz) || planet.Source.WaterSurfaceAt(cx, cz, out _)) return null;
        // Off every stamp (a place's pad, its cuttings, a road) by its reach and a margin, looked up in the ground's own buckets.
        float m = r + 4f;
        foreach (var (ox, oz) in new[] { (0f, 0f), (-m, -m), (m, -m), (-m, m), (m, m) })
            foreach (int i in planet.Source.StampsAt(cx + ox, cz + oz))
            {
                TerrainStamp st = planet.Source.Stamps[i];
                double dx = Mathx.WrapDelta(cx - st.MinX, planet.Width), dz = Mathx.WrapDelta(cz - st.MinZ, planet.Height);
                if (dx > -m && dx < st.MaxX - st.MinX + m && dz > -m && dz < st.MaxZ - st.MinZ + m) return null;
            }
        // Nor by anything the roads and the ground lay beside them: poles, wreck sites, ruins, ore tells.
        if (Zone.Infra == null || Zone.Infra.PieceNear(cx, cz, r + 14f)) return null;
        foreach (Vessel v in Zone.Vessels)
            if (v != self && !v.IsWreck && MathX.Flat(v.Body.Position - p).Length() < r + 25f) return null;
        float h0 = Zone.GroundAt(p.X, p.Z).Y;
        if (!float.IsFinite(h0) || h0 < planet.Source.WaterLevel + 1f) return null;
        for (int k = 0; k < 8; k++)
        {
            float b = k * MathF.Tau / 8f;
            foreach (float d in new[] { r, r * 0.5f })
                if (MathF.Abs(Zone.GroundAt(p.X + MathF.Cos(b) * d, p.Z + MathF.Sin(b) * d).Y - h0) > 0.9f) return null;
        }
        return h0;
    }

    /// <summary>How far round a spot the scatter is looked at: a crown reaches out over its trunk.</summary>
    const float CrownReach = 12f;

    readonly Dictionary<(int, int), System.Threading.Tasks.Task<List<PlanetSurface.Terrain.FloraSolid>>> _scatter = new();

    /// <summary>
    /// The trees, rocks and crowns the terrain's own scatter puts within a reach of a point (frame metres), from its far chunks
    /// built on a worker without streaming them in; null while they are still being worked out.
    /// </summary>
    public List<PlanetSurface.Terrain.FloraSolid> ScatterNear(N.Vector3 p, float reach)
    {
        var planet = Zone.Planet;
        double cx = Mathx.WrapX(p.X, planet.Width), cz = Mathx.Wrap(p.Z, planet.Height);
        int size = PlanetSurface.Terrain.FarBuilder.Size;
        int nx = PlanetSurface.Terrain.FarBuilder.Count((int)planet.Width), nz = PlanetSurface.Terrain.FarBuilder.Count((int)planet.Height);
        var found = new List<PlanetSurface.Terrain.FloraSolid>();
        bool ready = true;
        for (int fz = (int)Math.Floor((cz - reach) / size); fz <= (int)Math.Floor((cz + reach) / size); fz++)
        for (int fx = (int)Math.Floor((cx - reach) / size); fx <= (int)Math.Floor((cx + reach) / size); fx++)
        {
            var key = (((fx % nx) + nx) % nx, ((fz % nz) + nz) % nz);
            if (!_scatter.TryGetValue(key, out var job)) _scatter[key] = job = Zone.Streamer.SolidsOfChunk(key.Item1, key.Item2);
            if (!job.IsCompleted) { ready = false; continue; }
            if (job.IsFaulted) continue;
            foreach (var s in job.Result)
                if (Math.Abs(Mathx.WrapDelta(s.Center.X - cx, planet.Width)) < reach + Reach(s) && Math.Abs(Mathx.WrapDelta(s.Center.Z - cz, planet.Height)) < reach + Reach(s)) found.Add(s);
        }
        return ready ? found : null;
    }

    /// <summary>A solid's reach round its centre, flat, m: a trunk's radius, a box's half diagonal, a crown's at its tree's scale.</summary>
    public static float Reach(PlanetSurface.Terrain.FloraSolid s) =>
        s.IsTrunk ? s.Extents.X : new Vector2(s.Extents.X, s.Extents.Z).Length() * (s.IsCanopy ? s.Scale : 1f);

    /// <summary>Nothing of the scatter within a hull's reach of a spot (true), something there (false), or not known yet (null).</summary>
    bool? Unwooded(N.Vector3 p, float r)
    {
        List<PlanetSurface.Terrain.FloraSolid> near = ScatterNear(p, r + CrownReach);
        if (near == null) return null;
        var planet = Zone.Planet;
        double cx = Mathx.WrapX(p.X, planet.Width), cz = Mathx.Wrap(p.Z, planet.Height);
        foreach (var s in near)
        {
            float dx = (float)Mathx.WrapDelta(s.Center.X - cx, planet.Width), dz = (float)Mathx.WrapDelta(s.Center.Z - cz, planet.Height);
            float reach = r + 1.5f + Reach(s);
            if (dx * dx + dz * dz < reach * reach) return false;
        }
        return true;
    }

    /// <summary>Its reach round its middle on the ground, m.</summary>
    static float Radius(Structure s)
    {
        var (lo, hi) = s.Bounds();
        return 0.5f * new Vector2(hi.X - lo.X + 1, hi.Z - lo.Z + 1).Length() * s.Grid.CellSize + 1.5f;
    }

    /// <summary>The highest thing on the way ahead: the ground (or the sea) every 40 m, and over a place its buildings' allowance.</summary>
    public float HighestAhead(N.Vector3 from, N.Vector3 dir, float ahead)
    {
        float top = float.MinValue, sea = Zone.Planet.Source.WaterLevel;
        for (float d = 0f; d <= ahead + 1f; d += 40f)
        {
            N.Vector3 q = from + dir * d;
            top = MathF.Max(top, MathF.Max(Zone.GroundAt(q.X, q.Z).Y, sea));
        }
        var planet = Zone.Planet;
        double fx = Mathx.WrapX(from.X, planet.Width), fz = Mathx.Wrap(from.Z, planet.Height);
        foreach (var (x, z, reach, roof) in _roofs)
        {
            if (roof <= top) continue;
            float rx = (float)Mathx.WrapDelta(x - fx, planet.Width), rz = (float)Mathx.WrapDelta(z - fz, planet.Height);
            if (MathF.Abs(rx) > ahead + reach || MathF.Abs(rz) > ahead + reach) continue;
            float along = Math.Clamp(rx * dir.X + rz * dir.Z, 0f, ahead);
            float sx = rx - dir.X * along, sz = rz - dir.Z * along;
            if (sx * sx + sz * sz < reach * reach) top = roof;
        }
        return top;
    }

    /// <summary>Every place's middle (canonical), its reach and how high its buildings may stand: what the way ahead is held over.</summary>
    (double X, double Z, float Reach, float Roof)[] _roofs;

    /// <summary>The height a place's buildings may reach over its pad, m: a city's towers stand tall.</summary>
    static float Buildings(PoiSite s) => s.Tier >= PoiTier.City ? 90f : s.Tier >= PoiTier.Town ? 40f : 25f;

    /// <summary>The ground's height under a point: its collision where that has streamed in, else the ground's own.</summary>
    public float GroundUnder(N.Vector3 p) => Collides(p) ? Zone.GroundTop(p.X, p.Z) : Zone.GroundAt(p.X, p.Z).Y;

    public bool Collides(N.Vector3 p) => Zone.Streamer.CollidesAt(p.ToGodot());
}
