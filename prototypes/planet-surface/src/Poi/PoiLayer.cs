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
/// The mesher's buried ground is turned on with an empty plug and shows every face: the voxel terrain
/// itself (cut and filled by the place's stamp) is the ground around and under it, and it can be dug away. Places sit at their canonical position, drawn at the copy nearest the camera.
/// </summary>
public partial class PoiLayer : Node3D
{
    sealed class Place
    {
        public PoiSite Site;
        public Node3D Node;
        // By distance (PoiLayer.Tiers.cs): the far mesh, the merged cubes, and which cubes a structure has taken over.
        public Task<ChunkMesh> LowMesh;
        public MeshInstance3D Low;
        public Node3D Mid;
        public Task<List<ChunkMesh>> MidMeshes;
        public int MidNext;
        public bool Whole;
        public readonly Dictionary<Int3, MeshInstance3D> Cells = new();
        public readonly HashSet<Int3> Hidden = new();
    }

    readonly List<Place> _places = new();
    readonly List<Task<(Place, List<ChunkMesh>)>> _building = new();
    readonly List<(OmniLight3D Light, float Energy, LightMode Mode, float Phase, MeshInstance3D Glow)> _animated = new();
    /// <summary>Lamps with a colour and an animation of their own (PoiLights): their light and glow follow it.</summary>
    readonly List<(OmniLight3D Light, float Energy, GalHack.Structures.LightSettings Style, MeshInstance3D Glow)> _styled = new();
    double _time;
    PlanetWorld _world;

    public IReadOnlyList<PoiSite> Sites => _world?.Pois ?? new List<PoiSite>();

    /// <summary>The physics layer the places' collision is on: planet-surface's own by default; a game puts it on its world layer.</summary>
    public static uint PlaceLayer = Layers.Poi;

    /// <summary>The node drawn for a place, once built (null before): a game that stands a near place up itself hides this one's blocks.</summary>
    public Node3D NodeOf(PoiSite site) => _places.FirstOrDefault(p => p.Site == site)?.Node;
    public bool Built => _building.Count == 0 && _places.All(p => p.Node != null);

    public void Build(PlanetWorld world)
    {
        Clear();
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off; // places stand still; they only move by whole planets at a seam
        _world = world;
        if (Tiered)
        {
            BuildTiered(world);
            return;
        }
        foreach (PoiSite site in world.Pois)
        {
            var place = new Place { Site = site };
            _places.Add(place);
            _building.Add(Task.Run(() => (place, Mesh(site))));
        }
    }

    public static List<ChunkMesh> Mesh(PoiSite site)
    {
        BlockGrid g = site.Instance.Blocks;
        return Mesher.Build(g, site.Instance.Layout.Occupied, MeshOptions.OnPlanet(g.Min));
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
        if (Tiered) TickTiers(camera);
        foreach (Place p in _places)
        {
            if (p.Node == null) continue;
            var origin = new Vector3(p.Site.Origin.X, p.Site.Origin.Y, p.Site.Origin.Z);
            // Written only when it changes: a same-spot write still tells every body and mesh under the node.
            Vector3 at = _world.NearestCopy(origin, camera);
            if (p.Node.Position != at) p.Node.Position = at;
        }
    }

    Node3D BuildNode(PoiSite site, List<ChunkMesh> meshes)
    {
        PoiInstance inst = site.Instance;
        BlockMaterials materials = MaterialsFor(inst);
        var node = new Node3D { Name = site.Family.Id };
        var body = new StaticBody3D { Name = "Collision", CollisionLayer = PlaceLayer, CollisionMask = 0 };
        node.AddChild(body);
        foreach (ChunkMesh chunk in meshes)
        {
            node.AddChild(new MeshInstance3D { Mesh = MeshBuild.ToMesh(chunk, materials.BySurface), Name = $"Chunk {chunk.Chunk}" });
            if (chunk.Collision.Count > 0) body.AddChild(new CollisionShape3D { Shape = MeshBuild.ToShape(chunk) });
        }
        AddLights(node, inst);
        AddSmoke(node, inst);
        DressFromDecoLibrary(node, inst);
        return node;
    }

