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
        if (c.Species.Flies && !c.Landed || !c.Repath && w.Time >= c.PathOnly && Clear(w, c, c.Position, goal))
        {
            c.Path.Clear();
            return to.LengthSquared() > 1e-4f ? to.Normalized() : Vector3.Zero;
        }
        bool walled = c.PathIndex < c.Path.Count && Tight(w, c, c.Position, c.Path[c.PathIndex]);
        bool stale = c.Repath || walled || c.Path.Count == 0 || c.PathGoal.DistanceTo(goal) > 4f || c.PathAge > 4f;
        if (stale && w.TakePathBudget())
        {
            NavGrid.Gait g = GaitOf(c.Species);
            bool built = Built(w, c, c.Position, goal) || walled && Built(w, c, c.Position, c.Path[c.PathIndex]);
            Ready(c, goal);
            if (built)
            {
                // Among buildings, by the fine window round them (a doorway is a metre wide).
                NavGrid fine = Fine(w, c, c.Position, goal);
                c.PathWhole = fine.FindPath(c.Position, fine.Inside(goal, 2), c.Path, 3000, g);
                c.PathReach = 0.5f;
            }
            else
            {
                // By the ground's own grid; its first stretch, where that crosses rough ground, on the fine window round
                // it (a ramp or a pass is narrower than the grid's cells).
                c.PathWhole = w.Nav.FindPath(c.Position, goal, c.Path, gait: g);
                if (c.Path.Count > 0 && Tight(w, c, c.Position, c.Path[0])) Refine(w, c, g);
            }
            if (c.Path.Count == 0) c.HasGoal = false; // nowhere it can get to from here: another goal
        }
        // No way yet (or none): it waits a moment rather than walk into what is in the way.
        if (c.Path.Count == 0) return Vector3.Zero;
        Passed(c, c.Position);
        if (Short(c, c.Position, goal)) return Vector3.Zero;
        Vector3 dir = WildSim.Flat(c.Path[c.PathIndex] - c.Position);
        return dir.LengthSquared() > 1e-4f ? dir.Normalized() : Vector3.Zero;
    }

    /// <summary>A way's first stretch, to its first point, found again on the fine window round it: the cells it goes by put in front of the rest.</summary>
    static void Refine(WildSim w, Creature c, NavGrid.Gait g)
    {
        Vector3 to = c.Path[0];
        NavGrid fine = Fine(w, c, c.Position, to);
        bool through = fine.FindPath(c.Position, fine.Inside(to, 2), w.Way, 3000, g);
        if (w.Way.Count == 0) return;
        // (No way through at the finer cells, a ramp too steep for these legs after all: as far as it gets, and it waits there at the edge.)
        if (through) c.Path.RemoveAt(0);
        else { c.Path.Clear(); c.PathWhole = false; }
        c.Path.InsertRange(0, w.Way);
        c.PathReach = 0.5f;
    }

    /// <summary>A new way is being laid: from its start, for this goal.</summary>
    static void Ready(Creature c, Vector3 goal)
    {
        c.Repath = false;
        c.PathIndex = 0;
        c.PathGoal = goal;
        c.PathAge = 0f;
        c.PathReach = c.Space != null ? 0.9f : 1.6f;
        c.PathWhole = true;
    }

    /// <summary>
    /// At the end of its way short of the goal: a way that couldn't get there gives the goal up (it stands; a wander or a
    /// forage picks another; a hunter waits at the nearest it can get), one that only ran out (at a fine window's edge) is
    /// found again on from here. Nothing stands pushing at a wall toward somewhere it can't go.
    /// </summary>
    static bool Short(Creature c, Vector3 at, Vector3 goal)
    {
        Vector3 end = c.Path[^1];
        if (c.PathIndex < c.Path.Count - 1 || (end - at).Length() > 0.7f || (end - goal).Length() < 1f) return false;
        if (c.PathWhole)
        {
            c.Repath = true;
            return false;
        }
        c.HasGoal = false;
        return true;
    }

    /// <summary>Through a cave or a place along its surfaces: straight only when near, in the open and (a walker among furniture) its body gets by all the way; else on a path of its cells.</summary>
    static void GoInside(WildSim w, Creature c, Vector3 goal, float speed)
    {
        Vector3 center = Crawl.Center(c);
        float d = (goal - center).Length();
        if (!c.Repath && w.Time >= c.PathOnly && d < 2.5f && Clear(w, c, c.Position, goal))
        {
            c.Path.Clear();
            Vector3 straight = goal - center;
            float ease = MathF.Max(1.2f, Heft.StoppingDistance(c, c.CrawlSpeed) * 1.3f);
            c.Desired = straight.LengthSquared() > 1e-4f ? straight.Normalized() * speed * Math.Clamp(d / ease, 0.3f, 1f) : Vector3.Zero;
            return;
        }
        c.PathAge += c.ThinkDt;
        bool stale = c.Repath || c.Path.Count == 0 || c.PathGoal.DistanceTo(goal) > 1.5f || c.PathAge > 4f;
        if (stale && w.TakePathBudget())
        {
            Ready(c, goal);
            c.PathWhole = SpaceNav.FindPath(c.Space, center, goal, SpaceNav.BodyOf(c.Species), c.Path, 2500);
            if (c.Path.Count == 0) c.HasGoal = false;
        }
        c.Desired = Vector3.Zero;
        if (c.Path.Count == 0) return;
        WaypointsPassed(c, center);
        if (Short(c, center, goal)) return;
        Vector3 dir = c.Path[c.PathIndex] - center;
        if (dir.LengthSquared() > 1e-4f) c.Desired = dir.Normalized() * speed;
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
        if (c.Space == null) return w.Nav.LineClear(a, b, gait: GaitOf(c.Species)) && !Tight(w, c, a, b);
        Vector3 lift = c.Facing.Y * c.Clearance;
        return SpaceMath.Clear(c.Space, a + lift, b + lift, 0f) && (c.Species.Climbs || c.Space.Parts is not { } parts || parts.Sweep(c.Position, b with { Y = c.Position.Y }, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species)));
    }

    /// <summary>
    /// What a body's legs manage on open ground (<see cref="NavGrid.Gait"/>; owner: "wall climbers should be able to go up and
    /// down those, but not a person or regular animal"): a climber, a flier or a leaper anything; a person 40 degrees up or
    /// down and off a two-metre edge; a big heavy animal 35 degrees and off a short one; any other walker 45 degrees and off
    /// an edge half again as high as it stands.
    /// </summary>
    public static NavGrid.Gait GaitOf(Species s) =>
        s.Climbs || s.Flies || s.Rig is RigKind.Hopper or RigKind.Swarm or RigKind.GasBag ? default
        : s.Figure != null ? new(0.84f, 2f)
        : s.Body.Size > 3f ? new(0.7f, Math.Clamp(s.BodyHeight * 0.5f, 1f, 2f))
        : new(1f, Math.Clamp(s.BodyHeight * 1.5f, 1f, 3f));

    /// <summary>
    /// Whether the ground's 3 m cells are too coarse for a straight line: something built across it (a doorway is a metre
    /// wide), or rough ground on it where only the fine window's 1 m cells tell whether these legs get across.
    /// </summary>
    static bool Tight(WildSim w, Creature c, Vector3 a, Vector3 b)
    {
        if (Built(w, c, a, b)) return true;
        NavGrid.Gait g = GaitOf(c.Species);
        return !g.Any && w.Nav.Rough(a, b, g.Slope * 0.8f) && !Fine(w, c, a, b).LineClear(a, b, gait: g);
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
    static void Back(WildSim w, Creature c, Vector3 from, float speed, float reach = BackReach)
    {
        c.Backing = c.Space == null;
        c.FacePoint ??= from;
        Away(w, c, from, reach, speed);
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
                bool walled = Tight(w, c, c.Position, ahead) || Tight(w, c, c.Position, from);
                if (walled) c.PathReach = 0.5f;
                NavGrid grid = walled ? Fine(w, c, c.Position, c.Position) : w.Nav;
                c.HasAway = grid.Away(c.Position, from, MathF.Min(reach, walled ? 20f : reach), c.Path, walled ? 900 : 500, keep, keepRadius, GaitOf(c.Species));
            }
            if (c.HasAway) c.PathGoal = c.AwayGoal = c.Path[^1];
        }
        else if (!c.HasAway)
        {
            // No spot picked yet (this step's searches are spent): two metres straight off it, where that line is clear.
            Vector3 off = WildSim.Flat(c.Position - from);
            Vector3 step = c.Position + (off.LengthSquared() > 0.01f ? off.Normalized() : c.Forward) * 2f;
            if (Clear(w, c, c.Position, step))
            {
                Go(w, c, step, speed);
                return;
            }
        }
        if (!c.HasAway)
        {
            // Cornered: it stands its ground, facing it.
            c.Desired = Vector3.Zero;
            c.FacePoint ??= from;
            return;
        }
        Go(w, c, c.AwayGoal, speed);
    }

    // ---- Chases -----------------------------------------------------------------------------

    /// <summary>How long a chaser remembers someone it gave up on, s (back within reach in that time, it takes the chase up again); how often it looks.</summary>
    const float ChaseMemory = 60f, ReachLook = 2f;

    /// <summary>
    /// The one way every chaser closes on whom it is after: a wild hunter, a pack, a Husk, a band's fighter, a place's creature (owner:
    /// "what happens if the player moves to a position a chasing enemy can't reach? Does the enemy stop their pursuit and then give
    /// up?"). It goes by its own grid and gait (a climber or a leaper anywhere it can); with no whole way there it goes to the nearest
    /// spot it can get to and waits there (<see cref="Short"/>; one with a ranged attack uses it from there, by its brain's reach).
    /// After its give-up time with no whole way (<see cref="Stats.GiveUp"/>) it loses interest and goes back to what it does
    /// (<see cref="GiveUp"/>); it remembers them a minute, and takes the chase up again only once there is a whole way to them.
    /// </summary>
    static void Chase(WildSim w, Creature c, ITarget t, Vector3 goal, float speed)
    {
        if ((WildSim.Ab & 128) != 0) { Go(w, c, goal, speed); return; }
        if (Remembers(w, c, t))
        {
            GiveUp(w, c, t);
            return;
        }
        if (c.Chased != t) (c.Chased, c.Unreached) = (t, 0f);
        Go(w, c, goal, speed);
        if (c.PathWhole || c.Path.Count == 0 && c.Desired.LengthSquared() > 0.01f) c.Unreached = 0f;
        else if ((c.Unreached += c.ThinkDt) > c.Stats.GiveUp) GiveUp(w, c, t);
    }

    /// <summary>
    /// Whether it gave up on someone within its memory who is still out of its reach (looked at every two seconds, by a way on its
    /// own grid): it doesn't notice them, nor go after them for a hurt. Back within reach, it forgets it gave up.
    /// </summary>
    public static bool Remembers(WildSim w, Creature c, ITarget t)
    {
        if ((WildSim.Ab & 128) != 0 || t == null || c.GaveUpOn != t) return false;
        if (w.Time - c.GaveUpAt > ChaseMemory) { c.GaveUpOn = null; return false; }
        if (w.Time < c.ReachCheck || !w.TakePathBudget()) return true;
        c.ReachCheck = w.Time + ReachLook;
        bool reach = c.Space != null ? SpaceNav.FindPath(c.Space, Crawl.Center(c), t.Position, SpaceNav.BodyOf(c.Species), w.Way, 2500)
            : Clear(w, c, c.Position, t.Position) || w.Nav.FindPath(c.Position, t.Position, w.Way, gait: GaitOf(c.Species));
        if (reach) c.GaveUpOn = null;
        return !reach;
    }

    /// <summary>
    /// Loses interest in someone it can't get to: drops all it held of them and goes back to what it does: a place's creature to
    /// its lair or home spot, an animal back toward its own ground (with none away from them, its ground where it stands by
    /// them: off away from them, out of their reach), a band's fighter to its band (a band with no gun to reach them with lets
    /// the fight go).
    /// </summary>
    static void GiveUp(WildSim w, Creature c, ITarget t)
    {
        if (c.GaveUpOn != t) w.GaveUp++;
        (c.GaveUpOn, c.GaveUpAt, c.ReachCheck, c.Unreached, c.Chased) = (t, w.Time, w.Time + ReachLook, 0f, null);
        Forget(w, c, t);
        if (c.Pack is { } p && p.Prey == t) EndHunt(w, p, 0f);
        if (c.Herd is { } h)
        {
            if (h.Hunting == t) h.Hunting = null;
            if (h.Party is { } b && b.Foe == t && !h.Members.Exists(m => m.Alive && m.Species.Figure is { } f && Weapons.Ranged(f.Prop))) Settle(w, h, b, Leader(h) ?? c, PartyMood.Easy);
        }
        c.Path.Clear();
        c.HasAway = false;
        c.Desired = Vector3.Zero;
        if (c.Species.IsDeep) c.SetState(CState.ReturnHome);
        else if (c.Herd == null && c.Pack == null)
        {
            Vector3 away = WildSim.Flat(c.Position - t.Position);
            float off = away.Length();
            Vector3 to = WildSim.Flat(c.Home - t.Position).Length() > off + 8f ? c.Home : c.Position + (off > 0.1f ? away / off : -c.Forward) * GoneOff;
            (c.Goal, c.HasGoal) = (w.FreeSpot(to, c.Rng), true);
        }
    }

    /// <summary>How far off from someone it gave up on a creature with no ground of its own away from them goes, m.</summary>
    const float GoneOff = 18f;

    /// <summary>Drops someone from all it holds of them: its attack or grip, its notice, a grudge, its quarry, prey or warning; out of a chase.</summary>
    static void Forget(WildSim w, Creature c, ITarget t)
    {
        if (c.Holding == t) Seizing.Release(w, c, false);
        else if (c.Attacking && c.AttackTarget == t) Attacks.Cancel(c);
        if (c.AttackTarget == t) c.AttackTarget = null;
        if (c.Noticed == t) { c.Noticed = null; c.Awareness = 0f; }
        if (c.ProvokedBy == t) c.ProvokedBy = null;
        if (c.Quarry == t) c.Quarry = null;
        if (c.TellTarget == t) c.TellTarget = null;
        if (c.Prey != null && c.Prey.AsTarget == t) c.Prey = null;
        if (c.State is CState.Chase or CState.Stalk or CState.Warn or CState.Tell or CState.Guard) { c.SetState(CState.Wander); c.HasGoal = false; }
        c.ThinkEvery = 0f;
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
        bool going = want > 0.3f && c.State is not (CState.Graze or CState.Idle or CState.Rest) && !c.Attacking && c.HeldBy == null && c.Holding == null && !c.InWalls && !c.Falling && !c.Thrown && c.Downed <= 0f && c.StunFor <= 0f
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
            c.PathOnly = w.Time + 3f; // (and keeps to a way a while, not the straight line it was stuck on)
            w.Repaths++;
        }
        else if (c.StuckFor >= 2f * slow) Unstick(w, c);
    }

    /// <summary>
    /// Still stuck after looking for another way: back where it last stood with its way going well (if that is near: a
    /// body carried to another sim's ground finds its way afresh), or, stuck where it was put down, onto the nearest open
    /// spot round it; its way to be found again.
    /// </summary>
    static void Unstick(WildSim w, Creature c)
    {
        // Pressed up against another body in its way (you, a town's person, another creature on its floor): nothing it is stuck on
        // (bodies keep apart); it gives up where it was going and tries again in a while. Of two creatures face to face (a one-wide
        // way), the later one gives way: it backs off out of the other's way a moment (unless the other is already giving way to
        // it), and the other waits for it; one held up by a body standing about backs off from it too and finds another way.
        // (A climber in a place passes anyone on its walls: never held by them.)
        if (WildSim.Spaced(c) && Blocked(w, c, out Creature by))
        {
            (c.StuckFor, c.Repathed, c.HasGoal, c.HasAway) = (0f, false, false, false);
            if (by != null && (by.Id < c.Id || by.Desired.LengthSquared() < 0.09f) && !(by.GiveWayTo == c && w.Time < by.GiveWayUntil)) (c.GiveWayTo, c.GiveWayUntil, c.ThinkEvery) = (by, w.Time + GiveWay, 0f);
            return;
        }
        bool back = (c.Behind - c.Position).LengthSquared() is > 1f and < 25f;
        (Vector3 was, Basis facing) = (c.Position, c.Facing);
        if (back && c.Space != null) Crawl.Place(w, c, c.Behind + c.BehindUp * 0.05f, c.Species.Climbs ? c.BehindUp : null);
        else if (back) c.Position = new Vector3(c.Behind.X, w.Terrain.Height(c.Behind.X, c.Behind.Z), c.Behind.Z);
        // (Never back into a wall: where that set it down in one, the nearest open spot instead.)
        if (back && Interpenetration.Inside(w, c)) (back, c.Position, c.Facing) = (false, was, facing);
        if (back || Loose(w, c))
        {
            c.Velocity = Vector3.Zero;
            c.Rig?.Reset(w, c);
            w.Unstuck++;
        }
        c.GoodAt = c.Behind = c.ProgressAt = c.Position;
        c.Path.Clear();
        c.Repath = true;
        c.PathOnly = w.Time + 3f;
        c.HasGoal = false;
        c.HasAway = false;
        c.StuckFor = 0f;
        c.Repathed = false;
    }

    /// <summary>How long one gives way to another met face to face, s.</summary>
    const float GiveWay = 2.5f;

    /// <summary>Whether a body is held up by another in its way: you, a town's person (<paramref name="by"/> null), or a creature before it on its floor.</summary>
    static bool Blocked(WildSim w, Creature c, out Creature by)
    {
        by = null;
        foreach (ITarget t in w.Targets)
            if (t.Attackable && Pressed(c, t.Position, 0.35f)) return true;
        foreach (Creature o in w.Creatures)
            if (o != c && WildSim.Spaced(o) && Pressed(c, o.Position, o.Radius)) { by = o; return true; }
        return false;
    }

    /// <summary>Whether another body stands on a spot (a lurker's home taken: where it is will do).</summary>
    static bool Taken(WildSim w, Creature c, Vector3 spot)
    {
        foreach (Creature o in w.Creatures)
            if (o != c && WildSim.Spaced(o) && MathF.Abs(o.Position.Y - spot.Y) < 1.5f && WildSim.Flat(o.Position - spot).Length() < c.Radius + o.Radius) return true;
        return false;
    }

    /// <summary>Whether a body is up against another of this radius at a point, wanting to go its way.</summary>
    static bool Pressed(Creature c, Vector3 at, float radius)
    {
        Vector3 to = at - c.Position;
        return MathF.Abs(to.Y) < 1.5f && WildSim.Flat(to).Length() < c.Radius + radius + 0.3f && to.Dot(c.Desired) > 0f;
    }

    /// <summary>The nearest open spot a metre or two round a body that its own grid lets it stand on and go from (false, and left where it was, if none).</summary>
    static bool Loose(WildSim w, Creature c)
    {
        if (c.Space == null)
        {
            for (float r = 1f; r <= 3f; r += 1f)
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = c.Position + new Vector3(MathF.Cos(k * MathF.Tau / 8f), 0f, MathF.Sin(k * MathF.Tau / 8f)) * r;
                    if (!w.Terrain.Inside(p, 6f) || w.Steep(p) || w.Nav.Rock(p, Locomotion.Footprint(c.Species)) || Interpenetration.SolidSurface(w, p, c.Species) || !w.Nav.LineClear(c.Position, p)) continue;
                    c.Position = new Vector3(p.X, w.Terrain.Height(p.X, p.Z), p.Z);
                    return true;
                }
            return false;
        }
        // In a place: a cell its body can hold two or three metres off (a walker's floor; any surface for a climber, in a
        // duct as on a wall), set down on what is under it there, not where it was.
        bool climbs = c.Species.Climbs;
        SpaceNav.Body body = climbs ? SpaceNav.BodyOf(c.Species) : SpaceNav.Body.Of(false, SpaceNav.Headroom(c.Species));
        Vector3I at = SpaceMath.Cell(Crawl.Center(c));
        (Vector3 was, Basis facing) = (c.Position, c.Facing);
        for (int r = 2; r <= 3; r++)
        for (int dy = climbs ? -r : -1; dy <= (climbs ? r : 1); dy++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            var cell = new Vector3I(at.X + dx, at.Y + dy, at.Z + dz);
            if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dz), climbs ? Math.Abs(dy) : 0)) != r || !SpaceNav.Walkable(c.Space, cell, body)) continue;
            Crawl.Place(w, c, SpaceNav.Point(c.Space, cell, body) - (climbs ? Vector3.Zero : Vector3.Up * 0.4f), null);
            if (!Interpenetration.Inside(w, c) && (c.Position - was).Length() > 1f) return true;
        }
        (c.Position, c.Facing) = (was, facing);
        return false;
    }
}
