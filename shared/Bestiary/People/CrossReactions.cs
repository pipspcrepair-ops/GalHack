using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>What a person does about a creature near them.</summary>
public enum Response { None, Watch, Herd, Shoo, Flee, Fight, Barricade } // (in order of how much it matters)

/// <summary>
/// People and animals reading each other, as a few plain rules (no one-offs): a person sizes up
/// a creature by what it is (a Husk, a predator, a pest, stock) and answers by their job, their
/// nerve and their group's temper; an animal keeps away from people by its own nature, stock
/// follow whoever works them, and a hunter waits until someone is alone.
/// </summary>
public static class CrossReactions
{
    /// <summary>A creature small enough to be a nuisance, not a danger: shooed off.</summary>
    public static bool Pest(Species s) => s.Domestic == DomesticKind.None && !s.Husk && s.Stats.Damage <= 0f && s.Body.Size < 0.6f;

    /// <summary>A creature that could kill someone.</summary>
    public static bool Danger(Species s) => s.Domestic == DomesticKind.None && !s.Husk && s.Stats.Damage > 0f && (s.Stats.Hunts || s.Behaviour == Behaviour.Predator || s.Body.Size > 1.2f);

    /// <summary>
    /// How one person answers a creature <paramref name="distance"/> metres off. Husks: the armed
    /// and the bold fight, a wary or superstitious group barricades itself in, everyone else
    /// runs. A predator: the armed hunt it, the rest run. A pest close to: waved off. Stock: a
    /// farmer goes to them. Anything else big: watched.
    /// </summary>
    public static Response Respond(Species s, Figure person, Temper temper, float nerve, float distance)
    {
        bool armed = Weapons.Armed(person.Prop);
        bool keeper = person.Role is PersonRole.Guard or PersonRole.Lawman;
        bool child = person.Stage == LifeStage.Child;
        if (s.Husk)
        {
            if (distance > 30f) return Response.None;
            if (armed && (keeper || nerve > 0.7f && !child)) return Response.Fight;
            if (temper != null && (temper.Stance is Stance.Wary or Stance.Superstitious or Stance.Guarded || temper.Wariness > 0.62f)) return Response.Barricade;
            return Response.Flee;
        }
        if (Danger(s))
        {
            if (distance > 25f) return Response.None;
            if (armed && keeper) return Response.Fight;
            return distance < 16f || child ? Response.Flee : Response.Watch;
        }
        if (s.Domestic == DomesticKind.Stock) return person.Role == PersonRole.Farmer && distance < 14f ? Response.Herd : Response.None;
        if (Pest(s)) return distance < 4.5f && !child ? Response.Shoo : Response.None;
        return s.Body.Size > 1f && distance < 12f ? Response.Watch : Response.None;
    }

    /// <summary>How far a species keeps from people, as a share of its flight distance: the skittish more, the bold less, the big least.</summary>
    public static float Shyness(Species s) => s.Domestic != DomesticKind.None ? 0f
        : (s.Temperament switch { Temperament.Skittish => 1.5f, Temperament.Calm => 0.9f, Temperament.Aggressive => 0.35f, _ => 1f }) * Math.Clamp(1.3f - 0.3f * s.Body.Size, 0.7f, 1.3f);

    /// <summary>Nobody else within reach of them: a straggler, what a hunter waits for.</summary>
    public static bool Alone(IReadOnlyList<ITarget> all, ITarget who, float reach)
    {
        foreach (ITarget t in all)
        {
            if (t == who || !t.Attackable) continue;
            if (WildSim.Flat(t.Position - who.Position).Length() < reach) return false;
        }
        return true;
    }
}
