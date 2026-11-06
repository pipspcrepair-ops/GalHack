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
        for (int k = 0; before - PoiFactory.CountStructure(g) < target && k < 24; k++)
        {
            PlainBox b = order[k % order.Count];
            if (walls && k % 2 == 0) Breach(inst, b, rng, fallen);
            else RoofHole(inst, b, rng, fallen);
        }
        Collapse(inst, rng);
        // Rubble lies where it fell: some of what came down, in its own colour, on what's below it.
        rng.Shuffle(fallen);
        foreach (var (c, b) in fallen.Take(Math.Min(70, fallen.Count / 6)))
        {
            Int3 at = c;
            while (at.Y > 0 && g.Get(at + Int3.Down).IsAir) at += Int3.Down;
            if (at.Y > 0 && !BlockInfo.Supports(g.Get(at + Int3.Down).Type)) continue;
            if (g.Get(at).Type is not (BlockType.Air or BlockType.Plate) || inst.Protected[at]) continue;
            Put(inst, at, BlockType.Rubble, Material.Rock, b.Color.Scale(0.8f), Role.Debris);
        }
        // The glass goes, a pane or a window at a time.
        foreach (var (p, b) in g.Blocks().ToList())
            if (b.Role == Role.Window && Hash.Cell(new Int3(p.X / 2, p.Y / 2, p.Z / 2), 93 + (uint)inst.SpawnSeed % 7) % 100 < 70) g.Set(p, Block.Air);
        // Boards rot where the rain has got to them, leaving the ground to the weeds.
        foreach (var (p, b) in g.Blocks().ToList())
            if (b.Type == BlockType.Plate && b.Role == Role.Floor && OpenToSky(g, p) && Hash.Cell(new Int3(p.X / 2, 0, p.Z / 2), 95) % 100 < 60) g.Set(p, Block.Air);
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
        Cut(inst, b, cx, cz, rng.Float(4f, 7f), rng.Float(4f, 7f), low, fallen);
    }

    /// <summary>Takes everything above a smooth bowl: <paramref name="low"/> at its centre, rising to clear the roof at its rim, ragged by a metre or two a column.</summary>
    static void Cut(PoiInstance inst, PlainBox b, float cx, float cz, float rx, float rz, int low, List<(Int3, Block)> fallen)
    {
        var g = inst.Blocks;
        for (int z = b.Min.Z; z <= b.Max.Z; z++)
            for (int x = b.Min.X; x <= b.Max.X; x++)
            {
                float dx = (x + 0.5f - cx) / rx, dz = (z + 0.5f - cz) / rz, d = dx * dx + dz * dz;
                if (d >= 1f) continue;
                int keep = Math.Max(1, low + (int)(d * d * 16f) + (int)(Hash.Cell(new Int3(x, 0, z), 91) % 3));
                for (int y = keep; y <= b.Max.Y + 2; y++)
                {
                    var p = new Int3(x, y, z);
                    Block s = g.Get(p);
                    if (s.IsAir || inst.Extras.Lasting.Contains(p) || s.Role is Role.Loot or Role.Debris) continue;
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
        foreach (var (p, b) in g.Blocks().ToList())
        {
            if (!Loose(b) || nav.Solid(p + Int3.Down)) continue;
            Int3 c = p;
            while (c.Y > g.Min.Y && g.Get(c + Int3.Down).IsAir && !nav.IsRock(c + Int3.Down)) c += Int3.Down;
            g.Set(p, Block.Air);
            if (g.Get(c).IsAir && nav.Solid(c + Int3.Down) && !inst.Protected[c]) g.Set(c, b);
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
        var columns = new Dictionary<(int X, int Z), List<(Int3 Cell, Block Block)>>();
        foreach (var (p, b) in g.Blocks())
        {
            if (p.Y < Assembler.GroundY) continue;
            // On the pad and its apron the ground is the pad.
            if (apron >= 0 && p.X >= lo.X - apron && p.X < hi.X + apron && p.Z >= lo.Z - apron && p.Z < hi.Z + apron) continue;
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
                if (Firm(c) && OpenToSky(g, c) && rng.Chance(0.3f)) Put(inst, c, BlockType.Plant, Material.Plant, Weed(), Role.Debris);
        foreach (var (p, b) in g.Blocks().Where(t => t.Cell.Y == 0 && t.Block.Role is Role.Wall or Role.Floor).ToList())
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 c = p + d.Vec();
                if (Firm(c) && !inst.Protected[c] && Hash.Cell(new Int3(c.X / 3, 0, c.Z / 3), 97) % 100 < 45 && rng.Chance(0.4f)) Put(inst, c, BlockType.Plant, Material.Plant, Weed(), Role.Debris);
            }
        foreach (Int3 p in g.Cells())
        {
            ref Block b = ref g.At(p);
            if (b.IsAir || b.Role is not (Role.Roof or Role.Wall) || !g.Get(p + Int3.Up).IsAir) continue;
            if (Hash.Cell(new Int3(p.X / 4, 0, p.Z / 4), 99) % 100 < 30) b.Color = b.Color.Lerp(Moss, 0.32f);
        }
    }
}
