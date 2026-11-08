#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>A paint on a plain material: what a player would pick to build it.</summary>
public readonly record struct Stuff2(Material M, Rgb C)
{
    public Stuff2 Scale(float k) => new(M, C.Scale(k));
}

/// <summary>What a large place is built of, by the tech level that built it: plain materials, a few paints.</summary>
public sealed class Fab
{
    public Stuff2 Wall, Panel, Plinth, Floor, Roof, Steel, Rust, Pipe, PipeHot, Accent, Grating;
    public Rgb Hazard = new(0xe0, 0xa8, 0x10), Black = new(0x1e, 0x1e, 0x20), Glass = new(0x5a, 0x6c, 0x70), Lamp = new(0xff, 0xd8, 0xa0), Emergency = new(0xff, 0x40, 0x28);

    public static Fab For(TechLevel tech, Rng r) => tech switch
    {
        TechLevel.Low => new Fab
        {
            Wall = new(Material.Rock, r.Pick(new[] { new Rgb(0x7a, 0x5a, 0x4a), new Rgb(0x6e, 0x5c, 0x50), new Rgb(0x84, 0x6a, 0x54) })),
            Panel = new(Material.Timber, new Rgb(0x6a, 0x58, 0x46)), Plinth = new(Material.Rock, new Rgb(0x5e, 0x5a, 0x54)), Floor = new(Material.Rock, new Rgb(0x66, 0x60, 0x58)),
            Roof = new(Material.Timber, new Rgb(0x4a, 0x3c, 0x32)), Steel = new(Material.Timber, new Rgb(0x5a, 0x48, 0x38)), Rust = new(Material.Rust, new Rgb(0x6e, 0x46, 0x34)),
            Pipe = new(Material.Rust, new Rgb(0x72, 0x4c, 0x38)), PipeHot = new(Material.Rust, new Rgb(0x5c, 0x48, 0x3e)), Accent = new(Material.Timber, new Rgb(0x7a, 0x3c, 0x2c)), Grating = new(Material.Timber, new Rgb(0x60, 0x4e, 0x3e)),
            Lamp = new Rgb(0xff, 0xc0, 0x70),
        },
        TechLevel.High => new Fab
        {
            Wall = new(Material.Resin, r.Pick(new[] { new Rgb(0xb4, 0xba, 0xbc), new Rgb(0xa6, 0xae, 0xb4), new Rgb(0xbc, 0xb8, 0xae) })),
            Panel = new(Material.Alloy, new Rgb(0x8c, 0x98, 0xa2)), Plinth = new(Material.Concrete, new Rgb(0x5e, 0x64, 0x6a)), Floor = new(Material.Concrete, new Rgb(0x72, 0x78, 0x7c)),
            Roof = new(Material.Alloy, new Rgb(0x56, 0x60, 0x6a)), Steel = new(Material.Alloy, new Rgb(0x6e, 0x7a, 0x84)), Rust = new(Material.Alloy, new Rgb(0x4e, 0x58, 0x62)),
            Pipe = new(Material.Alloy, new Rgb(0x9a, 0xa6, 0xb0)), PipeHot = new(Material.Alloy, new Rgb(0x36, 0x6a, 0x7e)), Accent = new(Material.Alloy, new Rgb(0x1e, 0x6a, 0x84)), Grating = new(Material.Alloy, new Rgb(0x58, 0x64, 0x6e)),
            Lamp = new Rgb(0xc8, 0xe8, 0xff), Hazard = new Rgb(0xe8, 0x96, 0x14),
        },
        _ => new Fab
        {
            Wall = new(Material.Concrete, r.Pick(new[] { new Rgb(0x8c, 0x88, 0x80), new Rgb(0x84, 0x82, 0x7c), new Rgb(0x92, 0x8c, 0x80) })),
            Panel = new(Material.Concrete, new Rgb(0x70, 0x6e, 0x6a)), Plinth = new(Material.Concrete, new Rgb(0x5c, 0x5a, 0x56)), Floor = new(Material.Concrete, new Rgb(0x6a, 0x68, 0x64)),
            Roof = new(Material.Concrete, new Rgb(0x56, 0x54, 0x50)), Steel = new(Material.Steel, new Rgb(0x5c, 0x60, 0x62)), Rust = new(Material.Rust, new Rgb(0x7a, 0x48, 0x30)),
            Pipe = new(Material.Steel, new Rgb(0x74, 0x78, 0x76)), PipeHot = new(Material.Rust, new Rgb(0x7e, 0x50, 0x38)), Accent = new(Material.Steel, new Rgb(0x2a, 0x4e, 0x62)), Grating = new(Material.Steel, new Rgb(0x56, 0x5a, 0x5c)),
        },
    };
}

