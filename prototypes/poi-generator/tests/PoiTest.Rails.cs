using System;
using System.Collections.Generic;
using System.Linq;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// Railings, fences and bars run along their line: a rail reaches toward the rails beside it (and on into a
    /// wall at a line's end), turns a corner where two lines meet, and never sticks out sideways; one on its own
    /// runs across its Facing.
    /// </summary>
    void Rails()
    {
        // Which sides of its cell a rail's boxes reach.
        static HashSet<Dir> Reach(List<ShapeBox> shape)
        {
            var r = new HashSet<Dir>();
            foreach (ShapeBox b in shape)
            {
                if (b.Lo.X <= 0.001f) r.Add(Dir.West);
                if (b.Hi.X >= 0.999f) r.Add(Dir.East);
                if (b.Lo.Z <= 0.001f) r.Add(Dir.North);
                if (b.Hi.Z >= 0.999f) r.Add(Dir.South);
            }
            return r;
        }
        string Says(HashSet<Dir> r) => r.Count == 0 ? "nowhere" : string.Join(" ", r.OrderBy(d => d));

        var g = new BlockGrid(new Int3(0, 0, 0), new Int3(16, 3, 16));
        Block rail = new(BlockType.Rail, Material.Steel, new Rgb(90, 90, 90), Role.Decor);
        for (int x = 1; x <= 6; x++) g.Set(new Int3(x, 0, 2), rail);
        for (int z = 3; z <= 6; z++) g.Set(new Int3(6, 0, z), rail);
        g.Set(new Int3(10, 0, 10), rail with { Facing = Dir.East });
        g.Set(new Int3(13, 0, 13), rail with { Facing = Dir.North });
        // A short run between two walls, and bars standing alone.
        g.Set(new Int3(1, 0, 12), new Block(BlockType.Cube, Material.Concrete, new Rgb(120, 120, 120), Role.Wall));
        g.Set(new Int3(4, 0, 12), new Block(BlockType.Cube, Material.Concrete, new Rgb(120, 120, 120), Role.Wall));
        g.Set(new Int3(2, 0, 12), rail);
        g.Set(new Int3(3, 0, 12), rail);
        var bars = new Block(BlockType.Rail, Material.Steel, new Rgb(90, 90, 90), Role.Wall);
        g.Set(new Int3(8, 0, 14), bars);
        g.Set(new Int3(8, 1, 14), bars);

        void Expect(Int3 c, string what, params Dir[] sides)
        {
            HashSet<Dir> got = Reach(Shapes.For(g.Get(c), c, g));
            Check(got.SetEquals(sides), $"a rail {what} reaches {Says(new HashSet<Dir>(sides))} ({Says(got)})");
        }
        Expect(new Int3(3, 0, 2), "in the middle of a line along x", Dir.East, Dir.West);
        Expect(new Int3(1, 0, 2), "at the open end of a line", Dir.East);
        Expect(new Int3(6, 0, 2), "at a corner", Dir.West, Dir.South);
        Expect(new Int3(6, 0, 4), "in a line along z", Dir.North, Dir.South);
        Expect(new Int3(10, 0, 10), "alone, facing east", Dir.North, Dir.South);
        Expect(new Int3(13, 0, 13), "alone, facing north", Dir.East, Dir.West);
        Expect(new Int3(2, 0, 12), "at a line's end against a wall", Dir.West, Dir.East);
        var barShape = Shapes.For(bars, new Int3(8, 0, 14), g);
        Check(barShape.All(b => b.Hi.Z - b.Lo.Z <= 0.1f) && barShape.Count(b => b.Hi.Y - b.Lo.Y > 0.8f) >= 6, $"bars on their own stand in a row across their Facing ({barShape.Count} pieces)");

        // Every rail in fresh city lots and works: nothing reaches toward a side with no rail, wall or post on it.
        var rand = new Random();
        int rails = 0, stray = 0;
        string first = "";
        foreach (FamilyDef f in _lib.Families.Where(f => f.Id.StartsWith("city_") || f.Id is "sewage_works" or "old_farm").OrderBy(_ => rand.Next()).Take(8))
        {
            PoiInstance inst;
            try { inst = PoiFactory.Generate(_lib, f, rand.Next(f.Variants.Count), f.Rarity, PoiState.Abandoned, (ulong)rand.NextInt64()); }
            catch (InvalidOperationException) { continue; }
            BlockGrid b = inst.Blocks;
            foreach (var (c, blk) in b.Blocks())
            {
                if (blk.Type != BlockType.Rail) continue;
                rails++;
                if (Shapes.RailRun(blk, c, b).Alone) continue;
                foreach (Dir d in Reach(Shapes.For(blk, c, b)))
                {
                    Int3 n = c + d.Vec();
                    bool joined = new[] { n, n + Int3.Up, n + Int3.Down }.Any(q => b.Get(q).Type == BlockType.Rail)
                        || BlockInfo.IsFullCube(b.Get(n).Type) || b.Get(n).Type is BlockType.Pillar or BlockType.Rib or BlockType.Lattice;
                    if (joined) continue;
                    stray++;
                    if (first.Length == 0) first = $"{f.Id} at {c} toward {d}";
                }
            }
        }
        Check(rails > 50 && stray == 0, $"of {rails} rails in fresh city lots and works, {stray} reach out sideways{(first.Length > 0 ? $" (first: {first})" : "")}");
    }
}
