#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What makes a broken house read as one, over <see cref="StateTransforms.Weathered"/>: wall tops fall away along
/// cracks (a drop of at most a block between neighbours, never a tooth or a pinhole), the slabs round a hole sag toward
/// it, and the years stain what is left by the world's wet or dry (streaks under sills and eaves, moss and damp,
/// or dust) and leave a few readable remains by what the building was.
/// </summary>
public static partial class StateTransforms
{
    static readonly Rgb Dust = new(0xb4, 0xa4, 0x86), Streak = new(0x3c, 0x34, 0x2c);

    /// <summary>Wall tops fall along cracks: no exposed wall stands more than a block over a neighbour along its line. What falls joins <paramref name="fallen"/>.</summary>
    static void Crack(PoiInstance inst, List<(Int3, Block)> fallen)
    {
        var g = inst.Blocks;
        var top = new Dictionary<(int X, int Z), int>();
        foreach (var (p, b) in g.Blocks())
            if (b.Role is Role.Wall or Role.Window && p.Y >= 1 && g.Get(p + Int3.Up).IsAir) top[(p.X, p.Z)] = p.Y;
        // Walls only ever shed from the top: a few sweeps settle a long run.
        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            foreach (var ((x, z), y) in top.ToList())
            {
                int low = y;
                foreach (Dir d in Dirs.Horizontal)
                {
                    Int3 n = new(x + d.Vec().X, y, z + d.Vec().Z);
                    if (top.TryGetValue((n.X, n.Z), out int ny)) low = Math.Min(low, ny);
                    else if (g.Get(n).IsAir && g.Get(n + Int3.Down).Role is Role.Wall or Role.Window) low = Math.Min(low, FootOf(g, n));
                }
                if (y - low <= 1) continue;
                for (int cy = y; cy > low + 1; cy--)
                {
                    var p = new Int3(x, cy, z);
                    Block s = g.Get(p);
                    if (s.Role is not (Role.Wall or Role.Window) || inst.Protected[p] || inst.Extras.Lasting.Contains(p)) break;
                    g.Set(p, Block.Air);
                    fallen.Add((p, s));
                    top[(x, z)] = cy - 1;
                    moved = true;
                }
            }
            if (!moved) break;
        }
    }

    /// <summary>
    /// A stretch of an outer wall broken down in a V: the wall's top falls away along 45 degree cracks from a low point, and the roof
    /// and ceiling go with it a little way in (never the ground floor's sills on a house of one storey: the break starts above them).
    /// What falls joins <paramref name="fallen"/>, to lie at its foot.
    /// </summary>
    static void Notch(PoiInstance inst, PlainBox b, Rng rng, List<(Int3, Block)> fallen)
    {
        var g = inst.Blocks;
        Int3 o = (rng.Chance(0.35f) ? b.Front : Dirs.Horizontal[rng.Range(0, 4)]).Vec();
        bool alongX = o.X == 0;
        int lo = alongX ? b.Min.X : b.Min.Z, hi = alongX ? b.Max.X : b.Max.Z, edge = o.X + o.Z > 0 ? (alongX ? b.Max.Z : b.Max.X) : (alongX ? b.Min.Z : b.Min.X);
        if (hi - lo < 8) return;
        int c = rng.Range(lo + 3, hi - 2), half = rng.Range(3, Math.Min(7, (hi - lo) / 2)), floor = b.Eave > 6 ? 5 : 2;
        int low = rng.Range(floor, Math.Max(floor + 1, b.Eave - 1));
        for (int a = c - half; a <= c + half; a++)
        {
            int cut = low + Math.Abs(a - c) + (int)(Hash.Cell(new Int3(a >> 1, 0, edge), 17) % 2);
            for (int depth = 0; depth < 4; depth++)
                for (int y = cut; y <= b.Max.Y + 2; y++)
                {
                    var p = alongX ? new Int3(a, y, edge - o.Z * depth) : new Int3(edge - o.X * depth, y, a);
                    Block s = g.Get(p);
                    if (s.IsAir || inst.Protected[p] || inst.Extras.Lasting.Contains(p) || s.Role is Role.Loot or Role.Core or Role.Terminal or Role.Debris) continue;
                    if (depth >= 2 && s.Role is not (Role.Roof or Role.Floor)) continue;
                    g.Set(p, Block.Air);
                    fallen.Add((p, s));
                }
        }
    }

    /// <summary>Joists show where a roof or ceiling is gone: a timber beam across each second line of an opening, resting on the slab or wall at both ends.</summary>
    static void Joists(PoiInstance inst, Rng rng)
    {
        var g = inst.Blocks;
        var wood = new Rgb(0x4a, 0x3a, 0x2a);
        foreach (var (p, b) in g.Blocks().ToList())
        {
            if (b.Role is not (Role.Roof or Role.Floor) || !BlockInfo.IsFullCube(b.Type) || p.Y < 3) continue;
            foreach (Dir d in new[] { Dir.East, Dir.South })
            {
                Int3 v = d.Vec();
                if ((d == Dir.East ? p.Z : p.X) % 2 != 0 || !g.Get(p + v).IsAir) continue;
                int k = 1;
                while (k <= 9 && g.Get(p + v * k).IsAir && g.Get(p + v * k + Int3.Up).IsAir && g.Get(p + v * k + Int3.Down * 2).IsAir) k++;
                if (k < 3 || k > 9 || !BlockInfo.IsFullCube(g.Get(p + v * k).Type) || g.Get(p + v * k).Role is not (Role.Roof or Role.Floor or Role.Wall) || !rng.Chance(0.7f)) continue;
                for (int j = 1; j < k; j++) g.Set(p + v * j, new Block(BlockType.Beam, Material.Timber, wood.Lerp(new Rgb(0x6a, 0x5a, 0x48), rng.Float()), Role.Roof, d));
            }
        }
    }

    /// <summary>The y of the top of the wall run under an air cell (a gap beside a wall top counts as a drop to where the wall stops).</summary>
    static int FootOf(BlockGrid g, Int3 air)
    {
        int y = air.Y;
        while (y > 0 && g.Get(air with { Y = y - 1 }).IsAir) y--;
        return y;
    }

    /// <summary>Slabs at the edge of a hole slump toward it: a roof or floor block with open air beside it and none above becomes a slope rising away from the break.</summary>
    static void Sag(PoiInstance inst, Rng rng)
    {
        var g = inst.Blocks;
        float chance = 0.25f + 0.55f * inst.Ruin.Wear;
        foreach (var (p, b) in g.Blocks().ToList())
        {
            if (b.Role is not (Role.Roof or Role.Floor) || b.Type != BlockType.Cube || p.Y < 2 || !g.Get(p + Int3.Up).IsAir || inst.Protected[p] || inst.Extras.Lasting.Contains(p)) continue;
            Dir? toHole = null;
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 n = p + d.Vec();
                if (g.Get(n).IsAir && g.Get(n + Int3.Down).IsAir && inst.Layout.Occupied[n] && g.Get(p + d.Opposite().Vec()).Role is Role.Roof or Role.Floor) toHole = d;
            }
            if (toHole is not Dir hole || !rng.Chance(chance)) continue;
            g.Set(p, new Block(BlockType.Ramp, b.Material, b.Color.Scale(0.94f), b.Role, hole.Opposite()));
        }
    }

    /// <summary>Streaks under what lets the weather in, by how wet the world is: damp and moss on a wet one, dust on a dry one, grime between.</summary>
    static void Stains(PoiInstance inst)
    {
        var g = inst.Blocks;
        float wet = inst.Ruin.Water, wear = inst.Ruin.Wear;
        foreach (var (p, b) in g.Blocks().ToList())
        {
            // A streak starts under a sill, a break or an eave and runs down the wall, thinning.
            if (b.Role is not Role.Wall || !BlockInfo.IsFullCube(b.Type) || BlockInfo.IsTransparent(b.Material) || b.Material is Material.Lumen or Material.Glyph) continue;
            Int3 up = p + Int3.Up;
            Block above = g.Get(up);
            if (!(above.IsAir || above.Role is Role.Window)) continue;
            uint h = Hash.Cell(new Int3(p.X, 0, p.Z), 311 + (uint)inst.SpawnSeed % 5);
            if (h % 100 >= 90 * wear) continue;
            Rgb tint = wet > 0.6f ? Mould : wet < 0.25f ? Dust : Streak;
            for (int k = 0; k < 3 + (int)(h % 3); k++)
            {
                Int3 q = p + Int3.Down * k;
                ref Block s = ref g.At(q);
                if (s.IsAir || s.Role is not Role.Wall || !BlockInfo.IsFullCube(s.Type) || BlockInfo.IsTransparent(s.Material)) break;
                s.Color = s.Color.Lerp(tint, (0.7f - 0.14f * k) * Math.Clamp(0.5f + wear, 0f, 1f));
            }
        }
    }

    /// <summary>A few things left on a ground floor by what the building was, each resting on the floor and never in a way through.</summary>
    static void Remains(PoiInstance inst, Rng rng)
    {
        var g = inst.Blocks;
        var tags = inst.Layout.Family.Tags;
        BlockType[] left = tags.Contains("shop") ? new[] { BlockType.Table, BlockType.Crate, BlockType.Crate }
            : tags.Contains("house") ? new[] { BlockType.Bunk, BlockType.Table }
            : tags.Contains("wayside") ? new[] { BlockType.Tank, BlockType.Crate }
            : tags.Contains("farmstead") || tags.Contains("shed") ? new[] { BlockType.Crate, BlockType.Crate }
            : Array.Empty<BlockType>();
        if (left.Length == 0) return;
        var way = new HashSet<Int3>(inst.Layout.Doorways.SelectMany(d => d.Cells).Concat(inst.Layout.ExitCells));
        var nav = inst.Nav();
        var spots = new List<Int3>();
        foreach (PlacedModule m in inst.Layout.Modules.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall))
            foreach (Int3 c in m.Cells.Where(c => c.Y == m.Min.Y && g.Get(c).IsAir && g.Get(c + Int3.Up).IsAir && !inst.Protected[c] && nav.Solid(c + Int3.Down) && !way.Contains(c)))
                if (Dirs.Horizontal.Any(d => g.Get(c + d.Vec()).Role is Role.Wall) && Dirs.Horizontal.All(d => !way.Contains(c + d.Vec())) && OpenToSky(g, c) == false) spots.Add(c);
        rng.Shuffle(spots);
        for (int i = 0; i < Math.Min(left.Length, spots.Count); i++)
        {
            Rgb c = left[i] == BlockType.Tank ? Debris.Rust.Scale(0.8f) : new Rgb(0x5a, 0x4a, 0x38).Lerp(Dust, 0.3f * inst.Ruin.Wear);
            g.Set(spots[i], new Block(left[i], left[i] == BlockType.Tank ? Material.Steel : Material.Timber, c, Role.Prop, Dirs.Horizontal[rng.Range(0, 4)]));
        }
    }
}
