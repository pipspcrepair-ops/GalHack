using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.World;
using Wildlife;
using Rng = Wildlife.Rng;
using Terrain = Wildlife.Terrain;

namespace Slice.Surface;

/// <summary>
/// The planet's own fauna, rolled by the wildlife generator from the planet's seed, living on a
/// square of real ground around the survivor. The wildlife sim works in its own frame, with its
/// origin at the middle of the patch: positions here are converted on the way in and out.
/// Creatures read the planet's natural heights, baked on the patch's 2 m grid.
///
/// The patch follows the survivor on foot: once they are well away from its middle, a new patch
/// is baked on a worker around them (on a 100 m grid, so a place always gets the same animals),
/// and swapped in with every creature that stands inside it carried across. Meeting one puts
/// nothing on screen in the slice (wildlife's discovery card is not wired in).
/// </summary>
public partial class WildlifePatch : Node3D
{
    const float Snap = 100f;

    public SurfaceZone Zone;

    public WildSim Sim { get; private set; }
    /// <summary>Everything that moves on this world, from the bestiary.</summary>
    public WorldLife Life { get; private set; }
    public PlanetFauna Fauna => Life?.Fauna;
    public WildlifeView View { get; private set; }

    /// <summary>Wildlife's own synthesised calls: every wind-up is heard before it lands.</summary>
    public ProceduralAudio Audio { get; private set; }

    /// <summary>What's in the air from their throats.</summary>
    public SpitView Spit { get; private set; }

    /// <summary>The tells of the patch's ambushers and hunters: grit, shadows, scratching, rattles.</summary>
    public TellView Tells { get; private set; }

    /// <summary>Creatures living inside places and caves, each in a sim of its own anchored where it is (never swapped).</summary>
    public readonly List<DeepSite> Sites = new();

    /// <summary>A new patch took over: settlements re-register their area and their animals (old sim, its origin, the new one).</summary>
    public event Action<WildSim, Vector3, WildSim> Swapped;

    /// <summary>The patch's middle, in the zone's frame.</summary>
    public Vector3 Origin { get; private set; }

    /// <summary>Whether a zone point is inside something built on the surface (the places standing; set by the game).</summary>
    public Func<Vector3, bool> SolidAt;

    /// <summary>Your standing with whoever holds the ground at a zone point (set by the game; nobody: 0).</summary>
    public Func<Vector3, float> StandingAt;

    /// <summary>The player as a target for creatures (set by the game).</summary>
    public ITarget Player { get; private set; }

    /// <summary>Patches baked so far (the first counts).</summary>
    public int Patches { get; private set; }

    /// <summary>A creature lunged at the player: hit or not, with its damage (wildlife's own scale, a share of 100).</summary>
    public event Action<AttackEvent> Attacked;

    /// <summary>What was thrown from a throat came down (hit someone or not).</summary>
    public event Action<Gob, bool> Splashed;

    /// <summary>A creature went for someone other than the player (a townsperson out of town).</summary>
    public event Action<AttackEvent> Bitten;

    /// <summary>A creature started winding up an attack on the player: the warning before any harm.</summary>
    public event Action<AttackEvent> Telegraphed;

    /// <summary>A creature died, and what killed it (the player's target, another creature, or nothing).</summary>
    public event Action<Creature, ITarget> Died;

    bool _frozen;
    public bool Frozen
    {
        get => _frozen;
        set
        {
            _frozen = value;
            if (Sim != null) Sim.Frozen = value;
        }
    }

    /// <summary>The world's fish, or null where its water is tainted.</summary>
    public Schools Fish { get; private set; }

    /// <summary>How livable this world is, 0..1.</summary>
    public float Livable { get; private set; } = 1f;

    PlanetWorld _planet;
    Task<(Terrain terrain, NavGrid nav, Vector3 origin)> _baking;
    Vector3 _bakingFrom;