/// <summary>
/// A large place drawn the way a player would build it. A block here is the player's 2 m large block
/// (eight of the POI grid's 1 m cells), and every size is in whole blocks: walls a block thick, storeys
/// a whole number of blocks, half-block decks, 45 degree large slopes, the player's door block (one block, the
/// standard 2 m door) wherever a person goes through a wall.
/// So anything drawn here keeps to the 2 m lattice, whichever way the place is turned, and can be read
/// and rebuilt block for block. Rooms are volumes, not a plan of cells: a hall, a gallery, a tunnel.
/// <para>u and v are cells in the place's own frame (u along its front, v back from it), i and k blocks in
/// that frame, j a block level: level j covers cells 2j and 2j + 1, and you stand in level 0 on the ground.</para>
/// </summary>
public sealed class Plot
{
    public readonly Canvas C;
    public readonly Rng R;
    public readonly TechLevel Tech;
    public readonly Fab F;
    readonly int _q;
    readonly HashSet<Int3> _claimed = new();
    readonly List<(int M, int I0, int J0, int K0, int I1, int J1, int K1)> _pending = new();
    /// <summary>Modules named for the loot, set by the place's own plan.</summary>
    public int Yard = -1;

    public Plot(Canvas c, Rng r, TechLevel tech, Dir front)
    {
        C = c;
        R = r;
        Tech = tech;
        F = Fab.For(tech, r);
        _q = front is Dir.North or Dir.East or Dir.South or Dir.West ? (int)front : 0;
        c.ExitCells = new List<Int3>();
        c.X.Levels.Add(new LevelInfo { Storey = 0, WalkY = 0, Name = "ground" });
    }

    // ---- the frame -----------------------------------------------------------------------------

    /// <summary>A cell of the place's frame on the map. Turning keeps whole blocks whole: cell u maps to -1 - u, not -u.</summary>
    public Int3 Cell(int u, int y, int v) => _q switch
    {
        0 => new Int3(u, y, v),
        1 => new Int3(-1 - v, y, u),
        2 => new Int3(-1 - u, y, -1 - v),
        _ => new Int3(v, y, -1 - u),
    };

    public Dir Turn(Dir d) => d is Dir.Up or Dir.Down ? d : (Dir)(((int)d + _q) % 4);

    public static Block Cube(Stuff2 s, Role r) => new(BlockType.Cube, s.M, s.C, r);

    /// <summary>Sets a cell, claiming it for the place.</summary>
    public void Put(int u, int y, int v, Block b)
    {
        b.Facing = Turn(b.Facing);
        C.Set(Cell(u, y, v), b);
    }

    /// <summary>Sets a cell without claiming it: something lying on open ground.</summary>
    public void Lay(int u, int y, int v, Block b)
    {
        b.Facing = Turn(b.Facing);
        C.Blocks[Cell(u, y, v)] = b;
    }

    public Block Get(int u, int y, int v) => C.Get(Cell(u, y, v));

    /// <summary>Open air the place owns (below ground it is carved).</summary>
    public void Air(int u, int y, int v) => C.Clear(Cell(u, y, v));

    /// <summary>Fills whole blocks, corners inclusive.</summary>
    public void Fill(int i0, int j0, int k0, int i1, int j1, int k1, Block b)
    {
        for (int k = 2 * k0; k <= 2 * k1 + 1; k++)
            for (int y = 2 * j0; y <= 2 * j1 + 1; y++)
                for (int i = 2 * i0; i <= 2 * i1 + 1; i++) Put(i, y, k, b);
    }

    /// <summary>Takes whole blocks out, corners inclusive.</summary>
    public void Dig(int i0, int j0, int k0, int i1, int j1, int k1)
    {
        for (int k = 2 * k0; k <= 2 * k1 + 1; k++)
            for (int y = 2 * j0; y <= 2 * j1 + 1; y++)
                for (int i = 2 * i0; i <= 2 * i1 + 1; i++) Air(i, y, k);
    }

    /// <summary>Paves ground with thin plates, under anything already standing.</summary>
    public void Pave(int i0, int k0, int i1, int k1, Stuff2 s, Role role = Role.Floor, int y = 0)
    {
        for (int k = 2 * k0; k <= 2 * k1 + 1; k++)
            for (int i = 2 * i0; i <= 2 * i1 + 1; i++)
            {
                Int3 c = Cell(i, y, k);
                if (!C.Blocks.ContainsKey(c)) C.Blocks[c] = new Block(BlockType.Plate, s.M, s.C.Scale(0.94f + (Hash.Cell(new Int3(i / 4, 0, k / 4), 141) % 12) / 100f), role);
            }
    }

    /// <summary>
    /// A room: every open cell of a box of blocks that no other room holds. A big volume is settled at
    /// <see cref="Finish"/>, so gantries, flights and pits inside it stay rooms of their own. Returns the module.
    /// </summary>
    public int Room(ModuleKind kind, string purpose, int storey, int i0, int j0, int k0, int i1, int j1, int k1, Func<Int3, bool>? keep = null)
    {
        if (keep == null && (i1 - i0 + 1) * (j1 - j0 + 1) * (k1 - k0 + 1) > 64)
        {
            int late = C.AddModule(kind, purpose, storey, new[] { Cell(2 * i0, 2 * j0, 2 * k0) });
            _pending.Add((late, i0, j0, k0, i1, j1, k1));
            return late;
        }
        var cells = BoxCells(i0, j0, k0, i1, j1, k1, keep);
        return C.AddModule(kind, purpose, storey, cells);
    }

