using System;
using Godot;

namespace GalHack.Net;

/// <summary>One property in a <see cref="NetSync"/> synchronizer.</summary>
public readonly record struct SyncProperty(string Name, bool Spawn, SceneReplicationConfig.ReplicationMode Mode)
{
    /// <summary>Sent with the spawn only.</summary>
    public static SyncProperty Once(string name) => new(name, true, SceneReplicationConfig.ReplicationMode.Never);

    /// <summary>Streamed unreliably every replication interval (positions, motion).</summary>
    public static SyncProperty Stream(string name) => new(name, true, SceneReplicationConfig.ReplicationMode.Always);

    /// <summary>Sent reliably whenever it changes (flags, IDs, the zone).</summary>
    public static SyncProperty OnChange(string name) => new(name, true, SceneReplicationConfig.ReplicationMode.OnChange);
}

/// <summary>Builds MultiplayerSynchronizers in code, with zone visibility.</summary>
public static class NetSync
{
    /// <summary>
    /// A synchronizer for its parent node. When <paramref name="visibleTo"/> is given (on the
    /// server), it decides per peer whether that peer gets this node at all: Godot only spawns
    /// a node on peers that can see one of its synchronizers, despawns it when they can't any
    /// more, and only sends them its updates while they can.
    /// </summary>
    public static MultiplayerSynchronizer Create(string name, float interval, Func<int, bool> visibleTo, params SyncProperty[] properties)
    {
        var config = new SceneReplicationConfig();
        foreach (SyncProperty p in properties)
        {
            var path = new NodePath(".:" + p.Name);
            config.AddProperty(path);
            config.PropertySetSpawn(path, p.Spawn);
            config.PropertySetReplicationMode(path, p.Mode);
        }
        var sync = new MultiplayerSynchronizer
        {
            Name = name,
            RootPath = new NodePath(".."),
            ReplicationConfig = config,
            ReplicationInterval = interval,
            DeltaInterval = 0.05f,
        };
        if (visibleTo != null) sync.AddVisibilityFilter(Callable.From(visibleTo));
        return sync;
    }

    /// <summary>A zone filter: a peer sees the entity only while both are in the same zone.</summary>
    public static Func<int, bool> SameZone(Func<int> entityZone, Func<int, int> zoneOfPeer, int exceptPeer = 0) =>
        peer => peer != exceptPeer && zoneOfPeer(peer) == entityZone();
}
