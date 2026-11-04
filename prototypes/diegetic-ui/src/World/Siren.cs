using Godot;

namespace DiegeticUi.World;

/// <summary>
/// A ceiling beacon: a red dome with a reflector spinning inside it, throwing a red beam
/// around the room. Dark and still until a warning switches it on.
/// </summary>
public partial class Siren : Node3D
{
    static readonly Color Red = new("ff2a2a");

    public bool Active { get; private set; }

    /// <summary>Turns per second. The warning sequence speeds it up as the threat gets close.</summary>
    public float Speed { get; set; } = 0.8f;

    Node3D _spinner;
    SpotLight3D _beam;
    StandardMaterial3D _dome;

    public override void _Ready()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.2f, Height = 0.1f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("23252e"), Roughness = 0.5f },
            Position = new Vector3(0, -0.05f, 0),
        });
        _dome = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.06f, 0.06f, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.2f,
            EmissionEnabled = true,
            Emission = Red,
            EmissionEnergyMultiplier = 0,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.15f, Height = 0.3f, IsHemisphere = true },
            MaterialOverride = _dome,
            RotationDegrees = new Vector3(180, 0, 0),
            Position = new Vector3(0, -0.1f, 0),
        });

        _spinner = new Node3D { Name = "Spinner", Position = new Vector3(0, -0.17f, 0) };
        AddChild(_spinner);
        _spinner.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.02f, 0.1f, 0.12f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("ffd166"), EmissionEnabled = true, Emission = new Color("ffd166"), EmissionEnergyMultiplier = 2 },
        });
        _beam = new SpotLight3D
        {
            LightColor = Red,
            LightEnergy = 9,
            SpotRange = 11,
            SpotAngle = 24,
            SpotAttenuation = 0.6f,
            Visible = false,
            RotationDegrees = new Vector3(-28, 0, 0), // tipped down into the room
        };
        _spinner.AddChild(_beam);
    }

    public void SetActive(bool active)
    {
        Active = active;
        _beam.Visible = active;
        _dome.EmissionEnergyMultiplier = active ? 3 : 0;
    }

    public override void _Process(double delta)
    {
        if (!Active) return;
        _spinner.RotateY((float)delta * Speed * Mathf.Tau);
        // The dome pulses with the beam's sweep, so even a siren facing away reads as live.
        float pulse = 0.5f + 0.5f * Mathf.Sin(_spinner.Rotation.Y * 2);
        _dome.EmissionEnergyMultiplier = 1.5f + 3 * pulse;
    }
}
