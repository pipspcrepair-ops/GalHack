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
    /// <summary>Who the worst overlap was between (tests).</summary>
    public string WorstNote = "";

    /// <summary>Your weight against theirs where bodies meet (your own share of a parting is never applied).</summary>
    const float PlayerGive = 0.6f;

    /// <summary>A person's body radius, m: their folk's width.</summary>
    static float BodyRadius(Person p) => (p.Looks?.Stats.Radius ?? 0.35f) * 0.78f;

    /// <summary>How much someone gives way when bodies meet: a child easily, the panicked hardly (they shoulder past), a braced guard little, one at work or sitting less.</summary>
    static float Give(Person p) => p.Fleeing ? 0.35f
        : p.Figure?.Stage == LifeStage.Child ? 1.4f
        : p.Role == "guard" ? (p.Holding || p.Halt > 0f ? 0.3f : 0.55f)
        : p.Sitting ? 0.3f : p.Working ? 0.6f : 1f;

    /// <summary>Whether a body can stand here: the cell at its feet, or on a stair the one below or above (between levels a walker's height is in between).</summary>
    // (The furniture is each body's own test, at its own size, after the move: OffParts.)
    bool Open(Vector3 local) => OnGrid(local);

    /// <summary>The cell at its feet (or on a stair the one below or above) is one the walking grid stands people in.</summary>
    bool OnGrid(Vector3 local)
    {
        if (_nav == null) return true;
        int x = (int)MathF.Floor(local.X), z = (int)MathF.Floor(local.Z);
        // (By the blocks alone: the furniture is each body's own test, at its own size.)
        return _nav.StandsOn(new PInt3(x, (int)MathF.Floor(local.Y + 0.05f), z)) || _nav.StandsOn(new PInt3(x, (int)MathF.Ceiling(local.Y - 0.05f), z));
    }

    /// <summary>How tall a person stands.</summary>
    static float Height(Person p) => p.Looks != null ? MathF.Max(1f, p.Figure.Height) : PlaceParts.PersonHeight;

    /// <summary>
    /// In the furniture on purpose: sitting or lying in it, or on the last step into or the first out of the
    /// seat or bed they use (that piece is what they are in; nothing else of the room is).
    /// </summary>
    bool InUse(Person p) => p.Sitting || p.GettingUp || p.Asleep || p.Slept > 0f || p.Indoors || IntoSeat(p) || InSeat(p, p.Left)
        || p.Seat >= 0 || p.Path != null && p.Step >= p.Path.Count - 2 && p.Spot >= 0 && p.Spot < _own.Count && _own[p.Spot] != null;

    /// <summary>How deep a person's body is in the place's furniture or clutter, m (0: clear, as it should always be; tests).</summary>
    public float InParts(Person p) => _parts == null || InUse(p) ? 0f : MathF.Max(0f, -_parts.Gap(p.Pos, BodyRadius(p), Height(p)));

    /// <summary>The place's furniture and clutter as people go round it (null in the old whole-cell rule; tests).</summary>
    public Wildlife.PartSet Parts => _parts;

    /// <summary>
    /// A person's body held off the place's parts after a move: pushed out sideways where it went in, or back
    /// where it was when that can't be done (a step into a gap too narrow for them waits rather than goes through).
    /// </summary>
    void OffParts(Person p, Vector3 was)
    {
        if (_parts == null) return;
        float r = BodyRadius(p), h = Height(p);
        // Getting up: out the way they sat down, till clear of the seat and the table.
        if (p.GettingUp && _parts.Gap(p.Pos, r, h) >= 0f) p.GettingUp = false;
        if (InUse(p)) return;
        float gap = _parts.Gap(p.Pos, r, h);
        if (gap >= 0f) return;
        Vector3 to = _parts.PushOut(p.Pos, r, h);
        to.Y = p.Pos.Y;
        float after = _parts.Gap(to, r, h);
        if (after >= 0f && OnGrid(to)) { p.Pos = to; return; }
        // No way clear: never deeper in than it was (one set down in a piece gets out of it by any way not further in).
        Vector3 best = p.Pos;
        float most = gap;
        if (after > most && OnGrid(to)) { best = to; most = after; }
        p.Pos = most >= _parts.Gap(was, r, h) - 0.005f ? best : was;
    }

    /// <summary>Once a tick, after everyone has moved: bodies apart, you stepped round, pushes answered.</summary>
    void KeepApart(Vector3 player, float dt)
    {
        _space.Bodies.Clear();
        _bodies.Clear();
        foreach (Person p in People)
        {
            if (p.Dead || p.Gone || p.Indoors || p.Away || p.Looks == null || _held.Contains(p)) continue;
            // One stepping back to let another by is still on their way: counted standing, they would have the other wait in
            // turn, and the two would take turns giving way in a narrow way for good.
            bool giving = p.Yield > 0f && p.Path != null && p.Step < p.Path.Count;
            Vector3 heading = p.Now > 0.1f || giving ? new Vector3(-MathF.Sin(p.Yaw), 0f, -MathF.Cos(p.Yaw)) : Vector3.Zero;
            _space.Bodies.Add(new PersonalSpace.Body { Pos = p.Pos, Radius = BodyRadius(p), Heading = heading, Give = Give(p) });
            _bodies.Add(p);
        }
        if (_bodies.Count == 0) return;
        // You are a body they keep off too (you aren't moved: your share is dropped); walking into them is the push rule's
        // (Town.Colliders). Your share isn't nothing, so one in you is moved off you at their own give: a braced guard least.
        if (_places.Zone.Wildlife.Player is { Attackable: true }) _space.Bodies.Add(new PersonalSpace.Body { Pos = player, Radius = Slice.Player.Survivor.Radius - 0.03f, Give = PlayerGive }); // (just inside their capsules' touch)
        _space.Resolve(dt, Open, 4);
        // (Someone who moved a long way at once, far off and updated now and then, can land on another: settle it now.)
        if (_space.WorstOverlap > 0.04f) _space.Resolve(0f, Open, 6);
        if (_space.WorstOverlap > WorstOverlap)
        {
            WorstOverlap = _space.WorstOverlap;
            var (wa, wb) = _space.WorstPair;
            string Who(int k) => k < _bodies.Count ? $"{_bodies[k].Job} at {_bodies[k].Pos.X:F1},{_bodies[k].Pos.Y:F1},{_bodies[k].Pos.Z:F1} {(Open(_bodies[k].Pos) ? "open" : "in a wall")}{(_bodies[k].Sitting ? " sitting" : "")}{(_bodies[k].Now > 0.1f ? " walking" : "")}" : "you";
            WorstNote = $"{Who(wa)} and {Who(wb)}";
        }
        for (int i = 0; i < _bodies.Count; i++)
        {
            Person p = _bodies[i];
            PersonalSpace.Body b = _space.Bodies[i];
            Vector3 was = p.Pos;
            p.Pos = new Vector3(b.Pos.X, p.Pos.Y, b.Pos.Z);
            OffParts(p, was);
            if (b.Wait > 0f) p.Yield = MathF.Max(p.Yield, b.Wait);
        }
        // Two stood still in each other with no room to part (sent to the one spot in a narrow way): the second goes elsewhere.
        if (_space.WorstOverlap > 0.05f)
            for (int i = 0; i < _bodies.Count; i++)
            {
                Person p = _bodies[i];
                if (p.Now > 0.1f || p.Sitting || p.Path != null && p.Step < p.Path.Count) continue;
                for (int j = 0; j < i; j++)
                {
                    Person o = _bodies[j];
                    if (MathF.Abs(o.Pos.Y - p.Pos.Y) > 1.5f) continue;
                    float d = new Vector2(o.Pos.X - p.Pos.X, o.Pos.Z - p.Pos.Z).Length();
                    if (d >= BodyRadius(o) + BodyRadius(p) - 0.05f) continue;
                    p.Wait = 0f;
                    p.Partner = null;
                    break;
                }
            }
    }

    // ---- Walking someone through a place (tests) -------------------------------------------------

    /// <summary>A place's walking grid with nobody living there yet, to walk people through it one by one (tests).</summary>
    public static Town Walking(PlaceMemory m)
    {
        var town = new Town(m, new Townsfolk());
        town._nav = Place.FootNavOf(m);
        town._parts = PlaceParts.Of(m);
        return town;
    }

    /// <summary>Whether a person can be stood at a point by the walking grid.</summary>
    public bool CanStand(Vector3 local) => OnGrid(local);

    /// <summary>Someone of a given folk set at a point with a route to another by the walking grid (null: no route).</summary>
    public Person Walker(Species looks, Vector3 from, Vector3 to)
    {
        if (_nav == null) return null;
        PInt3 a = Cell(from), b = Cell(to);
        if (!_nav.Standable(a) || !_nav.Standable(b)) return null;
        System.Collections.Generic.List<PInt3> cells = _nav.FindPath(a, b, 30000);
        if (cells == null) return null;
        var path = Points(cells);
        path.Add(to);
        return new Person { Town = this, Looks = looks, Pos = from, Path = path, Step = 0, Spot = -1, Speed = 1.3f, Spawn = new CitizenSpawn("walker", "walk", a, -1) };
    }

    /// <summary>A walker's step along their route, as the town walks its people (true once there).</summary>
    public bool StepWalker(Person p, float dt) => Move(p, dt);

    /// <summary>A person's body radius (their folk's width) and how tall they stand.</summary>
    public static float RadiusOf(Person p) => BodyRadius(p);
    public static float HeightOf(Person p) => Height(p);
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
