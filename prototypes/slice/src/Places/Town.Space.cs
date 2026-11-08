using System;
using Godot;
using PoiGenerator.Gen;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Places;

/// <summary>
/// The town's people keep out of each other and out of you (<see cref="PersonalSpace"/>): each is
/// a body the width their folk is (a shell or a thick tail wider), pushed apart from anyone they
/// overlap, bearing right to pass on a path, waiting a moment face to face in a doorway. You give
/// nothing: walk into someone and they are moved aside by their weight and stance (a child
/// easily, a braced guard less), so you can get past someone blocking a door. They answer a push
/// by their job and how they stand with you: a step aside, a glare, a shove back; never a fight.
/// </summary>
public sealed partial class Town
{
    readonly PersonalSpace _space = new();
    readonly System.Collections.Generic.List<Person> _bodies = new();

    /// <summary>The deepest two of them (or one of them and you) have been in each other since this was last reset, m (tests); and pushes you've given.</summary>
    public float WorstOverlap;
    public int Pushed { get; private set; }

    /// <summary>A person's body radius, m: their folk's width.</summary>
    static float BodyRadius(Person p) => (p.Looks?.Stats.Radius ?? 0.35f) * 0.78f;

    /// <summary>How much someone gives way when bodies meet: a child easily, the panicked hardly (they shoulder past), a braced guard little, one at work or sitting less.</summary>
    static float Give(Person p) => p.Fleeing ? 0.35f
        : p.Figure?.Stage == LifeStage.Child ? 1.4f
        : p.Role == "guard" ? (p.Holding || p.Halt > 0f ? 0.3f : 0.55f)
        : p.Sitting ? 0.3f : p.Working ? 0.6f : 1f;

    bool Open(Vector3 local) => _nav == null || _nav.Standable(new PInt3((int)MathF.Floor(local.X), (int)MathF.Round(local.Y), (int)MathF.Floor(local.Z)));

    /// <summary>Once a tick, after everyone has moved: bodies apart, you stepped round, pushes answered.</summary>
    void KeepApart(Vector3 player, float dt)
    {
        _space.Bodies.Clear();
        _bodies.Clear();
        foreach (Person p in People)
        {
            if (p.Dead || p.Gone || p.Indoors || p.Away || p.Looks == null || _held.Contains(p)) continue;
            Vector3 heading = p.Now > 0.1f ? new Vector3(-MathF.Sin(p.Yaw), 0f, -MathF.Cos(p.Yaw)) : Vector3.Zero;
            _space.Bodies.Add(new PersonalSpace.Body { Pos = p.Pos, Radius = BodyRadius(p), Heading = heading, Give = Give(p) });
            _bodies.Add(p);
        }
        if (_bodies.Count == 0) return;
        // You are a body they keep off too (giving nothing); walking into them is the push rule's (Town.Colliders).
        if (_places.Zone.Wildlife.Player is { Attackable: true }) _space.Bodies.Add(new PersonalSpace.Body { Pos = player, Radius = Slice.Player.Survivor.Radius, Give = 0f });
        _space.Resolve(dt, Open, 4);
        WorstOverlap = MathF.Max(WorstOverlap, _space.WorstOverlap);
        for (int i = 0; i < _bodies.Count; i++)
        {
            Person p = _bodies[i];
            PersonalSpace.Body b = _space.Bodies[i];
            p.Pos = new Vector3(b.Pos.X, p.Pos.Y, b.Pos.Z);
            if (b.Wait > 0f) p.Yield = MathF.Max(p.Yield, b.Wait);
        }
    }

    /// <summary>A push from you: a step aside for a friend, a glare from the wary, a shove back from a guard or one who doesn't like you. Never a fight.</summary>
    void Shoved(Person p, Vector3 player)
    {
        Pushed++;
        p.ShovedFor = 2.5f;
        bool cold = Unwelcome || p.Robbed || Memory.Mood == Mood.Hostile;
        p.Answer = p.Role == "guard" || p.Job == PersonRole.Raider ? BodyAct.Warn : cold ? BodyAct.WaveOff : p.Figure.Stage == LifeStage.Child ? BodyAct.Startle : BodyAct.None;
        p.AnswerAt = _clock + 0.2f;
        p.AnswerFor = 1.6f;
        if (p.Answer == BodyAct.None) return;
        // A shove back: they lean into you, which only moves them, never you.
        if (p.Answer == BodyAct.Warn) p.Yield = MathF.Max(p.Yield, 0.8f);
    }
}
