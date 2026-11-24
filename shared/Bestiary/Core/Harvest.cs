using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

public enum PlantKind { Tree, Bush }

/// <summary>A harvestable plant. Trees are also obstacles on the nav grid while they stand.</summary>
public sealed class Plant
{
    public int Id;
    public PlantKind Kind;
    public Vector3 Position;
    public float TrunkRadius, Height, Canopy;
    public int Variant;
    public float Health, MaxHealth;
    public bool Alive = true;
    public float RegrowTimer;
    /// <summary>0..1 while growing back, for the view.</summary>
    public float Grow = 1f;
    /// <summary>1 when just hit, decaying: the view shakes the plant.</summary>
    public float Shake;
    public Obstacle Obstacle;
    internal int WoodGiven, OrganicsGiven;
}

/// <summary>What one chip of a plant gave.</summary>
public struct Yield
{
    public int Wood, Organics;
    public bool Felled;
}

/// <summary>
/// The forest and the bushes. Chipping a plant pays out wood and plant matter as it goes
/// (so the hold fills while you cut, not in one lump at the end); a felled plant comes back
/// after a timer, but only when no player is near enough to watch it pop up.
/// </summary>
public sealed class HarvestField
{
    public readonly HarvestRules Rules;
    public readonly List<Plant> Plants = new();
    readonly NavGrid _nav;

    public event Action<Plant> Felled, Regrown;

    public HarvestField(HarvestRules rules, NavGrid nav)
    {
        Rules = rules;
        _nav = nav;
    }

    public Plant Add(PlantKind kind, Vector3 position, float trunkRadius, float height, float canopy, int variant)
    {
        var p = new Plant
        {
            Id = Plants.Count,
            Kind = kind,
            Position = position,
            TrunkRadius = trunkRadius,
            Height = height,
            Canopy = canopy,
            Variant = variant,
            MaxHealth = kind == PlantKind.Tree ? Rules.TreeHealth * Math.Clamp(height / 7f, 0.6f, 1.8f) : Rules.BushHealth,
        };
        p.Health = p.MaxHealth;
        if (kind == PlantKind.Tree && _nav != null) p.Obstacle = _nav.AddObstacle(position, trunkRadius);
        Plants.Add(p);
        return p;
    }

    /// <summary>Everything a standing plant still has to give, in storage units.</summary>
    public int RemainingYield(Plant p)
    {
        if (!p.Alive) return 0;
        int wood = p.Kind == PlantKind.Tree ? Rules.WoodPerTree : 0;
        int organics = p.Kind == PlantKind.Tree ? Rules.OrganicsPerTree : Rules.OrganicsPerBush;
        return wood - p.WoodGiven + organics - p.OrganicsGiven;
    }

    public Yield Chip(Plant p, float damage)
    {
        var y = new Yield();
        if (!p.Alive || damage <= 0f) return y;
        p.Health = MathF.Max(0f, p.Health - damage);
        p.Shake = 1f;
        float done = 1f - p.Health / p.MaxHealth;
        int woodTotal = p.Kind == PlantKind.Tree ? Rules.WoodPerTree : 0;
        int organicsTotal = p.Kind == PlantKind.Tree ? Rules.OrganicsPerTree : Rules.OrganicsPerBush;
        int wood = (int)(woodTotal * done), organics = (int)(organicsTotal * done);
        if (p.Health <= 0f)
        {
            wood = woodTotal;
            organics = organicsTotal;
        }
        y.Wood = wood - p.WoodGiven;
        y.Organics = organics - p.OrganicsGiven;
        p.WoodGiven = wood;
        p.OrganicsGiven = organics;
        if (p.Health <= 0f)
        {
            p.Alive = false;
            p.Grow = 0f;
            p.RegrowTimer = Rules.RegrowSeconds;
            if (p.Obstacle != null) _nav.SetActive(p.Obstacle, false);
            y.Felled = true;
            Felled?.Invoke(p);
        }
        return y;
    }

    /// <summary>Counts down felled plants and regrows them once nobody is within the block radius.</summary>
    public void Update(float dt, IReadOnlyList<Vector3> players)
    {
        foreach (Plant p in Plants)
        {
            p.Shake = MathF.Max(0f, p.Shake - dt * 3f);
            if (p.Alive)
            {
                if (p.Grow < 1f) p.Grow = MathF.Min(1f, p.Grow + dt / 4f);
                continue;
            }
            p.RegrowTimer -= dt;
            if (p.RegrowTimer > 0f || AnyNear(p.Position, players, Rules.RegrowBlockRadius)) continue;
            p.Alive = true;
            p.Health = p.MaxHealth;
            p.WoodGiven = p.OrganicsGiven = 0;
            p.Grow = 0.02f;
            if (p.Obstacle != null) _nav.SetActive(p.Obstacle, true);
            Regrown?.Invoke(p);
        }
    }

