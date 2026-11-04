using System.Collections.Generic;
using System.Linq;
using Godot;
using Invasions.Director;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// Who is in the fight and how shots resolve. PLACEHOLDER combat: hitscan rays, flat damage, simple
/// target picking. It knows the base (to turn grid cells into world positions and route damage to
/// blocks), the defenders invaders may shoot, and the invaders defenders may shoot.
/// </summary>
public partial class CombatWorld : Node3D
{
    public BaseModel Model;
    public IAssaultPlanner Planner;
    public InvasionData Data;

    /// <summary>Things invaders shoot at: players, and the heads of the base's turrets.</summary>
    public readonly List<IDamageable> Players = new();
    public readonly List<IDamageable> TurretHeads = new();
    public readonly List<IInvaderTroop> Troops = new();
    public readonly List<IInvaderVehicle> Vehicles = new();

    /// <summary>Seconds of game time; the player's grace window runs on this.</summary>
    public float Time { get; private set; }

    /// <summary>Test hook: turrets hold fire while false.</summary>
    public bool TurretsEnabled = true;

    public readonly RandomNumberGenerator Rng = new() { Seed = 7 };

    public override void _PhysicsProcess(double delta)
    {
        Time += (float)delta;
        Troops.RemoveAll(t => !IsInstanceValid(t.Body));
        Vehicles.RemoveAll(v => !IsInstanceValid(v.Body));
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

    /// <summary>
    /// Fires one hitscan round. Hurts the first thing it hits if that thing is on the other side.
    /// Returns what it hit (or null).
    /// </summary>
    public GodotObject Shoot(Vector3 from, Vector3 dir, float range, float spreadDegrees, uint mask, Faction shooter,
        float damage, Color tracer, Godot.Collections.Array<Rid> exclude = null)
    {
        dir = Spread(dir.Normalized(), spreadDegrees);
        Vector3 to = from + dir * range;
        var query = PhysicsRayQueryParameters3D.Create(from, to, mask, exclude ?? new Godot.Collections.Array<Rid>());
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        GodotObject collider = null;
        Vector3 end = to;
        if (hit.Count > 0)
        {
            collider = hit["collider"].AsGodotObject();
            end = hit["position"].AsVector3();
            if (collider is IDamageable target) target.TakeDamage(damage, from, shooter);
            Fx.Spark(end, tracer, 0.16f);
        }
        Fx.Tracer(from + dir * 0.5f, end, tracer);
        return collider;
    }

    /// <summary>Damages blocks and anyone standing near a blast. Breach charges use this.</summary>
    public void Blast(Vector3 at, float radius, float blockDamage, float personDamage, Faction by)
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
