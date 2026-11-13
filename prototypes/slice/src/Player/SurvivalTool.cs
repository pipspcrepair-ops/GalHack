using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Fx;
using PlanetSurface.Terrain;
using Slice.Game;
using Slice.Items;
using Slice.Places;
using Slice.Surface;
using TerrainDigging.Terrain;
using Wildlife;
using Layers = GalHack.Physics.Layers;

namespace Slice.Player;

/// <summary>
/// The tool's modes (Q steps through them): gather, shoot, deconstruct, which takes a block down
/// and gives its item back, and plate, the plating gun (the build tool does those two:
/// <c>BuildTool.TickDeconstruct</c>, <c>BuildTool.TickPlate</c>).
/// </summary>
public enum ToolMode { Gather, Shoot, Deconstruct, Plate }

/// <summary>
/// The survival tool, an item on the toolbar: in Gather mode it digs a small ball of ground under
/// the crosshair (terrain-digging's rules: bedrock and anchored ground refuse) and cuts plants;
/// in Shoot mode it is a short blaster whose bolt you can see fly from the muzzle and land, with
/// sparks and a scorch where it strikes. Shots hurt creatures and the blocks they hit. Every ray
/// asks the world the survivor is in (<see cref="WorldRay"/>), so inside a vessel the tool meets
/// the vessel's own walls: it digs nothing through a floor, and a shot damages the wall it hits.
/// Dug ore and cut plants go into the pack as shared/Items' raw items.
/// </summary>
public sealed class SurvivalTool
{
    readonly SurfaceZone _zone;
    readonly Survivor _survivor;
    readonly Suit _suit;
    readonly PlayerInventory _pack;
    readonly SliceRules.ToolRules _rules;
    readonly SliceRules.MaterialRules _yields;

    public ToolMode Mode = ToolMode.Gather;
    /// <summary>The planet's standing places: the tool salvages their blocks and dressing and breaks their hidden walls.</summary>
    public PoiPlaces Places;
    public Debris Debris;
    public Sfx Sfx;
    public ShotFx Fx;

    /// <summary>What firing feels like (<see cref="GunFeel"/> id): the flash at the muzzle and the kick.</summary>
    public string Feel = GunFeel.Tool.Id;

    /// <summary>The held tool's barrel tip in the world, when the hands show it (set by <see cref="Hands"/>); null otherwise.</summary>
    public Func<Vector3?> HeldMuzzle;

    /// <summary>What the tool did, for the visor ("+12 Iron ore", "BEDROCK").</summary>
    public event Action<string, Color> Said;

    /// <summary>Ore cells dug (for experience), plants cut, a creature hit.</summary>
    public event Action<int> OreDug;
    public event Action PlantCut;
    public event Action<Creature> CreatureHit;
    /// <summary>A shot landed on a den's worm mass.</summary>
    public event Action<WormMass> MassHit;

    /// <summary>The same hit, with where it landed and which way it was going.</summary>
    public event Action<Creature, Vector3, Vector3> CreatureStruck;

    /// <summary>How far a shot carries, m: what lives round here turns to look.</summary>
    const float ShotHeard = 45f;

    /// <summary>Items gathered since the start, by id (tests and stats).</summary>
    public readonly SortedDictionary<string, int> Gathered = new(StringComparer.Ordinal);

    /// <summary>What was set down because the pack was full.</summary>
    public event Action<Bill> Overflowed;

    public int PlantsCut { get; private set; }
    public int OreCells { get; private set; }

    /// <summary>Ore broken off tell rocks so far (a few a pulse), apart from what the cells gave.</summary>
    public int TellOre { get; private set; }
    public int Shots { get; private set; }

    public bool HasTarget { get; private set; }
    public Int3 Target { get; private set; }
    public Verdict TargetVerdict { get; private set; }
    public string TargetLabel { get; private set; } = "";

    /// <summary>The crosshair is on a structure's block nearer than any ground (the tool can't dig it).</summary>
    public bool OnBlock { get; private set; }

    /// <summary>The crosshair is on a place's block, a piece of its dressing or a registered wreck, nearer than the ground: the tool works that.</summary>
    public bool OnPlace => _placeHit != null || _wreck != null;

