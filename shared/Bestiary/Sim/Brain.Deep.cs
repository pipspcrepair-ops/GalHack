using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// The creatures of caves and places (POIs), by role. They live on a solid space's surfaces and
/// move with <see cref="Crawl"/>; they notice players gradually, by sight along a real line or by
/// sound (<see cref="Senses"/>), and until they do, the place's hunters go after its subsisters: a
/// small food chain a careful player can watch.
///
/// Fairness: nothing here may start an attack on a player until it has given itself away
/// with a tell (<see cref="TellKind"/>: a rattle, dust, scratching, breathing, a hiss) that has
/// run for the species' tell time, at least a second. Only then does the attack begin, and the
/// attack's own wind-up is the usual telegraph on top. <see cref="StrikeAt"/> is the one gate.
/// </summary>
public static partial class Brain
{
    static void Deep(WildSim w, Creature c, float dt)
    {
        Species s = c.Species;
        c.Wake = Mathf.MoveToward(c.Wake, 0f, dt * 0.4f);
        c.Hunger = MathF.Min(1f, c.Hunger + dt / 150f);
        // What it can see and hear of a player.
        if (c.Space != null && !c.Falling)
        {
            ITarget player = NearestIn3D(w, c, out float pd);
            Senses.Inside(w, c, player, pd, dt);
        }
        if (c.InWalls)
        {
            InWalls(w, c, dt);
            return;
        }
        if (c.Falling || c.Space == null)
        {
            c.Desired = Vector3.Zero;
            return;
        }
        if (c.Noticed != null) c.LookAt = c.LastSeen + Vector3.Up * 1.2f;

        // Shot by a player: it knows exactly where they are.
        if (c.ProvokedBy != null && w.Time - c.ProvokedAt < 12f && c.ProvokedBy.Attackable)
        {
            c.Noticed = c.ProvokedBy;
            c.NoticedAt = w.Time;
        }

        // Those that keep to themselves (a place's insects) go about it and scatter, unless just hurt.
        if (s.Passive && !(c.ProvokedBy != null && w.Time - c.ProvokedAt < 12f && c.ProvokedBy.Attackable))
        {
            Subsist(w, c, dt);
            return;
        }
        switch (s.Role)
        {
            case DeepRole.Subsister: Subsist(w, c, dt); break;
            case DeepRole.Prowler: Prowl(w, c, dt); break;
            case DeepRole.Lurker: Lurk(w, c, dt); break;
            case DeepRole.Nester:
            case DeepRole.DerelictNester: Guard(w, c, dt); break;
            default: AmbushRole(w, c, dt); break;
        }
    }

    // ---- Noticing -----------------------------------------------------------------------

    static ITarget NearestIn3D(WildSim w, Creature c, out float distance)
    {
        ITarget best = null;
        distance = float.MaxValue;
        foreach (ITarget t in w.Targets)
        {
            if (!t.Attackable) continue;
            float d = (t.Position - c.Position).Length();
            if (d < distance)
            {
                distance = d;
                best = t;
            }
        }
        return best;
    }

    // ---- The tell, then the strike --------------------------------------------------------

    /// <summary>
    /// Attack a target in reach. A player is never attacked without a tell first: the first
    /// call starts the tell and the creature holds still, giving itself away, for its tell
    /// time; once that has run the attack (with its own telegraphed wind-up) begins. Another
    /// creature (prey) is attacked straight away, with the usual wind-up.
    /// </summary>
    static bool StrikeAt(WildSim w, Creature c, ITarget t, TellKind tell, Vector3? at = null)
    {
        if (t == null || !t.Attackable || c.Stats.Damage <= 0f) return false;
        if (t is not CreatureTarget)
        {
            bool fresh = c.TellTarget != t || w.Time - c.TellStart > 30f;
            if (fresh)
            {
                BeginTell(w, c, t, tell, at);
                return false;
            }
            if (w.Time - c.TellStart < c.Species.TellSeconds)
            {
                c.SetState(CState.Tell);
                c.Desired = Vector3.Zero;
                c.FacePoint = t.Position;
                return false;
            }
        }
        if (!Attacks.Start(w, c, t)) return false;
        c.SetState(CState.Chase);
        return true;
    }

