#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// One age's way of building (data/eras.txt): the large places' plain materials and paints, what a
/// style kit's made materials become, what roofs are covered in, and how its buildings stand against
/// the sky (how many windows, pitched roofs, cut corners, rounded edges, light strips, its lamps, what
/// stands in its yards). Tuned from reference material, never from code.
/// </summary>
public sealed class EraKit
{
    public TechLevel Level;
    public string Name = "";
    /// <summary>The large places' plain materials, by part (wall, panel, plinth...): one material, one or more colours.</summary>
    public readonly Dictionary<string, (Material M, Rgb[] C)> Stuff = new();
    public readonly Dictionary<string, Rgb> Paints = new();
    public readonly Dictionary<Material, List<(Material M, Finish F, float W)>> Swaps = new();
    public readonly List<(Material M, Finish F, float W)> Roofing = new();
    public readonly Dictionary<Material, Rgb[]> Tones = new();
    public readonly HashSet<BlockType> Keep = new();
    public float Windows = 1f, Ribbon;
    public int Gable;
    public bool Chamfer, RoundEdge;
    public Rgb? Strips, Lamp;
    public string Vehicles = "motors";

    /// <summary>Whether this age changes a kit-built place at all (the present day is what the kits were drawn in).</summary>
    public bool Reshapes => Swaps.Count > 0 || Roofing.Count > 0 || Windows < 1f || Ribbon > 0f || Gable > 0 || Chamfer || RoundEdge || Strips != null || Lamp != null;

    /// <summary>The large places' materials in this age, over <paramref name="fallback"/> for any part the table leaves out.</summary>
    public Fab Fab(Rng? r, Fab fallback)
    {
        Stuff2 S(string part, Stuff2 old)
        {
            if (!Stuff.TryGetValue(part, out var s)) return old;
            return new Stuff2(s.M, s.C.Length > 1 && r != null ? r.Pick(s.C) : s.C[0]);
        }
        Rgb P(string name, Rgb old) => Paints.TryGetValue(name, out Rgb c) ? c : old;
        var f = fallback;
        f.Wall = S("wall", f.Wall); f.Panel = S("panel", f.Panel); f.Plinth = S("plinth", f.Plinth); f.Floor = S("floor", f.Floor);
        f.Roof = S("roof", f.Roof); f.Steel = S("steel", f.Steel); f.Rust = S("rust", f.Rust); f.Pipe = S("pipe", f.Pipe);
        f.PipeHot = S("pipehot", f.PipeHot); f.Accent = S("accent", f.Accent); f.Grating = S("grating", f.Grating);
        f.Hazard = P("hazard", f.Hazard); f.Black = P("black", f.Black); f.Glass = P("glass", f.Glass); f.Lamp = P("lamp", f.Lamp); f.Emergency = P("emergency", f.Emergency);
        return f;
    }
}

/// <summary>The three ages (data/eras.txt). The library sets <see cref="Current"/> as it loads.</summary>
public sealed class EraBook
{
    public static EraBook Current = new();
    public readonly Dictionary<TechLevel, EraKit> Eras = new();

    public EraKit? Of(TechLevel t) => Eras.TryGetValue(t, out var k) ? k : null;

