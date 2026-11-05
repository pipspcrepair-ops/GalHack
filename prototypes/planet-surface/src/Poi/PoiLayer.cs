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
    readonly List<(OmniLight3D Light, float Energy, LightMode Mode, float Phase)> _animated = new();
    double _time;
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
        _time += delta;
        Animate(_time);
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
        AddSmoke(node, inst);
        return node;
    }

    /// <summary>
    /// The place's own lights as the POI generator left them at spawn: the lamps still burning (most
    /// are dead in an abandoned place), the one lit window, beacons on masts and towers, the Bloom's
    /// glow, and the core's glow only where a faction holds the place. Lights above ground and
    /// beacons come first so the place carries from a distance; the count is capped for the frame.
    /// </summary>
    void AddLights(Node3D node, PoiInstance inst)
    {
        const int MaxLights = 20;
        var chosen = inst.Lights
            .OrderByDescending(l => l.Mode == LightMode.Beacon)
            .ThenByDescending(l => l.Cell.Y >= 0)
            .ThenByDescending(l => l.Cell.Y)
            .Take(MaxLights);
        foreach (LightSpot spot in chosen)
        {
            var light = new OmniLight3D
            {
                LightColor = new Color(spot.Color.R / 255f, spot.Color.G / 255f, spot.Color.B / 255f),
                LightEnergy = spot.Energy, OmniRange = spot.Range, ShadowEnabled = false,
                Position = new Vector3(spot.Cell.X + 0.5f, spot.Cell.Y + 0.5f, spot.Cell.Z + 0.5f),
            };
            node.AddChild(light);
            if (spot.Mode is LightMode.Flicker or LightMode.Beacon or LightMode.Pulse or LightMode.Bloom)
                _animated.Add((light, spot.Energy, spot.Mode, (float)(Hash.Cell(spot.Cell, 7) % 1000) / 160f));
        }
        if (inst.HasCore)
        {
            Int3 core = inst.CoreCell;
            node.AddChild(new OmniLight3D
            {
                LightColor = new Color("3de8ff"), LightEnergy = inst.State == PoiState.Active ? 1.4f : 0.4f, OmniRange = 6f,
                Position = new Vector3(core.X + 0.5f, core.Y + 1.6f, core.Z + 0.5f),
            });
        }
    }

    /// <summary>A thin column of smoke (or Bloom spores) where something still burns or vents: quiet, seen from afar.</summary>
    static void AddSmoke(Node3D node, PoiInstance inst)
    {
        foreach (Marker m in inst.Markers.Where(m => m.Kind == "smoke").Take(2))
        {
            bool spores = m.Note == "spores";
            var tint = spores ? new Color(0.55f, 0.42f, 0.62f) : new Color(0.42f, 0.42f, 0.42f);
            var ramp = new Gradient();
            ramp.SetColor(0, new Color(tint, 0f));
            ramp.SetOffset(0, 0f);
            ramp.SetColor(1, new Color(tint, 0f));
            ramp.SetOffset(1, 1f);
            ramp.AddPoint(0.15f, new Color(tint, spores ? 0.22f : 0.3f));
            ramp.AddPoint(0.6f, new Color(tint.Lightened(0.15f), 0.14f));
            var curve = new Curve();
            curve.AddPoint(new Vector2(0f, 0.4f));
            curve.AddPoint(new Vector2(1f, 1f));
            var smoke = new CpuParticles3D
            {
                Name = spores ? "Spores" : "Smoke",
                Amount = 28, Lifetime = 16f, Preprocess = 16f, FixedFps = 20, LocalCoords = false,
                Mesh = new QuadMesh
                {
                    Size = new Vector2(3f, 3f),
                    Material = new StandardMaterial3D
                    {
                        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
                        DisableReceiveShadows = true,
                    },
                },
                Direction = Vector3.Up, Spread = 6f,
                InitialVelocityMin = 1.4f, InitialVelocityMax = 2.2f,
                Gravity = new Vector3(0.35f, 0.05f, 0.12f),
                ScaleAmountMin = 1.2f, ScaleAmountMax = 2.4f, ScaleAmountCurve = curve,
                ColorRamp = ramp,
                EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.8f,
                Position = new Vector3(m.Cell.X + 0.5f, m.Cell.Y + 1.5f, m.Cell.Z + 0.5f),
                VisibilityAabb = new Aabb(new Vector3(-40, -4, -40), new Vector3(80, 60, 80)),
            };
            node.AddChild(smoke);
        }
    }

    /// <summary>Flickering lamps, blinking beacons and the Bloom's slow pulse.</summary>
    void Animate(double t)
    {
        foreach (var (light, energy, mode, phase) in _animated)
        {
            if (!IsInstanceValid(light)) continue;
            float s = (float)t + phase;
            light.LightEnergy = mode switch
            {
                LightMode.Beacon => (s % 1.6f) < 0.25f ? energy : energy * 0.05f,
                LightMode.Flicker => energy * (Mathf.Sin(s * 23f) * Mathf.Sin(s * 7.3f) > 0.55f ? 0.15f : 0.85f + 0.15f * Mathf.Sin(s * 41f)),
                _ => energy * (0.6f + 0.4f * Mathf.Sin(s * 0.9f)),
            };
        }
    }

    public void Clear()
    {
        foreach (Place p in _places) p.Node?.QueueFree();
        _places.Clear();
        _building.Clear();
        _animated.Clear();
        _world = null;
    }
}
