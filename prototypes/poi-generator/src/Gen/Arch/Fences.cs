#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A perimeter fence, the one every place's fence line is laid by (an airfield's and a port's runs, a compound's, a works'):
/// posts every <see cref="Spacing"/> m with the panels between them, <see cref="Height"/> m high, chain-link on galvanised
/// posts or, where the builders worked in timber, posts and rails (drawn by <see cref="Shapes.IsFence"/>'s cells). A gate is a
/// gap with a post each side and its leaf slid open along the inside of the line. What the years do to it is
/// <see cref="Decay"/>: panels sag or lie flat beside their gap, posts lean, a run goes; never loose blocks.
/// </summary>
public static class Fences
{
    /// <summary>Between posts, m.</summary>
    public const int Spacing = 3;

    /// <summary>How high the fence stands, m (cells).</summary>
    public const int Height = 2;

    /// <summary>A place that is nothing but fence (a run of a perimeter): no loot, no litter, no doors.</summary>
    public static bool Only(BlockGrid g) => g.Blocks().Any(q => Shapes.IsFence(q.Block)) && g.Blocks().All(q => Shapes.IsFence(q.Block));

    public static Block Cell(bool timber, Rgb color, Dir outward, byte flags) =>
        new(BlockType.Rail, timber ? Material.Timber : Material.Steel, color, Role.Wall, outward, (byte)(Shapes.FenceBit | (timber ? Shapes.FenceTimber : 0) | flags));

    /// <summary>
    /// A straight run of <paramref name="len"/> cells along a line, through <paramref name="put"/>(along, up, in): along the line
    /// from its start, up from the ground, in from the line toward what it fences (0 the line itself, 1 the cell inside).
    /// Gates are gaps <paramref name="gateHalf"/> cells either side of each centre, a post each side, the leaf slid open
    /// inside the line beside it (on whichever side the run has room for it).
    /// </summary>
    public static void Line(Action<int, int, int, Block> put, int len, Dir outward, bool timber, Rgb color, IReadOnlyCollection<int> gates, int gateHalf)
    {
        bool Gap(int a) => gates.Any(g => Math.Abs(a - g) <= gateHalf);
        for (int a = 0; a < len; a++)
        {
            if (Gap(a)) continue;
            bool post = a % Spacing == 0 || a == len - 1 || Gap(a - 1) || Gap(a + 1);
            for (int y = 0; y < Height; y++) put(a, y, 0, Cell(timber, color, outward, post ? Shapes.FencePost : (byte)0));
        }
        foreach (int g in gates)
        {
            int leaf = 2 * gateHalf + 1, from = g + gateHalf + 2;
            if (from + leaf > len) from = g - gateHalf - 1 - leaf;
            if (from < 0) continue;
            for (int a = from; a < from + leaf; a++)
                for (int y = 0; y < Height; y++)
                    put(a, y, 1, Cell(timber, color, outward, (byte)(Shapes.FenceLeaf | (a == from || a == from + leaf - 1 ? Shapes.FencePost : 0))));
        }
    }

    /// <summary>
    /// The years on a place's fences, <paramref name="share"/> of the bays touched: a bay's panel sags out on its posts, or is
    /// gone and lies flat on the ground inside its gap; a post leans; in a ruin now and then a whole run of bays is down, the
    /// panels lying along it. Bays are the panels between two posts along a line; a gate's leaf, once fallen, lies flat where
    /// it stood.
    /// </summary>
    public static void Decay(PoiInstance inst, Rng rng, float share, bool ruined)
    {
        BlockGrid g = inst.Blocks;
        var feet = g.Blocks().Where(q => Shapes.IsFence(q.Block) && !Shapes.IsFence(g.Get(q.Cell + Int3.Down)) && (q.Block.Variant & Shapes.FenceDown) == 0).Select(q => q.Cell).ToList();
        if (feet.Count == 0) return;
        var done = new HashSet<Int3>();
        void Flag(Int3 foot, byte flag)
        {
            for (Int3 c = foot; Shapes.IsFence(g.Get(c)); c += Int3.Up) g.At(c).Variant |= flag;
        }
        // Down: the column goes, and its panel lies flat inside the line where it stood (or outside, if that is taken).
        void Fell(Int3 foot)
        {
            Block b = g.Get(foot);
            for (Int3 c = foot; Shapes.IsFence(g.Get(c)); c += Int3.Up) g.Set(c, Block.Air);
            if ((b.Variant & Shapes.FencePost) != 0 && (b.Variant & Shapes.FenceLeaf) == 0) return; // (a post that went leaves no panel)
            // (A gate's leaf, off its track, lies where it stood.)
            Int3 inward = foot - b.Facing.Vec();
            foreach (Int3 at in (b.Variant & Shapes.FenceLeaf) != 0 ? new[] { foot } : new[] { inward, foot + b.Facing.Vec() })
                if (g.InBounds(at) && g.Get(at).IsAir && !inst.Protected[at])
                {
                    g.Set(at, b with { Variant = (byte)((b.Variant & (Shapes.FenceBit | Shapes.FenceTimber)) | Shapes.FenceDown) });
                    return;
                }
        }
        foreach (Int3 foot in feet.OrderBy(c => c.X).ThenBy(c => c.Z))
        {
            Block b = g.Get(foot);
            if (!done.Add(foot) || (b.Variant & Shapes.FencePost) == 0) continue;
            // The bay on from this post along its line, to the next post.
            Int3 along = (b.Facing is Dir.North or Dir.South ? Dir.East : Dir.South).Vec();
            bool leaf = (b.Variant & Shapes.FenceLeaf) != 0;
            bool Standing(Block n) => Shapes.IsFence(n) && (n.Variant & Shapes.FenceDown) == 0 && (n.Variant & Shapes.FenceLeaf) == (b.Variant & Shapes.FenceLeaf);
            var bay = new List<Int3>();
            for (Int3 c = foot + along; Standing(g.Get(c)) && (leaf || bay.Count < Spacing && (g.Get(c).Variant & Shapes.FencePost) == 0); c += along) bay.Add(c);
            if (leaf)
            {
                // A gate's leaf (the whole of it, to its far post) stays slid open, or has come off its track and lies where it stood.
                if (ruined && rng.Chance(share * 2f)) { Fell(foot); foreach (Int3 c in bay) { done.Add(c); Fell(c); } }
                else done.UnionWith(bay);
                continue;
            }
            float r = rng.Float();
            if (ruined && r < share * 0.12f)
            {
                // A run down: this bay and the next few, posts and all.
                Int3 c = foot;
                for (int n = 0, end = Spacing * rng.Between(2, 4); n <= end && Shapes.IsFence(g.Get(c)); n++, c += along) { done.Add(c); Fell(c); }
                continue;
            }
            if (r < share * 0.45f) { foreach (Int3 c in bay) Fell(c); continue; }
            if (r < share) foreach (Int3 c in bay) Flag(c, Shapes.FenceSag);
            if (rng.Chance(share * 0.4f)) Flag(foot, Shapes.FenceLean);
        }
    }
}
