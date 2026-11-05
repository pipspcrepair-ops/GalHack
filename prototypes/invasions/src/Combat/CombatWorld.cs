using System.Collections.Generic;
using System.Linq;
using Godot;
using Invasions.Director;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// Who is in the fight and how shots resolve. PLACEHOLDER combat: simple target picking and flat
/// damage, but rounds are real projectiles with visible travel time (speeds from fire_control.json),
/// and AI fire goes through <see cref="FireControl"/> (warning shots, accuracy ramp, shooter × target
/// accuracy). Damage is never scaled. It also knows the base, to turn grid cells into world positions
/// and route damage to blocks.
/// </summary>
public partial class CombatWorld : Node3D
{
    public BaseModel Model;
    /// <summary>Routes to the core for invaders whose faction has no planning tactics of its own.</summary>
    public IAssaultPlanner Planner;
    /// <summary>Makes a planner with a faction's own wall cost (Kessari cut through walls, so walls cost them less).</summary>
    public System.Func<double, IAssaultPlanner> MakePlanner = hpPerCell => new BreachPlanner(hpPerCell);
    public InvasionData Data;
    public FireControlData FireData;

    readonly Dictionary<string, IAssaultPlanner> _planners = new();

    /// <summary>The planner for a faction's invaders: the shared one, or one with the faction's wall cost.</summary>
    public IAssaultPlanner PlannerFor(string faction)
    {
        if (_planners.TryGetValue(faction, out IAssaultPlanner planner)) return planner;
        double? own = Data.Faction(faction).Tactics.HpPerCellOfDetour;
        planner = own is double hp ? MakePlanner(hp) : Planner;
        _planners[faction] = planner;
        return planner;
    }

    /// <summary>A faction's colours, for its troops, vehicles and tracers.</summary>
    public Livery LiveryFor(string faction) => Livery.For(Data.Faction(faction));

    /// <summary>Things invaders shoot at: players, and the heads of the base's turrets.</summary>
    public readonly List<IDamageable> Players = new();
    public readonly List<IDamageable> TurretHeads = new();
    public readonly List<IInvaderTroop> Troops = new();
    public readonly List<IInvaderVehicle> Vehicles = new();

    /// <summary>Seconds of game time; fire control's ramps run on this.</summary>
    public float Time { get; private set; }

    /// <summary>Counters for tests and tuning.</summary>
    public int RoundsFired { get; private set; }
    public int WarningRoundsFired { get; private set; }
    /// <summary>Warning rounds that hit the player they were fired past. Must stay zero.</summary>
    public int WarningRoundsThatHitTheirTarget { get; private set; }
    public int LiveRounds => _rounds.Count;

    /// <summary>Test hook: turrets hold fire while false.</summary>
    public bool TurretsEnabled = true;

    public readonly RandomNumberGenerator Rng = new() { Seed = 7 };

    public override void _PhysicsProcess(double delta)
    {
        Time += (float)delta;
        Troops.RemoveAll(t => !IsInstanceValid(t.Body));
        Vehicles.RemoveAll(v => !IsInstanceValid(v.Body));
        StepRounds((float)delta);
    }

    // ---- The base in world space ------------------------------------------------------

    public float WallHeight => (float)Model.Data.WallHeight;

    public Vector3 CellCentre(Cell c)
    {
        GroundPos p = Model.Grid.CellCentre(c);
        return new Vector3((float)p.X, 0f, (float)p.Z);
    }

    public Cell CellAt(Vector3 p) => Model.Grid.CellAt(new GroundPos(p.X, p.Z));

    public Vector3 BaseCentre => new((float)Model.Centre.X, 0f, (float)Model.Centre.Z);

    public Vector3 BlockAim(Block b) => CellCentre(b.Cell) + Vector3.Up * WallHeight * 0.45f;

    // ---- Shooting ---------------------------------------------------------------------

