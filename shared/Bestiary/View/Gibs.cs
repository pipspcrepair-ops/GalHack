using System;
using System.Collections.Generic;
using GalHack.Physics;
using Godot;

namespace Wildlife;

/// <summary>
/// What a blast leaves of a body, and what a burning weapon leaves. Blown apart, a body comes down
/// as pieces of itself (its own limbs, head and trunk, as they were posed when it went, and a few
/// bits of what was inside: flesh in its blood's colour, or a machine's gears, plates and wire),
/// each a real rigid body that bounces, rolls and comes to rest on whatever is under it, then
/// freezes there. Burned away, a body leaves a low heap of ash on a scorch. Pieces are capped: the
/// oldest at rest go first, so a battle never costs the frame.
/// </summary>
public partial class Gibs : Node3D
{
    /// <summary>Most pieces in the world at once, and how long a piece may tumble before it is held where it lies.</summary>
    public int MostPieces = 90;
    public const float MostTumble = 7f;

    sealed class Gib
    {
        public RigidBody3D Body;
        public float Age;
        public bool Resting;
    }

    readonly List<Gib> _gibs = new();
    readonly List<Node3D> _ash = new();
    static StandardMaterial3D _scorch, _ashMat;
    static readonly Dictionary<Color, StandardMaterial3D> Bits = new();

    /// <summary>Pieces flying or at rest now, and how many were ever thrown (tests).</summary>
    public int Count => _gibs.Count;
    public int Thrown { get; private set; }
    public int AshHeaps => _ash.Count;
    /// <summary>Each piece's body (tests: where they came to rest).</summary>
    public IEnumerable<RigidBody3D> Pieces { get { foreach (Gib g in _gibs) yield return g.Body; } }
    /// <summary>Whether every piece has come to rest.</summary>
    public bool AllResting => _gibs.TrueForAll(g => g.Resting);

    /// <summary>
    /// Blows a body apart. <paramref name="pose"/> is each piece where it was (world), <paramref name="from"/>
    /// where the blast was, <paramref name="force"/> how hard (m/s at the body). Pieces that are mere
    /// threads (a bow's string) are left out.
    /// </summary>
    public void Burst(Species s, RigLayout layout, IReadOnlyList<Transform3D> pose, Vector3 from, float force)
    {
        Material material = s.Figure != null ? CreatureMaterials.Clothed : CreatureMaterials.For(s);
        var rng = new Rng((ulong)(Time.GetTicksUsec() + (ulong)s.Seed));
        Vector3 middle = Vector3.Zero;
        for (int i = 0; i < pose.Count; i++) middle += pose[i].Origin;
        middle /= Math.Max(1, pose.Count);
        for (int i = 0; i < layout.Pieces.Count && i < pose.Count; i++)
        {
            Piece piece = layout.Pieces[i];
            if (piece.Name.StartsWith("string") || piece.Blur) continue;
            Mesh mesh = MeshBuilder.For(piece);
            Aabb box = mesh.GetAabb();
            if (box.Size.X * box.Size.Y * box.Size.Z < 1e-6f) continue;
            Transform3D at = pose[i].Orthonormalized();
            Throw(mesh, material, box, at, middle, from, force, rng);
        }
        // What was inside: lumps of flesh, or a machine's works.
        int bits = 4 + rng.Int(0, 3);
        float size = MathF.Max(0.06f, s.Figure != null ? s.Figure.Height * 0.05f : s.Body.Size * 0.06f);
        for (int k = 0; k < bits; k++)
        {
            Mesh mesh;
            Color c;
            if (s.Mechanical)
            {
                c = k % 3 == 0 ? new Color(0.55f, 0.47f, 0.25f) : k % 3 == 1 ? s.Colors.Base.Darkened(0.3f) : new Color(0.12f, 0.12f, 0.13f);
                mesh = k % 3 == 0 ? new CylinderMesh { TopRadius = size, BottomRadius = size, Height = size * 0.35f, RadialSegments = 8 } // a gear
                    : k % 3 == 1 ? new BoxMesh { Size = new Vector3(size * 2.2f, size * 0.2f, size * 1.4f) } // a plate
                    : new CylinderMesh { TopRadius = size * 0.12f, BottomRadius = size * 0.12f, Height = size * 3f, RadialSegments = 5 }; // a run of cable
            }
            else
            {
                c = s.Blood.Darkened(0.25f + 0.2f * rng.Float());
                mesh = new SphereMesh { Radius = size * rng.Range(0.6f, 1.1f), Height = size * rng.Range(0.8f, 1.6f), RadialSegments = 7, Rings = 4 };
            }
            Aabb box = mesh.GetAabb();
            var at = new Transform3D(Basis.FromEuler(new Vector3(rng.Range(0f, 6f), rng.Range(0f, 6f), rng.Range(0f, 6f))), middle + new Vector3(rng.Range(-0.15f, 0.15f), rng.Range(-0.1f, 0.2f), rng.Range(-0.15f, 0.15f)));
            Throw(mesh, Bit(c), box, at, middle, from, force * 1.15f, rng);
        }
        Trim();
    }

    static StandardMaterial3D Bit(Color c)
    {
        if (Bits.TryGetValue(c, out StandardMaterial3D m)) return m;
        if (Bits.Count > 64) Bits.Clear();
        return Bits[c] = new StandardMaterial3D { AlbedoColor = c, Roughness = 0.55f };
    }