    static bool AnyNear(Vector3 at, IReadOnlyList<Vector3> players, float radius)
    {
        foreach (Vector3 q in players)
            if (WildSim.Flat(q - at).Length() < radius) return true;
        return false;
    }
}

/// <summary>What the blades did this tick, for particles and the vessel's drag.</summary>
public struct BladeTick
{
    public int Wood, Organics, Meat;
    public int PlantsHit, CreaturesHit, Felled, Killed;
    public Vector3 DebrisAt;
    public bool HitWood, HitFlesh;
    /// <summary>Plants and creatures the blades left alone because the hold couldn't take all of them.</summary>
    public int Refused;
}

/// <summary>
/// Harvester blades on the front of a hover vessel: a box in the vessel's frame. Anything
/// standing in it gets chewed, and the yield goes straight into the vessel's storage: wood
/// and plant matter from plants, meat (and the odd rare part) from creatures run down.
/// Nothing harvested is ever thrown away: the blades only bite into something whose whole
/// yield still fits in the hold, and idle otherwise (the vessel just shoves it aside).
/// </summary>
public sealed class HarvesterBlades
{
    public Vector3 Center = new(0f, 0.7f, -3.6f);
    public Vector3 HalfExtents = new(1.9f, 1.3f, 1.0f);
    public float PlantDps, CreatureDps;
    public bool Spinning = true;

    public HarvesterBlades(HarvestRules rules)
    {
        PlantDps = rules.BladeDps;
        CreatureDps = rules.BladeCreatureDps;
    }

    public bool Inside(Transform3D vessel, Vector3 point, float radius)
    {
        Vector3 local = vessel.AffineInverse() * point - Center;
        return MathF.Abs(local.X) < HalfExtents.X + radius
               && MathF.Abs(local.Z) < HalfExtents.Z + radius
               && local.Y > -HalfExtents.Y - 2.5f && local.Y < HalfExtents.Y + radius;
    }

    public BladeTick Tick(Transform3D vessel, float dt, HarvestField field, WildSim sim, Storage storage, ITarget driver)
    {
        var tick = new BladeTick();
        if (!Spinning) return tick;
        Vector3 bladeWorld = vessel * Center;

        if (field != null)
        {
            foreach (Plant p in field.Plants)
            {
                if (!p.Alive || WildSim.Flat(p.Position - bladeWorld).Length() > HalfExtents.Length() + 3f) continue;
                if (!Inside(vessel, p.Position + Vector3.Up * 0.5f, p.Kind == PlantKind.Tree ? p.TrunkRadius : p.Canopy * 0.6f)) continue;
                if (storage.Free < field.RemainingYield(p))
                {
                    tick.Refused++;
                    continue;
                }
                Yield y = field.Chip(p, PlantDps * dt);
                tick.PlantsHit++;
                tick.HitWood |= p.Kind == PlantKind.Tree;
                tick.DebrisAt = p.Position + Vector3.Up * 1f;
                if (y.Felled) tick.Felled++;
                tick.Wood += storage.Add(Items.Wood, y.Wood);
                tick.Organics += storage.Add(Items.Organics, y.Organics);
            }
        }

        if (sim != null)
        {
            foreach (Creature c in sim.Creatures)
            {
                if (c.Removed || c.Harvested) continue;
                Vector3 at = c.Rig.BoundsCenter;
                if (WildSim.Flat(at - bladeWorld).Length() > HalfExtents.Length() + c.Radius + 4f) continue;
                if (!Inside(vessel, c.Position + Vector3.Up * c.Radius, c.Radius)) continue;
                int room = (int)MathF.Ceiling(c.Dead ? c.MeatLeft : c.Species.Drops.Meat) + (c.Species.Drops.RarePart != "" ? 1 : 0);
                if (storage.Free < room)
                {
                    tick.Refused++;
                    continue;
                }
                tick.CreaturesHit++;
                tick.HitFlesh = true;
                tick.DebrisAt = at;
                if (!c.Dead) sim.Damage(c, CreatureDps * dt, driver);
                if (c.Dead)
                {
                    // Into the hopper: all the meat that's left, and maybe a rare part.
                    tick.Meat += storage.Add(Items.Meat, (int)MathF.Ceiling(c.MeatLeft));
                    var rng = Rng.For(c.Species.Seed, "drop", c.Id);
                    if (c.Species.Drops.RarePart != "" && rng.Chance(c.Species.Drops.RareChance))
                        storage.Add(c.Species.Drops.RarePart, 1);
                    c.MeatLeft = 0f;
                    c.Harvested = true;
                    c.Removed = true;
                    tick.Killed++;
                }
            }
        }
        return tick;
    }
}