    public void SetWorld(PlanetWorld planet, Vector3 landing)
    {
        _planet = planet;
        var rules = Zone.Data.Rules.Wildlife;
        Livable = rules.LivableOn(planet.Spec);
        var world = WorldConditions.Of(planet.Spec, rules.Alienness);
        // What the suit reads on this world is what bodies here live with.
        var climate = Zone.Data.Rules.Climate;
        if (!world.Vacuum && climate.OxygenByAir.TryGetValue(planet.Sky.Air.ToString(), out double oxygen)) world.Oxygen = (float)oxygen;
        if (climate.RadiationByArchetype.TryGetValue(planet.Spec.Archetype.ToString(), out double radiation)) world.Radiation = (float)radiation;
        // Its depth in the galaxy: the Husks spread outward from the core. And a world whose roll has the Bloom's country is a Hollow world.
        world.Coreward = Math.Max(0f, planet.Depth);
        // What its layers say of what lives here: a Hollow world's fungus has its wildlife; its history
        // sets loose stock, feral packs and twisted predators, thins it out or twists it.
        PoiGenerator.Gen.WorldReading layers = planet.Reading;
        world.Bloom = layers.HollowWorld;
        world.Stock = layers.Stock;
        world.Working = layers.Working;
        world.Ferals = layers.Ferals;
        world.Predators = layers.Predators;
        world.Density = layers.Density;
        world.Mutagen = Math.Max(world.Mutagen, layers.Mutagen);
        // Its story: how it fell, its theme, who lives on it now, its eras and paint: what walks it beside its wild.
        Story(world, planet.Roll, planet.Profile, layers);
        // How much lives here is the world's own: its type's livability and what its layers say of it (a dead,
        // barren or poisoned world quiet, a rich one alive); where on it, its land decides (Richness).
        Livable *= Mathf.Lerp(1f, layers.Density, rules.LayerWeight);
        MeasureRichness();
        Life = Bestiary.Roll(Zone.Data.Wildlife, world);
        // Fish in its waters, where the water is clean.
        if (Schools.Clean(planet.Spec.Archetype, world, planet.Roll))
        {
            Fish = new Schools { Name = "Fish", Planet = planet, Seed = Life.Seed, Eye = () => Zone.Camera?.GlobalPosition ?? (Player != null ? FromPatch(Player.Position) : Origin) };
            AddChild(Fish);
        }
        View = new WildlifeView { Name = "Creatures", MostNew = 2 };
        View.Shown = (c, d, shown, jump) =>
        {
            PlanetSurface.Fx.PopLog.Note("creature", shown ? "comes into view" : "goes from view", d, c.Species.BodyLength, jump);
            if (shown && d < 90f && PlanetSurface.Fx.PopLog.On) GD.Print($"(came into view near: {c.Species.Name} at {d:F0} m, {c.Age:F1} s old, {(c.Species.Flies ? "a flyer" : "on the ground")}, kept {Kept(c)})");
        };
        View.Fader = (n, shows) =>
        {
            if (shows >= 1f) PlanetSurface.Fx.Fade.Clear(n);
            else PlanetSurface.Fx.Fade.Set(n, 0f, shows);
        };
        PersonView.Fader = View.Fader; // people dissolve in and out at the far edge as creatures do
        // Far off, a body stands on the far land as drawn, not the sim's finer ground under it (a far chunk is a coarser mesh).
        View.Lift = c =>
        {
            if (Zone.Streamer?.DrawnGround(FromPatch(c.Position)) is not float drawn) return 0f;
            return drawn - Sim.Terrain.Height(c.Position.X, c.Position.Z);
        };
        PersonView.Arrives = d => PlanetSurface.Fx.PopLog.Note("person", "comes into view", d, 1.8f, 0f);
        AddChild(View);
        Audio = new ProceduralAudio { Name = "Calls" };
        AddChild(Audio);
        View.Audio = Audio;
        Spit = new SpitView { Name = "Spit" };
        AddChild(Spit);
        Tells = new TellView { Name = "Tells", Audio = Audio };
        AddChild(Tells);
        var (terrain, nav, origin) = Bake(planet, Snapped(landing), SpawnReach.PatchHalf(rules.PatchHalfSize));
        Swap(terrain, nav, origin, keepClear: landing, clear: rules.SafeRadius);
    }

    static Vector3 Snapped(Vector3 p) => new(MathF.Round(p.X / Snap) * Snap, 0f, MathF.Round(p.Z / Snap) * Snap);

    /// <summary>The ground and nav grid of a patch of the planet around a point (worker-safe: the generator's heights only).</summary>
    static (Terrain, NavGrid, Vector3) Bake(PlanetWorld planet, Vector3 origin, float halfSize)
    {
        float ox = origin.X, oz = origin.Z;
        // Land animals don't sink: over water their ground is the surface, less a body's draught, so at
        // the very most one driven in floats. Pure generator reads: safe on a worker.
        float Height(float x, float z)
        {
            double wx = Mathx.WrapX(ox + x, planet.Width), wz = Mathx.Wrap(oz + z, planet.Height);
            float ground = planet.GroundHeight(wx, wz);
            return planet.Source.WaterSurfaceAt(wx, wz, out float surface) ? MathF.Max(ground, surface - Draught) : ground;
        }
        var terrain = new Terrain(Height, halfSize);
        var nav = new NavGrid(terrain.HalfSize, 3f);
        // Creatures keep out of the sea and the rivers, coming no nearer than the edge to drink.
        for (float z = -terrain.HalfSize + 3f; z < terrain.HalfSize; z += 6f)
        for (float x = -terrain.HalfSize + 3f; x < terrain.HalfSize; x += 6f)
            if (terrain.Height(x, z) < 0.5f || planet.Source.WaterSurfaceAt(Mathx.WrapX(ox + x, planet.Width), Mathx.Wrap(oz + z, planet.Height), out _))
                nav.AddObstacle(new Vector3(x, 0f, z), 3.5f);
        return (terrain, nav, origin);
    }

