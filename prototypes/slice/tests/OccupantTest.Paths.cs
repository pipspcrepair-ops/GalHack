using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Slice.Places;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

/// <summary>
/// NPCs on their paths in every state (OccupantTest --paths; owner, 17 Nov: "They have a tendency to flee into walls,
/// rather than pathing their way out... they get stuck on things a lot in cramped interiors"). Over fresh worlds:
/// - cramped places: places furnished as in play and filled as the game fills them (with vermin and a prowler of their
///   own kinds put in too, so something always flees and something hunts), an intruder walking room to room and
///   shooting at what it meets: so they wander, patrol, stalk, hunt, flee, go home and fight, among the furniture, in
///   the corridors and below ground;
/// - open ground: a place's buildings standing on it with rocks about, grazers, hunters and a band of people round and
///   in it, scared off and shot at by an intruder walking about.
/// Every step: no body in a wall, a piece of furniture or a rock; nobody stuck (wanting to go and not a quarter metre
/// on in a second) for more than 2% of their time, nor over 3 s at once; and whoever flees ends up farther from what it
/// fled, by the way round (path distance on its own grid), not only in a straight line.
/// </summary>
public partial class OccupantTest
{
    sealed class PathTally
    {
        public int Places, Steps, InWall, InParts, InRock, Entries, Flees, Farther, Cornered, Cracks, Searches, Repaths, Unstuck;
        public float NpcSeconds, Stuck, Longest;
        public double StepMs, WorstMs;
        public int SimSteps;
        public string LongestAt = "", WallAt = "", NearerAt = "";
        public readonly HashSet<CState> States = new();
    }

    sealed class Watch
    {
        public Vector3 Anchor;
        public float Still, FleeAt = -1f, From;
        public Vector3 Threat;
        public int Steps, Jumps;
        public bool Out, HadSpot, WasIn;
        public AttackPhase PrevPhase;
        public float PrevGap = 9f;
        public Vector3 PrevAt;
        public CState PrevState;
        public Vector3 Last;
    }

    const float PathDt = 1f / 30f;
    static readonly Occupant[] PathHolders = { Occupant.Nesters, Occupant.Prowlers, Occupant.Machines, Occupant.Empty, Occupant.Turned };

    void Paths()
    {
        GD.Print("-- NPCs on their paths: cramped places and open ground --");
        Decor decor = Decor.Shared;
        var inside = new PathTally();
        var outside = new PathTally();
        var steep = new PathTally();
        var faces = new SteepTally();
        var families = _lib.Families.Where(f => f.Types.Count == 0 && !f.Space && f.StateWeights.Any(w => w > 0f)).ToList();
        for (int w = 0; w < 3; w++)
        {
            string arch = Archetypes[_rng.Next(Archetypes.Length)];
            ulong seed = Seed();
            WorldRoll roll = Roll(seed, arch);
            WorldProfile profile = WorldProfile.Roll(seed, arch, false);
            WorldConditions cond = Conditions(roll, seed);
            Slice.Surface.WildlifePatch.Story(cond, roll, profile, WorldReading.Of(roll));
            WorldLife life = Bestiary.Roll(_data, cond);
            var deep = new PlaceFauna.Deep(life, null, 1f);
            int done = 0;
            foreach (FamilyDef f in families.OrderBy(_ => _rng.Next()))
                if (done < 5 && InsideWalk(f, deep, decor, inside)) done++;
            done = 0;
            foreach (FamilyDef f in families.OrderBy(_ => _rng.Next()))
                if (done < 2 && OutsideWalk(f, life, outside)) done++;
            SteepWalk(life, steep, faces, w);
        }
        PathChecks("in cramped places", inside, 10);
        PathChecks("on open ground round buildings", outside, 4);
        PathChecks("on steep ground", steep, 3);
        SteepChecks(faces);
    }

