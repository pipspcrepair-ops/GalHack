using System;
using System.Collections.Generic;

namespace Encounters.Director;

/// <summary>The director's tunables, loaded from data/director.json. See that file for what each one does.</summary>
public sealed class DirectorConfig
{
    public float TickHz { get; set; } = 10f;
    public float ViewConeHalfAngleDeg { get; set; } = 70f;
    public float MinSpawnDistance { get; set; } = 1800f;
    public float MaxSpawnDistance { get; set; } = 4200f;
    public int SpawnAttempts { get; set; } = 24;
    public float SpawnVerticalSpread { get; set; } = 0.3f;
    public BudgetConfig Budget { get; set; } = new();
    public Dictionary<string, int> CategoryCaps { get; set; } = new();
    public float[] SpawnGap { get; set; } = { 8f, 18f };
    public float SpawnGapPlayerFactor { get; set; } = 0.5f;
    public float RetryDelay { get; set; } = 1.5f;
    public Dictionary<string, float> CategoryCooldowns { get; set; } = new();
    public float NoRepeatWindow { get; set; } = 60f;
    public WarmStartConfig WarmStart { get; set; } = new();
    public float[] HostileShareByDanger { get; set; } = { 0.05f, 0.15f, 0.3f, 0.45f, 0.6f, 0.75f };
    public TerritoryConfig Territory { get; set; } = new();
    public DespawnConfig Despawn { get; set; } = new();
    public float WreckWitnessDistance { get; set; } = 8000f;
    public float TouchDistance { get; set; } = 250f;
    public LeadConfig Lead { get; set; } = new();
    /// <summary>Creatures also keep this far outside every player base's circle, measured from its edge.</summary>
    public float CreatureBaseClearance { get; set; } = 1000f;

    public double TickSeconds => 1.0 / TickHz;

    /// <summary>cos of the view cone's half angle: a point is in view if its direction's dot with the view is above it.</summary>
    public float ViewConeCos => MathF.Cos(ViewConeHalfAngleDeg * MathX.Deg);

    public int MaxEvents(int players) => Budget.Events + (int)(Budget.EventsPerExtraPlayer * Math.Max(0, players - 1));

    public int MaxShips(int players) => Budget.Ships + (int)(Budget.ShipsPerExtraPlayer * Math.Max(0, players - 1));

    public int MaxCreatures(int players) => Budget.Creatures + (int)(Budget.CreaturesPerExtraPlayer * Math.Max(0, players - 1));

    /// <summary>Categories without a cap get one at a time, so a typo in the deck can't flood a zone.</summary>
    public int CategoryCap(string category) => CategoryCaps.TryGetValue(category, out int cap) ? cap : 1;

    public float CategoryCooldown(string category) => CategoryCooldowns.TryGetValue(category, out float s) ? s : 0f;

    public float HostileShare(int danger) => HostileShareByDanger[Math.Clamp(danger, 0, HostileShareByDanger.Length - 1)];

    public static DirectorConfig Parse(string json)
    {
        var config = Json.Parse<DirectorConfig>(json, "director");
        if (config.TickHz <= 0) throw new FormatException("director: tickHz must be positive");
        if (config.MinSpawnDistance > config.MaxSpawnDistance) throw new FormatException("director: minSpawnDistance > maxSpawnDistance");
        if (config.HostileShareByDanger.Length != 6) throw new FormatException("director: hostileShareByDanger needs 6 values (danger 0..5)");
        if (config.SpawnGap is not { Length: 2 } || config.SpawnGap[0] > config.SpawnGap[1]) throw new FormatException("director: spawnGap must be [min, max]");
        return config;
    }
}

public sealed class BudgetConfig
{
    public int Events { get; set; } = 4;
    public float EventsPerExtraPlayer { get; set; } = 1.5f;
    public int Ships { get; set; } = 16;
    public float ShipsPerExtraPlayer { get; set; } = 6f;
    /// <summary>Space fauna have their own budget: a shoal shouldn't crowd out the traffic.</summary>
    public int Creatures { get; set; } = 14;
    public float CreaturesPerExtraPlayer { get; set; } = 4f;
}

public sealed class WarmStartConfig
{
    public int Events { get; set; } = 2;
    public float Delay { get; set; } = 1f;
    public float Gap { get; set; } = 2f;
}

public sealed class TerritoryConfig
{
    public float HomeBoost { get; set; } = 2.5f;
    public float BorderMargin { get; set; } = 600f;
}

public sealed class DespawnConfig
{
    public float MinDistance { get; set; } = 1800f;
    public float FarDistance { get; set; } = 9000f;
}

public sealed class LeadConfig
{
    public float AngleFuzzDeg { get; set; } = 10f;
    public float DistanceFuzz { get; set; } = 0.2f;
    public float ResolveDistance { get; set; } = 700f;
}
