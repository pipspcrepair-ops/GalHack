using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GalHack.Terrain;
using PlanetSurface.Infra;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Planets.Gen;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;
using NavGrid = Wildlife.NavGrid;
using Terrain = Wildlife.Terrain;

namespace Slice.Places;

/// <summary>
/// A scavenger's trip (owner: "we can't be having disappearing people"): a real place to pick over, a ruin, an abandoned
/// place, and a way there on the land, walked the whole time. Out through the way in, along a road where one goes that way,
/// else over open ground their legs can take (no water, nothing too steep, round what is built), a while searching round
/// its walls, and home along the same way with a bundle on their back, before the end of their day. One body all the way:
/// near you posed and solid and answering you as anyone of the town does; far off the same walk stepped seldom, a point on
/// its way, and drawn again only where that point is, faded in as any far body. Danger on the way (a beast, raiders, you
/// turned on them, a downpour) sends them home at once, at a run if frightened. Robbed or hurt out there, the town hears of
/// it when they get home; killed, it misses them come nightfall.
/// </summary>
public sealed class ScavRun
{
    /// <summary>The way out (the place's frame, on the ground, a point every 2 m at most): the way in's spot, out by it, over the land to the find.</summary>
    public List<Vector3> Out;
    /// <summary>The spots round the find they search at, in order round its walls (the first is the way's end).</summary>
    public List<Vector3> Round;
    /// <summary>The find's middle (the place's frame), what it is, how long the way out is (m), and whether it follows a road.</summary>
    public Vector3 Find;
    public string What = "";
    public float Length;
    public bool Road;
    /// <summary>The way walked now and the next point of it; the round's spot searched now (-1: not there) and for how long still.</summary>
    public List<Vector3> Way;
    public int Leg, Searching = -1;
    public float SearchFor;
    /// <summary>Turned for home; carrying what they found; robbed or turned on by you out here (the town hears of it at home); missed by the town at nightfall.</summary>
    public bool Home, Carrying, Robbed, Wronged, Missed;
    /// <summary>The spot of the round they are at or bound for, and how many they have searched (tests).</summary>
    public int Spot, Searched;
}

public sealed partial class Town
{
    /// <summary>How near and far a find may lie from the way in, m (tests narrow it).</summary>
    public static float RunNearest = 100f, RunFarthest = 1500f;
    /// <summary>How long a spot round a find is searched, s (and the spots searched at most).</summary>
    const float SearchEach = 12f;
    const int SearchSpots = 4;
    /// <summary>How near something has to be to one of them out on the land to be seen as on their way, m.</summary>
    const float WaySight = 45f;

    /// <summary>Runs set out on, finished home, with something brought back, and those missed at nightfall (tests).</summary>
    public int RunsOut { get; private set; }
    public int RunsHome { get; private set; }
    public int Hauls { get; private set; }
    public int Missed { get; private set; }
    /// <summary>What the last run planned came to (tests).</summary>
    public string LastPlan { get; private set; } = "";

    int _gate = -1;
    /// <summary>The spot by the way in.</summary>
    int Gate => _gate >= 0 ? _gate : _gate = Spots.Select((s, i) => (s, i)).OrderBy(t => t.s.Cell.Manhattan(Memory.Inst.Layout.EntranceCell)).First().i;

    System.Threading.Tasks.Task<ScavRun> _planning;
    Person _planFor;

