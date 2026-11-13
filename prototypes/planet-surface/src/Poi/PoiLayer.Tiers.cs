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

    /// <summary>Places whose merged blocks are being built (on workers, then put in a little a frame), at most <see cref="MidJobs"/> at once.</summary>
    readonly List<Place> _midJobs = new();
    const int MidJobs = 3;
    readonly List<(float Rank, Place Place)> _midWanted = new();

    static bool Building(Place p) => p.MidMeshes != null;

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
        /// <summary>How far its far mesh has dissolved into its buildings' merged blocks (0..1), and which way it is going (+1, -1, 0).</summary>
        public float Blend;
        public int Way;
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
            // Its far mesh is made once it has been built (PoiBuilds); until then a plain block of its size stands for it.
            if (site.Siting != "city")
            {
                place.StandIn = StandIn(site);
                AddChild(place.StandIn);
                continue;
            }
            var key = ((int)Mathf.Floor(site.Origin.X / TileSize), (int)Mathf.Floor(site.Origin.Z / TileSize));
            if (!tiles.TryGetValue(key, out Tile tile)) tiles[key] = tile = new Tile { First = site };
            tile.Members.Add(place);
            _tileOf[place] = tile;
        }
        _tiles.AddRange(tiles.Values);
    }

    static StandardMaterial3D _standIn;

    /// <summary>A plain block over a place's footprint, as high as it stands: what shows of it until it has been built.</summary>
    MeshInstance3D StandIn(PoiSite site)
    {
        _standIn ??= new StandardMaterial3D { AlbedoColor = new Color(0.46f, 0.45f, 0.43f), Roughness = 1f };
        var lo = site.Footprint.Min;
        var hi = site.Footprint.Max;
        float top = site.Layout.Blocks.Min.Y + site.Layout.Blocks.Size.Y - 8; // (the layout's grid keeps 8 cells of air over the place)
        var size = new Vector3(hi.X - lo.X + 1, Mathf.Max(2f, top), hi.Z - lo.Z + 1);
        return new MeshInstance3D
        {
            Name = "Stand-in " + site.Family.Id, Mesh = new BoxMesh { Size = size, Material = _standIn }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = _world.NearestCopy(new Vector3((lo.X + hi.X + 1) * 0.5f, site.Origin.Y + size.Y * 0.5f, (lo.Z + hi.Z + 1) * 0.5f), _focus),
        };
    }

    /// <summary>A city tile's far mesh, once every building in it has been built.</summary>
    void StartTile(Tile tile)
    {
        double w = _world.Map.WidthMeters, h = _world.Map.HeightMeters;
        var members = tile.Members.Select(p => p.Site).ToList();
        {
            var anchor = tile.First.Origin;
            tile.Mesh = Task.Run(() =>
            {
                // Each building's far mesh moved by its offset from the tile's first, the short way round the seams.
                var all = new ChunkMesh();
                foreach (PoiSite s in members)
                {
                    ChunkMesh one = Mesher.Coarse(s.Built.Blocks, s.Layout.Occupied);
                    float ox = (float)Planets.Gen.Mathx.WrapDelta(s.Origin.X - anchor.X, w), oy = s.Origin.Y - anchor.Y, oz = (float)Planets.Gen.Mathx.WrapDelta(s.Origin.Z - anchor.Z, h);
                    all.Quads += one.Quads;
                    // Their finish codes with them, so the far tile is drawn in the same surfaces as each building near.
                    for (int k = 0; k < one.Surfaces.Length; k++) all.Surfaces[k].Append(one.Surfaces[k], 1f, new V3(ox, oy, oz));
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
            // (No smoke: a city fell long ago, and nothing in it still burns.)

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

    /// <summary>A far mesh (or merged blocks, at a long detail distance) sinks with the ground under it over the round world's horizon (<see cref="Terrain.TerrainStreamer.RoundSink"/>), or it would stand over it.</summary>
    static void SinkFar(Node3D low, float distance)
    {
        if (low == null) return;
        var at = new Vector3(0f, -Terrain.TerrainStreamer.RoundSink(distance), 0f);
        if (low.Position != at) low.Position = at;
    }

    /// <summary>How long a place's far mesh and its merged blocks take to dissolve one into the other, s.</summary>
    public const float BlendSeconds = 0.7f;

    /// <summary>The focus's own speed and way, m/s (the game sets it): at speed places' merged blocks are started further out, the way you are going first.</summary>
    public Vector3 Heading;

    /// <summary>How many seconds of travel ahead merged blocks are started, and the most that adds, m.</summary>
    const float AheadSeconds = 8f, MostAhead = 700f;

    /// <summary>Nearer than this a place's far mesh never shows, m: you are all but in it.</summary>
    const float InsideFar = 40f;

    double _lastTick = -1;

    void TickTiers(Vector3 camera)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        float dt = _lastTick < 0 ? 0f : (float)Mathf.Min(now - _lastTick, 0.1);
        _lastTick = now;
        bool built = false;
        // A place's far mesh as soon as it has been built (PoiBuilds builds them nearest first); a city tile's once all of it has.
        foreach (Place p in _places)
            if (p.Node == null && p.LowMesh == null && !_tileOf.ContainsKey(p) && p.Site.Built is { } made)
                p.LowMesh = Task.Run(() => Mesher.Coarse(made.Blocks, made.Layout.Occupied));
        foreach (Tile t in _tiles)
            if (t.Node == null && t.Mesh == null && t.Members.All(p => p.Site.Built != null)) StartTile(t);
        foreach (Place p in _places)
        {
            if (p.Node != null || p.LowMesh is not { IsCompleted: true }) continue;
            if (p.LowMesh.IsFaulted)
            {
                // Said once, and the place left without a far mesh: rethrown every frame it stopped the whole layer.
                GD.PushError($"could not make {p.Site.Name}'s far mesh: {p.LowMesh.Exception?.GetBaseException().Message}");
                p.LowMesh = null;
                continue;
            }
            p.Node = BuildLow(p);
            AddChild(p.Node);
            p.StandIn?.QueueFree();
            p.StandIn = null;
            built = true;
            break;
        }
        foreach (Tile t in _tiles)
        {
            if (t.Node != null) { Vector3 at = _world.NearestCopy(new Vector3(t.First.Origin.X, t.First.Origin.Y, t.First.Origin.Z), camera); if (t.Node.Position != at) t.Node.Position = at; continue; }
            if (built || t.Mesh is not { IsCompleted: true }) continue;
            if (t.Mesh.IsFaulted)
            {
                GD.PushError($"could not make a city tile's far mesh: {t.Mesh.Exception?.GetBaseException().Message}");
                t.Mesh = null;
                continue;
            }
            BuildTile(t);
            built = true;
        }
        if (_midJobs.Count > 0) WorkMid();
        foreach (Place p in _blending) StepBlend(p, dt);
        _blending.RemoveAll(p => p.Way == 0);
        foreach (Tile t in _tilesBlending) StepBlend(t, dt);
        _tilesBlending.RemoveAll(t => t.Way == 0);
        if (--_tierTimer > 0) return;
        _tierTimer = 12;
        _midWanted.Clear();
        float speed = new Vector2(Heading.X, Heading.Z).Length();
        float ahead = Mathf.Min(speed * AheadSeconds, MostAhead);
        // Merged blocks reach as far as the detail distance takes them (DetailReach): out to the horizon at the top.
        float midRadius = Terrain.DetailReach.Toward(MidRadius), midDrop = midRadius + MidDrop - MidRadius;
        float cityRadius = Terrain.DetailReach.Toward(CityMidRadius), cityDrop = cityRadius + CityMidDrop - CityMidRadius;
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
            float d = tile?.Distance ?? Away(p, camera);
            SinkFar(p.Mid, d);
            float radius = (tile != null ? cityRadius : midRadius) + ahead, drop = (tile != null ? cityDrop : midDrop) + ahead;
            if (tile == null)
            {
                // Out of reach: the far mesh dissolves back in over the merged blocks, and they go once it has.
                if (p.Whole && d > drop && p.Way != -1) Blend(p, -1);
                else if (p.Whole && d <= drop && p.Way == -1) Blend(p, +1);
                if (p.Mid != null && !p.Whole && d > drop && !Building(p) && p.Way == 0) FreeMid(p);
                if (p.Way == 0)
                {
                    bool low = !p.Whole && d > InsideFar;
                    if (p.Low != null && p.Low.Visible != low)
                    {
                        Fx.PopLog.Note("place", low ? "far blocks back" : "far blocks hidden, nothing in", d, p.Size, 1f);
                        p.Low.Visible = low;
                    }
                }
                SinkFar(p.Low, d);
            }
            if (p.Mid == null && !Building(p) && d < radius && p.Way == 0)
            {
                // The way you are going first; what you are leaving, not at all once it is half the reach off.
                float along = 0f;
                if (speed > 3f)
                {
                    var lo = p.Site.Footprint.Min;
                    var hi = p.Site.Footprint.Max;
                    Vector3 to = _world.NearestCopy(new Vector3((lo.X + hi.X + 1) * 0.5f, 0f, (lo.Z + hi.Z + 1) * 0.5f), camera) - camera;
                    along = (to.X * Heading.X + to.Z * Heading.Z) / speed;
                }
                if (along < 0f && d > radius * 0.5f) continue;
                _midWanted.Add((d - along * 0.5f - radius, p));
            }
        }
        foreach (Tile t in _tiles)
        {
            if (t.Low == null || t.Way != 0) continue;
            float drop = cityDrop + ahead;
            // Every building of a tile in: the tile's far mesh dissolves into them. Out of reach: back.
            if (t.Distance <= drop && t.Low.Visible && t.Members.All(p => p.Whole)) Blend(t, +1);
            else if (t.Distance > drop && !t.Low.Visible) Blend(t, -1);
            else if (t.Distance > drop && t.Low.Visible)
                foreach (Place p in t.Members)
                    if (p.Mid != null && !Building(p)) FreeMid(p);
        }
        MidPending = _midWanted.Count > 0 || _midJobs.Count > 0;
        if (_midJobs.Count >= MidJobs || _midWanted.Count == 0) return;
        _midWanted.Sort(static (a, b) => a.Rank.CompareTo(b.Rank));
        for (int i = 0; i < _midWanted.Count && _midJobs.Count < MidJobs; i++) StartMid(_midWanted[i].Place);
    }

    /// <summary>A place's merged blocks (and their occluders) built on a worker.</summary>
    void StartMid(Place p)
    {
        _midJobs.Add(p);
        BlockGrid g = p.Site.Instance.Blocks;
        CellMask occupied = p.Site.Instance.Layout.Occupied;
        p.MidMeshes = Task.Run(() => Mesher.Build(g, occupied, MidOptions(g)));
        p.MidOccluders = Occlude ? p.MidMeshes.ContinueWith(t => t.Result.ConvertAll(c => OccluderOf(c, g)), TaskContinuationOptions.OnlyOnRanToCompletion) : null;
    }

    readonly List<Place> _blending = new();
    readonly List<Tile> _tilesBlending = new();

    void FreeMid(Place p)
    {
        Fx.Fade.Forget(p.Mid);
        p.Mid.QueueFree();
        p.Mid = null;
        p.Cells.Clear();
        p.Occluders.Clear();
        p.Whole = false;
    }

    /// <summary>A place's far mesh starts dissolving into its merged blocks (+1), or back (-1).</summary>
    void Blend(Place p, int way)
    {
        Vector3 eye = GetViewport()?.GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        Fx.PopLog.Note("place", way > 0 ? "far to merged blocks" : "merged blocks to far", Away(p, eye), p.Size, BlendStep);
        p.Way = way;
        if (p.Low != null) p.Low.Visible = true;
        p.Mid.Visible = true;
        Bands(p.Low, p.Mid, p.Blend);
        if (!_blending.Contains(p)) _blending.Add(p);
    }

    void Blend(Tile t, int way)
    {
        Fx.PopLog.Note("city", way > 0 ? "tile far mesh to buildings" : "buildings to tile far mesh", t.Distance, TileSize, BlendStep);
        t.Way = way;
        t.Low.Visible = true;
        foreach (Place p in t.Members) p.Mid.Visible = true;
        Bands(t);
        if (!_tilesBlending.Contains(t)) _tilesBlending.Add(t);
    }

    /// <summary>The most of a blend that passes in one frame at 60 a second: what the pop-in log counts it as.</summary>
    static float BlendStep => 1f / (60f * BlendSeconds);

    static void Bands(MeshInstance3D low, Node3D mid, float blend)
    {
        if (low != null) Fx.Fade.Set(low, blend, 1f);
        Fx.Fade.Set(mid, 0f, blend);
    }

    void Bands(Tile t)
    {
        Fx.Fade.Set(t.Low, t.Blend, 1f);
        foreach (Place p in t.Members) Fx.Fade.Set(p.Mid, 0f, t.Blend);
    }

    void StepBlend(Place p, float dt)
    {
        p.Blend = Mathf.Clamp(p.Blend + p.Way * dt / BlendSeconds, 0f, 1f);
        Bands(p.Low, p.Mid, p.Blend);
        if (p.Way > 0 && p.Blend >= 1f)
        {
            if (p.Low != null)
            {
                p.Low.Visible = false;
                Fx.Fade.Clear(p.Low);
            }
            Fx.Fade.Clear(p.Mid);
            p.Way = 0;
        }
        else if (p.Way < 0 && p.Blend <= 0f)
        {
            if (p.Low != null) Fx.Fade.Clear(p.Low);
            FreeMid(p);
            p.Way = 0;
        }
    }

    void StepBlend(Tile t, float dt)
    {
        t.Blend = Mathf.Clamp(t.Blend + t.Way * dt / BlendSeconds, 0f, 1f);
        Bands(t);
        if (t.Way > 0 && t.Blend >= 1f)
        {
            t.Low.Visible = false;
            Fx.Fade.Clear(t.Low);
            foreach (Place p in t.Members) Fx.Fade.Clear(p.Mid);
            t.Way = 0;
        }
        else if (t.Way < 0 && t.Blend <= 0f)
        {
            Fx.Fade.Clear(t.Low);
            foreach (Place p in t.Members)
                if (p.Mid != null && !Building(p)) FreeMid(p);
            t.Way = 0;
        }
    }

    /// <summary>The finished places' merged blocks put in, nearest first, within one budget a frame.</summary>
    void WorkMid()
    {
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < _midJobs.Count && clock.Elapsed.TotalMilliseconds < TierBudgetMs; i++)
            if (WorkMid(_midJobs[i], clock)) _midJobs.RemoveAt(i--);
    }

    /// <summary>Puts in what it can of one place's merged blocks; true once it is done with (all in, or failed).</summary>
    bool WorkMid(Place p, Stopwatch clock)
    {
        if (!p.MidMeshes.IsCompleted || p.MidOccluders is { IsCompleted: false }) return false;
        if (p.MidMeshes.IsFaulted)
        {
            GD.PushError($"could not draw {p.Site.Name}: {p.MidMeshes.Exception?.GetBaseException()}");
            p.MidMeshes = null;
            return true;
        }
        p.Mid ??= new Node3D { Name = "Mid", Visible = false };
        if (p.Mid.GetParent() == null) p.Node.AddChild(p.Mid);
        List<ChunkMesh> meshes = p.MidMeshes.Result;
        Material[] materials = MaterialsFor(p.Site.Instance).BySurface;
        while (p.MidNext < meshes.Count && clock.Elapsed.TotalMilliseconds < TierBudgetMs)
        {
            ChunkMesh c = meshes[p.MidNext++];
            var cube = new MeshInstance3D { Name = $"Cube {c.Chunk}", Mesh = MeshBuild.ToMesh(c, materials), Visible = !p.Hidden.Contains(c.Chunk) };
            p.Mid.AddChild(cube);
            p.Cells[c.Chunk] = cube;
            if (p.MidOccluders is { IsCompletedSuccessfully: true }) AddOccluder(p, c.Chunk, p.MidOccluders.Result[p.MidNext - 1]);
        }
        if (p.MidNext < meshes.Count) return false;
        p.Whole = true;
        p.MidMeshes = null;
        p.MidOccluders = null;
        p.MidNext = 0;
        // A city's building waits for the rest of its tile, hidden; a lone place dissolves in now.
        if (_tileOf.ContainsKey(p)) return true;
        p.Blend = 0f;
        Blend(p, +1);
        return true;
    }

    /// <summary>
    /// One 32 m cube of a place shown through the part of the fade pattern its structure's own near
    /// meshes leave it (<paramref name="blend"/> to 1: they hold 0 to it), while the two dissolve one
    /// into the other; done, it is whole again (and hidden or shown by <see cref="HideGroup"/>).
    /// </summary>
    public void FadeGroup(PoiSite site, Int3 group, float blend, bool done = false)
    {
        Place p = _places.Find(x => x.Site == site);
        if (p == null || !p.Cells.TryGetValue(group, out MeshInstance3D cube)) return;
        if (done)
        {
            Fx.Fade.Clear(cube);
            return;
        }
        cube.Visible = true;
        Fx.Fade.Set(cube, blend, 1f);
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
