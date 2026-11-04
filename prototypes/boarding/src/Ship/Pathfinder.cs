using System;
using System.Collections.Generic;
using Godot;

namespace Boarding;

/// <summary>
/// A* over a ship's block grid. The caller says which cells are passable right now, so
/// doors, sealed bulkheads and blown-out walls all fall out of the same question: a locked
/// door is simply impassable until it opens, and a breached hull plate becomes a corridor.
///
/// Eight-way moves, but never across a corner: a diagonal step needs both of the
/// orthogonal cells beside it open, so guards don't clip wall edges or squeeze past doors.
/// </summary>
public static class Pathfinder
{
    const float Straight = 1f, Diagonal = 1.41421356f;

    static readonly Vector2I[] Steps =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    /// <summary>
    /// Cells from start to goal inclusive, or null if the goal can't be reached.
    /// The start cell itself is allowed to be impassable (a guard standing in a doorway
    /// that just sealed can still walk out of it).
    /// </summary>
    /// <param name="extraCost">Optional extra cost for stepping into a cell (crowding), never negative.</param>
    public static List<Vector2I> FindPath(Vector2I start, Vector2I goal, Func<Vector2I, bool> passable,
        Func<Vector2I, float> extraCost = null, int maxNodes = 4000)
    {
        if (start == goal) return new List<Vector2I> { start };
        if (!passable(goal)) return null;

        var open = new PriorityQueue<Vector2I, float>();
        var cameFrom = new Dictionary<Vector2I, Vector2I>();
        var cost = new Dictionary<Vector2I, float> { [start] = 0f };
        var closed = new HashSet<Vector2I>();
        open.Enqueue(start, Heuristic(start, goal));

        while (open.Count > 0 && closed.Count < maxNodes)
        {
            Vector2I current = open.Dequeue();
            if (current == goal) return Rebuild(cameFrom, current);
            if (!closed.Add(current)) continue;

            foreach (Vector2I step in Steps)
            {
                Vector2I next = current + step;
                if (closed.Contains(next) || !passable(next)) continue;
                bool diagonal = step.X != 0 && step.Y != 0;
                if (diagonal && (!passable(new Vector2I(next.X, current.Y)) || !passable(new Vector2I(current.X, next.Y))))
                    continue;

                float g = cost[current] + (diagonal ? Diagonal : Straight) + (extraCost?.Invoke(next) ?? 0f);
                if (cost.TryGetValue(next, out float known) && known <= g) continue;
                cost[next] = g;
                cameFrom[next] = current;
                open.Enqueue(next, g + Heuristic(next, goal));
            }
        }
        return null;
    }

    /// <summary>Octile distance: exact on an open 8-way grid, so A* stays optimal.</summary>
    static float Heuristic(Vector2I a, Vector2I b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return Straight * Math.Max(dx, dy) + (Diagonal - Straight) * Math.Min(dx, dy);
    }

    static List<Vector2I> Rebuild(Dictionary<Vector2I, Vector2I> cameFrom, Vector2I end)
    {
        var path = new List<Vector2I> { end };
        while (cameFrom.TryGetValue(end, out var previous))
        {
            end = previous;
            path.Add(end);
        }
        path.Reverse();
        return path;
    }
}
