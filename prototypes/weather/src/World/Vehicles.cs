using System;
using Godot;
using Weather.Forces;

namespace Weather;

/// <summary>
/// A light hover skiff, moored to a post on the pad. Four hover pads hold it up on springs;
/// nothing grips the ground, so the wind moves it freely and only the mooring line holds it.
/// A tornado snaps the line.
/// </summary>
public partial class HoverVessel : RigidBody3D, IForceBody
{
    const float RideHeight = 1.25f;
    const float SpringPerPad = 2100f;   // N/m
    const float DampPerPad = 380f;      // N per m/s
    const float TetherSlack = 3f;
    const float TetherStiffness = 75f;  // N/m past the slack
    const float TetherHard = 34f;       // beyond this the line goes stiff
    const float GroundEffectDrag = 30f; // N per m/s sideways while hovering

    public Vector3 Anchor;
    public bool Moored = true;

    float IForceBody.Mass => Specs.HoverMass;
    float IForceBody.DragArea => Specs.HoverDragArea;
    Vector3 IForceBody.Position => GlobalPosition;
    Vector3 IForceBody.Velocity => LinearVelocity;
    void IForceBody.ApplyForce(Vector3 force) => ApplyCentralForce(force);
    void IForceBody.ApplyImpulse(Vector3 impulse) => ApplyCentralImpulse(impulse);

    static readonly Vector3[] Pads = { new(-1.1f, 0, -1.9f), new(1.1f, 0, -1.9f), new(-1.1f, 0, 1.9f), new(1.1f, 0, 1.9f) };
    MeshInstance3D _line;
    StandardMaterial3D _padGlow;

    /// <summary>One ray per pad. Nodes, not per-tick queries: the engine updates them and nothing is allocated.</summary>
    readonly RayCast3D[] _rays = new RayCast3D[4];