    /// <summary>
    /// At the way in, ready to go: a find and a way there worked out on a worker (they wait at the way in meanwhile), then off.
    /// False with nowhere to go today (too late in their day for anything in reach, nothing there, no way over the land).
    /// </summary>
    bool SetOut(Person p)
    {
        if (_planFor == p)
        {
            if (!_planning.IsCompleted) return true;
            if (_planning.IsFaulted) GD.PushError($"run from {Memory.Site?.Name} failed: {_planning.Exception?.GetBaseException()}");
            ScavRun found = _planning.IsFaulted ? null : _planning.Result;
            (_planning, _planFor) = (null, null);
            if (found == null || !Rolled) { p.Wait = 40f; return true; } // (nothing worth the walk: about the way in a while)
            p.Run = found;
            (found.Way, found.Leg) = (found.Out, 1);
            (p.Path, p.Step, p.Partner, p.Working) = (null, 0, null, false);
            RunsOut++;
            return true;
        }
        if (_planning != null || p.Looks == null || _places?.Zone.Planet is not PlanetWorld planet) return false;
        // Only with the day to get there, search and get home in, with time to spare.
        float time = (float)_places.Zone.Sky.TimeOfDay, end = MathF.Min(p.Figure.Routine?.Out is { Length: 2 } o && o[1] > o[0] ? o[1] : 0.78f, 0.78f);
        float seconds = (end - time) * _places.Zone.Sky.System.DayMinutes * 60f / MathF.Max(0.01f, _places.Zone.Sky.TimeScale);
        if (seconds < 120f) return false;
        Vector3 gate = At(Gate);
        var ask = new Ask
        {
            Planet = planet,
            Origin = _origin,
            Gate = gate,
            Exit = Outside(gate),
            Budget = seconds * 0.6f,
            Speed = p.Speed,
            Seed = Wildlife.Rng.Mix(Memory.Inst.SpawnSeed, (ulong)(People.IndexOf(p) + 1) * 131UL + (ulong)RunsOut),
            Gait = Brain.GaitOf(p.Looks),
            Trunks = Trunks(),
        };
        _planning = System.Threading.Tasks.Task.Run(() => GalHack.Testing.AllocMeter.Measure("town runs", () => Plan(ask)));
        _planFor = p;
        return true;
    }

    /// <summary>What a run is planned from (taken on the main thread, read on a worker).</summary>
    sealed class Ask
    {
        public PlanetWorld Planet;
        public Vector3 Origin, Gate, Exit;
        public float Budget, Speed;
        public ulong Seed;
        public NavGrid.Gait Gait;
        public List<(Vector3 At, float Radius)> Trunks;
    }

    /// <summary>The wild patch's rocks and trunks (world), as its ground's grid has them: what a way over the land goes round.</summary>
    List<(Vector3, float)> Trunks()
    {
        var list = new List<(Vector3, float)>();
        if (_places.Zone.Wildlife?.Sim is not { } sim) return list;
        Vector3 origin = _places.Zone.Wildlife.Origin;
        foreach (Obstacle o in sim.Nav.Obstacles)
            if (o.Active) list.Add((o.Position + origin, o.Radius));
        return list;
    }

    /// <summary>A thing built on the land (world, a footprint's box), and what it is.</summary>
    readonly record struct Footing(Vector3 Mid, float HalfX, float HalfZ, string What, bool Worth);

    /// <summary>
    /// The run (worker): the finds in reach, nearer ones likelier, each tried until one has a way there and back that fits the
    /// day (null: none does). The way: by road where one runs from near the way in to near the find, else over the land.
    /// </summary>
    ScavRun Plan(Ask a)
    {
        PlanetWorld planet = a.Planet;
        Vector3 exit = a.Origin + a.Exit;
        var rng = Wildlife.Rng.For(a.Seed, "run");
        // Everything built round: ruins by the roads and in the country, and the places (the abandoned and ruined worth a look).
        var built = new List<Footing>();
        void Add(TerrainStamp s, string what, bool worth)
        {
            if (s == null) return;
            Vector3 mid = planet.NearestCopy(new Vector3((s.MinX + s.MaxX) * 0.5f, 0f, (s.MinZ + s.MaxZ) * 0.5f), exit);
            if (Flat(mid - exit) > RunFarthest + 200f) return;
            built.Add(new Footing(mid, (s.MaxX - s.MinX) * 0.5f, (s.MaxZ - s.MinZ) * 0.5f, what, worth));
        }
        foreach (FlavourRuin r in planet.Infra.Ruins) Add(r.Stamp, r.Family.Id, true);
        foreach (PlanetSurface.Poi.PoiSite s in planet.Pois)
            if (s != Memory.Site) Add(s.Stamp, s.Family.Id, s.State is PoiState.Abandoned or PoiState.Ruined);
        // (As far as the day allows: there and back at a walk, the way some way longer than the crow flies, and the search.)
        float reach = MathF.Min(RunFarthest, (a.Budget - SearchSpots * (SearchEach + 6f)) * a.Speed * 0.5f / 1.3f);
        var finds = built.Where(f => f.Worth && Flat(f.Mid - exit) is var d && d >= RunNearest && d <= reach)
            .OrderBy(f => Flat(f.Mid - exit) * (0.5f + rng.Float())).Take(5).ToList();
        int no = 0, far = 0;
        foreach (Footing find in finds)
        {
            // The walls are gone round from the side nearest home.
            List<Vector3> round = Round(planet, find);
            if (round.Count < 2) continue;
            int first = 0;
            for (int i = 1; i < round.Count; i++) if (Flat(round[i] - exit) < Flat(round[first] - exit)) first = i;
            round = Enumerable.Range(0, Math.Min(SearchSpots, round.Count)).Select(i => round[(first + i) % round.Count]).ToList();
            List<Vector3> land = ByRoad(planet, exit, round[0], built, a, out bool road) ?? Leg(planet, exit, round[0], built, a);
            if (land == null) { no++; continue; }
            float length = 0f;
            for (int i = 1; i < land.Count; i++) length += Flat(land[i] - land[i - 1]);
            float trip = 2f * (length + 20f) / MathF.Max(0.5f, a.Speed) + round.Count * (SearchEach + 6f);
            if (trip > a.Budget) { far++; continue; }
            var run = new ScavRun
            {
                Out = new List<Vector3> { a.Gate },
                Round = round.Select(r => r - a.Origin).ToList(),
                Find = find.Mid with { Y = Height(planet, find.Mid) } - a.Origin,
                What = find.What,
                Length = length,
                Road = road,
            };
            run.Out.AddRange(land.Select(q => q - a.Origin));
            run.Round[0] = run.Out[^1];
            LastPlan = $"{run.What} {length:F0} m off{(road ? " by road" : "")}";
            return run;
        }
        LastPlan = $"no run: {finds.Count} finds {RunNearest:F0}-{reach:F0} m off of {built.Count} built round ({no} with no way there, {far} too far for the day)";
        return null;
    }

