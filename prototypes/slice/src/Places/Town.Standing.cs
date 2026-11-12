using System;
using System.Collections.Generic;
using Godot;
using PoiGenerator.Gen;
using Wildlife;

namespace Slice.Places;

/// <summary>What you did that a settlement remembers.</summary>
public enum Deed { Help, Trade, Thanks, Threat, Harm, Kill, Rob, Word }

/// <summary>
/// A settlement's standing with you (<see cref="PlaceMemory.Standing"/>, -100 to 100). Helping and
/// trading raise it; threats, harm and robbery drop it. It sets how they greet you, their prices
/// and whether their gates open: below <see cref="StandingWary"/> they treat you as a wary place
/// does (warned off at the checkpoint, held at gunpoint if you come on), at
/// <see cref="StandingBarred"/> the gates stay shut, and at <see cref="StandingHostile"/> they turn
/// on you. Robbing someone is possible and costly: they remember you, the word goes round the
/// place from one to the next, every new mouth it reaches costs a little more, and the guards who
/// hear come for you.
/// </summary>
public sealed partial class Town
{
    public const float StandingMin = -100f, StandingMax = 100f, StandingBarred = -50f, StandingHostile = -80f;

    /// <summary>How much each deed moves it.</summary>
    public static float Weight(Deed d) => d switch
    {
        Deed.Help => 6f, Deed.Trade => 2f, Deed.Thanks => 1f, Deed.Threat => -4f, Deed.Harm => -15f, Deed.Kill => -40f, Deed.Rob => -25f, Deed.Word => -1.5f, _ => 0f,
    };

    /// <summary>Their gates stay shut against you: shut in against danger, too low a standing, or turned hostile.</summary>
    public bool GatesShut => Barricaded || Standing <= StandingBarred || Memory.Mood == Mood.Hostile;

    /// <summary>What they ask for their goods, as a share of the usual: dearer the lower you stand, a little cheaper among friends.</summary>
    public float PriceFactor => Standing >= 0f ? 1f - 0.15f * Standing / StandingMax : 1f + 1.5f * -Standing / -StandingMin;

    /// <summary>Robberies here, and how many of the place have heard of yours (tests).</summary>
    public int Robberies { get; private set; }
    public int HeardOf => _knows.Count;
    readonly HashSet<Person> _knows = new();
    float _wordIn;

    /// <summary>Moves the place's standing with you by what you did.</summary>
    public void Shift(float by, Deed why)
    {
        Memory.Standing = Math.Clamp(Memory.Standing + by, StandingMin, StandingMax);
        StandingChanged?.Invoke(why, by);
        if (_place != null) MoodOf(why);
        if (Memory.Standing <= StandingHostile && Memory.Mood != Mood.Hostile) Provoked();
    }

    public void Did(Deed d) => Shift(Weight(d), d);

    public event Action<Deed, float> StandingChanged;

    /// <summary>The person you could rob now: one with their hands up to you, close enough to take from.</summary>
    public Person Robbable(Vector3 world, float reach = 2.6f)
    {
        if (_place == null) return null;
        Vector3 at = _place.FromWorld(world);
        foreach (Person p in People)
            if (!p.Dead && !p.Gone && !p.Indoors && !p.Away && !p.Robbed && p.Act is BodyAct.HandsUp or BodyAct.Cower && (p.Pos - at).Length() < reach) return p;
        return null;
    }

    /// <summary>
    /// You rob someone: whatever they carried is yours. They remember; everyone who saw it knows
    /// at once, the rest hear it in time; the standing falls; guards who know come for you.
    /// </summary>
    public void Rob(Person p)
    {
        p.Robbed = true;
        Robberies++;
        Did(Deed.Rob);
        p.ScaredFor = 8f;
        Learn(p);
        Vector3 at = p.Pos;
        foreach (Person q in People)
            if (q != p && !q.Dead && !q.Indoors && !q.Away && ((q.Pos - at).Length() < 14f || q.Role == "guard" && (q.Pos - at).Length() < 40f)) Learn(q); // the guards keep an eye on the streets
    }

    void Learn(Person q)
    {
        if (!_knows.Add(q)) return;
        // Each new mouth costs a little, up to the first ten: by then it's the talk of the place.
        if (_knows.Count > 1 && _knows.Count <= 11) Did(Deed.Word);
        // A guard who knows comes for you, gun up.
        if (q.Role == "guard") q.Pursue = 25f;
    }

    /// <summary>Word goes round: anyone who knows tells whoever they pass close by, now and then.</summary>
    void Word(float dt)
    {
        if (_knows.Count == 0 || (_wordIn -= dt) > 0f) return;
        _wordIn = 1f;
        var told = new List<Person>();
        foreach (Person k in _knows)
        {
            if (k.Dead || k.Indoors || k.Away) continue;
            foreach (Person q in People)
                if (!_knows.Contains(q) && !q.Dead && !q.Indoors && !q.Away && (q.Pos - k.Pos).Length() < 6f && _rng.NextDouble() < 0.35) told.Add(q);
        }
        foreach (Person q in told) Learn(q);
    }

    /// <summary>A guard who heard of a robbery: to the spot nearest you, then held on you there.</summary>
    bool Pursuit(Person p, Vector3 player, float dt)
    {
        if (p.Pursue <= 0f) return false;
        p.Pursue -= dt;
        if (p.Pursue <= 0f) { p.Holding = false; return false; }
        float away = (player - p.Pos).Length();
        if (away < 7f)
        {
            Vector3 d = player - p.Pos;
            p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-d.X, -d.Z), MathF.Min(1f, dt * 6f));
            p.Now = 0f;
            if (!p.Holding && Weapons.Ranged(p.Figure.Prop)) { p.Holding = true; HoldAims++; }
            return true;
        }
        if (p.Path == null || p.Step >= p.Path.Count)
        {
            int near = -1;
            float best = float.MaxValue;
            for (int i = 0; i < Spots.Count; i++)
            {
                float k = (At(i) - player).LengthSquared();
                if (k < best) { best = k; near = i; }
            }
            if (near >= 0 && near != p.Spot) GoTo(p, near);
            p.Speed = 2.6f;
        }
        Move(p, dt);
        return true;
    }
}