    List<Int3> BoxCells(int i0, int j0, int k0, int i1, int j1, int k1, Func<Int3, bool>? keep = null)
    {
        var cells = new List<Int3>();
        for (int k = 2 * k0; k <= 2 * k1 + 1; k++)
            for (int y = 2 * j0; y <= 2 * j1 + 1; y++)
                for (int i = 2 * i0; i <= 2 * i1 + 1; i++)
                {
                    Int3 p = Cell(i, y, k);
                    if ((keep == null || keep(p)) && _claimed.Add(p)) cells.Add(p);
                }
        return cells;
    }

    /// <summary>Settles the big rooms, now the small ones inside them are known. Every plan ends with this.</summary>
    public void Finish()
    {
        // The smallest first: a gallery or an office inside a hall keeps its own cells, and the hall takes the rest.
        foreach (var (m, i0, j0, k0, i1, j1, k1) in _pending.OrderBy(q => (q.I1 - q.I0 + 1) * (q.J1 - q.J0 + 1) * (q.K1 - q.K0 + 1)).ThenBy(q => q.M))
            C.Reshape(m, BoxCells(i0, j0, k0, i1, j1, k1));
        _pending.Clear();
        C.X.Levels = C.X.Levels.OrderBy(l => l.Storey).ToList();
    }

    /// <summary>A room of cells worked out by the plan (a round basin), in the place's frame.</summary>
    public int RoomOf(ModuleKind kind, string purpose, int storey, IEnumerable<(int U, int Y, int V)> cells)
    {
        var list = cells.Select(c => Cell(c.U, c.Y, c.V)).Where(_claimed.Add).ToList();
        return C.AddModule(kind, purpose, storey, list);
    }

    public void Claim(IEnumerable<Int3> cells) => _claimed.UnionWith(cells);

    /// <summary>A storey a hall or tunnel walks at, so the checks and the wrongness know of it.</summary>
    public void Level(int storey, int j, string name)
    {
        if (C.X.Levels.All(l => l.Storey != storey))
            C.X.Levels.Add(new LevelInfo { Storey = storey, WalkY = 2 * j, Name = name });
    }

    /// <summary>A building's weather box, for its decay.</summary>
    public void Weathers(int i0, int j0, int k0, int i1, int j1, int k1, int eaveJ)
    {
        Int3 a = Cell(2 * i0, 2 * j0, 2 * k0), b = Cell(2 * i1 + 1, 2 * j1 + 1, 2 * k1 + 1);
        C.X.Plain.Add(new PlainBox(Int3.Min(a, b), Int3.Max(a, b), 2 * eaveJ, Turn(Dir.North)));
    }

    // ---- buildings -----------------------------------------------------------------------------

    /// <summary>
    /// A hall: walls a block thick round a footprint (outer faces inclusive), h clear levels from level j, a
    /// slab roof a block thick. A floor slab where it stands above or below ground, plain plates on it.
    /// Returns its module, the whole inside.
    /// </summary>
    public int Hall(string purpose, ModuleKind kind, int storey, int i0, int k0, int i1, int k1, int j, int h, Stuff2 wall, Stuff2? roof = null, bool weather = true)
    {
        Stuff2 floor = F.Floor, top = roof ?? F.Roof;
        for (int k = k0; k <= k1; k++)
            for (int i = i0; i <= i1; i++)
            {
                bool edge = i == i0 || i == i1 || k == k0 || k == k1;
                if (j != 0 || edge) Fill(i, j - 1, k, i, j - 1, k, Cube(j > 0 ? floor : F.Plinth, Role.Floor));
                if (edge) Fill(i, j, k, i, j + h - 1, k, Cube(wall, Role.Wall));
                else Dig(i, j, k, i, j + h - 1, k);
                Fill(i, j + h, k, i, j + h, k, Cube(top, Role.Roof));
            }
        if (j == 0) Pave(i0 + 1, k0 + 1, i1 - 1, k1 - 1, floor);
        if (j == 0 && weather) Plinth(i0, k0, i1, k1, 0);
        if (weather && j >= 0) Weathers(i0 - 1, 0, k0 - 1, i1 + 1, j + h + 1, k1 + 1, j + h);
        return Room(kind, purpose, storey, i0 + 1, j, k0 + 1, i1 - 1, j + h - 1, k1 - 1);
    }

    /// <summary>A storey's walls, floor and roof with no room of its own: the rooms are drawn inside it after. Same box as <see cref="Hall"/>.</summary>
    public void Shell(int i0, int k0, int i1, int k1, int j, int h, Stuff2 wall, Stuff2 roof)
    {
        for (int k = k0; k <= k1; k++)
            for (int i = i0; i <= i1; i++)
            {
                bool edge = i == i0 || i == i1 || k == k0 || k == k1;
                if (j != 0 || edge) Fill(i, j - 1, k, i, j - 1, k, Cube(j > 0 ? F.Floor : F.Plinth, Role.Floor));
                if (edge) Fill(i, j, k, i, j + h - 1, k, Cube(wall, Role.Wall));
                else Dig(i, j, k, i, j + h - 1, k);
                Fill(i, j + h, k, i, j + h, k, Cube(roof, Role.Roof));
            }
        if (j == 0) Pave(i0 + 1, k0 + 1, i1 - 1, k1 - 1, F.Floor);
    }

