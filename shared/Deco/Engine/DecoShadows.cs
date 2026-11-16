using System.Collections.Generic;
using Godot;

namespace GalHack.Deco.Engine;

/// <summary>
/// Small things cast the sun's shadow only out to where it can still be made out: <see cref="NearShadow"/> metres and
/// <see cref="ShadowPerMetre"/> more for each metre of a thing's longest side (a cup to 40 m, a chair to about 75, a
/// wardrobe to 110), switched only as it crosses its line. A city's furnished rooms were thousands of casters in every
/// split of the sun's shadow, most of them indoors and far off; the start bunker's fittings, underground, 150 more.
/// Decorations join as they enter the tree (<see cref="DecoNode"/>), anything else by <see cref="TrackAll"/>. A slice of
/// them is looked at each frame.
/// </summary>
public partial class DecoShadows : Node
{
    public const float NearShadow = 40f, ShadowPerMetre = 35f;

    /// <summary>Off: everything casts as far as the sun's shadows reach (a check measuring the difference).</summary>
    public static bool On = true;

    /// <summary>How many frames pass before every caster's distance is worked out again.</summary>
    const int Sweep = 30;

    static DecoShadows _instance;
    static readonly List<(GeometryInstance3D G, float Reach)> Casters = new();
    static readonly Dictionary<GeometryInstance3D, int> Index = new();
    int _frame;

    /// <summary>Casting now, of those in the tree (tests).</summary>
    public static int Casting { get; private set; }
    public static int Count => Casters.Count;

    /// <summary>How far a thing whose longest side is this casts, m.</summary>
    public static float Reach(float longest) => NearShadow + ShadowPerMetre * longest;

    /// <summary>A thing casting the sun's shadow only out to its reach, from now until it is untracked.</summary>
    public static void Track(GeometryInstance3D g, float longest)
    {
        if (!On || g == null || Index.ContainsKey(g)) return;
        Index[g] = Casters.Count;
        Casters.Add((g, Reach(longest)));
        if (g.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off) Casting++;
        if (!IsInstanceValid(_instance) && Godot.Engine.GetMainLoop() is SceneTree tree)
        {
            _instance = new DecoShadows { Name = "DecoShadows" };
            tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
        }
    }

    public static void Untrack(GeometryInstance3D g)
    {
        if (g == null || !Index.Remove(g, out int at)) return;
        if (g.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off) Casting--;
        var last = Casters[^1];
        Casters[at] = last;
        if (!ReferenceEquals(last.G, g)) Index[last.G] = at;
        Casters.RemoveAt(Casters.Count - 1);
    }

    /// <summary>Every shadow-casting mesh under a node, each by its own size, for as long as it is in the tree.</summary>
    public static void TrackAll(Node root)
    {
        foreach (Node n in root.FindChildren("*", "GeometryInstance3D", true, false))
        {
            if (n is not GeometryInstance3D g || g.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off || !g.IsInsideTree()) continue;
            Track(g, g.GetAabb().GetLongestAxisSize() * g.GlobalBasis.Scale.X);
            g.TreeExiting += () => Untrack(g);
        }
    }

    public override void _Process(double delta)
    {
        int count = Casters.Count;
        if (count == 0 || GetViewport()?.GetCamera3D() is not Camera3D camera) return;
        Vector3 eye = camera.GlobalPosition;
        int slice = (count + Sweep - 1) / Sweep;
        int from = _frame++ % Sweep * slice;
        for (int i = from; i < from + slice && i < count; i++)
        {
            var (g, reach) = Casters[i];
            float d2 = g.GlobalPosition.DistanceSquaredTo(eye);
            bool casting = g.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off;
            // A tenth either side of the line: a thing at its edge doesn't flick on and off.
            if (casting && d2 > reach * reach * 1.21f) { g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; Casting--; }
            else if (!casting && d2 < reach * reach * 0.81f) { g.CastShadow = GeometryInstance3D.ShadowCastingSetting.On; Casting++; }
        }
    }
}
