using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Draws one worm mass: its generated mesh (one draw call) lying on its surface, the worms swaying
/// in the shader, stirred and reaching at whatever moves near (<see cref="WormMasses.Step"/>). A
/// dead one sinks and goes limp, and stays where it was.
/// </summary>
public partial class WormMassView : Node3D
{
    public WormMass Mass { get; private set; }
    MeshInstance3D _mesh;
    float _sunk, _stirShown = -1f;
    Vector4 _reachShown = new(9f, 9f, 9f, 9f);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WormMass, ArrayMesh> Meshes = new();
    static StandardMaterial3D _scorchMaterial;

    /// <summary>What a burned-out mass leaves: a dark scorch on the surface it lay on, a little wider than it was.</summary>
    void Scorch()
    {
        if (_scorch != null) return;
        _scorchMaterial ??= MakeScorch();
        float across = Mass.Size * 1.5f;
        _scorch = new MeshInstance3D { Name = "Scorch", Mesh = new QuadMesh { Size = new Vector2(across, across), Orientation = PlaneMesh.OrientationEnum.Y, Material = _scorchMaterial }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = Vector3.Up * 0.02f, VisibilityRangeEnd = 70f };
        AddChild(_scorch);
    }

    static StandardMaterial3D MakeScorch()
    {
        var g = new Gradient { Offsets = new[] { 0f, 0.55f, 1f }, Colors = new[] { new Color(0.04f, 0.035f, 0.03f, 0.92f), new Color(0.07f, 0.055f, 0.045f, 0.7f), new Color(0.08f, 0.06f, 0.05f, 0f) } };
        var tex = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 64, Height = 64 };
        return new StandardMaterial3D { AlbedoTexture = tex, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 1f, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled };
    }

    /// <summary>Burned away to its scorch (tests).</summary>
    public bool Scorched => _scorch != null;

    public void Build(WormMass m)
    {
        Mass = m;
        Name = "WormMass";
        var material = CreatureMaterials.Plain(0f);
        material.SetShaderParameter("worm_scale", m.Size * 0.16f);
        material.SetShaderParameter("roughness_value", 0.4f);
        material.SetShaderParameter("detail_freq", 40f);
        material.SetShaderParameter("detail_strength", 0.12f);
        _mesh = new MeshInstance3D { Mesh = Meshes.GetValue(m, mass => MeshBuilder.For(WormMasses.Build(mass))), MaterialOverride = material, VisibilityRangeEnd = 70f };
        AddChild(_mesh);
        Transform = new Transform3D(Geo.AlongY(m.Normal), m.Position);
        Sync(0f);
    }

    MeshInstance3D _scorch;
    float _hurtShown = -1f;

    /// <summary>Its worms' stir and reach, a burning one glowing and shrivelling, a dead one gone to a scorch.</summary>
    public void Sync(float dt)
    {
        WormMass m = Mass;
        if (m.Dead && _sunk < 1f)
        {
            _sunk = MathF.Min(1f, _sunk + dt * 1.2f);
            float left = m.BurntOut ? 1f - _sunk : 1f;
            _mesh.Scale = new Vector3(1f + 0.15f * _sunk, MathF.Max(0.01f, 1f - 0.7f * _sunk), 1f + 0.15f * _sunk) * MathF.Max(0.01f, left);
            if (m.BurntOut && _sunk >= 1f) { _mesh.Visible = false; Scorch(); }
        }
        else if (!m.Dead && m.Burning > 0f)
        {
            // Alight: it flickers hot and shrivels as it burns down.
            float k = Math.Clamp(m.Burning / WormMass.BurnSeconds, 0f, 1f);
            _mesh.Scale = Vector3.One * (1f - 0.45f * k);
        }
        float hurt = !m.Dead && m.Burning > 0f ? MathF.Round((0.45f + 0.35f * MathF.Sin(m.Burning * 23f)) * 10f) / 10f : 0f;
        if (MathF.Abs(hurt - _hurtShown) > 0.01f)
        {
            _hurtShown = hurt;
            _mesh.SetInstanceShaderParameter("hurt", hurt);
        }
        // Limp once dead: the shader's sway is all but stopped.
        // Close to, eager: faster, and stretching further toward what is near (calm, just their own sway).
        float stir = m.Dead ? -0.8f : MathF.Round((m.Stir + 0.9f * m.Eager) * 10f) / 10f;
        if (MathF.Abs(stir - _stirShown) > 0.01f)
        {
            _stirShown = stir;
            _mesh.SetInstanceShaderParameter("worm_stir", stir);
        }
        Vector3 dir = m.Dead ? Vector3.Zero : Basis.Inverse() * m.Reach;
        var reach = new Vector4(dir.X, dir.Y, dir.Z, MathF.Max(0f, stir) * (1f + 1.4f * m.Eager));
        if ((reach - _reachShown).LengthSquared() > 0.003f)
        {
            _reachShown = reach;
            _mesh.SetInstanceShaderParameter("worm_reach", reach);
        }
    }
}
