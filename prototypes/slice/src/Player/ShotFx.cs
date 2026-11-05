using System;
using System.Collections.Generic;
using Godot;

namespace Slice.Player;

/// <summary>What a shot struck, for its impact.</summary>
public enum ImpactKind { Ground, Block, Creature, Nothing }

/// <summary>
/// What a shot looks like: a bolt of light from the tool's muzzle that flies to where the shot
/// lands, then sparks, a flash and a scorch on ground and blocks, or a spray on a creature. All
/// procedural (no textures or models). The scorch fades over half a minute. Effects on a
/// structure ride on its node, so they stay on its wall however it moves.
/// </summary>
public partial class ShotFx : Node3D
{
    const float BoltSpeed = 240f, BoltLength = 1.6f, ScorchLife = 30f;

    sealed class Bolt
    {
        public MeshInstance3D Mesh;
        public OmniLight3D Light;
        public Vector3 From, To;
        public float Travelled, Length;
        public Action Arrived;
    }

    readonly List<Bolt> _bolts = new();
    readonly List<(Decal decal, float age)> _scorches = new();
    readonly List<(OmniLight3D light, float left)> _flashes = new();
    StandardMaterial3D _boltMaterial;
    BoxMesh _boltMesh;
    ImageTexture _scorchTexture;

    /// <summary>For tests and screenshots: bolts fired, impacts shown, and the last impact.</summary>
    public int Bolts { get; private set; }
    public int Impacts { get; private set; }
    public (Vector3 At, ImpactKind Kind) LastImpact { get; private set; }
    public int BoltsInFlight => _bolts.Count;

