using System;
using System.Collections.Generic;

namespace GalHack.Persistence;

/// <summary>
/// Thrown by a <see cref="FaultInjector"/> to stand in for the process dying. Nothing in the
/// persistence core catches it or cleans up after it, exactly as nothing would after a real crash.
/// </summary>
public sealed class SimulatedCrash : Exception
{
    public SimulatedCrash(string point) : base($"simulated crash at: {point}") => Point = point;
    public string Point { get; }
}

/// <summary>
/// A hook the store calls at every crash point of every disk write: halfway through writing a
/// file, after it is flushed, after the manifest is replaced, after each old file is deleted.
/// Points are numbered in the order they are reached, so a test can run the same commit once to
/// count them and then crash at each one in turn. The chaos key swaps the crash for a real kill.
/// </summary>
public sealed class FaultInjector
{
    long _reached;

    /// <summary>The point to crash at, counting from 0. -1 (the default) never crashes.</summary>
    public long CrashAt { get; set; } = -1;

    /// <summary>When set, the name of every point reached is added to it, in order.</summary>
    public List<string> Trace { get; set; }

    /// <summary>
    /// What a crash does before the SimulatedCrash is thrown. The chaos key kills the process
    /// here; tests leave it null.
    /// </summary>
    public Action<string> OnCrash { get; set; }

    public long PointsReached => _reached;

    /// <summary>Counts a crash point. True if the process should die here.</summary>
    public bool Due(string point)
    {
        long n = _reached++;
        Trace?.Add(point);
        return n == CrashAt;
    }

    /// <summary>Dies. Never returns.</summary>
    public void Crash(string point)
    {
        OnCrash?.Invoke(point);
        throw new SimulatedCrash(point);
    }

    /// <summary>A crash point between two steps.</summary>
    public void Point(string point)
    {
        if (Due(point)) Crash(point);
    }
}
