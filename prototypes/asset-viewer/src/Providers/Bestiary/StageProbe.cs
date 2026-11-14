using Godot;

namespace AssetViewer.Providers;

/// <summary>What the Bestiary and People tests ask of a stage: run it, look at its pose, report what is wrong with it.</summary>
public interface IStageProbe
{
    /// <summary>Runs the simulation and presents it for so many seconds, without a frame loop.</summary>
    void Advance(float seconds);
    /// <summary>Empty if the pose is sound (no NaN, nothing sunk through the ground, nothing floating); otherwise what is wrong.</summary>
    string Problems();
    /// <summary>A number that changes when the pose does, so two different animations can be told apart.</summary>
    long PoseSignature();
}

/// <summary>A screenshot aid: an environment variable can run every animation this many seconds ahead before it is first drawn.</summary>
static class FastForward
{
    public static float DebugEvery { get; } = float.TryParse(System.Environment.GetEnvironmentVariable("ASSET_VIEWER_DEBUG"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d) ? d : 0f;
    public static float Seconds { get; } = float.TryParse(System.Environment.GetEnvironmentVariable("ASSET_VIEWER_FASTFORWARD"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s) ? s : 0f;
}
