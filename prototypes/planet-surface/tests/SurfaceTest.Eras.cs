using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.Poi;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Archetype = Planets.Gen.Archetype;
using Material = PoiGenerator.Gen.Material;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface;

/// <summary>Places by era on fresh worlds of each age: their own layouts, their age's tech and look, one people to a region.</summary>
public partial class SurfaceTest
{
    static ulong LayoutPrint(Layout l)
    {
        ulong h = 1469598103934665603UL;
        foreach (var (p, b) in l.Blocks.Blocks())
            h = PoiGenerator.Gen.Hash.Combine(h, PoiGenerator.Gen.Hash.Combine((ulong)PoiGenerator.Gen.Hash.Cell(p), (ulong)b.Type << 8 | (ulong)b.Material));
        return h;
    }

    async Task EraChecks()
    {
        var rand = new Random();
        double fixedMs = 0, freshMs = 0;
        int worlds = 0, repeats = 0, pairs = 0, overTech = 0, lowLaunch = 0, lowFabric = 0, lowModern = 0, highFabric = 0, highStellar = 0, houses = 0, mixedRegions = 0, regions = 0, strangeRegions = 0;
        string first = "";
        void Note(string s) { if (first.Length == 0) first = $" (first: {s})"; }
        var modern = new HashSet<Material> { Material.Concrete, Material.Steel, Material.Alloy, Material.Resin, Material.Armour };
        var stellar = new HashSet<Material> { Material.Alloy, Material.Resin, Material.Glass, Material.Lumen };
        try
        {
            foreach (TechLevel era in new[] { TechLevel.Low, TechLevel.Mid, TechLevel.High, TechLevel.Low })
            {
                WorldForce.Current = new WorldForce { Before = era };
                var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), Archetype.Temperate, 0);
                // The same world laid out the old way (most families from one authored seed) and the new, timed side by side.
                PoiSiting.FixedLayouts = true;
                await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
                double was = PoiSiting.LastLayoutMs;
                PoiSiting.FixedLayouts = false;
                PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
                double now = PoiSiting.LastLayoutMs;
                fixedMs += was;
                freshMs += now;
                worlds++;
                WorldProfile profile = w.Profile;
                var places = w.Pois.Where(p => p.Siting != "city").ToList();
                foreach (var g in places.GroupBy(p => p.Family.Id).Where(g => g.Count() > 1))
                {
                    var prints = g.Select(p => LayoutPrint(p.Instance.Layout)).ToList();
                    pairs += prints.Count - 1;
                    int dup = prints.Count - prints.Distinct().Count();
                    repeats += dup;
                    if (dup > 0) Note($"two {g.Key} alike on a {WorldProfile.Tag(era)} world");
                }
                foreach (PoiSite p in w.Pois)
                {
                    if (!profile.TechFits(p.Family)) { overTech++; Note($"{p.Family.Id} on a {WorldProfile.Tag(profile.Before)}/{WorldProfile.Tag(profile.Now)} world"); }
                    if (profile.Before == TechLevel.Low && p.Family.Tags.Contains("scrappy")) lowLaunch++;
                    Layout l = p.Instance.Layout;
                    if (l.Tech == null || !EraPass.Applies(l)) continue;
                    EraKit kit = EraBook.Current.Of(l.Tech.Value);
                    foreach (var (_, b) in l.Blocks.Blocks())
                    {
                        if (b.Role is not (Role.Wall or Role.Roof) || b.Type is not (BlockType.Cube or BlockType.Ramp or BlockType.Corner) || kit.Keep.Contains(b.Type)) continue;
                        if (l.Tech == TechLevel.Low) { lowFabric++; if (modern.Contains(b.Material)) lowModern++; }
                        if (l.Tech == TechLevel.High) { highFabric++; if (stellar.Contains(b.Material)) highStellar++; }
                    }
                }
                // One people to a region: every house of a region (or a stretch of open country) strange, or none.
                var byRegion = new Dictionary<int, (int Strange, int Regular)>();
                foreach (FlavourRuin r in w.Infra.Ruins)
                    foreach (HomeNote h in r.Layout.Extras.Homes)
                    {
                        var (cx, cz) = r.Center;
                        int key = FlavourRuins.CultureAt(w.Infra.RegionPlan, w.Map, cx, cz);
                        bool odd = Main.Catalog.Library.Homes.Find(h.Family)?.Odd == true;
                        var t = byRegion.GetValueOrDefault(key);
                        byRegion[key] = odd ? (t.Strange + 1, t.Regular) : (t.Strange, t.Regular + 1);
                        houses++;
                    }
                regions += byRegion.Count;
                strangeRegions += byRegion.Values.Count(v => v.Strange > 0 && v.Regular == 0);
                mixedRegions += byRegion.Values.Count(v => v.Strange > 0 && v.Regular > 0);
                GD.Print($"      {WorldProfile.Tag(era)} world: {w.Pois.Count} places, {w.Infra.Ruins.Count} ruins, laid out in {was:F0} ms the old way and {now:F0} ms each its own");
            }
        }
        finally
        {
            WorldForce.Current = new WorldForce();
            PoiSiting.FixedLayouts = false;
        }
        Check(repeats == 0 && pairs > 0, $"no two spawns of a family are one building: {repeats} alike in {pairs} pairs{first}");
        Check(overTech == 0, $"no place needs more tech than its world had: {overTech} over{first}");
        Check(lowLaunch == 0, $"no launch field on a low-tech world: {lowLaunch}");
        Check(lowFabric > 0 && lowModern < lowFabric * 0.08, $"low-tech places read low: {lowModern} of {lowFabric} wall and roof blocks modern");
        Check(highFabric > 0 && highStellar > highFabric * 0.55, $"high-tech places read high: {highStellar} of {highFabric} wall and roof blocks alloy, resin, glass or light");
        Check(mixedRegions == 0 && houses > 0, $"no region mixes strange and regular houses: {mixedRegions} mixed of {regions} ({strangeRegions} strange) over {houses} houses");
        Check(freshMs <= fixedMs * 1.35 + 400, $"laying out each spawn afresh keeps its time: {freshMs / worlds:F0} ms a world against {fixedMs / worlds:F0} ms the old way");
    }
}
