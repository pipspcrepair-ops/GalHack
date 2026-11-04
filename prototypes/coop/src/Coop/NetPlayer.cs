using System;
using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>
/// A player's replicated state. Its owner sends it to the host (trusted: the host just takes
/// it), and the host shares it with everyone else in the same zone. The owner never gets their
/// own copy back.
/// </summary>
public partial class NetPlayer : Node
{
    [Export] public int PeerId { get; set; }
    [Export] public string PlayerName { get; set; } = "";
    [Export] public int Zone { get; set; }
    [Export] public int Stamp { get; set; }
    [Export] public int Mode { get; set; }
    [Export] public int ShipId { get; set; }
    [Export] public Vector3 Pos { get; set; }
    [Export] public float Yaw { get; set; }
    [Export] public float Pitch { get; set; }
    [Export] public bool Crouch { get; set; }

    public const float Interval = 1f / 30f;

    public readonly SnapshotBuffer<PlayerSnap> Buffer = new();
    public MultiplayerSynchronizer StateSync { get; private set; }

    public static NetPlayer Create(int peer, string name, int zone, Func<int, int> zoneOfPeer)
    {
        var player = new NetPlayer { Name = $"Player{peer}", PeerId = peer, PlayerName = name, Zone = zone };
        Func<int, bool> filter = zoneOfPeer == null ? null : NetSync.SameZone(() => player.Zone, zoneOfPeer, exceptPeer: peer);
        player.StateSync = NetSync.Create("StateSync", Interval, filter,
            SyncProperty.Once(nameof(PeerId)), SyncProperty.Once(nameof(PlayerName)), SyncProperty.OnChange(nameof(Zone)),
            SyncProperty.Stream(nameof(Stamp)), SyncProperty.Stream(nameof(Mode)), SyncProperty.Stream(nameof(ShipId)),
            SyncProperty.Stream(nameof(Pos)), SyncProperty.Stream(nameof(Yaw)), SyncProperty.Stream(nameof(Pitch)),
            SyncProperty.Stream(nameof(Crouch)));
        player.AddChild(player.StateSync);
        return player;
    }

    public override void _Ready()
    {
        if (Multiplayer.IsServer()) return;
        Push();
        StateSync.Synchronized += Push;
    }

    public PlayerSnap Snap => new() { Mode = Mode, ShipId = ShipId, Pos = Pos, Yaw = Yaw, Pitch = Pitch, Crouch = Crouch };

    /// <summary>Host: takes a state report from the player's own client.</summary>
    public void Report(int stamp, int zone, PlayerSnap snap, bool buffer = true)
    {
        Stamp = stamp;
        Zone = zone;
        Mode = snap.Mode;
        ShipId = snap.ShipId;
        Pos = snap.Pos;
        Yaw = snap.Yaw;
        Pitch = snap.Pitch;
        Crouch = snap.Crouch;
        if (buffer) Buffer.Push(stamp / 1000.0, snap, NetClock.Now);
    }

    void Push()
    {
        if (Stamp != 0) Buffer.Push(Stamp / 1000.0, Snap, NetClock.Now);
    }
}
