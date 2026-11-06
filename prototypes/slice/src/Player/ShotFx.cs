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
        _starTexture = FlashTexture(true);
        _bloomTexture = FlashTexture(false);
        _smokeRamp = new Gradient { Colors = new[] { new Color(0.8f, 0.78f, 0.74f, 0.35f), new Color(0.5f, 0.5f, 0.5f, 0f) } };
        _smokeGrow = new Curve();
        _smokeGrow.AddPoint(new Vector2(0f, 0.4f));
        _smokeGrow.AddPoint(new Vector2(1f, 1.6f));
        _coneMesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.22f, Height = 1f, RadialSegments = 10, Rings = 1 };
        _quad = new QuadMesh { Size = Vector2.One };
        for (int i = 0; i < PoolSize; i++) _slots.Add(MakeSlot(i));
    }

    // ---- Muzzle flashes ------------------------------------------------------------------------

    const int PoolSize = 16;

    /// <summary>One pooled muzzle flash: a billboard, a backblast cone, a light and a smoke emitter, all kept for good.</summary>
    sealed class Slot
    {
        public Node3D Root;
        public MeshInstance3D Star, Cone;
        public OmniLight3D Light;
        public CpuParticles3D Smoke;
        public StandardMaterial3D StarMaterial, ConeMaterial;
        public int FramesLeft, TotalFrames;
        public float SmokeLeft, LightEnergy;
        public ulong Age;
        public bool Busy => FramesLeft > 0 || SmokeLeft > 0f;
    }

    readonly List<Slot> _slots = new();
    ImageTexture _starTexture, _bloomTexture;
    Gradient _smokeRamp;
    Curve _smokeGrow;
    CylinderMesh _coneMesh;
    QuadMesh _quad;
    ulong _fired;
    double _lastFlash = -10;

    /// <summary>For tests: muzzle flashes shown, how many are still showing or smoking, and the pool's size.</summary>
    public int Flashes { get; private set; }
    public int FlashesActive { get { int n = 0; foreach (Slot s in _slots) if (s.Busy) n++; return n; } }
    public int FlashesShowing { get { int n = 0; foreach (Slot s in _slots) if (s.FramesLeft > 0) n++; return n; } }
    public int FlashPool => _slots.Count;
    public string LastFeel { get; private set; } = "";

    Slot MakeSlot(int i)
    {
        var s = new Slot { Root = new Node3D { Name = $"Flash{i}", Visible = false } };
        s.StarMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true, NoDepthTest = true,
        };
        s.ConeMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = new Color(1f, 0.6f, 0.2f, 0.8f),
        };
        s.Star = new MeshInstance3D { Mesh = _quad, MaterialOverride = s.StarMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        s.Cone = new MeshInstance3D { Mesh = _coneMesh, MaterialOverride = s.ConeMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        s.Light = new OmniLight3D { Visible = false, ShadowEnabled = false };
        s.Smoke = new CpuParticles3D
        {
            Emitting = false, OneShot = true, Amount = 14, LocalCoords = false, Mesh = new SphereMesh { Radius = 0.018f, Height = 0.036f, RadialSegments = 6, Rings = 3 },
            Direction = Vector3.Forward, Spread = 22f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.8f, Gravity = new Vector3(0f, 0.25f, 0f),
            ColorRamp = _smokeRamp, ScaleAmountCurve = _smokeGrow, ScaleAmountMin = 0.8f, ScaleAmountMax = 1.6f, Explosiveness = 0.9f,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true,
            },
        };
        s.Root.AddChild(s.Star);
        s.Root.AddChild(s.Cone);
        s.Root.AddChild(s.Light);
        s.Root.AddChild(s.Smoke);
        AddChild(s.Root);
        return s;
    }

    /// <summary>
    /// A muzzle flash at the muzzle for a kind of fire (<see cref="GunFeel"/>): a flash that lasts a few
    /// frames with a short light, and smoke if the kind has it. A vessel gun passes a larger
    /// <paramref name="scale"/> for its calibre. Nodes come from a fixed pool, so nothing is allocated.
    /// </summary>
    public void Muzzle(string feel, Vector3 at, Vector3 dir, Color colour = default, float scale = 1f)
    {
        GunFeel f = GunFeel.Get(feel);
        Flashes++;
        LastFeel = f.Id;
        Slot s = null;
        foreach (Slot c in _slots)
            if (!c.Busy) { s = c; break; }
        if (s == null)
        {
            s = _slots[0]; // all busy: take the oldest
            foreach (Slot c in _slots) if (c.Age < s.Age) s = c;
        }
        s.Age = ++_fired;
        // Held fire doesn't strobe: a shot hard on the last one's heels is dimmer.
        double now = Time.GetTicksMsec() / 1000.0;
        float dim = now - _lastFlash < GunFeel.RepeatWindow ? GunFeel.RepeatDim : 1f;
        _lastFlash = now;
        Color col = colour != default && f.Style is FlashStyle.Bloom or FlashStyle.Flare ? colour : f.Colour;
        float size = f.Size * scale;
        s.Root.Visible = true;
        s.Root.GlobalTransform = new Transform3D(Basis.LookingAt(dir, MathF.Abs(dir.Y) > 0.99f ? Vector3.Right : Vector3.Up), at);

        s.StarMaterial.AlbedoTexture = f.Style == FlashStyle.Star ? _starTexture : _bloomTexture;
        s.StarMaterial.AlbedoColor = new Color(col.R * dim, col.G * dim, col.B * dim);
        float flicker = 0.85f + 0.3f * (float)(_fired * 2654435761UL % 1000UL / 1000.0);
        float wide = f.Style == FlashStyle.Star ? 2.4f : f.Style == FlashStyle.Bloom ? 2.0f : 1.5f;
        s.Star.Scale = Vector3.One * size * wide * flicker;
        s.Star.Position = Vector3.Forward * size * 0.15f;
        s.Star.Visible = true;

        bool rocket = f.Style == FlashStyle.Rocket;
        s.Cone.Visible = rocket;
        if (rocket)
        {
            // The backblast cone points back along the barrel (the cone's Y turned to the root's +Z).
            s.Cone.Basis = new Basis(Vector3.Right, Mathf.Pi / 2f).Scaled(new Vector3(size * 1.3f, size * 2.6f, size * 1.3f));
            s.Cone.Position = Vector3.Back * size * 1.3f;
        }

        s.Light.LightColor = col;
        s.LightEnergy = f.LightEnergy * scale * dim;
        s.Light.LightEnergy = s.LightEnergy;
        s.Light.OmniRange = f.LightRange * MathF.Sqrt(scale);
        s.Light.Visible = true;
        s.FramesLeft = s.TotalFrames = f.Frames;

        s.SmokeLeft = 0f;
        if (f.Smoke > 0f)
        {
            s.Smoke.Lifetime = f.SmokeLife;
            s.Smoke.Explosiveness = rocket ? 0.15f : 0.9f;
            s.Smoke.InitialVelocityMax = rocket ? 0.5f : 0.8f;
            s.Smoke.Direction = rocket ? Vector3.Back : Vector3.Forward;
            s.Smoke.Spread = rocket ? 14f : 22f;
            s.Smoke.ScaleAmountMax = rocket ? 3f : 1.6f;
            s.Smoke.Restart();
            s.Smoke.Emitting = true;
            s.SmokeLeft = f.SmokeLife + 0.1f;
        }
    }

    void TickFlashes(float dt)
    {
        foreach (Slot s in _slots)
        {
            if (s.FramesLeft > 0)
            {
                s.Light.LightEnergy = s.LightEnergy * s.FramesLeft / s.TotalFrames;
                if (--s.FramesLeft == 0)
                {
                    s.Star.Visible = false;
                    s.Cone.Visible = false;
                    s.Light.Visible = false;
                }
            }
            if (s.SmokeLeft > 0f) s.SmokeLeft -= dt;
            if (!s.Busy && s.Root.Visible) s.Root.Visible = false;
        }
    }

    /// <summary>A four-point star with small diagonal spikes (or a soft round bloom), white-hot in the middle, drawn into a texture.</summary>
    static ImageTexture FlashTexture(bool star)
    {
        const int n = 96;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float r = MathF.Sqrt(dx * dx + dy * dy);
            float a;
            if (star)
            {
                float ax = MathF.Abs(dx), ay = MathF.Abs(dy);
                float u = MathF.Abs(dx + dy) * 0.7071f, v = MathF.Abs(dx - dy) * 0.7071f;
                float cross = MathF.Max(Spike(ax, ay, 0.09f), Spike(ay, ax, 0.09f));
                float diag = MathF.Max(Spike(u, v, 0.07f), Spike(v, u, 0.07f)) * 0.5f;
                a = MathF.Max(MathF.Max(cross, diag), Mathf.Clamp(1f - r * 3.2f, 0f, 1f));
            }
            else a = MathF.Pow(Mathf.Clamp(1f - r, 0f, 1f), 1.8f);
            image.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp(a, 0f, 1f)));
        }
        return ImageTexture.CreateFromImage(image);
    }

    // A spike along one axis: bright near the centre, tapering to a point at the edge.
    static float Spike(float along, float across, float width)
    {
        float t = Mathf.Clamp(1f - along, 0f, 1f);
        return t * Mathf.Clamp(1f - across / (width * (0.25f + t)), 0f, 1f);
    }

    /// <summary>Fires a bolt from the muzzle to where the shot lands; the impact shows when it gets there.</summary>
    public void Fire(Vector3 muzzle, Vector3 end, Color colour, Action arrived, string feel = null)
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
        Vector3 aim = end - muzzle;
        Muzzle(feel, muzzle, aim.LengthSquared() < 1e-6f ? Vector3.Forward : aim.Normalized(), colour);
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
        TickFlashes(dt);
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