    /// <summary>A round in flight.</summary>
    sealed class Round
    {
        public Vector3 Position, Velocity;
        public float Speed, Damage, Travelled, MaxDistance, TurnRate;
        public uint Mask;
        public Team Shooter;
        public Color Color;
        public Godot.Collections.Array<Rid> Exclude;
        public IDamageable Homing;
        public IDamageable WarnedTarget;
        public MeshInstance3D Streak;
    }

    readonly List<Round> _rounds = new();

    /// <summary>
    /// AI fire at a target, through its gun's fire control: a deliberate warning miss when it has
    /// only just started on a player, otherwise aimed with the ramped spread for this shooter and
    /// target class. Homing weapons (rockets) track the target.
    /// </summary>
    public void FireAt(FireControl fireControl, Vector3 muzzle, IDamageable target, uint mask, Team shooter, Color color,
        Godot.Collections.Array<Rid> exclude = null, float maxDistance = 400f)
    {
        bool atPlayer = Players.Contains(target);
        ShotSolution shot = fireControl.NextShot(target, target.Class, atPlayer, Time);
        var ignore = exclude != null ? new Godot.Collections.Array<Rid>(exclude) : new Godot.Collections.Array<Rid>();
        Vector3 aim = target.AimPoint;
        IDamageable homing = fireControl.Weapon.TurnRateDegrees > 0 ? target : null;
        IDamageable warned = null;
        if (shot.Warning)
        {
            // Close enough to see and hear, and it cannot hit them: their body is excluded from the round.
            aim = WarningPoint(muzzle, target.AimPoint);
            if (target is CollisionObject3D body) ignore.Add(body.GetRid());
            homing = null;
            warned = target;
            WarningRoundsFired++;
        }
        Fire(muzzle, aim, fireControl.Weapon, (float)shot.SpreadDegrees, mask, shooter, color, ignore, homing, maxDistance, warned);
    }

    /// <summary>Fires one round toward a point, with a spread cone in degrees.</summary>
    public void Fire(Vector3 from, Vector3 aim, WeaponData weapon, float spreadDegrees, uint mask, Team shooter, Color color,
        Godot.Collections.Array<Rid> exclude = null, IDamageable homing = null, float maxDistance = 400f, IDamageable warnedTarget = null)
    {
        Vector3 dir = Spread((aim - from).Normalized(), spreadDegrees);
        float speed = (float)weapon.ProjectileSpeed;
        bool rocket = weapon.TurnRateDegrees > 0;
        float length = rocket ? 0.9f : Mathf.Clamp(speed * 0.012f, 0.6f, 7f);
        float width = rocket ? 0.18f : 0.07f;
        var streak = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(width, width, length) },
            MaterialOverride = Palette.Glow(rocket ? Palette.Fire : color, rocket ? 3f : 2.2f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(streak);
        var round = new Round
        {
            Position = from,
            Velocity = dir * speed,
            Speed = speed,
            Damage = (float)weapon.Damage, // never scaled
            MaxDistance = maxDistance,
            TurnRate = Mathf.DegToRad((float)weapon.TurnRateDegrees),
            Mask = mask,
            Shooter = shooter,
            Color = color,
            Exclude = exclude ?? new Godot.Collections.Array<Rid>(),
            Homing = homing,
            WarnedTarget = warnedTarget,
            Streak = streak,
        };
        PlaceStreak(round);
        streak.ResetPhysicsInterpolation();
        _rounds.Add(round);
        RoundsFired++;
    }

