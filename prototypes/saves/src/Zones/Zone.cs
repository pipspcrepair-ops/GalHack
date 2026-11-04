using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Saves.Zones;

public enum ZoneState
{
    /// <summary>Generating on a worker thread: terrain, layouts, textures. No nodes yet.</summary>
    Loading,
    /// <summary>Turning that into nodes on the main thread, a few milliseconds per frame.</summary>
    Building,
    /// <summary>Ready: simulating, and drawn if it is the active zone.</summary>
    Live,
}

/// <summary>
/// One live zone: an isolated Godot world (a SubViewport with its own World3D) holding the
/// zone's terrain and a node for each of its records. Every live zone keeps simulating physics;
/// only the active one renders (tests/WorldProbe.cs in walkable-ship proves a hidden world steps).
/// </summary>
public partial class Zone : SubViewport
{
    public string ZoneId { get; init; }
    public ZoneState State { get; internal set; } = ZoneState.Loading;

    /// <summary>Everything in the zone hangs off this.</summary>
    public Node3D Root { get; private set; }

    /// <summary>Seconds left before this empty zone unloads. Reset whenever a player is in it.</summary>
    public double LingerLeft { get; internal set; }

    /// <summary>The node standing for each record in the zone, cabin contents of its ships included.</summary>
    public Dictionary<string, Node> Spawned { get; } = new(StringComparer.Ordinal);

    /// <summary>Records the zone should hold that did not come back when it loaded. Never silently dropped: shown to the host.</summary>
    public List<string> AuditProblems { get; } = new();

    // Timings, for the HUD and the tests.
    public double RequestedAt { get; internal set; }
    public double GeneratedAt { get; internal set; }
    public double ReadyAt { get; internal set; }
    public double GenerateSeconds { get; internal set; }
    public int BuildFrames { get; internal set; }
    public double LongestBuildStepMs { get; internal set; }
    public int BuildStepsTotal { get; internal set; }
    public int BuildStepsDone { get; internal set; }

    internal Task<object> Generating;
    internal Queue<Action> BuildSteps;

    public bool IsReady => State == ZoneState.Live;

    /// <summary>0 to 1, for a loading bar.</summary>
    public float Progress => State switch
    {
        ZoneState.Loading => 0.1f,
        ZoneState.Building => 0.1f + 0.9f * BuildStepsDone / Math.Max(1, BuildStepsTotal),
        _ => 1f,
    };

    public Zone()
    {
        OwnWorld3D = true;
        RenderTargetUpdateMode = UpdateMode.Disabled;
        Size = new Vector2I(2, 2);
        Msaa3D = Msaa.Msaa2X;
        Root = new Node3D { Name = "Root" };
        AddChild(Root);
    }

    public void Register(string recordId, Node node) => Spawned[recordId] = node;

    public void Unregister(string recordId) => Spawned.Remove(recordId);
}
