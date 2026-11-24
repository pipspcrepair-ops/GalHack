using System.Threading;
using Godot;

namespace Slice.Tests;

/// <summary>
/// Listens to the engine's error log for a body Jolt could not create (its limit reached): a piece without a
/// body has no collision, and nothing may pass through shapes. A test that installs it fails on any.
/// </summary>
public partial class JoltWatch : Logger
{
    static JoltWatch _on;
    static int _failed;

    /// <summary>Bodies the engine failed to create since <see cref="Install"/>.</summary>
    public static int Failed => Volatile.Read(ref _failed);

    public static void Install()
    {
        if (_on != null) return;
        _on = new JoltWatch();
        OS.AddLogger(_on);
    }

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (code.Contains("create underlying Jolt Physics body") || rationale.Contains("create underlying Jolt Physics body")) Interlocked.Increment(ref _failed);
    }

    /// <summary>Collision bodies and the shapes on them in a tree (the live count a test prints before and after a change).</summary>
    public static (int Bodies, int Shapes) Census(Node root)
    {
        int bodies = 0, shapes = 0;
        void Walk(Node n)
        {
            if (n is CollisionObject3D o)
            {
                bodies++;
                foreach (uint owner in o.GetShapeOwners()) shapes += o.ShapeOwnerGetShapeCount(owner);
            }
            foreach (Node c in n.GetChildren()) Walk(c);
        }
        Walk(root);
        return (bodies, shapes);
    }
}
