#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The design's faction ids, the same ones the encounters prototype uses. None is for places
/// nobody living built, such as Precursor ruins.
/// </summary>
public enum Faction : byte { None, Meridian, Exchange, Vharn, Kessari, Reavers, Freeholders, Hollow }

public static class Factions
{
    /// <summary>The id in data files and saves: meridian, exchange, vharn, kessari, reavers, freeholders, hollow.</summary>
    public static string Id(this Faction f) => f.ToString().ToLowerInvariant();

    public static string DisplayName(this Faction f) => f switch
    {
        Faction.Meridian => "Meridian Combine",
        Faction.Exchange => "Ledger Exchange",
        Faction.Vharn => "Vharn Dominion",
        Faction.Kessari => "Kessari Clans",
        Faction.Reavers => "Carrion Reavers",
        Faction.Freeholders => "Freeholders",
        Faction.Hollow => "Hollow Bloom",
        _ => "No one living",
    };
}

public enum ModuleKind : byte { Entrance, Corridor, Room, Hall, Vault, Tower, Descent }

/// <summary>Spots a module offers to the dressing pass. All are air in the template.</summary>
public enum SlotKind : byte { Loot, Terminal, Core, Spawn, Boss, Prop }

/// <summary>One palette symbol: the block it stamps. Several colours give a patchwork.</summary>
public sealed class PaletteEntry
{
    public char Symbol;
    public BlockType Type;
    public Material Material;
    public Rgb[] Colors = Array.Empty<Rgb>();
    public Role Role;

    /// <summary>Picks the colour from the world cell, so patchwork differs between spawns of one module.</summary>
    public Block Make(Int3 worldCell, Dir facing)
    {
        Rgb c = Colors.Length == 1 ? Colors[0] : Colors[Hash.Cell(worldCell, 17) % (uint)Colors.Length];
        return new Block(Type, Material, c, Role, facing);
    }
}

/// <summary>
/// A doorway on a module's outside face: a rectangle of cells, Width wide and Height tall.
/// Two modules join when their sockets match in size and face each other; the doorway is then
/// two cells deep, one from each side.
/// </summary>
public sealed class SocketDef
{
    public List<Int3> Cells = new();
    public Int3 Min;
    public Dir Facing;
    public int Width, Height;
    public bool Exterior; // the way in from outside (entrances only)

    public SocketDef Rotated(Func<Int3, Int3> map)
    {
        var cells = Cells.Select(map).ToList();
        Int3 min = cells.Aggregate(Int3.Min);
        return new SocketDef { Cells = cells, Min = min, Facing = Facing.RotateCw(1), Width = Width, Height = Height, Exterior = Exterior };
    }
}

public readonly record struct SlotDef(SlotKind Kind, Int3 Cell);

/// <summary>A template cell: a palette entry (or none for air) plus a facing.</summary>
public struct TemplateCell
{
    public short Palette;     // -1 = air
    public BlockType Type;
    public Dir Facing;
    public bool Occupied;     // part of the module (air inside counts), as opposed to outside it
    public bool Socket;
}

/// <summary>A module template in one of its four rotations.</summary>
public sealed class ModuleShape
{
    public int W, H, D;
    public TemplateCell[] Cells = Array.Empty<TemplateCell>();
    public List<Int3> Occupied = new();
    public List<SocketDef> Sockets = new();
    public List<SlotDef> Slots = new();

    public ref TemplateCell At(int x, int y, int z) => ref Cells[(y * D + z) * W + x];
    public bool InBounds(Int3 p) => (uint)p.X < (uint)W && (uint)p.Y < (uint)H && (uint)p.Z < (uint)D;

