using System;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>Where a jump puts the structure: the zone, and its place and turn there, at rest in that zone's frame.</summary>
public readonly record struct JumpArrival(SurfaceZone Zone, N.Vector3 Position, N.Quaternion Rotation);

/// <summary>
/// What a jump goes to (an in-system warp's world or a fold jump's star). It starts being made the moment the drive
/// spools, off the main thread where it can; the tunnel lasts until it is ready.
/// </summary>
public interface IJumpDestination
{
    /// <summary>What it is, for the console and the logs.</summary>
    string Name { get; }

    /// <summary>The drive spools: start making it.</summary>
    void Begin(Spaceflight flight);

    /// <summary>One short piece of setting it up on the main thread (well under a frame). True once it can be entered.</summary>
    bool Step(Spaceflight flight);

    /// <summary>Where the structure arrives, worked out the moment it does.</summary>
    JumpArrival Arrival(Spaceflight flight, Vessel carrier);

    /// <summary>It has arrived: what to do with the zone it left (park it as it was, or let it go).</summary>
    void Left(Spaceflight flight, SurfaceZone from);

    /// <summary>The jump was called off before it arrived: what was made for it alone may go.</summary>
    void Dropped(Spaceflight flight);
}

/// <summary>
/// One jump, from the drive's spool to the arrival: shared by the warp drive and the fold drive. <see cref="Spaceflight"/>
/// runs it every physics tick (<see cref="Spaceflight.StartJump"/>).
/// <list type="number">
/// <item><b>Spool.</b> The drive spools on its structure's power (<see cref="GalHack.Items.Bridge.StructurePower.Engage"/>);
/// the destination starts being made. It can be called off (<see cref="Spaceflight.CancelJump"/>); power lost calls it off.</item>
/// <item><b>Tunnel.</b> The drive has burnt its fuel; the structure is held still in the zone it left, the tunnel drawn round
/// it, for at least the trip's time and until the destination is ready: a slower machine gets a longer tunnel, never a freeze.</item>
/// <item><b>Arrive.</b> The record of the structure's new zone is committed, then it and everyone aboard move there in one tick
/// (<see cref="Handover"/>), at rest in that zone's frame.</item>
/// <item><b>Clean up.</b> The destination decides what becomes of the zone left (<see cref="IJumpDestination.Left"/>).</item>
/// </list>
/// </summary>
public sealed class Jump
{
    public enum Stage { Spool, Tunnel, Arriving, Done, Cancelled }

    public readonly Vessel Carrier;

    /// <summary>The drive's block function ("warp-drive", "fold-drive") and the distance its fuel is burnt for (AU, LY).</summary>
    public readonly string Drive;
    public readonly double Distance;

    /// <summary>The least time in the tunnel, s: the trip itself.</summary>
    public readonly double TripSeconds;

    public readonly IJumpDestination To;

    public Stage Now { get; internal set; } = Stage.Spool;

    /// <summary>How far the spool has come, 0 to 1.</summary>
    public double Spooled { get; internal set; }

    /// <summary>Seconds in the tunnel so far, and how long it lasted once it ended.</summary>
    public double InTunnel { get; internal set; }

    /// <summary>The tunnel's share gone, 0 to 1 (over 1 while it waits on the destination).</summary>
    public double TunnelShare => TripSeconds <= 0 ? 1 : InTunnel / TripSeconds;

    /// <summary>The destination is ready to enter.</summary>
    public bool Ready { get; internal set; }

    /// <summary>The longest one tick's setting up of the destination took on the main thread, ms (it must not show).</summary>
    public double LongestStepMs { get; internal set; }

    /// <summary>Why it stopped, if it was called off.</summary>
    public string Trouble { get; internal set; }

    /// <summary>Where it arrived (once it has).</summary>
    public JumpArrival? Arrived { get; internal set; }

    /// <summary>The zone it left.</summary>
    public SurfaceZone From { get; internal set; }

    /// <summary>The fuel items the drive burnt.</summary>
    public int Burnt { get; internal set; }

    /// <summary>What the ledger's audit of the arrival found wrong (empty: nothing lost, doubled or misplaced).</summary>
    public System.Collections.Generic.List<string> Audit { get; internal set; } = new();

    /// <summary>Where the structure is held through the tunnel.</summary>
    internal N.Vector3 HeldAt;
    internal N.Quaternion HeldTurn;

    /// <summary>The drive has spooled and burnt its fuel (StructurePower.Jumped).</summary>
    internal bool Fired;

    internal Action<string, double> OnJumped;

    internal Jump(Vessel carrier, string drive, double distance, double tripSeconds, IJumpDestination to)
    {
        Carrier = carrier;
        Drive = drive;
        Distance = distance;
        TripSeconds = tripSeconds;
        To = to;
    }

    /// <summary>Spooling or in the tunnel: not arrived yet, not called off.</summary>
    public bool Running => Now is Stage.Spool or Stage.Tunnel or Stage.Arriving;

    /// <summary>The structure is held still in the tunnel (or arriving out of it).</summary>
    public bool Holding => Now is Stage.Tunnel or Stage.Arriving;
}
