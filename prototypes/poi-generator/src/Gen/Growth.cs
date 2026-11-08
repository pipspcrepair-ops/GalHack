using System;

namespace PoiGenerator.Gen;

/// <summary>
/// A place the land has come back through: the generator only says where something could root. What
/// grows there is the planet's own flora, planted by the game (PlanetSurface.World.RuinGrowth), so a
/// ruin's trees are the trees round it, and a world without trees grows none.
/// </summary>
public readonly record struct GrowthSpot(Int3 Cell);

/// <summary>What a root and a crown need of the blocks round them, and the world's colours on a place's loose rubble.</summary>
public static class Growth
{
    /// <summary>Still a place to root once the place has finished decaying: nothing solid here, firm under it, open to the sky.</summary>
    public static bool Rootable(PoiInstance inst, Int3 c)
    {
        BlockGrid g = inst.Blocks;
        if (BlockInfo.Collides(g.Get(c).Type)) return false;
        Block under = g.Get(c + Int3.Down);
        if (!under.IsAir && !BlockInfo.Supports(under.Type)) return false;
        for (int y = c.Y + 1; y <= g.Max.Y; y++)
            if (BlockInfo.Collides(g.Get(c with { Y = y }).Type)) return false;
        return true;
    }

    /// <summary>
    /// Whether a disc of air stands round a root over a band of heights (metres above the root's foot):
    /// no wall, roof or floor inside it. Anything thin (weeds, litter, a cable) a branch can grow through.
    /// </summary>
    public static bool ClearBand(BlockGrid g, Int3 root, float from, float to, float radius)
    {
        int y0 = root.Y + (int)MathF.Floor(from), y1 = root.Y + (int)MathF.Ceiling(to), r = (int)MathF.Ceiling(radius);
        float cx = root.X + 0.5f, cz = root.Z + 0.5f;
        for (int y = y0; y < y1; y++)
            for (int z = root.Z - r; z <= root.Z + r; z++)
                for (int x = root.X - r; x <= root.X + r; x++)
                {
                    // The block's nearest point to the axis.
                    float dx = MathF.Max(MathF.Max(x - cx, 0f), cx - x - 1f), dz = MathF.Max(MathF.Max(z - cz, 0f), cz - z - 1f);
                    if (dx * dx + dz * dz >= radius * radius) continue;
                    var c = new Int3(x, y, z);
                    if (g.InBounds(c) && BlockInfo.Collides(g.Get(c).Type)) return false;
                }
        return true;
    }

    /// <summary>
    /// The world's own colours on what lies loose in a place: rubble is the planet's rock with a little of
    /// what it broke off of, litter has faded, and weeds are the planet's own growth, or gone where nothing grows.
    /// </summary>
    public static void Earthen(PoiInstance inst, Rgb rock, Rgb? weed)
    {
        BlockGrid g = inst.Blocks;
        foreach (Int3 p in g.Cells())
        {
            ref Block b = ref g.At(p);
            if (b.Role != Role.Debris || b.Type is BlockType.Ash or BlockType.Scorch or BlockType.Puddle or BlockType.Rebar) continue;
            int l = (b.Color.R * 77 + b.Color.G * 150 + b.Color.B * 29) >> 8;
            var grey = new Rgb((byte)l, (byte)l, (byte)l);
            if (b.Type == BlockType.Plant)
            {
                if (weed is Rgb w) b.Color = w.Scale(0.82f + 0.08f * (Hash.Cell(p, 117) % 4));
                else b = Block.Air;
            }
            // What a building dropped keeps its own stuff's colour under the dust; loose stone is the planet's rock.
            else if (b.Type == BlockType.Rubble && b.Variant != Debris.Lumps) b.Color = b.Color.Lerp(grey, 0.2f).Lerp(rock, 0.1f);
            else if (b.Type == BlockType.Rubble || b.Material == Material.Rock) b.Color = rock.Scale(0.78f + 0.1f * (Hash.Cell(p, 113) % 4)).Lerp(grey, 0.22f);
            else b.Color = b.Color.Lerp(grey, 0.55f);
        }
    }
}