    /// <summary>Turns the module a quarter clockwise seen from above: north becomes east.</summary>
    public ModuleShape RotateCw()
    {
        var r = new ModuleShape { W = D, H = H, D = W };
        r.Cells = new TemplateCell[Cells.Length];
        Int3 Map(Int3 p) => new(D - 1 - p.Z, p.Y, p.X);
        for (int y = 0; y < H; y++)
            for (int z = 0; z < D; z++)
                for (int x = 0; x < W; x++)
                {
                    var c = At(x, y, z);
                    c.Facing = c.Facing.RotateCw(1);
                    Int3 m = Map(new Int3(x, y, z));
                    r.At(m.X, m.Y, m.Z) = c;
                }
        r.Occupied = Occupied.Select(Map).ToList();
        r.Sockets = Sockets.Select(s => s.Rotated(Map)).ToList();
        r.Slots = Slots.Select(s => s with { Cell = Map(s.Cell) }).ToList();
        return r;
    }
}

public sealed class ModuleTemplate
{
    public string Id = "";
    public ModuleKind Kind;
    public float Weight = 1f;
    public string[] Tags = Array.Empty<string>();
    public Kit Kit = null!;
    public ModuleShape[] Rotations = new ModuleShape[4];
    public ModuleShape[] Buried = new ModuleShape[4];   // the same, cropped at the roof
    public ModuleShape Shape => Rotations[0];

    public bool HasTag(string tag) => string.IsNullOrEmpty(tag) || Tags.Contains(tag);
    public override string ToString() => $"{Kit.Id}:{Id}";
}

/// <summary>A faction kit: one palette and the modules built from it.</summary>
public sealed class Kit
{
    public string Id = "";
    public string Name = "";
    public Faction Faction;
    public Rgb Light = new(255, 220, 170);
    public List<PaletteEntry> Palette = new();
    public Dictionary<char, short> Symbols = new();
    public List<ModuleTemplate> Modules = new();
    public short CapEntry = -1;   // seals sockets nobody connected to
    public short RampEntry = -1;  // what ^ v < > are made of
    public short FloorEntry = -1; // floors laid under props

    public PaletteEntry Entry(short i) => Palette[i];
    public PaletteEntry? BySymbol(char c) => Symbols.TryGetValue(c, out short i) ? Palette[i] : null;
    public PaletteEntry FirstWithRole(Role r) => Palette.FirstOrDefault(p => p.Role == r) ?? Palette[0];
}

/// <summary>
/// Reads a .kit file. See README.md, "Authoring a kit module", for the format. In short:
///
///   kit vharn
///   faction vharn
///   palette
///     #  cube  chitin  3d1d36|46213e  wall
///   module vh_gullet
///     kind corridor
///     layer fill _
///     layer x3
///       |#DD#|
///       |#..#|
///       |#DD#|
///     layer fill =
///
/// Layers go bottom to top; in each grid, rows run north to south and columns west to east.
/// </summary>
public static class KitParser
{
    public const char Outside = ' ', AirChar = '.', SocketChar = 'D', ExitChar = 'X';

    static readonly Dictionary<char, SlotKind> SlotChars = new()
    {
        ['$'] = SlotKind.Loot, ['T'] = SlotKind.Terminal, ['C'] = SlotKind.Core,
        ['!'] = SlotKind.Spawn, ['B'] = SlotKind.Boss, ['?'] = SlotKind.Prop,
    };

    static readonly Dictionary<char, Dir> RampChars = new()
    {
        ['^'] = Dir.North, ['v'] = Dir.South, ['<'] = Dir.West, ['>'] = Dir.East,
    };

    public static bool IsReserved(char c) =>
        c is Outside or AirChar or SocketChar or ExitChar or '|' || SlotChars.ContainsKey(c) || RampChars.ContainsKey(c);

    sealed class LayerSpec
    {
        public List<string> Rows = new();
        public char Fill;   // non-zero for "layer fill S"
        public int Repeat = 1;
        public int Line;
    }

    sealed class ModuleDraft
    {
        public ModuleTemplate T = new();
        public List<LayerSpec> Layers = new();
        public int Line;
    }

