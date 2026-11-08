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

    /// <summary>Its worms' stir and reach, and a dead one sinking.</summary>
    public void Sync(float dt)
    {
        WormMass m = Mass;
        if (m.Dead && _sunk < 1f)
        {
            _sunk = MathF.Min(1f, _sunk + dt * 1.2f);
            _mesh.Scale = new Vector3(1f + 0.15f * _sunk, 1f - 0.7f * _sunk, 1f + 0.15f * _sunk);
        }
        // Limp once dead: the shader's sway is all but stopped.
        float stir = m.Dead ? -0.8f : MathF.Round(m.Stir * 10f) / 10f;
        if (MathF.Abs(stir - _stirShown) > 0.01f)
        {
            _stirShown = stir;
            _mesh.SetInstanceShaderParameter("worm_stir", stir);
        }
        Vector3 dir = m.Dead ? Vector3.Zero : Basis.Inverse() * m.Reach;
        var reach = new Vector4(dir.X, dir.Y, dir.Z, MathF.Max(0f, stir));
        if ((reach - _reachShown).LengthSquared() > 0.003f)
        {
            _reachShown = reach;
            _mesh.SetInstanceShaderParameter("worm_reach", reach);
        }
    }
}
