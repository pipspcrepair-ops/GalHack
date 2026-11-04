using System;
using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>
/// A ship's replicated state. The host writes it from the real <see cref="Ship"/> every tick;
/// clients buffer it and draw their copy of the ship from the buffer. Godot only spawns it on
/// (and only sends it to) peers in the same zone.
/// </summary>
public partial class NetShip : Node
{
    [Export] public int Id { get; set; }
    [Export] public string ShipName { get; set; } = "";
    [Export] public int Zone { get; set; }
    [Export] public int Stamp { get; set; }
    [Export] public Vector3 Pos { get; set; }
    [Export] public Quaternion Rot { get; set; } = Quaternion.Identity;
    [Export] public Vector3 Vel { get; set; }
    [Export] public Vector3 Spin { get; set; }
    [Export] public int Pilot { get; set; }
    [Export] public bool Gravity { get; set; } = true;
    [Export] public bool Stunt { get; set; }
    [Export] public int CargoStamp { get; set; }
    [Export] public float[] Cargo { get; set; } = Array.Empty<float>();

    public const float MotionInterval = 1f / 30f;
    public const float CargoAwakeInterval = 1f / 20f;
    public const float CargoAsleepInterval = 1f;

    public readonly SnapshotBuffer<ShipSnap> Motion = new();
    public readonly SnapshotBuffer<float[]> CargoBuffer = new();
    public MultiplayerSynchronizer MotionSync { get; private set; }
    public MultiplayerSynchronizer CargoSync { get; private set; }

    public static NetShip Create(int id, string name, int zone, Func<int, int> zoneOfPeer)
    {
        var ship = new NetShip { Name = $"Ship{id}", Id = id, ShipName = name, Zone = zone };
        Func<int, bool> filter = zoneOfPeer == null ? null : NetSync.SameZone(() => ship.Zone, zoneOfPeer);
        ship.MotionSync = NetSync.Create("MotionSync", MotionInterval, filter,
            SyncProperty.Once(nameof(Id)), SyncProperty.Once(nameof(ShipName)), SyncProperty.OnChange(nameof(Zone)),
            SyncProperty.Stream(nameof(Stamp)), SyncProperty.Stream(nameof(Pos)), SyncProperty.Stream(nameof(Rot)),
            SyncProperty.Stream(nameof(Vel)), SyncProperty.Stream(nameof(Spin)),
            SyncProperty.OnChange(nameof(Pilot)), SyncProperty.OnChange(nameof(Gravity)), SyncProperty.OnChange(nameof(Stunt)));
        ship.CargoSync = NetSync.Create("CargoSync", CargoAwakeInterval, filter,
            SyncProperty.Stream(nameof(CargoStamp)), SyncProperty.Stream(nameof(Cargo)));
        ship.AddChild(ship.MotionSync);
        ship.AddChild(ship.CargoSync);
        return ship;
    }

    public override void _Ready()
    {
        if (Multiplayer.IsServer()) return;
        // The spawn carried the first state; then every update goes into the buffers.
        PushMotion();
        PushCargo();
        MotionSync.Synchronized += PushMotion;
        CargoSync.Synchronized += PushCargo;
    }

    public ShipSnap Snap => new() { Pos = Pos, Rot = Rot, Vel = Vel, Spin = Spin };

    void PushMotion() => Motion.Push(Stamp / 1000.0, Snap, NetClock.Now);

    void PushCargo()
    {
        if (Cargo.Length > 0) CargoBuffer.Push(CargoStamp / 1000.0, Cargo, NetClock.Now);
    }
}