    /// <summary>The foot of a wall in a darker course.</summary>
    public void Plinth(int i0, int k0, int i1, int k1, int j)
    {
        foreach (var (i, k) in Ring(i0, k0, i1, k1)) Fill(i, j, k, i, j, k, Cube(F.Plinth, Role.Wall));
    }

    static IEnumerable<(int I, int K)> Ring(int i0, int k0, int i1, int k1)
    {
        for (int k = k0; k <= k1; k++)
            for (int i = i0; i <= i1; i++)
                if (i == i0 || i == i1 || k == k0 || k == k1) yield return (i, k);
    }

    /// <summary>A way through a wall, in blocks, with no door: a vehicle's mouth, a gap.</summary>
    public void Mouth(int i0, int j0, int k0, int i1, int j1, int k1, int a = -1, int b = -1)
    {
        Dig(i0, j0, k0, i1, j1, k1);
        for (int k = 2 * k0; k <= 2 * k1 + 1; k++)
            for (int i = 2 * i0; i <= 2 * i1 + 1; i++)
            {
                Int3 c = Cell(i, 2 * j0, k);
                if (j0 == 0 && !C.Blocks.ContainsKey(c)) C.Blocks[c] = new Block(BlockType.Plate, F.Floor.M, F.Floor.C, Role.Floor);
            }
        C.Link(a, b);
    }

    /// <summary>
    /// A player's door, the standard one (<see cref="DoorSize"/>): the player's door block, one block wide and tall,
    /// in a wall one block thick (along the wall's i or k) between two rooms.
    /// </summary>
    public int Door(int i, int j, int k, bool alongI, int a, int b)
    {
        // Facing across the wall: the door hangs in the wall's front layer, the back layer is open.
        var cells = new List<Int3>();
        for (int s = 0; s < 2; s++)
            for (int y = 2 * j; y < 2 * j + DoorSize.Tall; y++)
            {
                cells.Add(alongI ? Cell(2 * i + s, y, 2 * k) : Cell(2 * i, y, 2 * k + s));
                Air(alongI ? 2 * i + s : 2 * i + 1, y, alongI ? 2 * k + 1 : 2 * k + s);
            }
        int d = C.AddDoorway(cells, Turn(alongI ? Dir.South : Dir.East), a, b);
        return d;
    }

    /// <summary>A strip of windows through a wall, a block tall, on the 2 m lattice.</summary>
    public void Glazed(int i0, int j, int k0, int i1, int k1)
    {
        for (int k = 2 * k0; k <= 2 * k1 + 1; k++)
            for (int i = 2 * i0; i <= 2 * i1 + 1; i++)
                for (int y = 2 * j; y < 2 * j + 2; y++)
                    Put(i, y, k, new Block(BlockType.Pane, Material.Glass, F.Glass, Role.Window));
    }

    /// <summary>A gable roof of large slopes over a footprint, from the eaves' level up to its ridge; the ridge runs along i or k.</summary>
    public void Gable(int i0, int k0, int i1, int k1, int j, bool ridgeAlongI, Stuff2 roof, Stuff2 gable, int over = 1)
    {
        int a0 = 2 * (ridgeAlongI ? k0 : i0) - 2 * over, a1 = 2 * (ridgeAlongI ? k1 : i1) + 1 + 2 * over, b0 = 2 * (ridgeAlongI ? i0 : k0), b1 = 2 * (ridgeAlongI ? i1 : k1) + 1;
        int low = 2 * j;
        Dir up = ridgeAlongI ? Dir.South : Dir.East, down = up.Opposite();
        for (int a = a0; a <= a1; a++)
        {
            int left = a - a0, right = a1 - a, n = Math.Min(left, right), y = low + n;
            for (int b = b0; b <= b1; b++)
            {
                int u = ridgeAlongI ? b : a, v = ridgeAlongI ? a : b;
                bool end = b == b0 || b == b1;
                Stuff2 s = end ? gable : roof;
                Put(u, y, v, left == right ? Cube(s, Role.Roof) : new Block(BlockType.Ramp, s.M, s.C, Role.Roof, left < right ? up : down));
                // The deck under the slope holds it together.
                // At the gable ends the course under the slope wears the gable's own paint, so the verge reads as one line.
                if (n > 0) Put(u, y - 1, v, Cube(end ? s : s.Scale(0.8f), Role.Roof));
                // Gable walls fill up to it, two cells thick.
                bool inside = a >= 2 * (ridgeAlongI ? k0 : i0) && a <= 2 * (ridgeAlongI ? k1 : i1) + 1;
                if (inside && (b <= b0 + 1 || b >= b1 - 1))
                    for (int h = low; h < y - 1; h++) Put(u, h, v, Cube(gable, Role.Wall));
            }
        }
    }

    // ---- ways up, over and along ---------------------------------------------------------------

