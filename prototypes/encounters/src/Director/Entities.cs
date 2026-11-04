using System.Collections.Generic;
using System.Numerics;

namespace Encounters.Director;

public enum EntityKind { NpcShip, PlayerCargo, PlayerShip }

/// <summary>
/// How a player touched something. Any touch makes it a permanent record. Only a wreck (its core
/// destroyed) can be claimed: a live ship becomes yours only by boarding it and hijacking its
/// core, which belongs to combat and boarding, not to the director.
/// </summary>
public enum TouchKind
{
    None,
    /// <summary>A live ship the player flew up to and marked: remembered, still not theirs.</summary>
    Touched,
    /// <summary>A wreck claimed as salvage: now the player's.</summary>
    Claimed,
}

/// <summary>
/// Anything in a zone the director knows about: an event's crewed NPC ship, a wreck, or
/// something a player owns. While an event's ship is untouched it is transient and lives in
/// its zone instance; the moment it becomes lasting (touched, owned or a witnessed wreck) it
/// moves to the <see cref="PermanentRecords"/>, where nothing ever removes it.
/// </summary>
public sealed class WorldEntity
{
    public long Id;
    public string ZoneId = "";
    public EntityKind Kind;
    public string Hull = "";
    public string Faction = "";
    public string Role = "";
    public string Side = "";
    public string Label = "";
    /// <summary>Every NPC vehicle is crewed; there are no drones.</summary>
    public int Crew;
    public bool Hostile;

    /// <summary>The event that spawned it, if any (kept on records as history).</summary>
    public long EncounterId;
    public string EventId = "";

    public Vector3 Position;
    public Vector3 Forward = -Vector3.UnitZ;
    /// <summary>Its scripted flight while transient; null once it is a record or player-owned.</summary>
    public ShipScript Script;

    /// <summary>The owning player's id, or -1.</summary>
    public int Owner = -1;
    public TouchKind Touch;
    public int TouchedBy = -1;
    public bool Wrecked;
    public double CreatedAt;
    public double WreckedAt;

    /// <summary>True while it is an untouched event ship in a zone instance.</summary>
    public bool Live { get; internal set; }

    internal Encounter Encounter;

    public bool PlayerOwned => Owner >= 0;

    /// <summary>Lasting things are never cleaned up: player-owned, touched by a player, or a registered wreck.</summary>
    public bool IsLasting => PlayerOwned || Touch != TouchKind.None || Wrecked;

    /// <summary>Where it is at a given time: its scripted flight while transient, its fixed spot as a record.</summary>
    public void Sample(double time, out Vector3 position, out Vector3 forward)
    {
        if (Script != null) Script.Evaluate(time, out position, out forward);
        else
        {
            position = Position;
            forward = Forward;
        }
    }
}

public enum EncounterState { Active, Leaving, Done }

/// <summary>One spawned event: a card from the deck, played out by crewed ships in one zone.</summary>
public sealed class Encounter
{
    public long Id;
    public EventDef Def;
    public string ZoneId = "";
    public double Start, End;
    public EncounterState State;
    public Vector3 Anchor;
    public int FocusPlayer;
    public string SideA, SideB;
    public bool Touched;
    public bool Debug;

    /// <summary>Every ship it spawned, whether still transient, now a record, or gone.</summary>
    public readonly List<WorldEntity> Ships = new();

    internal readonly List<(long ShipId, double At)> WreckSchedule = new();

    public int LiveShips
    {
        get
        {
            int n = 0;
            foreach (WorldEntity ship in Ships)
                if (ship.Live) n++;
            return n;
        }
    }
}

/// <summary>A player as the director sees them: where they are and where they look.</summary>
public sealed class PlayerState
{
    public int Id;
    public string ZoneId;
    public Vector3 Position;
    public Vector3 Forward = -Vector3.UnitZ;
}

/// <summary>
/// A lead from a distress call or a faction transmission: a direction and a distance from where
/// the player heard it, both fuzzed, never an exact spot.
/// </summary>
public sealed class SignalLead
{
    public long Id;
    public int PlayerId;
    public string ZoneId = "";
    public long EncounterId;
    public long TargetId;
    public string Kind = "";
    public string Label = "";
    public Vector3 Origin;
    public Vector3 Direction;
    public float Distance;
    public double EmittedAt;

    /// <summary>The truth at emission, for tests and the debug overlay; the HUD never shows it.</summary>
    public Vector3 TrueOffset;

    /// <summary>The fuzzed spot the lead points at.</summary>
    public Vector3 Estimate => Origin + Direction * Distance;

    public float AngleErrorDeg => MathX.AngleDeg(Direction, TrueOffset);

    public float DistanceError => TrueOffset.Length() < 1e-3f ? 0f : System.MathF.Abs(Distance - TrueOffset.Length()) / TrueOffset.Length();
}

public readonly record struct LogEntry(double Time, string Zone, string Kind, long Id, string Text)
{
    public override string ToString() => MathX.Inv($"{Time,8:F1}  {Zone,-16} {Kind,-9} #{Id,-5} {Text}");
}

public readonly record struct JournalEntry(double Time, string Zone, long EntityId, string What);

public sealed class TickStats
{
    public long Ticks { get; private set; }
    public double LastMicros { get; private set; }
    public double MaxMicros { get; private set; }
    public double TotalMicros { get; private set; }
    public double AverageMicros => Ticks == 0 ? 0 : TotalMicros / Ticks;

    internal void Add(double micros)
    {
        Ticks++;
        LastMicros = micros;
        TotalMicros += micros;
        if (micros > MaxMicros) MaxMicros = micros;
    }

    public void Reset()
    {
        Ticks = 0;
        LastMicros = MaxMicros = TotalMicros = 0;
    }
}
