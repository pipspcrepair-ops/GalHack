using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Plants;
using GalHack.Terrain;
using Planets.Gen;
using Planets.Look;
using Planets.Surface;
using PlanetSurface.Terrain;
using PoiGenerator.Gen;
using Biome = Planets.Gen.Biome;
using Hash = Planets.Gen.Hash;

namespace PlanetSurface.Infra;

/// <summary>What stands beside a street: a lamp, a tree in the verge, a signal pole at a corner, or a sign post.</summary>
public enum StreetThing { Lamp, Tree, Signal, Sign }

/// <summary>One piece of street furniture: where it stands (world x, z and its foot), the way it faces (unit x, z: a lamp's arm and a sign's face point that way), and what it is.</summary>
public sealed record StreetItem(StreetThing Thing, double X, double Z, float Y, float FaceX, float FaceZ, int Symbol = 0);

/// <summary>
/// The street side of a town (design doc "Infrastructure"; the lot line out to the kerb is the roads side's):
/// lamps and trees standing in the verge between the kerb and the footpath, both stepping round junctions,
/// drives and yards; signal poles at the corners of a big town crossroads; and a sign post on every way that
/// gives way at a junction, a symbol on it and no words. A low-tech world's streets have their trees and
/// nothing electric. Planned once the roads, their junctions and the places' pads are laid.
/// </summary>
public static class StreetFurniture
{
    /// <summary>How far past a street's deck edge its verge things stand, m (the footpath lies beyond).</summary>
    public const float Verge = 0.8f;

    /// <summary>Sign symbols: give way, roundabout ahead, no through road.</summary>
    public const int GiveWay = 0, RoundaboutAhead = 1, NoThrough = 2;