    public static EraBook Parse(string text, string source)
    {
        var book = new EraBook();
        EraKit? e = null;
        string[] lines = text.Replace("\r", "").Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            string line = lines[n].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            FormatException Error(string msg) => new($"{source}:{n + 1}: {msg}");
            string[] p = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            EraKit E() => e ?? throw Error("needs an 'era <low|mid|high>' first");
            Material Mat(string s) => Enum.TryParse(s, true, out Material m) ? m : throw Error($"'{s}' is not a material");
            Rgb[] Colours(string s) => s.Split('|').Select(Rgb.Parse).ToArray();
            (Material, Finish, float) Choice(string s)
            {
                string[] wt = s.Split(':');
                string[] mf = wt[0].Split('.');
                Finish f = mf.Length > 1 ? (Enum.TryParse(mf[1], true, out Finish x) ? x : throw Error($"'{mf[1]}' is not a finish")) : Finish.Plain;
                return (Mat(mf[0]), f, wt.Length > 1 ? float.Parse(wt[1], CultureInfo.InvariantCulture) : 1f);
            }
            switch (p[0])
            {
                case "era":
                    e = new EraKit { Level = p[1] switch { "low" => TechLevel.Low, "mid" => TechLevel.Mid, "high" => TechLevel.High, _ => throw Error($"'{p[1]}' is not an era") } };
                    book.Eras[e.Level] = e;
                    break;
                case "name": E().Name = string.Join(' ', p.Skip(1)); break;
                case "stuff": E().Stuff[p[1]] = (Mat(p[2]), Colours(p[3])); break;
                case "paint": E().Paints[p[1]] = Rgb.Parse(p[2]); break;
                case "swap": E().Swaps[Mat(p[1])] = p.Skip(2).Select(Choice).ToList(); break;
                case "roofing": E().Roofing.AddRange(p.Skip(1).Select(Choice)); break;
                case "tone": E().Tones[Mat(p[1])] = Colours(p[2]); break;
                case "keep":
                    foreach (string t in p.Skip(1)) E().Keep.Add(Enum.TryParse(t, true, out BlockType bt) ? bt : throw Error($"'{t}' is not a block type"));
                    break;
                case "windows": E().Windows = float.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "ribbon": E().Ribbon = float.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "gable": E().Gable = int.Parse(p[1], CultureInfo.InvariantCulture); break;
                case "chamfer": E().Chamfer = p[1] == "yes"; break;
                case "edge": E().RoundEdge = p[1] == "round"; break;
                case "strips": E().Strips = p[1] == "none" ? null : Rgb.Parse(p[1]); break;
                case "lamp": E().Lamp = Rgb.Parse(p[1]); break;
                case "vehicles": E().Vehicles = p[1]; break;
                default: throw Error($"unknown keyword '{p[0]}'");
            }
        }
        Current = book;
        return book;
    }
}

/// <summary>
/// A place built from a style kit, rebuilt in its age (<see cref="EraKit"/>): the kits are drawn in the
/// present day, so a low-tech world's old places come out in stone, timber and thatch with few windows
/// and pitched roofs, and a stellar one's in alloy and glass with banded windows, cut corners, rounded
/// roof edges and light strips. It works on any layout by rule (what a block is made of and the part it
/// plays), so every generator's places take their age; a house, which is put together in its age from
/// the start, and anything no one on this world built are left as they are.
/// </summary>
public static class EraPass
{
    /// <summary>Tests only: build without the age pass, to compare against.</summary>
    public static bool Enabled = true;
    /// <summary>The shapes a place's fabric is made of: what an age rebuilds (props and dressing take their age elsewhere).</summary>
    static readonly HashSet<BlockType> Fabric = new()
    {
        BlockType.Cube, BlockType.Ramp, BlockType.Corner, BlockType.InvCorner, BlockType.Pillar, BlockType.Rib, BlockType.Stair,
        BlockType.Rail, BlockType.Lattice, BlockType.Door, BlockType.SealedDoor, BlockType.Breakable, BlockType.Ladder, BlockType.Plate,
        BlockType.Pane, BlockType.Machine, BlockType.Tank, BlockType.Pump, BlockType.Pipe, BlockType.Spike,
    };

    static bool Rebuilt(Role r) => r is Role.Wall or Role.Floor or Role.Roof or Role.Decor or Role.Door or Role.Traverse or Role.Hazard;

    static float Lum(Rgb c) => (0.3f * c.R + 0.59f * c.G + 0.11f * c.B) / 255f;

    /// <summary>Whether an age's pass applies to a layout: a place this world's people built, not a house (built in its age already).</summary>
    public static bool Applies(Layout layout) =>
        !WorldProfile.Unbuilt(layout.Family) && layout.Extras.Homes.Count == 0 && !layout.Extras.InSpace
        && !(layout.Family.Style?.StartsWith("alien", StringComparison.Ordinal) ?? false);

