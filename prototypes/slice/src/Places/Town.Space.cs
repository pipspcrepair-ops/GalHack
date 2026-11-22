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
    Func<int, Vector3, bool> _openAt;
    readonly System.Collections.Generic.List<Person> _bodies = new();

    /// <summary>The deepest two of them (or one of them and you) have been in each other since this was last reset, m (tests); and pushes you've given.</summary>
    public float WorstOverlap;
    public int Pushed { get; private set; }
    /// <summary>Who the worst overlap was between (tests).</summary>
    public string WorstNote = "";

    /// <summary>The fastest two bodies in each other, or a body and a piece of furniture, are parted, m/s.</summary>
    const float PartPace = 3f;

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
    bool Open(Vector3 local) => OnGrid(local) || Away(local) > 0f; // (past the place's cells: the open land, where a run goes)

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
    bool InUse(Person p) => p.Sitting || p.GettingUp || p.Asleep || p.Slept > 0f || IntoSeat(p) || InSeat(p, p.Left)
        || p.Seat >= 0 || p.Path != null && p.Step >= p.Path.Count - 2 && p.Spot >= 0 && p.Spot < _own.Count && _own[p.Spot] != null;

    /// <summary>How deep a person's body is in the place's furniture or clutter, m (0: clear, as it should always be; tests).</summary>
    public float InParts(Person p) => _parts == null || InUse(p) ? 0f : MathF.Max(0f, -_parts.Gap(p.Pos, BodyRadius(p), Height(p)));

    /// <summary>The place's furniture and clutter as people go round it (null in the old whole-cell rule; tests).</summary>
    public Wildlife.PartSet Parts => _parts;

    /// <summary>
    /// A person's body held off the place's parts after a move: pushed out sideways where it went in, by the least way out and
    /// at a walk at most (one wedged in a piece comes out of it over a few ticks, never in one), or back where it was when that
    /// can't be done (a step into a gap too narrow for them waits rather than goes through).
    /// </summary>
    void OffParts(Person p, Vector3 was, float dt)
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
        if (after >= 0f && OnGrid(to)) { p.Pos = p.Pos.MoveToward(to, PartPace * dt); return; }
        // No way clear: never deeper in than it was (one set down in a piece gets out of it by any way not further in).
        Vector3 best = p.Pos;
        float most = gap;
        if (after > most && OnGrid(to)) { best = p.Pos.MoveToward(to, PartPace * dt); most = _parts.Gap(best, r, h); }
        p.Pos = most >= _parts.Gap(was, r, h) ? best : was;
    }

    /// <summary>The distance people keep from you past touching, m: more than from each other (an arm's length), you being a stranger.</summary>
    const float FromStranger = 0.6f;

    /// <summary>How far someone keeps from others: an arm's length, close in a fright; none at their seat, bed or work (they hold their place; others keep off them).</summary>
    float Spacing(Person p) => InUse(p) || p.Working && (p.Path == null || p.Step >= p.Path.Count) || p.Engaged ? 0f : p.Fleeing ? 0.1f : PersonalSpace.ArmsLength;

    /// <summary>The pace someone walks at behind another going their way: on at the other's pace an arm's length back, never into them.</summary>
    static float Following(Person p, float wanted, float pace) => p.Clear < PersonalSpace.ArmsLength ? MathF.Min(wanted, pace * MathF.Max(0f, p.Clear) / PersonalSpace.ArmsLength) : wanted;

    /// <summary>How fast someone steps off into room when another is inside the distance they keep, m/s (a shuffle, not a shove).</summary>
    const float RoomPace = 0.5f;

    /// <summary>How far into the furniture each of those kept apart stands this tick (NaN: not yet asked).</summary>
    readonly System.Collections.Generic.List<float> _gapAt = new();

    /// <summary>Whether body i of those kept apart may be at a point by the floor: on the place's walking grid, or out on the land if out on a run (or you).</summary>
    bool Floor(int i, Vector3 at) => OnGrid(at) || (i >= _bodies.Count || _bodies[i].Run != null) && Away(at) > 0f;

    /// <summary>Whether body i of those kept apart can be put at a point: on the floor, and no further into the furniture than it is (else the other takes the push).</summary>
    bool Stands(int i, Vector3 at)
    {
        if (!Floor(i, at)) return false;
        if (i >= _bodies.Count || _parts == null || InUse(_bodies[i])) return true;
        Person p = _bodies[i];
        float r = BodyRadius(p), h = Height(p);
        if (float.IsNaN(_gapAt[i])) _gapAt[i] = MathF.Min(0f, _parts.Gap(p.Pos, r, h)); // (where it stands, once a tick)
        return _parts.Gap(at, r, h) >= _gapAt[i];
    }

    /// <summary>The last tick's keeping apart, ms (measurements).</summary>
    public float LastApartMs { get; private set; }

    /// <summary>Once a tick, after everyone has moved: bodies apart, you stepped round, pushes answered (<paramref name="all"/>: else only those out on runs, the place down).</summary>
    void KeepApart(Vector3 player, float dt, bool all = true)
    {
        long from = System.Diagnostics.Stopwatch.GetTimestamp();
        _space.Bodies.Clear();
        _bodies.Clear();
        _gapAt.Clear();
        foreach (Person p in People)
        {
            if (p.Dead || p.Gone || p.Asleep || p.Looks == null || _held.Contains(p) || !all && p.Run == null) continue;
            // One stepping back to let another by, or stopped a moment for one in the way, is still on their way: counted standing,
            // two face to face would never sort out who gives way in a narrow way.
            bool going = (p.Yield > 0f || p.StuckFor > 0f) && p.Path != null && p.Step < p.Path.Count;
            Vector3 heading = p.Now > 0.1f || going ? new Vector3(-MathF.Sin(p.Yaw), 0f, -MathF.Cos(p.Yaw)) : Vector3.Zero;
            _space.Bodies.Add(new PersonalSpace.Body { Pos = p.Pos, Radius = BodyRadius(p), Heading = heading, Give = Give(p), Keep = Spacing(p) });
            _bodies.Add(p);
            _gapAt.Add(float.NaN);
        }
        if (_bodies.Count == 0) return;
        // You are a body they keep off too (you aren't moved: your share is dropped); walking into them is the push rule's
        // (Town.Colliders). Your share isn't nothing, so one in you is moved off you at their own give: a braced guard least.
        if (_places.Zone.Wildlife.Player is { Attackable: true }) _space.Bodies.Add(new PersonalSpace.Body { Pos = player, Radius = Slice.Player.Survivor.Radius - 0.03f, Give = PlayerGive, Keep = FromStranger }); // (just inside their capsules' touch)
        _space.Resolve(dt, _openAt ??= Stands, 4);
        if (_space.WorstOverlap > WorstOverlap)
        {
            WorstOverlap = _space.WorstOverlap;
            var (wa, wb) = _space.WorstPair;
            string Who(int k) => k < _bodies.Count ? $"{_bodies[k].Job} at {_bodies[k].Pos.X:F1},{_bodies[k].Pos.Y:F1},{_bodies[k].Pos.Z:F1} {(Open(_bodies[k].Pos) ? "open" : "in a wall")}{(_bodies[k].Sitting ? " sitting" : "")}{(_bodies[k].Now > 0.1f ? " walking" : "")}" : "you";
            WorstNote = $"{Who(wa)} and {Who(wb)}";
        }
        // (You shove at your own pace: one you walk into is moved off you as fast as you come on, or you would be in them.)
        float yourPace = _hadPlayer && dt > 0f ? MathF.Max(PartPace, new Vector2(player.X - _lastPlayer.X, player.Z - _lastPlayer.Z).Length() / dt) : PartPace;
        for (int i = 0; i < _bodies.Count; i++)
        {
            Person p = _bodies[i];
            PersonalSpace.Body b = _space.Bodies[i];
            Vector3 was = p.Pos, by = new(b.Pos.X - was.X, 0f, b.Pos.Z - was.Z);
            // Someone inside the distance they keep: a step off into the room there is, before they touch.
            p.Clear = b.Clear;
            Vector3 room = b.Room * RoomPace * dt;
            if (room.LengthSquared() > 1e-10f && Floor(i, b.Pos + room)) by += Unpressed(p, b.Pos, b.Pos + room) - b.Pos; // (into nobody else)
            // Parted at a walk at most, over as many ticks as it takes: never shoved across the floor at once.
            bool onYou = new Vector2(was.X - player.X, was.Z - player.Z).Length() < BodyRadius(p) + Slice.Player.Survivor.Radius + 0.15f;
            float most = (onYou ? yourPace + PartPace : PartPace) * dt;
            p.Pos = was + (by.LengthSquared() > most * most ? by.Normalized() * most : by);
            OffParts(p, was, 0f); // (never parted into furniture: back where they were if it would)
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
        LastApartMs = (float)System.Diagnostics.Stopwatch.GetElapsedTime(from).TotalMilliseconds;
    }

    // ---- Walking someone through a place (tests) -------------------------------------------------

    /// <summary>A place's walking grid with nobody living there yet, to walk people through it one by one (tests).</summary>
    public static Town Walking(PlaceMemory m)
    {
        var town = new Town(m, new Townsfolk());
        town._nav = Place.FootNavOf(m);
        town._parts = PlaceParts.Of(m);
        town._fixed = PlaceParts.Fixed(m);
        town._ways = DoorWays.Of(m);
        town.Passages(m);
        m.Bodies.Add(town.BodiesNear);
        return town;
    }

    /// <summary>Walkers held a second on their way who stopped where they got to short of a spot others crowd, stepped aside, or found another way (tests).</summary>
    public int Settled { get; private set; }
    public int SteppedAside { get; private set; }
    public int Rerouted { get; private set; }

    /// <summary>Held up on their way this long, s: they stop and look at what holds them; then step aside once; then find another way.</summary>
    const float HeldLook = 1f, HeldAside = 2f, HeldWay = 3f;

    /// <summary>
    /// After a step along their way: someone who hasn't got a quarter metre on in a second is never moved for it (owner, of a
    /// set-back: "That nudge seems way too aggressive"), nor flicked from one way to another (owner: "npc's can get really twitchy
    /// when figuring out which way to path"). Near the end of their way with someone standing at its end (a crowd at a refuge),
    /// they stop where they got to. Else they stop and look at whoever is in their way a moment (one in the way mostly moves on),
    /// then step aside once off what holds them toward where they go, and only held again after that find another way from where
    /// they stand by the walking grid, kept till it too is held (bodies give way to each other meanwhile: PersonalSpace).
    /// </summary>
    void Headway(Person p, float dt)
    {
        // (A fighter's way is its fight's to pick, a few times a second: closing on you, held off you by your body, is no stuck walk.)
        if (p.Engaged) { p.ProgressAt = p.Pos; p.StuckFor = 0f; return; }
        if ((p.Pos - p.ProgressAt).LengthSquared() > 0.0625f)
        {
            p.ProgressAt = p.Pos;
            p.StuckFor = 0f;
            return;
        }
        float was = p.StuckFor;
        if ((p.StuckFor += dt) < HeldLook) return;
        if (Settles(p)) return;
        // Held a hand's breadth short of the end of their way (by the furniture, or someone): as near as they get is there.
        if (p.Path is { Count: > 0 } way && p.Step >= way.Count - 1 && Flat(way[^1] - p.Pos) < 0.3f && !ToSeat(p)) { (p.Step, p.StuckFor) = (way.Count, 0f); OffParts(p, p.Pos, dt); return; }
        // Someone in the way: stood still a moment, looking at them, rather than pressing on into them. (Held by the furniture: aside at once.)
        if (p.StuckFor < HeldAside && InTheWay(p) is { } o)
        {
            (p.Now, p.LookAt, p.LookFor) = (0f, o.Pos + Vector3.Up * 1.5f, 0.5f);
            return;
        }
        // Once on this way, a step aside off whatever holds them (one in the way, a table's corner, a bed's foot), toward where they go.
        if (!p.Held)
        {
            p.Held = true;
            if (Aside(p) is { } aside)
            {
                var on = new System.Collections.Generic.List<Vector3> { aside };
                if (p.Path != null && p.Step < p.Path.Count) on.AddRange(p.Path.GetRange(p.Step, p.Path.Count - p.Step));
                (p.Path, p.Step) = (on, 0);
                SteppedAside++;
                return;
            }
        }
        // Every few seconds still held, another way from where they stand; and one stood about in their way (not at a seat or at work) goes on
        // about their day, as anyone stood in a doorway moves for someone waiting to get by. Nobody walks through anybody.
        if ((int)(was / HeldWay) == (int)(p.StuckFor / HeldWay)) return;
        if (InTheWay(p) is { Now: < 0.1f } blocker && Spacing(blocker) > 0f && (blocker.Path == null || blocker.Step >= blocker.Path.Count)) blocker.Wait = MathF.Min(blocker.Wait, 0f);
        if (p.Spot < 0 || p.Spot >= Spots.Count) return;
        System.Collections.Generic.List<Vector3> path = PathFromHere(p, p.Spot, out bool pending);
        if (pending) { p.Path = new System.Collections.Generic.List<Vector3>(); p.Asked = p.Spot; }
        else if (path is { Count: > 0 }) p.Path = path;
        p.Step = 0;
        Rerouted++;
    }

    readonly System.Collections.Generic.List<int> _nearby = new();

    /// <summary>
    /// Someone's own step held from going any further into anyone else, you among them (as a creature's is, WildSim.Unpressed):
    /// what would take them into another goes along their side, dead on not at all, whichever way the other is going.
    /// </summary>
    Vector3 Unpressed(Person p, Vector3 from, Vector3 to)
    {
        _space.Near(to, _nearby);
        float r = BodyRadius(p);
        foreach (int j in _nearby)
        {
            if (j >= _bodies.Count) { to = PersonalSpace.Off(from, to, r, _space.Bodies[j].Pos, _space.Bodies[j].Radius); continue; } // (you)
            Person o = _bodies[j];
            if (o == p || o.Dead || o.Gone || o.Asleep) continue;
            to = PersonalSpace.Off(from, to, r, o.Pos, BodyRadius(o));
        }
        return to;
    }

    /// <summary>Near the end of their way with someone standing at its end (a crowd at a spot, at a refuge): they stop where they got to (true), if it is floor (never in a bed's cells).</summary>
    bool Settles(Person p)
    {
        if (p.Path is not { Count: > 0 } way || p.Step < way.Count - 1 || !OnGrid(p.Pos) || (way[^1] - p.Pos).Length() >= 2.5f || !Taken(p, way[^1])) return false;
        (p.Step, p.StuckFor) = (way.Count, 0f);
        Settled++;
        return true;
    }

    /// <summary>The nearest one standing or walking a step ahead of someone, in their way (null: nobody).</summary>
    Person InTheWay(Person p)
    {
        Person best = null;
        float near = BodyRadius(p) + 0.9f;
        foreach (Person o in People)
        {
            if (o == p || o.Dead || o.Gone || o.Asleep || o.Sitting || MathF.Abs(o.Pos.Y - p.Pos.Y) > 1.5f) continue;
            Vector3 d = (o.Pos - p.Pos) with { Y = 0f };
            float l = d.Length();
            if (l < near && l > 1e-3f && d.Dot(p.Forward) > 0.6f * l) (best, near) = (o, l);
        }
        return best;
    }

    /// <summary>Someone else stands at a point (local): it is taken.</summary>
    bool Taken(Person p, Vector3 at)
    {
        foreach (Person o in People)
            if (o != p && !o.Dead && !o.Gone && o.Now < 0.3f && MathF.Abs(o.Pos.Y - at.Y) < 1.5f
                && new Vector2(o.Pos.X - at.X, o.Pos.Z - at.Z).Length() < BodyRadius(o) + BodyRadius(p) + 0.1f) return true;
        return false;
    }

    /// <summary>A point half a metre or so off where someone stands, clear of the parts and walked to straight, nearest where they go (null: none).</summary>
    Vector3? Aside(Person p)
    {
        Vector3 to = p.Path != null && p.Step < p.Path.Count ? p.Path[p.Step] : p.Spot >= 0 && p.Spot < Spots.Count ? At(p.Spot) : p.Pos;
        float r = BodyRadius(p), h = Height(p), best = float.MaxValue;
        Vector3? found = null;
        for (int k = 0; k < 8; k++)
            foreach (float d in new[] { 0.5f, 0.9f })
            {
                Vector3 c = p.Pos + new Vector3(MathF.Sin(k * MathF.Tau / 8f), 0f, MathF.Cos(k * MathF.Tau / 8f)) * d;
                if (!OnGrid(c) || !Straight(p, c) || _parts != null && _parts.Gap(c, r, h) < 0.05f) continue;
                float score = (to - c).Length();
                if (score < best) (best, found) = (score, c);
            }
        return found;
    }

    /// <summary>
    /// Where someone frightened by something at a point gets in: their own home if it isn't that way (farther from it than
    /// they are, and the way there not passing close by it), else whichever of the place's home spots that isn't lies farthest from it
    /// and fewest others are making for.
    /// </summary>
    int Refuge(Person p, Vector3 danger)
    {
        float mine = (p.Pos - danger).Length();
        bool Wide(int h)
        {
            Vector3 a = p.Pos, ab = At(h) - a;
            float t = ab.LengthSquared() > 1e-4f ? Math.Clamp((danger - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            return (At(h) - danger).Length() > mine && (a + ab * t - danger).Length() > 3f;
        }
        if (_homes.Length == 0 || Wide(p.Home) && Whole(p.Home)) return p.Home;
        int best = p.Home;
        float most = float.MinValue;
        foreach (int h in _homes)
        {
            // (One others already make for counts for less: a crowd at the one spot wedges them all in a narrow way.)
            float score = (At(h) - danger).Length() - 0.3f * (At(h) - p.Pos).Length() - 4f * Making(p, h);
            if (score > most && Wide(h) && Whole(h)) { most = score; best = h; }
        }
        return best;
    }

    /// <summary>How many others are making for a spot (a refuge, a seat for the night).</summary>
    int Making(Person p, int spot)
    {
        int n = 0;
        foreach (Person o in People) if (o != p && o.Refuge == spot && !o.Dead) n++;
        return n;
    }

    /// <summary>Whether a body gets straight from where they stand to a point (local) by the walking grid: floor all the way (no wall, no gap between).</summary>
    bool Straight(Person p, Vector3 to)
    {
        Vector3 d = (to - p.Pos) with { Y = 0f };
        float len = d.Length();
        for (float s = 0.5f; s < len; s += 0.5f)
            if (!OnGrid(p.Pos + d * (s / len))) return false;
        return true; // (floor all the way to where it stops: the furniture its body goes round as it walks, OffParts)
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
        var walker = new Person { Town = this, Looks = looks, Pos = from, Path = path, Step = 0, Spot = -1, Speed = 1.3f, Spawn = new CitizenSpawn("walker", "walk", a, -1) };
        People.Add(walker);
        return walker;
    }

    /// <summary>A walker's step along their route, as the town walks its people (true once there).</summary>
    public bool StepWalker(Person p, float dt)
    {
        _clock += dt;
        bool there = Move(p, dt);
        TickWays(dt);
        return there;
    }

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
