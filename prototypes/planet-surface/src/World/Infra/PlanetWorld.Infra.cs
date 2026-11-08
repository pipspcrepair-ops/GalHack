using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PlanetSurface.Infra;
using PoiCatalog = PlanetSurface.Poi.PoiCatalog;
using PoiSite = PlanetSurface.Poi.PoiSite;
using WorldProfile = PoiGenerator.Gen.WorldProfile;

namespace PlanetSurface.World;

/// <summary>The world round the places: the old world's infrastructure, its power lines and its abandoned vehicles.</summary>
public sealed partial class PlanetWorld
{
    /// <summary>What is left of the old world's roads, railways, bridges and tunnels (none on a world never inhabited).</summary>
    public InfraNetwork Infra => _around as InfraNetwork ?? new InfraNetwork();

    /// <summary>A start world's ruined town, where the start begins: the slice's start-site search starts from its <see cref="StartTown.Edge"/>.</summary>
    public StartTown StartTown => Infra.Town;

    static partial void PlanAround(PlanetSpec spec, PoiCatalog catalog, WorldMap map, PlanetTerrainSource bare, List<PoiSite> pois, WorldProfile profile, LandingSite landing, List<TerrainStamp> stamps,
        PoiGenerator.Gen.WorldRoll roll, List<PoiGenerator.Gen.Strike> blasts, ref object around)
    {
        // The roads, railways, bridges and tunnels between the places, laid after the places' own stamps.
        var infra = InfraNetwork.Plan(map, bare, pois, profile, Hash.Of(spec.Seed, 0x1AF7));
        // A start world begins at the edge of a ruined town by the landing site.
        var ruins = new List<FlavourRuin>();
        if (PlanetSurface.Poi.PoiSiting.StartWorld)
            infra.Town = StartTown.Plan(infra, catalog?.Library, map, bare, pois, profile, landing, Hash.Of(spec.Seed, 0x57A2), ruins);
        // A city on a world whose layers built one: its streets are the network's, its buildings places of their own.
        infra.City = CityPlan.Plan(infra, catalog?.Library, map, bare, pois, profile, landing, Hash.Of(spec.Seed, 0xC171));
        if (infra.City != null)
        {
            pois.AddRange(infra.City.Sites);
            stamps.AddRange(infra.City.Stamps);
            foreach (PoiSite p in infra.City.Sites) stamps.Add(p.Stamp);
            Godot.GD.Print($"city: {infra.City.Streets.Count} streets, {infra.City.Lots.Count} lots, {infra.City.Sites.Count} buildings, fall {infra.City.Fall:F2}, {infra.City.BridgeAnchors.Count} bridge anchors, {infra.City.PlanMs:F0} ms {infra.City.Why}");
        }
        // A war's strikes, on what was worth striking: planned once the places, the city and the roads are
        // known, each ground burst's crater beside its target (laid after the places' own, before the ruins'
        // pads and the roads, which keep off it), the bridges they hit dropped. The world's blasts are one list.
        List<PoiGenerator.Gen.Strike> strikes = StrikeSites.Plan(catalog?.Library.Strikes, roll, infra, map, bare, pois, profile, landing, ruins,
            PlanetSurface.Poi.PoiSiting.StartWorld, Hash.Of(spec.Seed, 0x57B1), stamps);
        blasts.AddRange(strikes);
        infra.Blasts.AddRange(blasts);
        stamps.AddRange(infra.Craters);
        if (strikes.Count > 0) Godot.GD.Print($"strikes: {strikes.Count} ({strikes.Count(s => s.Bowl)} craters, {strikes.Count(s => s.Air && !s.Naval)} airbursts, {strikes.Count(s => s.Naval)} on the water, {strikes.Count(s => s.Miss)} missed, {strikes.Count(s => s.Wild)} over open country, {strikes.Count(s => s.Within)} in the towns they hit, {strikes.Count(s => s.Field)} shell holes) on {infra.Targets.Count} targets in {infra.StrikeMs:F0} ms");
        // Flavour ruins by the roads and in the country, their pads laid before the roads.
        infra.Ruins = FlavourRuins.Plan(infra, catalog?.Library, map, bare, pois, profile, Hash.Of(spec.Seed, 0xF1A7), ruins);
        foreach (FlavourRuin ruin in infra.Ruins) stamps.Add(ruin.Stamp);
        stamps.AddRange(infra.Stamps());
        foreach (FlavourRuin ruin in infra.Ruins) if (ruin.Drive != null) stamps.Add(ruin.Drive);
        // What runs under a town: its ways in on pads of their own, after the roads so they win there.
        if (infra.Town is StartTown town)
        {
            var ground = new PlanetTerrainSource(map, stamps);
            town.FindEdge(ground);
            if (town.Under is UnderTown under)
            {
                under.Plan(ground, pois);
                stamps.AddRange(under.Stamps);
            }
        }
        // The ground each place's people made round it, painted by the terrain shader: its pads are marked so the mesh leaves them to it.
        infra.Grounds = MadeGrounds.Plan(infra, pois, stamps, profile, map.WidthMeters, map.HeightMeters);
        around = infra;
    }

    static partial void FinishAround(PlanetSpec spec, PlanetTerrainSource source, List<PoiSite> pois, object around)
    {
        if (around is not InfraNetwork infra) return;
        source.Grounds = infra.Grounds;
        // Power lines and wrecked traffic stand on the finished ground, beside the roads.
        infra.Power = PowerLines.Plan(infra, source, Hash.Of(spec.Seed, 0x90E7));
        infra.Wrecks = Wrecks.Plan(infra, source, pois, Hash.Of(spec.Seed, 0x3EC5));
    }
}