    /// <summary>The crosshair is on an ore tell rock (an outcrop or scree over a deposit) that still has ore to give.</summary>
    public bool OnTell => _tell != null;
    /// <summary>The deposit whose ore rock the tool is on, if any.</summary>
    public Deposit? Tell => _tell;
    /// <summary>The pulses a deposit's ore rock still has in it.</summary>
    public int TellLeft(Deposit d) => TellPulses - _tellsWorked.GetValueOrDefault((d.X, d.Y));
    Deposit? _tell;
    readonly Dictionary<(double, double), int> _tellsWorked = new();
    /// <summary>Pulses a tell rock takes to break down; each gives a little of its ore.</summary>
    public const int TellPulses = 4, TellOrePerPulse = 1;
    public PoiPlaces.Hit? PlaceHit => _placeHit;
    PoiPlaces.Hit? _placeHit;
    SalvageSite _wreck;
    Vector3 _placePoint;

    /// <summary>A block or piece came away (experience by the piece).</summary>
    public event Action Salvaged;

    float _cooldown;

    public SurvivalTool(SurfaceZone zone, Survivor survivor, Suit suit, PlayerInventory pack)
    {
        _zone = zone;
        _survivor = survivor;
        _suit = suit;
        _pack = pack;
        _rules = zone.Data.Rules.Tool;
        _yields = zone.Data.Rules.Materials;
    }

    /// <summary>One tick with the trigger held or not.</summary>
    public void Tick(float dt, bool trigger)
    {
        _cooldown -= dt;
        Aim();
        if (trigger && !_suit.Dead && Mode == ToolMode.Gather && !OnBlock && (OnPlace || OnTell || HasTarget))
            ShowBeam(OnPlace || OnTell ? _placePoint : new Vector3(Target.X + 0.5f, Target.Y + 0.5f, Target.Z + 0.5f));
        if (!trigger || _cooldown > 0f || _suit.Dead) return;
        if (Mode == ToolMode.Shoot) Shoot();
        else if (StrikeCreature()) { }
        else if (!OnBlock && OnPlace) WorkPlace();
        else if (!OnBlock && _tell != null) WorkTell();
        else if (!OnBlock && !CutPlant()) Dig();
        else if (OnBlock)
        {
            _cooldown = 0.4f;
            Said?.Invoke("SURVIVAL TOOL · NO BLOCKS", Tones.Caution);
        }
    }

    // ---- Aim -----------------------------------------------------------------------------------

    void Aim()
    {
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        PlanetTerrain terrain = _zone.Planet.Terrain;
        HasTarget = terrain.Store.Raycast(eye.ToNumerics(), dir.ToNumerics(), _rules.Reach, out Int3 cell, out _, out float ground);
        // A structure's block (the one you are in, from inside) nearer than the ground blocks the dig.
        // (Trunks share the props layer with devices: only a structure's block or a device counts.)
        OnBlock = WorldRay.Cast(_zone, _survivor, _rules.Reach, Layers.Hull | Layers.Props, out LookHit block)
                  && (block.OnBlock || _zone.Devices.Exists(d => d.Collider == block.Collider))
                  && (!HasTarget || block.Distance < ground);
        TargetLabel = "";
        _placeHit = null;
        _wreck = null;
        _tell = null;
        if (!OnBlock && Places != null && WorldRay.Cast(_zone, _survivor, _rules.Reach, Layers.World | Layers.Props | Place.SceneryLayer, out LookHit p)
            && (!HasTarget || p.Distance < ground + 0.05f))
        {
            _placePoint = p.Point;
            if (Places.Resolve(p.Collider, p.Point, p.Normal, out PoiPlaces.Hit h, p.Shape)) _placeHit = h;
            else _wreck = Places.SalvageOf(p.Collider);
            if (!OnPlace && p.Collider is Node solid && _zone.Infra.IsAncestorOf(solid)) _tell = TellAt(p.Point); // the rock itself: a trunk or the ground over a deposit is no tell
            if (OnPlace)
            {
                TargetLabel = _placeHit is PoiPlaces.Hit ph ? Places.Label(ph) : _wreck.Name.ToUpperInvariant();
                return;
            }
        }
        if (OnBlock)
        {
            return; // a structure's block: nothing by the crosshair (where you are is on the CORE tab)
        }
        if (!HasTarget) return;
        Target = cell;
        TargetVerdict = terrain.Check(cell, EditKind.Tool);
        CellMaterial m = terrain.Store.MaterialAt(cell);
        // Only ore is named by the crosshair: something you can take (plain ground says nothing).
        TargetLabel = m == CellMaterial.Ore && TargetVerdict != Verdict.Bedrock ? (OreOf(_zone.Planet.Source.OreKindAt(cell))?.Name ?? "Ore").ToUpperInvariant() : "";
    }

