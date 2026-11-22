using System;
using System.Collections.Generic;
using GalHack.Physics;
using Godot;
using Wildlife;
using PoiGenerator.Gen;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Places;

/// <summary>
/// The town's people fall (owner, 21 Nov 2026: "if NPCs have the floor destroyed beneath them, they don't fall"): whoever has
/// nothing under their feet, in the rooms or out on the land, standing, walking, sitting, asleep or cowering, comes down under the
/// world's pull (<see cref="Drop"/>) on to whatever is really below (a floor, a stair, rubble, the ground, into water), is hurt
/// by the sleeper's own fall rule, and goes on from there, frightened. They are looked under only when something under them
/// goes (blocks broken, a piece taken, the ground dug), while they fall, and now and then one at a time near you.
/// </summary>
public sealed partial class Town
{
    public sealed partial class Person
    {
        /// <summary>Falling: how fast (m/s, down), and where they last stood (the place's frame).</summary>
        public bool Airborne;
        public float FallSpeed;
        public Vector3 FirmAt;
        /// <summary>How long more they are looked under each step (something under them went; its colliders may follow a moment later).</summary>
        public float CheckFor;
        /// <summary>When the blocks last showed no floor under them as they walked (clock): looked under once a second at most for it.</summary>
        public float NoFloorAt = -9f;
        /// <summary>Clambering up the side of a hole (held to it, not falling), and in a place, the rim they climb to (the place's frame).</summary>
        public bool Clambering;
        public Vector3? ClimbTo;
        /// <summary>Falls landed (tests), and how long the one under way has lasted.</summary>
        public int Falls;
        public float LastFallTime;
    }

    /// <summary>The world's pull, m/s².</summary>
    float Pull => _places?.Zone.Physics.Zone.Strength ?? 9.8f;

    /// <summary>How far down anything is looked for under a falling body, m.</summary>
    const float FallReach = 80f;

    /// <summary>How far over what is under their feet someone may be and still stand on it, m.</summary>
    const float FootSlack = 0.3f;

    /// <summary>A drop no more than this is a step down, not a fall that shakes them, m.</summary>
    const float StepDown = 0.6f;

    /// <summary>Looked under in the slow sweep only this near you (out beyond it there may be nothing built to collide with), m.</summary>
    const float SweepRange = 60f;

    /// <summary>Support checks made, rays cast for them (measurements).</summary>
    public int FallChecks { get; private set; }
    public int FallRays { get; private set; }
    /// <summary>The time all of it has taken (looking under them, falling, landing), ms (measurements).</summary>
    public double FallMs { get; private set; }

    int _sweep, _looks;

    /// <summary>How long the place's colliders have been behind its blocks, s; and how long that holds a look under anyone (a long rebuild never holds a fall back for long).</summary>
    float _staleFor;
    const float StaleWait = 0.3f;

    /// <summary>The most of them looked under in a tick (those falling are always stepped).</summary>
    const int LooksATick = 1;

    /// <summary>A look under someone this long or longer to come is for something that went from under them (not the sweep's), s.</summary>
    const float Pressing = 0.2f;

    /// <summary>
    /// Something under a stretch went (world frame: blocks broken, a piece taken, the ground dug): whoever stands, sits or lies over
    /// it is looked under for the next second, and a body lying there falls again, limp.
    /// </summary>
    public void Unfloored(Vector3 lo, Vector3 hi)
    {
        lo = Local(lo);
        hi = Local(hi);
        _gone.Add(new Aabb(lo - new Vector3(1.5f, 1f, 1.5f), hi - lo + new Vector3(3f, 4f, 3f)));
        foreach (Person p in People)
        {
            if (p.Gone || p.Pos.X < lo.X - 1.2f || p.Pos.X > hi.X + 1.2f || p.Pos.Z < lo.Z - 1.2f || p.Pos.Z > hi.Z + 1.2f || p.Pos.Y < lo.Y - 0.5f || p.Pos.Y > hi.Y + 2.5f) continue;
            if (!p.Dead) p.CheckFor = 1f;
            else if (p.Laid)
            {
                p.Laid = false;
                p.Rig?.Unsettle();
            }
        }
    }

    /// <summary>Where something has gone from under (<see cref="Unfloored"/>, widened by a stride and a storey), the place's frame.</summary>
    readonly List<Aabb> _gone = new();