    public override void _Ready()
    {
        Mass = Specs.HoverMass;
        CollisionLayer = Layers.Vehicles;
        CollisionMask = Layers.World | Layers.Vehicles | Layers.Player;
        AngularDamp = 1.5f;
        LinearDamp = 0.05f;
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = new Vector3(0, -0.2f, 0);
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.8f, 0.9f, 5.6f) }, Position = new Vector3(0, 0.2f, 0) });
        BuildLooks();
        for (int i = 0; i < Pads.Length; i++)
        {
            _rays[i] = new RayCast3D
            {
                Position = Pads[i] + Vector3.Up * 0.5f, TargetPosition = Vector3.Down * 4f,
                CollisionMask = Layers.World, ExcludeParent = true,
            };
            AddChild(_rays[i]);
        }
        _line = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 1f, RadialSegments = 5, Rings = 1 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("1a1a1a"), Roughness = 0.8f },
            TopLevel = true,
        };
        AddChild(_line);
    }

    public override void _PhysicsProcess(double delta)
    {
        bool hovering = false;
        for (int i = 0; i < Pads.Length; i++)
        {
            if (!_rays[i].IsColliding()) continue;
            Vector3 from = GlobalTransform * Pads[i];
            float distance = from.Y - _rays[i].GetCollisionPoint().Y;
            float compression = RideHeight - distance;
            if (compression < -1.2f) continue;
            hovering = true;
            Vector3 padVelocity = LinearVelocity + AngularVelocity.Cross(from - GlobalPosition);
            float push = SpringPerPad * Mathf.Max(compression, -0.3f) - DampPerPad * padVelocity.Y;
            ApplyForce(Vector3.Up * Mathf.Max(push, 0f), from - GlobalPosition);
        }

        // Keep it upright (weakly once airborne, so a tornado can still tumble it).
        Vector3 up = GlobalBasis.Y;
        float uprightK = hovering ? 9000f : 1500f;
        ApplyTorque(up.Cross(Vector3.Up) * uprightK - AngularVelocity * (hovering ? 1200f : 150f));

        Vector3 flat = new(LinearVelocity.X, 0, LinearVelocity.Z);
        if (hovering) ApplyCentralForce(-flat * GroundEffectDrag);

        if (Moored)
        {
            Vector3 toAnchor = Anchor - GlobalPosition;
            toAnchor.Y = 0;
            float stretch = toAnchor.Length() - TetherSlack;
            if (stretch > 0f)
            {
                float k = TetherStiffness + (stretch > TetherHard ? (stretch - TetherHard) * 800f : 0f);
                Vector3 along = toAnchor.Normalized();
                float closing = LinearVelocity.Dot(along);
                ApplyCentralForce(along * (k * stretch - Mathf.Min(closing, 0f) * 150f));
            }
        }
    }

    public override void _Process(double delta)
    {
        _line.Visible = Moored;
        if (!Moored) return;
        Vector3 a = Anchor + Vector3.Up * 1.6f, b = GlobalTransform * new Vector3(0, 0, -2.9f);
        Vector3 mid = (a + b) / 2f;
        float length = a.DistanceTo(b);
        if (length < 0.01f) return;
        // A unit cylinder along Y, stretched and turned to run from the post to the nose.
        Vector3 y = (b - a) / length;
        Vector3 x = y.Cross(Mathf.Abs(y.Y) > 0.95f ? Vector3.Right : Vector3.Up).Normalized();
        Vector3 z = x.Cross(y);
        _line.GlobalTransform = new Transform3D(new Basis(x, y * length, z), mid);
        _padGlow.EmissionEnergyMultiplier = 2.5f + 0.6f * Mathf.Sin(Time.GetTicksMsec() * 0.01f);
    }

    void BuildLooks()
    {
        var yellow = new StandardMaterial3D { AlbedoColor = new Color("f4a100"), Roughness = 0.45f, Metallic = 0.1f };
        var black = new StandardMaterial3D { AlbedoColor = new Color("1b1b22"), Roughness = 0.6f };
        var red = new StandardMaterial3D { AlbedoColor = new Color("c1121f"), Roughness = 0.5f };
        var glass = new StandardMaterial3D { AlbedoColor = new Color("0a9396"), Roughness = 0.08f, Metallic = 0.5f };
        _padGlow = new StandardMaterial3D { AlbedoColor = new Color("5ce1e6"), EmissionEnabled = true, Emission = new Color("5ce1e6"), EmissionEnergyMultiplier = 2.5f };

        Part(new BoxMesh { Size = new Vector3(2.6f, 0.55f, 5.4f) }, new Vector3(0, 0.15f, 0), yellow);
        Part(new PrismMesh { Size = new Vector3(2.6f, 0.9f, 1.6f), LeftToRight = 0.5f }, new Vector3(0, 0.3f, -3.2f), yellow, new Vector3(-90, 0, 0));
        Part(new BoxMesh { Size = new Vector3(1.6f, 0.75f, 2.0f) }, new Vector3(0, 0.75f, -0.6f), glass);
        Part(new BoxMesh { Size = new Vector3(2.64f, 0.14f, 3.2f) }, new Vector3(0, 0.36f, 0.6f), black);
        for (int i = 0; i < 5; i++)
            Part(new BoxMesh { Size = new Vector3(0.18f, 0.16f, 0.5f) }, new Vector3(-1.33f, 0.15f, -1.6f + i * 0.8f), black);
        Part(new BoxMesh { Size = new Vector3(0.25f, 1.1f, 1.2f) }, new Vector3(0, 0.95f, 2.0f), red);
        foreach (float x in new[] { -1.55f, 1.55f })
        {
            Part(new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.42f, Height = 2.2f }, new Vector3(x, 0.2f, 1.4f), black, new Vector3(90, 0, 0));
            Part(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.1f }, new Vector3(x, 0.2f, 2.52f), _padGlow, new Vector3(90, 0, 0));
        }
        foreach (Vector3 pad in Pads)
            Part(new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.5f, Height = 0.12f }, pad + new Vector3(0, -0.15f, 0), _padGlow);
    }

    void Part(Mesh mesh, Vector3 at, Material material, Vector3 rotation = default) =>
        AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at, RotationDegrees = rotation });
}

/// <summary>
/// A heavy rover built from blocks. 6.5 tonnes on big tyres that grip: the same wind that
/// sets the skiff straining at its line barely notices it.
/// </summary>
public partial class Rover : RigidBody3D, IForceBody
{
    /// <summary>Headlights and the light bar, switched on by the weather when visibility drops.</summary>
    public bool LightsOn
    {
        get => _lightsOn;
        set
        {
            if (_lightsOn == value) return;
            _lightsOn = value;
            foreach (SpotLight3D s in _beams) s.Visible = value;
            _lamp.EmissionEnergyMultiplier = value ? 6f : 0.4f;
            _tail.EmissionEnergyMultiplier = value ? 4f : 0.3f;
        }
    }

    bool _lightsOn = true;
    readonly System.Collections.Generic.List<SpotLight3D> _beams = new();
    StandardMaterial3D _lamp, _tail;
    float IForceBody.Mass => Specs.RoverMass;
    float IForceBody.DragArea => Specs.RoverDragArea;
    Vector3 IForceBody.Position => GlobalPosition;
    Vector3 IForceBody.Velocity => LinearVelocity;
    void IForceBody.ApplyForce(Vector3 force) => ApplyCentralForce(force);
    void IForceBody.ApplyImpulse(Vector3 impulse) => ApplyCentralImpulse(impulse);