    public static Kit Parse(string text, string source)
    {
        var kit = new Kit();
        ModuleDraft? draft = null;
        LayerSpec? layer = null;
        bool inPalette = false;
        var drafts = new List<ModuleDraft>();
        string[] lines = text.Replace("\r", "").Split('\n');

        for (int n = 0; n < lines.Length; n++)
        {
            string raw = lines[n];
            string trimmed = raw.Trim();
            int lineNo = n + 1;
            FormatException Error(string msg) => new($"{source}:{lineNo}: {msg}");

            // Grid rows are framed by pipes, so trailing spaces (outside cells) survive editors.
            if (trimmed.StartsWith('|'))
            {
                if (layer == null || layer.Fill != 0) throw Error("grid row outside a layer");
                string body = trimmed[1..];
                int end = body.LastIndexOf('|');
                if (end >= 0) body = body[..end];
                layer.Rows.Add(body);
                continue;
            }

            if (trimmed.Length == 0) continue;
            string[] parts = trimmed.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            string key = parts[0];

            // '#' is both the comment marker and the usual wall symbol, so inside a palette a
            // line is an entry when its second word is a block type; anything after the role
            // is a comment.
            if (inPalette && key.Length == 1 && parts.Length >= 5 && Enum.TryParse(parts[1], true, out BlockType _))
            {
                ParsePaletteLine(kit, parts, Error);
                continue;
            }
            if (trimmed.StartsWith('#')) continue;
            inPalette = false;

            switch (key)
            {
                case "kit": kit.Id = parts[1]; break;
                case "name": kit.Name = string.Join(' ', parts.Skip(1)); break;
                case "faction": kit.Faction = ParseEnum<Faction>(parts[1], Error); break;
                case "light": kit.Light = Rgb.Parse(parts[1]); break;
                case "palette": inPalette = true; break;
                case "cap": kit.CapEntry = Symbol(kit, parts[1][0], Error); break;
                case "module":
                    draft = new ModuleDraft { Line = lineNo };
                    draft.T.Id = parts[1];
                    draft.T.Kit = kit;
                    drafts.Add(draft);
                    layer = null;
                    break;
                case "kind":
                    Need(draft, Error).T.Kind = ParseEnum<ModuleKind>(parts[1], Error);
                    break;
                case "weight":
                    Need(draft, Error).T.Weight = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    break;
                case "tags":
                    Need(draft, Error).T.Tags = parts.Skip(1).ToArray();
                    break;
                case "layer":
                    layer = new LayerSpec { Line = lineNo };
                    for (int i = 1; i < parts.Length; i++)
                    {
                        if (parts[i] == "fill") layer.Fill = parts[++i][0];
                        else if (parts[i].StartsWith('x')) layer.Repeat = int.Parse(parts[i][1..], CultureInfo.InvariantCulture);
                        else throw Error($"unknown layer option '{parts[i]}'");
                    }
                    Need(draft, Error).Layers.Add(layer);
                    break;
                default:
                    throw Error($"unknown keyword '{key}'");
            }
        }

        if (string.IsNullOrEmpty(kit.Id)) throw new FormatException($"{source}: missing 'kit <id>'");
        if (kit.CapEntry < 0) kit.CapEntry = (short)kit.Palette.FindIndex(p => p.Role == Role.Wall && p.Type == BlockType.Cube);
        kit.RampEntry = (short)kit.Palette.FindIndex(p => p.Type == BlockType.Ramp);
        kit.FloorEntry = (short)kit.Palette.FindIndex(p => p.Role == Role.Floor && p.Type == BlockType.Cube);
        foreach (var d in drafts) kit.Modules.Add(Build(kit, d, source));
        return kit;
    }

    static void ParsePaletteLine(Kit kit, string[] parts, Func<string, FormatException> error)
    {
        // symbol type material colours role
        if (parts.Length < 5) throw error("palette lines are: symbol type material colour[|colour...] role");
        char sym = parts[0][0];
        if (IsReserved(sym)) throw error($"'{sym}' is reserved and can't be a palette symbol");
        var entry = new PaletteEntry
        {
            Symbol = sym,
            Type = ParseEnum<BlockType>(parts[1], error),
            Material = ParseEnum<Material>(parts[2], error),
            Colors = parts[3].Split('|').Select(Rgb.Parse).ToArray(),
            Role = ParseEnum<Role>(parts[4], error),
        };
        kit.Symbols[sym] = (short)kit.Palette.Count;
        kit.Palette.Add(entry);
    }