    // ---- Gather --------------------------------------------------------------------------------

    /// <summary>The raw item a deposit kind gives (shared/Items' deposit field).</summary>
    public ItemDef OreOf(DepositKind kind) => SliceItems.OreFor(_zone.Data.Items, kind.ToString());

    /// <summary>The hand tool in hand (the item id), which sets how fast and how wide it gathers; Game sets it each tick.</summary>
    public string ItemId = "survival-tool";
    SliceRules.ToolTier Tier => _rules.TierOf(ItemId);

    /// <summary>Damage one pulse does at the centre: the gather rate of the tool, over one interval.</summary>
    float Power => _rules.GatherRate * Tier.Rate * _rules.DigInterval;

    /// <summary>Work done on each cell the beam has touched but not yet taken (damage units), with when it last was.</summary>
    readonly Dictionary<Int3, (float Hurt, double At)> _cellHurt = new();
    /// <summary>Resources earned in fractions, paid out as whole items as they add up.</summary>
    readonly Dictionary<string, double> _pool = new(StringComparer.Ordinal);
    double _clock;

    /// <summary>Hurt cells still being remembered (tests and stats).</summary>
    public int ChippedCells => _cellHurt.Count;

    float DurabilityOf(CellMaterial m) => m switch
    {
        CellMaterial.Soil => _rules.Durability.Soil,
        CellMaterial.Rock => _rules.Durability.Rock,
        CellMaterial.DeepRock => _rules.Durability.DeepRock,
        CellMaterial.Ore => _rules.Durability.Ore,
        _ => _rules.Durability.Rock,
    };

    /// <summary>Earns a share of something: whole items go to the pack as the fractions add up.</summary>
    void Earn(string id, double amount, SortedDictionary<string, int> got)
    {
        double have = _pool.GetValueOrDefault(id) + amount;
        int whole = (int)Math.Floor(have + 1e-9);
        _pool[id] = have - whole;
        if (whole <= 0) return;
        Give(id, whole);
        got[id] = got.GetValueOrDefault(id) + whole;
    }

    void Announce(SortedDictionary<string, int> got, bool quietStone)
    {
        foreach (var (id, n) in got)
            if (!quietStone || id != "stone") Said?.Invoke($"+{n} {_zone.Data.Items.Item(id).Name}", Tones.Good);
    }

