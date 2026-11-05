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
    public PlanetFauna Fauna { get; private set; }
    public WildlifeView View { get; private set; }

    /// <summary>Wildlife's own synthesised calls: every wind-up is heard before it lands.</summary>
    public ProceduralAudio Audio { get; private set; }

    /// <summary>The patch's middle, in the zone's frame.</summary>
    public Vector3 Origin { get; private set; }

    /// <summary>The player as a target for creatures (set by the game).</summary>
    public ITarget Player { get; private set; }

    /// <summary>Patches baked so far (the first counts).</summary>
    public int Patches { get; private set; }

    /// <summary>A creature lunged at the player: hit or not, with its damage (wildlife's own scale, a share of 100).</summary>
    public event Action<AttackEvent> Attacked;

    /// <summary>A creature started winding up an attack on the player: the warning before any harm.</summary>
    public event Action<AttackEvent> Telegraphed;

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

    PlanetWorld _planet;
    Task<(Terrain terrain, NavGrid nav, Vector3 origin)> _baking;
    Vector3 _bakingFrom;

    public void SetWorld(PlanetWorld planet, Vector3 landing)
    {
        _planet = planet;
        var rules = Zone.Data.Rules.Wildlife;
        Fauna = new FaunaGenerator(Zone.Data.Wildlife.Fauna).Generate(planet.Spec.Seed, rules.Alienness, bloom: false);
        View = new WildlifeView { Name = "Creatures" };
        AddChild(View);
        Audio = new ProceduralAudio { Name = "Calls" };
        AddChild(Audio);
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
        var sim = new WildSim(Zone.Data.Wildlife, terrain, nav) { Frozen = _frozen };
        sim.Telegraph += e =>
        {
            Audio.Telegraph(e.Creature);
            if (e.Target == Player) Telegraphed?.Invoke(e);
        };
        sim.Warned += c => Audio.Telegraph(c);
        sim.Attacked += e => { if (e.Target == Player) Attacked?.Invoke(e); };
        int carried = 0;
        if (old != null)
        {
            sim.Time = old.Time;
            foreach (Creature c in old.Creatures)
            {
                if (c.Removed || c.Dead) continue;
                Vector3 local = c.Position + oldOrigin - origin;
                if (!terrain.Inside(local, 12f)) continue;
                sim.Spawn(c.Species, local);
                carried++;
            }
        }
        Sim = sim;
        Origin = origin;
        Position = origin;
        Audio.Origin = origin;
        if (Player != null) sim.Targets.Add(Player);
        View.Bind(sim, null);
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
        foreach (Species s in Fauna.Species)
        {
            int groups = s.Plan == BodyPlan.Giant ? 1 : 2;
            for (int g = 0; g < groups; g++)
            {
                float min = s.Behaviour is Behaviour.Predator or Behaviour.PackHunter || s.Plan == BodyPlan.Giant ? safe * 2.5f : safe;
                Vector3 at = default;
                bool found = false;
                for (int attempt = 0; attempt < 12 && !found; attempt++)
                {
                    float a = rng.Range(0f, MathF.Tau), r = rng.Range(inner, edge);
                    at = new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
                    found = new Vector2(at.X - clearOf.X, at.Z - clearOf.Z).Length() >= min;
                }
                if (!found) continue;
                Sim.SpawnGroup(s, Sim.FreeSpot(at, rng), Math.Max(1, rng.Int(s.GroupMin, s.GroupMax)));
            }
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

    /// <summary>The first creature along a ray (zone frame), within reach.</summary>
    public Creature Raycast(Vector3 from, Vector3 dir, float reach, out float distance, out Vector3 point)
    {
        point = default;
        distance = 0f;
        if (Sim == null) return null;
        Creature c = Sim.Raycast(PatchLocal(from), dir, reach, out distance, out Vector3 local);
        point = FromPatch(local);
        return c;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Sim == null) return;
        Sim.Clock.Time01 = (float)Zone.Sky.TimeOfDay;
        Sim.Step((float)delta);
    }

    public override void _Process(double delta)
    {
        if (Sim == null || Zone.Camera == null) return;
        View.Sync(PatchLocal(Zone.Camera.GlobalPosition));
    }
}
