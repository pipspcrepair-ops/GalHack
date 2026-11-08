#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A century of weather on a plain building: not noise, but what weather does. Water gets in at one
/// place and the roof goes there, the ceiling after it; a corner or a stretch of wall lets go and takes
/// the roof above it; rubble lies where it fell. Each collapse takes everything above a smooth surface,
/// so walls end in broken tops rather than holes, and nothing is left overhanging. The foundation
/// course and the chimneys stand. Whatever still hangs comes down; the weeds come last, on what's left.
/// </summary>
public static partial class StateTransforms
{
    /// <summary>Takes about <paramref name="share"/> of the structure down, in collapses; <paramref name="walls"/>: walls go as well as roofs.</summary>
    public static void Weathered(PoiInstance inst, Rng rng, float share, bool walls)
    {
        var g = inst.Blocks;
        var boxes = inst.Extras.Plain;
        if (boxes.Count == 0) return;
        int before = PoiFactory.CountStructure(g), target = (int)(before * share);
        var fallen = new List<(Int3 Cell, Block Block)>();
        // Each building takes one through the roof; then more, biggest first, until enough is down.
        var order = boxes.OrderByDescending(b => (b.Max.X - b.Min.X) * (b.Max.Z - b.Min.Z)).ToList();
        foreach (PlainBox b in order) RoofHole(inst, b, rng, fallen);
        // A big works takes more collapses than a house, each a bigger one, until its share is down.
        int collapses = 2 * Math.Max(24, (int)MathF.Sqrt(before));
        for (int k = 0; k < collapses; k++)
        {
            // Counting a whole works is slow: a look every few falls is enough.
            if (k % 8 == 0 && before - PoiFactory.CountStructure(g) >= target) break;
            PlainBox b = order[k % order.Count];
            if (walls && k % 2 == 0) Breach(inst, b, rng, fallen);
            else RoofHole(inst, b, rng, fallen);
        }
        // A tall building may lose a stretch of its outer wall from the first floor up and keep its floors: its rooms
        // stand open in section, what was in them on show.
        if (walls)
            foreach (PlainBox b in order.Where(b => b.Eave >= 7))
            {
                if (rng.Chance(0.2f + 0.6f * inst.Ruin.Wear)) Peel(inst, b, rng, fallen);
                if (rng.Chance(0.15f + 0.5f * inst.Ruin.Wear + 0.2f * inst.Ruin.Violence)) Pancake(inst, b, rng, fallen);
            }
        Collapse(inst, rng, fallen);
        // Rubble lies where it fell: what came down, in its own stuff and colour, under where it came from, heaped where a lot fell.
        Debris.Lay(inst, fallen, rng, 0.5f, 90);
        inst.Fallen.AddRange(fallen.Select(f => f.Cell));
        // The glass goes, a pane or a window at a time.
        foreach (var (p, b) in g.Blocks().ToList())
            if (b.Role == Role.Window && Hash.Cell(new Int3(p.X / 2, p.Y / 2, p.Z / 2), 93 + (uint)inst.SpawnSeed % 7) % 100 < 70) g.Set(p, Block.Air);
        // Boards rot where the rain has got to them, leaving the ground to the weeds.
        foreach (var (p, b) in g.Blocks().ToList())
            if (b.Type == BlockType.Plate && b.Role == Role.Floor && OpenToSky(g, p) && Hash.Cell(new Int3(p.X / 2, 0, p.Z / 2), 95) % 100 < 60) g.Set(p, Block.Air);
    }