    /// <summary>Starts a tell: the event the view turns into a rattle, falling dust, a shadow, scratching or a hiss.</summary>
    static void BeginTell(WildSim w, Creature c, ITarget t, TellKind kind, Vector3? at)
    {
        c.TellTarget = t;
        c.TellStart = w.Time;
        c.TellKind = kind;
        c.SetState(CState.Tell);
        c.Desired = Vector3.Zero;
        Vector3 where = at ?? Crawl.Center(c);
        Vector3 below = where;
        if (c.Space != null && SpaceMath.FloorBelow(c.Space, where.X, where.Y - 0.3f, where.Z, 8f) is float y) below = new Vector3(where.X, y, where.Z);
        if (kind == TellKind.Breath) c.Wake = 1f;
        w.RaiseTell(new TellEvent { Creature = c, Kind = kind, Position = where, Below = below, Time = w.Time, Duration = c.Species.TellSeconds, Target = t });
    }

    /// <summary>The tell that suits where an ambusher hides.</summary>
    static TellKind TellFor(Creature c, HideKind hide)
    {
        TellKind[] tells = c.Species.Tells;
        TellKind Pick(params TellKind[] wanted)
        {
            foreach (TellKind k in wanted) if (Array.IndexOf(tells, k) >= 0) return k;
            return tells.Length > 0 ? tells[0] : TellKind.Growl;
        }
        return hide switch
        {
            HideKind.Ceiling => Pick(TellKind.Dust, TellKind.Shadow, TellKind.Click),
            HideKind.Vent => Pick(TellKind.Rattle, TellKind.Scratch),
            HideKind.Door => Pick(TellKind.Thump, TellKind.Scratch),
            HideKind.Crack => Pick(TellKind.Scratch, TellKind.Click),
            HideKind.Dark => Pick(TellKind.Breath, TellKind.Click),
            _ => c.Species.Blind ? Pick(TellKind.Click, TellKind.Growl) : Pick(TellKind.Growl, TellKind.Hiss, TellKind.Click),
        };
    }

    // ---- Moving through the place --------------------------------------------------------

    /// <summary>Heads for a point through a cave or a place along its surfaces, round corners, as the brain does.</summary>
    public static void Toward(WildSim w, Creature c, Vector3 goal, float speed) => GoTo(w, c, goal, speed);

    /// <summary>Heads for a point along the place's surfaces, on a path when the way isn't straight.</summary>
    static void GoTo(WildSim w, Creature c, Vector3 goal, float speed)
    {
        Vector3 center = Crawl.Center(c);
        float d = (goal - center).Length();
        bool climbs = c.Species.Climbs;
        SpaceNav.Body body = SpaceNav.BodyOf(c.Species);
        // Straight there when it is near and in the open: for a walker among furniture, only if its body gets by all the way.
        if (d < 2.5f && SpaceMath.Clear(c.Space, center, goal, 0f)
            && (climbs || c.Space.Parts is not { } parts || parts.Sweep(c.Position, goal with { Y = c.Position.Y }, body.Radius, body.Height)))
        {
            c.SpacePath.Clear();
            Vector3 straight = goal - center;
            float ease = MathF.Max(1.2f, Heft.StoppingDistance(c, c.CrawlSpeed) * 1.3f);
            c.Desired = straight.LengthSquared() > 1e-4f ? straight.Normalized() * speed * Math.Clamp(d / ease, 0.3f, 1f) : Vector3.Zero;
            return;
        }
        c.SpacePathAge += c.ThinkDt;
        bool stale = c.SpacePath.Count == 0 || c.SpacePathIndex >= c.SpacePath.Count || c.SpacePathGoal.DistanceTo(goal) > 1.5f || c.SpacePathAge > 4f;
        if (stale && w.TakePathBudget())
        {
            SpaceNav.FindPath(c.Space, center, goal, body, c.SpacePath, 2500);
            c.SpacePathIndex = 0;
            c.SpacePathGoal = goal;
            c.SpacePathAge = 0f;
        }
        WaypointsPassed(c, center);
        // Stuck (no headway for a while): look for another way.
        if (c.Velocity.LengthSquared() < 0.0025f && speed > 0.1f) c.SpacePathAge += c.ThinkDt * 2f;
        Vector3 next = c.SpacePathIndex < c.SpacePath.Count ? c.SpacePath[c.SpacePathIndex] : goal;
        Vector3 dir = next - center;
        c.Desired = dir.LengthSquared() > 1e-4f ? dir.Normalized() * speed : Vector3.Zero;
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
        while (c.SpacePathIndex < c.SpacePath.Count - 1)
        {
            Vector3 to = c.SpacePath[c.SpacePathIndex] - center;
            float along = (to - up * to.Dot(up)).Length();
            // Among furniture a walker keeps to its way's points (the gaps it fits through), cutting no corner round a leg.
            bool close = parts != null && parts.Any(c.SpacePath[c.SpacePathIndex]) ? along < 0.3f && MathF.Abs(to.Dot(up)) < 1.6f : to.Length() < 0.9f;
            // A walker on a flight passes a cell's middle under or over it: its body stands a metre or so off it.
            if (close || (c.Species.Climbs && along < 0.4f && to.Length() < 1.8f)
                || (!c.Species.Climbs && along < 0.45f && MathF.Abs(to.Dot(up)) < 1.6f)) c.SpacePathIndex++;
            else break;
        }
    }

