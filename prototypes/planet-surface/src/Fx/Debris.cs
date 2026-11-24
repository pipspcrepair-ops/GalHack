using System;
using Godot;
using TerrainDigging.Terrain;

namespace PlanetSurface.Fx;

/// <summary>
/// Ported from terrain-digging. Flying debris for digging feedback: chunky cubes in the dug material that bounce off the
/// ground, plus soft dust puffs. One MultiMesh each, simulated here in a fixed pool, so a
/// crater costs the same draw calls as a single dig. Ore chunks get pulled into the player
/// and count as collected when they arrive.
/// </summary>
public partial class Debris : Node3D
{
    const int MaxChunks = 700, MaxDust = 220;

    struct Piece
    {
        public Vector3 Position, Velocity, Axis;
        public float Angle, Spin, Age, Life, Size;
        public Color Color;
        public bool Homing, Alive, Mote;
    }

    static Texture2D _haze;

    /// <summary>A soft, torn-edged bank of haze: billowing noise under a round falloff (also the weather's blowing dust).</summary>
    public static Texture2D Haze
    {
        get
        {
            if (_haze != null) return _haze;
            const int Size = 96;
            var noise = new FastNoiseLite { Seed = 17, Frequency = 0.035f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
            Image image = Image.CreateEmpty(Size, Size, true, Image.Format.Rgba8);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) / Size * 2f - 1f, dy = (y + 0.5f) / Size * 2f - 1f;
                float round = Mathf.Clamp(1f - MathF.Sqrt(dx * dx + dy * dy), 0f, 1f);
                float n = Mathf.Clamp(0.55f + noise.GetNoise2D(x, y) * 1.3f, 0f, 1f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, round * round * (3f - 2f * round) * n));
            }
            image.GenerateMipmaps();
            return _haze = ImageTexture.CreateFromImage(image);
        }
    }

    public ChunkStore Store;

    /// <summary>The colour of a dug material where it was dug (the planet's palette), and of dust.</summary>
    public Func<CellMaterial, Vector3, Color> ColorOf = (_, _) => new Color(0.5f, 0.45f, 0.4f);
    public Color Dust = new(0.6f, 0.55f, 0.48f, 0.5f);

    /// <summary>Where homing ore flies to (the player's chest).</summary>
    public Func<Vector3> Collector;

    /// <summary>An ore chunk reached the player. Purely visual: ore is counted when it's dug.</summary>
    public event Action OreArrived;

    /// <summary>How many digs have thrown debris (tests).</summary>
    public int Bursts { get; private set; }

    readonly Piece[] _chunks = new Piece[MaxChunks], _dust = new Piece[MaxDust];
    readonly float[] _chunkBuffer = new float[MaxChunks * 16], _dustBuffer = new float[MaxDust * 16];
    int _nextChunk, _nextDust;
    MultiMeshInstance3D _chunkMesh, _dustMesh;
    readonly RandomNumberGenerator _rng = new() { Seed = 1975 };

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _chunkMesh = MakeMultiMesh(MaxChunks, new BoxMesh { Size = Vector3.One }, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.9f,
        }, shadows: true);

        _dustMesh = MakeMultiMesh(MaxDust, new QuadMesh { Size = Vector2.One }, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = Haze,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            DisableReceiveShadows = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
            DistanceFadeMinDistance = 0.2f,
            DistanceFadeMaxDistance = 1.4f,
        }, shadows: false);
    }

    MultiMeshInstance3D MakeMultiMesh(int count, Mesh mesh, Material material, bool shadows)
    {
        mesh.SurfaceSetMaterial(0, material);
        var instance = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = count,
            },
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            // The pool can be anywhere in the world; never let culling hide live pieces.
            ExtraCullMargin = 16384f,
        };
        AddChild(instance);
        return instance;
    }

    /// <summary>Debris for a dig: a few chunks per removed cell, thrown away from the tool.</summary>
    public void Burst(System.Collections.Generic.IReadOnlyList<RemovedCell> cells, Vector3 toolOrigin, float strength = 1f)
    {
        Bursts++;
        int perCell = cells.Count > 120 ? 1 : cells.Count > 30 ? 2 : 3;
        for (int i = 0; i < cells.Count; i++)
        {
            RemovedCell cell = cells[i];
            var center = new Vector3(cell.Cell.X + 0.5f, cell.Cell.Y + 0.5f, cell.Cell.Z + 0.5f);
            Vector3 away = (center - toolOrigin).Normalized();
            bool ore = cell.Material == CellMaterial.Ore;
            int pieces = ore ? perCell + 2 : perCell;
            for (int k = 0; k < pieces; k++)
            {
                Vector3 jitter = new Vector3(_rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(-0.4f, 0.4f));
                Vector3 velocity = (-away * 1.5f + Vector3.Up * 3.2f + jitter * 6f) * strength;
                Color shade = ColorOf(cell.Material, center) * _rng.RandfRange(0.82f, 1.08f);
                shade.A = 1f;
                Spawn(center + jitter, velocity, shade, _rng.RandfRange(0.08f, 0.22f), ore);
            }
            if (i % 4 == 0) Puff(center, Dust.Lerp(ColorOf(cell.Material, center), 0.7f), 0.6f * strength);
        }
    }

    /// <summary>Sparks off something the tool can't cut.</summary>
    public void Sparks(Vector3 at, Vector3 normal, Color color)
    {
        for (int k = 0; k < 10; k++)
        {
            Vector3 v = (normal * 3f + new Vector3(_rng.RandfRange(-2f, 2f), _rng.RandfRange(0f, 3f), _rng.RandfRange(-2f, 2f)));
            Spawn(at + normal * 0.1f, v, color, _rng.RandfRange(0.05f, 0.1f), false, life: 0.45f);
        }
    }

    public void Puff(Vector3 at, Color color, float size)
    {
        // A soft bank that spreads and thins as it drifts...
        ref Piece p = ref _dust[_nextDust];
        _nextDust = (_nextDust + 1) % MaxDust;
        p = new Piece
        {
            Alive = true,
            Position = at + new Vector3(_rng.RandfRange(-0.2f, 0.2f), 0f, _rng.RandfRange(-0.2f, 0.2f)) * size,
            Velocity = new Vector3(_rng.RandfRange(-0.5f, 0.5f), _rng.RandfRange(0.15f, 0.6f), _rng.RandfRange(-0.5f, 0.5f)),
            Life = _rng.RandfRange(1.4f, 2.6f),
            Size = size * 1.5f,
            Color = color,
        };
        // ...and a couple of motes that hang in it and settle.
        for (int i = 0; i < 2; i++)
        {
            ref Piece m = ref _dust[_nextDust];
            _nextDust = (_nextDust + 1) % MaxDust;
            m = new Piece
            {
                Alive = true,
                Mote = true,
                Position = at + new Vector3(_rng.RandfRange(-0.5f, 0.5f), _rng.RandfRange(-0.1f, 0.4f), _rng.RandfRange(-0.5f, 0.5f)) * size,
                Velocity = new Vector3(_rng.RandfRange(-0.9f, 0.9f), _rng.RandfRange(0.1f, 1.1f), _rng.RandfRange(-0.9f, 0.9f)),
                Life = _rng.RandfRange(1.2f, 2.4f),
                Size = _rng.RandfRange(0.05f, 0.09f),
                Color = color,
            };
        }
    }

    void Spawn(Vector3 at, Vector3 velocity, Color color, float size, bool homing, float life = -1f)
    {
        ref Piece p = ref _chunks[_nextChunk];
        _nextChunk = (_nextChunk + 1) % MaxChunks;
        p = new Piece
        {
            Alive = true,
            Position = at,
            Velocity = velocity,
            Axis = new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)).Normalized(),
            Spin = _rng.RandfRange(4f, 14f),
            Life = life > 0 ? life : homing ? 2.5f : _rng.RandfRange(1.2f, 2f),
            Size = size,
            Color = color,
            Homing = homing,
        };
        if (p.Axis == Vector3.Zero) p.Axis = Vector3.Up;
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.05f);
        Vector3 collector = Collector?.Invoke() ?? Vector3.Zero;

        for (int i = 0; i < MaxChunks; i++)
        {
            ref Piece p = ref _chunks[i];
            if (!p.Alive)
            {
                WriteHidden(_chunkBuffer, i);
                continue;
            }
            p.Age += dt;
            if (p.Homing && p.Age > 0.3f)
            {
                // Pulled in by the tool: accelerate towards the player, then count it.
                Vector3 to = collector - p.Position;
                float distance = to.Length();
                if (distance < 0.6f)
                {
                    p.Alive = false;
                    OreArrived?.Invoke();
                    WriteHidden(_chunkBuffer, i);
                    continue;
                }
                p.Velocity = p.Velocity.Lerp(to / distance * Mathf.Min(24f, 6f + p.Age * 30f), Mathf.Min(1f, dt * 8f));
            }
            else
            {
                p.Velocity.Y -= 16f * dt;
            }
            p.Position += p.Velocity * dt;
            p.Angle += p.Spin * dt;

            if (!p.Homing && Store != null && p.Velocity.Y < 0f && Store.SampleDensity(ToNumerics(p.Position)) > 0f)
            {
                p.Position.Y += 0.05f;
                p.Velocity = new Vector3(p.Velocity.X * 0.5f, -p.Velocity.Y * 0.3f, p.Velocity.Z * 0.5f);
                p.Spin *= 0.6f;
            }

            if (p.Age >= p.Life)
            {
                p.Alive = false;
                WriteHidden(_chunkBuffer, i);
                continue;
            }
            float fade = Mathf.Clamp((p.Life - p.Age) / 0.35f, 0f, 1f);
            Color c = p.Color;
            if (p.Homing) c = new Color(c.R * 1.1f, c.G * 1.1f, c.B * 1.1f);
            Write(_chunkBuffer, i, new Basis(p.Axis, p.Angle).Scaled(Vector3.One * p.Size * fade), p.Position, c);
        }

        for (int i = 0; i < MaxDust; i++)
        {
            ref Piece p = ref _dust[i];
            if (!p.Alive)
            {
                WriteHidden(_dustBuffer, i);
                continue;
            }
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                p.Alive = false;
                WriteHidden(_dustBuffer, i);
                continue;
            }
            float t = p.Age / p.Life;
            p.Position += p.Velocity * dt;
            p.Velocity *= 1f - dt * 1.5f;
            Color c = p.Color;
            if (p.Mote)
            {
                p.Velocity.Y -= 0.5f * dt;
                c.A = 0.7f * (1f - t);
                Write(_dustBuffer, i, Basis.Identity.Scaled(Vector3.One * p.Size), p.Position, c);
                continue;
            }
            // In quickly, then a long thinning out as it spreads.
            c.A = 0.3f * Mathf.Min(1f, t * 7f) * (1f - t) * (1f - t);
            Write(_dustBuffer, i, Basis.Identity.Scaled(Vector3.One * p.Size * (0.8f + t * 2.6f)), p.Position, c);
        }

        _chunkMesh.Multimesh.Buffer = _chunkBuffer;
        _dustMesh.Multimesh.Buffer = _dustBuffer;
    }

    /// <summary>MultiMesh buffer layout: a 3Ã—4 row-major transform, then RGBA.</summary>
    static void Write(float[] buffer, int i, Basis b, Vector3 origin, Color c)
    {
        int o = i * 16;
        buffer[o + 0] = b.X.X; buffer[o + 1] = b.Y.X; buffer[o + 2] = b.Z.X; buffer[o + 3] = origin.X;
        buffer[o + 4] = b.X.Y; buffer[o + 5] = b.Y.Y; buffer[o + 6] = b.Z.Y; buffer[o + 7] = origin.Y;
        buffer[o + 8] = b.X.Z; buffer[o + 9] = b.Y.Z; buffer[o + 10] = b.Z.Z; buffer[o + 11] = origin.Z;
        buffer[o + 12] = c.R; buffer[o + 13] = c.G; buffer[o + 14] = c.B; buffer[o + 15] = c.A;
    }

    static void WriteHidden(float[] buffer, int i) => Array.Clear(buffer, i * 16, 12);

    static System.Numerics.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
}
