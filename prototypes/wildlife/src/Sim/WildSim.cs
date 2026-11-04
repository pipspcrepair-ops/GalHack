using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace Wildlife;

/// <summary>A creature's attack, from wind-up to contact or miss.</summary>
public struct AttackEvent
{
    public Creature Creature;
    public ITarget Target;
    public bool First;        // the telegraphed opener
    public float Windup;      // seconds of wind-up before this attack
    public bool Hit;
    public float Damage;
    public float Time;
}

/// <summary>
/// The whole wildlife simulation of one planet surface, in plain C#: creatures, herds, the
/// clock, and the players they react to. The game drives it from _PhysicsProcess and draws
/// it with CreatureView; the tests drive it directly, thousands of steps a second.
/// Each step runs brain, then locomotion, then the rig, for every creature.
/// </summary>
public sealed class WildSim
{
    public readonly WildlifeData Data;
    public readonly Terrain Terrain;
    public readonly NavGrid Nav;
    public readonly DayClock Clock;
    public readonly List<Creature> Creatures = new();
    public readonly List<Herd> Herds = new();
    public readonly List<ITarget> Targets = new();
    public Vector3 Wind = new(1.2f, 0f, 0.5f);
    public float Time;
    public int Frame;
    /// <summary>Path searches allowed per step, so a stampede can't stall a frame.</summary>
    public int PathBudgetPerStep = 6;
    int _pathBudget;
    int _nextId;

    /// <summary>When set, Step adds up time spent thinking, moving and posing (for the perf test).</summary>
    public bool Profile;
    public double ThinkMs, MoveMs, RigMs;

    /// <summary>A wind-up has started: the view plays the growl and the creature rears.</summary>
    public event Action<AttackEvent> Telegraph;
    /// <summary>A lunge has landed or missed.</summary>
    public event Action<AttackEvent> Attacked;
    public event Action<Creature, ITarget> Died;
    public event Action<Creature> Warned;

    public CombatRules Combat => Data.Combat;

    public WildSim(WildlifeData data, Terrain terrain, NavGrid nav)
    {
        Data = data;
        Terrain = terrain;
        Nav = nav;
        Clock = new DayClock(data.World);
    }

    // ---- Spawning ---------------------------------------------------------------------

    public Creature Spawn(Species species, Vector3 position, Herd herd = null)
    {
        var c = new Creature
        {
            Id = _nextId++,
            Species = species,
            Herd = herd,
            Health = species.Stats.MaxHealth,
            Home = position,
            Yaw = 0f,
        };
        c.Rng = new Rng(Rng.Mix(species.Seed, (ulong)c.Id));
        c.Yaw = c.Rng.Range(-MathF.PI, MathF.PI);
        c.Position = new Vector3(position.X, Terrain.Height(position.X, position.Z), position.Z);
        c.SwarmAlive = species.Body.Members;
        if (species.Flies)
        {
            c.Altitude = species.Rig == RigKind.Swarm ? c.Rng.Range(1.2f, 2.5f) : species.Rig == RigKind.GasBag ? c.Rng.Range(5f, 11f) : c.Rng.Range(8f, 15f);
            c.Position += Vector3.Up * c.Altitude;
        }
        if (herd != null)
        {
            c.HerdSlot = herd.Members.Count;
            herd.Members.Add(c);
        }
        c.Rig = Rig.Create(species, c);
        c.Rig.Reset(this, c);
        Creatures.Add(c);
        return c;
    }

    /// <summary>A species' natural group around a point: a herd if it herds, else loose neighbours.</summary>
    public List<Creature> SpawnGroup(Species species, Vector3 center, int count)
    {
        var list = new List<Creature>();
        Herd herd = null;
        if (species.Behaviour == Behaviour.Herd)
        {
            herd = new Herd { Home = center, Goal = center, Center = center, Spread = HerdSpread(species, count) };
            Herds.Add(herd);
        }
        var rng = Rng.For(species.Seed, "spawn", _nextId);
        for (int i = 0; i < count; i++)
        {
            float a = i * 2.39996f, r = (herd?.Spread ?? species.Body.Size * 3f) * MathF.Sqrt((i + 0.5f) / count);
            Vector3 p = center + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            p = FreeSpot(p, rng);
            list.Add(Spawn(species, p, herd));
        }
        return list;
    }

    public static float HerdSpread(Species s, int count) => Math.Max(3f, s.Stats.Radius * 2.2f * MathF.Sqrt(count));

    /// <summary>Nudges a spawn point off rocks and trunks.</summary>
    public Vector3 FreeSpot(Vector3 p, Rng rng)
    {
        for (int i = 0; i < 12 && Nav.Blocked(p); i++)
            p += new Vector3(rng.Range(-4f, 4f), 0f, rng.Range(-4f, 4f));
        return Terrain.Clamp(p, 8f);
    }

    public void Remove(Creature c) => c.Removed = true;

    public void Clear()
    {
        Creatures.Clear();
        Herds.Clear();
    }

    // ---- Stepping ---------------------------------------------------------------------

