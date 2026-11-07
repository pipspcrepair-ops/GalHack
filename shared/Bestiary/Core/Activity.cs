using System;
using System.Collections.Generic;

namespace Wildlife;

/// <summary>
/// When a species is out and about. Outside its hours it rests at its rest site, so the same
/// valley is a different place after dark: the grazers bed down and other hunters come out.
/// </summary>
public enum Activity
{
    /// <summary>Out by day, asleep in its den, burrow, roost or nest by night.</summary>
    Diurnal,
    /// <summary>Out by night.</summary>
    Nocturnal,
    /// <summary>Out at dawn and dusk, resting through the middle of the day and the dead of night.</summary>
    Crepuscular,
    /// <summary>Any hour: underground and inside places there is no day to keep.</summary>
    Cathemeral,
}

/// <summary>Where an animal spends the hours it is not out.</summary>
public enum RestKind
{
    None,
    /// <summary>A sheltered hollow against a rock or into a slope: hunters and walkers.</summary>
    Den,
    /// <summary>Dug into soft ground: crawlers, burrowers and small things.</summary>
    Burrow,
    /// <summary>High ground where fliers land and fold up.</summary>
    Roost,
    /// <summary>A bedding ground (herds lie down together) or a hive (swarms settle).</summary>
    Nest,
}

/// <summary>
/// The hours of each activity cycle (as windows of the day, 0 midnight, 0.5 noon), and how rest
/// works. Every creature keeps its own small offset (<see cref="Stagger"/>), so a herd doesn't
/// stand up in the same second, and a sleeping animal wakes if a player comes too close.
/// </summary>
public sealed class ActivityRules
{
    public Dictionary<string, float[][]> Windows { get; set; } = new()
    {
        ["Diurnal"] = new[] { new[] { 0.23f, 0.77f } },
        ["Nocturnal"] = new[] { new[] { 0.76f, 1f }, new[] { 0f, 0.24f } },
        ["Crepuscular"] = new[] { new[] { 0.17f, 0.33f }, new[] { 0.67f, 0.83f } },
    };
    /// <summary>Each creature's hours shift by up to this share of a day, so they don't all wake at once.</summary>
    public float Stagger { get; set; } = 0.015f;
    /// <summary>A resting animal wakes when a player comes this close (scaled by how alarming they are).</summary>
    public float WakeRadius { get; set; } = 7f;
    /// <summary>Woken, it stays up this long before going back to rest.</summary>
    public float WakeSeconds { get; set; } = 45f;
    /// <summary>Rest sites lie this far from where a group lives (min, max metres).</summary>
    public float[] RestRadius { get; set; } = { 12f, 40f };
    /// <summary>Glowing species lean nocturnal: their nocturnal weight is multiplied by this.</summary>
    public float GlowNight { get; set; } = 2.5f;
}

/// <summary>Pure helpers for activity cycles.</summary>
public static class ActivityCycle
{
    /// <summary>Is a species with this cycle out at this time of day (0..1), shifted by a creature's own offset?</summary>
    public static bool Active(ActivityRules rules, Activity activity, float time01, float offset = 0f)
    {
        if (activity == Activity.Cathemeral) return true;
        if (!rules.Windows.TryGetValue(activity.ToString(), out float[][] windows) || windows.Length == 0) return true;
        float t = time01 - offset;
        t -= MathF.Floor(t);
        foreach (float[] w in windows)
            if (w.Length >= 2 && t >= w[0] && t < w[1]) return true;
        return false;
    }

    /// <summary>A creature's own shift of its hours: a stable share of the stagger from its id.</summary>
    public static float Offset(ActivityRules rules, int id) => rules.Stagger * (((id * 2654435761u) >> 8) % 1000 / 999f * 2f - 1f);

    public static string Label(Activity a) => a switch
    {
        Activity.Diurnal => "out by day",
        Activity.Nocturnal => "out by night",
        Activity.Crepuscular => "out at dawn and dusk",
        _ => "out at any hour",
    };

    public static string Label(RestKind r) => r switch
    {
        RestKind.Den => "den",
        RestKind.Burrow => "burrow",
        RestKind.Roost => "roost",
        RestKind.Nest => "nest",
        _ => "",
    };
}