    void Throw(Mesh mesh, Material material, Aabb box, Transform3D at, Vector3 middle, Vector3 from, float force, Rng rng)
    {
        Vector3 size = box.Size;
        float volume = MathF.Max(size.X * size.Y * size.Z, 1e-5f);
        var body = new RigidBody3D
        {
            Name = "Gib",
            Mass = Math.Clamp(volume * 700f, 0.2f, 60f),
            CollisionLayer = Layers.Props,
            CollisionMask = Layers.World | Layers.Hull | Layers.Interior | Layers.Props,
            ContinuousCd = true,
            CanSleep = true,
            LinearDamp = 0.05f,
            AngularDamp = 0.6f,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.9f, Bounce = 0.15f },
        };
        // The box it is drawn in, a little inside its corners: what it rests on is what it shows.
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = (size * 0.86f).Max(new Vector3(0.03f, 0.03f, 0.03f)) }, Position = box.GetCenter() });
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = volume > 0.0005f ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(body);
        body.GlobalTransform = at;
        Vector3 centre = at * box.GetCenter();
        Vector3 away = centre - from;
        away = away.LengthSquared() > 1e-4f ? away.Normalized() : (centre - middle).Normalized();
        Vector3 spread = (centre - middle).LengthSquared() > 1e-4f ? (centre - middle).Normalized() : Vector3.Up;
        Vector3 v = (away * 0.7f + spread * 0.5f + Vector3.Up * 0.6f).Normalized() * force * rng.Range(0.55f, 1.15f);
        body.LinearVelocity = v.LimitLength(18f);
        body.AngularVelocity = new Vector3(rng.Range(-9f, 9f), rng.Range(-9f, 9f), rng.Range(-9f, 9f));
        _gibs.Add(new Gib { Body = body });
        Thrown++;
    }

    /// <summary>The oldest pieces go first once there are too many (resting ones before flying ones).</summary>
    void Trim()
    {
        while (_gibs.Count > MostPieces)
        {
            int k = _gibs.FindIndex(g => g.Resting);
            if (k < 0) k = 0;
            _gibs[k].Body.QueueFree();
            _gibs.RemoveAt(k);
        }
    }

    /// <summary>
    /// Burns a body away: a low heap of ash on a scorch, on the ground at <paramref name="at"/> (its
    /// <paramref name="up"/>), as wide as the body was.
    /// </summary>
    public Node3D Ash(Vector3 at, Vector3 up, float width, ulong seed)
    {
        var rng = new Rng(seed ^ 0xA5u);
        var root = new Node3D { Name = "Ash" };
        AddChild(root);
        Vector3 x = up.Cross(Vector3.Forward).LengthSquared() > 1e-3f ? up.Cross(Vector3.Forward).Normalized() : Vector3.Right;
        root.GlobalTransform = new Transform3D(new Basis(x, up, x.Cross(up)), at);
        _scorch ??= Scorch();
        _ashMat ??= new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.19f, 0.18f), Roughness = 1f };
        float w = Math.Clamp(width, 0.3f, 3f);
        root.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(w * 2.4f, w * 2.4f), Orientation = PlaneMesh.OrientationEnum.Y, Material = _scorch }, Position = Vector3.Up * 0.012f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        // The heap: a low mound and a few clumps round it, sitting on the ground (their bottoms flat on it).
        int clumps = 3 + rng.Int(0, 3);
        for (int k = 0; k <= clumps; k++)
        {
            float r = k == 0 ? w * 0.42f : w * rng.Range(0.1f, 0.2f);
            float h = r * (k == 0 ? 0.55f : rng.Range(0.4f, 0.8f));
            var mound = new SphereMesh { Radius = r, Height = h * 2f, IsHemisphere = true, RadialSegments = 10, Rings = 4, Material = _ashMat };
            float a = rng.Range(0f, MathF.Tau), d = k == 0 ? 0f : w * rng.Range(0.35f, 0.75f);
            root.AddChild(new MeshInstance3D { Mesh = mound, Position = new Vector3(MathF.Cos(a) * d, 0f, MathF.Sin(a) * d) });
        }
        _ash.Add(root);
        while (_ash.Count > 24)
        {
            _ash[0].QueueFree();
            _ash.RemoveAt(0);
        }
        return root;
    }

    static StandardMaterial3D Scorch()
    {
        var g = new Gradient { Offsets = new[] { 0f, 0.55f, 1f }, Colors = new[] { new Color(0.03f, 0.025f, 0.02f, 0.92f), new Color(0.06f, 0.05f, 0.04f, 0.6f), new Color(0.08f, 0.07f, 0.06f, 0f) } };
        var tex = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 128, Height = 128 };
        return new StandardMaterial3D { AlbedoTexture = tex, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 1f, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled };
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        for (int i = _gibs.Count - 1; i >= 0; i--)
        {
            Gib g = _gibs[i];
            if (g.Resting) continue;
            g.Age += dt;
            if (!GodotObject.IsInstanceValid(g.Body))
            {
                _gibs.RemoveAt(i);
                continue;
            }
            // At rest (or tumbling too long): held where it lies, no longer simulated.
            bool still = g.Body.Sleeping || g.Age > 0.6f && g.Body.LinearVelocity.LengthSquared() < 0.0025f && g.Body.AngularVelocity.LengthSquared() < 0.01f;
            if (!still && g.Age > MostTumble)
            {
                // Still going after all that: fallen out of the world, or caught on nothing. It goes rather than hang in the air.
                g.Body.QueueFree();
                _gibs.RemoveAt(i);
                continue;
            }
            if (still)
            {
                g.Body.Freeze = true;
                g.Body.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
                g.Resting = true;
            }
        }
    }
}
