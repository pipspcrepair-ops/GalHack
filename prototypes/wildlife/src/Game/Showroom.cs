using System;
using System.Collections.Generic;
using Godot;
using TD = TerrainDigging.Terrain;

namespace Wildlife;

/// <summary>
/// The underground showroom: the fauna of caves and places, playable. Four stations, one shown
/// at a time (keys 1 to 4):
/// 1. A chamber of a generator cave (real voxel terrain): cave crawlers on its ceiling, moss grubs
///    at the moss, cracks in its walls.
/// 2. A tunnel dug with the survival tool into the same rock, a creeper waiting on its ceiling.
/// 3. An abandoned outpost built of blocks, three levels deep: a corridor with a creeper on the
///    ceiling and a vent, a store behind a door, a dark room with a lurker and grazing grubs (its
///    food chain), a lower hall and a deep nest.
/// 4. A derelict freighter's hold, where wreck nesters have built in the hold and the engine room.
/// Everything is lit by your shoulder lamp (L) and what glows. 5 goes back to the planet.
/// </summary>
public partial class Showroom : Node3D
{
    public WildlifeData Data { get; private set; }
    public DeepFauna Fauna { get; private set; }
    public Player Player { get; private set; }
    public Camera3D Camera { get; private set; }
    public Hud Hud { get; private set; }
    public ProceduralAudio Audio { get; private set; }
    public readonly Codex Codex = new();
    /// <summary>Screenshots: drive the camera by hand.</summary>
    public bool FreeCamera;

    /// <summary>One place: its geometry, its sim and its views.</summary>
    public sealed class Area
    {
        public string Name = "";
        public Node3D Root;
        public WildSim Sim;
        public ISolidSpace Space;
        public WildlifeView View;
        public TellView Tells;
        public LairView Lairs;
        public BlockPlace Place;
        public readonly List<CollisionShape3D> Shapes = new();
        public readonly List<(Node3D node, Vector3 at, float shake)> Grilles = new();
        public readonly List<Door> Doors = new();
        public readonly List<OmniLight3D> Blinkers = new();
    }

    public sealed class Door
    {
        public Node3D Pivot;
        public StaticBody3D Body;
        public Vector3 At;
        public float Shake, Open;
    }

    public readonly List<(string label, Area area, Vector3 start, Vector3 look)> Stations = new();
    public Area Caves { get; private set; }
    public Area Outpost { get; private set; }
    public Area Hold { get; private set; }
    /// <summary>The mutation row: one species at strengths 0, 0.5 and 1, side by side.</summary>
    public Area Gallery { get; private set; }
    public readonly List<Creature> Row = new();
    public static readonly float[] RowStrengths = { 0f, 0.5f, 1f };
    /// <summary>A settlement's own animals: stock in a pen, a draught beast by its cart, pets with a townsperson.</summary>
    public Area Town { get; private set; }
    public readonly List<Creature> Domestics = new();
    /// <summary>The stand-in townsperson the pets follow (townsfolk come later).</summary>
    public readonly DummyTarget Townsperson = new() { Name = "Townsperson", Health = new Health(100f) };
    Node3D _figure;
    float _walk;
    public Area Current { get; private set; }
    public int Station { get; private set; } = -1;
    public Vector3 Chamber { get; private set; }
    public Vector3 TunnelAxis { get; private set; }

    SpotLight3D _lamp;
    Godot.Environment _env;
    float _gunTimer, _sightTimer, _downedTimer, _time;
    static StandardMaterial3D _rock;

