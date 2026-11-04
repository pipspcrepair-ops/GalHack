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
    AssaultPlan Plan(BaseGrid grid, Cell from, Cell target);
}

/// <summary>
/// Dijkstra over the base grid where any standing block can be breached at a cost proportional to its
/// hit points. Invaders can't open player doors, so a door is just a weaker wall. The cheapest mix of
/// walking round and blasting through wins: a long detour loses to a wall, a short one to a door.
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

    public AssaultPlan Plan(BaseGrid grid, Cell from, Cell target)
    {
        from = grid.Clamp(from);
        var cost = new Dictionary<Cell, double> { [from] = 0 };
        var cameFrom = new Dictionary<Cell, Cell>();
        var queue = new PriorityQueue<Cell, double>();
        queue.Enqueue(from, 0);
        Cell? goal = null;

        while (queue.TryDequeue(out Cell c, out double soFar))
        {
            if (soFar > cost[c]) continue;
            if (IsGoal(grid, c, target)) { goal = c; break; }

            foreach (Cell d in Directions)
            {
                Cell n = c + d;
                if (!grid.InBounds(n) || n == target) continue;
                bool diagonal = d.X != 0 && d.Z != 0;
                Block blocker = grid.At(n);
                double step;
                if (blocker == null)
                {
                    // No squeezing diagonally between two blocks.
                    if (diagonal && (!grid.IsOpen(new Cell(c.X + d.X, c.Z)) || !grid.IsOpen(new Cell(c.X, c.Z + d.Z)))) continue;
                    step = diagonal ? 1.41421356 : 1;
                }
                else
                {
                    if (diagonal) continue; // breach straight through a face, never a corner
                    step = BreachCost(blocker);
                }

                double next = soFar + step;
                if (cost.TryGetValue(n, out double known) && known <= next) continue;
                cost[n] = next;
                cameFrom[n] = c;
                queue.Enqueue(n, next);
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

    /// <summary>Done when standing (or about to stand, once it's breached) next to the target's face.</summary>
    static bool IsGoal(BaseGrid grid, Cell c, Cell target) =>
        Math.Abs(c.X - target.X) + Math.Abs(c.Z - target.Z) == 1;
}
