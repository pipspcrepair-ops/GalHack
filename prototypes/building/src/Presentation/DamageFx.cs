using System;
using System.Collections.Generic;
using Godot;
using GalHack.Structures;

namespace GalHack.Structures.Presentation;

/// <summary>
/// What a hit looks like on a structure, beyond the block's own damaged look: a spark burst, chips
/// of the block thrown off that fall, settle and fade, a scorched impact mark at the hit point, and
/// smoke and sparks from badly damaged devices. All procedural (no textures or models), and all
/// capped, so a barrage costs what a few hits cost and nothing is left floating.
///
/// It sits on the structure's node, so marks and smoke ride the hull. It reads the structure's
/// <see cref="Structure.RecentHits"/> once a frame; call <see cref="Smoke"/> as chunks are remeshed.
/// </summary>
public partial class DamageFx : Node3D
{
    const int MaxChips = 64, MaxMarks = 32, MaxSmokes = 8, MaxBurstsPerFrame = 4, MaxBurstsLive = 12;
    const float Settled = 3f, Fade = 1.2f, MarkLife = 40f, MarkFade = 6f;

    /// <summary>What chips land on: every layer unless a game narrows it.</summary>
    public uint GroundMask = uint.MaxValue;

    public Structure Structure { get; private set; }

    /// <summary>Chips in the air or fading, marks showing and devices smoking (tests).</summary>
    public int Chips => _chips.Count;
    public int Marks => _marks.Count;
    public int Smokes => _smokes.Count;
    public int Bursts { get; private set; }

    struct Chip
    {
        public Vector3 Position, Velocity, Spin;
        public float Floor, Age, Size, Rest;
        public Color Colour;
        public Basis Turn;
    }

    readonly List<Chip> _chips = new();
    readonly List<(Decal decal, float age)> _marks = new();
    readonly List<(CpuParticles3D burst, float left)> _bursts = new();
    readonly Dictionary<Int3, CpuParticles3D> _smokes = new();
    MultiMeshInstance3D _pieces;
    StandardMaterial3D _sparkMaterial, _smokeMaterial;
    BoxMesh _sparkMesh;
    QuadMesh _smokeMesh;
    static ImageTexture _markTexture;
    uint _rng = 0x9e3779b9;

    public void Setup(Structure structure)
    {
        Structure = structure;
        structure.RecentHits.Clear();
    }

