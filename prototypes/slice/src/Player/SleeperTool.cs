using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Terrain;
using Godot;
using GalHack.Arms;
using HitKind = GalHack.Arms.HitKind;
using SInt3 = GalHack.Structures.Int3;
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

/// <summary>The Sleeper tool's two modes (Q switches): attack/gather (blue) and repair (green).</summary>
public enum ToolMode { Gather, Repair }

/// <summary>
/// The Sleeper tool, an item on the toolbar (design: the Sleeper tool). Attack/gather is one mode:
/// every pulse is a short blast of lightning from the barrel tip, with a crackle, a thunderclap, a
/// flash that lights the surroundings and a kick. What it meets decides what it does (the shot
/// pipeline's own hit test, <see cref="Slice.Arms.ArmsHost.Sweep"/>): a person gets a short arc that
/// chips a little damage and stuns them in their flinch; a creature takes the blast; a block on a
/// core a sleeper owns, or on no core, comes away whole into the bag; a block on anyone else's core
/// takes damage, arcs crawling over it, and gives up a share of what it was made of as it goes; ore,
/// ground, plants and places are gathered as before (terrain-digging's rules: bedrock and anchored
/// ground refuse), with the lightning arcing to what is worked. Repair is a luminous green cone,
/// harmless to anyone and anything: it mends a damaged block, or restores a destroyed one, when the
/// bag or the core's storage holds what it takes. Every ray asks the world the sleeper is in
/// (<see cref="WorldRay"/>), so inside a vessel the tool meets the vessel's own walls.
/// </summary>
public sealed class SleeperTool
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
    /// <summary>The shot pipeline: its hit test, its sound pool and the hit confirmation (set by the game).</summary>
    public Slice.Arms.ArmsHost Arms;
    /// <summary>The build tool: blocks that come away whole go through its take-down, destroyed ones come back through its restore.</summary>
    public Slice.Construction.BuildTool Builder;

    /// <summary>The kick of a blast: the tool's flash with the data's recoil and camera tilt.</summary>
    public GunFeel Kick => _kick ??= GunFeel.Tool with { Recoil = _rules.Lightning.Kick, CameraKick = _rules.Lightning.CameraKick };
    GunFeel _kick;

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

    /// <summary>How far a blast carries, m: what lives round here turns to look.</summary>
    const float ShotHeard = 45f;

    /// <summary>Items gathered since the start, by id (tests and stats).</summary>
    public readonly SortedDictionary<string, int> Gathered = new(StringComparer.Ordinal);

    /// <summary>What was set down because the pack was full.</summary>
    public event Action<Bill> Overflowed;

    public int PlantsCut { get; private set; }
    public int OreCells { get; private set; }

    /// <summary>Ore broken off tell rocks so far (a few a pulse), apart from what the cells gave.</summary>
    public int TellOre { get; private set; }
    /// <summary>Blasts of lightning (each one kicks).</summary>
    public int Shots { get; private set; }
    /// <summary>People zapped and stunned, blocks taken whole, foreign blocks blasted, blocks repaired and restored (tests).</summary>
    public int Zapped { get; private set; }
    public int Stunned { get; private set; }
    public int TakenWhole { get; private set; }
    public int Extracted { get; private set; }
    public int Repaired { get; private set; }
    public int Restored { get; private set; }
    /// <summary>What the last repair pulse did (tests and the visor's line).</summary>
    public string LastMend { get; private set; } = "";

    public bool HasTarget { get; private set; }
    public Int3 Target { get; private set; }
    public Verdict TargetVerdict { get; private set; }
    public string TargetLabel { get; private set; } = "";

    /// <summary>The crosshair is on a structure's block nearer than any ground (the tool can't dig it).</summary>
    public bool OnBlock { get; private set; }

    /// <summary>The crosshair is on a structure's block or deco piece the build tool's take-down has outlined (its verdict is the label).</summary>
    public bool OnStructure { get; private set; }

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
    double _thunderAt = -10;

    public SleeperTool(SurfaceZone zone, Survivor survivor, Suit suit, PlayerInventory pack)
    {
        _zone = zone;
        _survivor = survivor;
        _suit = suit;
        _pack = pack;
        _rules = zone.Data.Rules.Tool;
        _yields = zone.Data.Rules.Materials;
    }

    SliceRules.LightningRules Bolt => _rules.Lightning;

    /// <summary>One tick with the trigger held or not.</summary>
    public void Tick(float dt, bool trigger)
    {
        _cooldown -= dt;
        Aim();
        // On a block of ours: outlined, with what comes back. Someone else's isn't ours to take down.
        OnStructure = Mode == ToolMode.Gather && Builder != null && Builder.AimTakeDown();
        if (OnStructure && !Ours(Builder.TargetVessel)) { OnStructure = false; Builder.Hide(); }
        if (OnStructure) TargetLabel = Builder.Verdict;
        if (!trigger || _suit.Dead) return;
        if (Mode == ToolMode.Repair) Mend();
        else if (_cooldown <= 0f) Blast();
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
    public string ItemId = "sleeper-tool";
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

    /// <summary>What felled trees leave (the zone's).</summary>
    public TreeFx Trees => _zone.Trees;

    readonly Dictionary<long, float> _plantHurt = new();

    /// <summary>Trees that have fallen to the tool (tests).</summary>
    public int TreesFelled { get; private set; }

    static bool IsBoulder(Flora kind) => kind is Flora.Rock or Flora.LavaRock or Flora.Crystal or Flora.IceShard;

    /// <summary>
    /// Chips the plant nearest the crosshair within reach: each pulse wears it down and gives a share
    /// of its biomass and organics (a boulder's stone), and when it is worked through it is gone from
    /// the ground for good (<see cref="SurfaceZone.Fell(in TerrainStreamer.PlantSighting, Vector3)"/>), so a
    /// tree's collider goes in the same frame and the cut is kept through streaming and saves. A tree
    /// falls over, leaving a stump.
    /// </summary>
    public bool CutPlant(out Vector3 struck)
    {
        struck = default;
        if (!FloraFinder.Nearest(_zone.Streamer, _survivor.WorldEye(), _survivor.WorldLook(), _rules.CutReach, out TerrainStreamer.PlantSighting sighting))
            return false;
        Vector3 plant = sighting.Root;
        float scale = sighting.Scale;
        long id = sighting.Id;
        Flora kind = sighting.Kind;
        GalHack.Plants.Plant species = _zone.SpeciesOf(sighting.Plant);
        if (_zone.Planet.Felled.Contains(id)) return false;
        bool tree = TreeFx.IsTree(kind), boulder = IsBoulder(kind);
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
        Vector3 crown = plant + Vector3.Up * (tree ? Math.Min(6f, species == null ? 2.2f * size + 1f : species.Height * scale * 0.5f) : 0.6f * size);
        Color leaf = TreeFx.LeafOf(kind, species);
        struck = boulder ? plant + Vector3.Up * 0.4f : plant.Lerp(crown, 0.5f);
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
        _zone.Fell(sighting, plant - _survivor.WorldEye());
        PlantsCut++;
        PlantCut?.Invoke();
        if (tree)
        {
            TreesFelled++;
            Sfx?.Play(Sfx.Sound.Dig, plant, 0.6f, -2f);
        }
        Announce(got, quietStone: false);
        return true;
    }

    /// <summary>The plating gun's beam from the barrel tip to the block it plates (call each frame it works).</summary>
    public void ShowBeam(Vector3 to) => Fx?.Beam(Muzzle, to);

    // ---- Attack/gather: the blast ----------------------------------------------------------------

    /// <summary>Where the blast leaves the tool: the held tool's barrel tip, else low and to the right of the eye, out along the look.</summary>
    public Vector3 Muzzle()
    {
        if (HeldMuzzle?.Invoke() is Vector3 tip) return tip;
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        Vector3 right = dir.Cross(_survivor.WorldUp());
        right = right.LengthSquared() < 1e-6f ? Vector3.Right : right.Normalized();
        Vector3 viewUp = right.Cross(dir).Normalized();
        return eye + dir * 0.55f + right * 0.17f - viewUp * 0.15f;
    }

    /// <summary>The Husk holding the survivor's head, if one is (set by the game): every blast goes into it.</summary>
    public Func<Creature> Seizer;
    /// <summary>A blast went into the Husk holding you: that breaks its hold.</summary>
    public event Action<Creature> SeizerHit;

    /// <summary>
    /// One blast of lightning at whatever the shot pipeline's hit test meets first along the look, within
    /// the blast's range: each kind of thing has its rule (see the class). It always flashes, crackles,
    /// thunders and kicks.
    /// </summary>
    void Blast()
    {
        Shots++;
        _cooldown = _rules.ShotInterval;
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        float damage = _rules.ShotDamage * Tier.Shot;
        if (Seizer?.Invoke() is { Dead: false } husk)
        {
            // A Husk's face against yours can't be missed: straight into it, at the head. Enough and it lets go.
            Vector3 face = _zone.Wildlife.WorldOf(husk) + Vector3.Up * husk.Species.Figure.Height * 0.88f;
            _zone.Wildlife.Damage(husk, damage * 1.5f);
            SeizerHit?.Invoke(husk);
            CreatureHit?.Invoke(husk);
            CreatureStruck?.Invoke(husk, face, (face - eye).Normalized());
            Strike(face, Vector3.One * 0.2f, 3, attack: true);
            return;
        }
        ShotHit hit = default;
        bool met = Arms != null && Arms.Sweep(eye.ToNumerics(), (eye + dir * Bolt.Range).ToNumerics(), Slice.Arms.ArmsHost.PlayerThing, 0, 0f, out hit);
        Vector3 at = met ? hit.Point.ToGodot() : eye + dir * Bolt.Range;
        switch (met ? hit.Kind : HitKind.Ground)
        {
            case HitKind.Person:
                Zap((Town.Person)hit.Thing, at, dir);
                return;
            case HitKind.Creature when ((Creature)hit.Thing).Species is { Figure: not null, Husk: false }:
                Zap((Creature)hit.Thing, at, dir); // one of the wild's people: a raider, a wanderer
                return;
            case HitKind.Creature:
                var c = (Creature)hit.Thing;
                _zone.Wildlife.Damage(c, damage * _zone.Wildlife.HitScale(c, at));
                CreatureHit?.Invoke(c);
                CreatureStruck?.Invoke(c, at, dir);
                Fx?.Impact(at, -dir, ImpactKind.Creature);
                Strike(at, Vector3.One * 0.3f, 3, attack: true);
                return;
            case HitKind.Field:
                var f = (Slice.Vessels.FieldHit)hit.Thing;
                // Aimed at one of our field's own emitters, past the edge of its sheet: the emitter, so the field, comes down whole.
                if (OnStructure && Builder.TargetVessel == f.Vessel && Enumerable.Range(0, 4).Any(i => f.Field.Corner(i) == f.Vessel.Structure.AnchorOf(Builder.TargetCell)))
                {
                    Blocks(f.Vessel, Builder.TargetCell, at, hit.Normal.ToGodot());
                    return;
                }
                Slice.Vessels.ForceFields.Hit(f.Vessel, f.Field, damage);
                Strike(at, new Vector3(0.6f, 0.6f, 0.05f), 3, attack: true);
                return;
            case HitKind.Ground when hit.Thing is WormMass mass:
                Arms.HurtMass(mass, damage);
                MassHit?.Invoke(mass);
                Strike(at, Vector3.One * 0.4f, 3, attack: true);
                return;
        }
        // Our own block or deco piece, outlined (from inside, the cabin's walls are met as ground): it comes back whole.
        if (OnStructure)
        {
            Blocks(Builder.TargetVessel, Builder.TargetCell, at, -dir);
            return;
        }
        if (met && hit.Kind == HitKind.Block)
        {
            Blocks((Slice.Vessels.Vessel)hit.Thing, new SInt3(hit.CellX, hit.CellY, hit.CellZ), at, hit.Normal.ToGodot());
            return;
        }
        // Ground, a plant, a place or nothing: gathered as before (each pulse at the gathering rate), the bolt to what is worked.
        if (!OnBlock && OnPlace)
        {
            if (_placeHit is PoiPlaces.Hit ph) Places.Shot(ph, damage); // a hazard goes up, a faction's core is shot out
            WorkPlace();
            Strike(_placePoint, Vector3.One * 0.35f, 2);
        }
        else if (!OnBlock && _tell != null) { WorkTell(); Strike(_placePoint, Vector3.One * 0.4f, 3); }
        else if (!OnBlock && CutPlant(out Vector3 plant)) Strike(plant, Vector3.One * 0.3f, 2);
        else if (!OnBlock && HasTarget && Dig() is { } edit)
        {
            // The flash of lightning arcing over the ground being worked.
            Vector3 cell = new(Target.X + 0.5f, Target.Y + 0.5f, Target.Z + 0.5f);
            Strike(cell, Vector3.One * (0.45f + 0.25f * Tier.Radius), edit.Removed.Count > 0 ? 4 : 2);
        }
        else
        {
            // Into the air, or ground beyond the tool's reach: lightning and nothing else. In a town that's noticed.
            if (met) Fx?.Impact(at, hit.Normal.ToGodot(), ImpactKind.Ground);
            Strike(at, Vector3.One * 0.25f, met ? 2 : 0, attack: true);
            (Places?.TownAt(eye) ?? Places?.TownAt(at))?.Provoked();
        }
    }

    /// <summary>
    /// A person in front: the arc jumps to them, chips a little damage and stuns them in their flinch for a
    /// moment (a hit like any other to them: they cry out, remember it, and their people take it badly).
    /// </summary>
    void Zap(Town.Person p, Vector3 at, Vector3 dir)
    {
        Town t = p.Town;
        Zapped++;
        t.Hit(p, at, dir, 0.7f, BodyEnd.Fall);
        t.Struck(p, Bolt.PersonDamage, GalHack.Arms.DamageClass.Energy, byPlayer: true);
        if (t.Stun(p, Bolt.StunSeconds, Bolt.RestunSeconds)) Stunned++;
        Arms?.Confirm(p.Dead);
        Arms?.Sounds?.Play(CombatSound.Flesh, at, 1.1f, -4f);
        Fx?.Impact(at, -dir, ImpactKind.Creature);
        Strike(at, new Vector3(0.22f, 0.45f, 0.22f), 4, attack: true);
    }

    /// <summary>The same for one of the wild's people: chipped, stunned in their flinch, and they react as hit.</summary>
    void Zap(Creature c, Vector3 at, Vector3 dir)
    {
        Zapped++;
        _zone.Wildlife.Damage(c, Bolt.PersonDamage);
        c.HitHeavy = 0.7f;
        if (c.Stun(Bolt.StunSeconds, Bolt.RestunSeconds)) Stunned++;
        CreatureHit?.Invoke(c);
        CreatureStruck?.Invoke(c, at, dir);
        Fx?.Impact(at, -dir, ImpactKind.Creature);
        Strike(at, new Vector3(0.22f, 0.45f, 0.22f), 4, attack: true);
    }

    /// <summary>A sleeper's structure, or one with no core: its blocks are ours to take back whole.</summary>
    public static bool Ours(Slice.Vessels.Vessel v) => v.Structure.Core == null || v.Structure.Owner == GalHack.Structures.Building.DefaultOwner;

    /// <summary>
    /// A structure's block: ours (a sleeper's core, or none), it comes away whole into the bag at once;
    /// on anyone else's core it takes damage, arcs crawling over it, and gives up a share of its bill
    /// as its hit points go.
    /// </summary>
    void Blocks(Slice.Vessels.Vessel v, SInt3 cell, Vector3 at, Vector3 normal)
    {
        GalHack.Structures.Structure s = v.Structure;
        if (!s.TryGetBlock(cell, out SInt3 anchor, out GalHack.Structures.Block b)) return;
        float size = s.Grid.CellSize;
        Vector3 centre = v.ToWorld(PhysicsSandbox.Bridges.StructureBridge.BlockCenter(s, anchor, b));
        if (Ours(v))
        {
            Strike(centre, Vector3.One * size * 0.5f, 3);
            if (Builder != null)
            {
                int before = Builder.TakenDown, decos = v.Decos.Count;
                Builder.TakeDown(v, cell);
                if (Builder.TakenDown > before || v.Decos.Count < decos) TakenWhole++;
            }
            return;
        }
        float amount = Bolt.BlockDamage * Tier.Shot, max = s.StatsOf(b).MaxHp;
        float dealt = MathF.Min(b.Hp, GalHack.Structures.Damage.Effective(s.StatsOf(b), GalHack.Structures.DamageType.Energy, amount));
        v.Hits.Shot(cell, at.ToNumerics(), normal.ToNumerics(), amount, GalHack.Structures.DamageType.Energy);
        Extracted++;
        var got = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (max > 0f && dealt > 0f)
            foreach (var (id, n) in GalHack.Items.Bridge.BuildingBridge.BillOf(_zone.Data.Items, s, b))
                Earn(id, n * Bolt.ExtractShare * dealt / max, got);
        Announce(got, quietStone: false);
        Fx?.Impact(at, normal, ImpactKind.Block, v.Node);
        Strike(centre, Vector3.One * size * 0.5f, 5, attack: true);
    }

    /// <summary>
    /// The lightning for one pulse: a bolt from the barrel tip to the point, arcs crawling over a box
    /// of this half-size about it, the flash, the crackle and the thunder. An attack is heard far
    /// (wildlife turns to look); gathering is quieter.
    /// </summary>
    void Strike(Vector3 to, Vector3 half, int arcs, bool attack = false)
    {
        Color blue = ShotFx.LightningBlue;
        Fx?.Lightning(Muzzle, to, blue, Bolt.FlashEnergy, Bolt.FlashRange, Bolt.FlashSeconds);
        if (arcs > 0) Fx?.ArcOver(to, half, blue, arcs, Bolt.FlashSeconds * 1.6f);
        float pitch = 0.9f + 0.2f * (float)Random.Shared.NextDouble();
        Arms?.Sounds?.Play(CombatSound.Crackle, to, pitch, attack ? 0f : -6f);
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _thunderAt >= Bolt.ThunderGap)
        {
            _thunderAt = now;
            Arms?.Sounds?.Play(CombatSound.Thunder, Muzzle(), pitch, attack ? 0f : -8f, loud: attack);
        }
        if (!attack) return; // (gathering works the suit itself)
        _suit.Worked();
        _zone.Wildlife.Noise(_survivor.WorldEye(), ShotHeard);
    }

    // ---- Repair: the green cone ------------------------------------------------------------------

    /// <summary>
    /// Repair held: the green cone shows and, a pulse at a time, mends the damaged block the look is on
    /// (whole again, for the share of its bill its damage is), or restores a destroyed block in the
    /// cone, paid for from the bag, else the core's storage. It harms nothing.
    /// </summary>
    void Mend()
    {
        SliceRules.RepairRules r = _rules.Repair;
        Fx?.RepairCone(Muzzle, _survivor.WorldLook, r.Range, r.Angle);
        if (_cooldown > 0f) return;
        _cooldown = r.Interval;
        Arms?.Sounds?.Play(CombatSound.Mend, Muzzle(), 0.95f + 0.1f * (float)Random.Shared.NextDouble(), -10f);
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        if (WorldRay.Cast(_zone, _survivor, r.Range, Layers.World | Layers.Hull, out LookHit look) && look.OnBlock
            && GalHack.Structures.Building.IsDamaged(look.Vessel.Structure, look.Cell))
        {
            Slice.Vessels.Vessel v = look.Vessel;
            Bill bill = v.Hits.RepairBill(look.Cell);
            SharedStorage from = _pack.Storage.Has(bill) ? _pack.Storage : v.Storage;
            GalHack.Structures.RepairStatus done = v.Hits.Repair(look.Cell, 1f, from);
            LastMend = $"{done} {look.Cell} for {_zone.Data.Items.Describe(bill, 2)}";
            if (done == GalHack.Structures.RepairStatus.Repaired)
            {
                Repaired++;
                if (from == _pack.Storage) _pack.Grid.Reconcile();
            }
            else Said?.Invoke($"REPAIR · {_zone.Data.Items.Describe(bill, 2)} NEEDED", Tones.Caution);
            return;
        }
        LastMend = look.OnBlock ? $"whole {look.Cell}" : "no block";
        // A destroyed block in the cone, nearest the look first.
        float cos = MathF.Cos(Mathf.DegToRad(r.Angle));
        string why = null;
        foreach (var (v, cell, b) in FallenInCone(eye, dir, r.Range, cos))
        {
            if (v.Structure.Has(cell)) { v.Hits.Fallen.Remove(cell); continue; }
            why = Builder?.Restore(v, cell, b);
            if (why != null) continue;
            v.Hits.Fallen.Remove(cell);
            Restored++;
            return;
        }
        if (why != null) Said?.Invoke(why, Tones.Caution);
    }

    /// <summary>Destroyed blocks of structures about, inside the cone, nearest the look's line first.</summary>
    List<(Slice.Vessels.Vessel, SInt3, GalHack.Structures.Block)> FallenInCone(Vector3 eye, Vector3 dir, float range, float cos)
    {
        var found = new List<(float, Slice.Vessels.Vessel, SInt3, GalHack.Structures.Block)>();
        foreach (Slice.Vessels.Vessel v in _zone.Vessels)
            foreach (var (cell, b) in v.Hits.Fallen)
            {
                Vector3 to = v.ToWorld(PhysicsSandbox.Bridges.StructureBridge.BlockCenter(v.Structure, cell, b)) - eye;
                float d = to.Length();
                if (d > range + 0.5f || d < 1e-3f || to.Dot(dir) / d < cos) continue;
                found.Add((1f - to.Dot(dir) / d, v, cell, b));
            }
        found.Sort((x, y) => x.Item1.CompareTo(y.Item1));
        return found.Select(f => (f.Item2, f.Item3, f.Item4)).ToList();
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
