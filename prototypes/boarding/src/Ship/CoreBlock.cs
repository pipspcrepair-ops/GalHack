using Godot;

namespace Boarding;

/// <summary>
/// The ship's core: a glowing column in the vault. Hold the Hijack Tool on it to take the
/// ship, or shoot it to death and the ship goes derelict. Its glow shifts from Vharn rose
/// to the player's teal as the hijack progresses, and flickers when shot.
/// </summary>
public partial class CoreBlock : Block
{
    MeshInstance3D _crystal;
    OmniLight3D _light;
    StandardMaterial3D _glow;
    float _flicker;

    protected override void BuildVisual()
    {
        var dark = Art.Flat(Palette.Dark, 0.4f, 0.6f);
        AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.9f, 0.5f, 1.9f)), dark, new Vector3(0, -Size.Y * 0.5f + 0.25f, 0)));
        AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.9f, 0.4f, 1.9f)), dark, new Vector3(0, Size.Y * 0.5f - 0.2f, 0)));
        foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.22f, Size.Y, 0.22f)), Art.Flat(Palette.Oxblood, 0.5f, 0.3f),
                new Vector3(corner.X * 0.8f, 0, corner.Y * 0.8f)));

        _glow = new StandardMaterial3D
        {
            AlbedoColor = Palette.VharnGlow,
            EmissionEnabled = true,
            Emission = Palette.VharnGlow,
            EmissionEnergyMultiplier = 3f,
        };
        _crystal = Art.Mesh(Art.BoxMesh(new Vector3(0.8f, Size.Y - 1.1f, 0.8f)), _glow, new Vector3(0, 0.05f, 0), false);
        _crystal.RotationDegrees = new Vector3(0, 45, 0);
        AddChild(_crystal);
        foreach (float y in new[] { -0.7f, 0f, 0.7f })
            AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.3f, 0.12f, 1.3f)), Art.Flat(Palette.Yellow, 0.4f, 0.5f), new Vector3(0, y, 0)));

        _light = new OmniLight3D { LightColor = Palette.VharnGlow, LightEnergy = 2.2f, OmniRange = 7f, Position = Vector3.Up * 0.4f };
        AddChild(_light);
    }

    protected override void BuildShape() =>
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.9f, Size.Y, 1.9f) } });

    /// <summary>0..1 hijack progress; 1 means the core now answers to the player.</summary>
    public void ShowHijack(float progress, bool channelling)
    {
        Color c = Palette.VharnGlow.Lerp(Palette.TealGlow, progress);
        _glow.AlbedoColor = c;
        _glow.Emission = c;
        _light.LightColor = c;
        float pulse = channelling ? 0.6f + 0.4f * Mathf.Sin(Time.GetTicksMsec() * 0.02f) : 1f;
        _glow.EmissionEnergyMultiplier = (3f + progress * 2f) * pulse * (_flicker > 0f ? 0.3f : 1f);
        _light.LightEnergy = 2.2f * pulse;
    }

    public override void _Process(double delta) => _flicker = Mathf.Max(0f, _flicker - (float)delta);

    protected override void OnDamaged(float amount, Vector3 point, Vector3 normal) => _flicker = 0.06f;
}
