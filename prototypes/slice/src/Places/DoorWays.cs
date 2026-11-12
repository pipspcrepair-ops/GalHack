using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Places;

/// <summary>
/// A place's doors as the things that move through it meet them, standing or not: each door's two leaves as
/// they really stand (slid open some way, shut, or jammed half open), held in the place's parts
/// (<see cref="PlaceParts"/>), so a shut door is solid to every body, a jammed one lets through what fits its
/// gap, and an open one is open to all. The leaves the player sees (<see cref="Place.Door"/>) and the ones
/// bodies meet are the same: one state, moved by whoever opens it (you, or a townsperson on their way).
/// Lift shaft doors are the lifts' own and stay out of it.
/// </summary>
public sealed class DoorWay
{
    public int Index;                 // doorway index, -1 for the way in
    public List<PInt3> Cells;
    public Dir Facing;
    public Vector3 Centre, Along;     // local, mid-height; the doorway's width direction
    public float Width, Height, Thick, Bottom;
    public bool Jammed, Blast, Lift;
    public string Clearance;
    /// <summary>How far open (0 shut, 1 open), and where it is going.</summary>
    public float Open, Target;
    /// <summary>Opened by you (a settlement that turns on you shuts those).</summary>
    public bool ByPlayer;
    /// <summary>Opened by someone of the place (a townsperson on their way): it shuts itself on the timer.</summary>
    public bool ByFolk;
    /// <summary>How long its doorway has been clear, s.</summary>
    public float ClearFor;
    /// <summary>How open its leaves stand in the parts.</summary>
    internal float InParts = -1f;

    public bool Shut => Open < 0.05f;

    /// <summary>Its leaves' boxes (local) at an opening.</summary>
    public IEnumerable<(Vector3 Lo, Vector3 Hi)> Leaves(float open)
    {
        bool alongX = MathF.Abs(Along.X) > 0.5f;
        var half = alongX ? new Vector3(Width / 4f, Height / 2f, Thick / 2f) : new Vector3(Thick / 2f, Height / 2f, Width / 4f);
        for (int side = 0; side < 2; side++)
        {
            float s = side == 0 ? -1f : 1f;
            Vector3 c = Centre + Along * (s * Width / 4f) + Along * (s * open * (Width / 2f - 0.08f));
            yield return (c - half, c + half);
        }
    }

    /// <summary>The widest gap between its leaves now, m.</summary>
    public float Gap => 2f * Open * (Width / 2f - 0.08f);

    /// <summary>Whether a local point is in the doorway (its cells).</summary>
    public bool Holds(PInt3 c) => Cells.Contains(c);
}

public static class DoorWays
{
    /// <summary>How fast a door slides, of its way a second (as the player's door always has).</summary>
    public const float Speed = 1.6f;

    /// <summary>
    /// The place's doors (made once; worker-safe), by the rules the standing place draws them by: a door left
    /// standing open, or whose walls either side are gone, has no leaves; a jammed one pinched by a block either
    /// side was forced wide.
    /// </summary>
    public static List<DoorWay> Of(PlaceMemory m)
    {
        lock (m)
        {
            if (m.DoorWays != null) return m.DoorWays;
            var list = new List<DoorWay>();
            PoiInstance inst = m.Inst;
            Layout layout = inst.Layout;
            var locks = layout.Extras.Locks.ToDictionary(l => l.Doorway);
            var liftDoor = new HashSet<int>();
            foreach (LiftShaft s in layout.Extras.Lifts) foreach (int d in s.StopDoors) liftDoor.Add(d);
            var groups = layout.Doorways.Select((d, i) => (d.Cells, d.Facing, i)).ToList();
            groups.Add((layout.ExitCells, layout.EntranceFacing, -1));
            foreach (var (cells, facing, index) in groups)
            {
                if (cells.Count == 0) continue;
                var doorCells = cells.Where(c => inst.Blocks.Get(c).Type == BlockType.Door).ToList();
                if (doorCells.Count == 0) continue;
                Block b0 = inst.Blocks.Get(doorCells[0]);
                bool lift = index >= 0 && liftDoor.Contains(index);
                bool pinched = b0.Variant == DoorState.Jammed && !lift && cells.Any(c => c.Y <= cells.Min(o => o.Y) + 1
                    && (BlockInfo.Collides(inst.Blocks.Get(c + facing.Vec()).Type) || BlockInfo.Collides(inst.Blocks.Get(c - facing.Vec()).Type)));
                if (pinched) foreach (PInt3 c in doorCells) inst.Blocks.At(c).Variant = DoorState.Open;
                if ((b0.Variant == DoorState.Open || pinched) && !lift) continue;
                PInt3 min = cells.Aggregate(PInt3.Min), max = cells.Aggregate(PInt3.Max);
                bool alongX = facing is Dir.North or Dir.South;
                PInt3 sideA = alongX ? min + new PInt3(-1, 0, 0) : min + new PInt3(0, 0, -1), sideB = alongX ? max with { Y = min.Y } + new PInt3(1, 0, 0) : max with { Y = min.Y } + new PInt3(0, 0, 1);
                if (!lift && (inst.Blocks.Get(sideA).IsAir || inst.Blocks.Get(sideB).IsAir))
                {
                    foreach (PInt3 c in doorCells) inst.Blocks.Set(c, Block.Air);
                    continue;
                }
                var lo = new Vector3(min.X, min.Y, min.Z);
                var hi = new Vector3(max.X + 1, max.Y + 1, max.Z + 1);
                bool blast = b0.Variant == DoorState.Blast;
                var way = new DoorWay
                {
                    Index = index, Cells = cells, Facing = facing, Along = alongX ? Vector3.Right : Vector3.Back,
                    Centre = (lo + hi) / 2f, Width = alongX ? hi.X - lo.X : hi.Z - lo.Z, Height = hi.Y - lo.Y, Bottom = lo.Y,
                    Thick = blast ? 0.4f : 0.18f, Jammed = b0.Variant == DoorState.Jammed, Blast = blast, Lift = lift,
                    Clearance = index >= 0 && locks.TryGetValue(index, out DoorLock lk) ? lk.Clearance : null,
                };
                way.Open = way.Jammed ? 0.5f : m.DoorsOpen.TryGetValue(index, out bool open) && open ? 1f : 0f;
                way.Target = way.Open;
                list.Add(way);
            }
            return m.DoorWays = list;
        }
    }