    public static void Apply(Layout layout, TechLevel tech)
    {
        layout.Tech = tech;
        if (EraBook.Current.Of(tech) is not { } era || !era.Reshapes || WorldProfile.Unbuilt(layout.Family) || layout.Extras.InSpace || (layout.Family.Style?.StartsWith("alien", StringComparison.Ordinal) ?? false)) return;
        // A house is put together in its age already (its cladding, roof and windows, its fixings): it takes only its age's lamps.
        bool house = !Applies(layout);
        layout.Lamp = era.Lamp;
        var rng = new Rng(Hash.Combine(layout.Seed, 0xE7A5UL + (ulong)tech));
        BlockGrid g = layout.Blocks;
        // The 2 m works keep to the player's large blocks: their shapes move in whole blocks.
        int lattice = layout.Family.Tags.Contains("grand") ? 2 : 1;

        // ---- what it is made of: one pick per material for the whole place, each block shaded as it was built
        var picks = new Dictionary<Material, (Material M, Finish F)>();
        (Material M, Finish F)? Pick(Material from)
        {
            if (!era.Swaps.TryGetValue(from, out var list) || list.Count == 0) return null;
            if (!picks.TryGetValue(from, out var got))
                picks[from] = got = ToPair(list[Math.Max(0, rng.PickWeighted(list.Select(x => x.W).ToList()))]);
            return got;
        }
        (Material M, Finish F)? roofing = era.Roofing.Count > 0 ? ToPair(era.Roofing[Math.Max(0, rng.PickWeighted(era.Roofing.Select(x => x.W).ToList()))]) : null;
        var tones = new Dictionary<Material, Rgb>();
        Rgb? Tone(Material m)
        {
            if (!era.Tones.TryGetValue(m, out var list) || list.Length == 0) return null;
            if (!tones.TryGetValue(m, out Rgb t)) tones[m] = t = rng.Pick(list);
            return t;
        }

        var changes = new List<(Int3 P, Block B, Material From, bool Roof)>();
        var sum = new Dictionary<(Material, bool), (float L, int N)>();
        foreach (var (p, b) in g.Blocks())
        {
            if (house || !Fabric.Contains(b.Type) || era.Keep.Contains(b.Type) || !Rebuilt(b.Role)) continue;
            if (b.Type == BlockType.Pane && b.Material == Material.Glass) continue;
            bool roof = b.Role == Role.Roof && roofing != null;
            if (!roof && !era.Swaps.ContainsKey(b.Material)) continue;
            changes.Add((p, b, b.Material, roof));
            var key = (b.Material, roof);
            var s = sum.GetValueOrDefault(key);
            sum[key] = (s.L + Lum(b.Color), s.N + 1);
        }
        foreach (var (p, b0, from, roof) in changes)
        {
            Block b = b0;
            (Material M, Finish F)? to = roof ? roofing : Pick(from);
            if (to is not { } t) continue;
            var (l, n) = sum[(from, roof)];
            float mean = Math.Max(0.05f, l / n), shade = Math.Clamp(Lum(b.Color) / mean, 0.72f, 1.28f);
            b.Material = t.M;
            if (Tone(t.M) is Rgb tone) b.Color = tone.Scale(shade);
            bool surface = b.Type is BlockType.Cube or BlockType.Ramp or BlockType.Corner or BlockType.InvCorner;
            if (surface)
            {
                // A wall shows its age's facing; a pitched roof its covering; a floor or a flat roof whatever its material lays.
                bool slope = b.Type != BlockType.Cube;
                b.Variant = t.F == Finish.Plain || b.Role == Role.Floor || (roof && !slope) ? (byte)0 : (byte)(1 + (int)t.F);
            }
            g.Set(p, b);
        }

        // ---- its lamps
        if (era.Lamp is Rgb lamp)
        {
            foreach (var (p, b) in g.Blocks().Where(x => x.Block.Role == Role.Light && x.Block.Material == Material.Lumen).ToList())
                g.Set(p, b with { Color = lamp });
            foreach (LightSpot s in layout.Extras.Lights.Where(s => s.Color == layout.Kit.Light)) s.Color = lamp;
        }

        var top = Tops(g);
        bool Clear(Int3 p) => !BlockInfo.Collides(g.Get(p).Type);
        bool Sky(Int3 p) => !top.TryGetValue((p.X, p.Z), out int ty) || p.Y > ty;
        bool Outside(Int3 p) => Clear(p) && (Sky(p) || Clear(p + Int3.Up * 2) && Sky(p + Int3.Up * 2) || Dirs.Horizontal.Any(d => Clear(p + d.Vec()) && Sky(p + d.Vec())));
        bool NearDoor(Int3 p)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (g.Get(p + new Int3(dx, dy, dz)).Type is BlockType.Door or BlockType.SealedDoor) return true;
            return layout.ExitCells.Any(e => Math.Abs(e.X - p.X) <= 1 && Math.Abs(e.Z - p.Z) <= 1 && Math.Abs(e.Y - p.Y) <= 2);
        }

