using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using PoiSite = PlanetSurface.Poi.PoiSite;
using PInt3 = PoiGenerator.Gen.Int3;

namespace PlanetSurface.Infra;

/// <summary>
/// A small ruin that isn't a place of its own: an abandoned house by the road, a farmstead, a
/// shed, a wayside stop. Its layout comes from one of a few small architect families (flavour_*),
/// shared by every ruin built from it; each ruin spawns it with its own seed, so the century of
/// decay (timber fallen in unless sheltered, concrete and stone shells, trees through) differs.
/// </summary>
public sealed class FlavourRuin
{
    public FamilyDef Family;
    public Layout Layout;
    public PoiLibrary Library;
    /// <summary>World cell of the layout's origin (x, z on the planet, as laid; y the pad's surface).</summary>
    public int OriginX, OriginY, OriginZ;
    public PoiState State = PoiState.Ruined;
    public ulong SpawnSeed;
    public TerrainStamp Stamp;
    /// <summary>How hard a nearby blast flattened it (0 none), and from which way ground zero lies.</summary>
    public float Blast;
    public float BlastX, BlastZ;
    /// <summary>The road it stands by (null for one out in the country).</summary>
    public InfraRoute Route;
    public (double X, double Z) Center => ((Stamp.MinX + Stamp.MaxX) * 0.5, (Stamp.MinZ + Stamp.MaxZ) * 0.5);

    /// <summary>The ruin as built and decayed (a pure function of its layout and seed): call off the main thread.</summary>
    public PoiInstance Spawn(WorldProfile world)
    {
        PoiInstance inst = PoiFactory.Spawn(Library, Layout, State, SpawnSeed, world);
        if (Blast > 0.02f) StateTransforms.Blast(inst, BlastX, BlastZ, Blast, new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(SpawnSeed, 0xB1A57UL)));
        return inst;
    }

    /// <summary>
    /// What the survival tool salvages from it, by what its blocks are made of: scrap metal, wiring,
    /// glass, plastics and rubble (an early source of raw and primary materials; each ruin runs out).
    /// </summary>
    public static Dictionary<string, int> Yields(PoiInstance inst)
    {
        var y = new Dictionary<string, int>();
        void Add(string k, int n) => y[k] = y.GetValueOrDefault(k) + n;
        foreach (var (_, b) in inst.Blocks.Blocks())
        {
            if (b.IsAir || b.Role == Role.Loot) continue;
            switch (b.Material)
            {
                case Material.Scrap or Material.Alloy or Material.Steel or Material.Rust or Material.Armour or Material.Bronze: Add("scrap metal", 1); break;
                case Material.Glass: Add("glass", 1); break;
                case Material.Lumen: Add("wiring", 1); break;
                default:
                    if (b.Role is Role.Light or Role.Prop) Add("wiring", 1);
                    else if (b.Role == Role.Decor) Add("plastics", 1);
                    else Add("rubble", 1);
                    break;
            }
        }
        // A block is a cubic metre: the tool takes a handful from each.
        foreach (var k in y.Keys.ToList()) y[k] = Math.Max(1, y[k] / 8);
        return y;
    }
}

/// <summary>
/// Lots of abandoned houses, farmsteads, sheds and wayside stops (design doc "Infrastructure"), not
/// places but flavour: mostly along the roads and tracks, some out in the countryside, a farmstead
/// where the land was farmed, a wayside stop on a highway. On once-inhabited worlds only, built at
/// the world's pre-collapse tech level. Each gets a small pad of its own, laid before the roads, and
/// never stands on a road bed, a place's pad or another ruin. Round a blast site they stand in its
/// ruin ring, flattened outward from ground zero.
/// </summary>
public static class FlavourRuins
{
    static readonly ConcurrentDictionary<(string, int), Layout> Layouts = new();

    /// <summary>
    /// How far a blast reaches into the land round its site, as a multiple of the site's own fade
    /// radius: the site holds its crater and inner ruin ring, the country round it the rest.
    /// </summary>
    public const float WorldReach = 4f;

    /// <summary>How hard a blast hits at a distance from ground zero, out in the land round its site.</summary>
    public static float BlastAt(BlastZone zone, float d) =>
        d <= zone.Crater ? 1f : d >= zone.Fade * WorldReach ? 0f : 1f - (d - zone.Crater) / (zone.Fade * WorldReach - zone.Crater);

    /// <summary>The flavour families the world can build: their layout for its tech level.</summary>
    public static Layout LayoutFor(PoiLibrary lib, FamilyDef f, int variant) =>
        Layouts.GetOrAdd((f.Id, variant), _ => PoiFactory.BuildLayout(lib, f, variant, Rarity.Common));

