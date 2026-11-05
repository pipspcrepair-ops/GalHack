using System;
using System.Collections.Generic;
using System.Numerics;
using TerrainDigging.Terrain;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace CapitalBase.Core;

/// <summary>
/// One block as a box in the world: its centre, the hull's orientation, half its size, and how far
/// it is from the hull's origin (a lever arm: how far it swings if the hull turns).
/// </summary>
public readonly record struct BlockBox(Vector3 Center, Quaternion Rotation, Vector3 Half, float Arm);

/// <summary>What lift-off has to cut, or why it can't.</summary>
public sealed class CutPlan
{
    /// <summary>Every terrain cell to remove, sorted, each once.</summary>
    public readonly List<TInt3> Cells = new();

    /// <summary>Cells in the way that the terrain refuses to give up (bedrock, a POI's anchored ground).</summary>
    public readonly List<TInt3> Refused = new();

    /// <summary>How far the hull climbs straight up before the pilot has it, m.</summary>
    public float Climb;

    /// <summary>Why lift-off can't go ahead, or null.</summary>
    public string Refusal;

    public bool Ok => Refusal == null;

    /// <summary>How deep the deepest block sits under the terrain above it, m (0 when nothing is buried).</summary>
    public float DeepestBurial;
}

/// <summary>
/// Cutting a hull free of the ground, and clearing the ground a new block is built into.
///
/// <b>Lift-off.</b> A hull leaves the ground straight up. Every terrain cell that lies within
/// <c>margin</c> of a block anywhere along that climb is cut, before the hull moves:
/// the sweep of each block's box, grown by the margin (plus its lever arm times the hull's tilt,
/// since the horizon assist may level the wings on the way up), from where it is to where it
/// will be after the climb. The climb is long enough that every block's column is open sky for
/// <c>clearBand</c> metres above it. So nothing is left to snag on, and since nothing is cut from
/// under the hull's path, nothing is left to push it either. A plan is a pure function of the
/// blocks and the terrain, so it is the same on every machine.
/// </summary>
public static class CutFree
{
    const float Epsilon = 1e-6f;

    /// <summary>The terrain's verdict on removing one cell (terrain-digging's rules).</summary>
    public delegate Verdict CellCheck(TInt3 cell);

    public static CutPlan Plan(IReadOnlyList<BlockBox> blocks, float tilt, CapitalRules.LiftoffData rules, CellCheck check)
    {
        var plan = new CutPlan();
        if (blocks.Count == 0)
        {
            plan.Climb = rules.MinClimb;
            return plan;
        }

        // How far to climb: until every block's own column is open for a band above it.
        float climb = rules.MinClimb;
        foreach (BlockBox b in blocks)
        {
            float margin = MarginOf(b, tilt, rules);
            (Vector3 lo, Vector3 hi) = Bounds(b, margin);
            float need = ClimbFor(lo, hi, rules, check, out float buried);
            plan.DeepestBurial = MathF.Max(plan.DeepestBurial, buried);
            if (need > rules.MaxClimb)
            {
                plan.Refusal = $"LIFT-OFF REFUSED: a block is buried over {rules.MaxClimb:F0} m deep. Dig it out first.";
                plan.Climb = need;
                return plan;
            }
            climb = MathF.Max(climb, need);
        }
        plan.Climb = climb;

        // Every cell any block's grown box passes through on the way up.
        var swept = new HashSet<TInt3>();
        var up = Vector3.UnitY;
        foreach (BlockBox b in blocks)
        {
            float margin = MarginOf(b, tilt, rules);
            (Vector3 lo, Vector3 hi) = Bounds(b, margin);
            hi.Y += climb;
            TInt3 a = TInt3.Floor(lo), z = TInt3.Floor(hi);
            Quaternion inverse = Quaternion.Inverse(b.Rotation);
            Vector3 axisUp = Vector3.Transform(up, inverse);
            Vector3 half = b.Half + new Vector3(margin);
            for (int y = a.Y; y <= z.Y; y++)
            for (int zz = a.Z; zz <= z.Z; zz++)
            for (int x = a.X; x <= z.X; x++)
            {
                var cell = new TInt3(x, y, zz);
                if (swept.Contains(cell)) continue;
                Vector3 local = Vector3.Transform(cell.Center - b.Center, inverse);
                if (SweptHits(local, axisUp, half, climb)) swept.Add(cell);
            }
        }

        foreach (TInt3 cell in swept)
        {
            switch (check(cell))
            {
                case Verdict.Removable: plan.Cells.Add(cell); break;
                case Verdict.AlreadyAir: break;
                default: plan.Refused.Add(cell); break;
            }
        }
        plan.Cells.Sort();
        plan.Refused.Sort();

        if (plan.Refused.Count > 0)
        {
            TInt3 first = plan.Refused[0];
            Verdict why = check(first);
            plan.Refusal = why == Verdict.Bedrock
                ? $"LIFT-OFF REFUSED: the hull is keyed into bedrock at {first} ({plan.Refused.Count} cells). Build it free or dig round it."
                : $"LIFT-OFF REFUSED: {plan.Refused.Count} cells of protected ground ({why}) hold the hull at {first}.";
        }
        else if (plan.Cells.Count > rules.MaxCells)
            plan.Refusal = $"LIFT-OFF REFUSED: {plan.Cells.Count} m³ of ground hold the hull (most a lift-off cuts is {rules.MaxCells}). Dig some of it out first.";
        return plan;
    }

