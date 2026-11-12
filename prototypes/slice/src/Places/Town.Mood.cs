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

    /// <summary>Events that have stirred moods (tests).</summary>
    public int MoodEvents { get; private set; }

    /// <summary>An event at a world point reaches everyone out and about within earshot, nearer ones harder.</summary>
    public void Stir(MoodEvent e, Vector3 world, float reach = 40f)
    {
        if (_place == null) return;
        Vector3 at = _place.FromWorld(world);
        MoodEvents++;
        foreach (Person p in People)
        {
            if (p.Dead || p.Gone || p.Away) continue;
            float d = (p.Pos - at).Length();
            if (d > reach) continue;
            Bind(p);
            p.Feel.Hit(e, 1f - d / reach);
        }
    }

    /// <summary>A shot was fired in or near the town.</summary>
    public void Gunshot() { if (_place != null) Stir(MoodEvent.Shot, _places.Zone.Wildlife.PlayerWorld(), 70f); }

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

    /// <summary>Settles every mood, sees deaths, lifts those at a fire, and sends the frightened running.</summary>
    void MoodTick(float dt)
    {
        if (_fires == null)
        {
            _fires = new List<Vector3>();
            foreach (Marker m in Memory.Inst.Markers) if (m.Kind == "campfire") _fires.Add(Walk(m.Cell));
        }
        foreach (Person p in People)
        {
            if (p.Gone || p.Looks == null) continue;
            Bind(p);
            if (p.Dead)
            {
                if (p.Feel.WasDead) continue;
                p.Feel.WasDead = true;
                // Those who saw it; a partner takes it hardest.
                foreach (Person q in People)
                {
                    if (q == p || q.Dead || q.Gone || q.Away || q.Indoors) continue;
                    float d = (q.Pos - p.Pos).Length();
                    if (d > 25f) continue;
                    Bind(q);
                    q.Feel.Hit(q.Partner == p || p.Partner == q ? MoodEvent.LostKin : MoodEvent.SawDeath, 1f - d / 25f);
                }
                continue;
            }
            bool atFire = false;
            foreach (Vector3 f in _fires) if (!p.Indoors && (f - p.Pos).Length() < 6f) { atFire = true; break; }
            p.Feel.Step(dt, atFire);
            if (p.Feel.Mood == PersonMood.Scared && p.Feel.Intent == MoodIntent.Flee && !p.Indoors) p.ScaredFor = MathF.Max(p.ScaredFor, 2f);
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