    static float Dist3(Creature c, Vector3 p) => (p - c.Position).Length();

    // ---- Cracks: in at one, out at another (or gone) --------------------------------------

    static Crack NearestCrack(WildSim w, Creature c, float radius, Vector3? awayFrom = null)
    {
        Crack best = null;
        float bestScore = float.MaxValue;
        foreach (Crack k in w.Cracks)
        {
            if (k.Kind == CrackKind.Door) continue;
            float d = Dist3(c, k.Position);
            if (d > radius) continue;
            float score = d;
            // Not past the thing it is running from.
            if (awayFrom is { } threat && (k.Position - threat).Length() < d * 0.8f) score += 20f;
            if (score < bestScore) { bestScore = score; best = k; }
        }
        return best;
    }

    /// <summary>
    /// Slips into a crack, a vent or a door. If its network leads anywhere it travels through
    /// the rock to another (scratching as it goes) and comes out there; if not, it is gone for
    /// good. <paramref name="park"/> keeps it waiting just inside, an ambusher in its hole.
    /// </summary>
    public static void EnterCrack(WildSim w, Creature c, Crack crack, bool park = false)
    {
        Attacks.Cancel(c);
        c.InCrack = crack;
        c.CrackExit = null;
        c.WallTime = 0f;
        c.WallLength = float.MaxValue;
        c.ScratchTimer = 0f;
        c.Velocity = Vector3.Zero;
        c.CrawlSpeed = 0f;
        c.SetState(CState.InWalls);
        c.Position = crack.Position;
        w.RaiseSlipped(c, crack, true);
        if (park) return;
        var exits = w.Cracks.FindAll(k => k != crack && k.Network == crack.Network && k.Kind != CrackKind.Door && (k.Position - crack.Position).Length() < 60f);
        if (exits.Count == 0)
        {
            c.Removed = true; // into a crack that leads nowhere you can follow: gone
            return;
        }
        c.CrackExit = c.Rng.Pick(exits);
        float speed = MathF.Max(0.3f, c.Stats.WalkSpeed * w.Data.Deep.Crawl.WallSpeed);
        c.WallLength = (c.CrackExit.Position - crack.Position).Length() / speed + c.Rng.Range(2f, 6f);
    }

    /// <summary>Comes out of a crack onto the surface round its mouth.</summary>
    public static void Emerge(WildSim w, Creature c, Crack at)
    {
        c.InCrack = null;
        c.CrackExit = null;
        Crawl.Place(w, c, at.Position + at.Normal * 0.3f, at.Normal);
        c.SetState(CState.Wander);
        c.HasGoal = false;
        c.SpacePath.Clear();
        w.RaiseSlipped(c, at, false);
    }

    static void InWalls(WildSim w, Creature c, float dt)
    {
        c.Desired = Vector3.Zero;
        if (c.CrackExit == null)
        {
            // Waiting just inside its hole.
            c.Position = c.InCrack.Position;
            if (c.Species.Role is DeepRole.Ambusher or DeepRole.Crawler) AmbushRole(w, c, dt);
            return;
        }
        c.WallTime += dt;
        float t = Math.Clamp(c.WallTime / MathF.Max(0.1f, c.WallLength), 0f, 1f);
        c.Position = c.InCrack.Position.Lerp(c.CrackExit.Position, t);
        // Scratching inside the walls, moving with it: you hear where it is going.
        c.ScratchTimer -= dt;
        if (c.ScratchTimer <= 0f)
        {
            c.ScratchTimer = w.Data.Deep.Crawl.ScratchEvery * c.Rng.Range(0.7f, 1.4f);
            w.RaiseTell(new TellEvent { Creature = c, Kind = TellKind.Scratch, Position = c.Position, Below = c.Position, Time = w.Time, Duration = 0f });
        }
        if (t >= 1f) Emerge(w, c, c.CrackExit);
    }

    // ---- Subsisters: graze, scavenge, flee -------------------------------------------------

