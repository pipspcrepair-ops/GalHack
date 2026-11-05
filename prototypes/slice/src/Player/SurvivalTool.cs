using System;
using System.Collections.Generic;
using GalHack.Physics.Engine;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Fx;
using PlanetSurface.Terrain;
using Slice.Game;
using Slice.Surface;
using TerrainDigging.Terrain;
using Wildlife;
using Inventory = GalHack.Structures.Inventory;

namespace Slice.Player;

/// <summary>The survival tool's two modes.</summary>
public enum ToolMode { Gather, Shoot }

/// <summary>
/// The survival tool every castaway keeps: in Gather mode it digs a small ball of ground under
/// the crosshair (terrain-digging's rules: bedrock and anchored ground refuse) and cuts plants;
/// in Shoot mode it is a short blaster. Dug ore and cut plants go straight into the pack as the
/// building catalogue's materials (a placeholder until manufacturing exists), plants also as
/// organics for the replicator.
/// </summary>
public sealed class SurvivalTool
{
    readonly SurfaceZone _zone;
    readonly Survivor _survivor;
    readonly Suit _suit;
    readonly Inventory _pack;
    readonly SliceRules.ToolRules _rules;
    readonly SliceRules.MaterialRules _yields;

    public ToolMode Mode = ToolMode.Gather;
    public Debris Debris;
    public Sfx Sfx;

    /// <summary>What the tool did, for the visor ("+6 iron", "BEDROCK").</summary>
    public event Action<string, Color> Said;

    /// <summary>A shot was fired from, to (for the tracer).</summary>
    public event Action<Vector3, Vector3, bool> Fired;

    /// <summary>Materials gathered since the start, by kind (tests and the wrist).</summary>
    public readonly SortedDictionary<string, int> Gathered = new(StringComparer.Ordinal);

    /// <summary>Plant columns cut so far (canonical cells), kept so they stay cut.</summary>
    public int PlantsCut { get; private set; }

    public bool HasTarget { get; private set; }
    public Int3 Target { get; private set; }
    public Verdict TargetVerdict { get; private set; }
    public string TargetLabel { get; private set; } = "";

    float _cooldown;

    /// <summary>Cut plants by frame column, so one isn't found again before the ground's flora is rebuilt.</summary>

    public SurvivalTool(SurfaceZone zone, Survivor survivor, Suit suit, Inventory pack)
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
        else if (!CutPlant()) Dig();
    }

    // ---- Aim -----------------------------------------------------------------------------------

    void Aim()
    {
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        PlanetTerrain terrain = _zone.Planet.Terrain;
        HasTarget = terrain.Store.Raycast(eye.ToNumerics(), dir.ToNumerics(), _rules.Reach, out Int3 cell, out _, out _);
        TargetLabel = "";
        if (!HasTarget) return;
        Target = cell;
        TargetVerdict = terrain.Check(cell, EditKind.Tool);
        CellMaterial m = terrain.Store.MaterialAt(cell);
        TargetLabel = TargetVerdict == Verdict.Bedrock ? "BEDROCK"
            : m == CellMaterial.Ore ? MaterialOf(_zone.Planet.Source.OreKindAt(cell)).ToUpperInvariant() + " ORE"
            : m.Label().ToUpperInvariant();
    }

    // ---- Gather --------------------------------------------------------------------------------

    /// <summary>One dig pulse at the target. Ore becomes materials in the pack.</summary>
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
            foreach (RemovedCell r in edit.Removed)
            {
                if (r.Material != CellMaterial.Ore) continue;
                string material = MaterialOf(_zone.Planet.Source.OreKindAt(r.Cell));
                if (!_yields.OrePerCell.TryGetValue(material, out int n) || n <= 0) continue;
                got[material] = got.GetValueOrDefault(material) + n;
            }
            foreach (var (material, n) in got) Give(material, n);
            Debris?.Burst(edit.Removed, _survivor.WorldEye());
            Sfx?.Play(Sfx.Sound.Dig, at);
            if (got.Count > 0)
            {
                Sfx?.Play(Sfx.Sound.Ore, at, 1f, -4f);
                foreach (var (material, n) in got) Said?.Invoke($"+{n} {material}", Tones.Good);
            }
            return edit;
        }
        _cooldown = 0.35f;
        if (edit.Headline == Verdict.Bedrock) Said?.Invoke("BEDROCK: the tool can't cut it", Tones.Warning);
        else if (edit.Headline == Verdict.Anchored) Said?.Invoke("ANCHORED: the ground here holds", Tones.Caution);
        Sfx?.Play(Sfx.Sound.Clank, at);
        return edit;
    }

    /// <summary>The deposit kinds as the catalogue's material names.</summary>
    public static string MaterialOf(DepositKind kind) => kind.ToString().ToLowerInvariant();

    void Give(string material, int n)
    {
        _pack.Add(new Dictionary<string, int> { [material] = n });
        Gathered[material] = Gathered.GetValueOrDefault(material) + n;
    }

    /// <summary>
    /// Cuts the plant nearest the crosshair within reach: organics for the replicator and carbon
    /// for building, and the plant is gone from the ground, and stays gone: planet-surface's felling
    /// rule (<see cref="PlanetWorld.Felled"/> and <see cref="TerrainStreamer.Fell"/>), so a tree's
    /// collider goes in the same frame and the cut is kept through streaming and saves.
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
        double organics = Math.Round(_yields.OrganicsPerPlant * Math.Clamp(scale, 0.5f, 3f), 1);
        int carbon = Math.Max(1, (int)MathF.Round(_yields.CarbonPerPlant * Math.Clamp(scale, 0.5f, 3f)));
        _suit.Kit.Organics += organics;
        Give("carbon", carbon);
        Debris?.Puff(plant + Vector3.Up * 0.8f, new Color(0.35f, 0.5f, 0.2f, 0.6f), 1.5f);
        Sfx?.Play(Sfx.Sound.Dig, plant, 1.4f, -6f);
        Said?.Invoke($"+{organics:F1} kg organics, +{carbon} carbon", Tones.Good);
        return true;
    }

    // ---- Shoot ---------------------------------------------------------------------------------

    void Shoot()
    {
        _cooldown = _rules.ShotInterval;
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        float reach = _rules.ShotRange;
        if (_zone.Planet.Terrain.Store.Raycast(eye.ToNumerics(), dir.ToNumerics(), reach, out _, out _, out float ground)) reach = ground;
        Creature c = _zone.Wildlife.Raycast(eye, dir, reach, out float distance, out Vector3 point);
        bool hit = c != null && !c.Dead;
        if (hit) _zone.Wildlife.Sim.Damage(c, _rules.ShotDamage, _zone.Wildlife.Player);
        Vector3 end = hit ? point : eye + dir * reach;
        Fired?.Invoke(eye + dir * 0.6f, end, hit);
        Sfx?.Play(Sfx.Sound.Ping, eye, hit ? 0.8f : 1.2f, -8f);
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
