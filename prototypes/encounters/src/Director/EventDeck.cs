using System;
using System.Collections.Generic;

namespace Encounters.Director;

/// <summary>The scripted flight an event's ships fly. Combat and boarding belong elsewhere.</summary>
public enum Behaviour { Pass, Convoy, Dogfight, Dock, Mine, Drift }

public enum TerritoryRule { Any, Inside, Outside, Border }

/// <summary>What happens to an event's ships when it is over.</summary>
public enum CleanupRule
{
    /// <summary>Untouched ships leave and are removed once out of sight.</summary>
    Despawn,
    /// <summary>As Despawn, but ships wrecked during the event stay as salvage records.</summary>
    KeepWrecks,
    /// <summary>If nobody touched it in time it is lost, and stays as a wreck.</summary>
    WreckOnExpire,
}

public sealed class ShipGroupDef
{
    public string Role { get; set; } = "";
    /// <summary>A hull name, or "$fighter" for the faction's fighter hull.</summary>
    public string Hull { get; set; } = "";
    /// <summary>Empty: the event's faction (or its side's faction, for border events).</summary>
    public string Faction { get; set; } = "";
    /// <summary>"a" or "b" in two-sided events.</summary>
    public string Side { get; set; } = "";
    public int[] Count { get; set; } = { 1, 1 };
    public float PerExtraPlayer { get; set; }
    public int[] Crew { get; set; } = { 2, 4 };
}

public sealed class SignalDef
{
    public string Kind { get; set; } = "distress";
    public string Label { get; set; } = "";
}

public sealed class WreckDef
{
    public int[] Count { get; set; } = { 0, 0 };
    public float[] When { get; set; } = { 0.3f, 0.9f };
}

public sealed class FactionDef
{
    public string Name { get; set; } = "";
    public string Fighter { get; set; } = "";
    /// <summary>False for factions that never attack (the Ledger Exchange, the Freeholders).</summary>
    public bool Fights { get; set; } = true;
}

/// <summary>One card in the deck. Pure data: see the header of data/deck.json.</summary>
public sealed class EventDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public float Weight { get; set; } = 1f;
    public string Faction { get; set; } = "";
    public TerritoryRule Territory { get; set; }
    public bool Hostile { get; set; }
    public string[] Requires { get; set; } = Array.Empty<string>();
    public string[] Excludes { get; set; } = Array.Empty<string>();
    public int[] Danger { get; set; } = { 0, 5 };
    public string Anchor { get; set; } = "";
    public Behaviour Behaviour { get; set; }
    public float[] Speed { get; set; } = { 50f, 50f };
    public float[] Duration { get; set; } = { 60f, 90f };
    public float[] SpawnDistance { get; set; }
    public CleanupRule Cleanup { get; set; }
    public WreckDef Wrecks { get; set; }
    public SignalDef Signal { get; set; }
    public List<ShipGroupDef> Ships { get; set; } = new();

    /// <summary>Each Requires entry split into its "a|b" alternatives, once, at load.</summary>
    internal string[][] RequiresAlternatives = Array.Empty<string[]>();

    /// <summary>The fewest ships this event can spawn with.</summary>
    public int MinShips { get; private set; }

    public bool MatchesTags(ZoneDef zone)
    {
        foreach (string[] alternatives in RequiresAlternatives)
            if (!zone.HasAnyTag(alternatives)) return false;
        foreach (string tag in Excludes)
            if (zone.HasTag(tag)) return false;
        return true;
    }

    internal void Prepare(EventDeck deck)
    {
        string where = $"deck: event '{Id}'";
        if (Id == "") throw new FormatException("deck: an event has no id");
        if (Category == "") throw new FormatException($"{where} has no category");
        if (Weight < 0) throw new FormatException($"{where} has a negative weight");
        CheckRange(Speed, where, "speed");
        CheckRange(Duration, where, "duration");
        if (SpawnDistance != null) CheckRange(SpawnDistance, where, "spawnDistance");
        if (Danger is not { Length: 2 } || Danger[0] > Danger[1]) throw new FormatException($"{where}: danger must be [min, max]");
        if (Ships.Count == 0) throw new FormatException($"{where} has no ships");
        if (Behaviour is Behaviour.Dock or Behaviour.Mine && Anchor == "")
            throw new FormatException($"{where}: {Behaviour} needs an anchor POI");
        if (Territory == TerritoryRule.Border && Faction != "*")
            throw new FormatException($"{where}: border events take faction \"*\"");

        MinShips = 0;
        foreach (ShipGroupDef group in Ships)
        {
            if (group.Count is not { Length: 2 } || group.Count[0] < 0 || group.Count[0] > group.Count[1])
                throw new FormatException($"{where}: ship count must be [min, max]");
            if (group.Crew is not { Length: 2 } || group.Crew[0] < 1 || group.Crew[0] > group.Crew[1])
                throw new FormatException($"{where}: every ship needs a crew (no drones): crew must be [min, max] >= 1");
            if (Territory == TerritoryRule.Border && group.Side is not ("a" or "b"))
                throw new FormatException($"{where}: border events need every group on side a or b");
            string faction = group.Faction != "" ? group.Faction : Faction;
            if (faction != "*" && !deck.Factions.ContainsKey(faction))
                throw new FormatException($"{where}: unknown faction '{faction}'");
            if (Hostile && faction != "*" && !deck.Factions[faction].Fights)
                throw new FormatException($"{where}: the {deck.Factions[faction].Name} never attack, so can't be in a hostile event");
            MinShips += group.Count[0];
        }
        if (MinShips == 0) throw new FormatException($"{where} can spawn with no ships");

        RequiresAlternatives = new string[Requires.Length][];
        for (int i = 0; i < Requires.Length; i++)
            RequiresAlternatives[i] = Requires[i].Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    static void CheckRange(float[] range, string where, string what)
    {
        if (range is not { Length: 2 } || range[0] > range[1] || range[0] < 0)
            throw new FormatException($"{where}: {what} must be [min, max]");
    }
}

public sealed class EventDeck
{
    public Dictionary<string, FactionDef> Factions { get; set; } = new();
    public List<EventDef> Events { get; set; } = new();

    public EventDef Get(string id)
    {
        foreach (EventDef def in Events)
            if (def.Id == id) return def;
        throw new KeyNotFoundException($"no event '{id}' in the deck");
    }

    public string FactionName(string id) => Factions.TryGetValue(id, out FactionDef f) ? f.Name : id;

    public bool Fights(string id) => !Factions.TryGetValue(id, out FactionDef f) || f.Fights;

    /// <summary>Turns "$fighter" into the faction's fighter hull; other hull names pass through.</summary>
    public string ResolveHull(string hull, string faction) =>
        hull == "$fighter" && Factions.TryGetValue(faction, out FactionDef f) ? f.Fighter : hull;

    public static EventDeck Parse(string json)
    {
        var deck = Json.Parse<EventDeck>(json, "deck");
        var seen = new HashSet<string>();
        foreach (EventDef def in deck.Events)
        {
            def.Prepare(deck);
            if (!seen.Add(def.Id)) throw new FormatException($"deck: duplicate event id '{def.Id}'");
        }
        return deck;
    }
}