    public override void _Ready()
    {
        Mass = Specs.RoverMass;
        CollisionLayer = Layers.Vehicles;
        CollisionMask = Layers.World | Layers.Vehicles | Layers.Player;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 1f, Rough = true };
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = new Vector3(0, 0.7f, 0);
        AngularDamp = 0.6f;
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3.6f, 1.9f, 6.4f) }, Position = new Vector3(0, 0.95f, 0) });
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3.0f, 1.6f, 2.6f) }, Position = new Vector3(0, 2.6f, -1.6f) });

        var cream = Mat("e9dfc4");
        var red = Mat("c1121f");
        var dark = Mat("2b2d42");
        var tyre = Mat("16161a");
        var orange = Mat("e85d04");
        var teal = Mat("0a9396");
        var glass = new StandardMaterial3D { AlbedoColor = new Color("142a33"), Roughness = 0.1f, Metallic = 0.4f };
        var lamp = _lamp = new StandardMaterial3D { AlbedoColor = new Color("ffe2b0"), EmissionEnabled = true, Emission = new Color("ffd28a"), EmissionEnergyMultiplier = 6f };
        _tail = new StandardMaterial3D { AlbedoColor = new Color("ff2a1a"), EmissionEnabled = true, Emission = new Color("ff2a1a"), EmissionEnergyMultiplier = 4f };

        Block(new Vector3(3.2f, 0.8f, 6.2f), new Vector3(0, 1.1f, 0), dark);          // chassis
        Block(new Vector3(3.0f, 1.6f, 2.6f), new Vector3(0, 2.3f, -1.6f), cream);     // cab
        Block(new Vector3(3.04f, 0.3f, 2.64f), new Vector3(0, 1.7f, -1.6f), red);     // cab stripe
        Block(new Vector3(2.6f, 0.7f, 0.1f), new Vector3(0, 2.55f, -2.92f), glass);   // windscreen
        Block(new Vector3(1.4f, 1.2f, 1.4f), new Vector3(-0.75f, 2.1f, 1.3f), orange); // cargo blocks
        Block(new Vector3(1.4f, 1.2f, 1.4f), new Vector3(0.75f, 2.1f, 1.3f), teal);
        Block(new Vector3(1.4f, 1.0f, 1.4f), new Vector3(-0.75f, 2.0f, 2.75f), cream);
        Block(new Vector3(1.4f, 1.6f, 1.4f), new Vector3(0.75f, 2.3f, 2.75f), red);
        Block(new Vector3(2.4f, 0.25f, 0.4f), new Vector3(0, 3.25f, -2.4f), dark);    // light bar
        for (int i = 0; i < 4; i++) Block(new Vector3(0.36f, 0.2f, 0.12f), new Vector3(-0.9f + i * 0.6f, 3.25f, -2.62f), lamp);
        foreach (float x in new[] { -1.15f, 1.15f })
        {
            Block(new Vector3(0.5f, 0.3f, 0.1f), new Vector3(x, 1.3f, -3.12f), lamp);   // headlights
            Block(new Vector3(0.4f, 0.25f, 0.1f), new Vector3(x, 1.3f, 3.12f), _tail);  // tail lights
            var beam = new SpotLight3D
            {
                LightColor = new Color("ffe2b0"), LightEnergy = 6f, SpotRange = 40f, SpotAngle = 28f, SpotAttenuation = 0.8f,
                Position = new Vector3(x, 1.3f, -3.2f), ShadowEnabled = false,
            };
            beam.RotationDegrees = new Vector3(-6f, 0f, 0f);
            AddChild(beam);
            _beams.Add(beam);
        }
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.05f, Height = 2.6f, RadialSegments = 6 },
            MaterialOverride = dark, Position = new Vector3(1.2f, 4.3f, -0.6f),
        });
        foreach (float z in new[] { -2.1f, 0f, 2.1f })
        foreach (float x in new[] { -1.75f, 1.75f })
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.85f, BottomRadius = 0.85f, Height = 0.6f, RadialSegments = 16 },
                MaterialOverride = tyre, Position = new Vector3(x, 0.85f, z), RotationDegrees = new Vector3(0, 0, 90),
            });
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.4f, Height = 0.64f, RadialSegments = 10 },
                MaterialOverride = orange, Position = new Vector3(x, 0.85f, z), RotationDegrees = new Vector3(0, 0, 90),
            });
        }
    }

    void Block(Vector3 size, Vector3 at, Material material) =>
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = at });

    static StandardMaterial3D Mat(string hex) => new() { AlbedoColor = new Color(hex), Roughness = 0.7f };
}
