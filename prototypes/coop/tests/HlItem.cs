using System;
using GalHack.Net;
using Godot;

namespace Coop;

/// <summary>A tiny replicated entity for <see cref="HighLevelSteamTest"/>: a zone and a value.</summary>
public partial class HlItem : Node
{
    [Export] public int Zone { get; set; }
    [Export] public int Value { get; set; }

    public static HlItem Create(int zone, Func<int, int> zoneOfPeer)
    {
        var item = new HlItem { Name = "Item", Zone = zone };
        Func<int, bool> filter = zoneOfPeer == null ? null : NetSync.SameZone(() => item.Zone, zoneOfPeer);
        item.AddChild(NetSync.Create("Sync", 0f, filter, SyncProperty.OnChange(nameof(Zone)), SyncProperty.Stream(nameof(Value))));
        return item;
    }
}
