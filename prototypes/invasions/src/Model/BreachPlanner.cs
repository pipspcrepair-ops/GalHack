using System;
using System.Collections.Generic;

namespace Invasions.Model;

/// <summary>A route to the core: the cells to walk, and the blocks that must be broken on the way, in order.</summary>
public sealed class AssaultPlan
{
    public bool Found { get; init; }
    public List<Cell> Cells { get; } = new();
    public List<int> BreachBlockIds { get; } = new();
    public double Cost { get; init; }
}

/// <summary>
/// Plans invaders' way to a target block through the base. PLACEHOLDER: the boarding prototype's A* on
/// block grids replaces this; anything implementing this interface can be dropped in.
/// </summary>
public interface IAssaultPlanner
{
    /// <param name="extraCost">Optional soft cost per cell, e.g. for cells other agents stand in.</param>
    AssaultPlan Plan(BaseGrid grid, Cell from, Cell target, Func<Cell, double> extraCost = null);
}

/// <summary>
/// A* over the base grid where any standing block can be breached at a cost proportional to its hit
/// points (walls and doors are passable at a cost). Invaders can't open player doors, so a door is just
/// a weaker wall. The cheapest mix of walking round and blasting through wins. Obstacles (crates, hulks,
/// rubble), already inflated by the agent's radius on the grid, can't be breached and are never entered,
/// and diagonal steps never cut a corner.
/// </summary>
public sealed class BreachPlanner : IAssaultPlanner
{
    readonly double _hpPerCell;

    /// <param name="hpPerCellOfDetour">A block with this many hit points costs as much as one extra cell of walking.</param>
    public BreachPlanner(double hpPerCellOfDetour) => _hpPerCell = Math.Max(1, hpPerCellOfDetour);

    static readonly Cell[] Directions =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    public double BreachCost(Block b) => 1 + b.Hp / _hpPerCell;

    /// <summary>Entering an obstacle cell: dearer than any reasonable detour.</summary>
    public const double ObstacleCost = 60;

    public AssaultPlan Plan(BaseGrid grid, Cell from, Cell target, Func<Cell, double> extraCost = null)
    {
        from = grid.Clamp(from);
        var cost = new Dictionary<Cell, double> { [from] = 0 };
        var cameFrom = new Dictionary<Cell, Cell>();
        var queue = new PriorityQueue<Cell, double>();
        queue.Enqueue(from, Heuristic(from, target));
        Cell? goal = null;

        while (queue.TryDequeue(out Cell c, out double priority))
        {
            double soFar = cost[c];
            if (priority > soFar + Heuristic(c, target) + 1e-9) continue; // stale entry
            if (IsGoal(grid, c, target)) { goal = c; break; }

            foreach (Cell d in Directions)
            {
                Cell n = c + d;
                if (!grid.InBounds(n) || n == target) continue;
                // Crates, hulks and rubble can't be breached: never planned through while any other way
                // exists, but not forbidden, so an agent shoved into one's margin still has a way out.
                double obstacle = grid.IsObstacle(n) ? ObstacleCost : 0;
                bool diagonal = d.X != 0 && d.Z != 0;
                Block blocker = grid.At(n);
                double step;
                if (blocker == null)
                {
                    // No squeezing diagonally past a block or an obstacle.
                    if (diagonal && (!grid.IsWalkable(new Cell(c.X + d.X, c.Z)) || !grid.IsWalkable(new Cell(c.X, c.Z + d.Z)))) continue;
                    step = diagonal ? 1.41421356 : 1;
                }
                else
                {
                    if (diagonal) continue; // breach straight through a face, never a corner
                    step = BreachCost(blocker);
                }
                step += obstacle;
                if (extraCost != null) step += extraCost(n);

                double next = soFar + step;
                if (cost.TryGetValue(n, out double known) && known <= next) continue;
                cost[n] = next;
                cameFrom[n] = c;
                queue.Enqueue(n, next + Heuristic(n, target));
            }
        }

        if (goal == null) return new AssaultPlan { Found = false };

        var plan = new AssaultPlan { Found = true, Cost = cost[goal.Value] };
        for (Cell c = goal.Value; ; c = cameFrom[c])
        {
            plan.Cells.Add(c);
            if (c == from) break;
        }
        plan.Cells.Reverse();
        foreach (Cell c in plan.Cells)
            if (grid.At(c) is Block b) plan.BreachBlockIds.Add(b.Id);
        return plan;
    }

    /// <summary>Octile distance to a cell beside the target: never more than the real cost, so A* stays exact.</summary>
    static double Heuristic(Cell c, Cell target)
    {
        int dx = Math.Abs(c.X - target.X), dz = Math.Abs(c.Z - target.Z);
        double octile = Math.Max(dx, dz) + 0.41421356 * Math.Min(dx, dz);
        return Math.Max(0, octile - 1.41421356);
    }

    /// <summary>Done when standing (or about to stand, once it's breached) next to the target's face.</summary>
    static bool IsGoal(BaseGrid grid, Cell c, Cell target) =>
        Math.Abs(c.X - target.X) + Math.Abs(c.Z - target.Z) == 1;
}