    static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>The ground's height at a world point (its own land, roads laid in it).</summary>
    static float Height(PlanetWorld planet, Vector3 w) => planet.GroundHeight(Mathx.WrapX(w.X, planet.Width), Mathx.Wrap(w.Z, planet.Height));

    static bool Wet(PlanetWorld planet, Vector3 w) => planet.Source.WaterSurfaceAt(Mathx.WrapX(w.X, planet.Width), Mathx.Wrap(w.Z, planet.Height), out _);

    /// <summary>Spots round a find's walls, 1.5 m out (its corners and the middles of its sides, in order round it), on dry ground.</summary>
    static List<Vector3> Round(PlanetWorld planet, Footing f)
    {
        float x = f.HalfX + 1.5f, z = f.HalfZ + 1.5f;
        var round = new List<Vector3>();
        foreach (var (u, v) in new[] { (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) })
        {
            Vector3 at = f.Mid + new Vector3(u * x, 0f, v * z);
            if (Wet(planet, at)) continue;
            round.Add(at with { Y = Height(planet, at) });
        }
        return round;
    }

    /// <summary>
    /// By road: a road (not a railway) passing near both ends, on the ground all the way between (no bridge, no tunnel) and not
    /// much the longer way round; on to it and off it again over the land. Null where none goes that way.
    /// </summary>
    List<Vector3> ByRoad(PlanetWorld planet, Vector3 start, Vector3 end, List<Footing> built, Ask a, out bool road)
    {
        road = false;
        const float Near = 60f;
        float straight = Flat(end - start), best = straight * 1.6f + 60f;
        (InfraRoute R, int A, int B)? pick = null;
        foreach (InfraRoute r in planet.Infra.Routes)
        {
            if (r.Kind == RouteKind.Rail || r.Maglev || r.X.Length < 3) continue;
            int ia = -1, ib = -1;
            float da = Near, db = Near;
            for (int i = 0; i < r.X.Length; i++)
            {
                Vector3 q = planet.NearestCopy(new Vector3((float)r.X[i], 0f, (float)r.Z[i]), start);
                float fa = Flat(q - start), fb = Flat(q - end);
                if (fa < da) (da, ia) = (fa, i);
                if (fb < db) (db, ib) = (fb, i);
            }
            if (ia < 0 || ib < 0 || Math.Abs(ib - ia) < 3) continue;
            int step = ib > ia ? 1 : -1;
            float along = 0f;
            bool ground = true;
            for (int i = ia; i != ib && ground; i += step)
            {
                ground = r.Span.Length == 0 || r.Span[Math.Min(i, i + step)] == RoadSpan.Ground;
                along += Flat(new Vector3((float)(r.X[i + step] - r.X[i]), 0f, (float)(r.Z[i + step] - r.Z[i])));
            }
            float total = da + along + db;
            if (ground && total < best) (best, pick) = (total, (r, ia, ib));
        }
        if (pick is not var (rr, a0, b0)) return null;
        Vector3 Point(int i) => planet.NearestCopy(new Vector3((float)rr.X[i], 0f, (float)rr.Z[i]), start);
        List<Vector3> on = Leg(planet, start, Point(a0), built, a), off = Leg(planet, Point(b0), end, built, a);
        if (on == null || off == null) return null;
        var way = new List<Vector3>(on);
        int dir = b0 > a0 ? 1 : -1;
        for (int i = a0 + dir; i != b0; i += dir) Densify(planet, way, Point(i));
        foreach (Vector3 q in off.Skip(1)) way.Add(q);
        road = true;
        return way;
    }

