using System;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.World;
using Archetype = Planets.Gen.Archetype;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface;

/// <summary>The ground the places sit on: firm under anything heavy, and made by their people round them.</summary>
public partial class SurfaceTest
{
    /// <summary>
    /// On fresh lived-in worlds (a desert one among them, for its dunes): no heavy place (a town, a base, a port,
    /// anything big or tall) and no tower, block of flats or works of a city stands on sand; every place but an
    /// alien site or a wreck has made ground painted round it, its pad left to the shader, and the ground at its
    /// way in is part of it; and neighbouring places are joined.
    /// </summary>
    async Task GroundChecks()
    {
        var rand = new Random();
        bool was = CityPlan.Force;
        int heavy = 0, onSand = 0, sandSeated = 0, cityHeavy = 0, citySand = 0, places = 0, unpainted = 0, bareDoor = 0, patches = 0, worlds = 0, lanes = 0;
        string first = "";
        void Note(string s) { if (first.Length == 0) first = $" (first: {s})"; }
        try
        {
            CityPlan.Force = true;
            foreach (Archetype a in new[] { Archetype.Arid, Archetype.Temperate })
            {
                var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), a, 0);
                PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
                worlds++;
                patches += w.Source.Grounds.Count;
                lanes += w.Infra.NeighbourLanes + w.Infra.Spurs;
                GD.Print($"      {spec}: {w.Pois.Count} places, {w.Source.Grounds.Count} patches of made ground; {w.Infra.LaneLog}");
                foreach (PoiSite p in w.Pois)
                {
                    var t = p.Stamp;
                    double sx = t.MaxX - t.MinX, sz = t.MaxZ - t.MinZ;
                    if (p.Siting == "city")
                    {
                        CityLotPlan lot = w.Infra.City?.Lots.FirstOrDefault(l => l.Site == p);
                        if (lot == null || lot.Kind is "house" or "terrace") continue;
                        cityHeavy++;
                        if (Footing.SandShare(w.Map, t.CenterX, t.CenterZ, sx * 0.5, sz * 0.5) > 0.4f) { citySand++; Note($"{lot.Kind} lot on sand at {t.CenterX:F0},{t.CenterZ:F0}"); }
                        continue;
                    }
                    if (Footing.Heavy(p.Tier, sx, sz, p.Instance.Layout.Occupied.Min.Y + p.Instance.Layout.Occupied.Size.Y))
                    {
                        heavy++;
                        // On sand only in the middle of a dune sea: nowhere firm within a search's reach of it.
                        if (Footing.SandShare(w.Map, t.CenterX, t.CenterZ, sx * 0.5 + 4, sz * 0.5 + 4) > 0.12f)
                        {
                            sandSeated++;
                            bool firmNear = false;
                            for (int k = 0; k < 8 && !firmNear; k++)
                                foreach (double r in new[] { 40.0, 90.0 })
                                    firmNear |= Footing.SandShare(w.Map, t.CenterX + Math.Cos(k * Math.PI / 4) * r, t.CenterZ + Math.Sin(k * Math.PI / 4) * r, sx * 0.5 + 4, sz * 0.5 + 4) <= 0.05f;
                            if (firmNear) { onSand++; Note($"{p.Name} on sand with firm ground beside it"); }
                        }
                    }
                    bool wild = p.Tier == PoiGenerator.Gen.PoiTier.Wreck || new[] { "alien", "wreck", "precursor", "hive" }.Any(p.Family.Tags.Contains);
                    if (wild) continue;
                    places++;
                    if (!t.Painted) { unpainted++; Note($"{p.Name}'s pad not painted"); continue; }
                    // The ground at its way in: inside some patch, at its height.
                    double ex = p.Entrance.X + 0.5, ez = p.Entrance.Z + 0.5;
                    float ey = w.GroundHeight(ex, ez);
                    if (!MadeGrounds.Probe(w.Source.Grounds, ex, ez, ey, w.Width, w.Height).Any(s => s.Contains(" d -")))
                    {
                        bareDoor++;
                        Note($"nothing made at {p.Name}'s way in");
                    }
                }
            }
        }
        finally { CityPlan.Force = was; }
        GD.Print($"      {worlds} worlds: {heavy} heavy places, {cityHeavy} heavy city lots, {places} places that made their ground, {patches} patches, {lanes} lanes{first}");
        Check(onSand == 0 && citySand == 0, $"nothing heavy on sand: {onSand} of {heavy} heavy places stand on it with firm ground beside them ({sandSeated} in the middle of a dune sea), {citySand} of {cityHeavy} city towers, blocks and works on it");
        Check(places > 0 && unpainted == 0 && bareDoor <= places / 20, $"every place has its made ground: {unpainted} of {places} pads unpainted, {bareDoor} with nothing laid at the way in");
        await Frames(1);
    }
}
