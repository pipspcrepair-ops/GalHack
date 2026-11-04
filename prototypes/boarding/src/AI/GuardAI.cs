using System.Collections.Generic;
using Godot;

namespace Boarding;

public enum GuardState { Patrol, Post, Investigate, Converge, Combat, Search, Seated }

/// <summary>
/// What a guard decides, tick by tick:
///
/// - Patrol the numbered loop from the layout, or hold a post if the loop is out of reach.
/// - See the player (range, field of view, line of sight; crouching and blackouts make that
///   harder). An unaware guard needs a moment to be sure, so a crouched player at range can
///   slip by; up close or once the alarm is up, it's instant.
/// - Hear noise: footsteps make a guard investigate; gunfire and explosions raise the alarm
///   and everyone converges on the last known position.
/// - Fight: find a crate to crouch behind, then cycle peek â†’ red aim laser â†’ burst â†’ duck.
///   The first burst at a new target is a deliberately wide warning. Hits make a guard
///   flinch (which delays the shot); heavy damage in a short window staggers it.
/// - Lose sight for a few seconds and go searching where the player was last seen.
/// </summary>
public partial class Guard
{
    public GuardState State { get; private set; } = GuardState.Patrol;
    public bool SeesPlayer { get; private set; }
    public Vector3 LastSeenLocal { get; private set; }
    /// <summary>Where the current path ends, for tests and debugging.</summary>
    public Vector2I? PathEnd { get; private set; }
    public bool Seated => State == GuardState.Seated && !Dead;
    public Vector3 PostLocal;

    enum FireStep { Idle, Telegraph, Burst, Cooldown }

    List<Vector3> _path;
    int _pathIndex;
    int _patrol = -1;
    float _perceive, _suspicion, _lostSight, _lookAround, _stagger, _repathTimer, _stuckTimer;
    float _yaw, _postYaw;
    Vector3 _lastPosition, _knock;
    FireStep _fire;
    float _fireTimer;
    int _burstLeft;
    bool _warned;
    Vector2I? _cover;

    void ReadyBrain()
    {
        PostLocal = Position;
        _yaw = Rotation.Y;
        _postYaw = _yaw;
        Rotation = Vector3.Zero;
        _perceive = GD.Randf() * 0.15f;   // stagger perception across guards
        switch (Type.Role)
        {
            case GuardRole.Gunner: State = GuardState.Seated; break;
            case GuardRole.Officer: State = GuardState.Post; break;
            default: StartPatrol(); break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Dead)
        {
            AnimateFigure(dt, 0f, _yaw);
            return;
        }
        if (Game.FreezeGuards)
        {
            AnimateFigure(dt, 0f, _yaw);
            return;
        }

        Perceive(dt);
        Vector3 move = Vector3.Zero;
        float speed = T.GuardWalkSpeed;

        switch (State)
        {
            case GuardState.Seated:
                Position = PostLocal;
                _yaw = _postYaw;
                if (SeesPlayer && Game.Player.IsInsideShip) FightFromHere(dt, aimOnly: false);
                else ShowLaser(false, Vector3.Zero);
                break;

            case GuardState.Post:
                // Sentries look around slowly.
                _lookAround += dt;
                _yaw = _postYaw + Mathf.Sin(_lookAround * 0.6f) * 0.9f;
                if (Position.DistanceTo(PostLocal) > 0.6f && _path == null) PathTo(PostLocal);
                move = FollowPath();
                break;

            case GuardState.Patrol:
                move = FollowPath();
                if (_path == null) NextPatrolPoint();
                break;

            case GuardState.Investigate:
            case GuardState.Converge:
            case GuardState.Search:
                speed = State == GuardState.Investigate ? T.GuardWalkSpeed * 1.3f : T.GuardRunSpeed;
                move = FollowPath();
                if (_path == null)
                {
                    // Arrived (or can't get there): look around, then go back to work.
                    _lookAround += dt;
                    _yaw += dt * 1.4f;
                    if (_lookAround > 4f) ReturnToDuty();
                }
                break;

            case GuardState.Combat:
                speed = T.GuardRunSpeed;
                move = Fight(dt);
                break;
        }

        if (_stagger > 0f)
        {
            _stagger -= dt;
            move = Vector3.Zero;
        }

        Vector3 local = move * speed * Type.SpeedFactor + _knock;
        _knock = _knock.MoveToward(Vector3.Zero, dt * 8f);
        if (move.LengthSquared() > 0.01f && !(State == GuardState.Combat && SeesPlayer))
            _yaw = Mathf.Atan2(-move.X, -move.Z);

        Vector3 up = Ship.GlobalBasis.Y;
        Vector3 vertical = Velocity.Dot(up) * up;
        Vector3 horizontal = Ship.GlobalBasis * local;
        Velocity = horizontal + (IsOnFloor() ? Vector3.Zero : vertical - up * 12f * dt);
        UpDirection = up;
        if (State != GuardState.Seated) MoveAndSlide();


        CheckStuck(dt, move);
        AnimateFigure(dt, State == GuardState.Seated ? 0f : local.Length(), _yaw);
    }

