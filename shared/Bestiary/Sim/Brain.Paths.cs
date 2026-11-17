using System;
using Godot;

namespace Wildlife;

/// <summary>
/// The one way anything goes anywhere (owner, 17 Nov: "They have a tendency to flee into walls, rather than pathing
/// their way out"). Every state picks a goal and <see cref="Go"/> takes it there on the grid its body moves on: out on
/// the ground the nav grid's 3 m cells round rocks and trunks, and among buildings a window of 1 m cells round their
/// walls and doorways (<see cref="NavGrid.Window"/>); in a cave or a place its own cells, walls, stairs and furniture
/// footprints (<see cref="SpaceNav"/>). Nothing goes straight at a point unless the line to it is clear on that grid.
/// A flight (<see cref="Away"/>) is a spot it can reach that is farther from the threat by the way round, picked on the
/// same grid: out of the room by its door, never into the wall behind it; backing off and giving way are short ones.
/// And a body that makes no headway on its way for a second looks for another way; still stuck a second later, it is
/// put back where it last stood with its way going well (<see cref="Headway"/>). The searches share the sim's budget a step.
/// </summary>
public static partial class Brain
{
    /// <summary>What a flight costs of the step's path budget (two floods), and how far one runs: out on the ground, and in a cave or a place, m.</summary>
    const int AwayCost = 2;
    const float FleeReach = 30f, FleeReachInside = 14f, BackReach = 6f;

    /// <summary>Heads for a point on its own grid: straight where the line is clear, else along a way found round what is in it.</summary>
    public static void Go(WildSim w, Creature c, Vector3 goal, float speed)
    {
        if (c.Space != null)
        {
            GoInside(w, c, goal, speed);
            return;
        }
        Vector3 dir = Way(w, c, goal);
        float d = WildSim.Flat(goal - c.Position).Length();
        // Eases in from as far out as it takes this body to stop.
        float ease = MathF.Max(1.5f, Heft.StoppingDistance(c, WildSim.Flat(c.Velocity).Length()) * 1.3f);
        c.Desired = dir * speed * Math.Clamp(d / ease, 0.25f, 1f);
    }

    /// <summary>
    /// Out on the ground: straight when the line is clear of rocks, trunks and anything built; otherwise along a way,
    /// found again when the goal has moved, the way is stale, the line to its next point is built across, or it made no headway.
    /// </summary>
    static Vector3 Way(WildSim w, Creature c, Vector3 goal)
    {
        Vector3 to = WildSim.Flat(goal - c.Position);
        if (c.Species.Flies && !c.Landed || !c.Repath && Clear(w, c, c.Position, goal))
        {
            c.Path.Clear();
            return to.LengthSquared() > 1e-4f ? to.Normalized() : Vector3.Zero;
        }
        bool walled = c.PathIndex < c.Path.Count && Built(w, c, c.Position, c.Path[c.PathIndex]);
        bool stale = c.Repath || walled || c.Path.Count == 0 || c.PathIndex >= c.Path.Count || c.PathGoal.DistanceTo(goal) > 4f || c.PathAge > 4f;
        if (stale && w.TakePathBudget())
        {
            // Among buildings, by the fine window round them; else by the ground's own grid.
            Ready(c, goal);
            if (walled || Built(w, c, c.Position, goal))
            {
                NavGrid fine = Fine(w, c, c.Position, goal);
                fine.FindPath(c.Position, fine.Inside(goal, 2), c.Path, 3000);
                c.PathReach = 0.5f;
            }
            else w.Nav.FindPath(c.Position, goal, c.Path);
        }
        Passed(c, c.Position);
        Vector3 next = c.PathIndex < c.Path.Count ? c.Path[c.PathIndex] : goal;
        Vector3 dir = WildSim.Flat(next - c.Position);
        return dir.LengthSquared() > 1e-4f ? dir.Normalized() : Vector3.Zero;
    }