    /// <summary>Points on to a way every 2 m at most up to a new point, each on the ground.</summary>
    static void Densify(PlanetWorld planet, List<Vector3> way, Vector3 end)
    {
        Vector3 start = way.Count > 0 ? way[^1] : end;
        int n = Math.Max(1, (int)MathF.Ceiling(Flat(end - start) / 2f));
        for (int k = 1; k <= n; k++)
        {
            Vector3 q = start.Lerp(end, k / (float)n);
            way.Add(q with { Y = Height(planet, q) });
        }
    }

    /// <summary>
    /// A way over the land between two world points (worker): the wild sim's grid (<see cref="NavGrid"/>) laid over the ground
    /// between them, 2 m cells, kept off water and anything built (the town's own walls among it) and round the rocks and
    /// trunks the patch knows, searched for these legs (no slope too steep, no drop too high), then a point every 2 m on the
    /// ground. Null where there is no way.
    /// </summary>
    List<Vector3> Leg(PlanetWorld planet, Vector3 start, Vector3 end, List<Footing> built, Ask a)
    {
        Vector3 mid = ((start + end) * 0.5f) with { Y = 0f };
        float half = MathF.Min(MathF.Max(MathF.Abs(end.X - start.X), MathF.Abs(end.Z - start.Z)) * 0.5f + 60f, 800f);
        var ground = new Terrain((x, z) => Height(planet, mid + new Vector3(x, 0f, z)), half);
        var nav = new NavGrid(half, 2f);
        nav.Ground(ground);
        Vector3 a0 = start - mid, b0 = end - mid;
        bool Clear(Vector3 local) => Flat(local - a0) > 2.5f && Flat(local - b0) > 2.5f;
        for (float z = -half + 1f; z < half; z += 4f)
        for (float x = -half + 1f; x < half; x += 4f)
            if (Wet(planet, mid + new Vector3(x, 0f, z)) && Clear(new Vector3(x, 0f, z))) nav.AddObstacle(new Vector3(x, 0f, z), 2.8f);
        foreach (Footing f in built)
        {
            Vector3 c = f.Mid - mid;
            if (MathF.Abs(c.X) > half + f.HalfX || MathF.Abs(c.Z) > half + f.HalfZ) continue;
            for (float z = -f.HalfZ; z <= f.HalfZ + 0.01f; z += 2f)
            for (float x = -f.HalfX; x <= f.HalfX + 0.01f; x += 2f)
                if (Clear(c + new Vector3(x, 0f, z))) nav.AddObstacle(c + new Vector3(x, 0f, z), 1.6f);
        }
        // The town's own walls: the cells of its blocks standing a body high over the ground.
        BlockGrid g = Memory.Inst.Blocks;
        for (int z = g.Min.Z; z < g.Min.Z + g.Size.Z; z += 2)
        for (int x = g.Min.X; x < g.Min.X + g.Size.X; x += 2)
        {
            Vector3 w = a.Origin + new Vector3(x + 0.5f, 0f, z + 0.5f);
            Vector3 l = w - mid;
            if (MathF.Abs(l.X) > half || MathF.Abs(l.Z) > half || !Clear(l)) continue;
            int foot = (int)MathF.Floor(Height(planet, w) - a.Origin.Y + 0.3f);
            for (int y = foot; y <= foot + 1; y++)
                if (!g.Get(new PInt3(x, y, z)).IsAir) { nav.AddObstacle(l, 1.3f); break; }
        }
        foreach (var (at, r) in a.Trunks)
        {
            Vector3 l = planet.NearestCopy(at, mid) - mid;
            if (MathF.Abs(l.X) < half && MathF.Abs(l.Z) < half && Clear(l)) nav.AddObstacle(l with { Y = 0f }, r + 0.3f);
        }
        var path = new List<Vector3>();
        if (!nav.FindPath(a0, b0, path, nav.Size * nav.Size / 2, a.Gait)) return null;
        var way = new List<Vector3> { start with { Y = Height(planet, start) } };
        foreach (Vector3 q in path) Densify(planet, way, q + mid);
        if (Flat(way[^1] - end) > 0.1f) Densify(planet, way, end);
        return way;
    }