    /// <summary>
    /// Whether the blocks hold them where they stand: their feet on the walking grid's floor or stair under them (out on the land, on
    /// the land as it stands). Where nothing has gone, a standing cell beside them at their height holds them too (a corner cut by a
    /// stairwell's edge: the place stands as it was built). Only otherwise do the colliders say (<see cref="Footed"/>: a bed, rubble).
    /// </summary>
    bool AsBuilt(Person p)
    {
        PInt3 c = Cell(p.Pos);
        if (!Memory.Inst.Blocks.InBounds(new PInt3(c.X, Memory.Inst.Blocks.Min.Y, c.Z))) return p.Pos.Y - LandAt(p.Pos) < FootSlack + 0.05f; // (out on the land, on it)
        if (Ground(p.Pos) is float ground && MathF.Abs(p.Pos.Y - ground) < FootSlack + 0.05f || _nav.RampAt(c, out _) || _nav.RampAt(c + PInt3.Down, out _)) return true; // (on a stair, whose steps the rays may miss)
        foreach (Aabb box in _gone) if (box.HasPoint(p.Pos)) return false;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (_nav.Standable(new PInt3(c.X + dx, c.Y, c.Z + dz)) || _nav.RampAt(new PInt3(c.X + dx, c.Y, c.Z + dz), out _) || _nav.RampAt(new PInt3(c.X + dx, c.Y - 1, c.Z + dz), out _)) return true;
        return false;
    }