    /// <summary>
    /// A flight at 45 degrees, width blocks wide, from (i, k) rising in direction d by rise levels from
    /// level j, a metre along for a metre up, solid under each step. Takes out whatever stands over it.
    /// You step off at the top onto level j + rise, rise blocks along.
    /// </summary>
    public int Flight(int i, int k, Dir d, int j, int rise, int width, string purpose = "stair", int storey = 0)
    {
        int y0 = 2 * j, n = 2 * rise;
        var cells = new List<Int3>();
        for (int t = 0; t < n; t++)
            for (int s = 0; s < 2 * width; s++)
            {
                var (u, v) = d switch
                {
                    Dir.East => (2 * i + t, 2 * k + s),
                    Dir.West => (2 * i + 1 - t, 2 * k + s),
                    Dir.South => (2 * i + s, 2 * k + t),
                    _ => (2 * i + s, 2 * k + 1 - t),
                };
                int y = y0 + t;
                if (t > 0) Put(u, y - 1, v, Cube(F.Plinth, Role.Traverse));
                Put(u, y, v, new Block(BlockType.Stair, F.Floor.M, F.Floor.C.Scale(1.08f), Role.Traverse, d));
                cells.Add(Cell(u, y, v));
                for (int up = 1; up <= 4; up++)
                {
                    Air(u, y + up, v);
                    cells.Add(Cell(u, y + up, v));
                }
            }
        var mine = cells.Where(_claimed.Add).ToList();
        return C.AddModule(ModuleKind.Stair, purpose, storey, mine);
    }

    /// <summary>Lines the shaft of a <see cref="Flight"/> going underground, whole blocks round it, on the same arguments; nothing above the ground and nothing a room already holds.</summary>
    public void Line(int i, int k, Dir d, int j, int rise, int width, Stuff2 lining)
    {
        for (int t = 0; t < rise; t++)
            for (int s = -1; s <= width; s++)
                for (int h = -1; h <= 3; h++)
                {
                    bool shell = s < 0 || s >= width || h == -1 || h == 3;
                    int bj = j + t + h;
                    if (!shell || bj >= 0) continue;
                    var (bi, bk) = d switch
                    {
                        Dir.East => (i + t, k + s),
                        Dir.West => (i - t, k + s),
                        Dir.South => (i + s, k + t),
                        _ => (i + s, k - t),
                    };
                    for (int kk = 2 * bk; kk < 2 * bk + 2; kk++)
                        for (int y = 2 * bj; y < 2 * bj + 2; y++)
                            for (int ii = 2 * bi; ii < 2 * bi + 2; ii++)
                                if (!C.Occ.Contains(Cell(ii, y, kk))) Put(ii, y, kk, Cube(lining, Role.Rock));
                }
    }

    /// <summary>A ladder up a face: from y0 up through y1, rungs against the wall behind it (back: which way the wall lies).</summary>
    public void Ladder(int u, int y0, int y1, int v, Dir back)
    {
        for (int y = y0; y <= y1; y++) Put(u, y, v, new Block(BlockType.Ladder, F.Steel.M, F.Steel.C, Role.Traverse, back.Opposite()));
    }

    /// <summary>
    /// A gantry: a half-block open deck you stand on at level j, rails down its long sides (2 blocks wide
    /// leaves 2 m between them), on posts down to the ground every few blocks unless hung from something.
    /// </summary>
    public int Gantry(string purpose, int storey, int i0, int k0, int i1, int k1, int j, int every = 5, bool railEnds = false, bool rails = true)
    {
        bool alongI = i1 - i0 >= k1 - k0;
        Block deck = new(BlockType.Lattice, F.Grating.M, F.Grating.C, Role.Floor);
        Block rail = new(BlockType.Rail, F.Hazard.R > 0 && Tech != TechLevel.Low ? Material.Steel : F.Steel.M, Tech == TechLevel.Low ? F.Steel.C : F.Hazard, Role.Decor);
        int y = 2 * j;
        for (int k = 2 * k0; k <= 2 * k1 + 1; k++)
            for (int i = 2 * i0; i <= 2 * i1 + 1; i++)
            {
                Put(i, y - 1, k, deck);
                bool side = alongI ? k == 2 * k0 || k == 2 * k1 + 1 : i == 2 * i0 || i == 2 * i1 + 1;
                bool end = alongI ? i == 2 * i0 || i == 2 * i1 + 1 : k == 2 * k0 || k == 2 * k1 + 1;
                if (rails && (side || railEnds && end)) Put(i, y, k, rail);
                else { Air(i, y, k); Air(i, y + 1, k); Air(i, y + 2, k); }
            }
        if (every > 0)
            for (int t = alongI ? i0 : k0; t <= (alongI ? i1 : k1); t += every)
                foreach (int s in new[] { alongI ? 2 * k0 : 2 * i0, alongI ? 2 * k1 + 1 : 2 * i1 + 1 })
                {
                    int u = alongI ? 2 * t : s, v = alongI ? s : 2 * t;
                    Post(u, y - 2, v, F.Steel);
                }
        int m = Room(ModuleKind.Corridor, purpose, storey, i0, j, k0, i1, j + 1, k1, p => C.Blocks.TryGetValue(p, out var b) ? b.Type != BlockType.Rail : true);
        return m;
    }

