using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Draws one person: a mesh per rig piece close to, one cheap mesh from a distance. The two swap
/// by the renderer's own visibility ranges, so a crowd costs nothing on the CPU to change over:
/// beyond <see cref="NearRange"/> only the far mesh shows, and a game need only move that.
/// </summary>
public partial class PersonView : Node3D
{
    /// <summary>The whole figure, posed; the one cheap mesh takes over here, m.</summary>
    public const float NearRange = 42f;
    /// <summary>Beyond this a person isn't drawn at all, m.</summary>
    public const float FarRange = 340f;

    readonly List<MeshInstance3D> _parts = new();
    MeshInstance3D _far;
    HumanRig _rig;
    float _hurtShown;

    public void Build(HumanRig rig)
    {
        _rig = rig;
        Species s = rig.Species;
        ShaderMaterial material = CreatureMaterials.Clothed;
        foreach (Piece piece in rig.Layout.Pieces)
        {
            var mi = new MeshInstance3D
            {
                Mesh = MeshBuilder.For(piece),
                MaterialOverride = material,
                CastShadow = piece.Shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = NearRange,
                VisibilityRangeEndMargin = 2f,
            };
            AddChild(mi);
            _parts.Add(mi);
        }
        _far = new MeshInstance3D
        {
            Mesh = MeshBuilder.For(FarPiece(s)),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeBegin = NearRange,
            VisibilityRangeBeginMargin = 2f,
            VisibilityRangeEnd = FarRange,
        };
        AddChild(_far);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Species, Piece> FarPieces = new();
    static Piece FarPiece(Species s) => FarPieces.GetValue(s, HumanBody.Far);

    /// <summary>Close to: every piece where the rig has it. Physics interpolation smooths it on fast screens.</summary>
    public void Sync()
    {
        Transform3D[] pose = _rig.Pose;
        for (int i = 0; i < _parts.Count; i++) _parts[i].Transform = pose[i];
    }

    /// <summary>From a distance: the one mesh, standing where they are.</summary>
    public void SyncFar(Vector3 feet, float yaw) => _far.Transform = new Transform3D(new Basis(Vector3.Up, yaw), feet);

    public void Hurt(float amount)
    {
        float hurt = MathF.Round(amount * 10f) / 10f;
        if (MathF.Abs(hurt - _hurtShown) < 0.01f) return;
        _hurtShown = hurt;
        foreach (MeshInstance3D p in _parts) p.SetInstanceShaderParameter("hurt", hurt);
    }
}
