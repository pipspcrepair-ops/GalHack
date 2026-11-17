using System.Collections.Generic;
using Godot;
using Wildlife;

namespace Slice.Places;

/// <summary>
/// The town's people and what's round them (<see cref="CrossReactions"/>): a few times a second each
/// person out and about sizes up what is near and answers by job, nerve and the group's temper.
/// Husks: the armed and the bold fight, a wary group barricades itself in, the rest run for their
/// doors. Predators, and people who'd do the town harm (raiders, a band that preys, a band the town
/// would turn on itself, whoever last hurt one of its own): whoever has the nerve and a weapon fights
/// them as they would you (Town.Combat: the same aim, cover and shots), the rest keep wide of them and
/// run in once they're close. Pests close by are waved off; stock are worked by the farmers, and follow them.
/// </summary>
public sealed partial class Town
{
    float _senseIn;
    bool _handlers;
    readonly List<Creature> _round = new();
    /// <summary>The last creature to hurt one of them, and when: met as a danger wherever it is, a while after.</summary>
    Creature _harmedBy;
    float _harmedAt = -99f;

    /// <summary>The place has shut itself in against something outside; its gates stay shut.</summary>
    public bool Barricaded => _barricadeFor > 0f;
    float _barricadeFor;

    /// <summary>What the town has done about creatures (tests): fights taken up, fled, kept wide of, barricaded, shooed, herded.</summary>
    public int Fought { get; private set; }
    public int FledFrom { get; private set; }
    public int Avoided { get; private set; }
    public int Barricades { get; private set; }
    public int Shooed { get; private set; }
    public int Herded { get; private set; }

    /// <summary>How many times the town has looked round it, and how many creatures it saw last (tests).</summary>
    public int Senses { get; private set; }
    public int Sensed { get; private set; }

    /// <summary>How often they look round, s; how long a fight with something lasts once it no longer calls for one, s; how long they remember who hurt one of them, s.</summary>
    const float SenseEvery = 0.4f, FightFor = 3f, HarmMemory = 30f;

    /// <summary>How the town takes outsiders by its own temper (what other groups go by).</summary>
    Leaning Nature => Temper.LeaningOf(_temper?.Stance ?? Stance.Welcoming);

    /// <summary>Whether the town takes one of a band out there as a danger: one that would prey on it, or that it would turn on itself, or whoever last hurt one of its own.</summary>
    bool Foe(Creature c) => c == _harmedBy && _clock - _harmedAt < HarmMemory
        || c.Herd?.Party is { } them && (CrossReactions.Regard(them.Nature, them.Raids, Nature, false) == Leaning.Hostile || CrossReactions.Regard(Nature, false, them.Nature, them.Raids) == Leaning.Hostile);

    /// <summary>A creature hurt one of them (a shot, a bite, a blow): it's met as a danger at once, wherever it is.</summary>
    void Harmed(Creature by)
    {
        _harmedBy = by;
        _harmedAt = _clock;
        _senseIn = 0f;
    }

    void Sense(float dt)
    {
        _barricadeFor = Mathf.Max(0f, _barricadeFor - dt);
        if ((_senseIn -= dt) > 0f) return;
        _senseIn = SenseEvery;
        WildSim sim = _places.Zone.Wildlife.Sim;
        if (sim == null) return;
        // Stock are worked by the place's farmers, once there are any.
        if (!_handlers)
        {
            _handlers = true;
            Person farmer = null;
            foreach (Person q in People) if (q.Job == PersonRole.Farmer && !q.Dead) { farmer = q; break; }
            if (farmer != null)
                foreach (var a in _animals)
                    if (a.Kind == DomesticKind.Stock) a.Creature.Owner = farmer;
        }
        Vector3 centre = _places.Zone.Wildlife.PatchLocal(World(Walk(Data.AreaCenter) with { Y = 0 }));
        float reach = Data.AreaRadius + 30f;
        Creature harmer = _harmedBy is { Dead: false, Removed: false } && _clock - _harmedAt < HarmMemory && sim.Creatures.Contains(_harmedBy) ? _harmedBy : null;
        _round.Clear();
        foreach (Creature c in sim.Creatures)
            if (!c.Removed && !c.Dead && (c == harmer || WildSim.Flat(c.Position - centre).Length() < reach && (!CrossReactions.People(c.Species) || Foe(c)))) _round.Add(c);
        Sensed = _round.Count;
        Senses++;
        // (Each is looked at even with nothing round: what they dealt with that went off, or is no longer in this sim, is let go.)
        foreach (Person p in People)
        {
            if (p.Dead || p.Gone || p.Indoors || p.Away || p.Looks == null) continue;
            Response best = Response.None;
            Creature what = null;
            float near = float.MaxValue;
            Vector3 at = p.Position;
            foreach (Creature c in _round)
            {
                float d = WildSim.Flat(c.Position - at).Length();
                // (Whoever hurt one of them is in reach of a fight wherever they are.)
                Response r = CrossReactions.Respond(c.Species, p.Looks, _temper, p.Nerve, c == harmer ? Mathf.Min(d, 20f) : d);
                if (r > best || r == best && d < near) { best = r; what = c; near = d; }
            }
            Creature was = p.Threat;
            p.Threat = best is Response.Fight or Response.Watch or Response.Shoo ? what : null;
            if (best != Response.Fight && p.Threat != was) p.Facing = 0f; // (a fight dies down a while after; never onto something else)
            switch (best)
            {
                case Response.Fight:
                    if (p.Facing <= 0f) Fought++;
                    p.Facing = FightFor;
                    break;
                case Response.Barricade:
                    if (_barricadeFor <= 0f) Barricades++;
                    _barricadeFor = 15f;
                    break;
                case Response.Flee:
                    if (p.ScaredFor <= 0f) FledFrom++;
                    p.ScaredFor = 10f;
                    p.FleeFrom = _place != null ? _place.FromWorld(_places.Zone.Wildlife.WorldOf(what)) : p.Pos;
                    p.FleeAt = _clock;
                    break;
                case Response.Avoid:
                    if (p.AvoidFor <= 0f) Avoided++;
                    p.AvoidFor = 6f;
                    break;
                case Response.Shoo:
                    if (p.ShooFor <= 0f) Shooed++;
                    p.ShooFor = 1.6f;
                    break;
                case Response.Herd:
                    if (!p.Herding) Herded++;
                    p.Herding = true;
                    break;
            }
            if (best != Response.Herd) p.Herding = false;
        }
    }
}