    /// <summary>A block's margin: the rule's, plus how far it could swing while the hull levels.</summary>
    public static float MarginOf(BlockBox b, float tilt, CapitalRules.LiftoffData rules) => rules.Margin + b.Arm * MathF.Sin(MathF.Min(tilt, 0.5f));

    /// <summary>
    /// The climb a block needs: from its top, up until its column (its grown footprint) has
    /// <c>clearBand</c> metres of air. <paramref name="buried"/> is how deep its top is under the
    /// highest solid cell in that column right above it.
    /// </summary>
    static float ClimbFor(Vector3 lo, Vector3 hi, CapitalRules.LiftoffData rules, CellCheck check, out float buried)
    {
        buried = 0f;
        TInt3 a = TInt3.Floor(lo), z = TInt3.Floor(hi);
        int top = (int)MathF.Floor(hi.Y);
        int band = (int)MathF.Ceiling(rules.ClearBand);
        int limit = top + (int)MathF.Ceiling(rules.MaxClimb) + band + 1;
        int clearRun = 0, lastSolid = int.MinValue;
        for (int y = top; y <= limit; y++)
        {
            bool solid = false;
            for (int zz = a.Z; zz <= z.Z && !solid; zz++)
            for (int x = a.X; x <= z.X && !solid; x++)
                solid = check(new TInt3(x, y, zz)) != Verdict.AlreadyAir;
            if (solid)
            {
                lastSolid = y;
                clearRun = 0;
            }
            else if (++clearRun >= band) break;
        }
        if (lastSolid == int.MinValue) return rules.MinClimb;
        buried = MathF.Max(0f, lastSolid + 1 - hi.Y);
        // Climb until the block's top clears the highest cell found, plus the band.
        return MathF.Max(rules.MinClimb, lastSolid + 1 - hi.Y + rules.ClearBand);
    }

    /// <summary>The world box around a block's box, grown by a margin.</summary>
    public static (Vector3 lo, Vector3 hi) Bounds(BlockBox b, float margin)
    {
        Vector3 half = b.Half + new Vector3(margin);
        Vector3 ex = Vector3.Transform(new Vector3(half.X, 0, 0), b.Rotation);
        Vector3 ey = Vector3.Transform(new Vector3(0, half.Y, 0), b.Rotation);
        Vector3 ez = Vector3.Transform(new Vector3(0, 0, half.Z), b.Rotation);
        var extent = new Vector3(
            MathF.Abs(ex.X) + MathF.Abs(ey.X) + MathF.Abs(ez.X),
            MathF.Abs(ex.Y) + MathF.Abs(ey.Y) + MathF.Abs(ez.Y),
            MathF.Abs(ex.Z) + MathF.Abs(ey.Z) + MathF.Abs(ez.Z));
        return (b.Center - extent, b.Center + extent);
    }

    /// <summary>
    /// Does a point (in the box's own frame) lie in the box anywhere along a climb of
    /// <paramref name="height"/> in direction <paramref name="dir"/>? The box moves, so test the
    /// point moving the other way: p − t·dir in the box for some t in [0, height].
    /// </summary>
    static bool SweptHits(Vector3 p, Vector3 dir, Vector3 half, float height)
    {
        float t0 = 0f, t1 = height;
        for (int i = 0; i < 3; i++)
        {
            float pi = i == 0 ? p.X : i == 1 ? p.Y : p.Z;
            float di = i == 0 ? dir.X : i == 1 ? dir.Y : dir.Z;
            float hi = i == 0 ? half.X : i == 1 ? half.Y : half.Z;
            if (MathF.Abs(di) < Epsilon)
            {
                if (MathF.Abs(pi) > hi) return false;
                continue;
            }
            float a = (pi - hi) / di, b = (pi + hi) / di;
            if (a > b) (a, b) = (b, a);
            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, b);
            if (t0 > t1) return false;
        }
        return true;
    }

    /// <summary>
    /// Building into the ground: every cell whose centre lies inside a new block's box, grown by
    /// <paramref name="margin"/>, sorted. The caller removes them through the terrain's edit API.
    /// </summary>
    public static List<TInt3> CellsInside(BlockBox b, float margin)
    {
        var cells = new List<TInt3>();
        (Vector3 lo, Vector3 hi) = Bounds(b, margin);
        TInt3 a = TInt3.Floor(lo), z = TInt3.Floor(hi);
        Quaternion inverse = Quaternion.Inverse(b.Rotation);
        Vector3 half = b.Half + new Vector3(margin);
        for (int y = a.Y; y <= z.Y; y++)
        for (int zz = a.Z; zz <= z.Z; zz++)
        for (int x = a.X; x <= z.X; x++)
        {
            var cell = new TInt3(x, y, zz);
            Vector3 local = Vector3.Transform(cell.Center - b.Center, inverse);
            if (MathF.Abs(local.X) <= half.X && MathF.Abs(local.Y) <= half.Y && MathF.Abs(local.Z) <= half.Z) cells.Add(cell);
        }
        cells.Sort();
        return cells;
    }
}