    /// <summary>
    /// The outer skin of one side gone over a stretch, from above the ground floor to the eaves: walls and windows
    /// only, a block deep, so the floors run out to the open edge and the rooms behind stand open.
    /// </summary>
    static void Peel(PoiInstance inst, PlainBox b, Rng rng, List<(Int3, Block)> fallen)
    {
        var g = inst.Blocks;
        CellMask occ = inst.Layout.Occupied;
        Dir side = rng.Chance(0.3f) ? b.Front : Dirs.Horizontal[rng.Range(0, 4)];
        Int3 o = side.Vec();
        bool alongX = o.X == 0;
        int lo = alongX ? b.Min.X : b.Min.Z, hi = alongX ? b.Max.X : b.Max.Z;
        int span = Math.Max(4, (int)((hi - lo) * rng.Float(0.3f, 0.7f)));
        int from = lo + rng.Range(0, Math.Max(1, hi - lo - span));
        for (int z = b.Min.Z; z <= b.Max.Z; z++)
            for (int x = b.Min.X; x <= b.Max.X; x++)
            {
                int a = alongX ? x : z;
                if (a < from || a > from + span) continue;
                for (int y = 3; y <= b.Eave; y++)
                {
                    var p = new Int3(x, y, z);
                    Block s = g.Get(p);
                    if (s.IsAir || s.Role is not (Role.Wall or Role.Window) || occ[p + o] || inst.Protected[p] || inst.Extras.Lasting.Contains(p)) continue;
                    g.Set(p, Block.Air);
                    fallen.Add((p, s));
                }
            }
    }

    /// <summary>
    /// A stretch of an upper floor gone down onto the floor under it: torn free along three sides, it hangs from the
    /// fourth and slopes down to lie flat on the floor below, a storey squashed under it. Nothing on the way to the
    /// loot is under it, and no stair or door.
    /// </summary>
    static void Pancake(PoiInstance inst, PlainBox b, Rng rng, List<(Int3, Block)> fallen)
    {
        var g = inst.Blocks;
        int mx = (b.Min.X + b.Max.X) / 2, mz = (b.Min.Z + b.Max.Z) / 2;
        var levels = new List<int>();
        for (int y = 3; y < b.Eave; y++)
            if (g.Get(new Int3(mx, y, mz)) is var f && f.Role is Role.Floor or Role.Roof && BlockInfo.IsFullCube(f.Type) && g.Get(new Int3(mx, y - 1, mz)).IsAir) levels.Add(y);
        if (levels.Count == 0) return;
        int yF = rng.Pick(levels), yL = yF - 1;
        while (yL > b.Min.Y && g.Get(new Int3(mx, yL, mz)).IsAir) yL--;
        int drop = yF - yL - 1;
        if (drop < 1 || drop > 8 || !BlockInfo.IsFullCube(g.Get(new Int3(mx, yL, mz)).Type)) return;
        bool alongX = rng.Chance(0.5f);
        int sign = rng.Chance(0.5f) ? 1 : -1, len = drop + 2 + rng.Range(0, 3), wide = rng.Range(3, 7);
        int a0 = (alongX ? b.Min.X : b.Min.Z) + 2, a1 = (alongX ? b.Max.X : b.Max.Z) - 2, c0 = (alongX ? b.Min.Z : b.Min.X) + 2, c1 = (alongX ? b.Max.Z : b.Max.X) - 2;
        if (a1 - a0 < len || c1 - c0 < wide) return;
        int hinge = sign > 0 ? rng.Range(a0, a1 - len + 1) : rng.Range(a0 + len, a1 + 1), across = rng.Range(c0, c1 - wide + 2);
        Int3 At(int t, int w, int y) => alongX ? new Int3(hinge + sign * t, y, across + w) : new Int3(across + w, y, hinge + sign * t);
        // Only where nothing that matters stands, and the slab is really there along its whole run.
        for (int t = 0; t < len; t++)
            for (int w = 0; w < wide; w++)
            {
                if (g.Get(At(t, w, yF)).Role is not (Role.Floor or Role.Roof)) return;
                for (int y = yL + 1; y <= yF + 1; y++)
                {
                    Int3 c = At(t, w, y);
                    Block x = g.Get(c);
                    if (inst.Protected[c] || x.Type is BlockType.Stair or BlockType.Ladder or BlockType.Door or BlockType.Ramp || x.Role is Role.Loot or Role.Core or Role.Terminal or Role.Wall or Role.Window) return;
                }
            }
        Block slab = g.Get(At(0, 0, yF));
        Block Cube() => new(BlockType.Cube, slab.Material, slab.Color.Scale(0.92f), Role.Floor);
        Dir up = alongX ? (sign > 0 ? Dir.West : Dir.East) : (sign > 0 ? Dir.North : Dir.South); // the slope rises back toward the hinge
        if (up.Vec() != (At(0, 0, 0) - At(1, 0, 0))) up = up.Opposite();
        for (int t = 1; t <= len; t++)
            for (int w = 0; w < wide; w++)
            {
                // What stood in the storey under it is crushed; the slab's own cell is torn out.
                for (int y = yL + 1; y < yF; y++) g.Set(At(t, w, y), Block.Air);
                Int3 torn = At(t, w, yF);
                if (t < len) { fallen.Add((torn, g.Get(torn))); g.Set(torn, Block.Air); }
                // The slab bends down from the hinge a metre a metre, then lies flat on the floor below.
                int top = Math.Max(yF - t + 1, yL + 1);
                if (t == len) continue;
                for (int y = yL + 1; y < top; y++) g.Set(At(t, w, y), Cube());
                g.Set(At(t, w, top), top > yL + 1 || t == 1 ? new Block(BlockType.Ramp, slab.Material, slab.Color.Scale(0.95f), Role.Floor, up) : Cube());
            }
    }

