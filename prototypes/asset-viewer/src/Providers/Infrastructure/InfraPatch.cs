using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.Poi;
using PlanetSurface.World;
using PoiGenerator.Gen;

namespace AssetViewer.Providers;

/// <summary>
/// The game's infrastructure as the planet draws it: a real world is generated (PlanetWorld: its terrain, places and the road, rail
/// and power network planned over them), the nearest example of the chosen thing is found, and a patch of that world is drawn: the
/// ground sampled from the world's terrain (its stamps and road surfaces coloured by the planet's palette) and the infrastructure
/// layer's own pieces for the tiles round it (bridges, viaducts, tunnel portals, rails, pylons and wires, poles). Worlds are kept
/// once built, so the first of each takes several seconds and the rest are quick.
/// </summary>
public static class InfraPatch
{
    public sealed record Feature(string Id, string Name, string Group);

    public static readonly Feature[] Features =
    {
        new("highway", "Highway", "Roads"), new("road", "Road", "Roads"), new("track", "Track", "Roads"), new("junction", "Road junction", "Roads"),
        new("bridge", "Bridge", "Crossings"), new("tunnel", "Tunnel portal", "Crossings"),
        new("rail", "Railway", "Rail"),
        new("pylon", "Pylon and power line", "Power"), new("pole", "Power poles", "Power"), new("substation", "Substation", "Power"),
    };

    public static readonly string[] Worlds = Enum.GetNames<Archetype>();

    static readonly Dictionary<(Archetype, ulong), (PlanetWorld World, Dictionary<long, List<InfraLayer.Piece>> Pieces)> Cache = new();
    static PoiCatalog _catalog;

    public static AssetOption[] Options() => new[]
    {
        new AssetOption("World", Worlds) { Rollable = true },
        new AssetOption("Reach", "120 m", "250 m", "400 m") { Default = 1 },
    };

    static (PlanetWorld, Dictionary<long, List<InfraLayer.Piece>>) WorldOf(Archetype type, ulong seed)
    {
        if (Cache.TryGetValue((type, seed), out var done)) return done;
        PlanetTerrain.ConfigureWorldBounds();
        PoiSiting.StartWorld = true;
        _catalog ??= PoiCatalog.Load(PlaceData.DataFolder);
        PlanetWorld world = PlanetWorld.Build(PlanetSpec.Roll(seed, type, 0), _catalog);
        var pieces = InfraLayer.Plan(world);
        return Cache[(type, seed)] = (world, pieces);
    }

    /// <summary>Where the nearest example of a feature is, world metres, or null if this world has none.</summary>
    static (double X, double Z)? Anchor(PlanetWorld w, string feature)
    {
        InfraNetwork net = w.Infra;
        double lx = w.Landing.X, lz = w.Landing.Y;
        double Away(double x, double z) => Math.Pow(Mathx.WrapDelta(x - lx, w.Width), 2) + Math.Pow(Mathx.WrapDelta(z - lz, w.Height), 2);
        (double, double)? Best(IEnumerable<(double X, double Z)> points) => points.Any() ? points.OrderBy(p => Away(p.X, p.Z)).First() : null;
        (double X, double Z) Mid(InfraRoute r, int a, int b) => (r.X[(a + b) / 2], r.Z[(a + b) / 2]);
        switch (feature)
        {
            case "highway": return Best(net.Routes.Where(r => r.Kind == RouteKind.Highway && r.Count > 12).Select(r => Mid(r, 0, r.Count - 1)));
            case "road": return Best(net.Routes.Where(r => r.Kind == RouteKind.Road && r.Count > 12).Select(r => Mid(r, 0, r.Count - 1)));
            case "track": return Best(net.Routes.Where(r => r.Kind == RouteKind.Track && r.Count > 12).Select(r => Mid(r, 0, r.Count - 1)));
            case "rail": return Best(net.Routes.Where(r => r.Kind == RouteKind.Rail && r.Count > 12).Select(r => Mid(r, 0, r.Count - 1)));
            case "junction": return Best(net.Routes.Where(r => r.Kind is RouteKind.Road or RouteKind.Highway && r.From == null && r.Count > 6).Select(r => (r.X[0], r.Z[0])));
            case "bridge": return Best(net.Structures.Where(s => s.Kind == RoadSpan.Bridge).Select(s => Mid(s.Route, s.First, s.Last)));
            case "tunnel": return Best(net.Structures.Where(s => s.Kind == RoadSpan.Tunnel).Select(s => (s.Route.X[s.First], s.Route.Z[s.First])));
            case "pylon": return Best(net.Power.Poles.Where(p => p.Pylon && !p.Fallen && p.Role == PowerRole.Line).Select(p => (p.X, p.Z)));
            case "pole": return Best(net.Power.Poles.Where(p => !p.Pylon && !p.Fallen && p.Role == PowerRole.Line).Select(p => (p.X, p.Z)));
            case "substation": return Best(net.Power.Poles.Where(p => p.Role == PowerRole.Substation).Select(p => (p.X, p.Z)));
        }
        return null;
    }

    public static InfraAsset Build(Feature feature, BuildRequest r)
    {
        Archetype type = Enum.TryParse(r.Get("World"), out Archetype a) ? a : Archetype.Temperate;
        float reach = r.Get("Reach") switch { "120 m" => 120f, "400 m" => 400f, _ => 250f };
        // The seed rolls the world; if it has none of the thing, the next seeds are tried (a few worlds at most).
        PlanetWorld world = null; (double X, double Z)? at = null; Dictionary<long, List<InfraLayer.Piece>> pieces = null;
        ulong seed = r.Seed;
        for (int k = 0; k < 4 && at == null; k++, seed++)
        {
            (world, pieces) = WorldOf(type, seed);
            at = Anchor(world, feature.Id);
        }
        // None of it on four worlds of this kind: show the landing site and say so, rather than nothing.
        bool found = at != null;
        at ??= (world.Landing.X, world.Landing.Y);
        return new InfraAsset(world, pieces, at.Value.X, at.Value.Z, reach, found ? feature.Name : feature.Name + " (none on these worlds; the landing site)", type, seed - 1);
    }
}