    /// <summary>How far below the surface a land animal's feet hang when it floats, metres.</summary>
    public const float Draught = 0.35f;

    /// <summary>
    /// A new patch takes over: creatures standing inside it come across where they stand, and
    /// the rest of it is populated, none within <paramref name="clear"/> of a point.
    /// </summary>
    void Swap(Terrain terrain, NavGrid nav, Vector3 origin, Vector3 keepClear, float clear)
    {
        WildSim old = Sim;
        Vector3 oldOrigin = Origin;
        // Day and night: creatures go to rest sites in their off hours (wildlife's activity cycles).
        var sim = new WildSim(Zone.Data.Wildlife, terrain, nav) { Frozen = _frozen, ActivityCycles = true };
        sim.SightLine = (a, b) => SightClear(ToGlobal(a), ToGlobal(b));
        // A band out here is of whoever holds the ground (the nearest living place's people): your standing with them moves its stance.
        sim.StandingAt = p => StandingAt?.Invoke(FromPatch(p)) ?? 0f;
        sim.GreetStyle = Gestures.FriendlyStyle(Life?.World.Seed ?? 0);
        // Places standing on the surface are solid to everything walking out there.
        // And so are the houses and farmsteads along the roads: nothing walks through their walls.
        // So is the start bunker, chamber and stairwell: its walls, floor and roof are not the ground's.
        sim.SolidAt = p => SolidAt != null && SolidAt(FromPatch(p)) || InHome(FromPatch(p)) || Bunker != null && Bunker.Over(FromPatch(p), 0.3f);
        // Creatures see by the same light the eye does; scent goes with the weather.
        sim.LightAt = p => Zone.Light?.LightAt(ToGlobal(p)) ?? 1f;
        sim.Clicked += c => Audio.Click(c);
        sim.Blocks = new Vessels.HullBlocks(Zone, () => Origin);
        sim.BreakBases = Zone.Data.Rules.Wildlife.BreakBases;
        sim.Battering += (c, b) => Audio.Batter(c, b.Point);
        sim.Broke += (c, b) => Audio.Batter(c, b.Point, landed: true);
        sim.Spat += g => Audio.Play("retch", g.Position, Math.Clamp(0.9f / MathF.Cbrt(Heft.Mass(g.From.Species) / 70f), 0.55f, 1.3f), -3f);
        sim.Splashed += (g, hit) => Splashed?.Invoke(g, hit);
        sim.Telegraph += e =>
        {
            Audio.Telegraph(e.Creature);
            Slice.Game.SoundCues.Cue(WorldOf(e.Creature), 1f);
            if (e.Target == Player) Telegraphed?.Invoke(e);
        };
        sim.Warned += c => { Audio.Telegraph(c); Slice.Game.SoundCues.Cue(WorldOf(c), 0.8f); };
        sim.Attacked += e => { if (e.Target == Player) Attacked?.Invoke(e); else Bitten?.Invoke(e); };
        sim.Died += (c, killer) => Died?.Invoke(c, killer);
        sim.Seized += e => { if (e.Victim == Player) Seized?.Invoke(e, sim); };
        sim.LetGo += e => { if (e.Victim == Player) LetGo?.Invoke(e); };
        sim.Murmured += c => Audio.Murmur(c);
        int carried = 0;
        var carriedNow = new List<Creature>();
        var across = new Dictionary<Creature, Creature>();
        // Herds and packs come across as herds and packs: a clump still clumps, a patrol still walks its road.
        var herds = new Dictionary<Herd, Herd>();
        var packs = new Dictionary<Pack, Pack>();
        Vector3 shift = oldOrigin - origin;
        if (old != null)
        {
            sim.Time = old.Time;
            // What measurements read goes on adding up across patches.
            (sim.Profile, sim.ThinkMs, sim.MoveMs, sim.RigMs, sim.Paths) = (old.Profile, old.ThinkMs, old.MoveMs, old.RigMs, old.Paths);
            foreach (Creature c in old.Creatures)
            {
                // A town's own animals are its to bring across (Swapped), with their pens.
                if (c.Removed || c.Dead || c.Species.Domestic != DomesticKind.None) continue;
                Vector3 local = c.Position + oldOrigin - origin;
                if (!terrain.Inside(local, 12f)) continue;
                Herd herd = null;
                if (c.Herd != null && !herds.TryGetValue(c.Herd, out herd))
                {
                    herd = new Herd { Home = c.Herd.Home + shift, Goal = c.Herd.Goal + shift, Center = c.Herd.Center + shift, Spread = c.Herd.Spread, GoalTimer = c.Herd.GoalTimer, Party = c.Herd.Party };
                    herds[c.Herd] = herd;
                    sim.Herds.Add(herd);
                }
                Pack pack = null;
                if (c.Pack != null && !packs.TryGetValue(c.Pack, out pack))
                {
                    pack = new Pack { Home = c.Pack.Home + shift, Center = c.Pack.Center + shift };
                    packs[c.Pack] = pack;
                    sim.Packs.Add(pack);
                }
                Creature next = sim.Spawn(c.Species, local, herd, pack, asIs: true); // the same one, not another of its kind
                next.Home = c.Home + shift;
                next.Health = c.Health;
                next.Yaw = c.Yaw;
                if (c.Species.Flies)
                {
                    // On the wing it stays on the wing, at its height and its speed; what stood stays standing.
                    next.Position = local;
                    next.Velocity = c.Velocity;
                    next.Altitude = c.Altitude;
                    next.Landed = c.Landed;
                }
                carriedNow.Add(next);
                across[c] = next;
                if (Kept(c)) _kept.Add(next);
                carried++;
            }
            foreach (Creature c in carriedNow) sim.AssignRest(new List<Creature> { c });
            RemapPatrols(herds);
        }
        Sim = sim;
        if (old != null) _refilledFrom += oldOrigin - origin; // where the last group came back, in the new patch's frame
        Origin = origin;
        Position = origin;
        Audio.Origin = origin;
        if (Player != null) sim.Targets.Add(Player);
        View.Bind(sim, null, across);
        Tells.Bind(sim);
        Swapped?.Invoke(old, oldOrigin, sim);
        SyncBases();
        Populate(keepClear - origin, clear, carriedOver: old != null);
        Patches++;
    }

