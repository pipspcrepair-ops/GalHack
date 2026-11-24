using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PlanetSurface.Infra;

namespace PlanetSurface.World;

public sealed partial class PlanetWorld
{
    /// <summary>
    /// What the world stands on the ground within a circle (centre x, z; metres), a kind for each thing that reaches it: "place",
    /// "city" (a lot of the ruined city), "ruin", "town" (a house of the start town), "wreck", "road", "rail", "street"
    /// (the town's or the city's), "pole", "pylon", "power" (a substation), "crater", "ore tell" (the rocks over a deposit), "landing". The planet lays all of these out
    /// before anything else is sited, so whatever sites something later (the start bunker) asks here.
    /// </summary>
    public IEnumerable<string> Occupants(double x, double z, float radius)
    {
        InfraNetwork net = Infra;
        bool Reaches(double cx, double cz, double halfX, double halfZ)
        {
            double dx = Math.Max(Math.Abs(Mathx.WrapDelta(cx - x, Width)) - halfX, 0), dz = Math.Max(Math.Abs(Mathx.WrapDelta(cz - z, Height)) - halfZ, 0);
            return dx * dx + dz * dz < (double)radius * radius;
        }
        bool Stamped(TerrainStamp s) => Reaches((s.MinX + s.MaxX) * 0.5, (s.MinZ + s.MaxZ) * 0.5, s.ReachX, s.ReachZ);
        foreach (Poi.PoiSite p in Pois)
            if (p.Stamps.Any(Stamped)) yield return net.City?.Sites.Contains(p) == true ? "city" : "place";
        foreach (FlavourRuin r in net.Ruins)
            if (Stamped(r.Stamp)) yield return net.Town?.Houses.Contains(r) == true ? "town" : "ruin";
        foreach (WreckSite w in net.Wrecks)
        {
            var (wx, wz) = w.World(w.Width * 0.5f, w.Length * 0.5f);
            float half = MathF.Sqrt(w.Width * w.Width + w.Length * w.Length) * 0.5f;
            if (Reaches(wx, wz, half, half)) yield return "wreck";
        }
        // A deck's width either side of its line (a bridge, a viaduct and an embankment stand on it): the nearest line within reach.
        const float Widest = 10f;
        if (net.Nearest(x, z, radius + Widest, out _, out double d) is { } route && d - route.HalfWidth < radius)
            yield return net.Town?.Streets.Contains(route) == true || net.City?.Streets.Contains(route) == true ? "street" : route.Kind == RouteKind.Rail ? "rail" : "road";
        foreach (PowerPole pole in net.Power.Poles)
        {
            // A pylon's feet spread a few metres, a substation has its yard.
            double reach = radius + (pole.Pylon ? 8 : pole.Role == PowerRole.Substation ? 12 : 1);
            if (Math.Pow(Mathx.WrapDelta(pole.X - x, Width), 2) + Math.Pow(Mathx.WrapDelta(pole.Z - z, Height), 2) < reach * reach)
                yield return pole.Role == PowerRole.Substation ? "power" : pole.Pylon ? "pylon" : "pole";
        }
        foreach (TerrainStamp crater in net.Craters)
            if (Stamped(crater)) yield return "crater";
        // An ore deposit's tell (InfraLayer.OrePieces: rocks stood over the deposits that show): its slabs and boulders spread to the body's edge.
        float seen = DepositField.MaxBodyRadius + radius;
        foreach (Deposit dep in Map.Deposits.InRect(x - seen, z - seen, x + seen, z + seen))
        {
            float reach = PlanetTerrainSource.OreRadius(dep) * 1.05f + radius;
            if (Math.Pow(Mathx.WrapDelta(dep.X - x, Width), 2) + Math.Pow(Mathx.WrapDelta(dep.Y - z, Height), 2) < reach * reach && InfraLayer.HasTell(this, dep)) yield return "ore tell";
        }
        foreach (LandingSpot l in net.LandingSpots)
            if (Reaches(l.X, l.Z, l.Radius, l.Radius)) yield return "landing";
    }
}