    /// <summary>The farthest point a body walks to straight out from a point (local) along the way in's facing, up to 10 m: floor all the way at its level, clear of the parts.</summary>
    Vector3 Outside(Vector3 from)
    {
        PInt3 f = Memory.Inst.Layout.EntranceFacing.Vec();
        Vector3 dir = new Vector3(f.X, 0f, f.Z) is var d && d.LengthSquared() > 0.1f ? d.Normalized() : Vector3.Zero;
        Vector3 last = from;
        for (float s = 0.5f; s <= 10f && dir != Vector3.Zero; s += 0.5f)
        {
            Vector3 at = from + dir * s;
            if (!OnGrid(at) || Ground(at) is not float g || MathF.Abs(g - from.Y) > 0.3f || _parts != null && _parts.Gap(at, PlaceParts.PersonRadius, PlaceParts.PersonHeight) < 0.05f) break;
            last = at;
        }
        return last;
    }

    // ---- Under way ----------------------------------------------------------------------------------

    /// <summary>Something (world) is near one of them out on the land: they see it as on their way.</summary>
    bool OnTheirWay(Vector3 world)
    {
        foreach (Person p in People)
            if (p.Run != null && !p.Dead && !p.Gone && Flat(World(p.Pos) - world) < WaySight) return true;
        return false;
    }

