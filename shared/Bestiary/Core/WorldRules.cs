using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>A simple day/night clock. 0 is midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset.</summary>
public sealed class DayClock
{
    public float Time01 = 0.38f;
    public readonly WorldRules Rules;

    public DayClock(WorldRules rules) => Rules = rules;

    public void Advance(float dt) => Time01 = Wrap(Time01 + dt / Rules.DayLengthSeconds);

    public bool IsNight => Time01 >= Rules.NightStart || Time01 < Rules.NightEnd;

    /// <summary>Sine of the sun's height: 1 at noon, -1 at midnight.</summary>
    public float SunHeight => MathF.Sin((Time01 - 0.25f) * MathF.Tau);

    /// <summary>0 at night, 1 in full day, smooth through dusk and dawn.</summary>
    public float Daylight => Math.Clamp((SunHeight + 0.12f) / 0.4f, 0f, 1f);

    public string Label => $"{(int)(Time01 * 24f):00}:{(int)(Time01 * 1440f % 60f):00}{(IsNight ? " night" : "")}";

    static float Wrap(float t) => t - MathF.Floor(t);
}

/// <summary>
/// Plain health. Damage is never capped or softened: fairness comes from creatures warning you
/// before they attack (see <see cref="CombatRules"/>), not from shaving hits.
/// </summary>
public sealed class Health
{
    public float Max, Current;

    public Health(float max)
    {
        Max = max;
        Current = max;
    }

    public bool Downed => Current <= 0f;
    public float Fraction => Max > 0f ? Current / Max : 0f;

    /// <summary>Takes the damage in full; returns how much health was actually lost.</summary>
    public float Apply(float damage)
    {
        if (Downed || damage <= 0f) return 0f;
        float dealt = MathF.Min(damage, Current);
        Current -= dealt;
        return dealt;
    }

    public void Heal(float amount) => Current = MathF.Min(Max, Current + amount);
    public void Restore() => Current = Max;
}

/// <summary>Item names shared by storage, drops and the HUD.</summary>
public static class Items
{
    public const string Wood = "Wood";
    public const string Organics = "Organics";
    public const string Meat = "Meat";
}

/// <summary>
/// A structure's storage: one shared inventory with a capacity in units. Whatever does not
/// fit is refused (Add returns how much was taken), never silently squeezed in.
/// </summary>
public sealed class Storage
{
    public readonly int Capacity;
    readonly Dictionary<string, int> _items = new();

    public Storage(int capacity) => Capacity = capacity;

    public int Total { get; private set; }
    public int Free => Capacity - Total;
    public bool Full => Total >= Capacity;
    public IReadOnlyDictionary<string, int> Items => _items;

    public int Count(string item) => _items.GetValueOrDefault(item);

    /// <summary>Adds up to <paramref name="amount"/>; returns how many units went in.</summary>
    public int Add(string item, int amount)
    {
        int taken = Math.Clamp(amount, 0, Free);
        if (taken == 0) return 0;
        _items[item] = Count(item) + taken;
        Total += taken;
        return taken;
    }

    public void Clear()
    {
        _items.Clear();
        Total = 0;
    }
}

/// <summary>What the codex knows about a species: enough for the discovery card and the codex page.</summary>
public sealed class CodexEntry
{
    public string SpeciesId = "", Name = "", CommonName = "", Form = "", Behaviour = "", Finder = "", Planet = "";
    /// <summary>Body length in metres.</summary>
    public float Size;
    public float FoundAt;
}

/// <summary>
/// Remembers each species the first time anyone gets a good look at one, and who that was.
/// The first sighting raises <see cref="Discovered"/> exactly once per species, whoever sees it
/// and however often after: the game shows a brief discovery card for it.
/// </summary>
public sealed class Codex
{
    readonly Dictionary<string, CodexEntry> _entries = new();

    /// <summary>A species was seen for the first time by anyone. Raised once per species, ever.</summary>
    public event Action<CodexEntry> Discovered;

    public int Count => _entries.Count;
    public IEnumerable<CodexEntry> Entries => _entries.Values;
    public CodexEntry Get(string speciesId) => _entries.GetValueOrDefault(speciesId);
    public bool Knows(string speciesId) => _entries.ContainsKey(speciesId);

    /// <summary>Records a sighting; returns true (and raises <see cref="Discovered"/>) only the first time this species is seen by anyone.</summary>
    public bool Record(Species species, string finder, float time, string planet)
    {
        if (_entries.ContainsKey(species.Id)) return false;
        var entry = new CodexEntry
        {
            SpeciesId = species.Id,
            Name = species.Name,
            CommonName = species.CommonName,
            Form = species.FormLabel,
            Behaviour = species.BehaviourLabel,
            Size = species.Body.Size,
            Finder = finder,
            Planet = planet,
            FoundAt = time,
        };
        _entries[species.Id] = entry;
        Discovered?.Invoke(entry);
        return true;
    }
}
