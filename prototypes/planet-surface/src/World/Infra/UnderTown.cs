using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using PoiSite = PlanetSurface.Poi.PoiSite;
using PInt3 = PoiGenerator.Gen.Int3;

namespace PlanetSurface.Infra;

/// <summary>
/// What runs under a town (design doc "Infrastructure", underground networks), on the town's real
/// ground: the generator's plan for its street grid, built into blocks, the cells it takes out of the
/// ground as buried stamps, and small pads where it comes to the surface. Cells are world cells on
/// the copy of the planet the town's centre is on, so they can run past a seam.
/// </summary>
public sealed class UnderTown
{
    public UnderPlan Scheme;
    public UnderBuilt Built;
    public readonly List<TerrainStamp> Stamps = new();
    /// <summary>Street-grid ranges the houses must keep clear of.</summary>
    public List<(int A0, int A1, int U0, int U1)> Reserved = new();
    /// <summary>The block cells, by tile of <see cref="MeshTile"/> metres: what each streamed tile draws.</summary>
    public readonly Dictionary<(int X, int Z), List<PInt3>> Tiles = new();
    /// <summary>Lowest and highest occupied cell, for the dark over the whole of it.</summary>
    public int Bottom, Top;

    public const int MeshTile = 48, StampTile = 32;

    UnderSite _site;

    /// <summary>A town's underground before its ground is known: which lots it needs. Plan it once the roads are down.</summary>
    public static UnderTown Reserve(StartTown town, WorldProfile profile, ulong seed)
    {
        if (profile == null || !profile.Inhabited) return null;
        var site = new UnderSite
        {
            X = (int)Math.Floor(town.CenterX), Z = (int)Math.Floor(town.CenterZ), AlongX = (int)town.AlongX, AlongZ = (int)town.AlongZ,
            MainHalf = (int)town.MainHalf, Block = (int)town.Block, Arm = (int)town.Arm, BackSide = town.BackSide,
            Tech = profile.Before, Seed = seed,
        };
        return new UnderTown { _site = site, Reserved = Underground.Reserved(site).ToList() };
    }

    /// <summary>Plans and builds it on the ground as the town and its roads left it: true deck, banks and pads under every shaft.</summary>
    public void Plan(PlanetTerrainSource ground, IReadOnlyList<PoiSite> pois)
    {
        double w = ground.Width, h = ground.Height;
        _site.Ground = (x, z) => ground.SurfaceHeight(Mathx.WrapX(x + 0.5, w), Mathx.Wrap(z + 0.5, h));
        _site.Avoid = (x, z) =>
        {
            double cx = Mathx.WrapX(x + 0.5, w), cz = Mathx.Wrap(z + 0.5, h);
            GroundSample g = ground.Sampler.Sample(cx, cz, 4);
            if (g.InRiver || g.Height < 2f) return true;
            foreach (PoiSite p in pois)
            {
                double dx = Math.Abs(Mathx.WrapDelta(p.Center.X - cx, w)), dz = Math.Abs(Mathx.WrapDelta(p.Center.Z - cz, h));
                if (dx < p.Stamp.ReachX + 6 && dz < p.Stamp.ReachZ + 6) return true;
            }
            return false;
        };
        Scheme = Underground.Plan(_site);
        Built = Underground.Build(Scheme, _site.Ground);
        Lay(w, h);
    }

    /// <summary>The stamps and the mesh tiles: one buried stamp for each patch of the ground it opens.</summary>
    void Lay(double w, double h)
    {
        var patches = new Dictionary<(int X, int Z), List<PInt3>>();
        Bottom = int.MaxValue;
        Top = int.MinValue;
        foreach (PInt3 c in Built.Occupied)
        {
            var key = (FloorDiv(c.X, StampTile), FloorDiv(c.Z, StampTile));
            if (!patches.TryGetValue(key, out var list)) patches[key] = list = new List<PInt3>();
            list.Add(c);
            Bottom = Math.Min(Bottom, c.Y);
            Top = Math.Max(Top, c.Y);
        }
        foreach (var (key, cells) in patches.OrderBy(p => p.Key.X).ThenBy(p => p.Key.Z))
        {
            int lo = cells.Min(c => c.Y), hi = cells.Max(c => c.Y), height = hi - lo + 1;
            int x0 = key.X * StampTile, z0 = key.Z * StampTile;
            var carve = new bool[height * StampTile * StampTile];
            foreach (PInt3 c in cells) carve[((c.Y - lo) * StampTile + (c.Z - z0)) * StampTile + (c.X - x0)] = true;
            Stamps.Add(TerrainStamp.Buried("Underground", WrapCell(x0, w), WrapCell(z0, h), StampTile, StampTile, lo, height, carve, w, h));
        }
        foreach (PInt3 c in Built.Blocks.Keys)
        {
            var key = (FloorDiv(c.X, MeshTile), FloorDiv(c.Z, MeshTile));
            if (!Tiles.TryGetValue(key, out var list)) Tiles[key] = list = new List<PInt3>();
            list.Add(c);
        }
        // Small level pads where it comes up: the head of the station stairs and the culvert's apron.
        foreach (UnderStairs s in Scheme.Stairs)
        {
            PInt3 d = s.Down.Vec();
            Stamps.Add(Pad("Station entrance", s.X - d.X * 3 - 4, s.Z - d.Z * 3 - 4, 9, 9, s.TopY, w, h, StampSurface.Concrete));
        }
        foreach (UnderOutfall o in Scheme.Outfalls)
            Stamps.Add(Pad("Outfall", o.X + o.DirX * 4 - 6, o.Z + o.DirZ * 4 - 6, 13, 13, o.Y, w, h, StampSurface.Natural));
    }

    static TerrainStamp Pad(string name, int x, int z, int sx, int sz, int y, double w, double h, StampSurface surface)
    {
        int cx = WrapCell(x, w), cz = WrapCell(z, h);
        return new TerrainStamp(name, cx, cz, cx + sx, cz + sz, y, 1f, 8f, w, h) { Surface = surface, Wear = 0.9f };
    }

    static int WrapCell(int v, double size) => (int)Math.Floor(Mathx.Wrap(v + 0.5, size));
    static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;
}