    public void Step(float dt)
    {
        Time += dt;
        Frame++;
        Clock.Advance(dt);
        _pathBudget = PathBudgetPerStep;

        long t0 = Profile ? Stopwatch.GetTimestamp() : 0, thinkTicks = 0, moveTicks = 0, rigTicks = 0;
        for (int i = 0; i < Creatures.Count; i++)
        {
            Creature c = Creatures[i];
            if (c.Removed) continue;
            c.Age += dt;
            c.HurtFlash = MathF.Max(0f, c.HurtFlash - dt * 4f);
            if (c.Dead) UpdateCorpse(c, dt);
            else
            {
                Brain.Update(this, c, dt);
                Attacks.Update(this, c, dt);
            }
            if (Profile) { long t = Stopwatch.GetTimestamp(); thinkTicks += t - t0; t0 = t; }
            Locomotion.Move(this, c, dt);
            if (Profile) { long t = Stopwatch.GetTimestamp(); moveTicks += t - t0; t0 = t; }

            // Far creatures re-pose less often; the rig catches up with the summed time.
            c.RigPending += dt;
            if (c.RigInterval <= 1 || (Frame + c.Id) % c.RigInterval == 0)
            {
                c.Rig.Update(this, c, c.RigPending);
                c.RigPending = 0f;
            }
            if (Profile) { long t = Stopwatch.GetTimestamp(); rigTicks += t - t0; t0 = t; }
        }
        if (Profile)
        {
            double ms = 1000.0 / Stopwatch.Frequency;
            ThinkMs += thinkTicks * ms;
            MoveMs += moveTicks * ms;
            RigMs += rigTicks * ms;
        }

        // Drop removed creatures now and then, never mid-loop.
        if (Frame % 120 == 0) Creatures.RemoveAll(c => c.Removed);
    }

    void UpdateCorpse(Creature c, float dt)
    {
        c.DeadTime += dt;
        c.Desired = Vector3.Zero;
        c.Phase = AttackPhase.None;
        if (c.DeadTime > 150f || (c.MeatLeft <= 0f && c.DeadTime > 4f)) c.Removed = true;
    }

    public bool TakePathBudget()
    {
        if (_pathBudget <= 0) return false;
        _pathBudget--;
        return true;
    }

    // ---- Damage -----------------------------------------------------------------------

    public void Damage(Creature c, float amount, ITarget source)
    {
        if (c.Dead || amount <= 0f) return;
        c.Health -= amount;
        c.HurtFlash = 1f;
        if (source != null)
        {
            c.ProvokedBy = source;
            c.ProvokedAt = Time;
            c.Herd?.Alarm(source.Position, Time);
        }
        if (c.Species.Rig == RigKind.Swarm)
            c.SwarmAlive = Math.Max(0, (int)MathF.Ceiling(c.Species.Body.Members * MathF.Max(0f, c.Health) / c.Stats.MaxHealth));
        if (c.Health <= 0f) Kill(c, source);
    }

    public void Kill(Creature c, ITarget killer)
    {
        if (c.Dead) return;
        c.Dead = true;
        c.Health = 0f;
        c.DeadTime = 0f;
        c.MeatLeft = c.Species.Drops.Meat;
        c.Phase = AttackPhase.None;
        c.Lunge = 0f;
        c.Rear = 0f;
        Died?.Invoke(c, killer);
    }

    internal void RaiseTelegraph(AttackEvent e) => Telegraph?.Invoke(e);
    internal void RaiseAttacked(AttackEvent e) => Attacked?.Invoke(e);
    internal void RaiseWarned(Creature c) => Warned?.Invoke(c);

    // ---- Queries ----------------------------------------------------------------------

    public ITarget NearestTarget(Vector3 from, out float distance)
    {
        ITarget best = null;
        distance = float.MaxValue;
        foreach (ITarget t in Targets)
        {
            if (t.Health != null && t.Health.Downed) continue;
            float d = Flat(t.Position - from).Length();
            if (d < distance)
            {
                distance = d;
                best = t;
            }
        }
        return best;
    }

    public Creature NearestCorpse(Vector3 from, float radius)
    {
        Creature best = null;
        float bestD = radius;
        foreach (Creature c in Creatures)
        {
            if (!c.Dead || c.Removed || c.MeatLeft <= 0f) continue;
            float d = Flat(c.Position - from).Length();
            if (d < bestD)
            {
                bestD = d;
                best = c;
            }
        }
        return best;
    }

    /// <summary>The creature a ray hits first, against each rig's hit spheres.</summary>
    public Creature Raycast(Vector3 origin, Vector3 dir, float maxDistance, out float distance, out Vector3 point)
    {
        Creature best = null;
        distance = maxDistance;
        point = origin + dir * maxDistance;
        foreach (Creature c in Creatures)
        {
            if (c.Removed) continue;
            // Cheap reject on a bounding sphere around the creature first.
            float reach = c.Rig.BoundsRadius;
            Vector3 toC = c.Rig.BoundsCenter - origin;
            float along = toC.Dot(dir);
            if (along < -reach || along > distance + reach) continue;
            if ((toC - dir * along).LengthSquared() > reach * reach) continue;

            for (int i = 0; i < c.Rig.HitCount; i++)
            {
                Vector4 s = c.Rig.Hits[i];
                var center = new Vector3(s.X, s.Y, s.Z);
                if (RaySphere(origin, dir, center, s.W, out float t) && t < distance)
                {
                    distance = t;
                    best = c;
                    point = origin + dir * t;
                }
            }
        }
        return best;
    }

    static bool RaySphere(Vector3 o, Vector3 d, Vector3 c, float r, out float t)
    {
        Vector3 m = o - c;
        float b = m.Dot(d), cc = m.Dot(m) - r * r;
        t = 0f;
        if (cc > 0f && b > 0f) return false;
        float disc = b * b - cc;
        if (disc < 0f) return false;
        t = MathF.Max(0f, -b - MathF.Sqrt(disc));
        return true;
    }

    public static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);
}
