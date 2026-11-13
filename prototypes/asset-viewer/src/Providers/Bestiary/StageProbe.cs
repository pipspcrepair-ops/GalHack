using System.Collections.Generic;
using Godot;
using Wildlife;

namespace AssetViewer.Providers;

/// <summary>One foot at one moment, in the stage's own frame: where the rig wants it, where it is drawn, and whether it should be holding the ground.</summary>
public readonly record struct FootMark(Vector3 Target, Vector3 Drawn, bool Planted);

/// <summary>What the Bestiary and People tests ask of a stage: run it, look at its pose, report what is wrong with it.</summary>
public interface IStageProbe
{
    /// <summary>Runs the simulation and presents it for so many seconds, without a frame loop.</summary>
    void Advance(float seconds);
    /// <summary>Empty if the pose is sound (no NaN, nothing sunk through the ground, nothing floating); otherwise what is wrong.</summary>
    string Problems();
    /// <summary>A number that changes when the pose does, so two different animations can be told apart.</summary>
    long PoseSignature();

    // What the filmstrip tool reads. All positions are in the stage's own frame, as drawn.
    /// <summary>The node whose meshes are the subject (not the ground or the marks), for measuring and for the pose.</summary>
    Node3D Subject { get; }
    /// <summary>Where the subject is, which a travelling animation's camera follows (the world slides under a walker, so mostly the origin).</summary>
    Vector3 Anchor { get; }
    /// <summary>How far the ground has been slid from where it stands in the world, or null for a stage with no ground to read travel by.</summary>
    Vector3? GroundShift { get; }
    /// <summary>Adds each foot's mark, in a fixed order, to the list.</summary>
    void Feet(List<FootMark> into);
}

static class FootMarks
{
    /// <summary>The rig's feet, moved by <paramref name="offset"/>. A rig with no separate target draws its target where the foot is.</summary>
    public static void From(Rig rig, Vector3 offset, List<FootMark> into)
    {
        for (int i = 0; i < rig.FootCount; i++)
        {
            Vector3 drawn = rig.FootDrawn(i), target = rig.FootTarget(i);
            into.Add(new FootMark(target == Vector3.Zero ? drawn + offset : target + offset, drawn + offset, rig.FootPlanted(i)));
        }
    }
}

/// <summary>A screenshot aid: an environment variable can run every animation this many seconds ahead before it is first drawn.</summary>
static class FastForward
{
    public static float DebugEvery { get; } = float.TryParse(System.Environment.GetEnvironmentVariable("ASSET_VIEWER_DEBUG"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d) ? d : 0f;
    public static float Seconds { get; } = float.TryParse(System.Environment.GetEnvironmentVariable("ASSET_VIEWER_FASTFORWARD"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s) ? s : 0f;
}