    static ModuleDraft Need(ModuleDraft? d, Func<string, FormatException> error) =>
        d ?? throw error("module keyword before any 'module <id>'");

    static short Symbol(Kit kit, char c, Func<string, FormatException> error) =>
        kit.Symbols.TryGetValue(c, out short i) ? i : throw error($"symbol '{c}' isn't in the palette");

    static T ParseEnum<T>(string s, Func<string, FormatException> error) where T : struct, Enum =>
        Enum.TryParse(s, ignoreCase: true, out T v) ? v : throw error($"'{s}' is not a {typeof(T).Name}");

    static ModuleTemplate Build(Kit kit, ModuleDraft d, string source)
    {
        FormatException Error(string msg) => new($"{source}:{d.Line}: module {d.T.Id}: {msg}");
        var explicitLayers = d.Layers.Where(l => l.Fill == 0).ToList();
        if (explicitLayers.Count == 0) throw Error("needs at least one grid layer");
        int depth = explicitLayers[0].Rows.Count;
        if (explicitLayers.Any(l => l.Rows.Count != depth))
            throw Error($"every grid layer must have {depth} rows (see the layer at line {explicitLayers.First(l => l.Rows.Count != depth).Line})");
        int width = explicitLayers.Max(l => l.Rows.Max(r => r.Length));

        // The footprint is every column some grid layer uses; fill layers cover exactly that.
        var footprint = new bool[width * depth];
        foreach (var l in explicitLayers)
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < l.Rows[z].Length; x++)
                    if (l.Rows[z][x] != Outside) footprint[z * width + x] = true;

        int height = d.Layers.Sum(l => l.Repeat);
        var shape = new ModuleShape { W = width, H = height, D = depth, Cells = new TemplateCell[width * height * depth] };
        var socketCells = new List<Int3>();
        var exitCells = new List<Int3>();
        var slotCells = new HashSet<SlotDef>();

        int y = 0;
        foreach (var l in d.Layers)
        {
            for (int rep = 0; rep < l.Repeat; rep++, y++)
            {
                for (int z = 0; z < depth; z++)
                    for (int x = 0; x < width; x++)
                    {
                        char c = l.Fill != 0
                            ? (footprint[z * width + x] ? l.Fill : Outside)
                            : (x < l.Rows[z].Length ? l.Rows[z][x] : Outside);
                        ref TemplateCell cell = ref shape.At(x, y, z);
                        cell.Palette = -1;
                        var p = new Int3(x, y, z);
                        if (c == Outside) continue;
                        cell.Occupied = true;
                        shape.Occupied.Add(p);
                        if (c == AirChar) continue;
                        if (c == SocketChar) { cell.Socket = true; socketCells.Add(p); continue; }
                        if (c == ExitChar) { cell.Socket = true; exitCells.Add(p); continue; }
                        if (SlotChars.TryGetValue(c, out SlotKind slot))
                        {
                            // A slot in a repeated layer is one spot on the floor, not a stack.
                            if (!slotCells.Contains(new SlotDef(slot, p + Int3.Down))) shape.Slots.Add(new SlotDef(slot, p));
                            slotCells.Add(new SlotDef(slot, p));
                            continue;
                        }
                        if (RampChars.TryGetValue(c, out Dir facing))
                        {
                            if (kit.RampEntry < 0 && kit.Palette.All(e => e.Type != BlockType.Ramp))
                                throw Error("uses a ramp but the palette has no ramp entry");
                            cell.Palette = (short)kit.Palette.FindIndex(e => e.Type == BlockType.Ramp);
                            cell.Type = BlockType.Ramp;
                            cell.Facing = facing;
                            continue;
                        }
                        if (!kit.Symbols.TryGetValue(c, out short entry))
                            throw Error($"symbol '{c}' (layer {y}, row {z}, column {x}) isn't in the {kit.Id} palette");
                        cell.Palette = entry;
                        cell.Type = kit.Palette[entry].Type;
                    }
            }
        }

