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
/// body somewhere it can't stand is not made.
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
        /// <summary>Out: how long it should wait before going on (a doorway), and the push it was given.</summary>
        public float Wait;
        public Vector3 Moved;
    }

    public readonly List<Body> Bodies = new();
    readonly Dictionary<long, List<int>> _grid = new();
    readonly Stack<List<int>> _spare = new();
    const float Cell = 2f;

    /// <summary>The deepest two bodies that give way sat in each other after the last pass, m (tests).</summary>
    public float WorstOverlap { get; private set; }
    public (int A, int B) WorstPair { get; private set; }

    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    /// <summary>Separates the bodies: a few passes of pushing pairs apart, then a nudge to the right for those coming face to face.</summary>
    public void Resolve(float dt, Func<Vector3, bool> open = null, int passes = 3)
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
                            bool aOk = open == null || open(a.Pos + pa), bOk = open == null || open(b.Pos + pb);
                            if (!aOk && b.Give > 0f) { pb = dir * overlap; bOk = open == null || open(b.Pos + pb); }
                            else if (!bOk && a.Give > 0f) { pa = -dir * overlap; aOk = open == null || open(a.Pos + pa); }
                            // Both against something: the one who gives more steps off to the side instead.
                            if (!aOk && !bOk)
                            {
                                Vector3 side = new Vector3(-dir.Z, 0f, dir.X) * overlap * ((i + j) % 2 == 0 ? 1f : -1f);
                                if (a.Give >= b.Give && (open == null || open(a.Pos + side))) { pa = side; aOk = true; }
                                else if (open == null || open(b.Pos - side)) { pb = -side; bOk = true; }
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
                            // Face to face on a narrow way: the one who gives more waits a moment.
                            if (a.Heading.Dot(b.Heading) < -0.5f && a.Heading.Dot(dir) > 0.6f)
                            {
                                if (a.Give >= b.Give) a.Wait = MathF.Max(a.Wait, 0.6f);
                                else b.Wait = MathF.Max(b.Wait, 0.6f);
                            }
                            Bodies[j] = b;
                        }
                    }
                Bodies[i] = a;
            }
        }
        // Coming up on someone ahead: bear right, so a path is shared on one side.
        Index();
        for (int i = 0; i < n; i++)
        {
            Body a = Bodies[i];
            if (a.Heading.LengthSquared() < 0.01f || a.Give <= 0f) continue;
            int cx = (int)MathF.Floor(a.Pos.X / Cell), cz = (int)MathF.Floor(a.Pos.Z / Cell);
            Vector3 right = new(-a.Heading.Z, 0f, a.Heading.X);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_grid.TryGetValue(Key(cx + dx, cz + dz), out List<int> near)) continue;
                    foreach (int j in near)
                    {
                        if (j == i) continue;
                        Vector3 d = Bodies[j].Pos - a.Pos;
                        d.Y = 0f;
                        float dist = d.Length();
                        if (dist > a.Radius + Bodies[j].Radius + 1.2f || dist < 1e-3f || a.Heading.Dot(d / dist) < 0.7f) continue;
                        Vector3 step = right * 0.5f * dt;
                        if (open == null || open(a.Pos + step)) { a.Pos += step; a.Moved += step; }
                    }
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