    // ---- Perception ----------------------------------------------------------------------

    void Perceive(float dt)
    {
        _perceive -= dt;
        if (_perceive > 0f) return;
        _perceive = 0.15f;

        Player player = Game.Player;
        SeesPlayer = CanSee(player, out float distance);
        if (!SeesPlayer)
        {
            _suspicion = Mathf.Max(0f, _suspicion - 0.08f);
            return;
        }
        LastSeenLocal = Ship.ToLocal(player.GlobalPosition);
        if (State == GuardState.Combat) return;
        if (State == GuardState.Seated)
        {
            // The gunner fights from his seat, and calls in anyone he sees aboard.
            if (player.IsInsideShip) Game.Alarm.ReportSighting(LastSeenLocal);
            return;
        }

        bool aware = Game.Alarm.Level >= AlarmLevel.Alert || distance < 7f
            || State is GuardState.Investigate or GuardState.Converge or GuardState.Search;
        _suspicion += aware ? 1f : 0.15f * Mathf.Lerp(1.8f, 0.6f, distance / T.GuardSightRange);
        // Turn toward what caught the eye while making up its mind.
        Vector3 to = LastSeenLocal - Position;
        _yaw = Mathf.Atan2(-to.X, -to.Z);
        if (_suspicion >= 1f) EnterCombat();
    }

    bool CanSee(Player player, out float distance)
    {
        distance = 999f;
        if (Game.PlayerHidden || player.Health.Dead) return false;
        Vector3 eye = GlobalPosition + Ship.GlobalBasis.Y * (_capsule.Height - 0.15f);
        Vector3 chest = player.GlobalPosition + Vector3.Up * (player.Crouched ? 0.6f : 1.1f);
        Vector3 head = player.GlobalPosition + Vector3.Up * (player.Crouched ? 0.95f : 1.6f);
        distance = eye.DistanceTo(chest);

        float range = T.GuardSightRange * (player.Crouched ? 0.75f : 1f);
        if (Ship.Lights == LightMode.Blackout && distance > 5f) range *= 0.55f;
        if (distance > range) return false;

        bool alert = State is GuardState.Combat or GuardState.Converge or GuardState.Search;
        if (!alert && distance > 3.5f)
        {
            Vector3 forward = Ship.GlobalBasis * new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
            if (forward.Dot((chest - eye).Normalized()) < Mathf.Cos(Mathf.DegToRad(T.GuardFovDegrees * 0.5f))) return false;
        }
        return Clear(eye, chest) || Clear(eye, head);
    }