    public override void _Ready()
    {
        _boltMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.55f, 0.2f),
            EmissionEnabled = true, Emission = new Color(1f, 0.5f, 0.15f), EmissionEnergyMultiplier = 4f,
        };
        _boltMesh = new BoxMesh { Size = new Vector3(0.05f, 0.05f, BoltLength) };
        _scorchTexture = ScorchTexture();
    }

    /// <summary>Fires a bolt from the muzzle to where the shot lands; the impact shows when it gets there.</summary>
    public void Fire(Vector3 muzzle, Vector3 end, Color colour, Action arrived)
    {
        Bolts++;
        var mesh = new MeshInstance3D { Mesh = _boltMesh, MaterialOverride = _boltMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        if (colour != default)
            mesh.MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = colour,
                EmissionEnabled = true, Emission = colour, EmissionEnergyMultiplier = 4f,
            };
        var light = new OmniLight3D { LightColor = colour == default ? new Color(1f, 0.55f, 0.2f) : colour, LightEnergy = 1.6f, OmniRange = 4f };
        AddChild(mesh);
        AddChild(light);
        Flash(muzzle, new Color(1f, 0.7f, 0.4f), 2.5f, 0.06f);
        var bolt = new Bolt { Mesh = mesh, Light = light, From = muzzle, To = end, Length = muzzle.DistanceTo(end), Arrived = arrived };
        _bolts.Add(bolt);
        Place(bolt);
    }

    void Place(Bolt b)
    {
        Vector3 dir = (b.To - b.From).Normalized();
        float head = Mathf.Min(b.Travelled, b.Length);
        Vector3 at = b.From + dir * Mathf.Max(0f, head - BoltLength * 0.5f);
        var basis = Basis.LookingAt(dir.IsZeroApprox() ? Vector3.Forward : dir, MathF.Abs(dir.Y) > 0.99f ? Vector3.Right : Vector3.Up);
        b.Mesh.GlobalTransform = new Transform3D(basis, at);
        b.Light.GlobalPosition = at;
    }

    /// <summary>The impact where a shot landed: sparks, a flash, and a scorch on ground or blocks (on a structure, riding its node).</summary>
    public void Impact(Vector3 at, Vector3 normal, ImpactKind kind, Node3D onto = null)
    {
        if (kind == ImpactKind.Nothing) return;
        Impacts++;
        LastImpact = (at, kind);
        Color spark = kind == ImpactKind.Creature ? new Color(0.75f, 0.12f, 0.08f) : new Color(1f, 0.75f, 0.3f);
        var burst = new CpuParticles3D
        {
            Emitting = true, OneShot = true, Explosiveness = 1f, Amount = kind == ImpactKind.Creature ? 18 : 28, Lifetime = 0.45f,
            Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.03f, 0.08f) },
            Direction = normal == Vector3.Zero ? Vector3.Up : normal, Spread = 55f,
            InitialVelocityMin = 3f, InitialVelocityMax = 8f, Gravity = new Vector3(0, -9.8f, 0),
            ScaleAmountMin = 0.6f, ScaleAmountMax = 1.3f, Color = spark,
        };
        burst.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
            EmissionEnabled = true, Emission = spark, EmissionEnergyMultiplier = 3f,
        };
        AddChild(burst);
        burst.GlobalPosition = at + normal * 0.05f;
        GetTree().CreateTimer(1.2).Timeout += burst.QueueFree;
        Flash(at + normal * 0.2f, spark, kind == ImpactKind.Creature ? 1.2f : 3f, 0.12f);
        if (kind == ImpactKind.Creature) return;

        var scorch = new Decal
        {
            Size = new Vector3(0.7f, 0.5f, 0.7f), TextureAlbedo = _scorchTexture, Modulate = new Color(1, 1, 1, 0.95f),
            AlbedoMix = 1f, CullMask = 1, UpperFade = 0.2f, LowerFade = 0.2f,
        };
        Node3D parent = onto ?? (Node3D)this;
        parent.AddChild(scorch);
        Vector3 n = normal == Vector3.Zero ? Vector3.Up : normal.Normalized();
        // A decal projects along its own -Y: turn its Y to the surface normal.
        Vector3 side = MathF.Abs(n.Dot(Vector3.Forward)) > 0.9f ? Vector3.Right : Vector3.Forward;
        Vector3 x = side.Cross(n).Normalized();
        scorch.GlobalTransform = new Transform3D(new Basis(x, n, x.Cross(n)), at);
        _scorches.Add((scorch, 0f));
    }

    void Flash(Vector3 at, Color colour, float energy, float seconds)
    {
        var light = new OmniLight3D { LightColor = colour, LightEnergy = energy, OmniRange = 5f };
        AddChild(light);
        light.GlobalPosition = at;
        _flashes.Add((light, seconds));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            Bolt b = _bolts[i];
            b.Travelled += BoltSpeed * dt;
            if (b.Travelled >= b.Length + BoltLength * 0.5f)
            {
                b.Mesh.QueueFree();
                b.Light.QueueFree();
                _bolts.RemoveAt(i);
                b.Arrived?.Invoke();
                continue;
            }
            Place(b);
        }
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var (light, left) = _flashes[i];
            left -= dt;
            if (left <= 0f)
            {
                light.QueueFree();
                _flashes.RemoveAt(i);
            }
            else _flashes[i] = (light, left);
        }
        for (int i = _scorches.Count - 1; i >= 0; i--)
        {
            var (decal, age) = _scorches[i];
            age += dt;
            if (!IsInstanceValid(decal) || age > ScorchLife)
            {
                if (IsInstanceValid(decal)) decal.QueueFree();
                _scorches.RemoveAt(i);
                continue;
            }
            decal.Modulate = new Color(1, 1, 1, 0.95f * Mathf.Clamp((ScorchLife - age) / 6f, 0f, 1f));
            _scorches[i] = (decal, age);
        }
    }

    /// <summary>A soft black burn with a hot rim, drawn into a small texture.</summary>
    static ImageTexture ScorchTexture()
    {
        const int n = 64;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var rng = new Random(7);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float r = MathF.Sqrt(dx * dx + dy * dy) * (0.9f + 0.2f * (float)rng.NextDouble());
            float a = Mathf.Clamp(1.1f - r * 1.15f, 0f, 1f);
            float hot = Mathf.Clamp(1f - MathF.Abs(r - 0.18f) * 9f, 0f, 1f) * 0.6f;
            image.SetPixel(x, y, new Color(0.05f + hot * 0.9f, 0.04f + hot * 0.35f, 0.03f, a * a));
        }
        return ImageTexture.CreateFromImage(image);
    }
}
