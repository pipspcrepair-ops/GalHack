using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Surface;
using Weather.Model;
using Wildlife;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Vessels;

/// <summary>
/// Where a structure's damage lands on its blocks. A crash reaches it through the world's one
/// damage path (<see cref="PhysicsWorld.Hurt(Body, float, N.Vector3, N.Vector3)"/>, this body's
/// sink), with the amount from the one formula; shots, bites and blasts reach it through
/// <see cref="Shot"/> and <see cref="Blast"/>, and what they deal goes back through the same path,
/// so hull points count every kind of harm.
///
/// Hits are queued as they happen and applied once a frame (<see cref="Flush"/>), however many
/// physics ticks or hits there were. Hits that only wear blocks away just redraw the chunks they
/// touched; colliders, mass and the rest are only rebuilt when a block is actually gone.
/// </summary>
public sealed class VesselDamage : ILocatedDamageSink
{
    /// <summary>How much lightning does to a large block, per point it does to a person. Grids scale it, as they scale hit points.</summary>
    const float LightningToBlocks = 6f;

    readonly record struct Pending(float Raw, float Hp, N.Vector3 Point, N.Vector3 Into, Int3 Cell, DamageType Type, float Radius, HitKind Kind);

    enum HitKind { Crash, Shot, Blast }

    readonly Vessel _v;
    readonly List<Pending> _queue = new();
    int _totalVersion = -1;
    float _totalHp;

    /// <summary>Tests: flushes that did anything, hits taken and blocks lost since the start.</summary>
    public int Flushes { get; private set; }
    public int HitsTaken { get; private set; }
    public int BlocksLost { get; private set; }

    /// <summary>The last flush's cost, ms, and how many chunks it left to redraw.</summary>
    public double LastFlushMs { get; private set; }

    public int Queued => _queue.Count;

    public VesselDamage(Vessel vessel) => _v = vessel;

    Structure S => _v.Structure;
    float Cell => S.Grid.CellSize;

    // ---- Hits coming in -----------------------------------------------------------------------

    /// <summary>A hit with no idea where it landed: it counts on the hull, and no block shows it.</summary>
    public float Take(Body body, float raw) => raw;

    /// <summary>A crash, from the world's damage path: it lands where it struck.</summary>
    public float TakeAt(Body body, float raw, N.Vector3 point, N.Vector3 into)
    {
        _queue.Add(new Pending(raw, 0f, body.ToLocal(point) / Cell, MathX.Unrotate(body.Rotation, into), default, default, 0f, HitKind.Crash));
        return raw;
    }

    /// <summary>A bolt or a bite on one block: armour and resistance apply. World point, and the face's normal.</summary>
    public void Shot(Int3 cell, N.Vector3 point, N.Vector3 normal, float amount, DamageType type) =>
        _queue.Add(new Pending(0f, amount, _v.Body.ToLocal(point) / Cell, -MathX.Unrotate(_v.Body.Rotation, normal), cell, type, 0f, HitKind.Shot));

    /// <summary>An explosion at a world point, radius in metres: falls off with distance, as <see cref="Damage.Blast"/> does.</summary>
    public void Blast(N.Vector3 point, float radius, float amount, DamageType type = DamageType.Explosive) =>
        _queue.Add(new Pending(0f, amount, _v.Body.ToLocal(point) / Cell, default, default, type, radius / Cell, HitKind.Blast));

    /// <summary>Lightning that strikes close enough to a structure hits its blocks as a small blast.</summary>
    public void OnWeather(WeatherEvent e)
    {
        if (e.Kind != WeatherEventKind.LightningStrike || e.DamageRadius <= 0f || _v.Zone.VesselOf(_v.Body) != _v) return;
        N.Vector3 at = new(e.Position.X, e.Position.Y, e.Position.Z);
        if (_v.DistanceFromHull(_v.Body.ToLocal(at), e.DamageRadius) >= e.DamageRadius) return;
        Blast(at, e.DamageRadius, e.Damage * LightningToBlocks * S.Grid.HpScale, DamageType.Energy);
    }

    /// <summary>
    /// A creature's lunge that meets a structure on the way to its target lands on the block in
    /// the way, not on whoever is behind it. Returns true if it did.
    /// </summary>
    public static bool Struck(SurfaceZone zone, Creature c, Vector3 target)
    {
        Vector3 from = zone.Wildlife.FromPatch(c.Position) + Vector3.Up * 0.8f, to = target - from;
        float reach = to.Length();
        if (reach < 0.1f || !WorldRay.Cast(zone, null, from, to / reach, reach, GalHack.Physics.Layers.Hull, out LookHit hit) || !hit.OnBlock) return false;
        hit.Vessel.Hits.Shot(hit.Cell, hit.Point.ToNumerics(), hit.Normal.ToNumerics(), c.Stats.Damage, DamageType.Kinetic);
        return true;
    }

    // ---- Once a frame -------------------------------------------------------------------------

