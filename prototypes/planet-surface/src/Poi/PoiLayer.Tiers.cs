using System.Collections.Generic;
using System.Diagnostics;
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

    void BuildTiered(PlanetSurface.World.PlanetWorld world)
    {
        foreach (PoiSite site in world.Pois)
        {
            var place = new Place { Site = site };
            _places.Add(place);
            BlockGrid g = site.Instance.Blocks;
            CellMask occupied = site.Instance.Layout.Occupied;
            place.LowMesh = Task.Run(() => Mesher.Coarse(g, occupied));
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

    void TickTiers(Vector3 camera)
    {
        foreach (Place p in _places)
        {
            if (p.Node != null || p.LowMesh is not { IsCompleted: true }) continue;
            p.Node = BuildLow(p);
            AddChild(p.Node);
            break;
        }
        if (_midJob != null) WorkMid();
        if (--_tierTimer > 0) return;
        _tierTimer = 12;
        Place next = null;
        float best = MidRadius;
        foreach (Place p in _places)
        {
            if (p.Node == null) continue;
            float d = Away(p, camera);
            if (p.Mid != null && d > MidDrop && p != _midJob)
            {
                p.Mid.QueueFree();
                p.Mid = null;
                p.Cells.Clear();
                p.Whole = false;
            }
            // A place you are all but in is never its far blocks, even for the moment its mid ones take.
            bool low = !p.Whole && d > 200f;
            if (p.Low.Visible != low) p.Low.Visible = low;
            if (p.Mid == null && p != _midJob && d < best)
            {
                best = d;
                next = p;
            }
        }
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
        p.Low.Visible = false;
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

    /// <summary>Whether a place's mid blocks are in (tests).</summary>
    public bool MidReady(PoiSite site) => _places.Find(x => x.Site == site)?.Whole == true;
}
