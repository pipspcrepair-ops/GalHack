using System;

namespace GalHack.Persistence;

/// <summary>Where the store gets the time, so backup schedules can be tested on a simulated clock.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>A clock that only moves when told to.</summary>
public sealed class SimulatedClock : IClock
{
    public SimulatedClock(DateTime startUtc) => UtcNow = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
    public DateTime UtcNow { get; set; }
    public void Advance(TimeSpan by) => UtcNow += by;
}
