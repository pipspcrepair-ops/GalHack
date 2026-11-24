#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What makes a place read as a building from a distance: a darker course where its walls meet the
/// ground, a coping along their tops, a frame round each door to the outside where the style calls for
/// one (in the building's own trim, never a hazard colour: a dangerous opening wears its stripes on its
/// own door), and no lamps showing through an outer wall as bright tiles. It only recolours what a kit
/// built, so any generator's walls get it, while yards, wrecks and containers keep their own colours.
/// A kit without a <c>facade</c> line is left alone.
/// </summary>
public static class Facade
{
    public static Rgb Pilaster(Rgb wall) => wall.Scale(0.82f);
    public static Rgb Armoured(Rgb wall) => wall.Lerp(new Rgb(0x2a, 0x30, 0x3a), 0.55f);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PoiLibrary, Dictionary<Rgb, Kit>> Owners = new();

    /// <summary>Which kit a wall colour came from, so a site of several styles gets each one's trim.</summary>
    static Dictionary<Rgb, Kit> OwnersOf(PoiLibrary lib) => Owners.GetValue(lib, l =>
    {
        var owners = new Dictionary<Rgb, Kit>();
        foreach (Kit kit in l.Kits.Values.Where(k => k.BaseCourse != null).OrderBy(k => k.Id, StringComparer.Ordinal))
            foreach (PaletteEntry e in kit.Palette.Where(e => e.Role == Role.Wall && e.Type == BlockType.Cube && e.Material != Material.Rock))
                foreach (Rgb c in e.Colors)
                {
                    owners.TryAdd(c, kit);
                    owners.TryAdd(Pilaster(c), kit);
                    owners.TryAdd(Armoured(c), kit);
                }
        return owners;
    });

    public static void Apply(Layout layout, PoiLibrary lib)
    {
        if (layout.Extras.InSpace) return;
        BlockGrid g = layout.Blocks;
        var owners = OwnersOf(lib);
        var top = new Dictionary<(int, int), int>();
        var walls = new List<(Int3 Cell, Block Block)>();
        foreach (var (p, b) in g.Blocks())
        {
            if (BlockInfo.Collides(b.Type) && (!top.TryGetValue((p.X, p.Z), out int t) || p.Y > t)) top[(p.X, p.Z)] = p.Y;
            if (b.Type == BlockType.Cube && b.Role is Role.Wall or Role.Light && p.Y >= 0) walls.Add((p, b));
        }

        bool Clear(Int3 p) => !BlockInfo.Collides(g.Get(p).Type);
        bool Sky(Int3 p) => !top.TryGetValue((p.X, p.Z), out int t) || p.Y > t;
        // Outside is open air with the sky above it, here or a cell further out under the eaves.
        bool Exposed(Int3 p) => Dirs.Horizontal.Any(d =>
        {
            Int3 n = p + d.Vec();
            return Clear(n) && (Sky(n) || Clear(n + d.Vec()) && Sky(n + d.Vec()));
        });

        var doors = DoorsOut(layout, Exposed);
        var nearDoor = new HashSet<Int3>();
        foreach (var (cells, _) in doors)
            foreach (Int3 c in cells)
                for (int dy = -1; dy <= 3; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        for (int dz = -2; dz <= 2; dz++)
                            nearDoor.Add(c + new Int3(dx, dy, dz));

        foreach (var (p, b) in walls)
        {
            if (!Exposed(p)) continue;
            if (b.Role == Role.Light)
            {
                if (nearDoor.Contains(p)) continue; // a lamp by a door is a lamp by a door
                foreach (Dir d in Dirs.Horizontal)
                    if (g.Get(p + d.Vec()) is { Type: BlockType.Cube, Role: Role.Wall } beside) { g.Set(p, beside); break; }
                continue;
            }
            if (!owners.TryGetValue(b.Color, out Kit? kit)) continue;
            bool capped = !BlockInfo.Collides(g.Get(p + Int3.Up).Type) && g.Get(p + Int3.Down).Type == BlockType.Cube;
            Rgb? paint = capped ? kit.Coping : p.Y == 0 ? kit.BaseCourse : null;
            if (paint is Rgb c) Paint(g, p, c);
        }

        foreach (var (cells, side) in doors)
        {
            var hole = new HashSet<Int3>(cells);
            int high = cells.Max(c => c.Y);
            foreach (Int3 c in cells)
            {
                Frame(c + side);
                Frame(c - side);
                if (c.Y == high) { Frame(c + Int3.Up); Frame(c + Int3.Up + side); Frame(c + Int3.Up - side); }
            }

            void Frame(Int3 p)
            {
                if (hole.Contains(p) || g.Get(p) is not { Type: BlockType.Cube, Role: Role.Wall } w) return;
                // A house is clad in its own paint, not a kit's: it frames its doors in its own trim where its style
                // calls for one (Homes), never in a kit's hazard colour.
                if (layout.Extras.Homes.Count > 0) return;
                Kit kit = owners.TryGetValue(w.Color, out Kit? k) ? k : layout.Kit;
                if (kit.DoorFrame is Rgb frame && !IsHazard(frame)) Paint(g, p, frame);
            }
        }
    }

    /// <summary>A safety yellow or a gold that reads as one at a distance: never round a person's door.</summary>
    public static bool IsHazard(Rgb c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        if (max < 170 || max - min < max * 0.45f || c.B == max) return false; // bright: an ochre or timber trim is no hazard
        float hue = c.R == max ? 60f * (c.G - c.B) / (max - min) : 60f * (2f + (c.B - c.R) / (float)(max - min));
        return hue >= 36f && hue <= 66f;
    }

    static void Paint(BlockGrid g, Int3 p, Rgb color)
    {
        Block b = g.Get(p);
        b.Color = color;
        g.Set(p, b);
    }

    /// <summary>Doorways that open on the outside, each with the way along its wall.</summary>
    static List<(List<Int3> Cells, Int3 Side)> DoorsOut(Layout layout, Func<Int3, bool> exposed)
    {
        var doors = new List<(List<Int3>, Int3)>();
        void Add(List<Int3> cells, Dir facing)
        {
            if (cells.Count == 0 || !facing.IsHorizontal() || cells.Min(c => c.Y) < 0) return;
            if (cells.Any(exposed)) doors.Add((cells, facing.RotateCw(1).Vec()));
        }
        Add(layout.ExitCells, layout.EntranceFacing);
        foreach (Doorway d in layout.Doorways) Add(d.Cells, d.Facing);
        return doors;
    }
}