    /// <summary>
    /// One pulse of the beam at the target: it wears the cell under the crosshair, and less the
    /// cells about it, and resources trickle in as it does. A cell that has taken all it can is gone
    /// (the terrain store holds a cell whole, so the ground sinks a cell at a time, never in partial
    /// density). Bedrock and anchored ground refuse.
    /// </summary>
    public PlanetEdit Dig()
    {
        if (!HasTarget) return null;
        _cooldown = _rules.DigInterval;
        PlanetTerrain terrain = _zone.Planet.Terrain;
        Vector3 at = new(Target.X + 0.5f, Target.Y + 0.5f, Target.Z + 0.5f);
        Verdict verdict = terrain.Check(Target, EditKind.Tool);
        if (verdict != Verdict.Removable)
        {
            _cooldown = 0.35f;
            var refused = new PlanetEdit(EditKind.Tool);
            if (verdict == Verdict.Bedrock) { refused.Bedrock = 1; Said?.Invoke("BEDROCK", Tones.Warning); }
            else if (verdict == Verdict.Anchored) refused.Anchored = 1;
            else refused.AlreadyAir = 1;
            Sfx?.Play(Sfx.Sound.Clank, at);
            return refused;
        }
        _suit.Worked();
        _clock += _rules.DigInterval;
        float power = Power, radius = Tier.Radius;
        var finished = new List<Int3>();
        var got = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (Int3 cell in ChunkStore.SphereCells(Target, radius))
        {
            CellMaterial m = terrain.Store.MaterialAt(cell);
            if (m is CellMaterial.Air or CellMaterial.Bedrock || terrain.Check(cell, EditKind.Tool) != Verdict.Removable) continue;
            float d = new Vector3(cell.X - Target.X, cell.Y - Target.Y, cell.Z - Target.Z).Length();
            float weight = d < 0.01f ? 1f : 0.8f * (1f - d / (1.2f * radius));
            if (weight <= 0f) continue;
            float durability = DurabilityOf(m), hurt = _cellHurt.TryGetValue(cell, out var h) ? h.Hurt : 0f;
            float dealt = MathF.Min(power * weight, durability - hurt);
            if (dealt <= 0f) dealt = 0f;
            double share = dealt / durability;
            if (m == CellMaterial.Ore && OreOf(_zone.Planet.Source.OreKindAt(cell)) is ItemDef item) Earn(item.Id, _yields.OrePerCell * share, got);
            else if (m is CellMaterial.Rock or CellMaterial.DeepRock && _yields.StonePerRockCell > 0) Earn("stone", _yields.StonePerRockCell * share, got);
            hurt += power * weight;
            if (hurt >= durability) { finished.Add(cell); _cellHurt.Remove(cell); }
            else _cellHurt[cell] = (hurt, _clock);
        }
        PruneHurt();
        PlanetEdit edit = finished.Count > 0 ? _zone.Edits.DigCells(finished) : new PlanetEdit(EditKind.Tool);
        int ore = 0;
        foreach (RemovedCell r in edit.Removed) if (r.Material == CellMaterial.Ore) ore++;
        if (edit.Removed.Count > 0)
        {
            OreCells += ore;
            if (ore > 0) OreDug?.Invoke(ore);
            Debris?.Burst(edit.Removed, _survivor.WorldEye());
            Sfx?.Play(Sfx.Sound.Dig, at);
            if (ore > 0) Sfx?.Play(Sfx.Sound.Ore, at, 1f, -4f);
        }
        else
        {
            // Still chipping: a puff of grit and a low scrape.
            Debris?.Puff(at, _zone.Planet.Grit(at), 0.35f);
            Sfx?.Play(Sfx.Sound.Dig, at, 0.8f, -9f);
        }
        Announce(got, quietStone: got.Count > 1);
        return edit;
    }

    /// <summary>Forgets cells nobody has worked for a while, so the memory stays small.</summary>
    void PruneHurt()
    {
        if (_cellHurt.Count < 400) return;
        foreach (Int3 c in _cellHurt.Where(kv => _clock - kv.Value.At > 30.0).Select(kv => kv.Key).ToList()) _cellHurt.Remove(c);
    }

    /// <summary>A living creature in arm's reach in front of what the tool is on takes the blow instead (its body is solid to the tool too).</summary>
    bool StrikeCreature()
    {
        if (Seizer?.Invoke() is { Dead: false })
        {
            _cooldown = 0.4f;
            _suit.Worked();
            return IntoSeizer(_rules.ShotDamage * Tier.Shot * 0.5f);
        }
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        Creature c = _zone.Wildlife.Raycast(eye, dir, _rules.CutReach, out float distance, out Vector3 point);
        if (c == null || c.Dead) return false;
        if (OnPlace && _placePoint.DistanceTo(eye) < distance) return false;
        _cooldown = 0.4f;
        _suit.Worked();
        _zone.Wildlife.Damage(c, _rules.ShotDamage * Tier.Shot * 0.5f * _zone.Wildlife.HitScale(c, point));
        CreatureHit?.Invoke(c);
        CreatureStruck?.Invoke(c, point, dir);
        Sfx?.Play(Sfx.Sound.Dig, point, 1.2f, -6f);
        return true;
    }

    /// <summary>The deposit whose tell rock stands at a point (its outcrop or scree, within the ore body's radius), if it still has rock to break.</summary>
    Deposit? TellAt(Vector3 point)
    {
        var world = _zone.Planet;
        double px = Mathx.WrapX(point.X, world.Width), pz = Mathx.Wrap(point.Z, world.Height);
        foreach (Deposit d in world.Map.Deposits.InRect(px - 8, pz - 8, px + 8, pz + 8))
        {
            if (PlanetSurface.Infra.InfraLayer.TellOf(d) == GalHack.Deco.DecoTellKind.Fracture || !PlanetSurface.Infra.InfraLayer.HasTell(world, d)) continue;
            double dx = Mathx.Wrap(px - d.X, world.Width), dz = Mathx.Wrap(pz - d.Y, world.Height);
            float r = PlanetTerrainSource.OreRadius(d) + 0.6f;
            if (dx * dx + dz * dz > r * r) continue;
            return _tellsWorked.GetValueOrDefault((d.X, d.Y)) < TellPulses && !world.Felled.Contains(PlanetSurface.Infra.InfraLayer.TellId(d)) ? d : (Deposit?)null;
        }
        return null;
    }