    /// <summary>Takes rails out of a stretch of deck, where another way joins it.</summary>
    public void Unrail(int u0, int y, int v0, int u1, int v1)
    {
        for (int v = v0; v <= v1; v++)
            for (int u = u0; u <= u1; u++)
                if (Get(u, y, v).Type == BlockType.Rail) { C.Blocks.Remove(Cell(u, y, v)); C.Occ.Add(Cell(u, y, v)); }
    }

    /// <summary>A post from a cell straight down to whatever it stands on.</summary>
    public void Post(int u, int top, int v, Stuff2 s)
    {
        for (int y = top; y >= -24; y--)
        {
            Block here = Get(u, y, v);
            if (!here.IsAir && BlockInfo.Collides(here.Type) || y < 0 && C.Solid(Cell(u, y, v))) break;
            Put(u, y, v, new Block(BlockType.Rib, s.M, s.C, Role.Decor));
        }
    }

    /// <summary>A lined tunnel below ground: h clear levels from level j, concrete round it, carved out of the rock. Lining never fills a room already there.</summary>
    public int Tunnel(string purpose, int storey, int i0, int k0, int i1, int k1, int j, int h, Stuff2 lining, ModuleKind kind = ModuleKind.Hall)
    {
        void Line(int i, int jj, int k, Role role)
        {
            for (int kk = 2 * k; kk < 2 * k + 2; kk++)
                for (int y = 2 * jj; y < 2 * jj + 2; y++)
                    for (int ii = 2 * i; ii < 2 * i + 2; ii++)
                    {
                        Int3 c = Cell(ii, y, kk);
                        if (!C.Occ.Contains(c) || C.Blocks.TryGetValue(c, out var old) && old.Type == BlockType.Cube) Put(ii, y, kk, Cube(lining, role));
                    }
        }
        for (int k = k0 - 1; k <= k1 + 1; k++)
            for (int i = i0 - 1; i <= i1 + 1; i++)
            {
                // A lining is part of the ground it was cast in: the weather never gets to it.
                Line(i, j - 1, k, Role.Rock);
                Line(i, j + h, k, Role.Rock);
                bool edge = i < i0 || i > i1 || k < k0 || k > k1;
                for (int jj = j; jj < j + h; jj++)
                    if (edge) Line(i, jj, k, Role.Rock);
            }
        Dig(i0, j, k0, i1, j + h - 1, k1);
        return Room(kind, purpose, storey, i0, j, k0, i1, j + h - 1, k1);
    }

    /// <summary>A run of 1 m pipe, of len cells from a cell along a map direction.</summary>
    public void Pipe(int u, int y, int v, Dir d, int len, Stuff2 s)
    {
        Int3 step = d.Vec();
        for (int t = 0; t < len; t++) Put(u + step.X * t, y + step.Y * t, v + step.Z * t, new Block(BlockType.Pipe, s.M, s.C, Role.Decor, d));
    }

