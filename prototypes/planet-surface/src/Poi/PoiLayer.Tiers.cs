using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PoiGenerator;
using PoiGenerator.Gen;
using Material = Godot.Material;

namespace PlanetSurface.Poi;

/// <summary>
/// The planet's places by distance, for a game that stands near places up itself (the slice). Every
/// place has one small mesh for the far distance, made while the planet loads: 4 m cells, solid where
/// enough stands in them, one draw. Within <see cref="MidRadius"/> it gives way to the same blocks merged
/// into big flat quads, a mesh for each 32 m cube of the place, built on a worker and put in a little a
/// frame. Nothing here collides: a place near enough to touch stands as a structure, and that structure
/// hides this layer's cubes one by one as its own full-detail ones arrive (<see cref="HideGroup"/>).
/// </summary>
public partial class PoiLayer
{
    /// <summary>Draw by distance, as above, rather than every place whole and solid.</summary>
    public static bool Tiered;

    /// <summary>The side of the cubes a place is drawn in.</summary>
    public const int GroupSize = 32;

    /// <summary>Within this of the camera (to a place's nearest edge) its merged blocks are built; past <see cref="MidDrop"/> they go.</summary>
    public float MidRadius = 760f, MidDrop = 960f;

    /// <summary>Main-thread time a frame may spend putting meshes in, ms.</summary>
    const double TierBudgetMs = 2f;

    int _tierTimer;
    Place _midJob;

    /// <summary>
    /// A city's buildings far off: a tile of the city at a time, their far meshes merged into one, one draw for
    /// the lot. A whole skyline is a few dozen draws, not hundreds. Its buildings come in close as a tile: once
    /// the camera is within <see cref="CityMidRadius"/> of any of them, all of them get their merged blocks
    /// (with their floors inside), and the tile's far mesh goes once they all have.
    /// </summary>
    sealed class Tile
    {
        public readonly List<Place> Members = new();
        public PlanetSurface.Poi.PoiSite First;
        public Task<ChunkMesh> Mesh;
        public Node3D Node;
        public MeshInstance3D Low;
        public float Distance;
    }

    readonly List<Tile> _tiles = new();
    readonly Dictionary<Place, Tile> _tileOf = new();

    /// <summary>A city tile's side, metres.</summary>
    public const float TileSize = 256f;

    /// <summary>Within this of a city tile its buildings' merged blocks are built; past <see cref="CityMidDrop"/> they go. Nearer than a lone place's: a city has hundreds.</summary>
    public float CityMidRadius = 420f, CityMidDrop = 560f;

    /// <summary>A city building's lights in the distance: a few, so a lit district glows without hundreds of lights.</summary>
    const int CityLights = 3;

    void BuildTiered(PlanetSurface.World.PlanetWorld world)
    {
        var tiles = new Dictionary<(int, int), Tile>();
        foreach (PoiSite site in world.Pois)
        {
            var place = new Place { Site = site };
            _places.Add(place);
            BlockGrid g = site.Instance.Blocks;
            CellMask occupied = site.Instance.Layout.Occupied;
            if (site.Siting != "city")
            {
                place.LowMesh = Task.Run(() => Mesher.Coarse(g, occupied));
                continue;
            }
            var key = ((int)Mathf.Floor(site.Origin.X / TileSize), (int)Mathf.Floor(site.Origin.Z / TileSize));
            if (!tiles.TryGetValue(key, out Tile tile)) tiles[key] = tile = new Tile { First = site };
            tile.Members.Add(place);
            _tileOf[place] = tile;
        }
        double w = world.Map.WidthMeters, h = world.Map.HeightMeters;
        foreach (Tile tile in tiles.Values)
        {
            _tiles.Add(tile);
            var members = tile.Members.Select(p => p.Site).ToList();
            var anchor = tile.First.Origin;
            tile.Mesh = Task.Run(() =>
            {
                // Each building's far mesh moved by its offset from the tile's first, the short way round the seams.
                var all = new ChunkMesh();
                foreach (PoiSite s in members)
                {
                    ChunkMesh one = Mesher.Coarse(s.Instance.Blocks, s.Instance.Layout.Occupied);
                    float ox = (float)Planets.Gen.Mathx.WrapDelta(s.Origin.X - anchor.X, w), oy = s.Origin.Y - anchor.Y, oz = (float)Planets.Gen.Mathx.WrapDelta(s.Origin.Z - anchor.Z, h);
                    all.Quads += one.Quads;
                    for (int k = 0; k < one.Surfaces.Length; k++)
                    {
                        MeshBuffer from = one.Surfaces[k], to = all.Surfaces[k];
                        int first = to.VertexCount;
                        for (int v = 0; v < from.VertexCount; v++)
                        {
                            to.Positions.Add(from.Positions[v * 3] + ox);
                            to.Positions.Add(from.Positions[v * 3 + 1] + oy);
                            to.Positions.Add(from.Positions[v * 3 + 2] + oz);
                        }
                        to.Normals.AddRange(from.Normals);
                        to.Colors.AddRange(from.Colors);
                        foreach (int i in from.Indices) to.Indices.Add(first + i);
                    }
                }
                return all;
            });
        }
    }