    /// <summary>One pulse on an ore tell rock: it gives a little of its ore, and after a few pulses it is broken down and the tool digs on through to the ground.</summary>
    void WorkTell()
    {
        Deposit d = _tell.Value;
        _cooldown = _rules.DigInterval;
        _suit.Worked();
        // A faster tool takes the rock down in fewer pulses, a share of the ore in each.
        int worked = _tellsWorked.GetValueOrDefault((d.X, d.Y)), pulses = Math.Min(TellPulses - worked, (int)MathF.Ceiling(Tier.Rate));
        _tellsWorked[(d.X, d.Y)] = worked + pulses;
        Vector3 at = _placePoint;
        Sfx?.Play(Sfx.Sound.Clank, at, 1.1f, -8f);
        Debris?.Puff(at, _zone.Planet.Grit(at), 0.5f);
        if (OreOf(d.Kind) is ItemDef item)
        {
            Give(item.Id, TellOrePerPulse * pulses);
            TellOre += TellOrePerPulse * pulses;
            Said?.Invoke($"+{TellOrePerPulse * pulses} {item.Name}", Tones.Good);
        }
        // Worked through: the rock and its collider go, and the tool digs on into the ground under it.
        if (_tellsWorked[(d.X, d.Y)] >= TellPulses) _zone.Infra.BreakTell(d);
    }

    /// <summary>One pulse on a place's block or piece, or a wreck: it comes away once worked through, its materials into the pack.</summary>
    void WorkPlace()
    {
        _cooldown = _rules.DigInterval;
        _suit.Worked();
        float damage = Places.Rules.Salvage.ToolDamage * Tier.Rate;
        var (got, message) = _wreck != null ? Places.Work(_wreck, damage) : Places.Work(_placeHit.Value, damage);
        Sfx?.Play(Sfx.Sound.Clank, _placePoint, 1.25f, -12f);
        Debris?.Puff(_placePoint, new Color(0.55f, 0.52f, 0.48f, 0.6f), 0.5f);
        if (got != null && !got.IsEmpty)
        {
            foreach (var (id, n) in got) Give(id, n);
            Said?.Invoke(string.Join(", ", got.Select(kv => $"+{kv.Value} {_zone.Data.Items.Item(kv.Key).Name}")), Tones.Good);
            Salvaged?.Invoke();
        }
        if (!string.IsNullOrEmpty(message)) Said?.Invoke(message, got != null || message.Contains("room") ? Tones.Good : Tones.Caution);
    }

    void Give(string id, int n)
    {
        Gathered[id] = Gathered.GetValueOrDefault(id) + n;
        Bill left = _pack.Add(id, n);
        if (!left.IsEmpty) Overflowed?.Invoke(left);
    }

    public TreeFx Trees { get; private set; }

    readonly Dictionary<long, float> _plantHurt = new();

    /// <summary>Trees that have fallen to the tool (tests).</summary>
    public int TreesFelled { get; private set; }

    static bool IsTree(Flora kind) => kind is Flora.Conifer or Flora.Broadleaf or Flora.JungleTree or Flora.Palm or Flora.GiantMushroom;
    static bool IsBoulder(Flora kind) => kind is Flora.Rock or Flora.LavaRock or Flora.Crystal or Flora.IceShard;

    /// <summary>The species a plant is, in the world's herbarium, or null for a rock.</summary>
    GalHack.Plants.Plant SpeciesOf(int index)
    {
        var plants = _zone.Planet.Flora.Book.Plants;
        return index >= 0 && index < plants.Count ? plants[index] : null;
    }

    /// <summary>What flies off a plant: its own colour.</summary>
    static Color LeafOf(Flora kind, GalHack.Plants.Plant species) =>
        species != null ? new Color(species.Leaf.R, species.Leaf.G, species.Leaf.B) : TreeFx.LeafColour(kind);