    /// <summary>A new way is being laid: from its start, for this goal.</summary>
    static void Ready(Creature c, Vector3 goal)
    {
        c.Repath = false;
        c.PathIndex = 0;
        c.PathGoal = goal;
        c.PathAge = 0f;
        c.PathReach = c.Space != null ? 0.9f : 1.6f;
    }

    /// <summary>Through a cave or a place along its surfaces: straight only when near, in the open and (a walker among furniture) its body gets by all the way; else on a path of its cells.</summary>
    static void GoInside(WildSim w, Creature c, Vector3 goal, float speed)
    {
        Vector3 center = Crawl.Center(c);
        float d = (goal - center).Length();
        if (!c.Repath && d < 2.5f && Clear(w, c, c.Position, goal))
        {
            c.Path.Clear();
            Vector3 straight = goal - center;
            float ease = MathF.Max(1.2f, Heft.StoppingDistance(c, c.CrawlSpeed) * 1.3f);
            c.Desired = straight.LengthSquared() > 1e-4f ? straight.Normalized() * speed * Math.Clamp(d / ease, 0.3f, 1f) : Vector3.Zero;
            return;
        }
        c.PathAge += c.ThinkDt;
        bool stale = c.Repath || c.Path.Count == 0 || c.PathIndex >= c.Path.Count || c.PathGoal.DistanceTo(goal) > 1.5f || c.PathAge > 4f;
        if (stale && w.TakePathBudget())
        {
            Ready(c, goal);
            SpaceNav.FindPath(c.Space, center, goal, SpaceNav.BodyOf(c.Species), c.Path, 2500);
        }
        WaypointsPassed(c, center);
        Vector3 next = c.PathIndex < c.Path.Count ? c.Path[c.PathIndex] : goal;
        Vector3 dir = next - center;
        c.Desired = dir.LengthSquared() > 1e-4f ? dir.Normalized() * speed : Vector3.Zero;
    }

    /// <summary>
    /// Between thinks: keeps to the way the brain chose, round its corners, at the speed it chose.
    /// Nothing is looked for here, so it costs next to nothing.
    /// </summary>
    public static void Follow(Creature c)
    {
        float speed = c.Desired.Length();
        if (speed < 0.01f || c.PathIndex >= c.Path.Count) return;
        if (c.Space != null)
        {
            Vector3 center = Crawl.Center(c);
            WaypointsPassed(c, center);
            Vector3 dir = c.Path[c.PathIndex] - center;
            if (dir.LengthSquared() > 1e-4f) c.Desired = dir.Normalized() * speed;
            return;
        }
        Passed(c, c.Position);
        Vector3 to = WildSim.Flat(c.Path[c.PathIndex] - c.Position);
        if (to.LengthSquared() > 1e-4f) c.Desired = to.Normalized() * speed;
    }

    /// <summary>On the ground: a waypoint is passed once it is near (nearer on a fine way among walls, whose corners are doorways).</summary>
    static void Passed(Creature c, Vector3 at)
    {
        while (c.PathIndex < c.Path.Count - 1 && WildSim.Flat(c.Path[c.PathIndex] - at).Length() < c.PathReach) c.PathIndex++;
    }

    /// <summary>
    /// A waypoint is passed once it is close, or (for a climber) once it lies straight out from
    /// the surface it holds: in a corner the next cell can sit along the normal, with nothing
    /// left to walk towards on this face; or (for a walker) once it is over or under it, as on a flight.
    /// </summary>
    static void WaypointsPassed(Creature c, Vector3 center)
    {
        Vector3 up = c.Facing.Y;
        PartSet parts = c.Species.Climbs ? null : c.Space.Parts;
        while (c.PathIndex < c.Path.Count - 1)
        {
            Vector3 to = c.Path[c.PathIndex] - center;
            float along = (to - up * to.Dot(up)).Length();
            // Among furniture a walker keeps to its way's points (the gaps it fits through), cutting no corner round a leg.
            bool close = parts != null && parts.Any(c.Path[c.PathIndex]) ? along < 0.3f && MathF.Abs(to.Dot(up)) < 1.6f : to.Length() < 0.9f;
            // A walker on a flight passes a cell's middle under or over it: its body stands a metre or so off it.
            if (close || (c.Species.Climbs && along < 0.4f && to.Length() < 1.8f)
                || (!c.Species.Climbs && along < 0.45f && MathF.Abs(to.Dot(up)) < 1.6f)) c.PathIndex++;
            else break;
        }
    }

