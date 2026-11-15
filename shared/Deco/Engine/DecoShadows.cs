using System.Collections.Generic;
using Godot;

namespace GalHack.Deco.Engine;

/// <summary>
/// Decorations cast the sun's shadow only out to where it can still be made out: <see cref="NearShadow"/> metres and
/// <see cref="ShadowPerMetre"/> more for each metre of a piece's longest side (a cup to 40 m, a chair to about 75, a
/// wardrobe to 110), switched only as a piece crosses its line. A city's furnished rooms were thousands of casters in
/// every split of the sun's shadow, most of them indoors and far off. A slice of the pieces is looked at each frame.
/// </summary>
public partial class DecoShadows : Node
{
    public const float NearShadow = 40f, ShadowPerMetre = 35f;

    /// <summary>Off: every piece casts as far as the sun's shadows reach (a check measuring the difference).</summary>
    public static bool On = true;

    /// <summary>How many frames pass before every piece's distance is worked out again.</summary>
    const int Sweep = 30;

    static DecoShadows _instance;
    static readonly List<DecoNode> Pieces = new();
    int _frame;

    /// <summary>Pieces casting now, of those in the tree (tests).</summary>
    public static int Casting { get; private set; }
    public static int Count => Pieces.Count;

    /// <summary>How far a piece of this size casts, m.</summary>
    public static float Reach(float longest) => NearShadow + ShadowPerMetre * longest;

    internal static void Join(DecoNode piece)
    {
        if (!On) return;
        piece.ShadowIndex = Pieces.Count;
        Pieces.Add(piece);
        if (piece.MeshInstance is { } m && m.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off) Casting++;
        if (IsInstanceValid(_instance) || piece.GetTree() is not SceneTree tree) return;
        _instance = new DecoShadows { Name = "DecoShadows" };
        tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
    }

    internal static void Leave(DecoNode piece)
    {
        int at = piece.ShadowIndex;
        if (at < 0 || at >= Pieces.Count || !ReferenceEquals(Pieces[at], piece)) return;
        if (piece.MeshInstance is { } m && m.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off) Casting--;
        DecoNode last = Pieces[^1];
        Pieces[at] = last;
        last.ShadowIndex = at;
        Pieces.RemoveAt(Pieces.Count - 1);
        piece.ShadowIndex = -1;
    }

    public override void _Process(double delta)
    {
        int count = Pieces.Count;
        if (count == 0 || GetViewport()?.GetCamera3D() is not Camera3D camera) return;
        Vector3 eye = camera.GlobalPosition;
        int slice = (count + Sweep - 1) / Sweep;
        int from = _frame++ % Sweep * slice;
        for (int i = from; i < from + slice && i < count; i++)
        {
            DecoNode p = Pieces[i];
            MeshInstance3D m = p.MeshInstance;
            if (m == null) continue;
            float reach = p.ShadowReach, d2 = p.GlobalPosition.DistanceSquaredTo(eye);
            bool casting = m.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off;
            // A tenth either side of the line: a piece at its edge doesn't flick on and off.
            if (casting && d2 > reach * reach * 1.21f) { m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; Casting--; }
            else if (!casting && d2 < reach * reach * 0.81f) { m.CastShadow = GeometryInstance3D.ShadowCastingSetting.On; Casting++; }
        }
    }
}