    /// <summary>
    /// A step of a run: out along the way, searching round the find, home along it again; at once for home with danger about,
    /// at the end of their day, or in a downpour (at a run if frightened). Home through the way in, they're back in the day of
    /// the town, with what they found, and what happened to them out there told.
    /// </summary>
    void Trek(Person p, Vector3 player, Mood mood, bool offDuty, float dt)
    {
        ScavRun r = p.Run;
        float rain = _places.Zone.Weather?.Sim?.RainAt(World(p.Pos)) ?? 0f;
        bool frightened = p.ScaredFor > 0f || r.Wronged && !Fighter(p);
        if (!r.Home && (frightened || r.Wronged || offDuty || Night() || rain > 0.5f || Late(p))) TurnHome(p); // (something only to keep wide of, they keep wide of and go on)
        p.Fleeing = r.Home && frightened;
        float walk = p.Spawn.Gait == "hurry" ? 3.0f : p.Spawn.Gait == "amble" ? 1.1f : 1.3f;
        p.Speed = p.Fleeing ? 3.4f : walk * (p.Job switch { PersonRole.Elder => 0.78f, PersonRole.Drunk => 0.72f, _ => 1f });
        p.Working = r.Searching >= 0;
        if (r.Searching >= 0)
        {
            // Searching round the walls: bent to it, facing them, a while at each spot; then the next, and home after the last.
            p.Now = Mathf.MoveToward(p.Now, 0f, PeopleAccel * 2f * dt);
            Vector3 d = r.Find - p.Pos;
            p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-d.X, -d.Z), MathF.Min(1f, dt * 3f));
            if ((r.SearchFor -= dt) > 0f) return;
            r.Searched++;
            r.Searching = -1;
            if (++r.Spot < r.Round.Count) (r.Way, r.Leg) = (new List<Vector3> { r.Round[r.Spot] }, 0);
            else { r.Carrying = p.Laden = true; TurnHome(p); }
            return;
        }
        if (!Walk(p, dt)) return;
        // (Home with the place still down, coming up as you come: they stand at the way in till it is up, in the town again from there;
        // with nobody near to see, they are simply home, kept with the place.)
        if (r.Home) { if (_place is { Ready: true, Dormant: false } || (player - p.Pos).Length() > PersonView.FarRange + 50f) Back(p); else p.Now = 0f; return; }
        // At the find, or the next spot round it: search there.
        r.Searching = r.Spot;
        r.SearchFor = SearchEach * (0.7f + 0.6f * (float)_rng.NextDouble());
    }

    /// <summary>Too little of their day left to get home in at a walk (with room to spare).</summary>
    bool Late(Person p)
    {
        float end = MathF.Min(p.Figure.Routine?.Out is { Length: 2 } o && o[1] > o[0] ? o[1] : 0.78f, 0.78f);
        float left = (end - (float)_places.Zone.Sky.TimeOfDay) * _places.Zone.Sky.System.DayMinutes * 60f / MathF.Max(0.01f, _places.Zone.Sky.TimeScale);
        return left < Flat(p.Pos - At(Gate)) * 1.5f / MathF.Max(0.5f, p.Speed) + 30f;
    }

    /// <summary>For home from wherever they are on it: back the way they came (round the find's walls, then the way out).</summary>
    void TurnHome(Person p)
    {
        ScavRun r = p.Run;
        if (r.Home) return;
        r.Home = true;
        r.Searching = -1;
        var back = new List<Vector3>();
        if (r.Way == r.Out)
            for (int i = Math.Min(r.Leg, r.Out.Count) - 1; i >= 0; i--) back.Add(r.Out[i]);
        else
        {
            for (int i = Math.Min(r.Spot, r.Round.Count) - 1; i >= 0; i--) back.Add(r.Round[i]);
            for (int i = r.Out.Count - 2; i >= 0; i--) back.Add(r.Out[i]);
        }
        (r.Way, r.Leg) = (back, 0);
    }

    /// <summary>Through the way in, back: their run over, what they brought in (on their back the rest of the day), what befell them told.</summary>
    void Back(Person p)
    {
        ScavRun r = p.Run;
        RunsHome++;
        if (r.Carrying) Hauls++;
        p.Run = null;
        (p.Path, p.Step, p.Wait, p.Fleeing, p.Working) = (null, 0, r.Carrying ? 4f : 45f, false, false); // (sent home empty-handed, a while before trying again)
        p.Spot = Gate;
        p.ProgressAt = p.Pos;
        // Robbed or shot at out there: now the town knows.
        if (r.Robbed) { Did(Deed.Rob); Learn(p); }
        if (r.Wronged) Did(Deed.Harm);
    }

    /// <summary>Along the way walked now, as anyone walks (easing into their pace and out of it at the end), on the ground under them; true at its end.</summary>
    bool Walk(Person p, float dt)
    {
        ScavRun r = p.Run;
        List<Vector3> way = r.Way;
        if (r.Leg >= way.Count) { p.Now = 0f; return true; }
        Vector3 next = way[r.Leg] - p.Pos;
        bool last = r.Leg >= way.Count - 1;
        float flat = Flat(next), pace = p.Speed * p.Feel.Pace;
        float wanted = last ? MathF.Min(pace, MathF.Max(0.3f, MathF.Sqrt(2f * PeopleAccel * flat))) : pace;
        p.Now = Mathf.MoveToward(p.Now, wanted, PeopleAccel * dt);
        float step = p.Now * dt;
        p.Gait += step;
        Vector3 heading = next;
        while (step > 0f && r.Leg < way.Count)
        {
            Vector3 to = way[r.Leg], d = to - p.Pos;
            float f = Flat(d);
            if (f <= step) { p.Pos = to; step -= f; r.Leg++; continue; }
            p.Pos += new Vector3(d.X, 0f, d.Z) / f * step;
            p.Pos.Y = Mathf.Lerp(p.Pos.Y, to.Y, step / f);
            heading = d;
            step = 0f;
        }
        if (Flat(heading) > 0.01f) p.Yaw = Mathf.RotateToward(p.Yaw, MathF.Atan2(-heading.X, -heading.Z), 5f * dt);
        p.Sitting = false;
        return r.Leg >= way.Count;
    }

    /// <summary>A shot at one of them out on a run (at them, or hitting them): by a creature, met as a danger; by you, they're done with you and the run.</summary>
    void ShotAt(Person p, object by)
    {
        if (by is Creature c) { Harmed(c); return; }
        if (by is not (null or Slice.Vessels.Vessel)) return;
        p.Run.Wronged = true;
        if (!Fighter(p)) p.ScaredFor = MathF.Max(p.ScaredFor, 12f);
    }

    /// <summary>Nightfall: one of theirs out on a run and not back (lying dead out there) is missed.</summary>
    void Miss()
    {
        if (!Night()) return;
        foreach (Person p in People)
        {
            if (p.Run is not { Missed: false } r || !p.Dead && !p.Gone) continue;
            r.Missed = true;
            Missed++;
            foreach (Person q in People)
                if (q != p && !q.Dead && !q.Gone && q.Run == null && q.Looks != null) { Bind(q); q.Feel.Hit(q.Partner == p ? MoodEvent.LostKin : MoodEvent.SawDeath, 0.5f); }
        }
    }

    // ---- What they carry home -----------------------------------------------------------------------

    /// <summary>The bundle off their back (robbed of it, or put down for the night).</summary>
    static void Unload(Person p)
    {
        p.Laden = false;
        if (p.Sack != null && GodotObject.IsInstanceValid(p.Sack)) p.Sack.QueueFree();
        p.Sack = null;
    }

    /// <summary>Their bundle where their back is as posed: made when first needed (a body built again is given it again); put down in bed.</summary>
    void PoseSack(Person p)
    {
        if (p.Laden && p.Asleep) Unload(p);
        if (!p.Laden || p.View == null || p.Rig == null) return;
        if (p.Sack == null || !GodotObject.IsInstanceValid(p.Sack) || p.Sack.GetParent() != p.View)
        {
            p.Sack = Sack(Wildlife.Rng.Mix(Memory.Inst.SpawnSeed, (ulong)People.IndexOf(p) + 7UL), p.Figure);
            p.View.AddChild(p.Sack);
        }
        int body = p.Rig.Layout.Pieces.FindIndex(x => x.Name == "body");
        if (body < 0) return;
        Transform3D t = (p.View.Drawn ?? p.Rig.Pose)[body];
        float torso = p.Figure.Torso, deep = p.Figure.ChestD * p.Figure.Padding;
        p.Sack.Transform = new Transform3D(t.Basis, t.Origin + t.Basis.Y * torso * 0.55f + t.Basis.Z * (deep + 0.14f));
    }

    /// <summary>A bundle of salvage made of simple parts: a cloth sack, tied at the neck, with a length of pipe and a plate of scrap lashed to it.</summary>
    static Node3D Sack(ulong seed, Figure f)
    {
        var rng = Wildlife.Rng.For(seed, "sack");
        var root = new Node3D { Name = "Sack" };
        float s = Math.Clamp(f.Height / 1.75f, 0.6f, 1.3f);
        Color cloth = Color.FromHsv(rng.Range(0.05f, 0.14f), rng.Range(0.2f, 0.45f), rng.Range(0.3f, 0.5f));
        StandardMaterial3D Mat(Color c, float metal) => new() { AlbedoColor = c, Roughness = 0.85f, Metallic = metal };
        void Part(Mesh m, Godot.Material mat, Vector3 at, Vector3 scale, Vector3 turn)
        {
            m.SurfaceSetMaterial(0, mat);
            root.AddChild(new MeshInstance3D { Mesh = m, Position = at * s, Scale = scale * s, RotationDegrees = turn });
        }
        Part(new SphereMesh { Radius = 0.2f, Height = 0.42f, RadialSegments = 10, Rings = 6 }, Mat(cloth, 0f), Vector3.Zero, new Vector3(1.05f, 1f, 0.7f), Vector3.Zero);
        Part(new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.06f, Height = 0.1f, RadialSegments = 8 }, Mat(cloth.Darkened(0.3f), 0f), new Vector3(0f, 0.22f, 0f), Vector3.One, Vector3.Zero);
        Color rust = Color.FromHsv(rng.Range(0.02f, 0.08f), rng.Range(0.3f, 0.6f), rng.Range(0.25f, 0.45f));
        Part(new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.62f, RadialSegments = 6 }, Mat(rust, 0.6f), new Vector3(rng.Range(-0.12f, 0.12f), 0.12f, 0.1f), Vector3.One, new Vector3(0f, 0f, rng.Range(-28f, 28f)));
        Part(new BoxMesh { Size = new Vector3(0.26f, 0.2f, 0.02f) }, Mat(rust.Lightened(0.15f), 0.5f), new Vector3(0f, -0.04f, 0.15f), Vector3.One, new Vector3(rng.Range(-8f, 8f), 0f, rng.Range(-15f, 15f)));
        return root;
    }
}