    /// <summary>The hit points a whole hull's integrity stands for: this share of the hull's points is this share of its blocks' health.</summary>
    float HpPerRaw()
    {
        if (_totalVersion != S.Version)
        {
            _totalVersion = S.Version;
            _totalHp = 0f;
            foreach (var (_, b) in S.Blocks()) _totalHp += S.MaxHp(b);
        }
        return _v.Body.Integrity > 0f ? _totalHp / _v.Body.Integrity : 0f;
    }

    /// <summary>Applies everything queued since the last frame, in one go.</summary>
    public void Flush()
    {
        if (_queue.Count == 0) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        float perRaw = HpPerRaw();
        var report = new DamageReport();
        float harm = 0f;
        foreach (Pending p in _queue)
        {
            HitsTaken++;
            float before = report.Dealt;
            switch (p.Kind)
            {
                case HitKind.Crash:
                    Damage.Crash(S, p.Point, p.Into, p.Raw * perRaw, report);
                    break;
                case HitKind.Shot:
                    Damage.Hit(S, p.Cell, p.Type, p.Hp, report, p.Point, -p.Into);
                    harm += report.Dealt - before;
                    break;
                default:
                    Damage.BlastInto(S, p.Point, p.Radius, p.Type, p.Hp, report);
                    harm += report.Dealt - before;
                    break;
            }
        }
        _queue.Clear();
        Flushes++;

        // Crashes came through the world's path already; the rest count on the hull the same way.
        if (harm > 0f && perRaw > 0f) _v.Zone.Physics.Hurt(_v.Body, harm / perRaw);

        if (report.Destroyed.Count > 0) Lost(report);
        else
        {
            // Nothing changed shape: redraw the worn chunks, and leave colliders, mass and sleepers alone.
            _v.View.Queue(S.DirtyChunks);
            S.DirtyChunks.Clear();
        }
        LastFlushMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>Blocks are gone: what they held up breaks away, what they stored is set down, and the core going ends the vessel.</summary>
    void Lost(DamageReport report)
    {
        BlocksLost += report.Destroyed.Count;
        if (_v.IsBase)
            foreach (var (p, _) in report.Destroyed) _v.Zone.Edits?.ReleaseStructure(new TerrainDigging.Terrain.VolumeKey(_v.Body.Id, p.X, p.Y, p.Z));

        Vector3 at = _v.ToWorld(StructureBridge.CellCenter(S, report.Destroyed[0].pos));
        if (S.Count > 0)
            foreach (Structure piece in Connectivity.SplitOff(S)) _v.Sever(piece);

        _v.Storage.CapacitySu = BuildingBridge.StorageCapacity(S);
        Bill overflow = _v.Storage.TakeOverflow();
        S.CargoSu = _v.Storage.UsedSu;
        if (!overflow.IsEmpty) _v.Zone.Loot.Spill(overflow, new Vector3(at.X, MathF.Max(at.Y - 0.2f, _v.Zone.GroundAt(at.X, at.Z).Y), at.Z));

        _v.ApplyEdits();
        if (S.Count == 0)
        {
            _v.Zone.Physics.Unregister(_v.Body);
            _v.Zone.Physics.StuckFields.Remove(_v.Field);
            _v.Zone.Untrack(_v);
            _v.Node.QueueFree();
        }
        else if (S.Core == null && _v.Body.Kind == BodyKind.Vessel) _v.Zone.Physics.MakeWreck(_v.Body); // no core, no power
    }

    // ---- Repair -------------------------------------------------------------------------------

    /// <summary>
    /// Gives a damaged block back a share of its health, paid for from the structure's shared
    /// storage: that share of the block's bill, rounded up. The look steps back as it heals.
    /// </summary>
    public RepairStatus Repair(Int3 cell, float share = 1f)
    {
        if (!Building.IsDamaged(S, cell)) return RepairStatus.NotDamaged;
        if (!_v.Zone.WorldRules.Creative)
        {
            Bill bill = RepairBill(cell, share);
            if (!_v.Storage.TryTake(bill)) return RepairStatus.NotEnoughMaterials;
            S.CargoSu = _v.Storage.UsedSu;
        }
        Building.Repair(S, cell, share);
        _v.View.Queue(S.DirtyChunks);
        S.DirtyChunks.Clear();
        return RepairStatus.Repaired;
    }

    /// <summary>What repairing this share of a block would cost.</summary>
    public Bill RepairBill(Int3 cell, float share = 1f)
    {
        var bill = new Bill();
        if (!S.TryGet(cell, out Block b) || !BuildingBridge.Priced(_v.Zone.Data.Items, S, b)) return bill;
        float paid = Building.RepairShare(S, cell, share);
        foreach (var (id, n) in BuildingBridge.BillOf(_v.Zone.Data.Items, S, b))
            bill.Add(id, Math.Max(1, (int)MathF.Ceiling(n * paid)));
        return bill;
    }
}