        // ---- its windows: few in a low age, banded in a stellar one
        if (era.Windows < 1f && !house)
            foreach (var (p, b) in g.Blocks().Where(x => x.Block.Type == BlockType.Pane && x.Block.Material == Material.Glass && x.Block.Role == Role.Window).ToList())
            {
                uint h = Hash.Cell(new Int3(Floor(p.X, lattice), 0, Floor(p.Z, lattice)), 0x3A11);
                if (h / (float)uint.MaxValue < era.Windows) continue;
                // Walled in with the wall round it: over or under it first (a band of glass has glass either side).
                Block? fill = null;
                foreach (Dir d in new[] { Dir.Up, Dir.Down, Dir.North, Dir.East, Dir.South, Dir.West })
                    for (int k = 1; k <= 2 * lattice && fill == null; k++)
                        if (g.Get(p + d.Vec() * k) is { Type: BlockType.Cube, Role: Role.Wall } wall) fill = wall;
                if (fill is Block w) g.Set(p, w);
            }
        if (era.Ribbon > 0f && lattice == 1 && !house)
            foreach (var (p, pane) in g.Blocks().Where(x => x.Block.Type == BlockType.Pane && x.Block.Material == Material.Glass && x.Block.Role == Role.Window).ToList())
            {
                foreach (Dir d in new[] { Dir.East, Dir.South })
                {
                    Int3 along = d.Vec(), across = d.RotateCw(1).Vec();
                    // The wall runs this way: glass or wall either side along it, open either side across it.
                    if (!Clear(p + across) || !Clear(p - across)) continue;
                    if (Hash.Cell(p with { X = d == Dir.East ? 0 : p.X, Z = d == Dir.South ? 0 : p.Z }, 0x81BB) / (float)uint.MaxValue >= era.Ribbon) continue;
                    foreach (int sign in new[] { 1, -1 })
                        for (int k = 1; k <= 8; k++)
                        {
                            Int3 q = p + along * (k * sign);
                            Block w = g.Get(q);
                            if (w.Type == BlockType.Pane && w.Material == Material.Glass) continue;
                            if (w is not { Type: BlockType.Cube, Role: Role.Wall } || !Clear(q + across) || !Clear(q - across) || NearDoor(q)
                                || !BlockInfo.Collides(g.Get(q + Int3.Up).Type) || !BlockInfo.Collides(g.Get(q + Int3.Down).Type)
                                || !BlockInfo.Collides(g.Get(q + along * sign).Type)) break;
                            g.Set(q, pane);
                        }
                }
            }

        // ---- its corners, cut on the diagonal
        if (era.Chamfer && lattice == 1 && !house)
        {
            var cut = new List<Int3>();
            foreach (var (p, b) in g.Blocks())
            {
                if (b.Type != BlockType.Cube || b.Role is not (Role.Wall or Role.Roof) || p.Y < 0) continue;
                foreach (Dir d in Dirs.Horizontal)
                {
                    Int3 a = d.Vec(), c = d.RotateCw(1).Vec();
                    if (Outside(p + a) && Outside(p + c) && BlockInfo.Collides(g.Get(p - a).Type) && BlockInfo.Collides(g.Get(p - c).Type)
                        && !BlockInfo.Collides(g.Get(p + a + c).Type) && !NearDoor(p)) { cut.Add(p); break; }
                }
            }
            foreach (Int3 p in cut) g.Set(p, Block.Air);
            if (cut.Count > 0) top = Tops(g);
        }

