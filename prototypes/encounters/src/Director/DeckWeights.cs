using System.Collections.Generic;
using System.Numerics;

namespace Encounters.Director;

/// <summary>Why a card can't be drawn in this zone at this spot, before any runtime limits.</summary>
public enum Gate { Open, ZoneTags, Danger, NoPoi, Territory, Border }

/// <summary>One card's weight at one spot in one zone.</summary>
/// <param name="Raw">The deck weight after tag gating and the territory boost.</param>
/// <param name="Share">The chance of drawing it, once hostile and peaceful cards are scaled to the danger grade's share.</param>
/// <param name="SideA">For border events, the two factions that meet here.</param>
public readonly record struct WeightedEvent(EventDef Def, float Raw, float Share, Gate Gate, string SideA, string SideB);

/// <summary>
/// Turns the deck into draw chances for one spot in one zone. The zone's tags gate cards in or
/// out, faction territory boosts or allows faction cards, and the danger grade sets how much of
/// the deck is hostile. Runtime limits (caps, cooldowns, no-repeat, budget) come after this.
/// </summary>
public static class DeckWeights
{
    public static List<WeightedEvent> Evaluate(EventDeck deck, ZoneDef zone, Vector3 at, DirectorConfig config, List<WeightedEvent> into = null)
    {
        into ??= new List<WeightedEvent>();
        into.Clear();
        var inside = new List<string>(4);
        var near = new List<string>(4);
        zone.FactionsAt(at, 0f, inside);
        zone.FactionsAt(at, config.Territory.BorderMargin, near);
        near.RemoveAll(f => !deck.Fights(f)); // settlers and merchants never take a side in a border war

        float hostileRaw = 0f, peacefulRaw = 0f;
        foreach (EventDef def in deck.Events)
        {
            Gate gate = Gate.Open;
            float raw = def.Weight;
            string sideA = null, sideB = null;

            if (!def.MatchesTags(zone)) gate = Gate.ZoneTags;
            else if (zone.Danger < def.Danger[0] || zone.Danger > def.Danger[1]) gate = Gate.Danger;
            else if (def.Anchor != "" && zone.FindPoi(def.Anchor) == null) gate = Gate.NoPoi;
            else
            {
                switch (def.Territory)
                {
                    case TerritoryRule.Inside:
                        if (!inside.Contains(def.Faction)) gate = Gate.Territory;
                        break;
                    case TerritoryRule.Outside:
                        if (inside.Contains(def.Faction)) gate = Gate.Territory;
                        break;
                    case TerritoryRule.Any:
                        if (inside.Contains(def.Faction)) raw *= config.Territory.HomeBoost;
                        break;
                    case TerritoryRule.Border:
                        if (near.Count < 2) gate = Gate.Border;
                        else (sideA, sideB) = (near[0], near[1]);
                        break;
                }
            }

            if (gate != Gate.Open) raw = 0f;
            if (def.Hostile) hostileRaw += raw;
            else peacefulRaw += raw;
            into.Add(new WeightedEvent(def, raw, 0f, gate, sideA, sideB));
        }

        // The danger grade decides the hostile share outright, whatever the raw weights say.
        float hostileShare = config.HostileShare(zone.Danger);
        if (hostileRaw <= 0f) hostileShare = 0f;
        else if (peacefulRaw <= 0f) hostileShare = 1f;

        for (int i = 0; i < into.Count; i++)
        {
            WeightedEvent w = into[i];
            if (w.Raw <= 0f) continue;
            float share = w.Def.Hostile ? hostileShare * w.Raw / hostileRaw : (1f - hostileShare) * w.Raw / peacefulRaw;
            into[i] = w with { Share = share };
        }
        return into;
    }
}
