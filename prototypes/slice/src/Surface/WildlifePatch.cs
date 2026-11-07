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

    /// <summary>The tells of the patch's ambushers and hunters: grit, shadows, scratching, rattles.</summary>
    public TellView Tells { get; private set; }

    /// <summary>Creatures living inside places and caves, each in a sim of its own anchored where it is (never swapped).</summary>
    public readonly List<DeepSite> Sites = new();

    /// <summary>A new patch took over: settlements re-register their area and their animals (old sim, its origin, the new one).</summary>
    public event Action<WildSim, Vector3, WildSim> Swapped;

    /// <summary>The patch's middle, in the zone's frame.</summary>
    public Vector3 Origin { get; private set; }

    /// <summary>The player as a target for creatures (set by the game).</summary>
    public ITarget Player { get; private set; }

    /// <summary>Patches baked so far (the first counts).</summary>
    public int Patches { get; private set; }

    /// <summary>A creature lunged at the player: hit or not, with its damage (wildlife's own scale, a share of 100).</summary>
    public event Action<AttackEvent> Attacked;

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
        Life = Bestiary.Roll(Zone.Data.Wildlife, planet.Spec.Seed, rules.Alienness);
        View = new WildlifeView { Name = "Creatures" };
        AddChild(View);
        Audio = new ProceduralAudio { Name = "Calls" };
        AddChild(Audio);
        Tells = new TellView { Name = "Tells", Audio = Audio };
        AddChild(Tells);
        var (terrain, nav, origin) = Bake(planet, Snapped(landing), rules.PatchHalfSize);
        Swap(terrain, nav, origin, keepClear: landing, clear: rules.SafeRadius);
    }

    static Vector3 Snapped(Vector3 p) => new(MathF.Round(p.X / Snap) * Snap, 0f, MathF.Round(p.Z / Snap) * Snap);

    /// <summary>The ground and nav grid of a patch of the planet around a point (worker-safe: the generator's heights only).</summary>
    static (Terrain, NavGrid, Vector3) Bake(PlanetWorld planet, Vector3 origin, float halfSize)
    {
        float ox = origin.X, oz = origin.Z;
        var terrain = new Terrain((x, z) => planet.GroundHeight(Mathx.WrapX(ox + x, planet.Width), Mathx.Wrap(oz + z, planet.Height)), halfSize);
        var nav = new NavGrid(terrain.HalfSize, 3f);
        // Creatures keep out of the sea and the rivers.
        for (float z = -terrain.HalfSize + 3f; z < terrain.HalfSize; z += 6f)
        for (float x = -terrain.HalfSize + 3f; x < terrain.HalfSize; x += 6f)
            if (terrain.Height(x, z) < 0.5f) nav.AddObstacle(new Vector3(x, 0f, z), 3.5f);
        return (terrain, nav, origin);
    }

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
        sim.Telegraph += e =>
        {
            Audio.Telegraph(e.Creature);
            if (e.Target == Player) Telegraphed?.Invoke(e);
        };
        sim.Warned += c => Audio.Telegraph(c);
        sim.Attacked += e => { if (e.Target == Player) Attacked?.Invoke(e); else Bitten?.Invoke(e); };
        sim.Died += (c, killer) => Died?.Invoke(c, killer);
        int carried = 0;
        var carriedNow = new List<Creature>();
        if (old != null)
        {
            sim.Time = old.Time;
            foreach (Creature c in old.Creatures)
            {
                // A town's own animals are its to bring across (Swapped), with their pens.
                if (c.Removed || c.Dead || c.Species.Domestic != DomesticKind.None) continue;
                Vector3 local = c.Position + oldOrigin - origin;
                if (!terrain.Inside(local, 12f)) continue;
                Creature across = sim.Spawn(c.Species, local);
                across.Health = c.Health;
                across.Yaw = c.Yaw;
                if (c.Species.Flies)
                {
                    // On the wing it stays on the wing, at its height and its speed; what stood stays standing.
                    across.Position = local;
                    across.Velocity = c.Velocity;
                    across.Altitude = c.Altitude;
                    across.Landed = c.Landed;
                }
                carriedNow.Add(across);
                carried++;
            }
            foreach (Creature c in carriedNow) sim.AssignRest(new List<Creature> { c });
        }
        Sim = sim;
        Origin = origin;
        Position = origin;
        Audio.Origin = origin;
        if (Player != null) sim.Targets.Add(Player);
        View.Bind(sim, null);
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
        // Half what a species could hold, less on a harsher world; and a far side is only part of a patch.
        float density = Sim.Data.Spawning.Density * Livable * (carriedOver ? 0.6f : 1f);
        int most = (int)MathF.Ceiling(Zone.Data.Rules.Wildlife.MostPerPatch * Livable);
        foreach (Species s in Fauna.Species)
        {
            int groups = SpawnRules.Groups((s.Plan == BodyPlan.Giant ? 1 : 2) * density, rng);
            for (int g = 0; g < groups && Sim.Creatures.Count < most; g++)
            {
                int count = Math.Max(1, (int)MathF.Round(rng.Int(s.GroupMin, s.GroupMax) * Mathf.Lerp(0.5f, 1f, Livable)));
                float spread = Sim.GroupRadius(s, count);
                float min = (s.Behaviour is Behaviour.Predator or Behaviour.PackHunter || s.Plan == BodyPlan.Giant ? safe * 2.5f : safe) + spread;
                Vector3 at = default;
                bool found = false;
                for (int attempt = 0; attempt < 12 && !found; attempt++)
                {
                    float a = rng.Range(0f, MathF.Tau), r = rng.Range(inner, edge);
                    at = new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
                    // A patch taking over from another is seen arriving: only where nobody is near or looking.
                    found = new Vector2(at.X - clearOf.X, at.Z - clearOf.Z).Length() >= min && !Sim.InBaseZone(at, spread) && (!carriedOver || Sim.ClearOfPlayers(at, spread));
                }
                if (!found) continue;
                Sim.SpawnGroup(s, Sim.FreeSpot(at, rng), count, carriedOver ? p => Sim.ClearOfPlayers(p, 0f) : null);
            }
        }
    }

    /// <summary>
    /// Tells the sim where the bases stand, so nothing spawns within the data's clearance of one,
    /// measured from each footprint's edge (design 310). A structure counts while its core is in
    /// base mode; a capital vessel blocks nothing. Run on every new patch and once a second.
    /// </summary>
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
        if (!onFoot || Sim == null) return;
        if (new Vector2(at.X - Origin.X, at.Z - Origin.Z).Length() < Sim.Terrain.HalfSize * 0.5f) return;
        PlanetWorld planet = _planet;
        Vector3 next = Snapped(at);
        float half = Sim.Terrain.HalfSize;
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

    /// <summary>A line through what stands in the world: ground, walls, closed doors, hulls. For creatures' eyes; only from a physics step.</summary>
    public bool SightClear(Vector3 from, Vector3 to)
    {
        var ray = PhysicsRayQueryParameters3D.Create(from, to, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull);
        return GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
    }

    /// <summary>A loud sound (zone frame): whatever is in earshot, in any sim, turns to it.</summary>
    public void Noise(Vector3 at, float radius)
    {
        Sim?.Noise(PatchLocal(at), radius);
        foreach (DeepSite s in Sites) s.Sim.Noise(s.Local(at), radius);
    }

    /// <summary>Zone frame to patch frame and back.</summary>
    public Vector3 PatchLocal(Vector3 frame) => frame - Origin;
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

    /// <summary>The survivor hurts a creature, in whichever sim it lives.</summary>
    public void Damage(Creature c, float amount)
    {
        WildSim sim = SimOf(c, out _, out ITarget player);
        sim?.Damage(c, amount, player);
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

    internal void RaiseTelegraphed(AttackEvent e) => Telegraphed?.Invoke(e);
    internal void RaiseAttacked(AttackEvent e) => Attacked?.Invoke(e);
    internal void RaiseDied(Creature c, ITarget killer) => Died?.Invoke(c, killer);

    float _baseSync;

    public override void _PhysicsProcess(double delta)
    {
        if (Sim == null) return;
        Sim.Clock.Time01 = (float)Zone.Sky.TimeOfDay;
        if ((_baseSync += (float)delta) >= 1f)
        {
            _baseSync = 0f;
            SyncBases();
        }
        Sim.Step((float)delta);
    }

    public override void _Process(double delta)
    {
        if (Sim == null || Zone.Camera == null) return;
        View.Sync(PatchLocal(Zone.Camera.GlobalPosition));
    }
}