    /// <summary>A hole through the roof and the ceiling under it, away from the eaves.</summary>
    static void RoofHole(PoiInstance inst, PlainBox b, Rng rng, List<(Int3, Block)> fallen)
    {
        int w = b.Max.X - b.Min.X, d = b.Max.Z - b.Min.Z;
        float cx = b.Min.X + w * rng.Float(0.25f, 0.75f), cz = b.Min.Z + d * rng.Float(0.25f, 0.75f);
        Cut(inst, b, cx, cz, rng.Float(2.6f, 3.2f) + w * rng.Float(0.08f, 0.2f), rng.Float(2.6f, 3.2f) + d * rng.Float(0.08f, 0.2f), b.Eave, fallen);
    }

    /// <summary>A stretch of wall or a corner gone, with the roof above it; mostly at the back or the side, so the front still tells you what it was.</summary>
    static void Breach(PoiInstance inst, PlainBox b, Rng rng, List<(Int3, Block)> fallen)
    {
        Int3 toFront = b.Front.Vec(), toSide = new(-toFront.Z, 0, toFront.X);
        float mx = (b.Min.X + b.Max.X) * 0.5f, mz = (b.Min.Z + b.Max.Z) * 0.5f, hx = (b.Max.X - b.Min.X) * 0.5f, hz = (b.Max.Z - b.Min.Z) * 0.5f;
        float halfFront = Math.Abs(toFront.X) * hx + Math.Abs(toFront.Z) * hz, halfSide = Math.Abs(toSide.X) * hx + Math.Abs(toSide.Z) * hz;
        float along = rng.Float(-1f, 1f), out_ = rng.Chance(0.75f) ? -1f : rng.Float(-0.5f, 0.6f);
        if (rng.Chance(0.3f)) along = rng.Chance(0.5f) ? 1f : -1f;
        float cx = mx + toFront.X * out_ * halfFront + toSide.X * along * halfSide, cz = mz + toFront.Z * out_ * halfFront + toSide.Z * along * halfSide;
        // Upper storeys fall first; a ground floor keeps its sills.
        int low = b.Eave > 6 ? 5 : rng.Between(2, 3);
        float big = MathF.Min(b.Max.X - b.Min.X, b.Max.Z - b.Min.Z) * 0.14f;
        Cut(inst, b, cx, cz, rng.Float(4f, 7f) + big, rng.Float(4f, 7f) + big, low, fallen);
    }