    bool Clear(Vector3 from, Vector3 to)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, Layers.Sight, new Godot.Collections.Array<Rid> { GetRid() });
        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    /// <summary>A noise somewhere. Guards decide for themselves whether it reached them.</summary>
    public void OnNoise(Noise noise)
    {
        if (Dead || Game.FreezeGuards || State is GuardState.Combat or GuardState.Seated) return;
        if (GlobalPosition.DistanceTo(noise.Position) > noise.Radius * T.GuardHearing) return;
        Vector3 local = Ship.ToLocal(noise.Position);
        if (noise.Kind == NoiseKind.Footstep)
        {
            if (!noise.FromPlayer || State is GuardState.Converge or GuardState.Search) return;
            State = GuardState.Investigate;
            _lookAround = 0f;
            PathTo(local);
            return;
        }
        // Shots and bangs: raise the alarm; the alarm sends everyone (us included) to converge.
        Game.Alarm.ReportLoudNoise(local);
        Converge(local);
    }

    /// <summary>The alarm has a new last known position.</summary>
    public void Converge(Vector3 local)
    {
        if (Dead || Game.FreezeGuards || State is GuardState.Combat or GuardState.Seated) return;
        State = GuardState.Converge;
        _lookAround = 0f;
        PathTo(local);
    }

    void EnterCombat()
    {
        if (State == GuardState.Seated) return;
        State = GuardState.Combat;
        _suspicion = 1f;
        _lostSight = 0f;
        _fire = FireStep.Idle;
        _fireTimer = 0.2f + GD.Randf() * 0.3f;
        _path = null;
        PathEnd = null;
        LastSeenLocal = Ship.ToLocal(Game.Player.GlobalPosition);
        Game.Alarm.ReportSighting(LastSeenLocal);
    }

    void ReturnToDuty()
    {
        _lookAround = 0f;
        if (Type.Role == GuardRole.Officer) { State = GuardState.Post; _path = null; return; }
        StartPatrol();
    }

    // ---- Patrol ----------------------------------------------------------------------------

    void StartPatrol()
    {
        State = GuardState.Patrol;
        var loop = Ship.Layout.PatrolLoop;
        if (loop.Count == 0) { State = GuardState.Post; return; }
        // Join the loop at the nearest point we can actually reach.
        float best = float.MaxValue;
        int start = -1;
        for (int i = 0; i < loop.Count; i++)
        {
            float d = Ship.Layout.CellCenter(loop[i]).DistanceSquaredTo(Position);
            if (d < best && Ship.FindPath(Position, Ship.Layout.CellCenter(loop[i])) != null) { best = d; start = i; }
        }
        if (start < 0) { State = GuardState.Post; _patrol = -1; return; }
        _patrol = start;
        PathTo(Ship.Layout.CellCenter(loop[_patrol]));
    }

    void NextPatrolPoint()
    {
        var loop = Ship.Layout.PatrolLoop;
        for (int tries = 0; tries < loop.Count; tries++)
        {
            _patrol = (_patrol + 1) % loop.Count;
            if (PathTo(Ship.Layout.CellCenter(loop[_patrol]))) return;
        }
        State = GuardState.Post;   // sealed in: hold position
    }

    // ---- Paths -----------------------------------------------------------------------------

    bool PathTo(Vector3 local)
    {
        List<Vector2I> cells = Ship.FindPath(Position, local);
        if (cells == null)
        {
            _path = null;
            PathEnd = null;
            return false;
        }
        _path = new List<Vector3>(cells.Count);
        for (int i = 1; i < cells.Count; i++) _path.Add(Ship.Layout.CellCenter(cells[i]));
        _pathIndex = 0;
        PathEnd = cells[^1];
        _repathTimer = 1.5f;
        if (_path.Count == 0) _path = null;
        return true;
    }

    /// <summary>A unit direction (ship-local) toward the next waypoint, or zero.</summary>
    Vector3 FollowPath()
    {
        if (_path == null) return Vector3.Zero;
        while (_pathIndex < _path.Count)
        {
            Vector3 to = _path[_pathIndex] - Position;
            to.Y = 0f;
            if (to.Length() > 0.35f) return to.Normalized();
            _pathIndex++;
        }
        _path = null;
        return Vector3.Zero;
    }

    void CheckStuck(float dt, Vector3 move)
    {
        if (move == Vector3.Zero || _path == null)
        {
            _stuckTimer = 0f;
            _lastPosition = Position;
            return;
        }
        _stuckTimer += dt;
        if (_stuckTimer < 1.2f) return;
        if (Position.DistanceTo(_lastPosition) < 0.3f && PathEnd is Vector2I end)
            PathTo(Ship.Layout.CellCenter(end));   // a door was slow or someone was in the way
        _stuckTimer = 0f;
        _lastPosition = Position;
    }

    // ---- Combat ----------------------------------------------------------------------------

    Vector3 Fight(float dt)
    {
        Player player = Game.Player;
        Vector3 playerLocal = Ship.ToLocal(player.GlobalPosition);
        if (SeesPlayer) _lostSight = 0f;
        else _lostSight += dt;

        if (_lostSight > 3f || player.Health.Dead)
        {
            ReleaseCover();
            ShowLaser(false, Vector3.Zero);
            SetCrouch(false);
            State = GuardState.Search;
            _lookAround = 0f;
            PathTo(LastSeenLocal);
            return Vector3.Zero;
        }

        // Positioning: get behind a crate if there's one handy, else close to a sensible range.
        _repathTimer -= dt;
        if (_cover == null && _repathTimer <= 0f)
        {
            _repathTimer = 1.5f;
            _cover = FindCover(playerLocal);
            if (_cover is Vector2I c) PathTo(Ship.Layout.CellCenter(c));
            else if (Position.DistanceTo(playerLocal) > 14f || !SeesPlayer) PathTo(LastSeenLocal);
            else { _path = null; PathEnd = null; }
        }
        Vector3 move = FollowPath();
        bool inCover = _cover is Vector2I cell && _path == null && Ship.Layout.CellAt(Position) == cell;
        if (_cover != null && _path == null && !inCover) ReleaseCover();   // lost it (crate destroyed, path failed)

        if (SeesPlayer || _fire != FireStep.Idle)
        {
            Vector3 to = playerLocal - Position;
            _yaw = Mathf.Atan2(-to.X, -to.Z);
        }
        FightFromHere(dt, aimOnly: false, inCover);
        return move;
    }

    /// <summary>The fire cycle: peek â†’ telegraph laser â†’ burst â†’ (duck) cooldown.</summary>
    void FightFromHere(float dt, bool aimOnly, bool inCover = false)
    {
        Player player = Game.Player;
        Vector3 aim = player.GlobalPosition + Vector3.Up * (player.Crouched ? 0.7f : 1.2f);
        if (_stagger > 0f)
        {
            ShowLaser(false, aim);
            return;
        }
        _fireTimer -= dt;
        switch (_fire)
        {
            case FireStep.Idle:
                if (inCover) SetCrouch(true);
                if (_fireTimer <= 0f && SeesPlayer && !aimOnly)
                {
                    SetCrouch(false);   // peek
                    _fire = FireStep.Telegraph;
                    _fireTimer = T.GuardTelegraph * (Type.Heavy ? 1.6f : 1f);
                }
                else if (_fireTimer <= 0f && inCover)
                {
                    // Pop up to look for the player.
                    SetCrouch(false);
                    _fireTimer = 0.4f;
                }
                break;

            case FireStep.Telegraph:
                ShowLaser(true, aim, 1f - Mathf.Clamp(_fireTimer / T.GuardTelegraph, 0f, 1f));
                if (!SeesPlayer)
                {
                    _fire = FireStep.Cooldown;
                    _fireTimer = 0.5f;
                }
                else if (_fireTimer <= 0f)
                {
                    _fire = FireStep.Burst;
                    _burstLeft = Type.Heavy ? T.GuardBurst + 2 : T.GuardBurst;
                    _fireTimer = 0f;
                }
                break;

            case FireStep.Burst:
                ShowLaser(true, aim, 1f);
                if (_fireTimer <= 0f)
                {
                    FireAt(player, Accuracy(player));
                    _burstLeft--;
                    _fireTimer = T.GuardBurstInterval * (Type.Heavy ? 1.6f : 1f);
                    if (_burstLeft <= 0)
                    {
                        _warned = true;
                        _fire = FireStep.Cooldown;
                        _fireTimer = T.GuardBurstCooldown * (0.8f + GD.Randf() * 0.5f);
                    }
                }
                break;

            case FireStep.Cooldown:
                ShowLaser(false, aim);
                if (inCover) SetCrouch(true);   // duck between bursts
                if (_fireTimer <= 0f) _fire = FireStep.Idle;
                break;
        }
    }

    float Accuracy(Player player)
    {
        float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
        float a = T.GuardAccuracy * Mathf.Clamp(1.25f - distance / 35f, 0.35f, 1.1f);
        if (!_warned) a *= 0.35f;              // the opening burst is a warning
        if (player.Sprinting) a *= 0.7f;
        if (player.Crouched) a *= 0.85f;
        if (Ship.Lights == LightMode.Blackout) a *= 0.7f;
        return a;
    }

    Vector2I? FindCover(Vector3 playerLocal)
    {
        ShipLayout layout = Ship.Layout;
        Vector2I me = layout.CellAt(Position);
        Vector2I them = layout.CellAt(playerLocal);
        Vector2I? best = null;
        float bestScore = float.MaxValue;
        for (int dx = -4; dx <= 4; dx++)
        for (int dy = -4; dy <= 4; dy++)
        {
            var c = new Vector2I(me.X + dx, me.Y + dy);
            if (!Ship.Passable(c) || Game.CoverClaims.Contains(c)) continue;
            var toPlayer = new Vector2(them.X - c.X, them.Y - c.Y);
            if (toPlayer.Length() < 2.5f) continue;
            toPlayer = toPlayer.Normalized();
            foreach (Vector2I n in ShipLayout.Neighbours4)
            {
                if (layout[c + n] != CellKind.Cover || !Ship.Blocks.ContainsKey(c + n)) continue;
                if (new Vector2(n.X, n.Y).Dot(toPlayer) < 0.5f) continue;
                float score = Mathf.Sqrt(dx * dx + dy * dy);
                if (score < bestScore) { bestScore = score; best = c; }
            }
        }
        if (best is Vector2I chosen)
        {
            if (Ship.FindPath(Position, layout.CellCenter(chosen)) == null) return null;
            Game.CoverClaims.Add(chosen);
        }
        return best;
    }

    void ReleaseCover()
    {
        if (_cover is Vector2I c) Game.CoverClaims.Remove(c);
        _cover = null;
    }

    void OnHurt(Vector3 direction, bool stagger)
    {
        if (State != GuardState.Combat && State != GuardState.Seated) EnterCombat();
        if (stagger)
        {
            _stagger = 0.7f;
            _fire = FireStep.Cooldown;
            _fireTimer = 0.6f;
            ShowLaser(false, Vector3.Zero);
            _knock = Ship.GlobalBasis.Inverse() * new Vector3(direction.X, 0, direction.Z).Normalized() * 2.5f;
            SetCrouch(false);
        }
        else if (_fire == FireStep.Telegraph)
        {
            _fireTimer += 0.15f;   // a flinch throws off the aim
        }
    }

    void OnDied()
    {
        ReleaseCover();
        _path = null;
        PathEnd = null;
        Game.OnGuardKilled(this);
    }

    /// <summary>Sends a fresh guard (a reinforcement) straight to a point.</summary>
    public void Dispatch(Vector3 local)
    {
        State = GuardState.Converge;
        _lookAround = 0f;
        PathTo(local);
    }
}