    /// <summary>
    /// Chips the plant nearest the crosshair within reach: each pulse wears it down and gives a share
    /// of its biomass and organics (a boulder's stone), and when it is worked through it is gone from
    /// the ground for good: planet-surface's felling rule (<see cref="PlanetSurface.World.PlanetWorld.Felled"/>
    /// and <see cref="TerrainStreamer.Fell"/>), so a tree's collider goes in the same frame and the
    /// cut is kept through streaming and saves. A tree falls over, leaving a stump.
    /// </summary>
    public bool CutPlant()
    {
        if (!FloraFinder.Nearest(_zone.Streamer, _survivor.WorldEye(), _survivor.WorldLook(), _rules.CutReach, out TerrainStreamer.PlantSighting sighting))
            return false;
        Vector3 plant = sighting.Root;
        float scale = sighting.Scale;
        long id = sighting.Id;
        Flora kind = sighting.Kind;
        GalHack.Plants.Plant species = SpeciesOf(sighting.Plant);
        if (_zone.Planet.Felled.Contains(id)) return false;
        bool tree = IsTree(kind), boulder = IsBoulder(kind);
        float size = Math.Clamp(scale, 0.5f, 3f);
        float durability = (boulder ? _rules.Durability.Boulder : tree ? _rules.Durability.Tree : _rules.Durability.Plant) * size;
        float done = _plantHurt.GetValueOrDefault(id);
        float dealt = MathF.Min(Power, durability - done);
        done += Power;
        _cooldown = _rules.DigInterval;
        _suit.Worked();
        double share = dealt / durability, scaleUp = tree ? _rules.Durability.TreeYield : 1f;
        var got = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (boulder) Earn("stone", _rules.Durability.BoulderStone * size * share, got);
        else
        {
            _suit.Kit.Organics += _yields.OrganicsPerPlant * size * scaleUp * share;
            Earn("biomass", Math.Max(1.0, Math.Round(_yields.BiomassPerPlant * size)) * scaleUp * share, got);
        }
        Trees ??= AddTrees();
        Vector3 crown = plant + Vector3.Up * (tree ? Math.Min(6f, species == null ? 2.2f * size + 1f : species.Height * scale * 0.5f) : 0.6f * size);
        Color leaf = LeafOf(kind, species);
        if (boulder) Debris?.Puff(plant + Vector3.Up * 0.4f, _zone.Planet.Grit(plant), 0.7f);
        else Trees.Leaves(crown, leaf, tree ? 14 : 8, tree ? 1.2f : 0.4f);
        Sfx?.Play(Sfx.Sound.Dig, plant, tree ? 0.8f : 1.4f, -9f);
        if (done < durability)
        {
            _plantHurt[id] = done;
            Announce(got, quietStone: false);
            return true;
        }
        _plantHurt.Remove(id);
        _zone.Planet.Felled.Add(id);
        _zone.Streamer.Fell(id);
        PlantsCut++;
        PlantCut?.Invoke();
        if (tree)
        {
            TreesFelled++;
            float height = 3.2f * size, radius = 0.28f * size;
            foreach (var (solid, _) in _zone.Streamer.SolidsNear(plant, 2.5f))
                if (solid.Id == id && solid.IsTrunk) { height = MathF.Max(2f, solid.Extents.Y * 2f); radius = solid.Extents.X; }
            Color bark = species == null ? new Color(0.36f, 0.25f, 0.16f) : new Color(species.Bark.R, species.Bark.G, species.Bark.B);
            Trees.Fell(plant, plant - _survivor.WorldEye(), height, radius, leaf, crown: true, bark, _zone.Streamer.MeshOf(sighting), sighting.Turn, custom: sighting.OwnTint);
            Trees.Leaves(plant + Vector3.Up * height, leaf, 30, 1.8f);
            Sfx?.Play(Sfx.Sound.Dig, plant, 0.6f, -2f);
        }
        Announce(got, quietStone: false);
        return true;
    }

    TreeFx AddTrees()
    {
        var fx = new TreeFx();
        _zone.AddChild(fx);
        return fx;
    }

    /// <summary>The blue working beam from the barrel tip to where the tool is digging, cutting or taking down (call each frame it works).</summary>
    public void ShowBeam(Vector3 to) => Fx?.Beam(Muzzle, to);