    static void Subsist(WildSim w, Creature c, float dt)
    {
        Species s = c.Species;
        // A hunter coming at it, or a player it has noticed: run, into a crack if one is near.
        Vector3? threat = null;
        Creature hunter = Hunting(w, c);
        if (hunter != null) threat = hunter.Position;
        else if (c.Noticed != null) threat = c.Noticed.Position;
        if (threat is { } from)
        {
            c.SetState(CState.Flee);
            Crack hole = s.FleesIntoCracks ? NearestCrack(w, c, 14f, from) : null;
            if (hole != null)
            {
                if (Dist3(c, hole.Position) < 0.7f + c.Clearance) EnterCrack(w, c, hole);
                else GoTo(w, c, hole.Position, c.Stats.RunSpeed);
                return;
            }
            Vector3 away = c.Position - from;
            away -= c.Facing.Y * away.Dot(c.Facing.Y);
            c.Desired = (away.LengthSquared() > 1e-4f ? away.Normalized() : -c.Facing.Z) * c.Stats.RunSpeed;
            return;
        }

        // Feeding: settle on a patch a while, then move on to another.
        if (c.State == CState.Eat)
        {
            FoodPatch here = NearestFood(w, c, 2f, 0f);
            if (here == null || here.Amount <= 0.05f || c.StateTime > 9f + c.Id % 5)
            {
                c.SetState(CState.Wander);
                c.HasGoal = false;
                return;
            }
            c.Desired = Vector3.Zero;
            c.WantGraze = 1f;
            c.FacePoint = here.Position;
            here.Amount = MathF.Max(0f, here.Amount - dt * 0.012f);
            return;
        }
        if (!c.HasGoal || c.StateTime > 25f)
        {
            FoodPatch patch = PickFood(w, c);
            c.Goal = patch?.Position ?? c.Home;
            c.HasGoal = true;
            c.SetState(CState.Wander);
        }
        if (Dist3(c, c.Goal) < 0.9f + c.Clearance)
        {
            c.Desired = Vector3.Zero;
            if (NearestFood(w, c, 2f, 0.1f) != null)
            {
                c.SetState(CState.Eat);
                return;
            }
            c.SetState(CState.Idle);
            if (c.StateTime > 2f) c.HasGoal = false;
            return;
        }
        GoTo(w, c, c.Goal, c.Stats.WalkSpeed * 0.8f);
    }

    /// <summary>A hunter closing on it fast, or already winding up at it.</summary>
    static Creature Hunting(WildSim w, Creature c)
    {
        foreach (Creature h in w.Creatures)
        {
            if (h == c || h.Dead || h.Removed || h.InWalls || !h.Species.HuntsPrey || h.Space != c.Space) continue;
            float d = Dist3(c, h.Position);
            // A creeping hunter goes unseen; one that charges or springs does not.
            bool atMe = h.Prey == c || h.AttackTarget == c.AsTarget;
            if (d < 3.5f && atMe && (h.State == CState.Chase || h.Phase is AttackPhase.Lunge or AttackPhase.Recover)) return h;
            if (d < 2f && h.State == CState.Chase) return h;
        }
        return null;
    }

    static FoodPatch NearestFood(WildSim w, Creature c, float radius, float minAmount)
    {
        FoodPatch best = null;
        float bestD = radius;
        foreach (FoodPatch f in w.Food)
        {
            if (f.Amount < minAmount) continue;
            float d = Dist3(c, f.Position);
            if (d < bestD) { bestD = d; best = f; }
        }
        return best;
    }

    /// <summary>A patch with food on it near home, chosen so a group spreads over a few.</summary>
    static FoodPatch PickFood(WildSim w, Creature c)
    {
        FoodPatch best = null;
        float bestScore = float.MaxValue;
        foreach (FoodPatch f in w.Food)
        {
            if (f.Amount < 0.2f || f.Kind != c.Species.Diet && c.Species.Diet != Diet.None) continue;
            float d = (f.Position - c.Home).Length();
            if (d > 30f) continue;
            float score = d + c.Rng.Range(0f, 8f) - f.Amount * 4f;
            if (score < bestScore) { bestScore = score; best = f; }
        }
        return best;
    }

    // ---- Hunting the place's own creatures ----------------------------------------------

    /// <summary>A subsister worth going after: alive, out of its crack, in the same place, near, in sight.</summary>
    static Creature FindPrey(WildSim w, Creature c, float radius)
    {
        Creature best = null;
        float bestD = radius;
        foreach (Creature p in w.Creatures)
        {
            if (p == c || p.Dead || p.Removed || p.InWalls || p.Species.Role != DeepRole.Subsister || p.Space != c.Space) continue;
            float d = Dist3(c, p.Position);
            if (d >= bestD) continue;
            if (!c.Species.Blind && !SpaceMath.Clear(c.Space, Crawl.Center(c), Crawl.Center(p), 0f)) continue;
            bestD = d;
            best = p;
        }
        return best;
    }