    public static List<StreetItem> Plan(InfraNetwork net, PlanetTerrainSource source, ulong seed)
    {
        var items = new List<StreetItem>();
        if (net.Profile == null) return items;
        bool low = net.Profile.Before == TechLevel.Low;
        double w = net.Width, h = net.Height;
        float Ground(double x, double z) => source.SurfaceHeight(Mathx.WrapX(x, w), Mathx.Wrap(z, h));
        double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, w), 2) + Math.Pow(Mathx.WrapDelta(az - bz, h), 2));
        var gates = net.Ruins.Select(r => (X: r.OriginX + r.Layout.Extras.Gate.X + 0.5, Z: r.OriginZ + r.Layout.Extras.Gate.Z + 0.5)).ToList();
        if (net.City is CityPlan city) gates.AddRange(city.Lots.Select(l => l.Gate));
        // A spot in a verge takes a thing: no pad or yard there, no other road's deck, clear of drives and junction mouths.
        bool Free(double x, double z)
        {
            double cx = Mathx.WrapX(x, w), cz = Mathx.Wrap(z, h);
            if (source.Built(cx, cz, out _) > 0.6f || source.WaterSurfaceAt(cx, cz, out _) || !net.OffDeck(x, z, 0.4)) return false;
            if (gates.Any(g => Dist(g.X, g.Z, x, z) < 7)) return false;
            if (items.Any(i => Dist(i.X, i.Z, x, z) < 4)) return false;
            return !net.Joins.Any(j => Dist(j.X, j.Z, x, z) < 14);
        }
        // ---- the verges: a lamp and a tree by turns, every 13 m or so, one side or both.
        foreach (InfraRoute r in net.Routes.Where(r => r.Laid.Length == 0 && r.Kind == RouteKind.Road && !r.Ring && r.Length > 40))
        {
            var rng = new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(seed, (ulong)r.Id * 7919UL + 0x57EEUL));
            bool trees = rng.Chance(0.7f), lamps = !low && r.Surface is StampSurface.Tarmac or StampSurface.Concrete;
            if (!trees && !lamps) continue;
            int sides = rng.Chance(0.5f) ? 2 : 1, first = rng.Chance(0.5f) ? 1 : -1;
            for (int s = 0; s < sides; s++)
            {
                int side = s == 0 ? first : -first, turn = 0;
                for (double t = 10 + rng.Float() * 6; t < r.Length - 10; t += 13)
                {
                    int i = Math.Clamp((int)(t / InfraNetwork.Spacing), 1, r.Count - 2);
                    if (r.Span[i] != RoadSpan.Ground || r.Span[i - 1] != RoadSpan.Ground) continue;
                    double dx = r.X[i + 1] - r.X[i - 1], dz = r.Z[i + 1] - r.Z[i - 1], l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
                    double nx = -dz / l * side, nz = dx / l * side;
                    double x = r.X[i] + nx * (r.HalfWidth + Verge), z = r.Z[i] + nz * (r.HalfWidth + Verge);
                    float y = Ground(x, z);
                    if (Math.Abs(y - r.Y[i]) > 0.6f || !Free(x, z)) continue;
                    StreetThing thing = lamps && (!trees || turn % 2 == 0) ? StreetThing.Lamp : StreetThing.Tree;
                    turn++;
                    items.Add(new StreetItem(thing, Mathx.WrapX(x, w), Mathx.Wrap(z, h), y, (float)-nx, (float)-nz));
                }
            }
        }
        if (low) return items;
        // ---- the junctions: signal poles at a big town crossroads' corners; elsewhere a sign on each way that gives way.
        foreach (InfraJunction j in net.Joins)
        {
            bool town = j.Arms.All(a => a.Route.Laid.Length == 0);
            if (j.Form == JunctionForm.Crossroads && town && j.Arms.All(a => a.Route.HalfWidth >= 3.9f))
            {
                var arms = j.Arms.OrderBy(a => Math.Atan2(a.DirZ, a.DirX)).ToList();
                for (int k = 0; k < arms.Count; k++)
                {
                    JunctionArm a = arms[k], b = arms[(k + 1) % arms.Count];
                    double bx = a.DirX + b.DirX, bz = a.DirZ + b.DirZ, bl = Math.Sqrt(bx * bx + bz * bz);
                    if (bl < 0.2) continue;
                    double half = Math.Acos(Math.Clamp(a.DirX * b.DirX + a.DirZ * b.DirZ, -1, 1)) * 0.5;
                    double d = Math.Min(14, (Math.Max(a.Route.HalfWidth, b.Route.HalfWidth) + 1.6) / Math.Max(0.3, Math.Sin(half)));
                    double x = j.X + bx / bl * d, z = j.Z + bz / bl * d;
                    // Round a rounded corner: in its verge, a little past the middle of its kerb's arc.
                    RoadCorner rc = net.Corners.Where(c => Dist(c.X, c.Z, j.X, j.Z) < 40).OrderBy(c => Dist(c.X, c.Z, x, z)).FirstOrDefault();
                    if (rc != null && Dist(rc.X, rc.Z, x, z) < rc.R + 6)
                    {
                        double mx = rc.AX + rc.BX, mz = rc.AZ + rc.BZ, ml = Math.Max(1e-6, Math.Sqrt(mx * mx + mz * mz));
                        x = rc.X + mx / ml * (rc.R - 1.0);
                        z = rc.Z + mz / ml * (rc.R - 1.0);
                    }
                    double cx = Mathx.WrapX(x, w), cz = Mathx.Wrap(z, h);
                    if (source.Built(cx, cz, out _) > 0.6f || !net.OffDeck(x, z, 0.3)) continue;
                    items.Add(new StreetItem(StreetThing.Signal, cx, cz, Ground(x, z), (float)(-bx / bl), (float)(-bz / bl)));
                }
                continue;
            }
            if (j.Form is not (JunctionForm.Tee or JunctionForm.Crossroads or JunctionForm.Roundabout)) continue;
            foreach (JunctionArm a in j.Arms.Where(a => a.Route != j.Through || j.Form == JunctionForm.Roundabout))
            {
                // On the right of the way coming in, a little short of the junction, facing whoever comes.
                double back = j.Form == JunctionForm.Roundabout ? j.RingR + 12 : 9 + j.Through.HalfWidth;
                double tx = -a.DirX, tz = -a.DirZ, rx = -tz, rz = tx;
                double x = 0, z = 0;
                // (Back past the corner's rounded kerb: a sign stands in the verge beside the way in, never on the corner's deck.)
                for (int tries = 0; tries < 10; tries++, back += 2)
                {
                    x = j.X + a.DirX * back + rx * (a.Route.HalfWidth + 1.1);
                    z = j.Z + a.DirZ * back + rz * (a.Route.HalfWidth + 1.1);
                    if (net.OffDeck(x, z, 0.4)) break;
                }
                double cx = Mathx.WrapX(x, w), cz = Mathx.Wrap(z, h);
                if (source.Built(cx, cz, out _) > 0.6f || !net.OffDeck(x, z, 0.4) || source.WaterSurfaceAt(cx, cz, out _)) continue;
                items.Add(new StreetItem(StreetThing.Sign, cx, cz, Ground(x, z), (float)-tx, (float)-tz, j.Form == JunctionForm.Roundabout ? RoundaboutAhead : GiveWay));
            }
        }
        return items;
    }

    /// <summary>
    /// The trees planted in the verges, of the species that grows round there and kept small as a planted tree is;
    /// none where the biome grows no trees or the land has died.
    /// </summary>
    public static void PlantTrees(IEnumerable<StreetItem> items, GroundPalette pal)
    {
        foreach (StreetItem item in items.Where(i => i.Thing == StreetThing.Tree))
        {
            double x = item.X, z = item.Z;
            Biome biome = pal.BiomeAt(x, z);
            FloraRule[] trees = pal.Flora.RulesOf(biome).Where(r => FloraMeshes.IsTree(r.Kind)).ToArray();
            if (trees.Length == 0) continue;
            ulong h = Hash.Of(0x57EE, (long)Math.Floor(x * 8), (long)Math.Floor(z * 8));
            if (pal.DeadZones.Count > 0 && Hash.Unit(h, 1) < pal.Deadness(x, z)) continue;
            float total = trees.Sum(r => r.Density), acc = Hash.Unit(h, 2) * total;
            FloraRule rule = trees[^1];
            foreach (FloraRule r in trees)
                if ((acc -= r.Density) < 0f) { rule = r; break; }
            Plant plant = pal.Flora.Pick(rule.Kind, biome, x, z, Hash.Unit(h, 6));
            if (plant == null) continue;
            long id = FarBuilder.ScatterIdAt(x, z);
            if (pal.Planted.ContainsKey(id)) continue;
            pal.Planted[id] = new PlantedFlora(rule.Kind, x, z, item.Y, Mathx.Lerp(0.55f, 0.8f, Hash.Unit(h, 3)), Hash.Unit(h, 4) * MathF.Tau, pal.Flora.Tint(plant, Hash.Unit(h, 5), x, z), plant);
        }
    }
}