using System.Collections.Generic;
using Godot;

namespace Weather;

/// <summary>
/// Burning debris in a fire storm. There is no lead-up: nothing falls into view and nothing marks the
/// landing spot. A hit shows only as the fireball and light at the moment it lands, from a small
/// fixed pool, so a long fire storm never allocates.
/// </summary>
public partial class DebrisFx : Node3D
{
    public WeatherFx Fx;

    sealed class Burst
    {
        public MeshInstance3D Fireball;
        public OmniLight3D Light;
        public float Age = 9f;
    }

    readonly List<Burst> _pool = new();
    int _next;

    public override void _Ready()
    {
        var fireMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true,
            AlbedoTexture = Fx.SoftDot, AlbedoColor = new Color(4f, 1.6f, 0.4f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        for (int i = 0; i < 4; i++)
        {
            var burst = new Burst
            {
                Fireball = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(10f, 10f) }, MaterialOverride = fireMaterial, TopLevel = true, Visible = false },
                Light = new OmniLight3D { LightColor = new Color("ff7a2a"), OmniRange = 30f, LightEnergy = 0f, TopLevel = true },
            };
            AddChild(burst.Fireball);
            AddChild(burst.Light);
            _pool.Add(burst);
        }
    }

    public override void _Process(double delta)
    {
        foreach (Burst b in _pool)
        {
            b.Age += (float)delta;
            float fade = Mathf.Clamp(1f - b.Age / 0.8f, 0f, 1f);
            b.Fireball.Visible = fade > 0f;
            b.Fireball.Scale = Vector3.One * (0.5f + b.Age * 2.5f);
            b.Light.LightEnergy = fade * 12f;
        }
    }

    /// <summary>The model says it landed: the fireball starts here and now.</summary>
    public void Impact(Vector3 at)
    {
        Burst b = _pool[_next++ % _pool.Count];
        b.Age = 0f;
        b.Fireball.GlobalPosition = at + Vector3.Up * 2f;
        b.Light.GlobalPosition = at + Vector3.Up * 3f;
    }
}