    public override void _Ready()
    {
        Data = DataFiles.LoadWildlife();
        Fauna = new DeepFauna(Data, 2024, 0.5f);
        BuildWorld();
        Audio = new ProceduralAudio { Name = "Audio" };
        AddChild(Audio);
        Camera = new Camera3D { Name = "Camera", Fov = 78f, Near = 0.05f, Far = 400f };
        AddChild(Camera);
        Camera.MakeCurrent();
        Player = new Player { Name = "Player", Camera = Camera };
        AddChild(Player);
        Camera.AddChild(Player.Muzzle);
        // The shoulder lamp: a warm, narrow beam, so the dark stays dark round its edges.
        _lamp = new SpotLight3D
        {
            Name = "Lamp", LightColor = new Color("ffe2b8"), LightEnergy = 4.2f, SpotRange = 26f, SpotAngle = 36f, SpotAngleAttenuation = 1.2f,
            ShadowEnabled = true, Position = new Vector3(0.25f, 0.1f, 0f),
        };
        Camera.AddChild(_lamp);
        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.Danger = UnderAttack;
        Hud.CardShown += _ => Audio.PlayUi("discover");

        Caves = BuildCaves();
        Outpost = BuildPlace(BlockPlace.Outpost(OutpostSeed()), new Color("4a4740"), new Color("6a655b"), new Color("3c3a36"));
        Hold = BuildPlace(HoldWithNesters(), new Color("2e3134"), new Color("3c4045"), new Color("26292c"));
        Gallery = BuildGallery();
        Town = BuildTown();
        Stations.Add(("A generator cave", Caves, Chamber + new Vector3(0f, 0.2f, 0f), Chamber + new Vector3(4f, 2.5f, 0f)));
        Stations.Add(("A tunnel dug into the hillside", Caves, TunnelAxis + new Vector3(0f, -1.4f, -4f), TunnelAxis + new Vector3(0f, 1.5f, 6f)));
        Stations.Add(($"{Outpost.Place.Name}", Outpost, Outpost.Place.Start, Outpost.Place.StartLook));
        Stations.Add(($"{Hold.Place.Name}", Hold, Hold.Place.Start, Hold.Place.StartLook));
        Stations.Add(($"{Gallery.Place.Name}: one species at mutation 0, 0.5 and 1", Gallery, Gallery.Place.Start, Gallery.Place.StartLook));
        Stations.Add(($"{Town.Place.Name}: stock in a pen, a draught beast by its cart, pets", Town, Town.Place.Start, Town.Place.StartLook));
        GoTo(0);
        if (!FreeCamera) Input.MouseMode = Input.MouseModeEnum.Captured;
        GetTree().AutoAcceptQuit = false;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Shutdown.Quit(this);
    }

    public override void _ExitTree()
    {
        if (IsInsideTree()) GetTree().AutoAcceptQuit = true;
    }

    ulong OutpostSeed()
    {
        for (ulong s = 41; s < 400; s++)
        {
            PoiPopulation pop = PoiFauna.Populate(BlockPlace.Outpost(s).Plan, Fauna, Data.Deep);
            if (!pop.Silent && pop.Count(DeepRole.Lurker) >= 1 && pop.Count(DeepRole.Ambusher) >= 2 && pop.Count(DeepRole.Subsister) >= 4 && pop.Count(DeepRole.Prowler) >= 1 && pop.Nests.Count >= 1) return s;
        }
        return 41;
    }

    BlockPlace HoldWithNesters()
    {
        for (ulong s = 7; s < 200; s++)
        {
            BlockPlace p = BlockPlace.Hold(s);
            PoiPopulation pop = PoiFauna.Populate(p.Plan, Fauna, Data.Deep);
            if (!pop.Silent && pop.Count(DeepRole.DerelictNester) >= 2) return p; // wreck nesters, as nests are no more
        }
        return BlockPlace.Hold();
    }

    // ---- The world --------------------------------------------------------------------