    /// <summary>
    /// Goes after a subsister: closes in low and slow, then the signature attack (telegraphed
    /// like any first attack); after a kill it feeds. Returns false if there is nothing to hunt.
    /// </summary>
    static bool HuntPrey(WildSim w, Creature c, float dt)
    {
        if (!c.Species.HuntsPrey) return false;
        if (c.State == CState.Eat)
        {
            Creature corpse = c.Prey;
            if (corpse == null || corpse.Removed || corpse.MeatLeft <= 0f || c.StateTime > 12f)
            {
                c.Prey = null;
                c.SetState(CState.Wander);
                return false;
            }
            c.Desired = Vector3.Zero;
            c.WantGraze = 1f;
            c.FacePoint = corpse.Position;
            corpse.MeatLeft -= dt * 0.3f;
            c.Hunger = MathF.Max(0f, c.Hunger - dt * 0.12f);
            return true;
        }
        if (c.Prey != null && c.Prey.Dead && !c.Prey.Removed)
        {
            // Its kill: go and feed.
            if (Dist3(c, c.Prey.Position) < c.Radius + 0.9f) c.SetState(CState.Eat);
            else GoTo(w, c, c.Prey.Position, c.Stats.WalkSpeed);
            return true;
        }
        if (c.Prey == null || c.Prey.Removed || c.Prey.InWalls || c.StateTime > 40f && c.State == CState.Stalk)
        {
            c.Prey = null;
            if (c.Hunger < 0.35f || w.Time < c.PreyLookAt) return false;
            c.PreyLookAt = w.Time + 0.45f;
            c.Prey = FindPrey(w, c, MathF.Max(10f, c.Species.NoticeRadius * 1.4f));
            if (c.Prey == null) return false;
            c.SetState(CState.Stalk);
        }
        Creature prey = c.Prey;
        float d = Dist3(c, prey.Position);
        if (d <= Attacks.Range(c) + 0.2f)
        {
            c.Desired = Vector3.Zero;
            c.FacePoint = prey.Position;
            Attacks.Start(w, c, prey.AsTarget);
            return true;
        }
        // Low and slow, all the way in: an ambush hunter creeps up and pounces from close.
        c.SetState(CState.Stalk);
        c.WantCrouch = 1f;
        GoTo(w, c, prey.Position, c.Stats.WalkSpeed * 0.6f);
        return true;
    }

    /// <summary>
    /// Goes for a player it has noticed. First it gives itself away (a growl, clicking, breath,
    /// a hiss) and holds still while the tell runs; then it closes in, and the strike is its
    /// signature attack with the usual telegraphed wind-up.
    /// </summary>
    static void HuntPlayer(WildSim w, Creature c, ITarget p, TellKind tell)
    {
        float d = Dist3(c, p.Position);
        c.FacePoint = p.Position;
        if (c.TellTarget != p || w.Time - c.TellStart > 30f)
        {
            BeginTell(w, c, p, tell, null);
            return;
        }
        if (w.Time - c.TellStart < c.Species.TellSeconds)
        {
            c.SetState(CState.Tell);
            c.Desired = Vector3.Zero;
            return;
        }
        if (d <= Attacks.Range(c, p) + 0.3f)
        {
            c.Desired = Vector3.Zero;
            StrikeAt(w, c, p, tell);
            if (c.State != CState.Tell) c.SetState(CState.Chase);
            return;
        }
        c.SetState(CState.Chase);
        GoTo(w, c, Senses.Aim(c, p), c.Stats.RunSpeed);
    }

    // ---- Prowlers: a beat through the corridors ------------------------------------------

    static void Prowl(WildSim w, Creature c, float dt)
    {
        if (c.Noticed is { } p)
        {
            HuntPlayer(w, c, p, TellFor(c, HideKind.None));
            return;
        }
        // Something not yet placed: it stops and turns to it. A loud noise brings it over to look.
        if (c.Awareness >= Senses.Curious)
        {
            c.SetState(CState.Idle);
            c.FacePoint = c.LastSeen;
            if (w.Time - c.HeardAt < 8f && Dist3(c, c.LastSeen) > 2.5f) GoTo(w, c, c.LastSeen, c.Stats.WalkSpeed);
            else c.Desired = Vector3.Zero;
            return;
        }
        if (HuntPrey(w, c, dt)) return;
        // Blind prowlers click as they walk: you hear one coming round a corner.
        c.ScratchTimer -= dt;
        if (c.Species.Blind && c.ScratchTimer <= 0f)
        {
            c.ScratchTimer = c.Rng.Range(2.5f, 4f);
            w.RaiseTell(new TellEvent { Creature = c, Kind = TellKind.Click, Position = Crawl.Center(c), Below = c.Position, Time = w.Time });
        }
        List<Vector3> route = c.Route;
        if (route == null || route.Count == 0)
        {
            WanderIn(w, c, 10f);
            return;
        }
        c.SetState(CState.Patrol);
        Vector3 wp = route[Math.Clamp(c.RouteIndex, 0, route.Count - 1)];
        if (Dist3(c, wp) < 1.2f)
        {
            if (c.RouteIndex + c.RouteStep < 0 || c.RouteIndex + c.RouteStep >= route.Count) c.RouteStep = -c.RouteStep;
            c.RouteIndex = Math.Clamp(c.RouteIndex + c.RouteStep, 0, route.Count - 1);
        }
        GoTo(w, c, wp, c.Stats.WalkSpeed);
    }

