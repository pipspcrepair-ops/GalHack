using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Items.Production;

public enum RestockState
{
    /// <summary>The toggle is off: nothing is queued.</summary>
    Off,
    /// <summary>On, but the structure has no guns.</summary>
    NoWeapons,
    /// <summary>Every ammo is at or above its threshold.</summary>
    Stocked,
    /// <summary>Ammo jobs are queued in the restock lane.</summary>
    Restocking,
    /// <summary>Free materials ran short: it says exactly what it needs.</summary>
    Paused,
    /// <summary>An ammo needs a higher-tier constructor than this one.</summary>
    NeedsTier,
}

/// <summary>
/// A constructor's auto-restock setting: one toggle, and a threshold per ammo type counted in
/// boxes (belts, racks), the same unit storage and the constructor count. When the stock of an
/// ammo the structure's guns fire falls below its threshold, the constructor queues that ammo in
/// its lowest-priority lane until the stock is back up to the threshold, exactly: one run makes
/// one box, so it never overshoots. It never takes materials the player's own jobs have reserved,
/// and never moves or delays a job the player queued.
/// </summary>
public sealed class AmmoRestock
{
    public const int MaxThreshold = 999;

    readonly Dictionary<string, int> _thresholds = new(StringComparer.Ordinal);
    bool _enabled;
    IReadOnlyList<string> _inUse = Array.Empty<string>();

    public int Version { get; private set; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Version++;
        }
    }

    /// <summary>The ammo the structure's guns fire, in a stable order. The structure sets it whenever its weapons change.</summary>
    public IReadOnlyList<string> InUse
    {
        get => _inUse;
        set
        {
            var list = (value ?? Array.Empty<string>()).Distinct().ToList();
            if (list.SequenceEqual(_inUse)) return;
            _inUse = list;
            Version++;
        }
    }

    /// <summary>The threshold in boxes: the player's setting, or the ammo's default from items.json.</summary>
    public int Threshold(string ammo, ItemCatalogue items) =>
        _thresholds.TryGetValue(ammo, out int t) ? t : items.TryGet(ammo, out ItemDef item) ? item.RestockDefault : 0;

    public void SetThreshold(string ammo, int boxes)
    {
        boxes = Math.Clamp(boxes, 0, MaxThreshold);
        if (_thresholds.TryGetValue(ammo, out int old) && old == boxes) return;
        _thresholds[ammo] = boxes;
        Version++;
    }

    // ---- Status, written by the constructor --------------------------------------------------

    public RestockState State { get; private set; } = RestockState.Off;
    /// <summary>When paused: what it needs to finish restocking.</summary>
    public Bill Need { get; private set; } = new();
    /// <summary>One line for the screen, such as "Ammo restock paused: need 12 iron ore".</summary>
    public string Status { get; private set; } = "Ammo restock off";

    internal void SetStatus(RestockState state, Bill need, IReadOnlyList<ItemDef> locked, ItemCatalogue items)
    {
        State = state;
        Need = need;
        Status = state switch
        {
            RestockState.Off => "Ammo restock off",
            RestockState.NoWeapons => "Ammo restock on: no guns aboard",
            RestockState.Stocked => "Ammo stocked",
            RestockState.Restocking => "Restocking ammo",
            RestockState.Paused => "Ammo restock paused: need " + string.Join(", ",
                need.Select(kv => $"{kv.Value} {(items.TryGet(kv.Key, out ItemDef i) ? i.Name.ToLowerInvariant() : kv.Key)}")),
            _ => "Ammo restock paused: " + string.Join(", ",
                locked.Select(i => $"{i.Name.ToLowerInvariant()} needs a {items.Machines.TierName(i.Recipe.Tier)} constructor")),
        };
    }
}