    /// <summary>A 2 m main, block by block, from block (i, j, k) along a direction, of len blocks. Each block's lowest cell draws it, whichever way the place is turned.</summary>
    public void Main(int i, int j, int k, Dir d, int len, Stuff2 s)
    {
        Int3 step = d.Vec();
        for (int t = 0; t < len; t++)
        {
            int bi = i + step.X * t, bj = j + step.Y * t, bk = k + step.Z * t;
            Int3 a = Cell(2 * bi, 2 * bj, 2 * bk), b = Cell(2 * bi + 1, 2 * bj + 1, 2 * bk + 1), lo = Int3.Min(a, b);
            Dir turned = Turn(d);
            for (int n = 0; n < 2; n++)
            {
                Int3 v = turned.Vec();
                Int3 c = lo + new Int3(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z)) * n;
                C.Set(c, new Block(BlockType.Pipe, s.M, s.C, Role.Decor, turned, 1));
                // The rest of the block belongs to the pipe, so nothing else is built through it.
            }
        }
    }

    // ---- round things --------------------------------------------------------------------------

    public static bool In(double ci, double ck, double r, int i, int k)
    {
        double x = i + 0.5 - ci, z = k + 0.5 - ck;
        return x * x + z * z <= r * r;
    }

    /// <summary>A drum of whole blocks: walls a block thick on a circle, from level j0 to j1. Below ground the inside is dug out.</summary>
    public void Drum(double ci, double ck, double r, int j0, int j1, Stuff2 wall, Func<int, Stuff2>? course = null)
    {
        int rr = (int)Math.Ceiling(r) + 1;
        for (int k = (int)ck - rr; k <= (int)ck + rr; k++)
            for (int i = (int)ci - rr; i <= (int)ci + rr; i++)
            {
                if (!In(ci, ck, r, i, k)) continue;
                bool shell = !In(ci, ck, r - 1.0, i, k);
                for (int j = j0; j <= j1; j++)
                    if (shell) Fill(i, j, k, i, j, k, Cube(course?.Invoke(j) ?? wall, Role.Wall));
                    else Dig(i, j, k, i, j, k);
            }
    }

    /// <summary>A disc of whole blocks.</summary>
    public void Disc(double ci, double ck, double r, int j, Block b)
    {
        int rr = (int)Math.Ceiling(r) + 1;
        for (int k = (int)ck - rr; k <= (int)ck + rr; k++)
            for (int i = (int)ci - rr; i <= (int)ci + rr; i++)
                if (In(ci, ck, r, i, k)) Fill(i, j, k, i, j, k, b);
    }

    /// <summary>
    /// A faceted dome of the player's slopes on a drum: an eight-sided pyramid round the drum, four faces of plain
    /// slopes square to the axes and four steeper ones on the diagonals, each a smooth plane of corner pieces,
    /// meeting in straight ridges. At one cell a cell (step 1) every corner height is whole, so every cell is
    /// exactly one piece, or an inner corner under a corner on the diagonals, with no flats or steps.
    /// </summary>
    public void Cap(double ci, double ck, double r, int j, Stuff2 s, float step = 1f)
    {
        bool facets = step == 1f;
        double cu = facets ? Math.Round(2 * ci) : 2 * ci, cv = facets ? Math.Round(2 * ck) : 2 * ck, big = facets ? Math.Round(2 * r) : 2 * r;
        double chamfer = Math.Round(big * (Math.Sqrt(2) - 1));
        int reach = (int)Math.Ceiling(big) + 1, low = 2 * j;
        // The height at a cell corner, in cells over the rim, never below it: the lower of the square faces and the diagonal ones.
        int At(int u, int v)
        {
            double x = Math.Abs(u - cu), z = Math.Abs(v - cv);
            // Any other pitch (a works' low tank roof) stays a plain cone: its heights aren't whole, so it would terrace either way.
            if (!facets) return (int)Math.Round(Math.Max(0, (big - Math.Sqrt(x * x + z * z)) / step));
            return (int)Math.Max(0, Math.Min(big - Math.Max(x, z), big + chamfer - x - z));
        }
        for (int v = (int)cv - reach; v <= (int)cv + reach; v++)
            for (int u = (int)cu - reach; u <= (int)cu + reach; u++)
            {
                if ((u + 0.5 - cu) * (u + 0.5 - cu) + (v + 0.5 - cv) * (v + 0.5 - cv) > big * big) continue;
                int nw = At(u, v), ne = At(u + 1, v), sw = At(u, v + 1), se = At(u + 1, v + 1);
                int h = Math.Min(Math.Min(nw, ne), Math.Min(sw, se)), top = Math.Max(Math.Max(nw, ne), Math.Max(sw, se));
                // Solid in the roof's own colour to the lowest corner (a flat where the corners round alike), then
                // one piece a level while any corner rises past it: a steep diagonal stacks a corner on an inner corner.
                for (int y = low; y < low + h; y++) Put(u, y, v, Cube(s, Role.Roof));
                for (int y = h; y < (facets ? top : Math.Min(top, h + 1)); y++)
                    if (Piece(nw > y, ne > y, sw > y, se > y, s) is { } piece) Put(u, low + y, v, piece);
                if (top == 0) Put(u, low, v, Cube(s, Role.Roof));
            }
    }

    /// <summary>
    /// The player's slope piece whose top runs through these raised corners (frame directions): none raised,
    /// nothing (the cell's floor is the surface); one, a corner slope; two along a side, a slope; three, an
    /// inner corner; all four, a cube. Two across a diagonal is a saddle no piece makes: the higher reading wins.
    /// </summary>
    public static Block? Piece(bool nw, bool ne, bool sw, bool se, Stuff2 s)
    {
        int n = (nw ? 1 : 0) + (ne ? 1 : 0) + (sw ? 1 : 0) + (se ? 1 : 0);
        Block Make(BlockType t, Dir d) => new(t, s.M, s.C, Role.Roof, d);
        return n switch
        {
            0 => null,
            4 => Cube(s, Role.Roof),
            1 => Make(BlockType.Corner, nw ? Dir.North : ne ? Dir.East : se ? Dir.South : Dir.West),
            3 => Make(BlockType.InvCorner, !se ? Dir.North : !sw ? Dir.East : !nw ? Dir.South : Dir.West),
            _ => nw && ne ? Make(BlockType.Ramp, Dir.North) : ne && se ? Make(BlockType.Ramp, Dir.East)
                : sw && se ? Make(BlockType.Ramp, Dir.South) : nw && sw ? Make(BlockType.Ramp, Dir.West) : Cube(s, Role.Roof),
        };
    }

    // ---- paint and marks -----------------------------------------------------------------------

    /// <summary>Repaints whatever stands in a box of cells.</summary>
    public void Repaint(int u0, int y0, int v0, int u1, int y1, int v1, Func<int, int, int, Rgb> paint)
    {
        for (int v = v0; v <= v1; v++)
            for (int y = y0; y <= y1; y++)
                for (int u = u0; u <= u1; u++)
                {
                    Int3 c = Cell(u, y, v);
                    if (C.Blocks.TryGetValue(c, out var b) && BlockInfo.Collides(b.Type)) { b.Color = paint(u, y, v); C.Blocks[c] = b; }
                }
    }

    /// <summary>Hazard stripes: black and yellow diagonals 2 m wide over a box of cells.</summary>
    public void Stripes(int u0, int y0, int v0, int u1, int y1, int v1) =>
        Repaint(u0, y0, v0, u1, y1, v1, (u, y, v) => ((u + v + y) >> 1 & 1) == 0 ? F.Hazard : F.Black);

    /// <summary>A hazard stripe painted on floor plates along the edge of a pit or a line of travel.</summary>
    public void FloorStripes(int u0, int v0, int u1, int v1, int y = 0)
    {
        for (int v = v0; v <= v1; v++)
            for (int u = u0; u <= u1; u++)
            {
                Int3 c = Cell(u, y, v);
                C.Blocks[c] = new Block(BlockType.Plate, Material.Concrete, ((u + v) >> 1 & 1) == 0 ? F.Hazard : F.Black, Role.Floor);
            }
    }

    static readonly string[] Font =
    {
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111",
    };

    /// <summary>
    /// Stencils a number on a wall's face, 3 by 5 paint blocks a digit: from cell (u, y, v), reading along +u
    /// or +v, each paint block scale cells square, on the wall's face layer. mirror: for a face read from the
    /// other side of the plan (a wall facing north reads along -u).
    /// </summary>
    public void Stencil(string text, int u, int y, int v, bool alongU, Rgb ink, int scale = 2, bool mirror = false)
    {
        int at = 0, wide = text.Length * 4 * scale - scale;
        foreach (char ch in text)
        {
            if (ch >= '0' && ch <= '9')
            {
                string g = Font[ch - '0'];
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        if (g[row * 3 + col] != '1') continue;
                        int a = at + col * scale, top = y + (4 - row) * scale;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sa = 0; sa < scale; sa++)
                            {
                                int along = mirror ? wide - 1 - (a + sa) : a + sa;
                                Int3 c = alongU ? Cell(u + along, top + sy, v) : Cell(u, top + sy, v + along);
                                if (C.Blocks.TryGetValue(c, out var b)) { b.Color = ink; C.Blocks[c] = b; }
                            }
                    }
            }
            at += 4 * scale;
        }
    }

    // ---- what's in a room ----------------------------------------------------------------------

    /// <summary>A light in a ceiling: a lamp block and the light it gives, for decay to keep or kill.</summary>
    public void Lamp(int u, int y, int v, int module, float energy = 1.8f, float range = 14f, LightMode mode = LightMode.Steady)
    {
        Int3 c = Cell(u, y, v);
        Put(u, y, v, new Block(BlockType.Cube, Material.Lumen, F.Lamp, Role.Light));
        C.X.Lights.Add(new LightSpot { Cell = c + Int3.Down, Color = F.Lamp, Mode = mode, Energy = energy, Range = range, Module = module });
    }

    /// <summary>A prop on a floor cell: stands or lies where it's put, belonging to no wall.</summary>
    public void Prop(int u, int y, int v, BlockType t, Material m, Rgb c, Dir d = Dir.North) => Lay(u, y, v, new Block(t, m, c, Role.Prop, d));

    /// <summary>A rectangle of machine, a block or two high: plain, heavy, plain to read.</summary>
    public void Machine(int i0, int j, int k0, int i1, int k1, int h, Stuff2 s)
    {
        Fill(i0, j, k0, i1, j + h - 1, k1, new Block(BlockType.Cube, s.M, s.C, Role.Prop));
        // A dark casing band, so it reads as a machine and not a plinth.
        Repaint(2 * i0, 2 * (j + h) - 2, 2 * k0, 2 * i1 + 1, 2 * (j + h) - 2, 2 * k1 + 1, (_, _, _) => s.C.Scale(0.6f));
    }

    /// <summary>A crop sown in a cell: the game grows one of the world's own crop species there.</summary>
    public void Sow(int u, int y, int v, int kind) => C.X.Crops.Add(new CropSpot(Cell(u, y, v), (byte)kind));

    /// <summary>A loot cache in a room, on an open floor cell.</summary>
    public void Cache(int u, int y, int v, int module)
    {
        Int3 c = Cell(u, y, v);
        C.Blocks.Remove(c);
        C.AddSlot(SlotKind.Loot, c, module);
    }

    public void Spawn(int u, int y, int v, int module) => C.AddSlot(SlotKind.Spawn, Cell(u, y, v), module);
    public void Terminal(int u, int y, int v, int module) => C.AddSlot(SlotKind.Terminal, Cell(u, y, v), module);

    /// <summary>The patch of ground at the way in: the place's first room, and where the road ends.</summary>
    public void Approach(int i, int k, Dir facing)
    {
        var cells = new List<Int3>();
        for (int dk = -1; dk <= 1; dk++)
            for (int di = -1; di <= 1; di++)
                for (int y = 0; y <= 2; y++)
                {
                    Int3 c = Cell(2 * i + di, y, 2 * k + dk);
                    if (_claimed.Add(c)) cells.Add(c);
                }
        Yard = C.EntranceModule = C.AddModule(ModuleKind.Outdoor, "approach", 0, cells);
        C.EntranceCell = Cell(2 * i, 0, 2 * k);
        C.EntranceFacing = Turn(facing);
        C.X.Gate = C.EntranceCell;
    }

    /// <summary>A way out from the yard onto a room: open ground runs to it.</summary>
    public void Yards(params int[] rooms)
    {
        foreach (int r in rooms) C.Link(Yard, r);
    }
}