    /// <summary>Takes everything above a smooth bowl: <paramref name="low"/> at its centre, rising to clear the roof at its rim, ragged by a metre or two a column.</summary>
    static void Cut(PoiInstance inst, PlainBox b, float cx, float cz, float rx, float rz, int low, List<(Int3, Block)> fallen)
    {
        var g = inst.Blocks;
        for (int z = Math.Max(b.Min.Z, (int)(cz - rz) - 1); z <= Math.Min(b.Max.Z, (int)(cz + rz) + 1); z++)
            for (int x = Math.Max(b.Min.X, (int)(cx - rx) - 1); x <= Math.Min(b.Max.X, (int)(cx + rx) + 1); x++)
            {
                // On the 2 m lattice: a hole's edge is jagged a whole block at a time, never pinholes a metre wide.
                int bx = x >> 1, bz = z >> 1;
                float dx = (bx * 2 + 1 - cx) / rx, dz = (bz * 2 + 1 - cz) / rz, d = dx * dx + dz * dz;
                if (d >= 1f) continue;
                int keep = Math.Max(1, low + (int)(d * d * 16f) + 2 * (int)(Hash.Cell(new Int3(bx, 0, bz), 91) % 2));
                keep += keep & 1;
                for (int y = keep; y <= b.Max.Y + 2; y++)
                {
                    var p = new Int3(x, y, z);
                    Block s = g.Get(p);
                    if (s.IsAir || inst.Extras.Lasting.Contains(p) || s.Role is Role.Loot or Role.Debris || inst.Protected[p]) continue;
                    g.Set(p, Block.Air);
                    fallen.Add((p, s));
                }
            }
    }

    /// <summary>What lies or stands loose: it rests on what is under it, and nothing else holds it up.</summary>
    static bool Loose(Block b) => b.Role is not (Role.Loot or Role.Core or Role.Terminal or Role.Growth or Role.Decor) && b.Type is
        BlockType.Crate or BlockType.Tank or BlockType.Rubble or BlockType.Plant or BlockType.Ash or BlockType.Scatter or BlockType.Scorch or BlockType.Puddle
        or BlockType.Remains or BlockType.Barricade or BlockType.Bench or BlockType.Table or BlockType.Meal or BlockType.Bunk or BlockType.Bed or BlockType.Statue;

    /// <summary>
    /// The last word on what decay left hanging: drums, crates, rubble and weeds rest on what is under
    /// them, however well they touch a wall beside them. Whatever lost its floor drops to the next one
    /// down, or goes if something already stands there.
    /// </summary>
    public static void Rest(PoiInstance inst)
    {
        if (inst.Layout.Extras.InSpace) return;
        var g = inst.Blocks;
        var nav = inst.Nav();
        for (int pass = 0; pass < 3; pass++)
        {
            bool moved = false;
            foreach (var (p, b) in g.Blocks().ToList())
            {
                if (!Loose(b) || nav.Solid(p + Int3.Down) || g.Get(p + Int3.Up).Role is Role.Loot or Role.Core or Role.Terminal) continue;
                Int3 c = p;
                while (c.Y > g.Min.Y && g.Get(c + Int3.Down).IsAir && !nav.IsRock(c + Int3.Down)) c += Int3.Down;
                g.Set(p, Block.Air);
                if (g.Get(c).IsAir && nav.Solid(c + Int3.Down) && !inst.Protected[c]) g.Set(c, b);
                moved = true;
            }
            // A heap that came away may have been all that joined a bit of wall to the rest: that comes down too.
            foreach (Int3 p in Structure.Floating(g, inst.Layout.Occupied, null, inst.Extras.Plain.Count > 0))
            {
                if (g.Get(p).Role is not (Role.Loot or Role.Core or Role.Terminal))
                {
                    g.Set(p, Block.Air);
                    continue;
                }
                // A cache is never lost to it: it sits on the heap that came down under it.
                for (Int3 c = p + Int3.Down; c.Y >= g.Min.Y && g.Get(c).IsAir && !nav.IsRock(c); c += Int3.Down)
                    g.Set(c, new Block(BlockType.Cube, Material.Rock, new Rgb(0x6a, 0x60, 0x58), Role.Debris));
            }
            if (!moved) break;
        }
    }