    public static List<FlavourRuin> Plan(InfraNetwork net, PoiLibrary lib, WorldMap map, PlanetTerrainSource bare, IReadOnlyList<PoiSite> pois, WorldProfile profile, ulong seed,
        List<FlavourRuin> already = null)
    {
        var ruins = already ?? new List<FlavourRuin>();
        if (profile == null || !profile.Inhabited || lib == null) return ruins;
        var families = lib.Families.Where(f => f.Tags.Contains("flavour") && (f.Techs.Count == 0 || f.Techs.Contains(WorldProfile.Tag(profile.Before)))).ToList();
        if (families.Count == 0) return ruins;
        var rng = new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(seed, 0xF1A7UL));
        string tag = WorldProfile.Tag(profile.Before);
        FamilyDef Pick(string kind) => families.FirstOrDefault(f => f.Tags.Contains(kind));
        int VariantFor(FamilyDef f)
        {
            var fit = f.Variants.Where(v => v.Tech.Length == 0 || v.Tech == tag).ToList();
            if (fit.Count == 0) fit = f.Variants;
            return fit[rng.Range(0, fit.Count)].Index;
        }
        double w = map.WidthMeters, h = map.HeightMeters;
        bool farmland = profile.Trade == Trade.Agricultural;

        // Along the roads and tracks: a ruin every few hundred metres, set back from the verge.
        foreach (InfraRoute r in net.Routes)
        {
            if (r.Kind == RouteKind.Rail) continue;
            double every = r.Kind switch { RouteKind.Highway => 520, RouteKind.Road => 330, _ => 450 };
            double next = every * rng.Float();
            double run = 0;
            for (int i = 1; i + 1 < r.Count; i++)
            {
                run += InfraNetwork.Spacing;
                if (run < next || r.Span[i] != RoadSpan.Ground) continue;
                next = run + every * (0.5 + rng.Float());
                string kind = r.Kind == RouteKind.Highway && profile.Before != TechLevel.Low && rng.Chance(0.3f) ? "wayside"
                    : (farmland || r.Kind == RouteKind.Track) && rng.Chance(0.4f) ? "farmstead"
                    : rng.Chance(0.2f) ? "shed" : "house";
                FamilyDef f = Pick(kind) ?? Pick("house");
                if (f == null) continue;
                int variant = VariantFor(f);
                Layout layout = LayoutFor(lib, f, variant);
                double dx = r.X[i + 1] - r.X[i - 1], dz = r.Z[i + 1] - r.Z[i - 1], len = Math.Sqrt(dx * dx + dz * dz);
                if (len < 1e-6) continue;
                int side = rng.Chance(0.5f) ? 1 : -1;
                double nx = -dz / len * side, nz = dx / len * side;
                var size = layout.Occupied.Size;
                // Back from the road by the lot's half-diagonal, so a large lot sits as well as a small one.
                double back = Math.Sqrt(size.X * size.X + size.Z * size.Z) * 0.5 + 18 + rng.Float() * 8;
                var ruin = Try(lib, f, layout, r.X[i] + nx * back, r.Z[i] + nz * back, bare, net, pois, ruins, w, h, rng);
                if (ruin == null) continue;
                ruin.Route = r;
                ruins.Add(ruin);
            }
        }
        // Out in the countryside: a farmstead here and there, a lone house.
        int country = (int)(w * h / 1e6 * (farmland ? 1.2 : 0.5));
        for (int k = 0, tries = 0; k < country && tries < country * 20; tries++)
        {
            FamilyDef f = Pick(farmland || rng.Chance(0.5f) ? "farmstead" : "house") ?? Pick("house");
            if (f == null) break;
            Layout layout = LayoutFor(lib, f, VariantFor(f));
            var ruin = Try(lib, f, layout, rng.Float() * w, rng.Float() * h, bare, net, pois, ruins, w, h, rng);
            if (ruin == null) continue;
            ruins.Add(ruin);
            k++;
        }
        // Round a blast: the ruin ring, every ruin in it flattened outward from ground zero.
        foreach (PoiSite p in pois)
        {
            if (p.GroundZero is not (double gx, double gz) || p.Instance.Layout.Extras.Blast is not BlastZone zone) continue;
            double inner = Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ) + 24, outer = zone.Fade * (WorldReach - 1);
            if (outer <= inner) outer = inner + 120;
            for (int k = 0, placed = 0; k < 60 && placed < 12; k++)
            {
                double a = rng.Float() * Math.PI * 2, d = inner + (outer - inner) * rng.Float();
                FamilyDef f = Pick("house");
                if (f == null) break;
                Layout layout = LayoutFor(lib, f, VariantFor(f));
                var ruin = Try(lib, f, layout, gx + Math.Cos(a) * d, gz + Math.Sin(a) * d, bare, net, pois, ruins, w, h, rng);
                if (ruin == null) continue;
                ruins.Add(ruin);
                placed++;
            }
        }
        foreach (FlavourRuin ruin in ruins) Blasted(ruin, pois, w, h);
        return ruins;
    }

    /// <summary>Takes a ruin's share of any blast round it: the nearer ground zero, the harder.</summary>
    static void Blasted(FlavourRuin ruin, IReadOnlyList<PoiSite> pois, double w, double h)
    {
        foreach (PoiSite p in pois)
        {
            if (p.GroundZero is not (double gx, double gz) || p.Instance.Layout.Extras.Blast is not BlastZone zone) continue;
            var (cx, cz) = ruin.Center;
            double dx = Mathx.WrapDelta(cx - gx, w), dz = Mathx.WrapDelta(cz - gz, h);
            float d = (float)Math.Sqrt(dx * dx + dz * dz), s = BlastAt(zone, d);
            if (s <= ruin.Blast || d < 1f) continue;
            ruin.Blast = s;
            ruin.BlastX = (float)(-dx / d);
            ruin.BlastZ = (float)(-dz / d);
        }
    }

    /// <summary>
    /// A ruin's pad at a spot, if the ground there takes one: dry, not too steep, clear of roads, places
    /// and other ruins. <paramref name="clearance"/> replaces the corner-and-gap distance from any road's
    /// centre where the caller knows its lot lies square to the street.
    /// </summary>
    public static FlavourRuin Try(PoiLibrary lib, FamilyDef f, Layout layout, double cx, double cz, PlanetTerrainSource bare, InfraNetwork net, IReadOnlyList<PoiSite> pois,
        List<FlavourRuin> ruins, double w, double h, PoiGenerator.Gen.Rng rng, double roadGap = 14, double spacing = 18, float maxCut = 4.5f, double? clearance = null)
    {
        cx = Mathx.WrapX(cx, w);
        cz = Mathx.Wrap(cz, h);
        CellMask occ = layout.Occupied;
        double halfX = occ.Size.X * 0.5, halfZ = occ.Size.Z * 0.5;
        const float Apron = 2f;
        // Clear of every place's reach, every road's bed and banks, and other ruins.
        foreach (PoiSite p in pois)
        {
            double dx = Math.Abs(Mathx.WrapDelta(p.Center.X - cx, w)), dz = Math.Abs(Mathx.WrapDelta(p.Center.Z - cz, h));
            double reach = Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ) + Math.Max(halfX, halfZ) + 12;
            if (dx < reach && dz < reach) return null;
        }
        double corner = Math.Sqrt(halfX * halfX + halfZ * halfZ) + Apron;
        if (net.Nearest(cx, cz, corner + 30, out _, out double gap) != null && gap < (clearance ?? corner + roadGap)) return null;
        foreach (FlavourRuin o in ruins)
        {
            var (rx, rz) = o.Center;
            double ohx = (o.Stamp.MaxX - o.Stamp.MinX) * 0.5, ohz = (o.Stamp.MaxZ - o.Stamp.MinZ) * 0.5;
            if (Math.Abs(Mathx.WrapDelta(rx - cx, w)) < halfX + ohx + spacing && Math.Abs(Mathx.WrapDelta(rz - cz, h)) < halfZ + ohz + spacing) return null;
        }
        // The ground: dry, and level enough to cut and fill a small pad.
        var samples = new List<float>();
        for (double sz = -halfZ - Apron; sz <= halfZ + Apron + 0.01; sz += (halfZ + Apron) / 2)
        for (double sx = -halfX - Apron; sx <= halfX + Apron + 0.01; sx += (halfX + Apron) / 2)
        {
            GroundSample g = bare.Sampler.Sample(cx + sx, cz + sz, 2);
            if (g.InRiver || g.Height < 2f) return null;
            samples.Add(g.Height);
        }
        samples.Sort();
        int pad = (int)MathF.Round(samples[samples.Count / 2]);
        float cut = MathF.Max(samples[^1] - pad, pad - samples[0]);
        if (cut > maxCut) return null;
        int ox = (int)Math.Floor(cx - (occ.Min.X + halfX)), oz = (int)Math.Floor(cz - (occ.Min.Z + halfZ));
        int carveH = Math.Max(0, -occ.Min.Y);
        bool[] carve = null;
        if (carveH > 0)
        {
            carve = new bool[carveH * occ.Size.Z * occ.Size.X];
            for (int y = 0; y < carveH; y++)
            for (int z = 0; z < occ.Size.Z; z++)
            for (int x = 0; x < occ.Size.X; x++)
                carve[(y * occ.Size.Z + z) * occ.Size.X + x] = occ[new PInt3(occ.Min.X + x, occ.Min.Y + y, occ.Min.Z + z)];
        }
        float blend = Math.Clamp(cut * 2.4f + 6f, 6f, 24f);
        var stamp = new TerrainStamp(f.Name, ox + occ.Min.X, oz + occ.Min.Z, ox + occ.Min.X + occ.Size.X, oz + occ.Min.Z + occ.Size.Z, pad, Apron, blend, w, h,
            ox + occ.Min.X, pad + occ.Min.Y, oz + occ.Min.Z, occ.Size.X, carveH, occ.Size.Z, carve)
        {
            Surface = f.Tags.Contains("farmstead") || f.Tags.Contains("shed") ? StampSurface.Dirt : StampSurface.Natural, Wear = 0.9f,
        };
        return new FlavourRuin
        {
            Family = f, Layout = layout, Library = lib, OriginX = ox, OriginY = pad, OriginZ = oz, SpawnSeed = rng.NextU64(), Stamp = stamp,
            State = rng.Chance(0.75f) ? PoiState.Ruined : PoiState.Abandoned,
        };
    }
}
