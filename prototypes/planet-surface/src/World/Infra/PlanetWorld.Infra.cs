using System.Collections.Generic;
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

    static partial void PlanAround(PlanetSpec spec, PoiCatalog catalog, WorldMap map, PlanetTerrainSource bare, List<PoiSite> pois, WorldProfile profile, List<TerrainStamp> stamps, ref object around)
    {
        // The roads, railways, bridges and tunnels between the places, laid after the places' own stamps.
        var infra = InfraNetwork.Plan(map, bare, pois, profile, Hash.Of(spec.Seed, 0x1AF7));
        var ruins = new List<FlavourRuin>();
        // Flavour ruins by the roads and in the country, their pads laid before the roads.
        infra.Ruins = FlavourRuins.Plan(infra, catalog?.Library, map, bare, pois, profile, Hash.Of(spec.Seed, 0xF1A7), ruins);
        foreach (FlavourRuin ruin in infra.Ruins) stamps.Add(ruin.Stamp);
        stamps.AddRange(infra.Stamps());
        around = infra;
    }

    static partial void FinishAround(PlanetSpec spec, PlanetTerrainSource source, List<PoiSite> pois, object around)
    {
        if (around is not InfraNetwork infra) return;
        // Power lines and wrecked traffic stand on the finished ground, beside the roads.
        infra.Power = PowerLines.Plan(infra, source, Hash.Of(spec.Seed, 0x90E7));
        infra.Wrecks = Wrecks.Plan(infra, source, pois, Hash.Of(spec.Seed, 0x3EC5));
    }
}
