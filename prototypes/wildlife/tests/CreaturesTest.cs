using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using TD = TerrainDigging.Terrain;

namespace Wildlife;

/// <summary>
/// Headless test of day and night, cave crawlers, the fauna of places (POIs), their food chains,
/// tells and nests. Run with:
///   godot --headless --path prototypes/wildlife res://tests/CreaturesTest.tscn
/// Exits 0 if every check passes. Everything drives the pure simulation; caves are real voxel
/// terrain (terrain-digging's store: a tunnel dug with the dig tool, and one of its generator
/// caves) and places are 1 m block grids. Deterministic by seed; timings take the best of three.
/// </summary>
public partial class CreaturesTest : Node
{
    const float Dt = 1f / 60f;
    WildlifeData _data;
    FaunaGenerator _gen;
    int _failures, _checks;

    public override void _Ready() => Run();

    /// <summary>--probe=name runs one scenario with a trace (for debugging by hand).</summary>
    string _probe = "";
    bool _trace;

    void Trace(string line)
    {
        if (_trace) GD.Print("    " + line);
    }

    void Run()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--probe=")) _probe = arg["--probe=".Length..];
        try
        {
            _data = DataFiles.LoadWildlife();
            _gen = new FaunaGenerator(_data.Fauna, _data.Activity);
            if (_probe != "")
            {
                _trace = true;
                GetType().GetMethod(_probe, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(this, null);
                GD.Print(_failures == 0 ? $"ALL PASSED ({_checks} checks)" : $"{_failures} OF {_checks} CHECK(S) FAILED");
                Shutdown.Quit(this, _failures == 0 ? 0 : 1);
                return;
            }
            ActivityData();
            DayAndNight();
            SleepersWake();
            RestSitesAreSensible();
            DeepSpecies();
            CrawlerInDugTunnel();
            CrawlerInGeneratorCave();
            CaveRun();
            PlacePopulations();
            DepthMakesItWorse();
            AmbushesAreTold();
            WalkThroughThePlace();
            FoodChain();
            WreckNesters();
            DeepPerformance();
        }
        catch (Exception e)
        {
            Fail($"crashed: {e}");
        }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_checks} checks)" : $"{_failures} OF {_checks} CHECK(S) FAILED");
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    // ---- Day and night --------------------------------------------------------------------

    void ActivityData()
    {
        int planets = 200, withDay = 0, withNightHunter = 0, herdByDay = 0;
        var counts = new Dictionary<Activity, int>();
        var rests = new Dictionary<RestKind, int>();
        bool hunterRests = true, grazerRests = true;
        for (int i = 0; i < planets; i++)
        {
            PlanetFauna f = _gen.Generate((ulong)(7000 + i), i / (float)planets, i % 5 == 0);
            if (f.Species.Exists(s => s.Activity == Activity.Diurnal)) withDay++;
            if (f.Species.Exists(s => s.IsHunter && s.Activity == Activity.Nocturnal)) withNightHunter++;
            if (f.Species[0].Activity is Activity.Diurnal or Activity.Crepuscular) herdByDay++;
            foreach (Species s in f.Species)
            {
                counts[s.Activity] = counts.GetValueOrDefault(s.Activity) + 1;
                rests[s.RestKind] = rests.GetValueOrDefault(s.RestKind) + 1;
                if (s.Behaviour is Behaviour.Predator or Behaviour.PackHunter && s.Rig == RigKind.Legged && s.RestKind != RestKind.Den) hunterRests = false;
                if (s.Behaviour == Behaviour.Herd && s.Rig == RigKind.Legged && s.RestKind != RestKind.Nest) grazerRests = false;
            }
        }
        string mix = string.Join(", ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key.ToString().ToLowerInvariant()} {k.Value}"));
        Check(withDay == planets && withNightHunter == planets && herdByDay == planets,
            $"every planet has animals out by day and a hunter out by night, its herd grazing by day ({planets} planets: {withDay} with day animals, {withNightHunter} with a night hunter, {herdByDay} herds by day; {mix})");
        Check(hunterRests && grazerRests && rests.GetValueOrDefault(RestKind.Roost) > 0 && rests.GetValueOrDefault(RestKind.Burrow) > 0,
            $"rest sites suit the body: walking hunters den, herds bed down, fliers roost, crawlers burrow ({string.Join(", ", rests.OrderBy(k => k.Key).Select(k => $"{k.Key.ToString().ToLowerInvariant()} {k.Value}"))})");
        PlanetFauna again = _gen.Generate(7003, 0.015f, false);
        PlanetFauna once = _gen.Generate(7003, 0.015f, false);
        Check(again.Species.Select(s => s.Activity + "/" + s.RestKind).SequenceEqual(once.Species.Select(s => s.Activity + "/" + s.RestKind)),
            "activity cycles and rest sites are the same on every run of the same seed");
    }

    (WildSim sim, Spawner spawner, PlanetFauna fauna) Valley(ulong seed, float alienness)
    {
        var terrain = new Terrain(1977);
        var nav = new NavGrid(terrain.HalfSize, 3f);
        var rng = Rng.For(seed, "rocks");
        for (int i = 0; i < 50; i++) nav.AddObstacle(new Vector3(rng.Range(-200f, 200f), 0f, rng.Range(-200f, 200f)), rng.Range(1.5f, 4f));
        var sim = new WildSim(_data, terrain, nav) { ActivityCycles = true };
        var groves = new List<Vector3>();
        for (int i = 0; i < 8; i++) groves.Add(new Vector3(rng.Range(-150f, 150f), 0f, rng.Range(-150f, 150f)));
        sim.Groves.AddRange(groves);
        PlanetFauna fauna = _gen.Generate(seed, alienness, false);
        var spawner = new Spawner(sim, fauna, groves, seed);
        spawner.Populate();
        return (sim, spawner, fauna);
    }

    static bool Out(Creature c) => !Brain.IsResting(c) && c.State != CState.ReturnHome;

    void DayAndNight()
    {
        var (sim, _, fauna) = Valley(4242, 0.4f);
        HashSet<Species> RunAt(float time, float seconds, out int resting, out int atSite, out float worst)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                sim.Clock.Time01 = time;
                sim.Step(Dt);
            }
            var active = new HashSet<Species>();
            resting = 0;
            atSite = 0;
            worst = 0f;
            foreach (Creature c in sim.Creatures)
            {
                if (c.Removed || c.Dead) continue;
                if (Out(c)) active.Add(c.Species);
                if (c.State == CState.Rest && c.Rest != null)
                {
                    resting++;
                    float d = WildSim.Flat(c.Position - c.Rest.Position).Length();
                    worst = MathF.Max(worst, d - c.Rest.Radius);
                    if (d <= c.Rest.Radius + 2.5f) atSite++;
                }
            }
            return active;
        }
        HashSet<Species> noon = RunAt(0.5f, 150f, out int restNoon, out int siteNoon, out float worstNoon);
        HashSet<Species> night = RunAt(0.0f, 150f, out int restNight, out int siteNight, out float worstNight);
        string Names(IEnumerable<Species> set) => string.Join(", ", set.Select(s => $"{s.CommonName} ({s.Activity.ToString().ToLowerInvariant()})"));
        Species herd = fauna.Species[0];
        Species nightHunter = fauna.Species.Find(s => s.IsHunter && s.Activity == Activity.Nocturnal);
        GD.Print($"  noon, out: {Names(noon)}");
        GD.Print($"  midnight, out: {Names(night)}");
        Check(!noon.SetEquals(night) && noon.Contains(herd) && !night.Contains(herd) && nightHunter != null && night.Contains(nightHunter) && !noon.Contains(nightHunter),
            $"the same valley at noon and at midnight has different animals out ({noon.Count} species at noon, {night.Count} at midnight; the {herd.CommonName} grazes by day, the {nightHunter?.CommonName} hunts by night)");
        Check(restNoon > 0 && restNight > 0 && siteNoon == restNoon && siteNight == restNight,
            $"resting animals are at their rest sites ({restNoon} resting at noon, {restNight} at midnight; furthest {MathF.Max(worstNoon, worstNight):F1} m past its site's edge)");
        var kinds = sim.Creatures.Where(c => c.State == CState.Rest).Select(c => c.Rest.Kind).Distinct().OrderBy(k => k).ToList();
        Check(kinds.Count >= 2, $"they rest in their own kinds of site ({string.Join(", ", kinds.Select(k => k.ToString().ToLowerInvariant()))})");
    }

    void SleepersWake()
    {
        var (sim, _, fauna) = Valley(4242, 0.4f);
        for (float t = 0f; t < 150f; t += Dt)
        {
            sim.Clock.Time01 = 0.0f;
            sim.Step(Dt);
        }
        Species herd = fauna.Species[0];
        Creature sleeper = sim.Creatures.First(c => c.Species == herd && c.State == CState.Rest);
        // Creep up crouched, then stand: crouching gets you closer before it wakes.
        float WakeDistance(float threat)
        {
            var (s2, _, _) = Valley(4242, 0.4f);
            for (float t = 0f; t < 150f; t += Dt)
            {
                s2.Clock.Time01 = 0.0f;
                s2.Step(Dt);
            }
            Creature c = s2.Creatures.First(x => x.Id == sleeper.Id);
            Vector3 dir = new Vector3(1f, 0f, 0.3f).Normalized();
            var walker = new DummyTarget { Name = "Walker", ThreatScale = threat, Position = c.Position + dir * 30f };
            s2.Targets.Add(walker);
            for (float d = 30f; d > 0.5f; d -= 1.2f * Dt)
            {
                walker.Position = c.Position + dir * d;
                s2.Clock.Time01 = 0.0f;
                s2.Step(Dt);
                if (c.State != CState.Rest) return d;
            }
            return 0f;
        }
        float standing = WakeDistance(1f), crouched = WakeDistance(0.6f);
        var (s3, _, _) = Valley(4242, 0.4f);
        for (float t = 0f; t < 150f; t += Dt)
        {
            s3.Clock.Time01 = 0.0f;
            s3.Step(Dt);
        }
        Creature one = s3.Creatures.First(x => x.Id == sleeper.Id);
        var walker2 = new DummyTarget { Position = one.Position + new Vector3(3f, 0f, 0f) };
        s3.Targets.Add(walker2);
        for (float t = 0f; t < 4f; t += Dt)
        {
            s3.Clock.Time01 = 0.0f;
            s3.Step(Dt);
        }
        int awake = one.Herd.Members.Count(m => m.Alive && m.State != CState.Rest);
        Check(standing > crouched && crouched > 0f && awake == one.Herd.AliveCount && one.Herd.Alarmed,
            $"a sleeping herd wakes when you come close (at {standing:F1} m walking, {crouched:F1} m crouched) and the whole herd gets up and runs ({awake} of {one.Herd.AliveCount})");
    }

    void RestSitesAreSensible()
    {
        var (sim, _, _) = Valley(4242, 0.4f);
        var rng = new Rng(5);
        float Avg(RestKind kind, Func<Vector3, float> measure, bool sites)
        {
            var list = sites ? sim.RestSites.Where(r => r.Kind == kind).Select(r => r.Position).ToList() : Enumerable.Range(0, 400).Select(_ => new Vector3(rng.Range(-200f, 200f), 0f, rng.Range(-200f, 200f))).ToList();
            return list.Count == 0 ? float.NaN : list.Average(measure);
        }
        float Slope(Vector3 p) => 1f - sim.Terrain.Normal(p.X, p.Z).Y;
        float denSlope = Avg(RestKind.Den, Slope, true), anySlope = Avg(RestKind.Den, Slope, false);
        float nestSlope = Avg(RestKind.Nest, Slope, true);
        bool clear = sim.RestSites.All(r => !sim.InBaseZone(r.Position) && !sim.Nav.Blocked(r.Position));
        Check((float.IsNaN(denSlope) || denSlope > anySlope) && (float.IsNaN(nestSlope) || nestSlope < anySlope) && clear,
            $"dens are dug into slopes (slope {denSlope:F3} against {anySlope:F3} on average ground), bedding grounds lie flat ({nestSlope:F3}), none in rock or a base's zone ({sim.RestSites.Count} sites)");
    }

    // ---- The deep fauna -------------------------------------------------------------------

    void DeepSpecies()
    {
        int species = 0, problems = 0, blindDeep = 0, deepCount = 0, armoured = 0, armouredOf = 0, glowAlien = 0, alienOf = 0, warnSafe = 0;
        float paleSat = 0f, topSat = 0f;
        int paleN = 0, topN = 0;
        var firstProblem = "";
        for (int planet = 0; planet < 40; planet++)
        {
            var fauna = new DeepFauna(_data, (ulong)(900 + planet), planet / 40f);
            foreach (DeepRole role in Enum.GetValues<DeepRole>())
            {
                if (role == DeepRole.None) continue;
                foreach (PoiTheme theme in Enum.GetValues<PoiTheme>())
                for (int depth = 0; depth <= 4; depth += 2)
                {
                    Species s = fauna.For(role, theme, depth, planet % 7 == 0);
                    species++;
                    var p = FaunaQuality.Problems(_data.Fauna, s);
                    if (p.Count > 0)
                    {
                        problems++;
                        if (firstProblem == "") firstProblem = $"{s}: {string.Join("; ", p)}";
                    }
                    s.Colors.Base.ToHsv(out _, out float sat, out _);
                    if (depth >= _data.Deep.Depth.BlindFrom) { deepCount++; if (s.Blind) blindDeep++; }
                    if (depth >= _data.Deep.Depth.PaleFrom && !s.Corrupted) { paleSat += sat; paleN++; }
                    if (depth == 0 && !s.Corrupted) { topSat += sat; topN++; }
                    if (theme is PoiTheme.Industrial or PoiTheme.Derelict && !s.Corrupted) { armouredOf++; if (s.Part("back") is "plates" or "shell" || s.Form == "scrapper") armoured++; }
                    if (theme == PoiTheme.Alien && depth == 0 && !s.Corrupted) { alienOf++; if (s.Colors.GlowStrength > 0f) glowAlien++; }
                    if (role == DeepRole.Subsister && s.Colors.Pattern != Pattern.Bands && !s.Corrupted) warnSafe++;
                }
            }
        }
        Check(problems == 0, $"all {species} cave and place species pass every part, form, behaviour and proportion rule{(problems > 0 ? $" ({problems} fail; first: {firstProblem})" : "")}");
        Check(blindDeep == deepCount && paleSat / paleN < topSat / topN * 0.75f,
            $"deep down they go pale and then blind (saturation {paleSat / paleN:F2} below level {_data.Deep.Depth.PaleFrom} against {topSat / topN:F2} near the way in; {blindDeep} of {deepCount} below level {_data.Deep.Depth.BlindFrom} blind)");
        Check(armoured >= armouredOf * 0.6f && glowAlien >= alienOf * 0.5f,
            $"industrial places and wrecks breed armour ({armoured} of {armouredOf} plated, shelled or scrappers), alien places glow ({glowAlien} of {alienOf})");
        var one = new DeepFauna(_data, 77, 0.5f);
        var two = new DeepFauna(_data, 77, 0.5f);
        Check(one.For(DeepRole.Lurker, PoiTheme.Industrial, 1).PartSignature == two.For(DeepRole.Lurker, PoiTheme.Industrial, 1).PartSignature
              && one.For(DeepRole.Lurker, PoiTheme.Industrial, 1).Name == two.For(DeepRole.Lurker, PoiTheme.Industrial, 1).Name,
            "the same planet always has the same creatures in its places");
        int subs = 0, subsOf = 0;
        foreach (PoiTheme theme in Enum.GetValues<PoiTheme>())
        {
            Species s = one.For(DeepRole.Subsister, theme, 0);
            subsOf++;
            if (s.Stats.Damage <= 0f && s.FleesIntoCracks && s.Colors.Pattern != Pattern.Bands) subs++;
        }
        Check(subs == subsOf && warnSafe > 0, $"subsisters are harmless, run for cracks and wear no warning colours ({subs} of {subsOf} themes)");
    }

    // ---- Crawlers on real voxel rock -------------------------------------------------------

    static TD.ChunkStore NewStore() => new(new TD.SimpleTerrainSource(1977));

    sealed class Watch
    {
        public float WorstInside = float.MinValue, WorstDetach, WorstSegment = float.MinValue;
        public int Steps, Detached;
        public bool Floor, Wall, Ceiling, Finite = true;
        public string Order = "";

        public Action<string> Trace;
        /// <summary>How far inside the solid a point is: the voxel density, or for blocks, depth into a solid cell (else negative).</summary>
        public Func<Vector3, float> Inside;
        readonly Queue<string> _recent = new();
        bool _told;

        public void Look(Creature c, ISolidSpace s)
        {
            if (c.InWalls || c.Removed) return;
            Steps++;
            Vector3 center = Crawl.Center(c);
            if (!Geo.Finite(center)) Finite = false;
            float inside = Inside != null ? Inside(center) : s.Density(center);
            if (Trace != null)
            {
                _recent.Enqueue($"pos {c.Position} centre {center} n {c.Facing.Y} falling {c.Falling} phase {c.Phase} state {c.State} vel {c.Velocity} desired {c.Desired}");
                if (_recent.Count > 8) _recent.Dequeue();
                if (inside > 0.05f && !_told)
                {
                    _told = true;
                    Trace($"{c.Species.CommonName} body inside ({inside:F2}); last frames:");
                    foreach (string line in _recent) Trace("  " + line);
                }
            }
            WorstInside = MathF.Max(WorstInside, inside);
            if (c.Rig is CrawlRig rig)
                for (int i = 0; i < rig.SegmentCenters.Count; i++)
                {
                    float d = Inside != null ? Inside(rig.SegmentCenters[i]) : s.Density(rig.SegmentCenters[i]);
                    if (d > 0.02f && d > WorstSegment) Trace?.Invoke($"segment {i} of {rig.SegmentCenters.Count} inside ({d:F2}) at {rig.SegmentCenters[i]}, centre {center}, n {c.Facing.Y}, falling {c.Falling}, phase {c.Phase}, state {c.State}");
                    WorstSegment = MathF.Max(WorstSegment, d);
                }
            if (!c.Falling && c.Phase != AttackPhase.Lunge)
            {
                float d = SpaceMath.Distance(s, center, out _);
                float err = MathF.Abs(d + c.Clearance);
                WorstDetach = MathF.Max(WorstDetach, err);
                if (err > 0.2f) Detached++;
                float ny = c.Facing.Y.Y;
                void Mark(ref bool flag, string name)
                {
                    if (flag) return;
                    flag = true;
                    Order += (Order == "" ? "" : " > ") + name;
                }
                if (ny > 0.8f) Mark(ref Floor, "floor");
                else if (MathF.Abs(ny) < 0.35f) Mark(ref Wall, "wall");
                else if (ny < -0.8f) Mark(ref Ceiling, "ceiling");
            }
        }
    }

    Species Creeper(ulong planet)
    {
        for (ulong s = planet; s < planet + 60; s++)
        {
            Species c = new DeepFauna(_data, s, 0.5f).For(DeepRole.Crawler, PoiTheme.Natural, 1);
            if (c.Form == "creeper") return c;
        }
        throw new InvalidOperationException("no creeper in 60 planets");
    }

    /// <summary>Drives a crawler towards points in turn (no brain): floor, then a wall, then the ceiling.</summary>
    void CrawlTo(WildSim sim, Creature c, Watch watch, float seconds, params Vector3[] points)
    {
        int i = 0;
        for (float t = 0f; t < seconds && i < points.Length; t += Dt)
        {
            Vector3 center = Crawl.Center(c);
            Vector3 to = points[i] - center;
            if (to.Length() < 0.7f)
            {
                i++;
                continue;
            }
            sim.RefillPathBudget();
            Brain.Toward(sim, c, points[i], c.Stats.WalkSpeed);
            Locomotion.Move(sim, c, Dt);
            c.Rig.Update(sim, c, Dt);
            sim.Time += Dt;
            watch.Look(c, c.Space);
            if (_trace && (int)(t / Dt) % 30 == 0)
            {
                Vector3 cc = Crawl.Center(c);
                float dd = SpaceMath.Distance(c.Space, cc, out Vector3 nn);
                Trace($"t {t:F1} wp {i} center {cc} n {c.Facing.Y} speed {c.CrawlSpeed:F2} dist {dd:F2} falling {c.Falling} density {c.Space.Density(cc):F2} to-wp {to.Length():F2}");
            }
        }
    }

    /// <summary>The whole climb, the drop and the chase, on one bit of rock.</summary>
    void ClimbDropReach(string where, ISolidSpace space, Vector3 floor, Vector3 wall, Vector3 ceiling, Vector3 farTarget, Vector3 ceilingUp)
    {
        var sim = new WildSim(_data, new Terrain(5, 40f), new NavGrid(40f, 3f));
        Species sp = Creeper(31);
        Creature c = sim.SpawnIn(sp, space, floor);
        var watch = new Watch { Trace = Trace };
        watch.Look(c, space);
        CrawlTo(sim, c, watch, 40f, wall, ceiling);
        bool onCeiling = c.Facing.Y.Y < -0.6f;
        float ceilingAt = Crawl.Center(c).DistanceTo(ceiling);

        // Something walks under it: dust falls, then it drops.
        c.Ambush = new AmbushPoint { Kind = HideKind.Ceiling, Position = Crawl.Center(c), Normal = c.Facing.Y };
        Vector3 under = Crawl.Center(c);
        float floorY = SpaceMath.FloorBelow(space, under.X, under.Y - 0.3f, under.Z, 8f) ?? floor.Y;
        var prey = new DummyTarget { Name = "Walker", Position = new Vector3(under.X, floorY, under.Z), Health = new Health(1e6f) };
        sim.Targets.Add(prey);
        float toldAt = -1f, hitAt = -1f;
        TellKind told = TellKind.Growl;
        sim.Told += e => { if (e.Creature == c && toldAt < 0f) { toldAt = e.Time; told = e.Kind; } };
        sim.Attacked += e => { if (e.Creature == c && e.Hit && hitAt < 0f) hitAt = e.Time; };
        for (float t = 0f; t < 12f && hitAt < 0f; t += Dt)
        {
            sim.Step(Dt);
            watch.Look(c, space);
        }
        float landedUp = -2f;
        for (float t = 0f; t < 3f; t += Dt)
        {
            sim.Step(Dt);
            watch.Look(c, space);
            if (landedUp < -1f && hitAt >= 0f && !c.Falling && c.Phase != AttackPhase.Lunge) landedUp = c.Facing.Y.Y;
        }
        Trace($"landed with its surface facing {landedUp:F2} up; now falling {c.Falling}, n {c.Facing.Y}, state {c.State}");
        bool landed = landedUp > 0.6f;

        // Then it goes after the walker as it moves off along the rock.
        prey.Position = farTarget;
        c.Noticed = prey;
        c.NoticedAt = sim.Time;
        float reachedAt = -1f, start = sim.Time;
        for (float t = 0f; t < 40f && reachedAt < 0f; t += Dt)
        {
            sim.Step(Dt);
            watch.Look(c, space);
            c.NoticedAt = sim.Time; // it keeps it in sight
            if ((c.Position - prey.Position).Length() < c.Stats.AttackRange + 0.6f) reachedAt = sim.Time - start;
        }
        Check(watch.Floor && watch.Wall && watch.Ceiling && onCeiling,
            $"{where}: a {sp.CommonName} crawls {watch.Order} ({ceilingAt:F1} m from the ceiling point it was heading for)");
        Check(toldAt >= 0f && hitAt - toldAt >= 1f && landed,
            $"{where}: it drops onto a walker below, giving itself away first ({told.ToString().ToLowerInvariant()} {hitAt - toldAt:F2} s before the hit), and lands on its feet");
        Check(watch.Detached == 0 && watch.WorstInside < 0f && watch.WorstSegment < 0.02f && sim.Rescues == 0 && watch.Finite,
            $"{where}: it stays attached (worst {watch.WorstDetach:F3} m off its surface, {watch.Detached} slips) and never inside the rock (worst body density {watch.WorstInside:F2}, segments {watch.WorstSegment:F2}; {sim.Rescues} rescues, {watch.Steps} steps)");
        Check(reachedAt >= 0f, $"{where}: it reaches a target {farTarget.DistanceTo(floor):F0} m along the rock ({reachedAt:F1} s)");
    }

    void TunnelSection()
    {
        TD.ChunkStore store = NewStore();
        var src = (TD.SimpleTerrainSource)store.Source;
        float y = float.MaxValue;
        for (int z = 40; z <= 72; z++) y = MathF.Min(y, src.SurfaceHeight(0f, z));
        int ty = (int)MathF.Floor(y - 9f);
        for (int z = 40; z <= 70; z++)
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, 2), (-1, 2), (1, 2) })
            store.Dig(new TD.Int3(dx, ty + dy, z), 2f);
        var space = new VoxelSpace(store);
        GD.Print($"ty {ty}");
        for (int yy = ty + 6; yy >= ty - 4; yy--)
        {
            var line = new System.Text.StringBuilder($"{yy,4} ");
            for (int x = -5; x <= 5; x++) line.Append(store.IsSolid(new TD.Int3(x, yy, 50)) ? '#' : '.');
            line.Append("   ");
            for (int x = -4; x <= 4; x++) line.Append($"{space.Density(new Vector3(x + 0.5f, yy + 0.5f, 50.5f)),6:F2}");
            GD.Print(line.ToString());
        }
        var path = new List<Vector3>();
        bool ok = SpaceNav.FindPath(space, new Vector3(2.3f, 8f, 49.9f), new Vector3(0.5f, ty + 3.25f, 50f), true, 1, path);
        GD.Print($"path ok {ok}: {string.Join(" ", path.Select(p => $"({p.X:F1},{p.Y:F1},{p.Z:F1})"))}");
        Vector3 c0 = new(2.3f, 7.99f, 49.9f);
        for (float dx = -0.4f; dx <= 0.41f; dx += 0.2f)
        for (float dy = -0.4f; dy <= 0.41f; dy += 0.2f)
        {
            Vector3 q = c0 + new Vector3(dx, dy, 0f);
            float d = SpaceMath.Distance(space, q, out Vector3 n);
            GD.Print($"  at ({q.X:F1},{q.Y:F1}) density {space.Density(q):F2} dist {d:F2} n {n}");
        }
    }

    void CrawlerInDugTunnel()
    {
        TD.ChunkStore store = NewStore();
        var src = (TD.SimpleTerrainSource)store.Source;
        // A tunnel dug with the survival tool, the way a player digs one: balls of rock 2 m
        // round, side by side and one above the other, 30 m into the hillside, 7 m under it.
        float y = float.MaxValue;
        for (int z = 40; z <= 72; z++) y = MathF.Min(y, src.SurfaceHeight(0f, z));
        int ty = (int)MathF.Floor(y - 9f);
        int removed = 0;
        for (int z = 40; z <= 70; z++)
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, 2), (-1, 2), (1, 2) })
            removed += store.Dig(new TD.Int3(dx, ty + dy, z), 2f).Removed.Count;
        var space = new VoxelSpace(store);
        Vector3 axis = new(0.5f, ty + 1.5f, 46f);
        float floorY = SpaceMath.FloorBelow(space, axis.X, axis.Y, axis.Z, 4f) ?? axis.Y - 2f;
        float ceilY = axis.Y + SpaceMath.Ray(space, axis, Vector3.Up, 4f);
        float wallX = axis.X + SpaceMath.Ray(space, axis, Vector3.Right, 4f);
        Check(removed > 300 && ceilY - floorY > 2.5f, $"a tunnel dug with the tool ({removed} cells, {ceilY - floorY:F1} m floor to ceiling)");
        ClimbDropReach("dug tunnel", space,
            floor: new Vector3(axis.X, floorY + 0.3f, 44f),
            wall: new Vector3(wallX - 0.3f, axis.Y, 47f),
            ceiling: new Vector3(axis.X, ceilY - 0.25f, 50f),
            farTarget: new Vector3(axis.X, floorY, 62f),
            ceilingUp: Vector3.Down);
    }

    /// <summary>The roomiest spot in the generator's first cave: open floor, high ceiling.</summary>
    static (Vector3 floor, float height) CaveChamber(TD.ChunkStore store, ISolidSpace space)
    {
        var src = (TD.SimpleTerrainSource)store.Source;
        Vector3 best = Vector3.Zero;
        float bestScore = -1f, bestH = 0f;
        for (int x = -90; x <= -30; x++)
        for (int z = -5; z <= 55; z++)
        {
            int top = (int)MathF.Floor(src.SurfaceHeight(x + 0.5f, z + 0.5f));
            for (int y = top - 18; y < top - 3; y++)
            {
                if (store.IsSolid(new TD.Int3(x, y, z)) || !store.IsSolid(new TD.Int3(x, y - 1, z))) continue;
                int h = 0;
                while (h < 7 && !store.IsSolid(new TD.Int3(x, y + h, z))) h++;
                if (h < 4 || h >= 7) continue;
                int wide = 0;
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    if (!store.IsSolid(new TD.Int3(x + dx, y, z + dz)) && store.IsSolid(new TD.Int3(x + dx, y - 1, z + dz))) wide++;
                float score = wide + h * 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new Vector3(x + 0.5f, y, z + 0.5f);
                    bestH = h;
                }
            }
        }
        return (best, bestH);
    }

    void CrawlerInGeneratorCave()
    {
        TD.ChunkStore store = NewStore();
        var space = new VoxelSpace(store);
        var (floor, height) = CaveChamber(store, space);
        Check(height >= 4f && store.RemovedCount == 0, $"the generator's cave has a chamber {height:F0} m high at ({floor.X:F0}, {floor.Y:F0}, {floor.Z:F0}), with nothing dug");
        Vector3 mid = floor + Vector3.Up * 1.2f;
        // The nearest wall, and the ceiling over the middle of the chamber.
        Vector3 wallDir = Vector3.Right;
        float wallD = float.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            float a = i * MathF.Tau / 16f;
            var d = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            float r = SpaceMath.Ray(space, mid, d, 6f);
            if (r < wallD) { wallD = r; wallDir = d; }
        }
        float ceilD = SpaceMath.Ray(space, floor + Vector3.Up * 0.5f, Vector3.Up, 8f);
        Vector3 ceiling = floor + Vector3.Up * (0.5f + ceilD - 0.25f) - wallDir * 0.8f;
        // A floor point about ten metres off along the cave, that a walker could stand on.
        HashSet<Vector3I> reach = SpaceNav.Reach(space, floor + Vector3.Up * 0.5f, false, 2, 8000);
        Vector3 far = floor;
        float farD = 0f;
        foreach (Vector3I cell in reach)
        {
            Vector3 p = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            float d = (p - floor).Length();
            if (d > farD && d < 12f) { farD = d; far = p; }
        }
        ClimbDropReach("generator cave", space,
            floor: floor + Vector3.Up * 0.3f,
            wall: mid + wallDir * (wallD - 0.3f) + Vector3.Up * 0.6f,
            ceiling: ceiling,
            farTarget: far,
            ceilingUp: Vector3.Down);
    }

    /// <summary>A whole cave populated and left to run with a player walking through: nothing clips or falls out.</summary>
    void CaveRun()
    {
        TD.ChunkStore store = NewStore();
        var space = new VoxelSpace(store);
        var (floor, _) = CaveChamber(store, space);
        var sim = new WildSim(_data, new Terrain(5, 40f), new NavGrid(40f, 3f));
        var fauna = new DeepFauna(_data, 31, 0.5f);
        List<Creature> made = CaveFauna.Populate(sim, space, fauna, floor + Vector3.Up, 16f, 99, depth: 1, crawlers: 3, subsisters: 5);
        var walker = new DummyTarget { Name = "Caver", Position = floor, Health = new Health(1e6f) };
        sim.Targets.Add(walker);
        HashSet<Vector3I> reach = SpaceNav.Reach(space, floor + Vector3.Up * 0.5f, false, 2, 8000);
        var stops = reach.Where(c => (SpaceMath.Center(c) - floor).Length() < 14f).OrderBy(c => c.X * 7919 + c.Z * 31 + c.Y).Where((_, i) => i % 37 == 0).Select(c => new Vector3(c.X + 0.5f, c.Y, c.Z + 0.5f)).Take(8).ToList();
        var watches = made.ToDictionary(c => c, _ => new Watch());
        int stop = 0, hits = 0, told = 0;
        var firstTell = new Dictionary<Creature, float>();
        float worstLead = float.MaxValue;
        sim.Told += e => { if (e.Target == walker) { told++; firstTell.TryAdd(e.Creature, e.Time); } };
        sim.Attacked += e =>
        {
            if (e.Target != walker || !e.Hit) return;
            hits++;
            worstLead = MathF.Min(worstLead, firstTell.TryGetValue(e.Creature, out float at) ? e.Time - at : -1f);
        };
        for (float t = 0f; t < 180f; t += Dt)
        {
            if (stops.Count > 0)
            {
                Vector3 to = stops[stop % stops.Count] - walker.Position;
                if (to.Length() < 0.3f) stop++;
                else walker.Position += to.Normalized() * MathF.Min(to.Length(), 1.4f * Dt);
            }
            sim.Step(Dt);
            foreach (var (c, w) in watches) w.Look(c, space);
        }
        float worstInside = watches.Values.Max(w => w.WorstInside), worstSeg = watches.Values.Max(w => w.WorstSegment);
        int slips = watches.Values.Sum(w => w.Detached);
        bool finite = watches.Values.All(w => w.Finite);
        Check(worstInside < 0f && worstSeg < 0.02f && slips == 0 && sim.Rescues == 0 && finite,
            $"a populated cave runs 3 minutes with a caver walking through: {made.Count} creatures, none inside the rock (worst {worstInside:F2}, segments {worstSeg:F2}), {slips} slips, {sim.Rescues} put back");
        Check(hits == 0 || worstLead >= 1f, $"in the cave every hit on the caver came after a tell from that creature at least a second before ({hits} hits, {told} tells, tightest {(hits == 0 ? 0f : worstLead):F2} s)");
    }

    // ---- Places -----------------------------------------------------------------------------

    /// <summary>Random places of every kind and state, as the POI generator would describe them.</summary>
    static PoiPlan RandomPlan(Rng rng, ulong seed, PlaceState state, bool derelict)
    {
        var plan = new PoiPlan { Seed = seed, State = state, Derelict = derelict, Theme = (PoiTheme)rng.Int(0, 2) };
        int rooms = rng.Int(5, 14), maxDepth = rng.Int(0, 4);
        float y = 0f;
        for (int i = 0; i < rooms; i++)
        {
            RoomKind kind = i == 0 ? RoomKind.Entrance
                : derelict ? (RoomKind)new[] { (int)RoomKind.Hold, (int)RoomKind.Duct, (int)RoomKind.EngineRoom, (int)RoomKind.Corridor }[rng.Int(0, 3)]
                : (RoomKind)new[] { (int)RoomKind.Corridor, (int)RoomKind.Corridor, (int)RoomKind.Room, (int)RoomKind.Hall, (int)RoomKind.Vault, (int)RoomKind.Vent }[rng.Int(0, 5)];
            int depth = Math.Min(maxDepth, i * (maxDepth + 1) / rooms);
            var min = new Vector3(i * 12f, -depth * 4f, 0f);
            Vector3 size = kind == RoomKind.Corridor ? new Vector3(10f, 3f, 2f) : kind is RoomKind.Vent or RoomKind.Duct ? new Vector3(6f, 1f, 1f) : new Vector3(rng.Range(5f, 10f), 4f, rng.Range(5f, 10f));
            var room = new PoiRoom { Index = i, Kind = kind, Min = min, Max = min + size, Depth = depth, Dark = rng.Chance(0.6f) || depth > 0 };
            if (i > 0)
            {
                room.Links.Add(i - 1);
                plan.Rooms[i - 1].Links.Add(i);
            }
            if (kind is RoomKind.Room or RoomKind.Hall && rng.Chance(0.5f)) room.Vents.Add((new Vector3(min.X + 1f, min.Y + 3f, min.Z), Vector3.Back));
            if (kind is RoomKind.Room or RoomKind.Hall or RoomKind.Vault) room.Doors.Add((new Vector3(min.X, min.Y, min.Z + 1f), Vector3.Right));
            plan.Rooms.Add(room);
            y = min.Y;
        }
        return plan;
    }

    void PlacePopulations()
    {
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        var rng = new Rng(11);
        var silent = new Dictionary<PlaceState, int>();
        var total = new Dictionary<PlaceState, int>();
        var creatures = new Dictionary<PlaceState, int>();
        int nestsOk = 0, nestsAll = 0, chains = 0, chainsPossible = 0, derelictOk = 0, derelictAll = 0, roles = 0;
        var seenRoles = new HashSet<DeepRole>();
        for (int i = 0; i < 400; i++)
        {
            PlaceState state = (PlaceState)(i % 4);
            bool derelict = i % 9 == 0 && state != PlaceState.Active;
            PoiPlan plan = RandomPlan(rng, (ulong)(5000 + i), state, derelict);
            PoiPopulation pop = PoiFauna.Populate(plan, fauna, _data.Deep);
            total[state] = total.GetValueOrDefault(state) + 1;
            if (pop.Silent) silent[state] = silent.GetValueOrDefault(state) + 1;
            creatures[state] = creatures.GetValueOrDefault(state) + pop.Spawns.Count;
            int maxDepth = plan.Rooms.Max(r => r.Depth);
            foreach (PoiSpawn s in pop.Spawns)
            {
                seenRoles.Add(s.Role);
                if (s.Nest < 0) continue;
            }
            foreach (Nest n in pop.Nests)
            {
                nestsAll++;
                PoiRoom room = plan.Rooms.Find(r => n.Position.X >= r.Min.X - 0.01f && n.Position.X <= r.Max.X + 0.01f && n.Position.Z >= r.Min.Z - 0.01f && n.Position.Z <= r.Max.Z + 0.01f && MathF.Abs(n.Position.Y - r.Min.Y) < 0.1f);
                bool deepEnough = room != null && (room.Depth >= Math.Max(1, maxDepth) || derelict || state == PlaceState.Infested && room.Depth >= 1);
                if (deepEnough) nestsOk++;
            }
            bool hasPred = pop.Spawns.Any(s => s.Species.HuntsPrey), hasPrey = pop.Spawns.Any(s => s.Role == DeepRole.Subsister);
            if (hasPred && hasPrey) { chainsPossible++; if (pop.FoodChain.Count > 0) chains++; }
            if (derelict && !pop.Silent)
            {
                derelictAll++;
                if (pop.Spawns.Any(s => s.Role == DeepRole.DerelictNester) && pop.Nests.All(n => n.Derelict)) derelictOk++;
            }
        }
        float Share(PlaceState s) => silent.GetValueOrDefault(s) / (float)total[s];
        float Per(PlaceState s) => creatures.GetValueOrDefault(s) / (float)Math.Max(1, total[s] - silent.GetValueOrDefault(s));
        Check(Share(PlaceState.Abandoned) is > 0.1f and < 0.45f && Share(PlaceState.Ruined) is > 0.1f and < 0.5f && Share(PlaceState.Infested) == 0f && Share(PlaceState.Active) > 0.75f,
            $"some places are empty and silent: abandoned {Share(PlaceState.Abandoned):P0}, ruined {Share(PlaceState.Ruined):P0}, infested {Share(PlaceState.Infested):P0}, active {Share(PlaceState.Active):P0} (garrisons keep theirs clear)");
        Check(Per(PlaceState.Infested) > Per(PlaceState.Abandoned) * 1.6f && Per(PlaceState.Active) < Per(PlaceState.Abandoned) * 0.5f,
            $"infested places are overrun ({Per(PlaceState.Infested):F1} creatures a place against {Per(PlaceState.Abandoned):F1} in abandoned ones, {Per(PlaceState.Active):F1} in active ones)");
        Check(nestsAll > 20 && nestsOk == nestsAll, $"nests are in the deepest levels (or a wreck's holds, or an infested place's lower levels): {nestsOk} of {nestsAll}");
        Check(chainsPossible > 20 && chains == chainsPossible, $"every place with hunters and subsisters has a food chain ({chains} of {chainsPossible})");
        Check(derelictAll > 5 && derelictOk == derelictAll, $"derelict ships have their own nesters in holds, ducts and engine rooms ({derelictOk} of {derelictAll})");
        var expected = new[] { DeepRole.Lurker, DeepRole.Prowler, DeepRole.Ambusher, DeepRole.Nester, DeepRole.Subsister, DeepRole.DerelictNester };
        Check(expected.All(seenRoles.Contains), $"places get every role: {string.Join(", ", seenRoles.OrderBy(r => r).Select(r => r.ToString().ToLowerInvariant()))}");
        PoiPopulation a = PoiFauna.Populate(RandomPlan(new Rng(3), 77, PlaceState.Abandoned, false), fauna, _data.Deep);
        PoiPopulation b = PoiFauna.Populate(RandomPlan(new Rng(3), 77, PlaceState.Abandoned, false), fauna, _data.Deep);
        Check(a.Spawns.Select(s => s.Role + "@" + s.Position).SequenceEqual(b.Spawns.Select(s => s.Role + "@" + s.Position)), "a place's population is the same every time from its seed");
    }

    void DepthMakesItWorse()
    {
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        // Within a depth band it is the same species, bigger and tougher the deeper its nest.
        Species near = fauna.For(DeepRole.Nester, PoiTheme.Industrial, 3), mid = fauna.For(DeepRole.Nester, PoiTheme.Industrial, 4), deep = fauna.For(DeepRole.Nester, PoiTheme.Industrial, 5);
        int Guards(int depth)
        {
            var plan = new PoiPlan { Seed = 1, State = PlaceState.Abandoned, Theme = PoiTheme.Industrial };
            plan.Rooms.Add(new PoiRoom { Index = 0, Kind = RoomKind.Entrance, Min = Vector3.Zero, Max = new Vector3(6, 4, 6) });
            plan.Rooms.Add(new PoiRoom { Index = 1, Kind = RoomKind.Vault, Min = new Vector3(10, -4 * depth, 0), Max = new Vector3(20, -4 * depth + 5, 10), Depth = depth, Dark = true });
            for (ulong s = 1; s < 50; s++)
            {
                plan.Seed = s;
                PoiPopulation pop = PoiFauna.Populate(plan, fauna, _data.Deep);
                if (!pop.Silent && pop.Nests.Count > 0) return pop.Spawns.Count(x => x.Nest >= 0);
            }
            return 0;
        }
        int g1 = Guards(1), g3 = Guards(3), g5 = Guards(5);
        Check(g1 < g3 && g3 < g5 && near.Body.Size < mid.Body.Size && mid.Body.Size < deep.Body.Size && near.Stats.MaxHealth < deep.Stats.MaxHealth && near.Name == deep.Name,
            $"nests get worse with depth: {g1}, {g3} and {g5} guards at levels 1, 3 and 5; the same {near.Name} grows from {near.Body.Size:F2} to {mid.Body.Size:F2} to {deep.Body.Size:F2} m and {near.Stats.MaxHealth:F0} to {deep.Stats.MaxHealth:F0} health from level 3 to 5");
    }

    // ---- Tells: frightening but fair -----------------------------------------------------

    sealed class Fairness
    {
        public readonly Dictionary<(Creature, ITarget), float> FirstTell = new();
        public int Hits, Tells;
        public float WorstLead = float.MaxValue;
        public readonly List<string> Kinds = new();
        public string Bad = "";

        public void Watch(WildSim sim, ITarget target)
        {
            sim.Told += e =>
            {
                if (e.Target != target) return;
                Tells++;
                if (FirstTell.TryAdd((e.Creature, target), e.Time)) Kinds.Add(e.Kind.ToString().ToLowerInvariant());
            };
            sim.Attacked += e =>
            {
                if (e.Target != target || !e.Hit) return;
                Hits++;
                float lead = FirstTell.TryGetValue((e.Creature, target), out float at) ? e.Time - at : -1f;
                if (lead < WorstLead) WorstLead = lead;
                if (lead < 1f && Bad == "") Bad = $"{e.Creature.Species.CommonName} hit with a lead of {lead:F2} s";
            };
        }
    }

    WildSim PlaceSim(BlockPlace place) => new(_data, new Terrain(5, 40f), new NavGrid(40f, 3f));

    /// <summary>How deep a point is inside a solid block (negative when it is in the open).</summary>
    static Func<Vector3, float> BlockDepth(BlockPlace place) => p =>
    {
        int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
        if (!place.Solid(x, y, z)) return -1f;
        float fx = p.X - x, fy = p.Y - y, fz = p.Z - z;
        float depth = 1f;
        // The depth to the nearest open face of this block.
        if (!place.Solid(x - 1, y, z)) depth = MathF.Min(depth, fx);
        if (!place.Solid(x + 1, y, z)) depth = MathF.Min(depth, 1f - fx);
        if (!place.Solid(x, y - 1, z)) depth = MathF.Min(depth, fy);
        if (!place.Solid(x, y + 1, z)) depth = MathF.Min(depth, 1f - fy);
        if (!place.Solid(x, y, z - 1)) depth = MathF.Min(depth, fz);
        if (!place.Solid(x, y, z + 1)) depth = MathF.Min(depth, 1f - fz);
        return depth;
    };

    float FloorAt(ISolidSpace s, Vector3 p) => SpaceMath.FloorBelow(s, p.X, p.Y + 0.6f, p.Z, 4f) ?? p.Y;

    /// <summary>One ambush, set up by hand: someone walks into it and stands still; they must be told, then hit.</summary>
    (bool hit, float lead, string tell, bool dodged) Ambush(string label, Func<WildSim, BlockSpace, Creature> make, Vector3 from, Vector3 stand, Vector3 refuge, bool pursuer)
    {
        BlockPlace place = BlockPlace.Outpost();
        BlockSpace space = place.Space;
        WildSim sim = PlaceSim(place);
        Creature c = make(sim, space);
        float Run(DummyTarget who, bool dodge)
        {
            float firstTell = -1f, hitAt = -1f, committedAt = -1f;
            bool firstMissed = false, attacked = false;
            Vector3 away = Vector3.Zero;
            sim.Told += e => { if (e.Target == who && firstTell < 0f) firstTell = e.Time; };
            sim.Attacked += e =>
            {
                if (e.Target != who) return;
                if (!attacked) firstMissed = !e.Hit;
                attacked = true;
                if (e.Hit && hitAt < 0f) hitAt = e.Time;
            };
            sim.Committed += e =>
            {
                if (e.Target != who || committedAt >= 0f) return;
                committedAt = e.Time;
                away = WildSim.Flat(who.Position - e.Creature.Position).Normalized();
            };
            who.Position = new Vector3(from.X, FloorAt(space, from), from.Z);
            sim.Targets.Add(who);
            Vector3 goal = new(stand.X, FloorAt(space, stand), stand.Z);
            var flight = new[] { from, refuge };
            int leg = 0;
            for (float t = 0f; t < 25f && hitAt < 0f; t += Dt)
            {
                if (pursuer && dodge)
                {
                    // A hunter coming for you: when it commits, step back out of its lunge.
                    if (attacked) break;
                    if (committedAt >= 0f && sim.Time - committedAt < 0.4f) who.Position += away * 6f * Dt;
                    else if (committedAt < 0f)
                    {
                        Vector3 to = goal - who.Position;
                        if (to.Length() > 0.05f) who.Position += to.Normalized() * MathF.Min(to.Length(), 1.5f * Dt);
                    }
                }
                else if (firstTell >= 0f && dodge)
                {
                    // Heard it: run back the way they came, and on, at a sprint.
                    if (sim.Time - firstTell > 8f) break;
                    Vector3 to = flight[Math.Min(leg, 1)] - who.Position;
                    to.Y = 0f;
                    if (to.Length() < 0.2f) leg++;
                    else who.Position += to.Normalized() * MathF.Min(to.Length(), 5.5f * Dt);
                }
                else
                {
                    Vector3 to = goal - who.Position;
                    if (to.Length() > 0.05f) who.Position += to.Normalized() * MathF.Min(to.Length(), 1.5f * Dt);
                }
                who.Position = new Vector3(who.Position.X, FloorAt(space, who.Position), who.Position.Z);
                sim.Step(Dt);
            }
            sim.Targets.Remove(who);
            if (pursuer && dodge) return attacked && firstMissed ? -1f : 1f;
            return hitAt < 0f ? -1f : hitAt - firstTell;
        }
        var victim = new DummyTarget { Name = "Victim", Health = new Health(1e6f) };
        float lead = Run(victim, false);
        // A fresh one, who reacts to the tell by backing away: fair means it can be escaped.
        BlockPlace place2 = BlockPlace.Outpost();
        sim = PlaceSim(place2);
        space = place2.Space;
        c = make(sim, space);
        TellKind kind = TellKind.Growl;
        sim.Told += e => kind = e.Kind;
        var wary = new DummyTarget { Name = "Wary", Health = new Health(1e6f) };
        float dodgeLead = Run(wary, true);
        return (lead >= 0f, lead, kind.ToString().ToLowerInvariant(), dodgeLead < 0f);
    }

    void ProwlerCase()
    {
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        Species prowler = fauna.For(DeepRole.Prowler, PoiTheme.Industrial, 0);
        BlockPlace place = BlockPlace.Outpost();
        WildSim sim = PlaceSim(place);
        Creature c = sim.SpawnIn(prowler, place.Space, new Vector3(25f, 9.2f, 6f));
        c.Route = new List<Vector3> { new(25f, 9f, 6f), new(32f, 9f, 6f) };
        var victim = new DummyTarget { Name = "Victim", Position = new Vector3(14f, 9f, 6f), Health = new Health(1e6f) };
        sim.Targets.Add(victim);
        sim.Told += e => Trace($"tell {e.Kind} at {e.Time:F2}");
        sim.Telegraph += e => Trace($"wind-up at {e.Time:F2}");
        sim.Attacked += e => Trace($"attack hit {e.Hit} at {e.Time:F2}");
        for (float t = 0f; t < 12f; t += Dt)
        {
            sim.Step(Dt);
            if ((int)(t / Dt) % 30 == 0) Trace($"t {t:F1} pos {c.Position} state {c.State} phase {c.Phase} noticed {c.Noticed?.Name} desired {c.Desired} path {c.SpacePath.Count}/{c.SpacePathIndex} range {c.Stats.AttackRange:F2} clr {c.Clearance:F2} d {(c.Position - victim.Position).Length():F2}");
        }
    }

    void AmbushesAreTold()
    {
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        var results = new List<string>();
        bool allHit = true, allTold = true, allDodged = true;
        float worst = float.MaxValue;
        void Case(string label, Func<WildSim, BlockSpace, Creature> make, Vector3 from, Vector3 stand, Vector3? refuge = null, bool pursuer = false)
        {
            var (hit, lead, tell, dodged) = Ambush(label, make, from, stand, refuge ?? new Vector3(3f, 9f, 3f), pursuer);
            allHit &= hit;
            allTold &= lead >= 1f;
            allDodged &= dodged;
            if (hit) worst = MathF.Min(worst, lead);
            results.Add($"{label} ({tell}, {(hit ? $"{lead:F2} s" : "no hit")}{(dodged ? pursuer ? ", its lunge dodged" : ", escapable" : ", NOT escapable")})");
        }
        Species dropper = Enumerable.Range(0, 40).Select(i => new DeepFauna(_data, (ulong)(2024 + i), 0.5f).For(DeepRole.Ambusher, PoiTheme.Industrial, 0)).First(s => s.Form == "creeper");
        Species eel = Enumerable.Range(0, 40).Select(i => new DeepFauna(_data, (ulong)(2024 + i), 0.5f).For(DeepRole.Ambusher, PoiTheme.Industrial, 0)).First(s => s.Form == "ducteel");
        Case("a creeper on the corridor ceiling", (sim, space) =>
        {
            Creature c = sim.SpawnIn(dropper, space, new Vector3(15.5f, 11.8f, 6f), Vector3.Down);
            c.Ambush = new AmbushPoint { Kind = HideKind.Ceiling, Position = Crawl.Center(c), Normal = c.Facing.Y };
            return c;
        }, new Vector3(9f, 9f, 6f), new Vector3(15.5f, 9f, 6f));
        Case("a duct eel in the corridor vent", (sim, space) =>
        {
            var vent = new Crack { Id = 0, Kind = CrackKind.Vent, Position = new Vector3(16.5f, 11.5f, 5f), Normal = Vector3.Back, Network = 2 };
            sim.Cracks.Add(vent);
            Creature c = sim.SpawnIn(eel, space, vent.Position + vent.Normal * 0.3f, vent.Normal);
            c.Ambush = new AmbushPoint { Kind = HideKind.Vent, Position = vent.Position, Hole = vent };
            Brain.EnterCrack(sim, c, vent, park: true);
            return c;
        }, new Vector3(9f, 9f, 6f), new Vector3(16.5f, 9f, 6.2f));
        Case("something behind the store's door", (sim, space) =>
        {
            var door = new Crack { Id = 0, Kind = CrackKind.Door, Position = new Vector3(22f, 9.2f, 7.5f), Normal = Vector3.Forward, Network = 3, Size = 1f };
            sim.Cracks.Add(door);
            Creature c = sim.SpawnIn(eel, space, new Vector3(22f, 9.2f, 9f));
            c.Ambush = new AmbushPoint { Kind = HideKind.Door, Position = door.Position, Hole = door };
            Brain.EnterCrack(sim, c, door, park: true);
            return c;
        }, new Vector3(13f, 9f, 6f), new Vector3(22f, 9f, 6f));
        Species lurker = fauna.For(DeepRole.Lurker, PoiTheme.Industrial, 0);
        Case($"a lurker ({lurker.CommonName}) in the dark room", (sim, space) => sim.SpawnIn(lurker, space, new Vector3(34.5f, 9.2f, 9.5f)),
            new Vector3(29f, 9f, 3f), new Vector3(33f, 9f, 8f));
        Species prowler = fauna.For(DeepRole.Prowler, PoiTheme.Industrial, 0);
        Case($"a prowler ({prowler.CommonName}) on the corridor", (sim, space) =>
        {
            Creature c = sim.SpawnIn(prowler, space, new Vector3(25f, 9.2f, 6f));
            c.Route = new List<Vector3> { new(25f, 9f, 6f), new(32f, 9f, 6f) };
            return c;
        }, new Vector3(5f, 9f, 6f), new Vector3(14f, 9f, 6f), pursuer: true);
        Species nester = fauna.For(DeepRole.Nester, PoiTheme.Industrial, 2);
        Case($"a nest guard ({nester.CommonName}) in the deep room", (sim, space) =>
        {
            var nest = new Nest { Position = new Vector3(6f, 1f, 26f), Depth = 2, Radius = 1.7f };
            sim.Nests.Add(nest);
            Creature c = sim.SpawnIn(nester, space, new Vector3(8.5f, 1.2f, 26f), pack: nest.Turns);
            c.Nest = nest;
            nest.Guards.Add(c);
            return c;
        }, new Vector3(11f, 1f, 22f), new Vector3(8f, 1f, 24f), new Vector3(22f, 5f, 27f));
        Check(allHit && allTold, $"every ambush gives itself away at least a second before it lands (tightest {worst:F2} s): {string.Join("; ", results)}");
        Check(allDodged, "and every one can be escaped: run when you hear or see an ambush's tell and it never lands; step back when a hunter commits and its lunge misses");
    }

    /// <summary>The rich showroom population of the outpost: a seed that gives it everything.</summary>
    public static ulong RichSeed(WildlifeData data, DeepFauna fauna)
    {
        for (ulong s = 41; s < 400; s++)
        {
            BlockPlace p = BlockPlace.Outpost(s);
            PoiPopulation pop = PoiFauna.Populate(p.Plan, fauna, data.Deep);
            if (pop.Silent) continue;
            if (pop.Count(DeepRole.Lurker) >= 1 && pop.Count(DeepRole.Ambusher) >= 2 && pop.Count(DeepRole.Subsister) >= 4 && pop.Count(DeepRole.Prowler) >= 1 && pop.Nests.Count >= 1) return s;
        }
        return 41;
    }

    void WalkThroughThePlace()
    {
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        ulong seed = RichSeed(_data, fauna);
        BlockPlace place = BlockPlace.Outpost(seed);
        PoiPopulation pop = PoiFauna.Populate(place.Plan, fauna, _data.Deep);
        WildSim sim = PlaceSim(place);
        List<Creature> made = PoiFauna.Spawn(sim, pop, place.Space);
        var walker = new DummyTarget { Name = "Explorer", Health = new Health(1e7f) };
        var f = new Fairness();
        f.Watch(sim, walker);
        // The way through: hall, corridor, dark room, stairs, lower hall, passage, nest room.
        var path = new List<Vector3>
        {
            new(5f, 9f, 6f), new(12f, 9f, 6f), new(27f, 9f, 6f), new(32f, 9f, 6f), new(32.5f, 9f, 11f), new(32.5f, 5f, 22.5f),
            new(30f, 5f, 27f), new(20f, 5f, 27f), new(15f, 5f, 27f), new(10f, 1f, 27f), new(6f, 1f, 24f), new(5f, 1f, 30f),
        };
        walker.Position = path[0];
        sim.Targets.Add(walker);
        int traced = 0;
        sim.Rescued += r => { if (traced++ < 6) Trace($"rescued {r.Species.CommonName} ({r.Species.Role}, climbs {r.Species.Climbs}) at {r.Position} last firm {r.LastFirm} state {r.State} in walls {r.InWalls} dead {r.Dead}"); };
        int leg = 1;
        var watch = made.ToDictionary(c => c, _ => new Watch { Trace = _trace ? Trace : null, Inside = BlockDepth(place) });
        for (float t = 0f; t < 240f; t += Dt)
        {
            if (leg < path.Count)
            {
                Vector3 to = path[leg] - walker.Position;
                if (to.Length() < 0.2f) leg++;
                else walker.Position += to.Normalized() * MathF.Min(to.Length(), 1.3f * Dt);
            }
            sim.Step(Dt);
            foreach (var (c, w) in watch) w.Look(c, place.Space);
        }
        float worstInside = watch.Values.Max(w => w.WorstInside);
        Check(f.Hits > 0 && f.WorstLead >= 1f,
            $"walking right through the outpost ({made.Count} creatures): {f.Hits} hits, every one after a tell from its creature at least a second before (tightest {f.WorstLead:F2} s; tells heard: {string.Join(", ", f.Kinds.Distinct())}){(f.Bad != "" ? $" FAIL: {f.Bad}" : "")}");
        Check(worstInside <= 0.02f && sim.Rescues == 0, $"nothing in the outpost ever ends up inside a block (deepest {MathF.Max(0f, worstInside):F2} m, {sim.Rescues} rescues)");
    }

    // ---- The food chain ---------------------------------------------------------------------

    void FoodChain()
    {
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        BlockPlace place = BlockPlace.Outpost();
        BlockSpace space = place.Space;
        WildSim sim = PlaceSim(place);
        PoiRoom dark = place.Plan.Rooms.Find(r => r.Kind == RoomKind.Room && r.Size.X > 8f);
        Species lurker = fauna.For(DeepRole.Lurker, PoiTheme.Industrial, 0);
        Species grub = fauna.For(DeepRole.Subsister, PoiTheme.Industrial, 0);
        var moss = new List<FoodPatch>
        {
            new() { Position = new Vector3(33f, 9f, 9f), Kind = grub.Diet }, new() { Position = new Vector3(35.5f, 9f, 6.5f), Kind = grub.Diet }, new() { Position = new Vector3(32f, 9f, 11f), Kind = grub.Diet },
        };
        sim.Food.AddRange(moss);
        sim.Cracks.Add(new Crack { Id = 0, Kind = CrackKind.Crack, Position = new Vector3(28.02f, 9.3f, 10f), Normal = Vector3.Right, Network = 1 });
        sim.Cracks.Add(new Crack { Id = 1, Kind = CrackKind.Crack, Position = new Vector3(36.98f, 9.3f, 5f), Normal = Vector3.Left, Network = 1 });
        Creature hunter = sim.SpawnIn(lurker, space, new Vector3(35f, 9.2f, 10.5f));
        hunter.Hunger = 0.8f;
        var col = new Colony { Species = grub, Space = space, Home = moss[0].Position, Target = 5 };
        col.Sources.AddRange(sim.Cracks);
        sim.Colonies.Add(col);
        for (int i = 0; i < 5; i++)
        {
            Creature g = sim.SpawnIn(grub, space, moss[i % 3].Position + new Vector3(0.4f * i, 0.3f, 0.3f));
            g.Home = moss[i % 3].Position;
        }
        // A player crouched in the corridor's mouth, watching.
        var watcher = new DummyTarget { Name = "Watcher", Position = new Vector3(26.5f, 9f, 6f), ThreatScale = 0.6f, Health = new Health(1e6f) };
        sim.Targets.Add(watcher);
        int kills = 0, unnoticed = 0, slipped = 0, toldOnWindup = 0;
        var windups = new Dictionary<Creature, float>();
        float minWindup = float.MaxValue;
        sim.Telegraph += e => { if (e.Target is CreatureTarget) windups[e.Creature] = e.Windup; };
        sim.Preyed += (pred, prey) =>
        {
            kills++;
            if (windups.TryGetValue(pred, out float w)) { toldOnWindup++; minWindup = MathF.Min(minWindup, w); }
            if (!sim.Creatures.Any(c => c.Alive && c.Noticed == watcher)) unnoticed++;
        };
        sim.Slipped += (c, k, entering) => { if (entering && c.Species.Role == DeepRole.Subsister) slipped++; };
        int watcherHits = 0;
        sim.Attacked += e => { if (e.Target == watcher && e.Hit) watcherHits++; };
        for (float t = 0f; t < 420f; t += Dt) sim.Step(Dt);
        int ate = sim.Creatures.Count(c => c.Species.HuntsPrey && c.Hunger < 0.3f);
        Check(kills >= 2 && unnoticed >= 1 && toldOnWindup == kills && minWindup >= 0.9f,
            $"the place's food chain runs while you watch, crouched and unnoticed: {kills} subsisters taken in 7 minutes ({unnoticed} with nobody noticing the watcher), each after a full wind-up (shortest {minWindup:F2} s)");
        Check(slipped >= 1 && sim.Creatures.Count(c => c.Alive && c.Species == grub) >= 2,
            $"subsisters run from the hunters into cracks ({slipped} slipped in) and their colony keeps coming back ({sim.Creatures.Count(c => c.Alive && c.Species == grub)} grazing at the end)");
        Check(watcherHits == 0 && ate >= 1, $"the hunters feed on their kills ({ate} fed) and leave the still watcher alone ({watcherHits} hits)");
    }

    void WreckNesters()
    {
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        BlockPlace hold = BlockPlace.Hold();
        PoiPopulation pop = null;
        ulong seed = 7;
        for (; seed < 200; seed++)
        {
            hold = BlockPlace.Hold(seed);
            pop = PoiFauna.Populate(hold.Plan, fauna, _data.Deep);
            if (!pop.Silent && pop.Nests.Count >= 2) break;
        }
        WildSim sim = PlaceSim(hold);
        List<Creature> made = PoiFauna.Spawn(sim, pop, hold.Space);
        float before = pop.Nests.Sum(n => n.Size);
        for (float t = 0f; t < 150f; t += Dt) sim.Step(Dt);
        float after = pop.Nests.Sum(n => n.Size);
        var rooms = pop.Nests.Select(n => hold.Plan.Rooms.Find(r => n.Position.X >= r.Min.X - 0.1f && n.Position.X <= r.Max.X + 0.1f && n.Position.Z >= r.Min.Z - 0.1f && n.Position.Z <= r.Max.Z + 0.1f)?.Kind.ToString().ToLowerInvariant() ?? "?").ToList();
        Check(made.Any(c => c.Species.Role == DeepRole.DerelictNester) && after > before + 0.1f,
            $"wreck nesters ({made.First(c => c.Species.Role == DeepRole.DerelictNester).Species.CommonName}) build up their nests from the hold's scraps ({before:F2} to {after:F2} over 2.5 minutes, nests in the {string.Join(" and ", rooms)})");
        // A salvager walks up to a nest: a hiss and a display, then the bite if they keep coming.
        var salvager = new DummyTarget { Name = "Salvager", Health = new Health(1e6f) };
        var f = new Fairness();
        f.Watch(sim, salvager);
        Nest target = pop.Nests[0];
        salvager.Position = new Vector3(2.5f, 2f, 9f);
        sim.Targets.Add(salvager);
        float warnedAt = -1f;
        sim.Told += e => { if (e.Target == salvager && e.Kind == TellKind.Hiss && warnedAt < 0f) warnedAt = e.Time; };
        float warnDistance = -1f;
        for (float t = 0f; t < 40f; t += Dt)
        {
            Vector3 to = target.Position - salvager.Position;
            to.Y = 0f;
            if (to.Length() > 0.8f) salvager.Position += to.Normalized() * 1.2f * Dt;
            salvager.Position = new Vector3(salvager.Position.X, FloorAt(hold.Space, salvager.Position), salvager.Position.Z);
            if (warnedAt >= 0f && warnDistance < 0f) warnDistance = (target.Position - salvager.Position).Length();
            sim.Step(Dt);
        }
        Check(warnedAt >= 0f && f.Hits > 0 && f.WorstLead >= 1f,
            $"its guards warn an intruder off (a hiss at {warnDistance:F1} m from the nest) and bite only after (tightest {f.WorstLead:F2} s after the first tell, {f.Hits} hits)");
    }

    // ---- Cost ----------------------------------------------------------------------------

    void DeepPerformance()
    {
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        BlockPlace place = BlockPlace.Outpost(RichSeed(_data, fauna));
        PoiPopulation pop = PoiFauna.Populate(place.Plan, fauna, _data.Deep);
        WildSim sim = PlaceSim(place);
        PoiFauna.Spawn(sim, pop, place.Space);
        TD.ChunkStore store = NewStore();
        var cave = new VoxelSpace(store);
        var (floor, _) = CaveChamber(store, cave);
        CaveFauna.Populate(sim, cave, fauna, floor + Vector3.Up, 16f, 5, 1, 4, 8);
        var walker = new DummyTarget { Name = "Explorer", Position = new Vector3(20f, 9f, 6f), Health = new Health(1e7f) };
        sim.Targets.Add(walker);
        for (int i = 0; i < 120; i++) sim.Step(Dt);
        int n = sim.Creatures.Count(c => !c.Removed);
        var windows = new List<double>();
        for (int w = 0; w < 3; w++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 300; i++) sim.Step(Dt);
            windows.Add(sw.Elapsed.TotalMilliseconds / 300.0);
        }
        double best = windows.Min();
        Check(best < 2.0, $"{n} cave and place creatures, every one posed every frame, cost {best:F2} ms/frame (budget 2 ms, best of three windows: {string.Join(", ", windows.Select(x => x.ToString("F2")))})");
    }

    // ---- Plumbing -----------------------------------------------------------------------

    void Check(bool ok, string what)
    {
        _checks++;
        if (ok) GD.Print($"PASS  {what}");
        else
        {
            _failures++;
            GD.PrintErr($"FAIL  {what}");
            GD.Print($"FAIL  {what}");
        }
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
        GD.Print($"FAIL  {what}");
    }
}
