using System;
using Godot;

namespace Slice.Surface;

/// <summary>
/// What a wound leaves: a spurt at the hit in the creature's own blood, and splats on whatever the
/// ray finds behind it and under it. A splat only ever lies on a real surface: with nothing there,
/// nothing is drawn. Swarm critters go in a quick burst of their own. Pooled: the oldest splat is
/// reused, and each fades away after half a minute.
/// </summary>
public partial class BloodFx : Node3D
{
    const int BurstCount = 8, SplatCount = 40;
    const float SplatLife = 30f, Fade = 5f;

    readonly CpuParticles3D[] _bursts = new CpuParticles3D[BurstCount];
    readonly MeshInstance3D[] _splats = new MeshInstance3D[SplatCount];
    readonly float[] _age = new float[SplatCount];
    int _nextBurst, _nextSplat;

    /// <summary>Spurts and splats so far (tests).</summary>
    public int Bursts { get; private set; }
    public int Splats { get; private set; }

    public override void _Ready()
    {
        var drop = new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 6, Rings = 3 };
        drop.Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.35f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        for (int i = 0; i < BurstCount; i++)
        {
            var p = new CpuParticles3D
            {
                Name = $"Burst{i}", Emitting = false, OneShot = true, Explosiveness = 0.95f, Lifetime = 0.5f, Amount = 16, Mesh = drop,
                Direction = Vector3.Up, Spread = 45f, Gravity = Vector3.Down * 9f, InitialVelocityMin = 1.5f, InitialVelocityMax = 4f,
                ScaleAmountMin = 0.6f, ScaleAmountMax = 1.4f, LocalCoords = false,
            };
            AddChild(p);
            _bursts[i] = p;
        }
        Texture2D stain = Stain();
        var quad = new QuadMesh { Size = Vector2.One };
        for (int i = 0; i < SplatCount; i++)
        {
            var m = new MeshInstance3D
            {
                Name = $"Splat{i}", Mesh = quad, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoTexture = stain, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.25f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            };
            AddChild(m);
            _splats[i] = m;
        }
        _age.AsSpan().Fill(SplatLife);
    }

    /// <summary>An irregular stain: a blob with lumps round its edge, made once.</summary>
    static Texture2D Stain()
    {
        const int Size = 64;
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        var rng = new Wildlife.Rng(5);
        var lumps = new Vector3[9];
        for (int i = 0; i < lumps.Length; i++)
        {
            float a = rng.Range(0f, MathF.Tau), r = i == 0 ? 0f : rng.Range(0.3f, 0.75f);
            lumps[i] = new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, i == 0 ? 0.55f : rng.Range(0.08f, 0.24f));
        }
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
            float alpha = 0f;
            foreach (Vector3 l in lumps)
                alpha = MathF.Max(alpha, Math.Clamp((l.Z - p.DistanceTo(new Vector2(l.X, l.Y))) * 24f, 0f, 1f));
            image.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * 0.9f));
        }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A shot or blow lands: a spurt back along it, and splats on what is behind and under the wound.</summary>
    public void Hit(Vector3 at, Vector3 dir, Color blood, float size)
    {
        Burst(at, -dir, blood, Math.Clamp(10 + (int)(size * 6f), 10, 24), 0.5f, 45f);
        float scale = Math.Clamp(0.35f + size * 0.15f, 0.35f, 1.1f);
        Splat(at, dir, 3f, blood, scale);
        Splat(at, Vector3.Down, 4f, blood, scale * 0.8f);
    }

    /// <summary>A critter of a swarm, or the whole cloud, gone in a quick burst.</summary>
    public void Pop(Vector3 at, Color blood, int amount)
    {
        Burst(at, Vector3.Up, blood, amount, 0.3f, 180f);
        Splat(at, Vector3.Down, 5f, blood, 0.35f);
    }

    void Burst(Vector3 at, Vector3 dir, Color blood, int amount, float life, float spread)
    {
        CpuParticles3D p = _bursts[_nextBurst++ % BurstCount];
        p.GlobalPosition = at;
        p.Amount = amount;
        p.Lifetime = life;
        p.Spread = spread;
        p.Direction = dir.LengthSquared() > 1e-4f ? dir.Normalized() : Vector3.Up;
        p.Color = blood;
        p.Restart();
        Bursts++;
    }

    void Splat(Vector3 at, Vector3 dir, float reach, Color blood, float size)
    {
        var ray = PhysicsRayQueryParameters3D.Create(at, at + dir.Normalized() * reach, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull);
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0) return; // nothing there: nothing hangs in the air
        Vector3 point = (Vector3)hit["position"], normal = (Vector3)hit["normal"];
        int i = _nextSplat++ % SplatCount;
        MeshInstance3D m = _splats[i];
        Vector3 side = MathF.Abs(normal.Y) > 0.9f ? Vector3.Right : Vector3.Up;
        Vector3 x = side.Cross(normal).Normalized();
        Basis basis = new Basis(x, normal.Cross(x).Normalized(), normal).Rotated(normal, i * 2.4f).Scaled(Vector3.One * size * (1f + (i % 3) * 0.2f));
        m.GlobalTransform = new Transform3D(basis, point + normal * 0.015f);
        ((StandardMaterial3D)m.MaterialOverride).AlbedoColor = blood;
        m.Visible = true;
        _age[i] = 0f;
        Splats++;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = 0; i < SplatCount; i++)
        {
            MeshInstance3D m = _splats[i];
            if (!m.Visible) continue;
            _age[i] += dt;
            if (_age[i] >= SplatLife)
            {
                m.Visible = false;
                continue;
            }
            float left = SplatLife - _age[i];
            if (left >= Fade) continue;
            var material = (StandardMaterial3D)m.MaterialOverride;
            Color c = material.AlbedoColor;
            material.AlbedoColor = new Color(c.R, c.G, c.B, left / Fade);
        }
    }

    /// <summary>The world's frame moved: everything lying in it moves too.</summary>
    public void Shift(Vector3 by)
    {
        foreach (MeshInstance3D m in _splats) if (m.Visible) m.GlobalPosition += by;
    }
}
