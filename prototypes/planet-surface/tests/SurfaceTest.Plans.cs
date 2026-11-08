using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.Poi;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Archetype = Planets.Gen.Archetype;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface;

/// <summary>Worlds laid out by their plans: fresh worlds every run, each read for reasons.</summary>
public partial class SurfaceTest
{
    async Task PlanChecks()
    {
        var rand = new Random();
        int worlds = 0, planned = 0, sited = 0, sewageWorks = 0, unserved = 0, rails = 0, loose = 0, plants = 0, unfed = 0, inWild = 0, ruinsTotal = 0, breaks = 0;
        double emptyShare = 0, slowest = 0;
        int worstStar = 0, junctions = 0, served = 0, wobbles = 0, streets = 0;
        double roadKm = 0;
        var themes = new Dictionary<string, int>();
        string first = "";
        void Note(string s) { if (first.Length == 0) first = $" (first: {s})"; }
        var kinds = new[] { Archetype.Temperate, Archetype.Arid, Archetype.Temperate, Archetype.Ice, Archetype.Lava, Archetype.Temperate };
        foreach (Archetype kind in kinds)
        {
            var watch = Stopwatch.StartNew();
            var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), kind, 0);
            PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
            slowest = Math.Max(slowest, watch.Elapsed.TotalSeconds);
            worlds++;
            InfraNetwork net = w.Infra;
            RegionPlan plan = net.RegionPlan;
            if (plan == null)
            {
                GD.Print($"      {spec.Archetype}: {w.Profile}: no plan, {w.Pois.Count} places");
                continue;
            }
            planned++;
            themes[plan.Roll.Theme.Id] = themes.GetValueOrDefault(plan.Roll.Theme.Id) + 1;
            var roles = w.Pois.GroupBy(p => p.Planned?.Role ?? "none").Select(g => $"{g.Key} {g.Count()}");
            var occs = w.Pois.Where(p => p.Instance.Occupancy != null).GroupBy(p => p.Instance.Occupancy.Main).Select(g => $"{g.Key} {g.Count()}");
            GD.Print($"      {spec.Archetype} {w.Width / 1000:F0} km, {plan.Roll}: {plan.Regions.Count} regions, {w.Pois.Count}/{plan.Places.Count} seated ({string.Join(", ", roles)}), people {string.Join(", ", occs)}, " +
                     $"{net.Routes.Count} routes, {net.Ruins.Count} ruins, {net.Power.Poles.Count} poles, {w.BuildMs / 1000:F1} s");
            // (A city's buildings are its plan's, not the region plan's: they have their lots.)
            foreach (PoiSite p in w.Pois.Where(p => p.Siting != "city"))
            {
                sited++;
                if (p.Planned == null) { Note($"{p.Name} has no reason"); unserved++; continue; }
                if (string.IsNullOrEmpty(p.Planned.Why)) { Note($"{p.Name} has no reason"); unserved++; }
                if (p.Planned.Breaks) breaks++;
            }
            foreach (PoiSite s in w.Pois.Where(p => p.Family.Id == "sewage_works"))
            {
                sewageWorks++;
                if (s.Planned?.Serves is not int hub || hub < 0 || plan.Places[hub].Seated is not PoiSite town) { unserved++; Note($"a sewage works serves nothing"); continue; }
                double d = plan.Dist(s.Center.X, s.Center.Z, town.Center.X, town.Center.Z);
                bool joined = net.PlannedRoutes.Any(r => r.A == s && r.B == town || r.A == town && r.B == s);
                if (d > 2000 || !joined) { unserved++; Note($"a sewage works {d:F0} m from its town, joined {joined}"); }
            }
            // A network, not a star: most places come off a road at a junction, and no town's gate gathers them all.
            var roads = net.Routes.Where(r => r.Kind != RouteKind.Rail).ToList();
            int leastGates = net.Places.Count == 0 ? 0 : net.Places.Max(p => roads.Count(r => r.From == p || r.To == p));
            worstStar = Math.Max(worstStar, leastGates);
            // Roads are straight: over each 40 m the heading holds or turns one way; back and forth is a wobble.
            foreach (InfraRoute r in roads.Where(r => r.Kind != RouteKind.Track && r.Count > 40))
            {
                const int Stride = 10;
                double last = 0;
                for (int i = Stride; i + Stride < r.Count; i += Stride)
                {
                    double a0 = Math.Atan2(r.Z[i] - r.Z[i - Stride], r.X[i] - r.X[i - Stride]), a1 = Math.Atan2(r.Z[i + Stride] - r.Z[i], r.X[i + Stride] - r.X[i]);
                    double turn = Math.IEEERemainder(a1 - a0, Math.PI * 2);
                    if (Math.Abs(turn) < 0.05) continue;
                    if (last != 0 && Math.Sign(turn) != Math.Sign(last)) wobbles++;
                    last = turn;
                }
                roadKm += r.Count * InfraNetwork.Spacing / 1000.0;
            }
            streets += net.TownStreets;
            junctions += net.Junctions;
            served += net.Places.Count;
            GD.Print($"      roads: {roads.Count(r => r.Kind == RouteKind.Highway)} highway, {roads.Count(r => r.Kind == RouteKind.Road)} road, {roads.Count(r => r.Kind == RouteKind.Track)} track stretches; {net.Junctions} junctions, {net.TownStreets} town streets, {net.Surveyed} straight runs, {net.Wandered} wandered, busiest gate {leastGates}, {net.Isolated.Count} alone, {net.SeaSeparated} over the sea, {net.Unrouted} unrouted");
            foreach (InfraRoute r in net.Routes.Where(r => r.Kind == RouteKind.Rail))
            {
                rails++;
                bool Depot(PoiSite p) => p?.Planned?.Role is "depot" or "port" or "industry";
                if (!Depot(r.From) && !Depot(r.To)) { loose++; Note($"a railway from {r.From?.Name ?? "a junction"} to {r.To?.Name ?? "a junction"} ends at no depot"); }
            }
            foreach (PlannedLink l in plan.Links.Where(l => l.Kind == "power"))
            {
                plants++;
                if (plan.Places[l.A].Seated is PoiSite a && plan.Places[l.B].Seated is PoiSite && plan.Roll.Power != "none" && !w.Infra.Power.Poles.Any()) { unfed++; Note($"{a.Name} feeds nothing"); }
            }
            // Empty land between: most of a world's dry ground is in no region at all.
            int dry = 0, wild = 0;
            for (double z = 200; z < w.Height; z += 400)
                for (double x = 200; x < w.Width; x += 400)
                {
                    if (w.Source.SurfaceHeight(x, z) < 3f) continue;
                    dry++;
                    if (plan.RegionAt(x, z) == null) wild++;
                }
            emptyShare += wild / Math.Max(1.0, dry);
            foreach (FlavourRuin ruin in net.Ruins.Where(r => !r.Family.Id.Contains("wayside")))
            {
                ruinsTotal++;
                if (plan.RegionAt(ruin.Center.X, ruin.Center.Z) == null) inWild++;
            }
        }
        Check(worlds == kinds.Length && planned >= kinds.Length / 2, $"plans: {planned} of {worlds} fresh worlds are laid out by a plan ({string.Join(", ", themes.Select(t => $"{t.Key} {t.Value}"))}), slowest build {slowest:F1} s");
        Check(unserved == 0 && sewageWorks > 0, $"every one of {sited} places has its reason, and each of {sewageWorks} sewage works serves a town it stands near and is joined to ({unserved} not){first}");
        Check(loose == 0, $"every one of {rails} railway stretches ends at a depot, a works or a port ({loose} not){first}");
        Check(unfed == 0, $"{plants} plants feed their towns ({unfed} not)");
        Check(planned == 0 || wobbles <= roadKm * 1.5, $"roads run straight: {wobbles} back-and-forth bends in {roadKm:F0} km of highway and road");
        Check(planned == 0 || streets > 0, $"towns have grids of streets: {streets} laid");
        Check(planned == 0 || junctions >= served / 3 && worstStar <= 6, $"roads form a network: {junctions} junctions for {served} places, the busiest place has {worstStar} roads out of it (no stars)");
        Check(planned == 0 || emptyShare / planned > 0.3, $"empty land between places: {emptyShare / Math.Max(1, planned):P0} of dry land is in no region");
        Check(inWild <= ruinsTotal / 8, $"small ruins stand in the regions, not scattered: {inWild} of {ruinsTotal} out in the wilds");

        // The same seed lays out the same world.
        var again = PlanetSpec.Roll((ulong)rand.NextInt64(), Archetype.Temperate, 0);
        var one = PoiSitingOnly(again);
        var two = PoiSitingOnly(again);
        Check(one.SequenceEqual(two) && one.Count > 0, $"the same seed lays out the same world, place for place ({one.Count})");
        await Task.CompletedTask;
    }

    static List<string> PoiSitingOnly(PlanetSpec spec)
    {
        PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
        return w.Pois.Select(p => $"{p.Family.Id} {p.Origin.X} {p.Origin.Z} {p.Instance.State} {p.Instance.Occupancy}").ToList();
    }
}
