using System;
using Godot;

namespace Wildlife;

/// <summary>Where a band stands with an outsider now: going about their business, watching them, warning them off, in a fight, or running from something real.</summary>
public enum PartyMood { Easy, Watching, Warning, Fighting, Fleeing }

/// <summary>What a band's members each take in, in their own time, the leader first.</summary>
public enum PartyEvent { None, Seen, Greet, Warn, Fight, Flee, Calm }

/// <summary>
/// A band of people out in the world (a hunting party, pilgrims, a patrol, raiders): they go about
/// together round a leader and take to an outsider as people do, by the stance their makeup and
/// their world rolled (<see cref="Temper"/>), moved by your standing with their people and by what
/// you do. Friendly, they carry on and keep a comfortable distance; wary, they stop and watch, back
/// off, and warn you before anything else; hostile, they fight as one. Nobody runs from less than a
/// real threat. The leader reacts first, and the rest take it from them. Plain state: the brain
/// (<see cref="Brain"/>) runs it.
/// </summary>
public sealed class Party
{
    /// <summary>The temper the band rolled; the stance it takes with you now (the roll moved by standing, hostile once turned).</summary>
    public Temper Temper;
    public Stance Stance;
    public PartyMood Mood;
    public float MoodSince;
    /// <summary>Who they are dealing with, and whom they are fighting.</summary>
    public ITarget Outsider, Foe;
    /// <summary>Turned on you by harm (one of them hurt, or a friend nearby): it lasts while they remember you, and they never warm again.</summary>
    public bool Turned;
    /// <summary>When they last knew where their foe or the outsider was.</summary>
    public float FoeSeen, OutsiderSeen;
    /// <summary>The leader's warning: when, where you stood, and how many given.</summary>
    public float WarnedAt = -999f;
    public Vector3 WarnedFrom;
    public int Warnings;
    /// <summary>The leader came over to meet you, until when; you came within greeting this time.</summary>
    public float MeetUntil = -999f;
    public bool Greeted;
    /// <summary>Something they can't face (shot at, a gun on them up close, a Husk, a predator): where it was last; the last shot and harm taken in; since when a gun has been held on them; when you last threatened them.</summary>
    public Vector3 ThreatAt;
    public float ShotHeard = -999f, HarmSeen = -999f, AimedSince = -1f, Threatened = -999f;
    /// <summary>Losses enough to run: no new fight before this.</summary>
    public float BrokenUntil = -999f;
    /// <summary>The band's events: how many so far, the last, when the leader takes it in.</summary>
    public int Events;
    public PartyEvent Event;
    public float LeaderReactsAt;
    /// <summary>A gesture of yours seen: which, how many so far, when, and what it pointed at; what it asks of them, until when.</summary>
    public PlayerGesture Heard;
    public int Gestures;
    public float HeardAt, HoldUntil = -999f, BackUntil = -999f, LookUntil = -999f;
    public Vector3 HeardFrom, LookAt;
    public float UpdatedAt = -999f;
    /// <summary>Tests: members who took in an event after their leader had, and before.</summary>
    public int AfterLeader, BeforeLeader;
    /// <summary>Tests: a wary band's turns to a fight over what you did (not harm, not a shot).</summary>
    public int Fights;

    public Leaning Leaning => Temper.LeaningOf(Stance);

    /// <summary>A band's temper, from who they are (their world's tech and collapse, and their own makeup) and its own seed.</summary>
    public static Party Roll(Species s, ulong seed)
    {
        float lean = Lean(s, out float caution);
        Temper t = Temper.Roll(s.Tech, s.Collapse, seed, lean, caution);
        return new Party { Temper = t, Stance = t.Stance };
    }

    /// <summary>
    /// How a band's own makeup leans its temper, -1 to 1, by the bodies it is drawn from: raiders toward
    /// a fight; the kind's own temper (aggressive, bold; calm; skittish, toward keeping you off); arms
    /// toward caution; children and the old away from a fight. A band with no weapons among it never
    /// picks a fight: at worst it is wary.
    /// </summary>
    public static float Lean(Species s, out float caution)
    {
        int n = 0, armed = 0, frail = 0;
        bool raider = false;
        foreach (Species b in s.Hosts ?? new System.Collections.Generic.List<Species> { s })
        {
            if (b.Figure == null) continue;
            n++;
            if (Weapons.Armed(b.Figure.Prop)) armed++;
            if (b.Figure.Stage is LifeStage.Child or LifeStage.Elder) frail++;
            raider |= b.Figure.Occupancy == Occupancy.Raider;
        }
        float arms = n > 0 ? armed / (float)n : 0f, weak = n > 0 ? frail / (float)n : 0f;
        caution = Math.Clamp((s.Temperament switch { Temperament.Skittish => 0.6f, Temperament.Bold => -0.2f, Temperament.Calm => -0.3f, _ => 0f }) + 0.2f * arms, -1f, 1f);
        if (armed == 0) return -1f;
        float lean = (raider ? 0.75f : 0f) + (s.Temperament switch { Temperament.Aggressive => 0.15f, Temperament.Bold => 0.05f, Temperament.Calm => -0.3f, _ => -0.15f }) + 0.1f * arms - 0.5f * weak;
        return Math.Clamp(lean, -1f, 1f);
    }
}