/// <summary>A square of a planet: its ground and the infrastructure pieces on it, framed on one spot.</summary>
public sealed partial class InfraAsset : ProviderAsset
{
    readonly Aabb _bounds;
    readonly List<(string, string)> _facts = new();

    public InfraAsset(PlanetWorld world, Dictionary<long, List<InfraLayer.Piece>> pieces, double cx, double cz, float reach, string name, Archetype type, ulong seed)
    {
        Stage.Ground = false;
        float groundY = world.GroundHeight(Mathx.WrapX(cx, world.Width), Mathx.Wrap(cz, world.Height));
        // The ground: a grid of points on the world's own height, coloured by its palette (road and pad surfaces from the stamps).
        const float Step = 4f;
        int n = (int)(reach * 2 / Step);
        var verts = new Vector3[(n + 1) * (n + 1)];
        var colours = new Color[verts.Length];
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                double x = cx - reach + i * Step, z = cz - reach + j * Step;
                double wx = Mathx.WrapX(x, world.Width), wz = Mathx.Wrap(z, world.Height);
                float h = world.GroundHeight(wx, wz);
                Color land = world.Palette.Vertex(TerrainDigging.Terrain.CellMaterial.Soil, wx, wz, h, out _);
                var stamp = world.Source.StampAt(wx, wz);
                if (stamp != null) land = world.Palette.Made(land, stamp.Surface, 1f, stamp.Wear, wx, wz);
                verts[j * (n + 1) + i] = new Vector3(i * Step - reach, h - groundY, j * Step - reach);
                colours[j * (n + 1) + i] = new Color(land.R, land.G, land.B);
            }
        var indices = new List<int>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                indices.AddRange(new[] { a, b, d, a, d, c });
            }
        var normals = new Vector3[verts.Length];
        Array.Fill(normals, Vector3.Up);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 1f });
        var ground = new MeshInstance3D { Name = "Ground", Mesh = mesh };
        AddChild(ground);
        // Standing water over the ground where the world has a river or sea, as a faint surface.
        var wet = new Flat();
        float lowest = verts.Min(v => v.Y);
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float? s = null;
                for (int k = 0; k < 4 && s == null; k++)
                {
                    int ci = i + (k & 1), cj = j + (k >> 1);
                    double wx = Mathx.WrapX(cx - reach + ci * Step, world.Width), wz = Mathx.Wrap(cz - reach + cj * Step, world.Height);
                    s = world.WaterSurfaceAt(new Vector3((float)wx, verts[cj * (n + 1) + ci].Y + groundY, (float)wz));
                }
                if (s is float level) wet.Quad(i * Step - reach, j * Step - reach, (i + 1) * Step - reach, (j + 1) * Step - reach, level - groundY, new Color(world.Look.Shallow.R, world.Look.Shallow.G, world.Look.Shallow.B, 0.7f));
            }
        if (wet.Count > 0) AddChild(wet.ToMesh("Water", 0.1f, true));
        // A plain plane out to the horizon, under the lowest ground.
        var plane = new Flat();
        plane.Quad(-30000, -30000, 30000, 30000, lowest - 0.5f, world.Palette.Vertex(TerrainDigging.Terrain.CellMaterial.Soil, cx, cz, groundY, out _));
        AddChild(plane.ToMesh("Land"));

        // The infrastructure: every tile that touches the square, its pieces emitted by the layer's own builder.
        float tile = InfraLayer.TileSize;
        int tiles = 0, count = 0;
        int x0 = (int)Math.Floor((cx - reach) / tile), x1 = (int)Math.Floor((cx + reach) / tile), z0 = (int)Math.Floor((cz - reach) / tile), z1 = (int)Math.Floor((cz + reach) / tile);
        for (int tz = z0; tz <= z1; tz++)
            for (int tx = x0; tx <= x1; tx++)
            {
                int ix = (int)(Mathx.WrapX(tx * tile, world.Width) / tile), iz = (int)(Mathx.Wrap(tz * tile, world.Height) / tile);
                if (!pieces.TryGetValue((long)ix << 32 | (uint)iz, out var list)) continue;
                var b = new TileBuilder(world, tx * tile, tz * tile);
                foreach (var p in list) p.Emit(b);
                count += list.Count;
                if (!b.HasContent && b.Extra.Count == 0) continue;
                Node3D node = b.ToNode($"Infra {ix},{iz}");
                node.Position = new Vector3((float)(tx * tile - cx), -groundY, (float)(tz * tile - cz));
                AddChild(node);
                tiles++;
            }
        _bounds = new Aabb(new Vector3(-reach, lowest, -reach), new Vector3(reach * 2, Math.Max(40f, verts.Max(v => v.Y) - lowest + 30f), reach * 2));
        _facts.Add(("World", $"{type}, seed {seed}"));
        _facts.Add(("Showing", name));
        _facts.Add(("Patch", $"{reach * 2:0} m square at {cx:0}, {cz:0}; {tiles} tiles, {count} pieces"));
        _facts.Add(("Ground height", $"{groundY:0} m"));
        _facts.Add(("Built at", world.Profile.Before.ToString() + " tech before the fall"));
    }

    public override Aabb? FrameBounds => _bounds;
    public override IEnumerable<(string Label, string Value)> Facts() => _facts;
}