    public override void _Ready()
    {
        // Effects are placed per frame in the world's own coordinates.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _markTexture ??= MarkTexture();
        _sparkMesh = new BoxMesh { Size = new Vector3(0.03f, 0.03f, 0.09f) };
        _smokeMesh = new QuadMesh { Size = Vector2.One };
        _sparkMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
            EmissionEnabled = true, Emission = new Color(1f, 0.7f, 0.3f), EmissionEnergyMultiplier = 3f,
        };
        _smokeMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _pieces = new MultiMeshInstance3D
        {
            Name = "Chips", TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.8f },
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                Mesh = new BoxMesh { Size = Vector3.One }, InstanceCount = MaxChips, VisibleInstanceCount = 0,
            },
        };
        AddChild(_pieces);
    }

    float Random()
    {
        _rng = unchecked(_rng * 1664525u + 1013904223u);
        return ((_rng >> 8) & 0xffff) / 65536f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Drain();
        StepChips(dt);
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var (burst, left) = _bursts[i];
            left -= dt;
            if (left > 0f)
            {
                _bursts[i] = (burst, left);
                continue;
            }
            burst.QueueFree();
            _bursts.RemoveAt(i);
        }
        for (int i = _marks.Count - 1; i >= 0; i--)
        {
            var (decal, age) = _marks[i];
            age += dt;
            if (!IsInstanceValid(decal) || age > MarkLife)
            {
                if (IsInstanceValid(decal)) decal.QueueFree();
                _marks.RemoveAt(i);
                continue;
            }
            decal.Modulate = new Color(1, 1, 1, Mathf.Clamp((MarkLife - age) / MarkFade, 0f, 1f));
            _marks[i] = (decal, age);
        }
    }

    /// <summary>Turns this frame's hits into effects, the hardest few only: a barrage of 200 shows as a handful of bursts.</summary>
    void Drain()
    {
        if (Structure == null || Structure.RecentHits.Count == 0) return;
        List<BlockHit> hits = Structure.RecentHits;
        hits.Sort((a, b) => (b.Dealt / b.MaxHp).CompareTo(a.Dealt / a.MaxHp));
        for (int i = 0; i < hits.Count && i < MaxBurstsPerFrame; i++) Show(hits[i]);
        hits.Clear();
    }

    void Show(in BlockHit hit)
    {
        float cell = Structure.Grid.CellSize;
        Vector3 local = new Vector3(hit.Point.X, hit.Point.Y, hit.Point.Z) * cell;
        Vector3 outward = new(hit.Outward.X, hit.Outward.Y, hit.Outward.Z);
        if (outward.LengthSquared() < 1e-4f) outward = Vector3.Up;
        Vector3 at = GlobalTransform * local;
        Vector3 normal = (GlobalBasis * outward).Normalized();
        float share = Mathf.Clamp(hit.Dealt / Mathf.Max(1f, hit.MaxHp), 0f, 1f);
        Sparks(at, normal, 10 + (int)(share * 26f));
        Colour(hit.Cell, out Color colour);
        // Big hits and destroyed blocks throw bits; small ones only spark.
        int chips = hit.Destroyed ? 7 : share > 0.3f ? 4 : share > 0.1f ? 1 : 0;
        for (int i = 0; i < chips; i++) Throw(at, normal, cell, colour);
        Mark(local, outward, cell * (0.3f + share * 0.5f));
    }

    void Colour(Int3 cell, out Color colour)
    {
        colour = new Color(0.45f, 0.45f, 0.47f);
        if (!Structure.TryGet(cell, out Block b)) return;
        ushort paint = b.Paint[Dir.PosY];
        colour = ChunkMesher.Rgb(paint != 0 ? Structure.Palette[paint].Base : Structure.TypeOf(b).Color);
    }

    void Sparks(Vector3 at, Vector3 normal, int amount)
    {
        if (_bursts.Count >= MaxBurstsLive) return;
        Bursts++;
        var burst = new CpuParticles3D
        {
            Emitting = true, OneShot = true, Explosiveness = 1f, Amount = amount, Lifetime = 0.4f,
            Mesh = _sparkMesh, MaterialOverride = _sparkMaterial,
            Direction = normal, Spread = 60f, InitialVelocityMin = 3f, InitialVelocityMax = 9f,
            Gravity = new Vector3(0, -9.8f, 0), ScaleAmountMin = 0.6f, ScaleAmountMax = 1.4f,
            Color = new Color(1f, 0.75f, 0.32f), LocalCoords = false, TopLevel = true,
        };
        AddChild(burst);
        burst.GlobalPosition = at;
        _bursts.Add((burst, 0.8f));
    }

    /// <summary>One chip thrown from a point: it flies, lands on whatever is under it (or never does), lies there and fades.</summary>
    void Throw(Vector3 at, Vector3 normal, float cell, Color colour)
    {
        if (_chips.Count >= MaxChips) _chips.RemoveAt(0);
        Vector3 kick = new Vector3(Random() - 0.5f, Random() * 0.6f, Random() - 0.5f) * 2f + normal;
        var chip = new Chip
        {
            Position = at + normal * cell * 0.1f,
            Velocity = kick.Normalized() * (2f + Random() * 4f),
            Spin = new Vector3(Random() - 0.5f, Random() - 0.5f, Random() - 0.5f) * 14f,
            Size = cell * (0.06f + Random() * 0.1f),
            Colour = colour.Darkened(Random() * 0.3f),
            Turn = Basis.Identity,
            Floor = float.NegativeInfinity,
            Rest = -1f,
        };
        // One ray when it's thrown, no physics after that: it settles where the ground is.
        if (IsInsideTree() && GetWorld3D()?.DirectSpaceState is PhysicsDirectSpaceState3D space)
        {
            var query = PhysicsRayQueryParameters3D.Create(chip.Position, chip.Position + Vector3.Down * 40f, GroundMask);
            Godot.Collections.Dictionary hit = space.IntersectRay(query);
            if (hit.Count > 0) chip.Floor = ((Vector3)hit["position"]).Y + chip.Size * 0.5f;
        }
        _chips.Add(chip);
    }

    void StepChips(float dt)
    {
        for (int i = _chips.Count - 1; i >= 0; i--)
        {
            Chip c = _chips[i];
            c.Age += dt;
            if (c.Rest < 0f)
            {
                c.Velocity.Y -= 9.8f * dt;
                c.Position += c.Velocity * dt;
                if (c.Spin.LengthSquared() > 1e-4f) c.Turn = c.Turn.Rotated(c.Spin.Normalized(), c.Spin.Length() * dt).Orthonormalized();
                if (c.Position.Y <= c.Floor)
                {
                    c.Position.Y = c.Floor;
                    c.Rest = 0f;
                }
                // Nothing under it within reach: it still goes when its time is up.
                else if (c.Age > Settled) c.Rest = 0f;
            }
            else c.Rest += dt;
            if (c.Rest > Settled + Fade)
            {
                _chips.RemoveAt(i);
                continue;
            }
            _chips[i] = c;
        }

        MultiMesh mesh = _pieces?.Multimesh;
        if (mesh == null) return;
        mesh.VisibleInstanceCount = _chips.Count;
        for (int i = 0; i < _chips.Count; i++)
        {
            Chip c = _chips[i];
            float scale = c.Size * Mathf.Clamp((Settled + Fade - Mathf.Max(0f, c.Rest)) / Fade, 0f, 1f);
            mesh.SetInstanceTransform(i, new Transform3D(c.Turn.Scaled(Vector3.One * scale), c.Position));
            mesh.SetInstanceColor(i, c.Colour);
        }
    }

    /// <summary>A burned, chipped ring at the hit point, riding the hull, fading over time. The oldest goes first.</summary>
    void Mark(Vector3 local, Vector3 outward, float size)
    {
        if (_marks.Count >= MaxMarks)
        {
            if (IsInstanceValid(_marks[0].decal)) _marks[0].decal.QueueFree();
            _marks.RemoveAt(0);
        }
        var decal = new Decal
        {
            Size = new Vector3(size * 2f, Mathf.Max(0.3f, size), size * 2f), TextureAlbedo = _markTexture,
            AlbedoMix = 1f, CullMask = 1, UpperFade = 0.2f, LowerFade = 0.2f,
        };
        AddChild(decal);
        // A decal projects along its own -Y: turn its Y to the surface.
        Vector3 n = outward.Normalized();
        Vector3 side = MathF.Abs(n.Dot(Vector3.Forward)) > 0.9f ? Vector3.Right : Vector3.Forward;
        Vector3 x = side.Cross(n).Normalized();
        decal.Transform = new Transform3D(new Basis(x, n, x.Cross(n)), local);
        _marks.Add((decal, 0f));
    }

    /// <summary>
    /// A remeshed chunk's smoking devices. Those no longer in it (repaired, or destroyed) stop;
    /// new ones start, up to the cap. A device smokes and throws the odd spark until it's fixed.
    /// </summary>
    public void Smoke(Int3 chunk, List<Int3> smoking)
    {
        List<Int3> stale = null;
        foreach (Int3 cell in _smokes.Keys)
            if (Structure.ChunkOf(cell) == chunk && !smoking.Contains(cell)) (stale ??= new List<Int3>()).Add(cell);
        if (stale != null)
            foreach (Int3 cell in stale)
            {
                _smokes[cell].QueueFree();
                _smokes.Remove(cell);
            }
        foreach (Int3 cell in smoking)
        {
            if (_smokes.ContainsKey(cell) || _smokes.Count >= MaxSmokes) continue;
            float size = Structure.Grid.CellSize;
            var wisp = new CpuParticles3D
            {
                Name = $"Smoke{cell.X}_{cell.Y}_{cell.Z}", Amount = 10, Lifetime = 2.2f, Mesh = _smokeMesh, MaterialOverride = _smokeMaterial,
                Direction = Vector3.Up, Spread = 12f, Gravity = new Vector3(0, 0.5f, 0),
                InitialVelocityMin = 0.25f, InitialVelocityMax = 0.6f, ScaleAmountMin = size * 0.35f, ScaleAmountMax = size * 0.7f,
                Color = new Color(0.16f, 0.15f, 0.15f, 0.4f), LocalCoords = false, Randomness = 0.5f,
                Position = new Vector3(cell.X, cell.Y, cell.Z) * size,
            };
            wisp.ScaleAmountCurve = FadeOutCurve();
            AddChild(wisp);
            _smokes[cell] = wisp;
        }
    }

    static Curve FadeOutCurve()
    {
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 0.4f));
        curve.AddPoint(new Vector2(0.5f, 1f));
        curve.AddPoint(new Vector2(1f, 0f));
        return curve;
    }

    /// <summary>A dark burn with a broken edge, drawn into a small texture.</summary>
    static ImageTexture MarkTexture()
    {
        const int n = 64;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var rng = new Random(11);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = MathF.Sqrt(dx * dx + dy * dy) * (0.85f + 0.3f * (float)rng.NextDouble());
                float a = Mathf.Clamp(1.05f - r * 1.2f, 0f, 1f);
                image.SetPixel(x, y, new Color(0.03f, 0.028f, 0.025f, a * a));
            }
        return ImageTexture.CreateFromImage(image);
    }
}