    static void WanderIn(WildSim w, Creature c, float radius)
    {
        if (!c.HasGoal || c.StateTime > 20f || Dist3(c, c.Goal) < 1.2f)
        {
            // On the way it was going, drifting; back for home if that would take it too far.
            c.WanderTurn = Math.Clamp(c.WanderTurn + c.Rng.Range(-0.6f, 0.6f), -1.2f, 1.2f);
            Vector3 g = c.Position + (-c.Facing.Z).Rotated(c.Facing.Y, c.WanderTurn) * c.Rng.Range(3f, 7f);
            if ((g - c.Home).Length() > radius) g = c.Home + new Vector3(c.Rng.Range(-radius, radius), c.Rng.Range(-1f, 1f), c.Rng.Range(-radius, radius)) * 0.5f;
            Vector3I? cell = SpaceNav.Nearest(c.Space, g, SpaceNav.BodyOf(c.Species), 2);
            c.Goal = cell is { } k ? SpaceNav.Point(c.Space, k, SpaceNav.BodyOf(c.Species)) : c.Home;
            c.HasGoal = true;
            c.SetState(CState.Wander);
        }
        GoTo(w, c, c.Goal, c.Stats.WalkSpeed * 0.7f);
    }

    // ---- Lurkers: still in the dark until you come close ------------------------------------

    static void Lurk(WildSim w, Creature c, float dt)
    {
        Species s = c.Species;
        ITarget p = NearestIn3D(w, c, out float d);
        float strike = s.StrikeRadius * (p?.ThreatScale ?? 1f);
        bool chasing = c.State == CState.Chase && c.StateTime < 6f && c.Noticed != null;
        if (p != null && p.Attackable && (c.Sensing && d < strike || chasing || c.Sensing && c.State == CState.Tell && d < strike * 1.8f))
        {
            c.Wake = 1f;
            HuntPlayer(w, c, p, TellKind.Breath);
            return;
        }
        if (c.State == CState.Tell) c.SetState(CState.ReturnHome); // you backed off in time
        // Something to eat wandered into its room.
        if (c.Prey != null || c.Hunger > 0.35f)
        {
            Creature prey = c.Prey ?? FindPrey(w, c, s.StrikeRadius * 1.3f);
            if (prey != null)
            {
                c.Prey = prey;
                if (HuntPrey(w, c, dt)) return;
            }
        }
        if (Dist3(c, c.Home) > 1.2f && c.State != CState.Lurk)
        {
            c.SetState(CState.ReturnHome);
            GoTo(w, c, c.Home, c.Stats.WalkSpeed);
            return;
        }
        // Waiting: low, still, eyes shut. It watches, eyes half open, if you're in the room.
        c.SetState(CState.Lurk);
        c.Desired = Vector3.Zero;
        c.WantCrouch = 1f;
        c.WantHidden = 0.35f;
        if (c.Noticed != null)
        {
            c.Wake = MathF.Max(c.Wake, 0.5f);
            c.FacePoint = c.Noticed.Position;
        }
        else c.LookAt = null;
    }

    // ---- Ambushers: vents, ceilings, doors, cracks -------------------------------------