    /// <summary>A door's leaves belong to it.</summary>
    public static long OwnerOf(DoorWay d, int side) => (1L << 61) | ((long)(d.Index + 2) << 1) | (long)side;

    /// <summary>Its leaves into a place's parts as they stand (shut, if <paramref name="open"/> says so).</summary>
    public static void Put(PartSet parts, DoorWay d, float open)
    {
        if (d.Lift) return;
        for (int side = 0; side < 2; side++) parts.Remove(OwnerOf(d, side));
        int k = 0;
        foreach (var (lo, hi) in d.Leaves(open)) parts.AddBox(lo, hi, OwnerOf(d, k++));
        d.InParts = open;
    }

    /// <summary>
    /// How long a door one of the place's own opened stands open after its doorway is last clear, s: it shuts itself this
    /// long after the last body has left its doorway and its leaves' way (they never shut one by hand). A door you open
    /// stays as you leave it.
    /// </summary>
    public const float AutoClose = 4f;

    /// <summary>Leaves that moved into a body (tests: there must never be one).</summary>
    public static int ShutOnBody;

    /// <summary>Whether any body (a person, a creature, you) is in a door's doorway or its leaves' way: then it doesn't shut.</summary>
    public static bool InWay(PlaceMemory m, DoorWay d)
    {
        Vector3 n = new(d.Along.Z, 0f, d.Along.X);
        float halfAlong = d.Width / 2f, halfAcross = d.Thick / 2f + 0.05f;
        foreach (var source in m.Bodies)
            foreach (var (feet, r, h) in source())
            {
                if (feet.Y > d.Bottom + d.Height || feet.Y + h < d.Bottom) continue;
                Vector3 o = feet - d.Centre;
                float a = MathF.Max(MathF.Abs(o.Dot(d.Along)) - halfAlong, 0f), c = MathF.Max(MathF.Abs(o.X * n.X + o.Z * n.Z) - halfAcross, 0f);
                if (a * a + c * c < r * r) return true;
            }
        return false;
    }

    /// <summary>Whether any body is in a door's leaves as they stand now.</summary>
    static bool LeafOnBody(PlaceMemory m, DoorWay d)
    {
        foreach (var (lo, hi) in d.Leaves(d.Open))
            foreach (var source in m.Bodies)
                foreach (var (feet, r, h) in source())
                {
                    if (feet.Y > hi.Y || feet.Y + h < lo.Y) continue;
                    float dx = MathF.Max(MathF.Max(lo.X - feet.X, feet.X - hi.X), 0f), dz = MathF.Max(MathF.Max(lo.Z - feet.Z, feet.Z - hi.Z), 0f);
                    if (dx * dx + dz * dz < (r - 0.01f) * (r - 0.01f)) return true;
                }
        return false;
    }

    /// <summary>
    /// A door moved toward where it is going this tick. One opened shuts itself <see cref="AutoClose"/> s after its
    /// doorway was last clear; while anything is in the doorway it never shuts, and one shutting opens again. Its
    /// leaves in the parts never stand less than the real ones: opening, they follow it a tenth of its way at a
    /// time; shutting, they are shut at once. <paramref name="target"/>: where it would go (a lift's car away
    /// shuts its shaft door; a barred town's way in).
    /// </summary>
    public static void Step(PlaceMemory m, DoorWay d, float target, float dt)
    {
        if (d.Jammed) target = 0.5f;
        bool inWay = !d.Jammed && (target < d.Open || d.Target > 0.5f) && InWay(m, d);
        if (inWay) d.ClearFor = 0f;
        else d.ClearFor += dt;
        // Opened, and the way clear for long enough: it shuts itself.
        if (!d.Jammed && !d.Lift && d.ByFolk && !d.ByPlayer && d.Target > 0.5f && d.ClearFor >= AutoClose)
        {
            d.Target = 0f;
            d.ByFolk = false;
            m.DoorsOpen[d.Index] = false;
            if (target > 0.5f) target = 0f;
        }
        // Never onto anyone: a door shutting with someone in its way opens again.
        if (inWay && target < d.Open) target = 1f;
        float was = d.Open;
        if (d.Open != target) d.Open = Mathf.MoveToward(d.Open, target, dt * Speed);
        if (d.Open < was && LeafOnBody(m, d)) ShutOnBody++;
        if (d.Lift || m.Parts == null) return;
        float want = target < d.Open ? target : d.Open; // shutting: where they will be; opening: where they are
        if (d.InParts < 0f || MathF.Abs(want - d.InParts) >= 0.1f || want != d.InParts && want == target)
            lock (m) Put(m.Parts, d, want);
    }
    /// <summary>The door whose doorway holds a cell, if any.</summary>
    public static DoorWay At(PlaceMemory m, PInt3 c)
    {
        foreach (DoorWay d in Of(m)) if (d.Holds(c)) return d;
        return null;
    }
}
