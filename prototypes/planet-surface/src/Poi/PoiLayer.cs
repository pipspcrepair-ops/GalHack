using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PoiGenerator;
using PoiGenerator.Gen;
using PlanetSurface.World;
using Material = Godot.Material;

namespace PlanetSurface.Poi;

/// <summary>
/// Draws the planet's POIs: the POI generator's own mesher builds each place's chunked block
/// meshes (in its own grid, one unit a cell) on a worker; here they become meshes and collision.
/// The mesher's buried ground is turned on with an empty plug, so faces against the ground are
/// culled and the voxel terrain itself (cut and filled by the place's stamp) is the ground around
/// and under it. Places sit at their canonical position, drawn at the copy nearest the camera.
/// </summary>
public partial class PoiLayer : Node3D
{
    sealed class Place
    {
        public PoiSite Site;
        public Node3D Node;
    }

    readonly List<Place> _places = new();
    readonly List<Task<(Place, List<ChunkMesh>)>> _building = new();
    PlanetWorld _world;

    public IReadOnlyList<PoiSite> Sites => _world?.Pois ?? new List<PoiSite>();
    public bool Built => _building.Count == 0 && _places.All(p => p.Node != null);

    public void Build(PlanetWorld world)
    {
        Clear();
        _world = world;
        foreach (PoiSite site in world.Pois)
        {
            var place = new Place { Site = site };
            _places.Add(place);
            _building.Add(Task.Run(() => (place, Mesh(site))));
        }
    }

    static List<ChunkMesh> Mesh(PoiSite site)
    {
        BlockGrid g = site.Instance.Blocks;
        var options = new MeshOptions
        {
            Doors = true,
            Collision = true,
            Detail = true,
            Rock = true,
            // An empty plug: nothing is drawn for it, but faces against buried ground are culled.
            RockMin = new Int3(1, 0, 1),
            RockMax = new Int3(0, 0, 0),
            RockBottom = g.Min.Y,
        };
        return Mesher.Build(g, site.Instance.Layout.Occupied, options);
    }

    public override void _Process(double delta)
    {
        for (int i = _building.Count - 1; i >= 0; i--)
        {
            if (!_building[i].IsCompleted) continue;
            var (place, meshes) = _building[i].Result;
            _building.RemoveAt(i);
            if (!_places.Contains(place)) continue;
            place.Node = BuildNode(place.Site, meshes);
            AddChild(place.Node);
            break; // one place per frame keeps the main thread smooth
        }
        if (_world == null) return;
        Vector3 camera = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        foreach (Place p in _places)
        {
            if (p.Node == null) continue;
            var origin = new Vector3(p.Site.Origin.X, p.Site.Origin.Y, p.Site.Origin.Z);
            p.Node.Position = _world.NearestCopy(origin, camera);
        }
    }

    Node3D BuildNode(PoiSite site, List<ChunkMesh> meshes)
    {
        PoiInstance inst = site.Instance;
        float glow = inst.State switch { PoiState.Active => 2.4f, PoiState.Infested => 2.0f, _ => 1.6f };
        var materials = new BlockMaterials(glow);
        var node = new Node3D { Name = site.Family.Id };
        var body = new StaticBody3D { Name = "Collision", CollisionLayer = Layers.Poi, CollisionMask = 0 };
        node.AddChild(body);
        foreach (ChunkMesh chunk in meshes)
        {
            node.AddChild(new MeshInstance3D { Mesh = MeshBuild.ToMesh(chunk, materials.BySurface), Name = $"Chunk {chunk.Chunk}" });
            if (chunk.Collision.Count > 0) body.AddChild(new CollisionShape3D { Shape = MeshBuild.ToShape(chunk) });
        }
        AddLights(node, inst);
        return node;
    }

    /// <summary>A lamp per lit room when the place has power, the core's glow always.</summary>
    static void AddLights(Node3D node, PoiInstance inst)
    {
        Layout layout = inst.Layout;
        var light = new Color(inst.Kit.Light.R / 255f, inst.Kit.Light.G / 255f, inst.Kit.Light.B / 255f);
        if (inst.State == PoiState.Active)
            foreach (PlacedModule m in layout.Modules.Take(8))
                node.AddChild(new OmniLight3D
                {
                    LightColor = light, LightEnergy = 1.3f, OmniRange = System.Math.Max(m.Shape.W, m.Shape.D) * 0.85f + 2f, ShadowEnabled = false,
                    Position = new Vector3((m.Min.X + m.Max.X + 1) / 2f, m.Min.Y + 3f, (m.Min.Z + m.Max.Z + 1) / 2f),
                });
        Int3 core = inst.CoreCell;
        node.AddChild(new OmniLight3D
        {
            LightColor = new Color("3de8ff"), LightEnergy = inst.State == PoiState.Active ? 1.4f : 0.4f, OmniRange = 6f,
            Position = new Vector3(core.X + 0.5f, core.Y + 1.6f, core.Z + 0.5f),
        });
    }

    public void Clear()
    {
        foreach (Place p in _places) p.Node?.QueueFree();
        _places.Clear();
        _building.Clear();
        _world = null;
    }
}
