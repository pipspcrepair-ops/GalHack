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

    /// <summary>Draw a body as one skinned mesh (one draw call) rather than a node per piece. The old way stays for measuring.</summary>
    public static bool Merged = true;

    readonly List<MeshInstance3D> _parts = new();
    Skeleton3D _skeleton;
    MeshInstance3D _body;
    MeshInstance3D _far;
    HumanRig _rig;
    float _hurtShown;

    /// <summary>Nodes drawn close to (tests: one body, not a node per piece).</summary>
    public int DrawnNear => _body != null ? 1 : _parts.Count;

    /// <summary>A body's meshes, worked out ahead (safe on a worker thread): <see cref="Build"/> then only hands them over.</summary>
    public sealed class Ready
    {
        public MeshBuilder.Prepared Body, Far;
    }

    public static Ready Prepare(HumanRig rig) => new() { Body = MeshBuilder.Skinning(rig.Layout.Pieces), Far = MeshBuilder.Plain(HumanBody.Far(rig.Species)) };

    public void Build(HumanRig rig, Ready ready = null)
    {
        _rig = rig;
        Species s = rig.Species;
        ShaderMaterial material = CreatureMaterials.Clothed;
        if (Merged)
        {
            var pieces = rig.Layout.Pieces;
            _skeleton = new Skeleton3D { Name = "Bones" };
            var skin = new Skin();
            for (int i = 0; i < pieces.Count; i++)
            {
                _skeleton.AddBone(pieces[i].Name);
                skin.AddBind(i, Transform3D.Identity);
            }
            AddChild(_skeleton);
            _body = new MeshInstance3D { Mesh = ready != null ? ready.Body.Commit() : Bodies.GetValue(s, _ => MeshBuilder.Skinned(rig.Layout.Pieces)), Skin = skin, MaterialOverride = material, VisibilityRangeEnd = NearRange, VisibilityRangeEndMargin = 2f };
            _skeleton.AddChild(_body);
            _body.Skeleton = new NodePath("..");
        }
        else foreach (Piece piece in rig.Layout.Pieces)
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
            Mesh = ready != null ? ready.Far.Commit() : MeshBuilder.For(FarPiece(s)),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeBegin = NearRange,
            VisibilityRangeBeginMargin = 2f,
            VisibilityRangeEnd = FarRange,
        };
        AddChild(_far);
        // A soft dark blob under the feet: they sit on the ground, and read against it.
        _blob = new MeshInstance3D { Mesh = Blob, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = FarRange * 0.5f };
        AddChild(_blob);
    }

    MeshInstance3D _blob;
    static QuadMesh _blobMesh;

    static QuadMesh Blob => _blobMesh ??= MakeBlob();

    static QuadMesh MakeBlob()
    {
        var g = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(0f, 0f, 0f, 0.42f), new Color(0f, 0f, 0f, 0f) } };
        var tex = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 64, Height = 64 };
        var mat = new StandardMaterial3D { AlbedoTexture = tex, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled };
        return new QuadMesh { Size = new Vector2(0.9f, 0.9f), Orientation = PlaneMesh.OrientationEnum.Y, Material = mat };
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Species, ArrayMesh> Bodies = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Species, Piece> FarPieces = new();
    static Piece FarPiece(Species s) => FarPieces.GetValue(s, HumanBody.Far);

    /// <summary>Close to: every piece where the rig has it. Physics interpolation smooths it on fast screens.</summary>
    public void Sync()
    {
        Transform3D[] pose = _rig.Pose;
        if (_skeleton != null)
        {
            for (int i = 0; i < pose.Length; i++) _skeleton.SetBonePose(i, pose[i]);
            return;
        }
        for (int i = 0; i < _parts.Count; i++) _parts[i].Transform = pose[i];
    }

    /// <summary>From a distance: the one mesh, standing where they are.</summary>
    public void SyncFar(Vector3 feet, float yaw)
    {
        _far.Transform = new Transform3D(new Basis(Vector3.Up, yaw), feet);
        _blob.Transform = new Transform3D(Basis.Identity, feet + Vector3.Up * 0.03f);
    }

    public void Hurt(float amount)
    {
        float hurt = MathF.Round(amount * 10f) / 10f;
        if (MathF.Abs(hurt - _hurtShown) < 0.01f) return;
        _hurtShown = hurt;
        foreach (MeshInstance3D p in _parts) p.SetInstanceShaderParameter("hurt", hurt);
        _body?.SetInstanceShaderParameter("hurt", hurt);
    }
}
