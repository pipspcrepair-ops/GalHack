using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Personal space for every body, people and creatures alike: nobody walks through anybody. A
/// spatial grid finds each body's neighbours only (the cost stays bounded however many there
/// are), and overlapping pairs are pushed apart along the line between them, each by its share:
/// the one who gives way more moves more, a pushy or panicked one shoulders past by giving less
/// (never through), and the player, who gives nothing, is stepped round. Moving bodies coming up
/// on someone ahead bear to their own right, so two on a path pass on one side; where there is no
/// room to bear off (a doorway) the one who gives more waits a moment. A move that would put a
/// body somewhere it can't stand (each body's own say) is not made: the other takes the whole push.
/// Past touching, each keeps a distance of its kind (an arm's length for people, more from a stranger; a grazing herd loose,
/// a frightened one close): who is inside it is told which way there is room (<see cref="Body.Room"/>), to step that way
/// before anyone touches, and one coming up behind another going its way is told the gap left (<see cref="Body.Clear"/>), to
/// follow on at it rather than walk into them.
/// </summary>
public sealed class PersonalSpace
{
    public struct Body
    {
        /// <summary>Feet, its radius, which way it's heading (zero standing), and how much it gives way (0 never, 1 fully).</summary>
        public Vector3 Pos;
        public float Radius;
        public Vector3 Heading;
        public float Give;
        /// <summary>The distance it keeps past touching, m (0: none, it holds where it is).</summary>
        public float Keep;
        /// <summary>Out: how long it should wait before going on (a doorway), and the push it was given.</summary>
        public float Wait;
        public Vector3 Moved;
        /// <summary>Out: which way there is room from those inside the distance it keeps (flat, length 0 to 1: how far in they are).</summary>
        public Vector3 Room;
        /// <summary>Out: walking, the gap left to the one ahead going its way, m (MaxValue: nobody).</summary>
        public float Clear;
    }

    /// <summary>The distance people keep from each other past touching, m: about an arm's length.</summary>
    public const float ArmsLength = 0.3f;

    public readonly List<Body> Bodies = new();
    readonly Dictionary<long, List<int>> _grid = new();
    readonly Stack<List<int>> _spare = new();
    const float Cell = 2f;