    static void AmbushRole(WildSim w, Creature c, float dt)
    {
        Species s = c.Species;
        AmbushPoint a = c.Ambush;
        HideKind hide = a?.Kind ?? HideKind.None;
        // Who walks into it: the nearest player under or in front of it, or, for a hunter, prey.
        ITarget p = NearestIn3D(w, c, out _);
        Vector3 watch = a?.Hole?.Position ?? (c.InWalls ? c.InCrack.Position : Crawl.Center(c));
        float strike = s.StrikeRadius * (p?.ThreatScale ?? 1f);
        bool high = !c.InWalls && c.Facing.Y.Y < -0.5f;
        // Under a ceiling it drops from (up to a fall of 7.5 m); in front of a vent, door or crack.
        float rise = p != null ? watch.Y - p.Position.Y : 0f;
        bool inZone = p != null && p.Attackable && c.Sensing && WildSim.Flat(p.Position - watch).Length() < strike && (high ? rise > 0.5f && rise < 7.5f : MathF.Abs(rise) < 3f);

        if (c.InWalls)
        {
            // Hidden in a vent, a crack or behind a door: the tell plays from inside, then it bursts out.
            if (c.State == CState.Tell && c.TellTarget is { } t && t.Attackable)
            {
                if (w.Time - c.TellStart >= s.TellSeconds)
                {
                    Crack hole = c.InCrack;
                    Emerge(w, c, hole);
                    c.TellTarget = t; // the tell already ran: the strike may begin
                    c.FacePoint = t.Position;
                    Attacks.Start(w, c, t);
                    c.SetState(CState.Chase);
                }
                return;
            }
            if (inZone) BeginTell(w, c, p, TellFor(c, hide), c.InCrack.Position);
            return;
        }

        bool onCeiling = c.Facing.Y.Y < -0.5f;
        if (c.Attacking) return;
        if (onCeiling && c.State == CState.Tell && !inZone)
        {
            // It gave itself away and you got out from under it: it stays up there, waiting.
            if (c.TellTarget == null || !c.TellTarget.Attackable || w.Time - c.TellStart > s.TellSeconds + 2f) c.SetState(CState.Hidden);
            c.Desired = Vector3.Zero;
            return;
        }
        if (inZone || c.State == CState.Tell || (c.State == CState.Chase && c.StateTime < 6f && p != null && p.Attackable && c.Noticed != null))
        {
            // On the ceiling it drops, onto what is still under it; anywhere else it lunges.
            if (inZone && c.State != CState.Chase)
            {
                StrikeAt(w, c, p, TellFor(c, onCeiling ? HideKind.Ceiling : hide), onCeiling ? Crawl.Center(c) : null);
                if (c.State == CState.Tell || onCeiling) return;
            }
            if (p != null && p.Attackable && !onCeiling) HuntPlayer(w, c, p, TellFor(c, hide));
            return;
        }
        // A cave crawler that has seen you comes for you: along the ceiling until it is over
        // your head, if there is a ceiling to follow; otherwise down the walls.
        if (s.Role == DeepRole.Crawler && c.Noticed is { } seen)
        {
            if (onCeiling)
            {
                Vector3 head = seen.Position + Vector3.Up * 1.6f;
                float up = SpaceMath.Ray(c.Space, head, Vector3.Up, 7f);
                if (up < 6.9f)
                {
                    c.SetState(CState.Stalk);
                    GoTo(w, c, head + Vector3.Up * MathF.Max(0f, up - c.Clearance), c.Stats.RunSpeed * 0.8f);
                    return;
                }
            }
            HuntPlayer(w, c, seen, TellFor(c, HideKind.None));
            return;
        }
        if (HuntPrey(w, c, dt)) return;

        // Back to hiding: the ceiling spot, or into its hole to wait.
        if (a == null)
        {
            WanderIn(w, c, 8f);
            return;
        }
        if (a.Hole != null)
        {
            if (Dist3(c, a.Hole.Position) < 0.6f + c.Clearance) EnterCrack(w, c, a.Hole, park: true);
            else
            {
                c.SetState(CState.ReturnHome);
                GoTo(w, c, a.Hole.Position, c.Stats.WalkSpeed);
            }
            return;
        }
        if (Dist3(c, a.Position) > 0.8f)
        {
            c.SetState(CState.ReturnHome);
            GoTo(w, c, a.Position, c.Stats.WalkSpeed);
            return;
        }
        c.SetState(CState.Hidden);
        c.Desired = Vector3.Zero;
        c.WantCrouch = 1f; // flat against the ceiling
    }

    // ---- Nesters: guard the nest ----------------------------------------------------------

