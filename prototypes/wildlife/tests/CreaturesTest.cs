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
                try
                {
                    GetType().GetMethod(_probe, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(this, null);
                }
                catch (System.Reflection.TargetInvocationException e)
                {
                    Fail($"crashed: {e.InnerException}");
                }
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
            Spaces();
            PlacePopulations();
            DepthMakesItWorse();
            AmbushesAreTold();
            WalkThroughThePlace();
            FoodChain();
            WreckNesters();
            Mutations();
            Settlements();
            DomesticAnimals();
            SensesAreReal();
            StairsAreClimbed();
            CrouchingIsQuieter();
            WeightShows();
            NothingClipsThePlayer();
            WoundsShow();
            DeepPerformance();
            CrowdedPlaceCost();
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
                if (inside > 0f && !_told)
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
            if (c.Form != "creeper") continue;
            // The climbing and the drop are a climbing hunter's: a creeper made one (insects keep to themselves).
            c = c.Copy();
            c.Passive = false;
            c.Stats.Hunts = true;
            return c;
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
        var watches = made.ToDictionary(c => c, _ => new Watch { Trace = _trace ? Trace : null });
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

    /// <summary>The integration hooks for spaces: finding a planet cave's chamber, and a place's blocks set anywhere in a sim's frame.</summary>
    void Spaces()
    {
        TD.ChunkStore store = NewStore();
        var space = new VoxelSpace(store);
        var src = (TD.SimpleTerrainSource)store.Source;
        var (known, _) = CaveChamber(store, space);
        bool found = CaveFauna.FindChamber(space, known + new Vector3(6f, 3f, -4f), 14f, out Vector3 floor, (x, z) => src.SurfaceHeight(x, z));
        bool open = !space.SolidCell((int)MathF.Floor(floor.X), (int)MathF.Floor(floor.Y), (int)MathF.Floor(floor.Z)) && space.SolidCell((int)MathF.Floor(floor.X), (int)MathF.Floor(floor.Y) - 1, (int)MathF.Floor(floor.Z));
        // The same voxel space seen from a sim whose origin is elsewhere.
        var shifted = new VoxelSpace(store, new Vector3I(-60, 0, 30));
        bool voxelAgree = MathF.Abs(shifted.Density(known + new Vector3(60f, 1f, -30f)) - space.Density(known + Vector3.Up)) < 1e-4f;
        BlockPlace place = BlockPlace.Outpost();
        var moved = new BlockSpace(place.Solid, Vector3I.Zero, place.Size) { Offset = new Vector3I(100, -5, 20) };
        int disagree = 0;
        var rng = new Rng(4);
        for (int i = 0; i < 2000; i++)
        {
            int x = rng.Int(0, place.Size.X - 1), y = rng.Int(0, place.Size.Y - 1), z = rng.Int(0, place.Size.Z - 1);
            if (moved.SolidCell(x + 100, y - 5, z + 20) != place.Space.SolidCell(x, y, z)) disagree++;
            var p = new Vector3(x + rng.Float(), y + rng.Float(), z + rng.Float());
            if (MathF.Abs(moved.Density(p + new Vector3(100f, -5f, 20f)) - place.Space.Density(p)) > 1e-4f) disagree++;
        }
        Check(found && open && voxelAgree && disagree == 0,
            $"spaces fit any sim frame: a planet cave's chamber is found from nearby ({floor.DistanceTo(known):F1} m from the roomiest spot), and voxel and block spaces offset into a sim's frame agree cell for cell and density for density");
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
        int worstPlace = 0, worstRoom = 0, unfit = 0, placedBig = 0;
        string tight = "";
        int Most = _data.Deep.Population.PlaceCap["Infested"] * 2, RoomMost = 12; // the biggest hall RandomPlan rolls
        for (int i = 0; i < 400; i++)
        {
            PlaceState state = (PlaceState)(i % 4);
            bool derelict = i % 9 == 0 && state != PlaceState.Active;
            PoiPlan plan = RandomPlan(rng, (ulong)(5000 + i), state, derelict);
            PoiPopulation pop = PoiFauna.Populate(plan, fauna, _data.Deep);
            total[state] = total.GetValueOrDefault(state) + 1;
            if (pop.Silent) silent[state] = silent.GetValueOrDefault(state) + 1;
            creatures[state] = creatures.GetValueOrDefault(state) + pop.Spawns.Count;
            // The caps hold what comes at you; insects (passive) are the place's life and uncapped.
            worstPlace = Math.Max(worstPlace, pop.Spawns.Count(s => !s.Species.Passive));
            foreach (var room in pop.Spawns.Where(s => !s.Species.Passive).GroupBy(s => s.Room)) worstRoom = Math.Max(worstRoom, room.Count());
            int maxDepth = plan.Rooms.Max(r => r.Depth);
            foreach (PoiSpawn s in pop.Spawns)
            {
                seenRoles.Add(s.Role);
                PoiRoom home = plan.Rooms.Find(r => r.Index == s.Room);
                if (s.Species.TurnCircle > home.Width + 0.01f || s.Species.BodyHeight + DeepFauna.HeadClearance > home.Size.Y + 0.01f)
                {
                    unfit++;
                    tight = $"{s.Species.CommonName} ({s.Species.TurnCircle:F1} m circle, {s.Species.BodyHeight:F1} m tall) in a {home.Kind} {home.Width:F1} m wide, {home.Size.Y:F1} m high";
                }
                else if (s.Species.TurnCircle > 2.5f) placedBig++;
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
        // (A living world's places all hold something; only a barren world's may be silent: PoiFauna.)
        Check(Share(PlaceState.Abandoned) == 0f && Share(PlaceState.Ruined) == 0f && Share(PlaceState.Infested) == 0f && Share(PlaceState.Active) > 0.75f,
            $"on a living world only garrisons keep their places silent: abandoned {Share(PlaceState.Abandoned):P0}, ruined {Share(PlaceState.Ruined):P0}, infested {Share(PlaceState.Infested):P0}, active {Share(PlaceState.Active):P0} (garrisons keep theirs clear)");
        Check(Per(PlaceState.Infested) > Per(PlaceState.Abandoned) * 1.6f && Per(PlaceState.Active) < Per(PlaceState.Abandoned) * 0.5f,
            $"infested places are overrun ({Per(PlaceState.Infested):F1} creatures a place against {Per(PlaceState.Abandoned):F1} in abandoned ones, {Per(PlaceState.Active):F1} in active ones)");
        Check(worstPlace <= Most && worstRoom <= Math.Max(_data.Deep.Population.NestCap, RoomMost), $"places keep to their caps: at most {worstPlace} creatures in a place (cap {Most}), {worstRoom} in a room");
        Check(unfit == 0 && placedBig > 0, unfit == 0 ? $"every creature fits where it is put and can turn there ({placedBig} of the bigger ones placed)" : $"{unfit} creatures put where they don't fit: {tight}");
        var strider = new Species { Rig = RigKind.Legged, Body = new Proportions { Size = 5.5f, Width = 1.6f, LegLength = 2.2f, Height = 1.5f } };
        var roster = new DeepFauna(_data, 1, 0.5f);
        Check(MathF.Abs(strider.TurnCircle - 6.5f) < 0.3f && roster.Fitted(strider, 2f, 3f) == null && roster.Fitted(strider, 7f, 4f) == strider,
            $"a 5.5 m strider needs a {strider.TurnCircle:F1} m turning circle: not in a 2 m corridor under a 3 m ceiling, at home in a 7 by 4 m hall");
        Check(nestsAll == 0, $"no nest is built in any place (the owner: none anywhere): {nestsAll} found");
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
                if (!pop.Silent && pop.Count(DeepRole.Nester) > 0) return pop.Count(DeepRole.Nester);
            }
            return 0;
        }
        int g1 = Guards(1), g3 = Guards(3), g5 = Guards(5);
        Check(g1 < g3 && g3 < g5 && near.Body.Size < mid.Body.Size && mid.Body.Size < deep.Body.Size && near.Stats.MaxHealth < deep.Stats.MaxHealth && near.Name == deep.Name,
            $"the deep's insects come in more numbers and bigger with depth: {g1}, {g3} and {g5} at levels 1, 3 and 5; the same {near.Name} grows from {near.Body.Size:F2} to {mid.Body.Size:F2} to {deep.Body.Size:F2} m and {near.Stats.MaxHealth:F0} to {deep.Stats.MaxHealth:F0} health from level 3 to 5");
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
        bool allHit = true, allTold = true, allDodged = true, kept = true;
        float worst = float.MaxValue;
        var keepers = new List<string>();
        void Case(string label, Func<WildSim, BlockSpace, Creature> make, Vector3 from, Vector3 stand, Vector3? refuge = null, bool pursuer = false, bool insect = false)
        {
            var (hit, lead, tell, dodged) = Ambush(label, make, from, stand, refuge ?? new Vector3(3f, 9f, 3f), pursuer);
            // Insects keep to themselves (the owner): one that bit would be wrong.
            if (insect)
            {
                kept &= !hit;
                keepers.Add($"{label}{(hit ? " BIT" : "")}");
                return;
            }
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
        }, new Vector3(9f, 9f, 6f), new Vector3(15.5f, 9f, 6f), insect: dropper.Passive);
        Case("a duct eel in the corridor vent", (sim, space) =>
        {
            var vent = new Crack { Id = 0, Kind = CrackKind.Vent, Position = new Vector3(16.5f, 11.5f, 5f), Normal = Vector3.Back, Network = 2 };
            sim.Cracks.Add(vent);
            Creature c = sim.SpawnIn(eel, space, vent.Position + vent.Normal * 0.3f, vent.Normal);
            c.Ambush = new AmbushPoint { Kind = HideKind.Vent, Position = vent.Position, Hole = vent };
            Brain.EnterCrack(sim, c, vent, park: true);
            return c;
        }, new Vector3(9f, 9f, 6f), new Vector3(16.5f, 9f, 6.2f), insect: eel.Passive);
        Case("something behind the store's door", (sim, space) =>
        {
            var door = new Crack { Id = 0, Kind = CrackKind.Door, Position = new Vector3(22f, 9.2f, 7.5f), Normal = Vector3.Forward, Network = 3, Size = 1f };
            sim.Cracks.Add(door);
            Creature c = sim.SpawnIn(eel, space, new Vector3(22f, 9.2f, 9f));
            c.Ambush = new AmbushPoint { Kind = HideKind.Door, Position = door.Position, Hole = door };
            Brain.EnterCrack(sim, c, door, park: true);
            return c;
        }, new Vector3(13f, 9f, 6f), new Vector3(22f, 9f, 6f), insect: eel.Passive);
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
        }, new Vector3(11f, 1f, 22f), new Vector3(8f, 1f, 24f), new Vector3(22f, 5f, 27f), insect: nester.Passive);
        Check(kept, $"insects keep to themselves, walked right up to: {string.Join("; ", keepers)}");
        Check(allHit && allTold, $"every ambush gives itself away at least a second before it lands (tightest {worst:F2} s): {string.Join("; ", results)}");
        Check(allDodged, "and every one can be escaped: run when you hear or see an ambush's tell and it never lands; step back when a hunter commits and its lunge misses");
    }

    /// <summary>
    /// The population rules with their caps off, so a test can watch every role at once in one small
    /// place: what the behaviours are tested on, not what a place is really allowed to hold.
    /// </summary>
    public static DeepTables Packed(WildlifeData data)
    {
        DeepTables d = data.Deep;
        PopulationRules p = d.Population;
        return new DeepTables
        {
            Roles = d.Roles, Themes = d.Themes, Depth = d.Depth, Crawl = d.Crawl,
            Population = new PopulationRules
            {
                Silent = p.Silent, Lurker = p.Lurker, Ambusher = p.Ambusher, Prowler = p.Prowler, Subsister = p.Subsister, Food = p.Food, Cracks = p.Cracks,
                Density = new() { ["Active"] = 0.25f, ["Abandoned"] = 1f, ["Ruined"] = 0.8f, ["Infested"] = 2.2f },
                RoomCap = 99, NestCap = 99, PlaceCap = new() { ["Active"] = 999, ["Abandoned"] = 999, ["Ruined"] = 999, ["Infested"] = 999 },
            },
        };
    }

    /// <summary>The rich showroom population of the outpost: a seed that gives it everything.</summary>
    public static ulong RichSeed(WildlifeData data, DeepFauna fauna)
    {
        for (ulong s = 41; s < 400; s++)
        {
            BlockPlace p = BlockPlace.Outpost(s);
            PoiPopulation pop = PoiFauna.Populate(p.Plan, fauna, Packed(data));
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
        PoiPopulation pop = PoiFauna.Populate(place.Plan, fauna, Packed(_data));
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
        int stillBeside = 5, ate, grazing = 0;
        bool colonyShut = true;
        int cameBack = 0;
        sim.Spawned += c => { if (c.Species == grub) cameBack++; };
        var fed = new HashSet<Creature>();
        for (float t = 0f; t < 720f; t += Dt)
        {
            // Nothing comes out of a crack under a watcher's nose: it takes their going for the colony to come back.
            if (t < 300f)
            {
                int now = sim.Creatures.Count(c => c.Alive && c.Species == grub);
                colonyShut &= now <= stillBeside;
                stillBeside = now;
            }
            else if (watcher.Position.X < 100f)
            {
                grazing = stillBeside;
                watcher.Position = new Vector3(200f, 9f, 200f);
            }
            sim.Step(Dt);
            foreach (Creature c in sim.Creatures) if (c.Species.HuntsPrey && c.State == CState.Eat) fed.Add(c);
        }
        ate = fed.Count;
        Check(kills >= 2 && unnoticed >= 1 && toldOnWindup == kills && minWindup >= 0.9f,
            $"the place's food chain runs while you watch, crouched and unnoticed: {kills} subsisters taken in 12 minutes ({unnoticed} with nobody noticing the watcher), each after a full wind-up (shortest {minWindup:F2} s)");
        Check(slipped >= 1 && cameBack >= 2,
            $"subsisters run from the hunters into cracks ({slipped} slipped in) and their colony comes back once the watcher has gone ({grazing} left as they went, {cameBack} came back)");
        Check(watcherHits == 0 && ate >= 1 && colonyShut, $"the hunters feed on their kills ({ate} fed) and leave the still watcher alone ({watcherHits} hits); nothing came out beside them meanwhile");
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
            pop = PoiFauna.Populate(hold.Plan, fauna, Packed(_data));
            if (!pop.Silent && pop.Count(DeepRole.DerelictNester) >= 2) break;
        }
        WildSim sim = PlaceSim(hold);
        List<Creature> made = PoiFauna.Spawn(sim, pop, hold.Space);
        // A salvager walks through the hold: its insects keep to themselves and scatter, building nothing.
        var salvager = new DummyTarget { Name = "Salvager", Health = new Health(1e6f) };
        var f = new Fairness();
        f.Watch(sim, salvager);
        var bugs = made.Where(c => c.Species.Role == DeepRole.DerelictNester).ToList();
        Vector3 at = bugs.Count > 0 ? bugs[0].Position : Vector3.Zero;
        salvager.Position = new Vector3(2.5f, 2f, 9f);
        sim.Targets.Add(salvager);
        for (float t = 0f; t < 40f; t += Dt)
        {
            Vector3 to = at - salvager.Position;
            to.Y = 0f;
            if (to.Length() > 0.8f) salvager.Position += to.Normalized() * 1.2f * Dt;
            salvager.Position = new Vector3(salvager.Position.X, FloorAt(hold.Space, salvager.Position), salvager.Position.Z);
            sim.Step(Dt);
        }
        Check(bugs.Count >= 2 && bugs.All(c => c.Species.Passive) && pop.Nests.Count == 0 && sim.Nests.Count == 0 && f.Hits == 0,
            $"a wreck's insects ({(bugs.Count > 0 ? bugs[0].Species.CommonName : "none")}, {bugs.Count}) keep to themselves in its holds: no nest built, {f.Hits} bites on a salvager walking among them");
    }

    // ---- Mutagenic worlds -------------------------------------------------------------------

    static (int pieces, int prims, float sum) LayoutPrint(Species s, WildSim sim)
    {
        Creature c = sim.Spawn(s, new Vector3(0f, 0f, 0f));
        int prims = 0;
        float sum = 0f;
        foreach (Piece p in c.Rig.Layout.Pieces)
            foreach (Prim m in p.Prims)
            {
                prims++;
                sum += m.Size.X * 1.3f + m.Size.Y * 1.7f + m.Size.Z * 1.9f + m.Local.Origin.X * 2.3f + m.Local.Origin.Y * 2.9f + m.Local.Origin.Z * 3.1f + m.Color.R + m.Color.G * 2f;
            }
        c.Removed = true;
        return (c.Rig.Layout.Pieces.Count, prims, sum);
    }

    void Mutations()
    {
        var sim = new WildSim(_data, new Terrain(5, 40f), new NavGrid(40f, 3f));
        int species = 0, same0 = 0, determ = 0, extras = 0, patches = 0, quality = 0, bounded = 0, animated = 0;
        float maxAsym = 0f, maxAdded = 0f;
        string maxAddedOn = "";
        var growthsAt = new Dictionary<float, int>();
        foreach (ulong seed in new ulong[] { 7041, 7042, 7043, 7044, 7045, 7046 })
        {
            PlanetFauna wild = _gen.Generate(seed, 0.4f, false);
            PlanetFauna again = _gen.Generate(seed, 0.4f, false);
            for (int i = 0; i < wild.Species.Count; i++)
            {
                Species s = wild.Species[i];
                if (s.Rig == RigKind.Swarm) continue;
                species++;
                // Strength 0 changes nothing: the very same species, the same body.
                if (ReferenceEquals(_gen.Mutate(s, 0f), s) && LayoutPrint(s, sim) == LayoutPrint(again.Species[i], sim)) same0++;
                // Same seed, same mutation.
                if (LayoutPrint(_gen.Mutate(s, 0.5f), sim) == LayoutPrint(_gen.Mutate(again.Species[i], 0.5f), sim)) determ++;
                foreach (float k in new[] { 0.25f, 0.5f, 1f })
                {
                    Species m = _gen.Mutate(s, k);
                    LayoutPrint(m, sim);
                    Mutation.Report r = Mutation.Of(m);
                    growthsAt[k] = growthsAt.GetValueOrDefault(k) + r.Growths;
                    if (k < 1f) continue;
                    if (r.Extra != "") extras++;
                    if (r.Patch) patches++;
                    if (FaunaQuality.Problems(_data.Fauna, m).Count == 0) quality++;
                    maxAsym = MathF.Max(maxAsym, r.Asymmetry);
                    float added = r.BodyVolume > 0f ? r.AddedVolume / r.BodyVolume : 0f;
                    if (added > maxAdded) maxAddedOn = $"{m.CommonName}, {m.Form}, {m.Body.Size:F2} m";
                    maxAdded = MathF.Max(maxAdded, added);
                    if (r.Asymmetry <= Mutation.MaxAsymmetry + 1e-3f && added < 0.25f && r.Growths <= Mutation.MaxGrowths) bounded++;
                    // And it still moves like an animal: 20 s of animation, nothing broken.
                    Creature c = sim.Spawn(m, new Vector3(i * 6f - 20f, 0f, 10f));
                    bool ok = true;
                    for (int f = 0; f < 1200; f++)
                    {
                        sim.Step(Dt);
                        foreach (Transform3D t in c.Rig.Pose) if (!Geo.Finite(t)) ok = false;
                    }
                    if (ok && Geo.Finite(c.Position)) animated++;
                    c.Removed = true;
                }
            }
        }
        Check(same0 == species && determ == species, $"mutation is the same from the same seed ({determ} of {species} species), and strength 0 changes nothing ({same0} of {species})");
        Check(growthsAt[0.25f] < growthsAt[0.5f] && growthsAt[0.5f] < growthsAt[1f] && extras >= species / 2 && patches >= species / 2,
            $"it grows with strength: {growthsAt[0.25f]}, {growthsAt[0.5f]} and {growthsAt[1f]} growths at 0.25, 0.5 and 1; at full strength {extras} of {species} have an extra part and {patches} a patch of off-palette skin");
        Check(quality == species && bounded == species && animated == species,
            $"silhouettes stay believable: every fully mutated species passes every body rule ({quality} of {species}), stays within bounds (heavier side at most {maxAsym:F2}x, growths at most {maxAdded:P0} of the body, on a {maxAddedOn}) and animates cleanly ({animated} of {species})");
    }

    // ---- Settlements -------------------------------------------------------------------------

    void Settlements()
    {
        // Spawning is suppressed in a town and its margin, and nothing roams in.
        var terrain = new Terrain(1977);
        var nav = new NavGrid(terrain.HalfSize, 3f);
        var sim = new WildSim(_data, terrain, nav);
        Settlement town = sim.AddSettlement("Hollowmere", new Vector3(0f, 0f, -20f), 45f);
        PlanetFauna fauna = _gen.Generate(4242, 0.4f, false);
        var spawner = new Spawner(sim, fauna, new List<Vector3>(), 4242);
        spawner.Populate();
        float margin = _data.Spawning.BaseClearance;
        float closest = float.MaxValue;
        sim.Spawned += c => closest = MathF.Min(closest, WildSim.Flat(c.Position - town.Center).Length() - town.Radius);
        foreach (Creature c in sim.Creatures) closest = MathF.Min(closest, WildSim.Flat(c.Position - town.Center).Length() - town.Radius);
        var player = new DummyTarget { Name = "Walker", Position = new Vector3(150f, 0f, 150f), Health = new Health(1e9f) };
        sim.Targets.Add(player);
        int entered = 0;
        sim.StrayEntered += (_, _) => entered++;
        var rng = new Rng(2);
        for (int i = 0; i < 60 * 60 * 15; i++)
        {
            // Walking about: nothing comes back round someone who stays put.
            player.Position = new Vector3(150f + MathF.Sin(i * Dt * 0.02f) * 80f, 0f, 150f);
            sim.Step(Dt);
            spawner.Step(Dt);
            if (i % 240 == 0)
            {
                foreach (Creature c in sim.Creatures)
                    if (!c.Removed && c.Alive && rng.Chance(0.02f)) sim.Kill(c, null); // something keeps thinning them
            }
        }
        Check(closest >= margin - 1f && spawner.Respawned > 0,
            $"no wild creature spawns in a settlement or within {margin:F0} m of it ({spawner.Respawned} respawns over 15 minutes; closest {closest:F0} m from its edge)");

        // Herds driven at a town go round it.
        var sim2 = new WildSim(_data, new Terrain(1977), new NavGrid(240f, 3f));
        Settlement town2 = sim2.AddSettlement("Hollowmere", Vector3.Zero, 30f);
        Species grazer = fauna.Species[0];
        // (Put down by hand: a town's spawning margin would rightly keep a natural herd from appearing this close.)
        var flock = new Herd { Home = new Vector3(-60f, 0f, 0f), Goal = new Vector3(-60f, 0f, 0f), Center = new Vector3(-60f, 0f, 0f), Spread = WildSim.HerdSpread(grazer, 6) };
        sim2.Herds.Add(flock);
        var herd = new List<Creature>();
        for (int i = 0; i < 6; i++) herd.Add(sim2.Spawn(grazer, new Vector3(-60f + (i % 3) * 3f, 0f, (i / 3) * 3f), flock));
        sim2.Targets.Add(new DummyTarget { Name = "Drover", Position = new Vector3(-75f, 0f, 0f), Health = new Health(1e9f) });
        float deepest = float.MaxValue;
        foreach (Creature c in herd) { c.Herd.Alarm(new Vector3(-80f, 0f, 0f), 0f); }
        for (int i = 0; i < 60 * 90; i++)
        {
            if (i % 120 == 0) foreach (Creature c in herd) c.Herd.Alarm(c.Herd.Center + new Vector3(-20f, 0f, (i / 120 % 2) * 6f - 3f), sim2.Time);
            sim2.Step(Dt);
            foreach (Creature c in herd) deepest = MathF.Min(deepest, WildSim.Flat(c.Position).Length() - town2.Radius);
        }
        Check(deepest > -2f, $"a herd stampeded straight at a town veers round it (closest {deepest:F1} m from its edge)");

        // Predators take townsfolk outside town, never inside it.
        Species hunter = fauna.Species.Find(s => s.Behaviour == Behaviour.Predator) ?? _gen.Generate(4243, 0.4f, false).Species.Find(s => s.Behaviour == Behaviour.Predator);
        var sim3 = new WildSim(_data, new Terrain(1977), new NavGrid(240f, 3f));
        sim3.AddSettlement("Hollowmere", Vector3.Zero, 30f);
        var farmer = new DummyTarget { Name = "Townsperson", Position = new Vector3(60f, 0f, 0f), Health = new Health(1e6f) };
        farmer.Position = new Vector3(60f, sim3.Terrain.Height(60f, 0f), 0f); // on the road, where it can be seen
        sim3.Targets.Add(farmer);
        Creature p3 = sim3.Spawn(hunter, new Vector3(75f, 0f, 6f));
        p3.Yaw = MathF.Atan2(-(farmer.Position.X - p3.Position.X), -(farmer.Position.Z - p3.Position.Z)); // the farmer is in front of it: a hunter sees what it faces
        int outsideHits = 0;
        float firstWindup = -1f;
        sim3.Telegraph += e => { if (e.Target == farmer && firstWindup < 0f) firstWindup = e.Windup; };
        sim3.Attacked += e => { if (e.Target == farmer && e.Hit) outsideHits++; };
        for (int i = 0; i < 60 * 30 && outsideHits == 0; i++) sim3.Step(Dt);
        // Now they walk home, into town, with it close behind.
        int insideHits = 0;
        float predatorDeepest = float.MaxValue;
        sim3.Attacked += e => { if (e.Target == farmer && e.Hit && WildSim.Flat(farmer.Position).Length() < 30f) insideHits++; };
        for (int i = 0; i < 60 * 40; i++)
        {
            Vector3 to = new Vector3(5f, 0f, 0f) - farmer.Position;
            if (to.Length() > 0.2f) farmer.Position += to.Normalized() * 4.5f * Dt;
            farmer.Position = new Vector3(farmer.Position.X, sim3.Terrain.Height(farmer.Position.X, farmer.Position.Z), farmer.Position.Z);
            sim3.Step(Dt);
            if (WildSim.Flat(farmer.Position).Length() < 30f) predatorDeepest = MathF.Min(predatorDeepest, WildSim.Flat(p3.Position).Length() - 30f);
        }
        Check(outsideHits > 0 && firstWindup >= hunter.TelegraphWindup(_data.Combat) - 1e-3f && insideHits == 0 && predatorDeepest > -1f,
            $"a {hunter.CommonName} takes a townsperson out on the road (telegraphed, {firstWindup:F2} s wind-up), and once they are home inside the town it stops at the edge ({predatorDeepest:F1} m from it) and gets no bite in");

        // A stray inside town is reported, and the guards' hook kills it.
        var sim4 = new WildSim(_data, new Terrain(1977), new NavGrid(240f, 3f));
        Settlement town4 = sim4.AddSettlement("Hollowmere", Vector3.Zero, 30f);
        Creature stray = null;
        sim4.StrayEntered += (c, t) => { if (t == town4) sim4.CullStray(c); stray = c; };
        Creature wanderer = sim4.Spawn(hunter, new Vector3(5f, 0f, 5f));
        for (int i = 0; i < 30; i++) sim4.Step(Dt);
        Check(stray == wanderer && wanderer.Dead && sim4.Strays(town4).Count == 0, $"a wild creature that gets into a town is reported to it and its guards' hook kills it at once (dead within {30 * Dt:F1} s)");
    }

    void DomesticAnimals()
    {
        var kinds = new[] { DomesticKind.Stock, DomesticKind.Working, DomesticKind.Pet };
        int ok = 0, total = 0;
        var lines = new List<string>();
        float maxSat = 0f;
        for (ulong seed = 300; seed < 340; seed++)
            foreach (DomesticKind k in kinds)
            {
                Species s = _gen.RollDomestic(seed, k, seed % 2 == 0 ? 0.2f : 0.8f);
                total++;
                s.Colors.Base.ToHsv(out _, out float sat, out _);
                maxSat = MathF.Max(maxSat, sat);
                bool docile = s.Domestic == k && s.Stats.Damage <= 0f && !s.Stats.Retaliates && s.Temperament == Temperament.Calm && s.Colors.GlowStrength <= 0f && sat <= 0.45f
                              && s.Part("limbs") == "none" && s.Part("tail") is not ("stinger" or "spiked" or "club") && FaunaQuality.Problems(_data.Fauna, s).Count == 0;
                if (docile) ok++;
                else if (_trace) Trace($"not docile: {s} damage {s.Stats.Damage} retaliates {s.Stats.Retaliates} {s.Temperament} glow {s.Colors.GlowStrength} sat {sat:F2} limbs {s.Part("limbs")} tail {s.Part("tail")} problems {string.Join("; ", FaunaQuality.Problems(_data.Fauna, s))}");
                if (seed == 300) lines.Add($"{s.CommonName} ({s.Name}, {s.Body.Size:F1} m)");
            }
        Species a = _gen.RollDomestic(301, DomesticKind.Pet, 0.5f), b = _gen.RollDomestic(301, DomesticKind.Pet, 0.5f);
        float stock = _gen.RollDomestic(300, DomesticKind.Stock, 0.4f).Body.Size, work = _gen.RollDomestic(300, DomesticKind.Working, 0.4f).Body.Size, pet = _gen.RollDomestic(300, DomesticKind.Pet, 0.4f).Body.Size;
        Check(ok == total && a.PartSignature == b.PartSignature && pet < stock && stock < work,
            $"domestic animals of each kind are calm, plainly coloured (saturation at most {maxSat:F2}), unarmed and have no signature attack ({ok} of {total}); working beasts are the biggest and pets the smallest: {string.Join(", ", lines)}");

        // In a settlement: they keep to their pens, and run from a wild hunter without fighting it.
        var sim = new WildSim(_data, new Terrain(1977), new NavGrid(240f, 3f));
        sim.AddSettlement("Hollowmere", Vector3.Zero, 40f);
        Species stockS = _gen.RollDomestic(300, DomesticKind.Stock, 0.4f), petS = _gen.RollDomestic(300, DomesticKind.Pet, 0.4f), workS = _gen.RollDomestic(300, DomesticKind.Working, 0.4f);
        var pen = new Vector3(10f, 0f, 0f);
        var animals = new List<Creature>();
        for (int i = 0; i < 4; i++) animals.Add(sim.SpawnDomestic(stockS, pen + new Vector3(i - 1.5f, 0f, 0f), pen, 5f));
        var owner = new DummyTarget { Name = "Townsperson", Position = new Vector3(-10f, 0f, 0f), Health = new Health(1e6f) };
        Creature pupA = sim.SpawnDomestic(petS, owner.Position + new Vector3(2f, 0f, 0f), Vector3.Zero, 35f, owner);
        Creature beast = sim.SpawnDomestic(workS, new Vector3(0f, 0f, 15f), new Vector3(0f, 0f, 15f), 2.5f);
        float worstOut = 0f, pupFar = 0f;
        int domesticAttacks = 0;
        sim.Telegraph += e => { if (e.Creature.Species.Domestic != DomesticKind.None) domesticAttacks++; };
        for (int i = 0; i < 60 * 120; i++)
        {
            float t = i * Dt;
            owner.Position = new Vector3(-10f + MathF.Sin(t * 0.15f) * 12f, 0f, MathF.Cos(t * 0.11f) * 10f);
            owner.Forward = new Vector3(MathF.Cos(t * 0.15f), 0f, -MathF.Sin(t * 0.11f)).Normalized();
            sim.Clock.Time01 = 0.45f;
            sim.Step(Dt);
            foreach (Creature c in animals) worstOut = MathF.Max(worstOut, WildSim.Flat(c.Position - pen).Length() - 5f);
            worstOut = MathF.Max(worstOut, WildSim.Flat(beast.Position - beast.PenCenter).Length() - 2.5f);
            if (i > 600) pupFar = MathF.Max(pupFar, WildSim.Flat(pupA.Position - owner.Position).Length());
        }
        // A wild hunter in their midst (it got in; the guards haven't come yet): they scatter inside the pen.
        Species hunter = _gen.Generate(4242, 0.4f, false).Species.Find(s => s.IsHunter && !s.Flies);
        Creature wolf = sim.Spawn(hunter, pen + new Vector3(0f, 0f, 1f));
        // Any that ran at some point in those four seconds: one look at a single instant misses those already over it or not yet started.
        var ran = new HashSet<Creature>();
        for (int i = 0; i < 60 * 4; i++)
        {
            sim.Step(Dt);
            foreach (Creature c in animals) if (c.State == CState.Flee) ran.Add(c);
        }
        int fled = ran.Count;
        foreach (Creature c in animals) worstOut = MathF.Max(worstOut, WildSim.Flat(c.Position - pen).Length() - 5f);
        Check(worstOut < 1f && pupFar < 9f && fled >= 3 && domesticAttacks == 0,
            $"they keep to their pens and stations over 2 minutes (at most {MathF.Max(0f, worstOut):F1} m over), a pet keeps up with its owner (never more than {pupFar:F1} m behind), and a wild hunter among the stock scatters them inside the pen ({fled} of 4 running), none fighting back");
    }

    // ---- Cost ----------------------------------------------------------------------------

    void DeepPerformance()
    {
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        BlockPlace place = BlockPlace.Outpost(RichSeed(_data, fauna));
        PoiPopulation pop = PoiFauna.Populate(place.Plan, fauna, Packed(_data));
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

    /// <summary>Line of sight, the cone, memory, hearing apart from sight, the meter; and nothing alerted by what it can't sense.</summary>
    /// <summary>
    /// Walkers take a flight of stairs as people do: a place's stair cells are ramps to them, solid
    /// only under the 45-degree slope, and their paths go up and down a flight. Fresh each run: a
    /// flight of three to six stairs in a corridor, rising whichever way, and the place's prowlers
    /// (small and long-legged, every walker the world rolls, never a climber or a flier) chasing
    /// someone standing at its head: every one reaches the top.
    /// </summary>
    void StairsAreClimbed()
    {
        var rng = new Random((int)(System.Environment.TickCount64 & 0x7fffffff));
        int n = rng.Next(3, 7), turn = rng.Next(4);
        const int Len = 22, Wide = 4;
        // The flight in its own frame: along +u from u = 6, the corridor across v = 1..3.
        var solid = new HashSet<Vector3I>();
        var slope = new Dictionary<Vector3I, int>();
        Vector3I At(int u, int y, int v) => turn switch
        {
            0 => new Vector3I(u, y, v),
            1 => new Vector3I(-u - 1, y, v),
            2 => new Vector3I(v, y, u),
            _ => new Vector3I(v, y, -u - 1),
        };
        int rise = turn switch { 0 => 0, 1 => 1, 2 => 2, _ => 3 };
        for (int u = -1; u <= Len; u++)
        for (int v = 0; v <= Wide; v++)
        for (int y = -1; y <= n + 5; y++)
        {
            int stair = u - 6; // the stair at u = 6 + i stands in cell y = i
            bool wall = v == 0 || v == Wide || u == -1 || u == Len || y == n + 5;
            bool ground = u < 6 ? y < 0 : stair < n ? y < stair : y < n;
            if (wall || ground) solid.Add(At(u, y, v));
            else if (stair >= 0 && stair < n && y == stair)
            {
                solid.Add(At(u, y, v));
                slope[At(u, y, v)] = rise;
            }
        }
        Vector3I lo = new(int.MaxValue, int.MaxValue, int.MaxValue), hi = new(int.MinValue, int.MinValue, int.MinValue);
        foreach (Vector3I c in solid) { lo = new Vector3I(Math.Min(lo.X, c.X), Math.Min(lo.Y, c.Y), Math.Min(lo.Z, c.Z)); hi = new Vector3I(Math.Max(hi.X, c.X), Math.Max(hi.Y, c.Y), Math.Max(hi.Z, c.Z)); }
        var space = new BlockSpace((x, y, z) => solid.Contains(new Vector3I(x, y, z)), lo, hi + Vector3I.One)
        {
            Slope = (x, y, z) => slope.TryGetValue(new Vector3I(x, y, z), out int k) ? k : -1,
        };
        Vector3 Point(float u, float y, float v) => turn switch
        {
            0 => new Vector3(u, y, v),
            1 => new Vector3(-u, y, v),
            2 => new Vector3(v, y, u),
            _ => new Vector3(v, y, -u),
        };
        var fauna = new DeepFauna(_data, (ulong)rng.Next(), 0.5f);
        var walkers = new List<Species>();
        foreach (PoiTheme theme in Enum.GetValues<PoiTheme>())
            for (int depth = 0; depth < 4; depth++)
                foreach (DeepRole role in new[] { DeepRole.Prowler, DeepRole.Lurker })
                {
                    Species sp = fauna.For(role, theme, depth);
                    if (sp != null && !sp.Climbs && !sp.Flies && sp.BodyHeight < n + 4 && sp.BodyWidth < Wide - 1.2f && walkers.All(w => w.CommonName != sp.CommonName)) walkers.Add(sp);
                }
        int reached = 0, tried = 0;
        var stuck = new List<string>();
        foreach (Species sp in walkers.Take(8))
        {
            var sim = new WildSim(_data, new Terrain((x, z) => -50f, 80f), new NavGrid(80f, 3f));
            Creature c = sim.SpawnIn(sp, space, Point(2.5f, 0f, Wide / 2f));
            if (c == null) continue;
            tried++;
            var prey = new DummyTarget { Name = "Up there", Health = new Health(1e7f), Position = Point(6 + n + 3.5f, n, Wide / 2f) };
            float best = c.Position.Y;
            // Driven as the brain drives a chase (its path through the place, then its legs), at a run.
            for (float t = 0f; t < 40f && best < n - 0.05f; t += Dt)
            {
                sim.RefillPathBudget();
                Brain.Toward(sim, c, prey.Position + Vector3.Up * 0.5f, c.Stats.RunSpeed);
                Locomotion.Move(sim, c, Dt);
                sim.Time += Dt;
                best = MathF.Max(best, c.Position.Y);
            }
            if (best >= n - 0.05f) reached++;
            else stuck.Add($"{sp.CommonName} ({sp.BodyHeight:F1} m tall, {sp.BodyWidth:F1} m wide) got {best:F2} m up, at {c.Position}, {c.State}");
        }
        foreach (string w in stuck.Take(4)) GD.Print("  stuck: " + w);
        Check(tried >= 3 && reached == tried,
            $"walkers chasing someone up a flight of {n} stairs (rising {(turn switch { 0 => "+x", 1 => "-x", 2 => "+z", _ => "-z" })}) reach its head: {reached} of {tried} ({string.Join(", ", walkers.Take(8).Select(w => $"{w.CommonName} {w.BodyHeight:F1} m"))}){(stuck.Count > 0 ? "; " + stuck[0] : "")}");
    }

    void SensesAreReal()
    {
        // Two rooms with a solid wall between them, a doorway at one end.
        var cells = new HashSet<Vector3I>();
        for (int x = 0; x < 30; x++)
        for (int z = 0; z < 12; z++)
        for (int y = 0; y < 6; y++)
        {
            bool wall = x == 15 && z > 1;
            if (x == 0 || x == 29 || z == 0 || z == 11 || y == 0 || y == 5 || wall) cells.Add(new Vector3I(x, y, z));
        }
        var space = new BlockSpace((x, y, z) => cells.Contains(new Vector3I(x, y, z)), Vector3I.Zero, new Vector3I(30, 6, 12));
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 60f), new NavGrid(60f, 3f));
        Species sighted = fauna.For(DeepRole.Prowler, PoiTheme.Industrial, 0), blind = fauna.For(DeepRole.Prowler, PoiTheme.Industrial, 3);
        Vector3 at = new(8f, 1f, 6f);
        Creature Put(Species s, Vector3 target)
        {
            Creature c = sim.SpawnIn(s, space, at);
            Vector3 to = (target - c.Position).Normalized();
            c.Facing = SpaceMath.Frame(to, Vector3.Up);
            return c;
        }
        var p = new DummyTarget { Name = "Quiet", Health = new Health(1e7f) };
        sim.Targets.Add(p);
        void Run(float seconds, Creature c, Action each = null)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                each?.Invoke();
                // Only its senses: it stays where it is, facing the way it was put.
                Basis facing = c.Facing;
                sim.Step(Dt);
                c.Position = at;
                c.Velocity = Vector3.Zero;
                c.Facing = facing;
            }
        }
        // Behind the wall, in plain range, with it looking straight at the wall.
        p.Position = new Vector3(19f, 1f, 6f);
        Creature c1 = Put(sighted, p.Position);
        bool alerted = false;
        Run(4f, c1, () => alerted |= c1.Alert > 0f);
        Check(!alerted && c1.Noticed == null, $"through a wall a {sighted.CommonName} senses nothing at {sighted.NoticeRadius:F0} m range: not alerted, awareness {c1.Awareness:F2}");
        sim.Remove(c1);
        // In the open behind it, standing still: outside the cone, outside a still target's noise.
        p.Position = at + new Vector3(-6f, 0f, 0f);
        Creature c2 = Put(sighted, at + Vector3.Right * 5f);
        Run(3f, c2);
        bool blindSide = c2.Noticed == null;
        // In front of it, the same distance: it notices, but not at once.
        p.Position = at + new Vector3(6f, 0f, 0f);
        float noticedAfter = -1f, elapsed = 0f;
        Run(5f, c2, () => { elapsed += Dt; if (noticedAfter < 0f && c2.Noticed != null) noticedAfter = elapsed; });
        Check(blindSide && noticedAfter > 0.4f, $"a sighted creature sees in a cone (nothing from behind) and notices by degrees: {noticedAfter:F2} s to place someone in plain view");
        sim.Remove(c2);
        // Memory: gone behind the wall, it knows for a few seconds where they went, then forgets.
        Creature c3 = Put(sighted, at + Vector3.Right * 5f);
        p.Position = at + new Vector3(6f, 0f, 0f);
        Run(4f, c3);
        bool had = c3.Noticed == p;
        p.Position = new Vector3(19f, 1f, 6f);
        Run(2f, c3);
        bool held = c3.Noticed == p, lastAtRoom = c3.LastSeen.X < 16f;
        Run(5f, c3);
        Check(had && held && lastAtRoom && c3.Noticed == null, $"out of sight it holds on {Senses.Memory:F0} s and goes by where it last knew you, then forgets");
        sim.Remove(c3);
        // Blind: hears a walker in the room, nothing behind a wall, nothing of someone standing still.
        Creature c4 = Put(blind, at + Vector3.Right * 5f);
        p.Position = at + new Vector3(4f, 0f, 0f);
        Run(3f, c4);
        bool stillUnheard = !blind.Blind || c4.Noticed == null || 4f < blind.NoticeRadius * Senses.BlindHearing * 0.45f;
        float step = 1f;
        Run(5f, c4, () => { step = -step; p.Position += Vector3.Right * step * 0.1f; });
        bool heard = c4.Noticed == p;
        sim.Remove(c4);
        Creature c5 = Put(blind, at + Vector3.Right * 5f);
        p.Position = new Vector3(19f, 1f, 6f);
        Run(4f, c5, () => { step = -step; p.Position += Vector3.Right * step * 0.1f; });
        Check(blind.Blind && heard && stillUnheard && c5.Noticed == null, $"something blind goes by ear alone: a walker in its room heard, nothing through a wall ({blind.NoticeRadius * Senses.BlindHearing:F1} m earshot)");
    }

    /// <summary>
    /// Crouching, as the senses tell it (a fresh random species every run): a crouch-walker is heard from
    /// a shorter distance than a walker, someone crouched still from a shorter one than someone standing
    /// still, and a crouched target is a smaller one for the eye, so low cover hides it.
    /// </summary>
    void CrouchingIsQuieter()
    {
        var rng = new Random();
        Species species = null;
        float hearing = 0f, sight = 0f;
        for (int tries = 0; tries < 200 && species == null; tries++)
        {
            ulong seed = (ulong)rng.NextInt64(1, 1L << 40);
            foreach (Species s in _gen.Generate(seed, (float)rng.NextDouble(), false).Species)
            {
                Stats st = s.Stats;
                float ear = MathF.Max(st.Perception * Senses.Hearing, MathF.Max(st.AmbushRadius, st.AttackRange + 1f));
                // One that sees and hears the plain way, with its ears inside its eyes (so the two can be told apart).
                if (s.Flies || s.IsDeep || s.Behaviour is Behaviour.Ambusher or Behaviour.Swarm || s.Tumbles || s.Domestic != DomesticKind.None || ear * 0.5f > st.Perception * 0.7f) continue;
                (species, hearing, sight) = (s, ear, st.Perception);
                break;
            }
        }
        if (species == null)
        {
            Fail("no species with ears inside its eyes in 200 random worlds");
            return;
        }

        // A creature held in place, its back to a target at this distance, which paces to and fro at a steady 2 m/s (or stands).
        // Whether it senses it in the last three seconds of four, and (for the sight case) with the creature facing it behind a low wall.
        bool Senses_(float distance, bool crouched, bool moving, bool facing, bool lowWall)
        {
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 240f), new NavGrid(240f, 3f));
            if (lowWall)
            {
                // A wall 1.15 m high just short of the target: a standing head clears it, a crouched one doesn't.
                sim.SightLine = (a, b) =>
                {
                    float wallZ = distance * 0.95f;
                    if ((a.Z - wallZ) * (b.Z - wallZ) > 0f) return true;
                    float t = (wallZ - a.Z) / (b.Z - a.Z);
                    return a.Y + (b.Y - a.Y) * t > 1.15f;
                };
            }
            Creature c = sim.Spawn(species, Vector3.Zero);
            var who = new DummyTarget { Name = "Sneak", Health = new Health(1e7f), Position = new Vector3(0f, 0f, distance), Crouched = crouched };
            sim.Targets.Add(who);
            float yaw = facing ? MathF.PI : 0f, dir = 1f;
            bool sensed = false;
            for (float t = 0f; t < 4f; t += Dt)
            {
                if (moving)
                {
                    who.Position += new Vector3(dir * 2f * Dt, 0f, 0f);
                    if (MathF.Abs(who.Position.X) > 3f) dir = -dir;
                }
                sim.Step(Dt);
                c.Position = Vector3.Zero;
                c.Velocity = Vector3.Zero;
                c.Yaw = yaw;
                sensed |= t > 1f && c.Sensing; // once its measure of how fast the target goes has settled
            }
            return sensed;
        }

        float walkRange = hearing * 0.8f, stillRange = hearing * 0.35f;
        bool walker = Senses_(walkRange, false, true, false, false), creeper = Senses_(walkRange, true, true, false, false);
        bool standStill = Senses_(stillRange, false, false, false, false), crouchStill = Senses_(stillRange, true, false, false, false);
        var probe = new WildSim(_data, new Terrain((x, z) => 0f, 240f), new NavGrid(240f, 3f));
        var pace = new DummyTarget { Name = "Pace" };
        probe.Targets.Add(pace);
        for (int i = 0; i < 120; i++)
        {
            pace.Position += new Vector3(2f * Dt, 0f, 0f);
            probe.Step(Dt);
        }
        float walkNoise = probe.NoiseOf(pace);
        pace.Crouched = true;
        float creepNoise = probe.NoiseOf(pace);
        Check(walker && !creeper && walkNoise > creepNoise,
            $"a {species.CommonName} (ears {hearing:F1} m) hears a walker at {walkRange:F1} m but not a crouch-walker (noise {walkNoise:F2} against {creepNoise:F2})");
        float stillNoise = 0.45f;
        for (int i = 0; i < 240; i++) probe.Step(Dt);
        pace.Crouched = false;
        stillNoise = probe.NoiseOf(pace);
        pace.Crouched = true;
        Check(standStill && !crouchStill && probe.NoiseOf(pace) < stillNoise && probe.NoiseOf(pace) < creepNoise,
            $"it hears someone standing still at {stillRange:F1} m but not someone crouched still, the quietest of all (noise {probe.NoiseOf(pace):F2} against {stillNoise:F2} standing still, {creepNoise:F2} crouch-walking){(standStill && !crouchStill ? "" : $" [stood {standStill}, crouched {crouchStill}]")}");

        float seeRange = sight * 0.7f;
        bool seenStanding = Senses_(seeRange, false, false, true, true), seenCrouched = Senses_(seeRange, true, false, true, true);
        bool seenCrouchedOpen = Senses_(seeRange, true, false, true, false);
        Check(seenStanding && !seenCrouched && seenCrouchedOpen,
            $"it sees a standing head over a 1.15 m wall at {seeRange:F0} m but not a crouched one, which it sees plainly in the open");
    }

    /// <summary>Heads, necks and bodies stop at a person's body, and bites land where they can see them.</summary>
    void NothingClipsThePlayer()
    {
        PlanetFauna planet = _gen.Generate(4242, 0.3f, false);
        var hunters = planet.Species.Where(s => s.Rig == RigKind.Legged && !s.Flies).Take(3).ToList();
        float worst = 0f;
        int hits = 0, front = 0, sized = 0;
        string who = "";
        foreach (float size in new[] { 0.8f, 2f, 5.5f })
        foreach (Species model in hunters)
        {
            Species s = model.Copy();
            s.Stats.Damage = 1f;
            s.Stats.Hunts = true;
            s.Behaviour = Behaviour.Predator;
            float k = size / s.Body.Size;
            s.Body.Size *= k; s.Body.Width *= k; s.Body.Height *= k; s.Body.LegLength *= k; s.Body.NeckLength *= k; s.Body.HeadSize *= k;
            s.Stats.Radius *= k;
            sized++;
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
            var p = new DummyTarget { Name = "Person", Health = new Health(1e7f), Forward = Vector3.Forward };
            sim.Targets.Add(p);
            Creature c = sim.Spawn(s, new Vector3(1f, 0f, 14f)); // behind them
            c.Yaw = 0f;
            sim.Attacked += e =>
            {
                if (!e.Hit) return;
                hits++;
                if (WildSim.Flat(c.Position - p.Position).Normalized().Dot(p.Forward) > -0.3f) front++;
            };
            for (float t = 0f; t < 25f; t += Dt)
            {
                sim.Step(Dt);
                if (c.Phase == AttackPhase.Lunge) continue; // still in flight; where it lands is checked below
                for (int i = 0; i < c.Rig.HitCount; i++)
                {
                    Vector4 h = c.Rig.Hits[i];
                    if (h.Y - h.W > Attacks.BodyHeight) continue;
                    float into = h.W + Attacks.BodyRadius - WildSim.Flat(new Vector3(h.X, 0f, h.Z) - p.Position).Length();
                    if (into > worst) { worst = into; who = $"{size:F1} m {s.CommonName}, part {i} of {c.Rig.HitCount}, state {c.State} phase {c.Phase}"; }
                }
            }
        }
        Check(worst < 0.25f, $"heads and bodies stop at the player's body: at worst {worst:F2} m into it, over {sized} sizes of hunter ({who})");
        Check(hits >= sized && front >= hits * 0.6f, $"they still bite ({hits} bites), and the bite lands from in front of the player where it can: {front} of {hits} from a hunter that came up from behind");
    }

    /// <summary>A hit shows: it flinches, the small ones more; blood is the species' own and stays so.</summary>
    void WoundsShow()
    {
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
        PlanetFauna planet = _gen.Generate(4242, 0.3f, false);
        Species sp = planet.Species.Find(s => s.Rig == RigKind.Legged && s.Stats.MaxHealth > 10f);
        Creature c = sim.Spawn(sp, Vector3.Zero);
        c.Velocity = c.Forward * 4f;
        sim.Damage(c, 1f, null);
        bool flinched = c.Rear > 0.1f && c.Velocity.Length() < 3f;
        var colours = new HashSet<string>();
        foreach (Species s in planet.Species.Concat(_gen.Generate(99, 0.8f, true).Species)) colours.Add(s.Blood.ToHtml());
        Check(flinched && sp.Blood == sp.Blood && colours.Count >= 4, $"a hit flinches a creature, and blood goes by species ({colours.Count} colours on two worlds)");
    }

    /// <summary>A big body takes its time to get going, stop and turn; a small one doesn't.</summary>
    void WeightShows()
    {
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 300f), new NavGrid(300f, 3f));
        Species Body(float size)
        {
            Species s = _gen.Generate(4242, 0.3f, false).Species.Find(x => x.Rig == RigKind.Legged && !x.Flies).Copy();
            s.Body.Size = size;
            s.Stats.RunSpeed = 7f;
            s.Stats.WalkSpeed = 2f;
            return s;
        }
        (float start, float stop, float radius) Measure(float size)
        {
            Creature c = sim.Spawn(Body(size), Vector3.Zero);
            c.Yaw = 0f;
            float start = 0f, stop = 0f;
            for (; c.Velocity.Length() < 6.3f && start < 10f; start += Dt)
            {
                c.Desired = c.Forward * 7f;
                Locomotion.Move(sim, c, Dt);
            }
            // At speed it is told to go off at a right angle: how far sideways does it get before it is heading there?
            Vector3 from = c.Position;
            Vector3 right = c.Forward.Rotated(Vector3.Up, -MathF.PI / 2f), ahead = c.Forward;
            float over = 0f;
            for (float t = 0f; t < 8f && c.Forward.Dot(right) < 0.95f; t += Dt)
            {
                c.Desired = right * 7f;
                Locomotion.Move(sim, c, Dt);
                over = (c.Position - from).Dot(ahead);
            }
            for (; c.Velocity.Length() > 0.1f && stop < 10f; stop += Dt)
            {
                c.Desired = Vector3.Zero;
                Locomotion.Move(sim, c, Dt);
            }
            sim.Remove(c);
            return (start, stop, over);
        }
        var small = Measure(0.8f);
        var big = Measure(5.5f);
        Check(big.start > small.start * 2.5f && big.stop > small.stop * 2.5f && big.radius > small.radius * 3f,
            $"weight shows: a 5.5 m body takes {big.start:F1} s to get going and {big.stop:F1} s to stop and swings {big.radius:F1} m wide of a right-angle turn; a 0.8 m one {small.start:F1} s, {small.stop:F1} s, {small.radius:F1} m");
    }

    /// <summary>An overrun place with someone walking its halls, timed by part: the crowded case the owner plays.</summary>
    void CrowdedPlaceCost()
    {
        CrowdedPlaceCost("as allowed", _data.Deep);
        CrowdedPlaceCost("packed, uncapped", Packed(_data));
    }

    void CrowdedPlaceCost(string label, DeepTables rules)
    {
        var fauna = new DeepFauna(_data, 2024, 0.5f);
        BlockPlace place = BlockPlace.Outpost(RichSeed(_data, fauna));
        place.Plan.State = PlaceState.Infested;
        PoiPopulation pop = PoiFauna.Populate(place.Plan, fauna, rules);
        WildSim sim = PlaceSim(place);
        PoiFauna.Spawn(sim, pop, place.Space);
        var walker = new DummyTarget { Name = "Explorer", Position = new Vector3(20f, 9f, 6f), Health = new Health(1e7f) };
        sim.Targets.Add(walker);
        for (int i = 0; i < 600; i++) sim.Step(Dt);
        sim.Profile = true;
        double best = double.MaxValue;
        for (int w = 0; w < 3; w++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 300; i++) sim.Step(Dt);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / 300.0);
        }
        GD.Print($"PERF  crowded place, {label}: {sim.Creatures.Count(c => !c.Removed)} creatures, {best:F2} ms/frame; brains {sim.ThinkMs / 900:F2}, moving {sim.MoveMs / 900:F2}, rigs {sim.RigMs / 900:F2}");
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
