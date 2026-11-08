using System.Collections.Generic;
using Godot;
using Wildlife;

namespace Slice.Places;

/// <summary>
/// The town's people and the creatures round them (<see cref="CrossReactions"/>): a few times a
/// second each person out and about sizes up what is near and answers by job, nerve and the
/// group's temper. Husks: the armed and the bold fight, a wary group barricades itself in, the
/// rest run for their doors. Predators: the guards and the law shoot them, the rest run. Pests
/// close by are waved off; stock are worked by the farmers, and follow them.
/// </summary>
public sealed partial class Town
{
    float _senseIn;
    bool _handlers;
    readonly List<Creature> _round = new();

    /// <summary>The place has shut itself in against something outside; its gates stay shut.</summary>
    public bool Barricaded => _barricadeFor > 0f;
    float _barricadeFor;

    /// <summary>What the town has done about creatures (tests): fought, fled, barricaded, shooed, herded.</summary>
    public int Fought { get; private set; }
    public int FledFrom { get; private set; }
    public int Barricades { get; private set; }
    public int Shooed { get; private set; }
    public int Herded { get; private set; }

    /// <summary>One of the town's people fires at something that isn't you (from, to): only the shot is drawn.</summary>
    public event System.Action<Vector3, Vector3> ShotAway;

    /// <summary>How many times the town has looked round it, and how many creatures it saw last (tests).</summary>
    public int Senses { get; private set; }
    public int Sensed { get; private set; }

    const float SenseEvery = 0.4f, FightAim = 1.2f;

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
        _round.Clear();
        foreach (Creature c in sim.Creatures)
            if (!c.Removed && !c.Dead && WildSim.Flat(c.Position - centre).Length() < reach) _round.Add(c);
        Sensed = _round.Count;
        Senses++;
        if (_round.Count == 0) return;
        foreach (Person p in People)
        {
            if (p.Dead || p.Gone || p.Indoors || p.Away || p.Looks == null) continue;
            Response best = Response.None;
            Creature what = null;
            float near = float.MaxValue;
            Vector3 at = p.Position;
            foreach (Creature c in _round)
            {
                if (c.Species.Figure != null && !c.Species.Husk) continue; // people out there are people
                float d = WildSim.Flat(c.Position - at).Length();
                Response r = CrossReactions.Respond(c.Species, p.Figure, _temper, p.Nerve, d);
                if (r > best || r == best && d < near) { best = r; what = c; near = d; }
            }
            p.Threat = best is Response.Fight or Response.Watch or Response.Shoo ? what : null;
            switch (best)
            {
                case Response.Fight:
                    if (p.Facing <= 0f) p.Facing = FightAim;
                    break;
                case Response.Barricade:
                    if (_barricadeFor <= 0f) Barricades++;
                    _barricadeFor = 15f;
                    break;
                case Response.Flee:
                    if (p.ScaredFor <= 0f) FledFrom++;
                    p.ScaredFor = 10f;
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
            if (best != Response.Fight && p.Facing > 0f) { p.Facing = 0f; if (p.Halt <= 0f) p.Holding = false; }
        }
    }

    /// <summary>A guard or the law with something dangerous in their sights: they face it, raise, and shoot it.</summary>
    bool Fight(Person p, float dt)
    {
        if (p.Threat == null || p.Facing <= 0f) return false;
        Creature c = p.Threat;
        if (c.Removed || c.Dead) { p.Threat = null; p.Facing = 0f; return false; }
        Vector3 d = _place.FromWorld(_places.Zone.Wildlife.WorldOf(c)) - p.Pos;
        p.Yaw = Mathf.LerpAngle(p.Yaw, Mathf.Atan2(-d.X, -d.Z), Mathf.Min(1f, dt * 6f));
        p.Now = 0f;
        p.Holding = true;
        p.Facing -= dt;
        if (p.Facing > 0f) return true;
        // The shot: the place's own people deal with what got this close.
        _places.Zone.Wildlife.Sim.CullStray(c, p);
        ShotAway?.Invoke(_place.ToWorld(p.Pos + Vector3.Up * 1.5f), _places.Zone.Wildlife.WorldOf(c) + Vector3.Up * 0.6f);
        Fought++;
        p.Threat = null;
        p.Holding = false;
        return true;
    }
}