    /// <summary>Whether a body gets straight from one point to another on its own grid: rocks, trunks and what is built on the ground; walls, floors and furniture in a place.</summary>
    public static bool Clear(WildSim w, Creature c, Vector3 a, Vector3 b)
    {
        if (c.Space == null) return w.Nav.LineClear(a, b) && !Built(w, c, a, b);
        Vector3 lift = c.Facing.Y * c.Clearance;
        return SpaceMath.Clear(c.Space, a + lift, b + lift, 0f) && (c.Species.Climbs || c.Space.Parts is not { } parts || parts.Sweep(c.Position, b with { Y = c.Position.Y }, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species)));
    }

    /// <summary>Whether anything built stands across a straight line on the ground (a place's walls, a house, the bunker): by the fine window's 1 m cells.</summary>
    static bool Built(WildSim w, Creature c, Vector3 a, Vector3 b) => w.SolidAt != null && !Fine(w, c, a, b).LineClear(a, b, built: true);

    /// <summary>The fine window for a body going from one point to another: round both where it holds them, else round the first of the way.</summary>
    static NavGrid Fine(WildSim w, Creature c, Vector3 a, Vector3 b)
    {
        Vector3 to = WildSim.Flat(b - a);
        float len = to.Length(), most = NavGrid.WindowSize * 0.4f;
        Vector3 centre = len < most * 2f ? (a + b) * 0.5f : a + to / len * most;
        return w.Nav.Window(centre, w.BuiltGround, Locomotion.Footprint(c.Species));
    }

    // ---- Flights ----------------------------------------------------------------------------

    /// <summary>Runs from a point: to a spot it can reach, farther from it by the way round (<see cref="Away"/>).</summary>
    static void Flee(WildSim w, Creature c, Vector3 from)
    {
        c.SetState(CState.Flee);
        Away(w, c, from, c.Space != null ? FleeReachInside : FleeReach, c.Stats.RunSpeed);
    }

    /// <summary>Backs off from a point with its eyes on it, or steps out of its way: a short flight, facing it.</summary>
    static void Back(WildSim w, Creature c, Vector3 from, float speed)
    {
        c.Backing = c.Space == null;
        c.FacePoint ??= from;
        Away(w, c, from, BackReach, speed);
    }

    /// <summary>
    /// Away from a point, to a spot on its own grid it can reach that is farther from it by the way round: picked again
    /// once it gets there, when the threat has moved on a few metres, every two seconds, or when it made no headway;
    /// held within a pen if given. Cornered (nowhere farther), it stands its ground facing the threat.
    /// </summary>
    static void Away(WildSim w, Creature c, Vector3 from, float reach, float speed, Vector3 keep = default, float keepRadius = float.MaxValue)
    {
        bool there = c.HasAway && (c.AwayGoal - c.Position).Length() < 1.5f;
        if ((!c.HasAway || there || c.Repath || (c.AwayFrom - from).Length() > 3f || w.Time - c.AwayAt > 2f) && w.TakePathBudget(AwayCost))
        {
            c.AwayAt = w.Time;
            c.AwayFrom = from;
            Ready(c, c.Position);
            if (c.Space != null) c.HasAway = SpaceNav.Away(c.Space, Crawl.Center(c), from + Vector3.Up * 0.5f, SpaceNav.BodyOf(c.Species), reach, c.Path);
            else
            {
                // Among buildings, on the fine window round it (doorways are a metre wide); else on the ground's own cells.
                Vector3 off = WildSim.Flat(c.Position - from);
                Vector3 ahead = c.Position + (off.LengthSquared() > 0.01f ? off.Normalized() : c.Forward) * reach;
                bool walled = Built(w, c, c.Position, ahead) || Built(w, c, c.Position, from);
                if (walled) c.PathReach = 0.5f;
                NavGrid grid = walled ? Fine(w, c, c.Position, c.Position) : w.Nav;
                c.HasAway = grid.Away(c.Position, from, MathF.Min(reach, walled ? 20f : reach), c.Path, walled ? 900 : 500, keep, keepRadius);
            }
            if (c.HasAway) c.PathGoal = c.AwayGoal = c.Path[^1];
        }
        if (!c.HasAway)
        {
            c.Desired = Vector3.Zero;
            c.FacePoint ??= from;
            return;
        }
        Go(w, c, c.AwayGoal, speed);
    }

    // ---- Headway ----------------------------------------------------------------------------

    /// <summary>
    /// Once a step, after it has moved: a body wanting to go that hasn't got a quarter metre on (less for a slow one) in a
    /// second looks for another way at once; still stuck a second later (two, for a hopper between its hops), it is put
    /// back where it last stood with its way going well, a metre or more back, and finds its way again from there. Nothing
    /// stands pushing into a wall.
    /// </summary>
    public static void Headway(WildSim w, Creature c, float dt)
    {
        float want = c.Desired.Length();
        bool going = want > 0.3f && !c.Attacking && c.HeldBy == null && c.Holding == null && !c.InWalls && !c.Falling && !c.Thrown && c.Downed <= 0f && c.StunFor <= 0f
            && !(c.Space == null && c.Species.Flies && !c.Landed) && c.Species.Rig is not (RigKind.Swarm or RigKind.GasBag);
        float need = Math.Clamp(want * 0.4f, 0.08f, 0.25f);
        if (!going || (c.Position - c.ProgressAt).LengthSquared() > need * need)
        {
            c.ProgressAt = c.Position;
            c.StuckFor = 0f;
            c.Repathed = false;
            // A metre on in the open: what it would be put back on.
            if (going && (c.Position - c.GoodAt).LengthSquared() > 1f && !Interpenetration.Inside(w, c))
            {
                (c.Behind, c.BehindUp) = (c.GoodAt, c.GoodUp);
                (c.GoodAt, c.GoodUp) = (c.Position, c.Space != null ? c.Facing.Y : Vector3.Up);
            }
            return;
        }
        c.StuckFor += dt;
        float slow = c.Species.Rig == RigKind.Hopper ? 2f : 1f;
        if (c.StuckFor >= slow && !c.Repathed)
        {
            c.Repathed = true;
            c.Repath = true;
            c.ThinkEvery = 0f;
            w.Repaths++;
        }
        else if (c.StuckFor >= 2f * slow) Unstick(w, c);
    }

    /// <summary>Still stuck after looking for another way: back where it last stood with its way going well (if that is near: a body carried to another sim's ground finds its way afresh), its way to be found again.</summary>
    static void Unstick(WildSim w, Creature c)
    {
        // Held by someone it is up against (you, one of a town's people): nothing it is stuck on; it tries again in a while.
        foreach (ITarget t in w.Targets)
            if (t.Attackable && (t.Position - c.Position).Length() < c.Radius + 0.8f)
            {
                c.StuckFor = 0f;
                c.Repathed = false;
                return;
            }
        Vector3 to = c.Behind;
        if ((to - c.Position).LengthSquared() is > 0.04f and < 25f)
        {
            if (c.Space != null) Crawl.Place(w, c, to + c.BehindUp * 0.05f, c.Species.Climbs ? c.BehindUp : null);
            else
            {
                c.Position = new Vector3(to.X, w.Terrain.Height(to.X, to.Z), to.Z);
                c.Velocity = Vector3.Zero;
            }
            c.Rig?.Reset(w, c);
            w.Unstuck++;
        }
        c.GoodAt = c.Behind = c.ProgressAt = c.Position;
        c.Path.Clear();
        c.Repath = true;
        c.HasGoal = false;
        c.HasAway = false;
        c.StuckFor = 0f;
        c.Repathed = false;
    }
}
