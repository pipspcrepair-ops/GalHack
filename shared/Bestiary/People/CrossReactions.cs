using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>What a person does about a creature near them.</summary>
public enum Response { None, Watch, Herd, Shoo, Avoid, Flee, Fight, Barricade } // (in order of how much it matters)

/// <summary>
/// People and animals reading each other, as a few plain rules (no one-offs): a person sizes up
/// a creature by what it is (a Husk, a predator, a pest, stock, people who'd do them harm) and
/// answers by their job, their nerve and their group's temper; a group takes another group by what
/// each is; an animal keeps away from people by its own nature, stock follow whoever works them,
/// and a hunter waits until someone is alone.
/// </summary>
public static class CrossReactions
{
    /// <summary>
    /// The distances, m: a predator is fought within this and watched within this; people within
    /// this; anyone runs from it this close; the widest berth anyone gives (the timid and unarmed).
    /// </summary>
    const float FightBeast = 25f, FightPeople = 35f, RunIn = 16f, BerthFar = 40f;
    /// <summary>How much nerve it takes to stand against something with a weapon in hand, and what running hot (bold or aggressive) adds.</summary>
    const float StandGrit = 0.55f, HotGrit = 0.25f;

    /// <summary>A creature small enough to be a nuisance, not a danger: shooed off.</summary>
    public static bool Pest(Species s) => s.Domestic == DomesticKind.None && !s.Husk && s.Stats.Damage <= 0f && s.Body.Size < 0.6f;

    /// <summary>People going about as people (a band, a town's folk): what they are to others is by how each group takes the other, never their teeth.</summary>
    public static bool People(Species s) => s.Figure != null && !s.Husk && s.Behaviour == Behaviour.Folk;

    /// <summary>A creature that could kill someone (a feral counts; people going about as people don't).</summary>
    public static bool Danger(Species s) => s.Domestic == DomesticKind.None && !s.Husk && !People(s) && s.Stats.Damage > 0f && (s.Stats.Hunts || s.Behaviour == Behaviour.Predator || s.Body.Size > 1.2f);

    /// <summary>
    /// How one group of people takes another it comes across (a band met on the road; a town and a band
    /// at its edge), from what each already is: one that preys (raiders, a band whose temper rolled
    /// hostile) goes for anyone not of its own sort (gangs size each other up instead); one that is preyed
    /// on, or wary by nature, warns and gives way; the rest pass. Their own people always pass (the caller's).
    /// </summary>
    public static Leaning Regard(Leaning mine, bool mineRaid, Leaning theirs, bool theirsRaid)
    {
        if (mine == Leaning.Hostile && !(mineRaid && theirsRaid)) return Leaning.Hostile;
        return theirs == Leaning.Hostile || theirsRaid || mine == Leaning.Wary ? Leaning.Wary : Leaning.Friendly;
    }

    /// <summary>
    /// Whether someone stands and fights when it comes to it, rather than run: only with a weapon in
    /// hand, and never a child against an animal; the guards and the law always; anyone else by their
    /// nerve, the hot-blooded (bold or aggressive) and a group quick to escalate more readily.
    /// </summary>
    public static bool Stands(Species who, Temper temper, float nerve, bool animal)
    {
        if (who.Figure is not { } f) return true;
        if (!Weapons.Armed(f.Prop) || animal && f.Stage == LifeStage.Child) return false;
        if (f.Role is PersonRole.Guard or PersonRole.Lawman) return true;
        float grit = nerve + (who.Temperament is Temperament.Bold or Temperament.Aggressive ? HotGrit : 0f) + 0.3f * ((temper?.Escalation ?? 0.5f) - 0.5f);
        return grit > StandGrit;
    }

    /// <summary>How wide a berth someone gives a danger they've seen, m: the timid widest, a weapon in hand far less.</summary>
    public static float Berth(float nerve, bool armed) => MathF.Max(RunIn, BerthFar * (1.25f - Math.Clamp(nerve, 0f, 1f)) * (armed ? 0.55f : 1f));

    /// <summary>
    /// How one person answers a creature <paramref name="distance"/> metres off. Husks: the armed
    /// and the bold fight, a wary or superstitious group barricades itself in, everyone else
    /// runs. A predator, or people who'd do them harm (the caller asks only about those): those who
    /// stand (<see cref="Stands"/>) fight it in reach; the rest run once it's close (children at once),
    /// keep wide of it within their berth, and watch it beyond. A pest close to: waved off. Stock: a
    /// farmer goes to them. Anything else big: watched.
    /// </summary>
    public static Response Respond(Species s, Species who, Temper temper, float nerve, float distance)
    {
        Figure person = who.Figure;
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
        bool people = People(s);
        if (people || Danger(s))
        {
            float berth = Berth(nerve, armed), fight = people ? FightPeople : FightBeast;
            if (distance > MathF.Max(berth, fight)) return Response.None;
            if (distance <= fight && Stands(who, temper, nerve, !people)) return Response.Fight;
            if (distance < RunIn || child && distance < berth) return Response.Flee;
            return distance < berth ? Response.Avoid : Response.Watch;
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