    void PathChecks(string where, PathTally t, int places)
    {
        GD.Print($"    {where}: {t.Places} places, {t.NpcSeconds:F0} NPC-seconds over {t.SimSteps} steps; states {string.Join(" ", t.States.OrderBy(s => s))}; "
            + $"a step {t.StepMs / Math.Max(1, t.SimSteps):F3} ms on average, worst {t.WorstMs:F2} ms, {t.Searches} path searches; {t.Repaths} ways found again for no headway, {t.Unstuck} put back where they last went well");
        Check(t.Places >= places && t.NpcSeconds > 300f, $"{where}: {t.Places} places walked, {t.NpcSeconds:F0} NPC-seconds");
        Check(t.InWall + t.InParts + t.InRock == 0, $"{where}: no body in a wall, furniture or a rock ({t.Entries} times in: {t.InWall} body-steps in walls, {t.InParts} in furniture, {t.InRock} in rocks, of {t.Steps}){t.WallAt}");
        float share = t.Stuck / Math.Max(1f, t.NpcSeconds);
        Check(share < 0.02f && t.Longest <= 3f, $"{where}: stuck {share:P2} of the time (under 2%), longest {t.Longest:F1} s (3 s at most){t.LongestAt}");
        Check(t.Flees >= 5 && t.Farther >= t.Flees * 0.9f, $"{where}: {t.Farther} of {t.Flees} flights end farther from what they fled by the way round (nine in ten at least; {t.Cornered} cornered, with nowhere farther to reach, and {t.Cracks} running for a crack){t.NearerAt}");
    }