    /// <summary>Herds, packs, loners and giants spread over the patch, none close to a point (patch frame).</summary>
    void Populate(Vector3 clearOf, float safe, bool carriedOver)
    {
        var rng = Rng.For(Fauna.Seed ^ (ulong)(long)(Origin.X * 31 + Origin.Z), "population", 0);
        float edge = Sim.Terrain.HalfSize - 30f;
        // A patch that took creatures over from the last one only fills its far side.
        float inner = carriedOver ? Sim.Terrain.HalfSize * 0.55f : 0f;
        // What each species could hold, less on a harsher world; and a far side is only part of a patch.
        float density = Zone.Data.Rules.Wildlife.Density * Livable * (carriedOver ? 0.6f : 1f);
        int most = Most;
        foreach (Species s in Fauna.Species)
        {
            int groups = SpawnRules.Groups((s.Plan == BodyPlan.Giant ? 1 : 2) * density * s.Scarcity, rng);
            for (int g = 0; g < groups && Sim.Creatures.Count < most; g++)
            {
                int count = GroupSize(s, rng);
                float spread = Sim.GroupRadius(s, count);
                float min = (s.Behaviour is Behaviour.Predator or Behaviour.PackHunter || s.Plan == BodyPlan.Giant ? safe * 2.5f : safe) + spread;
                Vector3 at = default;
                bool found = false;
                for (int attempt = 0; attempt < 12 && !found; attempt++)
                {
                    float a = rng.Range(0f, MathF.Tau), r = rng.Range(inner, edge);
                    at = new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
                    // A patch taking over from another is seen arriving: only where nobody is near or looking. And on ground that holds it.
                    found = new Vector2(at.X - clearOf.X, at.Z - clearOf.Z).Length() >= min && Open(s, at, spread) && (!carriedOver || Clear(at, spread)) && (attempt == 11 || Keep(s, at, rng));
                }
                if (!found) continue;
                Sim.SpawnGroup(s, Sim.FreeSpot(at, rng), count, carriedOver ? p => Clear(p, 0f) : null);
            }
        }
    }

    /// <summary>The most wild creatures this patch holds: the data's most, by how livable the world is.</summary>
    public int Most => (int)MathF.Ceiling(Zone.Data.Rules.Wildlife.MostPerPatch * Livable * SpawnReach.HoldScale(Sim?.Terrain.HalfSize ?? Zone.Data.Rules.Wildlife.PatchHalfSize, Zone.Data.Rules.Wildlife.PatchHalfSize));

    /// <summary>How many go about together: the species' own group, smaller on a harsher world.</summary>
    int GroupSize(Species s, Rng rng) => Math.Max(1, (int)MathF.Round(rng.Int(s.GroupMin, s.GroupMax) * Mathf.Lerp(0.5f, 1f, Math.Min(1f, Livable))));

