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
            foreach (PoiSite p in w.Pois)
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
