#nullable enable
using System.Collections.Generic;

namespace PoiGenerator.Gen;

/// <summary>
/// A roof that climbs in courses is a pitch, and a pitch is slopes: wherever the outermost cube of a roof
/// course has the next course up one cell further in, it becomes a slope rising toward it. Whatever drew the
/// roof (a kit's module art, a cone of discs, a hand-built gable) ends with the player's own slopes on it,
/// and the course under each slope stays, so nothing is seen through.
/// </summary>
public static class RoofSlopes
{
    static readonly (Dir D, Int3 V)[] Ways = { (Dir.East, new Int3(1, 0, 0)), (Dir.West, new Int3(-1, 0, 0)), (Dir.South, new Int3(0, 0, 1)), (Dir.North, new Int3(0, 0, -1)) };

    public static int Smooth(BlockGrid g)
    {
        bool Roof(Int3 p) => g.Get(p) is { Type: BlockType.Cube, Role: Role.Roof };
        var turn = new List<(Int3 Cell, Dir Up)>();
        foreach (var (c, b) in g.Blocks())
        {
            if (b.Type != BlockType.Cube || b.Role != Role.Roof || c.Y < 0 || !g.Get(c + Int3.Up).IsAir) continue;
            foreach (var (d, v) in Ways)
                // The course's edge, open on the outside, the next course up just inside it.
                if (Roof(c + v + Int3.Up) && g.Get(c - v).IsAir && g.Get(c - v + Int3.Up).IsAir && !Roof(c - v + Int3.Down) || Roof(c + v + Int3.Up) && Roof(c - v + Int3.Down))
                {
                    turn.Add((c, d));
                    break;
                }
        }
        foreach (var (c, up) in turn)
        {
            Block b = g.Get(c);
            g.Set(c, new Block(BlockType.Ramp, b.Material, b.Color, Role.Roof, up));
        }
        return turn.Count;
    }
}
