using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>What it struck with; how near its striking part came to the victim's body, m (negative: into it); how the blow throws them, m/s.</summary>
    public AttackMove Move;
    public float Gap;
    public Vector3 Push;
    /// <summary>Where its striking part was at the blow (a gob where it splashed), sim frame.</summary>
    public Vector3 Part;
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
    /// <summary>The dangerous and the people of every band, by ground cell: what bands and towns look round for.</summary>
    public readonly Sightings Sightings = new();
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

    /// <summary>Creatures the last step moved (the dormant and removed left out), and path searches taken so far (measurements).</summary>
    public int Stepped, Paths;
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
    /// <summary>The ground shakes under something heavy: a stomp, a charge's footfalls.</summary>
    public event Action<QuakeEvent> Quaked;
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

    /// <summary>
    /// Whether a point (sim frame) is inside something solid that the terrain's heights don't know
    /// of: a place's walls and floors standing on the surface. The game sets it; walkers out on the
    /// surface never step into it, and nothing spawns in it. (Creatures of a cave or a place go by
    /// their own <see cref="ISolidSpace"/>.)
    /// </summary>
    public Func<Vector3, bool> SolidAt;

    /// <summary>A Husk has seized someone by the head; let go of them (broken free or not); turned them into one of its own.</summary>
    public event Action<SeizeEvent> Seized, LetGo, Turned;
    internal void RaiseSeized(SeizeEvent e) => Seized?.Invoke(e);
    internal void RaiseLetGo(SeizeEvent e) => LetGo?.Invoke(e);
    internal void RaiseTurned(SeizeEvent e) => Turned?.Invoke(e);
    /// <summary>Someone held at gunpoint tossed a bundle of their goods toward whoever held them (who, at whom, where it is to land, how frightened 0..1): the game throws the bag.</summary>
    public event Action<Creature, ITarget, Vector3, float> Offered;
    internal void RaiseOffered(Creature c, ITarget to, Vector3 at, float fear) => Offered?.Invoke(c, to, at, fear);
    /// <summary>Rummages played over a dead one's things, and offers made (tests).</summary>
    public int Rummages, Offers;
    /// <summary>A Husk murmured through its worms (a game plays it, low).</summary>
    public event Action<Creature> Murmured;
    internal void RaiseMurmur(Creature c) => Murmured?.Invoke(c);

    /// <summary>Husk dens' worm masses on the floors and walls, and one destroyed.</summary>
    public readonly List<WormMass> Masses = new();
    /// <summary>The veins of worm flesh spread round a den's masses (<see cref="Veins"/>): drawn, never stepped.</summary>
    public readonly List<Vein> Veins = new();
    /// <summary>How far gone the den this sim holds is, 0..1 (<see cref="PoiPlan.Severity"/>); none for open country.</summary>
    public float DenSeverity;
    /// <summary>Where the Husks are, refreshed now and then for animals to keep off (<see cref="Wrongness.Shunned"/>), and dens elsewhere a game adds.</summary>
    public readonly List<Vector3> HuskSpots = new();
    public readonly List<Vector3> WrongPlaces = new();
    /// <summary>Whoever was just let go of, and until when (this sim's clock) nothing may seize them again (<see cref="Seizing.Spare"/>).</summary>
    public readonly Dictionary<ITarget, float> Spared = new();
    public event Action<WormMass> MassDied;
    internal void RaiseMassDied(WormMass m) => MassDied?.Invoke(m);

    /// <summary>Who a seized person becomes: the same person as a Husk. A game may hand it the world's own (<see cref="WorldLife.Turn"/>).</summary>
    public Func<Species, ulong, Species> TurnHost;

    /// <summary>Creatures found inside solid geometry and freed (see <see cref="Interpenetration"/>); should stay at zero.</summary>
    public int Freed;
    float _scanAt;

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
            // Only where there is room to dash: never into a rock, a trunk or a wall.
            if (!Brain.Clear(this, c, c.Position, c.Position + c.FriskDir * 2f)) { c.FriskFor = 0f; return; }
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

    /// <summary>A loud sound: everything within the radius turns to look. A shot says who fired it and along which way (people read a shot past them as being shot at).</summary>
    public void Noise(Vector3 at, float radius, Vector3 dir = default, ITarget by = null) => Noises.Add(new WorldNoise { Position = at, Radius = radius, Time = Time, Dir = dir, By = by });

    /// <summary>Your standing with whoever holds the ground at a point (their people out in the world), the game's -100 to 100; left out, nobody holds it.</summary>
    public Func<Vector3, float> StandingAt;

    /// <summary>This world's form of the friendly gestures (<see cref="Gestures.FriendlyStyle"/>).</summary>
    public int GreetStyle;

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
        nav.Ground(terrain); // (one height a cell: every gait's moves are judged by it)
        Clock = new DayClock(data.World);
        TurnHost = (host, seed) => Husks.Turn(host, seed, Data.Husks);
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

    /// <summary>Something throws a creature: it flies, whatever it was doing, and lands hard (<see cref="Locomotion"/>).</summary>
    public void Throw(Creature c, Vector3 velocity)
    {
        if (c.Space != null || c.Removed || c.Thrown) return;
        c.Thrown = true;
        c.Velocity = velocity;
        c.Position += Vector3.Up * 0.3f;
        // Thrown unevenly, it turns end over end about the way it flies, and round with the throw; the
        // smaller it is the faster.
        var flat = new Vector3(velocity.X, 0f, velocity.Z);
        float size = MathF.Max(0.3f, c.Species.Body.Size);
        c.TumbleSpin = (Vector3.Up.Cross(flat) * 0.35f + Vector3.Up * flat.Length() * 0.15f) / size;
    }

    /// <summary>
    /// Puts one creature down exactly where asked. This is the raw primitive the tests and the
    /// screenshot staging use; natural spawning goes through <see cref="SpawnGroup"/>, which keeps
    /// out of base zones.
    /// </summary>
    public Creature Spawn(Species species, Vector3 position, Herd herd = null, Pack pack = null) => Spawn(species, position, herd, pack, false);

    /// <summary>
    /// A creature of a species here. <paramref name="asIs"/>: exactly this body, as when one is
    /// carried from another sim (no fresh pick among a kind's bodies or sexes: it stays who it was).
    /// </summary>
    public Creature Spawn(Species species, Vector3 position, Herd herd, Pack pack, bool asIs)
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
        // A kind drawn from several bodies (a world's Husks) is one of them each time.
        if (!asIs && species.Hosts is { Count: > 0 } hosts)
        {
            species = hosts[c.Rng.Int(0, hosts.Count - 1)];
            c.Species = species;
            c.Health = species.Stats.MaxHealth;
        }
        // Half of a dimorphic species is the other sex.
        if (!asIs && species.Mate != null && c.Rng.Chance(0.5f))
        {
            species = species.Mate;
            c.Species = species;
            c.Health = species.Stats.MaxHealth;
        }
        c.Yaw = c.Rng.Range(-MathF.PI, MathF.PI);
        c.Nerve = species.Mechanical ? 1f : Rng.For(species.Seed, "nerve", c.Id).Float(); // its own stream: nothing else it rolls changes
        c.Position = c.GoodAt = c.Behind = c.ProgressAt = new Vector3(position.X, Terrain.Height(position.X, position.Z), position.Z);
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
        if (species.Behaviour is Behaviour.Herd or Behaviour.Folk)
        {
            herd = new Herd { Home = center, Goal = center, Center = center, Spread = HerdSpread(species, count) };
            // People go about as a band: its stance rolled now, from who they are and their world.
            if (species.Behaviour == Behaviour.Folk) herd.Party = Party.Roll(species, Rng.Mix(species.Seed, (ulong)_nextId));
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
            if (InBaseZone(p) || (allow != null && !allow(p)) || Interpenetration.SolidSurface(this, p, species)) continue;
            list.Add(Spawn(i < young ? species.Young : species, p, herd, pack));
        }
        if (ActivityCycles && list.Count > 0) Rests.Assign(this, list);
        if (herd?.Party != null && list.Count > 0) Kennel(herd, center, rng, allow);
        return list;
    }

    /// <summary>Wild kinds people round here may keep on their side (the game sets them from its world's: <see cref="CrossReactions.Keepable"/>).</summary>
    public readonly List<Species> Beasts = new();

    /// <summary>A beast people keep from now on: its kind's collared body (<see cref="Species.Kept"/>), on their side.</summary>
    public Creature Keep(Creature c, IKeeper keeper)
    {
        c.Keeper = keeper;
        if (c.Species.Collared) return c;
        c.Species = c.Species.Kept;
        c.Rig = Rig.Create(c.Species, c);
        c.Rig.Reset(this, c);
        return c;
    }

    /// <summary>A band that keeps beasts (by the data's odds for its sort: raiders, or any other) has one or two of the world's own at its side.</summary>
    void Kennel(Herd band, Vector3 at, Rng rng, Func<Vector3, bool> allow)
    {
        if (Beasts.Count == 0 || !rng.Chance(Data.People.Keeps(band.Party.Raids ? "raiders" : "band"))) return;
        Species s = Beasts[rng.Int(0, Beasts.Count - 1)];
        for (int i = rng.Chance(0.4f) ? 2 : 1; i > 0; i--)
        {
            Vector3 p = FreeSpot(at + new Vector3(rng.Range(-4f, 4f), 0f, rng.Range(-4f, 4f)), rng);
            if (!InBaseZone(p) && (allow == null || allow(p)) && !Interpenetration.SolidSurface(this, p, s)) Keep(Spawn(s, p), band);
        }
    }

    public static float HerdSpread(Species s, int count) => Math.Max(3f, s.Stats.Radius * 2.2f * MathF.Sqrt(count));

    /// <summary>How far from its centre a natural group of this species may spread when it spawns.</summary>
    public float GroupRadius(Species s, int count) => s.Behaviour is Behaviour.Herd or Behaviour.Folk ? HerdSpread(s, count)
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
        for (int i = 0; i < 12 && (Nav.Blocked(p) || Steep(p)); i++)
            p += new Vector3(rng.Range(-4f, 4f), 0f, rng.Range(-4f, 4f));
        return Terrain.Clamp(p, 8f);
    }

    /// <summary>Ground at a point steeper than a slope (by default the least any walker takes: a face, a cliff): no spot to be put or to make for.</summary>
    public bool Steep(Vector3 p, float slope = 0.7f)
    {
        Vector3 n = Terrain.Normal(p.X, p.Z);
        return new Vector2(n.X, n.Z).Length() > n.Y * slope;
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
        // A spot inside a wall, a pillar or a stair is no spot: the nearest open floor near it, or nowhere.
        if (Interpenetration.Inside(this, c) && !Interpenetration.Free(this, c, near)) c.Removed = true;
        // Nor in another body: a walker put down where one stands already goes to open floor a step or two off.
        if (!c.Removed && !species.Climbs && Crowded(c, c.Position)) Spread(c);
        c.Home = c.Position;
        c.Rig.Reset(this, c);
        return c;
    }

    /// <summary>Whether another body on a floor of the same place (asleep or not) stands within reach of one at a point.</summary>
    bool Crowded(Creature c, Vector3 at)
    {
        foreach (Creature o in Creatures)
            if (o != c && o.Space == c.Space && !o.Removed && !o.Dead && !o.InWalls && !o.Species.Climbs && MathF.Abs(o.Position.Y - at.Y) < 1.5f && Flat(o.Position - at).Length() < c.Radius + o.Radius) return true;
        return false;
    }

    /// <summary>Onto the nearest floor round it, in the open and in sight of where it was, that no other body stands on (left where it was if none within three metres).</summary>
    void Spread(Creature c)
    {
        Vector3 from = Crawl.Center(c);
        for (float r = c.Radius * 2f; r <= 3f; r += 0.5f)
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = c.Position + new Vector3(MathF.Cos(k * MathF.Tau / 8f), 0f, MathF.Sin(k * MathF.Tau / 8f)) * r;
                if (Crawl.Stands(c, p) is not Vector3 at || Crowded(c, at) || !SpaceMath.Clear(c.Space, from, at + Vector3.Up * c.Clearance, 0f)) continue;
                c.Position = at;
                return;
            }
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

    /// <summary>Personal space: no two bodies on the ground pass through each other, nor through you (<see cref="PersonalSpace"/>).</summary>
    public readonly PersonalSpace Space = new();
    readonly List<Creature> _spaced = new();

    /// <summary>
    /// Whether a body keeps its personal space: one on the ground or a floor (not one closing to strike or seize: an attack
    /// needs its reach; in a cave or a place, not a climber holding its walls; not a swarm, a cloud, nor a gas bag, which floats).
    /// </summary>
    public static bool Spaced(Creature c) =>
        !(c.Removed || c.Dead || c.Dormant || c.HeldBy != null || c.Holding != null || c.Attacking || c.Thrown || c.InWalls
          || c.Space != null && (c.Species.Climbs || c.Falling)
          || c.Species.Rig is RigKind.Swarm or RigKind.GasBag || c.Species.Flies && !c.Landed && c.Space == null);

    void KeepApart(float dt)
    {
        Space.Bodies.Clear();
        _spaced.Clear();
        ISolidSpace inside = null;
        foreach (Creature c in Creatures)
        {
            c.Spaced = -1;
            if (!Spaced(c)) continue;
            c.Spaced = _spaced.Count;
            inside ??= c.Space;
            Vector3 v = Flat(c.Velocity);
            Space.Bodies.Add(new PersonalSpace.Body { Pos = c.Position, Radius = c.Radius, Heading = v.LengthSquared() > 0.04f ? v.Normalized() : Vector3.Zero, Give = c.Attacking ? 0.4f : 1f });
            _spaced.Add(c);
        }
        if (_spaced.Count == 0) return;
        // Whoever the creatures meet on foot (you, a town's people) gives nothing here: they are stepped round.
        foreach (ITarget t in Targets)
            if (t.Attackable) Space.Bodies.Add(new PersonalSpace.Body { Pos = t.Position, Radius = 0.35f, Give = 0f });
        // Where each body can be put (its own say, so that of two the one that can't be moved, the other takes the whole push).
        Space.Resolve(dt, _stands ??= (i, p) => i >= _spaced.Count || Stands(_spaced[i], p), 4);
        for (int i = 0; i < _spaced.Count; i++)
        {
            Vector3 moved = Space.Bodies[i].Moved;
            if (moved.LengthSquared() <= 1e-8f) continue;
            Creature c = _spaced[i];
            Vector3 was = c.Position, to = was + new Vector3(moved.X, 0f, moved.Z);
            if (c.Space != null)
            {
                if (Crawl.Stands(c, to) is Vector3 at) c.Position = at;
                continue;
            }
            if (!Stands(c, to)) continue;
            c.Position = to;
            if (Interpenetration.Inside(this, c)) c.Position = was;
        }
    }

    Func<int, Vector3, bool> _stands;
    readonly List<int> _near = new();

    /// <summary>
    /// Whether a body pushed (by another) to a point would stand there: in a cave or a place, on its floor, clear of the walls and
    /// no further into the furniture; out on the ground, never into a trunk or a rock, onto a face its legs don't take, nor into anything built.
    /// </summary>
    bool Stands(Creature c, Vector3 p) =>
        c.Space != null ? Crawl.Stands(c, p) != null
        : !Locomotion.IntoTrees(this, c, p) && !Locomotion.Face(this, c.Position, p, Brain.GaitOf(c.Species)) && !Interpenetration.SolidSurface(this, p, c.Species);

    /// <summary>Whether a striking body moved to a point would be further into anyone but whom it strikes at than it is (a lunge or a charge stops at an ally or a bystander, never goes through).</summary>
    public bool IntoBody(Creature c, Vector3 to, ITarget at)
    {
        Space.Near(to, _near);
        foreach (int j in _near)
        {
            Vector3 o;
            float r;
            if (j < _spaced.Count)
            {
                Creature b = _spaced[j];
                if (b == c || !Spaced(b) || at is CreatureTarget ct && ct.Creature == b) continue;
                (o, r) = (b.Position, b.Radius);
            }
            else if (at != null && Flat(Space.Bodies[j].Pos - at.Position).LengthSquared() < 0.04f) continue;
            else (o, r) = (Space.Bodies[j].Pos, Space.Bodies[j].Radius);
            float d = Flat(to - o).Length();
            if (MathF.Abs(o.Y - to.Y) < 1.5f && d < c.Radius + r && d < Flat(c.Position - o).Length()) return true;
        }
        return false;
    }

    /// <summary>
    /// A body's own step, held from going any further into anyone else than it already is (owner: "NPCs should path around
    /// each other, too"): what would take it into another goes along its side instead, and dead on not at all. Nobody walks
    /// through anybody, whatever its way says; personal space parts what is left.
    /// </summary>
    public Vector3 Unpressed(Creature c, Vector3 from, Vector3 to)
    {
        if (c.Spaced < 0 || c.Spaced >= _spaced.Count || _spaced[c.Spaced] != c) return to;
        Space.Near(to, _near);
        foreach (int j in _near)
        {
            if (j == c.Spaced) continue;
            Vector3 o;
            float r;
            if (j < _spaced.Count)
            {
                // (Not one walking off away from it: that gap opens by itself.)
                Creature b = _spaced[j];
                Vector3 off = Flat(b.Position - from);
                if (!Spaced(b) || off.LengthSquared() > 1e-4f && Flat(b.Velocity).Dot(off.Normalized()) > 0.3f) continue;
                (o, r) = (b.Position, b.Radius);
            }
            else (o, r) = (Space.Bodies[j].Pos, Space.Bodies[j].Radius);
            if (MathF.Abs(o.Y - from.Y) > 1.5f) continue;
            Vector3 now = Flat(from - o), next = Flat(to - o);
            float want = c.Radius + r, d = next.Length();
            if (d >= want || d >= now.Length()) continue; // (clear of it, or no further in)
            // The part of the step toward it goes; what is left goes along its side.
            Vector3 n = now.LengthSquared() > 1e-6f ? now.Normalized() : next.LengthSquared() > 1e-6f ? next.Normalized() : Vector3.Right;
            Vector3 step = Flat(to - from);
            float into = step.Dot(n);
            if (into < 0f) step -= n * into;
            to = new Vector3(from.X + step.X, to.Y, from.Z + step.Z);
        }
        return to;
    }

    readonly List<Creature> _rigDue = new();
    Action<int> _pose;

    /// <summary>Rigs posed on the worker threads once this many are due in a step (fewer aren't worth the hand-off); off for an A/B.</summary>
    public static bool ParallelRigs = true;
    const int ParallelFrom = 8;

    void Pose(Creature c)
    {
        c.Rig.Update(this, c, c.RigPending);
        c.RigPending = 0f;
    }

    public void Step(float dt)
    {
        Time += dt;
        Frame++;
        Clock.Advance(dt);
        _pathBudget = PathBudgetPerStep;
        WatchTargets(dt);
        if (Gobs.Count > 0) Spitting.Step(this, dt);

        long t0 = Profile ? Stopwatch.GetTimestamp() : 0, thinkTicks = 0, moveTicks = 0, rigTicks = 0;
        Stepped = 0;
        for (int i = 0; i < Creatures.Count; i++)
        {
            Creature c = Creatures[i];
            if (c.Removed || c.Dormant && !c.Dead) continue;
            Stepped++;
            c.Age += dt;
            c.HurtFlash = MathF.Max(0f, c.HurtFlash - dt * 4f);
            c.StunReady = MathF.Max(0f, c.StunReady - dt);
            if (c.HeldBy != null && (c.HeldBy.Removed || c.HeldBy.Dead || c.HeldBy.Holding != c.AsTarget)) c.HeldBy = null;
            if (c.Dead) UpdateCorpse(c, dt);
            else if (c.HeldBy != null)
            {
                // Seized face to face: it can do nothing but be held.
                c.Desired = Vector3.Zero;
                c.Velocity = Vector3.Zero;
                c.FacePoint = c.HeldBy.Position;
                c.LookAt = c.HeldBy.Position + Vector3.Up * 1.5f;
                c.Rear = 0.6f;
            }
            else if (c.StunFor > 0f)
            {
                // Stunned: caught where it stands, in its flinch, for a moment.
                c.StunFor -= dt;
                c.Desired = Vector3.Zero;
                c.Velocity = Vector3.Zero;
            }
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
            if (!c.Dead && !Frozen) Brain.Headway(this, c, dt);
            // In a cave or a place nothing ends its step in a wall or a piece of furniture (a landing, a spawn): out at once.
            if (c.Space != null && !c.Dead && Interpenetration.Inside(this, c)) Interpenetration.Out(this, c);
            if (Profile) { long t = Stopwatch.GetTimestamp(); moveTicks += t - t0; t0 = t; }

            // Far creatures re-pose less often; the rig catches up with the summed time.
            c.RigPending += dt;
            if (c.RigInterval <= 1 || c.Attacking || (Frame + c.Id) % c.RigInterval == 0) _rigDue.Add(c); // a strike is judged on the pose, so it's posed every step
        }
        // The poses, after every body has moved: each rig reads only its own body, the ground and the place's blocks, so
        // a crowd's are worked out across the cores.
        if (Profile) t0 = Stopwatch.GetTimestamp();
        if (ParallelRigs && _rigDue.Count >= ParallelFrom) System.Threading.Tasks.Parallel.For(0, _rigDue.Count, _pose ??= i => Pose(_rigDue[i]));
        else foreach (Creature c in _rigDue) Pose(c);
        _rigDue.Clear();
        if (Profile)
        {
            rigTicks = Stopwatch.GetTimestamp() - t0;
            double ms = 1000.0 / Stopwatch.Frequency;
            ThinkMs += thinkTicks * ms;
            MoveMs += moveTicks * ms;
            RigMs += rigTicks * ms;
        }

        if (!Frozen) KeepApart(dt);
        if (Colonies.Count > 0) Colony.StepAll(this, dt);
        if (Masses.Count > 0) WormMasses.Step(this, dt);
        if (Settlements.Count > 0 && Frame % 15 == 0) WatchTowns();
        if (Frame % 15 == 7) Wrongness.Refresh(this);
        if (Frame % 15 == 3) Sightings.Refresh(this);
        // Now and then, anything that has ended up inside a wall or a floor is put back in the open.
        if (Time >= _scanAt)
        {
            _scanAt = Time + 1f;
            foreach (Creature c in Interpenetration.Scan(this)) if (Interpenetration.Free(this, c)) Freed++;
        }
        // Moss, fungus and scraps grow back slowly.
        if (Food.Count > 0 && Frame % 30 == 0)
            foreach (FoodPatch f in Food) f.Amount = MathF.Min(1f, f.Amount + dt * 30f / 90f);

        // Drop removed creatures now and then, never mid-loop.
        if (Frame % 120 == 0)
        {
            Creatures.RemoveAll(c => c.Removed);
            // Bodies lie a long while, but not without end: past the most a world keeps, the oldest go first.
            int bodies = 0;
            foreach (Creature c in Creatures) if (c.Dead) bodies++;
            if (bodies > MostBodies)
                foreach (Creature c in Creatures.Where(c => c.Dead).OrderByDescending(c => c.DeadTime).Take(bodies - MostBodies)) c.Removed = true;
        }
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

    /// <summary>How long a body lies before it goes, seconds of play: long enough to come back to (the owner wants them to stay "a lot a lot longer"). Picked clean, the remains lie on as long.</summary>
    public const float CorpseLasts = 3600f;

    /// <summary>The most bodies lying in the world at once; past it the oldest go.</summary>
    public const int MostBodies = 150;

    void UpdateCorpse(Creature c, float dt)
    {
        c.DeadTime += dt;
        c.Desired = Vector3.Zero;
        c.Phase = AttackPhase.None;
        if (c.DeadTime > CorpseLasts) c.Removed = true;
    }

    /// <summary>Gives back this step's path searches (for driving creatures outside <see cref="Step"/>).</summary>
    public void RefillPathBudget() => _pathBudget = PathBudgetPerStep;

    /// <summary>A search (or a flight's two floods: <paramref name="cost"/> 2) out of this step's budget, if it has that much left.</summary>
    public bool TakePathBudget(int cost = 1)
    {
        if (_pathBudget < cost) return false;
        _pathBudget -= cost;
        Paths++;
        return true;
    }

    /// <summary>Bodies that made no headway for a second and looked for another way, and those still stuck that were put back where they last went well (<see cref="Brain.Headway"/>).</summary>
    public int Repaths, Unstuck;

    /// <summary>Chasers that lost interest in someone they couldn't get to (<see cref="Brain"/>'s GiveUp; tests).</summary>
    public int GaveUp;

    /// <summary>A way worked out for a group (a herd's or a pack's flight), reused.</summary>
    public readonly List<Vector3> Way = new();

    Func<Vector3, bool> _builtGround;

    /// <summary>Whether a point of the ground has something built on it at a body's middle (<see cref="SolidAt"/> there): what the fine window round buildings is blocked by.</summary>
    public Func<Vector3, bool> BuiltGround => _builtGround ??= p => SolidAt != null && SolidAt(new Vector3(p.X, Terrain.Height(p.X, p.Z) + 0.4f, p.Z));

    // ---- Damage -----------------------------------------------------------------------

    public void Damage(Creature c, float amount, ITarget source)
    {
        if (c.Dead || amount <= 0f) return;
        c.Health -= amount;
        c.HurtFlash = 1f;
        // What the body shows of it: a hit from where the source is, heavier the more of its health it took.
        c.HitSerial++;
        c.HitHeavy = Math.Clamp(amount / MathF.Max(1f, c.Stats.MaxHealth) * 2.5f, 0.15f, 1f);
        c.HitPoint = c.Rig != null && c.Rig.HitCount > 0 ? new Vector3(c.Rig.Hits[0].X, c.Rig.Hits[0].Y, c.Rig.Hits[0].Z) : c.Position + Vector3.Up * c.Species.BodyHeight * 0.6f;
        Vector3 from = source != null ? Flat(c.Position - source.Position) : Vector3.Zero;
        c.HitDir = from.LengthSquared() > 1e-4f ? from.Normalized() : -c.Forward;
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
            if (Arming.Fights(c.Species)) Rally(c, source);
            // A kept beast set on: its people turn on whoever did it.
            if (c.Keeper is { } keeper && !keeper.Kin(source)) keeper.Wronged(this, c, source);
        }
        if (c.Species.Rig == RigKind.Swarm)
            c.SwarmAlive = Math.Max(0, (int)MathF.Ceiling(c.Species.Body.Members * MathF.Max(0f, c.Health) / c.Stats.MaxHealth));
        if (c.Health <= 0f) Kill(c, source);
    }

    /// <summary>Rallies (tests): one of a group set on, and how many of its own it brought in.</summary>
    public int Rallies, Rallied;

    /// <summary>
    /// One of a group of people or machines is set on: every one of its side within earshot turns on
    /// whoever did it at once (a raider shot in sight of the rest, a machine with them). They take
    /// it in a beat apart, the nearest first.
    /// </summary>
    void Rally(Creature c, ITarget source)
    {
        bool friendly = Arming.Friendly(c.Species);
        Rallies++;
        foreach (Creature o in Creatures)
        {
            if (o == c || o.Dead || o.Removed || !Arming.Fights(o.Species) || Arming.Friendly(o.Species) != friendly) continue;
            if (source is CreatureTarget { Creature.Herd: { } theirs } && o.Herd == theirs) continue; // (nobody turns on their own)
            float d = (o.Position - c.Position).Length();
            if (d > Arming.RallyRadius) continue;
            if (o.ProvokedBy == source && Time - o.ProvokedAt < 4f) continue;
            o.ProvokedBy = source;
            o.ProvokedAt = Time + MathF.Min(0.8f, d * 0.02f);
            o.ThinkEvery = 0f;
            Rallied++;
        }
    }

    /// <summary>Something it can't face (a flame) sends it running from a point for so many seconds, whatever its temper; it thinks again at once.</summary>
    public void Scare(Creature c, Vector3 from, float seconds)
    {
        if (c.Dead || seconds <= 0f) return;
        c.ScaredUntil = MathF.Max(c.ScaredUntil, Time + seconds);
        c.ScaredFrom = from;
        c.Think = 0f;
        c.ThinkEvery = 0f;
        c.Herd?.Alarm(from, Time);
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
        if (killer is CreatureTarget { Creature: var predator })
        {
            RaisePreyed(predator, c);
            // Fed: a pack shares its kill and is done hunting a while; a lone hunter eats it (Brain's Carrion), and hunts again once hungry.
            if (predator.Pack != null) foreach (Creature m in predator.Pack.Members) m.Hunger = 0f;
            else predator.Hunger = MathF.Min(predator.Hunger, PreyHunger - 0.05f);
        }
        // A person a Husk killed is not done with: it kneels at the body and puts its worms in.
        if (killer is CreatureTarget { Creature: { Species.Husk: true } husk }) Brain.Infest(this, husk, c);
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
    internal void RaiseQuake(QuakeEvent e) => Quaked?.Invoke(e);
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

    /// <summary>
    /// Whom a creature would go for, and how far (flat): the nearest of the players and towns' people it may hunt
    /// (<see cref="NearestTarget"/>), and for one that goes for people (<see cref="CrossReactions.Hunts"/>) the
    /// nearest person of a band round it too: wild people, raiders, a place's own.
    /// </summary>
    public ITarget Quarry(Creature c, out float distance)
    {
        ITarget best = NearestTarget(c.Position, out distance);
        NearestPerson(c, c.Stats.Perception, true, ref best, ref distance);
        if (Brain.Preys(c.Species)) PreyOn(c, ref best, ref distance);
        return best;
    }

    /// <summary>How hungry a hunter gets before it goes after another animal (0..1: it eats what it finds dead sooner), and how often it looks round for one, s.</summary>
    public const float PreyHunger = 0.5f, PreyLook = 3f;

    /// <summary>
    /// A hungry hunter's quarry among the animals (owner: predators hunt prey by size and diet, a pack bringing down a
    /// grazer): every few seconds, each on its own beat, the nearest it would take (<see cref="Brain.Takes"/>) within its
    /// senses is picked again; nearer than anyone else, it is what the hunter goes for.
    /// </summary>
    void PreyOn(Creature c, ref ITarget best, ref float distance)
    {
        if (c.PreyLookAt <= 0f)
        {
            // Hungry or not from the start, by its own stream (nothing else it rolls changes).
            c.Hunger = Rng.For(c.Species.Seed, "hunger", c.Id).Float();
            c.PreyLookAt = Time + 0.1f + c.Id % 9 * 0.35f;
        }
        if (c.Hunger < PreyHunger) { c.Prey = null; return; }
        if (Time >= c.PreyLookAt)
        {
            c.PreyLookAt = Time + PreyLook;
            c.Prey = null;
            float near = c.Stats.Perception;
            foreach (Creature o in Creatures)
            {
                if (!Brain.Takes(c, o)) continue;
                float d = Flat(o.Position - c.Position).Length();
                if (d < near) { near = d; c.Prey = o; }
            }
        }
        if (c.Prey is not { Dead: false, Removed: false } prey) return;
        float at = Flat(prey.Position - c.Position).Length();
        if (at < distance) { distance = at; best = prey.AsTarget; }
    }

    /// <summary>A band's person nearer than <paramref name="distance"/> (and within reach) for a creature that goes for people (none of its own band), flat or straight.</summary>
    public void NearestPerson(Creature c, float reach, bool flat, ref ITarget best, ref float distance)
    {
        if (c.Herd?.Party != null || !CrossReactions.Hunts(c.Species)) return;
        foreach (Creature o in Sightings.Near(c.Position, MathF.Min(distance, reach)))
        {
            if (o.Herd?.Party == null || o.Space != c.Space || !CrossReactions.People(o.Species)) continue;
            float d = flat ? Flat(o.Position - c.Position).Length() : (o.Position - c.Position).Length();
            if (d < distance) { distance = d; best = o.AsTarget; }
        }
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
