using System;
using System.Collections.Generic;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Fx;
using PlanetSurface.Terrain;
using Slice.Game;
using Slice.Items;
using Slice.Surface;
using TerrainDigging.Terrain;
using Wildlife;
using Layers = GalHack.Physics.Layers;

namespace Slice.Player;

/// <summary>The survival tool's two modes, switched with a key the visor shows.</summary>
public enum ToolMode { Gather, Shoot }

/// <summary>
/// The survival tool, an item on the toolbar: in Gather mode it digs a small ball of ground under
/// the crosshair (terrain-digging's rules: bedrock and anchored ground refuse) and cuts plants;
/// in Shoot mode it is a short blaster whose bolt you can see fly from the muzzle and land, with
/// sparks and a scorch where it strikes. Shots only hurt creatures. Every ray asks the world the
/// survivor is in (<see cref="WorldRay"/>), so inside a vessel the tool meets the vessel's own
/// walls: it digs nothing through a floor, and a shot scorches the wall it hits, harmlessly.
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
    public Debris Debris;
    public Sfx Sfx;
    public ShotFx Fx;

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
        if (!trigger || _cooldown > 0f || _suit.Dead) return;
        if (Mode == ToolMode.Shoot) Shoot();
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
        else if (edit.Headline == Verdict.Anchored) Said?.Invoke("ANCHORED: the ground here holds", Tones.Caution);
        Sfx?.Play(Sfx.Sound.Clank, at);
        return edit;
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

    // ---- Shoot ---------------------------------------------------------------------------------

    /// <summary>Where the bolt leaves the tool: low and to the right of the eye, out along the look (the first-person tool's muzzle).</summary>
    public Vector3 Muzzle()
    {
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
        if (WorldRay.Cast(_zone, _survivor, reach, mask, out LookHit hit) && hit.Distance <= reach)
        {
            reach = hit.Distance;
            normal = hit.Normal;
            kind = hit.OnBlock ? ImpactKind.Block : ImpactKind.Ground;
            onto = hit.Vessel?.Node;
        }
        Creature c = _zone.Wildlife.Raycast(eye, dir, reach, out float distance, out Vector3 point);
        bool struck = c != null && !c.Dead;
        Vector3 end = struck ? point : eye + dir * reach;
        if (struck)
        {
            kind = ImpactKind.Creature;
            onto = null;
            _zone.Wildlife.Sim.Damage(c, _rules.ShotDamage, _zone.Wildlife.Player);
            CreatureHit?.Invoke(c);
        }
        Vector3 muzzle = Muzzle();
        Vector3 at = end, face = normal;
        Node3D on = onto;
        ImpactKind landed = kind;
        if (Fx != null) Fx.Fire(muzzle, end, default, () => Fx.Impact(at, face, landed, on));
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
