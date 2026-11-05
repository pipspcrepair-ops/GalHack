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
    public readonly List<Pack> Packs = new();
    public readonly List<ITarget> Targets = new();
    /// <summary>Player structures on the surface. Bases (core in base mode) keep spawns away; see <see cref="SpawnRules"/>.</summary>
    public readonly List<Structure> Structures = new();

    /// <summary>
    /// Day and night: each species keeps its hours and rests at its rest site the rest of the
    /// day. Natural spawns (<see cref="SpawnGroup"/>) get rest sites when this is on; creatures
    /// put down by hand with <see cref="Spawn"/> stay awake unless given one (<see cref="AssignRest"/>).
    /// Off by default, so a sim that predates it behaves exactly as before.
    /// </summary>
    public bool ActivityCycles;
    /// <summary>Groves: cover that bedding grounds and burrows like to be near.</summary>
    public readonly List<Vector3> Groves = new();
    public readonly List<RestSite> RestSites = new();

    // Caves and places: the cracks small things slip into, nests, and what subsisters eat.
    public readonly List<Crack> Cracks = new();
    public readonly List<Nest> Nests = new();
    public readonly List<FoodPatch> Food = new();
    /// <summary>Subsister species that keep their numbers up from a place's cracks.</summary>
    public readonly List<Colony> Colonies = new();
    /// <summary>Kills of one creature by another (a place's food chain at work).</summary>
    public int Predations;
    /// <summary>Times a creature in a space lost every surface and was put back where it last stood (should stay at zero).</summary>
    public int Rescues;
    /// <summary>A creature was put back where it last stood firmly (it lost every surface).</summary>
    public event Action<Creature> Rescued;
    internal void RaiseRescued(Creature c)
    {
        Rescues++;
        Rescued?.Invoke(c);
    }
    /// <summary>An ambush or a hunt gives itself away: a rattle, dust, scratching, breathing.</summary>
    public event Action<TellEvent> Told;
    /// <summary>A predator brought down another creature.</summary>
    public event Action<Creature, Creature> Preyed;
    /// <summary>Something slipped into a crack, vent or door, or came out of one.</summary>
    public event Action<Creature, Crack, bool> Slipped;
    public Vector3 Wind = new(1.2f, 0f, 0.5f);
    public float Time;
    public int Frame;
    /// <summary>Path searches allowed per step, so a stampede can't stall a frame.</summary>
    public int PathBudgetPerStep = 6;
    int _pathBudget;
    int _nextId;

    /// <summary>
    /// Stops every brain and attack: creatures stand where they are and only idle (breathe,
    /// look about, fliers hover). For the bestiary lineup screenshot.
    /// </summary>
    public bool Frozen;

    /// <summary>When set, Step adds up time spent thinking, moving and posing (for the perf test).</summary>
    public bool Profile;
    public double ThinkMs, MoveMs, RigMs;

    /// <summary>A wind-up has started: the view plays the growl and the creature rears.</summary>
    public event Action<AttackEvent> Telegraph;
    /// <summary>A wind-up has reached its commit: the creature is now locked onto where you stood.</summary>
    public event Action<AttackEvent> Committed;
    /// <summary>A lunge has landed or missed.</summary>
    public event Action<AttackEvent> Attacked;
    public event Action<Creature, ITarget> Died;
    public event Action<Creature> Warned;
    /// <summary>A creature was added to the world, by any path.</summary>
    public event Action<Creature> Spawned;

    public CombatRules Combat => Data.Combat;

    public WildSim(WildlifeData data, Terrain terrain, NavGrid nav)
    {
        Data = data;
        Terrain = terrain;
        Nav = nav;
        Clock = new DayClock(data.World);
    }

    // ---- Spawning ---------------------------------------------------------------------

    /// <summary>
    /// True inside a base's no-spawn zone (widened by <paramref name="margin"/>). Natural spawns
    /// (<see cref="SpawnGroup"/> and the <see cref="Spawner"/>) never land there.
    /// </summary>
    public bool InBaseZone(Vector3 p, float margin = 0f) => BaseZones.Blocks(Structures, p, Data.Spawning.BaseClearance, margin);

    /// <summary>
    /// Puts one creature down exactly where asked. This is the raw primitive the tests and the
    /// screenshot staging use; natural spawning goes through <see cref="SpawnGroup"/>, which keeps
    /// out of base zones.
    /// </summary>
    public Creature Spawn(Species species, Vector3 position, Herd herd = null, Pack pack = null)
    {
        var c = new Creature
        {
            Id = _nextId++,
            Species = species,
            Herd = herd,
            Pack = pack,
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
        if (pack != null)
        {
            c.HerdSlot = pack.Members.Count;
            pack.Members.Add(c);
            pack.Leader ??= c;
        }
        c.Rig = Rig.Create(species, c);
        c.Rig.Reset(this, c);
        Creatures.Add(c);
        Spawned?.Invoke(c);
        return c;
    }

    /// <summary>
    /// A species' natural group around a point: a herd if it herds, else loose neighbours. A
    /// member whose spot falls inside a base's zone, or that <paramref name="allow"/> turns down,
    /// is not spawned at all.
    /// </summary>
    public List<Creature> SpawnGroup(Species species, Vector3 center, int count, Func<Vector3, bool> allow = null)
    {
        var list = new List<Creature>();
        Herd herd = null;
        if (species.Behaviour == Behaviour.Herd)
        {
            herd = new Herd { Home = center, Goal = center, Center = center, Spread = HerdSpread(species, count) };
            Herds.Add(herd);
        }
        Pack pack = null;
        if (species.Behaviour == Behaviour.PackHunter)
        {
            pack = new Pack { Home = center, Center = center };
            Packs.Add(pack);
        }
        var rng = Rng.For(species.Seed, "spawn", _nextId);
        float spread = herd?.Spread ?? (pack != null ? Data.Fauna.Pack.Spacing * MathF.Sqrt(count) : species.Body.Size * 3f);
        for (int i = 0; i < count; i++)
        {
            float a = i * 2.39996f, r = spread * MathF.Sqrt((i + 0.5f) / count);
            Vector3 p = center + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            p = FreeSpot(p, rng);
            if (InBaseZone(p) || (allow != null && !allow(p))) continue;
            list.Add(Spawn(species, p, herd, pack));
        }
        if (ActivityCycles && list.Count > 0) Rests.Assign(this, list);
        return list;
    }

    public static float HerdSpread(Species s, int count) => Math.Max(3f, s.Stats.Radius * 2.2f * MathF.Sqrt(count));

    /// <summary>How far from its centre a natural group of this species may spread when it spawns.</summary>
    public float GroupRadius(Species s, int count) => s.Behaviour == Behaviour.Herd ? HerdSpread(s, count)
        : s.Behaviour == Behaviour.PackHunter ? Data.Fauna.Pack.Spacing * MathF.Sqrt(count) : s.Body.Size * 3f;

    /// <summary>Nudges a spawn point off rocks and trunks.</summary>
    public Vector3 FreeSpot(Vector3 p, Rng rng)
    {
        for (int i = 0; i < 12 && Nav.Blocked(p); i++)
            p += new Vector3(rng.Range(-4f, 4f), 0f, rng.Range(-4f, 4f));
        return Terrain.Clamp(p, 8f);
    }

    public void Remove(Creature c) => c.Removed = true;

    /// <summary>
    /// Puts a creature into a cave or a place, on the surface nearest <paramref name="near"/>
    /// (a floor for a walker; floor, wall or ceiling for a climber, the one along
    /// <paramref name="up"/> if given). Its body is held off the surface by its clearance.
    /// </summary>
    public Creature SpawnIn(Species species, ISolidSpace space, Vector3 near, Vector3? up = null, Herd herd = null, Pack pack = null)
    {
        Creature c = Spawn(species, near, herd, pack);
        c.Space = space;
        c.Clearance = Crawl.ClearanceFor(species);
        Crawl.Place(this, c, near, up);
        c.Home = c.Position;
        c.Rig.Reset(this, c);
        return c;
    }

    /// <summary>Gives creatures put down by hand a rest site (a group shares one), as natural spawns get.</summary>
    public void AssignRest(List<Creature> group) => Rests.Assign(this, group);

    public void Clear()
    {
        Creatures.Clear();
        Herds.Clear();
        Packs.Clear();
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
            else if (Frozen)
            {
                c.Desired = Vector3.Zero;
                c.FacePoint = null;
            }
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

        if (Colonies.Count > 0) Colony.StepAll(this, dt);
        // Moss, fungus and scraps grow back slowly.
        if (Food.Count > 0 && Frame % 30 == 0)
            foreach (FoodPatch f in Food) f.Amount = MathF.Min(1f, f.Amount + dt * 30f / 90f);

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

    /// <summary>Gives back this step's path searches (for driving creatures outside <see cref="Step"/>).</summary>
    public void RefillPathBudget() => _pathBudget = PathBudgetPerStep;

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
        c.Falling = c.Space != null;
        Died?.Invoke(c, killer);
        if (killer is CreatureTarget predator) RaisePreyed(predator.Creature, c);
    }

    internal void RaiseTell(TellEvent e) => Told?.Invoke(e);
    internal void RaisePreyed(Creature predator, Creature prey)
    {
        Predations++;
        Preyed?.Invoke(predator, prey);
    }
    internal void RaiseSlipped(Creature c, Crack crack, bool entering) => Slipped?.Invoke(c, crack, entering);
    internal void RaiseTelegraph(AttackEvent e) => Telegraph?.Invoke(e);
    internal void RaiseAttacked(AttackEvent e) => Attacked?.Invoke(e);
    internal void RaiseCommitted(AttackEvent e) => Committed?.Invoke(e);
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
    public Creature Raycast(Vector3 origin, Vector3 dir, float maxDistance, out float distance, out Vector3 point) =>
        Raycast(Creatures, origin, dir, maxDistance, out distance, out point);

    /// <summary>The creature of a list a ray hits first (the space simulation shares it).</summary>
    public static Creature Raycast(List<Creature> creatures, Vector3 origin, Vector3 dir, float maxDistance, out float distance, out Vector3 point)
    {
        Creature best = null;
        distance = maxDistance;
        point = origin + dir * maxDistance;
        foreach (Creature c in creatures)
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

    internal static bool RaySphere(Vector3 o, Vector3 d, Vector3 c, float r, out float t)
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
