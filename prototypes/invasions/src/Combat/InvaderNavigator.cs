using System.Collections.Generic;
using Godot;
using Invasions.Director;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// PLACEHOLDER navigation for one invader on foot; the boarding prototype's A* and steering replace it.
/// Kept deliberately simple and robust:
/// - Plans on the base grid, where blocks are passable at a breach cost and obstacles (crates, vehicle
///   hulks, rubble) are marked inflated by the agent's radius, so routes never hug them.
/// - Smooths the route only where a straight line with the agent's full radius is clear, so it never
///   cuts a corner.
/// - Holds at a radius-safe spot in front of a block it has to breach and lets the soldier attack it,
///   instead of grinding into it.
/// - Light separation from other agents, cells they stand in cost a little extra when planning, and a
///   short look-ahead that steers round anything solid the grid doesn't know about.
/// - Stuck detection: no progress for 1.5 s re-plans; still stuck, a side-step; still stuck, a hop to
///   the nearest free cell. An agent is never left frozen.
/// </summary>
public sealed class InvaderNavigator
{
    public const float Radius = 0.34f;
    /// <summary>Extra margin around obstacles and in line checks.</summary>
    public const float Clearance = 0.14f;
    /// <summary>Within this distance a rifle can work on the block in the way.</summary>
    public const float ShootReach = 6.5f;

    // Counters across all agents, for the soak test.
    public static int Repaths, Sidesteps, Unsticks;

    readonly CombatWorld _world;
    readonly CharacterBody3D _body;
    readonly bool _mustTouch; // breachers walk right up to plant their charge

    AssaultPlan _plan;
    int _aim;
    bool _needsPlan = true;
    Cell _goal;
    Vector3? _final;
    float _arrive = 1.5f;

    Vector3 _anchor;
    float _stuckTime, _sidestep, _avoidTimer, _aimTimer;
    int _stage, _sideSign = 1;
    Vector3 _sidestepDir, _avoidDir;

    public InvaderNavigator(CombatWorld world, CharacterBody3D body, bool mustTouch)
    {
        _world = world;
        _body = body;
        _mustTouch = mustTouch;
        _anchor = body.GlobalPosition;
    }

    /// <summary>The block standing in the way (or the core, at the end of an assault).</summary>
    public Block BreachTarget { get; private set; }
    /// <summary>Standing still on purpose, at an engagement position.</summary>
    public bool Holding { get; private set; }
    /// <summary>Reached the final point of a withdrawal.</summary>
    public bool Arrived { get; private set; }
    public float StuckTime => _stuckTime;

    BaseGrid Grid => _world.Model.Grid;
    Vector3 Position => _body.GlobalPosition;

    public void SetGoal(Cell goal, Vector3? finalPoint, float arriveRadius = 1.5f)
    {
        _goal = goal;
        _final = finalPoint;
        _arrive = arriveRadius;
        _needsPlan = true;
        Arrived = false;
    }

    /// <summary>The world changed (a block fell, an obstacle appeared): plan again next tick.</summary>
    public void Invalidate() => _needsPlan = true;

    /// <summary>The horizontal velocity to move at this tick.</summary>
    public Vector3 Steer(float dt, float speed)
    {
        Vector3? point = NextPoint();
        Vector3 desired = Vector3.Zero;
        if (point is Vector3 p && !Holding)
        {
            Vector3 to = (p - Position) with { Y = 0 };
            float distance = to.Length();
            if (distance > 0.2f) desired = to / distance * speed * Mathf.Clamp(distance / 1.0f, 0.3f, 1f);
            if (desired != Vector3.Zero) desired = Avoid(desired, Mathf.Min(distance, 2.4f), dt);
        }
        desired += Separation() * (Holding ? 0.8f : 1.6f);
        return Unstick(desired, point, dt, speed);
    }

    // ---- Following the route ----------------------------------------------------------

