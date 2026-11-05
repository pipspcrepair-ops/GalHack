using System;
using System.Collections.Generic;

namespace Invasions.Director;

public enum StandingTier { Hostile, Neutral, Friendly }

/// <summary>
/// The players' standing with each faction: a number from -100 to 100, held between the faction's own
/// floor and ceiling. At or below the hostile line a faction is hostile, and only a hostile faction that
/// invades at all sends invasions. Deliberate wrongs count in full; accidental ones count by the
/// faction's accident weight, so the Ledger Exchange, the Freeholders and the Kessari can't be turned
/// hostile by a stray shot. This is the simple model the prototype needs until a real reputation
/// system exists; the director only asks it <see cref="Invades"/>.
/// </summary>
public sealed class FactionStandings
{
    readonly FactionsData _data;
    readonly Dictionary<string, double> _values = new();

    public FactionStandings(FactionsData data)
    {
        _data = data;
        foreach (FactionData f in data.All) _values[f.Id] = Clamp(f, f.Standing.Start);
    }

    public double this[string faction] => _values.TryGetValue(faction, out double v) ? v : 0;

    public StandingTier Tier(string faction)
    {
        double v = this[faction];
        return v <= _data.Standing.HostileAt ? StandingTier.Hostile : v >= _data.Standing.FriendlyAt ? StandingTier.Friendly : StandingTier.Neutral;
    }

    public bool IsHostile(string faction) => Tier(faction) == StandingTier.Hostile;

    /// <summary>The faction sends invasions: it invades at all, and it is hostile.</summary>
    public bool Invades(string faction) => _data.Has(faction) && _data.Get(faction).Invades && IsHostile(faction);

    /// <summary>Sets a standing, held to the faction's floor and ceiling. Returns where it ended up.</summary>
    internal double Set(string faction, double value)
    {
        FactionData f = _data.Get(faction);
        return _values[faction] = Clamp(f, value);
    }

    /// <summary>What a wrong of this size would leave the standing at.</summary>
    internal double AfterWrong(string faction, double amount, bool deliberate)
    {
        FactionData f = _data.Get(faction);
        double weight = deliberate ? 1 : Math.Max(0, f.AccidentWeight);
        return this[faction] - Math.Max(0, amount) * weight;
    }

    double Clamp(FactionData f, double value)
    {
        double lo = Math.Max(_data.Standing.Min, f.Standing.Floor), hi = Math.Min(_data.Standing.Max, f.Standing.Ceiling);
        return Math.Clamp(value, lo, Math.Max(lo, hi));
    }
}
