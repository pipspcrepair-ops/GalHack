using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Invasions.Director;

public enum EnemyBaseKind { Outpost, Garrison, Fortress }

public enum EnemyBaseState { Active, Destroyed, Hijacked }

/// <summary>Where an invasion comes from: one of the enemy base kinds, or a ship from further away.</summary>
public enum SourceKind { Outpost, Garrison, Fortress, Ship }

public enum InvasionPhase
{
    /// <summary>Announced and counting down through the warnings.</summary>
    Incoming,
    /// <summary>The force has arrived and is fighting toward the core.</summary>
    Assault,
    /// <summary>The invaders are leaving (core destroyed, or nobody home). Sirens sound until they're gone.</summary>
    Withdrawing,
    Ended,
}

public enum InvasionOutcome { None, Victory, CoreDestroyed, CalledOff, Abandoned }

/// <summary>A Vharn base on the map. Destroying or hijacking it stops the invasions it sends.</summary>
public sealed class EnemyBase
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public EnemyBaseKind Kind { get; init; }
    public GroundPos Position { get; init; }
    public EnemyBaseState State { get; internal set; } = EnemyBaseState.Active;

    public bool CanSendInvasions => State == EnemyBaseState.Active;
}

/// <summary>What the scaling formula looks at: stored goods value, size and defences.</summary>
public readonly record struct BaseStats(double StoredValue, int Blocks, int Turrets);

public sealed class InvasionSource
{
    public SourceKind Kind { get; init; }
    /// <summary>The enemy base sending the invasion, or null for a ship raid.</summary>
    public string EnemyBaseId { get; init; }
    public string Name { get; init; } = "";
    public GroundPos Position { get; init; }
    /// <summary>Compass bearing from the player base toward the source: the direction the invaders come from.</summary>
    public double Bearing { get; init; }
    public double Distance { get; init; }

    public string Compass => GroundPos.Compass(Bearing);
    public int BearingDegrees => (int)System.Math.Round(Bearing) % 360;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Kind} '{Name}' bearing {BearingDegrees:000} {Compass} at {Distance:F0} m");
}

/// <summary>One crewed carrier and the troops it delivers.</summary>
public sealed class VehiclePlan
{
    public int Index { get; init; }
    public string Type { get; init; } = "";
    public string Label { get; init; } = "";
    public bool Flies { get; init; }
    public double Speed { get; init; }
    public int Gunners { get; init; }
    public List<string> Troops { get; } = new();
    /// <summary>Sideways offset from the approach line, metres, so vehicles come in abreast.</summary>
    public double Offset { get; set; }
    /// <summary>Seconds after arrival this vehicle appears, so they don't move in lockstep.</summary>
    public double Delay { get; set; }

    /// <summary>Driver or pilot, plus gunners.</summary>
    public int Crew => 1 + Gunners;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Type}#{Index} offset {Offset:F1} delay {Delay:F2} crew {Crew} troops [{string.Join(",", Troops)}]");
}

/// <summary>The force composed for one invasion, from the scaling data.</summary>
public sealed class ForcePlan
{
    public double Threat { get; init; }
    public double PlayerMultiplier { get; init; }
    public int PlayersPresent { get; init; }
    public int Troops { get; init; }
    public string Tier { get; init; } = "";
    public List<VehiclePlan> Vehicles { get; } = new();

    public int Crew => Vehicles.Sum(v => v.Crew);
    /// <summary>Everyone who has to be down or gone before the sirens stop.</summary>
    public int TotalInvaders => Troops + Crew;

    /// <summary>"2 APCs and 1 dropship".</summary>
    public string VehicleSummary(InvasionData data)
    {
        var parts = Vehicles.GroupBy(v => v.Type).OrderBy(g => g.Key, System.StringComparer.Ordinal)
            .Select(g =>
            {
                string label = data.Vehicles.TryGetValue(g.Key, out var t) ? t.Label : g.Key;
                return $"{g.Count()} {label}{(g.Count() == 1 ? "" : "s")}";
            }).ToList();
        return parts.Count switch
        {
            0 => "no vehicles",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Tier}: {Troops} troops, threat {Threat:F3} x{PlayerMultiplier:F2} ({PlayersPresent}p); ") +
        string.Join("; ", Vehicles);
}

/// <summary>A player base as the director sees it.</summary>
public sealed class PlayerBase
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public GroundPos Position { get; init; }
    public int PlayersPresent { get; internal set; }
    public BaseStats Stats { get; internal set; }
    public bool CoreAlive { get; internal set; } = true;

    /// <summary>The invasion currently incoming, assaulting or withdrawing, if any.</summary>
    public Invasion Active { get; internal set; }
    public List<Invasion> History { get; } = new();

    // Scheduling. The countdown only runs while a player is home, the core is up and
    // nothing is already under way; otherwise what's left of it is parked in CountdownRemaining.
    internal double CountdownRemaining;
    internal double? NextAt;
    internal double? AbsentSince;
    internal bool HadSource = true;
    internal DeterministicRng Rng;
    internal int InvasionsBegun;
}

public sealed class Invasion
{
    public int Id { get; init; }
    public string BaseId { get; init; } = "";
    public InvasionSource Source { get; init; }
    public double ScheduledAt { get; init; }
    /// <summary>When the force arrives. Every warning is timed back from this.</summary>
    public double ArrivalAt { get; internal set; }
    public InvasionPhase Phase { get; internal set; } = InvasionPhase.Incoming;
    public InvasionOutcome Outcome { get; internal set; }
    /// <summary>The highest warning level issued so far (0 = none yet).</summary>
    public int WarningLevel { get; internal set; }
    public bool SirenOn { get; internal set; }
    public bool OnLongRangeScan { get; internal set; }
    /// <summary>Composed when the force shows up on long-range scans (or on arrival, at the latest).</summary>
    public ForcePlan Force { get; internal set; }
    public double? ArrivedAt { get; internal set; }
    public double? EndedAt { get; internal set; }
    public int InvadersRemaining { get; internal set; }

    internal int NextWarning;
    internal DeterministicRng Rng;

    public double SecondsToArrival(double now) => ArrivalAt - now;
}

public enum DirectorEventKind
{
    InvasionScheduled,
    Warning,
    ForceOnScan,
    SirenOn,
    SirenOff,
    ForceArrived,
    Withdraw,
    InvasionEnded,
    SourceLost,
    InvasionsStopped,
    InvasionsResumed,
    BaseOffline,
    BaseOnline,
}

/// <summary>
/// Something the director decided, stamped with the simulated time it happened. The scene drains
/// these each frame to present them; the full log doubles as the replay record.
/// </summary>
public sealed record DirectorEvent(double Time, DirectorEventKind Kind, string BaseId, int InvasionId, int Level, string Text)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Time:R} {Kind} base={BaseId} inv={InvasionId} lvl={Level} {Text}");
}