    // ---- Shoot ---------------------------------------------------------------------------------

    /// <summary>Where the bolt leaves the tool: low and to the right of the eye, out along the look (the first-person tool's muzzle).</summary>
    public Vector3 Muzzle()
    {
        if (HeldMuzzle?.Invoke() is Vector3 tip) return tip;
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        Vector3 right = dir.Cross(_survivor.WorldUp());
        right = right.LengthSquared() < 1e-6f ? Vector3.Right : right.Normalized();
        Vector3 viewUp = right.Cross(dir).Normalized();
        return eye + dir * 0.55f + right * 0.17f - viewUp * 0.15f;
    }

    /// <summary>How far past a voxel cube a shot looks for the drawn ground: ample for a shot along a slope, short of reaching through an unloaded hill to what stands behind it.</summary>
    const float GrazePast = 24f;

    /// <summary>The Husk holding the survivor's head, if one is (set by the game): every shot and blow goes into it.</summary>
    public Func<Creature> Seizer;
    /// <summary>A shot or a blow went into the Husk holding you: that breaks its hold.</summary>
    public event Action<Creature> SeizerHit;

    /// <summary>
    /// A shot or a blow with a Husk's face against yours can't miss: it goes straight into it, at
    /// the head, no ray needed. Damage enough and it lets go (the game's hold sees to that).
    /// </summary>
    bool IntoSeizer(float damage)
    {
        if (Seizer?.Invoke() is not { Dead: false } husk) return false;
        Vector3 eye = _survivor.WorldEye();
        Vector3 face = _zone.Wildlife.WorldOf(husk) + Vector3.Up * husk.Species.Figure.Height * 0.88f;
        Vector3 dir = (face - eye).LengthSquared() > 1e-4f ? (face - eye).Normalized() : _survivor.WorldLook();
        _zone.Wildlife.Damage(husk, damage * 1.5f);
        SeizerHit?.Invoke(husk);
        CreatureHit?.Invoke(husk);
        CreatureStruck?.Invoke(husk, face, dir);
        if (Fx != null) Fx.Fire(Muzzle(), face, default, () => Fx.Impact(face, -dir, ImpactKind.Creature, null), Feel, HeldMuzzle);
        Sfx?.Play(Sfx.Sound.Ping, face, 0.8f, -8f);
        return true;
    }

