using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
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

/// <summary>The survival tool's two modes, switched with a key the visor shows.</summary>
/// <summary>
/// The tool's modes (Q steps through them): gather, shoot, and deconstruct, which takes a block
/// down and gives its item back (the build tool does that part: <c>BuildTool.TickDeconstruct</c>).
/// </summary>
public enum ToolMode { Gather, Shoot, Deconstruct }

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

    /// <summary>Items gathered since the start, by id (tests and stats).</summary>
    public readonly SortedDictionary<string, int> Gathered = new(StringComparer.Ordinal);

    /// <summary>What was set down because the pack was full.</summary>
    public event Action<Bill> Overflowed;

    public int PlantsCut { get; private set; }
    public int OreCells { get; private set; }
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
    Deposit? _tell;
    readonly Dictionary<(double, double), int> _tellsWorked = new();
    /// <summary>Pulses a tell rock takes to break down; each gives a little of its ore.</summary>
    public const int TellPulses = 4, TellOrePerPulse = 3;
    /// <summary>Ore the rocks over deposits have given so far (tests).</summary>
    public int TellOre { get; private set; }
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
        if (trigger && !_suit.Dead && Mode == ToolMode.Gather && !OnBlock && (OnPlace || HasTarget))
            ShowBeam(OnPlace ? _placePoint : new Vector3(Target.X + 0.5f, Target.Y + 0.5f, Target.Z + 0.5f));
        if (!trigger || _cooldown > 0f || _suit.Dead) return;
        if (Mode == ToolMode.Shoot) Shoot();
        else if (StrikeCreature()) { }
        else if (!OnBlock && OnPlace) WorkPlace();
        else if (!OnBlock && _tell != null) WorkTell();
        else if (!OnBlock && !CutPlant()) Dig();
        else if (OnBlock)
        {
            _cooldown = 0.4f;
            Said?.Invoke("The survival tool doesn't dig blocks", Tones.Caution);
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
            if (Places.Resolve(p.Collider, p.Point, p.Normal, out PoiPlaces.Hit h)) _placeHit = h;
            else _wreck = Places.SalvageOf(p.Collider);
            if (!OnPlace) _tell = TellAt(p.Point);
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

    /// <summary>One dig pulse at the target. Ore and rock become raw items in the pack.</summary>
    public PlanetEdit Dig()
    {
        if (!HasTarget) return null;
        _cooldown = _rules.DigInterval;
        _suit.Worked();
        PlanetEdit edit = _zone.Edits.Dig(Target, _rules.DigRadius);
        Vector3 at = new(Target.X + 0.5f, Target.Y + 0.5f, Target.Z + 0.5f);
        if (edit.Removed.Count > 0)
        {
            var got = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int ore = 0;
            foreach (RemovedCell r in edit.Removed)
            {
                if (r.Material == CellMaterial.Ore && OreOf(_zone.Planet.Source.OreKindAt(r.Cell)) is ItemDef item)
                {
                    got[item.Id] = got.GetValueOrDefault(item.Id) + _yields.OrePerCell;
                    ore++;
                }
                else if (r.Material is CellMaterial.Rock or CellMaterial.DeepRock && _yields.StonePerRockCell > 0)
                    got["stone"] = got.GetValueOrDefault("stone") + _yields.StonePerRockCell;
            }
            foreach (var (id, n) in got) Give(id, n);
            OreCells += ore;
            if (ore > 0) OreDug?.Invoke(ore);
            Debris?.Burst(edit.Removed, _survivor.WorldEye());
            Sfx?.Play(Sfx.Sound.Dig, at);
            if (ore > 0) Sfx?.Play(Sfx.Sound.Ore, at, 1f, -4f);
            foreach (var (id, n) in got)
                if (id != "stone" || ore == 0) Said?.Invoke($"+{n} {_zone.Data.Items.Item(id).Name}", Tones.Good);
            return edit;
        }
        _cooldown = 0.35f;
        if (edit.Headline == Verdict.Bedrock) Said?.Invoke("BEDROCK: the tool can't cut it", Tones.Warning);
        Sfx?.Play(Sfx.Sound.Clank, at);
        return edit;
    }

    /// <summary>A living creature in arm's reach in front of what the tool is on takes the blow instead (its body is solid to the tool too).</summary>
    bool StrikeCreature()
    {
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        Creature c = _zone.Wildlife.Raycast(eye, dir, _rules.CutReach, out float distance, out Vector3 point);
        if (c == null || c.Dead) return false;
        if (OnPlace && _placePoint.DistanceTo(eye) < distance) return false;
        _cooldown = 0.4f;
        _suit.Worked();
        _zone.Wildlife.Damage(c, _rules.ShotDamage * 0.5f * _zone.Wildlife.HitScale(c, point));
        CreatureHit?.Invoke(c);
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
            if (PlanetSurface.Infra.InfraLayer.TellOf(d) == GalHack.Deco.DecoTellKind.Fracture) continue;
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
        _tellsWorked[(d.X, d.Y)] = _tellsWorked.GetValueOrDefault((d.X, d.Y)) + 1;
        Vector3 at = _placePoint;
        Sfx?.Play(Sfx.Sound.Clank, at, 1.1f, -8f);
        Debris?.Puff(at, new Color(0.55f, 0.52f, 0.48f, 0.6f), 0.5f);
        if (OreOf(d.Kind) is ItemDef item)
        {
            Give(item.Id, TellOrePerPulse);
            TellOre += TellOrePerPulse;
            Said?.Invoke($"+{TellOrePerPulse} {item.Name}", Tones.Good);
        }
        // Worked through: the rock and its collider go, and the tool digs on into the ground under it.
        if (_tellsWorked[(d.X, d.Y)] >= TellPulses) _zone.Infra.BreakTell(d);
    }

    /// <summary>One pulse on a place's block or piece, or a wreck: it comes away once worked through, its materials into the pack.</summary>
    void WorkPlace()
    {
        _cooldown = _rules.DigInterval;
        _suit.Worked();
        float damage = Places.Rules.Salvage.ToolDamage;
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

    /// <summary>
    /// Cuts the plant nearest the crosshair within reach: biomass, and organics for the replicator,
    /// and the plant is gone from the ground for good: planet-surface's felling rule
    /// (<see cref="PlanetSurface.World.PlanetWorld.Felled"/> and <see cref="TerrainStreamer.Fell"/>),
    /// so a tree's collider goes in the same frame and the cut is kept through streaming and saves.
    /// </summary>
    public bool CutPlant()
    {
        if (!FloraFinder.Nearest(_zone.Streamer, _survivor.WorldEye(), _survivor.WorldLook(), _rules.CutReach, out Vector3 plant, out float scale, out long id))
            return false;
        if (!_zone.Planet.Felled.Add(id)) return false;
        _zone.Streamer.Fell(id);
        PlantsCut++;
        _cooldown = _rules.DigInterval * 2f;
        _suit.Worked();
        float size = Math.Clamp(scale, 0.5f, 3f);
        double organics = Math.Round(_yields.OrganicsPerPlant * size, 1);
        int biomass = Math.Max(1, (int)MathF.Round(_yields.BiomassPerPlant * size));
        _suit.Kit.Organics += organics;
        Give("biomass", biomass);
        PlantCut?.Invoke();
        Debris?.Puff(plant + Vector3.Up * 0.8f, new Color(0.35f, 0.5f, 0.2f, 0.6f), 1.5f);
        Sfx?.Play(Sfx.Sound.Dig, plant, 1.4f, -6f);
        Said?.Invoke($"+{biomass} Biomass, +{organics:F1} kg organics", Tones.Good);
        return true;
    }

    /// <summary>The blue working beam from the barrel tip to where the tool is digging, cutting or taking down (call each frame it works).</summary>
    public void ShowBeam(Vector3 to) => Fx?.Beam(Muzzle(), to);

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

    void Shoot()
    {
        _cooldown = _rules.ShotInterval;
        Shots++;
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
        uint mask = Layers.World | Layers.Hull | Layers.Props | PlanetSurface.World.Layers.Flora;
        // The voxel cast counts whole cubes, and a cube's corner stands proud of the smooth ground that is
        // drawn: a block behind it (a bunker wall at an angle to the grid) is what the bolt meets, and on a
        // shallow slope the drawn ground lies further on, the flatter the shot the further. So the world's
        // own ray runs on past the cube by what a cube's height is worth along this shot, and has the say.
        float past = kind == ImpactKind.Ground ? Math.Clamp(1.2f / MathF.Max(MathF.Abs(dir.Y), 0.01f), 0.75f, 6f) : 0f;
        if (WorldRay.Cast(_zone, _survivor, reach + past, mask, out LookHit hit))
        {
            reach = hit.Distance;
            normal = hit.Normal;
            kind = hit.OnBlock ? ImpactKind.Block : ImpactKind.Ground;
            onto = hit.Vessel?.Node;
        }
        PoiPlaces.Hit? onPlace = null;
        if (Places != null && kind != ImpactKind.Nothing && WorldRay.Cast(_zone, _survivor, reach + 0.01f, Layers.World, out LookHit wall)
            && Places.Resolve(wall.Collider, wall.Point, wall.Normal, out PoiPlaces.Hit ph)) onPlace = ph;
        Creature c = _zone.Wildlife.Raycast(eye, dir, reach, out float distance, out Vector3 point);
        bool struck = c != null && !c.Dead;
        // Someone of a town in the way: hit, and the town takes it badly. Shooting inside a town at all does.
        float personAt = 0f;
        Town.Person person = Places == null ? null : Places.PersonAlong(eye, dir, struck ? distance : reach, out personAt);
        if (person != null)
        {
            struck = false;
            c = null;
            kind = ImpactKind.Creature;
            reach = personAt;
            person.Town.Shoot(person, _rules.ShotDamage);
            onPlace = null;
        }
        else (Places?.TownAt(eye) ?? Places?.TownAt(eye + dir * MathF.Min(reach, 30f)))?.Provoked();
        if (onPlace is PoiPlaces.Hit target && !struck && Places.Shot(target, _rules.ShotDamage))
            Said?.Invoke("The cracked panel gives way: a room behind it", Tones.Good);
        Vector3 end = struck ? point : eye + dir * reach;
        if (struck)
        {
            kind = ImpactKind.Creature;
            onto = null;
            _zone.Wildlife.Damage(c, _rules.ShotDamage * _zone.Wildlife.HitScale(c, point));
            CreatureHit?.Invoke(c);
        }
        else if (kind == ImpactKind.Block && hit.OnBlock)
            hit.Vessel.Hits.Shot(hit.Cell, hit.Point.ToNumerics(), hit.Normal.ToNumerics(), _rules.ShotDamage, GalHack.Structures.DamageType.Energy);
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
    public static bool Nearest(TerrainStreamer streamer, Vector3 eye, Vector3 dir, float reach, out Vector3 at, out float scale, out long id)
    {
        at = default;
        scale = 0f;
        id = 0;
        float best = float.MaxValue;
        foreach (var (_, root, s, plantId) in streamer.FloraNear(eye, reach + 2f))
        {
            // A plant is a column from its root up to about twice its scale.
            Vector3 mid = root + Vector3.Up * Mathf.Min(1.2f, s);
            float along = (mid - eye).Dot(dir);
            if (along < 0f || along > reach + 0.5f) continue;
            if ((eye + dir * along - mid).Length() > 0.6f + 0.4f * s) continue;
            if (along < best)
            {
                best = along;
                at = root;
                scale = s;
                id = plantId;
            }
        }
        return best < float.MaxValue;
    }
}