    /// <summary>One place furnished and filled as in play, walked by an intruder for 40 s; false if it holds too little to watch.</summary>
    bool InsideWalk(FamilyDef f, PlaceFauna.Deep deep, Decor decor, PathTally t)
    {
        PoiInstance inst;
        try { inst = PoiFactory.Spawn(_lib, PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, Seed()), PoiState.Abandoned, Seed(), null, new OccupancyRoll(PathHolders[_rng.Next(PathHolders.Length)])); }
        catch (Exception) { return false; }
        PlaceMemory m = Memory(inst);
        m.Rooms = decor.Plan(m);
        PlaceFauna fauna = PlaceFauna.Plan(m, deep);
        var rooms = fauna.Rooms.Rooms.Where(r => r.Kind is not (RoomKind.Vent or RoomKind.Duct) && r.Size.Y >= 2f && r.Size.X >= 2f && r.Size.Z >= 2f).ToList();
        if (rooms.Count < 3) return false;
        var sim = new WildSim(_data, new Wildlife.Terrain((x, z) => -50f, 64f), new Wildlife.NavGrid(64f, 3f));
        BlockSpace space = PlaceFauna.SpaceOf(m);
        if (!fauna.Silent) PoiFauna.Spawn(sim, fauna.Population, space);
        var rng = Wildlife.Rng.For(inst.SpawnSeed, "paths");
        // Something to flee and something to hunt it: vermin and a prowler of the place's own kinds, fitted to the room they're put in.
        for (int i = 0; i < 6; i++)
        {
            PoiRoom r = rooms[rng.Int(0, rooms.Count - 1)];
            Species s = deep.Fauna.For(i < 4 ? DeepRole.Subsister : DeepRole.Prowler, fauna.Rooms.Theme, r.Depth);
            s = s == null ? null : deep.Fauna.Fitted(s, MathF.Min(r.Size.X, r.Size.Z), r.Size.Y);
            // (These vermin run for it through the rooms, never into a crack: the flights watched.)
            if (s != null && i < 4) { s = s.Resized(s.Body.Size); s.FleesIntoCracks = false; }
            if (s != null && Floor(space, r, SpaceNav.BodyOf(s), rng) is { } at) sim.SpawnIn(s, space, at);
        }
        PlaceFauna.CullUnreached(sim, PlaceFauna.Reach(inst, fauna.Rooms));
        if (sim.Creatures.Count(c => !c.Removed) < 3) return false;
        var person = SpaceNav.Body.Of(false, 2);
        Vector3? start = Floor(space, rooms[0], person, rng);
        if (start == null) return false;
        t.Places++;
        var you = new DummyTarget { Name = "intruder", Position = start.Value };
        sim.Targets.Add(you);
        var way = new List<Vector3>();
        int next = 0;
        var watch = new Dictionary<Creature, Watch>();
        int searches = SpaceNav.Searches;
        for (int step = 0; step < 40 * 30; step++)
        {
            // From room to room by the place's own ways, at a walk; now and then a shot at whatever is nearest in sight.
            if (next >= way.Count)
            {
                next = 0;
                if (Floor(space, rooms[rng.Int(0, rooms.Count - 1)], person, rng) is { } goal) SpaceNav.FindPath(space, you.Position + Vector3.Up * 0.5f, goal, person, way, 8000);
                for (int k = 0; k < way.Count; k++) way[k] -= Vector3.Up * 0.5f;
            }
            if (next < way.Count)
            {
                Vector3 to = way[next] - you.Position;
                if (to.Length() < 0.25f) next++;
                else
                {
                    you.Position += to.Normalized() * MathF.Min(to.Length(), 1.5f * PathDt);
                    if (new Vector2(to.X, to.Z).LengthSquared() > 1e-4f) you.Forward = new Vector3(to.X, 0f, to.Z).Normalized();
                }
            }
            if (step % 150 == 75) Shoot(sim, you, c => SpaceMath.Clear(space, you.Position + Vector3.Up * 1.5f, Crawl.Center(c), 0f));
            you.Health.Restore();
            Stepped(sim, t);
            Watched(sim, t, watch, you, f.Id, (c, from) => Through(space, c, from));
        }
        t.Searches += SpaceNav.Searches - searches;
        (t.Repaths, t.Unstuck) = (t.Repaths + sim.Repaths, t.Unstuck + sim.Unstuck);
        return true;
    }

    /// <summary>A place's buildings standing on open ground with rocks about, grazers, hunters and a band in and round them, for 60 s.</summary>
    bool OutsideWalk(FamilyDef f, WorldLife life, PathTally t)
    {
        PoiInstance inst;
        try { inst = PoiFactory.Spawn(_lib, PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, Seed()), PoiState.Ruined, Seed(), null, null); }
        catch (Exception) { return false; }
        BlockGrid g = inst.Blocks;
        float half = MathF.Max(MathF.Max(-g.Min.X, g.Min.X + g.Size.X), MathF.Max(-g.Min.Z, g.Min.Z + g.Size.Z)) + 40f;
        if (half > 140f) return false;
        var nav = new Wildlife.NavGrid(half, 3f);
        var sim = new WildSim(_data, new Wildlife.Terrain((x, z) => 0f, half), nav);
        sim.SolidAt = p =>
        {
            var c = new PInt3((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
            return g.InBounds(c) && PlaceFauna.BuiltSolid(inst, c);
        };
        // Is there much built standing on the ground? (A place all below ground has nothing to walk round.)
        int walls = 0;
        for (int x = g.Min.X; x < g.Min.X + g.Size.X; x++)
            for (int z = g.Min.Z; z < g.Min.Z + g.Size.Z; z++)
                if (sim.SolidAt(new Vector3(x + 0.5f, 0.5f, z + 0.5f))) walls++;
        if (walls < 60) return false;
        var rng = Wildlife.Rng.For(inst.SpawnSeed, "paths-out");
        Vector3 Open(float r)
        {
            for (int k = 0; k < 400; k++)
            {
                var p = new Vector3(rng.Range(-half + 12f, half - 12f), 0f, rng.Range(-half + 12f, half - 12f));
                if (k < 300 && !g.InBounds(new PInt3((int)p.X, 0, (int)p.Z))) continue; // in among the buildings, mostly
                if (!Built(sim, p) && !Rock(nav, p, r)) return p;
            }
            return Vector3.Zero;
        }
        for (int i = 0; i < 16; i++)
        {
            Vector3 p = Open(2f);
            if (p != Vector3.Zero) nav.AddObstacle(p, rng.Range(0.5f, 1.4f));
        }
        var walkers = life.Fauna.Species.Where(s => s.Rig == RigKind.Legged && !s.Flies && !s.Mechanical && s.Domestic == DomesticKind.None && s.Body.Size < 2.5f).ToList();
        if (walkers.Count == 0) return false;
        Species grazer = walkers[rng.Int(0, walkers.Count - 1)].Resized(1f);
        grazer.Behaviour = Behaviour.Grazer;
        Species hunter = walkers[rng.Int(0, walkers.Count - 1)].Resized(1.2f);
        hunter.Behaviour = Behaviour.Predator;
        hunter.Temperament = Temperament.Aggressive;
        hunter.Stats.Hunts = true;
        hunter.Stats.Damage = 1f;
        Species folk = life.People.Person((ulong)_rng.NextInt64(), PersonRole.Scavenger, Occupancy.Friendly);
        Species raider = life.People.Person((ulong)_rng.NextInt64(), PersonRole.Raider, Occupancy.Raider);
        raider.Figure.Prop = "club";
        sim.SpawnGroup(grazer, Open(1f), 5);
        sim.SpawnGroup(grazer, Open(1f), 4);
        sim.SpawnGroup(folk, Open(1f), 4);
        sim.SpawnGroup(raider, Open(1f), 3);
        sim.Spawn(hunter, Open(1f));
        sim.Spawn(hunter, Open(1f));
        foreach (Creature c in sim.Creatures.ToArray()) if (Interpenetration.SolidSurface(sim, c.Position, c.Species) || Rock(nav, c.Position, Locomotion.Footprint(c.Species))) c.Removed = true;
        if (sim.Creatures.Count(c => !c.Removed) < 8) return false;
        t.Places++;
        var you = new DummyTarget { Name = "intruder", Position = Open(1f) };
        sim.Targets.Add(you);
        var way = new List<Vector3>();
        int next = 0;
        var watch = new Dictionary<Creature, Watch>();
        int searches = sim.Nav.Searches;
        Func<int, int, bool> blocked = (x, z) => Built(sim, new Vector3(x + 0.5f, 0f, z + 0.5f)) || Rock(nav, new Vector3(x + 0.5f, 0f, z + 0.5f), 0.3f);
        for (int step = 0; step < 60 * 30; step++)
        {
            if (next >= way.Count)
            {
                next = 0;
                way.Clear();
                Vector3 goal = Open(1f);
                if (FlatRoute(blocked, you.Position, goal, 70f, way) == null) you.Position = goal;
            }
            if (next < way.Count)
            {
                Vector3 to = WildSim.Flat(way[next] - you.Position);
                if (to.Length() < 0.25f) next++;
                else
                {
                    you.Position += to.Normalized() * MathF.Min(to.Length(), 1.6f * PathDt);
                    you.Forward = to.Normalized();
                }
            }
            if (step % 150 == 75) Shoot(sim, you, c => true);
            if (step % 210 == 100)
                foreach (Creature c in sim.Creatures)
                    if (!c.Dead && !c.Removed && WildSim.Flat(c.Position - you.Position).Length() < 30f && (c.Species.Behaviour == Behaviour.Grazer || c.Herd?.Party != null && c.Id % 2 == 0)) sim.Scare(c, you.Position, 5f);
            you.Health.Restore();
            Stepped(sim, t);
            Watched(sim, t, watch, you, f.Id, (c, from) => FlatRoute(blocked, c.Position, from, 70f, null) ?? float.PositiveInfinity);
        }
        t.Searches += sim.Nav.Searches - searches;
        (t.Repaths, t.Unstuck) = (t.Repaths + sim.Repaths, t.Unstuck + sim.Unstuck);
        return true;
    }

    /// <summary>A place's built blocks at a point of open ground, at a body's middle.</summary>
    static bool Built(WildSim sim, Vector3 p) => sim.SolidAt(new Vector3(p.X, 0.5f, p.Z));

    static bool Rock(Wildlife.NavGrid nav, Vector3 p, float r)
    {
        foreach (Obstacle o in nav.Around(p, r + 1.6f))
            if (o.Active && WildSim.Flat(p - o.Position).Length() < o.Radius + r) return true;
        return false;
    }

    /// <summary>Shot at by the intruder: the nearest living thing in reach it can see, hurt a little.</summary>
    static void Shoot(WildSim sim, DummyTarget you, Func<Creature, bool> seen)
    {
        Creature best = null;
        float bestD = 16f;
        foreach (Creature c in sim.Creatures)
        {
            if (c.Dead || c.Removed || c.InWalls) continue;
            float d = (c.Position - you.Position).Length();
            if (d < bestD && seen(c)) { bestD = d; best = c; }
        }
        if (best == null) return;
        sim.Noise(you.Position + Vector3.Up * 1.5f, 30f, (best.Position - you.Position).Normalized(), you);
        sim.Damage(best, best.Stats.MaxHealth * 0.06f, you);
    }

    static void Stepped(WildSim sim, PathTally t)
    {
        long t0 = Stopwatch.GetTimestamp();
        sim.Step(PathDt);
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        t.StepMs += ms;
        t.WorstMs = Math.Max(t.WorstMs, ms);
        t.SimSteps++;
    }

    /// <summary>Every body this step: in a wall, furniture or a rock; making no headway while it wants to go; and how its flight went.</summary>
    static void Watched(WildSim sim, PathTally t, Dictionary<Creature, Watch> watch, DummyTarget you, string place, Func<Creature, Vector3, float> distance)
    {
        foreach (Creature c in sim.Creatures)
        {
            if (c.Removed || c.Dead || c.InWalls || c.Falling || c.Dormant || c.Thrown || c.HeldBy != null || c.Species.Flies && !c.Landed && c.Space == null)
            {
                if (!watch.TryGetValue(c, out Watch gone)) watch[c] = gone = new Watch();
                (gone.Still, gone.FleeAt, gone.Anchor, gone.Last, gone.Out) = (0f, -1f, c.Position, c.Position, c.InWalls);
                continue;
            }
            t.Steps++;
            t.NpcSeconds += PathDt;
            t.States.Add(c.State);
            if (!watch.TryGetValue(c, out Watch k)) watch[c] = k = new Watch { Anchor = c.Position, Last = c.Position };
            if ((c.Position - k.Last).Length() > 0.6f) k.Jumps++;
            k.Last = c.Position;
            string who = $"{place}: {c.Species.CommonName} ({(c.Species.Figure != null ? "person" : c.Species.Climbs ? "climber" : "walker")}, {c.State}) at {c.Position.X:F1},{c.Position.Y:F1},{c.Position.Z:F1}";
            bool out_ = k.Out;
            k.Out = false;
            if (Interpenetration.Inside(sim, c))
            {
                bool parts = c.Space?.Parts is { } ps && !c.Species.Climbs && ps.Gap(c.Position, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species)) < -Interpenetration.Tolerance;
                if (parts) t.InParts++; else t.InWall++;
                if (!k.WasIn) t.Entries++;
                if (t.WallAt == "" && !k.WasIn) t.WallAt = $"; e.g. {who} in {(parts ? "furniture" : "a wall")} (the step before {k.PrevPhase}, {k.PrevState}, its body {k.PrevGap:F2} m off the furniture and {(c.Position - k.PrevAt).Length():F2} m from where it is now, {(c.Space?.Parts is { } pp ? pp.Gap(c.Position, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species)) : 0f):F2} now; {(out_ ? "just out of a crack" : k.Steps == 0 ? "as first seen" : $"{k.Steps} steps after first seen")}, {c.Phase}, {c.Species.Role}, density {c.Space?.Density(Crawl.Center(c)) ?? 0f:F2}, bare {c.Space?.Bare.Density(Crawl.Center(c)) ?? 0f:F2}, up {c.Facing.Y.X:F1},{c.Facing.Y.Y:F1},{c.Facing.Y.Z:F1})";
            }
            else if (c.Space == null && Rock(sim.Nav, c.Position, Locomotion.Footprint(c.Species) - 0.05f))
            {
                t.InRock++;
                if (t.WallAt == "") t.WallAt = $"; e.g. {who} in a rock";
            }
            // Stuck: wanting to go, and not a quarter metre on.
            bool wants = c.Desired.Length() > 0.3f && !c.Attacking && c.State != CState.Tell;
            if (!wants || (c.Position - k.Anchor).Length() > 0.25f) { k.Anchor = c.Position; k.Still = 0f; }
            else
            {
                k.Still += PathDt;
                if (k.Still >= 1f) t.Stuck += PathDt;
                if (k.Still > t.Longest) { t.Longest = k.Still; t.LongestAt = $"; e.g. {who}, wanting {c.Desired.Length():F1} m/s, {c.Species.Role}, way {c.PathIndex}/{c.Path.Count}, goal {(c.HasGoal ? $"{c.Goal.X:F1},{c.Goal.Y:F1},{c.Goal.Z:F1}" : "none")}, put back on {(c.Behind - c.Position).Length():F1} m off, stuck {c.StuckFor:F1} s, {k.Jumps} jumps, crawling {c.CrawlSpeed:F2}, up {c.Facing.Y.X:F1},{c.Facing.Y.Y:F1},{c.Facing.Y.Z:F1}, next {(c.PathIndex < c.Path.Count ? (c.Path[c.PathIndex] - Crawl.Center(c)).Length() : -1f):F1} m off"; }
            }
            // A flight: from where what it ran from was as it set off, by the way round, at the start and at its end (six seconds on at most; at least 1.5 s).
            bool fleeing = c.State == CState.Flee;
            if (fleeing && k.FleeAt < 0f)
            {
                k.FleeAt = sim.Time;
                k.Threat = Threat(sim, c, you);
                k.From = distance(c, k.Threat);
                k.HadSpot = false;
            }
            if (fleeing && k.FleeAt >= 0f && (c.HasAway && sim.Time - c.AwayAt < 6f || c.Herd is { Alarmed: true })) k.HadSpot = true;
            if (k.FleeAt == float.MaxValue) { if (!fleeing) k.FleeAt = -1f; } // (one count a flight)
            else if (k.FleeAt >= 0f && (!fleeing || sim.Time - k.FleeAt >= 6f))
            {
                if (sim.Time - k.FleeAt < 1.5f || float.IsPositiveInfinity(k.From)) { }
                // (Cornered, with nowhere it can reach farther from it, a flight is no flight: it stands its ground.)
                else if (!k.HadSpot) { if (c.Species.FleesIntoCracks) t.Cracks++; else t.Cornered++; }
                else
                {
                    t.Flees++;
                    float now = distance(c, k.Threat);
                    if (now > k.From + 0.5f) t.Farther++;
                    else if (t.NearerAt == "") t.NearerAt = $"; e.g. {who}: {k.From:F1} m by the way round from it at the start, {now:F1} m after {sim.Time - k.FleeAt:F1} s; spot {(c.HasAway ? $"{(c.AwayGoal - c.Position).Length():F1} m off" : "none")}, way {c.PathIndex}/{c.Path.Count}, wanting {c.Desired.Length():F1} m/s, {k.Jumps} jumps";
                }
                k.FleeAt = fleeing ? float.MaxValue : -1f;
            }
            k.Steps++;
            (k.WasIn, k.PrevPhase, k.PrevState, k.PrevAt) = (Interpenetration.Inside(sim, c), c.Phase, c.State, c.Position);
            k.PrevGap = c.Space?.Parts is { } gp && !c.Species.Climbs ? gp.Gap(c.Position, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species)) : 9f;
        }
    }

    /// <summary>What a fleeing creature runs from: what its flight was picked from, its herd's fright, a fire, a hunter after it, else the intruder.</summary>
    static Vector3 Threat(WildSim sim, Creature c, DummyTarget you)
    {
        if (c.HasAway && sim.Time - c.AwayAt < 0.5f) return c.AwayFrom;
        if (c.Herd is { Alarmed: true } herd) return herd.ThreatPos;
        if (c.Space == null && sim.Time < c.ScaredUntil) return c.ScaredFrom;
        foreach (Creature h in sim.Creatures)
            if (h != c && !h.Dead && !h.Removed && (h.Prey == c || h.AttackTarget == c.AsTarget) && (h.Position - c.Position).Length() < 6f) return h.Position;
        return you.Position;
    }

    /// <summary>A walkable floor point in a room for a body, if it has one.</summary>
    static Vector3? Floor(ISolidSpace space, PoiRoom r, SpaceNav.Body body, Wildlife.Rng rng)
    {
        for (int k = 0; k < 12; k++)
        {
            var p = new Vector3(rng.Range(r.Min.X + 0.6f, r.Max.X - 0.6f), r.Min.Y + 0.5f, rng.Range(r.Min.Z + 0.6f, r.Max.Z - 0.6f));
            if (SpaceNav.Nearest(space, p, body, 2) is { } cell) return SpaceNav.Point(space, cell, body) - (body.Climbs ? Vector3.Zero : Vector3.Up * 0.5f);
        }
        return null;
    }

    /// <summary>How far a creature in a place is from a point by the way round (its own body's walkable cells), m; infinite where the point can't be reached.</summary>
    static float Through(ISolidSpace space, Creature c, Vector3 from)
    {
        SpaceNav.Body body = SpaceNav.BodyOf(c.Species);
        Vector3I? a = SpaceNav.Nearest(space, from + Vector3.Up * 0.5f, body, 3), end = SpaceNav.Nearest(space, Crawl.Center(c), body, 2);
        if (a is not { } start || end == null) return float.PositiveInfinity;
        var dist = new Dictionary<Vector3I, float> { [start] = 0f };
        var open = new PriorityQueue<Vector3I, float>();
        open.Enqueue(start, 0f);
        while (open.Count > 0 && dist.Count < 20000)
        {
            Vector3I cur = open.Dequeue();
            if (cur == end.Value) return dist[cur];
            float g = dist[cur];
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0 || !body.Climbs && dy != 0 && Math.Abs(dx) + Math.Abs(dz) != 1) continue;
                var n = new Vector3I(cur.X + dx, cur.Y + dy, cur.Z + dz);
                if (!SpaceNav.Walkable(space, n, body)) continue;
                float ng = g + MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                if (dist.TryGetValue(n, out float old) && old <= ng) continue;
                dist[n] = ng;
                open.Enqueue(n, ng);
            }
        }
        return float.PositiveInfinity;
    }

    /// <summary>
    /// A way over open ground by 1 m cells round what <paramref name="blocked"/> says (walls, rocks), within a reach of the start:
    /// its length (null where there is none), and its points into <paramref name="path"/> if given.
    /// </summary>
    static float? FlatRoute(Func<int, int, bool> blocked, Vector3 from, Vector3 to, float reach, List<Vector3> path, Func<int, int, int, int, bool> steps = null)
    {
        var a = ((int)MathF.Floor(from.X), (int)MathF.Floor(from.Z));
        var b = ((int)MathF.Floor(to.X), (int)MathF.Floor(to.Z));
        var dist = new Dictionary<(int, int), float> { [a] = 0f };
        var came = new Dictionary<(int, int), (int, int)>();
        var open = new PriorityQueue<(int X, int Z), float>();
        open.Enqueue(a, 0f);
        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            if (cur == b)
            {
                if (path != null)
                {
                    for (var k = cur; k != a; k = came[k]) path.Add(new Vector3(k.Item1 + 0.5f, 0f, k.Item2 + 0.5f));
                    path.Reverse();
                }
                return dist[cur];
            }
            float g = dist[cur];
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                var n = (cur.X + dx, cur.Z + dz);
                if (MathF.Abs(n.Item1 - a.Item1) > reach || MathF.Abs(n.Item2 - a.Item2) > reach || blocked(n.Item1, n.Item2) || steps != null && !steps(cur.X, cur.Z, n.Item1, n.Item2)) continue;
                if (dx != 0 && dz != 0 && (blocked(cur.X + dx, cur.Z) || blocked(cur.X, cur.Z + dz))) continue;
                float ng = g + (dx != 0 && dz != 0 ? 1.4142f : 1f);
                if (dist.TryGetValue(n, out float old) && old <= ng) continue;
                dist[n] = ng;
                came[n] = cur;
                open.Enqueue(n, ng + MathF.Sqrt((n.Item1 - b.Item1) * (n.Item1 - b.Item1) + (n.Item2 - b.Item2) * (n.Item2 - b.Item2)));
            }
        }
        return null;
    }
}