    /// <summary>The top of what is solid under a point (the place's frame), from <paramref name="above"/> over it to <paramref name="below"/> under it; null with nothing (or nothing built to tell).</summary>
    float? Top(Vector3 local, float above, float below)
    {
        // (The place's own colliders where it stands; away from it (its people out on the land, in a river), the land's.)
        Node3D near = _view is { } v && v.IsInsideTree() ? v : _places?.Zone is { } z && z.IsInsideTree() ? z : null;
        if (near == null) return null;
        Vector3 from = World(local + Vector3.Up * above);
        var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * (above + below), Layers.World | Layers.Hull | Layers.Interior);
        var hit = near.GetWorld3D().DirectSpaceState.IntersectRay(q);
        return hit.Count == 0 ? null : Local((Vector3)hit["position"]).Y;
    }

    /// <summary>Their feet's reach round their middle: the four points of their footing besides the middle, m.</summary>
    static Vector3 Foot(Person p, int i)
    {
        float r = BodyRadius(p) * 0.8f;
        return i switch { 1 => new(r, 0f, 0f), 2 => new(-r, 0f, 0f), 3 => new(0f, 0f, r), 4 => new(0f, 0f, -r), _ => Vector3.Zero };
    }

    /// <summary>
    /// Whether something holds them up, by their real footing (their middle and four points round it): under the middle, or under
    /// three of the rest, looked at from a hand clear of what they stand against (never held by the very edge of a wall they touch).
    /// Held only by an edge, they go off it as they fall (<see cref="Down"/> passes such an edge by).
    /// </summary>
    bool Footed(Person p)
    {
        FallChecks++;
        FallRays += 5;
        int held = 0;
        Vector3 at = p.Pos + OffWall(p) * 0.05f;
        for (int i = 0; i < 5; i++)
            if (Top(at + Foot(p, i), 0.5f, FootSlack) is float)
            {
                if (i == 0) return true;
                held++;
            }
        return held >= 3;
    }

    /// <summary>A seat's top under someone sitting, or null if the seat has gone from under them.</summary>
    float? SeatUnder(Person p) => Top(p.Pos + Vector3.Up * 0.9f, 0f, 0.85f);

    /// <summary>
    /// Their fall this step: looked under if something under them went (or the sweep's turn is theirs), and if nothing holds them,
    /// down they go, wherever they were and whatever they were at; then each step down to what is below. True while it has them
    /// (and the step they land).
    /// </summary>
    bool Falling(Person p, float dt)
    {
        if (p.Swimming || p.Wading) { p.CheckFor = 0f; return false; } // (the water has them: Town.Water)
        if (_view == null || !_view.IsInsideTree())
        {
            p.CheckFor = 0f; // (nothing built to stand on or to tell by: a day without the game)
            return false;
        }
        long from = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (!p.Airborne)
            {
                // (The place's colliders a moment behind its blocks: looked under once they have caught up; a few a tick at most, the rest the next.)
                // (Those something went from under are looked at every step; the sweep's and a walker's missing floor, one a step.)
                if (p.CheckFor <= 0f || p.Clambering && p.ClimbTo != null || p.CheckFor < Pressing && _looks >= LooksATick) return false; // (held to a wall climbing out: not looked under)
                // (The colliders still catching up with the blocks: where the blocks show nothing under them, held there a moment, not
                // walked on over the gap on the old floor; looked under once the colliders have it.)
                if (_staleFor > 0f)
                {
                    if (!AsBuilt(p)) p.Now = 0f;
                    if (_staleFor < StaleWait) return false;
                }
                if (p.CheckFor < Pressing) _looks++;
                _looks++;
                if (_staleFor <= 0f) p.CheckFor -= dt; // (its time runs once the colliders have caught up)
                // (Held by the blocks where they stand, or else by what is really under them; a seat gone from under one sitting, down
                // on the floor and up off it frightened.)
                if (AsBuilt(p) || Footed(p))
                {
                    if (p.Sitting && p.Seated > 0.5f && SeatUnder(p) == null) { Unseat(p); p.ScaredFor = MathF.Max(p.ScaredFor, 4f); }
                    return false;
                }
                LoseFooting(p);
            }
            Down(p, dt);
            return true;
        }
        finally { FallMs += System.Diagnostics.Stopwatch.GetElapsedTime(from).TotalMilliseconds; }
    }

    /// <summary>Off their feet: up out of a seat or a bed, falling from where they are (their way kept: a step down, they go on along it).</summary>
    void LoseFooting(Person p)
    {
        Unseat(p);
        (p.Airborne, p.FallSpeed, p.FirmAt, p.CheckFor, p.LastFallTime) = (true, 0f, p.Pos, 0f, 0f);
        (p.Partner, p.Now, p.Yield, p.ClimbTo, p.Clambering) = (null, 0f, 0f, null, false);
    }

    void Unseat(Person p) => (p.Sitting, p.Seat, p.GettingUp, p.Asleep, p.Working) = (false, -1, false, false, false); // (sat or lain, they come out of it as their pose eases)

    /// <summary>A step of a fall: down by the world's pull to the highest thing under their footing (or into water), landing there.</summary>
    void Down(Person p, float dt)
    {
        p.LastFallTime += dt;
        // Held off what is beside them first, and looked under from there: never caught on the very edge of what they stood against.
        OffSides(p, 1);
        FallRays += 5;
        float reach = p.FallSpeed * dt + 0.5f * Pull * dt * dt + 0.05f;
        // What they come down on holds them as standing does (Footed): under their middle, or under three of the rest; an edge
        // under less of them they go past (off it), never caught on it.
        // (On what is under their middle when there is something; else the highest of the rest, if under three of them.)
        float ground = float.NegativeInfinity, rest = float.NegativeInfinity;
        int under = 0;
        for (int i = 0; i < 5; i++)
        {
            if (Top(p.Pos + Foot(p, i), 0.5f, reach) is not float top) continue;
            if (i == 0) { ground = top; break; } // (on what is under their middle: the rest need not be looked at)
            else (rest, under) = (MathF.Max(rest, top), under + 1);
        }
        if (float.IsNegativeInfinity(ground) && under >= 3) ground = rest;
        // (Off the place's grid the land is under them as it stands, built to collide with or not.)
        if (!Memory.Inst.Blocks.InBounds(Cell(p.Pos)) && _places?.Zone.Planet is { } planet && World(p.Pos) is var w)
            ground = MathF.Max(ground, planet.Terrain.GroundNow(w.X, w.Z) - _origin.Y);
        // Into water: down to where they swim, or to its floor where it is shallow enough to stand in; deep enough, it takes the blow.
        bool wet = false;
        if (Surface(p.Pos) is float water && water > ground)
        {
            wet = water - ground > Wade(p);
            ground = MathF.Max(ground, water - SwimDepth(p));
        }
        float y = p.Pos.Y, speed = p.FallSpeed;
        bool down = Drop.Step(ref y, ref speed, ground, Pull, dt);
        p.Pos.Y = y;
        p.FallSpeed = speed;
        // Clear of what they now pass (a ledge, an eave): a long fall held off it before it is into them; a step down or a landing
        // eased off it (0.1 m a tick at most with the first).
        OffSides(p, down ? 1 : speed > 6f ? 4 : speed > 3f ? 2 : 1);
        if (!down) return;
        Land(p, speed, wet);
    }

    readonly CapsuleShape3D _body = new();
    PhysicsShapeQueryParameters3D _bodyQuery;

    /// <summary>
    /// The way out of what their body touches beside them (a wall's foot, the edge of the hole), flat and a unit; zero touching
    /// nothing. (Out along the face it touches, turned away from where it touches if the two disagree.)
    /// </summary>
    Vector3 OffWall(Person p)
    {
        float r = BodyRadius(p), h = MathF.Max(2f * r, Height(p) - 0.25f);
        (_body.Radius, _body.Height) = (r, h);
        _bodyQuery ??= new PhysicsShapeQueryParameters3D { Shape = _body, CollisionMask = Layers.World | Layers.Hull | Layers.Interior };
        _bodyQuery.Transform = new Transform3D(Basis.Identity, World(p.Pos + Vector3.Up * (0.25f + h * 0.5f)));
        var rest = _view.GetWorld3D().DirectSpaceState.GetRestInfo(_bodyQuery);
        if (rest.Count == 0) return Vector3.Zero;
        Vector3 face = (Vector3)rest["normal"] with { Y = 0f }, away = (World(p.Pos) - (Vector3)rest["point"]) with { Y = 0f };
        if (face.LengthSquared() < 1e-4f) face = away;
        else if (away.LengthSquared() > 1e-4f && face.Dot(away) < 0f) face = -face;
        return face.LengthSquared() < 1e-6f ? Vector3.Zero : face.Normalized();
    }

    /// <summary>Falling past something beside them (a wall's top, a beam, the edge of the hole): held off it sideways, never through it, never further than a metre from where they stood (held off it, not carried along it).</summary>
    void OffSides(Person p, int most)
    {
        for (int k = 0; k < most; k++)
        {
            Vector3 away = OffWall(p);
            if (away == Vector3.Zero) return;
            Vector3 next = p.Pos + away * 0.05f;
            if (new Vector2(next.X - p.FirmAt.X, next.Z - p.FirmAt.Z).Length() > 1f) return;
            p.Pos = next;
        }
    }

    /// <summary>Down: hurt by the sleeper's fall rule (water takes the blow), frightened, and on from where they landed, their way looked for again.</summary>
    void Land(Person p, float speed, bool wet)
    {
        p.Airborne = false;
        p.Falls++;
        float share = wet ? 0f : Slice.Player.Survivor.FallShare(_places.Zone.Physics.Rules, speed);
        if (share > 0f) Struck(p, share * p.Health.Max, GalHack.Arms.DamageClass.Kinetic, byPlayer: false);
        if (p.Dead) return;
        p.CheckFor = 0.3f; // (landed on an edge: off it, or stood on what is there)
        if (BedIn(p.Pos) != null) { EnterWater(p); return; } // (in water: out of it first, Town.Water)
        // A step down (off a kerb, a pad's edge): on as they were, unshaken; out on a run their way goes on by itself.
        if (p.FirmAt.Y - p.Pos.Y < StepDown) return;
        // Startled by it (their body says so), on at once.
        (p.StartleFor, p.Wait, p.Refuge, p.ProgressAt, p.StuckFor) = (0.9f, 0f, -1, p.Pos, 0f);
        // On from here: out on a run, home; in the place, by a way from where they landed to the nearest spot there is one to; in a hole
        // with none, up its side to where there is; off the place's grid, home over the land; on open ground past the search, where they
        // were bound (or somewhere else).
        if (p.Run != null) p.ScaredFor = MathF.Max(p.ScaredFor, 4f);
        else if (!OnAfterFall(p))
        {
            if (p.Spot >= 0 && Whole(p.Spot)) GoTo(p, p.Spot);
            else Next(p);
        }
        if (share > 0f) p.Feel.Hit(MoodEvent.Harm); // (hurt by it, shaken by it)
    }

    /// <summary>The slow sweep: each town step, the next of them near you is looked under (a floor gone with nothing told).</summary>
    void Sweep(Vector3 player)
    {
        for (int k = 0; k < People.Count; k++)
        {
            Person p = People[_sweep = (_sweep + 1) % People.Count];
            if (p.Dead || p.Gone || p.Airborne || (p.Pos - player).LengthSquared() > SweepRange * SweepRange) continue;
            p.CheckFor = MathF.Max(p.CheckFor, 1e-3f);
            return;
        }
    }

    /// <summary>
    /// Out on the land a run's way has the ground's heights as the planet made it. Where it has been dug since, their feet go on the
    /// ground as it is: over a hole they are looked under (and fall in), and out of one they clamber up its side, not walk through it.
    /// </summary>
    void OnLand(Person p, Vector3 was, float dt)
    {
        p.Clambering = false;
        // (Walked into water deeper than they wade: out of it to the nearest bank, Town.Water; their way, planned round water, goes on after.
        // Shallower, they wade across on their way, feet on its bed and slowed: Walk.)
        if (BedIn(p.Pos) is float bed)
        {
            if (Surface(p.Pos) - bed > Wade(p)) EnterWater(p);
            else p.Pos.Y = bed;
            return;
        }
        GalHack.Terrain.PlanetTerrain t = _places.Zone.Planet.Terrain;
        Vector3 w = World(p.Pos);
        var chunk = TerrainDigging.Terrain.ChunkStore.ChunkOf(t.Canonical(new TerrainDigging.Terrain.Int3((int)MathF.Floor(w.X), (int)MathF.Floor(w.Y), (int)MathF.Floor(w.Z))));
        if (t.Store.RemovedMask(chunk) == null && t.Store.RemovedMask(new TerrainDigging.Terrain.Int3(chunk.X, chunk.Y - 1, chunk.Z)) == null) return;
        float ground = t.GroundNow(w.X, w.Z) - _origin.Y;
        if (ground < was.Y - FootSlack)
        {
            p.Pos.Y = was.Y;
            p.CheckFor = MathF.Max(p.CheckFor, 0.05f);
        }
        else if (ground > was.Y + 0.7f)
        {
            p.Pos = was with { Y = was.Y + Clamber * dt };
            p.Clambering = true;
        }
        else p.Pos.Y = ground;
    }

    /// <summary>How fast someone clambers up out of a hole, m/s.</summary>
    const float Clamber = 1.2f;

    /// <summary>The cell someone stands in, or the nearest standable beside it or a cell under it (never over: that is a climb); null if none.</summary>
    PInt3? StandIn(PInt3 c)
    {
        if (_nav.Standable(c)) return c;
        for (int dy = 0; dy >= -1; dy--)
            for (int r = dy == 0 ? 1 : 0; r <= 2; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == r && _nav.Standable(new PInt3(c.X + dx, c.Y + dy, c.Z + dz))) return new PInt3(c.X + dx, c.Y + dy, c.Z + dz);
        return null;
    }

    /// <summary>Spots by their cell and the cells beside them (a seat's own cell may be no place to stand: the one by it is).</summary>
    Dictionary<PInt3, int> _spotBy;

    /// <summary>How far over the walking grid a way on is looked for from where someone lands, cells (well under a millisecond).</summary>
    const int SpotSearch = 1000;

    /// <summary><see cref="SpotFrom"/>: no spot in the cells searched, but more beyond them (open ground: there are ways, only far).</summary>
    const int SpotSearchSpent = -2;

    /// <summary>
    /// The nearest spot by steps over the walking grid from a cell (a spot's cell or one beside it reached), and the cells of the way
    /// there; -1 if every cell that can be walked to from it has been searched and none is a spot (a pit, a hole with no way out),
    /// <see cref="SpotSearchSpent"/> if the search ran out first.
    /// </summary>
    int SpotFrom(PInt3 from, out List<PInt3> way)
    {
        way = null;
        if (_spotBy == null)
        {
            _spotBy = new Dictionary<PInt3, int>();
            for (int i = 0; i < Spots.Count; i++)
                foreach (PInt3 d in new[] { PInt3.Zero, new PInt3(1, 0, 0), new PInt3(-1, 0, 0), new PInt3(0, 0, 1), new PInt3(0, 0, -1) })
                    _spotBy.TryAdd(Spots[i].Cell + d, i);
        }
        var back = new Dictionary<PInt3, PInt3> { [from] = from };
        var next = new Queue<PInt3>();
        var near = new List<PInt3>(8);
        next.Enqueue(from);
        while (next.Count > 0)
        {
            if (back.Count >= SpotSearch) return SpotSearchSpent;
            PInt3 c = next.Dequeue();
            if (_spotBy.TryGetValue(c, out int spot))
            {
                way = new List<PInt3>();
                for (PInt3 at = c; ; at = back[at]) { way.Add(at); if (at == from) break; }
                way.Reverse();
                return spot;
            }
            near.Clear();
            _nav.NeighboursInto(c, near);
            foreach (PInt3 n in near)
                if (back.TryAdd(n, c)) next.Enqueue(n);
        }
        return -1;
    }

    /// <summary>
    /// On from where they stand (landed, or over the rim they climbed to): along the way to the nearest spot there is one to, and on
    /// with their day from there; with none, up the side to a rim that has one. Returns what <see cref="SpotFrom"/> found.
    /// </summary>
    int OnFrom(Person p)
    {
        if (StandIn(Cell(p.Pos)) is not { } c) { GetOut(p); return -1; }
        int spot = SpotFrom(c, out List<PInt3> way);
        if (spot == -1) GetOut(p);
        if (spot < 0) return spot;
        List<Vector3> path = Points(way);
        if (way[^1] == Spots[spot].Cell) path.Add(At(spot));
        (p.Path, p.Step, p.Spot, p.Asked, p.Wait) = (path, 0, spot, -1, 1f);
        return spot;
    }

    /// <summary>
    /// On from where someone stands after a fall (their routes, worked out from the spots, knowing nothing of it): by the way
    /// <see cref="OnFrom"/> finds, up a side by <see cref="GetOut"/>, or off the place's grid home over the land. False with none of
    /// those (open ground past the search, or stranded): their day goes on as it would.
    /// </summary>
    bool OnAfterFall(Person p)
    {
        if (Memory.Inst.Blocks.InBounds(Cell(p.Pos))) return OnFrom(p) >= 0 || p.ClimbTo != null;
        HomeOverLand(p, "fell off the place");
        return p.Run != null;
    }

    /// <summary>Whether there is no walking anywhere from where someone stands, nor climbing out to where there is (tests).</summary>
    public bool Stranded(Person p) => p.ClimbTo == null && (StandIn(Cell(p.Pos)) is not { } c || SpotFrom(c, out _) == -1) && WayOut(p) == null;

    /// <summary>Their way on from where they stand, told (tests).</summary>
    public string WayOn(Person p) => StandIn(Cell(p.Pos)) is not { } c ? "nowhere to stand" : SpotFrom(c, out var way) switch
    {
        -1 => $"no spot to walk to from {c}, {(WayOut(p) is { } up ? $"a way out by {up}" : "no way out")}",
        SpotSearchSpent => $"open ground round {c}, no spot within {SpotSearch} cells",
        int s => $"spot {s} {way.Count} cells away from {c}",
    };

    /// <summary>
    /// Landed where the walking grid has no way out (a pit, a hole in a floor): the rim beside them, up to five cells up (back up
    /// the hole they fell through), that a way goes on from, with clear air over them to climb by; null if there is none. On landing.
    /// </summary>
    Vector3? WayUp(Person p)
    {
        PInt3 c = Cell(p.Pos);
        int tries = 0;
        for (int reach = 1; reach <= 3; reach++)
            for (int dy = 1; dy <= 5; dy++)
            {
                // (Clear over them, up to a head over the rim.)
                bool clear = true;
                for (int k = 1; k <= dy + 1 && clear; k++) clear = !_nav.Solid(c + new PInt3(0, k, 0));
                if (!clear) continue;
                for (int dz = -reach; dz <= reach; dz++)
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != reach) continue;
                        var q = new PInt3(c.X + dx, c.Y + dy, c.Z + dz);
                        if (!_nav.Standable(q)) continue;
                        if (tries++ < 12 && SpotFrom(q, out _) != -1) return Walk(q);
                    }
            }
        return null;
    }

    /// <summary>A way out of where they landed with no walking on: up the side of a hole, else down off the edge of a ledge or a roof.</summary>
    Vector3? WayOut(Person p) => WayUp(p) ?? WayDown(p);

    /// <summary>Out by <see cref="WayOut"/>: holding on and climbing for a way up (not looked under meanwhile), walking for a way down.</summary>
    void GetOut(Person p)
    {
        Vector3? up = WayUp(p);
        Vector3? to = up ?? WayDown(p);
        (p.ClimbTo, p.Clambering, p.Path, p.Asked) = (to, to is { } t && t.Y > p.Pos.Y + 0.5f, null, -1); // (up a side or over a parapet, held on)
    }

    /// <summary>The most a way down steps off onto the land past the place's grid: a fall at about the speed a body takes unhurt, m.</summary>
    const float StepOff = 12f;

    /// <summary>
    /// On a ledge or a roof with no way down by the walking grid: the nearest edge (within 16 m) with open air beside it down to a floor a few
    /// cells under that a way goes on from (or past the place's grid, the land no more than <see cref="StepOff"/> under), over which they
    /// step off (and fall: <see cref="Falling"/>); null if there is none.
    /// </summary>
    Vector3? WayDown(Person p)
    {
        PInt3 c = Cell(p.Pos);
        int tries = 0;
        for (int reach = 1; reach <= 16; reach++)
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != reach) continue;
                    // (Open beside them at their height and down the column to a floor; a parapet on the way, they climb over it.)
                    if (_nav.Solid(new PInt3(c.X + dx, c.Y, c.Z + dz)) || _nav.Solid(new PInt3(c.X + dx, c.Y + 1, c.Z + dz))) continue;
                    bool over = false, blocked = false;
                    for (int k = 1; k < reach; k++)
                    {
                        var mid = new PInt3(c.X + (int)MathF.Round(dx * k / (float)reach), c.Y, c.Z + (int)MathF.Round(dz * k / (float)reach));
                        if (!_nav.Solid(mid)) continue;
                        over = true;
                        blocked |= _nav.Solid(mid + PInt3.Up) || _nav.Solid(mid + PInt3.Up * 2);
                    }
                    if (blocked) continue;
                    // (Past the place's grid, the land as it stands: stepped off onto it if the drop is one a body takes unhurt.)
                    if (!Memory.Inst.Blocks.InBounds(new PInt3(c.X + dx, Memory.Inst.Blocks.Min.Y, c.Z + dz)))
                    {
                        var off = new Vector3(c.X + dx + 0.5f, p.Pos.Y + (over ? 1.05f : 0f), c.Z + dz + 0.5f);
                        if (p.Pos.Y - LandAt(off) <= StepOff) return off;
                        continue;
                    }
                    for (int dy = 1; dy <= 14; dy++)
                    {
                        var q = new PInt3(c.X + dx, c.Y - dy, c.Z + dz);
                        if (_nav.Solid(q)) break;
                        if (!_nav.Standable(q)) continue;
                        if (tries++ < 12 && SpotFrom(q, out _) != -1) return new Vector3(q.X + 0.5f, p.Pos.Y + (over ? 1.05f : 0f), q.Z + 0.5f);
                        break;
                    }
                }
        return null;
    }

    /// <summary>
    /// Out of where they landed: up a hole's side (holding on, straight up to the rim's height, then over onto it), or over the
    /// edge of a ledge (walking to it, looked under each step: off it they fall). True while it has them.
    /// </summary>
    bool Climb(Person p, float dt)
    {
        Vector3 to = p.ClimbTo.Value;
        p.Now = 0f;
        if (p.Clambering && p.Pos.Y < to.Y)
        {
            p.Pos.Y = MathF.Min(to.Y, p.Pos.Y + Clamber * dt);
            return true;
        }
        if (!p.Clambering) p.CheckFor = MathF.Max(p.CheckFor, Pressing);
        Vector3 d = new(to.X - p.Pos.X, 0f, to.Z - p.Pos.Z);
        float step = Clamber * dt;
        if (d.Length() > step)
        {
            p.Pos += d.Normalized() * step;
            p.Yaw = MathF.Atan2(-d.X, -d.Z);
            return true;
        }
        (p.Pos, p.ClimbTo, p.Clambering, p.CheckFor) = (to, null, false, 1f); // (over a ledge's edge or a parapet: nothing under them, and they fall)
        if (OnFrom(p) == SpotSearchSpent) Next(p);
        return true;
    }

    /// <summary>Whether a spot still stands by the place's blocks (its floor not broken away from under it): a refuge or a seat for the night only then.</summary>
    bool Whole(int spot)
    {
        PInt3 under = Spots[spot].Cell + PInt3.Down;
        BlockGrid g = Memory.Inst.Blocks;
        return _nav == null || !g.InBounds(under) || !g.Get(under).IsAir || _nav.IsRock(under) || _nav.RampAt(Spots[spot].Cell, out _);
    }

    /// <summary>Whether a point (the place's frame) has a floor under it by the place's blocks (off its grid: not for them to say).</summary>
    bool Floored(Vector3 local)
    {
        PInt3 c = Cell(local);
        return !Memory.Inst.Blocks.InBounds(c) || _nav.StandsOn(c) || _nav.StandsOn(c + PInt3.Down);
    }
}
