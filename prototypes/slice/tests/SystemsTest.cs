using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Skies;
using GalHack.Items.Bridge;
using Slice.Game;
using Slice.Orbit;
using Slice.Surface;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The star-system and planet rules (the owner's, 13, 17 and 22 Nov), over fresh random systems every run (no seed
/// is ever pinned): gravity is the size class's from the one table; the smallest bodies are almost all vacuum and
/// the largest never, with the densest air; a moon is smaller than what it orbits (a giant's any size), some of a
/// giant's moons have moons of their own, in reach and on the one clock; suns and gas giants out-size every world
/// you could land on, have bands, storms and spots and no ground; warp fuel lies on vacuum worlds only (as a
/// deposit, plus loot in space places and ships), fold fuel on the dangerous high-gravity ones (plus ships), and
/// the drives' materials with them: each drive's whole bill, walked down to ore, comes from its own worlds and is dug
/// and made there (SystemsTest.Drives.cs); every system holds a high-gravity world; every start has a vacuum moon with
/// enough warp fuel for a first jump; moons have no rings, and a ring's look follows the frost line and its
/// giant's moons; every landable world holds what the cheapest flyer the recipe tree allows takes; and nothing
/// marks a planet for a jump fuel. The rules on the systems' bodies run over thousands of systems; the ones
/// that need a world's map over the first <see cref="Mapped"/>.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/SystemsTest.tscn
/// </summary>
public partial class SystemsTest : Node
{
    const int Systems = 4000, Mapped = 240;
    const double TimeLimitMinutes = 12;

    /// <summary>Warp cells for one jump of the smallest warp drive, its longest: 15 AU at one cell an AU (docs/grid-figures.md section 4.4).</summary>
    const int FirstJumpCells = 15;

    int _failures, _passes;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(TimeLimitMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {TimeLimitMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        var clock = Stopwatch.StartNew();
        try { Run(); }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
    }

    /// <summary>The worlds you can land on: planets and moons with ground.</summary>
    static IEnumerable<Orbiter> Landable(StarSystem s) => s.Bodies.Where(b => b.Spec != null);

    void Run()
    {
        var systems = new List<StarSystem>();
        var daySeconds = new List<double>();
        for (int i = 0; i < Systems; i++)
        {
            WorldChoice world = WorldChoice.Fresh(WorldChoice.Types[i % WorldChoice.Types.Count].Key);
            PlanetSpec spec = world.Spec();
            SkySystem sky = SkySystem.For(spec);
            systems.Add(StarSystem.For(spec, sky, spec.WidthMeters));
            daySeconds.Add(sky.DayMinutes * 60.0);
        }
        GD.Print($"({systems.Count} fresh systems, {systems.Sum(s => Landable(s).Count())} worlds with ground; maps for the first {Mapped})");
        MoonSpeeds(systems, daySeconds);
        Gravity(systems);
        Vacuum(systems);
        Barren(systems);
        Moons(systems);
        Planes(systems);
        Rings(systems);
        SunsAndGiants(systems);
        HeavyWorlds(systems);
        DriveMaterials(systems);
        List<StarSystem> mapped = systems.Take(Mapped).ToList();
        Memory("the systems made");
        List<Ground> ground = Survey(mapped);
        Memory($"{ground.Count} worlds' maps read");
        StartMoons(ground);
        Fuels(ground);
        NoWaterOnBareWorlds(ground, mapped);
        DriveBills(ground);
        BarrenMaps(ground);
        Flyer(ground);
        Loot();
        NoMarkers(systems);
        DriveProof(mapped.Take(2));
        Memory("the proof dug and made");
    }

    /// <summary>Prints what the process holds now and the most it has held (the test is kept to a few GB).</summary>
    static void Memory(string when)
    {
        using var self = Process.GetCurrentProcess();
        GD.Print($"(memory after {when}: {GC.GetTotalMemory(false) / 1e9:F2} GB managed, {self.WorkingSet64 / 1e9:F2} GB held, peak {self.PeakWorkingSet64 / 1e9:F2} GB)");
    }

    // ---- 0. Moons can be caught; planets keep their years -----------------------------------------

    void MoonSpeeds(List<StarSystem> systems, List<double> daySeconds)
    {
        int moons = 0, fast = 0, unlocked = 0, years = 0, offYear = 0;
        double fastest = 0, nearFrom = double.MaxValue, nearTo = 0, oldFrom = double.MaxValue, oldTo = 0, monthFrom = double.MaxValue, monthTo = 0;
        for (int i = 0; i < systems.Count; i++)
        {
            StarSystem s = systems[i];
            foreach (Orbiter b in s.Bodies)
            {
                if (b.Kind == OrbiterKind.Moon)
                {
                    moons++;
                    double v = Math.Tau * b.Orbit / (b.Period * daySeconds[i]);
                    fastest = Math.Max(fastest, v);
                    if (v > StarSystem.MoonSpeedCap + 1e-6) fast++;
                    if (b != s.Home && b.DayLength != b.Period) unlocked++;
                }
                else if (b.Parent == s.Star)
                {
                    years++;
                    if (Math.Abs(b.Period / (StarSystem.TemperateYearDays * Math.Pow(b.Habitable, 1.5)) - 1) > 1e-9) offYear++;
                }
            }
            Orbiter near = s.FuelMoon;
            if (near.Parent != s.Home) continue;
            // The near moon's month as it was before the cap (rockMonth), for the record.
            double was = 3.8 * Math.Pow(near.Orbit / s.Home.Radius / 16.0, 1.5), speed = Math.Tau * near.Orbit / (near.Period * daySeconds[i]);
            nearFrom = Math.Min(nearFrom, speed); nearTo = Math.Max(nearTo, speed);
            oldFrom = Math.Min(oldFrom, Math.Tau * near.Orbit / (was * daySeconds[i])); oldTo = Math.Max(oldTo, Math.Tau * near.Orbit / (was * daySeconds[i]));
            monthFrom = Math.Min(monthFrom, near.Period); monthTo = Math.Max(monthTo, near.Period);
        }
        Check(moons > systems.Count && fast == 0,
            $"moons: all {moons} go round their parents at {StarSystem.MoonSpeedCap:F0} m/s at most, fastest {fastest:F1} m/s ({fast} over); the near moon {nearFrom:F0}-{nearTo:F0} m/s on a {monthFrom:F2}-{monthTo:F2} day month (uncapped {oldFrom:F0}-{oldTo:F0} m/s)");
        Check(unlocked == 0, $"moons: every one keeps one face to its parent, its day its month ({unlocked} did not)");
        Check(years > systems.Count && offYear == 0, $"moons: all {years} planets, giants and belts keep the year their distance gives ({offYear} did not)");
    }

    // ---- 1. Gravity comes from size ---------------------------------------------------------------

    void Gravity(List<StarSystem> systems)
    {
        int off = 0, worlds = 0;
        var bySize = new SortedDictionary<int, HashSet<float>>();
        foreach (StarSystem s in systems)
            foreach (Orbiter b in Landable(s))
            {
                worlds++;
                float g = s.GravityOf(b, 9.8f);
                // The owner's ladder (22 Nov): 8 km 0.2 g, 16 km 1 g, 32 km 2.2 g, 64 km 5 g, from the one table.
                float want = 9.8f * (b.Spec.WidthKm switch { 8 => 0.2f, 16 => 1f, 32 => 2.2f, 64 => 5f, _ => float.NaN });
                if (!(Math.Abs(g - want) < 1e-3f)) off++;
                if (!bySize.TryGetValue(b.Spec.WidthKm, out var set)) bySize[b.Spec.WidthKm] = set = new HashSet<float>();
                set.Add(MathF.Round(g, 3));
            }
        Check(off == 0 && bySize.Count == 4 && bySize.Values.All(v => v.Count == 1),
            $"gravity: every one of {worlds} worlds pulls its size's: {string.Join(", ", bySize.Select(p => $"{p.Key} km {p.Value.First():F2} m/s²"))} ({off} did not)");
    }

    // ---- 2. Air by size: the smallest almost all vacuum, the largest never and the densest ------------

    void Vacuum(List<StarSystem> systems)
    {
        var total = new SortedDictionary<int, int>();
        var airless = new SortedDictionary<int, int>();
        var thick = new SortedDictionary<int, (float Min, float Max)>();
        int fuelMoons = 0, fuelMoonsBare = 0, homeMoons = 0, homeMoonsBare = 0, kindsThinner = 0;
        foreach (StarSystem s in systems)
            foreach (Orbiter b in Landable(s))
            {
                if (b == s.Home) continue;
                int w = b.Spec.WidthKm;
                if (b.Parent == s.Home) { homeMoons++; homeMoonsBare += b.Spec.Airless ? 1 : 0; }
                if (b == s.FuelMoon) { fuelMoons++; fuelMoonsBare += b.Spec.Airless ? 1 : 0; }
                if (b == s.FuelMoon) continue; // made airless, so not in the size's odds
                total[w] = total.GetValueOrDefault(w) + 1;
                if (b.Spec.Airless)
                {
                    airless[w] = airless.GetValueOrDefault(w) + 1; // what it is and its weather: Barren
                    continue;
                }
                // The air's thickness is the world's size's, and its density the same kind's grows with it.
                SkySystem sky = SkySystem.For(b.Spec), start = SkySystem.For(b.Spec with { WidthKm = PlanetSpec.StartWidthKm });
                var (lo, hi) = thick.GetValueOrDefault(w, (float.MaxValue, 0f));
                thick[w] = (MathF.Min(lo, sky.Thickness), MathF.Max(hi, sky.Thickness));
                if (sky.Air != start.Air || (w > PlanetSpec.StartWidthKm ? sky.Density <= start.Density : w < PlanetSpec.StartWidthKm && sky.Density >= start.Density)) kindsThinner++;
            }
        double Rate(int w) => airless.GetValueOrDefault(w) / (double)Math.Max(1, total.GetValueOrDefault(w));
        string rates = string.Join(", ", total.Keys.Select(w => $"{w} km {Rate(w) * 100:F1}% of {total[w]}"));
        Check(total.GetValueOrDefault(8) > 500 && Rate(8) is > 0.92 and < 0.98, $"vacuum: the smallest bodies are about 95% airless ({rates})");
        Check(total.GetValueOrDefault(64) > 50 && Rate(64) == 0.0, $"vacuum: the largest worlds always have air ({rates})");
        var widths = total.Keys.ToList();
        bool falling = true;
        for (int i = 1; i < widths.Count; i++) falling &= Rate(widths[i]) < Rate(widths[i - 1]);
        Check(falling, $"vacuum: the bigger the body the likelier it keeps its air ({rates})");
        var sized = thick.Keys.ToList();
        bool thicker = sized.Count == 4 && thick.Values.All(t => t.Min == t.Max);
        for (int i = 1; i < sized.Count; i++) thicker &= thick[sized[i]].Min > thick[sized[i - 1]].Max;
        Check(thicker && kindsThinner == 0, $"air: one thickness for each size, thicker the bigger, the largest's the densest ({string.Join(", ", thick.Select(p => $"{p.Key} km {p.Value.Min:F1}"))}); the same kind of air is denser on a bigger world ({kindsThinner} were not)");
        Check(fuelMoons == systems.Count && fuelMoonsBare == fuelMoons, $"vacuum: every start's thruster moon is airless ({fuelMoonsBare} of {fuelMoons})");
        // The owner's bug (17 Nov): three small moons round a start world, every one with air. A start world's own
        // moons are smaller than it, so 8 km, and those are almost all airless.
        Check(homeMoons > systems.Count / 2 && homeMoonsBare >= homeMoons * 0.92, $"vacuum: a start world's own moons are small and {homeMoonsBare} of {homeMoons} airless");
    }

    // ---- 3. Moons: smaller than what they orbit; a giant's any size, with moons of their own -------------

    void Moons(List<StarSystem> systems)
    {
        int moons = 0, notSmaller = 0, smallHolds = 0, ofMoons = 0, ofMoonsWrong = 0, farApart = 0;
        var giantMoonSizes = new SortedDictionary<int, int>();
        foreach (StarSystem s in systems)
            foreach (Orbiter b in Landable(s).Where(b => b.Kind == OrbiterKind.Moon))
            {
                moons++;
                Orbiter p = b.Parent;
                if (p.Kind == OrbiterKind.GasGiant) giantMoonSizes[b.Spec.WidthKm] = giantMoonSizes.GetValueOrDefault(b.Spec.WidthKm) + 1;
                else if (p.Spec == null || b.Spec.WidthKm >= p.Spec.WidthKm) notSmaller++;
                if (p.Spec?.WidthKm == PlanetSpec.Widths[0]) smallHolds++;
                if (p.Kind != OrbiterKind.Moon || p == s.Home) continue;
                // A moon's moon: its parent a giant's moon, inside half the parent's reach against the giant, on the one clock.
                ofMoons++;
                double hill = p.Orbit * p.Radius / p.Parent.Radius / Math.Cbrt(3.0);
                if (p.Parent.Kind != OrbiterKind.GasGiant || b.Orbit > hill * 0.5 + 1.0 || b.Orbit < p.Radius + b.Radius) ofMoonsWrong++;
                double day = Random.Shared.NextDouble() * 400.0;
                if (Math.Abs((s.Position(b, day) - s.Position(p, day)).Length - b.Orbit) > 1.0) farApart++;
            }
        Check(notSmaller == 0 && smallHolds == 0, $"moons: all {moons} are smaller than the world they orbit ({notSmaller} were not), and no 8 km world has one ({smallHolds} did)");
        Check(giantMoonSizes.Count == 4, $"moons: a giant's are of any size ({string.Join(", ", giantMoonSizes.Select(p => $"{p.Key} km x{p.Value}"))})");
        Check(ofMoons > systems.Count / 20 && ofMoonsWrong == 0 && farApart == 0,
            $"moons: {ofMoons} giants' moons have moons of their own, each in its parent's reach ({ofMoonsWrong} not) and going round it on the one clock ({farApart} off their orbit)");
        SameFromEverywhere(systems);
    }

    /// <summary>
    /// Celestial bodies are real: a moon's moon stands in the same direction from the start world's ground, from its
    /// sibling's sky and from space, because every view reads the one model (<see cref="StarSystem.See"/>, <see cref="StarSystem.SeenFrom"/>).
    /// </summary>
    void SameFromEverywhere(List<StarSystem> systems)
    {
        int seen = 0, off = 0;
        foreach (StarSystem s in systems)
            foreach (Orbiter b in s.Bodies.Where(b => b.Kind == OrbiterKind.Moon && b.Parent.Kind == OrbiterKind.Moon && b.Parent != s.Home))
            {
                double day = Random.Shared.NextDouble() * 400.0;
                var eye = new D3(0, s.Home.Radius + 2.0, 0);
                StarSystem.Orientation o = s.Orient(day);
                D3 fromGround = o.ToSystem(s.See(b, day, eye).Direction);
                D3 at = s.Position(s.Home, day) + o.ToSystem(eye), truly = (s.Position(b, day) - at).Normalized;
                StarSystem sibling = s.SeenFrom(b.Parent);
                StarSystem.Orientation so = sibling.Orient(day);
                var near = new D3(0, b.Parent.Radius * 1.5, 0);
                D3 fromParent = so.ToSystem(sibling.See(b, day, near).Direction), trulyThere = (s.Position(b, day) - (s.Position(b.Parent, day) + so.ToSystem(near))).Normalized;
                seen++;
                if (fromGround.Dot(truly) < 1.0 - 1e-9 || fromParent.Dot(trulyThere) < 1.0 - 1e-9) off++;
            }
        Check(seen > 0 && off == 0, $"moons: each of {seen} moons' moons stands where it truly is from the ground and from its parent's sky ({off} did not)");
    }

    // ---- 3b. Orbits aren't one flat plane (owner, 22 Nov) ------------------------------------------------

    /// <summary>An orbit's normal in the system's frame, as <see cref="StarSystem.Position"/> turns it.</summary>
    static D3 Normal(double i, double node) => new(-Math.Sin(i) * Math.Sin(node), Math.Cos(i), -Math.Sin(i) * Math.Cos(node));

    static double Deg(double radians) => radians * 180.0 / Math.PI;

    void Planes(List<StarSystem> systems)
    {
        StarSystem.OrbitPlanes p = StarSystem.Planes;
        int planets = 0, flat = 0, innerOff = 0, outerTilted = 0, wanderers = 0, tooSteep = 0, near = 0, nearOff = 0, far = 0, captured = 0, backwards = 0;
        double worstNear = 0;
        foreach (StarSystem s in systems)
            foreach (Orbiter b in s.Bodies)
            {
                double i = Deg(b.Inclination);
                if (b.Parent == s.Star && (b.Kind is OrbiterKind.Planet or OrbiterKind.GasGiant) && b != s.Home)
                {
                    planets++;
                    if (i <= p.Planets) flat++;
                    else if (b.Habitable <= StarSystem.FrostLine) innerOff++;
                    else if (i <= p.Outer) outerTilted++;
                    else if (i <= p.Wanderer) wanderers++;
                    else tooSteep++;
                    continue;
                }
                if (b.Kind != OrbiterKind.Moon || b == s.Home || b.Parent == s.Home.Parent && b.Orbit == s.Home.Orbit) continue; // the start world, and its sister on its orbit
                if (b.Orbit / b.Parent.Radius < p.LockedRadii)
                {
                    near++;
                    D3 equator = b.Parent == s.Home ? s.Axis : b.Parent.RingNormal;
                    double off = Deg(Math.Acos(Math.Clamp(Normal(b.Inclination, b.Node).Dot(equator), -1.0, 1.0)));
                    worstNear = Math.Max(worstNear, off);
                    if (off > p.NearMoons * 1.5) nearOff++;
                    continue;
                }
                far++;
                if (i > p.FarMoons) captured++;
                if (i > 90.0) backwards++;
            }
        Check(innerOff == 0 && tooSteep == 0 && flat >= planets * 0.9 && outerTilted > 0 && wanderers > 0,
            $"planes: of {planets} worlds round the star {flat} lie within {p.Planets}° of the plane, {outerTilted} outer ones up to {p.Outer}°, {wanderers} wanderers up to {p.Wanderer}° ({innerOff} inner ones off the plane, {tooSteep} steeper)");
        Check(near > 0 && nearOff == 0, $"planes: all {near} moons within {p.LockedRadii} radii keep to their parent's equator, where its rings lie ({worstNear:F1}° off at worst)");
        Check(far > 0 && Math.Abs(captured / (double)far - p.CapturedShare) < 0.03 && backwards > 0,
            $"planes: {captured} of {far} farther moons are captured, past {p.FarMoons}° ({captured * 100.0 / far:F1}%, {p.CapturedShare:P0} wanted), {backwards} going round backwards");
    }

    // ---- 4. Rings ---------------------------------------------------------------------------------------

    /// <summary>A colour's brightness: its brightest channel.</summary>
    static float Bright(Rgb c) => MathF.Max(c.R, MathF.Max(c.G, c.B));

    void Rings(List<StarSystem> systems)
    {
        int moonRings = 0, moonLooks = 0, giants = 0, ringed = 0, rockRings = 0, rockWrong = 0, iceRings = 0, rockRingsIn = 0;
        float iceBright = 1f, rockBright = 0f, crowdedOuter = 0f, openOuter = 9f;
        foreach (StarSystem s in systems)
            foreach (Orbiter b in s.Bodies)
            {
                if (b.Kind == OrbiterKind.Moon)
                {
                    if (b.Rings) moonRings++;
                    if (b != s.Home && b.Spec != null && (ArchetypeLook.For(b.Spec).Ring || b.Spec.Has(Modifier.RingedSky))) moonLooks++;
                    continue;
                }
                if (b.Kind == OrbiterKind.Planet && b.Rings)
                {
                    rockRings++;
                    if (b.Spec.WidthKm < PlanetSpec.StartWidthKm || b.RingOuter >= 2.42) rockWrong++;
                }
                if (b.Kind != OrbiterKind.GasGiant) continue;
                // The odds leave out the start's own giant, whose rings come from its sky's roll; the look is every giant's.
                if (b != s.Neighbour) { giants++; ringed += b.Rings ? 1 : 0; }
                if (!b.Rings) continue;
                if (b.Habitable > StarSystem.FrostLine) iceRings++;
                else rockRingsIn++;
                bool crowded = s.Under(b).Count(m => m.Spec.HighGravity) >= StarSystem.CrowdingMoons;
                if (b.Habitable > StarSystem.FrostLine && !crowded) iceBright = MathF.Min(iceBright, Bright(b.RingColor));
                if (b.Habitable <= StarSystem.FrostLine && !crowded) rockBright = MathF.Max(rockBright, Bright(b.RingColor));
                if (crowded) crowdedOuter = MathF.Max(crowdedOuter, (float)b.RingOuter);
                else openOuter = MathF.Min(openOuter, (float)b.RingOuter);
            }
        Check(moonRings == 0 && moonLooks == 0, $"rings: no moon has rings, in the model or in its own look ({moonRings} did, {moonLooks} looked it)");
        Check(giants > 0 && Math.Abs(ringed / (double)giants - 0.45) < 0.04, $"rings: {ringed} of {giants} giants are ringed (45% wanted)");
        Check(rockWrong == 0 && rockRings < giants / 10, $"rings: {rockRings} rocky worlds have rings, all start-sized or bigger and narrow ({rockWrong} not)");
        Check(iceRings > 0 && rockRingsIn > 0 && iceBright > rockBright && crowdedOuter < openOuter,
            $"rings: the {iceRings} ice rings past the frost line are brighter (at least {iceBright:F2}) than the {rockRingsIn} of rock and dust in (at most {rockBright:F2}); a giant with several large moons keeps a smaller ring (out to {crowdedOuter:F2} radii, others {openOuter:F2} or more)");
    }

    // ---- 5. Drive materials ----------------------------------------------------------------------------

    void DriveMaterials(List<StarSystem> systems)
    {
        int faults = 0, warpWrong = 0, foldWrong = 0;
        foreach (StarSystem s in systems)
        {
            if (s.StartFault() is { } fault) { faults++; GD.Print($"   start fault: {fault}"); }
            foreach (Orbiter b in s.Bodies)
            {
                if (b.Deposits.Contains(PlanetSurface.Skies.Deposit.WarpDrive) && b.Spec is not { Airless: true }) warpWrong++;
                if (b.Deposits.Contains(PlanetSurface.Skies.Deposit.FoldDriveMaterials) && b.Spec is not { HighGravity: true }) foldWrong++;
            }
        }
        Check(faults == 0 && warpWrong == 0 && foldWrong == 0,
            $"drives: in all {systems.Count} systems the start's thruster moon holds warp fuel and the warp drive's materials, and a dangerous high-gravity world a warp away the fold drive's ({faults} faults, {warpWrong} warp drive materials on a world with air, {foldWrong} fold drive materials on a light world)");
    }

    // ---- 3. Suns and gas giants -----------------------------------------------------------------------

    void SunsAndGiants(List<StarSystem> systems)
    {
        int bigs = 0, small = 0, ground = 0, plain = 0, outSized = 0, geoOnGround = 0;
        foreach (StarSystem s in systems)
        {
            double largest = Landable(s).Max(b => b.Radius);
            foreach (Orbiter b in s.Bodies.Where(b => b.Kind is OrbiterKind.Star or OrbiterKind.GasGiant))
            {
                bigs++;
                if (b.Radius <= StarSystem.LargestLandableRadius || b.Radius <= largest) small++;
                if (b.Spec != null) ground++;
                Geography g = b.Geography;
                bool bands = b.Kind == OrbiterKind.GasGiant ? g is { BandCount: > 0 } : g != null;
                if (g == null || !bands || g.Marks.Count == 0 || g.Marks.Any(m => m.Size <= 0 || Math.Abs(m.Latitude) > Math.PI / 2)) plain++;
            }
            if (s.Bodies.Any(b => b.Kind == OrbiterKind.GasGiant)) outSized++;
            geoOnGround += s.Bodies.Count(b => b.Spec != null && b.Geography != null);
        }
        Check(small == 0, $"suns and giants: all {bigs} are bigger than the largest landable world ({StarSystem.LargestLandableRadius / 1000:F1} km radius) and than every world in their system ({small} were not)");
        Check(ground == 0 && geoOnGround == 0, $"suns and giants: none has a surface to land on, and no world with ground wears a giant's bands ({ground} did, {geoOnGround} worlds)");
        Check(plain == 0, $"suns and giants: each has its bands, storms or spots ({plain} had none)");
        Check(outSized == systems.Count, $"suns and giants: every system has a gas giant ({outSized} of {systems.Count})");
    }

    // ---- 6. Every system has a dangerous high-gravity world -----------------------------------------------

    void HeavyWorlds(List<StarSystem> systems)
    {
        int without = 0, heavyHome = 0, total = 0;
        var sizes = new SortedDictionary<int, int>();
        foreach (StarSystem s in systems)
        {
            var heavy = Landable(s).Where(b => b.Spec.HighGravity).ToList();
            if (heavy.Count == 0) without++;
            total += heavy.Count;
            foreach (Orbiter b in heavy) sizes[b.Spec.WidthKm] = sizes.GetValueOrDefault(b.Spec.WidthKm) + 1;
            if (s.Home.Spec.HighGravity) heavyHome++;
        }
        Check(without == 0, $"every system has a dangerous high-gravity world ({without} of {systems.Count} had none; {total} found, by size: {string.Join(", ", sizes.Select(p => $"{p.Key} km x{p.Value}"))})");
        Check(heavyHome == 0, $"the start world itself is never the dangerous one ({heavyHome} were)");
    }

    // ---- 7. A start has a vacuum moon with warp fuel for a first jump ----------------------------------------

    void StartMoons(List<Ground> ground)
    {
        int moons = 0, noMoon = 0, notVacuum = 0, thin = 0, faults = 0, unreachable = 0;
        long least = long.MaxValue;
        foreach (Ground g in ground.Where(g => g.Body == g.System.FuelMoon))
        {
            moons++;
            StarSystem s = g.System;
            Orbiter moon = g.Body;
            if (moon.Parent != s.Home && moon.Parent != s.Home.Parent) noMoon++;
            if (moon.Spec is not { Airless: true }) notVacuum++;
            double cells = g.Ore.GetValueOrDefault(DepositKind.WarpFuel);
            least = Math.Min(least, (long)cells);
            if (cells < FirstJumpCells || g.BiggestWarp < FirstJumpCells) thin++;
            if (s.StartFault() is { } fault) { faults++; GD.Print($"   start fault: {fault}"); }
            if ((s.Position(moon, 0) - s.Position(s.Home, 0)).Length > s.Home.Radius * (StarSystem.ThrusterReachRadii + 1.0) && moon.Parent == s.Home) unreachable++;
        }
        noMoon += Mapped - moons;
        Check(noMoon == 0 && notVacuum == 0, $"start: every system has a vacuum moon to start from ({noMoon} none, {notVacuum} with air)");
        Check(thin == 0, $"start: each of the {moons} moons holds warp fuel for a first jump ({FirstJumpCells} cells) in one deposit and the whole ({thin} too thin; the least {least} cells)");
        Check(faults == 0 && unreachable == 0, $"start: the moon is within thrusters' reach and nothing else the design promises is missing ({faults} faults, {unreachable} out of reach)");
    }

    /// <summary>What a deposit yields dug out, in ore: its sphere of ore at the terrain's radius, a dug cell giving 12 (the poorest grade, 0.6).</summary>
    static double Units(Planets.Gen.Deposit d)
    {
        float r = GalHack.Terrain.PlanetTerrainSource.OreRadius(d);
        return 4.0 / 3.0 * Math.PI * r * r * r * 12 * 0.6;
    }

    /// <summary>
    /// What one landable world's coarse map holds, kept in place of the map: a large world's map and deposit index run
    /// to megabytes, and thousands are walked.
    /// </summary>
    sealed record Ground(StarSystem System, Orbiter Body, Dictionary<DepositKind, double> Ore, double BiggestWarp, bool Dry, BarrenRead? Barren);

    /// <summary>Every landable world of the mapped systems, each coarse map made, read and dropped on a worker: no more than one a core is held at once.</summary>
    static List<Ground> Survey(List<StarSystem> systems)
    {
        var worlds = systems.SelectMany(s => Landable(s).Select(b => (System: s, Body: b))).ToList();
        var ground = new Ground[worlds.Count];
        System.Threading.Tasks.Parallel.For(0, worlds.Count, i =>
        {
            WorldMap map = BodySurfaces.Coarse(worlds[i].Body.Spec);
            var ore = map.Deposits.All().GroupBy(d => d.Kind).ToDictionary(g => g.Key, g => g.Sum(Units));
            double biggest = map.Deposits.All().Where(d => d.Kind == DepositKind.WarpFuel).Select(Units).DefaultIfEmpty(0).Max();
            ground[i] = new Ground(worlds[i].System, worlds[i].Body, ore, biggest, Dry(map), ReadBarren(map));
        });
        return ground.ToList();
    }

    // ---- 4 and 5. The jump fuels ------------------------------------------------------------------------------

    void Fuels(List<Ground> ground)
    {
        int warpOn = 0, warpWrong = 0, hyperOn = 0, hyperMissing = 0, hyperElsewhere = 0, bare = 0, heavy = 0;
        foreach (Ground g in ground)
        {
            Orbiter b = g.Body;
            bool warp = g.Ore.ContainsKey(DepositKind.WarpFuel), crystal = g.Ore.ContainsKey(DepositKind.Crystal);
            if (warp) warpOn++;
            bool cold = b.Spec.Airless; // vacuum worlds alone, icy or not (owner, 13 Nov)
            if (warp != cold) warpWrong++;
            if (cold) bare++;
            if (b.Spec.HighGravity)
            {
                heavy++;
                if (crystal) hyperOn++;
                else hyperMissing++;
            }
            else if (crystal && b.Spec.Archetype != Archetype.Alien) hyperElsewhere++; // only the alien crystal world's own signature, else the heavy worlds' alone
        }
        Check(warpWrong == 0 && bare > 0, $"warp fuel (pentaxite): it lies on {warpOn} worlds, every vacuum one of {bare} and no other ({warpWrong} wrong)");
        Check(hyperMissing == 0 && heavy > 0, $"fold fuel: every one of the {heavy} dangerous high-gravity worlds holds some ({hyperMissing} did not)");
        Check(hyperElsewhere == 0, $"fold fuel: it lies nowhere else, but on the alien crystal worlds that always had it ({hyperElsewhere} other worlds had it)");
    }

    // ---- 8. Every world has what the cheapest flyer takes ------------------------------------------------------------

    void Flyer(List<Ground> ground)
    {
        GameData data = GameData.Shared;
        ItemCatalogue items = data.Items;
        string folder = GameData.Own("data/blueprints");
        Expansion cheapest = null;
        string name = null;
        foreach (string file in Directory.GetFiles(folder, "*.gbp"))
        {
            Structure s;
            try { s = data.Blueprint(Path.GetFileName(file)); }
            catch { continue; }
            if (!StructureStats.Compute(s).Flies) continue;
            Expansion e = items.Graph.Expand(items.Blocks.Total(BuildingBridge.BlocksOf(s)));
            if (cheapest == null || e.Leaves.Total < cheapest.Leaves.Total) (cheapest, name) = (e, Path.GetFileName(file));
        }
        Check(cheapest != null, "flyer: the stock blueprints include a flyer");
        if (cheapest == null) return;

        // What it takes, as ore: each raw leaf of its recipe tree with its count (dug stone is everywhere).
        var need = new Dictionary<DepositKind, int>();
        foreach (var (id, n) in cheapest.Leaves)
        {
            ItemDef leaf = items.Item(id);
            if (leaf.Source == ItemSource.Dug) continue;
            if (!Enum.TryParse(leaf.DepositKind, out DepositKind kind)) { Check(false, $"flyer: {id} is a leaf of the cheapest flyer and no deposit gives it"); return; }
            need[kind] = need.GetValueOrDefault(kind) + n;
        }
        GD.Print($"(the cheapest flyer is {name}: {cheapest.Leaves.Total} ore, tier {cheapest.MaxTier}: {items.Describe(cheapest.Leaves)})");

        var short1 = new List<string>();
        foreach (Ground g in ground)
            foreach (var (kind, n) in need)
                if (g.Ore.GetValueOrDefault(kind) < n) short1.Add($"{g.Body.Spec} lacks {kind} ({g.Ore.GetValueOrDefault(kind):F0} of {n})");
        Check(short1.Count == 0, $"flyer: all {ground.Count} worlds hold the {string.Join(", ", need.Select(p => $"{p.Value} {p.Key}"))} of the cheapest flyer ({short1.Count} fell short{(short1.Count > 0 ? ": " + short1[0] : "")})");
    }

    // ---- 4 and 5, loot: in space places and in ships -----------------------------------------------------------------

    void Loot()
    {
        GameData data = GameData.Shared;
        PoiGenerator.Gen.PoiLibrary library = WorldChoice.Library;
        foreach (string fuel in new[] { "warp-cell", "fold-crystal" })
            Check(data.Items.Has(fuel) && library.Loot.Any(l => l.Id == fuel && l.Tags.Contains("ship")), $"loot: {fuel} is in the places' loot pool, leaning to ships");

        // Ships on the ground: every family tagged ship, many seeds.
        int ships = 0, withWarp = 0, withHyper = 0;
        var rng = new Random();
        foreach (var family in library.Families.Where(f => f.Tags.Contains("ship")))
            for (int i = 0; i < 40; i++)
            {
                var inst = PoiGenerator.Gen.PoiFactory.Generate(library, family, i % Math.Max(1, family.Variants.Count), family.Rarity, PoiGenerator.Gen.PoiState.Abandoned, (ulong)rng.NextInt64());
                ships++;
                var ids = inst.Caches.SelectMany(c => c.Items).Select(l => l.Id).ToHashSet();
                if (ids.Contains("warp-cell")) withWarp++;
                if (ids.Contains("fold-crystal") || ids.Contains("fold-crystal-raw")) withHyper++;
            }
        Check(withWarp > 0 && withWarp < ships, $"loot: {withWarp} of {ships} grounded or crashed ships hold warp cells, some and not all");
        Check(withHyper > 0 && withHyper < ships, $"loot: {withHyper} of {ships} grounded or crashed ships hold fold crystal (fold fuel), some and not all");

        // Wrecks in space: the start system's planned derelicts, fresh seeds.
        int sites = 0, spaceWarp = 0;
        OrbitRules rules = OrbitRules.Shared;
        for (int i = 0; i < 30; i++)
        {
            var map = new GlobeMap(16000, 8000, 16000 / Math.Tau);
            foreach (DerelictPlan p in Derelicts.Plan(library, (ulong)rng.NextInt64(), map, 16000 / Math.Tau * 1.2, rules, data.Catalogue, data.Items))
            {
                sites++;
                if (p.Loot["warp-cell"] > 0) spaceWarp++;
            }
        }
        Check(spaceWarp > 0 && spaceWarp < sites, $"loot: {spaceWarp} of {sites} wrecks in space hold warp cells, some and not all");
    }

    // ---- 10. Vacuum worlds have no liquid water -------------------------------------------------------------------

    /// <summary>No sea, lake or river on an airless world: no water or river cell, no ground under the waterline. Ice deposits stay.</summary>
    static bool Dry(WorldMap map)
    {
        for (int k = 0; k < map.Count; k++)
            if (map.Has(k, CellFlags.Water) || map.Has(k, CellFlags.River) || map.Elevation[k] < 0f || map.RiverFlow[k] > 0f) return false;
        return true;
    }

    void NoWaterOnBareWorlds(List<Ground> ground, List<StarSystem> systems)
    {
        int bare = 0, wet = 0, wetOnes = 0, withAir = 0, watery = 0, ice = 0;
        foreach (Ground g in ground)
        {
            if (!g.Body.Spec.Airless)
            {
                withAir++;
                if (!g.Dry) watery++;
                continue;
            }
            bare++;
            if (!g.Dry) wet++;
            if ((g.Body.Spec.Archetype == Archetype.Ice || g.Body.Spec is { Archetype: Archetype.Barren, Variant: Archetypes.Icy }) && g.Ore.ContainsKey(DepositKind.Water)) ice++;
        }
        // The same at the game's own resolution, on every start's moon (each map dropped once read).
        var full = new bool[systems.Count];
        System.Threading.Tasks.Parallel.For(0, systems.Count, i => full[i] = Dry(PlanetGenerator.Generate(systems[i].FuelMoon.Spec)));
        wetOnes = full.Count(d => !d);
        Check(bare > 0 && wet == 0, $"no water: none of {bare} airless worlds has a sea, lake or river, or ground under the waterline ({wet} had) ; {withAir} worlds with air keep theirs ({watery} have some)");
        Check(watery > 0, "no water: the worlds with air still have their seas and rivers (the rule is the vacuum's alone)");
        Check(wetOnes == 0, $"no water: and at the game's own resolution none of the {full.Length} start moons has any ({wetOnes} had)");
        Check(ice > 0, $"no water: ice deposits stay on airless ice worlds ({ice} such worlds had them)");
    }

    // ---- 9. No map marker for warp fuel -----------------------------------------------------------------------

    void NoMarkers(List<StarSystem> systems)
    {
        var fuels = new[] { "warp-fuel", "fold-fuel", "hyperdrive-fuel", "hyperspace-fuel", "warp-cell", "fold-crystal" };
        int marked = systems.Sum(s => s.Bodies.Sum(b => b.Deposits.Count(d => fuels.Any(f => d.Contains(f, StringComparison.OrdinalIgnoreCase)))));
        Check(marked == 0, $"no markers: no planet or moon in {systems.Count} systems is marked as holding a jump fuel ({marked} were)");
        Check(DepositFinder.Kinds.All(k => k is not (DepositKind.WarpFuel or DepositKind.Crystal)), "no markers: the scanner and the compass never look for a jump fuel");
    }
}