    void StepRounds(float dt)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        for (int i = _rounds.Count - 1; i >= 0; i--)
        {
            Round r = _rounds[i];
            if (r.TurnRate > 0 && Usable(r.Homing))
            {
                // Rockets track, but slowly: a dodge inside their turning circle beats them.
                Vector3 want = (r.Homing.AimPoint - r.Position).Normalized();
                Vector3 now = r.Velocity.Normalized();
                float angle = now.AngleTo(want);
                if (angle > 1e-4f)
                {
                    Vector3 axis = now.Cross(want);
                    if (axis.LengthSquared() > 1e-8f)
                        r.Velocity = now.Rotated(axis.Normalized(), Mathf.Min(angle, r.TurnRate * dt)) * r.Speed;
                }
            }

            Vector3 step = r.Velocity * dt;
            var query = PhysicsRayQueryParameters3D.Create(r.Position, r.Position + step, r.Mask, r.Exclude);
            Godot.Collections.Dictionary hit = space.IntersectRay(query);
            if (hit.Count > 0)
            {
                GodotObject collider = hit["collider"].AsGodotObject();
                Vector3 at = hit["position"].AsVector3();
                if (collider is IDamageable victim)
                {
                    if (r.WarnedTarget != null && ReferenceEquals(victim, r.WarnedTarget)) WarningRoundsThatHitTheirTarget++;
                    victim.TakeDamage(r.Damage, r.Position, r.Shooter);
                }
                Fx.Spark(at, r.Color, r.TurnRate > 0 ? 0.5f : 0.16f);
                if (r.TurnRate > 0) Fx.Explosion(at, 1.2f, withDebris: false);
                Retire(i);
                continue;
            }

            r.Position += step;
            r.Travelled += step.Length();
            if (r.Travelled > r.MaxDistance || r.Position.Y < -1f)
            {
                Retire(i);
                continue;
            }
            PlaceStreak(r);
        }
    }

    void Retire(int index)
    {
        if (IsInstanceValid(_rounds[index].Streak)) _rounds[index].Streak.QueueFree();
        _rounds.RemoveAt(index);
    }

    static void PlaceStreak(Round r)
    {
        Vector3 dir = r.Velocity.Normalized();
        Vector3 up = Mathf.Abs(dir.Y) > 0.98f ? Vector3.Right : Vector3.Up;
        float length = ((BoxMesh)r.Streak.Mesh).Size.Z;
        // The streak trails behind the round's head, so it never pokes through what it is about to hit.
        r.Streak.GlobalTransform = new Transform3D(Basis.LookingAt(dir, up), r.Position - dir * length * 0.5f);
    }

    /// <summary>Where a warning round goes: beside the target, a metre or three off, often kicking up dirt at their feet.</summary>
    Vector3 WarningPoint(Vector3 muzzle, Vector3 targetAim)
    {
        WarningData w = FireData.Warning;
        Vector3 line = (targetAim - muzzle) with { Y = 0 };
        if (line.LengthSquared() < 0.01f) line = Vector3.Forward;
        Vector3 side = new Vector3(-line.Z, 0, line.X).Normalized() * (Rng.Randf() < 0.5f ? -1f : 1f);
        float off = Rng.RandfRange((float)w.MissDistanceMin, (float)w.MissDistanceMax);
        float drop = Rng.RandfRange(-1.6f, 0.4f);
        return targetAim + side * off + Vector3.Up * drop;
    }

    /// <summary>Damages blocks and anyone standing near a blast. Breach charges use this.</summary>
    public void Blast(Vector3 at, float radius, float blockDamage, float personDamage, Team by)
    {
        Fx.Explosion(at, radius * 0.7f);
        // The block the charge is stuck to takes the blast; its neighbours only get scorched.
        var near = Model.Grid.Alive.Select(b => (block: b, d: BlockAim(b).DistanceTo(at)))
            .Where(x => x.d < radius + 1f).OrderBy(x => x.d).ToList();
        for (int i = 0; i < near.Count; i++)
            Model.Damage(near[i].block, i == 0 ? blockDamage : blockDamage * 0.12f);
        foreach (IDamageable person in Players.ToList())
            if (person.IsAlive && person.AimPoint.DistanceTo(at) < radius) person.TakeDamage(personDamage, at, by);
    }

    public bool LineOfSight(Vector3 from, IDamageable target, uint mask, Godot.Collections.Array<Rid> exclude = null)
    {
        Vector3 to = target.AimPoint;
        var query = PhysicsRayQueryParameters3D.Create(from, to, mask, exclude ?? new Godot.Collections.Array<Rid>());
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return true;
        if (hit["collider"].AsGodotObject() == target as GodotObject) return true;
        return hit["position"].AsVector3().DistanceTo(to) < 0.6f;
    }

    Vector3 Spread(Vector3 dir, float degrees)
    {
        if (degrees <= 0f) return dir;
        Vector3 side = dir.Cross(Mathf.Abs(dir.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
        Vector3 lift = side.Cross(dir).Normalized();
        float angle = Mathf.DegToRad(degrees) * Mathf.Sqrt(Rng.Randf());
        float turn = Rng.Randf() * Mathf.Tau;
        return (dir + (side * Mathf.Cos(turn) + lift * Mathf.Sin(turn)) * Mathf.Tan(angle)).Normalized();
    }

    // ---- Obstacles --------------------------------------------------------------------

    /// <summary>Raised when obstacles are added or removed, so agents re-plan.</summary>
    public event System.Action ObstaclesChanged;

    /// <summary>
    /// Marks something solid that can't be breached (a spill crate, a vehicle hulk, rubble) on the
    /// planning grid, grown by an agent's radius plus a margin (or by <paramref name="inflate"/>), so
    /// routes go round it.
    /// </summary>
    public void RegisterObstacle(Vector3 centre, Vector3 size, float yaw, float? inflate = null) => MarkObstacle(centre, size, yaw, inflate, +1);

    public void UnregisterObstacle(Vector3 centre, Vector3 size, float yaw, float? inflate = null) => MarkObstacle(centre, size, yaw, inflate, -1);

    void MarkObstacle(Vector3 centre, Vector3 size, float yaw, float? inflate, int delta)
    {
        int cells = Model.Grid.MarkObstacle(new GroundPos(centre.X, centre.Z), size.X, size.Z, yaw,
            inflate ?? InvaderNavigator.Radius + InvaderNavigator.Clearance, delta);
        if (cells > 0) ObstaclesChanged?.Invoke();
    }

    // ---- Targets ----------------------------------------------------------------------

    /// <summary>
    /// A remembered target is only worth shooting while it is alive and its node still exists:
    /// a turret's hit box is freed the moment its block is destroyed.
    /// </summary>
    public static bool Usable(IDamageable target) =>
        target != null && (target is not GodotObject node || IsInstanceValid(node)) && target.IsAlive;

    /// <summary>Living invaders a defender could shoot: foot soldiers and vehicle crew.</summary>
    public IEnumerable<IDamageable> InvaderTargets()
    {
        foreach (IInvaderTroop t in Troops)
            if (t.IsAlive && !t.IsGone && t.Body is IDamageable d) yield return d;
        foreach (IInvaderVehicle v in Vehicles)
            if (!v.IsGone)
                foreach (IDamageable crew in v.CrewTargets)
                    if (crew.IsAlive) yield return crew;
    }

    public IDamageable NearestInvader(Vector3 from, float range, uint losMask, Godot.Collections.Array<Rid> exclude = null) =>
        Nearest(InvaderTargets(), from, range, losMask, exclude);

    public IDamageable NearestPlayer(Vector3 from, float range, uint losMask, Godot.Collections.Array<Rid> exclude = null) =>
        Nearest(Players.Where(p => p.IsAlive), from, range, losMask, exclude);

    public IDamageable NearestTurret(Vector3 from, float range, uint losMask, Godot.Collections.Array<Rid> exclude = null) =>
        Nearest(TurretHeads.Where(t => t.IsAlive), from, range, losMask, exclude);

    IDamageable Nearest(IEnumerable<IDamageable> candidates, Vector3 from, float range, uint losMask, Godot.Collections.Array<Rid> exclude)
    {
        IDamageable best = null;
        float bestDistance = range;
        foreach (IDamageable c in candidates)
        {
            float d = c.AimPoint.DistanceTo(from);
            if (d >= bestDistance) continue;
            if (!LineOfSight(from, c, losMask, exclude)) continue;
            best = c;
            bestDistance = d;
        }
        return best;
    }
}