    /// <summary>
    /// The place's own lights as the POI generator left them at spawn: the lamps still burning (most
    /// are dead in an abandoned place), the one lit window, beacons on masts and towers, the Bloom's
    /// glow, and the core's glow only where a faction holds the place. Lights above ground and
    /// beacons come first so the place carries from a distance; the count is capped for the frame.
    /// </summary>
    void AddLights(Node3D node, PoiInstance inst, int MaxLights = 20)
    {
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
            MeshInstance3D glow = Glow(node, inst, spot);
            if (spot.Style is { } style) _styled.Add((light, spot.Energy, style, glow));
            else if (spot.Mode is LightMode.Flicker or LightMode.Beacon or LightMode.Pulse or LightMode.Bloom)
                _animated.Add((light, spot.Energy, spot.Mode, (float)(Hash.Cell(spot.Cell, 7) % 1000) / 160f, glow));
        }
        // Glowing paint: strips and panels that only glow.
        PoiLitFaces.Build(node, inst);
        AddCrops(node, inst);
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

    readonly Dictionary<int, Mesh> _cropMeshes = new();
    ShaderMaterial _cropMaterial;

    /// <summary>
    /// A place's crops as the world's own crop species (the flora module's grain, leaf and vine forms), one
    /// multimesh a species: rows in the fields and beds under glass. A world whose herbarium grows no crops
    /// shows bare soil. Each plant leans and turns a little, so a row isn't stamped.
    /// </summary>
    void AddCrops(Node3D node, PoiInstance inst)
    {
        if (inst.Crops.Count == 0 || _world?.Flora?.Book is not { } book) return;
        var crops = book.Plants.Where(p => p.Family == GalHack.Plants.PlantFamily.Crop).ToArray();
        if (crops.Length == 0) return;
        _cropMaterial ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/flora.gdshader") };
        foreach (var group in inst.Crops.GroupBy(c => c.Kind % crops.Length))
        {
            GalHack.Plants.Plant plant = crops[group.Key];
            if (!_cropMeshes.TryGetValue(plant.Index, out Mesh mesh)) _cropMeshes[plant.Index] = mesh = GalHack.Plants.PlantMeshes.ToGodot(plant.Near[0]);
            var spots = group.ToList();
            var buffer = new float[spots.Count * 16];
            for (int i = 0; i < spots.Count; i++)
            {
                Int3 c = spots[i].Cell;
                uint h = Hash.Cell(c, 0xC40);
                float turn = (h % 628) / 100f, scale = 0.85f + (h >> 10) % 30 / 100f;
                var basis = new Basis(Vector3.Up, turn).Scaled(Vector3.One * scale);
                double x = node.Position.X + c.X + 0.5, z = node.Position.Z + c.Z + 0.5;
                var tint = _world.Flora.Tint(plant, (h >> 4) % 100 / 100f, x, z);
                int o = i * 16;
                buffer[o] = basis.X.X; buffer[o + 1] = basis.Y.X; buffer[o + 2] = basis.Z.X; buffer[o + 3] = c.X + 0.5f;
                buffer[o + 4] = basis.X.Y; buffer[o + 5] = basis.Y.Y; buffer[o + 6] = basis.Z.Y; buffer[o + 7] = c.Y;
                buffer[o + 8] = basis.X.Z; buffer[o + 9] = basis.Y.Z; buffer[o + 10] = basis.Z.Z; buffer[o + 11] = c.Z + 0.5f;
                buffer[o + 12] = tint.R; buffer[o + 13] = tint.G; buffer[o + 14] = tint.B; buffer[o + 15] = 0f;
            }
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = spots.Count, Buffer = buffer };
            node.AddChild(new MultiMeshInstance3D { Name = "Crops", Multimesh = mm, MaterialOverride = _cropMaterial, VisibilityRangeEnd = 420f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    /// <summary>
    /// A glow you can see from far off: beacons, lit windows and lamps out in the open get a soft
    /// additive halo a few metres across that fades in past 40 m (up close the light itself does).
    /// Lights inside rooms get none, so nothing glows through a roof.
    /// </summary>
    static MeshInstance3D Glow(Node3D node, PoiInstance inst, LightSpot spot)
    {
        if (spot.Cell.Y < 0) return null;
        Block b = inst.Blocks.Get(spot.Cell);
        bool beacon = spot.Mode == LightMode.Beacon;
        bool outward = b.Material == PoiGenerator.Gen.Material.Lumen && (b.Role is Role.Window or Role.Light);
        if (!beacon && !outward && !(spot.Range >= 10f && !inst.Layout.Occupied[spot.Cell])) return null;
        var c = new Color(spot.Color.R / 255f, spot.Color.G / 255f, spot.Color.B / 255f);
        float size = beacon ? 8f : 3.5f;
        var glow = new MeshInstance3D
        {
            Name = "Glow",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new QuadMesh
            {
                Size = new Vector2(size, size),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    AlbedoColor = new Color(c, 0.55f),
                    AlbedoTexture = GlowTexture,
                    DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
                    DistanceFadeMinDistance = 40f, DistanceFadeMaxDistance = 120f,
                },
            },
            Position = new Vector3(spot.Cell.X + 0.5f, spot.Cell.Y + 0.5f, spot.Cell.Z + 0.5f),
        };
        node.AddChild(glow);
        return glow;
    }

    static Texture2D _glowTexture;
    /// <summary>A soft round falloff, made once.</summary>
    static Texture2D GlowTexture
    {
        get
        {
            if (_glowTexture != null) return _glowTexture;
            var g = new Gradient();
            g.SetColor(0, new Color(1, 1, 1, 1));
            g.SetColor(1, new Color(1, 1, 1, 0));
            _glowTexture = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 64, Height = 64 };
            return _glowTexture;
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
                Amount = 40, Lifetime = 26f, Preprocess = 26f, FixedFps = 20, LocalCoords = false,
                Mesh = new QuadMesh
                {
                    Size = new Vector2(5f, 5f),   // big enough to read as a column from 400 m
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
                InitialVelocityMin = 2.2f, InitialVelocityMax = 3.2f,
                Gravity = new Vector3(0.35f, 0.05f, 0.12f),
                ScaleAmountMin = 1.2f, ScaleAmountMax = 2.8f, ScaleAmountCurve = curve,
                ColorRamp = ramp,
                EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.8f,
                Position = new Vector3(m.Cell.X + 0.5f, m.Cell.Y + 1.5f, m.Cell.Z + 0.5f),
                VisibilityAabb = new Aabb(new Vector3(-60, -4, -60), new Vector3(120, 110, 120)),
            };
            node.AddChild(smoke);
        }
    }

    /// <summary>Flickering lamps, blinking beacons and the Bloom's slow pulse.</summary>
    void Animate(double t)
    {
        foreach (var (light, energy, mode, phase, glow) in _animated)
        {
            if (!IsInstanceValid(light)) continue;
            float s = (float)t + phase;
            light.LightEnergy = mode switch
            {
                LightMode.Beacon => (s % 1.6f) < 0.25f ? energy : energy * 0.05f,
                LightMode.Flicker => energy * (Mathf.Sin(s * 23f) * Mathf.Sin(s * 7.3f) > 0.55f ? 0.15f : 0.85f + 0.15f * Mathf.Sin(s * 41f)),
                _ => energy * (0.6f + 0.4f * Mathf.Sin(s * 0.9f)),
            };
            if (glow != null && IsInstanceValid(glow)) glow.Visible = light.LightEnergy > energy * 0.3f;
        }
        foreach (var (light, energy, style, glow) in _styled)
        {
            if (!IsInstanceValid(light)) continue;
            (light.LightEnergy, light.LightColor) = PoiLitFaces.At(style, energy, t);
            if (glow != null && IsInstanceValid(glow)) glow.Visible = light.LightEnergy > energy * 0.3f;
        }
    }

    public void Clear()
    {
        foreach (Place p in _places) p.Node?.QueueFree();
        _places.Clear();
        foreach (Tile t in _tiles) t.Node?.QueueFree();
        _tiles.Clear();
        _tileOf.Clear();
        _building.Clear();
        _animated.Clear();
        _styled.Clear();
        _midJob = null;
        _world = null;
    }
}