    Vector3? NextPoint()
    {
        Holding = false;
        BreachTarget = null;
        BaseGrid grid = Grid;
        Cell here = _world.CellAt(Position);

        if (!grid.InBounds(here))
        {
            // Off the planned area: walk straight in (or out, to the final point).
            if (_final is Vector3 f) return Finish(f);
            return _world.CellCentre(_goal);
        }

        if (_needsPlan) Replan();
        if (_plan is not { Found: true })
        {
            // No route from here: get onto a walkable cell first (stuck recovery takes over if that fails).
            if (_final is Vector3 f2) return Finish(f2);
            Cell? free = grid.NearestWalkable(Ground(Position), 4);
            _needsPlan = true;
            return free is Cell c ? _world.CellCentre(c) : Position + Vector3.Forward;
        }

        List<Cell> cells = _plan.Cells;
        int firstBlocked = cells.Count;
        for (int i = _aim; i < cells.Count; i++)
            if (grid.At(cells[i]) is { Alive: true }) { firstBlocked = i; break; }

        // Aim at the furthest cell along the route that a body of our radius can reach in a straight line.
        // Refreshed a few times a second, or on reaching the current aim.
        float width = Radius + Clearance;
        _aimTimer -= (float)_body.GetPhysicsProcessDeltaTime();
        bool reached = _aim < cells.Count && Flat(Position, _world.CellCentre(cells[_aim])) < 0.8f;
        if (_aimTimer > 0f && !reached && _aim < firstBlocked) return Target(cells, firstBlocked);
        _aimTimer = 0.15f;
        int before = _aim;
        for (int j = Mathf.Min(firstBlocked - 1, _aim + 8); j > _aim; j--)
            if (grid.ClearLine(Ground(Position), grid.CellCentre(cells[j]), width)) { _aim = j; break; }
        // At the aim with nothing further in clear sight (the route skirts an obstacle's margin): the
        // planner says the next cell is adjacent, so step to it; stuck recovery covers a real block.
        if (_aim == before && reached && _aim + 1 < firstBlocked) _aim++;
        if (_aim < firstBlocked && !grid.ClearLine(Ground(Position), grid.CellCentre(cells[_aim]), width))
        {
            // Pushed off the route (separation, a shove): go back to the nearest cell we can see, or re-plan.
            int back = -1;
            for (int j = _aim; j >= Mathf.Max(0, _aim - 4); j--)
                if (grid.ClearLine(Ground(Position), grid.CellCentre(cells[j]), width)) { back = j; break; }
            // Nothing in sight: head for the current aim cell's centre; stuck detection covers the rest.
            if (back >= 0) _aim = back;
        }
        return Target(cells, firstBlocked);
    }

    /// <summary>Where to head along the route, and whether that means holding to break a block or the core.</summary>
    Vector3? Target(List<Cell> cells, int firstBlocked)
    {
        BaseGrid grid = Grid;
        Vector3 aimPoint = _world.CellCentre(cells[Mathf.Min(_aim, cells.Count - 1)]);
        bool lastOpen = _aim >= firstBlocked - 1;
        if (firstBlocked < cells.Count && lastOpen)
        {
            // A block is in the way: hold at the last open cell (never inside the block) and break it.
            Vector3 hold = _world.CellCentre(cells[Mathf.Max(0, firstBlocked - 1)]);
            BreachTarget = grid.At(cells[firstBlocked]);
            Holding = InReach(BreachTarget, hold);
            return hold;
        }
        if (_aim >= cells.Count - 1)
        {
            if (_final is Vector3 f) return Finish(f);
            // At the end of an assault route, beside the core: hit it until it falls.
            BreachTarget = grid.Core;
            if (BreachTarget != null) Holding = InReach(BreachTarget, aimPoint);
            return aimPoint;
        }
        return aimPoint;
    }

    /// <summary>Close enough to work on the block: breachers at the hold cell, everyone else within rifle reach.</summary>
    bool InReach(Block block, Vector3 holdPoint)
    {
        float toHold = Flat(Position, holdPoint);
        if (toHold < 0.9f) return true;
        if (_mustTouch) return false;
        return Flat(Position, _world.BlockAim(block)) < ShootReach &&
               Grid.ClearLine(Ground(Position), Ground(holdPoint), 0.05f);
    }

    Vector3? Finish(Vector3 final)
    {
        if (Flat(Position, final) < _arrive)
        {
            Arrived = true;
            Holding = true; // waiting at the pickup is not being stuck
        }
        return final;
    }

    void Replan()
    {
        _needsPlan = false;
        BaseGrid grid = Grid;
        // Other agents count as soft obstacles, so a squad spreads over the open cells.
        var occupied = new HashSet<Cell>();
        foreach (IInvaderTroop t in _world.Troops)
            if (t.Body != _body && t.IsAlive && !t.IsGone && IsInstanceValid(t.Body)) occupied.Add(_world.CellAt(t.Body.GlobalPosition));
        Cell from = grid.Clamp(_world.CellAt(Position));
        _plan = _world.Planner.Plan(grid, from, _goal, c => occupied.Contains(c) ? 2.5 : 0);
        _aim = 0;
    }

    // ---- Local steering ---------------------------------------------------------------

    Vector3 Separation()
    {
        Vector3 push = Vector3.Zero;
        foreach (IInvaderTroop other in _world.Troops)
        {
            if (other.Body == _body || !other.IsAlive || other.IsGone || !IsInstanceValid(other.Body)) continue;
            Vector3 away = (Position - other.Body.GlobalPosition) with { Y = 0 };
            float d = away.Length();
            if (d < 1.0f && d > 0.001f) push += away / d * (1.0f - d);
        }
        return push;
    }