    /// <summary>Puts a finished city tile in: its merged far mesh, and each of its buildings' own node (lights, smoke, dressing; its merged blocks come later).</summary>
    void BuildTile(Tile tile)
    {
        tile.Node = new Node3D { Name = $"City tile {tile.First.Origin.X / (int)TileSize},{tile.First.Origin.Z / (int)TileSize}" };
        tile.Low = new MeshInstance3D { Name = "Low", Mesh = MeshBuild.ToMesh(tile.Mesh.Result, MaterialsFor(tile.First.Instance).BySurface), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        tile.Node.AddChild(tile.Low);
        tile.Mesh = null;
        AddChild(tile.Node);
        foreach (Place p in tile.Members)
        {
            PoiInstance inst = p.Site.Instance;
            p.Node = new Node3D { Name = p.Site.Family.Id };
            AddLights(p.Node, inst, CityLights);
            foreach (Node n in p.Node.GetChildren())
                if (n is OmniLight3D l)
                {
                    l.DistanceFadeEnabled = true;
                    l.DistanceFadeBegin = 500f;
                    l.DistanceFadeLength = 120f;
                }
            // A few fires still smoking over a city, not one in every building.
            if (Hash.Cell(new Int3(p.Site.Origin.X, 0, p.Site.Origin.Z), 0x5E0) % 6 == 0) AddSmoke(p.Node, inst);
            DressFromDecoLibrary(p.Node, inst);
            AddChild(p.Node);
        }
    }

    static MeshOptions MidOptions(BlockGrid g) => new()
    {
        ChunkSize = GroupSize, Merged = true, Doors = true, Collision = false, Detail = false, Rock = true,
        // An empty plug, as the near drawing has: every outer face stays, however deep.
        RockMin = new Int3(1, 0, 1), RockMax = new Int3(0, 0, 0), RockBottom = g.Min.Y, GroundShow = MeshOptions.AllGround,
    };

    static readonly Dictionary<float, BlockMaterials> Shared = new();

    static BlockMaterials MaterialsFor(PoiInstance inst)
    {
        float glow = inst.State switch { PoiState.Active => 2.4f, PoiState.Infested => 2.0f, _ => 1.6f };
        if (!Shared.TryGetValue(glow, out BlockMaterials m)) Shared[glow] = m = new BlockMaterials(glow);
        return m;
    }

    Node3D BuildLow(Place p)
    {
        PoiInstance inst = p.Site.Instance;
        var node = new Node3D { Name = p.Site.Family.Id };
        p.Low = new MeshInstance3D { Name = "Low", Mesh = MeshBuild.ToMesh(p.LowMesh.Result, MaterialsFor(inst).BySurface), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        node.AddChild(p.Low);
        p.LowMesh = null;
        AddLights(node, inst);
        foreach (Node n in node.GetChildren())
            if (n is OmniLight3D l)
            {
                l.DistanceFadeEnabled = true;
                l.DistanceFadeBegin = 500f;
                l.DistanceFadeLength = 120f;
            }
        AddSmoke(node, inst);
        DressFromDecoLibrary(node, inst);
        return node;
    }

    float Away(Place p, Vector3 camera)
    {
        var lo = p.Site.Footprint.Min;
        var hi = p.Site.Footprint.Max;
        Vector3 c = _world.NearestCopy(new Vector3((lo.X + hi.X + 1) * 0.5f, 0f, (lo.Z + hi.Z + 1) * 0.5f), camera);
        float hx = (hi.X - lo.X + 1) * 0.5f, hz = (hi.Z - lo.Z + 1) * 0.5f;
        float dx = Mathf.Max(0f, Mathf.Abs(camera.X - c.X) - hx), dz = Mathf.Max(0f, Mathf.Abs(camera.Z - c.Z) - hz);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>A far mesh sinks with the ground under it over the round world's horizon (<see cref="Terrain.TerrainStreamer.RoundSink"/>), or it would stand over it.</summary>
    static void SinkFar(MeshInstance3D low, float distance)
    {
        if (low == null) return;
        var at = new Vector3(0f, -Terrain.TerrainStreamer.RoundSink(distance), 0f);
        if (low.Position != at) low.Position = at;
    }

    void TickTiers(Vector3 camera)
    {
        bool built = false;
        foreach (Place p in _places)
        {
            if (p.Node != null || p.LowMesh is not { IsCompleted: true }) continue;
            p.Node = BuildLow(p);
            AddChild(p.Node);
            built = true;
            break;
        }
        foreach (Tile t in _tiles)
        {
            if (t.Node != null) { Vector3 at = _world.NearestCopy(new Vector3(t.First.Origin.X, t.First.Origin.Y, t.First.Origin.Z), camera); if (t.Node.Position != at) t.Node.Position = at; continue; }
            if (built || t.Mesh is not { IsCompleted: true }) continue;
            BuildTile(t);
            built = true;
        }
        if (_midJob != null) WorkMid();
        if (--_tierTimer > 0) return;
        _tierTimer = 12;
        Place next = null;
        float best = float.MaxValue;
        // A city's tiles come in whole: each building in one goes by the nearest of them.
        foreach (Tile t in _tiles)
        {
            t.Distance = float.MaxValue;
            foreach (Place p in t.Members) t.Distance = Mathf.Min(t.Distance, Away(p, camera));
            SinkFar(t.Low, t.Distance);
        }
        foreach (Place p in _places)
        {
            if (p.Node == null) continue;
            Tile tile = _tileOf.GetValueOrDefault(p);
            float d = tile?.Distance ?? Away(p, camera), radius = tile != null ? CityMidRadius : MidRadius, drop = tile != null ? CityMidDrop : MidDrop;
            if (p.Mid != null && d > drop && p != _midJob)
            {
                p.Mid.QueueFree();
                p.Mid = null;
                p.Cells.Clear();
                p.Whole = false;
            }
            // A place you are all but in is never its far blocks, even for the moment its mid ones take.
            bool low = !p.Whole && d > 200f;
            if (p.Low != null && p.Low.Visible != low) p.Low.Visible = low;
            if (tile == null) SinkFar(p.Low, d);
            if (p.Mid == null && p != _midJob && d < radius && d - radius < best)
            {
                best = d - radius;
                next = p;
            }
        }
        // A tile's far mesh stands until every building in it has its blocks in.
        foreach (Tile t in _tiles)
        {
            bool show = t.Members.Any(p => !p.Whole);
            if (t.Low != null && t.Low.Visible != show) t.Low.Visible = show;
        }
        MidPending = next != null || _midJob != null;
        if (next != null && _midJob == null)
        {
            _midJob = next;
            BlockGrid g = next.Site.Instance.Blocks;
            CellMask occupied = next.Site.Instance.Layout.Occupied;
            next.MidMeshes = Task.Run(() => Mesher.Build(g, occupied, MidOptions(g)));
        }
    }

    void WorkMid()
    {
        Place p = _midJob;
        if (!p.MidMeshes.IsCompleted) return;
        if (p.MidMeshes.IsFaulted)
        {
            GD.PushError($"could not draw {p.Site.Name}: {p.MidMeshes.Exception?.GetBaseException()}");
            p.MidMeshes = null;
            _midJob = null;
            return;
        }
        p.Mid ??= new Node3D { Name = "Mid", Visible = false };
        if (p.Mid.GetParent() == null) p.Node.AddChild(p.Mid);
        List<ChunkMesh> meshes = p.MidMeshes.Result;
        Material[] materials = MaterialsFor(p.Site.Instance).BySurface;
        var clock = Stopwatch.StartNew();
        while (p.MidNext < meshes.Count && clock.Elapsed.TotalMilliseconds < TierBudgetMs)
        {
            ChunkMesh c = meshes[p.MidNext++];
            var cube = new MeshInstance3D { Name = $"Cube {c.Chunk}", Mesh = MeshBuild.ToMesh(c, materials), Visible = !p.Hidden.Contains(c.Chunk) };
            p.Mid.AddChild(cube);
            p.Cells[c.Chunk] = cube;
        }
        if (p.MidNext < meshes.Count) return;
        p.Mid.Visible = true;
        if (p.Low != null) p.Low.Visible = false;
        p.Whole = true;
        p.MidMeshes = null;
        p.MidNext = 0;
        _midJob = null;
    }

    /// <summary>Hides or shows one 32 m cube of a place: its structure's own near meshes have taken over there, or just given it back.</summary>
    public void HideGroup(PoiSite site, Int3 group, bool hide)
    {
        Place p = _places.Find(x => x.Site == site);
        if (p == null) return;
        if (hide) p.Hidden.Add(group);
        else p.Hidden.Remove(group);
        if (p.Cells.TryGetValue(group, out MeshInstance3D cube)) cube.Visible = !hide;
    }

    /// <summary>Whether a place in reach still waits for its merged blocks (tests and proof frames wait for none).</summary>
    public bool MidPending { get; private set; } = true;

    /// <summary>Whether a place's mid blocks are in (tests).</summary>
    public bool MidReady(PoiSite site) => _places.Find(x => x.Site == site)?.Whole == true;
}