    static void Guard(WildSim w, Creature c, float dt)
    {
        Species s = c.Species;
        Nest nest = c.Nest;
        Vector3 at = nest?.Position ?? c.Home;
        ITarget p = NearestIn3D(w, c, out _);
        float fromNest = p != null ? (p.Position - at).Length() : float.MaxValue;
        bool intruder = p != null && p.Attackable && fromNest < s.WarnRadius && (c.Sensing || c.Noticed == p);
        if (intruder)
        {
            c.FacePoint = p.Position;
            // It bites what comes onto the nest, or lingers by it after the warning, or shot at
            // it; it keeps at it only while you stay close. Back off and it lets you go.
            bool close = fromNest < s.DefendRadius || c.ProvokedBy == p;
            bool lingering = c.TellTarget == p && w.Time - c.TellStart > s.TellSeconds + 3f && fromNest < s.WarnRadius * 0.75f;
            bool engaged = c.State == CState.Chase && fromNest < s.DefendRadius * 1.4f;
            if (close || lingering || engaged)
            {
                HuntPlayer(w, c, p, TellKind.Hiss);
                return;
            }
            // The display: between you and the nest, reared up and hissing. Back off.
            if (c.TellTarget != p || w.Time - c.TellStart > 30f) BeginTell(w, c, p, TellKind.Hiss, null);
            c.SetState(CState.Warn);
            c.Rear = 0.8f;
            Vector3 toward = p.Position - at;
            toward.Y = 0f;
            Vector3 stand = at + (toward.LengthSquared() > 0.01f ? toward.Normalized() : Vector3.Forward) * (nest?.Radius ?? 2f) * 0.9f;
            GoTo(w, c, stand, c.Stats.WalkSpeed);
            if (Dist3(c, stand) < 0.8f) c.Desired = Vector3.Zero;
            return;
        }
        if (c.State == CState.Warn || c.State == CState.Tell) c.SetState(CState.ReturnHome);
        // Prey that strays onto the nest is taken.
        if (c.Hunger > 0.3f && FindPrey(w, c, (nest?.Radius ?? 2f) + 3f) is { } stray)
        {
            c.Prey = stray;
            if (HuntPrey(w, c, dt)) return;
        }
        // Wreck nesters build: scraps fetched from round the hold, worked into the nest.
        if (nest != null && nest.Derelict && nest.Size < 1f && c.Id % 2 == 0)
        {
            if (c.State == CState.Scavenge)
            {
                if (c.StateTime > 3f)
                {
                    c.SetState(CState.Wander); // carrying a piece back
                    c.Goal = at;
                    c.HasGoal = true;
                }
                c.Desired = Vector3.Zero;
                c.WantGraze = 1f;
                return;
            }
            if (c.HasGoal && c.Goal == at)
            {
                if (Dist3(c, at) < nest.Radius + 0.6f)
                {
                    nest.Size = MathF.Min(1f, nest.Size + 0.08f);
                    c.HasGoal = false;
                }
                else GoTo(w, c, at, c.Stats.WalkSpeed);
                return;
            }
            FoodPatch scrap = NearestScrap(w, c, at);
            if (scrap != null)
            {
                if (Dist3(c, scrap.Position) < 1f + c.Clearance) c.SetState(CState.Scavenge);
                else GoTo(w, c, scrap.Position, c.Stats.WalkSpeed);
                return;
            }
        }
        // Tending: slow circuits round the nest, then still at its edge.
        float r = (nest?.Radius ?? 2f) + 0.6f;
        if (!c.HasGoal || c.StateTime > 12f || Dist3(c, c.Goal) < 0.8f)
        {
            float ang = c.Rng.Range(0f, MathF.Tau);
            Vector3 g = at + new Vector3(MathF.Cos(ang) * r, 0.5f, MathF.Sin(ang) * r);
            Vector3I? cell = SpaceNav.Nearest(c.Space, g, SpaceNav.BodyOf(c.Species), 2);
            c.Goal = cell is { } k ? SpaceNav.Point(c.Space, k, SpaceNav.BodyOf(c.Species)) : at;
            c.HasGoal = true;
            c.SetState(c.Rng.Chance(0.5f) ? CState.Guard : CState.Wander);
        }
        if (c.State == CState.Guard && c.StateTime < 6f)
        {
            c.Desired = Vector3.Zero;
            c.WantCrouch = 0.6f;
            return;
        }
        GoTo(w, c, c.Goal, c.Stats.WalkSpeed * 0.5f);
    }

    static FoodPatch NearestScrap(WildSim w, Creature c, Vector3 nest)
    {
        FoodPatch best = null;
        float bestD = 30f;
        foreach (FoodPatch f in w.Food)
        {
            if (f.Kind != Diet.Scraps || f.Amount < 0.2f) continue;
            float d = (f.Position - nest).Length();
            if (d < bestD) { bestD = d; best = f; }
        }
        return best;
    }

    /// <summary>Is this creature resting (in its den, burrow, roost or nest, or hidden through its off hours)?</summary>
    public static bool IsResting(Creature c) =>
        c.State == CState.Rest || (c.Species.Behaviour == Behaviour.Ambusher && !c.Species.IsDeep && c.State == CState.Hidden) || (c.Species.Rooted && c.State == CState.Root);
}
