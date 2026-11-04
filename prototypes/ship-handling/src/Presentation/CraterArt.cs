using Godot;
using ShipHandling.Physics;

namespace ShipHandling;

/// <summary>
/// Draws the craters the physics module reports: a scorched blast decal, a raised rim of
/// thrown-up rock and a scatter of debris. Visual only; the ground's collision is untouched.
/// </summary>
public partial class CraterArt : Node3D
{
    readonly FlightPhysics _physics;
    static StandardMaterial3D _rimMat, _debrisMat;

    public CraterArt(FlightPhysics physics)
    {
        _physics = physics;
        Name = "Craters";
    }

    public override void _Ready() => _physics.Impact += OnImpact;
    public override void _ExitTree() => _physics.Impact -= OnImpact;

    void OnImpact(ImpactInfo hit)
    {
        if (hit.CraterRadius > 0f) Spawn(hit.Point, hit.Normal, hit.CraterRadius);
    }

    public void Spawn(Vector3 point, Vector3 normal, float radius)
    {
        var crater = new Node3D { Name = "Crater" };
        AddChild(crater);
        crater.GlobalTransform = new Transform3D(VesselArt.AlignY(normal), point);

        crater.AddChild(new Decal
        {
            TextureAlbedo = VesselArt.ScorchTexture(),
            Size = new Vector3(radius * 2.6f, radius, radius * 2.6f),
            AlbedoMix = 1f,
        });

        _rimMat ??= new StandardMaterial3D { AlbedoColor = new Color("6e3418"), Roughness = 1f };
        _debrisMat ??= new StandardMaterial3D { AlbedoColor = new Color("3b2618"), Roughness = 1f };
        crater.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = radius * 0.78f, OuterRadius = radius * 1.12f, Rings = 32, RingSegments = 8 },
            MaterialOverride = _rimMat,
            Scale = new Vector3(1f, 0.32f, 1f),
            Position = new Vector3(0, radius * 0.02f, 0),
        });

        var rng = new RandomNumberGenerator { Seed = (ulong)(point.X * 1000f + point.Z * 7f) };
        int pieces = 6 + (int)radius;
        for (int i = 0; i < pieces; i++)
        {
            float a = rng.Randf() * Mathf.Tau, d = radius * rng.RandfRange(0.9f, 1.9f), s = radius * rng.RandfRange(0.05f, 0.14f);
            crater.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(s, s * 0.6f, s * 0.8f) },
                MaterialOverride = rng.Randf() < 0.5f ? _rimMat : _debrisMat,
                Position = new Vector3(Mathf.Cos(a) * d, s * 0.2f, Mathf.Sin(a) * d),
                Rotation = new Vector3(rng.Randf(), rng.Randf() * 3f, rng.Randf()),
            });
        }
    }
}