    void BuildWorld()
    {
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.01f, 0.012f, 0.016f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.32f, 0.36f, 0.44f),
            AmbientLightEnergy = 0.06f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.1f,
            FogEnabled = true,
            FogLightColor = new Color(0.05f, 0.055f, 0.06f),
            FogDensity = 0.03f,
            GlowEnabled = true,
            GlowIntensity = 1.0f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            AdjustmentEnabled = true,
            AdjustmentContrast = 1.08f,
            AdjustmentSaturation = 1.05f,
        };
        AddChild(new WorldEnvironment { Environment = _env });
    }

    Area NewArea(string name, ISolidSpace space)
    {
        var a = new Area { Name = name, Space = space };
        a.Root = new Node3D { Name = name };
        AddChild(a.Root);
        a.Sim = new WildSim(Data, new Terrain(5, 40f), new NavGrid(40f, 3f));
        a.View = new WildlifeView { Name = "Creatures" };
        a.Root.AddChild(a.View);
        a.View.Bind(a.Sim, null);
        a.Tells = new TellView { Name = "Tells" };
        a.Root.AddChild(a.Tells);
        a.Tells.Bind(a.Sim);
        a.Lairs = new LairView { Name = "Lairs" };
        a.Root.AddChild(a.Lairs);
        a.Sim.Telegraph += e => Audio?.Telegraph(e.Creature);
        a.Sim.Attacked += OnAttacked;
        a.Tells.Shown += e => Shake(a, e);
        a.Sim.Slipped += (c, crack, entering) =>
        {
            if (crack.Kind != CrackKind.Door || entering) return;
            foreach (Door d in a.Doors)
                if (d.At.DistanceTo(crack.Position) < 2f) d.Open = MathF.Max(d.Open, 0.01f); // burst open
        };
        return a;
    }

    Area BuildCaves()
    {
        TD.ChunkStore store = CaveSite.Store();
        var (axis, _, ty) = CaveSite.DigTunnel(store);
        TunnelAxis = axis;
        var (floor, _) = CaveSite.Chamber(store);
        Chamber = floor;
        var space = new VoxelSpace(store);
        Area a = NewArea("Caves", space);
        // Mesh the rock round the chamber and the tunnel.
        var chunks = new HashSet<TD.Int3>();
        void Around(Vector3 c, Vector3 half)
        {
            for (int z = (int)MathF.Floor((c.Z - half.Z) / 16f); z <= (int)MathF.Floor((c.Z + half.Z) / 16f); z++)
            for (int y = (int)MathF.Floor((c.Y - half.Y) / 16f); y <= (int)MathF.Floor((c.Y + half.Y) / 16f); y++)
            for (int x = (int)MathF.Floor((c.X - half.X) / 16f); x <= (int)MathF.Floor((c.X + half.X) / 16f); x++)
                chunks.Add(new TD.Int3(x, y, z));
        }
        Around(floor, new Vector3(26f, 14f, 26f));
        Around(new Vector3(0f, ty, 55f), new Vector3(10f, 12f, 20f));
        var mesher = new TD.SurfaceNetsMesher();
        _rock ??= RockMaterial();
        var body = new StaticBody3D { Name = "Rock", CollisionLayer = Layers.World, CollisionMask = 0 };
        a.Root.AddChild(body);
        foreach (TD.Int3 chunk in chunks)
        {
            TD.ChunkMesh m = mesher.Build(store.CaptureNeighborhood(chunk));
            if (m.IsEmpty) continue;
            var verts = new Vector3[m.Positions.Length];
            var normals = new Vector3[m.Positions.Length];
            var colors = new Color[m.Positions.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = new Vector3(m.Positions[i].X, m.Positions[i].Y, m.Positions[i].Z);
                normals[i] = new Vector3(m.Normals[i].X, m.Normals[i].Y, m.Normals[i].Z);
                colors[i] = RockColor(m.Materials[i], verts[i]);
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Color] = colors;
            arrays[(int)Mesh.ArrayType.Index] = m.Indices;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            a.Root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _rock });
            var faces = new Vector3[m.Indices.Length];
            for (int i = 0; i < faces.Length; i++) faces[i] = verts[m.Indices[i]];
            var shape = new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces } };
            body.AddChild(shape);
            a.Shapes.Add(shape);
        }
        // Who lives here: crawlers on the chamber ceiling, grubs at the moss; one on the tunnel's ceiling.
        CaveFauna.Populate(a.Sim, space, Fauna, floor + Vector3.Up, 16f, 99, depth: 2, crawlers: 3, subsisters: 5);
        CaveFauna.Populate(a.Sim, space, Fauna, axis + new Vector3(0f, -1f, 6f), 9f, 7, depth: 1, crawlers: 1, subsisters: 3);
        a.Lairs.Build(a.Sim);
        // A faint cold glow far down the chamber, so it has depth before your lamp finds it.
        a.Root.AddChild(new OmniLight3D { LightColor = new Color("6fb6c8"), LightEnergy = 0.25f, OmniRange = 14f, Position = floor + new Vector3(-6f, 2f, 4f) });
        return a;
    }

    static Color RockColor(TD.CellMaterial m, Vector3 p)
    {
        float k = 0.5f + 0.5f * MathF.Sin(p.Y * 1.7f + MathF.Sin(p.X * 0.3f) * 2f) * MathF.Cos(p.Z * 0.21f);
        Color c = m switch
        {
            TD.CellMaterial.Soil => new Color(0.3f, 0.24f, 0.18f),
            TD.CellMaterial.DeepRock => new Color(0.24f, 0.24f, 0.25f),
            TD.CellMaterial.Ore => new Color(0.36f, 0.3f, 0.24f),
            TD.CellMaterial.Bedrock => new Color(0.16f, 0.16f, 0.17f),
            _ => new Color(0.33f, 0.31f, 0.28f),
        };
        return c.Darkened(0.18f * k);
    }

    static StandardMaterial3D RockMaterial() => new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        AlbedoTexture = new NoiseTexture2D
        {
            Noise = new FastNoiseLite { Frequency = 0.08f, FractalOctaves = 4 },
            Seamless = true, Width = 256, Height = 256,
            ColorRamp = new Gradient { Colors = new[] { new Color(0.62f, 0.62f, 0.62f), Colors.White }, Offsets = new[] { 0f, 1f } },
        },
        Uv1Triplanar = true,
        Uv1Scale = Vector3.One * 0.35f,
        Roughness = 0.95f,
    };

    /// <summary>The species the mutation row shows: a horned, tailed walker, the clearest silhouette to twist.</summary>
    public static Species RowSpecies(WildlifeData data)
    {
        var gen = new FaunaGenerator(data.Fauna, data.Activity);
        for (ulong seed = 7041; seed < 7200; seed++)
        {
            PlanetFauna f = gen.Generate(seed, 0.35f, false);
            Species s = f.Species.Find(x => x.Rig == RigKind.Legged && x.Plan == BodyPlan.Walker && x.Has("horns") && x.Has("tail") && x.Body.Size > 1.4f);
            if (s != null) return s;
        }
        return gen.Generate(7041, 0.35f, false).Species[0];
    }

    Area BuildGallery()
    {
        Area a = BuildPlace(BlockPlace.Gallery(), new Color("57524a"), new Color("6d675c"), new Color("4a4640"), populate: false);
        var gen = new FaunaGenerator(Data.Fauna, Data.Activity);
        Species s = RowSpecies(Data);
        for (int i = 0; i < RowStrengths.Length; i++)
        {
            Species m = gen.Mutate(s, RowStrengths[i]);
            Creature c = a.Sim.SpawnIn(m, a.Space, new Vector3(9f + i * 6f, 1.2f, 6f));
            a.Root.AddChild(new Label3D
            {
                Text = $"mutation {RowStrengths[i]:0.#}",
                Position = new Vector3(9f + i * 6f, 1.02f, 9.2f),
                RotationDegrees = new Vector3(-90f, 0f, 0f),
                FontSize = 64,
                PixelSize = 0.006f,
                Modulate = new Color(0.85f, 0.82f, 0.74f),
                Shaded = false,
            });
            c.Yaw = 2.2f; // three-quarters on to the viewer
            c.Facing = SpaceMath.Frame(c.Forward, Vector3.Up);
            c.Rig.Reset(a.Sim, c);
            Row.Add(c);
        }
        a.Sim.Frozen = true;
        // Lit plainly, to see the bodies by.
        a.Root.AddChild(new DirectionalLight3D { LightEnergy = 1.1f, ShadowEnabled = true, RotationDegrees = new Vector3(-48f, 30f, 0f), LightColor = new Color("fff1dc") });
        return a;
    }

    Area BuildTown()
    {
        Area a = BuildPlace(BlockPlace.Gallery("A settlement's animals"), new Color("5d5446"), new Color("6f6352"), new Color("4a4338"), populate: false);
        a.Root.AddChild(new DirectionalLight3D { LightEnergy = 1.15f, ShadowEnabled = true, RotationDegrees = new Vector3(-50f, -35f, 0f), LightColor = new Color("fff3df") });
        // The town covers the hall: wildlife keeps out, and its animals live inside.
        a.Sim.AddSettlement("Hollowmere", new Vector3(15f, 1f, 7.5f), 30f);
        var gen = new FaunaGenerator(Data.Fauna, Data.Activity);
        Species stock = gen.RollDomestic(2024, DomesticKind.Stock, 0.4f), beast = gen.RollDomestic(2024, DomesticKind.Working, 0.4f), pet = gen.RollDomestic(2024, DomesticKind.Pet, 0.4f);
        var wood = new StandardMaterial3D { AlbedoColor = new Color("6b4f34"), Roughness = 0.9f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("3b2c1f"), Roughness = 0.9f };
        // A pen of posts and rails.
        var pen = new Vector3(8.5f, 1f, 6.5f);
        const float half = 3.6f;
        for (float t = -half; t <= half + 0.01f; t += 1.2f)
            foreach (Vector3 at in new[] { new Vector3(t, 0f, -half), new Vector3(t, 0f, half), new Vector3(-half, 0f, t), new Vector3(half, 0f, t) })
                a.Root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 1.2f, 0.12f) }, MaterialOverride = dark, Position = pen + at + Vector3.Up * 0.6f });
        foreach (float y in new[] { 0.55f, 1.0f })
        {
            foreach (float z in new[] { -half, half }) a.Root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(half * 2f, 0.08f, 0.06f) }, MaterialOverride = wood, Position = pen + new Vector3(0f, y, z) });
            foreach (float x in new[] { -half, half }) a.Root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.08f, half * 2f) }, MaterialOverride = wood, Position = pen + new Vector3(x, y, 0f) });
        }
        for (int i = 0; i < 3; i++)
            Domestics.Add(a.Sim.SpawnDomestic(stock, pen + new Vector3(-1.5f + i * 1.5f, 0f, -1f + i), pen, half - 0.8f));
        // A cart, its shafts towards you, and the beast that pulls it standing in them.
        var cart = new Vector3(22f, 1f, 3.6f);
        a.Root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.8f, 0.5f, 2.6f) }, MaterialOverride = wood, Position = cart + new Vector3(0f, 0.95f, 0f) });
        foreach (float s in new[] { -1f, 1f })
        {
            a.Root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.62f, Height = 0.12f }, MaterialOverride = dark, Position = cart + new Vector3(s * 1.0f, 0.62f, 0f), RotationDegrees = new Vector3(0f, 0f, 90f) });
            a.Root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, 2.6f) }, MaterialOverride = wood, Position = cart + new Vector3(s * 0.85f, 0.75f, 2.5f), RotationDegrees = new Vector3(8f, 0f, 0f) });
        }
        Creature draught = a.Sim.SpawnDomestic(beast, cart + new Vector3(0f, 0f, 3.4f), cart + new Vector3(0f, 0f, 3.4f), 1.6f);
        draught.Yaw = MathF.PI; // facing out along the shafts
        Domestics.Add(draught);
        // A townsperson stand-in, with two pets at heel.
        _figure = new Node3D { Name = "Townsperson" };
        var coat = new StandardMaterial3D { AlbedoColor = new Color("5a5f4c"), Roughness = 0.85f };
        _figure.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.26f, Height = 1.5f }, MaterialOverride = coat, Position = new Vector3(0f, 0.75f, 0f) });
        _figure.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.15f, Height = 0.3f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("b08a6a") }, Position = new Vector3(0f, 1.66f, 0f) });
        a.Root.AddChild(_figure);
        Townsperson.Position = new Vector3(15f, 1f, 10f);
        for (int i = 0; i < 2; i++)
            Domestics.Add(a.Sim.SpawnDomestic(pet, Townsperson.Position + new Vector3(i * 1.2f - 0.6f, 0f, 1f), new Vector3(15f, 1f, 7.5f), 13f, Townsperson));
        foreach (Creature c in Domestics)
        {
            c.Space = a.Space;
            c.Clearance = Crawl.ClearanceFor(c.Species);
            Crawl.Place(a.Sim, c, c.Position + Vector3.Up * 0.3f, null);
            c.Home = c.Position;
            c.Rig.Reset(a.Sim, c);
        }
        return a;
    }

    /// <summary>The townsperson strolls a slow loop round the hall, stopping now and then: the pets keep at heel.</summary>
    void StepTown(float dt)
    {
        Town.Sim.Clock.Time01 = 0.45f;
        _walk += dt;
        float cycle = _walk % 26f;
        bool stopped = cycle > 16f;
        if (!stopped)
        {
            float a = cycle / 16f * MathF.Tau;
            var to = new Vector3(15f + MathF.Cos(a) * 4f, 1f, 9.5f + MathF.Sin(a) * 2f);
            Vector3 step = to - Townsperson.Position;
            if (step.LengthSquared() > 1e-4f) Townsperson.Forward = step.Normalized();
            Townsperson.Position = Townsperson.Position.MoveToward(to, 1.3f * dt);
        }
        _figure.Position = Townsperson.Position;
        _figure.Rotation = new Vector3(0f, MathF.Atan2(-Townsperson.Forward.X, -Townsperson.Forward.Z), 0f);
    }

    /// <summary>A block-built place: face-culled block mesh and collision, vents, doors, ramps, lamps, and its population.</summary>
    Area BuildPlace(BlockPlace place, Color wall, Color floor, Color ceiling, bool populate = true)
    {
        BlockSpace space = place.Space;
        Area a = NewArea(place.Name, space);
        a.Place = place;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var propColor = new Dictionary<Vector3I, Color>();
        foreach (var (min, max, color) in place.Props)
            for (int y = min.Y; y < max.Y; y++)
            for (int z = min.Z; z < max.Z; z++)
            for (int x = min.X; x < max.X; x++) propColor[new Vector3I(x, y, z)] = color;
        Vector3I[] dirs = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
        for (int y = 0; y < place.Size.Y; y++)
        for (int z = 0; z < place.Size.Z; z++)
        for (int x = 0; x < place.Size.X; x++)
        {
            if (!place.Solid(x, y, z)) continue;
            foreach (Vector3I d in dirs)
            {
                if (place.Solid(x + d.X, y + d.Y, z + d.Z)) continue;
                if (x + d.X < 0 || y + d.Y < 0 || z + d.Z < 0 || x + d.X >= place.Size.X || y + d.Y >= place.Size.Y || z + d.Z >= place.Size.Z) continue;
                // Floors, walls and ceilings in their own tones, each block a little different.
                Color c = propColor.TryGetValue(new Vector3I(x, y, z), out Color pc) ? pc : d.Y > 0 ? floor : d.Y < 0 ? ceiling : wall;
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
                c = c.Darkened((h % 100) / 100f * 0.16f);
                if (d.Y == 0 && ((h >> 8) % 9 == 0)) c = c.Lerp(new Color(0.36f, 0.2f, 0.1f), 0.35f); // rust streaks
                Face(verts, normals, colors, new Vector3(x, y, z), d, c);
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            AlbedoTexture = new NoiseTexture2D
            {
                Noise = new FastNoiseLite { Frequency = 0.12f, FractalOctaves = 3 },
                Seamless = true, Width = 256, Height = 256,
                ColorRamp = new Gradient { Colors = new[] { new Color(0.7f, 0.7f, 0.7f), Colors.White }, Offsets = new[] { 0f, 1f } },
            },
            Uv1Triplanar = true,
            Uv1Scale = Vector3.One * 0.5f,
            Roughness = 0.85f,
        };
        a.Root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat });
        var body = new StaticBody3D { Name = "Blocks", CollisionLayer = Layers.World, CollisionMask = 0 };
        a.Root.AddChild(body);
        var shape = new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = verts.ToArray() } };
        body.AddChild(shape);
        a.Shapes.Add(shape);
        // The player's feet ride slopes over the stairs.
        foreach (var (from, to, width) in place.Ramps)
        {
            Vector3 d = to - from;
            var ramp = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(width, 0.2f, d.Length() + 1f) } };
            body.AddChild(ramp);
            ramp.GlobalTransform = new Transform3D(Basis.LookingAt(d.Normalized(), Vector3.Up), (from + to) * 0.5f + Vector3.Down * 0.1f);
            a.Shapes.Add(ramp);
        }
        var metal = new StandardMaterial3D { AlbedoColor = new Color("5a5d60"), Metallic = 0.6f, Roughness = 0.45f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("0b0b0c"), Roughness = 1f };
        foreach (var (at, n) in place.Vents)
        {
            // A grille over the hole: a frame and slats; it rattles when something moves behind it.
            var g = new Node3D();
            a.Root.AddChild(g);
            g.GlobalTransform = new Transform3D(SpaceMath.Frame(MathF.Abs(n.Y) > 0.8f ? Vector3.Forward : Vector3.Up, n), at + n * 0.02f);
            g.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.02f, 0.9f) }, MaterialOverride = dark, Position = Vector3.Down * 0.03f });
            for (int i = 0; i < 6; i++)
                g.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.86f, 0.03f, 0.05f) }, MaterialOverride = metal, Position = new Vector3(0f, 0f, -0.36f + i * 0.145f) });
            foreach (float s in new[] { -1f, 1f })
            {
                g.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.05f, 0.96f) }, MaterialOverride = metal, Position = new Vector3(s * 0.45f, 0f, 0f) });
                g.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.96f, 0.05f, 0.06f) }, MaterialOverride = metal, Position = new Vector3(0f, 0f, s * 0.45f) });
            }
            a.Grilles.Add((g, at, 0f));
        }
        var doorMat = new StandardMaterial3D { AlbedoColor = new Color("5b4a36"), Metallic = 0.3f, Roughness = 0.6f };
        foreach (var (at, n, size) in place.Doors)
        {
            var pivot = new Node3D();
            a.Root.AddChild(pivot);
            Vector3 side = n.Cross(Vector3.Up).Normalized();
            float wide = MathF.Abs(side.X) > 0.5f ? size.X : size.Z;
            pivot.GlobalTransform = new Transform3D(Basis.LookingAt(-n, Vector3.Up), at - side * wide * 0.5f);
            var panel = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(wide, size.Y, 0.08f) }, MaterialOverride = doorMat, Position = new Vector3(wide * 0.5f, size.Y * 0.5f, 0f) };
            pivot.AddChild(panel);
            var doorBody = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
            doorBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(wide, size.Y, 0.08f) }, Position = panel.Position });
            pivot.AddChild(doorBody);
            a.Doors.Add(new Door { Pivot = pivot, Body = doorBody, At = at + Vector3.Up * 0.2f });
        }
        foreach (var (at, color, range) in place.Lamps)
        {
            var lamp = new OmniLight3D { LightColor = color, LightEnergy = range < 5f ? 1.6f : 1.1f, OmniRange = range, ShadowEnabled = range >= 5f, Position = at };
            a.Root.AddChild(lamp);
            a.Root.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                Position = at,
            });
            if (range < 5f) a.Blinkers.Add(lamp);
        }
        if (!populate) return a;
        PoiPopulation pop = PoiFauna.Populate(place.Plan, Fauna, Data.Deep);
        PoiFauna.Spawn(a.Sim, pop, space);
        a.Lairs.Build(a.Sim);
        GD.Print($"{place.Name}: {pop.Note}");
        return a;
    }

    static void Face(List<Vector3> v, List<Vector3> n, List<Color> c, Vector3 cell, Vector3I d, Color color)
    {
        Vector3 normal = new(d.X, d.Y, d.Z);
        // Two axes across the face, so the corners wind clockwise seen from outside.
        Vector3 a = MathF.Abs(normal.Y) > 0.5f ? Vector3.Right : Vector3.Up;
        Vector3 b = normal.Cross(a);
        Vector3 center = cell + Vector3.One * 0.5f + normal * 0.5f;
        Vector3 p0 = center - a * 0.5f - b * 0.5f, p1 = center + a * 0.5f - b * 0.5f, p2 = center + a * 0.5f + b * 0.5f, p3 = center - a * 0.5f + b * 0.5f;
        foreach (Vector3 p in new[] { p0, p2, p1, p0, p3, p2 })
        {
            v.Add(p);
            n.Add(normal);
            c.Add(color);
        }
    }

    // ---- Stations -----------------------------------------------------------------------

    public void GoTo(int station)
    {
        if (station < 0 || station >= Stations.Count) return;
        var (label, area, start, look) = Stations[station];
        if (Current != area)
        {
            foreach (Area a in new[] { Caves, Outpost, Hold, Gallery, Town })
            {
                bool on = a == area;
                a.Root.Visible = on;
                foreach (CollisionShape3D s in a.Shapes) s.Disabled = !on;
                foreach (Door d in a.Doors) d.Body.ProcessMode = on ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
                a.Sim.Targets.Clear();
                if (on) a.Sim.Targets.Add(Player.Target);
            }
            Current = area;
            // The gallery is lit; everywhere else is dark but for your lamp and what glows.
            bool lit = area == Gallery || area == Town;
            _env.AmbientLightEnergy = lit ? 0.55f : 0.06f;
            _env.FogDensity = lit ? 0.004f : 0.03f;
        }
        Station = station;
        Player.Teleport(start + Vector3.Up * 0.1f);
        Vector3 to = look - (start + Vector3.Up * 1.6f);
        Player.SetLook(MathF.Atan2(-to.X, -to.Z), MathF.Atan2(to.Y, new Vector2(to.X, to.Z).Length()));
        Player.Health.Restore();
    }

    // ---- Input ------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured) Player.HandleMouse(motion.Relative);
        else if (e is InputEventMouseButton { Pressed: true } button)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Captured;
            else if (button.ButtonIndex == MouseButton.Left) Fire();
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Key1: GoTo(0); break;
                case Key.Key2: GoTo(1); break;
                case Key.Key3: GoTo(2); break;
                case Key.Key4: GoTo(3); break;
                case Key.Key5: GoTo(4); break;
                case Key.Key6: GoTo(5); break;
                case Key.Key7: BackToPlanet(); break;
                case Key.L: _lamp.Visible = !_lamp.Visible; break;
                case Key.Escape: Input.MouseMode = Input.MouseModeEnum.Visible; break;
            }
        }
    }

    async void BackToPlanet()
    {
        await Shutdown.Quiet(this);
        GetTree().ChangeSceneToFile("res://Main.tscn");
    }

    // ---- Frame ------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        if (!Player.Health.Downed && !FreeCamera) Player.Step(dt);
        if (Current == Town) StepTown(dt);
        Current.Sim.Step(dt);
        if (!FreeCamera) Player.PlaceCamera();
        Current.View.Sync(Camera.GlobalPosition);
        Animate(Current, dt);
        _gunTimer -= dt;
        RecordSightings(dt);
        if (Player.Health.Downed)
        {
            _downedTimer += dt;
            if (_downedTimer > 3f)
            {
                _downedTimer = 0f;
                GoTo(Station);
            }
        }
        UpdateHud();
    }

    /// <summary>Grilles rattle and doors bang with the tells behind them; a door bursts open when its ambusher comes out; the emergency lamp blinks.</summary>
    void Animate(Area a, float dt)
    {
        for (int i = 0; i < a.Grilles.Count; i++)
        {
            var (node, at, shake) = a.Grilles[i];
            shake = MathF.Max(0f, shake - dt);
            node.Position = node.Position.Lerp(at + new Vector3(MathF.Sin(_time * 61f), MathF.Sin(_time * 47f), MathF.Cos(_time * 53f)) * 0.012f * MathF.Min(1f, shake), 0.5f);
            a.Grilles[i] = (node, at, shake);
        }
        foreach (Door d in a.Doors)
        {
            d.Shake = MathF.Max(0f, d.Shake - dt);
            if (d.Open > 0f)
            {
                d.Open = MathF.Min(1f, d.Open + dt * 4f);
                d.Body.ProcessMode = ProcessModeEnum.Disabled;
            }
            float swing = d.Open * 1.9f + MathF.Sin(_time * 40f) * 0.02f * MathF.Min(1f, d.Shake);
            d.Pivot.Rotation = new Vector3(0f, d.Pivot.Rotation.Y * 0f + BaseYaw(d) + swing, 0f);
        }
        foreach (OmniLight3D blink in a.Blinkers) blink.LightEnergy = MathF.Sin(_time * 2.2f) > 0.2f ? 1.6f : 0.08f;
    }

    readonly Dictionary<Door, float> _baseYaw = new();
    float BaseYaw(Door d)
    {
        if (!_baseYaw.TryGetValue(d, out float y)) _baseYaw[d] = y = d.Pivot.Rotation.Y;
        return y;
    }

    void Shake(Area a, TellEvent e)
    {
        if (e.Kind == TellKind.Rattle)
            for (int i = 0; i < a.Grilles.Count; i++)
                if (a.Grilles[i].at.DistanceTo(e.Position) < 1.5f) a.Grilles[i] = (a.Grilles[i].node, a.Grilles[i].at, e.Duration + 0.3f);
        if (e.Kind is TellKind.Thump or TellKind.Scratch)
            foreach (Door d in a.Doors)
                if (d.At.DistanceTo(e.Position) < 2.5f) d.Shake = MathF.Max(d.Shake, e.Kind == TellKind.Thump ? e.Duration : 0.4f);
    }

    void OnAttacked(AttackEvent e)
    {
        if (e.Target != Player.Target) return;
        if (e.Hit)
        {
            Hud.Hurt(e.Damage);
            Audio.Play("thud", Player.Eye, 1f, 2f);
        }
        else Audio.Play("snap", e.Creature.Position + Vector3.Up * 0.5f, 1.2f, -4f);
    }

    void Fire()
    {
        if (_gunTimer > 0f || Player.Health.Downed) return;
        _gunTimer = 0.2f;
        Vector3 origin = Camera.GlobalPosition, dir = -Camera.GlobalBasis.Z;
        float best = 60f;
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + dir * best, Layers.World, new Godot.Collections.Array<Rid> { Player.GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0) best = origin.DistanceTo((Vector3)hit["position"]);
        Creature c = Current.Sim.Raycast(origin, dir, best, out _, out _);
        if (c != null && !c.InWalls) Current.Sim.Damage(c, 16f, Player.Target);
        Player.Recoil = 1f;
        Audio.Play("shot", origin, 1f, -4f);
    }

    bool UnderAttack()
    {
        if (Player.Health.Downed) return true;
        foreach (Creature c in Current.Sim.Creatures)
            if (!c.Removed && c.AttackTarget == Player.Target && c.Phase is AttackPhase.Windup or AttackPhase.Lunge) return true;
        return false;
    }

    /// <summary>The same first-meeting card as on the planet: a good look at something new, once per species. Nothing hidden counts.</summary>
    void RecordSightings(float dt)
    {
        _sightTimer -= dt;
        if (_sightTimer > 0f) return;
        _sightTimer = 0.25f;
        Vector3 eye = Camera.GlobalPosition, look = -Camera.GlobalBasis.Z;
        foreach (Creature c in Current.Sim.Creatures)
        {
            if (c.Removed || c.Dead || c.InWalls || Codex.Knows(c.Species.Id)) continue;
            Vector3 to = c.Rig.BoundsCenter - eye;
            float d = to.Length();
            if (d > 14f || look.Dot(to / d) < 0.9f) continue;
            if (!SpaceMath.Clear(Current.Space, eye, c.Rig.BoundsCenter, 0f)) continue;
            if (Codex.Record(c.Species, Player.Target.Name, Current.Sim.Time, Fauna.PlanetName == "" ? "this world" : Fauna.PlanetName))
                Hud.QueueDiscovery(Codex.Get(c.Species.Id), c.Species.Colors.GlowStrength > 0.3f ? c.Species.Colors.Glow : c.Species.Colors.Accent.Lightened(0.25f));
        }
    }

    void UpdateHud()
    {
        int alive = 0, hidden = 0;
        foreach (Creature c in Current.Sim.Creatures)
        {
            if (c.Removed || !c.Alive) continue;
            alive++;
            if (c.InWalls) hidden++;
        }
        string where = Station >= 0 ? Stations[Station].label : "";
        Hud.SetStatus(
            $"{where}   {(Player.Health.Downed ? "You were brought down." : $"health {Player.Health.Current:F0}/{Player.Health.Max:F0}")}\n" +
            $"{alive} creatures here ({hidden} out of sight in cracks, vents and doors)   kills by its hunters {Current.Sim.Predations}\n\n" +
            "1 cave  2 dug tunnel  3 outpost  4 derelict hold  5 mutation row  6 a settlement's animals  7 back to the planet\n" +
            "WASD move  Shift sprint  Ctrl crouch (creep past)  Mouse look  LMB shoot  L lamp\n" +
            "Listen: scratching, rattling vents, grit falling, breathing in the dark. Every ambush tells first.\n" +
            $"{Engine.GetFramesPerSecond()} fps");
        Hud.SetHold(new Storage(1), Player.Pack, false);
        Hud.SetHint("");
        Hud.SetScan("");
    }
}