    /// <summary>
    /// A place's outside on the real ground: a pad is level only out to its apron, and past that the land
    /// goes its own way. What stands off the place's footprint (litter, thrown plates, a fence, a yard's
    /// paving) follows the ground under it, column by column, sunk in rather than hanging over it, so a slab
    /// bends with the land a metre at a time. <paramref name="ground"/> is the height above the pad at a
    /// cell's middle; <paramref name="apron"/> how far round the footprint it can be left out, or negative
    /// where a neighbour's pad may blend over this one. Returns how many blocks moved.
    /// </summary>
    public static int Settle(PoiInstance inst, Func<int, int, float> ground, int apron)
    {
        var g = inst.Blocks;
        CellMask occ = inst.Layout.Occupied;
        Int3 lo = occ.Min, hi = occ.Min + occ.Size;
        // On the pad and its apron the ground is the pad, and what is built there keeps to it. Loose litter
        // (weeds, debris, props) goes by the ground wherever it lies: a lot's yard between its buildings, or a
        // pad's blended edge, is the land as it is, and on the level pad it stays put.
        bool InBounds(int x, int z) => apron >= 0 && x >= lo.X - apron && x < hi.X + apron && z >= lo.Z - apron && z < hi.Z + apron;
        var columns = new Dictionary<(int X, int Z), List<(Int3 Cell, Block Block)>>();
        foreach (var (p, b) in g.Blocks())
        {
            if (p.Y < Assembler.GroundY) continue;
            if (!columns.TryGetValue((p.X, p.Z), out var list)) columns[(p.X, p.Z)] = list = new List<(Int3, Block)>();
            list.Add((p, b));
        }
        int moved = 0;
        foreach (var (key, cells) in columns)
        {
            // Only the stack that meets the ground: an eave overhead keeps to its building.
            if (cells[0].Cell.Y > 0) continue;
            int count = 1;
            while (count < cells.Count && cells[count].Cell.Y == cells[0].Cell.Y + count) count++;
            var list = cells.GetRange(0, count);
            if (list.Any(c => occ[c.Cell] || c.Block.Role is Role.Loot or Role.Core or Role.Terminal or Role.Door)) continue;
            if (InBounds(key.X, key.Z) && !list.All(c => c.Block.Role is Role.Debris or Role.Prop && !BlockInfo.IsFullCube(c.Block.Type))) continue;
            int dy = (int)MathF.Floor(ground(key.X, key.Z) + 0.02f);
            if (dy == 0) continue;
            foreach (var (p, _) in list) g.Set(p, Block.Air);
            foreach (var (p, b) in list)
            {
                moved++;
                if (g.InBounds(p + Int3.Up * dy)) g.Set(p + Int3.Up * dy, b);
            }
        }
        return moved;
    }

    /// <summary>
    /// The land coming back, after everything else, on what is left: weeds on floors open to the sky and
    /// along the foot of the walls, moss in patches on what faces up. Every weed stands on something solid.
    /// </summary>
    public static void Overgrow(PoiInstance inst, Rng rng)
    {
        var g = inst.Blocks;
        var nav = inst.Nav();
        Rgb Weed() => Moss.Lerp(new Rgb(0x76, 0x7e, 0x46), rng.Float());
        bool Firm(Int3 c) => g.Get(c).IsAir && nav.Solid(c + Int3.Down) && (BlockInfo.Supports(g.Get(c + Int3.Down).Type) || g.Get(c + Int3.Down).IsAir);
        foreach (var m in inst.Layout.Modules.Where(m => m.Kind != ModuleKind.Outdoor))
            foreach (Int3 c in m.Cells.Where(c => c.Y == m.Min.Y))
                if (Firm(c) && OpenToSky(g, c) && rng.Chance(0.06f + 0.36f * inst.Ruin.Wear)) Put(inst, c, BlockType.Plant, Material.Plant, Weed(), Role.Debris);
        foreach (var (p, b) in g.Blocks().Where(t => t.Cell.Y == 0 && t.Block.Role is Role.Wall or Role.Floor).ToList())
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 c = p + d.Vec();
                if (Firm(c) && !inst.Protected[c] && Hash.Cell(new Int3(c.X / 3, 0, c.Z / 3), 97) % 100 < 45 && rng.Chance(0.08f + 0.45f * inst.Ruin.Wear)) Put(inst, c, BlockType.Plant, Material.Plant, Weed(), Role.Debris);
            }
        foreach (var (p, _) in g.Blocks())
        {
            ref Block b = ref g.At(p);
            if (b.IsAir || b.Role is not (Role.Roof or Role.Wall) || !g.Get(p + Int3.Up).IsAir) continue;
            if (Hash.Cell(new Int3(p.X / 4, 0, p.Z / 4), 99) % 100 < 45 * inst.Ruin.Wear) b.Color = b.Color.Lerp(Moss, 0.32f);
        }
    }
}