    void Shoot()
    {
        _cooldown = _rules.ShotInterval;
        Shots++;
        if (IntoSeizer(_rules.ShotDamage * Tier.Shot)) return;
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        float reach = _rules.ShotRange;
        ImpactKind kind = ImpactKind.Nothing;
        Vector3 normal = -dir;
        Node3D onto = null;

        // The nearest of: the voxel ground, any solid thing in the world you are in (the vessel's
        // own walls from inside, other hulls, trunks, devices), and a creature.
        if (_zone.Planet.Terrain.Store.Raycast(eye.ToNumerics(), dir.ToNumerics(), reach, out _, out Int3 entered, out float ground))
        {
            reach = ground;
            kind = ImpactKind.Ground;
            var entry = new Vector3(entered.X, entered.Y, entered.Z); // the face the ray entered through
            normal = entry.LengthSquared() > 0.5f ? entry.Normalized() : -dir;
        }
        uint mask = Layers.World | Layers.Hull | Layers.Props | Layers.Flora;
        // The voxel cast counts whole cubes, and a cube's corner stands proud of the smooth ground that is
        // drawn: a block behind it (a bunker wall at an angle to the grid) is what the bolt meets, and down
        // along a slope the drawn ground lies many metres further on, however steep the shot. So the world's
        // own ray runs on past the cube and has the say; only with no collision there does the cube stand.
        float past = kind == ImpactKind.Ground ? GrazePast : 0f;
        if (WorldRay.Cast(_zone, _survivor, reach + past, mask, out LookHit hit))
        {
            reach = hit.Distance;
            normal = hit.Normal;
            kind = hit.OnBlock ? ImpactKind.Block : ImpactKind.Ground;
            onto = hit.Vessel?.Node;
        }
        PoiPlaces.Hit? onPlace = null;
        if (Places != null && kind != ImpactKind.Nothing && WorldRay.Cast(_zone, _survivor, reach + 0.01f, Layers.World, out LookHit wall)
            && Places.Resolve(wall.Collider, wall.Point, wall.Normal, out PoiPlaces.Hit ph, wall.Shape)) onPlace = ph;
        _zone.Wildlife.Noise(eye, ShotHeard);
        Creature c = _zone.Wildlife.Raycast(eye, dir, reach, out float distance, out Vector3 point);
        bool struck = c != null && !c.Dead;
        // A den's worm mass in the way takes the shot (enough of them and it's dead).
        if (_zone.Wildlife.RaycastMass(eye, dir, struck ? distance : reach, out float massAt, out WildSim massSim) is { } mass)
        {
            WormMasses.Damage(massSim, mass, _rules.ShotDamage * Tier.Shot);
            struck = false;
            c = null;
            kind = ImpactKind.Creature;
            reach = massAt;
            onto = null;
            onPlace = null;
            MassHit?.Invoke(mass);
        }
        // Someone of a town in the way: hit, and the town takes it badly. Shooting inside a town at all does.
        float personAt = 0f;
        Town.Person person = Places == null ? null : Places.PersonAlong(eye, dir, struck ? distance : reach, out personAt);
        if (person != null)
        {
            struck = false;
            c = null;
            kind = ImpactKind.Creature;
            reach = personAt;
            person.Town.Shoot(person, _rules.ShotDamage * Tier.Shot);
            onPlace = null;
        }
        else (Places?.TownAt(eye) ?? Places?.TownAt(eye + dir * MathF.Min(reach, 30f)))?.Provoked();
        if (onPlace is PoiPlaces.Hit target && !struck) Places.Shot(target, _rules.ShotDamage * Tier.Shot); // a cracked panel gives way: the room behind it says so
        Vector3 end = struck ? point : eye + dir * reach;
        if (struck)
        {
            kind = ImpactKind.Creature;
            onto = null;
            _zone.Wildlife.Damage(c, _rules.ShotDamage * Tier.Shot * _zone.Wildlife.HitScale(c, point));
            CreatureHit?.Invoke(c);
            CreatureStruck?.Invoke(c, point, dir);
        }
        else if (kind == ImpactKind.Block && hit.OnBlock)
            hit.Vessel.Hits.Shot(hit.Cell, hit.Point.ToNumerics(), hit.Normal.ToNumerics(), _rules.ShotDamage * Tier.Shot, GalHack.Structures.DamageType.Energy);
        Vector3 muzzle = Muzzle();
        Vector3 at = end, face = normal;
        Node3D on = onto;
        ImpactKind landed = kind;
        if (Fx != null) Fx.Fire(muzzle, end, default, () => Fx.Impact(at, face, landed, on), Feel, HeldMuzzle);
        Sfx?.Play(Sfx.Sound.Ping, muzzle, struck ? 0.8f : 1.2f, -8f);
        if (kind != ImpactKind.Nothing) Sfx?.Play(Sfx.Sound.Clank, end, 1.6f, -14f);
    }
}

/// <summary>Finds the plant nearest a look ray, from the planet streamer's own flora scatter.</summary>
public static class FloraFinder
{
    public static bool Nearest(TerrainStreamer streamer, Vector3 eye, Vector3 dir, float reach, out TerrainStreamer.PlantSighting found)
    {
        found = default;
        float best = float.MaxValue;
        foreach (TerrainStreamer.PlantSighting seen in streamer.FloraNear(eye, reach + 2f))
        {
            Vector3 root = seen.Root;
            float s = seen.Scale;
            // A plant is a column from its root up to about twice its scale, as wide as it really is:
            // a plant you aren't pointing at never takes the pulse from the ground you are.
            Vector3 mid = root + Vector3.Up * Mathf.Min(1.2f, s);
            float along = (mid - eye).Dot(dir);
            if (along < 0f || along > reach + 0.5f) continue;
            var book = streamer.World.Flora.Book.Plants;
            float trunk = seen.Plant >= 0 && seen.Plant < book.Count ? book[seen.Plant].TrunkRadius * s : 0f;
            if ((eye + dir * along - mid).Length() > Mathf.Max(0.35f + 0.3f * s, trunk + 0.3f)) continue;
            if (along < best)
            {
                best = along;
                found = seen;
            }
        }
        return best < float.MaxValue;
    }
}
