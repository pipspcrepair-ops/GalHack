using System;
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
        /// <summary>Falling: how fast (m/s, down), from what height and where they last stood (the place's frame).</summary>
        public bool Airborne;
        public float FallSpeed, FellFrom;
        public Vector3 FirmAt;
        /// <summary>How long more they are looked under each step (something under them went; its colliders may follow a moment later).</summary>
        public float CheckFor;
        /// <summary>Out on the land, clambering up the side of a hole (held to it, not falling).</summary>
        public bool Clambering;
        /// <summary>Falls landed, the last one's drop (m), speed (m/s) and share of their health it cost (tests).</summary>
        public int Falls;
        public float LastDrop, LastFallSpeed, LastFallShare, LastFallTime;
    }

    /// <summary>The world's pull, m/s².</summary>
    float Pull => _places?.Zone.Physics.Zone.Strength ?? 9.8f;

    /// <summary>How far down anything is looked for under a falling body, m.</summary>
    const float FallReach = 80f;

    /// <summary>How far over what is under their feet someone may be and still stand on it, m.</summary>
    const float FootSlack = 0.3f;

    /// <summary>Looked under in the slow sweep only this near you (out beyond it there may be nothing built to collide with), m.</summary>
    const float SweepRange = 60f;

    /// <summary>Falls broken off for want of anything under them, put back where they stood (tests: none).</summary>
    public int FallRescues { get; private set; }

    /// <summary>Support checks made, rays cast for them (measurements).</summary>
    public int FallChecks { get; private set; }
    public int FallRays { get; private set; }

    int _sweep;

    /// <summary>
    /// Something under a stretch went (world frame: blocks broken, a piece taken, the ground dug): whoever stands, sits or lies over
    /// it is looked under for the next second, and a body lying there falls again, limp.
    /// </summary>
    public void Unfloored(Vector3 lo, Vector3 hi)
    {
        lo = Local(lo);
        hi = Local(hi);
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

    /// <summary>The top of what is solid under a point (the place's frame), from <paramref name="above"/> over it to <paramref name="below"/> under it; null with nothing (or nothing built to tell).</summary>
    float? Top(Vector3 local, float above, float below)
    {
        if (_view == null || !_view.IsInsideTree()) return null;
        Vector3 from = World(local + Vector3.Up * above);
        var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * (above + below), Layers.World | Layers.Hull | Layers.Interior);
        var hit = _view.GetWorld3D().DirectSpaceState.IntersectRay(q);
        return hit.Count == 0 ? null : Local((Vector3)hit["position"]).Y;
    }

    /// <summary>Their feet's reach round their middle: the four points of their footing besides the middle, m.</summary>
    static Vector3 Foot(Person p, int i)
    {
        float r = BodyRadius(p) * 0.8f;
        return i switch { 1 => new(r, 0f, 0f), 2 => new(-r, 0f, 0f), 3 => new(0f, 0f, r), 4 => new(0f, 0f, -r), _ => Vector3.Zero };
    }

    /// <summary>
    /// What holds them up, by their real footing (their middle and four points round it): the top of it, or null if nothing under
    /// enough of it does (nothing under the middle and less than three of the rest). Held only by an edge, <paramref name="off"/>
    /// is the step off it, away from what is left.
    /// </summary>
    float? Footed(Person p, out Vector3 off)
    {
        FallChecks++;
        FallRays += 5;
        off = Vector3.Zero;
        float best = float.NegativeInfinity;
        int held = 0;
        bool middle = false;
        Vector3 lean = Vector3.Zero;
        for (int i = 0; i < 5; i++)
        {
            if (Top(p.Pos + Foot(p, i), 0.5f, FootSlack) is not float top) continue;
            held++;
            best = MathF.Max(best, top);
            if (i == 0) middle = true;
            else lean += Foot(p, i);
        }
        if (middle || held >= 3) return best;
        if (held > 0 && lean.LengthSquared() > 1e-6f) off = -lean.Normalized() * BodyRadius(p);
        return null;
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
        if (_view == null || !_view.IsInsideTree())
        {
            p.CheckFor = 0f; // (nothing built to stand on or to tell by: a day without the game)
            return false;
        }
        if (!p.Airborne)
        {
            if (p.CheckFor <= 0f || p.Clambering) return false;
            p.CheckFor -= dt;
            if (Footed(p, out Vector3 off) is float top)
            {
                // On what is left: their feet on its top (a stair, rubble), never in it or over it.
                if (MathF.Abs(top - p.Pos.Y) < FootSlack) p.Pos.Y = top;
                // Sitting on a seat that has gone: down on the floor (their pose eases down), up off it frightened.
                if (p.Sitting && p.Seated > 0.5f && SeatUnder(p) == null) { Unseat(p); p.ScaredFor = MathF.Max(p.ScaredFor, 4f); }
                return false;
            }
            else LoseFooting(p, off);
        }
        Down(p, dt);
        return true;
    }

    /// <summary>Off their feet: up out of a seat or a bed, their way given up, falling from where they are (stepped off the edge that held them).</summary>
    void LoseFooting(Person p, Vector3 off)
    {
        Unseat(p);
        if (off != Vector3.Zero && Open(p.Pos + off)) p.Pos += off;
        (p.Airborne, p.FallSpeed, p.FellFrom, p.FirmAt, p.CheckFor, p.LastFallTime) = (true, 0f, p.Pos.Y, p.Pos, 0f, 0f);
        (p.Path, p.Step, p.Asked, p.Partner, p.Now, p.Yield) = (null, 0, -1, null, 0f, 0f);
    }

    void Unseat(Person p) => (p.Sitting, p.Seat, p.GettingUp, p.Asleep, p.Working) = (false, -1, false, false, false); // (sat or lain, they come out of it as their pose eases)

    /// <summary>A step of a fall: down by the world's pull to the highest thing under their footing (or into water), landing there.</summary>
    void Down(Person p, float dt)
    {
        p.LastFallTime += dt;
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
            if (i == 0) ground = top;
            else (rest, under) = (MathF.Max(rest, top), under + 1);
        }
        if (float.IsNegativeInfinity(ground) && under >= 3) ground = rest;
        // Water takes them: they go in, and stand in it or tread it (its floor under them, or chest-deep), unhurt by it.
        bool wet = false;
        if (_places?.Zone.Planet?.WaterSurfaceAt(World(p.Pos)) is float surface)
        {
            float tread = surface - _origin.Y - Height(p) * 0.75f;
            if (tread > ground) (ground, wet) = (tread, true);
        }
        float y = p.Pos.Y, speed = p.FallSpeed;
        bool down = Drop.Step(ref y, ref speed, ground, Pull, dt);
        p.Pos.Y = y;
        OffSides(p);
        p.FallSpeed = speed;
        if (down) Land(p, speed, wet);
        else if (p.LastFallTime > 20f)
        {
            // Fallen out of everything (nothing built under them to land on): back where they last stood.
            FallRescues++;
            (p.Pos, p.Airborne, p.FallSpeed) = (p.FirmAt, false, 0f);
        }
    }

    readonly CapsuleShape3D _body = new();
    PhysicsShapeQueryParameters3D _bodyQuery;

    /// <summary>Falling past something beside them (a wall's top, a beam, the edge of the hole): held off it sideways, never through it.</summary>
    void OffSides(Person p)
    {
        float r = BodyRadius(p), h = MathF.Max(2f * r, Height(p) - 0.25f);
        (_body.Radius, _body.Height) = (r, h);
        _bodyQuery ??= new PhysicsShapeQueryParameters3D { Shape = _body, CollisionMask = Layers.World | Layers.Hull | Layers.Interior };
        var space = _view.GetWorld3D().DirectSpaceState;
        for (int k = 0; k < 6; k++)
        {
            _bodyQuery.Transform = new Transform3D(Basis.Identity, World(p.Pos + Vector3.Up * (0.25f + h * 0.5f)));
            var rest = space.GetRestInfo(_bodyQuery);
            if (rest.Count == 0) return;
            Vector3 n = (Vector3)rest["normal"] with { Y = 0f };
            if (n.LengthSquared() < 1e-4f) return;
            p.Pos += n.Normalized() * 0.05f;
        }
    }

    /// <summary>Down: hurt by the sleeper's fall rule (water takes the blow), frightened, and on from where they landed, their way looked for again.</summary>
    void Land(Person p, float speed, bool wet)
    {
        p.Airborne = false;
        p.Falls++;
        (p.LastDrop, p.LastFallSpeed) = (p.FellFrom - p.Pos.Y, speed);
        p.LastFallShare = wet ? 0f : Slice.Player.Survivor.FallShare(_places.Zone.Physics.Rules, speed);
        if (p.LastFallShare > 0f) Struck(p, p.LastFallShare * p.Health.Max, GalHack.Arms.DamageClass.Kinetic, byPlayer: false);
        if (p.Dead) return;
        // Startled by it and shaken (their mood), and on at once from where they landed, by a way from there; out on a run, home.
        (p.StartleFor, p.Wait, p.Refuge, p.ProgressAt, p.StuckFor) = (0.9f, 0f, -1, p.Pos, 0f);
        // (Where they were bound, by a way from here; with no floor left there, somewhere else.)
        if (p.Run != null) p.ScaredFor = MathF.Max(p.ScaredFor, 4f);
        else if (p.Spot >= 0 && Whole(p.Spot)) GoTo(p, p.Spot);
        else Next(p);
        // (No way there from here: the nearest spot there is a way to, if any; stranded, they stay.)
        if (p.Run == null && p.Asked < 0 && p.Path is not { Count: > 0 } && NearestReachable(p) is int near and >= 0) GoTo(p, near);
        p.Feel.Hit(MoodEvent.Harm);
        p.CheckFor = 0.3f; // (landed on an edge: off it, or stood on what is there)
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

    /// <summary>The nearest of the town's spots that can be walked to from where someone stands (-1: none, they are stranded there). A search over the walking grid: only on landing.</summary>
    int NearestReachable(Person p)
    {
        if (Stand(Cell(p.Pos)) is not { } c) return -1;
        var reach = _nav.Reachable(c);
        int best = -1;
        for (int i = 0; i < Spots.Count; i++)
            if (reach.Contains(Spots[i].Cell) && (best < 0 || (At(i) - p.Pos).LengthSquared() < (At(best) - p.Pos).LengthSquared())) best = i;
        return best;
    }

    /// <summary>Whether there is no walking anywhere from where someone stands (tests).</summary>
    public bool Stranded(Person p) => NearestReachable(p) < 0;

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
