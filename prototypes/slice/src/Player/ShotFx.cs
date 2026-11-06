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
        BuildFlashMeshes();
        _smokeRamp = new Gradient { Colors = new[] { new Color(0.8f, 0.78f, 0.74f, 0.3f), new Color(0.5f, 0.5f, 0.5f, 0f) } };
        _smokeGrow = new Curve();
        _smokeGrow.AddPoint(new Vector2(0f, 0.4f));
        _smokeGrow.AddPoint(new Vector2(1f, 1.6f));
        _coneMesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.22f, Height = 1f, RadialSegments = 6, Rings = 1 };
        for (int i = 0; i < PoolSize; i++) _slots.Add(MakeSlot(i));
    }

    // ---- Muzzle flashes ------------------------------------------------------------------------

    const int PoolSize = 16;

    /// <summary>One pooled muzzle flash: a chunky petal burst (or a round bloom), a hot core, a backblast cone, a light and a smoke emitter, all kept for good.</summary>
    sealed class Slot
    {
        public Node3D Root;
        public MeshInstance3D Burst, Core, Orb, Cone;
        public OmniLight3D Light;
        public CpuParticles3D Smoke;
        public StandardMaterial3D BurstMaterial, OrbMaterial, ConeMaterial;
        public int FramesLeft, TotalFrames;
        public float SmokeLeft, LightEnergy;
        public ulong Age;
        public Func<Vector3?> Follow;
        public bool Busy => FramesLeft > 0 || SmokeLeft > 0f;
    }

    readonly List<Slot> _slots = new();
    readonly Dictionary<string, ArrayMesh> _burstMeshes = new();
    ArrayMesh _coreBurst;
    SphereMesh _orbMesh, _orbCore;
    StandardMaterial3D _coreMaterial;
    Gradient _smokeRamp;
    Curve _smokeGrow;
    CylinderMesh _coneMesh;
    ulong _fired;
    double _lastFlash = -10;

    /// <summary>For tests: muzzle flashes shown, how many are still showing or smoking, and the pool's size.</summary>
    public int Flashes { get; private set; }
    public int FlashesActive { get { int n = 0; foreach (Slot s in _slots) if (s.Busy) n++; return n; } }
    public int FlashesShowing { get { int n = 0; foreach (Slot s in _slots) if (s.FramesLeft > 0) n++; return n; } }
    public int FlashPool => _slots.Count;
    public string LastFeel { get; private set; } = "";

    void BuildFlashMeshes()
    {
        foreach (var (id, shape) in GunFeel.Shapes) _burstMeshes[id] = BuildBurst(shape);
        _coreBurst = BuildOctahedron();
        _orbMesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 14, Rings = 7 };
        _orbCore = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 5 };
        _coreMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.97f, 0.85f), CullMode = BaseMaterial3D.CullModeEnum.Disabled, VertexColorUseAsAlbedo = true };
    }

    // One petal: a four-sided spike from the barrel tip out along dir, flat-shaded by its facets.
    static void Petal(SurfaceTool st, Vector3 dir, float len, float width, float shade)
    {
        dir = dir.Normalized();
        Vector3 side = dir.Cross(Vector3.Up);
        if (side.LengthSquared() < 1e-4f) side = dir.Cross(Vector3.Right);
        side = side.Normalized();
        Vector3 up = side.Cross(dir).Normalized();
        Vector3 tip = dir * len;
        Vector3[] b = { side * width, up * width, -side * width, -up * width };
        for (int k = 0; k < 4; k++)
        {
            float face = shade * (k % 2 == 0 ? 1f : 0.8f); // alternating facets read as flat-shaded
            st.SetColor(new Color(face * 0.75f, face * 0.75f, face * 0.75f));
            st.AddVertex(b[k]);
            st.AddVertex(b[(k + 1) % 4]);
            st.SetColor(new Color(face, face, face));
            st.AddVertex(tip);
        }
    }

    /// <summary>A forward burst: a long centre petal and a ring of shorter ones in a cone, pointing along -Z, one unit long.</summary>
    static ArrayMesh BuildBurst(BurstShape s)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Petal(st, Vector3.Forward, 1f, s.CentreWidth, 1f);
        float theta = Mathf.DegToRad(s.Spread);
        for (int i = 0; i < s.Petals; i++)
        {
            float phi = 0.4f + i * Mathf.Tau / s.Petals;
            var dir = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Sin(theta) * MathF.Sin(phi), -MathF.Cos(theta));
            Petal(st, dir, s.SideLength, s.Width, 0.9f);
        }
        return st.Commit();
    }

    static ArrayMesh BuildOctahedron()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] p = { Vector3.Right, Vector3.Up, Vector3.Left, Vector3.Down };
        for (int k = 0; k < 4; k++)
            foreach (Vector3 apex in new[] { Vector3.Forward, Vector3.Back })
            {
                float face = 0.85f + 0.15f * (k % 2);
                st.SetColor(new Color(face, face, face));
                st.AddVertex(p[k]);
                st.AddVertex(p[(k + 1) % 4]);
                st.AddVertex(apex);
            }
        return st.Commit();
    }

    Slot MakeSlot(int i)
    {
        var s = new Slot { Root = new Node3D { Name = $"Flash{i}", Visible = false } };
        s.BurstMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        s.OrbMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        s.ConeMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = new Color(1f, 0.6f, 0.2f, 0.7f),
        };
        const GeometryInstance3D.ShadowCastingSetting off = GeometryInstance3D.ShadowCastingSetting.Off;
        s.Burst = new MeshInstance3D { MaterialOverride = s.BurstMaterial, CastShadow = off, Visible = false };
        s.Core = new MeshInstance3D { MaterialOverride = _coreMaterial, CastShadow = off, Visible = false };
        s.Orb = new MeshInstance3D { Mesh = _orbMesh, MaterialOverride = s.OrbMaterial, CastShadow = off, Visible = false };
        s.Cone = new MeshInstance3D { Mesh = _coneMesh, MaterialOverride = s.ConeMaterial, CastShadow = off, Visible = false };
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
        s.Root.AddChild(s.Burst);
        s.Root.AddChild(s.Core);
        s.Root.AddChild(s.Orb);
        s.Root.AddChild(s.Cone);
        s.Root.AddChild(s.Light);
        s.Root.AddChild(s.Smoke);
        AddChild(s.Root);
        return s;
    }

    /// <summary>
    /// A muzzle flash at the barrel tip for a kind of fire (<see cref="GunFeel"/>), a few frames long
    /// with a short light: a chunky low-poly petal burst for bullets and the tool, a round bloom
    /// for energy, a backblast for rockets. <paramref name="follow"/> keeps it on the tip while it lasts
    /// (the hand's recoil); a vessel gun passes a larger <paramref name="scale"/> for its calibre.
    /// Nodes come from a fixed pool, so nothing is allocated.
    /// </summary>
    public void Muzzle(string feel, Vector3 at, Vector3 dir, Color colour = default, float scale = 1f, Func<Vector3?> follow = null, string shape = null)
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
        s.Follow = follow;
        // Held fire doesn't strobe: a shot hard on the last one's heels is dimmer.
        double now = Time.GetTicksMsec() / 1000.0;
        float dim = now - _lastFlash < GunFeel.RepeatWindow ? GunFeel.RepeatDim : 1f;
        _lastFlash = now;
        Color col = colour != default && f.Style == FlashStyle.Bloom ? colour : f.Colour;
        float size = f.Size * scale;
        // A little different each shot: a random roll and size.
        uint hash = (uint)(_fired * 2654435761UL);
        float roll = (hash % 1000) / 1000f * Mathf.Tau, jitter = 0.88f + 0.24f * ((hash >> 10) % 1000 / 1000f);
        s.Root.Visible = true;
        s.Root.GlobalTransform = new Transform3D(Basis.LookingAt(dir, MathF.Abs(dir.Y) > 0.99f ? Vector3.Right : Vector3.Up), at);

        bool bloom = f.Style == FlashStyle.Bloom, rocket = f.Style == FlashStyle.Rocket;
        s.Burst.Visible = !bloom;
        s.Core.Visible = true;
        s.Orb.Visible = bloom;
        Basis spin = new Basis(Vector3.Forward, roll);
        if (bloom)
        {
            s.OrbMaterial.AlbedoColor = new Color(col.R * dim, col.G * dim, col.B * dim, 0.75f);
            s.Orb.Scale = Vector3.One * size * jitter;
            s.Core.Mesh = _orbCore;
            s.Core.Scale = Vector3.One * size * jitter * 0.4f;
            s.Core.Position = Vector3.Zero;
        }
        else
        {
            s.Burst.Mesh = _burstMeshes[shape ?? f.Shape ?? "cone5"];
            s.BurstMaterial.AlbedoColor = new Color(col.R * dim, col.G * dim, col.B * dim);
            s.Burst.Basis = spin.Scaled(Vector3.One * size * jitter);
            s.Core.Mesh = _coreBurst;
            s.Core.Basis = spin.Scaled(new Vector3(0.2f, 0.2f, 0.32f) * size * jitter);
            s.Core.Position = Vector3.Forward * size * 0.12f;
        }
        s.Cone.Visible = rocket;
        if (rocket)
        {
            // The backblast cone points back along the barrel (the cone's Y turned to the root's +Z).
            s.Cone.Basis = new Basis(Vector3.Right, Mathf.Pi / 2f).Scaled(new Vector3(size * 0.7f, size * 1.6f, size * 0.7f));
            s.Cone.Position = Vector3.Back * size * 0.8f;
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
                if (s.Follow?.Invoke() is Vector3 tip) s.Root.GlobalPosition = tip; // rides the hand for its few frames
                s.Light.LightEnergy = s.LightEnergy * s.FramesLeft / s.TotalFrames;
                if (--s.FramesLeft == 0)
                {
                    s.Burst.Visible = s.Core.Visible = s.Orb.Visible = s.Cone.Visible = false;
                    s.Light.Visible = false;
                }
            }
            if (s.SmokeLeft > 0f) s.SmokeLeft -= dt;
            if (!s.Busy && s.Root.Visible) s.Root.Visible = false;
        }
    }

    /// <summary>Fires a bolt from the muzzle to where the shot lands; the impact shows when it gets there.</summary>
    public void Fire(Vector3 muzzle, Vector3 end, Color colour, Action arrived, string feel = null, Func<Vector3?> follow = null)
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
        Muzzle(feel, muzzle, aim.LengthSquared() < 1e-6f ? Vector3.Forward : aim.Normalized(), colour, 1f, follow);
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