    /// <summary>
    /// Looks a couple of metres ahead with a body-sized sphere. If something solid is there (a hulk or
    /// crate the grid doesn't cover, another obstacle), turns to the clearer side and keeps that side
    /// for a moment so it doesn't dither.
    /// </summary>
    Vector3 Avoid(Vector3 desired, float lookAhead, float dt)
    {
        _avoidTimer -= dt;
        if (_avoidTimer > 0f && _avoidDir != Vector3.Zero) return _avoidDir * desired.Length();
        _avoidTimer = 0.1f;
        _avoidDir = Vector3.Zero;
        Vector3 dir = desired.Normalized();
        if (Clear(dir, lookAhead)) return desired;
        foreach (float degrees in new[] { 35f, 70f, 105f })
            foreach (int side in new[] { _sideSign, -_sideSign })
            {
                Vector3 turned = dir.Rotated(Vector3.Up, Mathf.DegToRad(degrees * side));
                if (!Clear(turned, lookAhead)) continue;
                _sideSign = side;
                _avoidDir = turned;
                _avoidTimer = 0.4f;
                return turned * desired.Length();
            }
        return desired;
    }

    bool Clear(Vector3 dir, float distance)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = Sphere,
            Transform = new Transform3D(Basis.Identity, Position + Vector3.Up * 0.9f),
            Motion = dir * distance,
            CollisionMask = Layers.World | Layers.Blocks | Layers.Vehicles | Layers.Loot,
            Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() },
        };
        float[] result = _body.GetWorld3D().DirectSpaceState.CastMotion(query);
        return result.Length == 0 || result[0] >= 0.999f;
    }

    static readonly SphereShape3D Sphere = new() { Radius = Radius + 0.04f };
    static readonly CapsuleShape3D Capsule = new() { Radius = Radius + 0.02f, Height = 1.7f };

    // ---- Stuck --------------------------------------------------------------------------

    /// <summary>Re-path at 1.5 s without progress, side-step at 2 s, hop to the nearest free cell at 2.6 s.</summary>
    Vector3 Unstick(Vector3 desired, Vector3? point, float dt, float speed)
    {
        // Only holding at an engagement position or waiting at the pickup counts as resting; anything
        // else that isn't getting anywhere is stuck, even standing on its current waypoint.
        bool wantsToMove = !Holding && !Arrived && point != null;
        if (!wantsToMove || Flat(Position, _anchor) > 0.4f)
        {
            _anchor = Position;
            _stuckTime = 0f;
            _stage = 0;
        }
        else _stuckTime += dt;

        if (_stage == 0 && _stuckTime >= 1.5f)
        {
            _stage = 1;
            Repaths++;
            Replan();
        }
        else if (_stage == 1 && _stuckTime >= 2.0f)
        {
            _stage = 2;
            Sidesteps++;
            Vector3 along = desired.LengthSquared() > 0.01f ? desired.Normalized() : Vector3.Forward;
            _sidestepDir = new Vector3(-along.Z, 0, along.X) * _sideSign;
            _sideSign = -_sideSign;
            _sidestep = 0.5f;
        }
        else if (_stage == 2 && _stuckTime >= 2.6f)
        {
            Unsticks++;
            HopToFreeSpot();
            _anchor = Position;
            _stuckTime = 0f;
            _stage = 0;
            Replan();
        }

        if (_sidestep > 0f)
        {
            _sidestep -= dt;
            return _sidestepDir * speed;
        }
        return desired;
    }

    /// <summary>Last resort: move to the nearest free cell (or open spot off the grid) that a body fits in.</summary>
    void HopToFreeSpot()
    {
        BaseGrid grid = Grid;
        Cell here = _world.CellAt(Position);
        Cell? cell = grid.NearestWalkable(Ground(Position), 4, c => c != here && Fits(_world.CellCentre(c)));
        if (cell is Cell c)
        {
            _body.GlobalPosition = _world.CellCentre(c) + Vector3.Up * 0.05f;
            return;
        }
        for (int i = 0; i < 16; i++)
        {
            Vector3 spot = Position + Vector3.Forward.Rotated(Vector3.Up, i * Mathf.Tau / 8f) * (2f + i / 8 * 2f);
            if (Fits(spot)) { _body.GlobalPosition = spot with { Y = 0.05f }; return; }
        }
    }

    bool Fits(Vector3 at)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = Capsule,
            Transform = new Transform3D(Basis.Identity, at with { Y = 0.95f }),
            CollisionMask = Layers.World | Layers.Blocks | Layers.Vehicles | Layers.Loot | Layers.Invaders | Layers.Player,
            Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() },
        };
        return _body.GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    static bool IsInstanceValid(GodotObject o) => GodotObject.IsInstanceValid(o);
    static GroundPos Ground(Vector3 p) => new(p.X, p.Z);
    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