        shape.Sockets.AddRange(GroupSockets(shape, socketCells, false, Error));
        shape.Sockets.AddRange(GroupSockets(shape, exitCells, true, Error));

        var t = d.T;
        int exits = shape.Sockets.Count(s => s.Exterior);
        int doors = shape.Sockets.Count(s => !s.Exterior);
        if (t.Kind == ModuleKind.Entrance && exits != 1) throw Error("an entrance needs exactly one X exit");
        if (t.Kind != ModuleKind.Entrance && exits != 0) throw Error("only entrances have an X exit");
        if (doors < 1) throw Error("needs at least one D socket");
        if (t.Kind is ModuleKind.Vault && doors != 1) throw Error("a vault is a dead end: exactly one D socket");
        if (t.Kind is ModuleKind.Descent && shape.Sockets.Select(s => s.Min.Y).Distinct().Count() < 2)
            throw Error("a descent needs sockets at two different heights");

        t.Rotations[0] = shape;
        for (int r = 1; r < 4; r++) t.Rotations[r] = t.Rotations[r - 1].RotateCw();

        // Buried, a module loses its crown: domes and spires above the roof would be inside
        // the rock anyway, and dropping them lets tall kits go underground.
        int roofLine = 0;
        foreach (Int3 p in shape.Occupied)
        {
            var c = shape.At(p.X, p.Y, p.Z);
            if (c.Palette < 0 || c.Socket) roofLine = Math.Max(roofLine, p.Y + 1);
        }
        var buried = roofLine + 1 < shape.H ? Crop(shape, roofLine + 1) : shape;
        t.Buried[0] = buried;
        for (int r = 1; r < 4; r++) t.Buried[r] = t.Buried[r - 1].RotateCw();
        return t;
    }

    static ModuleShape Crop(ModuleShape s, int height)
    {
        var c = new ModuleShape { W = s.W, H = height, D = s.D, Cells = new TemplateCell[s.W * height * s.D] };
        Array.Copy(s.Cells, c.Cells, c.Cells.Length); // layers are stored bottom first
        c.Occupied = s.Occupied.Where(p => p.Y < height).ToList();
        c.Sockets = s.Sockets.ToList();
        c.Slots = s.Slots.Where(sl => sl.Cell.Y < height).ToList();
        return c;
    }

    /// <summary>Joins socket cells into rectangles and works out which face each one is on.</summary>
    static IEnumerable<SocketDef> GroupSockets(ModuleShape shape, List<Int3> cells, bool exterior, Func<string, FormatException> error)
    {
        var left = new HashSet<Int3>(cells);
        foreach (Int3 start in cells)
        {
            if (!left.Contains(start)) continue;
            var group = new List<Int3>();
            var queue = new Queue<Int3>();
            queue.Enqueue(start);
            left.Remove(start);
            while (queue.Count > 0)
            {
                Int3 p = queue.Dequeue();
                group.Add(p);
                foreach (Dir d in Dirs.All)
                {
                    Int3 q = p + d.Vec();
                    if (left.Remove(q)) queue.Enqueue(q);
                }
            }

            Int3 min = group.Aggregate(Int3.Min), max = group.Aggregate(Int3.Max);
            var faces = new List<Dir>();
            if (group.All(c => c.Z == 0)) faces.Add(Dir.North);
            if (group.All(c => c.Z == shape.D - 1)) faces.Add(Dir.South);
            if (group.All(c => c.X == 0)) faces.Add(Dir.West);
            if (group.All(c => c.X == shape.W - 1)) faces.Add(Dir.East);
            if (faces.Count != 1)
                throw error($"socket at {min} must lie on exactly one outside face of the module (found {faces.Count})");
            Dir face = faces[0];
            int width = face is Dir.North or Dir.South ? max.X - min.X + 1 : max.Z - min.Z + 1;
            int height = max.Y - min.Y + 1;
            if (group.Count != width * height) throw error($"socket at {min} isn't a filled rectangle");
            yield return new SocketDef { Cells = group, Min = min, Facing = face, Width = width, Height = height, Exterior = exterior };
        }
    }
}