    /// <summary>A spot a group may start at (patch frame): out of a base's zone, and for anything that walks, on dry land and off any place's ground (never inside its walls).</summary>
    bool Open(Species s, Vector3 at, float spread) => !Sim.InBaseZone(at, spread) && (s.Flies || !Sim.Nav.Blocked(at) && !OnPlace(at, 3f) && !InHome(FromPatch(at) with { Y = float.MinValue }, 1.5f));

    /// <summary>Wild creatures alive in the patch (not a town's own animals), and those within <paramref name="reach"/> of a point (patch frame).</summary>
    public int WildAlive(Vector3? near = null, float reach = float.MaxValue)
    {
        int n = 0;
        foreach (Creature c in Sim.Creatures)
            if (Wild(c) && (near is not Vector3 p || WildSim.Flat(c.Position - p).Length() <= reach)) n++;
        return n;
    }

    /// <summary>One of the wild's own numbers: alive, not a town's, not one the world's haunts and homes keep.</summary>
    bool Wild(Creature c) => !c.Removed && !c.Dead && c.Species.Domestic == DomesticKind.None && !Kept(c);

    /// <summary>How far round you the land is kept lively, m (patch frame, flat).</summary>
    public static float AroundYou => SpawnReach.Distance; // the spawn distance setting (SpawnReach)

    /// <summary>Groups brought back by <see cref="Refill"/>, and creatures let go far behind to make room (tests).</summary>
    public int Refilled { get; private set; }
    public int Retired { get; private set; }

    float _refillClock;
    Vector3 _refilledFrom;
    long _refills;

    /// <summary>
    /// Keeps the land lively as you go, by the bestiary's spawner's rules: every few seconds, once
    /// you have moved on since the last time, if fewer wild creatures than the data's share of the
    /// patch's most live within <see cref="AroundYou"/> of you, groups of the world's kinds (by how
    /// common each is) come back away from you and out of your sight, never in a base's zone or,
    /// for a walker, in the water. To make room, what is far behind you and out of sight is let go.
    /// Someone standing still sees nothing appear round them.
    /// </summary>
    void Refill(float dt)
    {
        if (_frozen || Player == null || Fauna == null || Fauna.Species.Count == 0) return;
        SpawnRules spawning = Sim.Data.Spawning;
        if ((_refillClock += dt) < spawning.CheckSeconds) return;
        _refillClock = 0f;
        Vector3 you = Player.Position;
        if (new Vector2(you.X - _refilledFrom.X, you.Z - _refilledFrom.Z).Length() < spawning.StayPutRadius) return;
        // As many as the land round you holds: more in its rich country, fewer on bare rock and high ground.
        int want = (int)MathF.Ceiling(Most * Zone.Data.Rules.Wildlife.NearShare * Math.Clamp(Richness(you), 0.55f, 1.4f));
        float total = 0f;
        foreach (Species k in Fauna.Species) total += Weight(k);
        if (total <= 0f) return;
        // A few groups a check at most: the land fills in over the next stretch, not all at once.
        for (int g = 0; g < 3 && WildAlive(you, AroundYou) < want; g++)
        {
            if (WildAlive() >= Most && !RetireFarthest(you)) return;
            var rng = Rng.For(Fauna.Seed, "refill", ++_refills);
            float pick = rng.Range(0f, total);
            Species s = Fauna.Species[^1];
            foreach (Species k in Fauna.Species)
                if ((pick -= Weight(k)) <= 0f) { s = k; break; }
            if (!BringBack(s, GroupSize(s, rng), you, rng)) return;
            Refilled++;
            _refilledFrom = you;
        }
    }

