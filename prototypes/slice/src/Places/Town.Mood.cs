using System;
using System.Collections.Generic;
using Godot;
using PoiGenerator.Gen;
using Wildlife;

namespace Slice.Places;

/// <summary>
/// Each person's own mood (<see cref="Feeling"/>), moved by what happens near them (a shot, someone
/// dying, a threat, a gift, an evening at a gathering fire) and settling back over time. It only
/// ever feeds the pace, the idle gesture, the flight and the voice: it never decides an attack.
/// </summary>
public sealed partial class Town
{
    List<Vector3> _fires;

    /// <summary>An event at a world point reaches everyone out and about within earshot, nearer ones harder.</summary>
    public void Stir(MoodEvent e, Vector3 world, float reach = 40f)
    {
        if (_view == null) return;
        Vector3 at = Local(world);
        foreach (Person p in People)
        {
            if (p.Dead || p.Gone) continue;
            float d = (p.Pos - at).Length();
            if (d > reach) continue;
            Bind(p);
            p.Feel.Hit(e, 1f - d / reach);
        }
    }

    /// <summary>A shot was fired in or near the town.</summary>
    public void Gunshot() { if (_view != null) Stir(MoodEvent.Shot, _places.Zone.Wildlife.PlayerWorld(), 70f); }

    /// <summary>
    /// A shot (a blow, a blast, where one landed) heard in or near the town (owner: "startled or trigger aggression [or] fleeing
    /// depending on their temperament", structure guns and turrets too): everyone out in earshot starts and looks to it, and it
    /// frightens them by their nerve (the timid run: <see cref="Feeling"/>). Fired in the town, or at any of them (passing or
    /// landing close), it is a threat: yours (by hand or a structure's guns) turns the place on you (<see cref="Provoked"/>), a
    /// creature's is met as a danger. Their own fire and their beasts' they take in their stride.
    /// </summary>
    public void Gunfire(Vector3 world, float radius, Vector3 dir, object by)
    {
        if (_view == null || by is Person { Town: var own } && own == this || by is Creature { Keeper: var k } && k == this) return;
        var shot = new WorldNoise { Position = Local(world), Radius = radius, Dir = dir };
        bool at = Covers(world);
        foreach (Person p in People)
        {
            if (p.Dead || p.Gone) continue;
            float d = (p.Pos - shot.Position).Length();
            if (d > radius) continue;
            // (Out on a run, a shot at them is theirs to answer, not the town's: Town.Runs.)
            if (p.Run != null) { if (shot.At(p.Pos + Vector3.Up * 1.2f)) ShotAt(p, by); }
            else at |= shot.At(p.Pos + Vector3.Up * 1.2f);
            Bind(p);
            p.Feel.Hit(MoodEvent.Shot, 1f - d / radius);
            p.StartleFor = MathF.Max(p.StartleFor, 0.7f);
            (p.LookAt, p.LookFor) = (shot.Position + Vector3.Up, 2.5f);
        }
        if (!at) return;
        if (by is Creature c) Harmed(c);
        else if (by is null or Slice.Vessels.Vessel) Provoked();
    }

    static void Bind(Person p)
    {
        if (p.Feel.Bound) return;
        p.Feel.Bound = true;
        p.Feel.Nerve = p.Nerve;
        p.Feel.Drunk = p.Job == PersonRole.Drunk;
        p.Feel.Armed = p.Figure != null && Weapons.Armed(p.Figure.Prop);
    }

    /// <summary>What a deed does to the moods of those near you.</summary>
    void MoodOf(Deed d)
    {
        Vector3 you = _places.Zone.Wildlife.PlayerWorld();
        switch (d)
        {
            case Deed.Help: case Deed.Trade: case Deed.Thanks: Stir(MoodEvent.Gift, you, 12f); break;
            case Deed.Threat: Stir(MoodEvent.Threat, you, 25f); break;
            case Deed.Harm: case Deed.Kill: Stir(MoodEvent.Harm, you, 35f); break;
        }
    }

    /// <summary>
    /// Settles every mood, sees deaths, lifts those at a fire, and sends the frightened running: everyone each call, or (the
    /// place down, only those out on the land about) one in <paramref name="every"/> in turn, with that much more time.
    /// </summary>
    void MoodTick(float dt, int every = 1)
    {
        if (_fires == null)
        {
            _fires = new List<Vector3>();
            foreach (Marker m in Memory.Inst.Markers) if (m.Kind == "campfire") _fires.Add(Walk(m.Cell));
        }
        dt *= every;
        for (int i = _tick % every; i < People.Count; i += every)
        {
            Person p = People[i];
            if (p.Gone || p.Looks == null) continue;
            Bind(p);
            if (p.Dead)
            {
                if (p.Feel.WasDead) continue;
                p.Feel.WasDead = true;
                // Those who saw it; a partner takes it hardest.
                foreach (Person q in People)
                {
                    if (q == p || q.Dead || q.Gone || q.Asleep) continue;
                    float d = (q.Pos - p.Pos).Length();
                    if (d > 25f) continue;
                    Bind(q);
                    q.Feel.Hit(q.Partner == p || p.Partner == q ? MoodEvent.LostKin : MoodEvent.SawDeath, 1f - d / 25f);
                }
                continue;
            }
            bool atFire = false;
            foreach (Vector3 f in _fires) if ((f - p.Pos).Length() < 6f) { atFire = true; break; }
            p.Feel.Step(dt, atFire);
            if (p.Feel.Flees) p.ScaredFor = MathF.Max(p.ScaredFor, 2f);
        }
    }

    /// <summary>
    /// A sound that isn't speech, in their voice and mood: the scared call out or breathe fast, the angry bark, the grieving
    /// cry, the content laugh, the unsettled breathe short, a worker grunts with effort. False: say something instead.
    /// </summary>
    bool Vocalise(Person who)
    {
        PersonMood m = who.Feel.Mood;
        bool child = who.Figure.Stage == LifeStage.Child;
        double roll = _rng.NextDouble();
        VocalKind? kind = m switch
        {
            PersonMood.Scared => roll < 0.4 ? VocalKind.Call : roll < 0.7 ? VocalKind.Breath : child || roll < 0.85 ? VocalKind.Cry : VocalKind.Pain,
            PersonMood.Angry => roll < 0.6 ? VocalKind.Call : VocalKind.Grunt,
            PersonMood.Grieving => roll < 0.7 ? VocalKind.Cry : VocalKind.Breath,
            PersonMood.Content => roll < 0.6 ? VocalKind.Laugh : null,
            PersonMood.Uneasy => roll < 0.5 ? VocalKind.Breath : null,
            PersonMood.Drunk => roll < 0.4 ? VocalKind.Laugh : roll < 0.6 ? VocalKind.Grunt : null,
            _ => who.Working ? (roll < 0.6 ? VocalKind.Effort : VocalKind.Grunt) : roll < 0.08 ? VocalKind.Breath : null,
        };
        if (kind is not { } k) return false;
        Vector3 at = World(who.Pos + Vector3.Up * who.Figure.Height * 0.9f);
        _places.Zone.Wildlife.Sound(Vector3.Zero, a => a.Vocal(who.Looks, k, m, at));
        return true;
    }

    /// <summary>The idle gesture their mood asks for, or none.</summary>
    BodyAct MoodAct(Person p) => p.Feel.Idle(_clock, People.IndexOf(p));
}
