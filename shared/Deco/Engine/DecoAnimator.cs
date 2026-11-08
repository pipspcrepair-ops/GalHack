using System.Collections.Generic;
using Godot;

namespace GalHack.Deco.Engine;

/// <summary>
/// Moves every animated decoration (a flickering screen, a blinking status light, an alien pulse, sparks)
/// from one place, and only the ones near the eye. A furnished place can hold hundreds of lit pieces,
/// and a node of its own costs a call into the game's code every frame wherever it stands: over a
/// settlement that was several milliseconds of every frame for lights nobody could see. Here a piece
/// within <see cref="NearRange"/> of the camera moves every frame, one within <see cref="MidRange"/> a
/// few times a second (a blink or a pulse that slow reads the same), and a farther one holds its last
/// look until the eye comes back. A piece asks to be moved by joining the tree (see <see cref="DecoNode"/>).
/// </summary>
public partial class DecoAnimator : Node
{
    /// <summary>Metres from the camera within which a piece animates every frame.</summary>
    public const float NearRange = 45f;

    /// <summary>Metres from the camera within which a piece animates every <see cref="MidEvery"/> frames.</summary>
    public const float MidRange = 140f;

    public const int MidEvery = 4;

    /// <summary>How many frames pass before every piece's distance is worked out again (a slice of the pieces each frame).</summary>
    const int Sweep = 20;

    static DecoAnimator _instance;
    static readonly List<DecoNode> Pieces = new();

    /// <summary>Animated pieces in the tree, and how many of them move every frame and a few times a second right now.</summary>
    public static int Count => Pieces.Count;
    public static int NearCount { get; private set; }
    public static int MidCount { get; private set; }

    int _frame;

    /// <summary>Called by a piece as it joins the tree: the first one brings the animator into being.</summary>
    internal static void Join(DecoNode piece)
    {
        piece.AnimIndex = Pieces.Count;
        piece.AnimBand = 0; // near until its distance is known: new pieces appear animated
        Pieces.Add(piece);
        if (IsInstanceValid(_instance) || piece.GetTree() is not SceneTree tree) return;
        _instance = new DecoAnimator { Name = "DecoAnimator" };
        tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
    }

    /// <summary>Called by a piece as it leaves the tree.</summary>
    internal static void Leave(DecoNode piece)
    {
        int at = piece.AnimIndex;
        if (at < 0 || at >= Pieces.Count || !ReferenceEquals(Pieces[at], piece)) return;
        DecoNode last = Pieces[^1];
        Pieces[at] = last;
        last.AnimIndex = at;
        Pieces.RemoveAt(Pieces.Count - 1);
        piece.AnimIndex = -1;
    }

    public override void _Process(double delta)
    {
        _frame++;
        int count = Pieces.Count;
        if (count == 0)
        {
            NearCount = MidCount = 0;
            return;
        }
        Camera3D camera = GetViewport()?.GetCamera3D();
        bool known = camera != null;
        Vector3 eye = known ? camera.GlobalPosition : Vector3.Zero;

        // A slice of the pieces each frame has its distance worked out again; the rest keep the band they were in.
        if (known)
        {
            int slice = (count + Sweep - 1) / Sweep;
            int from = _frame % Sweep * slice;
            for (int i = from; i < from + slice && i < count; i++)
            {
                DecoNode p = Pieces[i];
                float d = p.GlobalPosition.DistanceSquaredTo(eye);
                p.AnimBand = d <= NearRange * NearRange ? (byte)0 : d <= MidRange * MidRange ? (byte)1 : (byte)2;
            }
        }

        int near = 0, mid = 0;
        float dt = (float)delta;
        for (int i = 0; i < count; i++)
        {
            DecoNode p = Pieces[i];
            p.AnimOwed += dt;
            if (p.AnimBand == 0) near++;
            else if (p.AnimBand == 1) mid++;
            if (p.AnimBand == 2 || (p.AnimBand == 1 && (_frame + i) % MidEvery != 0)) continue;
            float owed = p.AnimOwed;
            p.AnimOwed = 0f;
            p.Animate(owed);
        }
        NearCount = near;
        MidCount = mid;
    }
}