    /// <summary>A group of a kind put down round you (patch frame): out of your way and sight, inside the patch, on open ground.</summary>
    bool BringBack(Species s, int count, Vector3 you, Rng rng)
    {
        SpawnRules spawning = Sim.Data.Spawning;
        float spread = Sim.GroupRadius(s, count), edge = Sim.Terrain.HalfSize - 30f;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float a = rng.Range(0f, MathF.Tau), r = rng.Range(spawning.MinPlayerDistance + spread, MathF.Max(spawning.MinPlayerDistance + spread + 20f, AroundYou - spread));
            var at = new Vector3(you.X + MathF.Cos(a) * r, 0f, you.Z + MathF.Sin(a) * r);
            if (MathF.Abs(at.X) > edge || MathF.Abs(at.Z) > edge || !Open(s, at, spread) || !Clear(at, spread)) continue;
            // Each to its own ground: herds on the open, small things in scrub, hunters in the woods and hills.
            if (attempt < 15 && !Keep(s, at, rng)) continue;
            Vector3 spot = Sim.FreeSpot(at, rng);
            if (!s.Flies && (Sim.Nav.Blocked(spot) || OnPlace(spot, 3f))) continue;
            List<Creature> group = Sim.SpawnGroup(s, spot, count, p => Clear(p, 0f) && (s.Flies || !OnPlace(p, 1f)));
            if (group.Count == 0) continue;
            Sim.AssignRest(group);
            return true;
        }
        return false;
    }

    /// <summary>Lets go the wild creature farthest from you, if it is well behind you and out of sight (beyond what the view draws). False if none is.</summary>
    bool RetireFarthest(Vector3 you)
    {
        Creature far = null;
        float best = MathF.Max(AroundYou, View.FarDistance) + 20f;
        foreach (Creature c in Sim.Creatures)
        {
            if (!Wild(c) || c.Species.Plan == BodyPlan.Giant) continue;
            float d = WildSim.Flat(c.Position - you).Length();
            if (d > best) { best = d; far = c; }
        }
        if (far == null) return false;
        Sim.Remove(far);
        Retired++;
        return true;
    }

    /// <summary>How likely a kind is to be the one that comes back: how common it is, giants half as often.</summary>
    static float Weight(Species s) => s.Domestic != DomesticKind.None ? 0f : s.Scarcity * (s.Plan == BodyPlan.Giant ? 0.5f : 1f);

    /// <summary>
    /// Tells the sim where the bases stand, so nothing spawns within the data's clearance of one,
    /// measured from each footprint's edge (design 310). A structure counts while its core is in
    /// base mode; a capital vessel blocks nothing. Run on every new patch and once a second.
    /// </summary>
    /// <summary>The start bunker: a base to the spawn clearance, and solid to everything that walks.</summary>
    public Slice.Start.Bunker Bunker;

    /// <summary>The bunker is dug: it becomes a base to the clearance, and whatever was put down within it (the patch is made first) goes.</summary>
    public void SetBunker(Slice.Start.Bunker bunker)
    {
        Bunker = bunker;
        SyncBases();
        if (Sim == null) return;
        foreach (Creature c in Sim.Creatures.ToArray())
            if (!c.Removed && Sim.InBaseZone(c.Position)) Sim.Remove(c);
    }

    public void SyncBases()
    {
        if (Sim == null || _planet == null) return;
        Sim.Structures.Clear();
        foreach (Vessels.Vessel v in Zone.Vessels)
        {
            GalHack.Structures.Structure s = v.Structure;
            if (!v.IsBase || s.Core == null || s.Count == 0) continue;
            var (min, max) = s.Bounds();
            float half = s.Grid.CellSize * 0.5f;
            System.Numerics.Vector3 lo = PhysicsSandbox.Bridges.StructureBridge.CellCenter(s, min), hi = PhysicsSandbox.Bridges.StructureBridge.CellCenter(s, max);
            System.Numerics.Vector3 middle = (lo + hi) * 0.5f;
            Vector3 centre = v.ToWorld(middle), along = v.ToWorld(middle + System.Numerics.Vector3.UnitX) - centre;
            Sim.Structures.Add(new Wildlife.Structure
            {
                Name = v.Name,
                Mode = CoreMode.Base,
                Center = PatchLocal(_planet.NearestCopy(centre, Origin)),
                HalfExtents = new Vector2(MathF.Abs(hi.X - lo.X) * 0.5f + half, MathF.Abs(hi.Z - lo.Z) * 0.5f + half),
                Yaw = MathF.Atan2(-along.Z, along.X),
            });
        }
        if (Bunker != null)
        {
            var (centre, along, hx, hz) = Bunker.Footprint();
            Sim.Structures.Add(new Wildlife.Structure
            {
                Name = "Bunker",
                Mode = CoreMode.Base,
                Center = PatchLocal(_planet.NearestCopy(centre, Origin)),
                HalfExtents = new Vector2(hx, hz),
                Yaw = MathF.Atan2(-along.Z, along.X),
            });
        }
    }

    public void SetPlayer(ITarget player)
    {
        Player = player;
        Sim.Targets.Clear();
        Sim.Targets.Add(player);
    }

    /// <summary>Keeps the patch around someone on foot: far from its middle, the next one is baked around them.</summary>
    public void Follow(Vector3 at, bool onFoot)
    {
        if (_baking != null)
        {
            if (!_baking.IsCompleted) return;
            var (terrain, nav, origin) = _baking.Result;
            _baking = null;
            Swap(terrain, nav, origin, keepClear: _bakingFrom, clear: Zone.Data.Rules.Wildlife.SafeRadius);
            return;
        }
        if (Sim == null) return;
        // A new spawn distance: the patch is baked again round you at its new size, whatever you are on.
        float half = SpawnReach.PatchHalf(Zone.Data.Rules.Wildlife.PatchHalfSize);
        bool resized = half != Sim.Terrain.HalfSize;
        if (!resized && (!onFoot || new Vector2(at.X - Origin.X, at.Z - Origin.Z).Length() < Sim.Terrain.HalfSize * 0.5f)) return;
        PlanetWorld planet = _planet;
        Vector3 next = Snapped(at);
        _bakingFrom = at;
        _baking = Task.Run(() => Bake(planet, next, half));
    }

    /// <summary>Waits for a patch being baked and swaps it in now (tests).</summary>
    public void FinishFollowing()
    {
        if (_baking == null) return;
        _baking.Wait();
        Follow(Vector3.Zero, false);
    }

    /// <summary>Lines of sight cast through the world's collision so far, by every sim (measurements).</summary>
    public int SightRays { get; private set; }

    /// <summary>A line through what stands in the world: ground, walls, closed doors, hulls. For creatures' eyes; only from a physics step.</summary>
    public bool SightClear(Vector3 from, Vector3 to)
    {
        SightRays++;
        var ray = PhysicsRayQueryParameters3D.Create(from, to, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull);
        return GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
    }

    /// <summary>A loud sound (zone frame): whatever is in earshot, in any sim, turns to it. Your shot says which way it went: people it passes read it as being shot at.</summary>
    public void Noise(Vector3 at, float radius, Vector3 shotDir = default)
    {
        Sim?.Noise(PatchLocal(at), radius, shotDir, shotDir == default ? null : Player);
        foreach (DeepSite s in Sites) s.Sim.Noise(s.Local(at), radius);
    }

    /// <summary>A gesture of yours (zone frame), read by every band of people out here near enough to see it.</summary>
    public void Heard(PlayerGesture g, Vector3 eye, Vector3 look)
    {
        if (Sim != null) Brain.Gestured(Sim, g, PatchLocal(eye), look);
    }

    /// <summary>Zone frame to patch frame and back.</summary>
    public Vector3 PatchLocal(Vector3 frame) => frame - Origin;

    /// <summary>
    /// Clear for something new to be put down (patch frame): of every player (<see cref="WildSim.ClearOfPlayers"/>),
    /// and of the eye itself, which a fast or high body can leave a way from its feet: no nearer than the
    /// least spawn distance, and nowhere in front of it within the view distance.
    /// </summary>
    bool Clear(Vector3 at, float margin)
    {
        if (!Sim.ClearOfPlayers(at, margin)) return false;
        Camera3D eye = Zone.Camera;
        if (eye == null) return true;
        SpawnRules rules = Sim.Data.Spawning;
        Vector3 to = WildSim.Flat(at - PatchLocal(eye.GlobalPosition));
        float d = to.Length();
        // (The faster you go, the further out: SpawnLead.)
        if (d < rules.MinPlayerDistance + margin + (d > 1e-3f ? Lead(to / d) : 0f)) return false;
        Vector3 look = WildSim.Flat(-eye.GlobalBasis.Z);
        return !(d < rules.ViewDistance + margin && look.LengthSquared() > 1e-4f && look.Normalized().Dot(to / d) > MathF.Cos(Mathf.DegToRad(rules.ViewConeDegrees)));
    }
    public Vector3 FromPatch(Vector3 local) => local + Origin;

    public void Shift(Vector3 by)
    {
        Origin += by;
        Position = Origin;
        Audio.Origin = Origin;
        _baking = null; // baked for the old frame: the next tick starts another

    }

    /// <summary>The first creature along a ray (zone frame), within reach: the patch's, or one living in a place or a cave.</summary>
    public Creature Raycast(Vector3 from, Vector3 dir, float reach, out float distance, out Vector3 point)
    {
        point = default;
        distance = 0f;
        if (Sim == null) return null;
        Creature c = Sim.Raycast(PatchLocal(from), dir, reach, out distance, out Vector3 local);
        if (c != null) { reach = distance; point = FromPatch(local); }
        foreach (DeepSite s in Sites)
        {
            Creature d = s.Sim.Raycast(s.Local(from), dir, reach, out float dd, out Vector3 dl);
            if (d == null) continue;
            c = d;
            reach = distance = dd;
            point = s.World(dl);
        }
        return c;
    }

    /// <summary>The sim a creature lives in (the patch's or a place's or cave's), and its frame's origin.</summary>
    public WildSim SimOf(Creature c, out Vector3 origin, out ITarget player)
    {
        foreach (DeepSite s in Sites)
            if (s.Sim.Creatures.Contains(c)) { origin = s.GlobalPosition; player = s.Player; return s.Sim; }
        origin = Origin;
        player = Player;
        return Sim;
    }

    /// <summary>Where a creature is in the zone, whichever sim it lives in.</summary>
    public Vector3 WorldOf(Creature c)
    {
        SimOf(c, out Vector3 origin, out _);
        return c.Position + origin;
    }

    /// <summary>What a hit is worth by where it landed (zone frame): on a head half as much again, on the body as it is.</summary>
    public float HitScale(Creature c, Vector3 point)
    {
        if (c.Rig == null) return 1f;
        SimOf(c, out Vector3 origin, out _);
        Vector3 p = point - origin;
        float head = c.Species.Body.HeadSize * 0.6f;
        for (int i = 0; i < c.Rig.HitCount; i++)
        {
            Vector4 s = c.Rig.Hits[i];
            if (MathF.Abs(s.W - head) < 1e-3f && new Vector3(s.X, s.Y, s.Z).DistanceTo(p) < s.W * 1.1f) return 1.5f;
        }
        return 1f;
    }

    /// <summary>The survivor hurts a creature, in whichever sim it lives (or someone else did: then it doesn't turn on the survivor).</summary>
    public void Damage(Creature c, float amount, bool byPlayer = true)
    {
        WildSim sim = SimOf(c, out _, out ITarget player);
        sim?.Damage(c, amount, byPlayer ? player : null);
    }

    /// <summary>The survivor's feet in the zone.</summary>
    public Vector3 PlayerWorld() => Player == null ? Origin : FromPatch(Player.Position);

    /// <summary>Plays one of wildlife's sounds for a sim whose frame starts at another origin.</summary>
    public void Sound(Vector3 origin, Action<ProceduralAudio> play)
    {
        Vector3 own = Audio.Origin;
        Audio.Origin = origin;
        play(Audio);
        Audio.Origin = own;
    }

    /// <summary>A Husk has the player by the head (in whichever sim), and let them go.</summary>
    public event Action<SeizeEvent, WildSim> Seized;
    public event Action<SeizeEvent> LetGo;
    internal void RaiseSeized(SeizeEvent e, WildSim sim) => Seized?.Invoke(e, sim);
    internal void RaiseLetGo(SeizeEvent e) => LetGo?.Invoke(e);

    /// <summary>A hold on the player ended: no Husk in any sim (the patch's, a place's, a cave's) may seize them for a while.</summary>
    public void SparePlayer(float seconds)
    {
        Seizing.Spare(Sim, Player, seconds);
        foreach (DeepSite s in Sites) Seizing.Spare(s.Sim, s.Player, seconds);
    }

    /// <summary>The first living worm mass along a ray (zone frame), within reach, in any place's sim.</summary>
    public WormMass RaycastMass(Vector3 from, Vector3 dir, float reach, out float distance, out WildSim sim)
    {
        WormMass best = null;
        distance = reach;
        sim = null;
        foreach (DeepSite s in Sites)
        {
            if (s.Sim.Masses.Count == 0) continue;
            WormMass m = WormMasses.Raycast(s.Sim, s.Local(from), dir, distance, out float d);
            if (m == null) continue;
            best = m;
            distance = d;
            sim = s.Sim;
        }
        return best;
    }

    internal void RaiseTelegraphed(AttackEvent e) => Telegraphed?.Invoke(e);
    internal void RaiseAttacked(AttackEvent e) => Attacked?.Invoke(e);
    internal void RaiseDied(Creature c, ITarget killer) => Died?.Invoke(c, killer);

    float _baseSync;

    public override void _PhysicsProcess(double delta)
    {
        if (Sim == null) return;
        Sim.Clock.Time01 = (float)Zone.Sky.TimeOfDay;
        Vector2 wind = Zone.Weather?.Wind2D ?? Vector2.Zero;
        Sim.Wind = new Vector3(wind.X, 0f, wind.Y);
        if ((_baseSync += (float)delta) >= 1f)
        {
            _baseSync = 0f;
            SyncBases();
        }
        TrackTravel((float)delta);
        Refill((float)delta);
        Haunting((float)delta);
        Own((float)delta);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        Sim.Step((float)delta);
        StepMs += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>Main-thread time the patch's sim has taken so far, ms (measurements).</summary>
    public double StepMs { get; private set; }

    int _reachVersion = -1;

    public override void _Process(double delta)
    {
        if (Sim == null || Zone.Camera == null) return;
        if (_reachVersion != SpawnReach.Version)
        {
            // How far bodies are drawn and nothing new is put in sight: the spawn distance's.
            _reachVersion = SpawnReach.Version;
            View.FarDistance = SpawnReach.Distance;
            View.MidDistance = MathF.Max(120f, SpawnReach.Distance * 0.45f);
            Sim.Data.Spawning.ViewDistance = SpawnReach.ViewDistance;
        }
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        View.Sync(PatchLocal(Zone.Camera.GlobalPosition));
        ViewMs += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Spit.Sync(Sim);
    }

    /// <summary>Main-thread time the creatures' bodies have taken to keep up with the sim so far, ms (measurements).</summary>
    public double ViewMs { get; private set; }
}
