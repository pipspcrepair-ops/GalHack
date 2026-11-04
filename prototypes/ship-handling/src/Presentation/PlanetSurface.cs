using Godot;
using ShipHandling.Physics;

namespace ShipHandling;

/// <summary>
/// The planet surface, built in code from one height function: a flat landing pad, rolling
/// hills, a bumpy test strip, a track of rock steps, a gentle and a too-steep ramp, a lake, a
/// rock wall and a cliff. Every feature sits at a fixed spot so the tests can drive to it.
///
/// The terrain is a 2 m heightmap; rocks, ramps and walls are boxes, Foss-style. Beyond the
/// map a flat plain dotted with mesas runs to the horizon.
/// </summary>
public partial class PlanetSurface : Node3D
{
    public const float HalfSize = 384f;
    public const float Cell = 2f;
    public const float WaterLevel = -1f;

    public static readonly Vector2 LakeCenter = new(-190f, -30f);
    public const float LakeRadius = 75f;

    /// <summary>The bumpy strip: rolling bumps of ±BumpHeight every BumpWave metres, along +X.</summary>
    public static readonly Rect2 BumpStrip = new(60f, 110f, 280f, 60f);
    public const float BumpHeight = 0.4f, BumpWave = 24f;
    public const float BumpLaneZ = 140f;

    /// <summary>The rock step track: flat ground along +X at z = StepLaneZ, with slabs across it.</summary>
    public static readonly Rect2 StepTrack = new(60f, 40f, 280f, 40f);
    public const float StepLaneZ = 60f;
    public static readonly (float x, float height)[] Steps = { (120f, 1.0f), (180f, 2.0f), (250f, 2.5f) };
    public const float StepLength = 12f;

    /// <summary>Two ramps rising along +X from RampStartX to a mesa: one climbable, one too steep.</summary>
    public static readonly Rect2 RampYard = new(60f, -100f, 280f, 80f);
    public const float RampStartX = 170f, RampTop = 8f;
    public const float GentleRampZ = -40f, GentleRampDegrees = 20f;
    public const float SteepRampZ = -80f, SteepRampDegrees = 40f;

    /// <summary>A flat yard north of the pad with a long rock wall to bump into.</summary>
    public static readonly Rect2 WallYard = new(-90f, -220f, 140f, 90f);
    public static readonly Vector3 WallCenter = new(-20f, 12f, -175f);
    public static readonly Vector3 WallSize = new(70f, 24f, 6f);

    public const float PadRadius = 70f;
    public const float CliffZ = -256f, CliffHeight = 40f;

    /// <summary>Headless tests skip the drawn meshes; collision is identical either way.</summary>
    public bool Visuals = true;

    public override void _Ready()
    {
        BuildTerrain();
        BuildLake();
        BuildRocks();
        BuildRamps();
        BuildWall();
        BuildPlain();
    }

    // ---- The height function --------------------------------------------------------------

    /// <summary>Ground height at a map point, before the heightmap samples it every 2 m.</summary>
    public static float Height(float x, float z)
    {
        float hills = 14f * (0.5f + 0.5f * Mathf.Sin(x * 0.021f + 1.3f) * Mathf.Cos(z * 0.017f - 0.4f))
                    + 7f * (0.5f + 0.5f * Mathf.Sin((x + z) * 0.037f))
                    + 3f * Mathf.Sin(x * 0.083f + z * 0.05f);
        float h = hills;
        h = Mathf.Lerp(h, 0f, CircleMask(x, z, Vector2.Zero, PadRadius, 50f));
        h = Mathf.Lerp(h, Bumps(x, z), RectMask(x, z, BumpStrip, 25f));
        h = Mathf.Lerp(h, 0f, RectMask(x, z, StepTrack, 25f));
        h = Mathf.Lerp(h, 0f, RectMask(x, z, RampYard, 25f));
        h = Mathf.Lerp(h, 0f, RectMask(x, z, WallYard, 25f));
        h = Mathf.Lerp(h, LakeBed(x, z), CircleMask(x, z, LakeCenter, LakeRadius + 20f, 40f));
        h += CliffHeight * Mathf.SmoothStep(CliffZ, CliffZ - 6f, z);
        float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        return Mathf.Lerp(h, 0f, Mathf.SmoothStep(HalfSize - 44f, HalfSize - 2f, edge));
    }

