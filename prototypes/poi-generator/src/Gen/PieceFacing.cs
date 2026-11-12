#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Every directional piece takes its facing from what it sits in, settled once when the place is spawned, after
/// everything that sets pieces down (the builders, the fixtures, the occupants, the dressing): whatever facing a
/// builder gave it, a piece ends up turned the one way its spot says. Panes have their own pass
/// (<see cref="PaneFacing"/>); doors face across their doorway as the builders lay them (<see cref="PlaceAudit"/>
/// counts both).
/// <list type="bullet">
/// <item>A console or a locker backs onto a wall and looks into the room; free-standing, it never faces into a wall.</item>
/// <item>A ladder's back is on its wall, the whole run of it one way.</item>
/// <item>A sign's plate lies flat on the wall behind it.</item>
/// <item>A bed's head is to the wall, never its foot.</item>
/// </list>
/// </summary>
public static class PieceFacing
{
    /// <summary>Turns every directional piece of a grid to its spot; returns how many it turned.</summary>
    public static int Apply(BlockGrid g, CellMask occupied, bool space = false)
    {
        int turned = 0;
        bool Wall(BlockGrid g, CellMask occupied, Int3 p) => PieceFacing.Wall(g, occupied, p, space);
        var ladders = new List<Int3>();
        foreach (var (p, b) in g.Blocks().ToList())
        {
            if (!b.Facing.IsHorizontal()) continue;
            Dir f = b.Facing, want = f;
            switch (b.Type)
            {
                case BlockType.Terminal or BlockType.Locker:
                    if (Wall(g, occupied, p - f.Vec()) && !Wall(g, occupied, p + f.Vec())) break;
                    // Back to a wall, front open; failing that, at least not facing a wall.
                    want = Pick(f, d => Wall(g, occupied, p - d.Vec()) && !Wall(g, occupied, p + d.Vec()))
                        ?? (Wall(g, occupied, p + f.Vec()) ? Pick(f, d => !Wall(g, occupied, p + d.Vec())) ?? f : f);
                    break;
                case BlockType.Sign:
                    if (Wall(g, occupied, p - f.Vec())) break;
                    want = Pick(f, d => Wall(g, occupied, p - d.Vec())) ?? f;
                    break;
                case BlockType.Bed:
                    if (Wall(g, occupied, p - f.Vec()) && !Wall(g, occupied, p + f.Vec()) && g.Get(p + f.Vec()).Type != BlockType.Bed) want = f.Opposite();
                    break;
                case BlockType.Ladder:
                    if (g.Get(p + Int3.Down).Type != BlockType.Ladder) ladders.Add(p); // the foot of a run
                    break;
            }
            if (want != f)
            {
                g.At(p).Facing = want;
                turned++;
            }
        }
        // A ladder run faces as most of it can: the way with the most wall at its back, its own way on a tie.
        foreach (Int3 foot in ladders)
        {
            var run = new List<Int3>();
            for (Int3 q = foot; g.Get(q).Type == BlockType.Ladder; q += Int3.Up) run.Add(q);
            Dir f = g.Get(foot).Facing;
            Dir best = f;
            int bestCount = run.Count(q => Wall(g, occupied, q - f.Vec()));
            foreach (Dir d in Dirs.Horizontal)
            {
                int n = run.Count(q => Wall(g, occupied, q - d.Vec()) && !Wall(g, occupied, q + d.Vec()));
                if (n > bestCount) { best = d; bestCount = n; }
            }
            if (best == f) continue;
            foreach (Int3 q in run) g.At(q).Facing = best;
            turned += run.Count;
        }
        return turned;
    }

    /// <summary>The first way that suits, its own way first, then the opposite, then the sides.</summary>
    static Dir? Pick(Dir f, System.Func<Dir, bool> ok)
    {
        foreach (Dir d in new[] { f, f.Opposite(), f.RotateCw(1), f.RotateCw(3) })
            if (ok(d)) return d;
        return null;
    }

    /// <summary>What a piece can back onto: a full block, glass, a lattice, a sealed door, a post, or the rock.</summary>
    public static bool Wall(BlockGrid g, CellMask occupied, Int3 p, bool space = false)
    {
        Block b = g.Get(p);
        if (b.IsAir) return !space && p.Y <= Assembler.GroundY && !occupied[p];
        return BlockInfo.IsFullCube(b.Type) || b.Type is BlockType.Pane or BlockType.Lattice or BlockType.SealedDoor or BlockType.Pillar or BlockType.Rib;
    }
}
