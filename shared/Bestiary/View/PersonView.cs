using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Draws one person: their real body at every distance, as creatures are drawn. It is one skinned
/// mesh posed by the rig; its small parts (eyes, buttons, hands, ears) drop out by size on screen
/// through the mesh's levels of detail, while every rig piece keeps its body. A game poses them less
/// often the further off they are (<see cref="PoseEvery"/>) and, with its fade, they dissolve in
/// once built and out over the last <see cref="FadeBand"/> before <see cref="FarRange"/>.
/// </summary>
public partial class PersonView : Node3D
{
    /// <summary>Beyond this a person isn't drawn at all, m.</summary>
    public static float FarRange = 340f; // a game's spawn distance can take it further
    /// <summary>The last stretch before <see cref="FarRange"/> they dissolve out over, with the game's fade, m.</summary>
    public const float FadeBand = 30f;
    /// <summary>How long a body built in sight takes to dissolve in, s.</summary>
    const float ArriveSeconds = 0.6f;

    /// <summary>
    /// The parts that drop with distance, by size (m, see <see cref="Keep"/>): under <see cref="Fine"/>
    /// (eyes, a mouth, bullets) they go first, under <see cref="Small"/> (hands, ears, pouches, the
    /// band on an arm) next. Each goes once it would be under half a pixel.
    /// </summary>
    const float Fine = 0.04f, Small = 0.12f;
    static readonly float[] Edges = { Fine * 2f, Small * 2f };

    /// <summary>Draw a body as one skinned mesh (one draw call) rather than a node per piece. The old way stays for measuring.</summary>
    public static bool Merged = true;

    /// <summary>
    /// Shows part of a body (0..1) through a dissolve, and 1 all of it again: the game's fade, as
    /// <see cref="WildlifeView.Fader"/> is for creatures. Left out, bodies show or go whole.
    /// </summary>
    public static Action<Node, float> Fader;

    readonly List<MeshInstance3D> _parts = new();
    Skeleton3D _skeleton;
    MeshInstance3D _body;
    HumanRig _rig;
    float _hurtShown, _shows = 1f, _arrived = 1f;
    bool _fresh = true;

    /// <summary>Nodes drawn close to (tests: one body, not a node per piece).</summary>
    public int DrawnNear => _body != null ? 1 : _parts.Count;

    /// <summary>How many ticks apart a person this far off is moved and posed: every tick close to, a few times a second far off (as WildlifeView spaces out creatures' rigs).</summary>
    public static int PoseEvery(float distance) => distance < 40f ? 1 : distance < 100f ? 2 : distance < 200f ? 4 : 6;

    /// <summary>A body's mesh, worked out ahead (safe on a worker thread): <see cref="Build"/> then only hands it over.</summary>
    public sealed class Ready
    {
        public MeshBuilder.Prepared Body;
    }

    public static Ready Prepare(HumanRig rig) => new() { Body = Mesh(rig.Layout.Pieces) };

    /// <summary>A body as one skinned mesh whose small parts drop out with distance.</summary>
    public static MeshBuilder.Prepared Mesh(IReadOnlyList<Piece> pieces) => MeshBuilder.Skinning(pieces, Keep, Edges);

    /// <summary>
    /// How many levels of detail a part stays in, by its size (its box): a piece's biggest part
    /// stays in all of them (an arm keeps its arm, a head its head), the rest by the size they show,
    /// the side of a square of their outline but no more than twice their thickness (a strap goes
    /// once it is too thin to see, however long it is).
    /// </summary>
    static int Keep(Vector3 size, bool biggest)
    {
        if (biggest) return Edges.Length;
        float a = size.X, b = size.Y, c = size.Z;
        float longest = MathF.Max(a, MathF.Max(b, c)), shortest = MathF.Min(a, MathF.Min(b, c));
        float middle = a + b + c - longest - shortest;
        float shows = MathF.Min(MathF.Sqrt(longest * middle), 2f * middle);
        return shows >= Small ? 2 : shows >= Fine ? 1 : 0;
    }

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
            _body = new MeshInstance3D { Mesh = ready != null ? ready.Body.Commit() : Bodies.GetValue(s, _ => Mesh(pieces).Commit()), Skin = skin, MaterialOverride = material, VisibilityRangeEnd = FarRange };
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
                VisibilityRangeEnd = FarRange,
            };
            AddChild(mi);
            _parts.Add(mi);
        }
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

    /// <summary>Every piece where the rig has it. Physics interpolation smooths it on fast screens.</summary>
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

    /// <summary>Where they stand: the shadow under their feet. (The body is wherever <see cref="Sync"/> last put it; the name is from when a far mesh stood in for it.)</summary>
    public void SyncFar(Vector3 feet, float yaw)
    {
        _blob.Transform = new Transform3D(Basis.Identity, feet + Vector3.Up * 0.03f);
    }

    /// <summary>
    /// How much of them shows this far off, through the game's fade (<see cref="Fader"/>): a body
    /// built in sight dissolves in over <see cref="ArriveSeconds"/>, and one near <see cref="FarRange"/>
    /// dissolves out by distance. False once none of them shows: nothing need pose them.
    /// </summary>
    /// <summary>A body built in sight, how far off (measurements: the game's pop-in log).</summary>
    public static System.Action<float> Arrives;

    public bool Fade(float distance, float dt)
    {
        if (_fresh)
        {
            _fresh = false;
            _arrived = distance < FarRange ? 0f : 1f;
            if (distance < FarRange) Arrives?.Invoke(distance);
        }
        else if (_arrived < 1f) _arrived = MathF.Min(1f, _arrived + dt / ArriveSeconds);
        float shows = Fader == null ? (distance < FarRange ? 1f : 0f) : MathF.Min(Math.Clamp((FarRange - distance) / FadeBand, 0f, 1f), _arrived);
        shows = MathF.Round(shows * 32f) / 32f;
        if (shows > 0f && shows != _shows) Fader?.Invoke(this, shows);
        _shows = shows;
        return shows > 0f;
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