    static float Bumps(float x, float z) =>
        BumpHeight * Mathf.Sin(Mathf.Tau * x / BumpWave) * Mathf.Cos(Mathf.Tau * (z - BumpLaneZ) / 90f);

    static float LakeBed(float x, float z)
    {
        float r = new Vector2(x, z).DistanceTo(LakeCenter);
        if (r < LakeRadius) return WaterLevel - 5f * (1f - r * r / (LakeRadius * LakeRadius));
        return WaterLevel + (r - LakeRadius) * 0.08f; // a gentle beach
    }

    static float CircleMask(float x, float z, Vector2 center, float radius, float falloff) =>
        1f - Mathf.SmoothStep(radius, radius + falloff, new Vector2(x, z).DistanceTo(center));

    static float RectMask(float x, float z, Rect2 rect, float falloff)
    {
        float dx = Mathf.Max(rect.Position.X - x, Mathf.Max(0f, x - rect.End.X));
        float dz = Mathf.Max(rect.Position.Y - z, Mathf.Max(0f, z - rect.End.Y));
        return 1f - Mathf.SmoothStep(0f, falloff, Mathf.Sqrt(dx * dx + dz * dz));
    }

    /// <summary>Ground height as the collision heightmap has it: linear between 2 m samples.</summary>
    public static float GroundAt(float x, float z)
    {
        float gx = (x + HalfSize) / Cell, gz = (z + HalfSize) / Cell;
        int ix = Mathf.Clamp((int)Mathf.Floor(gx), 0, Samples - 2), iz = Mathf.Clamp((int)Mathf.Floor(gz), 0, Samples - 2);
        float fx = gx - ix, fz = gz - iz;
        float h00 = Sample(ix, iz), h10 = Sample(ix + 1, iz), h01 = Sample(ix, iz + 1), h11 = Sample(ix + 1, iz + 1);
        return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fz);
    }

    /// <summary>Highest of ground and water: what a hover engine rides on.</summary>
    public static float SurfaceAt(float x, float z) => Mathf.Max(GroundAt(x, z), InLake(x, z) ? WaterLevel : float.MinValue);

    public static bool InLake(float x, float z) => new Vector2(x, z).DistanceTo(LakeCenter) < LakeRadius + 20f;

    static int Samples => (int)(HalfSize * 2f / Cell) + 1;
    static float Sample(int ix, int iz) => Height(-HalfSize + ix * Cell, -HalfSize + iz * Cell);

    // ---- Building ---------------------------------------------------------------------------

    void BuildTerrain()
    {
        int n = Samples;
        var heights = new float[n * n];
        for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
                heights[iz * n + ix] = Sample(ix, iz);

        // The heightmap shape is one unit per sample; a uniform scale of 2 makes it 2 m cells,
        // so its heights are stored halved.
        var scaled = new float[heights.Length];
        for (int i = 0; i < heights.Length; i++) scaled[i] = heights[i] / Cell;
        var ground = new StaticBody3D { Name = "Terrain", CollisionLayer = Layers.World, CollisionMask = 0 };
        ground.AddChild(new CollisionShape3D
        {
            Shape = new HeightMapShape3D { MapWidth = n, MapDepth = n, MapData = scaled },
            Scale = Vector3.One * Cell,
        });
        AddChild(ground);

        if (Visuals) ground.AddChild(TerrainMesh(heights, n));
    }

    static MeshInstance3D TerrainMesh(float[] heights, int n)
    {
        var vertices = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var colors = new Color[n * n];
        for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                int i = iz * n + ix;
                float x = -HalfSize + ix * Cell, z = -HalfSize + iz * Cell, h = heights[i];
                vertices[i] = new Vector3(x, h, z);
                float hl = heights[iz * n + Mathf.Max(ix - 1, 0)], hr = heights[iz * n + Mathf.Min(ix + 1, n - 1)];
                float hd = heights[Mathf.Max(iz - 1, 0) * n + ix], hu = heights[Mathf.Min(iz + 1, n - 1) * n + ix];
                Vector3 normal = new Vector3(hl - hr, 2f * Cell, hd - hu).Normalized();
                normals[i] = normal;

                // Sand on the flats, ochre up the hills, bare rust rock on anything steep, wet near water.
                Color c = Palette.Sand.Lerp(Palette.Ochre, Mathf.Clamp(h / 18f, 0f, 1f));
                c = c.Lerp(Palette.Rust, Mathf.SmoothStep(0.85f, 0.55f, normal.Y));
                if (h < WaterLevel + 0.4f) c = c.Lerp(new Color("6b4a2b"), Mathf.Clamp((WaterLevel + 0.4f - h) / 1.5f, 0f, 1f));
                colors[i] = c;
            }

        var indices = new int[(n - 1) * (n - 1) * 6];
        int k = 0;
        for (int iz = 0; iz < n - 1; iz++)
            for (int ix = 0; ix < n - 1; ix++)
            {
                int a = iz * n + ix, b = a + 1, c = a + n, d = c + 1;
                indices[k++] = a; indices[k++] = b; indices[k++] = c;
                indices[k++] = b; indices[k++] = d; indices[k++] = c;
            }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D { Name = "TerrainMesh", Mesh = mesh, MaterialOverride = GroundMaterial(true) };
    }

    static StandardMaterial3D GroundMaterial(bool vertexColors) => new()
    {
        VertexColorUseAsAlbedo = vertexColors,
        AlbedoColor = vertexColors ? Colors.White : Palette.Sand,
        AlbedoTexture = new NoiseTexture2D
        {
            Noise = new FastNoiseLite { Frequency = 0.03f, Seed = 77 },
            Seamless = true,
            Width = 256,
            Height = 256,
            ColorRamp = new Gradient { Colors = new[] { new Color(0.78f, 0.78f, 0.78f), Colors.White } },
        },
        Uv1Triplanar = true,
        Uv1Scale = Vector3.One * 0.08f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        Roughness = 1f,
    };

    void BuildLake()
    {
        // Water is its own layer: hover probes and landing gear stand on it, hulls pass through.
        var water = new StaticBody3D { Name = "Lake", CollisionLayer = Layers.Water, CollisionMask = 0 };
        water.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = LakeRadius + 20f, Height = 0.5f },
            Position = new Vector3(0, -0.25f, 0),
        });
        AddChild(water);
        water.Position = new Vector3(LakeCenter.X, WaterLevel, LakeCenter.Y);

        if (!Visuals) return;
        water.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = LakeRadius + 22f, BottomRadius = LakeRadius + 22f, Height = 0.02f, RadialSegments = 96 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Palette.Water,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.08f,
                Metallic = 0.4f,
                EmissionEnabled = true,
                Emission = new Color("0a6e75"),
                EmissionEnergyMultiplier = 0.25f,
            },
        });
    }

    void BuildRocks()
    {
        foreach (var (x, height) in Steps)
        {
            var size = new Vector3(StepLength, height, StepTrack.Size.Y - 6f);
            AddBlock("Step", new Vector3(x + StepLength / 2f, height / 2f, StepTrack.GetCenter().Y), size, Palette.Rock, Vector3.Zero);
            if (!Visuals) continue;
            // A hazard stripe along the leading edge, like paint on a loading dock.
            for (int i = 0; i < 12; i++)
            {
                float z = StepTrack.Position.Y + 3.5f + i * (size.Z - 1f) / 11f;
                AddVisual(new Vector3(x - 0.02f, height - 0.25f, z), new Vector3(0.05f, 0.4f, 1.2f), i % 2 == 0 ? Palette.Yellow : Palette.Dark);
            }
        }
    }

    void BuildRamps()
    {
        foreach (var (z, degrees, color) in new[] { (GentleRampZ, GentleRampDegrees, Palette.Ochre), (SteepRampZ, SteepRampDegrees, Palette.Rust) })
        {
            float angle = Mathf.DegToRad(degrees);
            float run = RampTop / Mathf.Tan(angle), slope = RampTop / Mathf.Sin(angle);
            const float thickness = 4f, width = 24f;
            // Tip a slab about Z so its top face rises along +X, then sink it so that face meets
            // the ground at RampStartX.
            var basis = new Basis(Vector3.Back, angle);
            Vector3 topMid = new Vector3(RampStartX + run / 2f, RampTop / 2f, z);
            Vector3 center = topMid - basis.Y * thickness / 2f;
            AddBlock("Ramp", center, new Vector3(slope, thickness, width), color, basis.GetEuler());
            AddBlock("Mesa", new Vector3(RampStartX + run + 20f, RampTop / 2f, z), new Vector3(40f, RampTop, width), color, Vector3.Zero);
        }
    }

    void BuildWall() => AddBlock("Wall", WallCenter, WallSize, Palette.Orange.Darkened(0.25f), Vector3.Zero);

    /// <summary>Flat plain past the map edge, with mesas and spires to judge speed and height by.</summary>
    void BuildPlain()
    {
        // Four slabs framing the heightmap, never under it: the lake bed and bump troughs dip
        // below the plain's level, and a slab beneath them would poke through.
        var plain = new StaticBody3D { Name = "Plain", CollisionLayer = Layers.World, CollisionMask = 0 };
        const float far = 8000f;
        Material material = Visuals ? GroundMaterial(false) : null;
        foreach (var (center, size) in new[]
        {
            (new Vector2(0f, -(HalfSize + far) / 2f), new Vector2(2f * far, far - HalfSize)),
            (new Vector2(0f, (HalfSize + far) / 2f), new Vector2(2f * far, far - HalfSize)),
            (new Vector2(-(HalfSize + far) / 2f, 0f), new Vector2(far - HalfSize, 2f * HalfSize)),
            (new Vector2((HalfSize + far) / 2f, 0f), new Vector2(far - HalfSize, 2f * HalfSize)),
        })
        {
            plain.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(size.X, 2f, size.Y) },
                Position = new Vector3(center.X, -1f, center.Y),
            });
            if (Visuals)
                plain.AddChild(new MeshInstance3D
                {
                    Mesh = new PlaneMesh { Size = size },
                    Position = new Vector3(center.X, 0f, center.Y),
                    MaterialOverride = material,
                });
        }
        AddChild(plain);

        var rng = new RandomNumberGenerator { Seed = 1977 };
        Color[] rock = { Palette.Rock, new("b5651d"), new("8b3a1e"), new("d2915a") };
        for (int i = 0; i < 120; i++)
        {
            var spot = new Vector2(rng.RandfRange(-4000f, 4000f), rng.RandfRange(-4000f, 4000f));
            if (Mathf.Max(Mathf.Abs(spot.X), Mathf.Abs(spot.Y)) < HalfSize + 60f) continue;
            float width = rng.RandfRange(15f, 90f), height = rng.RandfRange(20f, 160f);
            bool spire = rng.Randf() < 0.35f;
            Vector3 size = spire
                ? new Vector3(width * 0.3f, height * 1.6f, width * 0.3f)
                : new Vector3(width, height * 0.5f, width * rng.RandfRange(0.6f, 1.4f));
            AddBlock("Mesa", new Vector3(spot.X, size.Y / 2f, spot.Y), size, rock[i % rock.Length],
                new Vector3(0f, rng.RandfRange(0f, Mathf.Pi / 2f), 0f));
        }

        // A few spires in the hills as well, clear of the test features.
        for (int i = 0; i < 24; i++)
        {
            var spot = new Vector2(rng.RandfRange(-HalfSize + 40f, HalfSize - 40f), rng.RandfRange(-HalfSize + 40f, HalfSize - 40f));
            if (Height(spot.X, spot.Y) < 6f || spot.Y < CliffZ + 20f) continue;
            if (BumpStrip.Grow(30f).HasPoint(spot) || StepTrack.Grow(30f).HasPoint(spot) || RampYard.Grow(30f).HasPoint(spot)) continue;
            float h = rng.RandfRange(18f, 45f), w = rng.RandfRange(4f, 9f);
            AddBlock("Spire", new Vector3(spot.X, Height(spot.X, spot.Y) + h / 2f - 2f, spot.Y), new Vector3(w, h, w),
                rock[i % rock.Length], new Vector3(0f, rng.RandfRange(0f, 1.5f), 0f));
        }
    }

    void AddBlock(string name, Vector3 center, Vector3 size, Color color, Vector3 euler)
    {
        var body = new StaticBody3D { Name = name, CollisionLayer = Layers.World, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        if (Visuals)
            body.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f },
            });
        AddChild(body);
        body.Position = center;
        body.Rotation = euler;
    }

    void AddVisual(Vector3 center, Vector3 size, Color color) =>
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f },
            Position = center,
        });
}
