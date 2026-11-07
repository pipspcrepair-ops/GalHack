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
/// Each step moves every creature and poses it; each creature's brain, with its look round for
/// targets, runs on its own staggered timer of 0.2 to 0.5 s (<see cref="Brain.Interval"/>), which
/// is also what gives it a natural moment before it reacts.
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
    /// <summary>Settlements: wild creatures don't spawn in or near them, steer round them, and hunt nobody inside them.</summary>
    public readonly List<Settlement> Settlements = new();
    /// <summary>A wild creature has ended up inside a settlement: the town sends its guards (<see cref="CullStray"/>).</summary>
    public event Action<Creature, Settlement> StrayEntered;
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

    /// <summary>
    /// A line through the world's own collision, sim frame: true when nothing solid is on it. The game
    /// sets this to a physics ray (buildings, doors, voxel rock); unset, creatures go by the terrain,
    /// the solid cells of their space and the nav grid.
    /// </summary>
    public Func<Vector3, Vector3, bool> SightLine;

    /// <summary>Loud sounds of the last moment.</summary>
    public readonly List<WorldNoise> Noises = new();

    /// <summary>Things thrown from throats, in the air.</summary>
    public readonly List<Gob> Gobs = new();
    /// <summary>A gob left a mouth; a gob came down (on someone or not).</summary>
    public event Action<Gob> Spat;
    public event Action<Gob, bool> Splashed;
    internal void RaiseSpat(Gob g) => Spat?.Invoke(g);
    internal void RaiseSplash(Gob g, bool hit) => Splashed?.Invoke(g, hit);

    /// <summary>How lit a point is (this sim's frame), 0 to about 1; left out, everywhere is plain to see.</summary>
    public Func<Vector3, float> LightAt;
    readonly Dictionary<ITarget, (float time, float light)> _lit = new();

    /// <summary>How lit a target is. Worked out a few times a second however many are looking.</summary>
    public float LightOn(ITarget t)
    {
        if (LightAt == null) return 1f;
        if (_lit.TryGetValue(t, out var was) && Time - was.time < 0.3f) return was.light;
        float light = MathF.Max(LightAt(t.Position + Vector3.Up * 1.2f), t.Lamp);
        _lit[t] = (Time, light);
        return light;
    }

    /// <summary>The game's blocks, for the block-breakers; and whether they may harm players' bases (a server setting).</summary>
    public IBlockWorld Blocks;
    public bool BreakBases = true;
    /// <summary>A breaker started on a block (the warning), and landed a blow.</summary>
    public event Action<Creature, BlockAt> Battering, Broke;
    internal void RaiseBattering(Creature c, BlockAt b) => Battering?.Invoke(c, b);
    internal void RaiseBroke(Creature c, BlockAt b) => Broke?.Invoke(c, b);

    /// <summary>
    /// The young of a herd at ease don't walk calmly like their parents: a quick dash out round the
    /// herd and back, a sudden stop, another. When the herd is alarmed they keep to it.
    /// </summary>
    void Frisk(Creature c, float dt)
    {
        if (c.Herd == null || c.Herd.Alarmed || c.State is CState.Flee or CState.Rest) { c.FriskFor = 0f; return; }
        if (c.FriskFor > 0f)
        {
            c.FriskFor -= dt;
            if (c.FriskAfter is { Dead: false } mate)
            {
                // Chasing another of the young: at it, a pounce, and off again.
                Vector3 to = Flat(mate.Position - c.Position);
                if (to.Length() < c.Radius + mate.Radius + 0.4f) { c.Lunge = 1f; c.FriskFor = 0f; mate.FriskFor = MathF.Max(mate.FriskFor, 0.8f); mate.FriskDir = to.LengthSquared() > 1e-4f ? to.Normalized() : mate.Forward; }
                else c.FriskDir = to.Normalized();
            }
            c.Desired = c.FriskDir * c.Stats.RunSpeed * 0.8f;
            return;
        }
        if ((c.FriskIn -= dt) > 0f) return;
        c.FriskIn = c.Rng.Range(2f, 6f);
        c.FriskFor = c.Rng.Range(0.6f, 1.5f);
        // Out round the herd, never far: back towards the middle once it's out at the edge.
        Vector3 toMiddle = Flat(c.Herd.Center - c.Position);
        float a = c.Rng.Range(-MathF.PI, MathF.PI);
        Vector3 dir = new(MathF.Cos(a), 0f, MathF.Sin(a));
        if (toMiddle.Length() > c.Herd.Spread * 0.6f) dir = (dir * 0.4f + toMiddle.Normalized()).Normalized();
        c.FriskDir = dir;
        // Half the time it goes after another of the young; a grown one looks up to watch.
        c.FriskAfter = null;
        foreach (Creature o in c.Herd.Members)
        {
            if (o == c || o.Dead || o.Removed) continue;
            if (o.Species.Adult != null && c.FriskAfter == null && c.Rng.Chance(0.5f)) c.FriskAfter = o;
            else if (o.Species.Adult == null && o.LookAt == null) { o.LookAt = c.Position + Vector3.Up; break; }
        }
    }

    /// <summary>Something that hunts by its voice clicked.</summary>
    public event Action<Creature> Clicked;
    internal void RaiseClick(Creature c) => Clicked?.Invoke(c);

    /// <summary>A loud sound: everything within the radius turns to look.</summary>
    public void Noise(Vector3 at, float radius) => Noises.Add(new WorldNoise { Position = at, Radius = radius, Time = Time });

    readonly Dictionary<ITarget, (Vector3 at, float speed)> _moved = new();

    /// <summary>Noise of a crouch-walker and of someone crouched still, against 1 for a walker and 0.45 for standing still.</summary>
    public const float CrouchWalkNoise = 0.6f, CrouchStillNoise = 0.25f;

    /// <summary>How much noise a target is making: a share of its threat by how fast it is going, and less crouched.</summary>
    public float NoiseOf(ITarget t)
    {
        float speed = _moved.TryGetValue(t, out var m) ? m.speed : 2f;
        if (t.Crouched) return speed < 0.4f ? CrouchStillNoise : CrouchWalkNoise;
        return speed < 0.4f ? 0.45f : speed > 4.5f ? 1.3f : 1f;
    }

    void WatchTargets(float dt)
    {
        foreach (ITarget t in Targets)
        {
            Vector3 p = t.Position;
            float speed = _moved.TryGetValue(t, out var m) ? Mathf.Lerp(m.speed, (p - m.at).Length() / MathF.Max(dt, 1e-3f), 0.15f) : 2f;
            _moved[t] = (p, speed);
        }
        if (Noises.Count > 0) Noises.RemoveAll(n => Time - n.Time > 1f);
    }

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
    public bool InBaseZone(Vector3 p, float margin = 0f) => BaseZones.Blocks(Structures, p, Data.Spawning.BaseClearance, margin) || TownZone(p, margin);

    /// <summary>A settlement registers its area here: from then on wildlife treats it like a base, and keeps out.</summary>
    public Settlement AddSettlement(string name, Vector3 center, float radius)
    {
        var s = new Settlement { Name = name, Center = center, Radius = radius };
        Settlements.Add(s);
        return s;
    }

    /// <summary>The settlement a point is inside (widened by a margin), if any.</summary>
    public Settlement TownAt(Vector3 p, float margin = 0f)
    {
        foreach (Settlement s in Settlements)
            if (s.Contains(p, margin)) return s;
        return null;
    }

    public bool InTown(Vector3 p, float margin = 0f) => TownAt(p, margin) != null;

    /// <summary>Inside a settlement or its spawning margin (the base clearance unless the town sets its own).</summary>
    bool TownZone(Vector3 p, float margin)
    {
        foreach (Settlement s in Settlements)
            if (s.Contains(p, (s.SpawnMargin >= 0f ? s.SpawnMargin : Data.Spawning.BaseClearance) + margin)) return true;
        return false;
    }

    /// <summary>
    /// The town's guards deal with a wild creature that got in: it is killed outright (its
    /// corpse stays, for whoever butchers it). The guard AI that calls this comes later.
    /// </summary>
    public void CullStray(Creature c, ITarget guards = null)
    {
        if (c.Dead || c.Species.Domestic != DomesticKind.None) return;
        Damage(c, c.Health + 1f, guards);
    }

    /// <summary>Wild creatures inside a settlement now.</summary>
    public List<Creature> Strays(Settlement town)
    {
        var list = new List<Creature>();
        foreach (Creature c in Creatures)
            if (!c.Removed && c.Alive && c.Species.Domestic == DomesticKind.None && !c.Species.InSpace && town.Contains(c.Position)) list.Add(c);
        return list;
    }

    /// <summary>
    /// A settlement's own animal, kept to an area: stock in a pen or field, a working beast by
    /// its cart, a pet with its owner (it follows them about, or lies around near home).
    /// </summary>
    public Creature SpawnDomestic(Species species, Vector3 at, Vector3 penCenter, float penRadius, ITarget owner = null)
    {
        Creature c = Spawn(species, at);
        c.PenCenter = penCenter;
        c.PenRadius = penRadius;
        c.Owner = owner;
        return c;
    }

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
        // Half of a dimorphic species is the other sex.
        if (species.Mate != null && c.Rng.Chance(0.5f))
        {
            species = species.Mate;
            c.Species = species;
            c.Health = species.Stats.MaxHealth;
        }
        c.Yaw = c.Rng.Range(-MathF.PI, MathF.PI);
        c.Position = new Vector3(position.X, Terrain.Height(position.X, position.Z), position.Z);
        c.SwarmAlive = species.Body.Members;
        c.ThinkEvery = c.Rng.Range(0f, 0.3f); // so a group put down together doesn't all think on one frame
        if (species.Flies)
        {
            c.Altitude = species.Rig == RigKind.Swarm ? c.Rng.Range(1.2f, 2.5f) : species.Rig == RigKind.GasBag ? c.Rng.Range(5f, 11f)
                : species.Flight == FlightKind.Hover ? c.Rng.Range(1.8f, 4.5f) : c.Rng.Range(8f, 15f);
            c.Position += Vector3.Up * c.Altitude;
            if (species.Flight == FlightKind.Glide) c.Velocity = c.Forward * Heft.CruiseAirspeed(c); // already flying, as a glider has to be
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
        // A herd keeps its young in the middle, where the spiral starts: the adults stand round them.
        int young = herd != null && species.Young != null && count >= 3 ? Math.Max(1, count / 4) : 0;
        for (int i = 0; i < count; i++)
        {
            float a = i * 2.39996f, r = spread * MathF.Sqrt((i + 0.5f) / count);
            Vector3 p = center + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            p = FreeSpot(p, rng);
            if (InBaseZone(p) || (allow != null && !allow(p))) continue;
            list.Add(Spawn(i < young ? species.Young : species, p, herd, pack));
        }
        if (ActivityCycles && list.Count > 0) Rests.Assign(this, list);
        return list;
    }

    public static float HerdSpread(Species s, int count) => Math.Max(3f, s.Stats.Radius * 2.2f * MathF.Sqrt(count));

    /// <summary>How far from its centre a natural group of this species may spread when it spawns.</summary>
    public float GroupRadius(Species s, int count) => s.Behaviour == Behaviour.Herd ? HerdSpread(s, count)
        : s.Behaviour == Behaviour.PackHunter ? Data.Fauna.Pack.Spacing * MathF.Sqrt(count) : s.Body.Size * 3f;

    /// <summary>Far enough from every player, and not in front of anyone who could see it (see <see cref="SpawnRules"/>).</summary>
    public bool ClearOfPlayers(Vector3 at, float margin)
    {
        SpawnRules rules = Data.Spawning;
        float cone = MathF.Cos(Mathf.DegToRad(rules.ViewConeDegrees));
        foreach (ITarget t in Targets)
        {
            Vector3 to = Flat(at - t.Position);
            float d = to.Length();
            if (d < rules.MinPlayerDistance + margin) return false;
            Vector3 look = Flat(t.Forward);
            if (d < rules.ViewDistance + margin && look.LengthSquared() > 1e-4f && look.Normalized().Dot(to / d) > cone) return false;
        }
        return true;
    }

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
        WatchTargets(dt);
        if (Gobs.Count > 0) Spitting.Step(this, dt);

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
                c.ThinkClock += dt;
                if (c.Attacking || c.ThinkClock >= c.ThinkEvery)
                {
                    c.ThinkDt = c.ThinkClock;
                    c.ThinkClock = 0f;
                    Brain.Update(this, c, c.ThinkDt);
                    c.ThinkEvery = Brain.Interval(c);
                }
                else Brain.Follow(c);
                Attacks.Update(this, c, dt);
                if (c.Species.Breaker != BreakerKind.None) Breaking.Step(this, c, dt);
                if (c.Species.Adult != null) Frisk(c, dt);
            }
            if (Profile) { long t = Stopwatch.GetTimestamp(); thinkTicks += t - t0; t0 = t; }
            Locomotion.Move(this, c, dt);
            Locomotion.KeepOff(this, c);
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
        if (Settlements.Count > 0 && Frame % 15 == 0) WatchTowns();
        // Moss, fungus and scraps grow back slowly.
        if (Food.Count > 0 && Frame % 30 == 0)
            foreach (FoodPatch f in Food) f.Amount = MathF.Min(1f, f.Amount + dt * 30f / 90f);

        // Drop removed creatures now and then, never mid-loop.
        if (Frame % 120 == 0) Creatures.RemoveAll(c => c.Removed);
    }

    /// <summary>Tells a town when something wild gets in.</summary>
    void WatchTowns()
    {
        foreach (Creature c in Creatures)
        {
            if (c.Removed || c.Dead || c.Species.Domestic != DomesticKind.None || c.Species.InSpace) continue;
            Settlement town = TownAt(c.Position);
            if (town != null && !c.InTown) StrayEntered?.Invoke(c, town);
            c.InTown = town != null;
        }
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
        // It flinches: rocks back and loses its stride, the bigger the less.
        if (!c.Attacking)
        {
            float heft = 1f - 0.7f * Heft.Bulk(c.Species);
            c.Rear = MathF.Max(c.Rear, 0.5f * heft);
            // A glider on the wing keeps its way on: it can't stop, and if this kills it, it falls along the line it was flying.
            if (c.Species.Flight != FlightKind.Glide || c.Landed) c.Velocity *= 1f - 0.7f * heft;
            c.CrawlSpeed *= 1f - 0.7f * heft;
            c.ThinkEvery = 0f;
        }
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
        c.AirAimOn = false;
        c.Flapping = false;
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
            // Nobody is hunted inside a settlement: wildlife keeps out.
            if (Settlements.Count > 0 && InTown(t.Position)) continue;
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