        // ---- its roofs: pitched in a low age, rounded off at the edge in a stellar one
        if (house) { }
        else if (era.Gable > 0) Gables(layout, g, top, era, roofing, lattice);
        else if (era.RoundEdge) RoundEdges(g, top, Clear, Sky);

        // ---- light strips along each floor line and the wall tops
        if (era.Strips is Rgb strip && !house)
        {
            var rows = new HashSet<int>(layout.Extras.Levels.Where(l => l.Storey > 0).Select(l => l.WalkY - 1));
            foreach (var (p, b) in g.Blocks().ToList())
            {
                if (b.Type != BlockType.Cube || b.Role != Role.Wall || p.Y < 1) continue;
                bool coping = !BlockInfo.Collides(g.Get(p + Int3.Up).Type);
                if (!coping && (lattice != 1 || !rows.Contains(p.Y))) continue;
                if (!Dirs.Horizontal.Any(d => Clear(p + d.Vec()) && Sky(p + d.Vec()))) continue;
                if (coping && !Dirs.Horizontal.Any(d => BlockInfo.Collides(g.Get(p - d.Vec()).Type) && Clear(p + d.Vec()))) continue;
                g.Set(p, new Block(BlockType.Cube, Material.Lumen, strip, Role.Decor));
            }
        }
    }

    static (Material, Finish) ToPair((Material M, Finish F, float W) c) => (c.M, c.F);

    static int Floor(int v, int lattice) => lattice == 1 ? v : (int)Math.Floor(v / (double)lattice);

    /// <summary>The top of what stands in each column.</summary>
    static Dictionary<(int, int), int> Tops(BlockGrid g)
    {
        var top = new Dictionary<(int, int), int>();
        foreach (var (p, b) in g.Blocks())
            if (BlockInfo.Collides(b.Type) && (!top.TryGetValue((p.X, p.Z), out int t) || p.Y > t)) top[(p.X, p.Z)] = p.Y;
        return top;
    }

    /// <summary>
    /// Pitched roofs: each flat roof that is a plain rectangle no wider than the age allows takes a gable,
    /// its ridge along the long side, 45 degree slopes in its covering, its ends filled in the wall below.
    /// The 2 m works' slopes are their large blocks' (two cells of the 1 m grid to a block, the same plane).
    /// </summary>
    static void Gables(Layout layout, BlockGrid g, Dictionary<(int, int), int> top, EraKit era, (Material M, Finish F)? roofing, int lattice)
    {
        // The flat tops: a cube with nothing above it, the roof or the wall it closes.
        var flat = new Dictionary<(int, int), int>();
        foreach (var ((x, z), y) in top)
            if (y >= 1 && g.Get(new Int3(x, y, z)) is { Type: BlockType.Cube, Role: Role.Roof or Role.Wall }) flat[(x, z)] = y;
        var seen = new HashSet<(int, int)>();
        foreach (var start in flat.Keys.OrderBy(k => k.Item2).ThenBy(k => k.Item1).ToList())
        {
            if (seen.Contains(start)) continue;
            int y = flat[start];
            var part = new List<(int X, int Z)>();
            var todo = new Stack<(int, int)>();
            todo.Push(start);
            seen.Add(start);
            while (todo.Count > 0)
            {
                var (x, z) = todo.Pop();
                part.Add((x, z));
                foreach (var n in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
                    if (!seen.Contains(n) && flat.TryGetValue(n, out int ny) && ny == y) { seen.Add(n); todo.Push(n); }
            }
            int x0 = part.Min(c => c.X), x1 = part.Max(c => c.X), z0 = part.Min(c => c.Z), z1 = part.Max(c => c.Z);
            int w = x1 - x0 + 1, d = z1 - z0 + 1, span = Math.Min(w, d);
            if (part.Count != w * d || span < 4 || span > era.Gable) continue;
            if (!part.Any(c => g.Get(new Int3(c.X, y, c.Z)).Role == Role.Roof)) continue;
            if (lattice == 2 && span % 2 != 0) continue;
            int rise = (span + 1) / 2;
            if (y + rise > g.Max.Y) continue;
            bool ridgeAlongX = w >= d;
            Block below = g.Get(new Int3(x0, y, z0));
            Material cover = roofing?.M ?? below.Material;
            Rgb coverC = era.Tones.TryGetValue(cover, out var ts) && ts.Length > 0 ? ts[(int)(Hash.Combine(layout.Seed, 0x60AB1EUL) % (ulong)ts.Length)].Scale(0.82f) : below.Color.Scale(0.8f);
            byte variant = roofing is { } r && r.F != Finish.Plain ? (byte)(1 + (int)r.F) : (byte)0;
            foreach (var (x, z) in part)
            {
                int a = ridgeAlongX ? z - z0 : x - x0, b = ridgeAlongX ? z1 - z : x1 - x;
                int off = Math.Min(a, b);
                bool end = ridgeAlongX ? x == x0 || x == x1 : z == z0 || z == z1;
                Block top0 = g.Get(new Int3(x, y, z));
                Block wallBelow = g.Get(new Int3(x, y - 1, z));
                Block fill = !end ? new Block(BlockType.Cube, cover, coverC, Role.Roof)
                    : wallBelow is { Type: BlockType.Cube, Role: Role.Wall } ? wallBelow : top0 with { Role = Role.Wall };
                for (int k = 1; k <= off; k++) g.Set(new Int3(x, y + k, z), fill);
                Dir up = ridgeAlongX ? (a < b ? Dir.South : Dir.North) : (a < b ? Dir.East : Dir.West);
                g.Set(new Int3(x, y + off + 1, z), a == b
                    ? new Block(BlockType.Cube, cover, coverC.Scale(0.9f), Role.Roof)
                    : new Block(BlockType.Ramp, cover, coverC, Role.Roof, up, variant));
            }
        }
    }

    /// <summary>A flat roof's edge rounded off: each wall-top or roof-edge block with open air outside and the roof behind it becomes a slope down to the outside.</summary>
    static void RoundEdges(BlockGrid g, Dictionary<(int, int), int> top, Func<Int3, bool> clear, Func<Int3, bool> sky)
    {
        var set = new List<(Int3, Block)>();
        foreach (var ((x, z), y) in top)
        {
            var p = new Int3(x, y, z);
            Block b = g.Get(p);
            if (y < 2 || b.Type != BlockType.Cube || b.Role is not (Role.Roof or Role.Wall) || !BlockInfo.Collides(g.Get(p + Int3.Down).Type)) continue;
            // Where a ladder comes up beside it, the edge stays square: it is the step off the top.
            if (Dirs.Horizontal.Any(d => g.Get(p + d.Vec()).Type == BlockType.Ladder || g.Get(p + d.Vec() + Int3.Down).Type == BlockType.Ladder)) continue;
            var open = Dirs.Horizontal.Where(d => clear(p + d.Vec()) && sky(p + d.Vec()) && !BlockInfo.Collides(g.Get(p + d.Vec() + Int3.Down).Type)).ToList();
            var held = Dirs.Horizontal.Where(d => g.Get(p + d.Vec()) is { Type: BlockType.Cube } n && n.Role is Role.Roof or Role.Wall).ToList();
            if (open.Count == 1 && held.Contains(open[0].Opposite()))
                set.Add((p, b with { Type = BlockType.Ramp, Facing = open[0].Opposite() }));
            else if (open.Count == 2 && open[0].Opposite() != open[1])
            {
                // An outer corner: one raised corner, toward the roof (between Facing and the next way round anticlockwise).
                Dir i = open[0].Opposite(), j = open[1].Opposite();
                Dir facing = i.RotateCw(3) == j ? i : j;
                if (held.Contains(i) && held.Contains(j)) set.Add((p, b with { Type = BlockType.Corner, Facing = facing }));
            }
        }
        foreach (var (p, b) in set) g.Set(p, b);
    }
}