    /// <summary>The deepest two bodies that give way sat in each other after the last pass, m (tests).</summary>
    public float WorstOverlap { get; private set; }
    public (int A, int B) WorstPair { get; private set; }

    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    /// <summary>Separates the bodies: a few passes of pushing pairs apart, then a nudge to the right for those coming face to face. <paramref name="open"/>: whether body i can stand at a point.</summary>
    public void Resolve(float dt, Func<int, Vector3, bool> open = null, int passes = 3)
    {
        int n = Bodies.Count;
        for (int pass = 0; pass < passes; pass++)
        {
            Index();
            for (int i = 0; i < n; i++)
            {
                Body a = Bodies[i];
                int cx = (int)MathF.Floor(a.Pos.X / Cell), cz = (int)MathF.Floor(a.Pos.Z / Cell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!_grid.TryGetValue(Key(cx + dx, cz + dz), out List<int> near)) continue;
                        foreach (int j in near)
                        {
                            if (j <= i) continue;
                            Body b = Bodies[j];
                            if (MathF.Abs(a.Pos.Y - b.Pos.Y) > 1.5f) continue; // another floor
                            Vector3 d = new(b.Pos.X - a.Pos.X, 0f, b.Pos.Z - a.Pos.Z);
                            float dist = d.Length(), want = a.Radius + b.Radius;
                            if (dist >= want) continue;
                            Vector3 dir = dist > 1e-4f ? d / dist : new Vector3(MathF.Cos(i * 2.4f + j), 0f, MathF.Sin(i * 2.4f + j));
                            float total = a.Give + b.Give;
                            if (total <= 0f) continue;
                            float overlap = want - dist;
                            Vector3 pa = -dir * overlap * a.Give / total, pb = dir * overlap * b.Give / total;
                            // One against a wall: the other takes the whole of it, if it can.
                            bool aOk = open == null || open(i, a.Pos + pa), bOk = open == null || open(j, b.Pos + pb);
                            if (!aOk && b.Give > 0f) { pb = dir * overlap; bOk = open == null || open(j, b.Pos + pb); }
                            else if (!bOk && a.Give > 0f) { pa = -dir * overlap; aOk = open == null || open(i, a.Pos + pa); }
                            // Both against something: the one who gives more steps off to the side instead.
                            if (!aOk && !bOk)
                            {
                                Vector3 side = new Vector3(-dir.Z, 0f, dir.X) * overlap * ((i + j) % 2 == 0 ? 1f : -1f);
                                if (a.Give >= b.Give && (open == null || open(i, a.Pos + side))) { pa = side; aOk = true; }
                                else if (open == null || open(j, b.Pos - side)) { pb = -side; bOk = true; }
                                // No room to part at all (a one-wide way): whoever is walking stops there and waits, never walks on through.
                                if (!aOk && !bOk)
                                {
                                    bool aWalks = a.Heading.LengthSquared() > 0.01f, bWalks = b.Heading.LengthSquared() > 0.01f;
                                    if (aWalks && (!bWalks || a.Give >= b.Give)) a.Wait = MathF.Max(a.Wait, 0.6f);
                                    else if (bWalks) b.Wait = MathF.Max(b.Wait, 0.6f);
                                }
                            }
                            if (aOk) { a.Pos += pa; a.Moved += pa; }
                            if (bOk) { b.Pos += pb; b.Moved += pb; }
                            Bodies[j] = b;
                        }
                    }
                Bodies[i] = a;
            }
        }
        // Past touching: the room from those inside the distance each keeps; coming up on someone ahead, a bearing to the right
        // (so a path is shared on one side), and behind one going the same way in its line, the gap left to follow at.
        Index();
        float most = 0f, keeps = 0f;
        foreach (Body b in Bodies) (most, keeps) = (MathF.Max(most, b.Radius), MathF.Max(keeps, b.Keep));
        for (int i = 0; i < n; i++)
        {
            Body a = Bodies[i];
            (a.Room, a.Clear) = (Vector3.Zero, float.MaxValue);
            bool walks = a.Heading.LengthSquared() > 0.01f && a.Give > 0f;
            if (!walks && a.Keep <= 0f) { Bodies[i] = a; continue; }
            int cx = (int)MathF.Floor(a.Pos.X / Cell), cz = (int)MathF.Floor(a.Pos.Z / Cell);
            int rings = Math.Clamp((int)MathF.Ceiling((a.Radius + most + MathF.Max(a.Keep > 0f ? keeps : 0f, walks ? 1.2f : 0f)) / Cell), 1, 3);
            Vector3 right = new(-a.Heading.Z, 0f, a.Heading.X);
            int bears = 0;
            bool rightTaken = false;
            for (int dx = -rings; dx <= rings; dx++)
                for (int dz = -rings; dz <= rings; dz++)
                {
                    if (!_grid.TryGetValue(Key(cx + dx, cz + dz), out List<int> near)) continue;
                    foreach (int j in near)
                    {
                        if (j == i) continue;
                        Body b = Bodies[j];
                        Vector3 d = new(b.Pos.X - a.Pos.X, 0f, b.Pos.Z - a.Pos.Z);
                        float dist = d.Length(), want = a.Radius + b.Radius;
                        if (dist < 1e-3f || MathF.Abs(a.Pos.Y - b.Pos.Y) > 1.5f) continue;
                        Vector3 dir = d / dist;
                        float keep = a.Keep > 0f ? MathF.Max(a.Keep, b.Keep) : 0f;
                        if (keep > 0f && dist < want + keep) a.Room -= dir * MathF.Min(1.5f, (want + keep - dist) / keep);
                        if (!walks) continue;
                        float ahead = a.Heading.Dot(dir);
                        if (dist <= want + 1.2f && ahead >= 0.7f) bears++;
                        rightTaken |= dist < want + 0.1f && dir.Dot(right) > 0.3f; // (no bearing into one beside)
                        // Face to face at touching on a narrow way (none walks into the other: they meet and stop): the one who gives more waits a moment.
                        if (dist < want + 0.15f && ahead > 0.6f && a.Heading.Dot(b.Heading) < -0.5f && (a.Give > b.Give || a.Give == b.Give && i < j)) a.Wait = MathF.Max(a.Wait, 0.6f);
                        if (ahead > 0.5f && b.Heading.Dot(a.Heading) > 0.5f && (d - a.Heading * (ahead * dist)).Length() < want) a.Clear = MathF.Min(a.Clear, dist - want);
                    }
                }
            if (a.Room.LengthSquared() > 1f) a.Room = a.Room.Normalized();
            for (int k = 0; k < (rightTaken ? 0 : bears); k++)
            {
                Vector3 step = right * 0.5f * dt;
                if (open == null || open(i, a.Pos + step)) { a.Pos += step; a.Moved += step; }
            }
            Bodies[i] = a;
        }
        // How deep anything still sits in anything else.
        Index();
        float worst = 0f;
        for (int i = 0; i < n; i++)
        {
            Body a = Bodies[i];
            int cx = (int)MathF.Floor(a.Pos.X / Cell), cz = (int)MathF.Floor(a.Pos.Z / Cell);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (_grid.TryGetValue(Key(cx + dx, cz + dz), out List<int> near))
                        foreach (int j in near)
                            if (j > i && a.Give > 0f && Bodies[j].Give > 0f && MathF.Abs(a.Pos.Y - Bodies[j].Pos.Y) < 1.5f)
                            {
                                float o = a.Radius + Bodies[j].Radius - new Vector2(a.Pos.X - Bodies[j].Pos.X, a.Pos.Z - Bodies[j].Pos.Z).Length();
                                if (o > worst) { worst = o; WorstPair = (i, j); }
                            }
        }
        WorstOverlap = worst;
    }

    /// <summary>
    /// A body's own step (from, to; its radius) held from going any further into another (at o, radius r) than it already is:
    /// the part of the step toward it goes, what is left goes along its side, dead on nothing (another floor: as it was).
    /// </summary>
    public static Vector3 Off(Vector3 from, Vector3 to, float radius, Vector3 o, float r)
    {
        if (MathF.Abs(o.Y - from.Y) > 1.5f) return to;
        Vector3 now = new(from.X - o.X, 0f, from.Z - o.Z), next = new(to.X - o.X, 0f, to.Z - o.Z);
        float d = next.Length();
        if (d >= radius + r || d >= now.Length()) return to; // (clear of it, or no further in)
        Vector3 n = now.LengthSquared() > 1e-6f ? now.Normalized() : next.LengthSquared() > 1e-6f ? next.Normalized() : Vector3.Right;
        Vector3 step = new(to.X - from.X, 0f, to.Z - from.Z);
        float into = step.Dot(n);
        if (into < 0f) step -= n * into;
        return new Vector3(from.X + step.X, to.Y, from.Z + step.Z);
    }

    /// <summary>The bodies near a point (within a cell or two), as the last <see cref="Resolve"/> left them (none it no longer holds).</summary>
    public void Near(Vector3 p, List<int> into)
    {
        into.Clear();
        int cx = (int)MathF.Floor(p.X / Cell), cz = (int)MathF.Floor(p.Z / Cell);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (_grid.TryGetValue(Key(cx + dx, cz + dz), out List<int> near))
                    foreach (int i in near)
                        if (i < Bodies.Count) into.Add(i);
    }

    void Index()
    {
        foreach (var list in _grid.Values) { list.Clear(); _spare.Push(list); }
        _grid.Clear();
        for (int i = 0; i < Bodies.Count; i++)
        {
            long k = Key((int)MathF.Floor(Bodies[i].Pos.X / Cell), (int)MathF.Floor(Bodies[i].Pos.Z / Cell));
            if (!_grid.TryGetValue(k, out List<int> list)) _grid[k] = list = _spare.Count > 0 ? _spare.Pop() : new List<int>();
            list.Add(i);
        }
    }
}
