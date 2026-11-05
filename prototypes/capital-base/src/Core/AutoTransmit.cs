using System;

namespace CapitalBase.Core;

/// <summary>Where a vessel's auto-transmit link stands, as its console shows it.</summary>
public enum LinkState
{
    /// <summary>The vessel's auto-transmit is switched off: what it farms stays aboard.</summary>
    Off,

    /// <summary>No structure is flagged as Home.</summary>
    NoHome,

    /// <summary>Within range of Home and Home has room: farmed items go straight there.</summary>
    InRange,

    /// <summary>Too far from Home: farmed items stay aboard until it is back in range.</summary>
    OutOfRange,

    /// <summary>In range but Home's storage is full: items stay aboard until Home frees room.</summary>
    HomeFull,
}

/// <summary>
/// The one Home. A player flags exactly one base or capital ship as Home; it is one field, so a new
/// flag replaces the old one and there can never be two.
/// </summary>
public sealed class HomeFlag
{
    public string HomeId { get; private set; }

    public bool IsHome(string id) => id != null && id == HomeId;

    /// <summary>Flags a structure as Home. Returns the one that stopped being Home, or null.</summary>
    public string Flag(string id)
    {
        string old = HomeId;
        HomeId = id;
        return old == id ? null : old;
    }

    public void Clear() => HomeId = null;
}

/// <summary>What one pass of a vessel's transmitter found and did.</summary>
public readonly record struct LinkReport(LinkState State, float Distance, double HomeFreeSu, int Holding, int Moved);

/// <summary>
/// Auto-transmit to Home, decided here and nowhere else.
/// - <b>Radius:</b> a data value (1 km): measured from the vessel to Home's core, wherever Home is
///   now, so a capital ship flying as Home works the same as a base.
/// - <b>Straight to Home:</b> a fresh haul goes into Home's storage while in range, as much as fits.
/// - <b>Part-full:</b> whole units move in ordinal item order until the next would not fit; the rest
///   stays in the vessel's cargo. Nothing is ever deleted or dropped.
/// - <b>Catching up:</b> every interval, whatever the vessel holds goes across the same way, so it
///   empties by itself once it is back in range or Home frees room.
/// </summary>
public static class AutoTransmit
{
    /// <summary>The link's state, from the facts the world measured.</summary>
    public static LinkState Evaluate(bool on, bool hasHome, float distance, float radius, double homeFreeSu, ItemCatalog items, Cargo holding)
    {
        if (!on) return LinkState.Off;
        if (!hasHome) return LinkState.NoHome;
        if (!(distance <= radius)) return LinkState.OutOfRange;
        return Room(items, holding, homeFreeSu) ? LinkState.InRange : LinkState.HomeFull;
    }

    /// <summary>
    /// Room for at least one more unit at Home: of something the vessel holds, or (holding nothing)
    /// of the largest item there is.
    /// </summary>
    static bool Room(ItemCatalog items, Cargo holding, double homeFreeSu)
    {
        if (holding == null || holding.Total == 0) return homeFreeSu + 1e-9 >= items.LargestUnit;
        foreach (string item in holding.Stacks.Keys)
            if (Cargo.Fits(item, items, homeFreeSu) > 0) return true;
        return false;
    }

    /// <summary>
    /// One pass of the transmitter: if the link is up, moves whatever fits from the vessel's cargo
    /// into Home's. Returns what it found and how many units went across.
    /// </summary>
    public static LinkReport Pump(bool on, bool hasHome, float distance, float radius, Cargo vessel, Cargo home, double homeCapacity, ItemCatalog items)
    {
        double free = home == null ? 0.0 : home.FreeSu(items, homeCapacity);
        LinkState state = Evaluate(on, hasHome, distance, radius, free, items, vessel);
        int moved = 0;
        if (state == LinkState.InRange && vessel.Total > 0)
        {
            moved = Cargo.MoveWhatFits(vessel, home, homeCapacity, items);
            free = home.FreeSu(items, homeCapacity);
            state = Evaluate(on, hasHome, distance, radius, free, items, vessel);
        }
        return new LinkReport(state, distance, free, vessel.Total, moved);
    }

    /// <summary>The console line for a link, the same wherever it is shown.</summary>
    public static string Describe(LinkReport r, string homeName, float radius) => r.State switch
    {
        LinkState.Off => "AUTO-TX OFF: what you dig stays aboard",
        LinkState.NoHome => "AUTO-TX: NO HOME FLAGGED",
        LinkState.InRange => $"AUTO-TX IN RANGE: Home {homeName} {r.Distance:F0} m of {radius:F0} m, {r.HomeFreeSu:F0} SU free" +
                             (r.Holding > 0 ? $", sending {r.Holding}" : ""),
        LinkState.OutOfRange => $"AUTO-TX OUT OF RANGE: Home {homeName} {r.Distance:F0} m (range {radius:F0} m), holding {r.Holding}",
        LinkState.HomeFull => $"AUTO-TX HOME FULL: {homeName} has {r.HomeFreeSu:F0} SU free, holding {r.Holding}",
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };
}
